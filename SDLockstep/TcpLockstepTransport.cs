using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SDLockstep;

/// <summary>
/// Reliable ordered TCP transport for the 2-player lockstep MVP. It deliberately keeps the
/// <see cref="ILockstepTransport"/> surface: peers register local receivers, <see cref="Send"/>
/// delivers to local receivers when present, otherwise frames the message onto the TCP stream.
/// Host mode can therefore run peer 0 (coordinator) and peer 1 (local player) in the same process
/// while remote peer 2 is carried over the socket.
/// </summary>
public sealed class TcpLockstepTransport : ILockstepTransport, IDisposable
{
    readonly Dictionary<int, Action<LockstepMessage>> Receivers = new();
    readonly Dictionary<int, List<Action<LockstepMessage>>> Observers = new();
    readonly Dictionary<int, Queue<LockstepMessage>> LocalInbox = new();
    readonly Queue<RemoteEnvelope> PendingRemote = new();
    readonly Queue<RemoteEnvelope> InboundRemote = new();
    readonly object Gate = new();
    readonly ManualResetEventSlim ConnectedEvent = new(false);

    TcpListener Listener;
    TcpClient Client;
    NetworkStream Stream;
    Task AcceptTask;
    Task ReadTask;
    bool Disposed;

    public int RemotePeerId { get; }
    public bool IsConnected { get; private set; }
    public bool IsDisposed => Disposed;
    public string LastError { get; private set; } = "";
    public int LocalMessageCount { get; private set; }
    public int RemoteMessageCount { get; private set; }

    TcpLockstepTransport(int remotePeerId) => RemotePeerId = remotePeerId;

    public static TcpLockstepTransport Host(int port, int remotePeerId = 2)
    {
        var transport = new TcpLockstepTransport(remotePeerId);
        transport.Listener = new TcpListener(IPAddress.Any, port);
        transport.Listener.Start();
        transport.AcceptTask = Task.Run(transport.AcceptLoop);
        return transport;
    }

    public static TcpLockstepTransport Join(string host, int port, int remotePeerId = LockstepHost.HostPeerId)
    {
        var transport = new TcpLockstepTransport(remotePeerId);
        var client = new TcpClient { NoDelay = true };
        client.Connect(host, port);
        transport.Attach(client);
        return transport;
    }

    public bool WaitForConnection(TimeSpan timeout) => ConnectedEvent.Wait(timeout);

    public void Register(int peerId, Action<LockstepMessage> onReceive)
    {
        lock (Gate)
        {
            Receivers[peerId] = onReceive;
            if (!LocalInbox.ContainsKey(peerId))
                LocalInbox[peerId] = new Queue<LockstepMessage>();
        }
    }

    public void AddObserver(int peerId, Action<LockstepMessage> onReceive)
    {
        lock (Gate)
        {
            if (!Observers.TryGetValue(peerId, out List<Action<LockstepMessage>> list))
            {
                list = new List<Action<LockstepMessage>>();
                Observers[peerId] = list;
            }
            list.Add(onReceive);
        }
    }

    public void Send(int toPeer, LockstepMessage message)
    {
        if (message == null)
            return;

        lock (Gate)
        {
            if (LocalInbox.ContainsKey(toPeer))
            {
                LocalInbox[toPeer].Enqueue(message);
                LocalMessageCount++;
                return;
            }
        }

        SendRemote(new RemoteEnvelope(toPeer, message));
    }

    public void Poll()
    {
        DrainInboundRemote();

        List<Delivery> deliveries = new();
        lock (Gate)
        {
            foreach (KeyValuePair<int, Queue<LockstepMessage>> kv in LocalInbox)
            {
                Queue<LockstepMessage> queue = kv.Value;
                int count = queue.Count;
                for (int i = 0; i < count; ++i)
                    deliveries.Add(new Delivery(kv.Key, queue.Dequeue()));
            }
        }

        foreach (Delivery delivery in deliveries)
            Deliver(delivery.PeerId, delivery.Message);
    }

    public void Dispose()
    {
        if (Disposed)
            return;
        Disposed = true;
        try { Stream?.Dispose(); } catch { }
        try { Client?.Close(); } catch { }
        try { Listener?.Stop(); } catch { }
        ConnectedEvent.Dispose();
    }

    void AcceptLoop()
    {
        try
        {
            TcpClient client = Listener.AcceptTcpClient();
            Attach(client);
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            lock (Gate) LastError = ex.Message;
        }
    }

    void Attach(TcpClient client)
    {
        client.NoDelay = true;
        lock (Gate)
        {
            Client = client;
            Stream = client.GetStream();
            IsConnected = true;
            LastError = "";
        }
        ConnectedEvent.Set();
        FlushPendingRemote();
        ReadTask = Task.Run(ReadLoop);
    }

    void ReadLoop()
    {
        try
        {
            while (!Disposed)
            {
                byte[] lengthBytes = ReadExact(4);
                if (lengthBytes == null)
                    break;
                int length = BitConverter.ToInt32(lengthBytes, 0);
                if (length <= 0 || length > 1_048_576)
                    throw new InvalidDataException($"Invalid lockstep packet length {length}");
                byte[] payload = ReadExact(length);
                if (payload == null)
                    break;

                DecodedLockstepMessage decoded = LockstepMessageCodec.Decode(payload);
                lock (Gate)
                {
                    InboundRemote.Enqueue(new RemoteEnvelope(decoded.ToPeer, decoded.Message));
                    RemoteMessageCount++;
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (IOException ex)
        {
            lock (Gate) LastError = ex.Message;
        }
        catch (Exception ex)
        {
            lock (Gate) LastError = ex.Message;
        }
        finally
        {
            lock (Gate) IsConnected = false;
        }
    }

    byte[] ReadExact(int length)
    {
        byte[] bytes = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = Stream.Read(bytes, offset, length - offset);
            if (read == 0)
                return null;
            offset += read;
        }
        return bytes;
    }

    void SendRemote(RemoteEnvelope envelope)
    {
        lock (Gate)
        {
            if (!IsConnected || Stream == null)
            {
                PendingRemote.Enqueue(envelope);
                return;
            }
        }
        WriteRemote(envelope);
    }

    void FlushPendingRemote()
    {
        while (true)
        {
            RemoteEnvelope envelope;
            lock (Gate)
            {
                if (PendingRemote.Count == 0)
                    return;
                envelope = PendingRemote.Dequeue();
            }
            WriteRemote(envelope);
        }
    }

    void WriteRemote(RemoteEnvelope envelope)
    {
        try
        {
            byte[] payload = LockstepMessageCodec.Encode(envelope.Message, envelope.ToPeer);
            byte[] length = BitConverter.GetBytes(payload.Length);
            lock (Gate)
            {
                if (Stream == null)
                {
                    PendingRemote.Enqueue(envelope);
                    return;
                }
                Stream.Write(length, 0, length.Length);
                Stream.Write(payload, 0, payload.Length);
                Stream.Flush();
            }
        }
        catch (Exception ex)
        {
            lock (Gate)
            {
                LastError = ex.Message;
                IsConnected = false;
                PendingRemote.Enqueue(envelope);
            }
        }
    }

    void DrainInboundRemote()
    {
        while (true)
        {
            RemoteEnvelope envelope;
            lock (Gate)
            {
                if (InboundRemote.Count == 0)
                    return;
                envelope = InboundRemote.Dequeue();
                if (!LocalInbox.ContainsKey(envelope.ToPeer))
                    LocalInbox[envelope.ToPeer] = new Queue<LockstepMessage>();
                LocalInbox[envelope.ToPeer].Enqueue(envelope.Message);
            }
        }
    }

    void Deliver(int peerId, LockstepMessage message)
    {
        Action<LockstepMessage> receiver = null;
        List<Action<LockstepMessage>> observers = null;
        lock (Gate)
        {
            Receivers.TryGetValue(peerId, out receiver);
            if (Observers.TryGetValue(peerId, out List<Action<LockstepMessage>> list))
                observers = new List<Action<LockstepMessage>>(list);
        }

        if (observers != null)
            foreach (Action<LockstepMessage> observer in observers)
                observer(message);
        receiver?.Invoke(message);
    }

    readonly struct RemoteEnvelope
    {
        public readonly int ToPeer;
        public readonly LockstepMessage Message;
        public RemoteEnvelope(int toPeer, LockstepMessage message)
        {
            ToPeer = toPeer;
            Message = message;
        }
    }

    readonly struct Delivery
    {
        public readonly int PeerId;
        public readonly LockstepMessage Message;
        public Delivery(int peerId, LockstepMessage message)
        {
            PeerId = peerId;
            Message = message;
        }
    }
}

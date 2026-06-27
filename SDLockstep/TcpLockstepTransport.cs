using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SDLockstep;

/// <summary>
/// Reliable ordered TCP transport for lockstep sessions. It deliberately keeps the
/// <see cref="ILockstepTransport"/> surface: peers register local receivers, <see cref="Send"/>
/// delivers to local receivers when present, otherwise frames the message onto the TCP stream.
/// Host mode can therefore run peer 0 (coordinator) and peer 1 (local player) in the same process
/// while remote peers are carried over sockets. The original <see cref="Host(int,int)"/> path is
/// still a single fixed remote peer for Arena MP; <see cref="HostMulti(int)"/> accepts multiple
/// announced peers for the authoritative 4X host.
/// </summary>
public sealed class TcpLockstepTransport : ILockstepTransport, IDisposable
{
    readonly Dictionary<int, Action<LockstepMessage>> Receivers = new();
    readonly Dictionary<int, List<Action<LockstepMessage>>> Observers = new();
    readonly Dictionary<int, Queue<LockstepMessage>> LocalInbox = new();
    readonly Queue<RemoteEnvelope> PendingRemote = new();
    readonly Queue<RemoteEnvelope> InboundRemote = new();
    readonly List<RemoteConnection> Connections = new();
    readonly Dictionary<int, RemoteConnection> ConnectionsByPeer = new();
    readonly object Gate = new();
    readonly ManualResetEventSlim ConnectedEvent = new(false);

    TcpListener Listener;
    Task AcceptTask;
    bool Disposed;
    readonly bool MultiRemote;

    public int RemotePeerId { get; }
    public bool IsConnected { get; private set; }
    public bool IsDisposed => Disposed;
    public string LastError { get; private set; } = "";
    public int LocalMessageCount { get; private set; }
    public int RemoteMessageCount { get; private set; }
    public int RemoteWriteCount { get; private set; }
    public int DeliveredMessageCount { get; private set; }
    public int PendingRemoteCount { get { lock (Gate) return PendingRemote.Count; } }
    public int InboundRemoteCount { get { lock (Gate) return InboundRemote.Count; } }
    public Action<string> AuthoritativeFrameTrace { get; set; }

    TcpLockstepTransport(int remotePeerId, bool multiRemote = false)
    {
        RemotePeerId = remotePeerId;
        MultiRemote = multiRemote;
    }

    public static TcpLockstepTransport Host(int port, int remotePeerId = 2)
    {
        var transport = new TcpLockstepTransport(remotePeerId);
        transport.Listener = new TcpListener(IPAddress.Any, port);
        transport.Listener.Start();
        transport.AcceptTask = Task.Run(transport.AcceptLoop);
        return transport;
    }

    public static TcpLockstepTransport HostMulti(int port)
    {
        var transport = new TcpLockstepTransport(remotePeerId: 0, multiRemote: true);
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
        transport.Attach(client, remotePeerId);
        return transport;
    }

    public static TcpLockstepTransport JoinAsPeer(string host, int port, int localPeerId,
        int remotePeerId = LockstepHost.HostPeerId)
    {
        var transport = Join(host, port, remotePeerId);
        transport.Send(remotePeerId, new SessionHelloMessage
        {
            FromPeer = localPeerId,
            PeerId = localPeerId,
            ProtocolVersion = 1,
            PlayerName = $"Peer {localPeerId}",
        });
        return transport;
    }

    public bool WaitForConnection(TimeSpan timeout) => ConnectedEvent.Wait(timeout);
    public bool WaitForConnections(int count, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            lock (Gate)
            {
                if (Connections.Count(c => c.Connected) >= count)
                    return true;
            }
            Thread.Sleep(5);
        }
        lock (Gate)
            return Connections.Count(c => c.Connected) >= count;
    }

    public int[] ConnectedRemotePeerIds
    {
        get
        {
            lock (Gate)
                return ConnectionsByPeer.Keys.OrderBy(peer => peer).ToArray();
        }
    }

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

        bool local = false;
        lock (Gate)
        {
            if (LocalInbox.ContainsKey(toPeer))
            {
                LocalInbox[toPeer].Enqueue(message);
                LocalMessageCount++;
                local = true;
            }
        }

        if (local)
        {
            TraceAuthoritativeFrame("send-local", toPeer, message);
            return;
        }

        TraceAuthoritativeFrame("send-remote", toPeer, message);
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
        RemoteConnection[] connections;
        lock (Gate)
            connections = Connections.ToArray();
        foreach (RemoteConnection connection in connections)
        {
            try { connection.Stream?.Dispose(); } catch { }
            try { connection.Client?.Close(); } catch { }
        }
        try { Listener?.Stop(); } catch { }
        ConnectedEvent.Dispose();
    }

    void AcceptLoop()
    {
        try
        {
            while (!Disposed)
            {
                TcpClient client = Listener.AcceptTcpClient();
                Attach(client, MultiRemote ? 0 : RemotePeerId);
                if (!MultiRemote)
                    return;
            }
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            lock (Gate) LastError = ex.Message;
        }
    }

    void Attach(TcpClient client, int remotePeerId)
    {
        client.NoDelay = true;
        var connection = new RemoteConnection(client, remotePeerId);
        lock (Gate)
        {
            Connections.Add(connection);
            if (remotePeerId > 0)
                ConnectionsByPeer[remotePeerId] = connection;
            IsConnected = true;
            LastError = "";
        }
        ConnectedEvent.Set();
        FlushPendingRemote();
        connection.ReadTask = Task.Run(() => ReadLoop(connection));
    }

    void ReadLoop(RemoteConnection connection)
    {
        try
        {
            while (!Disposed)
            {
                byte[] lengthBytes = ReadExact(connection, 4);
                if (lengthBytes == null)
                    break;
                int length = BitConverter.ToInt32(lengthBytes, 0);
                if (length <= 0 || length > 1_048_576)
                    throw new InvalidDataException($"Invalid lockstep packet length {length}");
                byte[] payload = ReadExact(connection, length);
                if (payload == null)
                    break;

                DecodedLockstepMessage decoded = LockstepMessageCodec.Decode(payload);
                lock (Gate)
                {
                    MapConnection(decoded.Message.FromPeer, connection);
                    InboundRemote.Enqueue(new RemoteEnvelope(decoded.ToPeer, decoded.Message));
                    RemoteMessageCount++;
                }
                TraceAuthoritativeFrame("read-remote", decoded.ToPeer, decoded.Message, connection.PeerId);
                FlushPendingRemote();
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
            lock (Gate)
            {
                connection.Connected = false;
                if (connection.PeerId > 0
                    && ConnectionsByPeer.TryGetValue(connection.PeerId, out RemoteConnection current)
                    && ReferenceEquals(current, connection))
                    ConnectionsByPeer.Remove(connection.PeerId);
                IsConnected = Connections.Any(c => c.Connected);
            }
        }
    }

    byte[] ReadExact(RemoteConnection connection, int length)
    {
        byte[] bytes = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = connection.Stream.Read(bytes, offset, length - offset);
            if (read == 0)
                return null;
            offset += read;
        }
        return bytes;
    }

    void SendRemote(RemoteEnvelope envelope)
    {
        RemoteConnection connection;
        bool pending = false;
        lock (Gate)
        {
            if (!TryGetConnectionLocked(envelope.ToPeer, out connection))
            {
                PendingRemote.Enqueue(envelope);
                pending = true;
            }
        }
        if (pending)
        {
            TraceAuthoritativeFrame("send-remote-pending", envelope.ToPeer, envelope.Message);
            return;
        }
        WriteRemote(envelope, connection);
    }

    void FlushPendingRemote()
    {
        while (true)
        {
            RemoteEnvelope[] pending;
            lock (Gate)
            {
                if (PendingRemote.Count == 0)
                    return;
                pending = PendingRemote.ToArray();
                PendingRemote.Clear();
            }

            bool wroteAny = false;
            foreach (RemoteEnvelope envelope in pending)
            {
                RemoteConnection connection;
                bool waiting = false;
                lock (Gate)
                {
                    if (!TryGetConnectionLocked(envelope.ToPeer, out connection))
                    {
                        PendingRemote.Enqueue(envelope);
                        waiting = true;
                    }
                }
                if (waiting)
                {
                    TraceAuthoritativeFrame("flush-pending-wait", envelope.ToPeer, envelope.Message);
                    continue;
                }
                WriteRemote(envelope, connection);
                wroteAny = true;
            }
            if (!wroteAny)
                return;
        }
    }

    void WriteRemote(RemoteEnvelope envelope, RemoteConnection connection)
    {
        try
        {
            byte[] payload = LockstepMessageCodec.Encode(envelope.Message, envelope.ToPeer);
            byte[] length = BitConverter.GetBytes(payload.Length);
            lock (connection.WriteGate)
            {
                if (!connection.Connected || connection.Stream == null)
                {
                    lock (Gate)
                        PendingRemote.Enqueue(envelope);
                    return;
                }
                connection.Stream.Write(length, 0, length.Length);
                connection.Stream.Write(payload, 0, payload.Length);
                connection.Stream.Flush();
                lock (Gate)
                    RemoteWriteCount++;
            }
            TraceAuthoritativeFrame("write-remote", envelope.ToPeer, envelope.Message, connection.PeerId);
        }
        catch (Exception ex)
        {
            lock (Gate)
            {
                LastError = ex.Message;
                connection.Connected = false;
                IsConnected = Connections.Any(c => c.Connected);
                PendingRemote.Enqueue(envelope);
            }
            TraceAuthoritativeFrame("write-remote-error", envelope.ToPeer, envelope.Message, connection.PeerId);
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

    void MapConnection(int peerId, RemoteConnection connection)
    {
        if (peerId <= 0)
            return;
        connection.PeerId = peerId;
        ConnectionsByPeer[peerId] = connection;
    }

    bool TryGetConnectionLocked(int peerId, out RemoteConnection connection)
    {
        if (ConnectionsByPeer.TryGetValue(peerId, out connection) && connection.Connected)
            return true;

        connection = null;
        if (RemotePeerId == peerId)
            connection = Connections.FirstOrDefault(c => c.Connected);
        return connection != null;
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

        lock (Gate)
            DeliveredMessageCount++;
        TraceAuthoritativeFrame("deliver", peerId, message);
        if (observers != null)
            foreach (Action<LockstepMessage> observer in observers)
                observer(message);
        receiver?.Invoke(message);
    }

    void TraceAuthoritativeFrame(string stage, int toPeer, LockstepMessage message, int connectionPeer = 0)
    {
        Action<string> sink = AuthoritativeFrameTrace;
        if (sink == null || !TryDescribeAuthoritativeFrame(message, out string frame))
            return;

        int local;
        int remoteRead;
        int remoteWrite;
        int delivered;
        int pending;
        int inbound;
        bool connected;
        string error;
        lock (Gate)
        {
            local = LocalMessageCount;
            remoteRead = RemoteMessageCount;
            remoteWrite = RemoteWriteCount;
            delivered = DeliveredMessageCount;
            pending = PendingRemote.Count;
            inbound = InboundRemote.Count;
            connected = IsConnected;
            error = LastError ?? "";
        }

        try
        {
            sink($"{stage} to={toPeer} from={message.FromPeer} connPeer={connectionPeer} {frame} "
                 + $"counts local={local} remoteRead={remoteRead} remoteWrite={remoteWrite} "
                 + $"delivered={delivered} pending={pending} inbound={inbound} connected={connected} "
                 + $"error='{OneLine(error)}'");
        }
        catch
        {
            // Diagnostics must never perturb the transport.
        }
    }

    static bool TryDescribeAuthoritativeFrame(LockstepMessage message, out string frame)
    {
        switch (message)
        {
            case AuthoritativeCommandRequestMessage request:
                frame = $"frame=req seq={request.Sequence} empire={request.EmpireId} kind={request.Kind} "
                        + $"subject={request.SubjectId} target={request.TargetId} "
                        + $"textChars={(request.Text ?? "").Length}";
                return true;
            case AuthoritativeCommandResultMessage result:
                frame = $"frame=result seq={result.Sequence} origin={result.OriginPeer} "
                        + $"accepted={result.Accepted} tick={result.Tick} reasonChars={(result.Reason ?? "").Length}";
                return true;
            case AuthoritativeStateSnapshotMessage snapshot:
                frame = $"frame=snapshot tick={snapshot.Tick} hash=0x{snapshot.HashHi:X16}:0x{snapshot.HashLo:X16} "
                        + $"digest='{OneLine(snapshot.SyncDigest)}' payloadChars={(snapshot.Payload ?? "").Length}";
                return true;
            default:
                frame = "";
                return false;
        }
    }

    static string OneLine(string text)
        => string.IsNullOrEmpty(text) ? "" : text.Replace('\r', ' ').Replace('\n', ' ');

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

    sealed class RemoteConnection
    {
        public readonly TcpClient Client;
        public readonly NetworkStream Stream;
        public readonly object WriteGate = new();
        public Task ReadTask;
        public int PeerId;
        public bool Connected = true;

        public RemoteConnection(TcpClient client, int peerId)
        {
            Client = client;
            Stream = client.GetStream();
            PeerId = peerId;
        }
    }
}

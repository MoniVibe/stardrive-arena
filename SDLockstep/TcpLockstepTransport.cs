using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SDLockstep;

public sealed class TcpLockstepHazardProfile
{
    public int Seed { get; set; }
    public int LatencyMs { get; set; }
    public int JitterMs { get; set; }
    public int BurstEvery { get; set; }
    public int BurstDelayMs { get; set; }

    public bool Enabled => LatencyMs > 0 || JitterMs > 0 || (BurstEvery > 0 && BurstDelayMs > 0);

    public int DelayFor(int sequence, int toPeer, LockstepMessage message)
    {
        if (!Enabled || message == null)
            return 0;

        int delay = Math.Max(0, LatencyMs);
        int jitter = Math.Max(0, JitterMs);
        if (jitter > 0)
        {
            int span = checked(jitter * 2 + 1);
            delay += StableModulo(Mix(sequence, toPeer, message.FromPeer, message.GetType().Name), span) - jitter;
        }

        if (BurstEvery > 0 && BurstDelayMs > 0 && sequence % BurstEvery == 0)
            delay += BurstDelayMs;

        return Math.Max(0, delay);
    }

    int Mix(int sequence, int toPeer, int fromPeer, string typeName)
    {
        unchecked
        {
            int h = Seed == 0 ? 0x5EED4A11 : Seed;
            h = (h * 397) ^ sequence;
            h = (h * 397) ^ toPeer;
            h = (h * 397) ^ fromPeer;
            for (int i = 0; i < typeName.Length; ++i)
                h = (h * 397) ^ typeName[i];
            return h;
        }
    }

    static int StableModulo(int value, int modulus)
    {
        if (modulus <= 1)
            return 0;
        uint unsigned = unchecked((uint)value);
        return (int)(unsigned % (uint)modulus);
    }
}

public sealed class TcpLockstepHazardStats
{
    public int DelayedMessages { get; internal set; }
    public long TotalDelayMs { get; internal set; }
    public int MaxDelayMs { get; internal set; }

    internal void Record(int delayMs)
    {
        if (delayMs <= 0)
            return;
        DelayedMessages++;
        TotalDelayMs += delayMs;
        MaxDelayMs = Math.Max(MaxDelayMs, delayMs);
    }
}

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
    // Explicit peer-id routing aliases: sends addressed to KEY travel over the connection
    // currently mapped for VALUE. Needed when two protocol layers share one transport with
    // different peer-id address spaces (e.g. the Arena lobby speaks authority=1/joiner=slotN
    // while the Arena fight lockstep speaks host=0/player1=1/player2=2).
    readonly Dictionary<int, int> PeerRoutes = new();
    readonly HashSet<int> UnroutablePeersAlarmed = new();
    readonly object Gate = new();
    readonly ManualResetEventSlim ConnectedEvent = new(false);

    TcpListener Listener;
    Task AcceptTask;
    bool Disposed;
    readonly bool MultiRemote;
    int HazardSequence;

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
    /// <summary>
    /// Fired (once per destination peer) when a message addressed to a peer cannot be routed to
    /// any live connection even though the transport IS connected — i.e. a silent send-to-nowhere.
    /// Also fired when a registered observer throws during delivery. Diagnostics only; never
    /// treated as a transport error.
    /// </summary>
    public Action<string> RoutingAlarm { get; set; }
    public TcpLockstepHazardProfile HazardProfile { get; set; }
    public TcpLockstepHazardStats HazardStats { get; } = new();

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
        transport.AcceptTask = StartLongRunning(transport.AcceptLoop);
        return transport;
    }

    public static TcpLockstepTransport HostMulti(int port)
    {
        var transport = new TcpLockstepTransport(remotePeerId: 0, multiRemote: true);
        transport.Listener = new TcpListener(IPAddress.Any, port);
        transport.Listener.Start();
        transport.AcceptTask = StartLongRunning(transport.AcceptLoop);
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
        if (!transport.WaitForOutboundIdle(TimeSpan.FromSeconds(5)))
        {
            lock (transport.Gate)
                transport.LastError = $"Timed out sending peer hello for {localPeerId}.";
        }
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

    /// <summary>
    /// Routes future sends addressed to <paramref name="peerId"/> over the connection mapped for
    /// <paramref name="viaPeerId"/>. Use when a session protocol addresses peers by different ids
    /// than the ids the underlying connections were announced with (Arena fight lockstep over a
    /// lobby transport). Idempotent; safe to call before the connection exists.
    /// </summary>
    public void MapPeerRoute(int peerId, int viaPeerId)
    {
        lock (Gate)
        {
            PeerRoutes[peerId] = viaPeerId;
            UnroutablePeersAlarmed.Remove(peerId);
        }
        // A route can make previously parked messages deliverable.
        FlushPendingRemote();
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

    public bool WaitForOutboundIdle(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            FlushPendingRemote();
            lock (Gate)
            {
                if (PendingRemote.Count == 0
                    && Connections.All(c => !c.Connected || (c.Outbound.Count == 0 && c.ActiveWrites == 0)))
                    return true;
            }
            Thread.Sleep(1);
        }

        lock (Gate)
            return PendingRemote.Count == 0
                   && Connections.All(c => !c.Connected || (c.Outbound.Count == 0 && c.ActiveWrites == 0));
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
            try { connection.Outbound.CompleteAdding(); } catch { }
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
        connection.WriteTask = StartLongRunning(() => WriteLoop(connection));
        FlushPendingRemote();
        connection.ReadTask = StartLongRunning(() => ReadLoop(connection));
    }

    static Task StartLongRunning(Action action)
        => Task.Factory.StartNew(action, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

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
            AlarmIfUnroutable(envelope.ToPeer, envelope.Message);
            return;
        }
        QueueRemoteWrite(envelope, connection);
    }

    // A pending send while the transport already has live connections means the destination
    // peer id has no route — under the old code this was a SILENT send-to-nowhere (the exact
    // failure mode of the 2026-07-05 live arm-handshake deadlock). Alarm loudly, once per peer.
    void AlarmIfUnroutable(int toPeer, LockstepMessage message)
    {
        Action<string> alarm = RoutingAlarm;
        bool fire;
        lock (Gate)
        {
            fire = Connections.Any(c => c.Connected)
                   && !ConnectionsByPeer.ContainsKey(toPeer)
                   && !PeerRoutes.ContainsKey(toPeer)
                   && RemotePeerId != toPeer
                   && UnroutablePeersAlarmed.Add(toPeer);
        }
        if (!fire || alarm == null)
            return;
        try
        {
            alarm($"Lockstep send to peer {toPeer} ({message?.GetType().Name}) has no routable "
                  + "connection: the transport is connected but no connection is mapped for that "
                  + "peer id and no MapPeerRoute alias exists. The message is parked in "
                  + "PendingRemote and will never reach the wire until a route appears.");
        }
        catch
        {
            // Diagnostics must never perturb the transport.
        }
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
                QueueRemoteWrite(envelope, connection);
                wroteAny = true;
            }
            if (!wroteAny)
                return;
        }
    }

    void QueueRemoteWrite(RemoteEnvelope envelope, RemoteConnection connection)
    {
        if (connection == null || !connection.Connected || connection.Outbound.IsAddingCompleted)
        {
            lock (Gate)
                PendingRemote.Enqueue(envelope);
            TraceAuthoritativeFrame("send-remote-pending", envelope.ToPeer, envelope.Message,
                connection?.PeerId ?? 0);
            return;
        }

        try
        {
            connection.Outbound.Add(envelope);
            TraceAuthoritativeFrame("queue-remote", envelope.ToPeer, envelope.Message, connection.PeerId);
        }
        catch (InvalidOperationException)
        {
            lock (Gate)
                PendingRemote.Enqueue(envelope);
        }
    }

    void WriteLoop(RemoteConnection connection)
    {
        try
        {
            foreach (RemoteEnvelope envelope in connection.Outbound.GetConsumingEnumerable())
            {
                Interlocked.Increment(ref connection.ActiveWrites);
                try
                {
                    WriteRemote(envelope, connection);
                }
                finally
                {
                    Interlocked.Decrement(ref connection.ActiveWrites);
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
    }

    void WriteRemote(RemoteEnvelope envelope, RemoteConnection connection)
    {
        try
        {
            int delayMs = HazardDelay(envelope);
            if (delayMs > 0)
                Thread.Sleep(delayMs);

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

    int HazardDelay(RemoteEnvelope envelope)
    {
        TcpLockstepHazardProfile profile = HazardProfile;
        if (profile == null || !profile.Enabled)
            return 0;

        int sequence = Interlocked.Increment(ref HazardSequence);
        int delayMs = profile.DelayFor(sequence, envelope.ToPeer, envelope.Message);
        if (delayMs > 0)
        {
            HazardStats.Record(delayMs);
            TraceAuthoritativeFrame($"hazard-delay ms={delayMs} seq={sequence}", envelope.ToPeer,
                envelope.Message);
        }
        return delayMs;
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

        // Explicit peer route: the session layer addresses this peer by a different id than
        // the id the connection was announced with.
        if (PeerRoutes.TryGetValue(peerId, out int viaPeer)
            && ConnectionsByPeer.TryGetValue(viaPeer, out connection) && connection.Connected)
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
        int observerCount = 0;
        bool hasReceiver;
        lock (Gate)
        {
            Receivers.TryGetValue(peerId, out receiver);
            hasReceiver = receiver != null;
            if (Observers.TryGetValue(peerId, out List<Action<LockstepMessage>> list))
            {
                observers = new List<Action<LockstepMessage>>(list);
                observerCount = observers.Count;
            }
        }

        lock (Gate)
            DeliveredMessageCount++;
        TraceAuthoritativeFrame($"deliver receiver={hasReceiver} observers={observerCount}", peerId, message);
        if (observers != null)
        {
            foreach (Action<LockstepMessage> observer in observers)
            {
                try
                {
                    observer(message);
                }
                catch (Exception ex)
                {
                    // A throwing observer (e.g. one registered by an already-exited screen that
                    // shares this transport) must not break later observers or the receiver.
                    try { RoutingAlarm?.Invoke($"Lockstep observer for peer {peerId} threw during "
                        + $"delivery of {message?.GetType().Name}: {ex.Message}"); }
                    catch { }
                }
            }
        }
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
        public readonly BlockingCollection<RemoteEnvelope> Outbound = new();
        public readonly object WriteGate = new();
        public Task ReadTask;
        public Task WriteTask;
        public int PeerId;
        public bool Connected = true;
        public int ActiveWrites;

        public RemoteConnection(TcpClient client, int peerId)
        {
            Client = client;
            Stream = client.GetStream();
            PeerId = peerId;
        }
    }
}

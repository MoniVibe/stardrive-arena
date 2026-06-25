using System.Collections.Generic;

namespace SDLockstep;

/// <summary>
/// Host-authoritative lockstep coordinator (peer 0), advisor plan VS8. Buffers client command
/// submissions by their execute tick, broadcasts exactly one <see cref="CommandFrame"/> per tick
/// (empty if no commands) to every client, and collects per-tick checksums for desync detection.
///
/// The host does NOT simulate authoritatively — every client runs the full sim. The host's authority
/// is ordering: it assigns the canonical command frame for each tick, so all clients apply identical
/// input and therefore reach identical state.
/// </summary>
public sealed class LockstepHost
{
    public const int HostPeerId = 0;

    readonly ILockstepTransport Transport;
    readonly List<int> Clients = new();
    readonly Dictionary<uint, CommandFrame> Pending = new();

    public DesyncDetector Desync { get; } = new();

    /// <summary>The authoritative record of every committed frame — replay it to reproduce the match (VS6/VS10).</summary>
    public CommandLog History { get; } = new();

    public MultiplayerKernelState State;

    public LockstepHost(ILockstepTransport transport)
    {
        Transport = transport;
        State.Role = 0;
        Transport.Register(HostPeerId, OnReceive);
    }

    public void AddClient(int peerId)
    {
        if (!Clients.Contains(peerId))
            Clients.Add(peerId);
    }

    void OnReceive(LockstepMessage msg)
    {
        switch (msg)
        {
            case SubmitCommandMessage s:
                State.CommandsReceived++;
                uint execTick = s.Command.Tick;
                if (!Pending.TryGetValue(execTick, out CommandFrame f))
                {
                    f = new CommandFrame(execTick);
                    Pending[execTick] = f;
                }
                f.Add(s.Command);
                break;

            case ChecksumMessage c:
                Desync.Report(c.FromPeer, c.Tick, c.Lo, c.Hi);
                State.LastStateHashTick = c.Tick;
                State.LastStampedHashLo = c.Lo;
                State.LastStampedHashHi = c.Hi;
                break;
        }
    }

    /// <summary>Build and broadcast the committed frame for <paramref name="tick"/> to every client
    /// (empty frame if no commands were submitted for it).</summary>
    public void CommitTick(uint tick)
    {
        if (Pending.TryGetValue(tick, out CommandFrame frame))
            Pending.Remove(tick);
        else
            frame = new CommandFrame(tick);

        frame.Sort();
        History.Record(frame);
        State.CommitTick = tick;
        foreach (int client in Clients)
            Transport.Send(client, new CommandFrameMessage { FromPeer = HostPeerId, Frame = frame });
    }
}

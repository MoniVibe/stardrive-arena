namespace SDLockstep;

/// <summary>
/// A lockstep client (peer id &gt;= 1), advisor plan VS8. Wraps an <see cref="ILockstepSimulation"/>.
/// Submits local commands to the host, buffers broadcast frames, and advances ONLY when the next tick's
/// frame is present — then applies it and reports the authoritative checksum back to the host.
/// "Advance only when the frame exists" is the lockstep barrier that keeps clients in step.
/// </summary>
public sealed class LockstepClient
{
    readonly ILockstepTransport Transport;
    readonly int PeerId;
    readonly ILockstepSimulation Sim;
    readonly CommandFrameBuffer Buffer = new();

    public MultiplayerKernelState State;

    public LockstepClient(ILockstepTransport transport, int peerId, ILockstepSimulation sim)
    {
        Transport = transport;
        PeerId = peerId;
        Sim = sim;
        State.Role = 1;
        State.LocalPlayerId = peerId;
        Transport.Register(peerId, OnReceive);
    }

    void OnReceive(LockstepMessage msg)
    {
        if (msg is CommandFrameMessage f)
            Buffer.Add(f.Frame);
    }

    /// <summary>Submit a local command; it executes at <c>command.Tick</c> on all clients.</summary>
    public void Submit(SimCommand command)
        => Transport.Send(LockstepHost.HostPeerId, new SubmitCommandMessage { FromPeer = PeerId, Command = command });

    /// <summary>Apply every consecutive buffered frame, reporting each tick's checksum to the host.</summary>
    public void Pump()
    {
        while (Buffer.Has(Sim.Tick))
        {
            uint tick = Sim.Tick;
            CommandFrame frame = Buffer.Take(tick);
            Sim.Apply(frame);
            State.CommandsApplied += frame.Commands.Count;

            (ulong lo, ulong hi) = Sim.Hash();
            State.CurrentTick = Sim.Tick;
            State.LastStateHashTick = tick;
            State.LastStampedHashLo = lo;
            State.LastStampedHashHi = hi;

            Transport.Send(LockstepHost.HostPeerId,
                new ChecksumMessage { FromPeer = PeerId, Tick = tick, Lo = lo, Hi = hi });
        }
    }

    public (ulong lo, ulong hi) Hash() => Sim.Hash();
}

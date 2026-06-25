using System;
using System.Collections.Generic;

namespace SDLockstep;

/// <summary>
/// The simulation the lockstep kernel drives. StarDrive implements this over UniverseState in a real
/// clone (the VS8 integration); SDLockstep ships <see cref="ToySimulation"/> for the headless proof.
/// Contract: <see cref="Apply"/> processes the frame's commands and advances EXACTLY one fixed tick.
/// </summary>
public interface ILockstepSimulation
{
    uint Tick { get; }
    void Apply(CommandFrame frame);
    (ulong lo, ulong hi) Hash();
}

/// <summary>
/// A simulation that can snapshot/restore its full state — required for join-in-progress and desync
/// resync (advisor plan VS10). StarDrive implements this by serializing authoritative UniverseState
/// (RNG stream states, pending queues, tick, fixed-point state).
/// </summary>
public interface ISnapshotableSimulation : ILockstepSimulation
{
    byte[] SaveState();
    void LoadState(byte[] data);
}

/// <summary>In-process / network message envelope. FromPeer is the sender's peer id.</summary>
public abstract class LockstepMessage { public int FromPeer; }
public sealed class SubmitCommandMessage : LockstepMessage { public SimCommand Command; }
public sealed class CommandFrameMessage : LockstepMessage { public CommandFrame Frame; }
public sealed class ChecksumMessage : LockstepMessage { public uint Tick; public ulong Lo; public ulong Hi; }

/// <summary>Reliable in-order message transport. <see cref="FakeTransport"/> for tests; LiteNetLib at VS11.</summary>
public interface ILockstepTransport
{
    void Register(int peerId, Action<LockstepMessage> onReceive);
    void Send(int toPeer, LockstepMessage message);
    void Poll();
}

/// <summary>Session counters mirrored from PureDOTS MultiplayerKernelState (advisor reuse, VS8).</summary>
public struct MultiplayerKernelState
{
    public int Role;            // 0 = host, 1 = client
    public int LocalPlayerId;
    public uint CurrentTick;
    public uint CommitTick;
    public long CommandsReceived;
    public long CommandsApplied;
    public long CommandsRejected;
    public uint LastStateHashTick;
    public ulong LastStampedHashLo;
    public ulong LastStampedHashHi;
    public uint LastResyncTick;
}

/// <summary>Client-side store of broadcast frames, keyed by tick. Advance only when the next frame exists.</summary>
public sealed class CommandFrameBuffer
{
    readonly Dictionary<uint, CommandFrame> Frames = new();

    public void Add(CommandFrame frame) => Frames[frame.Tick] = frame;
    public bool Has(uint tick) => Frames.ContainsKey(tick);

    public CommandFrame Take(uint tick)
    {
        if (Frames.TryGetValue(tick, out CommandFrame f)) { Frames.Remove(tick); return f; }
        return null;
    }
}

/// <summary>Compares per-tick checksums across clients and reports the FIRST divergent tick.</summary>
public sealed class DesyncDetector
{
    readonly Dictionary<uint, (ulong lo, ulong hi, int peer)> First = new();

    public bool HasDesync { get; private set; }
    public long FirstDivergentTick { get; private set; } = -1;
    public int DivergentPeer { get; private set; } = -1;

    public void Report(int peer, uint tick, ulong lo, ulong hi)
    {
        if (First.TryGetValue(tick, out (ulong lo, ulong hi, int peer) prev))
        {
            if ((prev.lo != lo || prev.hi != hi) && !HasDesync)
            {
                HasDesync = true;
                FirstDivergentTick = tick;
                DivergentPeer = peer;
            }
        }
        else
        {
            First[tick] = (lo, hi, peer);
        }
    }
}

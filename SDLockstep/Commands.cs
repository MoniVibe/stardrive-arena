using System.Collections.Generic;

namespace SDLockstep;

/// <summary>
/// Slice command kinds (advisor plan VS7). Explicit stable numeric values — the wire/replay protocol
/// depends on these, so never renumber; add new kinds with new values.
/// </summary>
public enum SimCommandKind : byte
{
    NoOp = 0,          // required so empty frames still carry intent
    MoveShip = 1,
    AttackTarget = 2,
    StopShip = 3,
    Forfeit = 4,       // arena 8-player peer-drop (ruling C9): host proposes on wall-clock miss, every peer
                       // applies "mark SubjectId slot's ships dead + drop from LivePeers" on the committed
                       // exec tick. Append-only — never renumber. Forfeited slot rides in SimCommand.SubjectId.
    DebugSpawn = 200,  // test-only, pre-tick-0
}

/// <summary>
/// One tick-stamped player command — the unit lockstep sends over the wire. Engine-agnostic:
/// positions are carried as Fixed64 raw longs (deterministic), ids are stable sim ids. Immutable.
/// </summary>
public readonly struct SimCommand
{
    public const ushort Schema = 1;

    public readonly uint Tick;
    public readonly int PlayerId;
    public readonly uint PlayerLocalSequence;
    public readonly SimCommandKind Kind;
    public readonly int SubjectId;   // ship being commanded (stable sim id); -1 if n/a
    public readonly int TargetId;    // attack target / secondary id; -1 if n/a
    public readonly long PosXRaw;    // Fixed64 raw — MoveShip destination X
    public readonly long PosYRaw;    // Fixed64 raw — MoveShip destination Y

    public SimCommand(uint tick, int playerId, uint localSequence, SimCommandKind kind,
                      int subjectId = -1, int targetId = -1, long posXRaw = 0, long posYRaw = 0)
    {
        Tick = tick;
        PlayerId = playerId;
        PlayerLocalSequence = localSequence;
        Kind = kind;
        SubjectId = subjectId;
        TargetId = targetId;
        PosXRaw = posXRaw;
        PosYRaw = posYRaw;
    }

    /// <summary>Canonical same-tick ordering: playerId, then local sequence, then kind.</summary>
    public int CompareInTick(in SimCommand o)
    {
        if (PlayerId != o.PlayerId) return PlayerId < o.PlayerId ? -1 : 1;
        if (PlayerLocalSequence != o.PlayerLocalSequence) return PlayerLocalSequence < o.PlayerLocalSequence ? -1 : 1;
        return ((byte)Kind).CompareTo((byte)o.Kind);
    }
}

/// <summary>
/// All commands committed at a given tick, in canonical order — the broadcast unit. Every client
/// applies the identical frame at the identical tick, which is the whole point of lockstep.
/// </summary>
public sealed class CommandFrame
{
    public uint Tick;
    public readonly List<SimCommand> Commands = new();

    public CommandFrame(uint tick) => Tick = tick;

    public void Add(in SimCommand c) => Commands.Add(c);

    public bool IsEmpty => Commands.Count == 0;

    /// <summary>Sort into canonical order (playerId, localSeq, kind). Required before apply/hash.</summary>
    public void Sort() => Commands.Sort((a, b) => a.CompareInTick(b));
}

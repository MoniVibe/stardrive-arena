using SDUtils.Deterministic;

namespace Ship_Game.Determinism;

/// <summary>
/// Hash lanes (advisor plan §M1). Separating the canonical hash into lanes makes a divergence
/// report actionable: when two runs disagree, the lane hashes localize which subsystem moved.
/// </summary>
public enum DetLane
{
    Universe, Rng, Commands, Empires, Economy, Ships, ShipAI,
    Fleets, Planets, Combat, Spatial, Diplomacy, Research, Production
}

/// <summary>
/// Lane-aware debug determinism checksum (advisor plan §M1 + Reworks 1/2/4). Implements the versioned
/// <see cref="IDeterminismChecksum"/> surface, so a single state traversal can feed EITHER this (to get
/// per-lane debug hashes for divergence localization) OR a plain authoritative checksum (the wire value).
/// Per-lane and combined accumulators are FNV-1a-64 (<see cref="AlgorithmId"/> = "Fnv1a64-v1").
/// </summary>
public sealed class DeterminismHashWriter : DeterminismChecksumBase
{
    public const int LaneCount = 14;

    readonly DetHash[] LaneHashes = new DetHash[LaneCount];
    DetHash CombinedHash;
    int Active;

    public DeterminismHashWriter()
    {
        for (int i = 0; i < LaneCount; ++i)
            LaneHashes[i] = DetHash.New();
        CombinedHash = DetHash.New();
    }

    public override string AlgorithmId => "Fnv1a64-v1";

    public override void Lane(int laneIndex) => Active = laneIndex;

    /// <summary>Lane selection convenience (returns this for chaining).</summary>
    public DeterminismHashWriter Lane(DetLane lane)
    {
        Active = (int)lane;
        return this;
    }

    public override void WriteByte(byte b)
    {
        LaneHashes[Active].AddByte(b);
        CombinedHash.AddByte(b);
    }

    public override ulong Finish64() => CombinedHash.Value;

    public override (ulong lo, ulong hi) Finish128()
        => (CombinedHash.Value, DetRandom.Mix64(CombinedHash.Value ^ 0x9E3779B97F4A7C15UL));

    /// <summary>Combined hash over every lane — the canonical per-tick debug checksum.</summary>
    public ulong Full => CombinedHash.Value;

    /// <summary>Per-lane hash, for localizing a divergence to a subsystem.</summary>
    public ulong LaneHash(DetLane lane) => LaneHashes[(int)lane].Value;
}

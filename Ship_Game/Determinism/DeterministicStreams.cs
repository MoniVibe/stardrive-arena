using SDUtils.Deterministic;
using Ship_Game.Utils;

namespace Ship_Game.Determinism;

/// <summary>Logical RNG stream kinds, so different concerns draw from independent streams.</summary>
public enum RngStreamKind : ulong
{
    Universe = 1,
    Empire = 2,
    SolarBody = 3,
    Ship = 4,
    Event = 5,
}

/// <summary>
/// Save-independent per-entity deterministic RNG streams (advisor plan §M3 seed topology + Rework 3).
/// Delegates to the proven, parallel-safe <see cref="DetStreams"/> primitive (DetMiniSim proves
/// PARALLEL == SEQUENTIAL). The §M4 migration replaces the clock-seeded ThreadSafeRandom owners with
/// these streams.
///
/// Hard rule (Rework 3): a shared RNG must never cross a Parallel.For boundary. Inside parallel
/// per-entity loops use <see cref="ForTick"/> (keyed by entity + tick) so a draw never depends on
/// thread scheduling — that is the safe path, and it is the shortest one.
/// </summary>
public static class DeterministicStreams
{
    /// <summary>An independent reproducible RNG for an entity (e.g. an empire or body owner).</summary>
    public static DeterministicRandom For(ulong rootSeed, RngStreamKind kind, ulong stableId)
        => DeterministicRandom.FromStream(DetStreams.ForEntity(rootSeed, (ulong)kind, stableId));

    /// <summary>
    /// A fresh per-(entity, tick) RNG — the parallel-safe form for use inside Parallel.For over entities.
    /// </summary>
    public static DeterministicRandom ForTick(ulong rootSeed, RngStreamKind kind, ulong stableId, ulong tick)
        => DeterministicRandom.FromStream(DetStreams.ForEntityTick(rootSeed, (ulong)kind, stableId, tick));
}

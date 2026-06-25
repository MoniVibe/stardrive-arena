namespace SDUtils.Deterministic;

/// <summary>
/// Parallel-safe deterministic RNG stream derivation (advisor plan, Rework 3).
///
/// The cardinal rule of deterministic parallel sim: <b>no shared mutable RNG stream may cross a
/// Parallel.For boundary</b> — the value a draw returns must never depend on which worker ran first.
/// This type makes the safe path the shortest path: a worker asks for a stream keyed by stable
/// identity (and tick), never a shared object.
///
///  - <see cref="ForEntity"/> — one independent stream per (rootSeed, streamKind, stableId).
///  - <see cref="ForEntityTick"/> — a fresh per (entity, tick) stream; the preferred form inside a
///    parallel per-entity loop, since it depends only on inputs, never on scheduling. Proven
///    order-independent by <see cref="DetMiniSim"/> (PARALLEL == SEQUENTIAL).
/// </summary>
public static class DetStreams
{
    const ulong KindMul = 0x9E3779B97F4A7C15UL;
    const ulong IdAdd = 0xD1B54A32D192ED03UL;

    /// <summary>An independent, reproducible RNG for an entity, derived from the world root seed.</summary>
    public static DetRandom ForEntity(ulong rootSeed, ulong streamKind, ulong stableId)
    {
        ulong seed = DetRandom.Mix64(rootSeed
                     ^ DetRandom.Mix64(streamKind * KindMul)
                     ^ DetRandom.Mix64(stableId + IdAdd));
        return new DetRandom(seed == 0 ? 1UL : seed);
    }

    /// <summary>
    /// A fresh per-(entity, tick) RNG. Use this inside parallel per-entity loops so draws never
    /// depend on thread interleaving. Equivalent to <c>ForEntity(...).Fork(tick)</c>.
    /// </summary>
    public static DetRandom ForEntityTick(ulong rootSeed, ulong streamKind, ulong stableId, ulong tick)
        => ForEntity(rootSeed, streamKind, stableId).Fork(tick);
}

using SDUtils.Deterministic;

namespace Ship_Game.Utils;

/// <summary>
/// Lockstep-grade deterministic RNG backend for <see cref="RandomBase"/>.
///
/// Backed by SplitMix64 (integer-only), so its draws are bit-identical across machines/.NET
/// versions and its state is exactly save/restorable via <see cref="RandomBase.TryGetState"/> /
/// <see cref="RandomBase.SetState"/>. Use this for any owner whose draws must be reproducible
/// (UniverseState, Empire, SolarSystemBody, ...).
///
/// NOT thread-safe: it is a single advancing stream. For parallel per-entity consumption, give
/// each entity its own stream via <see cref="Fork"/> so draws never depend on thread interleaving.
/// </summary>
public sealed class DeterministicRandom : RandomBase
{
    DetRandom Rng;

    public DeterministicRandom(int seed) : base(seed == 0 ? 1 : seed)
    {
        Rng = new DetRandom((ulong)(uint)Seed);
    }

    public DeterministicRandom(ulong seed64) : base(seed64 == 0 ? 1 : unchecked((int)seed64))
    {
        Rng = new DetRandom(seed64 == 0 ? 1UL : seed64);
    }

    DeterministicRandom(DetRandom forked, int seedTag) : base(seedTag == 0 ? 1 : seedTag)
    {
        Rng = forked;
    }

    /// <summary>Wrap a pre-derived deterministic stream (e.g. from DetStreams) as a RandomBase.</summary>
    public static DeterministicRandom FromStream(DetRandom rng) => new(rng, 0);

    protected override double NextUnitDouble() => Rng.NextDouble();

    protected override int NextIntExclusive(int minInclusive, int maxExclusive)
        => Rng.NextInt(minInclusive, maxExclusive);

    public override bool TryGetState(out ulong state)
    {
        state = Rng.State;
        return true;
    }

    public override void SetState(ulong state) => Rng.State = state;

    /// <summary>
    /// Derive an independent deterministic sub-stream (e.g. one per entity) for parallel-safe draws.
    /// </summary>
    public DeterministicRandom Fork(ulong streamId)
        => new(Rng.Fork(streamId), unchecked((int)streamId));
}

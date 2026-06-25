namespace SDUtils.Deterministic;

/// <summary>
/// .NET 8 scenario seed derivation helpers (mirrors the PureDOTS <c>ScenarioSeedUtility</c> shape,
/// adapted off the Unity DOTS types — no <c>math.hash</c>, no <c>uint4</c>, no Burst).
///
/// This is a THIN convenience layer over the existing determinism primitives:
///  - String hashing reuses the FNV-1a construction (see <see cref="StableHash32"/>).
///  - Numeric mixing reuses <see cref="DetRandom.Mix64"/> (SplitMix64 finalizer).
///  - Per-entity / per-(entity, tick) derivation reuses <see cref="DetStreams"/>.
///
/// It does NOT introduce a second RNG or hash algorithm. For authoritative gameplay streams that the
/// lockstep protocol depends on, prefer <c>Ship_Game.Determinism.DeterministicStreams</c> directly so
/// the stream kind is explicit and self-documenting; <see cref="DeriveCommandSeed"/> and
/// <see cref="DeriveSpawnSeed"/> here are convenience wrappers over the same <see cref="DetStreams"/>
/// derivation for callers that only have a raw stable id.
/// </summary>
public static class ScenarioSeedUtilityNet8
{
    // FNV-1a 32-bit parameters. Matches the PureDOTS ScenarioSeedUtility content-id hash so the same
    // scenario/content string hashes identically on both engines.
    const uint Fnv32OffsetBasis = 2166136261u;
    const uint Fnv32Prime = 16777619u;

    /// <summary>
    /// Stable FNV-1a 32-bit hash over a string's UTF-16 code units (low byte then high byte per unit).
    ///
    /// This is deliberately NOT <see cref="object.GetHashCode"/> / <see cref="string.GetHashCode()"/> —
    /// the framework hash is randomized per process and not portable. Use this ONLY for content and
    /// scenario ids (stable, human-authored identifiers), never as a simulation-state checksum.
    /// </summary>
    public static uint StableHash32(string s)
    {
        unchecked
        {
            uint hash = Fnv32OffsetBasis;
            if (string.IsNullOrEmpty(s))
                return hash;

            for (int i = 0; i < s.Length; ++i)
            {
                char c = s[i];
                hash ^= (byte)c;
                hash *= Fnv32Prime;
                hash ^= (byte)(c >> 8);
                hash *= Fnv32Prime;
            }

            return hash;
        }
    }

    /// <summary>
    /// Derive a 64-bit sub-seed from a root seed and an ordered list of keys. ORDER-SENSITIVE:
    /// <c>DeriveSubSeed(r, a, b) != DeriveSubSeed(r, b, a)</c>. Each key is folded in via
    /// <see cref="DetRandom.Mix64"/> (the project's SplitMix64 finalizer) with a position-dependent
    /// gamma, so reordering or inserting keys changes the result.
    /// </summary>
    public static ulong DeriveSubSeed(ulong rootSeed, params ulong[] keys)
    {
        unchecked
        {
            // Prime with the root so an empty key list still yields a stable, root-dependent value.
            ulong acc = DetRandom.Mix64(rootSeed + Gamma);
            if (keys != null)
            {
                for (int i = 0; i < keys.Length; ++i)
                {
                    // Position-dependent mix keeps the fold order-sensitive.
                    acc = DetRandom.Mix64(acc ^ DetRandom.Mix64(keys[i] + (ulong)(i + 1) * Gamma));
                }
            }
            return acc;
        }
    }

    /// <summary>
    /// Derive a per-(stable id, tick) command seed. Equivalent to the state of the parallel-safe
    /// per-(entity, tick) stream produced by <see cref="DetStreams.ForEntityTick"/>, keyed on the
    /// <see cref="CommandStreamKind"/> lane. Use this for replayable per-command randomness.
    /// </summary>
    public static ulong DeriveCommandSeed(ulong rootSeed, ulong stableId, ulong tick)
        => DetStreams.ForEntityTick(rootSeed, CommandStreamKind, stableId, tick).State;

    /// <summary>
    /// Derive a per-stable-id spawn seed. Equivalent to the state of the per-entity stream produced by
    /// <see cref="DetStreams.ForEntity"/>, keyed on the <see cref="SpawnStreamKind"/> lane.
    /// </summary>
    public static ulong DeriveSpawnSeed(ulong rootSeed, ulong stableId)
        => DetStreams.ForEntity(rootSeed, SpawnStreamKind, stableId).State;

    const ulong Gamma = 0x9E3779B97F4A7C15UL;

    // Stream-kind lanes for the convenience derivations. Kept local so this helper has no dependency on
    // Ship_Game.Determinism.RngStreamKind (SDUtils is the lower layer). Distinct values keep command and
    // spawn streams decorrelated.
    const ulong CommandStreamKind = 0x436F6D6D616E6400UL; // "Command\0"
    const ulong SpawnStreamKind = 0x537061776E000000UL;   // "Spawn\0\0\0"
}

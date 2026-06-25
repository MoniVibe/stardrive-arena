using System.Collections.Generic;
using Ship_Game.Universe;

namespace Ship_Game.Determinism;

/// <summary>
/// Records and validates an authoritative per-tick hash trajectory (advisor plan replay validation;
/// mirrors the PureDOTS <c>ScenarioInputReplay</c> shape, adapted off the Unity DOTS types — no
/// NativeList / FixedString / Burst).
///
/// A recorder samples the canonical state hash each tick via
/// <see cref="UniverseStateHash.ComputeAuthoritativeStateHash"/>. The static
/// <see cref="ValidateTrajectory"/> entry point then compares two recorded trajectories element-wise and
/// reports the first divergent tick — it operates purely on recorded samples, so it is unit-testable
/// without spinning up a <see cref="UniverseState"/>.
///
/// The recorder also captures the <see cref="BuildFingerprint"/> hash and <see cref="DeterminismProfile"/>
/// it was recorded under, so comparing trajectories from incompatible builds/profiles is rejected up front
/// (<see cref="RejectsProfileMismatch"/>) instead of producing a misleading per-tick mismatch.
/// </summary>
public sealed class ScenarioInputReplay
{
    /// <summary>One recorded sample: the tick and its 128-bit authoritative state hash.</summary>
    public readonly List<(uint tick, ulong lo, ulong hi)> Samples = new();

    /// <summary>The math/determinism profile this trajectory was recorded under.</summary>
    public DeterminismProfile Profile { get; }

    /// <summary>Build-surface fingerprint for <see cref="Profile"/>, captured at construction.</summary>
    public ulong BuildFingerprintHash { get; }

    /// <summary>Algorithm id of the recorded hashes (set on the first appended sample).</summary>
    public string AlgorithmId { get; private set; } = "";

    public ScenarioInputReplay(DeterminismProfile profile)
    {
        Profile = profile;
        BuildFingerprintHash = BuildFingerprint.Compute(profile);
    }

    /// <summary>
    /// Append a tick sample taken from a live <see cref="UniverseState"/>. The hash is computed with the
    /// recorder's <see cref="Profile"/> via <see cref="UniverseStateHash.ComputeAuthoritativeStateHash"/>.
    /// </summary>
    public void RecordTick(uint tick, UniverseState us)
    {
        (ulong lo, ulong hi, string algorithm) = us.ComputeAuthoritativeStateHash(Profile);
        if (AlgorithmId.Length == 0)
            AlgorithmId = algorithm;
        Samples.Add((tick, lo, hi));
    }

    /// <summary>Append an already-computed sample (e.g. reconstructed from a saved log).</summary>
    public void RecordSample(uint tick, ulong lo, ulong hi)
        => Samples.Add((tick, lo, hi));

    /// <summary>
    /// Validate this recorder (the candidate) against a baseline recorder, rejecting first on a build-
    /// fingerprint / profile mismatch and then on the per-tick trajectory.
    /// </summary>
    public ReplayValidationResult ValidateAgainst(ScenarioInputReplay baseline)
    {
        ReplayValidationResult profileCheck = RejectsProfileMismatch(baseline, this);
        if (!profileCheck.Match)
            return profileCheck;

        string algorithmId = AlgorithmId.Length != 0 ? AlgorithmId : baseline.AlgorithmId;
        return ValidateTrajectory(baseline.Samples, Samples, algorithmId);
    }

    /// <summary>
    /// Reject up front when two recorders were captured under incompatible build fingerprints or profiles —
    /// such trajectories are not comparable and a per-tick diff would be misleading. Returns a matching
    /// result (with no divergent tick) when the recorders are compatible.
    /// </summary>
    public static ReplayValidationResult RejectsProfileMismatch(
        ScenarioInputReplay baseline, ScenarioInputReplay candidate)
    {
        if (baseline.Profile != candidate.Profile)
        {
            return ReplayValidationResult.Mismatched(
                -1,
                $"Determinism profile mismatch: baseline={baseline.Profile}, candidate={candidate.Profile}.",
                baseline.AlgorithmId,
                candidate.AlgorithmId);
        }

        if (baseline.BuildFingerprintHash != candidate.BuildFingerprintHash)
        {
            return ReplayValidationResult.Mismatched(
                -1,
                $"Build fingerprint mismatch: baseline=0x{baseline.BuildFingerprintHash:X16}, " +
                $"candidate=0x{candidate.BuildFingerprintHash:X16}.",
                baseline.AlgorithmId,
                candidate.AlgorithmId);
        }

        return ReplayValidationResult.Matched(
            candidate.AlgorithmId.Length != 0 ? candidate.AlgorithmId : baseline.AlgorithmId,
            "Profile and build fingerprint match.");
    }

    /// <summary>
    /// Compare two recorded hash trajectories element-wise and report the first divergent tick. Engine-
    /// light: operates only on the recorded samples, so it is unit-testable without running the universe.
    ///
    /// A length mismatch is reported as a structural divergence (first divergent tick = the tick of the
    /// first extra/missing sample, or -1 if even the empty prefix can't be aligned). Otherwise each
    /// (tick, lo, hi) triple must match; the first that differs is reported.
    /// </summary>
    public static ReplayValidationResult ValidateTrajectory(
        IReadOnlyList<(uint tick, ulong lo, ulong hi)> baseline,
        IReadOnlyList<(uint tick, ulong lo, ulong hi)> candidate,
        string algorithmId)
    {
        int min = baseline.Count < candidate.Count ? baseline.Count : candidate.Count;

        for (int i = 0; i < min; ++i)
        {
            (uint tick, ulong lo, ulong hi) b = baseline[i];
            (uint tick, ulong lo, ulong hi) c = candidate[i];

            if (b.tick != c.tick)
            {
                return ReplayValidationResult.Mismatched(
                    c.tick,
                    $"Tick index {i} misaligned: baseline tick={b.tick}, candidate tick={c.tick}.",
                    algorithmId, algorithmId);
            }

            if (b.lo != c.lo || b.hi != c.hi)
            {
                return ReplayValidationResult.Mismatched(
                    b.tick,
                    $"Hash divergence at tick {b.tick}: " +
                    $"baseline=0x{b.hi:X16}{b.lo:X16}, candidate=0x{c.hi:X16}{c.lo:X16}.",
                    algorithmId, algorithmId);
            }
        }

        if (baseline.Count != candidate.Count)
        {
            bool baselineLonger = baseline.Count > candidate.Count;
            var longer = baselineLonger ? baseline : candidate;
            long divergentTick = min < longer.Count ? longer[min].tick : -1;
            return ReplayValidationResult.Mismatched(
                divergentTick,
                $"Trajectory length mismatch: baseline={baseline.Count}, candidate={candidate.Count} " +
                $"(diverges after {min} matching tick(s)).",
                algorithmId, algorithmId);
        }

        return ReplayValidationResult.Matched(
            algorithmId,
            $"All {baseline.Count} recorded tick(s) match.");
    }
}

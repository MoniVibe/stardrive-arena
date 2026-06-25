namespace Ship_Game.Determinism;

/// <summary>
/// Immutable outcome of comparing two recorded determinism hash trajectories (advisor plan replay
/// validation). Mirrors the PureDOTS <c>ScenarioReplayValidationResult</c> shape, reduced to the engine-
/// light fields StarDrive needs: a match flag, the first tick where the trajectories diverged, a human-
/// readable detail string, and the two algorithm ids that were compared (so an algorithm/profile bump is
/// reported as a mismatch rather than silently producing a wrong "match").
/// </summary>
public readonly struct ReplayValidationResult
{
    /// <summary>True when the trajectories agree on every recorded tick (and metadata matched).</summary>
    public bool Match { get; }

    /// <summary>First tick whose hashes diverged, or -1 when there was no divergence.</summary>
    public long FirstDivergentTick { get; }

    /// <summary>Human-readable explanation of the outcome (cause of mismatch, or a confirmation).</summary>
    public string Detail { get; }

    /// <summary>Algorithm id of the baseline trajectory.</summary>
    public string BaselineAlgorithmId { get; }

    /// <summary>Algorithm id of the candidate trajectory.</summary>
    public string CandidateAlgorithmId { get; }

    public ReplayValidationResult(
        bool match,
        long firstDivergentTick,
        string detail,
        string baselineAlgorithmId,
        string candidateAlgorithmId)
    {
        Match = match;
        FirstDivergentTick = firstDivergentTick;
        Detail = detail;
        BaselineAlgorithmId = baselineAlgorithmId;
        CandidateAlgorithmId = candidateAlgorithmId;
    }

    /// <summary>A successful (matching) result with no divergent tick.</summary>
    public static ReplayValidationResult Matched(string algorithmId, string detail = "Trajectories match.")
        => new(true, -1, detail, algorithmId, algorithmId);

    /// <summary>A failed result; <paramref name="firstDivergentTick"/> may be -1 for a structural mismatch.</summary>
    public static ReplayValidationResult Mismatched(
        long firstDivergentTick,
        string detail,
        string baselineAlgorithmId,
        string candidateAlgorithmId)
        => new(false, firstDivergentTick, detail, baselineAlgorithmId, candidateAlgorithmId);

    public override string ToString()
        => Match
            ? $"Match (algorithm={BaselineAlgorithmId}): {Detail}"
            : $"Mismatch at tick {FirstDivergentTick} (baseline={BaselineAlgorithmId}, candidate={CandidateAlgorithmId}): {Detail}";
}

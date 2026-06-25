namespace Ship_Game.Determinism;

/// <summary>
/// Determinism target tiers (advisor plan §M0 / §M11). The active profile decides how strict the
/// simulation math must be and which sessions/replays are compatible with each other.
/// </summary>
public enum DeterminismProfile
{
    /// <summary>Tier A — same executable, same x64 Windows platform, identical binary. Floats allowed.</summary>
    ReplayWinX64Float,

    /// <summary>Tier B — same build, pinned runtime (tiered-comp off, etc.), cross-machine same-arch.</summary>
    MPSamePlatformPinnedFloat,

    /// <summary>Tier C — cross-platform lockstep. Simulation math must be fixed-point (no sim floats).</summary>
    MPCrossPlatformFixed
}

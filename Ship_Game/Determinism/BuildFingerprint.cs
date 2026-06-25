using System.Reflection;
using System.Runtime.InteropServices;
using SDUtils.Deterministic;

namespace Ship_Game.Determinism;

/// <summary>
/// Identity of the determinism-relevant build surface (advisor plan §M0). A replay or multiplayer
/// session must refuse to proceed unless both sides share the same fingerprint — otherwise a
/// difference in game version, runtime, RNG algorithm, or math profile silently desyncs.
/// </summary>
public static class BuildFingerprint
{
    public static string GameVersion =>
        typeof(BuildFingerprint).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    public static string RuntimeVersion => RuntimeInformation.FrameworkDescription;
    public static string ProcessArchitecture => RuntimeInformation.ProcessArchitecture.ToString();

    /// <summary>RNG algorithm id — bump when the PRNG changes so old replays are rejected, not silently wrong.</summary>
    public const string RngAlgorithm = "splitmix64-v1";

    /// <summary>Fixed-point format used by the Tier-C deterministic math kernel.</summary>
    public const string FixedPointFormat = "Q32.32";

    /// <summary>A stable 64-bit hash of the determinism-relevant build surface, scoped to a profile.</summary>
    public static ulong Compute(DeterminismProfile profile)
    {
        var h = DetHash.New();
        h.AddString(GameVersion);
        h.AddString(RuntimeVersion);
        h.AddString(ProcessArchitecture);
        h.AddString(RngAlgorithm);
        h.AddString(FixedPointFormat);
        h.AddInt((int)profile);
        return h.Value;
    }
}

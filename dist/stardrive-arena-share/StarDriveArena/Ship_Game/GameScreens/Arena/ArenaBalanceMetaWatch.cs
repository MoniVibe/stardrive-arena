using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Ship_Game.Ships;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// Stretch-engine balance guardian for the arena. It samples legal arena warships, runs
/// deterministic side-swapped duels, compares realized win rates to a simple strength-based
/// expectation, and flags designs whose real combat result drifts from that baseline.
/// </summary>
public static class ArenaBalanceMetaWatch
{
    public const string ReportFileName = "arena-balance-meta-watch.json";
    public const float DefaultDriftThreshold = 0.10f;

    public static BalanceMetaWatchReport Run(int sampleSize = 5, ulong seed = 0xA11E_BA1Au,
        float driftThreshold = DefaultDriftThreshold)
    {
        if (sampleSize < 3)
            throw new ArgumentOutOfRangeException(nameof(sampleSize), "Meta-watch needs at least three designs.");
        if (driftThreshold < 0f)
            throw new ArgumentOutOfRangeException(nameof(driftThreshold), "Drift threshold must be non-negative.");

        IShipDesign[] sample = SampleDesigns(sampleSize);
        var rows = new Dictionary<string, BalanceMetaWatchRow>(StringComparer.Ordinal);
        foreach (IShipDesign design in sample)
            rows[design.Name] = new BalanceMetaWatchRow(design.Name, design.Role.ToString().ToUpperInvariant(),
                design.BaseStrength);

        for (int i = 0; i < sample.Length; ++i)
        {
            for (int j = i + 1; j < sample.Length; ++j)
            {
                IShipDesign a = sample[i];
                IShipDesign b = sample[j];
                ulong pairSeed = seed + (ulong)(i * 101 + j * 17);
                RecordDuel(rows, CareerLadder.FairDuel(a.Name, b.Name, pairSeed));
            }
        }

        foreach (IShipDesign design in sample)
        {
            BalanceMetaWatchRow row = rows[design.Name];
            float expected = 0f;
            foreach (IShipDesign opponent in sample)
            {
                if (string.Equals(opponent.Name, design.Name, StringComparison.Ordinal))
                    continue;
                float total = Math.Max(1f, design.BaseStrength + opponent.BaseStrength);
                expected += design.BaseStrength / total;
            }
            expected /= Math.Max(1, sample.Length - 1);
            row.ExpectedWinRate = expected;
            row.Drift = row.WinRate - expected;
            row.Flagged = IsDriftFlagged(row.Drift, driftThreshold);
        }

        BalanceMetaWatchRow[] ordered = rows.Values
            .OrderByDescending(r => Math.Abs(r.Drift))
            .ThenByDescending(r => r.WinRate)
            .ThenBy(r => r.DesignName, StringComparer.Ordinal)
            .ToArray();
        int flagged = ordered.Count(r => r.Flagged);
        float maxAbsDrift = ordered.Length > 0 ? ordered.Max(r => Math.Abs(r.Drift)) : 0f;
        string verdict = flagged > 0
            ? $"meta-watch drift flag: {flagged}/{ordered.Length} sampled designs exceeded {driftThreshold:0.###}"
            : $"meta-watch stable: max drift {maxAbsDrift:0.###} below threshold {driftThreshold:0.###}";

        return new BalanceMetaWatchReport(sample.Length, seed, driftThreshold, flagged, maxAbsDrift,
            ordered, verdict);
    }

    static IShipDesign[] SampleDesigns(int sampleSize)
    {
        IShipDesign[] all = ResourceManager.Ships.Designs
            .Where(d => ArenaFightScreen.IsHeavyGunWarship(d) && !ArenaFightScreen.IsDevTestDesign(d))
            .OrderBy(d => d.BaseStrength)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();
        if (all.Length < sampleSize)
            throw new InvalidOperationException("Not enough legal arena warships for the balance meta-watch sample.");

        var sample = new List<IShipDesign>(sampleSize);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < sampleSize; ++i)
        {
            int index = sampleSize == 1 ? 0 : i * (all.Length - 1) / (sampleSize - 1);
            for (int offset = 0; offset < all.Length; ++offset)
            {
                IShipDesign design = all[(index + offset) % all.Length];
                if (!seen.Add(design.Name))
                    continue;
                sample.Add(design);
                break;
            }
        }
        return sample.ToArray();
    }

    public static bool IsDriftFlagged(float drift, float threshold)
    {
        if (threshold < 0f)
            throw new ArgumentOutOfRangeException(nameof(threshold), "Drift threshold must be non-negative.");
        return Math.Abs(drift) >= threshold;
    }

    static void RecordDuel(Dictionary<string, BalanceMetaWatchRow> rows, FairDuelResult duel)
    {
        BalanceMetaWatchRow a = rows[duel.DesignA];
        BalanceMetaWatchRow b = rows[duel.DesignB];
        a.Wins += duel.WinsA;
        a.Losses += duel.WinsB;
        b.Wins += duel.WinsB;
        b.Losses += duel.WinsA;
    }

    public static string WriteReport(BalanceMetaWatchReport report, string outputDir)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (outputDir == null || outputDir.Length == 0)
            throw new ArgumentException("Output directory is required.", nameof(outputDir));

        Directory.CreateDirectory(outputDir);
        string path = Path.Combine(outputDir, ReportFileName);
        File.WriteAllText(path, ToJson(report));
        return path;
    }

    public static string ToJson(BalanceMetaWatchReport report)
    {
        string rows = string.Join(",\n    ", report.Rows.Select(r =>
            $"{{\"design\":{J(r.DesignName)},\"role\":{J(r.RoleClass)},\"baseStrength\":{F(r.BaseStrength)}," +
            $"\"wins\":{r.Wins},\"losses\":{r.Losses},\"winRate\":{F(r.WinRate)}," +
            $"\"expectedWinRate\":{F(r.ExpectedWinRate)},\"drift\":{F(r.Drift)}," +
            $"\"flagged\":{(r.Flagged ? "true" : "false")}}}"));

        return "{\n" +
               "  \"experiment\": \"ARENA BALANCE META-WATCH: deterministic side-swapped duel sample comparing realized win rates against strength-based expectations; flags drift for nightly balance triage\",\n" +
               $"  \"seed\": {report.Seed},\n" +
               $"  \"sampleSize\": {report.SampleSize},\n" +
               $"  \"driftThreshold\": {F(report.DriftThreshold)},\n" +
               $"  \"flaggedCount\": {report.FlaggedCount},\n" +
               $"  \"maxAbsDrift\": {F(report.MaxAbsDrift)},\n" +
               $"  \"verdict\": {J(report.Verdict)},\n" +
               $"  \"rows\": [\n    {rows}\n  ]\n" +
               "}\n";
    }

    static string J(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

public sealed class BalanceMetaWatchReport
{
    public readonly int SampleSize;
    public readonly ulong Seed;
    public readonly float DriftThreshold;
    public readonly int FlaggedCount;
    public readonly float MaxAbsDrift;
    public readonly BalanceMetaWatchRow[] Rows;
    public readonly string Verdict;

    public BalanceMetaWatchReport(int sampleSize, ulong seed, float driftThreshold, int flaggedCount,
        float maxAbsDrift, BalanceMetaWatchRow[] rows, string verdict)
    {
        SampleSize = sampleSize;
        Seed = seed;
        DriftThreshold = driftThreshold;
        FlaggedCount = flaggedCount;
        MaxAbsDrift = maxAbsDrift;
        Rows = rows;
        Verdict = verdict;
    }
}

public sealed class BalanceMetaWatchRow
{
    public readonly string DesignName;
    public readonly string RoleClass;
    public readonly float BaseStrength;
    public int Wins;
    public int Losses;
    public float ExpectedWinRate;
    public float Drift;
    public bool Flagged;

    public BalanceMetaWatchRow(string designName, string roleClass, float baseStrength)
    {
        DesignName = designName;
        RoleClass = roleClass;
        BaseStrength = baseStrength;
    }

    public float WinRate
    {
        get
        {
            int games = Wins + Losses;
            return games > 0 ? Wins / (float)games : 0f;
        }
    }
}

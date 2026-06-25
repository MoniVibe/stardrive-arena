using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Ship_Game.Ships;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// Headless progression readout for the arena career class ramp. It samples the shared
/// career-level tier curve and verifies that fieldable/player/enemy classes rise together.
/// </summary>
public static class ArenaProgressionSimulator
{
    public const string ReportFileName = "arena-progression.json";
    const int DuelTicks = 900;
    const float DuelSpawnOffset = 1400f;
    const ulong Seed = 0xA11E_7100ul;

    public static ArenaProgressionReport Run(int[] careerLevels = null)
    {
        int[] levels = careerLevels ?? new[] { 0, 1, 2, 3, 5, 6, 7, 9 };
        ArenaProgressionRow[] rows = levels
            .Select(BuildRow)
            .ToArray();

        string verdict = $"arena progression levels={rows.Length}; " +
                         $"tier curve {string.Join(">", rows.Select(r => r.FieldableTier).Distinct())}; " +
                         $"winRate {F(rows.Min(r => r.SampledWinRate))}-{F(rows.Max(r => r.SampledWinRate))}";
        return new ArenaProgressionReport(rows, verdict);
    }

    static ArenaProgressionRow BuildRow(int careerLevel)
    {
        int fieldableTier = ArenaFightScreen.MaxAllowedCombatTierForCareerLevel(careerLevel);
        IShipDesign player = ArenaFightScreen.AutoPickPlayerWarship(null, careerLevel);
        IShipDesign enemy = ArenaFightScreen.PickEnemyEscort(null, round: Math.Min(ArenaFightScreen.TotalRounds, fieldableTier), careerLevel);
        IShipDesign finalEnemy = ArenaFightScreen.PickEnemyEscort(null, ArenaFightScreen.TotalRounds, careerLevel);
        IShipDesign boss = ArenaFightScreen.PickEnemyBoss(null, careerLevel);
        IShipDesign threat = boss ?? PickStrongestAllowed(careerLevel) ?? finalEnemy;
        IShipDesign investedPlayer = PickStrongestAllowed(careerLevel) ?? player;

        float sampledWinRate = DuelWinRate(player, enemy, Seed + (ulong)(careerLevel * 101 + 1));
        float investmentWinRate = DuelWinRate(investedPlayer, threat, Seed + (ulong)(careerLevel * 101 + 2));
        float noInvestmentWinRate = DuelWinRate(player, threat, Seed + (ulong)(careerLevel * 101 + 3));

        return new ArenaProgressionRow(
            careerLevel,
            fieldableTier,
            Math.Max(
                Math.Max(ArenaFightScreen.CombatTierForDesign(enemy),
                    ArenaFightScreen.CombatTierForDesign(boss)),
                ArenaFightScreen.CombatTierForDesign(threat)),
            player?.Name ?? "",
            player?.Role.ToString() ?? "",
            ArenaFightScreen.CombatTierForDesign(player),
            enemy?.Name ?? "",
            enemy?.Role.ToString() ?? "",
            ArenaFightScreen.CombatTierForDesign(enemy),
            boss?.Name ?? "",
            boss?.Role.ToString() ?? "",
            ArenaFightScreen.CombatTierForDesign(boss),
            sampledWinRate,
            investmentWinRate,
            noInvestmentWinRate);
    }

    static IShipDesign PickStrongestAllowed(int careerLevel)
    {
        return ResourceManager.Ships.Designs
            .Where(d => ArenaFightScreen.IsLegalCombatCraft(d)
                     && ArenaFightScreen.IsDesignAllowedForCareerLevel(d, careerLevel))
            .OrderByDescending(d => d.BaseStrength)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    static float DuelWinRate(IShipDesign a, IShipDesign b, ulong seed)
    {
        if (a == null || b == null)
            return 0f;
        FairDuelResult duel = CareerLadder.FairDuel(a, b, seed, DuelTicks, DuelSpawnOffset);
        return duel.Games > 0 ? duel.WinsA / (float)duel.Games : 0f;
    }

    public static string WriteReport(ArenaProgressionReport report, string outputDir)
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

    public static string ToJson(ArenaProgressionReport report)
    {
        string rows = string.Join(",\n    ", report.Rows.Select(r =>
            $"{{\"careerLevel\":{r.CareerLevel},\"fieldableTier\":{r.FieldableTier}," +
            $"\"enemyTier\":{r.EnemyTier},\"playerDesign\":{J(r.PlayerDesign)}," +
            $"\"playerRole\":{J(r.PlayerRole)},\"playerTier\":{r.PlayerTier}," +
            $"\"enemyDesign\":{J(r.EnemyDesign)},\"enemyRole\":{J(r.EnemyRole)}," +
            $"\"enemyDesignTier\":{r.EnemyDesignTier},\"bossDesign\":{J(r.BossDesign)}," +
            $"\"bossRole\":{J(r.BossRole)},\"bossTier\":{r.BossTier}," +
            $"\"sampledWinRate\":{F(r.SampledWinRate)},\"investmentWinRate\":{F(r.InvestmentWinRate)}," +
            $"\"noInvestmentWinRate\":{F(r.NoInvestmentWinRate)}}}"));

        return "{\n" +
               "  \"experiment\": \"ARENA CAREER CLASS PROGRESSION: career-level tier ramp for player, dealership, and enemy classes with sampled FairDuel win rates\",\n" +
               $"  \"verdict\": {J(report.Verdict)},\n" +
               $"  \"rows\": [\n    {rows}\n  ]\n" +
               "}\n";
    }

    static string J(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

public sealed class ArenaProgressionReport
{
    public readonly ArenaProgressionRow[] Rows;
    public readonly string Verdict;

    public ArenaProgressionReport(ArenaProgressionRow[] rows, string verdict)
    {
        Rows = rows ?? Array.Empty<ArenaProgressionRow>();
        Verdict = verdict ?? "";
    }
}

public readonly struct ArenaProgressionRow
{
    public readonly int CareerLevel;
    public readonly int FieldableTier;
    public readonly int EnemyTier;
    public readonly string PlayerDesign;
    public readonly string PlayerRole;
    public readonly int PlayerTier;
    public readonly string EnemyDesign;
    public readonly string EnemyRole;
    public readonly int EnemyDesignTier;
    public readonly string BossDesign;
    public readonly string BossRole;
    public readonly int BossTier;
    public readonly float SampledWinRate;
    public readonly float InvestmentWinRate;
    public readonly float NoInvestmentWinRate;

    public ArenaProgressionRow(int careerLevel, int fieldableTier, int enemyTier,
        string playerDesign, string playerRole, int playerTier,
        string enemyDesign, string enemyRole, int enemyDesignTier,
        string bossDesign, string bossRole, int bossTier,
        float sampledWinRate, float investmentWinRate, float noInvestmentWinRate)
    {
        CareerLevel = careerLevel;
        FieldableTier = fieldableTier;
        EnemyTier = enemyTier;
        PlayerDesign = playerDesign ?? "";
        PlayerRole = playerRole ?? "";
        PlayerTier = playerTier;
        EnemyDesign = enemyDesign ?? "";
        EnemyRole = enemyRole ?? "";
        EnemyDesignTier = enemyDesignTier;
        BossDesign = bossDesign ?? "";
        BossRole = bossRole ?? "";
        BossTier = bossTier;
        SampledWinRate = Math.Max(0f, Math.Min(1f, sampledWinRate));
        InvestmentWinRate = Math.Max(0f, Math.Min(1f, investmentWinRate));
        NoInvestmentWinRate = Math.Max(0f, Math.Min(1f, noInvestmentWinRate));
    }
}

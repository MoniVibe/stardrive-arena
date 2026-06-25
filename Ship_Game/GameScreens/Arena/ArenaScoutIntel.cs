using System;
using System.Globalization;
using System.Linq;
using SDUtils;

namespace Ship_Game.GameScreens.Arena;

public sealed class ArenaScoutIntelReport
{
    public readonly int ScoutTier;
    public readonly string GenericSummary;
    public readonly string DifficultyBand;
    public readonly string EnemyStrengthRange;
    public readonly int LikelyShipCount;
    public readonly string RivalName;
    public readonly string RivalEpithet;
    public readonly string RivalPersona;
    public readonly string RivalRecentForm;
    public readonly string BossWarning;
    public readonly float SurvivalOddsEstimate;

    public bool HasDifficultyBand => DifficultyBand.NotEmpty();
    public bool HasEnemyStrengthRange => EnemyStrengthRange.NotEmpty();
    public bool HasLikelyShipCount => LikelyShipCount > 0;
    public bool HasRivalIdentity => RivalName.NotEmpty();
    public bool HasBossWarning => BossWarning.NotEmpty();
    public bool HasSurvivalOdds => SurvivalOddsEstimate > 0f;

    public string Signature
        => $"{ScoutTier}|{GenericSummary}|{DifficultyBand}|{EnemyStrengthRange}|{LikelyShipCount}|"
         + $"{RivalName}|{RivalEpithet}|{RivalPersona}|{RivalRecentForm}|{BossWarning}|"
         + SurvivalOddsEstimate.ToString("0.000", CultureInfo.InvariantCulture);

    public ArenaScoutIntelReport(int scoutTier, string genericSummary, string difficultyBand,
        string enemyStrengthRange, int likelyShipCount, string rivalName, string rivalEpithet,
        string rivalPersona, string rivalRecentForm, string bossWarning, float survivalOddsEstimate)
    {
        ScoutTier = Math.Clamp(scoutTier, 0, 3);
        GenericSummary = genericSummary ?? "";
        DifficultyBand = difficultyBand ?? "";
        EnemyStrengthRange = enemyStrengthRange ?? "";
        LikelyShipCount = Math.Max(0, likelyShipCount);
        RivalName = rivalName ?? "";
        RivalEpithet = rivalEpithet ?? "";
        RivalPersona = rivalPersona ?? "";
        RivalRecentForm = rivalRecentForm ?? "";
        BossWarning = bossWarning ?? "";
        SurvivalOddsEstimate = Math.Clamp(survivalOddsEstimate, 0f, 1f);
    }
}

public static class ArenaScoutIntel
{
    public static int ScoutTier(ArenaCareer career)
        => Math.Clamp(ArenaPerks.Count(career?.Perks, ArenaPerks.ScoutId), 0, 3);

    public static ArenaScoutIntelReport Generate(ArenaCareer career, FightOption option, ulong seed)
    {
        int tier = ScoutTier(career);
        string generic = "A sealed arena contract is available.";
        if (tier <= 0 || option == null)
            return new ArenaScoutIntelReport(tier, generic, "", "", 0, "", "", "", "", "", 0f);

        string difficulty = option.DifficultyTier.ToString();
        int shipCount = option.EnemyCount;
        string bossWarning = option.HasBoss
            ? $"Boss threat: {BossName(option)}"
            : "No boss signature.";

        if (tier == 1)
            return new ArenaScoutIntelReport(tier, generic, difficulty, "", shipCount,
                "", "", "", "", bossWarning, 0f);

        string strengthRange = StrengthRange(option.StrengthRatio);
        ContenderRecord rival = PickRival(career, option, seed);
        string rivalName = rival?.Name ?? "";
        string rivalEpithet = rival?.Epithet ?? "";
        string rivalPersona = rival?.ArenaPersona ?? "";
        string recentForm = rival != null
            ? $"rating {rival.Rating}, form {rival.Wins}-{rival.Losses}"
            : "";

        if (tier == 2)
            return new ArenaScoutIntelReport(tier, generic, difficulty, strengthRange, shipCount,
                rivalName, rivalEpithet, rivalPersona, recentForm, bossWarning, 0f);

        return new ArenaScoutIntelReport(tier, generic, difficulty, strengthRange, shipCount,
            rivalName, rivalEpithet, rivalPersona, recentForm, bossWarning, option.EstimatedWinRate);
    }

    static string BossName(FightOption option)
    {
        if (option == null)
            return "";
        if (option.BossEncounter.Active && option.BossEncounter.Name.NotEmpty())
            return option.BossEncounter.Name;
        if (option.BossDesignName.NotEmpty())
            return option.BossDesignName;
        return "elite contact";
    }

    static string StrengthRange(float ratio)
    {
        float lo = Math.Max(0.01f, ratio * 0.92f);
        float hi = Math.Max(lo, ratio * 1.08f);
        return $"{lo.ToString("0.00", CultureInfo.InvariantCulture)}x-" +
               $"{hi.ToString("0.00", CultureInfo.InvariantCulture)}x mirror";
    }

    static ContenderRecord PickRival(ArenaCareer career, FightOption option, ulong seed)
    {
        if (career?.Contenders == null || career.Contenders.Length == 0 || option == null)
            return null;
        career.NormalizeForPersistence();
        return career.Contenders
            .Where(c => c != null && c.DesignName.NotEmpty())
            .OrderByDescending(c => string.Equals(c.DesignName, option.EscortDesignName, StringComparison.Ordinal))
            .ThenByDescending(c => c.RivalWins + c.RivalLosses)
            .ThenByDescending(c => c.Rating)
            .ThenBy(c => ArenaRivalDossiers.StableHash($"{seed:X16}|{c.ContenderId}|{option.OptionId}"))
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}

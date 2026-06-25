using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Ship_Game.Ships;

namespace Ship_Game.GameScreens.Arena;

public sealed class ArenaMonteCarloTelemetryReport
{
    public readonly ulong Seed;
    public readonly int SamplesPerCell;
    public readonly ArenaMonteCarloTelemetryRow[] Rows;

    public ArenaMonteCarloTelemetryReport(ulong seed, int samplesPerCell,
        ArenaMonteCarloTelemetryRow[] rows)
    {
        Seed = seed;
        SamplesPerCell = Math.Max(1, samplesPerCell);
        Rows = rows ?? Array.Empty<ArenaMonteCarloTelemetryRow>();
    }
}

public sealed class ArenaMonteCarloTelemetryRow
{
    public readonly string Archetype;
    public readonly int Fame;
    public readonly string DifficultyBand;
    public readonly int FleetSize;
    public readonly int Samples;
    public readonly float WinRate;
    public readonly float ShipLossRate;
    public readonly float CashDelta;
    public readonly float SalvageDelta;
    public readonly float RepairFrequency;
    public readonly float RefitFrequency;
    public readonly float RivalChurn;
    public readonly float BossSuccessRate;
    public readonly int SoftLockCount;

    public ArenaMonteCarloTelemetryRow(string archetype, int fame, string difficultyBand,
        int fleetSize, int samples, float winRate, float shipLossRate, float cashDelta,
        float salvageDelta, float repairFrequency, float refitFrequency, float rivalChurn,
        float bossSuccessRate, int softLockCount)
    {
        Archetype = archetype ?? "";
        Fame = Math.Max(0, fame);
        DifficultyBand = difficultyBand ?? "";
        FleetSize = Math.Max(1, fleetSize);
        Samples = Math.Max(1, samples);
        WinRate = Clamp01(winRate);
        ShipLossRate = Clamp01(shipLossRate);
        CashDelta = cashDelta;
        SalvageDelta = Math.Max(0f, salvageDelta);
        RepairFrequency = Clamp01(repairFrequency);
        RefitFrequency = Clamp01(refitFrequency);
        RivalChurn = Math.Max(0f, rivalChurn);
        BossSuccessRate = Clamp01(bossSuccessRate);
        SoftLockCount = Math.Max(0, softLockCount);
    }

    static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
}

public static class ArenaMonteCarloTelemetry
{
    public const string JsonFileName = "arena-monte-carlo-telemetry.json";
    public const string CsvFileName = "arena-monte-carlo-telemetry.csv";

    static readonly ArenaStartArchetype[] Archetypes =
    {
        ArenaStartArchetype.Ace,
        ArenaStartArchetype.Wingmates,
        ArenaStartArchetype.Swarm,
    };

    static readonly int[] FameTiers = { 0, 50, ArenaFightOptions.FullSlateFame };
    static readonly int[] FleetSizes = { 1, 3, 5 };
    static readonly FightDifficultyTier[] DifficultyBands =
    {
        FightDifficultyTier.Trivial,
        FightDifficultyTier.Normal,
        FightDifficultyTier.Hard,
        FightDifficultyTier.Wildcard,
    };

    public static ArenaMonteCarloTelemetryReport Run(ulong seed = 0xA12E_7E1E_0E77ul,
        int samplesPerCell = 4)
    {
        samplesPerCell = Math.Clamp(samplesPerCell, 1, 20);
        var rows = new List<ArenaMonteCarloTelemetryRow>();
        foreach (ArenaStartArchetype archetype in Archetypes)
        foreach (int fame in FameTiers)
        foreach (int fleetSize in FleetSizes)
        {
            ArenaCareer career = BuildTelemetryCareer(archetype, fame, fleetSize);
            FightOption[] options = ArenaFightOptions.GenerateFightOptions(career,
                seed ^ (ulong)((int)archetype * 0x10001) ^ (ulong)(fame * 0x101) ^ (ulong)fleetSize);
            foreach (FightDifficultyTier band in DifficultyBands)
                rows.Add(SampleBand(archetype, fame, fleetSize, band, options, seed, samplesPerCell));
        }

        return new ArenaMonteCarloTelemetryReport(seed, samplesPerCell, rows.ToArray());
    }

    static ArenaMonteCarloTelemetryRow SampleBand(ArenaStartArchetype archetype, int fame, int fleetSize,
        FightDifficultyTier band, FightOption[] options, ulong seed, int samples)
    {
        FightOption[] candidates = (options ?? Array.Empty<FightOption>())
            .Where(o => o != null && o.DifficultyTier == band)
            .ToArray();
        if (candidates.Length == 0)
        {
            candidates = (options ?? Array.Empty<FightOption>())
                .OrderBy(o => Math.Abs(ArenaFightOptions.DifficultyRank(o.DifficultyTier)
                                     - ArenaFightOptions.DifficultyRank(band)))
                .ThenBy(o => o.OptionId, StringComparer.Ordinal)
                .Take(1)
                .ToArray();
        }

        int wins = 0;
        float shipLoss = 0f;
        float cashDelta = 0f;
        float salvageDelta = 0f;
        int repairs = 0;
        int refits = 0;
        int rivalChurn = 0;
        int bossAttempts = 0;
        int bossWins = 0;
        int softLocks = 0;

        for (int i = 0; i < samples; ++i)
        {
            FightOption option = candidates[Math.Min(candidates.Length - 1,
                (int)(Mix(seed ^ (ulong)i ^ (ulong)candidates.Length) % (ulong)candidates.Length))];
            ulong sampleSeed = seed
                             ^ ((ulong)(int)archetype << 48)
                             ^ ((ulong)fame << 24)
                             ^ ((ulong)fleetSize << 16)
                             ^ ((ulong)ArenaFightOptions.DifficultyRank(band) << 8)
                             ^ (ulong)i;
            bool won = Unit(sampleSeed) <= option.EstimatedWinRate;
            float lossEstimate = ShipLossEstimate(option, won, sampleSeed);
            shipLoss += lossEstimate;
            if (won)
            {
                ++wins;
                cashDelta += option.PreviewCashTotal;
                if (option.FightType == FightOptionType.ContenderChallenge
                    || option.FightType == FightOptionType.LeagueBout)
                    ++rivalChurn;
            }
            else
            {
                cashDelta -= Math.Max(0, (int)Math.Round(option.ExpectedRewardValue * 0.20f));
                if (option.EstimatedWinRate < 0.08f)
                    ++softLocks;
            }

            if (lossEstimate > 0.05f)
                ++repairs;
            if (won && lossEstimate > 0.15f)
                ++refits;
            if (option.HasBoss)
            {
                ++bossAttempts;
                if (won)
                    ++bossWins;
            }
        }

        return new ArenaMonteCarloTelemetryRow(archetype.ToString(), fame, band.ToString(),
            fleetSize, samples, wins / (float)samples, shipLoss / samples, cashDelta / samples,
            salvageDelta / samples, repairs / (float)samples, refits / (float)samples,
            rivalChurn / (float)samples, bossAttempts > 0 ? bossWins / (float)bossAttempts : 0f,
            softLocks);
    }

    static ArenaCareer BuildTelemetryCareer(ArenaStartArchetype archetype, int fame, int fleetSize)
    {
        var career = CareerManager.CreateNewCareer(archetype);
        career.Fame = fame;
        career.CareerLevel = fame >= ArenaFightOptions.FullSlateFame ? 7 : fame >= 50 ? 4 : 0;
        career.Cash = 5000;
        CareerLadder.EnsureContenders(career);

        IShipDesign[] legal = ResourceManager.Ships.Designs
            .Where(d => ArenaFightScreen.IsLegalCombatCraft(d)
                     && ArenaFightScreen.IsDesignAllowedForCareerLevel(d, career.CareerLevel))
            .OrderBy(d => d.BaseStrength)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        int index = 0;
        while ((career.OwnedVessels?.Length ?? 0) < fleetSize && legal.Length > 0)
        {
            IShipDesign design = legal[Math.Min(legal.Length - 1,
                (index * Math.Max(1, legal.Length - 1)) / Math.Max(1, fleetSize - 1))];
            career.AddOwnedVessel(design.Name, $"{archetype} Telemetry {index + 1}",
                makeActive: career.OwnedVessels == null || career.OwnedVessels.Length == 0);
            ++index;
        }

        career.FleetVesselIds = (career.OwnedVessels ?? Array.Empty<OwnedVessel>())
            .Skip(1)
            .Take(Math.Max(0, fleetSize - 1))
            .Select(v => v.VesselId)
            .ToArray();
        career.NormalizeForPersistence();
        return career;
    }

    static float ShipLossEstimate(FightOption option, bool won, ulong seed)
    {
        float baseRisk = Math.Clamp((option.StrengthRatio - 0.65f) / 1.8f, 0.02f, 0.95f);
        float roll = Unit(seed ^ 0x51055ul) * 0.20f;
        float loss = won ? baseRisk * 0.45f + roll : baseRisk * 0.75f + roll;
        return Math.Clamp(loss, 0f, 1f);
    }

    public static string WriteReports(ArenaMonteCarloTelemetryReport report, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        string json = Path.Combine(outputDir, JsonFileName);
        string csv = Path.Combine(outputDir, CsvFileName);
        File.WriteAllText(json, ToJson(report), Encoding.UTF8);
        File.WriteAllText(csv, ToCsv(report), Encoding.UTF8);
        return json;
    }

    public static string ToJson(ArenaMonteCarloTelemetryReport report)
    {
        report ??= new ArenaMonteCarloTelemetryReport(0, 1, Array.Empty<ArenaMonteCarloTelemetryRow>());
        var lines = new List<string>
        {
            "{",
            $"  \"seed\": \"0x{report.Seed:X16}\",",
            $"  \"samplesPerCell\": {report.SamplesPerCell.ToString(CultureInfo.InvariantCulture)},",
            "  \"rows\": ["
        };
        for (int i = 0; i < report.Rows.Length; ++i)
        {
            ArenaMonteCarloTelemetryRow r = report.Rows[i];
            string comma = i + 1 == report.Rows.Length ? "" : ",";
            lines.Add("    {" +
                      $"\"archetype\":{J(r.Archetype)},\"fame\":{r.Fame},\"difficultyBand\":{J(r.DifficultyBand)}," +
                      $"\"fleetSize\":{r.FleetSize},\"samples\":{r.Samples},\"winRate\":{F(r.WinRate)}," +
                      $"\"shipLossRate\":{F(r.ShipLossRate)},\"cashDelta\":{F(r.CashDelta)}," +
                      $"\"salvageDelta\":{F(r.SalvageDelta)},\"repairFreq\":{F(r.RepairFrequency)}," +
                      $"\"refitFreq\":{F(r.RefitFrequency)},\"rivalChurn\":{F(r.RivalChurn)}," +
                      $"\"bossSuccess\":{F(r.BossSuccessRate)},\"softLockCount\":{r.SoftLockCount}" +
                      "}" + comma);
        }
        lines.Add("  ]");
        lines.Add("}");
        return string.Join("\n", lines) + "\n";
    }

    public static string ToCsv(ArenaMonteCarloTelemetryReport report)
    {
        var b = new StringBuilder();
        b.AppendLine("archetype,fame,difficultyBand,fleetSize,samples,winRate,shipLossRate,cashDelta,salvageDelta,repairFreq,refitFreq,rivalChurn,bossSuccess,softLockCount");
        foreach (ArenaMonteCarloTelemetryRow r in report?.Rows ?? Array.Empty<ArenaMonteCarloTelemetryRow>())
        {
            b.Append(r.Archetype).Append(',')
                .Append(r.Fame).Append(',')
                .Append(r.DifficultyBand).Append(',')
                .Append(r.FleetSize).Append(',')
                .Append(r.Samples).Append(',')
                .Append(F(r.WinRate)).Append(',')
                .Append(F(r.ShipLossRate)).Append(',')
                .Append(F(r.CashDelta)).Append(',')
                .Append(F(r.SalvageDelta)).Append(',')
                .Append(F(r.RepairFrequency)).Append(',')
                .Append(F(r.RefitFrequency)).Append(',')
                .Append(F(r.RivalChurn)).Append(',')
                .Append(F(r.BossSuccessRate)).Append(',')
                .Append(r.SoftLockCount).AppendLine();
        }
        return b.ToString();
    }

    static float Unit(ulong seed)
        => (Mix(seed) & 0xffffff) / (float)0x1000000;

    static ulong Mix(ulong x)
    {
        x ^= x >> 30;
        x *= 0xbf58476d1ce4e5b9ul;
        x ^= x >> 27;
        x *= 0x94d049bb133111ebul;
        return x ^ (x >> 31);
    }

    static string F(float value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    static string J(string value)
        => "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}

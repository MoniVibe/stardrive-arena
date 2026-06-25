using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SDUtils;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// Headless arena career/ladder season simulator. It runs deterministic contender seasons
/// across caller-supplied seeds and emits compact JSON evidence for balance triage.
/// </summary>
public static class ArenaCareerSeasonSimulator
{
    public const string ReportFileName = "arena-career-season.json";

    public static SeasonSimReport Simulate(int[] seeds, int rosterSize = 8, int roundsPerSeason = 2)
    {
        if (seeds == null || seeds.Length == 0)
            throw new ArgumentException("At least one season seed is required.", nameof(seeds));
        if (rosterSize < 2)
            throw new ArgumentOutOfRangeException(nameof(rosterSize), "Roster size must be at least two.");
        if (roundsPerSeason < 1)
            throw new ArgumentOutOfRangeException(nameof(roundsPerSeason), "At least one ladder round is required.");

        var seasonRows = new List<SeasonSimRow>(seeds.Length);
        var designRows = new Dictionary<string, SeasonDesignRow>(StringComparer.Ordinal);

        foreach (int seed in seeds)
        {
            var career = new ArenaCareer { Contenders = CareerLadder.SeedContenders(rosterSize) };
            foreach (ContenderRecord c in career.Contenders)
                EnsureDesignRow(designRows, c);

            for (int round = 0; round < roundsPerSeason; ++round)
                RunDecisiveSeasonRound(career, ((ulong)(uint)seed << 16) + (uint)round);

            foreach (ContenderRecord c in career.Contenders)
            {
                SeasonDesignRow row = EnsureDesignRow(designRows, c);
                row.Wins += c.Wins;
                row.Losses += c.Losses;
                row.FinalRatingTotal += c.Rating;
                row.SeasonsSeen += 1;
            }

            ContenderRecord champion = career.Contenders
                .OrderByDescending(c => c.Rating)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .First();
            SeasonDesignRow champRow = EnsureDesignRow(designRows, champion);
            champRow.Championships += 1;

            int high = int.MinValue;
            int low = int.MaxValue;
            int totalWins = 0;
            int totalLosses = 0;
            foreach (ContenderRecord c in career.Contenders)
            {
                high = Math.Max(high, c.Rating);
                low = Math.Min(low, c.Rating);
                totalWins += c.Wins;
                totalLosses += c.Losses;
            }
            seasonRows.Add(new SeasonSimRow(seed, champion.Name, champion.DesignName, champion.RoleClass,
                champion.Rating, high - low, totalWins, totalLosses));
        }

        SeasonDesignRow[] designs = designRows.Values
            .OrderByDescending(d => d.Championships)
            .ThenByDescending(d => d.WinRate)
            .ThenByDescending(d => d.AverageFinalRating)
            .ThenBy(d => d.DesignName, StringComparer.Ordinal)
            .ToArray();

        SeasonDesignRow leader = designs.FirstOrDefault();
        float leaderShare = leader != null ? leader.Championships / (float)seeds.Length : 0f;
        bool dominant = leaderShare > 0.5f;
        string verdict = leader == null
            ? "no contenders simulated"
            : dominant
                ? $"dominance flag: '{leader.DesignName}' won {leader.Championships}/{seeds.Length} seasons"
                : $"no single contender dominated; leader '{leader.DesignName}' won {leader.Championships}/{seeds.Length} seasons";

        return new SeasonSimReport(seeds, rosterSize, roundsPerSeason, seasonRows.ToArray(), designs, verdict);
    }

    static SeasonDesignRow EnsureDesignRow(Dictionary<string, SeasonDesignRow> rows, ContenderRecord contender)
    {
        if (!rows.TryGetValue(contender.DesignName, out SeasonDesignRow row))
        {
            row = new SeasonDesignRow(contender.DesignName, contender.RoleClass);
            rows[contender.DesignName] = row;
        }
        return row;
    }

    static void RunDecisiveSeasonRound(ArenaCareer career, ulong seed)
    {
        ContenderRecord[] standings = career.Contenders
            .Where(c => c != null && c.DesignName.NotEmpty())
            .OrderByDescending(c => c.Rating)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToArray();

        for (int i = 0; i < standings.Length / 2; ++i)
        {
            ContenderRecord a = standings[i];
            ContenderRecord b = standings[standings.Length - 1 - i];
            FairDuelResult duel = CareerLadder.FairDuel(a.DesignName, b.DesignName, seed + (ulong)i);
            ApplyResult(a, b, string.Equals(duel.WinnerDesignName, a.DesignName, StringComparison.Ordinal));
        }
    }

    static void ApplyResult(ContenderRecord a, ContenderRecord b, bool aWon)
    {
        ContenderRecord winner = aWon ? a : b;
        ContenderRecord loser  = aWon ? b : a;
        winner.Wins += 1;
        loser.Losses += 1;

        int delta = RatingDelta(winner.Rating, loser.Rating);
        winner.Rating += delta;
        loser.Rating = Math.Max(1, loser.Rating - delta);
    }

    static int RatingDelta(int winnerRating, int loserRating)
    {
        double expected = 1.0 / (1.0 + Math.Pow(10.0, (loserRating - winnerRating) / 400.0));
        return Math.Max(1, (int)Math.Round(24 * (1.0 - expected)));
    }

    public static string WriteReport(SeasonSimReport report, string outputDir)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (outputDir.IsEmpty())
            throw new ArgumentException("Output directory is required.", nameof(outputDir));

        Directory.CreateDirectory(outputDir);
        string path = Path.Combine(outputDir, ReportFileName);
        File.WriteAllText(path, ToJson(report));
        return path;
    }

    public static string ToJson(SeasonSimReport report)
    {
        string seeds = string.Join(",", report.Seeds);
        string seasons = string.Join(",\n    ", report.Seasons.Select(s =>
            $"{{\"seed\":{s.Seed},\"champion\":{J(s.ChampionName)},\"design\":{J(s.ChampionDesign)},\"role\":{J(s.ChampionRole)}," +
            $"\"rating\":{s.ChampionRating},\"ratingSpread\":{s.RatingSpread},\"wins\":{s.TotalWins},\"losses\":{s.TotalLosses}}}"));
        string designs = string.Join(",\n    ", report.Designs.Select(d =>
            $"{{\"design\":{J(d.DesignName)},\"role\":{J(d.RoleClass)},\"championships\":{d.Championships}," +
            $"\"wins\":{d.Wins},\"losses\":{d.Losses},\"winRate\":{F(d.WinRate)},\"avgFinalRating\":{F(d.AverageFinalRating)}}}"));

        return "{\n" +
               "  \"experiment\": \"ARENA CAREER LADDER SEASON: deterministic contender seasons across seeds; reports champion concentration, W/L spread, and rating spread for balance triage\",\n" +
               $"  \"seeds\": [{seeds}],\n" +
               $"  \"rosterSize\": {report.RosterSize},\n" +
               $"  \"roundsPerSeason\": {report.RoundsPerSeason},\n" +
               $"  \"verdict\": {J(report.Verdict)},\n" +
               $"  \"seasons\": [\n    {seasons}\n  ],\n" +
               $"  \"designs\": [\n    {designs}\n  ]\n" +
               "}\n";
    }

    static string J(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

public sealed class SeasonSimReport
{
    public readonly int[] Seeds;
    public readonly int RosterSize;
    public readonly int RoundsPerSeason;
    public readonly SeasonSimRow[] Seasons;
    public readonly SeasonDesignRow[] Designs;
    public readonly string Verdict;

    public SeasonSimReport(int[] seeds, int rosterSize, int roundsPerSeason,
        SeasonSimRow[] seasons, SeasonDesignRow[] designs, string verdict)
    {
        Seeds = seeds;
        RosterSize = rosterSize;
        RoundsPerSeason = roundsPerSeason;
        Seasons = seasons;
        Designs = designs;
        Verdict = verdict;
    }
}

public readonly struct SeasonSimRow
{
    public readonly int Seed;
    public readonly string ChampionName;
    public readonly string ChampionDesign;
    public readonly string ChampionRole;
    public readonly int ChampionRating;
    public readonly int RatingSpread;
    public readonly int TotalWins;
    public readonly int TotalLosses;

    public SeasonSimRow(int seed, string championName, string championDesign, string championRole,
        int championRating, int ratingSpread, int totalWins, int totalLosses)
    {
        Seed = seed;
        ChampionName = championName;
        ChampionDesign = championDesign;
        ChampionRole = championRole;
        ChampionRating = championRating;
        RatingSpread = ratingSpread;
        TotalWins = totalWins;
        TotalLosses = totalLosses;
    }
}

public sealed class SeasonDesignRow
{
    public readonly string DesignName;
    public readonly string RoleClass;
    public int Championships;
    public int Wins;
    public int Losses;
    public int FinalRatingTotal;
    public int SeasonsSeen;

    public SeasonDesignRow(string designName, string roleClass)
    {
        DesignName = designName;
        RoleClass = roleClass;
    }

    public float WinRate
    {
        get
        {
            int games = Wins + Losses;
            return games > 0 ? Wins / (float)games : 0f;
        }
    }

    public float AverageFinalRating => SeasonsSeen > 0 ? FinalRatingTotal / (float)SeasonsSeen : 0f;
}

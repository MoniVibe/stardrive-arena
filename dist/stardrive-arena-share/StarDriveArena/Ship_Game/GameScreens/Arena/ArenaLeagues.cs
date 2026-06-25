using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SDUtils;

namespace Ship_Game.GameScreens.Arena;

public static class ArenaLeagues
{
    public const string ReportFileName = "arena-leagues.json";
    public const int MinImplementedTeamSize = 1;
    public const int MaxSmallTeamSize = 5;
    public const int MaxImplementedTeamSize = 100;
    public static readonly int[] SmallTeamSizes = { 1, 2, 3, 4, 5 };
    public static readonly int[] BigTeamSizes = { 10, 20, 50, 100 };
    public static readonly int[] ImplementedTeamSizes = { 1, 2, 3, 4, 5, 10, 20, 50, 100 };

    public static ArenaTeam[] EnsureTeams(ArenaCareer career, int teamSize, int maxTeams = int.MaxValue)
    {
        ValidateTeamSize(teamSize);
        if (career == null)
            throw new ArgumentNullException(nameof(career));

        CareerLadder.EnsureContenders(career);
        ContenderRecord[] contenders = (career.Contenders ?? Empty<ContenderRecord>.Array)
            .Where(c => c != null && c.Name.NotEmpty() && c.DesignName.NotEmpty())
            .OrderByDescending(c => c.Rating)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToArray();

        int teamCount = Math.Min(Math.Max(0, maxTeams), contenders.Length / teamSize);
        var teams = new List<ArenaTeam>(teamCount);
        for (int i = 0; i < teamCount; ++i)
        {
            string[] members = contenders
                .Skip(i * teamSize)
                .Take(teamSize)
                .Select(c => c.Name)
                .ToArray();
            teams.Add(new ArenaTeam($"League {teamSize}v{teamSize} Team {i + 1:00}", members));
        }

        ArenaTeam[] keep = (career.Teams ?? Empty<ArenaTeam>.Array)
            .Where(t => t != null && t.Size != teamSize)
            .ToArray();
        career.Teams = keep.Concat(teams).ToArray();
        career.NormalizeForPersistence();
        return TeamsForLeague(career, teamSize);
    }

    public static ArenaTeam[] TeamsForLeague(ArenaCareer career, int teamSize)
    {
        ValidateTeamSize(teamSize);
        return (career?.Teams ?? Empty<ArenaTeam>.Array)
            .Where(t => t != null && t.Size == teamSize)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static ArenaTeam BuildPlayerTeam(ArenaCareer career, string playerMemberName, int teamSize, ulong seed)
    {
        ValidateTeamSize(teamSize);
        string player = playerMemberName.NotEmpty() ? playerMemberName : "PLAYER";
        ContenderRecord[] contenders = (career?.Contenders ?? Empty<ContenderRecord>.Array)
            .Where(c => c != null && c.Name.NotEmpty())
            .OrderBy(c => StableHash(c.Name, seed))
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Take(Math.Max(0, teamSize - 1))
            .ToArray();
        string[] members = new[] { player }.Concat(contenders.Select(c => c.Name)).ToArray();
        return new ArenaTeam($"Player {teamSize}v{teamSize}", members);
    }

    public static ArenaTeam PickEnemyTeam(ArenaCareer career, int teamSize, ulong seed)
    {
        ArenaTeam[] teams = TeamsForLeague(career, teamSize);
        if (teams.Length == 0)
            teams = EnsureTeams(career, teamSize);
        if (teams.Length == 0)
            return null;
        return teams[(int)(Mix(seed) % (ulong)teams.Length)];
    }

    public static PermadeathResult ApplyPermadeath(ArenaCareer career, TeamDuelResult duel, float chance, ulong seed)
    {
        if (career == null) throw new ArgumentNullException(nameof(career));
        if (duel == null) throw new ArgumentNullException(nameof(duel));
        chance = Math.Clamp(chance, 0f, 1f);

        TeamMemberDuelState[] downedLosers = duel.DownedLosers();
        if (downedLosers.Length == 0 || chance <= 0f)
            return new PermadeathResult(downedLosers.Length, 0, 0, Array.Empty<string>(), Array.Empty<string>());

        var deaths = new List<string>();
        for (int i = 0; i < downedLosers.Length; ++i)
        {
            string name = downedLosers[i].MemberName;
            if (name.IsEmpty())
                continue;
            if (chance >= 1f || UnitFloat(Mix(seed ^ StableHash(name, seed) ^ (uint)i)) < chance)
                deaths.Add(name);
        }

        if (deaths.Count == 0)
            return new PermadeathResult(downedLosers.Length, 0, 0, Array.Empty<string>(), Array.Empty<string>());

        var deathSet = new HashSet<string>(deaths, StringComparer.Ordinal);
        var survivors = (career.Contenders ?? Empty<ContenderRecord>.Array)
            .Where(c => c != null && !deathSet.Contains(c.Name))
            .ToList();
        var replacements = new List<ContenderRecord>(deaths.Count);
        int serialBase = (career.Contenders?.Length ?? 0) + 1;
        for (int i = 0; i < deaths.Count; ++i)
        {
            ContenderRecord rookie = ArenaLivingEcosystemSimulator.CreateReplacementRookie(
                survivors.Concat(replacements), seed + (ulong)(i * 97), serialBase + i);
            replacements.Add(rookie);
        }

        var replacementByDeath = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < deaths.Count; ++i)
            replacementByDeath[deaths[i]] = replacements[i].Name;

        survivors.AddRange(replacements);
        career.Contenders = survivors.ToArray();
        career.Teams = ReplaceDeadTeamMembers(career.Teams, replacementByDeath);
        career.NormalizeForPersistence();

        return new PermadeathResult(downedLosers.Length, deaths.Count, replacements.Count,
            deaths.ToArray(), replacements.Select(r => r.Name).ToArray());
    }

    public static Task<ArenaBigLeagueReport> RunLeagueSeasonAsync(ArenaCareer career,
        ArenaLeagueSeasonOptions options)
    {
        if (career == null) throw new ArgumentNullException(nameof(career));
        if (options == null) throw new ArgumentNullException(nameof(options));

        ArenaCareer snapshot = CloneCareer(career);
        return Task.Run(() => RunLeagueSeason(snapshot, options));
    }

    public static async Task<ArenaBigLeagueReport> RunLeagueSeasonAndApplyAsync(ArenaCareer career,
        ArenaLeagueSeasonOptions options)
    {
        ArenaBigLeagueReport report = await RunLeagueSeasonAsync(career, options);
        ApplyLeagueSeasonResults(career, report);
        return report;
    }

    public static ArenaBigLeagueReport RunLeagueSeason(ArenaCareer career, ArenaLeagueSeasonOptions options)
    {
        if (career == null) throw new ArgumentNullException(nameof(career));
        if (options == null) throw new ArgumentNullException(nameof(options));
        ValidateTeamSize(options.TeamSize);

        CareerLadder.EnsureContenders(career);
        int maxTeams = options.MaxTeams <= 0 ? int.MaxValue : options.MaxTeams;
        ArenaTeam[] teams = EnsureTeams(career, options.TeamSize, maxTeams);
        ArenaLeagueMatchup[] sampled = BuildSampledMatchups(teams, options.MatchupBudget, options.Seed,
            out int considered);
        int skipped = Math.Max(0, considered - sampled.Length);
        int concurrency = options.RunParallel ? Math.Min(sampled.Length, options.EffectiveMaxConcurrency) : 1;
        if (concurrency < 1)
            concurrency = 1;

        ArenaBigLeagueMatchResult[] matches = options.RunParallel && sampled.Length > 1
            ? RunMatchupsParallel(career, sampled, options, concurrency)
            : RunMatchupsSerial(career, sampled, options);
        Array.Sort(matches, (a, b) => a.MatchIndex.CompareTo(b.MatchIndex));

        ArenaBigLeagueStanding[] standings = BuildBigLeagueStandings(teams, matches);
        int totalGames = matches.Sum(m => m.Games);
        int totalTicks = matches.Sum(m => m.TicksSimulated);
        string signature = BigLeagueSignature(standings);
        string outcomeSignature = BigLeagueOutcomeSignature(matches);
        string verdict = $"big league {options.TeamSize}v{options.TeamSize}: " +
                         $"sampled {sampled.Length}/{considered}, skipped {skipped}, " +
                         $"parallel={options.RunParallel}, relaxed={options.RelaxedBackgroundMode}";
        return new ArenaBigLeagueReport(options.TeamSize, teams.Length, teams.Sum(t => t.Size),
            considered, options.MatchupBudget, sampled.Length, skipped, options.DuelTicks,
            options.SpawnOffset, concurrency, options.Seed, options.RunParallel, options.RelaxedBackgroundMode,
            totalGames, totalTicks, standings, matches, signature, outcomeSignature, verdict);
    }

    public static void ApplyLeagueSeasonResults(ArenaCareer career, ArenaBigLeagueReport report)
    {
        if (career == null) throw new ArgumentNullException(nameof(career));
        if (report == null) throw new ArgumentNullException(nameof(report));

        if (report.TeamCount > 0)
            EnsureTeams(career, report.TeamSize, report.TeamCount);

        var contenders = (career.Contenders ?? Empty<ContenderRecord>.Array)
            .Where(c => c != null)
            .ToDictionary(c => c.Name, StringComparer.Ordinal);
        var teams = (career.Teams ?? Empty<ArenaTeam>.Array)
            .Where(t => t != null)
            .ToDictionary(t => t.Name, StringComparer.Ordinal);

        foreach (ArenaBigLeagueMatchResult match in report.Matches)
        {
            if (!teams.TryGetValue(match.TeamA, out ArenaTeam a) || !teams.TryGetValue(match.TeamB, out ArenaTeam b))
                continue;
            bool aWon = string.Equals(match.WinnerTeamName, match.TeamA, StringComparison.Ordinal);
            ApplyTeamRecord(aWon ? a : b, contenders, won: true);
            ApplyTeamRecord(aWon ? b : a, contenders, won: false);
        }
        career.NormalizeForPersistence();
    }

    static void ApplyTeamRecord(ArenaTeam team, Dictionary<string, ContenderRecord> contenders, bool won)
    {
        foreach (string member in team.Members ?? Empty<string>.Array)
        {
            if (!contenders.TryGetValue(member ?? "", out ContenderRecord contender))
                continue;
            if (won) contender.Wins += 1;
            else contender.Losses += 1;
        }
    }

    static ArenaLeagueMatchup[] BuildSampledMatchups(ArenaTeam[] teams, int matchupBudget, ulong seed,
        out int considered)
    {
        teams = (teams ?? Empty<ArenaTeam>.Array)
            .Where(t => t != null)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();
        var all = new List<ArenaLeagueMatchup>();
        int index = 0;
        for (int i = 0; i < teams.Length; ++i)
        {
            for (int j = i + 1; j < teams.Length; ++j)
            {
                ulong matchSeed = seed ^ Mix((ulong)(index + 1) * 0x9E37_79B9_7F4A_7C15ul)
                                      ^ StableHash(teams[i].Name + "\0" + teams[j].Name, seed);
                all.Add(new ArenaLeagueMatchup(index++, teams[i], teams[j], matchSeed));
            }
        }

        considered = all.Count;
        int take = matchupBudget <= 0 ? all.Count : Math.Min(matchupBudget, all.Count);
        return all
            .OrderBy(m => Mix(m.Seed ^ seed))
            .ThenBy(m => m.TeamA.Name, StringComparer.Ordinal)
            .ThenBy(m => m.TeamB.Name, StringComparer.Ordinal)
            .Take(take)
            .OrderBy(m => m.MatchIndex)
            .ToArray();
    }

    static ArenaBigLeagueMatchResult[] RunMatchupsSerial(ArenaCareer career, ArenaLeagueMatchup[] matchups,
        ArenaLeagueSeasonOptions options)
    {
        var results = new ArenaBigLeagueMatchResult[matchups.Length];
        for (int i = 0; i < matchups.Length; ++i)
            results[i] = RunMatchup(career, matchups[i], options);
        return results;
    }

    static ArenaBigLeagueMatchResult[] RunMatchupsParallel(ArenaCareer career, ArenaLeagueMatchup[] matchups,
        ArenaLeagueSeasonOptions options, int concurrency)
    {
        var results = new ArenaBigLeagueMatchResult[matchups.Length];
        using var gate = new SemaphoreSlim(concurrency, concurrency);
        Task[] tasks = matchups.Select((matchup, i) => Task.Run(() =>
        {
            gate.Wait();
            try
            {
                results[i] = RunMatchup(career, matchup, options);
            }
            finally
            {
                gate.Release();
            }
        })).ToArray();
        Task.WaitAll(tasks);
        return results;
    }

    static ArenaBigLeagueMatchResult RunMatchup(ArenaCareer career, ArenaLeagueMatchup matchup,
        ArenaLeagueSeasonOptions options)
    {
        if (options.RelaxedBackgroundMode)
        {
            TeamDuelResult duel = CareerLadder.SimulateTeamDuel(career, matchup.TeamA, matchup.TeamB,
                matchup.Seed, options.DuelTicks, options.SpawnOffset);
            bool aWon = string.Equals(duel.WinnerTeamName, matchup.TeamA.Name, StringComparison.Ordinal);
            return new ArenaBigLeagueMatchResult(matchup.MatchIndex, matchup.TeamA.Name, matchup.TeamB.Name,
                duel.WinnerTeamName, 1, aWon ? 1 : 0, aWon ? 0 : 1, duel.SurvivorsA, duel.SurvivorsB,
                duel.RetainedStrengthA, duel.RetainedStrengthB, duel.DamageByA, duel.DamageByB,
                duel.TicksSimulated);
        }

        FairTeamDuelResult fair = CareerLadder.FairTeamDuel(career, matchup.TeamA, matchup.TeamB,
            matchup.Seed, options.DuelTicks, options.SpawnOffset);
        return new ArenaBigLeagueMatchResult(matchup.MatchIndex, matchup.TeamA.Name, matchup.TeamB.Name,
            fair.WinnerTeamName, fair.Games, fair.WinsA, fair.WinsB, fair.SurvivorsA, fair.SurvivorsB,
            fair.RetainedStrengthA, fair.RetainedStrengthB, fair.DamageByA, fair.DamageByB,
            fair.Forward.TicksSimulated + fair.Swapped.TicksSimulated);
    }

    static ArenaBigLeagueStanding[] BuildBigLeagueStandings(ArenaTeam[] teams, ArenaBigLeagueMatchResult[] matches)
    {
        var standings = (teams ?? Empty<ArenaTeam>.Array)
            .Where(t => t != null)
            .ToDictionary(t => t.Name, t => new MutableStanding(), StringComparer.Ordinal);
        foreach (ArenaBigLeagueMatchResult match in matches ?? Empty<ArenaBigLeagueMatchResult>.Array)
        {
            if (!standings.TryGetValue(match.TeamA, out MutableStanding a)
                || !standings.TryGetValue(match.TeamB, out MutableStanding b))
                continue;
            a.Wins += match.WinsA;
            a.Losses += match.WinsB;
            b.Wins += match.WinsB;
            b.Losses += match.WinsA;
        }

        return standings
            .Select(kv => new ArenaBigLeagueStanding(kv.Key, kv.Value.Wins, kv.Value.Losses))
            .OrderByDescending(s => s.Wins)
            .ThenBy(s => s.Losses)
            .ThenBy(s => s.TeamName, StringComparer.Ordinal)
            .ToArray();
    }

    static string BigLeagueSignature(ArenaBigLeagueStanding[] standings)
        => string.Join("|", (standings ?? Empty<ArenaBigLeagueStanding>.Array)
            .Select(s => $"{s.TeamName}:{s.Wins}:{s.Losses}"));

    static string BigLeagueOutcomeSignature(ArenaBigLeagueMatchResult[] matches)
        => string.Join("|", (matches ?? Empty<ArenaBigLeagueMatchResult>.Array)
            .OrderBy(m => m.MatchIndex)
            .Select(m => string.Join(":",
                m.MatchIndex.ToString(CultureInfo.InvariantCulture),
                m.TeamA,
                m.TeamB,
                m.WinnerTeamName,
                m.WinsA.ToString(CultureInfo.InvariantCulture),
                m.WinsB.ToString(CultureInfo.InvariantCulture),
                m.SurvivorsA.ToString(CultureInfo.InvariantCulture),
                m.SurvivorsB.ToString(CultureInfo.InvariantCulture),
                m.TicksSimulated.ToString(CultureInfo.InvariantCulture))));

    static ArenaTeam[] ReplaceDeadTeamMembers(ArenaTeam[] teams, Dictionary<string, string> replacementByDeath)
    {
        if (teams == null || teams.Length == 0 || replacementByDeath.Count == 0)
            return teams ?? Empty<ArenaTeam>.Array;

        var updated = new ArenaTeam[teams.Length];
        for (int i = 0; i < teams.Length; ++i)
        {
            ArenaTeam team = teams[i];
            if (team == null)
                continue;
            string[] members = (team.Members ?? Empty<string>.Array)
                .Select(m => replacementByDeath.TryGetValue(m ?? "", out string rookie) ? rookie : m)
                .ToArray();
            updated[i] = new ArenaTeam(team.Name, members);
        }
        return updated;
    }

    public static ArenaLeagueReport Simulate(int seasons, int rosterSize = 10, int maxTeamSize = MaxSmallTeamSize,
        float permadeathChance = 0f, ulong seed = 0xA11E_1EA6u, int duelTicks = 1800)
    {
        if (seasons < 1)
            throw new ArgumentOutOfRangeException(nameof(seasons));
        if (rosterSize < 2)
            throw new ArgumentOutOfRangeException(nameof(rosterSize));
        ValidateTeamSize(maxTeamSize);

        var leagues = new List<ArenaLeagueRow>();
        int totalDeaths = 0;
        int totalReplacements = 0;
        foreach (int size in ImplementedTeamSizes.Where(s => s <= maxTeamSize))
        {
            var career = new ArenaCareer
            {
                Contenders = CloneContenders(CareerLadder.SeedContenders(rosterSize)),
                PermadeathChance = permadeathChance,
            };
            ArenaTeam[] teams = EnsureTeams(career, size, maxTeams: 4);
            var standings = teams.ToDictionary(t => t.Name, _ => new MutableStanding(), StringComparer.Ordinal);
            int deaths = 0;
            int replacements = 0;

            for (int season = 0; season < seasons; ++season)
            {
                teams = TeamsForLeague(career, size);
                int pairs = teams.Length / 2;
                for (int p = 0; p < pairs; ++p)
                {
                    ArenaTeam a = teams[p];
                    ArenaTeam b = teams[teams.Length - 1 - p];
                    FairTeamDuelResult duel = CareerLadder.FairTeamDuel(career, a, b,
                        seed + (ulong)(size * 1009 + season * 101 + p), duelTicks);
                    standings[duel.WinnerTeamName].Wins += 1;
                    string loser = string.Equals(duel.WinnerTeamName, a.Name, StringComparison.Ordinal) ? b.Name : a.Name;
                    standings[loser].Losses += 1;
                    PermadeathResult death = ApplyPermadeath(career, duel.Forward, career.PermadeathChance,
                        seed + (ulong)(size * 7919 + season * 257 + p));
                    deaths += death.Deaths;
                    replacements += death.Rookies;
                }
            }

            ArenaLeagueStanding[] rows = standings
                .Select(kv => new ArenaLeagueStanding(kv.Key, kv.Value.Wins, kv.Value.Losses))
                .OrderByDescending(s => s.Wins)
                .ThenBy(s => s.Losses)
                .ThenBy(s => s.TeamName, StringComparer.Ordinal)
                .ToArray();
            string champion = rows.Length > 0 ? rows[0].TeamName : "";
            leagues.Add(new ArenaLeagueRow(size, teams.Length, teams.Sum(t => t.Size), deaths, replacements, champion, rows));
            totalDeaths += deaths;
            totalReplacements += replacements;
        }

        string verdict = $"arena leagues seasons={seasons} sizes={string.Join(",", ImplementedTeamSizes.Where(s => s <= maxTeamSize))}; " +
                         $"deaths={totalDeaths}, replacements={totalReplacements}, permadeath={permadeathChance:0.###}";
        return new ArenaLeagueReport(seasons, rosterSize, maxTeamSize, Math.Clamp(permadeathChance, 0f, 1f),
            seed, totalDeaths, totalReplacements, leagues.ToArray(), verdict);
    }

    static ArenaCareer CloneCareer(ArenaCareer career)
    {
        var clone = new ArenaCareer
        {
            Contenders = CloneContenders(career?.Contenders),
            Teams = CloneTeams(career?.Teams),
            PermadeathChance = career?.PermadeathChance ?? 0f,
        };
        clone.NormalizeForPersistence();
        return clone;
    }

    static ContenderRecord[] CloneContenders(ContenderRecord[] contenders)
        => (contenders ?? Empty<ContenderRecord>.Array)
            .Where(c => c != null)
            .Select(c => new ContenderRecord(c.Name, c.DesignName, c.RoleClass, c.Rating)
            {
                ContenderId = c.ContenderId,
                Epithet = c.Epithet,
                ArenaPersona = c.ArenaPersona,
                OriginHook = c.OriginHook,
                PreferredFleetScale = c.PreferredFleetScale,
                Bio = c.Bio,
                Wins = c.Wins,
                Losses = c.Losses,
                Seasons = c.Seasons,
                Experience = c.Experience,
                Level = c.Level,
                Evolutions = c.Evolutions,
                RivalName = c.RivalName,
                RivalWins = c.RivalWins,
                RivalLosses = c.RivalLosses,
            })
            .ToArray();

    static ArenaTeam[] CloneTeams(ArenaTeam[] teams)
        => (teams ?? Empty<ArenaTeam>.Array)
            .Where(t => t != null)
            .Select(t => new ArenaTeam(t.Name, (t.Members ?? Empty<string>.Array).ToArray()))
            .ToArray();

    public static string WriteReport(ArenaLeagueReport report, string outputDir)
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

    public static string ToJson(ArenaLeagueReport report)
    {
        string leagues = string.Join(",\n    ", report.Leagues.Select(l =>
        {
            string standings = string.Join(", ", l.Standings.Select(s =>
                $"{{\"team\":{J(s.TeamName)},\"wins\":{s.Wins},\"losses\":{s.Losses}}}"));
            return $"{{\"teamSize\":{l.TeamSize},\"teams\":{l.TeamCount},\"population\":{l.Population}," +
                   $"\"deaths\":{l.Deaths},\"replacements\":{l.Replacements},\"champion\":{J(l.Champion)}," +
                   $"\"standings\":[{standings}]}}";
        }));
        string bigLeagues = ToBigLeagueJson(report.BigLeague, "  ");

        return "{\n" +
               "  \"experiment\": \"ARENA TEAM LEAGUES: deterministic team duels with permadeath/backfill and sampled big leagues\",\n" +
               $"  \"seed\": {report.Seed},\n" +
               $"  \"seasons\": {report.Seasons},\n" +
               $"  \"rosterSize\": {report.RosterSize},\n" +
               $"  \"maxTeamSize\": {report.MaxTeamSize},\n" +
               $"  \"permadeathChance\": {F(report.PermadeathChance)},\n" +
               $"  \"deaths\": {report.Deaths},\n" +
               $"  \"replacements\": {report.Replacements},\n" +
               $"  \"verdict\": {J(report.Verdict)},\n" +
               $"  \"leagues\": [\n    {leagues}\n  ],\n" +
               $"  \"bigLeagues\": {bigLeagues}\n" +
               "}\n";
    }

    public static string ToJson(ArenaBigLeagueReport report)
        => ToBigLeagueJson(report, "");

    static string ToBigLeagueJson(ArenaBigLeagueReport report, string indent)
    {
        if (report == null)
            return "null";

        string inner = indent + "  ";
        string standings = string.Join(",\n" + inner + "    ", report.Standings.Select(s =>
            $"{{\"team\":{J(s.TeamName)},\"wins\":{s.Wins},\"losses\":{s.Losses},\"games\":{s.Games}}}"));
        string matches = string.Join(",\n" + inner + "    ", report.Matches.Select(m =>
            $"{{\"index\":{m.MatchIndex},\"teamA\":{J(m.TeamA)},\"teamB\":{J(m.TeamB)}," +
            $"\"winner\":{J(m.WinnerTeamName)},\"games\":{m.Games},\"winsA\":{m.WinsA},\"winsB\":{m.WinsB}," +
            $"\"survivorsA\":{m.SurvivorsA},\"survivorsB\":{m.SurvivorsB},\"ticks\":{m.TicksSimulated}}}"));

        return "{\n" +
               $"{inner}\"teamSize\": {report.TeamSize},\n" +
               $"{inner}\"teams\": {report.TeamCount},\n" +
               $"{inner}\"population\": {report.Population},\n" +
               $"{inner}\"matchupsConsidered\": {report.MatchupsConsidered},\n" +
               $"{inner}\"matchupBudget\": {report.MatchupBudget},\n" +
               $"{inner}\"matchupsSampled\": {report.MatchupsSampled},\n" +
               $"{inner}\"matchupsSkipped\": {report.MatchupsSkipped},\n" +
               $"{inner}\"duelTicks\": {report.DuelTicks},\n" +
               $"{inner}\"spawnOffset\": {F(report.SpawnOffset)},\n" +
               $"{inner}\"maxConcurrency\": {report.MaxConcurrency},\n" +
               $"{inner}\"parallel\": {Bool(report.RunParallel)},\n" +
               $"{inner}\"relaxedBackgroundMode\": {Bool(report.RelaxedBackgroundMode)},\n" +
               $"{inner}\"totalGames\": {report.TotalGames},\n" +
               $"{inner}\"totalTicksSimulated\": {report.TotalTicksSimulated},\n" +
               $"{inner}\"standingsSignature\": {J(report.StandingsSignature)},\n" +
               $"{inner}\"outcomeSignature\": {J(report.OutcomeSignature)},\n" +
               $"{inner}\"verdict\": {J(report.Verdict)},\n" +
               $"{inner}\"standings\": [\n{inner}    {standings}\n{inner}],\n" +
               $"{inner}\"sampledMatchups\": [\n{inner}    {matches}\n{inner}]\n" +
               $"{indent}}}";
    }

    public static void ValidateTeamSize(int teamSize)
    {
        if (!ImplementedTeamSizes.Contains(teamSize))
            throw new ArgumentOutOfRangeException(nameof(teamSize),
                $"Implemented arena leagues support {string.Join(",", ImplementedTeamSizes)}.");
    }

    static float UnitFloat(ulong mixed)
        => (mixed >> 11) * (1f / (1ul << 53));

    static ulong StableHash(string text, ulong seed)
    {
        const ulong Offset = 14695981039346656037ul;
        const ulong Prime = 1099511628211ul;
        ulong hash = Offset ^ seed;
        foreach (char c in text ?? "")
        {
            hash ^= c;
            hash *= Prime;
        }
        return hash;
    }

    static ulong Mix(ulong x)
    {
        x ^= x >> 30;
        x *= 0xbf58476d1ce4e5b9UL;
        x ^= x >> 27;
        x *= 0x94d049bb133111ebUL;
        x ^= x >> 31;
        return x;
    }

    static string J(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string Bool(bool v) => v ? "true" : "false";

    sealed class MutableStanding
    {
        public int Wins;
        public int Losses;
    }
}

public sealed class PermadeathResult
{
    public readonly int DownedLosers;
    public readonly int Deaths;
    public readonly int Rookies;
    public readonly string[] DeadMembers;
    public readonly string[] RookieMembers;

    public PermadeathResult(int downedLosers, int deaths, int rookies, string[] deadMembers, string[] rookieMembers)
    {
        DownedLosers = Math.Max(0, downedLosers);
        Deaths = Math.Max(0, deaths);
        Rookies = Math.Max(0, rookies);
        DeadMembers = deadMembers ?? Array.Empty<string>();
        RookieMembers = rookieMembers ?? Array.Empty<string>();
    }
}

sealed class ArenaLeagueMatchup
{
    public readonly int MatchIndex;
    public readonly ArenaTeam TeamA;
    public readonly ArenaTeam TeamB;
    public readonly ulong Seed;

    public ArenaLeagueMatchup(int matchIndex, ArenaTeam teamA, ArenaTeam teamB, ulong seed)
    {
        MatchIndex = matchIndex;
        TeamA = teamA;
        TeamB = teamB;
        Seed = seed;
    }
}

public sealed class ArenaLeagueSeasonOptions
{
    public readonly int TeamSize;
    public readonly int MatchupBudget;
    public readonly int DuelTicks;
    public readonly float SpawnOffset;
    public readonly int MaxConcurrency;
    public readonly int MaxTeams;
    public readonly ulong Seed;
    public readonly bool RunParallel;
    public readonly bool RelaxedBackgroundMode;

    public ArenaLeagueSeasonOptions(int teamSize, int matchupBudget, int duelTicks, ulong seed,
        bool runParallel = true, int maxConcurrency = 0, int maxTeams = 0,
        float spawnOffset = 6000f, bool relaxedBackgroundMode = false)
    {
        TeamSize = teamSize;
        MatchupBudget = Math.Max(0, matchupBudget);
        DuelTicks = Math.Max(1, duelTicks);
        SpawnOffset = spawnOffset > 0f ? spawnOffset : 6000f;
        MaxConcurrency = maxConcurrency;
        MaxTeams = Math.Max(0, maxTeams);
        Seed = seed;
        RunParallel = runParallel;
        RelaxedBackgroundMode = relaxedBackgroundMode;
    }

    public int EffectiveMaxConcurrency
        => Math.Max(1, Math.Min(MaxConcurrency > 0 ? MaxConcurrency : Environment.ProcessorCount,
            Math.Max(1, Environment.ProcessorCount)));
}

public sealed class ArenaBigLeagueReport
{
    public readonly int TeamSize;
    public readonly int TeamCount;
    public readonly int Population;
    public readonly int MatchupsConsidered;
    public readonly int MatchupBudget;
    public readonly int MatchupsSampled;
    public readonly int MatchupsSkipped;
    public readonly int DuelTicks;
    public readonly float SpawnOffset;
    public readonly int MaxConcurrency;
    public readonly ulong Seed;
    public readonly bool RunParallel;
    public readonly bool RelaxedBackgroundMode;
    public readonly int TotalGames;
    public readonly int TotalTicksSimulated;
    public readonly ArenaBigLeagueStanding[] Standings;
    public readonly ArenaBigLeagueMatchResult[] Matches;
    public readonly string StandingsSignature;
    public readonly string OutcomeSignature;
    public readonly string Verdict;

    public ArenaBigLeagueReport(int teamSize, int teamCount, int population,
        int matchupsConsidered, int matchupBudget, int matchupsSampled, int matchupsSkipped,
        int duelTicks, float spawnOffset, int maxConcurrency, ulong seed, bool runParallel,
        bool relaxedBackgroundMode, int totalGames, int totalTicksSimulated,
        ArenaBigLeagueStanding[] standings, ArenaBigLeagueMatchResult[] matches,
        string standingsSignature, string outcomeSignature, string verdict)
    {
        TeamSize = teamSize;
        TeamCount = teamCount;
        Population = population;
        MatchupsConsidered = matchupsConsidered;
        MatchupBudget = matchupBudget;
        MatchupsSampled = matchupsSampled;
        MatchupsSkipped = matchupsSkipped;
        DuelTicks = duelTicks;
        SpawnOffset = spawnOffset;
        MaxConcurrency = maxConcurrency;
        Seed = seed;
        RunParallel = runParallel;
        RelaxedBackgroundMode = relaxedBackgroundMode;
        TotalGames = totalGames;
        TotalTicksSimulated = totalTicksSimulated;
        Standings = standings ?? Array.Empty<ArenaBigLeagueStanding>();
        Matches = matches ?? Array.Empty<ArenaBigLeagueMatchResult>();
        StandingsSignature = standingsSignature ?? "";
        OutcomeSignature = outcomeSignature ?? "";
        Verdict = verdict ?? "";
    }
}

public sealed class ArenaBigLeagueMatchResult
{
    public readonly int MatchIndex;
    public readonly string TeamA;
    public readonly string TeamB;
    public readonly string WinnerTeamName;
    public readonly int Games;
    public readonly int WinsA;
    public readonly int WinsB;
    public readonly int SurvivorsA;
    public readonly int SurvivorsB;
    public readonly float RetainedStrengthA;
    public readonly float RetainedStrengthB;
    public readonly float DamageByA;
    public readonly float DamageByB;
    public readonly int TicksSimulated;

    public ArenaBigLeagueMatchResult(int matchIndex, string teamA, string teamB, string winnerTeamName,
        int games, int winsA, int winsB, int survivorsA, int survivorsB,
        float retainedStrengthA, float retainedStrengthB, float damageByA, float damageByB,
        int ticksSimulated)
    {
        MatchIndex = matchIndex;
        TeamA = teamA ?? "";
        TeamB = teamB ?? "";
        WinnerTeamName = winnerTeamName ?? "";
        Games = Math.Max(0, games);
        WinsA = Math.Max(0, winsA);
        WinsB = Math.Max(0, winsB);
        SurvivorsA = Math.Max(0, survivorsA);
        SurvivorsB = Math.Max(0, survivorsB);
        RetainedStrengthA = retainedStrengthA;
        RetainedStrengthB = retainedStrengthB;
        DamageByA = damageByA;
        DamageByB = damageByB;
        TicksSimulated = Math.Max(0, ticksSimulated);
    }
}

public readonly struct ArenaBigLeagueStanding
{
    public readonly string TeamName;
    public readonly int Wins;
    public readonly int Losses;
    public int Games => Wins + Losses;

    public ArenaBigLeagueStanding(string teamName, int wins, int losses)
    {
        TeamName = teamName ?? "";
        Wins = wins;
        Losses = losses;
    }
}

public sealed class ArenaLeagueReport
{
    public readonly int Seasons;
    public readonly int RosterSize;
    public readonly int MaxTeamSize;
    public readonly float PermadeathChance;
    public readonly ulong Seed;
    public readonly int Deaths;
    public readonly int Replacements;
    public readonly ArenaLeagueRow[] Leagues;
    public readonly ArenaBigLeagueReport BigLeague;
    public readonly string Verdict;

    public ArenaLeagueReport(int seasons, int rosterSize, int maxTeamSize, float permadeathChance,
        ulong seed, int deaths, int replacements, ArenaLeagueRow[] leagues, string verdict,
        ArenaBigLeagueReport bigLeague = null)
    {
        Seasons = seasons;
        RosterSize = rosterSize;
        MaxTeamSize = maxTeamSize;
        PermadeathChance = permadeathChance;
        Seed = seed;
        Deaths = deaths;
        Replacements = replacements;
        Leagues = leagues ?? Array.Empty<ArenaLeagueRow>();
        BigLeague = bigLeague;
        Verdict = verdict ?? "";
    }

    public ArenaLeagueReport WithBigLeague(ArenaBigLeagueReport bigLeague)
        => new(Seasons, RosterSize, MaxTeamSize, PermadeathChance, Seed, Deaths, Replacements,
            Leagues, Verdict, bigLeague);
}

public sealed class ArenaLeagueRow
{
    public readonly int TeamSize;
    public readonly int TeamCount;
    public readonly int Population;
    public readonly int Deaths;
    public readonly int Replacements;
    public readonly string Champion;
    public readonly ArenaLeagueStanding[] Standings;

    public ArenaLeagueRow(int teamSize, int teamCount, int population, int deaths, int replacements,
        string champion, ArenaLeagueStanding[] standings)
    {
        TeamSize = teamSize;
        TeamCount = teamCount;
        Population = population;
        Deaths = deaths;
        Replacements = replacements;
        Champion = champion ?? "";
        Standings = standings ?? Array.Empty<ArenaLeagueStanding>();
    }
}

public readonly struct ArenaLeagueStanding
{
    public readonly string TeamName;
    public readonly int Wins;
    public readonly int Losses;

    public ArenaLeagueStanding(string teamName, int wins, int losses)
    {
        TeamName = teamName ?? "";
        Wins = wins;
        Losses = losses;
    }
}

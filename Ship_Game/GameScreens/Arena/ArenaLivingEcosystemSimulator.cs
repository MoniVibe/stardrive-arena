using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Ship_Game.Ships;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// Stretch-engine prototype for a living arena contender ecosystem. It advances persistent
/// contenders through deterministic seasons: ladder bouts grant career XP/levels, old low-rated
/// contenders retire into underrepresented-class rookies, and each contender tries role/module-aware
/// design mutations that must beat the incumbent in <see cref="CareerLadder.FairDuel"/> before they
/// are kept. A diversity guard rejects evolutions that would collapse the roster into one class.
/// </summary>
public static class ArenaLivingEcosystemSimulator
{
    public const string ReportFileName = "arena-living-ecosystem.json";
    public const float EvolutionPlayerCapMultiplier = 4.0f;
    const int MinRetirementSeasons = 2;
    const int XpWin = 120;
    const int XpLoss = 40;
    const int MaxMutationCandidates = 6;

    static readonly RoleName[] EcosystemRoles =
    {
        RoleName.gunboat, RoleName.corvette, RoleName.frigate, RoleName.destroyer,
        RoleName.cruiser, RoleName.battleship, RoleName.capital,
    };

    public static LivingEcosystemReport Simulate(int seasons, int rosterSize = 8, ulong seed = 0xA11E_EC05u)
    {
        if (seasons < 1)
            throw new ArgumentOutOfRangeException(nameof(seasons), "At least one ecosystem season is required.");
        if (rosterSize < 4)
            throw new ArgumentOutOfRangeException(nameof(rosterSize), "Roster size must be at least four.");

        IShipDesign[] legal = LegalDesigns();
        if (legal.Length < rosterSize + 1)
            throw new InvalidOperationException("Not enough legal arena warships to simulate a living ecosystem.");

        var career = new ArenaCareer { Contenders = SeedDiverseContenders(legal, rosterSize) };
        foreach (ContenderRecord c in career.Contenders)
            NormalizeCareerFields(c);

        string[] initialNames = career.Contenders.Select(c => c.Name).ToArray();
        float initialAverageStrength = AverageStrength(career.Contenders);
        float initialTopStrength = TopStrength(career.Contenders);
        float evolutionStrengthCap = Math.Max(EvolutionStrengthCap(), initialTopStrength);
        IShipDesign baseline = legal[legal.Length / 2];
        int initialBaselineWins = CountBaselineWins(career.Contenders, baseline, seed ^ 0xBACEu);

        int ratingChurn = 0;
        int retirements = 0;
        int rookies = 0;
        int evolutions = 0;
        int rookieSerial = 1;
        var diversity = new List<EcosystemDiversityRow>(seasons + 1)
        {
            BuildDiversityRow(0, career.Contenders)
        };

        for (int season = 0; season < seasons; ++season)
        {
            foreach (ContenderRecord c in career.Contenders)
                c.Seasons += 1;

            ratingChurn += RunSeasonRound(career, seed + (ulong)(season * 101));
            evolutions += RunEvolutionPass(career, legal, seed + (ulong)(season * 4099), evolutionStrengthCap);

            IShipDesign[] cappedLegal = legal.Where(d => d.BaseStrength <= evolutionStrengthCap).ToArray();
            int retiredThisSeason = RetireAndReplace(career, cappedLegal.Length > 0 ? cappedLegal : legal,
                season, seed, ref rookieSerial);
            retirements += retiredThisSeason;
            rookies += retiredThisSeason;
            diversity.Add(BuildDiversityRow(season + 1, career.Contenders));
        }

        string[] finalNames = career.Contenders.Select(c => c.Name).ToArray();
        int survivorCount = finalNames.Count(n => initialNames.Contains(n, StringComparer.Ordinal));
        float finalAverageStrength = AverageStrength(career.Contenders);
        float finalTopStrength = TopStrength(career.Contenders);
        int finalBaselineWins = CountBaselineWins(career.Contenders, baseline, seed ^ 0xFACEu);

        ContenderCareerRow[] rows = career.Contenders
            .OrderByDescending(c => c.Rating)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => new ContenderCareerRow(
                c.Name, c.DesignName, c.RoleClass, c.Rating, c.Wins, c.Losses,
                c.Seasons, c.Experience, c.Level, c.Evolutions))
            .ToArray();

        string verdict = $"living ecosystem seasons={seasons} roster={rows.Length}; " +
                         $"survivors={survivorCount}/{initialNames.Length}, retirements={retirements}, " +
                         $"evolutions={evolutions}, strength {initialAverageStrength:0}->{finalAverageStrength:0}, " +
                         $"baselineWins {initialBaselineWins}->{finalBaselineWins}, " +
                         $"finalSpread {diversity[diversity.Count - 1].RoleSpread}";

        return new LivingEcosystemReport(seasons, rosterSize, seed, baseline.Name,
            initialAverageStrength, finalAverageStrength, initialBaselineWins, finalBaselineWins,
            initialTopStrength, finalTopStrength, evolutionStrengthCap,
            ratingChurn, retirements, rookies, evolutions, survivorCount, rows,
            diversity.ToArray(), verdict);
    }

    static IShipDesign[] LegalDesigns()
        => ResourceManager.Ships.Designs
            .Where(d => ArenaFightScreen.IsHeavyGunWarship(d) && !ArenaFightScreen.IsDevTestDesign(d))
            .OrderBy(d => d.BaseStrength)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

    static ContenderRecord[] SeedDiverseContenders(IShipDesign[] legal, int rosterSize)
    {
        var picked = new List<IShipDesign>(rosterSize);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (RoleName role in EcosystemRoles)
        {
            if (picked.Count >= rosterSize)
                break;
            IShipDesign[] bucket = legal
                .Where(d => d.Role == role)
                .OrderBy(d => d.BaseStrength)
                .ThenBy(d => d.Name, StringComparer.Ordinal)
                .ToArray();
            if (bucket.Length == 0)
                continue;
            IShipDesign design = bucket[Math.Min(bucket.Length - 1, Math.Max(0, bucket.Length / 3))];
            if (seen.Add(design.Name))
                picked.Add(design);
        }

        foreach (IShipDesign design in legal)
        {
            if (picked.Count >= rosterSize)
                break;
            if (seen.Add(design.Name))
                picked.Add(design);
        }

        return picked.Select((d, i) => new ContenderRecord(
                $"Eco {i + 1:00} of {d.Role.ToString().ToUpperInvariant()}",
                d.Name,
                d.Role.ToString().ToUpperInvariant(),
                InitialRating(d)))
            .ToArray();
    }

    static int RunSeasonRound(ArenaCareer career, ulong seed)
    {
        ContenderRecord[] before = career.Contenders
            .Where(c => c != null)
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToArray();
        var ratingsBefore = before.ToDictionary(c => c.Name, c => c.Rating, StringComparer.Ordinal);

        LadderRoundResult[] results = CareerLadder.RunLadderRound(career, seed);
        foreach (LadderRoundResult result in results)
        {
            ContenderRecord a = FindByName(career, result.ContenderA);
            ContenderRecord b = FindByName(career, result.ContenderB);
            bool aWon = a != null && string.Equals(result.WinnerDesignName, a.DesignName, StringComparison.Ordinal);
            AwardCareer(aWon ? a : b, won: true);
            AwardCareer(aWon ? b : a, won: false);
        }

        int churn = 0;
        foreach (ContenderRecord c in before)
            if (ratingsBefore.TryGetValue(c.Name, out int oldRating))
                churn += Math.Abs(c.Rating - oldRating);
        return churn;
    }

    static void AwardCareer(ContenderRecord c, bool won)
    {
        if (c == null)
            return;
        c.Experience += won ? XpWin : XpLoss;
        int level = 1 + (int)(c.Experience / 250f);
        if (level > c.Level)
            c.Level = level;
    }

    static int RunEvolutionPass(ArenaCareer career, IShipDesign[] legal, ulong seed, float evolutionStrengthCap)
    {
        int evolved = 0;
        ContenderRecord[] contenders = career.Contenders ?? Array.Empty<ContenderRecord>();
        for (int i = 0; i < contenders.Length; ++i)
        {
            ContenderRecord c = contenders[i];
            if (c == null || c.DesignName.IsEmpty())
                continue;
            if (!ResourceManager.Ships.GetDesign(c.DesignName, out IShipDesign current))
                continue;

            foreach (IShipDesign candidate in PickMutationCandidates(legal, contenders, i, current,
                         seed + (ulong)i, evolutionStrengthCap))
            {
                if (candidate == null || string.Equals(candidate.Name, current.Name, StringComparison.Ordinal))
                    continue;
                if (candidate.BaseStrength > evolutionStrengthCap)
                    continue;
                if (!PreservesRoleDiversity(contenders, i, candidate.Role))
                    continue;

                FairDuelResult duel = CareerLadder.FairDuel(current.Name, candidate.Name, seed + (ulong)(i * 17));
                if (!string.Equals(duel.WinnerDesignName, candidate.Name, StringComparison.Ordinal))
                    continue;

                c.DesignName = candidate.Name;
                c.RoleClass = candidate.Role.ToString().ToUpperInvariant();
                c.Rating = Math.Max(c.Rating, InitialRating(candidate));
                c.Evolutions += 1;
                evolved += 1;
                break;
            }
        }
        return evolved;
    }

    static IShipDesign[] PickMutationCandidates(IShipDesign[] legal, ContenderRecord[] contenders,
        int contenderIndex, IShipDesign current, ulong seed, float evolutionStrengthCap)
    {
        if (legal == null || current == null)
            return Array.Empty<IShipDesign>();

        RoleName underrepresented = UnderrepresentedRole(contenders, seed);
        IShipDesign[] roleAware = legal
            .Where(d => !string.Equals(d.Name, current.Name, StringComparison.Ordinal))
            .Where(d => d.BaseStrength >= current.BaseStrength * 0.85f
                     && d.BaseStrength <= current.BaseStrength * 10.00f
                     && d.BaseStrength <= evolutionStrengthCap)
            .Select(d => new
            {
                Design = d,
                DiversityOk = PreservesRoleDiversity(contenders, contenderIndex, d.Role),
                Underrepresented = d.Role == underrepresented,
                SameRole = d.Role == current.Role,
                AdjacentRole = RoleDistance(d.Role, current.Role) <= 1,
                ModuleDelta = ModuleDelta(current, d),
                MeaningfulUpgrade = d.BaseStrength > current.BaseStrength * 1.08f,
                StrengthTarget = Math.Abs((d.BaseStrength / Math.Max(1f, current.BaseStrength)) - 1.35f),
                Tie = StableHash(d.Name, seed),
            })
            .Where(x => x.DiversityOk)
            .OrderByDescending(x => x.SameRole)
            .ThenByDescending(x => x.AdjacentRole)
            .ThenByDescending(x => x.Underrepresented)
            .ThenByDescending(x => x.MeaningfulUpgrade)
            .ThenByDescending(x => x.ModuleDelta)
            .ThenBy(x => x.StrengthTarget)
            .ThenBy(x => x.Tie)
            .ThenBy(x => x.Design.Name, StringComparer.Ordinal)
            .Select(x => x.Design)
            .Take(Math.Max(1, MaxMutationCandidates / 2))
            .ToArray();

        IShipDesign[] powerFallback = legal
            .Where(d => !string.Equals(d.Name, current.Name, StringComparison.Ordinal))
            .Where(d => d.BaseStrength > current.BaseStrength * 1.08f)
            .Where(d => d.BaseStrength <= evolutionStrengthCap)
            .Where(d => PreservesRoleDiversity(contenders, contenderIndex, d.Role))
            .OrderBy(d => d.BaseStrength)
            .ThenByDescending(d => ModuleDelta(current, d))
            .ThenBy(d => StableHash(d.Name, seed))
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .Take(MaxMutationCandidates)
            .ToArray();

        IShipDesign[] candidates = roleAware
            .Concat(powerFallback)
            .GroupBy(d => d.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .Take(MaxMutationCandidates)
            .ToArray();
        if (candidates.Length > 0)
            return candidates;

        return legal
            .Where(d => !string.Equals(d.Name, current.Name, StringComparison.Ordinal)
                     && d.BaseStrength <= evolutionStrengthCap
                     && PreservesRoleDiversity(contenders, contenderIndex, d.Role))
            .OrderBy(d => RoleDistance(d.Role, current.Role))
            .ThenByDescending(d => ModuleDelta(current, d))
            .ThenBy(d => Math.Abs(d.BaseStrength - current.BaseStrength))
            .ThenBy(d => StableHash(d.Name, seed))
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .Take(MaxMutationCandidates)
            .ToArray();
    }

    static int RetireAndReplace(ArenaCareer career, IShipDesign[] legal, int season, ulong seed, ref int rookieSerial)
    {
        career.NormalizeForPersistence();
        ContenderRecord[] roster = career.Contenders ?? Array.Empty<ContenderRecord>();
        if (roster.Length == 0)
            return 0;

        int maxRetire = Math.Max(1, roster.Length / 4);
        ContenderRecord[] retirees = roster
            .Where(c => c != null && c.Seasons >= MinRetirementSeasons)
            .OrderBy(c => c.Rating)
            .ThenByDescending(c => c.Seasons)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Take(maxRetire)
            .ToArray();
        if (retirees.Length == 0)
            return 0;

        var retired = new HashSet<ContenderRecord>(retirees);
        career.RetiredContenders = (career.RetiredContenders ?? Array.Empty<ContenderRecord>())
            .Concat(retirees)
            .ToArray();
        var survivors = roster.Where(c => c != null && !retired.Contains(c)).ToList();
        var usedDesigns = new HashSet<string>(survivors.Select(c => c.DesignName), StringComparer.Ordinal);
        foreach (ContenderRecord retiree in retirees)
        {
            int serial = rookieSerial++;
            ContenderRecord rookie = CreateRookie(legal, usedDesigns, survivors, season, seed, serial);
            career.AddChronicleEvent("retirement", retiree.Name,
                $"{retiree.Name} retired from the ladder.", seed + (ulong)(season * 1000 + serial));
            career.AddChronicleEvent("rookie", rookie.Name,
                $"{rookie.Name} entered with {rookie.DesignName}.", seed + (ulong)(season * 1000 + serial + 500));
            survivors.Add(rookie);
        }

        career.Contenders = survivors.ToArray();
        career.NormalizeForPersistence();
        return retirees.Length;
    }

    static ContenderRecord CreateRookie(IShipDesign[] legal, HashSet<string> usedDesigns,
        List<ContenderRecord> survivors, int season, ulong seed, int serial)
    {
        RoleName targetRole = UnderrepresentedRole(survivors.ToArray(), seed + (ulong)serial);
        int start = (int)((seed + (ulong)(season * 37 + serial * 13)) % (ulong)legal.Length);
        IShipDesign picked = PickRookieDesign(legal, usedDesigns, targetRole, start)
                          ?? PickRookieDesign(legal, usedDesigns, RoleName.disabled, start);
        if (picked != null)
        {
            return new ContenderRecord(
                $"Rookie {serial:00}",
                picked.Name,
                picked.Role.ToString().ToUpperInvariant(),
                InitialRating(picked));
        }

        IShipDesign fallback = legal[start];
        return new ContenderRecord(
            $"Rookie {serial:00}",
            fallback.Name,
            fallback.Role.ToString().ToUpperInvariant(),
            InitialRating(fallback));
    }

    static IShipDesign PickRookieDesign(IShipDesign[] legal, HashSet<string> usedDesigns,
        RoleName targetRole, int start)
    {
        for (int offset = 0; offset < legal.Length; ++offset)
        {
            IShipDesign design = legal[(start + offset) % legal.Length];
            if (targetRole != RoleName.disabled && design.Role != targetRole)
                continue;
            if (!usedDesigns.Add(design.Name))
                continue;
            return design;
        }
        return null;
    }

    public static ContenderRecord CreateReplacementRookie(IEnumerable<ContenderRecord> survivors, ulong seed, int serial)
    {
        IShipDesign[] legal = LegalDesigns();
        if (legal.Length == 0)
            throw new InvalidOperationException("No legal arena designs are available for rookie replacement.");

        var survivorList = (survivors ?? Array.Empty<ContenderRecord>())
            .Where(c => c != null)
            .ToList();
        var usedDesigns = new HashSet<string>(survivorList.Select(c => c.DesignName), StringComparer.Ordinal);
        return CreateRookie(legal, usedDesigns, survivorList, season: 0, seed, serial);
    }

    static bool PreservesRoleDiversity(ContenderRecord[] contenders, int contenderIndex, RoleName newRole)
    {
        if (contenders == null || contenders.Length == 0)
            return true;

        var counts = RoleCounts(contenders);
        RoleName oldRole = RoleName.disabled;
        if (contenderIndex >= 0 && contenderIndex < contenders.Length
            && contenders[contenderIndex] != null
            && ResourceManager.Ships.GetDesign(contenders[contenderIndex].DesignName, out IShipDesign oldDesign))
            oldRole = oldDesign.Role;

        if (oldRole != RoleName.disabled && counts.ContainsKey(oldRole))
            counts[oldRole] = Math.Max(0, counts[oldRole] - 1);
        if (!counts.ContainsKey(newRole))
            counts[newRole] = 0;
        counts[newRole] += 1;

        int rosterSize = contenders.Count(c => c != null);
        int maxPerRole = MaxPerRole(rosterSize);
        int minDistinct = MinDistinctRoles(rosterSize);
        int distinct = counts.Count(kv => kv.Value > 0);
        return counts[newRole] <= maxPerRole && distinct >= minDistinct;
    }

    static RoleName UnderrepresentedRole(ContenderRecord[] contenders, ulong seed)
    {
        var counts = RoleCounts(contenders);
        return EcosystemRoles
            .OrderBy(r => counts.TryGetValue(r, out int n) ? n : 0)
            .ThenBy(r => StableHash(r.ToString(), seed))
            .First();
    }

    static Dictionary<RoleName, int> RoleCounts(IEnumerable<ContenderRecord> contenders)
    {
        var counts = new Dictionary<RoleName, int>();
        foreach (ContenderRecord c in contenders ?? Array.Empty<ContenderRecord>())
        {
            if (c == null || !ResourceManager.Ships.GetDesign(c.DesignName, out IShipDesign design))
                continue;
            if (!counts.ContainsKey(design.Role))
                counts[design.Role] = 0;
            counts[design.Role] += 1;
        }
        return counts;
    }

    static int MaxPerRole(int rosterSize)
        => Math.Max(2, (int)Math.Ceiling(Math.Max(1, rosterSize) * 0.40f));

    static int MinDistinctRoles(int rosterSize)
        => Math.Min(Math.Max(1, rosterSize), Math.Min(EcosystemRoles.Length, 4));

    static int RoleDistance(RoleName a, RoleName b)
        => Math.Abs(RoleIndex(a) - RoleIndex(b));

    static int RoleIndex(RoleName role)
    {
        for (int i = 0; i < EcosystemRoles.Length; ++i)
            if (EcosystemRoles[i] == role)
                return i;
        return EcosystemRoles.Length;
    }

    static int ModuleDelta(IShipDesign current, IShipDesign candidate)
    {
        var currentModules = new HashSet<string>(current?.UniqueModuleUIDs ?? Array.Empty<string>(), StringComparer.Ordinal);
        var candidateModules = new HashSet<string>(candidate?.UniqueModuleUIDs ?? Array.Empty<string>(), StringComparer.Ordinal);
        if (currentModules.Count == 0 && candidateModules.Count == 0)
            return 0;

        int delta = 0;
        foreach (string uid in candidateModules)
            if (!currentModules.Contains(uid))
                delta += 1;
        foreach (string uid in currentModules)
            if (!candidateModules.Contains(uid))
                delta += 1;
        return delta;
    }

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

    static ContenderRecord FindByName(ArenaCareer career, string name)
        => (career.Contenders ?? Array.Empty<ContenderRecord>())
            .FirstOrDefault(c => c != null && string.Equals(c.Name, name, StringComparison.Ordinal));

    static void NormalizeCareerFields(ContenderRecord c)
    {
        if (c == null)
            return;
        if (c.Experience < 0f) c.Experience = 0f;
        if (c.Level < 0) c.Level = 0;
        if (c.Seasons < 0) c.Seasons = 0;
        if (c.Evolutions < 0) c.Evolutions = 0;
    }

    static int InitialRating(IShipDesign design)
        => Math.Max(100, (int)Math.Round(Math.Sqrt(Math.Max(100f, design.BaseStrength)) * 10f));

    static float AverageStrength(ContenderRecord[] contenders)
    {
        float total = 0f;
        int count = 0;
        foreach (ContenderRecord c in contenders ?? Array.Empty<ContenderRecord>())
        {
            if (c == null || !ResourceManager.Ships.GetDesign(c.DesignName, out IShipDesign design))
                continue;
            total += design.BaseStrength;
            count += 1;
        }
        return count > 0 ? total / count : 0f;
    }

    static float TopStrength(ContenderRecord[] contenders)
    {
        float top = 0f;
        foreach (ContenderRecord c in contenders ?? Array.Empty<ContenderRecord>())
            if (c != null && ResourceManager.Ships.GetDesign(c.DesignName, out IShipDesign design))
                top = Math.Max(top, design.BaseStrength);
        return top;
    }

    public static float EvolutionStrengthCap()
    {
        IShipDesign tier3Player = ArenaFightScreen.AutoPickPlayerWarship(null, careerLevel: 7);
        float playerStrength = Math.Max(100f, tier3Player?.BaseStrength ?? 100f);
        return playerStrength * EvolutionPlayerCapMultiplier;
    }

    static EcosystemDiversityRow BuildDiversityRow(int season, ContenderRecord[] contenders)
    {
        var roleCounts = RoleCounts(contenders)
            .Where(kv => kv.Value > 0)
            .OrderBy(kv => RoleIndex(kv.Key))
            .ThenBy(kv => kv.Key.ToString(), StringComparer.Ordinal)
            .ToArray();
        string roleSpread = string.Join(",", roleCounts.Select(kv =>
            $"{kv.Key.ToString().ToUpperInvariant()}:{kv.Value}"));
        var dominantRole = roleCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => RoleIndex(kv.Key))
            .FirstOrDefault();

        var dominantDesign = (contenders ?? Array.Empty<ContenderRecord>())
            .Where(c => c != null && c.DesignName.NotEmpty())
            .GroupBy(c => c.DesignName, StringComparer.Ordinal)
            .Select(g => new { Design = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Design, StringComparer.Ordinal)
            .FirstOrDefault();

        return new EcosystemDiversityRow(
            season,
            roleCounts.Length,
            dominantRole.Key.ToString().ToUpperInvariant(),
            dominantRole.Value,
            dominantDesign?.Design ?? "",
            dominantDesign?.Count ?? 0,
            roleSpread);
    }

    static int CountBaselineWins(ContenderRecord[] contenders, IShipDesign baseline, ulong seed)
    {
        int wins = 0;
        int i = 0;
        foreach (ContenderRecord c in contenders ?? Array.Empty<ContenderRecord>())
        {
            if (c == null || c.DesignName.IsEmpty())
                continue;
            FairDuelResult duel = CareerLadder.FairDuel(c.DesignName, baseline.Name, seed + (ulong)i++);
            if (string.Equals(duel.WinnerDesignName, c.DesignName, StringComparison.Ordinal))
                wins += 1;
        }
        return wins;
    }

    public static string WriteReport(LivingEcosystemReport report, string outputDir)
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

    public static string ToJson(LivingEcosystemReport report)
    {
        string rows = string.Join(",\n    ", report.Contenders.Select(c =>
            $"{{\"name\":{J(c.Name)},\"design\":{J(c.DesignName)},\"role\":{J(c.RoleClass)}," +
            $"\"rating\":{c.Rating},\"wins\":{c.Wins},\"losses\":{c.Losses},\"seasons\":{c.Seasons}," +
            $"\"xp\":{F(c.Experience)},\"level\":{c.Level},\"evolutions\":{c.Evolutions}}}"));
        string diversity = string.Join(",\n    ", report.Diversity.Select(d =>
            $"{{\"season\":{d.Season},\"distinctRoles\":{d.DistinctRoles}," +
            $"\"dominantRole\":{J(d.DominantRole)},\"dominantRoleCount\":{d.DominantRoleCount}," +
            $"\"dominantDesign\":{J(d.DominantDesign)},\"dominantDesignCount\":{d.DominantDesignCount}," +
            $"\"roleSpread\":{J(d.RoleSpread)}}}"));

        return "{\n" +
               "  \"experiment\": \"ARENA LIVING CONTENDER ECOSYSTEM: deterministic seasons with contender careers, retirements, rookies, and mutation-by-duel evolution\",\n" +
               $"  \"seed\": {report.Seed},\n" +
               $"  \"seasons\": {report.Seasons},\n" +
               $"  \"rosterSize\": {report.RosterSize},\n" +
               $"  \"baselineDesign\": {J(report.BaselineDesign)},\n" +
               $"  \"initialAverageStrength\": {F(report.InitialAverageStrength)},\n" +
               $"  \"finalAverageStrength\": {F(report.FinalAverageStrength)},\n" +
               $"  \"initialTopStrength\": {F(report.InitialTopStrength)},\n" +
               $"  \"finalTopStrength\": {F(report.FinalTopStrength)},\n" +
               $"  \"evolutionStrengthCap\": {F(report.EvolutionStrengthCap)},\n" +
               $"  \"initialBaselineWins\": {report.InitialBaselineWins},\n" +
               $"  \"finalBaselineWins\": {report.FinalBaselineWins},\n" +
               $"  \"ratingChurn\": {report.RatingChurn},\n" +
               $"  \"retirements\": {report.Retirements},\n" +
               $"  \"rookies\": {report.Rookies},\n" +
               $"  \"evolutions\": {report.Evolutions},\n" +
               $"  \"survivors\": {report.Survivors},\n" +
               $"  \"verdict\": {J(report.Verdict)},\n" +
               $"  \"diversityBySeason\": [\n    {diversity}\n  ],\n" +
               $"  \"contenders\": [\n    {rows}\n  ]\n" +
               "}\n";
    }

    static string J(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

public sealed class LivingEcosystemReport
{
    public readonly int Seasons;
    public readonly int RosterSize;
    public readonly ulong Seed;
    public readonly string BaselineDesign;
    public readonly float InitialAverageStrength;
    public readonly float FinalAverageStrength;
    public readonly float InitialTopStrength;
    public readonly float FinalTopStrength;
    public readonly float EvolutionStrengthCap;
    public readonly int InitialBaselineWins;
    public readonly int FinalBaselineWins;
    public readonly int RatingChurn;
    public readonly int Retirements;
    public readonly int Rookies;
    public readonly int Evolutions;
    public readonly int Survivors;
    public readonly ContenderCareerRow[] Contenders;
    public readonly EcosystemDiversityRow[] Diversity;
    public readonly string Verdict;

    public LivingEcosystemReport(int seasons, int rosterSize, ulong seed, string baselineDesign,
        float initialAverageStrength, float finalAverageStrength, int initialBaselineWins,
        int finalBaselineWins, float initialTopStrength, float finalTopStrength, float evolutionStrengthCap,
        int ratingChurn, int retirements, int rookies, int evolutions, int survivors,
        ContenderCareerRow[] contenders, EcosystemDiversityRow[] diversity, string verdict)
    {
        Seasons = seasons;
        RosterSize = rosterSize;
        Seed = seed;
        BaselineDesign = baselineDesign;
        InitialAverageStrength = initialAverageStrength;
        FinalAverageStrength = finalAverageStrength;
        InitialTopStrength = initialTopStrength;
        FinalTopStrength = finalTopStrength;
        EvolutionStrengthCap = evolutionStrengthCap;
        InitialBaselineWins = initialBaselineWins;
        FinalBaselineWins = finalBaselineWins;
        RatingChurn = ratingChurn;
        Retirements = retirements;
        Rookies = rookies;
        Evolutions = evolutions;
        Survivors = survivors;
        Contenders = contenders;
        Diversity = diversity ?? Array.Empty<EcosystemDiversityRow>();
        Verdict = verdict;
    }
}

public readonly struct EcosystemDiversityRow
{
    public readonly int Season;
    public readonly int DistinctRoles;
    public readonly string DominantRole;
    public readonly int DominantRoleCount;
    public readonly string DominantDesign;
    public readonly int DominantDesignCount;
    public readonly string RoleSpread;

    public EcosystemDiversityRow(int season, int distinctRoles, string dominantRole, int dominantRoleCount,
        string dominantDesign, int dominantDesignCount, string roleSpread)
    {
        Season = season;
        DistinctRoles = distinctRoles;
        DominantRole = dominantRole ?? "";
        DominantRoleCount = dominantRoleCount;
        DominantDesign = dominantDesign ?? "";
        DominantDesignCount = dominantDesignCount;
        RoleSpread = roleSpread ?? "";
    }
}

public readonly struct ContenderCareerRow
{
    public readonly string Name;
    public readonly string DesignName;
    public readonly string RoleClass;
    public readonly int Rating;
    public readonly int Wins;
    public readonly int Losses;
    public readonly int Seasons;
    public readonly float Experience;
    public readonly int Level;
    public readonly int Evolutions;

    public ContenderCareerRow(string name, string designName, string roleClass, int rating, int wins,
        int losses, int seasons, float experience, int level, int evolutions)
    {
        Name = name;
        DesignName = designName;
        RoleClass = roleClass;
        Rating = rating;
        Wins = wins;
        Losses = losses;
        Seasons = seasons;
        Experience = experience;
        Level = level;
        Evolutions = evolutions;
    }
}

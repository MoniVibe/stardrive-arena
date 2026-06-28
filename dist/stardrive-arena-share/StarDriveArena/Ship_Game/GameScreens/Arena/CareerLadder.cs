using System;
using System.Collections.Generic;
using System.Linq;
using SDUtils;
using Ship_Game.AI;
using Ship_Game.Gameplay;
using Ship_Game.GameScreens.NewGame;
using Ship_Game.Ships;
using Ship_Game.Universe;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// Production ladder logic for the arena career: deterministic contender seeding and a
/// headless duel service the ladder, tests, and later challenge UI can all share.
/// </summary>
public static class CareerLadder
{
    public const int TargetRosterSize = 24;
    public const int MaxTeamDuelSize = 100;
    public const float MaxComparableContenderStrengthRatio = 5.0f;
    const int RatingK = 24;
    const int DuelTicks = 9000;
    const float DuelSpawnOffset = 6000f;
    const float CombatEvidenceEpsilon = 0.001f;
    static readonly FixedSimTime SimStep = new(1f / 60f);

    static readonly RoleName[] LadderRoles =
    {
        RoleName.fighter, RoleName.gunboat, RoleName.corvette, RoleName.frigate, RoleName.destroyer,
        RoleName.cruiser, RoleName.battleship, RoleName.capital,
    };

    static readonly string[] Names =
    {
        "Rook", "Vesper", "Kade", "Mira", "Sable", "Orion", "Nyx", "Talon",
        "Iris", "Drake", "Nova", "Vale", "Hex", "Astra", "Knox", "Lyra",
        "Juno", "Caius", "Sol", "Riven", "Echo", "Vega", "Ash", "Onyx",
    };

    public static void EnsureContenders(ArenaCareer career)
    {
        if (career == null)
            return;
        if (career.Contenders == null || career.Contenders.Length == 0)
            career.Contenders = SeedContenders();
    }

    public static ContenderRecord[] SeedContenders(int targetCount = TargetRosterSize)
    {
        targetCount = Math.Max(1, targetCount);
        IShipDesign[] all = ResourceManager.Ships.Designs
            .Where(ArenaFightScreen.IsLegalCombatCraft)
            .OrderBy(d => d.Role)
            .ThenBy(d => d.BaseStrength)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        var picked = new List<IShipDesign>(Math.Min(targetCount, all.Length));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (RoleName role in LadderRoles)
        {
            IShipDesign[] bucket = all.Where(d => d.Role == role).ToArray();
            AddRolePicks(bucket, picked, seen, targetCount);
            if (picked.Count >= targetCount)
                break;
        }

        foreach (IShipDesign d in all)
        {
            if (picked.Count >= targetCount)
                break;
            AddUnique(d, picked, seen);
        }

        var contenders = new ContenderRecord[picked.Count];
        for (int i = 0; i < picked.Count; ++i)
        {
            IShipDesign d = picked[i];
            contenders[i] = new ContenderRecord(
                name: $"{Names[i % Names.Length]} of the {RoleLabel(d.Role)}",
                designName: d.Name,
                roleClass: RoleLabel(d.Role),
                rating: InitialRating(d));
        }
        return contenders;
    }

    static void AddRolePicks(IShipDesign[] bucket, List<IShipDesign> picked, HashSet<string> seen, int targetCount)
    {
        if (bucket.Length == 0)
            return;

        int[] indices =
        {
            0,
            (bucket.Length - 1) / 3,
            (bucket.Length - 1) * 2 / 3,
            bucket.Length - 1,
        };

        foreach (int idx in indices)
        {
            if (picked.Count >= targetCount)
                return;
            AddUnique(bucket[idx], picked, seen);
        }
    }

    static void AddUnique(IShipDesign design, List<IShipDesign> picked, HashSet<string> seen)
    {
        if (design != null && seen.Add(design.Name))
            picked.Add(design);
    }

    static int InitialRating(IShipDesign design)
        => Math.Max(100, (int)Math.Round(Math.Sqrt(Math.Max(100f, design.BaseStrength)) * 10f));

    static string RoleLabel(RoleName role)
        => role.ToString().ToUpperInvariant();

    public static LadderRoundResult[] RunLadderRound(ArenaCareer career, ulong seed)
    {
        EnsureContenders(career);
        ContenderRecord[] contenders = career?.Contenders ?? Empty<ContenderRecord>.Array;
        if (contenders.Length < 2)
            return Empty<LadderRoundResult>.Array;

        ContenderRecord[] standings = contenders
            .Where(c => c != null && c.DesignName.NotEmpty())
            .OrderByDescending(c => c.Rating)
            .ThenBy(c => c.RoleClass, StringComparer.Ordinal)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToArray();

        var pairings = BuildComparablePairings(standings);
        var results = new List<LadderRoundResult>(pairings.Length);
        for (int i = 0; i < pairings.Length; ++i)
        {
            ContenderRecord a = pairings[i].A;
            ContenderRecord b = pairings[i].B;
            IShipDesign designA = pairings[i].DesignA;
            IShipDesign designB = pairings[i].DesignB;
            FairDuelResult duel = FairDuel(a.DesignName, b.DesignName, seed + (ulong)i);
            bool aWon = string.Equals(duel.WinnerDesignName, a.DesignName, StringComparison.Ordinal);
            int ratingABefore = a.Rating;
            int ratingBBefore = b.Rating;
            ApplyResult(a, b, aWon);
            RecordRivalry(a, b, aWon);
            ContenderRecord winner = aWon ? a : b;
            ContenderRecord loser = aWon ? b : a;
            int winnerRatingBefore = aWon ? ratingABefore : ratingBBefore;
            int loserRatingBefore = aWon ? ratingBBefore : ratingABefore;
            if (winnerRatingBefore < loserRatingBefore)
            {
                career.AddChronicleEvent("rival_upset", $"{winner.Name}>{loser.Name}",
                    $"{winner.Name} upset {loser.Name}.", seed + (ulong)i);
            }
            results.Add(new LadderRoundResult(a.Name, b.Name, duel.WinnerDesignName, a.Rating, b.Rating,
                designA?.Name, designB?.Name, ContenderStrengthRatio(designA, designB),
                ArenaFightScreen.CombatTierForDesign(designA), ArenaFightScreen.CombatTierForDesign(designB)));
        }

        return results.ToArray();
    }

    public static bool TryPickComparableContenderPair(ArenaCareer career, ulong seed,
        out ContenderRecord a, out ContenderRecord b, out IShipDesign designA, out IShipDesign designB)
    {
        EnsureContenders(career);
        var valid = ValidPairCandidates(career?.Contenders)
            .OrderBy(v => v.Contender.Name, StringComparer.Ordinal)
            .ThenBy(v => v.Design.Name, StringComparer.Ordinal)
            .ToList();

        a = b = null;
        designA = designB = null;
        if (valid.Count < 2)
            return false;

        int first = (int)(Mix(seed) % (ulong)valid.Count);
        PairCandidate anchor = valid[first];
        valid.RemoveAt(first);
        int second = BestOpponentIndex(anchor, valid);
        PairCandidate opponent = valid[second];

        a = anchor.Contender;
        designA = anchor.Design;
        b = opponent.Contender;
        designB = opponent.Design;
        return true;
    }

    public static bool AreComparableContenderDesigns(IShipDesign a, IShipDesign b)
    {
        int tierA = ArenaFightScreen.CombatTierForDesign(a);
        int tierB = ArenaFightScreen.CombatTierForDesign(b);
        if (tierA == 0 || tierB == 0 || Math.Abs(tierA - tierB) > 1)
            return false;
        return ContenderStrengthRatio(a, b) <= MaxComparableContenderStrengthRatio;
    }

    public static float ContenderStrengthRatio(IShipDesign a, IShipDesign b)
    {
        float sa = Math.Max(1f, a?.BaseStrength ?? 1f);
        float sb = Math.Max(1f, b?.BaseStrength ?? 1f);
        return Math.Max(sa, sb) / Math.Max(1f, Math.Min(sa, sb));
    }

    static (ContenderRecord A, ContenderRecord B, IShipDesign DesignA, IShipDesign DesignB)[] BuildComparablePairings(
        ContenderRecord[] standings)
    {
        var remaining = ValidPairCandidates(standings).ToList();
        var pairings = new List<(ContenderRecord, ContenderRecord, IShipDesign, IShipDesign)>(remaining.Count / 2);
        while (remaining.Count >= 2)
        {
            PairCandidate anchor = remaining[0];
            remaining.RemoveAt(0);
            int opponentIndex = BestOpponentIndex(anchor, remaining);
            PairCandidate opponent = remaining[opponentIndex];
            remaining.RemoveAt(opponentIndex);
            pairings.Add((anchor.Contender, opponent.Contender, anchor.Design, opponent.Design));
        }
        return pairings.ToArray();
    }

    static IEnumerable<PairCandidate> ValidPairCandidates(IEnumerable<ContenderRecord> contenders)
    {
        foreach (ContenderRecord c in contenders ?? Empty<ContenderRecord>.Array)
        {
            if (c == null || c.DesignName.IsEmpty())
                continue;
            if (!ResourceManager.Ships.GetDesign(c.DesignName, out IShipDesign design)
                || !ArenaFightScreen.IsLegalCombatCraft(design))
                continue;
            yield return new PairCandidate(c, design);
        }
    }

    static int BestOpponentIndex(PairCandidate anchor, List<PairCandidate> candidates)
    {
        bool hasComparable = candidates.Any(c => AreComparableContenderDesigns(anchor.Design, c.Design));
        int best = 0;
        double bestDistance = double.MaxValue;
        int bestTierGap = int.MaxValue;
        string bestName = "";
        for (int i = 0; i < candidates.Count; ++i)
        {
            PairCandidate candidate = candidates[i];
            bool comparable = AreComparableContenderDesigns(anchor.Design, candidate.Design);
            if (hasComparable && !comparable)
                continue;

            double distance = Math.Abs(Math.Log(ContenderStrengthRatio(anchor.Design, candidate.Design)));
            int tierGap = Math.Abs(ArenaFightScreen.CombatTierForDesign(anchor.Design)
                                 - ArenaFightScreen.CombatTierForDesign(candidate.Design));
            string name = candidate.Contender?.Name ?? "";
            if (distance < bestDistance
                || (Math.Abs(distance - bestDistance) < 0.000001 && tierGap < bestTierGap)
                || (Math.Abs(distance - bestDistance) < 0.000001 && tierGap == bestTierGap
                    && string.CompareOrdinal(name, bestName) < 0))
            {
                best = i;
                bestDistance = distance;
                bestTierGap = tierGap;
                bestName = name;
            }
        }
        return best;
    }

    readonly struct PairCandidate
    {
        public readonly ContenderRecord Contender;
        public readonly IShipDesign Design;

        public PairCandidate(ContenderRecord contender, IShipDesign design)
        {
            Contender = contender;
            Design = design;
        }
    }

    static ulong Mix(ulong x)
    {
        x ^= x >> 33;
        x *= 0xff51afd7ed558ccdUL;
        x ^= x >> 33;
        x *= 0xc4ceb9fe1a85ec53UL;
        x ^= x >> 33;
        return x;
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

    static void RecordRivalry(ContenderRecord a, ContenderRecord b, bool aWon)
    {
        if (a == null || b == null)
            return;

        a.RivalName = b.Name ?? "";
        b.RivalName = a.Name ?? "";
        if (aWon)
        {
            a.RivalWins += 1;
            b.RivalLosses += 1;
        }
        else
        {
            b.RivalWins += 1;
            a.RivalLosses += 1;
        }
    }

    public static string RivalryLine(ContenderRecord contender)
    {
        if (contender == null || contender.RivalName.IsEmpty())
            return "";
        if (contender.RivalWins > contender.RivalLosses)
            return $"{contender.Name}: {contender.RivalName} keeps feeding my legend.";
        if (contender.RivalLosses > contender.RivalWins)
            return $"{contender.Name}: {contender.RivalName}, the grudge is not over.";
        return $"{contender.Name}: {contender.RivalName} and I are not finished.";
    }

    static int RatingDelta(int winnerRating, int loserRating)
    {
        double expected = 1.0 / (1.0 + Math.Pow(10.0, (loserRating - winnerRating) / 400.0));
        return Math.Max(1, (int)Math.Round(RatingK * (1.0 - expected)));
    }

    public static DuelResult SimulateDuel(string designA, string designB, ulong seed)
        => SimulateDuel(designA, designB, seed, DuelTicks);

    public static DuelResult SimulateDuel(string designA, string designB, ulong seed, int duelTicks)
        => SimulateDuel(designA, designB, seed, duelTicks, DuelSpawnOffset);

    public static DuelResult SimulateDuel(string designA, string designB, ulong seed, int duelTicks, float spawnOffset)
    {
        if (designA.IsEmpty())
            throw new ArgumentException("Design A is required.", nameof(designA));
        if (designB.IsEmpty())
            throw new ArgumentException("Design B is required.", nameof(designB));
        if (!ResourceManager.Ships.GetDesign(designA, out IShipDesign a) || !ArenaFightScreen.IsLegalCombatCraft(a))
            throw new ArgumentException($"Design A is not a legal arena warship: {designA}", nameof(designA));
        if (!ResourceManager.Ships.GetDesign(designB, out IShipDesign b) || !ArenaFightScreen.IsLegalCombatCraft(b))
            throw new ArgumentException($"Design B is not a legal arena warship: {designB}", nameof(designB));

        return SimulateDuel(a, b, seed, duelTicks, spawnOffset);
    }

    public static DuelResult SimulateDuel(IShipDesign designA, IShipDesign designB, ulong seed)
        => SimulateDuel(designA, designB, seed, DuelTicks);

    public static DuelResult SimulateDuel(IShipDesign designA, IShipDesign designB, ulong seed, int duelTicks)
        => SimulateDuel(designA, designB, seed, duelTicks, DuelSpawnOffset);

    public static DuelResult SimulateDuel(IShipDesign designA, IShipDesign designB, ulong seed, int duelTicks, float spawnOffset)
        => SimulateDuel(designA, designB, seed, duelTicks, spawnOffset, applyArenaAntiKite: true);

    public static DuelResult SimulateDuelForBalanceProbe(IShipDesign designA, IShipDesign designB,
        ulong seed, int duelTicks, bool applyArenaAntiKite)
        => SimulateDuel(designA, designB, seed, duelTicks, DuelSpawnOffset, applyArenaAntiKite);

    static DuelResult SimulateDuel(IShipDesign designA, IShipDesign designB, ulong seed,
        int duelTicks, float spawnOffset, bool applyArenaAntiKite)
    {
        if (designA == null) throw new ArgumentNullException(nameof(designA));
        if (designB == null) throw new ArgumentNullException(nameof(designB));
        if (duelTicks < 1) throw new ArgumentOutOfRangeException(nameof(duelTicks));
        if (spawnOffset <= 0f) throw new ArgumentOutOfRangeException(nameof(spawnOffset));

        UniverseScreen screen = null;
        try
        {
            screen = new UniverseScreen(new UniverseParams(), universeRadius: 2_000_000f);
            UniverseState us = screen.UState;
            ArenaEngineCapabilities.TryEnableSeededRng(us, seed);
            us.P.GravityWellRange = 0;
            ArenaEngineCapabilities.TrySetParallelUpdate(us.Objects, false);
            us.Paused = false;
            us.ViewState = UniverseScreen.UnivScreenState.GalaxyView;

            Empire empireA = us.CreateEmpire(ResourceManager.MajorRaces[0], isPlayer: true);
            Empire empireB = us.CreateEmpire(ResourceManager.MajorRaces[1], isPlayer: false);
            // Re-apply after empire creation as well: the optional seeded engine hook affects
            // existing empires, and the duel's empires are created after the initial universe setup.
            ArenaEngineCapabilities.TryEnableSeededRng(us, seed);

            Empire.SetRelationsAsKnown(empireA, empireB);
            if (!empireA.IsAtWarWith(empireB))
                empireA.AI.DeclareWarOn(empireB, WarType.ImperialistWar);

            Ship shipA = Ship.CreateShipAtPoint(us, designA.Name, empireA, new Vector2(-spawnOffset, 0f));
            Ship shipB = Ship.CreateShipAtPoint(us, designB.Name, empireB, new Vector2(+spawnOffset, 0f));
            if (shipA == null || shipB == null)
                throw new InvalidOperationException($"Failed to spawn duel ships: '{designA.Name}' vs '{designB.Name}'.");

            if (applyArenaAntiKite)
            {
                ArenaCombatTuning.ApplyAntiKiteDefaults(shipA);
                ArenaCombatTuning.ApplyAntiKiteDefaults(shipB);
            }
            shipA.SensorRange = 400000f;
            shipB.SensorRange = 400000f;
            float initialHealthA = shipA.Health;
            float initialHealthB = shipB.Health;

            Engage(shipA, shipB);
            int ticks = 0;
            for (; ticks < duelTicks; ++ticks)
            {
                us.Objects.Update(SimStep);
                if (!shipA.IsAlive || !shipB.IsAlive)
                    break;
                if (ticks % 600 == 599)
                    Engage(shipA, shipB);
            }

            float finalStrengthA = shipA.IsAlive ? shipA.GetStrength() : 0f;
            float finalStrengthB = shipB.IsAlive ? shipB.GetStrength() : 0f;
            float damageToA = Math.Max(0f, initialHealthA - shipA.Health);
            float damageToB = Math.Max(0f, initialHealthB - shipB.Health);
            float retainedHealthA = initialHealthA > 0f ? Math.Max(0f, shipA.Health) / initialHealthA : 0f;
            float retainedHealthB = initialHealthB > 0f ? Math.Max(0f, shipB.Health) / initialHealthB : 0f;
            string winner = PickWinnerFromEvidence(designA, designB, shipA.IsAlive, shipB.IsAlive,
                finalStrengthA, finalStrengthB, damageToA, damageToB, retainedHealthA, retainedHealthB);
            return new DuelResult(
                designA.Name, designB.Name, winner, ticks + 1,
                shipA.IsAlive, shipB.IsAlive,
                finalStrengthA, finalStrengthB, damageToA, damageToB,
                retainedHealthA, retainedHealthB);
        }
        finally
        {
            screen?.Dispose();
        }
    }

    static void Engage(Ship a, Ship b)
    {
        if (a.IsAlive && b.IsAlive)
        {
            a.AI.OrderAttackSpecificTarget(b);
            b.AI.OrderAttackSpecificTarget(a);
        }
    }

    public static FairDuelResult FairDuel(string designA, string designB, ulong seed)
        => FairDuel(designA, designB, seed, DuelTicks);

    public static FairDuelResult FairDuel(string designA, string designB, ulong seed, int duelTicks)
        => FairDuel(designA, designB, seed, duelTicks, DuelSpawnOffset);

    public static FairDuelResult FairDuel(string designA, string designB, ulong seed, int duelTicks, float spawnOffset)
    {
        if (designA.IsEmpty())
            throw new ArgumentException("Design A is required.", nameof(designA));
        if (designB.IsEmpty())
            throw new ArgumentException("Design B is required.", nameof(designB));
        if (!ResourceManager.Ships.GetDesign(designA, out IShipDesign a) || !ArenaFightScreen.IsLegalCombatCraft(a))
            throw new ArgumentException($"Design A is not a legal arena warship: {designA}", nameof(designA));
        if (!ResourceManager.Ships.GetDesign(designB, out IShipDesign b) || !ArenaFightScreen.IsLegalCombatCraft(b))
            throw new ArgumentException($"Design B is not a legal arena warship: {designB}", nameof(designB));

        return FairDuel(a, b, seed, duelTicks, spawnOffset);
    }

    public static FairDuelResult FairDuel(IShipDesign designA, IShipDesign designB, ulong seed)
        => FairDuel(designA, designB, seed, DuelTicks);

    public static FairDuelResult FairDuel(IShipDesign designA, IShipDesign designB, ulong seed, int duelTicks)
        => FairDuel(designA, designB, seed, duelTicks, DuelSpawnOffset);

    public static FairDuelResult FairDuel(IShipDesign designA, IShipDesign designB, ulong seed, int duelTicks, float spawnOffset)
    {
        if (designA == null) throw new ArgumentNullException(nameof(designA));
        if (designB == null) throw new ArgumentNullException(nameof(designB));
        if (duelTicks < 1) throw new ArgumentOutOfRangeException(nameof(duelTicks));
        if (spawnOffset <= 0f) throw new ArgumentOutOfRangeException(nameof(spawnOffset));

        DuelResult forward = SimulateDuel(designA, designB, seed, duelTicks, spawnOffset, applyArenaAntiKite: false);
        DuelResult swapped = SimulateDuel(designB, designA, seed ^ 0x5EED_5EEDul, duelTicks, spawnOffset,
            applyArenaAntiKite: false);
        int winsA = 0;
        int winsB = 0;
        if (string.Equals(forward.WinnerDesignName, designA.Name, StringComparison.Ordinal)) ++winsA; else ++winsB;
        if (string.Equals(swapped.WinnerDesignName, designA.Name, StringComparison.Ordinal)) ++winsA; else ++winsB;

        float damageByA = forward.DamageToB + swapped.DamageToA;
        float damageByB = forward.DamageToA + swapped.DamageToB;
        float retainedStrengthA = forward.FinalStrengthA + swapped.FinalStrengthB;
        float retainedStrengthB = forward.FinalStrengthB + swapped.FinalStrengthA;
        string winner = PickFairDuelWinner(designA, designB, winsA, winsB, damageByA, damageByB,
            retainedStrengthA, retainedStrengthB);

        return new FairDuelResult(designA.Name, designB.Name, winner, forward, swapped,
            winsA, winsB, damageByA, damageByB, retainedStrengthA, retainedStrengthB);
    }

    public static TeamDuelResult SimulateTeamDuel(string[] designNamesA, string[] designNamesB, ulong seed)
        => SimulateTeamDuel(designNamesA, designNamesB, seed, DuelTicks);

    public static TeamDuelResult SimulateTeamDuel(string[] designNamesA, string[] designNamesB, ulong seed, int duelTicks)
        => SimulateTeamDuel(designNamesA, designNamesB, seed, duelTicks, DuelSpawnOffset);

    public static TeamDuelResult SimulateTeamDuel(string[] designNamesA, string[] designNamesB, ulong seed,
        int duelTicks, float spawnOffset)
    {
        TeamMemberSpec[] a = ResolveDesignTeam("Team A", designNamesA);
        TeamMemberSpec[] b = ResolveDesignTeam("Team B", designNamesB);
        return SimulateTeamDuel("Team A", "Team B", a, b, seed, duelTicks, spawnOffset);
    }

    public static TeamDuelResult SimulateTeamDuel(ArenaCareer career, ArenaTeam teamA, ArenaTeam teamB, ulong seed)
        => SimulateTeamDuel(career, teamA, teamB, seed, DuelTicks);

    public static TeamDuelResult SimulateTeamDuel(ArenaCareer career, ArenaTeam teamA, ArenaTeam teamB,
        ulong seed, int duelTicks)
        => SimulateTeamDuel(career, teamA, teamB, seed, duelTicks, DuelSpawnOffset);

    public static TeamDuelResult SimulateTeamDuel(ArenaCareer career, ArenaTeam teamA, ArenaTeam teamB,
        ulong seed, int duelTicks, float spawnOffset)
    {
        TeamMemberSpec[] a = ResolveCareerTeam(career, teamA);
        TeamMemberSpec[] b = ResolveCareerTeam(career, teamB);
        return SimulateTeamDuel(teamA.Name, teamB.Name, a, b, seed, duelTicks, spawnOffset);
    }

    public static FairTeamDuelResult FairTeamDuel(ArenaCareer career, ArenaTeam teamA, ArenaTeam teamB, ulong seed)
        => FairTeamDuel(career, teamA, teamB, seed, DuelTicks);

    public static FairTeamDuelResult FairTeamDuel(ArenaCareer career, ArenaTeam teamA, ArenaTeam teamB,
        ulong seed, int duelTicks)
        => FairTeamDuel(career, teamA, teamB, seed, duelTicks, DuelSpawnOffset);

    public static FairTeamDuelResult FairTeamDuel(ArenaCareer career, ArenaTeam teamA, ArenaTeam teamB,
        ulong seed, int duelTicks, float spawnOffset)
    {
        TeamDuelResult forward = SimulateTeamDuel(career, teamA, teamB, seed, duelTicks, spawnOffset);
        TeamDuelResult swapped = SimulateTeamDuel(career, teamB, teamA, seed ^ 0x5EED_7EA1ul, duelTicks, spawnOffset);

        int winsA = string.Equals(forward.WinnerTeamName, teamA.Name, StringComparison.Ordinal) ? 1 : 0;
        int winsB = 1 - winsA;
        if (string.Equals(swapped.WinnerTeamName, teamA.Name, StringComparison.Ordinal)) ++winsA; else ++winsB;

        int survivorsA = forward.SurvivorsFor(teamA.Name) + swapped.SurvivorsFor(teamA.Name);
        int survivorsB = forward.SurvivorsFor(teamB.Name) + swapped.SurvivorsFor(teamB.Name);
        float retainedA = forward.RetainedStrengthFor(teamA.Name) + swapped.RetainedStrengthFor(teamA.Name);
        float retainedB = forward.RetainedStrengthFor(teamB.Name) + swapped.RetainedStrengthFor(teamB.Name);
        float damageByA = forward.DamageByTeam(teamA.Name) + swapped.DamageByTeam(teamA.Name);
        float damageByB = forward.DamageByTeam(teamB.Name) + swapped.DamageByTeam(teamB.Name);
        string winner = PickFairTeamWinner(teamA.Name, teamB.Name, winsA, winsB, survivorsA, survivorsB,
            retainedA, retainedB, damageByA, damageByB, TeamBaseStrength(career, teamA), TeamBaseStrength(career, teamB));

        return new FairTeamDuelResult(teamA.Name, teamB.Name, teamA.Size, winner, forward, swapped,
            winsA, winsB, survivorsA, survivorsB, retainedA, retainedB, damageByA, damageByB);
    }

    static TeamDuelResult SimulateTeamDuel(string teamAName, string teamBName,
        TeamMemberSpec[] teamA, TeamMemberSpec[] teamB, ulong seed, int duelTicks, float spawnOffset)
    {
        ValidateTeamSpecs(teamA, teamB, duelTicks, spawnOffset);

        UniverseScreen screen = null;
        try
        {
            screen = new UniverseScreen(new UniverseParams(), universeRadius: 2_000_000f);
            UniverseState us = screen.UState;
            ArenaEngineCapabilities.TryEnableSeededRng(us, seed);
            us.P.GravityWellRange = 0;
            ArenaEngineCapabilities.TrySetParallelUpdate(us.Objects, false);
            us.Paused = false;
            us.ViewState = UniverseScreen.UnivScreenState.GalaxyView;

            Empire empireA = us.CreateEmpire(ResourceManager.MajorRaces[0], isPlayer: true);
            Empire empireB = us.CreateEmpire(ResourceManager.MajorRaces[1], isPlayer: false);
            ArenaEngineCapabilities.TryEnableSeededRng(us, seed);

            Empire.SetRelationsAsKnown(empireA, empireB);
            if (!empireA.IsAtWarWith(empireB))
                empireA.AI.DeclareWarOn(empireB, WarType.ImperialistWar);

            var shipsA = SpawnTeam(us, empireA, teamA, -spawnOffset);
            var shipsB = SpawnTeam(us, empireB, teamB, +spawnOffset);
            float[] initialA = shipsA.Select(s => s.Health).ToArray();
            float[] initialB = shipsB.Select(s => s.Health).ToArray();

            EngageTeams(shipsA, shipsB);
            int ticks = 0;
            for (; ticks < duelTicks; ++ticks)
            {
                us.Objects.Update(SimStep);
                if (AliveCount(shipsA) == 0 || AliveCount(shipsB) == 0)
                    break;
                if (ticks % 600 == 599)
                    EngageTeams(shipsA, shipsB);
            }

            TeamMemberDuelState[] membersA = BuildMemberStates(teamA, shipsA, initialA);
            TeamMemberDuelState[] membersB = BuildMemberStates(teamB, shipsB, initialB);
            int survivorsA = membersA.Count(m => m.Alive);
            int survivorsB = membersB.Count(m => m.Alive);
            float retainedA = membersA.Sum(m => m.RetainedStrength);
            float retainedB = membersB.Sum(m => m.RetainedStrength);
            float damageToA = membersA.Sum(m => m.DamageTaken);
            float damageToB = membersB.Sum(m => m.DamageTaken);
            string winner = PickTeamWinner(teamAName, teamBName, survivorsA, survivorsB,
                retainedA, retainedB, damageToB, damageToA, TeamBaseStrength(teamA), TeamBaseStrength(teamB));

            return new TeamDuelResult(teamAName, teamBName, teamA.Length, winner, ticks + 1,
                survivorsA, survivorsB, retainedA, retainedB, damageToB, damageToA, membersA, membersB);
        }
        finally
        {
            screen?.Dispose();
        }
    }

    static List<Ship> SpawnTeam(UniverseState us, Empire empire, TeamMemberSpec[] team, float x)
    {
        var ships = new List<Ship>(team.Length);
        float rowSpan = 900f;
        for (int i = 0; i < team.Length; ++i)
        {
            float y = (i - (team.Length - 1) / 2f) * rowSpan;
            Ship ship = Ship.CreateShipAtPoint(us, team[i].Design.Name, empire, new Vector2(x, y));
            if (ship == null)
                throw new InvalidOperationException($"Failed to spawn team ship: {team[i].Design.Name}");
            ArenaCombatTuning.ApplyAntiKiteDefaults(ship);
            ship.SensorRange = 400000f;
            ships.Add(ship);
        }
        return ships;
    }

    static void EngageTeams(List<Ship> shipsA, List<Ship> shipsB)
    {
        Order(shipsA, shipsB);
        Order(shipsB, shipsA);

        static void Order(List<Ship> attackers, List<Ship> targets)
        {
            foreach (Ship attacker in attackers)
            {
                if (attacker == null || !attacker.IsAlive)
                    continue;
                Ship nearest = null;
                float best = float.MaxValue;
                foreach (Ship target in targets)
                {
                    if (target == null || !target.IsAlive)
                        continue;
                    float dist = attacker.Position.SqDist(target.Position);
                    if (dist < best)
                    {
                        best = dist;
                        nearest = target;
                    }
                }
                if (nearest != null)
                    attacker.AI.OrderAttackSpecificTarget(nearest);
            }
        }
    }

    static TeamMemberDuelState[] BuildMemberStates(TeamMemberSpec[] specs, List<Ship> ships, float[] initialHealth)
    {
        var rows = new TeamMemberDuelState[specs.Length];
        for (int i = 0; i < specs.Length; ++i)
        {
            Ship ship = ships[i];
            bool alive = ship.IsAlive;
            float retained = alive ? ship.GetStrength() : 0f;
            float damage = Math.Max(0f, initialHealth[i] - Math.Max(0f, ship.Health));
            rows[i] = new TeamMemberDuelState(specs[i].MemberName, specs[i].Design.Name, alive, retained, damage);
        }
        return rows;
    }

    static int AliveCount(List<Ship> ships)
    {
        int alive = 0;
        foreach (Ship ship in ships)
            if (ship != null && ship.IsAlive)
                ++alive;
        return alive;
    }

    static TeamMemberSpec[] ResolveDesignTeam(string label, string[] designNames)
    {
        if (designNames == null)
            throw new ArgumentNullException(nameof(designNames));
        return designNames.Select((designName, i) =>
        {
            if (designName.IsEmpty())
                throw new ArgumentException($"{label} design {i} is empty.", nameof(designNames));
            if (!ResourceManager.Ships.GetDesign(designName, out IShipDesign design)
                || !ArenaFightScreen.IsLegalCombatCraft(design))
                throw new ArgumentException($"{label} design is not legal: {designName}", nameof(designNames));
            return new TeamMemberSpec($"{label} #{i + 1}", design);
        }).ToArray();
    }

    static TeamMemberSpec[] ResolveCareerTeam(ArenaCareer career, ArenaTeam team)
    {
        if (career == null) throw new ArgumentNullException(nameof(career));
        if (team == null) throw new ArgumentNullException(nameof(team));
        var specs = new List<TeamMemberSpec>();
        foreach (string memberName in team.Members ?? Empty<string>.Array)
        {
            ContenderRecord contender = (career.Contenders ?? Empty<ContenderRecord>.Array)
                .FirstOrDefault(c => c != null && string.Equals(c.Name, memberName, StringComparison.Ordinal));
            if (contender == null || contender.DesignName.IsEmpty())
                throw new ArgumentException($"Team '{team.Name}' has missing member '{memberName}'.", nameof(team));
            if (!ResourceManager.Ships.GetDesign(contender.DesignName, out IShipDesign design)
                || !ArenaFightScreen.IsLegalCombatCraft(design))
                throw new ArgumentException($"Team '{team.Name}' member '{memberName}' has illegal design '{contender.DesignName}'.", nameof(team));
            specs.Add(new TeamMemberSpec(contender.Name, design));
        }
        return specs.ToArray();
    }

    static void ValidateTeamSpecs(TeamMemberSpec[] teamA, TeamMemberSpec[] teamB, int duelTicks, float spawnOffset)
    {
        if (teamA == null || teamB == null)
            throw new ArgumentNullException(teamA == null ? nameof(teamA) : nameof(teamB));
        if (teamA.Length != teamB.Length)
            throw new ArgumentException("Team duels require equal team sizes.");
        if (teamA.Length < 1 || teamA.Length > MaxTeamDuelSize)
            throw new ArgumentOutOfRangeException(nameof(teamA), $"Team duel size must be 1..{MaxTeamDuelSize}.");
        if (duelTicks < 1) throw new ArgumentOutOfRangeException(nameof(duelTicks));
        if (spawnOffset <= 0f) throw new ArgumentOutOfRangeException(nameof(spawnOffset));
    }

    static string PickFairTeamWinner(string teamA, string teamB, int winsA, int winsB,
        int survivorsA, int survivorsB, float retainedA, float retainedB,
        float damageByA, float damageByB, float baseStrengthA, float baseStrengthB)
    {
        if (winsA != winsB)
            return winsA > winsB ? teamA : teamB;
        if (survivorsA != survivorsB)
            return survivorsA > survivorsB ? teamA : teamB;
        return PickTeamWinner(teamA, teamB, survivorsA, survivorsB,
            retainedA, retainedB, damageByA, damageByB, baseStrengthA, baseStrengthB);
    }

    static string PickTeamWinner(string teamA, string teamB, int survivorsA, int survivorsB,
        float retainedA, float retainedB, float damageByA, float damageByB,
        float baseStrengthA, float baseStrengthB)
    {
        if (survivorsA != survivorsB)
            return survivorsA > survivorsB ? teamA : teamB;
        if (Greater(retainedA, retainedB)) return teamA;
        if (Greater(retainedB, retainedA)) return teamB;
        if (Greater(damageByA, damageByB)) return teamA;
        if (Greater(damageByB, damageByA)) return teamB;
        if (baseStrengthA > baseStrengthB) return teamA;
        if (baseStrengthB > baseStrengthA) return teamB;
        return string.CompareOrdinal(teamA, teamB) <= 0 ? teamA : teamB;
    }

    static float TeamBaseStrength(ArenaCareer career, ArenaTeam team)
        => ResolveCareerTeam(career, team).Sum(s => s.Design.BaseStrength);

    static float TeamBaseStrength(TeamMemberSpec[] team)
        => team.Sum(s => s.Design.BaseStrength);

    readonly struct TeamMemberSpec
    {
        public readonly string MemberName;
        public readonly IShipDesign Design;

        public TeamMemberSpec(string memberName, IShipDesign design)
        {
            MemberName = memberName ?? "";
            Design = design;
        }
    }

    static string PickFairDuelWinner(IShipDesign designA, IShipDesign designB, int winsA, int winsB,
        float damageByA, float damageByB, float retainedStrengthA, float retainedStrengthB)
    {
        if (winsA != winsB)
            return winsA > winsB ? designA.Name : designB.Name;
        if (GreaterFairEvidence(damageByA, damageByB))
            return designA.Name;
        if (GreaterFairEvidence(damageByB, damageByA))
            return designB.Name;
        if (GreaterFairEvidence(retainedStrengthA, retainedStrengthB))
            return designA.Name;
        if (GreaterFairEvidence(retainedStrengthB, retainedStrengthA))
            return designB.Name;
        return FallbackByDesignStrength(designA, designB);
    }

    static string PickWinnerFromEvidence(IShipDesign designA, IShipDesign designB, bool aAlive, bool bAlive,
        float finalStrengthA, float finalStrengthB, float damageToA, float damageToB,
        float retainedHealthA, float retainedHealthB)
    {
        if (aAlive && !bAlive) return designA.Name;
        if (bAlive && !aAlive) return designB.Name;
        if (!aAlive && !bAlive) return FallbackByDesignStrength(designA, designB);

        float retainedDiff = Math.Abs(finalStrengthA - finalStrengthB);
        float decisiveMargin = Math.Max(1f, (finalStrengthA + finalStrengthB) * 0.05f);
        if (retainedDiff >= decisiveMargin)
            return finalStrengthA > finalStrengthB ? designA.Name : designB.Name;

        if (Greater(damageToB, damageToA)) return designA.Name;
        if (Greater(damageToA, damageToB)) return designB.Name;
        if (Greater(retainedHealthA, retainedHealthB)) return designA.Name;
        if (Greater(retainedHealthB, retainedHealthA)) return designB.Name;
        return FallbackByDesignStrength(designA, designB);
    }

    static bool Greater(float a, float b) => a > b + CombatEvidenceEpsilon;
    static bool GreaterFairEvidence(float a, float b)
        => a > b + Math.Max(CombatEvidenceEpsilon, (Math.Abs(a) + Math.Abs(b)) * 0.02f);

    static string FallbackByDesignStrength(IShipDesign designA, IShipDesign designB)
    {
        if (designA.BaseStrength > designB.BaseStrength) return designA.Name;
        if (designB.BaseStrength > designA.BaseStrength) return designB.Name;
        return string.CompareOrdinal(designA.Name, designB.Name) <= 0 ? designA.Name : designB.Name;
    }
}

public readonly struct DuelResult
{
    public readonly string DesignA;
    public readonly string DesignB;
    public readonly string WinnerDesignName;
    public readonly int TicksSimulated;
    public readonly bool AAlive;
    public readonly bool BAlive;
    public readonly float FinalStrengthA;
    public readonly float FinalStrengthB;
    public readonly float DamageToA;
    public readonly float DamageToB;
    public readonly float RetainedHealthFractionA;
    public readonly float RetainedHealthFractionB;

    public DuelResult(string designA, string designB, string winnerDesignName, int ticksSimulated,
        bool aAlive, bool bAlive, float finalStrengthA, float finalStrengthB, float damageToA, float damageToB,
        float retainedHealthFractionA, float retainedHealthFractionB)
    {
        DesignA = designA;
        DesignB = designB;
        WinnerDesignName = winnerDesignName;
        TicksSimulated = ticksSimulated;
        AAlive = aAlive;
        BAlive = bAlive;
        FinalStrengthA = finalStrengthA;
        FinalStrengthB = finalStrengthB;
        DamageToA = damageToA;
        DamageToB = damageToB;
        RetainedHealthFractionA = retainedHealthFractionA;
        RetainedHealthFractionB = retainedHealthFractionB;
    }
}

public readonly struct FairDuelResult
{
    public readonly string DesignA;
    public readonly string DesignB;
    public readonly string WinnerDesignName;
    public readonly DuelResult Forward;
    public readonly DuelResult Swapped;
    public readonly int WinsA;
    public readonly int WinsB;
    public readonly float DamageByA;
    public readonly float DamageByB;
    public readonly float RetainedStrengthA;
    public readonly float RetainedStrengthB;

    public FairDuelResult(string designA, string designB, string winnerDesignName,
        DuelResult forward, DuelResult swapped, int winsA, int winsB,
        float damageByA, float damageByB, float retainedStrengthA, float retainedStrengthB)
    {
        DesignA = designA;
        DesignB = designB;
        WinnerDesignName = winnerDesignName;
        Forward = forward;
        Swapped = swapped;
        WinsA = winsA;
        WinsB = winsB;
        DamageByA = damageByA;
        DamageByB = damageByB;
        RetainedStrengthA = retainedStrengthA;
        RetainedStrengthB = retainedStrengthB;
    }

    public int Games => WinsA + WinsB;
}

public readonly struct TeamMemberDuelState
{
    public readonly string MemberName;
    public readonly string DesignName;
    public readonly bool Alive;
    public readonly float RetainedStrength;
    public readonly float DamageTaken;

    public TeamMemberDuelState(string memberName, string designName, bool alive, float retainedStrength, float damageTaken)
    {
        MemberName = memberName ?? "";
        DesignName = designName ?? "";
        Alive = alive;
        RetainedStrength = retainedStrength;
        DamageTaken = damageTaken;
    }
}

public sealed class TeamDuelResult
{
    public readonly string TeamA;
    public readonly string TeamB;
    public readonly int TeamSize;
    public readonly string WinnerTeamName;
    public readonly int TicksSimulated;
    public readonly int SurvivorsA;
    public readonly int SurvivorsB;
    public readonly float RetainedStrengthA;
    public readonly float RetainedStrengthB;
    public readonly float DamageByA;
    public readonly float DamageByB;
    public readonly TeamMemberDuelState[] MembersA;
    public readonly TeamMemberDuelState[] MembersB;

    public TeamDuelResult(string teamA, string teamB, int teamSize, string winnerTeamName, int ticksSimulated,
        int survivorsA, int survivorsB, float retainedStrengthA, float retainedStrengthB,
        float damageByA, float damageByB, TeamMemberDuelState[] membersA, TeamMemberDuelState[] membersB)
    {
        TeamA = teamA ?? "";
        TeamB = teamB ?? "";
        TeamSize = Math.Max(1, teamSize);
        WinnerTeamName = winnerTeamName ?? "";
        TicksSimulated = ticksSimulated;
        SurvivorsA = survivorsA;
        SurvivorsB = survivorsB;
        RetainedStrengthA = retainedStrengthA;
        RetainedStrengthB = retainedStrengthB;
        DamageByA = damageByA;
        DamageByB = damageByB;
        MembersA = membersA ?? Array.Empty<TeamMemberDuelState>();
        MembersB = membersB ?? Array.Empty<TeamMemberDuelState>();
    }

    public int SurvivorsFor(string teamName)
        => string.Equals(teamName, TeamA, StringComparison.Ordinal) ? SurvivorsA
         : string.Equals(teamName, TeamB, StringComparison.Ordinal) ? SurvivorsB
         : 0;

    public float RetainedStrengthFor(string teamName)
        => string.Equals(teamName, TeamA, StringComparison.Ordinal) ? RetainedStrengthA
         : string.Equals(teamName, TeamB, StringComparison.Ordinal) ? RetainedStrengthB
         : 0f;

    public float DamageByTeam(string teamName)
        => string.Equals(teamName, TeamA, StringComparison.Ordinal) ? DamageByA
         : string.Equals(teamName, TeamB, StringComparison.Ordinal) ? DamageByB
         : 0f;

    public TeamMemberDuelState[] MembersFor(string teamName)
        => string.Equals(teamName, TeamA, StringComparison.Ordinal) ? MembersA
         : string.Equals(teamName, TeamB, StringComparison.Ordinal) ? MembersB
         : Array.Empty<TeamMemberDuelState>();

    public TeamMemberDuelState[] DownedLosers()
    {
        string loser = string.Equals(WinnerTeamName, TeamA, StringComparison.Ordinal) ? TeamB : TeamA;
        return MembersFor(loser).Where(m => !m.Alive).ToArray();
    }
}

public sealed class FairTeamDuelResult
{
    public readonly string TeamA;
    public readonly string TeamB;
    public readonly int TeamSize;
    public readonly string WinnerTeamName;
    public readonly TeamDuelResult Forward;
    public readonly TeamDuelResult Swapped;
    public readonly int WinsA;
    public readonly int WinsB;
    public readonly int SurvivorsA;
    public readonly int SurvivorsB;
    public readonly float RetainedStrengthA;
    public readonly float RetainedStrengthB;
    public readonly float DamageByA;
    public readonly float DamageByB;

    public FairTeamDuelResult(string teamA, string teamB, int teamSize, string winnerTeamName,
        TeamDuelResult forward, TeamDuelResult swapped, int winsA, int winsB,
        int survivorsA, int survivorsB, float retainedStrengthA, float retainedStrengthB,
        float damageByA, float damageByB)
    {
        TeamA = teamA ?? "";
        TeamB = teamB ?? "";
        TeamSize = Math.Max(1, teamSize);
        WinnerTeamName = winnerTeamName ?? "";
        Forward = forward;
        Swapped = swapped;
        WinsA = winsA;
        WinsB = winsB;
        SurvivorsA = survivorsA;
        SurvivorsB = survivorsB;
        RetainedStrengthA = retainedStrengthA;
        RetainedStrengthB = retainedStrengthB;
        DamageByA = damageByA;
        DamageByB = damageByB;
    }

    public int Games => WinsA + WinsB;
}

public readonly struct LadderRoundResult
{
    public readonly string ContenderA;
    public readonly string ContenderB;
    public readonly string WinnerDesignName;
    public readonly int RatingA;
    public readonly int RatingB;
    public readonly string DesignA;
    public readonly string DesignB;
    public readonly float StrengthRatio;
    public readonly int TierA;
    public readonly int TierB;

    public LadderRoundResult(string contenderA, string contenderB, string winnerDesignName, int ratingA, int ratingB,
        string designA = "", string designB = "", float strengthRatio = 1f, int tierA = 0, int tierB = 0)
    {
        ContenderA = contenderA;
        ContenderB = contenderB;
        WinnerDesignName = winnerDesignName;
        RatingA = ratingA;
        RatingB = ratingB;
        DesignA = designA ?? "";
        DesignB = designB ?? "";
        StrengthRatio = Math.Max(1f, strengthRatio);
        TierA = Math.Max(0, tierA);
        TierB = Math.Max(0, tierB);
    }
}

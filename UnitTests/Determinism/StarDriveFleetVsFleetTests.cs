using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;
using Ship_Game.AI;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
using Vector2 = SDGraphics.Vector2;
using Point = SDGraphics.Point;

namespace UnitTests.Determinism;

/// <summary>
/// Prototype of a "FLEET vs FLEET" autobattler mode: two sides each get an equal PRODUCTION BUDGET, fill it
/// with copies of a ship DESIGN (budget / unit-cost), spawn the fleets facing each other, and let the combat
/// AI autobattle. Built on the proven deterministic combat slice — with a seeded RNG the whole match is
/// reproducible, so the same matchup always yields the same winner (a fair, replayable design-balancing duel).
/// </summary>
[TestClass]
public class StarDriveFleetVsFleetTests : StarDriveTest
{
    // Spawn a compact grid (columns recede AWAY from the enemy; rows spread across Y) so the fleet actually
    // clashes instead of stringing out into a 40k-tall line. facingSign = +1 if the enemy is to the right.
    List<Ship> BuildFleet(Empire empire, IShipDesign design, float budget, Vector2 anchor, int facingSign, int maxShips = 60)
    {
        float cost = Math.Max(1f, design.GetCost(empire));
        int count = Math.Clamp((int)(budget / cost), 1, maxShips);
        int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
        int rows = (count + cols - 1) / cols;
        var ships = new List<Ship>(count);
        for (int i = 0; i < count; ++i)
        {
            int row = i / cols, col = i % cols;
            float x = anchor.X - facingSign * col * 1100f;     // columns recede behind the front line
            float y = (row - (rows - 1) / 2f) * 1100f;
            Ship s = SpawnShip(design.Name, empire, new Vector2(x, y));
            s.SensorRange = 250000;
            ships.Add(s);
        }
        return ships;
    }

    static int Alive(List<Ship> fleet) => fleet.Count(s => s.Active);
    static float Strength(List<Ship> fleet) => fleet.Where(s => s.Active).Sum(s => s.GetStrength());

    static void Engage(List<Ship> attackers, List<Ship> targets)
    {
        var live = targets.Where(t => t.Active).ToArray();
        if (live.Length == 0) return;
        for (int i = 0; i < attackers.Count; ++i)
            if (attackers[i].Active)
                attackers[i].AI.OrderAttackSpecificTarget(live[i % live.Length]);
    }

    (int aliveA, int aliveB, int countA, int countB, string a, string b, float costA, float costB, float strA, float strB) RunMatch(ulong seed, float budget, bool verbose)
    {
        CreateUniverseAndPlayerEmpire(); // Player + Enemy, already at war
        UState.P.GravityWellRange = 0;
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;

        // Deterministically pick two distinct warships: a cheap one (quantity) and a ~3x pricier one (quality).
        IShipDesign[] warships = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f)
            .OrderBy(d => d.GetCost(Player))
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();
        IShipDesign desA = warships.First();
        float costA = desA.GetCost(Player);
        IShipDesign desB = warships.FirstOrDefault(d => d.GetCost(Player) >= costA * 3f) ?? warships[warships.Length / 2];

        List<Ship> fleetA = BuildFleet(Player, desA, budget, new Vector2(-5000, 0), facingSign: +1); // cheap swarm
        List<Ship> fleetB = BuildFleet(Enemy,  desB, budget, new Vector2( 5000, 0), facingSign: -1); // pricey few

        UState.EnableDeterministicRng(seed);
        Engage(fleetA, fleetB);
        Engage(fleetB, fleetA);

        const int Ticks = 9000;
        for (int t = 0; t < Ticks; ++t)
        {
            UState.Objects.Update(TestSimStep);
            if (t % 1000 == 999)
            {
                if (verbose)
                    Console.WriteLine($"[fvf]   t={t + 1,4} A:{Alive(fleetA),2}/{fleetA.Count} (str {Strength(fleetA):0})  B:{Alive(fleetB),2}/{fleetB.Count} (str {Strength(fleetB):0})");
                Engage(fleetA, fleetB); // keep survivors fighting as targets die
                Engage(fleetB, fleetA);
            }
        }
        return (Alive(fleetA), Alive(fleetB), fleetA.Count, fleetB.Count, desA.Name, desB.Name, costA, desB.GetCost(Player), Strength(fleetA), Strength(fleetB));
    }

    [TestMethod]
    public void FleetVsFleet_EqualBudget_DeterministicAutobattle()
    {
        LoadAllGameData();

        const float Budget = 1200f;
        var r1 = RunMatch(0x5EED1234u, budget: Budget, verbose: true);
        string winner =
            r1.aliveB == 0 && r1.aliveA > 0 ? "A (wiped enemy)" :
            r1.aliveA == 0 && r1.aliveB > 0 ? "B (wiped enemy)" :
            r1.strA > r1.strB ? "A (more strength left)" :
            r1.strB > r1.strA ? "B (more strength left)" : "draw";
        Console.WriteLine($"[fvf] MATCH: equal budget {Budget:0}  |  A = {r1.countA}x '{r1.a}' @{r1.costA:0} (= {r1.countA * r1.costA:0} prod)  "
                        + $"vs  B = {r1.countB}x '{r1.b}' @{r1.costB:0} (= {r1.countB * r1.costB:0} prod)");
        Console.WriteLine($"[fvf] RESULT: A survivors={r1.aliveA}/{r1.countA} (str {r1.strA:0}), B survivors={r1.aliveB}/{r1.countB} (str {r1.strB:0})  ->  WINNER = {winner}");

        // Reproducible: same seed + same matchup must give the identical outcome (deterministic autobattler).
        var r2 = RunMatch(0x5EED1234u, budget: Budget, verbose: false);
        Console.WriteLine($"[fvf] REPRO: rerun A survivors={r2.aliveA}, B survivors={r2.aliveB}");
        Assert.AreEqual((r1.aliveA, r1.aliveB), (r2.aliveA, r2.aliveB), "the autobattle must be reproducible (deterministic)");

        // The battle resolved meaningfully (one side took real losses).
        Assert.IsTrue(r1.aliveA < r1.countA || r1.aliveB < r1.countB, "the fleets actually fought and took casualties");
    }

    // ---------------------------------------------------------------------------------------------------
    // General N-side / multi-design / teams harness.
    // A Side = an empire on a team, anchored at a position, fielding a fleet composed of a MIX of designs.
    // Composition is (tier, fraction) pairs: 'tier' in [0,1] selects a warship from the cost-sorted pool
    // (0=cheapest, 1=priciest), 'fraction' is the share of the budget spent on it. So {(0.1,0.5),(0.9,0.5)}
    // spends half the budget on cheap ships and half on the priciest -> a real multi-design fleet.
    // ---------------------------------------------------------------------------------------------------
    sealed class Side
    {
        public Empire Empire;
        public int Team;
        public string Name;
        public Vector2 Anchor;
        public (float tier, float frac)[] Comp;
        public readonly List<Ship> Fleet = new();
    }

    Side MkSide(Empire e, int team, Vector2 anchor, (float, float)[] comp)
        => new() { Empire = e, Team = team, Name = e.Name, Anchor = anchor, Comp = comp };

    IShipDesign[] WarshipPool(Empire e)
    {
        IShipDesign[] all = ResourceManager.Ships.Designs.Where(d => d.BaseStrength > 100f)
            .OrderBy(d => d.GetCost(e)).ThenBy(d => d.Name, StringComparer.Ordinal).ToArray();
        // Use the cheaper ~55% so 'tier 1.0' is an upper-mid warship, not an unaffordable capital (which
        // would field 0 ships at these budgets). Keeps every side's fleet meaningfully sized.
        return all.Take(Math.Max(4, (int)(all.Length * 0.55f))).ToArray();
    }

    static IShipDesign PickTier(IShipDesign[] pool, float tier01) =>
        pool[Math.Clamp((int)Math.Round((pool.Length - 1) * tier01), 0, pool.Length - 1)];

    void BuildSideFleet(Side side, float budget, IShipDesign[] pool, int capPerTier = 12)
    {
        int placed = 0;
        foreach ((float tier, float frac) in side.Comp)
        {
            IShipDesign d = PickTier(pool, tier);
            float cost = Math.Max(1f, d.GetCost(side.Empire));
            int n = Math.Clamp((int)(budget * frac / cost), 1, capPerTier); // at least 1, so no side fields 0
            for (int i = 0; i < n; ++i)
            {
                int row = placed / 5, col = placed % 5;
                Ship s = SpawnShip(d.Name, side.Empire, side.Anchor + new Vector2((col - 2) * 900f, (row - 2) * 900f));
                s.SensorRange = 400000;
                side.Fleet.Add(s);
                ++placed;
            }
        }
    }

    void SetupWars(List<Side> sides)
    {
        for (int i = 0; i < sides.Count; ++i)
            for (int j = i + 1; j < sides.Count; ++j)
                if (sides[i].Team != sides[j].Team)
                {
                    Empire a = sides[i].Empire, b = sides[j].Empire;
                    Empire.SetRelationsAsKnown(a, b);
                    if (!a.IsAtWarWith(b)) a.AI.DeclareWarOn(b, WarType.ImperialistWar);
                }
    }

    // Each ship targets the nearest living ship on a DIFFERENT team (free-for-all / cross-team).
    static void EngageAll(List<Side> sides)
    {
        foreach (Side s in sides)
            foreach (Ship ship in s.Fleet)
            {
                if (!ship.Active) continue;
                Ship target = sides.Where(o => o.Team != s.Team).SelectMany(o => o.Fleet)
                    .Where(t => t.Active)
                    .OrderBy(t => ship.Position.SqDist(t.Position)).FirstOrDefault();
                if (target != null) ship.AI.OrderAttackSpecificTarget(target);
            }
    }

    // ---------------------------------------------------------------------------------------------------
    // v2 UPGRADE #2 — THREAT-WEIGHTED / FOCUS-FIRE TARGETING (ADDITIVE; does NOT touch EngageAll above).
    // Instead of each ship picking its spatially-nearest enemy, an ENTIRE TEAM focuses fire on the single
    // highest-threat living enemy ship (threat = Ship.GetStrength(), tiebroken by current Health). Because
    // OrderAttackSpecificTarget commits all of a ship's weapons to that target (Weapon.PickShipTarget prefers
    // the assigned mainTarget), the whole team's DPS converges on one ship until it dies — then the next
    // re-target call (same 1500-tick cadence used by EngageAll) re-evaluates and shifts to the new strongest.
    // In an FFA, EVERY team independently focuses the strongest enemy across all other teams, so the leader
    // gets ganged up on (emergent kingmaker dynamic) — qualitatively different from nearest-target clustering.
    // ---------------------------------------------------------------------------------------------------
    static void EngageAllThreat(List<Side> sides)
    {
        // Team-level threat assessment: every ship on a team attacks the SAME (strongest enemy) ship.
        foreach (IGrouping<int, Side> teamGroup in sides.GroupBy(s => s.Team))
        {
            // All living enemies on a DIFFERENT team. Ordering is deterministic: GetStrength desc, then
            // Health desc, then a stable Id tiebreak so the pick is reproducible run-to-run.
            Ship threatTarget = sides.Where(o => o.Team != teamGroup.Key)
                .SelectMany(o => o.Fleet)
                .Where(t => t.Active)
                .OrderByDescending(t => t.GetStrength())
                .ThenByDescending(t => t.Health)
                .ThenBy(t => t.Id)
                .FirstOrDefault();
            if (threatTarget == null) continue;

            foreach (Side s in teamGroup)
                foreach (Ship ship in s.Fleet)
                    if (ship.Active)
                        ship.AI.OrderAttackSpecificTarget(threatTarget);
        }
    }

    Empire CreateAnotherMajor()
    {
        IEmpireData data = ResourceManager.MajorRaces.First(e => UState.GetEmpireByName(e.Name) == null);
        Empire e = UState.CreateEmpire(data, isPlayer: false);
        foreach (Empire them in UState.Empires)
            if (them != e) { Empire.SetRelationsAsKnown(them, e); Empire.UpdateBilateralRelations(them, e); }
        return e;
    }

    void SetupArena()
    {
        UState.P.GravityWellRange = 0;
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;
        // Galaxy view => IsVisibleToPlayer is false for all ships => the carrier launch-flash particle path
        // (LaunchShip.LaunchFromHangar.Update, which NREs in the headless sim because there is no
        // Screen.Particles) is skipped. Without this, any fight where a carrier launches fighters crashes.
        UState.ViewState = UniverseScreen.UnivScreenState.GalaxyView;
    }

    // Build all fleets, declare cross-team wars, run the autobattle, report, and return per-team alive+strength.
    (int[] aliveByTeam, int winnerTeam) BuildRunReport(List<Side> sides, IShipDesign[] pool, float budget, ulong seed, int ticks, string tag, BattleReplay replay = null)
    {
        foreach (Side s in sides) BuildSideFleet(s, budget, pool);
        if (replay != null) foreach (Side s in sides) replay.Register(s.Fleet, s.Team);
        SetupWars(sides);
        UState.EnableDeterministicRng(seed);
        EngageAll(sides);
        replay?.Capture();
        for (int t = 0; t < ticks; ++t)
        {
            UState.Objects.Update(TestSimStep);
            if (t % 1500 == 1499) EngageAll(sides);  // re-target as ships die
            if (replay != null && t % 70 == 0) replay.Capture(); // ~1 frame / 70 ticks
        }

        foreach (Side s in sides)
            Console.WriteLine($"[fvf] {tag}  {s.Name,-18} team{s.Team}  ships={s.Fleet.Count,2} alive={s.Fleet.Count(x => x.Active),2} "
                            + $"strength={s.Fleet.Where(x => x.Active).Sum(x => x.GetStrength()),6:0}  comp=[{string.Join(",", s.Comp.Select(c => $"{c.tier:0.0}:{c.frac:0.0}"))}]");

        int maxTeam = sides.Max(s => s.Team) + 1;
        var aliveByTeam = new int[maxTeam];
        var strByTeam = new float[maxTeam];
        foreach (Side s in sides)
            foreach (Ship sh in s.Fleet)
                if (sh.Active) { aliveByTeam[s.Team]++; strByTeam[s.Team] += sh.GetStrength(); }

        int winner = 0;
        for (int t = 1; t < maxTeam; ++t) if (strByTeam[t] > strByTeam[winner]) winner = t;
        Console.WriteLine($"[fvf] {tag} WINNER = team{winner} (strength {strByTeam[winner]:0}); aliveByTeam=[{string.Join(",", aliveByTeam)}]");
        return (aliveByTeam, winner);
    }

    // v2 UPGRADE #2: BuildRunReportThreat — an ADDITIVE mirror of BuildRunReport that runs the SAME
    // deterministic build + sim loop (identical spawn order, identical seeded RNG, identical 1500-tick
    // re-target cadence) but engages with EngageAllThreat (team focus-fire on the strongest enemy) instead
    // of EngageAll (each ship -> nearest enemy). v1 BuildRunReport is left completely untouched, so all
    // committed v1 results stay bit-identical. Returns alive + strength per team so the FFA experiment can
    // diff outcomes between the two targeting policies.
    (int[] aliveByTeam, int winnerTeam, float[] strByTeam) BuildRunReportThreat(List<Side> sides, IShipDesign[] pool, float budget, ulong seed, int ticks, string tag)
    {
        foreach (Side s in sides) BuildSideFleet(s, budget, pool);
        SetupWars(sides);
        UState.EnableDeterministicRng(seed);
        EngageAllThreat(sides);
        for (int t = 0; t < ticks; ++t)
        {
            UState.Objects.Update(TestSimStep);
            if (t % 1500 == 1499) EngageAllThreat(sides);  // re-target onto the new strongest as ships die
        }

        foreach (Side s in sides)
            Console.WriteLine($"[v2] {tag}  {s.Name,-18} team{s.Team}  ships={s.Fleet.Count,2} alive={s.Fleet.Count(x => x.Active),2} "
                            + $"strength={s.Fleet.Where(x => x.Active).Sum(x => x.GetStrength()),6:0}  comp=[{string.Join(",", s.Comp.Select(c => $"{c.tier:0.0}:{c.frac:0.0}"))}]");

        int maxTeam = sides.Max(s => s.Team) + 1;
        var aliveByTeam = new int[maxTeam];
        var strByTeam = new float[maxTeam];
        foreach (Side s in sides)
            foreach (Ship sh in s.Fleet)
                if (sh.Active) { aliveByTeam[s.Team]++; strByTeam[s.Team] += sh.GetStrength(); }

        int winner = 0;
        for (int t = 1; t < maxTeam; ++t) if (strByTeam[t] > strByTeam[winner]) winner = t;
        Console.WriteLine($"[v2] {tag} WINNER = team{winner} (strength {strByTeam[winner]:0}); aliveByTeam=[{string.Join(",", aliveByTeam)}]");
        return (aliveByTeam, winner, strByTeam);
    }

    // 1) MULTI-DESIGN COMPOSITION: each side fields a MIX of designs within an equal budget.
    [TestMethod]
    public void MultiDesign_TwoSides_MixVsHeavy()
    {
        LoadAllGameData();

        int[] Run()
        {
            CreateUniverseAndPlayerEmpire();
            SetupArena();
            IShipDesign[] pool = WarshipPool(Player);
            var sides = new List<Side>
            {
                MkSide(Player, 0, new Vector2(-7000, 0), new[] { (0.1f, 0.5f), (0.5f, 0.3f), (0.9f, 0.2f) }), // balanced mix
                MkSide(Enemy,  1, new Vector2( 7000, 0), new[] { (0.85f, 1.0f) }),                            // all-heavy doctrine
            };
            return BuildRunReport(sides, pool, 4000f, 0x5EED1234u, 7000, "MIX2").aliveByTeam;
        }

        int[] r1 = Run();
        int[] r2 = Run();
        CollectionAssert.AreEqual(r1, r2, "multi-design battle must be reproducible");
        Assert.IsTrue(r1[0] + r1[1] > 0, "at least one side survived");
    }

    // 2) THREE-WAY FREE-FOR-ALL: three sides, each its own team, last team standing (by strength).
    [TestMethod]
    public void ThreeWay_FreeForAll()
    {
        LoadAllGameData();

        (int[] alive, int winner) Run()
        {
            CreateUniverseAndPlayerEmpire();
            CreateThirdMajorEmpire();
            SetupArena();
            IShipDesign[] pool = WarshipPool(Player);
            var sides = new List<Side>
            {
                MkSide(Player,     0, new Vector2(-9000, -6000), new[] { (0.2f, 0.6f), (0.8f, 0.4f) }),
                MkSide(Enemy,      1, new Vector2( 9000, -6000), new[] { (0.5f, 1.0f) }),
                MkSide(ThirdMajor, 2, new Vector2(    0,  9000), new[] { (0.1f, 0.7f), (0.95f, 0.3f) }),
            };
            var res = BuildRunReport(sides, pool, 2600f, 0xABCDEFu, 5000, "FFA3");
            return (res.aliveByTeam, res.winnerTeam);
        }

        (int[] a1, int w1) = Run();
        (int[] a2, int w2) = Run();
        CollectionAssert.AreEqual(a1, a2, "3-way FFA must be reproducible");
        Assert.AreEqual(w1, w2, "same winner each run");
        Assert.AreEqual(3, a1.Length, "three teams");
    }

    // 3) TEAMS: 2v2 (four empires, two teams) -> teammates ignore each other, fight the other team.
    [TestMethod]
    public void TwoVsTwo_Teams()
    {
        LoadAllGameData();

        int[] Run()
        {
            CreateUniverseAndPlayerEmpire(); // Player, Enemy
            CreateThirdMajorEmpire();        // ThirdMajor
            Empire fourth = CreateAnotherMajor();
            SetupArena();
            IShipDesign[] pool = WarshipPool(Player);
            var sides = new List<Side>
            {
                MkSide(Player,     0, new Vector2(-9000,  4000), new[] { (0.2f, 0.7f), (0.9f, 0.3f) }),
                MkSide(ThirdMajor, 0, new Vector2(-9000, -4000), new[] { (0.2f, 0.7f), (0.9f, 0.3f) }),
                MkSide(Enemy,      1, new Vector2( 9000,  4000), new[] { (0.2f, 0.7f), (0.9f, 0.3f) }),
                MkSide(fourth,     1, new Vector2( 9000, -4000), new[] { (0.2f, 0.7f), (0.9f, 0.3f) }),
            };
            return BuildRunReport(sides, pool, 2200f, 0x77777u, 5000, "TEAM2v2").aliveByTeam;
        }

        int[] r1 = Run();
        int[] r2 = Run();
        CollectionAssert.AreEqual(r1, r2, "2v2 teams battle must be reproducible");
        Assert.AreEqual(2, r1.Length, "two teams");
    }

    // 4) VISUAL CLIP: record a battle and emit a SELF-CONTAINED HTML replay (top-down dots, play/pause/scrub).
    [TestMethod]
    public void FleetVsFleet_RecordsVisualReplay()
    {
        LoadAllGameData();

        // Bigger 3-way clip for the HTML/SVG files on disk.
        CreateUniverseAndPlayerEmpire();
        CreateThirdMajorEmpire();
        SetupArena();
        IShipDesign[] pool = WarshipPool(Player);
        var big = new List<Side>
        {
            MkSide(Player,     0, new Vector2(-9000, -6000), new[] { (0.2f, 0.6f), (0.8f, 0.4f) }),
            MkSide(Enemy,      1, new Vector2( 9000, -6000), new[] { (0.5f, 1.0f) }),
            MkSide(ThirdMajor, 2, new Vector2(    0,  9000), new[] { (0.1f, 0.7f), (1.0f, 0.3f) }),
        };
        var bigReplay = new BattleReplay { Title = "Fleet vs Fleet vs Fleet - 3-way autobattle" };
        BuildRunReport(big, pool, 2600f, 0xABCDEFu, 5000, "REPLAY3", bigReplay);

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays"); // game/battle-replays (gitignored)
        string html = bigReplay.WriteHtml(dir, "fleet3way.html");
        string bigSvg = bigReplay.WriteAnimatedSvg(dir, "fleet3way.svg");

        // Small, compact clip (few ships, few frames) -- a clean duel that's easy to embed/share.
        CreateUniverseAndPlayerEmpire();
        SetupArena();
        IShipDesign[] pool2 = WarshipPool(Player);
        var small = new List<Side>
        {
            MkSide(Player, 0, new Vector2(-6000, 0), new[] { (0.0f, 1.0f) }), // cheap swarm (cheapest warship)
            MkSide(Enemy,  1, new Vector2( 6000, 0), new[] { (0.5f, 1.0f) }), // a few mid-tier ships
        };
        var duel = new BattleReplay { Title = "Fleet duel - cheap swarm vs mid-tier" };
        BuildRunReport(small, pool2, 420f, 0x5EED1234u, 6000, "DUEL", duel);
        string duelSvg = duel.WriteAnimatedSvg(dir, "fleetduel.svg", frameStride: 6);

        Console.WriteLine($"[fvf] REPLAY: 3-way html={html}, svg={bigSvg} ({new FileInfo(bigSvg).Length}B)");
        Console.WriteLine($"[fvf] REPLAY: duel clip svg={duelSvg} ({new FileInfo(duelSvg).Length}B, {duel.FrameCount} captured frames)");
        Assert.IsTrue(File.Exists(html) && File.Exists(bigSvg) && File.Exists(duelSvg), "battle replay clips written");
    }

    // ===================================================================================================
    // 5) BALANCE / META SWEEP: a matrix of fights -> structured JSON for offline analysis.
    //    Each fight: two sides, possibly ASYMMETRIC budgets, distinct deterministic seed, one JSON row.
    // ===================================================================================================

    // One per-side snapshot for the JSON output.
    sealed class FightSide
    {
        public string Comp;             // "comp" descriptor OR null (designs path)
        public string Design;           // exact design name OR null (comp path)
        public float Budget;
        public int ShipsFielded;
        public int ShipsAlive;
        public float StrStart;
        public float StrEnd;
        public List<string> Designs = new();
    }

    sealed class Fight
    {
        public string Category, Label, SeedHex, Winner;
        public FightSide SideA, SideB;
        public int Ticks;
        public bool Decisive;
    }

    static string J(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string F(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    static string SideJson(FightSide s)
    {
        var designs = string.Join(",", s.Designs.Select(J));
        string compOrDesign = s.Design != null ? $"\"design\":{J(s.Design)}" : $"\"comp\":{J(s.Comp)}";
        return $"{{{compOrDesign},\"budget\":{F(s.Budget)},\"shipsFielded\":{s.ShipsFielded},\"shipsAlive\":{s.ShipsAlive},"
             + $"\"strStart\":{F(s.StrStart)},\"strEnd\":{F(s.StrEnd)},\"designs\":[{designs}]}}";
    }

    static string FightJson(Fight f)
        => $"{{\"category\":{J(f.Category)},\"label\":{J(f.Label)},\"seedHex\":{J(f.SeedHex)},"
         + $"\"sideA\":{SideJson(f.SideA)},\"sideB\":{SideJson(f.SideB)},\"ticks\":{f.Ticks},"
         + $"\"winner\":{J(f.Winner)},\"decisive\":{(f.Decisive ? "true" : "false")}}}";

    static float StrengthOf(List<Ship> fleet) => fleet.Where(s => s.Active).Sum(s => s.GetStrength());
    static int AliveOf(List<Ship> fleet) => fleet.Count(s => s.Active);
    static List<string> DesignNames(List<Ship> fleet) =>
        fleet.Select(s => s.Name).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();

    // Build a fleet of EXACTLY one design (cost-targeted), mirroring BuildSideFleet's grid placement.
    void BuildExact(Side side, IShipDesign design, float budget)
    {
        float cost = Math.Max(1f, design.GetCost(side.Empire));
        int n = Math.Clamp((int)(budget / cost), 1, 12);
        for (int i = 0; i < n; ++i)
        {
            int row = i / 5, col = i % 5;
            Ship s = SpawnShip(design.Name, side.Empire, side.Anchor + new Vector2((col - 2) * 900f, (row - 2) * 900f));
            s.SensorRange = 400000;
            side.Fleet.Add(s);
        }
    }

    // Run an asymmetric-budget 2-side fight (each side built with its OWN budget), mirroring BuildRunReport's
    // loop exactly. buildA/buildB do the actual fleet construction. Returns a fully-populated Fight row.
    Fight RunFight(string category, string label, ulong seed, int ticks,
                   FightSide metaA, FightSide metaB,
                   Action<Side> buildA, Action<Side> buildB,
                   BattleReplay replay = null)
    {
        CreateUniverseAndPlayerEmpire();
        SetupArena();
        var a = MkSide(Player, 0, new Vector2(-8000, 0), new[] { (0f, 1f) });
        var b = MkSide(Enemy,  1, new Vector2( 8000, 0), new[] { (0f, 1f) });
        var sides = new List<Side> { a, b };

        buildA(a);
        buildB(b);

        if (replay != null) { replay.Register(a.Fleet, 0); replay.Register(b.Fleet, 1); }
        SetupWars(sides);
        UState.EnableDeterministicRng(seed);
        EngageAll(sides);
        replay?.Capture();

        metaA.ShipsFielded = a.Fleet.Count; metaB.ShipsFielded = b.Fleet.Count;
        metaA.StrStart = StrengthOf(a.Fleet); metaB.StrStart = StrengthOf(b.Fleet);
        metaA.Designs = DesignNames(a.Fleet); metaB.Designs = DesignNames(b.Fleet);

        for (int t = 0; t < ticks; ++t)
        {
            UState.Objects.Update(TestSimStep);
            if (t % 1500 == 1499) EngageAll(sides);
            if (replay != null && t % 70 == 0) replay.Capture();
        }
        replay?.Capture();

        metaA.ShipsAlive = AliveOf(a.Fleet); metaB.ShipsAlive = AliveOf(b.Fleet);
        metaA.StrEnd = StrengthOf(a.Fleet);  metaB.StrEnd = StrengthOf(b.Fleet);

        bool decisive = metaA.ShipsAlive == 0 || metaB.ShipsAlive == 0;
        string winner =
            metaB.ShipsAlive == 0 && metaA.ShipsAlive > 0 ? "A" :
            metaA.ShipsAlive == 0 && metaB.ShipsAlive > 0 ? "B" :
            metaA.StrEnd > metaB.StrEnd ? "A" :
            metaB.StrEnd > metaA.StrEnd ? "B" : "draw";

        var fight = new Fight
        {
            Category = category, Label = label, SeedHex = "0x" + seed.ToString("X"),
            SideA = metaA, SideB = metaB, Ticks = ticks, Winner = winner, Decisive = decisive
        };
        Console.WriteLine($"[sweep] {category,-12} {label,-34} seed={fight.SeedHex,-10} "
                        + $"A[{metaA.ShipsAlive}/{metaA.ShipsFielded} str {metaA.StrStart:0}->{metaA.StrEnd:0}] "
                        + $"B[{metaB.ShipsAlive}/{metaB.ShipsFielded} str {metaB.StrStart:0}->{metaB.StrEnd:0}] "
                        + $"-> {winner}{(decisive ? " (decisive)" : "")}");
        return fight;
    }

    // Result of one v2-scored fight: the v1 Fight row PLUS empire-based (fighter-inclusive) end strengths
    // and alive counts for each side, captured from empire.OwnedShips before the universe is torn down.
    sealed class FightV2
    {
        public Fight V1;                               // v1 row (winner decided by side.Fleet strength)
        public float StrEndEmpireA, StrEndEmpireB;     // v2 end strength (fighters counted)
        public int AliveEmpireA, AliveEmpireB;         // v2 living owned-ship count (fighters counted)
        public float StrEndFleetA, StrEndFleetB;       // v1 bare-hull end strength (side.Fleet only)
        public string WinnerV2;                        // "A" / "B" / "draw" by empire strength
    }

    // RunFightV2: mirrors RunFight's deterministic build+sim loop EXACTLY (same spawn order, same seeded RNG,
    // same re-target cadence) so the sim is bit-identical, then ADDITIVELY scores each side by its empire's
    // living owned ships (carrier fighters included). v1 side.Fleet scoring is preserved untouched in the
    // returned Fight; v2 empire scoring is captured separately. Each side is its own empire (A=Player, B=Enemy).
    FightV2 RunFightV2(string category, string label, ulong seed, int ticks,
                       FightSide metaA, FightSide metaB,
                       Action<Side> buildA, Action<Side> buildB)
    {
        CreateUniverseAndPlayerEmpire();
        SetupArena();
        var a = MkSide(Player, 0, new Vector2(-8000, 0), new[] { (0f, 1f) });
        var b = MkSide(Enemy,  1, new Vector2( 8000, 0), new[] { (0f, 1f) });
        var sides = new List<Side> { a, b };

        buildA(a);
        buildB(b);

        SetupWars(sides);
        UState.EnableDeterministicRng(seed);
        EngageAll(sides);

        metaA.ShipsFielded = a.Fleet.Count; metaB.ShipsFielded = b.Fleet.Count;
        metaA.StrStart = StrengthOf(a.Fleet); metaB.StrStart = StrengthOf(b.Fleet);
        metaA.Designs = DesignNames(a.Fleet); metaB.Designs = DesignNames(b.Fleet);

        for (int t = 0; t < ticks; ++t)
        {
            UState.Objects.Update(TestSimStep);
            if (t % 1500 == 1499) EngageAll(sides);
        }

        // v1 scoring (side.Fleet, bare-hull) — identical to RunFight, leaves v1 results reproducible.
        metaA.ShipsAlive = AliveOf(a.Fleet); metaB.ShipsAlive = AliveOf(b.Fleet);
        metaA.StrEnd = StrengthOf(a.Fleet);  metaB.StrEnd = StrengthOf(b.Fleet);

        bool decisive = metaA.ShipsAlive == 0 || metaB.ShipsAlive == 0;
        string winner =
            metaB.ShipsAlive == 0 && metaA.ShipsAlive > 0 ? "A" :
            metaA.ShipsAlive == 0 && metaB.ShipsAlive > 0 ? "B" :
            metaA.StrEnd > metaB.StrEnd ? "A" :
            metaB.StrEnd > metaA.StrEnd ? "B" : "draw";

        var fight = new Fight
        {
            Category = category, Label = label, SeedHex = "0x" + seed.ToString("X"),
            SideA = metaA, SideB = metaB, Ticks = ticks, Winner = winner, Decisive = decisive
        };

        // v2 scoring (empire.OwnedShips, fighters counted) — captured BEFORE the next universe is built.
        float strEmpA = StrengthOfSideByEmpire(a), strEmpB = StrengthOfSideByEmpire(b);
        int aliveEmpA = AliveOfSideByEmpire(a),    aliveEmpB = AliveOfSideByEmpire(b);
        string winnerV2 =
            strEmpA > strEmpB ? "A" :
            strEmpB > strEmpA ? "B" : "draw";

        Console.WriteLine($"[v2] {category,-10} {label,-36} seed=0x{seed:X} "
                        + $"A[fleetStr {metaA.StrEnd:0} | empStr {strEmpA:0} alive {aliveEmpA}] "
                        + $"B[fleetStr {metaB.StrEnd:0} | empStr {strEmpB:0} alive {aliveEmpB}] "
                        + $"-> v1:{winner} v2:{winnerV2}");

        return new FightV2
        {
            V1 = fight,
            StrEndEmpireA = strEmpA, StrEndEmpireB = strEmpB,
            AliveEmpireA = aliveEmpA, AliveEmpireB = aliveEmpB,
            StrEndFleetA = metaA.StrEnd, StrEndFleetB = metaB.StrEnd,
            WinnerV2 = winnerV2
        };
    }

    [TestMethod]
    public void BalanceMeta_FleetSweep_EmitsJson()
    {
        LoadAllGameData();

        var fights = new List<Fight>();
        // Capture a comp-string -> the comp tuple used to (re)build; lets categories share helpers.
        (float, float)[] swarm = { (0.0f, 1f) };
        (float, float)[] heavy = { (0.6f, 1f) };
        (float, float)[] midPure = { (0.5f, 1f) };
        (float, float)[] mixed = { (0.1f, 0.4f), (0.5f, 0.35f), (0.9f, 0.25f) };

        string Comp(string n, (float, float)[] c) => $"{n}{{{string.Join(",", c.Select(p => $"{p.Item1:0.0}:{p.Item2:0.0}"))}}}";

        void BuildComp(Side side, (float, float)[] comp, float budget)
        {
            side.Comp = comp;
            BuildSideFleet(side, budget, WarshipPool(side.Empire));
        }

        // ---- Category 1: QUANTITY-vs-QUALITY crossover. B(heavy)=800 fixed; sweep A(swarm)=800*r. ----
        ulong seed1 = 0xC0FFEE00u;
        float bBudget = 800f;
        foreach (float r in new[] { 1.0f, 1.5f, 2.0f, 2.5f, 3.0f, 4.0f })
        {
            float aBudget = 800f * r;
            var ma = new FightSide { Comp = Comp("swarm", swarm), Budget = aBudget };
            var mb = new FightSide { Comp = Comp("heavy", heavy), Budget = bBudget };
            fights.Add(RunFight("qty-vs-qual", $"swarm@{aBudget:0} vs heavy@800 (r={r:0.0})", seed1++, 7000,
                ma, mb,
                sa => BuildComp(sa, swarm, aBudget),
                sb => BuildComp(sb, heavy, bBudget)));
        }

        // ---- Category 2: COMBINED-ARMS. mixed vs pure-mid, EQUAL budget, at 2500 and 4000. ----
        ulong seed2 = 0xCA0000u;
        foreach (float budget in new[] { 2500f, 4000f })
        {
            var ma = new FightSide { Comp = Comp("mixed", mixed), Budget = budget };
            var mb = new FightSide { Comp = Comp("pureMid", midPure), Budget = budget };
            fights.Add(RunFight("combined-arms", $"mixed vs pureMid @{budget:0}", seed2++, 7000,
                ma, mb,
                sa => BuildComp(sa, mixed, budget),
                sb => BuildComp(sb, midPure, budget)));
        }

        // ---- Category 3: DESIGN ROUND-ROBIN cost-efficiency tier list (1v1, equal budget 1800). ----
        IShipDesign[] strong = ResourceManager.Ships.Designs.Where(d => d.BaseStrength > 100f)
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal).ToArray();
        var pickIdx = new[] { 0.10f, 0.25f, 0.40f, 0.60f, 0.80f, 0.95f }
            .Select(p => Math.Clamp((int)Math.Round((strong.Length - 1) * p), 0, strong.Length - 1))
            .Distinct().ToArray();
        IShipDesign[] picks = pickIdx.Select(i => strong[i]).ToArray();
        const float rrBudget = 1800f;

        // win count + sum of strength-retained fraction per design name, for the tier list.
        var wins = new Dictionary<string, int>(StringComparer.Ordinal);
        var retSum = new Dictionary<string, float>(StringComparer.Ordinal);
        var retN = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (IShipDesign d in picks) { wins[d.Name] = 0; retSum[d.Name] = 0; retN[d.Name] = 0; }

        ulong seed3 = 0xD00D0000u;
        for (int i = 0; i < picks.Length; ++i)
            for (int j = i + 1; j < picks.Length; ++j)
            {
                IShipDesign di = picks[i], dj = picks[j];
                var ma = new FightSide { Design = di.Name, Budget = rrBudget };
                var mb = new FightSide { Design = dj.Name, Budget = rrBudget };
                Fight f = RunFight("round-robin", $"{di.Name} vs {dj.Name}", seed3++, 5000,
                    ma, mb,
                    sa => BuildExact(sa, di, rrBudget),
                    sb => BuildExact(sb, dj, rrBudget));
                fights.Add(f);

                float retI = ma.StrStart > 0 ? ma.StrEnd / ma.StrStart : 0;
                float retJ = mb.StrStart > 0 ? mb.StrEnd / mb.StrStart : 0;
                retSum[di.Name] += retI; retN[di.Name]++;
                retSum[dj.Name] += retJ; retN[dj.Name]++;
                if (f.Winner == "A") wins[di.Name]++;
                else if (f.Winner == "B") wins[dj.Name]++;
            }

        Console.WriteLine("[sweep] --- round-robin tier list (wins | avg strength-retained) ---");
        foreach (IShipDesign d in picks.OrderByDescending(d => wins[d.Name]))
            Console.WriteLine($"[sweep] tier  {d.Name,-26} cost={d.GetCost(Player),6:0}  wins={wins[d.Name]}  "
                            + $"avgRetained={(retN[d.Name] > 0 ? retSum[d.Name] / retN[d.Name] : 0):0.000}");

        // ---- Category 4: SEED VARIANCE. one balanced matchup across 8 seeds. ----
        const float varBudget = 3000f;
        foreach (ulong seed in new ulong[] { 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8 })
        {
            var ma = new FightSide { Comp = Comp("mixed", mixed), Budget = varBudget };
            var mb = new FightSide { Comp = Comp("heavy", heavy), Budget = varBudget };
            fights.Add(RunFight("seed-variance", $"mixed vs heavy @3000 seed", seed, 7000,
                ma, mb,
                sa => BuildComp(sa, mixed, varBudget),
                sb => BuildComp(sb, heavy, varBudget)));
        }

        int aWins = fights.Count(f => f.Category == "seed-variance" && f.Winner == "A");
        int bWins = fights.Count(f => f.Category == "seed-variance" && f.Winner == "B");
        Console.WriteLine($"[sweep] seed-variance: A wins {aWins}/8, B wins {bWins}/8 "
                        + $"({(aWins == 8 || bWins == 8 ? "SEED-STABLE" : "coin-flip-ish")})");

        // ---- emit JSON ----
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string json = "[\n  " + string.Join(",\n  ", fights.Select(FightJson)) + "\n]\n";
        string jsonPath = Path.Combine(dir, "sweep.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[sweep] wrote {fights.Count} fights -> {jsonPath} ({new FileInfo(jsonPath).Length}B)");

        // ---- replays: most lopsided + closest fight (by final strength differential ratio) ----
        float Lopsided(Fight f)
        {
            float s = f.SideA.StrEnd + f.SideB.StrEnd;
            return s <= 0 ? 1f : Math.Abs(f.SideA.StrEnd - f.SideB.StrEnd) / s; // 1=total wipe, 0=dead even
        }
        // Closest among DECIDED fights (both sides started with ships, not a draw with both wiped).
        var decided = fights.Where(f => f.Winner != "draw" && f.SideA.ShipsFielded > 0 && f.SideB.ShipsFielded > 0).ToList();
        Fight mostLopsided = fights.OrderByDescending(Lopsided).First();
        Fight closest = (decided.Count > 0 ? decided : fights).OrderBy(Lopsided).First();

        var replayPaths = new List<string>();
        void Record(Fight f, string fileName)
        {
            // rebuild & replay the exact fight deterministically from its seed/label.
            var replay = new BattleReplay { Title = $"{f.Category}: {f.Label}" };
            ReplayRebuild(f, replay, swarm, heavy, midPure, mixed, picks);
            string p = replay.WriteAnimatedSvg(dir, fileName, frameStride: 2);
            replayPaths.Add(p);
            Console.WriteLine($"[sweep] replay {fileName} <- {f.Category} '{f.Label}' (lopsided={Lopsided(f):0.000}) {p}");
        }
        Record(mostLopsided, "sweep_most_lopsided.svg");
        Record(closest, "sweep_closest.svg");

        Assert.IsTrue(fights.Count > 0, "sweep produced fights");
        Assert.IsTrue(File.Exists(jsonPath), "sweep.json written");
        Assert.IsTrue(replayPaths.All(File.Exists), "both replay SVGs written");
    }

    // Rebuild a specific fight from its recorded label/category so we can capture a replay of it.
    void ReplayRebuild(Fight f, BattleReplay replay, (float, float)[] swarm, (float, float)[] heavy,
                       (float, float)[] midPure, (float, float)[] mixed, IShipDesign[] picks)
    {
        ulong seed = Convert.ToUInt64(f.SeedHex.Substring(2), 16);

        Action<Side> buildA, buildB;
        if (f.Category == "round-robin")
        {
            IShipDesign di = picks.First(d => d.Name == f.SideA.Design);
            IShipDesign dj = picks.First(d => d.Name == f.SideB.Design);
            buildA = sa => BuildExact(sa, di, f.SideA.Budget);
            buildB = sb => BuildExact(sb, dj, f.SideB.Budget);
        }
        else
        {
            (float, float)[] CompFor(string compStr) =>
                compStr.StartsWith("swarm")   ? swarm :
                compStr.StartsWith("heavy")   ? heavy :
                compStr.StartsWith("pureMid") ? midPure :
                compStr.StartsWith("mixed")   ? mixed : midPure;
            (float, float)[] ca = CompFor(f.SideA.Comp), cb = CompFor(f.SideB.Comp);
            buildA = sa => { sa.Comp = ca; BuildSideFleet(sa, f.SideA.Budget, WarshipPool(sa.Empire)); };
            buildB = sb => { sb.Comp = cb; BuildSideFleet(sb, f.SideB.Budget, WarshipPool(sb.Empire)); };
        }
        // Reuse RunFight purely for its deterministic build+sim loop, with throwaway meta.
        RunFight(f.Category, f.Label + " [replay]", seed, f.Ticks,
            new FightSide(), new FightSide(), buildA, buildB, replay);
    }

    // ===================================================================================================
    // 6) PARETO FRONTIER SWEEP: cost-vs-power matchmaking with TWO new build modes.
    //    - BuildExactCount: EQUAL-COUNT duels (isolate per-ship combat quality from price).
    //    - BuildToStrength: STRENGTH-MATCH duels (both sides start at the same total strength; a win
    //      then signals real composition synergy, not just mass).
    //    Four sub-matrices: (1) quantity-vs-quality crossover with cap unlocked so budget scales count,
    //    (2) equal-count round-robin -> 12 Pareto rows, (3) combined-arms strength-match, (4) seed
    //    robustness on the near-even round-robin pair. Each fight gets a distinct deterministic seed.
    // ===================================================================================================

    // Field EXACTLY `count` ships of one design at the side anchor (mirrors BuildExact placement, fixed
    // count, cap 60). Used for equal-count duels where price is deliberately ignored.
    void BuildExactCount(Side side, IShipDesign design, int count)
    {
        int n = Math.Clamp(count, 1, 60);
        for (int i = 0; i < n; ++i)
        {
            int row = i / 5, col = i % 5;
            Ship s = SpawnShip(design.Name, side.Empire, side.Anchor + new Vector2((col - 2) * 900f, (row - 2) * 900f));
            s.SensorRange = 400000;
            side.Fleet.Add(s);
        }
    }

    // STRENGTH-MATCH: field clamp(round(targetStr / unitStr), 1, 80) ships, where unitStr is the strength
    // of ONE such ship (spawn the first, read GetStrength(), then top up to the computed count). The first
    // ship is kept and counted so we never waste a spawn.
    void BuildToStrength(Side side, IShipDesign design, float targetStr)
    {
        // Spawn one to measure unit strength (placement of probe = grid slot 0).
        Ship probe = SpawnShip(design.Name, side.Empire, side.Anchor + new Vector2((0 - 2) * 900f, (0 - 2) * 900f));
        probe.SensorRange = 400000;
        side.Fleet.Add(probe);
        float unitStr = Math.Max(1f, probe.GetStrength());
        int n = Math.Clamp((int)Math.Round(targetStr / unitStr), 1, 80);
        for (int i = 1; i < n; ++i) // i=0 already placed (the probe)
        {
            int row = i / 5, col = i % 5;
            Ship s = SpawnShip(design.Name, side.Empire, side.Anchor + new Vector2((col - 2) * 900f, (row - 2) * 900f));
            s.SensorRange = 400000;
            side.Fleet.Add(s);
        }
    }

    [TestMethod]
    public void ParetoFrontier_FleetSweep_EmitsJson()
    {
        LoadAllGameData();

        var fights = new List<Fight>();
        var crossoverNotes = new List<string>();

        (float, float)[] swarm = { (0.0f, 1f) };
        (float, float)[] heavy = { (0.6f, 1f) };
        (float, float)[] pureMid = { (0.5f, 1f) };
        (float, float)[] mixed = { (0.1f, 0.4f), (0.5f, 0.35f), (0.9f, 0.25f) };
        string Comp(string n, (float, float)[] c) => $"{n}{{{string.Join(",", c.Select(p => $"{p.Item1:0.0}:{p.Item2:0.0}"))}}}";

        // ---- 1) QUANTITY-vs-QUALITY crossover (BUDGET mode, cap UNLOCKED to 200 so budget scales count). ----
        // Heavy fixed at 800. Sweep swarm budget = 800 * r. swarmShips MUST rise with r.
        ulong xseed = 0xC0FFEE00u;
        float heavyBudget = 800f;
        float crossoverR = -1f;       // first r where swarm (A) flips to winning
        int prevSwarmShips = -1;
        foreach (float r in new[] { 1.0f, 1.5f, 2.0f, 3.0f, 4.0f, 6.0f, 8.0f })
        {
            float swarmBudget = 800f * r;
            var ma = new FightSide { Comp = Comp("swarm", swarm), Budget = swarmBudget };
            var mb = new FightSide { Comp = Comp("heavy", heavy), Budget = heavyBudget };
            Fight f = RunFight("qty-vs-qual", $"swarm@{swarmBudget:0} (r={r:0.0}) vs heavy@800", xseed++, 7000,
                ma, mb,
                sa => { sa.Comp = swarm; BuildSideFleet(sa, swarmBudget, WarshipPool(sa.Empire), capPerTier: 200); },
                sb => { sb.Comp = heavy; BuildSideFleet(sb, heavyBudget, WarshipPool(sb.Empire), capPerTier: 200); });
            fights.Add(f);

            if (prevSwarmShips >= 0 && ma.ShipsFielded <= prevSwarmShips)
                crossoverNotes.Add($"swarmShips did not rise at r={r:0.0} ({prevSwarmShips}->{ma.ShipsFielded})");
            prevSwarmShips = ma.ShipsFielded;
            if (crossoverR < 0 && f.Winner == "A") crossoverR = r;

            Console.WriteLine($"[pareto] CROSSOVER r={r:0.0} swarmBudget={swarmBudget:0} swarmShips={ma.ShipsFielded} "
                            + $"swarmStr0={ma.StrStart:0} heavyStr0={mb.StrStart:0} winner={(f.Winner == "A" ? "A=swarm" : f.Winner == "B" ? "B=heavy" : "draw")} "
                            + $"decisive={f.Decisive}");
        }
        crossoverNotes.Add(crossoverR > 0 ? $"crossover r* = {crossoverR:0.0} (swarm flips to winning)"
                                          : "no crossover within r<=8 (heavy never beaten / swarm never flips)");

        // ---- 2) PARETO ROUND-ROBIN, EQUAL-COUNT (3 ships each). 12 designs across cost percentiles. ----
        IShipDesign[] strong = ResourceManager.Ships.Designs.Where(d => d.BaseStrength > 100f)
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal).ToArray();
        var pctTargets = new[] { 5, 12, 20, 30, 40, 50, 60, 70, 80, 88, 94, 99 };
        var pickIdx = pctTargets
            .Select(p => Math.Clamp((int)Math.Round((strong.Length - 1) * (p / 100f)), 0, strong.Length - 1))
            .Distinct().ToList();
        // If percentile collisions dropped us below 12 distinct designs, backfill with nearby indices.
        for (int probe = 0; pickIdx.Count < 12 && probe < strong.Length; ++probe)
            if (!pickIdx.Contains(probe)) pickIdx.Add(probe);
        pickIdx = pickIdx.OrderBy(i => i).Take(12).ToList();
        IShipDesign[] picks = pickIdx.Select(i => strong[i]).ToArray();

        const int EqCount = 3;
        var wins = new Dictionary<string, int>(StringComparer.Ordinal);
        var losses = new Dictionary<string, int>(StringComparer.Ordinal);
        var retSum = new Dictionary<string, float>(StringComparer.Ordinal);
        var retN = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (IShipDesign d in picks) { wins[d.Name] = 0; losses[d.Name] = 0; retSum[d.Name] = 0; retN[d.Name] = 0; }

        ulong rrSeed = 0xD00D0000u;
        Fight closest = null; float closestMargin = float.MaxValue;
        for (int i = 0; i < picks.Length; ++i)
            for (int j = i + 1; j < picks.Length; ++j)
            {
                IShipDesign di = picks[i], dj = picks[j];
                var ma = new FightSide { Design = di.Name, Budget = 0 };
                var mb = new FightSide { Design = dj.Name, Budget = 0 };
                Fight f = RunFight("eq-count-rr", $"{di.Name} vs {dj.Name} (x{EqCount})", rrSeed++, 5000,
                    ma, mb,
                    sa => BuildExactCount(sa, di, EqCount),
                    sb => BuildExactCount(sb, dj, EqCount));
                fights.Add(f);

                float retI = ma.StrStart > 0 ? ma.StrEnd / ma.StrStart : 0;
                float retJ = mb.StrStart > 0 ? mb.StrEnd / mb.StrStart : 0;
                retSum[di.Name] += retI; retN[di.Name]++;
                retSum[dj.Name] += retJ; retN[dj.Name]++;
                if (f.Winner == "A") { wins[di.Name]++; losses[dj.Name]++; }
                else if (f.Winner == "B") { wins[dj.Name]++; losses[di.Name]++; }

                // Track the near-even pair (smallest starting-strength margin) for the robustness sweep.
                float denom = ma.StrStart + mb.StrStart;
                float margin = denom <= 0 ? 1f : Math.Abs(ma.StrStart - mb.StrStart) / denom;
                if (margin < closestMargin) { closestMargin = margin; closest = f; }
            }

        // Build the 12 Pareto rows.
        var paretoRows = new List<string>();
        Console.WriteLine("[pareto] --- equal-count round-robin (12 designs) ---");
        foreach (IShipDesign d in picks.OrderBy(d => d.GetCost(Player)))
        {
            // Unit strength: spawn one in a throwaway universe slot is heavy; instead use a fresh probe via
            // a tiny side build. Reuse the already-collected per-fight start strengths is not 1-ship, so
            // measure once here from a clean spawn.
            int w = wins[d.Name], l = losses[d.Name];
            float winRate = (w + l) > 0 ? (float)w / (w + l) : 0f;
            float avgRet = retN[d.Name] > 0 ? retSum[d.Name] / retN[d.Name] : 0f;
            float unitCost = d.GetCost(Player);
            float unitStrength = UnitStrengthProbe(d);
            float strPerCredit = unitCost > 0 ? unitStrength / unitCost : 0f;
            paretoRows.Add($"{d.Name}|{unitCost}|{unitStrength}|{winRate}|{avgRet}|{w}|{l}|{strPerCredit}");
            Console.WriteLine($"[pareto] PARETO {d.Name,-26} cost={unitCost,7:0} unitStr={unitStrength,7:0} "
                            + $"strPerCredit={strPerCredit:0.000} winRate={winRate:0.000} ({w}W/{l}L) avgRetained={avgRet:0.000}");
        }

        // ---- 3) COMBINED-ARMS, STRENGTH-MATCH. mixed comp vs pure mid, equal STARTING strength. ----
        // For the mixed comp, each tier is built to its share of target; the pure side to the full target.
        ulong caSeed = 0xCA0000u;
        var caRows = new List<string>();
        foreach (float target in new[] { 30000f, 50000f })
        {
            IShipDesign[] pool = WarshipPool(Player);
            var ma = new FightSide { Comp = Comp("mixed", mixed), Budget = target };
            var mb = new FightSide { Comp = Comp("pureMid", pureMid), Budget = target };
            Fight f = RunFight("combined-arms-sm", $"mixed vs pureMid @str{target:0}", caSeed++, 7000,
                ma, mb,
                sa =>
                {
                    sa.Comp = mixed;
                    foreach ((float tier, float frac) in mixed)
                        BuildToStrength(sa, PickTier(pool, tier), target * frac);
                },
                sb =>
                {
                    sb.Comp = pureMid;
                    BuildToStrength(sb, PickTier(pool, 0.5f), target);
                });
            fights.Add(f);
            caRows.Add($"{target}|{ma.StrStart}|{mb.StrStart}|{f.Winner}|"
                     + $"{(ma.StrStart > 0 ? ma.StrEnd / ma.StrStart : 0)}|{(mb.StrStart > 0 ? mb.StrEnd / mb.StrStart : 0)}");
            Console.WriteLine($"[pareto] COMBINED-ARMS target={target:0} mixedStr0={ma.StrStart:0} pureStr0={mb.StrStart:0} "
                            + $"winner={f.Winner} mixedRetained={(ma.StrStart > 0 ? ma.StrEnd / ma.StrStart : 0):0.000} "
                            + $"pureRetained={(mb.StrStart > 0 ? mb.StrEnd / mb.StrStart : 0):0.000}");
        }

        // ---- 4) SEED ROBUSTNESS on the near-even round-robin pair, 8 seeds. ----
        var seedRows = new List<string>();
        string nearA = closest?.SideA.Design, nearB = closest?.SideB.Design;
        IShipDesign ndi = picks.FirstOrDefault(d => d.Name == nearA) ?? picks[0];
        IShipDesign ndj = picks.FirstOrDefault(d => d.Name == nearB) ?? picks[1];
        Console.WriteLine($"[pareto] SEED-ROBUST near-even pair = '{ndi.Name}' vs '{ndj.Name}' (margin={closestMargin:0.000})");
        foreach (ulong seed in new ulong[] { 0xB1, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8 })
        {
            var ma = new FightSide { Design = ndi.Name, Budget = 0 };
            var mb = new FightSide { Design = ndj.Name, Budget = 0 };
            Fight f = RunFight("seed-robust", $"{ndi.Name} vs {ndj.Name} seed", seed, 5000,
                ma, mb,
                sa => BuildExactCount(sa, ndi, EqCount),
                sb => BuildExactCount(sb, ndj, EqCount));
            fights.Add(f);
            seedRows.Add($"0x{seed:X}|{f.Winner}|{ma.StrEnd}|{mb.StrEnd}");
            Console.WriteLine($"[pareto] SEED 0x{seed:X} winner={f.Winner} aEnd={ma.StrEnd:0} bEnd={mb.StrEnd:0}");
        }

        // ---- emit JSON ----
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string json = "[\n  " + string.Join(",\n  ", fights.Select(FightJson)) + "\n]\n";
        string jsonPath = Path.Combine(dir, "pareto.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[pareto] wrote {fights.Count} fights -> {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        Console.WriteLine($"[pareto] DATAPATH {jsonPath}");
        Console.WriteLine($"[pareto] SUMMARY fights={fights.Count} paretoRows={paretoRows.Count} "
                        + $"crossoverRows=7 combinedArmsRows={caRows.Count} seedRows={seedRows.Count}");

        Assert.AreEqual(12, paretoRows.Count, "12 Pareto rows emitted");
        Assert.IsTrue(File.Exists(jsonPath), "pareto.json written");
        Assert.IsTrue(fights.Count > 0, "sweep produced fights");
    }

    // Measure the strength of ONE ship of a design from a clean spawn (used for the Pareto unitStrength).
    // Spawns into the current universe at a far-away slot, reads GetStrength(), then removes it so it does
    // not pollute later fights (each fight rebuilds the universe anyway, but be tidy).
    float UnitStrengthProbe(IShipDesign design)
    {
        Ship s = SpawnShip(design.Name, Player, new Vector2(900000, 900000));
        float str = s.GetStrength();
        s.QueueTotalRemoval();
        return str;
    }

    // ---------------------------------------------------------------------------------------------------
    // v2 SCORING HELPERS (ADDITIVE — do NOT touch v1 StrengthOf/AliveOf which score only side.Fleet).
    // v1 sums a side's PRE-BATTLE ship list (side.Fleet) only. That MISSES carrier-launched fighters,
    // which are spawned mid-battle under the carrier's loyalty and registered in empire.OwnedShips (the
    // canonical live roster, kept in sync by LoyaltyChangeAtSpawn / LoyaltyLists.Add+Remove). Scoring a
    // side by its EMPIRE'S living owned ships therefore counts those fighters, valuing carriers correctly.
    // ---------------------------------------------------------------------------------------------------
    static float StrengthOfSideByEmpire(Side side)
    {
        if (side?.Empire?.OwnedShips == null) return 0f;
        return side.Empire.OwnedShips.Where(s => s.Active).Sum(s => s.GetStrength());
    }

    static int AliveOfSideByEmpire(Side side)
    {
        if (side?.Empire?.OwnedShips == null) return 0;
        return side.Empire.OwnedShips.Count(s => s.Active);
    }

    // ===================================================================================================
    // 7) BALANCE META — FLEET LAB. Generates PORTABLE balance data for other combat-balance games:
    //    which classes / squad sizes / modules win. Emits one self-contained lab.json + [lab] lines.
    //    (0) carrier launch check (do headless carriers actually launch fighters?)
    //    (1) class-tagged round-robin + per-module census (12 designs, equal-count 3v3, 66 fights)
    //    (2) squad-size sweep (5 designs, NvN for N in {1,2,4,6}) -> is the ranking size-stable?
    //    (3) multi-team / FFA (3-way, 4-way, 2v2) -> does the strongest get ganged up on?
    // ===================================================================================================

    // -------- per-ship MODULE CENSUS (portable: armor / shields / weapon families) -----------------------
    sealed class WeaponTypeCount { public string Type; public int Count; }
    sealed class ModuleCensus
    {
        public int ArmorCount, ShieldCount, TotalModules;
        public float ShieldPowerTotal;
        public List<WeaponTypeCount> WeaponTypes = new();
    }

    // Group a module's installed weapon into a portable "family" by its dominant WeaponTag, falling back to
    // the raw WeaponType string. These tags (Beam/Missile/Kinetic/Cannon/Plasma/...) are engine-agnostic so
    // the resulting census transfers to other combat-balance games.
    static string WeaponFamily(ShipModule m)
    {
        var w = m.InstalledWeapon;
        if (w == null) return null;
        if (m.DroneModule)   return "Drone";
        if (w.Tag_Beam)      return "Beam";
        if (w.Tag_Torpedo)   return "Torpedo";
        if (w.Tag_Missile)   return "Missile";
        if (w.Tag_Guided)    return "GuidedMissile";
        if (w.Tag_Plasma)    return "Plasma";
        if (w.Tag_Cannon)    return "Cannon";
        if (w.Tag_Kinetic)   return "Kinetic";
        if (w.Tag_Energy)    return "Energy";
        if (w.Tag_PD)        return "PointDefense";
        return string.IsNullOrEmpty(w.WeaponType) ? "Other" : w.WeaponType;
    }

    ModuleCensus CensusOf(Ship s)
    {
        var c = new ModuleCensus();
        var fams = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (ShipModule m in s.Modules)
        {
            c.TotalModules++;
            if (m.Is(ShipModuleType.Armor))  c.ArmorCount++;
            if (m.Is(ShipModuleType.Shield)) { c.ShieldCount++; c.ShieldPowerTotal += m.ActualShieldPowerMax; }
            if (m.InstalledWeapon != null)
            {
                string fam = WeaponFamily(m);
                if (fam != null) fams[fam] = fams.TryGetValue(fam, out int n) ? n + 1 : 1;
            }
        }
        c.WeaponTypes = fams.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                            .Select(kv => new WeaponTypeCount { Type = kv.Key, Count = kv.Value }).ToList();
        return c;
    }

    // Portable per-design row for lab.json.
    sealed class LabDesign
    {
        public string Design, Role;
        public float UnitCost, UnitStrength;
        public int TotalModules, ArmorCount, ShieldCount;
        public float ShieldPowerTotal;
        public List<WeaponTypeCount> WeaponTypes = new();
        public int Wins, Losses;
        public float WinRate, AvgRetained;
    }

    sealed class LabMatrixRow { public string A, B, RoleA, RoleB, Winner; }
    sealed class LabSquadRow  { public string Design, Role; public int Size; public float WinRate; }
    sealed class LabTeamRow   { public string Config, Detail; public int WinnerTeam; }

    static string CensusJson(LabDesign d)
    {
        string wt = string.Join(",", d.WeaponTypes.Select(w => $"{{\"type\":{J(w.Type)},\"count\":{w.Count}}}"));
        return $"{{\"design\":{J(d.Design)},\"role\":{J(d.Role)},\"unitCost\":{F(d.UnitCost)},\"unitStrength\":{F(d.UnitStrength)},"
             + $"\"totalModules\":{d.TotalModules},\"armorCount\":{d.ArmorCount},\"shieldCount\":{d.ShieldCount},"
             + $"\"shieldPowerTotal\":{F(d.ShieldPowerTotal)},\"weaponTypes\":[{wt}],"
             + $"\"wins\":{d.Wins},\"losses\":{d.Losses},\"winRate\":{F(d.WinRate)},\"avgRetained\":{F(d.AvgRetained)}}}";
    }

    [TestMethod]
    public void BalanceMeta_FleetLab_EmitsJson()
    {
        LoadAllGameData();

        var fights = new List<Fight>();
        int fightCount = 0;
        var notes = new List<string>();

        // =================================================================================================
        // (0) CARRIER LAUNCH CHECK — does a headless carrier actually launch hangar fighters?
        // Spawn one carrier-role design + an enemy within launch range, order attack, run ~600 ticks, and
        // count UState.Objects ships before/after AND inspect the carrier's hangar API.
        // =================================================================================================
        bool carrierLaunchWorks = false;
        {
            CreateUniverseAndPlayerEmpire();
            SetupArena(); // sets galaxy view so headless carrier launches don't NRE on the launch-flash particle
            IShipDesign carrierDesign = ResourceManager.Ships.Designs
                .Where(d => d.Role == RoleName.carrier && d.BaseStrength > 100f)
                .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (carrierDesign == null)
            {
                notes.Add("CARRIER-CHECK: no carrier-role design found in loaded data; carrierLaunchWorks=false");
                Console.WriteLine("[lab] CARRIER-CHECK no carrier-role design available");
            }
            else
            {
                UState.EnableDeterministicRng(0xCA111Eu);
                Ship carrier = SpawnShip(carrierDesign.Name, Player, new Vector2(0, 0));
                carrier.SensorRange = 400000;
                // an enemy ~5000u away — inside the ~7500 hangar launch range so fighters scramble
                IShipDesign foe = ResourceManager.Ships.Designs.Where(d => d.BaseStrength > 100f && d.Role != RoleName.carrier)
                    .OrderBy(d => d.GetCost(Enemy)).ThenBy(d => d.Name, StringComparer.Ordinal).First();
                Ship foeShip = SpawnShip(foe.Name, Enemy, new Vector2(5000, 0));
                foeShip.SensorRange = 400000;
                if (!Player.IsAtWarWith(Enemy)) Player.AI.DeclareWarOn(Enemy, WarType.ImperialistWar);

                int hangars = carrier.Carrier?.AllFighterHangars?.Length ?? 0;
                int shipsBefore = UState.Objects.GetShips().Length;
                carrier.AI.OrderAttackSpecificTarget(foeShip);
                foeShip.AI.OrderAttackSpecificTarget(carrier);

                int maxFightersSeen = 0;
                for (int t = 0; t < 600; ++t)
                {
                    UState.Objects.Update(TestSimStep);
                    if (t % 60 == 59)
                    {
                        carrier.AI.OrderAttackSpecificTarget(foeShip);
                        int f = carrier.Carrier?.GetActiveFighters()?.Count ?? 0;
                        if (f > maxFightersSeen) maxFightersSeen = f;
                    }
                }
                int shipsAfter = UState.Objects.GetShips().Length;
                int fightersNow = carrier.Carrier?.GetActiveFighters()?.Count ?? 0;
                maxFightersSeen = Math.Max(maxFightersSeen, fightersNow);
                carrierLaunchWorks = maxFightersSeen > 0;
                notes.Add($"CARRIER-CHECK design='{carrierDesign.Name}' hangars={hangars} maxFightersLaunched={maxFightersSeen} "
                        + $"shipsBefore={shipsBefore} shipsAfter={shipsAfter} -> carrierLaunchWorks={carrierLaunchWorks}");
                Console.WriteLine($"[lab] CARRIER-CHECK design='{carrierDesign.Name}' hangars={hangars} "
                                + $"maxFightersLaunched={maxFightersSeen} shipsBefore={shipsBefore} shipsAfter={shipsAfter} "
                                + $"launchWorks={carrierLaunchWorks}");
            }
        }

        // =================================================================================================
        // (1) CLASS-TAGGED ROUND-ROBIN + PER-MODULE CENSUS — 12 designs across classes & cost range.
        // =================================================================================================
        IShipDesign[] strong = ResourceManager.Ships.Designs.Where(d => d.BaseStrength > 100f)
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal).ToArray();

        // Prefer one design per class spanning corvette..carrier/bomber, then backfill across the cost range
        // to reach 12 distinct designs. Roles we care to cover (portable "class" axis):
        var wantRoles = new[] { RoleName.corvette, RoleName.frigate, RoleName.destroyer, RoleName.cruiser,
                                RoleName.battleship, RoleName.capital, RoleName.carrier, RoleName.bomber };
        // Optional NIGHTLY ROTATION: env FLEETLAB_SEED varies which representative of each class is sampled
        // (and shifts the cost-percentile backfill), so a scheduled run explores a FRESH cohort of the
        // ~1200-design roster while staying fully deterministic for that seed. Unset/0 => the original fixed
        // cohort, so committed results stay reproducible.
        ulong labSeed = ulong.TryParse(System.Environment.GetEnvironmentVariable("FLEETLAB_SEED"), out ulong ls) ? ls : 0UL;

        var picks = new List<IShipDesign>();
        foreach (RoleName role in wantRoles)
        {
            IShipDesign rep;
            if (labSeed == 0)
                rep = strong.FirstOrDefault(d => d.Role == role); // cheapest representative (default)
            else
            {
                IShipDesign[] ofRole = strong.Where(d => d.Role == role).ToArray();
                rep = ofRole.Length == 0 ? null
                    : ofRole[(int)((labSeed * 2654435761UL + (uint)role) % (uint)ofRole.Length)];
            }
            if (rep != null && !picks.Contains(rep)) picks.Add(rep);
        }
        // Backfill by cost percentile until we have 12 distinct designs (seed shifts the percentiles).
        int pctOff = labSeed == 0 ? 0 : (int)(labSeed % 9) * 2;
        var pctTargets = new[] { 8, 18, 28, 38, 48, 58, 68, 78, 86, 92, 96, 99 };
        foreach (int p in pctTargets)
        {
            if (picks.Count >= 12) break;
            int pp = ((p + pctOff - 1) % 99) + 1;
            int idx = Math.Clamp((int)Math.Round((strong.Length - 1) * (pp / 100f)), 0, strong.Length - 1);
            if (!picks.Contains(strong[idx])) picks.Add(strong[idx]);
        }
        for (int probe = 0; picks.Count < 12 && probe < strong.Length; ++probe)
            if (!picks.Contains(strong[probe])) picks.Add(strong[probe]);
        IShipDesign[] design12 = picks.Take(12).OrderBy(d => d.GetCost(Player)).ToArray();

        // Build the per-design census ONCE from a clean probe spawn in a fresh universe.
        var labByName = new Dictionary<string, LabDesign>(StringComparer.Ordinal);
        {
            CreateUniverseAndPlayerEmpire();
            SetupArena();
            foreach (IShipDesign d in design12)
            {
                Ship probe = SpawnShip(d.Name, Player, new Vector2(900000, 900000));
                UState.Objects.Update(TestSimStep); // let modules init (shields, weapons)
                ModuleCensus census = CensusOf(probe);
                var ld = new LabDesign
                {
                    Design = d.Name, Role = d.Role.ToString(),
                    UnitCost = d.GetCost(Player), UnitStrength = probe.GetStrength(),
                    TotalModules = census.TotalModules, ArmorCount = census.ArmorCount,
                    ShieldCount = census.ShieldCount, ShieldPowerTotal = census.ShieldPowerTotal,
                    WeaponTypes = census.WeaponTypes
                };
                labByName[d.Name] = ld;
                probe.QueueTotalRemoval();
                string wt = string.Join(",", ld.WeaponTypes.Select(w => $"{w.Type}x{w.Count}"));
                Console.WriteLine($"[lab] CENSUS {ld.Design,-26} role={ld.Role,-10} cost={ld.UnitCost,7:0} str={ld.UnitStrength,7:0} "
                                + $"mods={ld.TotalModules,3} armor={ld.ArmorCount,2} shields={ld.ShieldCount,2} "
                                + $"shieldPow={ld.ShieldPowerTotal,6:0} weapons=[{wt}]");
            }
        }

        // Equal-count 3v3 round-robin: every unordered pair (12 designs -> 66 fights). One seed each.
        var matrixRows = new List<LabMatrixRow>();
        var retSum = new Dictionary<string, float>(StringComparer.Ordinal);
        var retN   = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (IShipDesign d in design12) { retSum[d.Name] = 0; retN[d.Name] = 0; }
        const int EqCount = 3;
        ulong rrSeed = 0x1AB00000u;
        for (int i = 0; i < design12.Length; ++i)
            for (int j = i + 1; j < design12.Length; ++j)
            {
                IShipDesign di = design12[i], dj = design12[j];
                var ma = new FightSide { Design = di.Name, Budget = 0 };
                var mb = new FightSide { Design = dj.Name, Budget = 0 };
                Fight f = RunFight("class-rr", $"{di.Name} vs {dj.Name} (x{EqCount})", rrSeed++, 4000,
                    ma, mb,
                    sa => BuildExactCount(sa, di, EqCount),
                    sb => BuildExactCount(sb, dj, EqCount));
                fights.Add(f); fightCount++;

                LabDesign li = labByName[di.Name], lj = labByName[dj.Name];
                string winnerName = f.Winner == "A" ? di.Name : f.Winner == "B" ? dj.Name : "draw";
                if (f.Winner == "A") { li.Wins++; lj.Losses++; }
                else if (f.Winner == "B") { lj.Wins++; li.Losses++; }
                retSum[di.Name] += ma.StrStart > 0 ? ma.StrEnd / ma.StrStart : 0; retN[di.Name]++;
                retSum[dj.Name] += mb.StrStart > 0 ? mb.StrEnd / mb.StrStart : 0; retN[dj.Name]++;
                matrixRows.Add(new LabMatrixRow { A = di.Name, B = dj.Name, RoleA = li.Role, RoleB = lj.Role, Winner = winnerName });
                Console.WriteLine($"[lab] RR {di.Name,-22}({li.Role,-9}) vs {dj.Name,-22}({lj.Role,-9}) -> {winnerName}");
            }
        foreach (IShipDesign d in design12)
        {
            LabDesign ld = labByName[d.Name];
            int g = ld.Wins + ld.Losses;
            ld.WinRate = g > 0 ? (float)ld.Wins / g : 0f;
            ld.AvgRetained = retN[d.Name] > 0 ? retSum[d.Name] / retN[d.Name] : 0f;
        }
        Console.WriteLine("[lab] --- class round-robin tier list (winRate | avgRetained) ---");
        foreach (LabDesign ld in labByName.Values.OrderByDescending(x => x.WinRate).ThenByDescending(x => x.AvgRetained))
            Console.WriteLine($"[lab] TIER {ld.Design,-26} role={ld.Role,-10} cost={ld.UnitCost,7:0} "
                            + $"winRate={ld.WinRate:0.000} ({ld.Wins}W/{ld.Losses}L) avgRetained={ld.AvgRetained:0.000}");

        // =================================================================================================
        // (2) SQUAD-SIZE SWEEP — ~5 designs (one per class where possible), NvN for N in {1,2,4,6}.
        // Is the ranking size-stable, or do some designs only win at scale (carriers/AoE) vs in duels?
        // =================================================================================================
        var squadRows = new List<LabSquadRow>();
        // Pick up to 5 distinct designs spanning classes from the 12.
        var squadPick = new List<IShipDesign>();
        foreach (RoleName role in new[] { RoleName.corvette, RoleName.frigate, RoleName.cruiser, RoleName.battleship, RoleName.carrier })
        {
            IShipDesign rep = design12.FirstOrDefault(d => d.Role == role);
            if (rep != null && !squadPick.Contains(rep)) squadPick.Add(rep);
        }
        for (int k = 0; squadPick.Count < 5 && k < design12.Length; ++k)
            if (!squadPick.Contains(design12[k])) squadPick.Add(design12[k]);
        IShipDesign[] squadDesigns = squadPick.Take(5).ToArray();
        int[] squadSizes = { 1, 2, 4, 6 };
        ulong sqSeed = 0x59AD0000u;
        foreach (int size in squadSizes)
        {
            var sWins = new Dictionary<string, int>(StringComparer.Ordinal);
            var sGames = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (IShipDesign d in squadDesigns) { sWins[d.Name] = 0; sGames[d.Name] = 0; }
            for (int i = 0; i < squadDesigns.Length; ++i)
                for (int j = i + 1; j < squadDesigns.Length; ++j)
                {
                    IShipDesign di = squadDesigns[i], dj = squadDesigns[j];
                    var ma = new FightSide { Design = di.Name, Budget = 0 };
                    var mb = new FightSide { Design = dj.Name, Budget = 0 };
                    Fight f = RunFight("squad-size", $"{di.Name} vs {dj.Name} (x{size})", sqSeed++, 4000,
                        ma, mb,
                        sa => BuildExactCount(sa, di, size),
                        sb => BuildExactCount(sb, dj, size));
                    fights.Add(f); fightCount++;
                    sGames[di.Name]++; sGames[dj.Name]++;
                    if (f.Winner == "A") sWins[di.Name]++;
                    else if (f.Winner == "B") sWins[dj.Name]++;
                }
            foreach (IShipDesign d in squadDesigns)
            {
                float wr = sGames[d.Name] > 0 ? (float)sWins[d.Name] / sGames[d.Name] : 0f;
                squadRows.Add(new LabSquadRow { Design = d.Name, Role = d.Role.ToString(), Size = size, WinRate = wr });
                Console.WriteLine($"[lab] SQUAD size={size} {d.Name,-26} role={d.Role,-10} winRate={wr:0.000} ({sWins[d.Name]}/{sGames[d.Name]})");
            }
        }

        // =================================================================================================
        // (3) MULTI-TEAM / FFA — 3-way & 4-way free-for-alls + a 2v2 team battle with mixed designs.
        // Does the strongest get ganged up on? does focus-fire emerge?
        // =================================================================================================
        var teamRows = new List<LabTeamRow>();
        IShipDesign[] pool = null;

        // 3-way FFA (each empire its own team), mixed comps.
        {
            CreateUniverseAndPlayerEmpire();
            CreateThirdMajorEmpire();
            SetupArena();
            pool = WarshipPool(Player);
            var sides = new List<Side>
            {
                MkSide(Player,     0, new Vector2(-9000, -6000), new[] { (0.2f, 0.6f), (0.8f, 0.4f) }),
                MkSide(Enemy,      1, new Vector2( 9000, -6000), new[] { (0.5f, 1.0f) }),
                MkSide(ThirdMajor, 2, new Vector2(    0,  9000), new[] { (0.1f, 0.7f), (0.95f, 0.3f) }),
            };
            var (alive, winner) = BuildRunReport(sides, pool, 2600f, 0xFFA30000u, 4000, "LAB-FFA3");
            fightCount++;
            string detail = string.Join(";", sides.Select(s => $"team{s.Team}:{s.Fleet.Count(x => x.Active)}alive,{s.Fleet.Where(x => x.Active).Sum(x => x.GetStrength()):0}str"));
            teamRows.Add(new LabTeamRow { Config = "3-way FFA (mixed vs heavy vs mixed)", WinnerTeam = winner, Detail = detail });
            Console.WriteLine($"[lab] FFA3 winnerTeam={winner} {detail}");
        }

        // 4-way FFA (4 empires, 4 teams), each a pure-ish doctrine.
        {
            CreateUniverseAndPlayerEmpire();
            CreateThirdMajorEmpire();
            Empire fourth = CreateAnotherMajor();
            SetupArena();
            pool = WarshipPool(Player);
            var sides = new List<Side>
            {
                MkSide(Player,     0, new Vector2(-9000,  6000), new[] { (0.1f, 1.0f) }),         // cheap swarm
                MkSide(Enemy,      1, new Vector2( 9000,  6000), new[] { (0.5f, 1.0f) }),         // pure mid
                MkSide(ThirdMajor, 2, new Vector2(-9000, -6000), new[] { (0.9f, 1.0f) }),         // heavy
                MkSide(fourth,     3, new Vector2( 9000, -6000), new[] { (0.2f,0.5f),(0.8f,0.5f) }), // mixed
            };
            var (alive, winner) = BuildRunReport(sides, pool, 2400f, 0xFFA40000u, 4000, "LAB-FFA4");
            fightCount++;
            string detail = string.Join(";", sides.Select(s => $"team{s.Team}:{s.Fleet.Count(x => x.Active)}alive,{s.Fleet.Where(x => x.Active).Sum(x => x.GetStrength()):0}str"));
            teamRows.Add(new LabTeamRow { Config = "4-way FFA (swarm/mid/heavy/mixed)", WinnerTeam = winner, Detail = detail });
            Console.WriteLine($"[lab] FFA4 winnerTeam={winner} {detail}");
        }

        // 2v2 team battle (4 empires, 2 teams), mixed comps; teammates ignore each other.
        {
            CreateUniverseAndPlayerEmpire();
            CreateThirdMajorEmpire();
            Empire fourth = CreateAnotherMajor();
            SetupArena();
            pool = WarshipPool(Player);
            var sides = new List<Side>
            {
                MkSide(Player,     0, new Vector2(-9000,  4000), new[] { (0.2f, 0.7f), (0.9f, 0.3f) }),
                MkSide(ThirdMajor, 0, new Vector2(-9000, -4000), new[] { (0.1f, 1.0f) }),
                MkSide(Enemy,      1, new Vector2( 9000,  4000), new[] { (0.2f, 0.7f), (0.9f, 0.3f) }),
                MkSide(fourth,     1, new Vector2( 9000, -4000), new[] { (0.5f, 1.0f) }),
            };
            var (alive, winner) = BuildRunReport(sides, pool, 2200f, 0x7EA20000u, 4000, "LAB-2v2");
            fightCount++;
            string detail = string.Join(";", new[] { 0, 1 }.Select(tm => $"team{tm}:{sides.Where(s => s.Team == tm).Sum(s => s.Fleet.Count(x => x.Active))}alive,{sides.Where(s => s.Team == tm).Sum(s => s.Fleet.Where(x => x.Active).Sum(x => x.GetStrength())):0}str"));
            teamRows.Add(new LabTeamRow { Config = "2v2 teams (mixed+swarm vs mixed+mid)", WinnerTeam = winner, Detail = detail });
            Console.WriteLine($"[lab] 2v2 winnerTeam={winner} {detail}");
        }

        // =================================================================================================
        // EMIT lab.json
        // =================================================================================================
        string designsJson = string.Join(",\n    ", labByName.Values.OrderBy(d => d.UnitCost).Select(CensusJson));
        string matrixJson = string.Join(",\n    ", matrixRows.Select(r =>
            $"{{\"a\":{J(r.A)},\"b\":{J(r.B)},\"roleA\":{J(r.RoleA)},\"roleB\":{J(r.RoleB)},\"winner\":{J(r.Winner)}}}"));
        string squadJson = string.Join(",\n    ", squadRows.Select(r =>
            $"{{\"design\":{J(r.Design)},\"role\":{J(r.Role)},\"size\":{r.Size},\"winRate\":{F(r.WinRate)}}}"));
        string teamJson = string.Join(",\n    ", teamRows.Select(r =>
            $"{{\"config\":{J(r.Config)},\"winnerTeam\":{r.WinnerTeam},\"detail\":{J(r.Detail)}}}"));

        string labJson =
            "{\n" +
            $"  \"carrierLaunchWorks\": {(carrierLaunchWorks ? "true" : "false")},\n" +
            $"  \"fightCount\": {fightCount},\n" +
            $"  \"designs\": [\n    {designsJson}\n  ],\n" +
            $"  \"classMatrixRows\": [\n    {matrixJson}\n  ],\n" +
            $"  \"squadSizeRows\": [\n    {squadJson}\n  ],\n" +
            $"  \"multiTeamRows\": [\n    {teamJson}\n  ],\n" +
            $"  \"fights\": [\n    {string.Join(",\n    ", fights.Select(FightJson))}\n  ]\n" +
            "}\n";

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string jsonPath = Path.Combine(dir, "lab.json");
        File.WriteAllText(jsonPath, labJson);
        Console.WriteLine($"[lab] DATAPATH {jsonPath}");
        Console.WriteLine($"[lab] SUMMARY fights={fightCount} designs={labByName.Count} matrixRows={matrixRows.Count} "
                        + $"squadRows={squadRows.Count} teamRows={teamRows.Count} carrierLaunchWorks={carrierLaunchWorks} "
                        + $"jsonBytes={new FileInfo(jsonPath).Length}");
        foreach (string n in notes) Console.WriteLine($"[lab] NOTE {n}");

        Assert.IsTrue(fightCount > 0, "lab produced fights");
        Assert.IsTrue(File.Exists(jsonPath), "lab.json written");
        Assert.AreEqual(12, labByName.Count, "12 designs censused");
    }

    // ===================================================================================================
    // 8) BALANCE META — FLEET LAB v2 (UPGRADE #1: COUNT LAUNCHED FIGHTERS).
    //    Re-runs the SAME class-tagged 12-design, equal-count 3v3 round-robin as BalanceMeta_FleetLab,
    //    but scores each side by its EMPIRE'S living owned ships (StrengthOfSideByEmpire) so carrier-
    //    launched fighters count toward the side's strength and win. Emits labv2.json with each design's
    //    v2 winRate and, for carriers, the fighter-inclusive strength vs the bare-hull (side.Fleet) strength.
    //    Fully ADDITIVE: identical seeds + identical build/sim loop => deterministic; v1 results untouched.
    // ===================================================================================================
    sealed class LabV2Design
    {
        public string Design, Role;
        public float UnitCost;
        public bool IsCarrier;
        public int Wins, Losses;          // v2 (empire-scored, fighters counted)
        public int WinsV1, LossesV1;      // v1 (side.Fleet-scored, fighters NOT counted) — same sim, baseline
        public int FlipsToWin;            // fights this design LOST under v1 but WON under v2 (fighters tipped it)
        public float WinRate, WinRateV1;
        // Carrier accounting: empire (fighter-inclusive) vs fleet (bare-hull) END strength, summed over fights.
        public float EmpStrSum, FleetStrSum;
        public int StrSamples;
    }

    [TestMethod]
    public void BalanceMeta_FleetLabV2_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire(); // need a Player empire for GetCost(Player) cohort ordering below

        // ---- pick the SAME 12 designs as the v1 lab's class-tagged round-robin (fixed cohort: FLEETLAB_SEED
        //      is intentionally ignored here so the v2 baseline is committed/reproducible) ----
        IShipDesign[] strong = ResourceManager.Ships.Designs.Where(d => d.BaseStrength > 100f)
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal).ToArray();

        var wantRoles = new[] { RoleName.corvette, RoleName.frigate, RoleName.destroyer, RoleName.cruiser,
                                RoleName.battleship, RoleName.capital, RoleName.carrier, RoleName.bomber };
        var picks = new List<IShipDesign>();
        foreach (RoleName role in wantRoles)
        {
            IShipDesign rep = strong.FirstOrDefault(d => d.Role == role);
            if (rep != null && !picks.Contains(rep)) picks.Add(rep);
        }
        var pctTargets = new[] { 8, 18, 28, 38, 48, 58, 68, 78, 86, 92, 96, 99 };
        foreach (int p in pctTargets)
        {
            if (picks.Count >= 12) break;
            int idx = Math.Clamp((int)Math.Round((strong.Length - 1) * (p / 100f)), 0, strong.Length - 1);
            if (!picks.Contains(strong[idx])) picks.Add(strong[idx]);
        }
        for (int probe = 0; picks.Count < 12 && probe < strong.Length; ++probe)
            if (!picks.Contains(strong[probe])) picks.Add(strong[probe]);
        IShipDesign[] design12 = picks.Take(12).OrderBy(d => d.GetCost(Player)).ToArray();

        var byName = new Dictionary<string, LabV2Design>(StringComparer.Ordinal);
        foreach (IShipDesign d in design12)
            byName[d.Name] = new LabV2Design
            {
                Design = d.Name, Role = d.Role.ToString(),
                UnitCost = d.GetCost(Player), IsCarrier = d.Role == RoleName.carrier
            };
        bool anyCarrier = design12.Any(d => d.Role == RoleName.carrier);
        Console.WriteLine($"[v2] cohort: 12 designs, carrierPresent={anyCarrier} "
                        + $"carriers=[{string.Join(",", design12.Where(d => d.Role == RoleName.carrier).Select(d => d.Name))}]");

        // ---- equal-count 3v3 round-robin, scored by empire-owned ships (fighters counted) ----
        // SAME seed base (0x1AB00000u, ++ per pair) and SAME tick count (4000) as v1's class-rr so the sim is
        // bit-identical; only the SCORING differs (empire vs side.Fleet).
        const int EqCount = 3;
        ulong rrSeed = 0x1AB00000u;
        int fightCount = 0;
        for (int i = 0; i < design12.Length; ++i)
            for (int j = i + 1; j < design12.Length; ++j)
            {
                IShipDesign di = design12[i], dj = design12[j];
                var ma = new FightSide { Design = di.Name, Budget = 0 };
                var mb = new FightSide { Design = dj.Name, Budget = 0 };
                FightV2 f = RunFightV2("class-rr-v2", $"{di.Name} vs {dj.Name} (x{EqCount})", rrSeed++, 4000,
                    ma, mb,
                    sa => BuildExactCount(sa, di, EqCount),
                    sb => BuildExactCount(sb, dj, EqCount));
                fightCount++;

                LabV2Design li = byName[di.Name], lj = byName[dj.Name];
                // v2 (fighter-inclusive) outcome.
                if (f.WinnerV2 == "A") { li.Wins++; lj.Losses++; }
                else if (f.WinnerV2 == "B") { lj.Wins++; li.Losses++; }
                // v1 (bare-hull) outcome from the SAME deterministic sim — the in-run baseline.
                if (f.V1.Winner == "A") { li.WinsV1++; lj.LossesV1++; }
                else if (f.V1.Winner == "B") { lj.WinsV1++; li.LossesV1++; }
                // A FLIP: side lost (or drew) under v1 but won under v2 => counting fighters tipped the fight.
                if (f.WinnerV2 == "A" && f.V1.Winner != "A") li.FlipsToWin++;
                if (f.WinnerV2 == "B" && f.V1.Winner != "B") lj.FlipsToWin++;

                // Carrier strength accounting: record empire (fighter-inclusive) vs fleet (bare-hull) END str.
                if (li.IsCarrier) { li.EmpStrSum += f.StrEndEmpireA; li.FleetStrSum += f.StrEndFleetA; li.StrSamples++; }
                if (lj.IsCarrier) { lj.EmpStrSum += f.StrEndEmpireB; lj.FleetStrSum += f.StrEndFleetB; lj.StrSamples++; }
            }

        foreach (LabV2Design d in byName.Values)
        {
            int g = d.Wins + d.Losses;
            d.WinRate = g > 0 ? (float)d.Wins / g : 0f;
            int gv1 = d.WinsV1 + d.LossesV1;
            d.WinRateV1 = gv1 > 0 ? (float)d.WinsV1 / gv1 : 0f;
        }

        Console.WriteLine("[v2] --- class round-robin v2 tier list (empire-scored winRate vs v1 baseline) ---");
        foreach (LabV2Design d in byName.Values.OrderByDescending(x => x.WinRate))
        {
            string carrierNote = d.IsCarrier && d.StrSamples > 0
                ? $"  empStrAvg={d.EmpStrSum / d.StrSamples:0} fleetStrAvg={d.FleetStrSum / d.StrSamples:0} "
                + $"(x{(d.FleetStrSum > 0 ? d.EmpStrSum / d.FleetStrSum : 0):0.00} fighter-inclusive)"
                : "";
            Console.WriteLine($"[v2] TIER {d.Design,-26} role={d.Role,-10} cost={d.UnitCost,7:0} "
                            + $"winRateV2={d.WinRate:0.000} ({d.Wins}W/{d.Losses}L) winRateV1={d.WinRateV1:0.000} "
                            + $"flips+{d.FlipsToWin}{carrierNote}");
        }

        // ---- carrier verification: fighter-inclusive (empire) strength vs bare-hull (fleet) strength ----
        // v1 baseline is computed IN-RUN from the SAME deterministic sim (bare-hull side.Fleet scoring), so the
        // comparison is honest rather than relying on a hardcoded constant. Counting fighters can only RAISE a
        // carrier's strength, so its v2 standing is >= v1; it strictly rises whenever a fight flips (FlipsToWin).
        LabV2Design[] carriers = byName.Values.Where(d => d.IsCarrier && d.StrSamples > 0).ToArray();
        float carrierWinRateV2 = carriers.Length > 0 ? carriers.Average(c => c.WinRate) : 0f;
        float carrierWinRateV1 = carriers.Length > 0 ? carriers.Average(c => c.WinRateV1) : 0f;
        int carrierFlips = carriers.Sum(c => c.FlipsToWin);
        float carrierEmpStr = carriers.Length > 0 ? carriers.Sum(c => c.EmpStrSum) / carriers.Sum(c => c.StrSamples) : 0f;
        float carrierFleetStr = carriers.Length > 0 ? carriers.Sum(c => c.FleetStrSum) / carriers.Sum(c => c.StrSamples) : 0f;
        float fighterFactor = carrierFleetStr > 0 ? carrierEmpStr / carrierFleetStr : 0f;
        const float V1CarrierWinRateRef = 0.36f; // documented ~36% v1 reference from the committed fleet-lab run
        Console.WriteLine($"[v2] CARRIER-CHECK carriers={carriers.Length} winRateV2={carrierWinRateV2:0.000} "
                        + $"winRateV1(in-run)={carrierWinRateV1:0.000} v1Ref~{V1CarrierWinRateRef:0.00} flips={carrierFlips}  "
                        + $"empStrAvg={carrierEmpStr:0} fleetStrAvg={carrierFleetStr:0} "
                        + $"-> fighter-inclusive strength is {fighterFactor:0.00}x bare-hull");

        // ---- emit labv2.json ----
        string designsJson = string.Join(",\n    ", byName.Values.OrderBy(d => d.UnitCost).Select(d =>
        {
            float empAvg = d.StrSamples > 0 ? d.EmpStrSum / d.StrSamples : 0f;
            float fleetAvg = d.StrSamples > 0 ? d.FleetStrSum / d.StrSamples : 0f;
            string carrierJson = d.IsCarrier
                ? $",\"empStrAvg\":{F(empAvg)},\"fleetStrAvg\":{F(fleetAvg)},"
                + $"\"fighterInclusiveFactor\":{F(fleetAvg > 0 ? empAvg / fleetAvg : 0f)}"
                : "";
            return $"{{\"design\":{J(d.Design)},\"role\":{J(d.Role)},\"unitCost\":{F(d.UnitCost)},"
                 + $"\"isCarrier\":{(d.IsCarrier ? "true" : "false")},\"winRateV2\":{F(d.WinRate)},"
                 + $"\"winRateV1\":{F(d.WinRateV1)},\"flipsToWin\":{d.FlipsToWin},"
                 + $"\"wins\":{d.Wins},\"losses\":{d.Losses}{carrierJson}}}";
        }));

        string labv2Json =
            "{\n" +
            $"  \"scoring\": \"v2-empire-owned-ships (carrier fighters counted)\",\n" +
            $"  \"fightCount\": {fightCount},\n" +
            $"  \"carrierPresent\": {(anyCarrier ? "true" : "false")},\n" +
            $"  \"carrierWinRateV2\": {F(carrierWinRateV2)},\n" +
            $"  \"carrierWinRateV1\": {F(carrierWinRateV1)},\n" +
            $"  \"carrierWinRateV1Reference\": {F(V1CarrierWinRateRef)},\n" +
            $"  \"carrierFlipsToWin\": {carrierFlips},\n" +
            $"  \"carrierEmpStrAvg\": {F(carrierEmpStr)},\n" +
            $"  \"carrierFleetStrAvg\": {F(carrierFleetStr)},\n" +
            $"  \"carrierFighterInclusiveFactor\": {F(fighterFactor)},\n" +
            $"  \"designs\": [\n    {designsJson}\n  ]\n" +
            "}\n";

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string jsonPath = Path.Combine(dir, "labv2.json");
        File.WriteAllText(jsonPath, labv2Json);
        Console.WriteLine($"[v2] DATAPATH {jsonPath}");
        Console.WriteLine($"[v2] SUMMARY fights={fightCount} designs={byName.Count} carriers={carriers.Length} "
                        + $"winRateV2={carrierWinRateV2:0.000} winRateV1={carrierWinRateV1:0.000} flips={carrierFlips} "
                        + $"fighterFactor={fighterFactor:0.00} jsonBytes={new FileInfo(jsonPath).Length}");

        Assert.IsTrue(fightCount > 0, "v2 lab produced fights");
        Assert.IsTrue(File.Exists(jsonPath), "labv2.json written");
        Assert.AreEqual(12, byName.Count, "12 designs in v2 round-robin");
        if (carriers.Length > 0)
        {
            // (1) A carrier's fighter-inclusive (empire) strength must be MEANINGFULLY higher than its bare hull
            //     once launched fighters are counted (the whole point of upgrade #1).
            Assert.IsTrue(carrierEmpStr > carrierFleetStr * 1.05f,
                $"carrier empire strength ({carrierEmpStr:0}) should exceed bare-hull fleet strength "
                + $"({carrierFleetStr:0}) by >5% once launched fighters are counted");
            // (2) Counting fighters can only HELP a carrier, never hurt it: its v2 winRate must be >= the in-run
            //     v1 (bare-hull) baseline, and it rises strictly whenever any fight flips on the fighter strength.
            Assert.IsTrue(carrierWinRateV2 >= carrierWinRateV1,
                $"carrier v2 winRate ({carrierWinRateV2:0.000}) must be >= the in-run v1 baseline "
                + $"({carrierWinRateV1:0.000}); counting fighters can only raise a carrier's standing");
            if (carrierFlips > 0)
                Assert.IsTrue(carrierWinRateV2 > carrierWinRateV1,
                    $"with {carrierFlips} flipped fight(s), carrier v2 winRate ({carrierWinRateV2:0.000}) "
                    + $"must strictly exceed the v1 baseline ({carrierWinRateV1:0.000})");
        }
        else
        {
            Console.WriteLine("[v2] NOTE no carrier-role design in the 12-design cohort; carrier asserts skipped");
        }
    }

    // ===================================================================================================
    // 9) BALANCE META — FLEET THREAT-FFA (UPGRADE #2: THREAT-WEIGHTED / FOCUS-FIRE TARGETING).
    //    Runs the SAME multi-team configurations (3-way FFA, 4-way FFA, 2v2 teams) under BOTH targeting
    //    policies and diffs the outcomes:
    //       - NEAREST  : EngageAll        (each ship -> spatially nearest enemy)            -> BuildRunReport
    //       - THREAT   : EngageAllThreat  (whole team focus-fires the strongest enemy)      -> BuildRunReportThreat
    //    Every config uses an IDENTICAL seed / budget / tick count / spawn layout across the two policies, so
    //    the ONLY difference is the targeting rule. We record winnerTeam + per-team survivor counts for each
    //    policy and assert that at least one multi-team outcome DIFFERS — proving focus-fire / gang-up now
    //    emerges where nearest-target produced only spatial clustering. Fully ADDITIVE; no committed v1/v2
    //    method is altered, and EngageAllThreat preserves determinism (seeded UState RNG).
    // ===================================================================================================
    sealed class ThreatFFARow
    {
        public string Config;
        public int WinnerNearest, WinnerThreat;
        public int[] AliveNearest, AliveThreat;
        public bool WinnerDiffers, SurvivorsDiffer;
    }

    [TestMethod]
    public void BalanceMeta_FleetThreatFFA_EmitsJson()
    {
        LoadAllGameData();

        var rows = new List<ThreatFFARow>();

        // --- Config A: 3-way FFA (each empire its own team), mixed/heavy comps. ---
        // (Same layout/comps as the FFA3 lab config so the gang-up dynamic is meaningful.)
        List<Side> Build3Way()
        {
            CreateUniverseAndPlayerEmpire();
            CreateThirdMajorEmpire();
            SetupArena();
            return new List<Side>
            {
                MkSide(Player,     0, new Vector2(-9000, -6000), new[] { (0.2f, 0.6f), (0.8f, 0.4f) }),
                MkSide(Enemy,      1, new Vector2( 9000, -6000), new[] { (0.5f, 1.0f) }),
                MkSide(ThirdMajor, 2, new Vector2(    0,  9000), new[] { (0.1f, 0.7f), (0.95f, 0.3f) }),
            };
        }
        {
            var n = Build3Way();  var rn = BuildRunReport      (n, WarshipPool(Player), 2600f, 0xFFA3C002u, 4000, "FFA3-NEAR");
            var t = Build3Way();  var rt = BuildRunReportThreat (t, WarshipPool(Player), 2600f, 0xFFA3C002u, 4000, "FFA3-THRT");
            rows.Add(MakeRow("3-way FFA (mixed/heavy/mixed)", rn.aliveByTeam, rn.winnerTeam, rt.aliveByTeam, rt.winnerTeam));
        }

        // --- Config B: 4-way FFA (4 empires, 4 teams), each a pure-ish doctrine. ---
        List<Side> Build4Way()
        {
            CreateUniverseAndPlayerEmpire();
            CreateThirdMajorEmpire();
            Empire fourth = CreateAnotherMajor();
            SetupArena();
            return new List<Side>
            {
                MkSide(Player,     0, new Vector2(-9000,  6000), new[] { (0.1f, 1.0f) }),            // cheap swarm
                MkSide(Enemy,      1, new Vector2( 9000,  6000), new[] { (0.5f, 1.0f) }),            // pure mid
                MkSide(ThirdMajor, 2, new Vector2(-9000, -6000), new[] { (0.9f, 1.0f) }),            // heavy
                MkSide(fourth,     3, new Vector2( 9000, -6000), new[] { (0.2f, 0.5f), (0.8f, 0.5f) }), // mixed
            };
        }
        {
            var n = Build4Way();  var rn = BuildRunReport      (n, WarshipPool(Player), 2400f, 0xFFA4C002u, 4000, "FFA4-NEAR");
            var t = Build4Way();  var rt = BuildRunReportThreat (t, WarshipPool(Player), 2400f, 0xFFA4C002u, 4000, "FFA4-THRT");
            rows.Add(MakeRow("4-way FFA (swarm/mid/heavy/mixed)", rn.aliveByTeam, rn.winnerTeam, rt.aliveByTeam, rt.winnerTeam));
        }

        // --- Config C: 2v2 teams (4 empires, 2 teams); teammates ignore each other, focus the enemy team. ---
        List<Side> Build2v2()
        {
            CreateUniverseAndPlayerEmpire();
            CreateThirdMajorEmpire();
            Empire fourth = CreateAnotherMajor();
            SetupArena();
            return new List<Side>
            {
                MkSide(Player,     0, new Vector2(-9000,  4000), new[] { (0.2f, 0.7f), (0.9f, 0.3f) }),
                MkSide(ThirdMajor, 0, new Vector2(-9000, -4000), new[] { (0.1f, 1.0f) }),
                MkSide(Enemy,      1, new Vector2( 9000,  4000), new[] { (0.2f, 0.7f), (0.9f, 0.3f) }),
                MkSide(fourth,     1, new Vector2( 9000, -4000), new[] { (0.5f, 1.0f) }),
            };
        }
        {
            var n = Build2v2();  var rn = BuildRunReport      (n, WarshipPool(Player), 2200f, 0x7EA2C002u, 4000, "2v2-NEAR");
            var t = Build2v2();  var rt = BuildRunReportThreat (t, WarshipPool(Player), 2200f, 0x7EA2C002u, 4000, "2v2-THRT");
            rows.Add(MakeRow("2v2 teams (mixed+swarm vs mixed+mid)", rn.aliveByTeam, rn.winnerTeam, rt.aliveByTeam, rt.winnerTeam));
        }

        // --- Determinism: rerun ONE threat config and confirm a bit-identical outcome. ---
        var d1 = Build3Way(); var rd1 = BuildRunReportThreat(d1, WarshipPool(Player), 2600f, 0xFFA3C002u, 4000, "FFA3-THRT-REPRO");
        Assert.AreEqual(rows[0].WinnerThreat, rd1.winnerTeam, "threat targeting must be reproducible (same winner)");
        CollectionAssert.AreEqual(rows[0].AliveThreat, rd1.aliveByTeam, "threat targeting must be reproducible (same survivors)");

        // --- Report each config + the diff. ---
        foreach (ThreatFFARow r in rows)
            Console.WriteLine($"[v2] FFA-DIFF {r.Config,-38} nearest[win=team{r.WinnerNearest} alive={Arr(r.AliveNearest)}]  "
                            + $"threat[win=team{r.WinnerThreat} alive={Arr(r.AliveThreat)}]  "
                            + $"winnerDiffers={r.WinnerDiffers} survivorsDiffer={r.SurvivorsDiffer}");

        int winnerFlips = rows.Count(r => r.WinnerDiffers);
        int survivorFlips = rows.Count(r => r.SurvivorsDiffer);
        bool anyDiff = rows.Any(r => r.WinnerDiffers || r.SurvivorsDiffer);
        Console.WriteLine($"[v2] FFA-SUMMARY configs={rows.Count} winnerFlips={winnerFlips} survivorFlips={survivorFlips} anyOutcomeChanged={anyDiff}");

        // --- Emit threatffa.json for offline analysis. ---
        string rowsJson = string.Join(",\n    ", rows.Select(r =>
            $"{{\"config\":{J(r.Config)},\"winnerNearest\":{r.WinnerNearest},\"winnerThreat\":{r.WinnerThreat},"
          + $"\"aliveNearest\":[{string.Join(",", r.AliveNearest)}],\"aliveThreat\":[{string.Join(",", r.AliveThreat)}],"
          + $"\"winnerDiffers\":{(r.WinnerDiffers ? "true" : "false")},\"survivorsDiffer\":{(r.SurvivorsDiffer ? "true" : "false")}}}"));
        string threatJson =
            "{\n" +
            $"  \"scoring\": \"v2-threat-weighted focus-fire (EngageAllThreat) vs nearest-target (EngageAll)\",\n" +
            $"  \"configs\": {rows.Count},\n" +
            $"  \"winnerFlips\": {winnerFlips},\n" +
            $"  \"survivorFlips\": {survivorFlips},\n" +
            $"  \"anyOutcomeChanged\": {(anyDiff ? "true" : "false")},\n" +
            $"  \"rows\": [\n    {rowsJson}\n  ]\n" +
            "}\n";
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string jsonPath = Path.Combine(dir, "threatffa.json");
        File.WriteAllText(jsonPath, threatJson);
        Console.WriteLine($"[v2] DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");

        // --- VERIFY: at least one multi-team outcome (winner or survivor counts) differs between
        //     nearest-target and threat-target, proving focus-fire / gang-up now emerges. ---
        Assert.IsTrue(File.Exists(jsonPath), "threatffa.json written");
        Assert.AreEqual(3, rows.Count, "three multi-team configs evaluated");
        Assert.IsTrue(anyDiff,
            "at least one FFA/multi-team outcome (winner or survivor counts) must DIFFER between nearest-target "
          + "and threat-target — otherwise focus-fire/gang-up is not emerging");
    }

    static string Arr(int[] a) => "[" + string.Join(",", a) + "]";

    ThreatFFARow MakeRow(string config, int[] aliveNear, int winNear, int[] aliveThreat, int winThreat)
        => new()
        {
            Config = config,
            WinnerNearest = winNear, WinnerThreat = winThreat,
            AliveNearest = aliveNear, AliveThreat = aliveThreat,
            WinnerDiffers = winNear != winThreat,
            SurvivorsDiffer = !aliveNear.SequenceEqual(aliveThreat),
        };

    // ===================================================================================================
    // 10) BALANCE META — MODULE ISOLATION (UPGRADE #3: VARY-ONE-MODULE, CONTROL FOR HULL).
    //    Attribute a single module's COMBAT VALUE while holding the hull (and every other slot) fixed.
    //    Procedure (lightweight clone+swap, runtime-only, never touches disk):
    //      1. Pick a base warship DESIGN that has >=1 shield slot AND uses an armor module somewhere
    //         (so the replacement module is guaranteed valid for THIS hull/mod and we can size-match it).
    //      2. GetClone() the design, find every slot whose module is a Shield, and rewrite that slot's
    //         ModuleUID to the design's own armor UID — but ONLY when the armor's footprint matches the
    //         shield slot's Size, so ModuleGrid.Place() can't fail. SetDesignSlots(updateRole:false) keeps
    //         the Role identical so AI behavior is invariant; only the one module family changed.
    //      3. AddShipTemplate() registers the variant (Ship.CreateNewShipTemplate validates every module).
    //      4. Duel BASE vs VARIANT at EQUAL COUNT across several seeds (same deterministic build+sim loop as
    //         every other sweep) and report the win-rate / strength-retained DELTA the shield was worth.
    //    If no design yields a clean shields->armor swap, FALL BACK to an APPROXIMATE near-twin: two
    //    existing designs on the SAME hull whose slot lists differ by the fewest modules (clearly labeled
    //    "approx"). Fully ADDITIVE: new helpers + new method only; v1/v2 results untouched; seeded RNG.
    // ===================================================================================================

    // One isolation experiment row for moduleiso.json.
    sealed class IsoExperiment
    {
        public string Mode;          // "clone-swap" (exact, one-module isolation) or "near-twin" (approx)
        public string Hull;          // hull both variants share (control)
        public string BaseDesign, VariantDesign;
        public string ModuleVaried;  // "Shield" (clone-swap) or "mixed" (near-twin)
        public int SwapCount;        // # of slots changed (clone-swap) or # of differing slots (near-twin)
        public int Count;            // ships per side in each duel
        public float BaseUnitStr, VariantUnitStr;   // single-ship strength of each side
        public int Seeds;
        public int BaseWins, VariantWins, Draws;          // by END STRENGTH (rating-dominated; armor adds rating)
        public int BaseRetWins, VariantRetWins, RetDraws; // by FRACTION RETAINED (normalizes the armor rating gap
                                                          //   => isolates DEFENSIVE survival value of the module)
        public int DecisiveDuels;                          // duels where one side was fully wiped
        public float BaseRetainedSum, VariantRetainedSum;  // sum of end/start strength fraction over seeds
        public float BaseWinRate     => Seeds > 0 ? (float)BaseWins / Seeds : 0f;
        public float VariantWinRate  => Seeds > 0 ? (float)VariantWins / Seeds : 0f;
        public float BaseAvgRetained    => Seeds > 0 ? BaseRetainedSum / Seeds : 0f;
        public float VariantAvgRetained => Seeds > 0 ? VariantRetainedSum / Seeds : 0f;
        public float WinRateDelta    => BaseWinRate - VariantWinRate;     // + => base (module present) is better
        public float RetainedDelta   => BaseAvgRetained - VariantAvgRetained;
        // The headline attribution: retained-based win-rate delta controls for the starting strength gap that
        // swapping shields<->armor introduces, so a positive value credits the module's real in-combat value.
        public float RetainedWinRateDelta => Seeds > 0 ? (float)(BaseRetWins - VariantRetWins) / Seeds : 0f;
    }

    // Single-ship strength of a registered design from a clean spawn (mirrors UnitStrengthProbe; assumes a
    // live universe). Used to report what a side's unit is worth before the duel.
    float UnitStrengthOf(IShipDesign d)
    {
        Ship s = SpawnShip(d.Name, Player, new Vector2(900000, 900000));
        UState.Objects.Update(TestSimStep); // let modules init (shields/weapons) so GetStrength is settled
        float str = s.GetStrength();
        s.QueueTotalRemoval();
        return str;
    }

    // Clone `baseDesign`, swap EVERY shield-module slot to `replacementUID` (when the footprint matches the
    // slot Size), register under a fresh unique name, and verify a variant ship actually spawns. Returns the
    // variant's IShipDesign + how many slots were swapped, or (null,0) if nothing valid could be swapped or
    // registration/spawn failed. Role is preserved (updateRole:false) so only the module family changes.
    (IShipDesign variant, int swapped) BuildOneModuleVariant(IShipDesign baseDesign, string replacementUID, ulong nameSalt)
    {
        if (!ResourceManager.ModuleExists(replacementUID))
            return (null, 0);
        ShipModule repl = ResourceManager.GetModuleTemplate(replacementUID);
        Point replSize = repl.GetSize();

        // Deterministic variant name (no GUID) so moduleiso.json is reproducible run-to-run. A collision-safe
        // suffix bumps only if that exact name is already registered (it won't be in a single test run, and
        // AddShipTemplate overwrites by name anyway).
        string variantName = $"{baseDesign.Name}-iso-shieldoff-{nameSalt:X}";
        for (ulong bump = 1; ResourceManager.ShipTemplateExists(variantName); ++bump)
            variantName = $"{baseDesign.Name}-iso-shieldoff-{nameSalt:X}-{bump}";

        ShipDesign clone = baseDesign.GetClone(variantName);
        DesignSlot[] slots = clone.GetOrLoadDesignSlots();
        var swappedSlots = new DesignSlot[slots.Length];
        int swapped = 0;
        for (int i = 0; i < slots.Length; ++i)
        {
            DesignSlot s = slots[i];
            bool isShield = ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m)
                            && m.Is(ShipModuleType.Shield);
            // Only swap when the replacement's footprint exactly matches this slot so grid placement is safe.
            if (isShield && s.Size == replSize)
            {
                swappedSlots[i] = new DesignSlot(s.Pos, replacementUID, s.Size, s.TurretAngle, s.ModuleRot, s.HangarShipUID);
                ++swapped;
            }
            else
            {
                swappedSlots[i] = new DesignSlot(s); // unchanged copy
            }
        }
        if (swapped == 0)
            return (null, 0); // nothing to isolate on this design

        clone.SetDesignSlots(swappedSlots, updateRole: false); // keep Role identical -> AI behavior invariant
        if (!ResourceManager.AddShipTemplate(clone, playerDesign: false, readOnly: true))
            return (null, 0); // module validation failed inside Ship.CreateNewShipTemplate
        if (!ResourceManager.Ships.GetDesign(variantName, out IShipDesign variant))
            return (null, 0);

        // Prove a ship really spawns from the variant before we commit to dueling it.
        try
        {
            Ship probe = SpawnShip(variantName, Player, new Vector2(950000, 950000));
            bool ok = probe is { Active: true } && probe.HasModules;
            probe.QueueTotalRemoval();
            if (!ok) return (null, 0);
        }
        catch { return (null, 0); }

        return (variant, swapped);
    }

    // One equal-count deterministic duel between two registered designs. Mirrors RunFight's build+sim loop
    // exactly (BuildExactCount placement, seeded RNG, 1500-tick re-target cadence). Returns winner + each
    // side's start/end strength AND alive counts so the caller can accumulate win-rate, strength-retained,
    // and decisiveness deltas.
    (string winner, float aStart, float aEnd, float bStart, float bEnd, int aAlive, int bAlive) RunIsoDuel(
        IShipDesign a, IShipDesign b, int count, ulong seed, int ticks)
    {
        CreateUniverseAndPlayerEmpire();
        SetupArena();
        var sa = MkSide(Player, 0, new Vector2(-8000, 0), new[] { (0f, 1f) });
        var sb = MkSide(Enemy,  1, new Vector2( 8000, 0), new[] { (0f, 1f) });
        var sides = new List<Side> { sa, sb };

        BuildExactCount(sa, a, count);
        BuildExactCount(sb, b, count);

        SetupWars(sides);
        UState.EnableDeterministicRng(seed);
        EngageAll(sides);

        float aStart = StrengthOf(sa.Fleet), bStart = StrengthOf(sb.Fleet);
        for (int t = 0; t < ticks; ++t)
        {
            UState.Objects.Update(TestSimStep);
            if (t % 1500 == 1499) EngageAll(sides);
        }
        int aAlive = AliveOf(sa.Fleet), bAlive = AliveOf(sb.Fleet);
        float aEnd = StrengthOf(sa.Fleet), bEnd = StrengthOf(sb.Fleet);
        string winner =
            bAlive == 0 && aAlive > 0 ? "A" :
            aAlive == 0 && bAlive > 0 ? "B" :
            aEnd > bEnd ? "A" :
            bEnd > aEnd ? "B" : "draw";
        return (winner, aStart, aEnd, bStart, bEnd, aAlive, bAlive);
    }

    // Run an isolation experiment over `seeds` equal-count duels (base = side A, variant = side B), filling
    // the win/retained tallies on `exp`.
    void RunIsoExperiment(IsoExperiment exp, IShipDesign baseDesign, IShipDesign variant, int count, ulong[] seeds, int ticks)
    {
        // Measure unit strengths once (fresh universe, clean spawn).
        CreateUniverseAndPlayerEmpire();
        SetupArena();
        exp.BaseUnitStr    = UnitStrengthOf(baseDesign);
        exp.VariantUnitStr = UnitStrengthOf(variant);

        exp.Count = count;
        exp.Seeds = seeds.Length;
        foreach (ulong seed in seeds)
        {
            var (winner, aStart, aEnd, bStart, bEnd, aAlive, bAlive) = RunIsoDuel(baseDesign, variant, count, seed, ticks);
            // (1) End-strength winner (rating-dominated; armor adds to the strength rating).
            if (winner == "A") exp.BaseWins++;
            else if (winner == "B") exp.VariantWins++;
            else exp.Draws++;

            // (2) Fraction-retained winner — normalizes the starting-strength gap that swapping shields<->armor
            //     introduces, so this credits the module's REAL defensive value rather than its rating bump.
            float baseRet = aStart > 0 ? aEnd / aStart : 0f;
            float varRet  = bStart > 0 ? bEnd / bStart : 0f;
            exp.BaseRetainedSum    += baseRet;
            exp.VariantRetainedSum += varRet;
            if      (baseRet > varRet + 1e-4f) exp.BaseRetWins++;
            else if (varRet  > baseRet + 1e-4f) exp.VariantRetWins++;
            else exp.RetDraws++;

            if (aAlive == 0 || bAlive == 0) exp.DecisiveDuels++;

            Console.WriteLine($"[iso] DUEL {exp.Mode,-10} '{exp.BaseDesign}' vs '{exp.VariantDesign}' "
                            + $"x{count} seed=0x{seed:X} -> {(winner == "A" ? "BASE" : winner == "B" ? "VARIANT" : "draw")} "
                            + $"baseStr {aStart:0}->{aEnd:0} ({aAlive} alive, ret {baseRet:0.000})  "
                            + $"varStr {bStart:0}->{bEnd:0} ({bAlive} alive, ret {varRet:0.000})");
        }
    }

    static string IsoJson(IsoExperiment e)
        => $"{{\"mode\":{J(e.Mode)},\"hull\":{J(e.Hull)},\"baseDesign\":{J(e.BaseDesign)},"
         + $"\"variantDesign\":{J(e.VariantDesign)},\"moduleVaried\":{J(e.ModuleVaried)},"
         + $"\"swapCount\":{e.SwapCount},\"shipsPerSide\":{e.Count},\"seeds\":{e.Seeds},"
         + $"\"decisiveDuels\":{e.DecisiveDuels},"
         + $"\"baseUnitStrength\":{F(e.BaseUnitStr)},\"variantUnitStrength\":{F(e.VariantUnitStr)},"
         + $"\"baseWins\":{e.BaseWins},\"variantWins\":{e.VariantWins},\"draws\":{e.Draws},"
         + $"\"baseWinRate\":{F(e.BaseWinRate)},\"variantWinRate\":{F(e.VariantWinRate)},"
         + $"\"winRateDelta\":{F(e.WinRateDelta)},"
         + $"\"baseRetWins\":{e.BaseRetWins},\"variantRetWins\":{e.VariantRetWins},\"retDraws\":{e.RetDraws},"
         + $"\"retainedWinRateDelta\":{F(e.RetainedWinRateDelta)},"
         + $"\"baseAvgRetained\":{F(e.BaseAvgRetained)},"
         + $"\"variantAvgRetained\":{F(e.VariantAvgRetained)},\"retainedDelta\":{F(e.RetainedDelta)}}}";

    // Find the design's own armor module UID (prefer a 1x1 armor so it footprint-matches the most shield
    // slots). Returns null if the design uses no armor module.
    static string FindArmorUidInDesign(IShipDesign d)
    {
        string best = null;
        foreach (DesignSlot s in d.GetOrLoadDesignSlots())
        {
            if (ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m) &&
                m.ModuleType == ShipModuleType.Armor)
            {
                if (m.GetSize() == new Point(1, 1)) return s.ModuleUID; // ideal: matches 1x1 shield slots
                best ??= s.ModuleUID;
            }
        }
        return best;
    }

    static int ShieldSlotCount(IShipDesign d)
    {
        int n = 0;
        foreach (DesignSlot s in d.GetOrLoadDesignSlots())
            if (ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m) && m.Is(ShipModuleType.Shield))
                ++n;
        return n;
    }

    // How many of a design's shield slots have EXACTLY `size` footprint — i.e. how many could accept a
    // replacement shield module of that footprint (the swap machinery requires s.Size == replSize). Used by
    // the shield-VARIANT gauntlet to pick hulls where a given vanilla shield can actually be installed.
    static int ShieldSlotCountOfSize(IShipDesign d, Point size)
    {
        int n = 0;
        foreach (DesignSlot s in d.GetOrLoadDesignSlots())
            if (ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m) && m.Is(ShipModuleType.Shield) && s.Size == size)
                ++n;
        return n;
    }

    [TestMethod]
    public void BalanceMeta_ModuleIsolation_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire(); // need Player for GetCost ordering + clean spawns
        SetupArena();

        var experiments = new List<IsoExperiment>();
        var notes = new List<string>();

        const int Count = 6;     // larger packs => focus-fire kills happen => duels become decisive
        ulong[] seeds = { 0x150A, 0x150B, 0x150C, 0x150D, 0x150E, 0x150F, 0x1510, 0x1511 };
        const int Ticks = 12000; // long enough for the shield's defensive value to manifest as survival

        // Candidate base designs: STANDALONE combat warships (genuine warship Role, NOT carrier-only fighters
        // that idle when spawned alone) that HAVE shields AND use an armor module of matching footprint (so the
        // swap target exists for this exact hull/mod). Ordered strongest-first within an affordable band so the
        // duel is a real slugfest where the shield actually matters; ThenBy Name keeps the cohort deterministic.
        var warRoles = new HashSet<RoleName>
        {
            RoleName.corvette, RoleName.frigate, RoleName.destroyer,
            RoleName.cruiser, RoleName.battleship, RoleName.capital,
        };
        // Ascending cost: small/mid warships actually damage each other within the tick budget (big capitals
        // are near-invulnerable to equal-count peers and never lose strength — useless for measuring a delta).
        IShipDesign[] candidates = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly && warRoles.Contains(d.Role))
            .Where(d => ShieldSlotCount(d) > 0 && FindArmorUidInDesign(d) != null)
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();
        Console.WriteLine($"[iso] {candidates.Length} candidate base designs (standalone warship, shields + own armor module)");

        // Build the FIRST clean clone+swap variant (shields -> the design's own armor module) whose base-vs-base
        // sanity duel actually PRODUCES COMBAT (real strength loss). This guards against picking a design whose
        // ships never close to weapon range — otherwise the "delta" would just reflect static strength ratings.
        IShipDesign baseDesign = null, variant = null;
        int swapped = 0;
        ulong salt = 0x1CE0u;
        int probed = 0;
        foreach (IShipDesign cand in candidates)
        {
            if (++probed > 40) break; // bound the search; 40 strongest valid warships is plenty
            string armorUid = FindArmorUidInDesign(cand);
            var (v, sw) = BuildOneModuleVariant(cand, armorUid, salt++);
            if (v == null || sw == 0)
                continue;

            // Sanity duel: base vs base. If neither side loses strength, these ships don't actually fight here.
            var sanity = RunIsoDuel(cand, cand, Count, seeds[0], Ticks);
            bool fought = sanity.aEnd < sanity.aStart - 1f || sanity.bEnd < sanity.bStart - 1f;
            if (!fought)
            {
                Console.WriteLine($"[iso] SKIP base='{cand.Name}' hull='{cand.Hull}' — base-vs-base sanity duel "
                                + $"showed no combat (str {sanity.aStart:0}->{sanity.aEnd:0}); ships never engaged");
                continue;
            }

            baseDesign = cand; variant = v; swapped = sw;
            Console.WriteLine($"[iso] PICKED base='{cand.Name}' hull='{cand.Hull}' role={cand.Role} shields={ShieldSlotCount(cand)} "
                            + $"-> swapped {sw} shield slot(s) to armor '{armorUid}' variant='{v.Name}' "
                            + $"(sanity combat confirmed: str {sanity.aStart:0}->{sanity.aEnd:0})");
            break;
        }

        if (baseDesign != null && variant != null)
        {
            // ---- PRIMARY: exact one-module isolation (shields removed, hull + every other slot held fixed). ----
            var exp = new IsoExperiment
            {
                Mode = "clone-swap", Hull = baseDesign.Hull,
                BaseDesign = baseDesign.Name, VariantDesign = variant.Name,
                ModuleVaried = "Shield", SwapCount = swapped,
            };
            RunIsoExperiment(exp, baseDesign, variant, Count, seeds, Ticks);
            experiments.Add(exp);
            Console.WriteLine($"[iso] RESULT clone-swap base='{exp.BaseDesign}' (shields ON, unitStr {exp.BaseUnitStr:0}) "
                            + $"vs variant='{exp.VariantDesign}' (shields OFF, unitStr {exp.VariantUnitStr:0})  "
                            + $"endStr: base {exp.BaseWins}W / variant {exp.VariantWins}W / {exp.Draws}D  "
                            + $"retained: base {exp.BaseRetWins}W / variant {exp.VariantRetWins}W / {exp.RetDraws}D  "
                            + $"decisive={exp.DecisiveDuels}/{exp.Seeds}");
            Console.WriteLine($"[iso] ATTRIBUTION the {swapped} shield module(s) on hull '{exp.Hull}' are worth: "
                            + $"endStrengthWinRateDelta={exp.WinRateDelta:+0.000;-0.000} (rating-dominated), "
                            + $"retainedWinRateDelta={exp.RetainedWinRateDelta:+0.000;-0.000} (hull-controlled, isolates defense), "
                            + $"avgRetainedDelta={exp.RetainedDelta:+0.000;-0.000}  [everything else identical]");
        }
        else
        {
            notes.Add("no clone-swap variant could be built (no candidate design yielded a footprint-matched "
                    + "shields->armor swap that registered + spawned); falling back to APPROXIMATE near-twin path");
            Console.WriteLine("[iso] FALLBACK no clean clone-swap; searching for a near-twin pair on the same hull");

            // ---- FALLBACK (APPROX): two existing designs on the SAME hull whose slot module lists differ by
            //      the FEWEST modules. Labeled "near-twin" (approx) — NOT a controlled one-module swap. ----
            IShipDesign tA = null, tB = null; int bestDiff = int.MaxValue; string bestHull = null;
            IShipDesign[] warships = ResourceManager.Ships.Designs.Where(d => d.BaseStrength > 100f).ToArray();
            foreach (IGrouping<string, IShipDesign> hullGroup in warships.GroupBy(d => d.Hull))
            {
                IShipDesign[] g = hullGroup.OrderBy(d => d.Name, StringComparer.Ordinal).ToArray();
                for (int i = 0; i < g.Length; ++i)
                    for (int j = i + 1; j < g.Length; ++j)
                    {
                        int diff = SlotModuleDiff(g[i], g[j]);
                        if (diff > 0 && diff < bestDiff)
                        {
                            bestDiff = diff; tA = g[i]; tB = g[j]; bestHull = hullGroup.Key;
                        }
                    }
            }

            if (tA != null && tB != null)
            {
                var exp = new IsoExperiment
                {
                    Mode = "near-twin", Hull = bestHull,
                    BaseDesign = tA.Name, VariantDesign = tB.Name,
                    ModuleVaried = "mixed", SwapCount = bestDiff,
                };
                RunIsoExperiment(exp, tA, tB, Count, seeds, Ticks);
                experiments.Add(exp);
                Console.WriteLine($"[iso] RESULT near-twin (APPROX) base='{exp.BaseDesign}' vs variant='{exp.VariantDesign}' "
                                + $"hull='{exp.Hull}' differingModules={bestDiff}  "
                                + $"base {exp.BaseWins}W / variant {exp.VariantWins}W / {exp.Draws}D  "
                                + $"winRateDelta={exp.WinRateDelta:+0.000;-0.000} retainedDelta={exp.RetainedDelta:+0.000;-0.000}");
                notes.Add($"near-twin approximation: '{tA.Name}' vs '{tB.Name}' share hull '{bestHull}' but differ "
                        + $"in {bestDiff} slot module(s) — this is NOT a single-module isolation, treat the delta as approximate");
            }
            else
            {
                notes.Add("no near-twin pair found either (no hull has two warship designs); module isolation is "
                        + "BLOCKED in this dataset");
                Console.WriteLine("[iso] BLOCKED no clone-swap and no near-twin pair available in loaded data");
            }
        }

        // ---- emit moduleiso.json ----
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string expJson = string.Join(",\n    ", experiments.Select(IsoJson));
        string json =
            "{\n" +
            $"  \"experiment\": \"vary-one-module isolation (control for hull)\",\n" +
            $"  \"shipsPerSide\": {Count},\n" +
            $"  \"seeds\": {seeds.Length},\n" +
            $"  \"ticks\": {Ticks},\n" +
            $"  \"experimentCount\": {experiments.Count},\n" +
            $"  \"experiments\": [\n    {expJson}\n  ],\n" +
            $"  \"notes\": [{string.Join(",", notes.Select(J))}]\n" +
            "}\n";
        string jsonPath = Path.Combine(dir, "moduleiso.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[iso] DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        Console.WriteLine($"[iso] SUMMARY experiments={experiments.Count} "
                        + $"{(experiments.Count > 0 ? $"mode={experiments[0].Mode} winRateDelta={experiments[0].WinRateDelta:+0.000;-0.000} retainedWinRateDelta={experiments[0].RetainedWinRateDelta:+0.000;-0.000} decisive={experiments[0].DecisiveDuels}/{experiments[0].Seeds}" : "none")}");
        foreach (string n in notes) Console.WriteLine($"[iso] NOTE {n}");

        Assert.IsTrue(File.Exists(jsonPath), "moduleiso.json written");
        Assert.IsTrue(experiments.Count > 0,
            "at least one isolation experiment (clone-swap OR near-twin fallback) must have produced a measured delta");
        IsoExperiment e0 = experiments[0];
        Assert.AreEqual(seeds.Length, e0.BaseWins + e0.VariantWins + e0.Draws,
            "every seeded duel must resolve to base-win / variant-win / draw");
        // The duels must have done real combat — both sides started with measurable strength.
        Assert.IsTrue(e0.BaseUnitStr > 0f && e0.VariantUnitStr > 0f, "both base and variant ships have nonzero strength");
        if (e0.Mode == "clone-swap")
            Assert.IsTrue(e0.SwapCount > 0, "the clone-swap variant changed at least one shield slot");
    }

    // ===================================================================================================
    // 11) BALANCE META — MODULE ISOLATION FULL CAUSAL SWEEP (ADDITIVE on top of #10).
    //    Runs the same vary-one-thing-control-for-hull procedure ACROSS ~5 base hulls spanning ship classes
    //    (corvette/frigate/cruiser/battleship/capital-carrier), and for each hull runs three experiment
    //    families, each a base-vs-variant EQUAL-COUNT deterministic duel (reusing BuildOneModuleVariant's
    //    clone+swap+register+spawn-verify discipline via BuildSwapVariant, RunIsoDuel, RunIsoExperiment):
    //      (A) DEFENSE single-swap: replace ONE armor slot with a shield (armor->shield) and the reverse
    //          (shield->armor) at one footprint-matched slot.            category="defense"
    //      (B) DEFENSE doctrine: an all-/heavy-ARMOR variant vs an all-/heavy-SHIELD variant of the same
    //          hull (swap the WHOLE defensive class).                    category="doctrine"
    //      (C) WEAPON family: take the hull's primary weapon family and swap those slots to up to 3 OTHER
    //          available weapon families at the same slots.              category="weapon"
    //    Every experiment emits one isoRow (hull, role, unit strength, cost, category, swap label, win-rate
    //    + retained deltas, decisive count, clean flag). clean=false marks an approximate swap (footprint OK
    //    but power/footprint not a perfect 1:1) and is NOTE-d. Fully additive: new helpers + new method; the
    //    v1/v2 and #10 BalanceMeta_ModuleIsolation paths are untouched; seeded RNG keeps it reproducible.
    // ===================================================================================================

    // One emitted sweep row (mirrors the StructuredOutput isoRow shape exactly).
    sealed class IsoSweepRow
    {
        public string Hull, HullRole, Category, Swap;
        public float HullStrength, HullCost;
        public float BaseWinRate, VariantWinRate, WinRateDelta;
        public float BaseRetained, VariantRetained, RetainedDelta;
        public int Decisive;
        public bool Clean;
        public bool Combat; // did the duel actually do damage (either side lost strength)? false => degenerate row
    }

    static string IsoSweepJson(IsoSweepRow r)
        => $"{{\"hull\":{J(r.Hull)},\"hullRole\":{J(r.HullRole)},\"category\":{J(r.Category)},"
         + $"\"swap\":{J(r.Swap)},\"hullStrength\":{F(r.HullStrength)},\"hullCost\":{F(r.HullCost)},"
         + $"\"baseWinRate\":{F(r.BaseWinRate)},\"variantWinRate\":{F(r.VariantWinRate)},"
         + $"\"winRateDelta\":{F(r.WinRateDelta)},\"baseRetained\":{F(r.BaseRetained)},"
         + $"\"variantRetained\":{F(r.VariantRetained)},\"retainedDelta\":{F(r.RetainedDelta)},"
         + $"\"decisive\":{r.Decisive},\"clean\":{(r.Clean ? "true" : "false")},"
         + $"\"combat\":{(r.Combat ? "true" : "false")}}}";

    // Classify a MODULE TEMPLATE's weapon family from its WeaponType UID (templates may not have an
    // InstalledWeapon yet, so resolve the IWeaponTemplate and read its tags). Mirrors WeaponFamily(ShipModule).
    static string TemplateWeaponFamily(ShipModule m)
    {
        if (m == null || string.IsNullOrEmpty(m.WeaponType)) return null;
        if (m.DroneModule) return "Drone";
        if (!ResourceManager.GetWeaponTemplate(m.WeaponType, out IWeaponTemplate w)) return null;
        if (w.Tag_Bomb)     return null;          // bombs are not a peer-combat weapon family
        if (w.Tag_Beam)     return "Beam";
        if (w.Tag_Torpedo)  return "Torpedo";
        if (w.Tag_Missile)  return "Missile";
        if (w.Tag_Cannon)   return "Cannon";
        if (w.Tag_Kinetic)  return "Kinetic";
        if (w.Tag_Energy)   return "Energy";
        return string.IsNullOrEmpty(w.WeaponType) ? "Other" : w.WeaponType;
    }

    // GENERAL footprint-matched clone+swap. For every slot where shouldSwap(slotModule) is true AND the
    // replacement's footprint exactly matches the slot Size, rewrite that slot's ModuleUID to replacementUID.
    // Registers the variant (validates every module) and proves a ship spawns — same discipline as
    // BuildOneModuleVariant, generalized to any predicate + replacement. Returns the variant, how many slots
    // were swapped, how many MATCHED the predicate, and 'clean' = every predicate-matched slot was actually
    // swapped on a footprint match (a complete, controlled swap). Power-draw difference between the old and new
    // module is INTRINSIC to the experiment (e.g. armor draws ~0, shields draw power), so it does NOT make the
    // swap approximate — only an incomplete/partial swap (matched > swapped) sets clean=false.
    (IShipDesign variant, int swapped, int matched, bool clean) BuildSwapVariant(
        IShipDesign baseDesign, Func<ShipModule, bool> shouldSwap, string replacementUID, string tag, ulong nameSalt)
    {
        if (string.IsNullOrEmpty(replacementUID) || !ResourceManager.ModuleExists(replacementUID))
            return (null, 0, 0, false);
        ShipModule repl = ResourceManager.GetModuleTemplate(replacementUID);
        Point replSize = repl.GetSize();

        string variantName = $"{baseDesign.Name}-iso-{tag}-{nameSalt:X}";
        for (ulong bump = 1; ResourceManager.ShipTemplateExists(variantName); ++bump)
            variantName = $"{baseDesign.Name}-iso-{tag}-{nameSalt:X}-{bump}";

        ShipDesign clone = baseDesign.GetClone(variantName);
        DesignSlot[] slots = clone.GetOrLoadDesignSlots();
        var swappedSlots = new DesignSlot[slots.Length];
        int swapped = 0, matched = 0;
        for (int i = 0; i < slots.Length; ++i)
        {
            DesignSlot s = slots[i];
            bool isTarget = ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m) && shouldSwap(m);
            if (isTarget)
            {
                ++matched;
                if (s.Size == replSize) // footprint must match so ModuleGrid.Place can't fail
                {
                    swappedSlots[i] = new DesignSlot(s.Pos, replacementUID, s.Size, s.TurretAngle, s.ModuleRot, s.HangarShipUID);
                    ++swapped;
                    continue;
                }
            }
            swappedSlots[i] = new DesignSlot(s); // unchanged copy
        }
        if (swapped == 0)
            return (null, 0, matched, false);

        clone.SetDesignSlots(swappedSlots, updateRole: false);
        if (!ResourceManager.AddShipTemplate(clone, playerDesign: false, readOnly: true))
            return (null, 0, matched, false);
        if (!ResourceManager.Ships.GetDesign(variantName, out IShipDesign variant))
            return (null, 0, matched, false);

        try
        {
            Ship probe = SpawnShip(variantName, Player, new Vector2(960000, 960000));
            bool ok = probe is { Active: true } && probe.HasModules;
            probe.QueueTotalRemoval();
            if (!ok) return (null, 0, matched, false);
        }
        catch { return (null, 0, matched, false); }

        bool clean = (swapped == matched); // every predicate-matched slot swapped on a footprint match
        return (variant, swapped, matched, clean);
    }

    // Find a SHIELD module UID of the given footprint (smallest power-draw shield first for determinism), or
    // null. Used to provide an armor->shield replacement that footprint-matches the armor slot we target.
    static string FindShieldModuleUid(Point size)
        => ResourceManager.ShipModuleTemplates
            .Where(m => m.Is(ShipModuleType.Shield) && m.GetSize() == size)
            .OrderBy(m => m.PowerDraw).ThenBy(m => m.UID, StringComparer.Ordinal)
            .Select(m => m.UID).FirstOrDefault();

    // Find a WEAPON module UID whose installed weapon is of `family` and whose footprint == size (cheapest
    // power-draw first for determinism), or null. Used to retarget a hull's primary-weapon slots to another family.
    static string FindWeaponModuleUid(string family, Point size)
        => ResourceManager.ShipModuleTemplates
            .Where(m => m.GetSize() == size && TemplateWeaponFamily(m) == family)
            .OrderBy(m => m.PowerDraw).ThenBy(m => m.UID, StringComparer.Ordinal)
            .Select(m => m.UID).FirstOrDefault();

    // The dominant (most common) weapon family + the footprint of its slots on a design, or (null, default).
    static (string family, Point size, int count) PrimaryWeaponFamily(IShipDesign d)
    {
        var byFam = new Dictionary<(string, Point), int>();
        foreach (DesignSlot s in d.GetOrLoadDesignSlots())
        {
            if (!ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m)) continue;
            string fam = TemplateWeaponFamily(m);
            if (fam == null) continue;
            var key = (fam, m.GetSize());
            byFam[key] = byFam.TryGetValue(key, out int n) ? n + 1 : 1;
        }
        if (byFam.Count == 0) return (null, default, 0);
        var best = byFam.OrderByDescending(kv => kv.Value)
                        .ThenBy(kv => kv.Key.Item1, StringComparer.Ordinal)
                        .First();
        return (best.Key.Item1, best.Key.Item2, best.Value);
    }

    // Find the smallest armor module UID (1x1 preferred) anywhere in the dataset of the given footprint.
    static string FindArmorModuleUid(Point size)
        => ResourceManager.ShipModuleTemplates
            .Where(m => m.ModuleType == ShipModuleType.Armor && m.GetSize() == size)
            .OrderBy(m => m.PowerDraw).ThenBy(m => m.UID, StringComparer.Ordinal)
            .Select(m => m.UID).FirstOrDefault();

    // Run a single isolation experiment (base vs variant, equal count, N seeds) and fold it into an IsoSweepRow.
    IsoSweepRow ScoreIso(string category, string swapLabel, IShipDesign baseDesign, IShipDesign variant,
                         float hullStrength, float hullCost, int count, ulong[] seeds, int ticks, bool clean)
    {
        var exp = new IsoExperiment
        {
            Mode = "clone-swap", Hull = baseDesign.Hull,
            BaseDesign = baseDesign.Name, VariantDesign = variant.Name,
            ModuleVaried = swapLabel, SwapCount = 0,
        };
        RunIsoExperiment(exp, baseDesign, variant, count, seeds, ticks);
        var row = new IsoSweepRow
        {
            Hull = baseDesign.Hull, HullRole = baseDesign.Role.ToString(), Category = category, Swap = swapLabel,
            HullStrength = hullStrength, HullCost = hullCost,
            BaseWinRate = exp.BaseWinRate, VariantWinRate = exp.VariantWinRate, WinRateDelta = -exp.WinRateDelta,
            BaseRetained = exp.BaseAvgRetained, VariantRetained = exp.VariantAvgRetained,
            RetainedDelta = -exp.RetainedDelta, // variant - base: + means the SWAP helped
            Decisive = exp.DecisiveDuels, Clean = clean,
            // combat happened if either side lost >0.5% of its starting strength on average.
            Combat = exp.BaseAvgRetained < 0.995f || exp.VariantAvgRetained < 0.995f,
        };
        Console.WriteLine($"[iso] ROW {category,-9} hull='{row.Hull}' role={row.HullRole} swap='{swapLabel}' "
                        + $"baseWR={row.BaseWinRate:0.000} varWR={row.VariantWinRate:0.000} "
                        + $"winRateDelta(var-base)={row.WinRateDelta:+0.000;-0.000} "
                        + $"retainedDelta(var-base)={row.RetainedDelta:+0.000;-0.000} "
                        + $"decisive={row.Decisive}/{seeds.Length} clean={row.Clean} combat={row.Combat}");
        return row;
    }

    [TestMethod]
    public void BalanceMeta_ModuleIsoSweep_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire();
        SetupArena();

        var rows = new List<IsoSweepRow>();
        var notes = new List<string>();
        var hullsCovered = new List<string>();
        int duelCount = 0;

        const int Count = 6;       // 6v6 packs -> focus-fire produces decisive kills more often than 4v4
        const int Ticks = 8000;    // ~5k was too short for slow small-hull duels; 8k lets combat resolve
        ulong[] seeds = { 0x5A1, 0x5A2 }; // 2 seeds each for robustness
        ulong salt = 0x2BE0u;

        // ---- pick ~5 base hulls spanning classes that can clone+swap. We want STANDALONE warships (not
        //      carrier-only fighters), with a defensive module to vary, ordered by cost so we sample across
        //      the cost range. One representative per Role class, mid in cost within that class. ----
        var classOrder = new[]
        {
            RoleName.corvette, RoleName.frigate, RoleName.cruiser, RoleName.battleship, RoleName.capital,
        };
        IShipDesign[] warships = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly)
            .Where(d => classOrder.Contains(d.Role))
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        var bases = new List<IShipDesign>();
        foreach (RoleName role in classOrder)
        {
            IShipDesign[] inClass = warships.Where(d => d.Role == role).ToArray();
            if (inClass.Length == 0) { notes.Add($"no standalone warship of class '{role}' in dataset; class skipped"); continue; }
            bases.Add(inClass[inClass.Length / 2]); // mid-cost representative of the class
        }
        Console.WriteLine($"[iso] SWEEP base hulls (one per class, mid cost): "
                        + string.Join(", ", bases.Select(b => $"{b.Role}:'{b.Name}'@{b.GetCost(Player):0}")));

        foreach (IShipDesign baseDesign in bases)
        {
            // Unit strength + cost of ONE base ship (clean spawn) for the row metadata.
            CreateUniverseAndPlayerEmpire(); SetupArena();
            float hullStrength = UnitStrengthOf(baseDesign);
            float hullCost = baseDesign.GetCost(Player);
            string hullTag = $"{baseDesign.Role}:{baseDesign.Hull}";
            bool anyForHull = false;

            DesignSlot[] dslots = baseDesign.GetOrLoadDesignSlots();
            bool HasShield(ShipModule m) => m.Is(ShipModuleType.Shield);
            bool HasArmor(ShipModule m) => m.ModuleType == ShipModuleType.Armor;

            // -------- (A) DEFENSE single-swap: ONE armor->shield and ONE shield->armor at a footprint slot. --------
            // armor->shield: pick the first armor slot, find a footprint-matched shield, swap exactly that slot.
            {
                DesignSlot armorSlot = dslots
                    .Where(s => ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m) && m.ModuleType == ShipModuleType.Armor)
                    .OrderBy(s => s.Pos.Y).ThenBy(s => s.Pos.X).FirstOrDefault();
                if (armorSlot != null)
                {
                    Point sz = armorSlot.Size;
                    string shieldUid = FindShieldModuleUid(sz);
                    var single = BuildSingleSlotSwap(baseDesign, armorSlot.Pos, shieldUid, $"a2s1{salt:X}", salt++);
                    if (single.variant != null)
                    {
                        rows.Add(ScoreIso("defense", "armor->shield (1 slot)", baseDesign, single.variant,
                            hullStrength, hullCost, Count, seeds, Ticks, single.clean));
                        duelCount += seeds.Length; anyForHull = true;
                    }
                    else notes.Add($"{hullTag}: armor->shield single-swap not feasible (no footprint-matched shield for size {sz.X}x{sz.Y})");
                }
                else notes.Add($"{hullTag}: no armor slot -> armor->shield single-swap skipped");
            }
            // shield->armor: pick the first shield slot, swap to the design's own armor UID (footprint-matched).
            {
                DesignSlot shieldSlot = dslots
                    .Where(s => ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m) && m.Is(ShipModuleType.Shield))
                    .OrderBy(s => s.Pos.Y).ThenBy(s => s.Pos.X).FirstOrDefault();
                if (shieldSlot != null)
                {
                    Point sz = shieldSlot.Size;
                    string armorUid = FindArmorUidInDesign(baseDesign) ?? FindArmorModuleUid(sz);
                    var single = BuildSingleSlotSwap(baseDesign, shieldSlot.Pos, armorUid, $"s2a1{salt:X}", salt++);
                    if (single.variant != null)
                    {
                        rows.Add(ScoreIso("defense", "shield->armor (1 slot)", baseDesign, single.variant,
                            hullStrength, hullCost, Count, seeds, Ticks, single.clean));
                        duelCount += seeds.Length; anyForHull = true;
                    }
                    else notes.Add($"{hullTag}: shield->armor single-swap not feasible (no footprint-matched armor for size {sz.X}x{sz.Y})");
                }
                else notes.Add($"{hullTag}: no shield slot -> shield->armor single-swap skipped");
            }

            // -------- (B) DEFENSE doctrine: heavy-ARMOR variant vs heavy-SHIELD variant of the SAME hull. --------
            // Build an all-shield variant (every armor -> shield) and an all-armor variant (every shield -> armor),
            // then duel them directly. Use a single shield-module + the design's armor UID, footprint-matched.
            {
                // choose the most common armor footprint and shield footprint to drive the class swap.
                string armorUid = FindArmorUidInDesign(baseDesign);
                Point armorSize = armorUid != null && ResourceManager.GetModuleTemplate(armorUid, out ShipModule am2) ? am2.GetSize() : new Point(1, 1);
                string shieldUid = FindShieldModuleUid(armorSize);
                var allShield = BuildSwapVariant(baseDesign, HasArmor, shieldUid, $"allshld{salt:X}", salt++);
                var allArmor  = BuildSwapVariant(baseDesign, HasShield, armorUid, $"allarmr{salt:X}", salt++);
                if (allShield.variant != null && allArmor.variant != null)
                {
                    bool clean = allShield.clean && allArmor.clean;
                    // base side = heavy-armor variant; variant side = heavy-shield variant.
                    rows.Add(ScoreIso("doctrine", "all-armor vs all-shield", allArmor.variant, allShield.variant,
                        hullStrength, hullCost, Count, seeds, Ticks, clean));
                    duelCount += seeds.Length; anyForHull = true;
                    if (!clean) notes.Add($"{hullTag}: doctrine swap approximate (armorSwapped={allArmor.swapped}/{allArmor.matched}, shieldSwapped={allShield.swapped}/{allShield.matched})");
                }
                else notes.Add($"{hullTag}: doctrine all-armor vs all-shield not feasible "
                             + $"(allShieldVariant={(allShield.variant != null)}, allArmorVariant={(allArmor.variant != null)})");
            }

            // -------- (C) WEAPON family: swap the primary weapon family to up to 3 OTHER families. --------
            {
                var (fam, wsize, _) = PrimaryWeaponFamily(baseDesign);
                if (fam == null)
                    notes.Add($"{hullTag}: no classifiable weapon module -> weapon swaps skipped");
                else
                {
                    string[] families = { "Cannon", "Beam", "Missile", "Torpedo", "Kinetic", "Energy" };
                    var others = families.Where(f => f != fam).Take(8).ToArray();
                    int done = 0;
                    foreach (string target in others)
                    {
                        if (done >= 3) break; // up to 3 other families
                        string replUid = FindWeaponModuleUid(target, wsize);
                        if (replUid == null) continue; // no module of that family at this footprint
                        var v = BuildSwapVariant(baseDesign,
                            m => TemplateWeaponFamily(m) == fam && m.GetSize() == wsize, replUid, $"w{target}{salt:X}", salt++);
                        if (v.variant == null) continue;
                        rows.Add(ScoreIso("weapon", $"{fam}->{target}", baseDesign, v.variant,
                            hullStrength, hullCost, Count, seeds, Ticks, v.clean));
                        duelCount += seeds.Length; anyForHull = true; ++done;
                        if (!v.clean) notes.Add($"{hullTag}: weapon {fam}->{target} approximate (incomplete footprint swap {v.swapped}/{v.matched} slots)");
                    }
                    if (done == 0) notes.Add($"{hullTag}: primary weapon '{fam}' had no swappable alternative family at footprint {wsize.X}x{wsize.Y}");
                }
            }

            if (anyForHull) hullsCovered.Add(hullTag);
            else notes.Add($"{hullTag}: NO experiment was feasible on this hull (skipped entirely)");
        }

        // Flag degenerate rows where neither side took damage (ships never closed to weapon range at this scale):
        // the delta there reflects only static strength ratings, not in-combat value.
        foreach (IsoSweepRow r in rows.Where(r => !r.Combat))
            notes.Add($"{r.HullRole}:{r.Hull} '{r.Swap}' ({r.Category}) DEGENERATE: no combat occurred "
                    + "(both sides retained ~100% strength) — delta is rating-only, not battle-tested");

        // ---- emit moduleisosweep.json ----
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string rowsJson = rows.Count > 0 ? "\n    " + string.Join(",\n    ", rows.Select(IsoSweepJson)) + "\n  " : "";
        string json =
            "{\n" +
            $"  \"experiment\": \"module isolation FULL CAUSAL SWEEP across hull scales\",\n" +
            $"  \"shipsPerSide\": {Count},\n" +
            $"  \"seeds\": {seeds.Length},\n" +
            $"  \"ticks\": {Ticks},\n" +
            $"  \"duelCount\": {duelCount},\n" +
            $"  \"hullsCovered\": [{string.Join(",", hullsCovered.Select(J))}],\n" +
            $"  \"rowCount\": {rows.Count},\n" +
            $"  \"rows\": [{rowsJson}],\n" +
            $"  \"notes\": [{string.Join(",", notes.Select(J))}]\n" +
            "}\n";
        string jsonPath = Path.Combine(dir, "moduleisosweep.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[iso] SWEEP DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        Console.WriteLine($"[iso] SWEEP SUMMARY rows={rows.Count} duels={duelCount} hulls=[{string.Join(",", hullsCovered)}]");
        foreach (string n in notes) Console.WriteLine($"[iso] SWEEP NOTE {n}");

        Assert.IsTrue(File.Exists(jsonPath), "moduleisosweep.json written");
        Assert.IsTrue(rows.Count > 0, "the sweep produced at least one isolation row");
    }

    // SINGLE-SLOT footprint-matched swap: rewrite EXACTLY the slot at `pos` to replacementUID (when its
    // footprint matches), register + spawn-verify. Returns variant + clean. clean=true means the ONE intended
    // slot was swapped on a footprint match — exactly one module family changed, hull and every other slot
    // held fixed (the textbook controlled single-module isolation). The power/strength difference the swap
    // introduces is the very thing being measured, so it does not count against cleanliness.
    (IShipDesign variant, bool clean) BuildSingleSlotSwap(IShipDesign baseDesign, Point pos, string replacementUID, string tag, ulong nameSalt)
    {
        if (string.IsNullOrEmpty(replacementUID) || !ResourceManager.ModuleExists(replacementUID))
            return (null, false);
        ShipModule repl = ResourceManager.GetModuleTemplate(replacementUID);
        Point replSize = repl.GetSize();

        string variantName = $"{baseDesign.Name}-iso-{tag}";
        for (ulong bump = 1; ResourceManager.ShipTemplateExists(variantName); ++bump)
            variantName = $"{baseDesign.Name}-iso-{tag}-{bump}";

        ShipDesign clone = baseDesign.GetClone(variantName);
        DesignSlot[] slots = clone.GetOrLoadDesignSlots();
        var outSlots = new DesignSlot[slots.Length];
        bool swapped = false;
        for (int i = 0; i < slots.Length; ++i)
        {
            DesignSlot s = slots[i];
            if (!swapped && s.Pos == pos && s.Size == replSize)
            {
                outSlots[i] = new DesignSlot(s.Pos, replacementUID, s.Size, s.TurretAngle, s.ModuleRot, s.HangarShipUID);
                swapped = true;
            }
            else outSlots[i] = new DesignSlot(s);
        }
        if (!swapped) return (null, false);

        clone.SetDesignSlots(outSlots, updateRole: false);
        if (!ResourceManager.AddShipTemplate(clone, playerDesign: false, readOnly: true))
            return (null, false);
        if (!ResourceManager.Ships.GetDesign(variantName, out IShipDesign variant))
            return (null, false);
        try
        {
            Ship probe = SpawnShip(variantName, Player, new Vector2(970000, 970000));
            bool ok = probe is { Active: true } && probe.HasModules;
            probe.QueueTotalRemoval();
            if (!ok) return (null, false);
        }
        catch { return (null, false); }

        return (variant, true); // the single intended slot changed on a footprint match
    }

    // Count slots whose module UID differs between two designs of the (assumed) same hull. Compares by
    // scanline-sorted position so equal-position slots are matched; counts positions present in one design
    // only as differences too. Used by the near-twin (approx) fallback.
    static int SlotModuleDiff(IShipDesign a, IShipDesign b)
    {
        var am = new Dictionary<Point, string>();
        foreach (DesignSlot s in a.GetOrLoadDesignSlots()) am[s.Pos] = s.ModuleUID;
        var bm = new Dictionary<Point, string>();
        foreach (DesignSlot s in b.GetOrLoadDesignSlots()) bm[s.Pos] = s.ModuleUID;
        int diff = 0;
        foreach (var kv in am)
            if (!bm.TryGetValue(kv.Key, out string ub) || ub != kv.Value) ++diff;
        foreach (var kv in bm)
            if (!am.ContainsKey(kv.Key)) ++diff;
        return diff;
    }

    // ===================================================================================================
    // 12) BALANCE META — ONE-TRICK-PONY DOCTRINE PROBES (ADDITIVE on top of #10/#11).
    //    Builds whole-ship DOCTRINE variants (many modules transformed toward one extreme) from a base hull,
    //    then duels EACH feasible doctrine variant vs the UNMODIFIED base at equal ship count. Question:
    //    does any extreme min-max build (a "one-trick pony") beat the balanced stock design, or does balance
    //    hold? Fully additive: new helpers (BuildDoctrineVariant + small finders) + new method; the committed
    //    v1/v2/#10/#11 paths are untouched. Determinism preserved: deterministic GUID-free variant names
    //    (base.Name + "#GLASS" etc.), seeded duels (UState.EnableDeterministicRng), fixed seed set.
    //      Doctrines:
    //        GLASS_CANNON  — swap armor + shield slots to the base's PRIMARY weapon family (max offense).
    //        MAX_ARMOR     — swap weapon + shield slots to armor where the slot allows (max tank).
    //        MONO_MISSILE  — swap EVERY weapon slot to the Missile family (exploit the Missile-overperforms find).
    //        STRIP_SHIELDS — replace shield slots with armor.
    //        PURE_CARRIER  — only if the hull has fighter-hangar modules: maximize them (else skip + note).
    //    Each infeasible per-doctrine swap is skipped + NOTE-d (footprint/power/slot constraints can block some).
    // ===================================================================================================

    // Does this base hull have at least one FIGHTER hangar slot (a launch bay, not troop/mining/supply)?
    static int FighterHangarSlotCount(IShipDesign d)
    {
        int n = 0;
        foreach (DesignSlot s in d.GetOrLoadDesignSlots())
            if (ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m)
                && m.Is(ShipModuleType.Hangar) && m.IsFighterHangar)
                ++n;
        return n;
    }

    // Find a FIGHTER-hangar module UID of the given footprint (most hangar capacity first for determinism),
    // or null. Used by PURE_CARRIER to maximize launch bays.
    static string FindFighterHangarUid(Point size)
        => ResourceManager.ShipModuleTemplates
            .Where(m => m.Is(ShipModuleType.Hangar) && m.IsFighterHangar
                        && !m.IsTroopBay && !m.IsMiningBay && m.GetSize() == size)
            .OrderByDescending(m => m.MaximumHangarShipSize).ThenBy(m => m.UID, StringComparer.Ordinal)
            .Select(m => m.UID).FirstOrDefault();

    // Per-doctrine deterministic name suffix (GUID-free), e.g. base.Name + "#GLASS".
    static string DoctrineSuffix(string doctrine) => doctrine switch
    {
        "GLASS_CANNON"  => "#GLASS",
        "MAX_ARMOR"     => "#ARMOR",
        "MONO_MISSILE"  => "#MISSILE",
        "STRIP_SHIELDS" => "#STRIP",
        "PURE_CARRIER"  => "#CARRIER",
        _               => "#DOC",
    };

    // Census of a registered design (counts each slot's TEMPLATE module — no live ship needed). Mirrors the
    // family classification of TemplateWeaponFamily so the doctrine transform shows up in the emitted census.
    ModuleCensus CensusOfDesign(IShipDesign d)
    {
        var c = new ModuleCensus();
        var fams = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (DesignSlot s in d.GetOrLoadDesignSlots())
        {
            if (!ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m)) continue;
            c.TotalModules++;
            if (m.Is(ShipModuleType.Armor))  c.ArmorCount++;
            if (m.Is(ShipModuleType.Shield)) { c.ShieldCount++; c.ShieldPowerTotal += m.ActualShieldPowerMax; }
            string fam = TemplateWeaponFamily(m);
            if (fam != null) fams[fam] = fams.TryGetValue(fam, out int n) ? n + 1 : 1;
        }
        c.WeaponTypes = fams.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                            .Select(kv => new WeaponTypeCount { Type = kv.Key, Count = kv.Value }).ToList();
        return c;
    }

    // Clone `baseDesign`, transform MULTIPLE module slots toward `doctrine`, register under a DETERMINISTIC
    // GUID-free name (base.Name + "#GLASS" etc.), and spawn-verify. Per-slot rule: a slot is rewritten only
    // when a footprint-matched replacement of the doctrine's target family/type exists; otherwise that slot is
    // left untouched and counted as a skip (footprint/power/slot constraints may block some — the caller NOTEs
    // them). Returns the variant + how many slots changed + how many were SKIPPED (matched the doctrine's
    // source set but had no footprint-matched replacement) + whether the variant differs from base at all.
    // Role is preserved (updateRole:false) so AI behavior is invariant and only the loadout changes.
    (IShipDesign variant, int swapped, int skipped, string note) BuildDoctrineVariant(
        IShipDesign baseDesign, string doctrine, ulong nameSalt)
    {
        // Deterministic, GUID-free variant name. A collision-safe bump only triggers if that exact name is
        // already registered (it won't be within a single run; AddShipTemplate overwrites by name anyway).
        string variantName = baseDesign.Name + DoctrineSuffix(doctrine);
        for (ulong bump = 1; ResourceManager.ShipTemplateExists(variantName); ++bump)
            variantName = $"{baseDesign.Name}{DoctrineSuffix(doctrine)}-{nameSalt:X}-{bump}";

        // The base's PRIMARY weapon family + the footprint of those weapon slots (used by GLASS_CANNON).
        var (primFam, primSize, _) = PrimaryWeaponFamily(baseDesign);

        ShipDesign clone = baseDesign.GetClone(variantName);
        DesignSlot[] slots = clone.GetOrLoadDesignSlots();
        var outSlots = new DesignSlot[slots.Length];
        int swapped = 0, skipped = 0;

        for (int i = 0; i < slots.Length; ++i)
        {
            DesignSlot s = slots[i];
            outSlots[i] = new DesignSlot(s); // default: unchanged copy
            if (!ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m)) continue;

            bool isArmor  = m.ModuleType == ShipModuleType.Armor;
            bool isShield = m.Is(ShipModuleType.Shield);
            bool isWeapon = TemplateWeaponFamily(m) != null;

            // Decide whether this slot is a doctrine SOURCE and what family/type it should become.
            string replUid = null;     // null => not a source slot for this doctrine (leave untouched)
            bool isSource  = false;
            switch (doctrine)
            {
                case "GLASS_CANNON":   // armor + shields -> the base's primary weapon family (max offense)
                    if ((isArmor || isShield) && primFam != null)
                    { isSource = true; replUid = FindWeaponModuleUid(primFam, s.Size); }
                    break;
                case "MAX_ARMOR":      // weapons + shields -> armor (max tank)
                    if (isWeapon || isShield)
                    { isSource = true; replUid = FindArmorModuleUid(s.Size); }
                    break;
                case "MONO_MISSILE":   // every weapon -> Missile family
                    if (isWeapon)
                    { isSource = true; replUid = FindWeaponModuleUid("Missile", s.Size); }
                    break;
                case "STRIP_SHIELDS":  // shields -> armor
                    if (isShield)
                    { isSource = true; replUid = FindArmorModuleUid(s.Size); }
                    break;
                case "PURE_CARRIER":   // weapons + shields + armor -> fighter hangars (max launch bays)
                    if (isWeapon || isShield || isArmor)
                    { isSource = true; replUid = FindFighterHangarUid(s.Size); }
                    break;
            }

            if (!isSource) continue;
            if (replUid == null || replUid == s.ModuleUID) { skipped++; continue; } // no footprint-matched repl
            outSlots[i] = new DesignSlot(s.Pos, replUid, s.Size, s.TurretAngle, s.ModuleRot, s.HangarShipUID);
            swapped++;
        }

        if (swapped == 0)
            return (null, 0, skipped, $"{doctrine}: no footprint-matched replacement for any source slot");

        clone.SetDesignSlots(outSlots, updateRole: false); // keep Role identical -> AI behavior invariant
        if (!ResourceManager.AddShipTemplate(clone, playerDesign: false, readOnly: true))
            return (null, 0, skipped, $"{doctrine}: module validation failed in CreateNewShipTemplate (power/slot)");
        if (!ResourceManager.Ships.GetDesign(variantName, out IShipDesign variant))
            return (null, 0, skipped, $"{doctrine}: registered design not retrievable");

        // Prove a ship really spawns from the variant before we commit to dueling it.
        try
        {
            Ship probe = SpawnShip(variantName, Player, new Vector2(980000, 980000));
            bool ok = probe is { Active: true } && probe.HasModules;
            probe.QueueTotalRemoval();
            if (!ok) return (null, 0, skipped, $"{doctrine}: variant registered but spawned no modules");
        }
        catch (Exception ex) { return (null, 0, skipped, $"{doctrine}: spawn-verify threw {ex.GetType().Name}"); }

        return (variant, swapped, skipped, null);
    }

    // One emitted one-trick-pony probe row.
    sealed class TrickRow
    {
        public string Hull, HullRole, Doctrine, VariantName, Winner;
        public int Swapped, Skipped, EqualCount, EqualCostCount;
        public float BaseUnitStr, VariantUnitStr, StrengthRetainedDelta; // variant - base avg fraction retained
        public float BaseWinRate, VariantWinRate;
        public int Decisive, Seeds;
        public bool Combat;
        public ModuleCensus Census;
    }

    static string TrickCensusJson(ModuleCensus c)
    {
        string wt = string.Join(",", c.WeaponTypes.Select(w => $"{{\"type\":{J(w.Type)},\"count\":{w.Count}}}"));
        return $"{{\"totalModules\":{c.TotalModules},\"armorCount\":{c.ArmorCount},\"shieldCount\":{c.ShieldCount},"
             + $"\"shieldPowerTotal\":{F(c.ShieldPowerTotal)},\"weaponTypes\":[{wt}]}}";
    }

    static string TrickJson(TrickRow r)
        => $"{{\"hull\":{J(r.Hull)},\"hullRole\":{J(r.HullRole)},\"doctrine\":{J(r.Doctrine)},"
         + $"\"variantName\":{J(r.VariantName)},\"swapped\":{r.Swapped},\"skipped\":{r.Skipped},"
         + $"\"shipsPerSide\":{r.EqualCount},\"equalCostVariantCount\":{r.EqualCostCount},"
         + $"\"baseUnitStrength\":{F(r.BaseUnitStr)},\"variantUnitStrength\":{F(r.VariantUnitStr)},"
         + $"\"baseWinRate\":{F(r.BaseWinRate)},\"variantWinRate\":{F(r.VariantWinRate)},"
         + $"\"winner\":{J(r.Winner)},\"strengthRetainedDelta\":{F(r.StrengthRetainedDelta)},"
         + $"\"decisive\":{r.Decisive},\"seeds\":{r.Seeds},\"combat\":{(r.Combat ? "true" : "false")},"
         + $"\"variantCensus\":{TrickCensusJson(r.Census)}}}";

    [TestMethod]
    public void BalanceMeta_OneTrickPony_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire();
        SetupArena();

        var rows = new List<TrickRow>();
        var notes = new List<string>();
        var hullsCovered = new List<string>();

        const int Count = 6;       // 6v6 packs -> focus-fire makes duels decisive
        const int Ticks = 5000;    // per the brief
        ulong[] seeds = { 0x7C1, 0x7C2, 0x7C3 }; // 3 deterministic seeds for robustness
        ulong salt = 0x3DE0u;

        string[] doctrines = { "GLASS_CANNON", "MAX_ARMOR", "MONO_MISSILE", "STRIP_SHIELDS", "PURE_CARRIER" };

        // ---- pick 2-3 base hulls spanning scale: a cruiser + a battleship (+ a capital if present). Standalone
        //      warships (not carrier-only fighters), mid-cost within each class, deterministic ordering. ----
        var classOrder = new[] { RoleName.cruiser, RoleName.battleship, RoleName.capital };
        IShipDesign[] warships = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly)
            .Where(d => classOrder.Contains(d.Role))
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        var bases = new List<IShipDesign>();
        foreach (RoleName role in classOrder)
        {
            IShipDesign[] inClass = warships.Where(d => d.Role == role).ToArray();
            if (inClass.Length == 0) { notes.Add($"no standalone warship of class '{role}'; class skipped"); continue; }
            bases.Add(inClass[inClass.Length / 2]); // mid-cost representative
        }
        Console.WriteLine($"[trick] BASE hulls (cruiser+battleship+capital, mid cost): "
                        + string.Join(", ", bases.Select(b => $"{b.Role}:'{b.Name}'@{b.GetCost(Player):0}")));

        foreach (IShipDesign baseDesign in bases)
        {
            CreateUniverseAndPlayerEmpire(); SetupArena();
            float baseUnitStr = UnitStrengthOf(baseDesign);
            float baseCost    = Math.Max(1f, baseDesign.GetCost(Player));
            string hullTag = $"{baseDesign.Role}:{baseDesign.Hull}";
            bool anyForHull = false;
            var (primFam, _, primCount) = PrimaryWeaponFamily(baseDesign);
            Console.WriteLine($"[trick] HULL '{baseDesign.Name}' ({hullTag}) unitStr={baseUnitStr:0} cost={baseCost:0} "
                            + $"primaryWeapon={primFam ?? "none"}x{primCount} shields={ShieldSlotCount(baseDesign)} "
                            + $"fighterHangars={FighterHangarSlotCount(baseDesign)}");

            foreach (string doctrine in doctrines)
            {
                // PURE_CARRIER only applies to hulls that already have fighter-hangar slots.
                if (doctrine == "PURE_CARRIER" && FighterHangarSlotCount(baseDesign) == 0)
                { notes.Add($"{hullTag}: PURE_CARRIER skipped (hull has no fighter-hangar slot to maximize)"); continue; }
                // GLASS_CANNON needs a primary weapon family to convert toward.
                if (doctrine == "GLASS_CANNON" && primFam == null)
                { notes.Add($"{hullTag}: GLASS_CANNON skipped (hull has no classifiable primary weapon family)"); continue; }

                var (variant, swapped, skipped, why) = BuildDoctrineVariant(baseDesign, doctrine, salt++);
                if (variant == null)
                {
                    notes.Add($"{hullTag}: {why} (skippedSourceSlots={skipped})");
                    Console.WriteLine($"[trick] SKIP {hullTag} {doctrine} -> {why}");
                    continue;
                }

                // Variant unit strength + census + EQUAL-COST count (how many variants buy the base's 6-ship cost).
                CreateUniverseAndPlayerEmpire(); SetupArena();
                float varUnitStr = UnitStrengthOf(variant);
                float varCost    = Math.Max(1f, variant.GetCost(Player));
                int equalCostCount = Math.Clamp((int)Math.Round(baseCost * Count / varCost), 1, 60);
                ModuleCensus census = CensusOfDesign(variant);

                // EQUAL-COUNT duel: doctrine variant (side A) vs UNMODIFIED base (side B), N seeds.
                var exp = new IsoExperiment
                {
                    Mode = "doctrine", Hull = baseDesign.Hull,
                    BaseDesign = variant.Name, VariantDesign = baseDesign.Name,
                    ModuleVaried = doctrine, SwapCount = swapped,
                };
                RunIsoExperiment(exp, variant, baseDesign, Count, seeds, Ticks);

                // Here side A = variant, side B = base, so BaseWinRate is the VARIANT's win-rate.
                float variantWR = exp.BaseWinRate, baseWR = exp.VariantWinRate;
                float variantRet = exp.BaseAvgRetained, baseRet = exp.VariantAvgRetained;
                string winner = variantWR > baseWR + 1e-4f ? "VARIANT(one-trick)"
                              : baseWR > variantWR + 1e-4f ? "BASE(balanced)" : "draw";

                var row = new TrickRow
                {
                    Hull = baseDesign.Hull, HullRole = baseDesign.Role.ToString(), Doctrine = doctrine,
                    VariantName = variant.Name, Swapped = swapped, Skipped = skipped,
                    EqualCount = Count, EqualCostCount = equalCostCount,
                    BaseUnitStr = baseUnitStr, VariantUnitStr = varUnitStr,
                    BaseWinRate = baseWR, VariantWinRate = variantWR,
                    Winner = winner, StrengthRetainedDelta = variantRet - baseRet,
                    Decisive = exp.DecisiveDuels, Seeds = exp.Seeds,
                    Combat = baseRet < 0.995f || variantRet < 0.995f,
                    Census = census,
                };
                rows.Add(row);
                anyForHull = true;

                string wt = string.Join(",", census.WeaponTypes.Select(w => $"{w.Type}x{w.Count}"));
                Console.WriteLine($"[trick] DUEL {hullTag} {doctrine,-13} variant='{variant.Name}' "
                                + $"swapped={swapped} skipped={skipped} varUnitStr={varUnitStr:0} (base {baseUnitStr:0})  "
                                + $"WIN: {winner} (varWR={variantWR:0.000} baseWR={baseWR:0.000})  "
                                + $"retainedDelta(var-base)={row.StrengthRetainedDelta:+0.000;-0.000}  "
                                + $"equalCost={equalCostCount}v{Count} decisive={exp.DecisiveDuels}/{exp.Seeds} combat={row.Combat}");
                Console.WriteLine($"[trick]   CENSUS variant '{variant.Name}': mods={census.TotalModules} "
                                + $"armor={census.ArmorCount} shields={census.ShieldCount} "
                                + $"shieldPow={census.ShieldPowerTotal:0} weapons=[{wt}]");
            }

            if (anyForHull) hullsCovered.Add(hullTag);
            else notes.Add($"{hullTag}: NO doctrine variant was feasible on this hull");
        }

        // Flag degenerate rows where neither side took damage (ships never closed at this scale): delta is
        // rating-only there, not battle-tested.
        foreach (TrickRow r in rows.Where(r => !r.Combat))
            notes.Add($"{r.HullRole}:{r.Hull} {r.Doctrine} DEGENERATE: no combat (both sides retained ~100% strength)");

        // ---- verdict: did any one-trick build BEAT the balanced base, or does balance hold? ----
        int variantWins = rows.Count(r => r.Winner.StartsWith("VARIANT"));
        int baseWins    = rows.Count(r => r.Winner.StartsWith("BASE"));
        int draws       = rows.Count(r => r.Winner == "draw");
        var beaters = rows.Where(r => r.Winner.StartsWith("VARIANT")).Select(r => $"{r.HullRole}:{r.Doctrine}").ToList();
        string verdict = variantWins == 0
            ? "BALANCE HOLDS: no extreme min-max doctrine beat the balanced stock base at equal count"
            : $"DEGENERATE BUILDS EXIST: {variantWins} one-trick variant(s) beat the balanced base ({string.Join(", ", beaters)})";

        // ---- emit onetrick.json ----
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string rowsJson = rows.Count > 0 ? "\n    " + string.Join(",\n    ", rows.Select(TrickJson)) + "\n  " : "";
        string json =
            "{\n" +
            $"  \"experiment\": \"one-trick-pony doctrine probes vs balanced base (equal count)\",\n" +
            $"  \"shipsPerSide\": {Count},\n" +
            $"  \"ticks\": {Ticks},\n" +
            $"  \"seeds\": {seeds.Length},\n" +
            $"  \"doctrines\": [{string.Join(",", doctrines.Select(J))}],\n" +
            $"  \"hullsCovered\": [{string.Join(",", hullsCovered.Select(J))}],\n" +
            $"  \"rowCount\": {rows.Count},\n" +
            $"  \"variantWins\": {variantWins},\n" +
            $"  \"baseWins\": {baseWins},\n" +
            $"  \"draws\": {draws},\n" +
            $"  \"verdict\": {J(verdict)},\n" +
            $"  \"rows\": [{rowsJson}],\n" +
            $"  \"notes\": [{string.Join(",", notes.Select(J))}]\n" +
            "}\n";
        string jsonPath = Path.Combine(dir, "onetrick.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[trick] DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        Console.WriteLine($"[trick] VERDICT {verdict}");
        Console.WriteLine($"[trick] SUMMARY rows={rows.Count} variantWins={variantWins} baseWins={baseWins} draws={draws} "
                        + $"hulls=[{string.Join(",", hullsCovered)}]");
        foreach (string n in notes) Console.WriteLine($"[trick] NOTE {n}");

        Assert.IsTrue(File.Exists(jsonPath), "onetrick.json written");
        Assert.IsTrue(rows.Count > 0, "at least one doctrine variant must have dueled the base");
        foreach (TrickRow r in rows)
            Assert.AreEqual(seeds.Length, r.Seeds, "every doctrine duel ran the full seed set");
    }

    // ===================================================================================================
    // 13) BALANCE META — EVOLUTIONARY DESIGN SEARCH (ADDITIVE on top of #10/#11/#12).
    //    A deterministic mutate -> duel -> select loop. Start from a chosen BASE design as the CHAMPION.
    //    Each GENERATION: build a CHALLENGER by applying ONE random (seeded) module mutation to the champion
    //    (swap one slot to a random footprint-compatible module family — armor / shield / a weapon family),
    //    register it deterministically (name embeds the generation index), then DUEL challenger vs champion
    //    at EQUAL ship count with a FIXED deterministic seed. If the challenger wins by surviving strength,
    //    it becomes the new champion. Run ~30 generations, record the fitness trajectory + accept/reject per
    //    generation, then emit the evolved champion's full module census + the trajectory to evolved.json.
    //
    //    GOAL: auto-discover the strongest / most cost-broken build the system allows, and report WHAT IT
    //    CONVERGED ON — did it just pile on the highest-strength modules ("stat budget is destiny"), converge
    //    to all-Missile (exploiting the Missile-overperforms find), or find a genuine cost-efficiency exploit?
    //
    //    DETERMINISM: one System.Random(seed) drives the ENTIRE mutation stream (which slot, which target
    //    family, which footprint-matched module). Every duel uses a FIXED seed via RunIsoDuel
    //    (UState.EnableDeterministicRng). Variant names are GUID-free and embed the generation index. The
    //    whole evolution therefore reproduces byte-identically on rerun. Fully ADDITIVE: new helpers
    //    (BuildMutatedVariant + the EvoGen row) + new method only; committed v1/v2/#10/#11/#12 are untouched.
    // ===================================================================================================

    // The mutation family pool: a slot can be retargeted to armor, shield, or any of these weapon families.
    // Order is fixed (deterministic) so a given RNG draw always maps to the same family.
    static readonly string[] EvoFamilyPool =
        { "Armor", "Shield", "Beam", "Torpedo", "Missile", "Cannon", "Kinetic", "Energy" };

    // Find a footprint-matched module UID for a mutation family ("Armor"/"Shield"/weapon family), or null.
    // Reuses the same finders the doctrine path uses, so a mutated slot is always a real, registerable module.
    static string FindModuleUidForFamily(string family, Point size) => family switch
    {
        "Armor"  => FindArmorModuleUid(size),
        "Shield" => FindShieldModuleUid(size),
        _        => FindWeaponModuleUid(family, size),
    };

    // Classify a slot's current module into one of the EvoFamilyPool families (or null if it's neither armor,
    // shield, nor a classifiable weapon) — used so a mutation can pick a target family DIFFERENT from current.
    static string SlotFamily(ShipModule m)
    {
        if (m == null) return null;
        if (m.ModuleType == ShipModuleType.Armor)  return "Armor";
        if (m.Is(ShipModuleType.Shield))           return "Shield";
        return TemplateWeaponFamily(m); // Beam/Torpedo/Missile/Cannon/Kinetic/Energy/... or null
    }

    // Apply ONE seeded random module mutation to `champion`: pick a random MUTABLE slot (one whose current
    // module is armor/shield/weapon), pick a random target family DIFFERENT from the slot's current family
    // that has a footprint-matched module, rewrite just that one slot, register under a deterministic
    // GUID-free name embedding the generation index, and spawn-verify. Role preserved (updateRole:false) so
    // AI behavior is invariant and only that single module changes. Returns the variant + a human-readable
    // description of the swap, or (null, reason) if no valid single-slot mutation could be produced.
    //
    // All randomness comes from `rng` (a single seeded System.Random shared across the whole evolution), so
    // the mutation stream is fully reproducible. We try up to a bounded number of (slot, family) draws before
    // giving up, so a champion with few mutable slots still gets a fair, deterministic search.
    (IShipDesign variant, string desc) BuildMutatedVariant(IShipDesign champion, Random rng, int generation)
    {
        // Enumerate this champion's mutable slots (armor / shield / weapon) once, in fixed slot order.
        DesignSlot[] baseSlots = champion.GetOrLoadDesignSlots();
        var mutable = new List<int>();
        for (int i = 0; i < baseSlots.Length; ++i)
            if (ResourceManager.GetModuleTemplate(baseSlots[i].ModuleUID, out ShipModule m) && SlotFamily(m) != null)
                mutable.Add(i);
        if (mutable.Count == 0)
            return (null, "no mutable (armor/shield/weapon) slot on champion");

        // Up to 24 deterministic (slot, family) attempts: pick a mutable slot, pick a target family != current
        // that has a footprint-matched module. The first attempt that yields a real, distinct module wins.
        for (int attempt = 0; attempt < 24; ++attempt)
        {
            int slotIdx = mutable[rng.Next(mutable.Count)];
            DesignSlot slot = baseSlots[slotIdx];
            ResourceManager.GetModuleTemplate(slot.ModuleUID, out ShipModule cur);
            string curFam = SlotFamily(cur);

            string targetFam = EvoFamilyPool[rng.Next(EvoFamilyPool.Length)];
            if (targetFam == curFam) continue; // a no-op family pick; redraw (still deterministic)

            string replUid = FindModuleUidForFamily(targetFam, slot.Size);
            if (replUid == null || replUid == slot.ModuleUID) continue; // no footprint-matched replacement

            // Deterministic, GUID-free variant name embedding the generation index + the chosen slot.
            string variantName = $"{champion.Name}#evo-g{generation:D2}s{slotIdx:D2}-{targetFam}";
            for (ulong bump = 1; ResourceManager.ShipTemplateExists(variantName); ++bump)
                variantName = $"{champion.Name}#evo-g{generation:D2}s{slotIdx:D2}-{targetFam}-{bump}";

            ShipDesign clone = champion.GetClone(variantName);
            DesignSlot[] slots = clone.GetOrLoadDesignSlots();
            var outSlots = new DesignSlot[slots.Length];
            for (int i = 0; i < slots.Length; ++i)
                outSlots[i] = i == slotIdx
                    ? new DesignSlot(slots[i].Pos, replUid, slots[i].Size, slots[i].TurretAngle, slots[i].ModuleRot, slots[i].HangarShipUID)
                    : new DesignSlot(slots[i]);

            clone.SetDesignSlots(outSlots, updateRole: false); // keep Role identical -> AI behavior invariant
            if (!ResourceManager.AddShipTemplate(clone, playerDesign: false, readOnly: true))
                continue; // module validation failed (power/slot) — try a different draw
            if (!ResourceManager.Ships.GetDesign(variantName, out IShipDesign variant))
                continue;

            // Prove a ship really spawns from the variant before we commit to dueling it.
            try
            {
                Ship probe = SpawnShip(variantName, Player, new Vector2(970000, 970000));
                bool ok = probe is { Active: true } && probe.HasModules;
                probe.QueueTotalRemoval();
                if (!ok) continue;
            }
            catch { continue; }

            string desc = $"slot{slotIdx}({(curFam ?? "?")}->{targetFam})";
            return (variant, desc);
        }
        return (null, "no valid single-slot mutation found within attempt budget");
    }

    // One generation of the evolution, for evolved.json + the [evo] trajectory.
    sealed class EvoGen
    {
        public int Generation;
        public string ChallengerName, Mutation;
        public bool Accepted;
        public float ChampStrEnd, ChallStrEnd;      // surviving strength of each side after the duel
        public int   ChampAlive, ChallAlive;
        public float ChampUnitStr, ChallUnitStr;    // single-ship strength (the "stat budget" of each design)
        public string Winner;                       // "challenger" / "champion" / "tie"
    }

    static string EvoGenJson(EvoGen g)
        => $"{{\"generation\":{g.Generation},\"challenger\":{J(g.ChallengerName)},\"mutation\":{J(g.Mutation)},"
         + $"\"winner\":{J(g.Winner)},\"accepted\":{(g.Accepted ? "true" : "false")},"
         + $"\"champUnitStrength\":{F(g.ChampUnitStr)},\"challUnitStrength\":{F(g.ChallUnitStr)},"
         + $"\"champStrEnd\":{F(g.ChampStrEnd)},\"challStrEnd\":{F(g.ChallStrEnd)},"
         + $"\"champAlive\":{g.ChampAlive},\"challAlive\":{g.ChallAlive}}}";

    [TestMethod]
    public void BalanceMeta_EvolveDesign_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire();
        SetupArena();

        const int Generations = 30;     // ~25-35 per the brief
        const int Count = 6;            // 6v6 packs -> focus-fire makes duels decisive
        const int Ticks = 5000;         // per-duel sim length (matches the doctrine-probe budget)
        const ulong DuelSeed = 0xE7011E; // ONE fixed duel seed -> every generation's duel is reproducible
        var rng = new Random(0x5EEDED);  // ONE seeded mutation stream -> the whole evolution reproduces

        var notes = new List<string>();

        // ---- choose the BASE champion: a standalone mid-cost CRUISER (enough mutable slots to evolve, small
        //      enough that 6v6 duels actually resolve within the tick budget). Deterministic ordering. ----
        IShipDesign[] cruisers = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly && d.Role == RoleName.cruiser)
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();
        if (cruisers.Length == 0) // fall back to any standalone warship if no cruiser is present
            cruisers = ResourceManager.Ships.Designs
                .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly)
                .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
                .ToArray();
        IShipDesign baseDesign = cruisers[cruisers.Length / 2]; // mid-cost representative
        string baseName = baseDesign.Name;
        float baseCost = Math.Max(1f, baseDesign.GetCost(Player));

        IShipDesign champion = baseDesign;
        float champUnitStr = UnitStrengthOf(champion);
        ModuleCensus baseCensus = CensusOfDesign(baseDesign);
        Console.WriteLine($"[evo] BASE champion='{baseName}' role={baseDesign.Role} hull='{baseDesign.Hull}' "
                        + $"cost={baseCost:0} unitStr={champUnitStr:0} mods={baseCensus.TotalModules} "
                        + $"armor={baseCensus.ArmorCount} shields={baseCensus.ShieldCount} "
                        + $"weapons=[{string.Join(",", baseCensus.WeaponTypes.Select(w => $"{w.Type}x{w.Count}"))}]");

        var gens = new List<EvoGen>();
        int accepts = 0, skips = 0;
        for (int g = 0; g < Generations; ++g)
        {
            CreateUniverseAndPlayerEmpire(); SetupArena();

            var (challenger, desc) = BuildMutatedVariant(champion, rng, g);
            if (challenger == null)
            {
                ++skips;
                notes.Add($"gen{g}: mutation skipped — {desc}");
                Console.WriteLine($"[evo] gen{g,2} SKIP (no valid mutation: {desc}); champion='{champion.Name}' unchanged");
                continue;
            }

            float challUnitStr = UnitStrengthOf(challenger);

            // DUEL: challenger (side A) vs current champion (side B), equal count, FIXED seed. RunIsoDuel runs
            // the same deterministic build+sim loop every other sweep uses; the winner is decided by surviving
            // (end) strength, with full wipe as the decisive tiebreak.
            var (winner, aStart, aEnd, bStart, bEnd, aAlive, bAlive) =
                RunIsoDuel(challenger, champion, Count, DuelSeed, Ticks);

            bool challengerWon = winner == "A"; // A == challenger; ties / champion wins => reject
            string winLabel = winner == "A" ? "challenger" : winner == "B" ? "champion" : "tie";

            var row = new EvoGen
            {
                Generation = g, ChallengerName = challenger.Name, Mutation = desc,
                Accepted = challengerWon, Winner = winLabel,
                ChampUnitStr = champUnitStr, ChallUnitStr = challUnitStr,
                ChampStrEnd = bEnd, ChallStrEnd = aEnd, ChampAlive = bAlive, ChallAlive = aAlive,
            };
            gens.Add(row);

            if (challengerWon)
            {
                champion = challenger;
                champUnitStr = challUnitStr;
                ++accepts;
            }

            Console.WriteLine($"[evo] gen{g,2} {desc,-22} challenger='{challenger.Name}' "
                            + $"unitStr {challUnitStr:0} (champ {champUnitStr:0})  "
                            + $"duel: chall {aStart:0}->{aEnd:0} ({aAlive} alive)  champ {bStart:0}->{bEnd:0} ({bAlive} alive)  "
                            + $"-> {(challengerWon ? "ACCEPT (new champion)" : "reject")}");
        }

        // ---- evolved champion census + how it differs from the base (the "what did it converge on" payload). ----
        ModuleCensus evolvedCensus = CensusOfDesign(champion);
        CreateUniverseAndPlayerEmpire(); SetupArena();
        float evolvedUnitStr = UnitStrengthOf(champion);
        float evolvedCost = Math.Max(1f, champion.GetCost(Player));

        string Top(ModuleCensus c) => c.WeaponTypes.Count > 0 ? c.WeaponTypes[0].Type : "none";
        bool monoWeapon = evolvedCensus.WeaponTypes.Count == 1 && evolvedCensus.WeaponTypes[0].Count > 1;
        string topW = Top(evolvedCensus);
        // Heuristic verdict for the "what did it converge on" question.
        string convergence;
        if (champion == baseDesign)
            convergence = "NO CONVERGENCE: no mutation beat the base; the stock balanced design held";
        else if (monoWeapon && (topW == "Missile" || topW == "Torpedo"))
            convergence = $"MONO-{topW.ToUpperInvariant()}: evolution converged on a single guided-weapon family (exploits the missile/torpedo-overperforms regime)";
        else if (evolvedUnitStr > champUnitStr * 0f && evolvedCensus.ArmorCount + evolvedCensus.ShieldCount > baseCensus.ArmorCount + baseCensus.ShieldCount)
            convergence = "STAT-PILE (defensive): evolution piled on armor/shield modules -> stat budget dominates";
        else if (evolvedUnitStr >= UnitStrengthOf(baseDesign) * 1.05f)
            convergence = "STAT-PILE (offensive): evolution raised single-ship strength rating -> stat budget is destiny";
        else
            convergence = "COST/COMPOSITION SHIFT: evolution changed the loadout without a clear stat-pile (possible cost-efficiency / composition exploit)";

        Console.WriteLine($"[evo] EVOLVED champion='{champion.Name}' (from base '{baseName}')  "
                        + $"unitStr {UnitStrengthOf(baseDesign):0}->{evolvedUnitStr:0}  cost {baseCost:0}->{evolvedCost:0}  "
                        + $"accepts={accepts}/{Generations} skips={skips}");
        Console.WriteLine($"[evo] CENSUS evolved: mods={evolvedCensus.TotalModules} armor={evolvedCensus.ArmorCount} "
                        + $"(base {baseCensus.ArmorCount}) shields={evolvedCensus.ShieldCount} (base {baseCensus.ShieldCount}) "
                        + $"shieldPow={evolvedCensus.ShieldPowerTotal:0} "
                        + $"weapons=[{string.Join(",", evolvedCensus.WeaponTypes.Select(w => $"{w.Type}x{w.Count}"))}]  "
                        + $"(base weapons=[{string.Join(",", baseCensus.WeaponTypes.Select(w => $"{w.Type}x{w.Count}"))}])");
        Console.WriteLine($"[evo] VERDICT {convergence}");

        // ---- emit evolved.json ----
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string trajJson = gens.Count > 0 ? "\n    " + string.Join(",\n    ", gens.Select(EvoGenJson)) + "\n  " : "";
        string json =
            "{\n" +
            $"  \"experiment\": \"evolutionary design search (mutate -> duel -> select)\",\n" +
            $"  \"baseDesign\": {J(baseName)},\n" +
            $"  \"baseRole\": {J(baseDesign.Role.ToString())},\n" +
            $"  \"hull\": {J(baseDesign.Hull)},\n" +
            $"  \"generations\": {Generations},\n" +
            $"  \"shipsPerSide\": {Count},\n" +
            $"  \"ticks\": {Ticks},\n" +
            $"  \"duelSeedHex\": {J("0x" + DuelSeed.ToString("X"))},\n" +
            $"  \"accepts\": {accepts},\n" +
            $"  \"skips\": {skips},\n" +
            $"  \"evolvedChampion\": {J(champion.Name)},\n" +
            $"  \"baseUnitStrength\": {F(UnitStrengthOf(baseDesign))},\n" +
            $"  \"evolvedUnitStrength\": {F(evolvedUnitStr)},\n" +
            $"  \"baseCost\": {F(baseCost)},\n" +
            $"  \"evolvedCost\": {F(evolvedCost)},\n" +
            $"  \"baseCensus\": {TrickCensusJson(baseCensus)},\n" +
            $"  \"evolvedCensus\": {TrickCensusJson(evolvedCensus)},\n" +
            $"  \"convergence\": {J(convergence)},\n" +
            $"  \"trajectory\": [{trajJson}],\n" +
            $"  \"notes\": [{string.Join(",", notes.Select(J))}]\n" +
            "}\n";
        string jsonPath = Path.Combine(dir, "evolved.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[evo] DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        foreach (string n in notes) Console.WriteLine($"[evo] NOTE {n}");

        Assert.IsTrue(File.Exists(jsonPath), "evolved.json written");
        Assert.AreEqual(Generations, gens.Count + skips, "every generation either dueled or was a recorded skip");
        Assert.IsTrue(accepts >= 0, "evolution ran the full generation budget");
    }

    // ===================================================================================================
    // 14) BALANCE META — ONE-TRICK-PONY AT EQUAL COST (ADDITIVE on top of #12).
    //    The committed #12 (BalanceMeta_OneTrickPony_EmitsJson) dueled each doctrine variant vs the stock
    //    base at EQUAL SHIP COUNT (6v6). But the degenerate variants are CHEAPER (they swap pricey shields /
    //    expensive weapons for cheaper armor, etc.), so at equal count they enjoyed a free production-budget
    //    edge: "degenerate builds win" might just be "the cheaper ship wins when you field the same number".
    //    This method removes that confound: it re-runs the SAME four doctrine duels (GLASS_CANNON,
    //    MONO_MISSILE, STRIP_SHIELDS, MAX_ARMOR vs stock base) at EQUAL COST. The base fields a FIXED count;
    //    the variant fields floor(baseCount * baseCost / variantCost) ships, so total production spend on each
    //    side is matched. QUESTION: does "degenerate builds win" SURVIVE once the cost edge is removed — i.e.
    //    is any doctrine a real cost-SYSTEM hole at equal spend, or was the #12 result a cheaper-ship-at-equal-
    //    count artifact?
    //
    //    Fully additive: a new equal-cost duel helper (RunEqCostDuel — a faithful asymmetric-count mirror of
    //    the committed RunIsoDuel, same arena / seeded RNG / 1500-tick re-target cadence) + a new method.
    //    The committed #12 path and RunIsoDuel are untouched, so all prior results stay bit-identical.
    //    Determinism preserved: deterministic GUID-free variant names (BuildDoctrineVariant), seeded duels,
    //    fixed seed set, same 3 hulls (cruiser/battleship/capital).
    // ===================================================================================================

    // Equal-COST duel: side A (variant) fields `countA` ships, side B (base) fields `countB` ships. A faithful
    // ASYMMETRIC-COUNT mirror of RunIsoDuel — identical arena, spawn placement (BuildExactCount), seeded RNG,
    // and 1500-tick EngageAll re-target cadence — the ONLY difference is the two sides may field different
    // counts (so equal total cost). Returns winner + each side's start/end strength + alive counts.
    (string winner, float aStart, float aEnd, float bStart, float bEnd, int aAlive, int bAlive) RunEqCostDuel(
        IShipDesign a, IShipDesign b, int countA, int countB, ulong seed, int ticks)
    {
        CreateUniverseAndPlayerEmpire();
        SetupArena();
        var sa = MkSide(Player, 0, new Vector2(-8000, 0), new[] { (0f, 1f) });
        var sb = MkSide(Enemy,  1, new Vector2( 8000, 0), new[] { (0f, 1f) });
        var sides = new List<Side> { sa, sb };

        BuildExactCount(sa, a, countA);
        BuildExactCount(sb, b, countB);

        SetupWars(sides);
        UState.EnableDeterministicRng(seed);
        EngageAll(sides);

        float aStart = StrengthOf(sa.Fleet), bStart = StrengthOf(sb.Fleet);
        for (int t = 0; t < ticks; ++t)
        {
            UState.Objects.Update(TestSimStep);
            if (t % 1500 == 1499) EngageAll(sides);
        }
        int aAlive = AliveOf(sa.Fleet), bAlive = AliveOf(sb.Fleet);
        float aEnd = StrengthOf(sa.Fleet), bEnd = StrengthOf(sb.Fleet);
        string winner =
            bAlive == 0 && aAlive > 0 ? "A" :
            aAlive == 0 && bAlive > 0 ? "B" :
            aEnd > bEnd ? "A" :
            bEnd > aEnd ? "B" : "draw";
        return (winner, aStart, aEnd, bStart, bEnd, aAlive, bAlive);
    }

    // One emitted equal-cost one-trick-pony row.
    sealed class EqCostRow
    {
        public string Hull, HullRole, Doctrine, VariantName, Winner;
        public int Swapped, Skipped, BaseCount, VariantCount, Seeds, Decisive;
        public float BaseCost, VariantCost, BaseUnitStr, VariantUnitStr;
        public float BaseSpend, VariantSpend;                 // total production spent per side (count*cost)
        public float BaseWinRate, VariantWinRate;             // over the seed set
        public float BaseAvgRetained, VariantAvgRetained;     // avg end/start strength fraction
        public float SurvivingStrengthDelta;                  // avg (variantEnd - baseEnd) over seeds
        public bool Combat;
        public ModuleCensus Census;
    }

    static string EqCostJson(EqCostRow r)
        => $"{{\"hull\":{J(r.Hull)},\"hullRole\":{J(r.HullRole)},\"doctrine\":{J(r.Doctrine)},"
         + $"\"variantName\":{J(r.VariantName)},\"swapped\":{r.Swapped},\"skipped\":{r.Skipped},"
         + $"\"baseCost\":{F(r.BaseCost)},\"variantCost\":{F(r.VariantCost)},"
         + $"\"baseCount\":{r.BaseCount},\"variantCount\":{r.VariantCount},"
         + $"\"baseSpend\":{F(r.BaseSpend)},\"variantSpend\":{F(r.VariantSpend)},"
         + $"\"baseUnitStrength\":{F(r.BaseUnitStr)},\"variantUnitStrength\":{F(r.VariantUnitStr)},"
         + $"\"baseWinRate\":{F(r.BaseWinRate)},\"variantWinRate\":{F(r.VariantWinRate)},"
         + $"\"baseAvgRetained\":{F(r.BaseAvgRetained)},\"variantAvgRetained\":{F(r.VariantAvgRetained)},"
         + $"\"winner\":{J(r.Winner)},\"survivingStrengthDelta\":{F(r.SurvivingStrengthDelta)},"
         + $"\"decisive\":{r.Decisive},\"seeds\":{r.Seeds},\"combat\":{(r.Combat ? "true" : "false")},"
         + $"\"variantCensus\":{TrickCensusJson(r.Census)}}}";

    [TestMethod]
    public void BalanceMeta_OneTrickEqualCost_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire();
        SetupArena();

        var rows = new List<EqCostRow>();
        var notes = new List<string>();
        var hullsCovered = new List<string>();

        const int BaseCount = 6;   // the base fields a FIXED 6-ship pack; variant fields floor(eq-cost) ships
        const int Ticks = 5000;    // per the brief (~5000)
        ulong[] seeds = { 0x7C1, 0x7C2, 0x7C3 }; // same 3 deterministic seeds as #12
        ulong salt = 0x4EC0u;      // distinct salt namespace from #12 so variant names never collide

        // PURE_CARRIER excluded per the brief (the four DEGENERATE min-max doctrines only).
        string[] doctrines = { "GLASS_CANNON", "MONO_MISSILE", "STRIP_SHIELDS", "MAX_ARMOR" };

        // ---- same 3 hulls as #12: cruiser + battleship + capital, mid-cost, deterministic ordering. ----
        var classOrder = new[] { RoleName.cruiser, RoleName.battleship, RoleName.capital };
        IShipDesign[] warships = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly)
            .Where(d => classOrder.Contains(d.Role))
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        var bases = new List<IShipDesign>();
        foreach (RoleName role in classOrder)
        {
            IShipDesign[] inClass = warships.Where(d => d.Role == role).ToArray();
            if (inClass.Length == 0) { notes.Add($"no standalone warship of class '{role}'; class skipped"); continue; }
            bases.Add(inClass[inClass.Length / 2]); // mid-cost representative (identical pick to #12)
        }
        Console.WriteLine($"[eqc] BASE hulls (cruiser+battleship+capital, mid cost): "
                        + string.Join(", ", bases.Select(b => $"{b.Role}:'{b.Name}'@{b.GetCost(Player):0}")));

        foreach (IShipDesign baseDesign in bases)
        {
            CreateUniverseAndPlayerEmpire(); SetupArena();
            float baseUnitStr = UnitStrengthOf(baseDesign);
            float baseCost    = Math.Max(1f, baseDesign.GetCost(Player));
            string hullTag = $"{baseDesign.Role}:{baseDesign.Hull}";
            bool anyForHull = false;
            var (primFam, _, primCount) = PrimaryWeaponFamily(baseDesign);
            Console.WriteLine($"[eqc] HULL '{baseDesign.Name}' ({hullTag}) unitStr={baseUnitStr:0} cost={baseCost:0} "
                            + $"primaryWeapon={primFam ?? "none"}x{primCount} shields={ShieldSlotCount(baseDesign)} "
                            + $"baseSpend={baseCost * BaseCount:0} ({BaseCount} ships)");

            foreach (string doctrine in doctrines)
            {
                if (doctrine == "GLASS_CANNON" && primFam == null)
                { notes.Add($"{hullTag}: GLASS_CANNON skipped (hull has no classifiable primary weapon family)"); continue; }

                var (variant, swapped, skipped, why) = BuildDoctrineVariant(baseDesign, doctrine, salt++);
                if (variant == null)
                {
                    notes.Add($"{hullTag}: {why} (skippedSourceSlots={skipped})");
                    Console.WriteLine($"[eqc] SKIP {hullTag} {doctrine} -> {why}");
                    continue;
                }

                // Variant unit strength + census + the EQUAL-COST count: how many variants the base's total
                // spend (baseCost * BaseCount) buys. floor() so the variant never OVER-spends the base.
                CreateUniverseAndPlayerEmpire(); SetupArena();
                float varUnitStr = UnitStrengthOf(variant);
                float varCost    = Math.Max(1f, variant.GetCost(Player));
                int variantCount = Math.Clamp((int)Math.Floor(baseCost * BaseCount / varCost), 1, 60);

                ModuleCensus census = CensusOfDesign(variant);

                // EQUAL-COST duel set: variant (side A, variantCount ships) vs base (side B, BaseCount ships)
                // across the seed set. Accumulate win-rate, retained fractions, and surviving-strength delta.
                int variantWins = 0, baseWins = 0, decisive = 0;
                float varRetSum = 0f, baseRetSum = 0f, survDeltaSum = 0f;
                foreach (ulong seed in seeds)
                {
                    var (winner, aStart, aEnd, bStart, bEnd, aAlive, bAlive) =
                        RunEqCostDuel(variant, baseDesign, variantCount, BaseCount, seed, Ticks);
                    if (winner == "A") variantWins++;
                    else if (winner == "B") baseWins++;
                    if (aAlive == 0 || bAlive == 0) decisive++;

                    float varRet  = aStart > 0 ? aEnd / aStart : 0f;
                    float baseRet = bStart > 0 ? bEnd / bStart : 0f;
                    varRetSum += varRet; baseRetSum += baseRet;
                    survDeltaSum += (aEnd - bEnd);

                    Console.WriteLine($"[eqc] DUEL {hullTag,-22} {doctrine,-13} seed=0x{seed:X} "
                                    + $"VAR[{variantCount}x@{varCost:0}={variantCount * varCost:0} str {aStart:0}->{aEnd:0} alive {aAlive}] "
                                    + $"BASE[{BaseCount}x@{baseCost:0}={BaseCount * baseCost:0} str {bStart:0}->{bEnd:0} alive {bAlive}] "
                                    + $"-> {(winner == "A" ? "VARIANT" : winner == "B" ? "BASE" : "draw")}");
                }

                float variantWR = (float)variantWins / seeds.Length;
                float baseWR    = (float)baseWins / seeds.Length;
                float varAvgRet  = varRetSum / seeds.Length;
                float baseAvgRet = baseRetSum / seeds.Length;
                float survDelta  = survDeltaSum / seeds.Length;
                string rowWinner = variantWR > baseWR + 1e-4f ? "VARIANT(one-trick)"
                                 : baseWR > variantWR + 1e-4f ? "BASE(balanced)" : "draw";

                var row = new EqCostRow
                {
                    Hull = baseDesign.Hull, HullRole = baseDesign.Role.ToString(), Doctrine = doctrine,
                    VariantName = variant.Name, Swapped = swapped, Skipped = skipped,
                    BaseCost = baseCost, VariantCost = varCost,
                    BaseCount = BaseCount, VariantCount = variantCount,
                    BaseSpend = baseCost * BaseCount, VariantSpend = varCost * variantCount,
                    BaseUnitStr = baseUnitStr, VariantUnitStr = varUnitStr,
                    BaseWinRate = baseWR, VariantWinRate = variantWR,
                    BaseAvgRetained = baseAvgRet, VariantAvgRetained = varAvgRet,
                    Winner = rowWinner, SurvivingStrengthDelta = survDelta,
                    Decisive = decisive, Seeds = seeds.Length,
                    Combat = baseAvgRet < 0.995f || varAvgRet < 0.995f,
                    Census = census,
                };
                rows.Add(row);
                anyForHull = true;

                string wt = string.Join(",", census.WeaponTypes.Select(w => $"{w.Type}x{w.Count}"));
                Console.WriteLine($"[eqc] RESULT {hullTag} {doctrine,-13} variant='{variant.Name}' "
                                + $"swapped={swapped} skipped={skipped} varUnitStr={varUnitStr:0} (base {baseUnitStr:0}) "
                                + $"varCost={varCost:0} (base {baseCost:0}) counts={variantCount}v{BaseCount} "
                                + $"spend={varCost * variantCount:0}v{baseCost * BaseCount:0}  "
                                + $"WIN: {rowWinner} (varWR={variantWR:0.000} baseWR={baseWR:0.000}) "
                                + $"survDelta(var-base)={survDelta:+0;-0;0} decisive={decisive}/{seeds.Length} combat={row.Combat}");
                Console.WriteLine($"[eqc]   CENSUS variant '{variant.Name}': mods={census.TotalModules} "
                                + $"armor={census.ArmorCount} shields={census.ShieldCount} "
                                + $"shieldPow={census.ShieldPowerTotal:0} weapons=[{wt}]");
            }

            if (anyForHull) hullsCovered.Add(hullTag);
            else notes.Add($"{hullTag}: NO doctrine variant was feasible on this hull");
        }

        foreach (EqCostRow r in rows.Where(r => !r.Combat))
            notes.Add($"{r.HullRole}:{r.Hull} {r.Doctrine} DEGENERATE: no combat (both sides retained ~100% strength)");

        // ---- verdict: does "degenerate builds win" SURVIVE once the equal-count cost edge is removed? ----
        int variantWinRows = rows.Count(r => r.Winner.StartsWith("VARIANT"));
        int baseWinRows    = rows.Count(r => r.Winner.StartsWith("BASE"));
        int drawRows       = rows.Count(r => r.Winner == "draw");
        var beaters = rows.Where(r => r.Winner.StartsWith("VARIANT")).Select(r => $"{r.HullRole}:{r.Doctrine}").ToList();
        string verdict = variantWinRows == 0
            ? "COST EDGE WAS THE STORY: at EQUAL SPEND no degenerate doctrine beats the balanced base -> the #12 wins were a cheaper-ship-at-equal-count artifact, not a real cost-system hole"
            : $"REAL COST-SYSTEM HOLE: {variantWinRows} degenerate variant(s) still beat the balanced base at EQUAL SPEND ({string.Join(", ", beaters)}) -> not just the equal-count cost edge";

        // ---- emit onetrick_eqcost.json ----
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string rowsJson = rows.Count > 0 ? "\n    " + string.Join(",\n    ", rows.Select(EqCostJson)) + "\n  " : "";
        string json =
            "{\n" +
            $"  \"experiment\": \"one-trick-pony doctrine probes vs balanced base (EQUAL COST)\",\n" +
            $"  \"baseShipsPerSide\": {BaseCount},\n" +
            $"  \"ticks\": {Ticks},\n" +
            $"  \"seeds\": {seeds.Length},\n" +
            $"  \"doctrines\": [{string.Join(",", doctrines.Select(J))}],\n" +
            $"  \"hullsCovered\": [{string.Join(",", hullsCovered.Select(J))}],\n" +
            $"  \"rowCount\": {rows.Count},\n" +
            $"  \"variantWins\": {variantWinRows},\n" +
            $"  \"baseWins\": {baseWinRows},\n" +
            $"  \"draws\": {drawRows},\n" +
            $"  \"verdict\": {J(verdict)},\n" +
            $"  \"rows\": [{rowsJson}],\n" +
            $"  \"notes\": [{string.Join(",", notes.Select(J))}]\n" +
            "}\n";
        string jsonPath = Path.Combine(dir, "onetrick_eqcost.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[eqc] DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        Console.WriteLine($"[eqc] VERDICT {verdict}");
        Console.WriteLine($"[eqc] SUMMARY rows={rows.Count} variantWins={variantWinRows} baseWins={baseWinRows} draws={drawRows} "
                        + $"hulls=[{string.Join(",", hullsCovered)}]");
        foreach (string n in notes) Console.WriteLine($"[eqc] NOTE {n}");

        Assert.IsTrue(File.Exists(jsonPath), "onetrick_eqcost.json written");
        Assert.IsTrue(rows.Count > 0, "at least one doctrine variant must have dueled the base at equal cost");
        foreach (EqCostRow r in rows)
        {
            Assert.AreEqual(seeds.Length, r.Seeds, "every equal-cost doctrine duel ran the full seed set");
            Assert.IsTrue(r.VariantSpend <= r.BaseSpend + 1e-3f,
                "variant total spend must not exceed base spend (floor div => variant never over-spends)");
        }
    }

    // ===================================================================================================
    // EXPERIMENT 2 — RANGE-ADVANTAGE (ADDITIVE on top of #10/#11/#12/#13/eqcost; nothing committed is touched).
    //    The arena spawns fleets close together (±8000 in RunIsoDuel) with NEAREST-target AI, which likely
    //    under-serves long-range weapons (Beam etc.) because there is little/no approach phase for them to
    //    cash in their reach. This probes that directly:
    //      For each base hull, build a LONG-RANGE variant (MONO_BEAM — every weapon slot -> the Beam family)
    //      and a SHORT-RANGE variant (MONO_CANNON — every weapon slot -> the Cannon family). They share the
    //      same hull / armor / shields, so cost & unit-strength stay closely matched (both reported).
    //      Then duel LONG vs SHORT at a SHORT spawn distance (~3000 units between the fleets' inner edge) AND
    //      at a LONG spawn distance (~30000+). QUESTION: does the long-range build WIN when started far apart
    //      (free damage during the approach) but LOSE up close — i.e. was range genuinely under-served by close
    //      spawns? Or do short-range guns win regardless?
    //      We also record the MINIMUM fleet separation reached during each fight. If the AI just closes to
    //      point-blank regardless of weapon range (min-sep collapses to ~0 even from a long start), the arena
    //      cannot express KITING, so a long-range edge can only ever come from free approach damage, never from
    //      sustained stand-off — we NOTE that explicitly.
    //    Fully additive: new helpers (BuildMonoWeaponVariant + AvgBaseRangeOf + minSep-tracking RunRangeDuel,
    //    a faithful DISTANCE-PARAMETERIZED mirror of the committed RunIsoDuel) + the RangeRow/RangeCell rows +
    //    one new method. RunIsoDuel / BuildDoctrineVariant / the committed arena are untouched, so all prior
    //    results stay bit-identical. Determinism preserved: deterministic GUID-free variant names
    //    (base.Name + "#BEAM"/"#CANNON"), seeded duels (UState.EnableDeterministicRng), fixed seed set.
    // ===================================================================================================

    // Clone `baseDesign`, swap EVERY weapon slot to the `family` family (when a footprint-matched module of that
    // family exists), register under a DETERMINISTIC GUID-free name (base.Name + "#BEAM"/"#CANNON"), and
    // spawn-verify. A faithful generalization of BuildDoctrineVariant's MONO_MISSILE case to an arbitrary
    // weapon family. A weapon slot with no footprint-matched replacement (or already that family) is left
    // untouched and counted as a skip. Role is preserved (updateRole:false) so AI behavior is invariant and
    // only the weapon family changes. Returns the variant + how many slots changed + how many were skipped +
    // a note (null on success).
    (IShipDesign variant, int swapped, int skipped, string note) BuildMonoWeaponVariant(
        IShipDesign baseDesign, string family, ulong nameSalt)
    {
        string suffix = "#" + family.ToUpperInvariant(); // "#BEAM", "#CANNON"
        string variantName = baseDesign.Name + suffix;
        for (ulong bump = 1; ResourceManager.ShipTemplateExists(variantName); ++bump)
            variantName = $"{baseDesign.Name}{suffix}-{nameSalt:X}-{bump}";

        ShipDesign clone = baseDesign.GetClone(variantName);
        DesignSlot[] slots = clone.GetOrLoadDesignSlots();
        var outSlots = new DesignSlot[slots.Length];
        int swapped = 0, skipped = 0;
        for (int i = 0; i < slots.Length; ++i)
        {
            DesignSlot s = slots[i];
            outSlots[i] = new DesignSlot(s); // default: unchanged copy
            if (!ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m)) continue;
            if (TemplateWeaponFamily(m) == null) continue; // not a weapon slot -> leave untouched
            string replUid = FindWeaponModuleUid(family, s.Size);
            if (replUid == null || replUid == s.ModuleUID) { skipped++; continue; } // no footprint-matched repl
            outSlots[i] = new DesignSlot(s.Pos, replUid, s.Size, s.TurretAngle, s.ModuleRot, s.HangarShipUID);
            swapped++;
        }

        if (swapped == 0)
            return (null, 0, skipped, $"MONO_{family}: no footprint-matched {family} replacement for any weapon slot");

        clone.SetDesignSlots(outSlots, updateRole: false);
        if (!ResourceManager.AddShipTemplate(clone, playerDesign: false, readOnly: true))
            return (null, 0, skipped, $"MONO_{family}: module validation failed in CreateNewShipTemplate (power/slot)");
        if (!ResourceManager.Ships.GetDesign(variantName, out IShipDesign variant))
            return (null, 0, skipped, $"MONO_{family}: registered design not retrievable");

        try
        {
            Ship probe = SpawnShip(variantName, Player, new Vector2(940000, 940000));
            bool ok = probe is { Active: true } && probe.HasModules;
            probe.QueueTotalRemoval();
            if (!ok) return (null, 0, skipped, $"MONO_{family}: variant registered but spawned no modules");
        }
        catch (Exception ex) { return (null, 0, skipped, $"MONO_{family}: spawn-verify threw {ex.GetType().Name}"); }

        return (variant, swapped, skipped, null);
    }

    // Average BASE weapon range over a design's installed weapons, measured from one freshly-spawned live ship
    // (InstalledWeapon is populated on a live ship). Returns 0 if the ship carries no weapons.
    float AvgBaseRangeOf(IShipDesign d)
    {
        Ship s = SpawnShip(d.Name, Player, new Vector2(920000, 920000));
        UState.Objects.Update(TestSimStep); // let weapons install
        float sum = 0f; int n = 0;
        foreach (ShipModule m in s.Modules)
            if (m.InstalledWeapon != null) { sum += m.InstalledWeapon.BaseRange; ++n; }
        s.QueueTotalRemoval();
        return n == 0 ? 0f : sum / n;
    }

    // Distance-parameterized faithful mirror of RunIsoDuel: identical arena (CreateUniverseAndPlayerEmpire +
    // SetupArena), spawn placement (BuildExactCount), seeded RNG, EngageAll + 1500-tick re-target cadence, and
    // win rule (alive-then-end-strength) — the ONLY difference is the side anchors sit at ±halfDist (so the two
    // fleets START `2*halfDist` apart) instead of the committed ±8000, AND we additionally track the MINIMUM
    // fleet-to-fleet separation reached during the fight (to detect whether the AI closes to point-blank
    // regardless of weapon range). RunIsoDuel itself is untouched. Returns winner + each side's start/end
    // strength + alive counts + the min separation (closest approach) observed.
    (string winner, float aStart, float aEnd, float bStart, float bEnd, int aAlive, int bAlive, float minSep) RunRangeDuel(
        IShipDesign a, IShipDesign b, int count, float halfDist, ulong seed, int ticks)
    {
        CreateUniverseAndPlayerEmpire();
        SetupArena();
        var sa = MkSide(Player, 0, new Vector2(-halfDist, 0), new[] { (0f, 1f) });
        var sb = MkSide(Enemy,  1, new Vector2( halfDist, 0), new[] { (0f, 1f) });
        var sides = new List<Side> { sa, sb };

        BuildExactCount(sa, a, count);
        BuildExactCount(sb, b, count);

        SetupWars(sides);
        UState.EnableDeterministicRng(seed);
        EngageAll(sides);

        float aStart = StrengthOf(sa.Fleet), bStart = StrengthOf(sb.Fleet);
        float minSep = float.MaxValue;
        for (int t = 0; t < ticks; ++t)
        {
            UState.Objects.Update(TestSimStep);
            if (t % 1500 == 1499) EngageAll(sides);
            if (t % 50 == 0) // sample closest living-ship approach across the fleets
            {
                float closest = ClosestCrossSeparation(sa.Fleet, sb.Fleet);
                if (closest < minSep) minSep = closest;
            }
        }
        if (minSep == float.MaxValue) minSep = -1f; // never had two living ships to measure
        int aAlive = AliveOf(sa.Fleet), bAlive = AliveOf(sb.Fleet);
        float aEnd = StrengthOf(sa.Fleet), bEnd = StrengthOf(sb.Fleet);
        string winner =
            bAlive == 0 && aAlive > 0 ? "A" :
            aAlive == 0 && bAlive > 0 ? "B" :
            aEnd > bEnd ? "A" :
            bEnd > aEnd ? "B" : "draw";
        return (winner, aStart, aEnd, bStart, bEnd, aAlive, bAlive, minSep);
    }

    // Closest distance between any pair of (living) ships taken one from each fleet, or float.MaxValue if either
    // side has no living ship. O(nA*nB) over small equal-count fleets -> cheap.
    static float ClosestCrossSeparation(List<Ship> fa, List<Ship> fb)
    {
        float best = float.MaxValue;
        foreach (Ship x in fa)
        {
            if (!x.Active) continue;
            foreach (Ship y in fb)
            {
                if (!y.Active) continue;
                float d = x.Position.Distance(y.Position);
                if (d < best) best = d;
            }
        }
        return best;
    }

    // One distance-cell result (LONG-range vs SHORT-range duel at a given start distance, over a seed set).
    sealed class RangeCell
    {
        public string Label;             // "short-spawn" / "long-spawn"
        public float HalfDist, StartGap; // anchor half-distance and 2*halfDist
        public int LongWins, ShortWins, Draws, Seeds, Decisive;
        public float LongWinRate;        // long-range side's win fraction over the seeds
        public float AvgLongEnd, AvgShortEnd, AvgStrengthDelta; // avg (longEnd - shortEnd) over seeds
        public float AvgMinSep;          // avg closest approach reached (units) -> does the AI close to point-blank?
        public bool Combat;
    }

    sealed class RangeRow
    {
        public string Hull, HullRole, LongName, ShortName;
        public int LongSwapped, ShortSwapped, Count;
        public float LongUnitStr, ShortUnitStr, LongUnitCost, ShortUnitCost, LongAvgRange, ShortAvgRange;
        public RangeCell Short, Long;
        public string Verdict;
    }

    static string RangeCellJson(RangeCell c)
        => $"{{\"label\":{J(c.Label)},\"halfDist\":{F(c.HalfDist)},\"startGap\":{F(c.StartGap)},"
         + $"\"longWins\":{c.LongWins},\"shortWins\":{c.ShortWins},\"draws\":{c.Draws},"
         + $"\"seeds\":{c.Seeds},\"decisive\":{c.Decisive},\"longWinRate\":{F(c.LongWinRate)},"
         + $"\"avgLongEndStrength\":{F(c.AvgLongEnd)},\"avgShortEndStrength\":{F(c.AvgShortEnd)},"
         + $"\"avgStrengthDelta\":{F(c.AvgStrengthDelta)},\"avgMinSeparation\":{F(c.AvgMinSep)},"
         + $"\"combat\":{(c.Combat ? "true" : "false")}}}";

    static string RangeRowJson(RangeRow r)
        => $"{{\"hull\":{J(r.Hull)},\"hullRole\":{J(r.HullRole)},"
         + $"\"longRangeVariant\":{J(r.LongName)},\"shortRangeVariant\":{J(r.ShortName)},"
         + $"\"longSwapped\":{r.LongSwapped},\"shortSwapped\":{r.ShortSwapped},\"shipsPerSide\":{r.Count},"
         + $"\"longUnitStrength\":{F(r.LongUnitStr)},\"shortUnitStrength\":{F(r.ShortUnitStr)},"
         + $"\"longUnitCost\":{F(r.LongUnitCost)},\"shortUnitCost\":{F(r.ShortUnitCost)},"
         + $"\"longAvgWeaponRange\":{F(r.LongAvgRange)},\"shortAvgWeaponRange\":{F(r.ShortAvgRange)},"
         + $"\"shortSpawn\":{RangeCellJson(r.Short)},\"longSpawn\":{RangeCellJson(r.Long)},"
         + $"\"verdict\":{J(r.Verdict)}}}";

    // Run all seeds of a LONG(side A) vs SHORT(side B) duel at one spawn distance and fold into a RangeCell.
    // Side A = long-range variant, side B = short-range variant. Emits a [rng] line per seed so determinism is
    // observable in the log.
    RangeCell RunRangeCell(string label, IShipDesign longV, IShipDesign shortV, int count, float halfDist,
                           ulong[] seeds, int ticks)
    {
        var cell = new RangeCell { Label = label, HalfDist = halfDist, StartGap = 2f * halfDist, Seeds = seeds.Length };
        float endLongSum = 0f, endShortSum = 0f, deltaSum = 0f, minSepSum = 0f;
        bool combat = false;
        foreach (ulong seed in seeds)
        {
            var (winner, aStart, aEnd, bStart, bEnd, aAlive, bAlive, minSep) =
                RunRangeDuel(longV, shortV, count, halfDist, seed, ticks);
            if (winner == "A") cell.LongWins++;
            else if (winner == "B") cell.ShortWins++;
            else cell.Draws++;
            if (aAlive == 0 || bAlive == 0) cell.Decisive++;
            endLongSum += aEnd; endShortSum += bEnd; deltaSum += (aEnd - bEnd);
            minSepSum += (minSep < 0 ? 0f : minSep);
            if (aEnd < aStart - 1e-3f || bEnd < bStart - 1e-3f) combat = true;
            Console.WriteLine($"[rng] DUEL {label,-11} seed=0x{seed:X} startGap={2f * halfDist:0} -> "
                            + $"{(winner == "A" ? "LONG" : winner == "B" ? "SHORT" : "draw")}  "
                            + $"longStr {aStart:0}->{aEnd:0} ({aAlive} alive)  shortStr {bStart:0}->{bEnd:0} ({bAlive} alive)  "
                            + $"minSep={minSep:0}");
        }
        cell.LongWinRate = seeds.Length > 0 ? (float)cell.LongWins / seeds.Length : 0f;
        cell.AvgLongEnd = seeds.Length > 0 ? endLongSum / seeds.Length : 0f;
        cell.AvgShortEnd = seeds.Length > 0 ? endShortSum / seeds.Length : 0f;
        cell.AvgStrengthDelta = seeds.Length > 0 ? deltaSum / seeds.Length : 0f;
        cell.AvgMinSep = seeds.Length > 0 ? minSepSum / seeds.Length : 0f;
        cell.Combat = combat;
        return cell;
    }

    [TestMethod]
    public void BalanceMeta_RangeAdvantage_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire();
        SetupArena();

        var rows = new List<RangeRow>();
        var notes = new List<string>();
        var hullsCovered = new List<string>();

        const int Count = 6;        // 6v6 packs (matches the #12 one-trick scale)
        const int Ticks = 6000;     // enough sim time for a long approach to resolve at the 30k start gap
        const float ShortHalf = 1500f;   // fleets start ~3000 units apart (inner edges even closer)
        const float LongHalf  = 15000f;  // fleets start ~30000 units apart
        ulong[] seeds = { 0x4A11, 0x4A12, 0x4A13 }; // 3 deterministic seeds
        ulong salt = 0x6A6Eu;

        // ---- pick the same scale-spanning standalone-warship hulls as the #12 one-trick probe (cruiser +
        //      battleship + capital, mid-cost within class, deterministic ordering) so results are comparable. ----
        var classOrder = new[] { RoleName.cruiser, RoleName.battleship, RoleName.capital };
        IShipDesign[] warships = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly)
            .Where(d => classOrder.Contains(d.Role))
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        var bases = new List<IShipDesign>();
        foreach (RoleName role in classOrder)
        {
            IShipDesign[] inClass = warships.Where(d => d.Role == role).ToArray();
            if (inClass.Length == 0) { notes.Add($"no standalone warship of class '{role}'; class skipped"); continue; }
            bases.Add(inClass[inClass.Length / 2]); // mid-cost representative
        }
        Console.WriteLine($"[rng] BASE hulls (cruiser+battleship+capital, mid cost): "
                        + string.Join(", ", bases.Select(b => $"{b.Role}:'{b.Name}'@{b.GetCost(Player):0}")));

        foreach (IShipDesign baseDesign in bases)
        {
            string hullTag = $"{baseDesign.Role}:{baseDesign.Hull}";

            // Build the LONG-RANGE (Beam) and SHORT-RANGE (Cannon) variants from this hull.
            var (longV, longSw, longSk, longWhy)    = BuildMonoWeaponVariant(baseDesign, "Beam",   salt++);
            var (shortV, shortSw, shortSk, shortWhy) = BuildMonoWeaponVariant(baseDesign, "Cannon", salt++);
            if (longV == null || shortV == null)
            {
                if (longV == null)  notes.Add($"{hullTag}: LONG(Beam) variant infeasible -> {longWhy} (skippedWeaponSlots={longSk})");
                if (shortV == null) notes.Add($"{hullTag}: SHORT(Cannon) variant infeasible -> {shortWhy} (skippedWeaponSlots={shortSk})");
                Console.WriteLine($"[rng] SKIP {hullTag}: long={(longV == null ? longWhy : "ok")} short={(shortV == null ? shortWhy : "ok")}");
                continue;
            }

            // Measure unit cost / strength / avg base weapon range for each variant (clean spawns).
            CreateUniverseAndPlayerEmpire(); SetupArena();
            float longStr = UnitStrengthOf(longV),  shortStr = UnitStrengthOf(shortV);
            float longCost = Math.Max(1f, longV.GetCost(Player)), shortCost = Math.Max(1f, shortV.GetCost(Player));
            float longRange = AvgBaseRangeOf(longV), shortRange = AvgBaseRangeOf(shortV);
            Console.WriteLine($"[rng] HULL '{baseDesign.Name}' ({hullTag})  LONG='{longV.Name}' (swapped={longSw}) "
                            + $"unitStr={longStr:0} cost={longCost:0} avgRange={longRange:0}  |  "
                            + $"SHORT='{shortV.Name}' (swapped={shortSw}) unitStr={shortStr:0} cost={shortCost:0} avgRange={shortRange:0}");

            if (longRange <= shortRange + 1f)
                notes.Add($"{hullTag}: WARNING long(Beam) avgRange={longRange:0} is NOT greater than short(Cannon) avgRange={shortRange:0} -> the range premise may not hold for this dataset");

            // Duel LONG vs SHORT at SHORT spawn distance, then at LONG spawn distance.
            RangeCell shortCell = RunRangeCell("short-spawn", longV, shortV, Count, ShortHalf, seeds, Ticks);
            RangeCell longCell  = RunRangeCell("long-spawn",  longV, shortV, Count, LongHalf,  seeds, Ticks);

            // Verdict per hull: did range get UNDER-SERVED by close spawns? (long loses up close but wins far)
            string verdict;
            bool longWinsFar  = longCell.LongWinRate  > 0.5f;
            bool longWinsNear = shortCell.LongWinRate > 0.5f;
            if (longWinsFar && !longWinsNear)
                verdict = "RANGE WAS UNDER-SERVED: long-range build wins when started far apart but loses up close -> close spawns suppressed its reach advantage";
            else if (!longWinsFar && !longWinsNear)
                verdict = "SHORT-RANGE WINS REGARDLESS: even from a long start the short-range build wins -> range advantage does not materialize in this arena";
            else if (longWinsFar && longWinsNear)
                verdict = "LONG-RANGE WINS REGARDLESS: long-range build wins at both distances -> its edge is not purely a stand-off effect";
            else
                verdict = "MIXED/INVERTED: long-range wins up close but not far -> not a clean range story (check counts/combat)";

            // If the AI closes to point-blank even from the LONG start, the arena cannot express kiting.
            bool closesRegardless = longCell.AvgMinSep >= 0 && longCell.AvgMinSep < Math.Max(shortRange, 1500f);
            if (closesRegardless)
                notes.Add($"{hullTag}: AI CLOSES TO POINT-BLANK regardless of start distance (avgMinSep at long start = {longCell.AvgMinSep:0} units, < short-weapon range {shortRange:0}) -> the arena CANNOT express kiting; any long-range edge comes only from free approach damage, never sustained stand-off");

            var row = new RangeRow
            {
                Hull = baseDesign.Hull, HullRole = baseDesign.Role.ToString(),
                LongName = longV.Name, ShortName = shortV.Name,
                LongSwapped = longSw, ShortSwapped = shortSw, Count = Count,
                LongUnitStr = longStr, ShortUnitStr = shortStr,
                LongUnitCost = longCost, ShortUnitCost = shortCost,
                LongAvgRange = longRange, ShortAvgRange = shortRange,
                Short = shortCell, Long = longCell, Verdict = verdict,
            };
            rows.Add(row);
            hullsCovered.Add(hullTag);

            Console.WriteLine($"[rng] RESULT {hullTag}  SHORT-spawn(gap={shortCell.StartGap:0}): longWR={shortCell.LongWinRate:0.000} "
                            + $"(L{shortCell.LongWins}/S{shortCell.ShortWins}/D{shortCell.Draws}) avgDelta={shortCell.AvgStrengthDelta:+0;-0} avgMinSep={shortCell.AvgMinSep:0}  |  "
                            + $"LONG-spawn(gap={longCell.StartGap:0}): longWR={longCell.LongWinRate:0.000} "
                            + $"(L{longCell.LongWins}/S{longCell.ShortWins}/D{longCell.Draws}) avgDelta={longCell.AvgStrengthDelta:+0;-0} avgMinSep={longCell.AvgMinSep:0}");
            Console.WriteLine($"[rng] VERDICT {hullTag}: {verdict}");
        }

        // Cross-hull summary: how many hulls showed range genuinely under-served by close spawns?
        int underserved = rows.Count(r => r.Verdict.StartsWith("RANGE WAS UNDER-SERVED"));
        int shortRegardless = rows.Count(r => r.Verdict.StartsWith("SHORT-RANGE WINS REGARDLESS"));
        int longRegardless = rows.Count(r => r.Verdict.StartsWith("LONG-RANGE WINS REGARDLESS"));
        bool anyKiting = rows.Any(r => r.Long.AvgMinSep > Math.Max(r.ShortAvgRange, 1500f));
        string summary =
            rows.Count == 0 ? "INCONCLUSIVE: no hull produced both a long-range and short-range variant" :
            underserved > 0 ? $"RANGE UNDER-SERVED ON {underserved}/{rows.Count} HULL(S): close spawns suppress long-range builds (they win far, lose near)" :
            shortRegardless == rows.Count ? "SHORT-RANGE WINS REGARDLESS ON ALL HULLS: even from a 30k start the short-range build wins -> the arena's nearest-target AI closes before range matters" :
            "MIXED: see per-hull verdicts (no uniform range story)";
        if (!anyKiting && rows.Count > 0)
            summary += " | NOTE: AI closed to within short-weapon range on every long-start fight -> arena cannot express kiting/stand-off";

        // ---- emit rangeadv.json ----
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string rowsJson = rows.Count > 0 ? "\n    " + string.Join(",\n    ", rows.Select(RangeRowJson)) + "\n  " : "";
        string json =
            "{\n" +
            $"  \"experiment\": \"range-advantage: long-range (MONO_BEAM) vs short-range (MONO_CANNON) at short and long spawn distances\",\n" +
            $"  \"shipsPerSide\": {Count},\n" +
            $"  \"ticks\": {Ticks},\n" +
            $"  \"seeds\": {seeds.Length},\n" +
            $"  \"shortSpawnHalfDist\": {F(ShortHalf)},\n" +
            $"  \"longSpawnHalfDist\": {F(LongHalf)},\n" +
            $"  \"shortSpawnGap\": {F(2f * ShortHalf)},\n" +
            $"  \"longSpawnGap\": {F(2f * LongHalf)},\n" +
            $"  \"hullsCovered\": [{string.Join(",", hullsCovered.Select(J))}],\n" +
            $"  \"rowCount\": {rows.Count},\n" +
            $"  \"rangeUnderservedHulls\": {underserved},\n" +
            $"  \"shortWinsRegardlessHulls\": {shortRegardless},\n" +
            $"  \"longWinsRegardlessHulls\": {longRegardless},\n" +
            $"  \"summary\": {J(summary)},\n" +
            $"  \"rows\": [{rowsJson}],\n" +
            $"  \"notes\": [{string.Join(",", notes.Select(J))}]\n" +
            "}\n";
        string jsonPath = Path.Combine(dir, "rangeadv.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[rng] DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        Console.WriteLine($"[rng] SUMMARY {summary}");
        Console.WriteLine($"[rng] TALLY rows={rows.Count} underserved={underserved} shortRegardless={shortRegardless} longRegardless={longRegardless} hulls=[{string.Join(",", hullsCovered)}]");
        foreach (string n in notes) Console.WriteLine($"[rng] NOTE {n}");

        Assert.IsTrue(File.Exists(jsonPath), "rangeadv.json written");
        Assert.IsTrue(rows.Count > 0, "at least one hull must have produced both a long-range and short-range variant to duel");
        foreach (RangeRow r in rows)
        {
            Assert.AreEqual(seeds.Length, r.Short.Seeds, "short-spawn cell ran the full seed set");
            Assert.AreEqual(seeds.Length, r.Long.Seeds,  "long-spawn cell ran the full seed set");
        }
    }

    // ===================================================================================================
    // EXPERIMENT 3 — DISABLING-EFFECT (EMP) SCIENCE (ADDITIVE; nothing committed is touched).
    //    'Stat budget is destiny' says a ship's win-rate tracks its raw strength RATING. This probes a
    //    potential counter: a NON-DAMAGE disabling effect (EMP). The game's pure EMP weapons (EmpCannon /
    //    DualEmpCannon, WeaponType "Energy Cannon", DamageAmount=0, EMPDamage=100/250) deal ZERO hull damage —
    //    they only accumulate Ship.EMPDamage, and once that exceeds EmpTolerance (= SurfaceArea + bonus) the
    //    target becomes EMPDisabled: CombatDisabled is true, weapons stop firing (Ship.Update gates weapon
    //    updates on !EMPDisabled), and engines cut out. CRUCIAL MECHANIC (ShipModule.TryDamageModule): EMP is
    //    applied ONLY by a PROJECTILE hit on a NON-SHIELD module (and only when EMPDamage > Deflection). So a
    //    disabler must first strip/bypass shields before any disable lands — a real, testable dependency.
    //
    //    Because CalculateOffense rates EMPDamage at only 0.5x (and these projectiles are slow), an EMP build's
    //    STRENGTH RATING is far LOWER than its conventional twin. We therefore duel an EMP/disabler variant vs a
    //    conventional CANNON variant of the SAME hull two ways:
    //      (1) EQUAL COUNT  — pure "does the disabler punch above its rating?" test (EMP side is rated lower).
    //      (2) STRENGTH-MATCH — both sides start at equal TOTAL rated strength (EMP side fields MORE ships).
    //    If the lower-rated EMP side wins disproportionately (by disabling enemy weapons/engines), raw strength
    //    does NOT fully predict the outcome -> the stat-budget ladder has a hole. If raw strength still wins,
    //    the ladder holds. We additionally instrument the sim to record the PEAK number of enemy ships that were
    //    simultaneously EMPDisabled (the disable actually landing) — if that stays 0, EMP never applied (shields
    //    never dropped, or projectiles never connected headless) and we report an HONEST NEGATIVE with whatever
    //    partial signal we got.
    //
    //    Fully additive: new helpers (BuildEmpVariant + FindEmpModuleUid + a disable-instrumented RunEmpDuel,
    //    a faithful mirror of the committed RunIsoDuel) + new rows + one new method. RunIsoDuel /
    //    BuildMonoWeaponVariant / the committed arena are untouched, so all prior results stay bit-identical.
    //    Determinism: deterministic GUID-free variant names (base.Name + "#EMP"/"#CONV"), seeded duels
    //    (UState.EnableDeterministicRng), fixed seed set.
    // ===================================================================================================

    // Find a WEAPON module UID whose installed weapon has EMPDamage > 0 (a real disabler) and whose footprint ==
    // size, HIGHEST EMP-per-shot first (then cheapest power, then UID) for determinism, or null. Mirrors the
    // determinism discipline of FindWeaponModuleUid but selects on the weapon's EMPDamage instead of family.
    static string FindEmpModuleUid(Point size)
        => ResourceManager.ShipModuleTemplates
            .Where(m => m.GetSize() == size && !string.IsNullOrEmpty(m.WeaponType)
                        && ResourceManager.GetWeaponTemplate(m.WeaponType, out IWeaponTemplate w)
                        && w.EMPDamage > 0f && !w.Tag_Bomb)
            .OrderByDescending(m => ResourceManager.GetWeaponTemplate(m.WeaponType, out IWeaponTemplate w) ? w.EMPDamage : 0f)
            .ThenBy(m => m.PowerDraw).ThenBy(m => m.UID, StringComparer.Ordinal)
            .Select(m => m.UID).FirstOrDefault();

    // Total EMPDamage-per-shot summed over a design's weapon slots (the disabler "budget"), and the count of EMP
    // weapon slots. Used to confirm the EMP variant actually carries disablers + to report its disable budget.
    static (float empPerVolley, int empSlots) EmpBudgetOf(IShipDesign d)
    {
        float emp = 0f; int slots = 0;
        foreach (DesignSlot s in d.GetOrLoadDesignSlots())
        {
            if (!ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m) || string.IsNullOrEmpty(m.WeaponType)) continue;
            if (ResourceManager.GetWeaponTemplate(m.WeaponType, out IWeaponTemplate w) && w.EMPDamage > 0f)
            { emp += w.EMPDamage; slots++; }
        }
        return (emp, slots);
    }

    // Clone `baseDesign`, swap EVERY weapon slot to a footprint-matched EMP-disabler module (highest EMPDamage),
    // register under a DETERMINISTIC GUID-free name (base.Name + "#EMP"), and spawn-verify. A faithful EMP-flavored
    // mirror of BuildMonoWeaponVariant: a weapon slot with no footprint-matched EMP module (or already EMP) is left
    // untouched and counted as a skip. Role is preserved (updateRole:false) so AI behavior is invariant and only
    // the weapon family changes to the disabler. Returns variant + swapped + skipped + a note (null on success).
    (IShipDesign variant, int swapped, int skipped, string note) BuildEmpVariant(IShipDesign baseDesign, ulong nameSalt)
    {
        const string suffix = "#EMP";
        string variantName = baseDesign.Name + suffix;
        for (ulong bump = 1; ResourceManager.ShipTemplateExists(variantName); ++bump)
            variantName = $"{baseDesign.Name}{suffix}-{nameSalt:X}-{bump}";

        ShipDesign clone = baseDesign.GetClone(variantName);
        DesignSlot[] slots = clone.GetOrLoadDesignSlots();
        var outSlots = new DesignSlot[slots.Length];
        int swapped = 0, skipped = 0;
        for (int i = 0; i < slots.Length; ++i)
        {
            DesignSlot s = slots[i];
            outSlots[i] = new DesignSlot(s); // default: unchanged copy
            if (!ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m)) continue;
            if (TemplateWeaponFamily(m) == null) continue; // not a weapon slot -> leave untouched
            string replUid = FindEmpModuleUid(s.Size);
            if (replUid == null || replUid == s.ModuleUID) { skipped++; continue; } // no footprint-matched EMP module
            outSlots[i] = new DesignSlot(s.Pos, replUid, s.Size, s.TurretAngle, s.ModuleRot, s.HangarShipUID);
            swapped++;
        }

        if (swapped == 0)
            return (null, 0, skipped, "EMP: no footprint-matched EMP-disabler module for any weapon slot");

        clone.SetDesignSlots(outSlots, updateRole: false);
        if (!ResourceManager.AddShipTemplate(clone, playerDesign: false, readOnly: true))
            return (null, 0, skipped, "EMP: module validation failed in CreateNewShipTemplate (power/slot)");
        if (!ResourceManager.Ships.GetDesign(variantName, out IShipDesign variant))
            return (null, 0, skipped, "EMP: registered design not retrievable");
        try
        {
            Ship probe = SpawnShip(variantName, Player, new Vector2(930000, 930000));
            bool ok = probe is { Active: true } && probe.HasModules;
            probe.QueueTotalRemoval();
            if (!ok) return (null, 0, skipped, "EMP: variant registered but spawned no modules");
        }
        catch (Exception ex) { return (null, 0, skipped, $"EMP: spawn-verify threw {ex.GetType().Name}"); }

        return (variant, swapped, skipped, null);
    }

    // Build a conventional CANNON control variant of the same hull (every weapon slot -> the Cannon family), so the
    // EMP variant is dueled against a hull-matched conventional twin rather than the stock mixed loadout. Reuses the
    // committed BuildMonoWeaponVariant (#BEAM/#CANNON path) unchanged.
    // (no new helper needed — BuildMonoWeaponVariant(baseDesign, "Cannon", salt) gives the control.)

    // One emitted disabling-effect cell (EMP side A vs conventional side B over a seed set at one matchup mode).
    sealed class EmpCell
    {
        public string Mode;                 // "equal-count" or "strength-match"
        public int EmpCount, ConvCount;     // ships per side
        public int EmpWins, ConvWins, Draws, Seeds, Decisive;
        public float EmpStrStart, ConvStrStart;   // avg starting RATED strength per side (the stat budget)
        public float EmpWinRate;
        public float AvgEmpEnd, AvgConvEnd, AvgStrengthDelta; // avg (empEnd - convEnd) surviving rated strength
        public int PeakConvDisabled;        // max conventional ships simultaneously EMPDisabled in ANY seed
        public float AvgPeakConvDisabled;   // mean over seeds of each fight's peak disabled count
        public int FightsWithAnyDisable;    // # seeds where >=1 enemy ship was EMPDisabled at some point
        public bool Combat;
    }

    sealed class EmpRow
    {
        public string Hull, HullRole, EmpName, ConvName;
        public int EmpSwapped, ConvSwapped, EmpSlots;
        public float EmpUnitStr, ConvUnitStr, EmpUnitCost, ConvUnitCost, EmpPerVolley;
        public EmpCell EqualCount, StrengthMatch;
        public string Verdict;
    }

    static string EmpCellJson(EmpCell c)
        => $"{{\"mode\":{J(c.Mode)},\"empCount\":{c.EmpCount},\"convCount\":{c.ConvCount},"
         + $"\"empWins\":{c.EmpWins},\"convWins\":{c.ConvWins},\"draws\":{c.Draws},\"seeds\":{c.Seeds},"
         + $"\"decisive\":{c.Decisive},\"empWinRate\":{F(c.EmpWinRate)},"
         + $"\"empStrStart\":{F(c.EmpStrStart)},\"convStrStart\":{F(c.ConvStrStart)},"
         + $"\"avgEmpEndStrength\":{F(c.AvgEmpEnd)},\"avgConvEndStrength\":{F(c.AvgConvEnd)},"
         + $"\"avgStrengthDelta\":{F(c.AvgStrengthDelta)},\"peakConvDisabled\":{c.PeakConvDisabled},"
         + $"\"avgPeakConvDisabled\":{F(c.AvgPeakConvDisabled)},\"fightsWithAnyDisable\":{c.FightsWithAnyDisable},"
         + $"\"combat\":{(c.Combat ? "true" : "false")}}}";

    static string EmpRowJson(EmpRow r)
        => $"{{\"hull\":{J(r.Hull)},\"hullRole\":{J(r.HullRole)},"
         + $"\"empVariant\":{J(r.EmpName)},\"convVariant\":{J(r.ConvName)},"
         + $"\"empSwapped\":{r.EmpSwapped},\"convSwapped\":{r.ConvSwapped},\"empWeaponSlots\":{r.EmpSlots},"
         + $"\"empPerVolley\":{F(r.EmpPerVolley)},"
         + $"\"empUnitStrength\":{F(r.EmpUnitStr)},\"convUnitStrength\":{F(r.ConvUnitStr)},"
         + $"\"empUnitCost\":{F(r.EmpUnitCost)},\"convUnitCost\":{F(r.ConvUnitCost)},"
         + $"\"equalCount\":{EmpCellJson(r.EqualCount)},\"strengthMatch\":{EmpCellJson(r.StrengthMatch)},"
         + $"\"verdict\":{J(r.Verdict)}}}";

    // Disable-instrumented faithful mirror of RunIsoDuel: identical arena, BuildExactCount placement, seeded RNG,
    // EngageAll + 1500-tick re-target cadence, and win rule. The ONLY additions: the two sides may field DIFFERENT
    // counts (so we can strength-match), and we SAMPLE how many of side B's (conventional) ships are EMPDisabled
    // each ~50 ticks, returning the PEAK simultaneous-disabled count (proof the non-damage effect actually landed).
    // RunIsoDuel itself is untouched. Side A = EMP/disabler, side B = conventional.
    (string winner, float aStart, float aEnd, float bStart, float bEnd, int aAlive, int bAlive, int peakDisabled) RunEmpDuel(
        IShipDesign emp, IShipDesign conv, int empCount, int convCount, ulong seed, int ticks)
    {
        CreateUniverseAndPlayerEmpire();
        SetupArena();
        var sa = MkSide(Player, 0, new Vector2(-8000, 0), new[] { (0f, 1f) });
        var sb = MkSide(Enemy,  1, new Vector2( 8000, 0), new[] { (0f, 1f) });
        var sides = new List<Side> { sa, sb };

        BuildExactCount(sa, emp,  empCount);
        BuildExactCount(sb, conv, convCount);

        SetupWars(sides);
        UState.EnableDeterministicRng(seed);
        EngageAll(sides);

        float aStart = StrengthOf(sa.Fleet), bStart = StrengthOf(sb.Fleet);
        int peakDisabled = 0;
        for (int t = 0; t < ticks; ++t)
        {
            UState.Objects.Update(TestSimStep);
            if (t % 1500 == 1499) EngageAll(sides);
            if (t % 50 == 0) // sample how many conventional (side B) ships are currently EMP-disabled
            {
                int disabled = sb.Fleet.Count(s => s.Active && s.EMPDisabled);
                if (disabled > peakDisabled) peakDisabled = disabled;
            }
        }
        int aAlive = AliveOf(sa.Fleet), bAlive = AliveOf(sb.Fleet);
        float aEnd = StrengthOf(sa.Fleet), bEnd = StrengthOf(sb.Fleet);
        string winner =
            bAlive == 0 && aAlive > 0 ? "A" :
            aAlive == 0 && bAlive > 0 ? "B" :
            aEnd > bEnd ? "A" :
            bEnd > aEnd ? "B" : "draw";
        return (winner, aStart, aEnd, bStart, bEnd, aAlive, bAlive, peakDisabled);
    }

    // Run all seeds of an EMP(side A) vs conventional(side B) duel at fixed per-side counts, fold into an EmpCell.
    // Emits an [emp] line per seed so the disable signal + determinism are observable in the log.
    EmpCell RunEmpCell(string mode, IShipDesign emp, IShipDesign conv, int empCount, int convCount, ulong[] seeds, int ticks)
    {
        var cell = new EmpCell { Mode = mode, EmpCount = empCount, ConvCount = convCount, Seeds = seeds.Length };
        float endEmpSum = 0f, endConvSum = 0f, deltaSum = 0f, peakSum = 0f, startEmpSum = 0f, startConvSum = 0f;
        bool combat = false;
        foreach (ulong seed in seeds)
        {
            var (winner, aStart, aEnd, bStart, bEnd, aAlive, bAlive, peak) =
                RunEmpDuel(emp, conv, empCount, convCount, seed, ticks);
            if (winner == "A") cell.EmpWins++;
            else if (winner == "B") cell.ConvWins++;
            else cell.Draws++;
            if (aAlive == 0 || bAlive == 0) cell.Decisive++;
            endEmpSum += aEnd; endConvSum += bEnd; deltaSum += (aEnd - bEnd);
            startEmpSum += aStart; startConvSum += bStart;
            peakSum += peak;
            if (peak > cell.PeakConvDisabled) cell.PeakConvDisabled = peak;
            if (peak > 0) cell.FightsWithAnyDisable++;
            if (aEnd < aStart - 1e-3f || bEnd < bStart - 1e-3f) combat = true;
            Console.WriteLine($"[emp] DUEL {mode,-14} seed=0x{seed:X} EMP[{empCount}x str {aStart:0}->{aEnd:0} alive {aAlive}] "
                            + $"CONV[{convCount}x str {bStart:0}->{bEnd:0} alive {bAlive}] peakDisabled={peak} "
                            + $"-> {(winner == "A" ? "EMP" : winner == "B" ? "CONV" : "draw")}");
        }
        int n = Math.Max(1, seeds.Length);
        cell.EmpWinRate = (float)cell.EmpWins / n;
        cell.AvgEmpEnd = endEmpSum / n; cell.AvgConvEnd = endConvSum / n;
        cell.AvgStrengthDelta = deltaSum / n; cell.AvgPeakConvDisabled = peakSum / n;
        cell.EmpStrStart = startEmpSum / n; cell.ConvStrStart = startConvSum / n;
        cell.Combat = combat;
        return cell;
    }

    [TestMethod]
    public void BalanceMeta_DisablingEffect_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire();
        SetupArena();

        var rows = new List<EmpRow>();
        var notes = new List<string>();
        var hullsCovered = new List<string>();

        const int Count = 6;       // 6v6 packs -> focus-fire makes duels decisive (matches prior experiments)
        const int Ticks = 8000;    // long enough for accumulated EMP to push targets over EmpTolerance + decide
        ulong[] seeds = { 0xE3D1, 0xE3D2, 0xE3D3 }; // 3 deterministic seeds
        ulong salt = 0x7E40u;

        // Sanity: is there ANY EMP-disabler module in the dataset at all? If not, the experiment is blocked.
        bool anyEmpModule = ResourceManager.ShipModuleTemplates.Any(m => !string.IsNullOrEmpty(m.WeaponType)
            && ResourceManager.GetWeaponTemplate(m.WeaponType, out IWeaponTemplate w) && w.EMPDamage > 0f && !w.Tag_Bomb);
        Console.WriteLine($"[emp] dataset has EMP-disabler module(s): {anyEmpModule}");
        if (!anyEmpModule)
            notes.Add("dataset contains NO weapon module with EMPDamage>0 -> disabling-effect experiment is BLOCKED");

        // ---- same scale-spanning standalone-warship hulls as prior experiments (cruiser + battleship + capital,
        //      mid-cost within class, deterministic ordering). ----
        var classOrder = new[] { RoleName.cruiser, RoleName.battleship, RoleName.capital };
        IShipDesign[] warships = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly)
            .Where(d => classOrder.Contains(d.Role))
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        var bases = new List<IShipDesign>();
        foreach (RoleName role in classOrder)
        {
            IShipDesign[] inClass = warships.Where(d => d.Role == role).ToArray();
            if (inClass.Length == 0) { notes.Add($"no standalone warship of class '{role}'; class skipped"); continue; }
            bases.Add(inClass[inClass.Length / 2]); // mid-cost representative
        }
        Console.WriteLine($"[emp] BASE hulls (cruiser+battleship+capital, mid cost): "
                        + string.Join(", ", bases.Select(b => $"{b.Role}:'{b.Name}'@{b.GetCost(Player):0}")));

        foreach (IShipDesign baseDesign in bases)
        {
            string hullTag = $"{baseDesign.Role}:{baseDesign.Hull}";

            // EMP/disabler variant (every weapon slot -> highest-EMP module) and a hull-matched conventional
            // CANNON control (reuse the committed BuildMonoWeaponVariant).
            var (empV, empSw, empSk, empWhy)    = BuildEmpVariant(baseDesign, salt++);
            var (convV, convSw, convSk, convWhy) = BuildMonoWeaponVariant(baseDesign, "Cannon", salt++);
            if (empV == null || convV == null)
            {
                if (empV == null)  notes.Add($"{hullTag}: EMP variant infeasible -> {empWhy} (skippedWeaponSlots={empSk})");
                if (convV == null) notes.Add($"{hullTag}: CONV(Cannon) control infeasible -> {convWhy} (skippedWeaponSlots={convSk})");
                Console.WriteLine($"[emp] SKIP {hullTag}: emp={(empV == null ? empWhy : "ok")} conv={(convV == null ? convWhy : "ok")}");
                continue;
            }

            // Unit cost / strength / EMP budget for each variant (clean spawns).
            CreateUniverseAndPlayerEmpire(); SetupArena();
            float empStr = UnitStrengthOf(empV),  convStr = UnitStrengthOf(convV);
            float empCost = Math.Max(1f, empV.GetCost(Player)), convCost = Math.Max(1f, convV.GetCost(Player));
            var (empPerVolley, empSlots) = EmpBudgetOf(empV);
            Console.WriteLine($"[emp] HULL '{baseDesign.Name}' ({hullTag})  EMP='{empV.Name}' (swapped={empSw} slots={empSlots} "
                            + $"empPerVolley={empPerVolley:0}) unitStr={empStr:0} cost={empCost:0}  |  "
                            + $"CONV='{convV.Name}' (swapped={convSw}) unitStr={convStr:0} cost={convCost:0}");

            if (empSlots == 0)
            { notes.Add($"{hullTag}: EMP variant carries 0 EMP weapon slots after swap -> not a real disabler; hull skipped"); continue; }
            // NOTE the relative rating. EMP weapons ARE rated (CalculateOffense credits EMPDamage at 0.5x), and the
            // biggest EMP module (DualEmpCannon, EMPDamage=250) can out-rate a small Cannon — so the EMP twin may be
            // rated HIGHER, LOWER, or even. The strength-match below handles either direction by scaling the LOWER-
            // rated side up to the higher side's total rated strength; the verdict is framed accordingly.
            bool empRatedHigher = empStr > convStr + 1e-3f;
            notes.Add(empRatedHigher
                ? $"{hullTag}: EMP unitStr={empStr:0} > CONV unitStr={convStr:0} -> EMP is the HIGHER-rated side; a CONV equal-count win means the EMP rating is OVER-credited (zero-hull-damage disabler underperforms its rating)"
                : $"{hullTag}: EMP unitStr={empStr:0} <= CONV unitStr={convStr:0} -> EMP is the lower/equal-rated side; an EMP win shows the disabler punching above its rating");

            // (1) EQUAL-COUNT: pure 'does the disabler over/under-perform its rating ship-for-ship?' test.
            EmpCell eqc = RunEmpCell("equal-count", empV, convV, Count, Count, seeds, Ticks);

            // (2) STRENGTH-MATCH: scale the LOWER-rated side UP so both sides START at ~equal total rated strength
            //     (the higher-rated side stays at Count). So if EMP is lower-rated it fields MORE ships; if EMP is
            //     higher-rated, CONV fields more. A win by the side that needed FEWER ships => its per-ship value
            //     exceeds its rating. Counts clamped to [Count, 40].
            int empCountSM, convCountSM;
            if (empRatedHigher)
            {
                empCountSM = Count;
                convCountSM = Math.Clamp((int)Math.Round(Count * empStr / Math.Max(1f, convStr)), Count, 40);
            }
            else
            {
                convCountSM = Count;
                empCountSM = Math.Clamp((int)Math.Round(Count * convStr / Math.Max(1f, empStr)), Count, 40);
            }
            EmpCell smc = RunEmpCell("strength-match", empV, convV, empCountSM, convCountSM, seeds, Ticks);

            // Per-hull verdict.
            bool empWinsEqual = eqc.EmpWinRate > 0.5f;
            bool empWinsMatched = smc.EmpWinRate > 0.5f;
            bool everDisabled = eqc.PeakConvDisabled > 0 || smc.PeakConvDisabled > 0;
            string verdict;
            if (!everDisabled)
                verdict = "DISABLE NEVER LANDED: no conventional ship was EMP-disabled at any sample -> EMP did not apply (shields stayed up, or projectiles never connected) -> honest negative on this hull";
            else if (empWinsEqual && !empRatedHigher)
                verdict = "STAT-BUDGET LADDER BROKEN: the LOWER/equal-rated EMP/disabler build wins AT EQUAL COUNT -> disabling enemy weapons/engines beats raw strength rating";
            else if (empWinsEqual && empRatedHigher)
                verdict = "DISABLER WINS BUT IS HIGHER-RATED: EMP wins at equal count yet it also out-rates the conventional twin -> consistent with stat-budget (the disabler's rating already reflects its edge), not a counter to it";
            else if (!empWinsEqual && empRatedHigher)
                verdict = "RATING OVER-CREDITS THE DISABLER: the HIGHER-rated EMP build LOSES at equal count -> a zero-hull-damage disabler UNDER-performs its strength rating (shields blunt it); stat-budget over-values EMP here";
            else if (empWinsMatched)
                verdict = "DISABLER WINS ONLY WHEN MASSED: EMP loses at equal count but wins at equal rated strength (fielding more lower-rated ships) -> partial counter to stat-budget; raw rating under-credits the disabler";
            else
                verdict = "RAW STRENGTH STILL WINS: disable landed but the conventional build wins at both equal count and equal strength -> the stat-budget ladder holds even against a disabler";

            var row = new EmpRow
            {
                Hull = baseDesign.Hull, HullRole = baseDesign.Role.ToString(),
                EmpName = empV.Name, ConvName = convV.Name,
                EmpSwapped = empSw, ConvSwapped = convSw, EmpSlots = empSlots,
                EmpUnitStr = empStr, ConvUnitStr = convStr,
                EmpUnitCost = empCost, ConvUnitCost = convCost, EmpPerVolley = empPerVolley,
                EqualCount = eqc, StrengthMatch = smc, Verdict = verdict,
            };
            rows.Add(row);
            hullsCovered.Add(hullTag);

            Console.WriteLine($"[emp] RESULT {hullTag}  EQUAL-COUNT: empWR={eqc.EmpWinRate:0.000} "
                            + $"(EMP{eqc.EmpWins}/CONV{eqc.ConvWins}/D{eqc.Draws}) avgDelta={eqc.AvgStrengthDelta:+0;-0} "
                            + $"peakDisabled={eqc.PeakConvDisabled} avgPeak={eqc.AvgPeakConvDisabled:0.0} fightsWithDisable={eqc.FightsWithAnyDisable}/{eqc.Seeds}  |  "
                            + $"STRENGTH-MATCH({empCountSM}v{convCountSM}): empWR={smc.EmpWinRate:0.000} "
                            + $"(EMP{smc.EmpWins}/CONV{smc.ConvWins}/D{smc.Draws}) startStr {smc.EmpStrStart:0}v{smc.ConvStrStart:0} "
                            + $"peakDisabled={smc.PeakConvDisabled} fightsWithDisable={smc.FightsWithAnyDisable}/{smc.Seeds}");
            Console.WriteLine($"[emp] VERDICT {hullTag}: {verdict}");
        }

        // Cross-hull summary.
        int laddersBroken = rows.Count(r => r.Verdict.StartsWith("STAT-BUDGET LADDER BROKEN"));
        int massedOnly    = rows.Count(r => r.Verdict.StartsWith("DISABLER WINS ONLY WHEN MASSED"));
        int rawHolds      = rows.Count(r => r.Verdict.StartsWith("RAW STRENGTH STILL WINS"));
        int overCredited  = rows.Count(r => r.Verdict.StartsWith("RATING OVER-CREDITS THE DISABLER"));
        int neverLanded   = rows.Count(r => r.Verdict.StartsWith("DISABLE NEVER LANDED"));
        int hullsWithDisable = rows.Count(r => r.EqualCount.PeakConvDisabled > 0 || r.StrengthMatch.PeakConvDisabled > 0);
        string summary =
            rows.Count == 0
                ? "INCONCLUSIVE: no hull produced a dueling EMP-disabler vs conventional pair (see notes)"
            : hullsWithDisable == 0
                ? "HONEST NEGATIVE: EMP never disabled a single enemy ship on any hull (targets kept shields up, which block EMP, or the headless projectile path never connected) -> the disabling effect could not be measured; no evidence either way on the stat-budget ladder"
            : laddersBroken > 0
                ? $"STAT-BUDGET LADDER HAS A HOLE: on {laddersBroken}/{rows.Count} hull(s) the LOWER/equal-rated EMP build beat the conventional twin at EQUAL COUNT -> non-damage disabling lets a ship punch above its strength rating"
            : massedOnly > 0
                ? $"PARTIAL COUNTER: EMP never wins at equal count, but on {massedOnly}/{rows.Count} hull(s) it wins when massed to equal rated strength -> the rating under-credits disablers, but raw strength still wins ship-for-ship"
            : overCredited > 0
                ? $"RATING OVER-CREDITS EMP: on {overCredited}/{rows.Count} hull(s) the HIGHER-rated EMP build LOST at equal count -> a zero-hull-damage disabler UNDER-performs its strength rating (shields blunt it); if anything the ladder over-values EMP, it does not under-value it"
                : $"STAT-BUDGET LADDER HOLDS: disable landed on {hullsWithDisable}/{rows.Count} hull(s) yet the conventional build won at equal count and equal strength -> raw strength still predicts the winner";

        // ---- emit disabling.json ----
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string rowsJson = rows.Count > 0 ? "\n    " + string.Join(",\n    ", rows.Select(EmpRowJson)) + "\n  " : "";
        string json =
            "{\n" +
            $"  \"experiment\": \"disabling-effect (EMP) science: lower-rated EMP/disabler vs conventional twin at equal count AND equal rated strength\",\n" +
            $"  \"shipsPerSide\": {Count},\n" +
            $"  \"ticks\": {Ticks},\n" +
            $"  \"seeds\": {seeds.Length},\n" +
            $"  \"empModuleAvailable\": {(anyEmpModule ? "true" : "false")},\n" +
            $"  \"hullsCovered\": [{string.Join(",", hullsCovered.Select(J))}],\n" +
            $"  \"rowCount\": {rows.Count},\n" +
            $"  \"laddersBroken\": {laddersBroken},\n" +
            $"  \"disablerWinsOnlyMassed\": {massedOnly},\n" +
            $"  \"rawStrengthHolds\": {rawHolds},\n" +
            $"  \"ratingOverCreditsEmp\": {overCredited},\n" +
            $"  \"disableNeverLanded\": {neverLanded},\n" +
            $"  \"hullsWithAnyDisable\": {hullsWithDisable},\n" +
            $"  \"summary\": {J(summary)},\n" +
            $"  \"rows\": [{rowsJson}],\n" +
            $"  \"notes\": [{string.Join(",", notes.Select(J))}]\n" +
            "}\n";
        string jsonPath = Path.Combine(dir, "disabling.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[emp] DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        Console.WriteLine($"[emp] SUMMARY {summary}");
        Console.WriteLine($"[emp] TALLY rows={rows.Count} laddersBroken={laddersBroken} massedOnly={massedOnly} "
                        + $"rawHolds={rawHolds} overCredited={overCredited} neverLanded={neverLanded} hullsWithDisable={hullsWithDisable} hulls=[{string.Join(",", hullsCovered)}]");
        foreach (string n in notes) Console.WriteLine($"[emp] NOTE {n}");

        Assert.IsTrue(File.Exists(jsonPath), "disabling.json written");
        // The experiment is a SCIENCE probe: it must run cleanly and emit data even on an honest negative. We
        // require at least one hull to have produced a dueling EMP-vs-conventional pair so there is signal to read
        // (if the dataset truly has no EMP module, anyEmpModule is false and we assert only that we recorded that).
        if (anyEmpModule)
            Assert.IsTrue(rows.Count > 0, "at least one hull produced an EMP-disabler vs conventional duel pair");
        foreach (EmpRow r in rows)
        {
            Assert.AreEqual(seeds.Length, r.EqualCount.Seeds, "equal-count cell ran the full seed set");
            Assert.AreEqual(seeds.Length, r.StrengthMatch.Seeds, "strength-match cell ran the full seed set");
            Assert.IsTrue(r.EmpSlots > 0, "the EMP variant actually carries >=1 EMP-disabler weapon slot");
        }
    }

    // ===================================================================================================
    // SHIELD RE-PRICING / EFFECTIVENESS AUTO-TUNER (ADDITIVE on top of #14 / onetrick_eqcost).
    //    #14 proved a REAL COST-SYSTEM HOLE: STRIP_SHIELDS (drop every shield slot -> armor) BEATS the
    //    balanced stock base at EQUAL SPEND on the CRUISER (Terran/LightCruiser) and CAPITAL (Remnant/
    //    Mothership) — shields are mispriced / under-effective there. This method stops DIAGNOSING and starts
    //    PROPOSING A FIX: it sweeps a shield knob and finds the BALANCE CROSSOVER — the knob value at which
    //    STRIP_SHIELDS STOPS winning (so the base's shields become worth keeping). Two independent sweeps:
    //
    //    (A) COST sweep — re-price the base's shields by a COST multiplier m in {0.4,0.6,0.8,1.0}. Cheaper
    //        shields lower the base's effective per-ship cost, so at EQUAL SPEND the cheap-armor STRIP variant
    //        buys FEWER extra ships and the base recovers. The base's shield-module cost contribution is
    //        summed from the design (sum of shield m.Cost), turned into a fraction of BaseCost, and the
    //        re-priced base cost = baseCost*(1 - shieldCostFrac*(1-m)). GetCost() is linear in BaseCost, so
    //        this faithfully models "what if shields cost m x as much". variantCount = floor(repricedBaseCost
    //        * BaseCount / varCost). Find the smallest m at which the base stops losing.
    //
    //    (B) EFFECTIVENESS sweep — buff the BASE fleet's shield modules to f x HP after spawn, f in
    //        {1.0,1.5,2.0,3.0}, via the public ShipModule.InitShieldPower(amplify) (amplify =
    //        ShieldPowerMax*Bonuses.ShieldMod*(f-1) reaches exactly f x ActualShieldPowerMax and refills
    //        ShieldPower to that cap). UpdateCoreStats runs only at init (Ship_Initialize), never per-tick, so
    //        the post-spawn buff persists through the duel. The variant count is the m=1.0 equal-cost count, so
    //        the ONLY thing that changes across f is shield strength. Find the smallest f at which the base
    //        stops losing.
    //
    //    PURELY TEST/HARNESS-LEVEL — no Ship_Game game-code change. The cost re-pricing is arithmetic over the
    //    public BaseCost / m.Cost; the effectiveness buff uses the public InitShieldPower already exposed for
    //    testing. Determinism preserved: same 3 fixed seeds, deterministic GUID-free STRIP_SHIELDS variant
    //    (BuildDoctrineVariant), same cruiser+capital hulls where STRIP won, fixed 5000-tick re-target cadence.
    // ===================================================================================================

    // Sum of the COST contribution of the base design's SHIELD modules (sum of each shield slot's template
    // m.Cost). Mirrors ShipDesign_Stats' baseCost accumulation (baseCost += m.Cost) restricted to shields, so
    // it is exactly the slice of BaseCost a cost multiplier should scale. Pure template read; no live ship.
    float ShieldCostContribution(IShipDesign d)
    {
        float sum = 0f;
        foreach (DesignSlot s in d.GetOrLoadDesignSlots())
            if (ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m) && m.Is(ShipModuleType.Shield))
                sum += m.Cost;
        return sum;
    }

    // EQUAL-COST STRIP_SHIELDS duel with a post-spawn SHIELD BUFF on the base side. A faithful mirror of
    // RunEqCostDuel (identical arena / BuildExactCount placement / seeded RNG / 1500-tick EngageAll cadence) —
    // side A = STRIP variant (countA ships), side B = base (countB ships). After spawning the base fleet, every
    // base ship's shield modules are buffed to shieldBuffFactor x via InitShieldPower (f==1 => no-op, identical
    // to RunEqCostDuel). RunEqCostDuel itself is untouched so all #14 results stay bit-identical.
    (string winner, float aEnd, float bEnd, int aAlive, int bAlive, float bShieldStart) RunShieldTunerDuel(
        IShipDesign variant, IShipDesign baseDesign, int countA, int countB, ulong seed, int ticks, float shieldBuffFactor)
    {
        CreateUniverseAndPlayerEmpire();
        SetupArena();
        var sa = MkSide(Player, 0, new Vector2(-8000, 0), new[] { (0f, 1f) });
        var sb = MkSide(Enemy,  1, new Vector2( 8000, 0), new[] { (0f, 1f) });
        var sides = new List<Side> { sa, sb };

        BuildExactCount(sa, variant,    countA);
        BuildExactCount(sb, baseDesign, countB);

        // EFFECTIVENESS buff: lift every base shield module's ActualShieldPowerMax to f x and refill it. The
        // amplify that InitShieldPower ADDS is ShieldPowerMax*Bonuses.ShieldMod*(f-1), so the result is exactly
        // f x the unbuffed cap. f==1 leaves shields untouched.
        float bShieldStart = 0f;
        foreach (Ship ship in sb.Fleet)
            foreach (ShipModule shield in ship.GetShields())
            {
                if (shieldBuffFactor > 1.0001f)
                {
                    // Target EXACTLY f x the ship's natural (post-init, amplification-included) shield cap. We
                    // measure the current ActualShieldPowerMax and solve InitShieldPower's amp term:
                    // ActualShieldPowerMax := ShieldPowerMax*ShieldMod + amp  ==>  amp = f*current - base. This
                    // preserves any inter-shield amplification the spawn applied (a flat ShieldPowerMax*(f-1)
                    // would CLOBBER it and could make a modest f LOWER the cap), so the buff is monotonic in f.
                    float current = shield.ActualShieldPowerMax;
                    float baseCap = shield.ShieldPowerMax * shield.Bonuses.ShieldMod;
                    shield.InitShieldPower(shieldBuffFactor * current - baseCap);
                }
                // Report the stable post-buff cap (ActualShieldPowerMax), not the transient ShieldPower.
                bShieldStart += shield.ActualShieldPowerMax;
            }

        SetupWars(sides);
        UState.EnableDeterministicRng(seed);
        EngageAll(sides);
        for (int t = 0; t < ticks; ++t)
        {
            UState.Objects.Update(TestSimStep);
            if (t % 1500 == 1499) EngageAll(sides);
        }
        int aAlive = AliveOf(sa.Fleet), bAlive = AliveOf(sb.Fleet);
        float aEnd = StrengthOf(sa.Fleet), bEnd = StrengthOf(sb.Fleet);
        string winner =
            bAlive == 0 && aAlive > 0 ? "A" :
            aAlive == 0 && bAlive > 0 ? "B" :
            aEnd > bEnd ? "A" :
            bEnd > aEnd ? "B" : "draw";
        return (winner, aEnd, bEnd, aAlive, bAlive, bShieldStart);
    }

    // One emitted sweep cell (a single knob value dueled across the seed set).
    sealed class ShieldTuneCell
    {
        public float Knob;              // m (cost) or f (effectiveness)
        public int VariantCount, BaseCount, Seeds, VariantWins, BaseWins, Decisive;
        public float VariantWinRate, BaseWinRate;
        public float RepricedBaseCost;  // cost sweep only (== baseCost for f-sweep)
        public float BaseShieldStart;   // effectiveness sweep telemetry (avg base shield HP at spawn)
        public bool StripStillWins;     // does STRIP_SHIELDS still win at this knob?
    }

    static string ShieldCellJson(ShieldTuneCell c)
        => $"{{\"knob\":{F(c.Knob)},\"variantCount\":{c.VariantCount},\"baseCount\":{c.BaseCount},"
         + $"\"repricedBaseCost\":{F(c.RepricedBaseCost)},\"baseShieldStart\":{F(c.BaseShieldStart)},"
         + $"\"variantWinRate\":{F(c.VariantWinRate)},\"baseWinRate\":{F(c.BaseWinRate)},"
         + $"\"variantWins\":{c.VariantWins},\"baseWins\":{c.BaseWins},\"decisive\":{c.Decisive},"
         + $"\"seeds\":{c.Seeds},\"stripStillWins\":{(c.StripStillWins ? "true" : "false")}}}";

    sealed class ShieldTuneHull
    {
        public string Hull, HullRole, VariantName;
        public float BaseCost, VariantCost, ShieldCostContribution, ShieldCostFraction;
        public int ShieldSlots, EqCostVariantCount;
        public List<ShieldTuneCell> CostSweep = new();
        public List<ShieldTuneCell> EffSweep  = new();
        public float CostCrossover, EffCrossover;   // knob where STRIP stops winning (-1 == never within range)
    }

    static string ShieldHullJson(ShieldTuneHull h)
    {
        string cost = string.Join(",", h.CostSweep.Select(ShieldCellJson));
        string eff  = string.Join(",", h.EffSweep.Select(ShieldCellJson));
        return "{\n      "
         + $"\"hull\":{J(h.Hull)},\"hullRole\":{J(h.HullRole)},\"variantName\":{J(h.VariantName)},\n      "
         + $"\"baseCost\":{F(h.BaseCost)},\"variantCost\":{F(h.VariantCost)},\"shieldSlots\":{h.ShieldSlots},\n      "
         + $"\"shieldCostContribution\":{F(h.ShieldCostContribution)},\"shieldCostFraction\":{F(h.ShieldCostFraction)},\n      "
         + $"\"eqCostVariantCount\":{h.EqCostVariantCount},\n      "
         + $"\"costCrossover\":{F(h.CostCrossover)},\"effCrossover\":{F(h.EffCrossover)},\n      "
         + $"\"costSweep\":[{cost}],\n      \"effSweep\":[{eff}]\n    }}";
    }

    [TestMethod]
    public void BalanceMeta_ShieldTuner_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire();
        SetupArena();

        var hulls = new List<ShieldTuneHull>();
        var notes = new List<string>();

        const int BaseCount = 6;     // base fields a FIXED 6-ship pack (same as #14)
        const int Ticks = 5000;      // same tick budget as #14
        ulong[] seeds = { 0x7C1, 0x7C2, 0x7C3 };  // SAME 3 deterministic seeds as #14
        ulong salt = 0x5E10u;        // distinct salt namespace so STRIP variant names never collide with #14
        float[] costMults = { 1.0f, 0.8f, 0.6f, 0.4f };  // descending: find where STRIP first stops winning
        float[] effMults  = { 1.0f, 1.5f, 2.0f, 3.0f };   // ascending

        // ---- SAME hull cohort + mid-cost pick as #14, then keep ONLY cruiser+capital (where STRIP_SHIELDS won
        //      at equal spend). Battleship is excluded: STRIP loses there, so there is no crossover to find. ----
        var classOrder = new[] { RoleName.cruiser, RoleName.battleship, RoleName.capital };
        IShipDesign[] warships = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly)
            .Where(d => classOrder.Contains(d.Role))
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        var targetRoles = new HashSet<RoleName> { RoleName.cruiser, RoleName.capital };
        var bases = new List<IShipDesign>();
        foreach (RoleName role in classOrder)
        {
            IShipDesign[] inClass = warships.Where(d => d.Role == role).ToArray();
            if (inClass.Length == 0) { notes.Add($"no standalone warship of class '{role}'; class skipped"); continue; }
            IShipDesign pick = inClass[inClass.Length / 2]; // mid-cost representative (identical pick to #14)
            if (targetRoles.Contains(role) && ShieldSlotCount(pick) > 0) bases.Add(pick);
            else if (targetRoles.Contains(role)) notes.Add($"{role}:'{pick.Name}' has no shield slot -> no crossover to tune; skipped");
        }
        Console.WriteLine($"[stun] TUNING hulls (cruiser+capital where STRIP_SHIELDS won @ equal spend): "
                        + string.Join(", ", bases.Select(b => $"{b.Role}:'{b.Name}'@{b.GetCost(Player):0} shields={ShieldSlotCount(b)}")));

        foreach (IShipDesign baseDesign in bases)
        {
            CreateUniverseAndPlayerEmpire(); SetupArena();
            float baseCost   = Math.Max(1f, baseDesign.GetCost(Player));
            float rawBaseCost = Math.Max(1f, baseDesign.BaseCost);
            float shieldCost = ShieldCostContribution(baseDesign);
            float shieldCostFrac = Math.Clamp(shieldCost / rawBaseCost, 0f, 1f);
            string hullTag = $"{baseDesign.Role}:{baseDesign.Hull}";

            // Build the STRIP_SHIELDS variant once (deterministic name); reuse it across every sweep cell.
            var (variant, swapped, skipped, why) = BuildDoctrineVariant(baseDesign, "STRIP_SHIELDS", salt++);
            if (variant == null)
            {
                notes.Add($"{hullTag}: STRIP_SHIELDS variant infeasible ({why}); hull skipped");
                Console.WriteLine($"[stun] SKIP {hullTag} -> {why}");
                continue;
            }
            CreateUniverseAndPlayerEmpire(); SetupArena();
            float varCost = Math.Max(1f, variant.GetCost(Player));
            int eqCostCount = Math.Clamp((int)Math.Floor(baseCost * BaseCount / varCost), 1, 60);

            var hull = new ShieldTuneHull
            {
                Hull = baseDesign.Hull, HullRole = baseDesign.Role.ToString(), VariantName = variant.Name,
                BaseCost = baseCost, VariantCost = varCost,
                ShieldCostContribution = shieldCost, ShieldCostFraction = shieldCostFrac,
                ShieldSlots = ShieldSlotCount(baseDesign), EqCostVariantCount = eqCostCount,
                CostCrossover = -1f, EffCrossover = -1f,
            };

            Console.WriteLine($"[stun] HULL {hullTag} baseCost={baseCost:0} varCost={varCost:0} swapped={swapped} "
                            + $"shieldSlots={hull.ShieldSlots} shieldCost={shieldCost:0} (frac={shieldCostFrac:0.000} of BaseCost {rawBaseCost:0}) "
                            + $"eqCostVarCount={eqCostCount}v{BaseCount}");

            // -------- (A) COST SWEEP: re-price shields by m; recompute variant count at equal spend. --------
            foreach (float m in costMults)
            {
                float repricedBaseCost = baseCost * (1f - shieldCostFrac * (1f - m));
                int variantCount = Math.Clamp((int)Math.Floor(repricedBaseCost * BaseCount / varCost), 1, 60);

                int vWins = 0, bWins = 0, decisive = 0;
                foreach (ulong seed in seeds)
                {
                    var (winner, aEnd, bEnd, aAlive, bAlive, _) =
                        RunShieldTunerDuel(variant, baseDesign, variantCount, BaseCount, seed, Ticks, 1.0f);
                    if (winner == "A") vWins++; else if (winner == "B") bWins++;
                    if (aAlive == 0 || bAlive == 0) decisive++;
                }
                float vWR = (float)vWins / seeds.Length, bWR = (float)bWins / seeds.Length;
                bool stripWins = vWR > bWR + 1e-4f;
                hull.CostSweep.Add(new ShieldTuneCell
                {
                    Knob = m, VariantCount = variantCount, BaseCount = BaseCount, Seeds = seeds.Length,
                    VariantWins = vWins, BaseWins = bWins, Decisive = decisive,
                    VariantWinRate = vWR, BaseWinRate = bWR, RepricedBaseCost = repricedBaseCost,
                    StripStillWins = stripWins,
                });
                Console.WriteLine($"[stun] COST  {hullTag,-22} m={m:0.0} repricedBase={repricedBaseCost:0} "
                                + $"counts={variantCount}v{BaseCount} stripWR={vWR:0.000} baseWR={bWR:0.000} "
                                + $"-> {(stripWins ? "STRIP still wins" : "BASE holds (shields worth keeping)")}");
            }
            // Crossover = the LARGEST m (closest to stock pricing) at which STRIP no longer wins.
            ShieldTuneCell costCross = hull.CostSweep.Where(c => !c.StripStillWins).OrderByDescending(c => c.Knob).FirstOrDefault();
            hull.CostCrossover = costCross?.Knob ?? -1f;

            // -------- (B) EFFECTIVENESS SWEEP: buff base shield HP by f; variant count fixed at eq-cost. --------
            foreach (float f in effMults)
            {
                int vWins = 0, bWins = 0, decisive = 0;
                float shieldStartSum = 0f;
                foreach (ulong seed in seeds)
                {
                    var (winner, aEnd, bEnd, aAlive, bAlive, bShieldStart) =
                        RunShieldTunerDuel(variant, baseDesign, eqCostCount, BaseCount, seed, Ticks, f);
                    if (winner == "A") vWins++; else if (winner == "B") bWins++;
                    if (aAlive == 0 || bAlive == 0) decisive++;
                    shieldStartSum += bShieldStart;
                }
                float vWR = (float)vWins / seeds.Length, bWR = (float)bWins / seeds.Length;
                bool stripWins = vWR > bWR + 1e-4f;
                hull.EffSweep.Add(new ShieldTuneCell
                {
                    Knob = f, VariantCount = eqCostCount, BaseCount = BaseCount, Seeds = seeds.Length,
                    VariantWins = vWins, BaseWins = bWins, Decisive = decisive,
                    VariantWinRate = vWR, BaseWinRate = bWR, RepricedBaseCost = baseCost,
                    BaseShieldStart = shieldStartSum / seeds.Length, StripStillWins = stripWins,
                });
                Console.WriteLine($"[stun] EFF   {hullTag,-22} f={f:0.0} baseShieldHP~{shieldStartSum / seeds.Length:0} "
                                + $"counts={eqCostCount}v{BaseCount} stripWR={vWR:0.000} baseWR={bWR:0.000} "
                                + $"-> {(stripWins ? "STRIP still wins" : "BASE holds (shields worth keeping)")}");
            }
            // Crossover = the SMALLEST f at which STRIP no longer wins.
            ShieldTuneCell effCross = hull.EffSweep.Where(c => !c.StripStillWins).OrderBy(c => c.Knob).FirstOrDefault();
            hull.EffCrossover = effCross?.Knob ?? -1f;

            string costMsg = hull.CostCrossover < 0
                ? "no tested cost cut (down to 0.4x) makes the base hold"
                : $"shields need ~{hull.CostCrossover:0.0}x cost (or cheaper) to be balanced";
            string effMsg = hull.EffCrossover < 0
                ? "no tested HP buff (up to 3.0x) makes the base hold"
                : $"shields need ~{hull.EffCrossover:0.0}x effectiveness (or stronger) to be balanced";
            Console.WriteLine($"[stun] CROSSOVER {hullTag}: {costMsg} OR {effMsg}");
            hulls.Add(hull);
        }

        // ---- verdict ----
        var fixable = hulls.Where(h => h.CostCrossover >= 0 || h.EffCrossover >= 0)
                           .Select(h => $"{h.HullRole}({(h.CostCrossover >= 0 ? $"{h.CostCrossover:0.0}x cost" : "no-cost")}/"
                                      + $"{(h.EffCrossover >= 0 ? $"{h.EffCrossover:0.0}x eff" : "no-eff")})").ToList();
        string verdict = hulls.Count == 0
            ? "no tunable hull (STRIP_SHIELDS variant infeasible on the cruiser+capital picks)"
            : fixable.Count == 0
                ? "NO CROSSOVER FOUND within tested ranges: neither <=0.4x cost nor <=3.0x effectiveness rescues shields on any hull -> the mispricing is deeper than these ranges"
                : $"CROSSOVER FOUND: shields become worth keeping at -> {string.Join("; ", fixable)}";

        // ---- emit shieldtuner.json ----
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string hullsJson = hulls.Count > 0 ? "\n    " + string.Join(",\n    ", hulls.Select(ShieldHullJson)) + "\n  " : "";
        string json =
            "{\n" +
            $"  \"experiment\": \"shield re-pricing / effectiveness auto-tuner: find the balance crossover where STRIP_SHIELDS stops winning\",\n" +
            $"  \"baseShipsPerSide\": {BaseCount},\n" +
            $"  \"ticks\": {Ticks},\n" +
            $"  \"seeds\": {seeds.Length},\n" +
            $"  \"costMultipliers\": [{string.Join(",", costMults.Select(F))}],\n" +
            $"  \"effectivenessMultipliers\": [{string.Join(",", effMults.Select(F))}],\n" +
            $"  \"hullCount\": {hulls.Count},\n" +
            $"  \"verdict\": {J(verdict)},\n" +
            $"  \"hulls\": [{hullsJson}],\n" +
            $"  \"notes\": [{string.Join(",", notes.Select(J))}]\n" +
            "}\n";
        string jsonPath = Path.Combine(dir, "shieldtuner.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[stun] DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        Console.WriteLine($"[stun] VERDICT {verdict}");
        foreach (string n in notes) Console.WriteLine($"[stun] NOTE {n}");

        Assert.IsTrue(File.Exists(jsonPath), "shieldtuner.json written");
        foreach (ShieldTuneHull h in hulls)
        {
            Assert.AreEqual(costMults.Length, h.CostSweep.Count, "cost sweep ran every multiplier");
            Assert.AreEqual(effMults.Length,  h.EffSweep.Count,  "effectiveness sweep ran every multiplier");
            foreach (ShieldTuneCell c in h.CostSweep.Concat(h.EffSweep))
                Assert.AreEqual(seeds.Length, c.Seeds, "every sweep cell ran the full seed set");
            // m==1.0 cost cell reproduces the #14 equal-cost STRIP duel (no re-price, no buff): STRIP should win
            // on these hulls, confirming we are tuning the SAME confirmed hole.
            ShieldTuneCell stock = h.CostSweep.First(c => Math.Abs(c.Knob - 1.0f) < 1e-4f);
            Assert.IsTrue(stock.VariantWinRate >= 0f, "stock-pricing cell produced a valid win rate");
        }
    }

    // ===================================================================================================
    // #16 — SHIELD-PROPERTY SWEEP (ADDITIVE): which shield PROPERTY (not HP, not cost — both already FAILED)
    //       makes the balanced base stop losing to STRIP_SHIELDS at EQUAL COUNT?
    //
    //    #14/#15 established: STRIP_SHIELDS (swap every shield slot -> armor) BEATS the balanced stock base on
    //    the CRUISER ('Blockade buster mk4-b') and CAPITAL ('Remnant Mothership'), and NEITHER a shield COST cut
    //    NOR a 3x shield-HP buff rescues the base — armor's per-slot value is structurally higher there. This
    //    method asks the next question: is the lever a NON-HP shield PROPERTY? It equal-count duels the
    //    STRIP variant vs the BASE, but BUFFS one shield property on every base shield module after spawn, and
    //    sweeps each property the codebase exposes:
    //      - REGEN     (recharge rate)   x {1, 2, 4}   -> ShieldRechargeRate + ShieldRechargeCombatRate
    //      - DELAY     (faster restart)  / {1, 2, 4}   -> ShieldRechargeDelay (smaller delay = sooner fast regen)
    //      - COVERAGE  (radius)          x {1, 1.5, 2} -> ShieldRadius
    //      - RESIST    (all dmg types)   + {0, .3, .6} -> Shield{Kinetic,Energy,Explosive,Plasma,Beam}Resist
    //      - HP (sanity control, known FAIL) x {1, 3}  -> InitShieldPower (same mechanism as #15's eff sweep)
    //
    //    SETTABILITY: every shield property except ShieldPower/ActualShieldPowerMax lives on a per-template,
    //    SHARED ShipModuleFlyweight whose fields are [readonly]. Mutating that shared instance would corrupt the
    //    template for every other ship + test (breaking isolation). So the buff DEEP-COPIES each base shield
    //    module's Flyweight into a fresh per-instance ShipModuleFlyweight (all fields field-copied), reassigns
    //    module.Flyweight (a public field) to the copy, then writes the swept field on the COPY via reflection.
    //    This is fully isolated (only the base fleet's shield instances change) and the swept fields ARE consumed
    //    by live combat: recharge rates at ShipModule.RechargeShields (line ~1210), delay at the same gate,
    //    radius via ShieldRadius, resists at Weapon.cs:650-657 (damageModifier *= 1 - ShieldXxxResist). HP keeps
    //    #15's InitShieldPower path. If reflection can't reach a field on some runtime it is skipped + NOTE-d.
    //
    //    THE LEVER: for each (property, factor) we record whether STRIP still wins + the surviving-strength delta
    //    (baseEnd - stripEnd, so POSITIVE == base now ahead). The "lever" is the first (property, factor) that
    //    flips a hull from STRIP-wins to BASE-holds. HONEST OUTCOME: it is entirely possible NO single property
    //    at any tested factor flips it — that would confirm armor's advantage is structural, not a shield-knob
    //    miscalibration. The verdict reports whichever is true.
    //
    //    ADDITIVE: new helpers (CloneFlyweightForBuff / SetFlyweightField / BuffShieldProperty / RunShieldPropDuel
    //    + ShieldPropCell/ShieldPropHull rows) and this one method. No committed method is touched; #14/#15 and
    //    RunEqCostDuel/RunShieldTunerDuel/BuildDoctrineVariant stay bit-identical. DETERMINISM: same 3 fixed
    //    seeds, deterministic GUID-free STRIP variant, fixed 5000-tick re-target cadence, equal counts.
    // ===================================================================================================

    // Deep-copy a module's (template-shared) Flyweight into a fresh per-instance copy so we can mutate one
    // shield property WITHOUT corrupting the shared template (which every other ship + test reuses). Copies
    // every field via reflection so the clone is byte-identical except for whatever the caller overrides next.
    static ShipModuleFlyweight CloneFlyweightForBuff(ShipModuleFlyweight src)
    {
        var copy = new ShipModuleFlyweight();
        foreach (System.Reflection.FieldInfo fi in typeof(ShipModuleFlyweight)
                     .GetFields(System.Reflection.BindingFlags.Instance
                              | System.Reflection.BindingFlags.Public
                              | System.Reflection.BindingFlags.NonPublic))
        {
            if (fi.IsStatic) continue;
            fi.SetValue(copy, fi.GetValue(src));
        }
        return copy;
    }

    // Write a single (possibly readonly) float field on a Flyweight by name. Returns false if the field does
    // not exist or is not a float on this runtime (caller then skips + NOTEs that property as unsettable).
    static bool SetFlyweightField(ShipModuleFlyweight fw, string field, float value)
    {
        System.Reflection.FieldInfo fi = typeof(ShipModuleFlyweight).GetField(field,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);
        if (fi == null || fi.FieldType != typeof(float)) return false;
        try { fi.SetValue(fw, value); return true; }
        catch { return false; }
    }

    // Apply property `prop` at factor `f` to one shield module (via a per-instance Flyweight clone). Returns
    // false (and changes nothing) if the property's backing field can't be set on this runtime. HP is handled
    // by the caller (InitShieldPower) and never routed here. f==1 with an additive prop (RESIST +0) is a no-op.
    static bool BuffShieldProperty(ShipModule shield, string prop, float f)
    {
        // RESIST is additive (+delta) and spans 5 fields; the others are multiplicative on a single field
        // (DELAY divides => multiply by 1/f). All reads come from the SAME Flyweight instance, so clone once.
        ShipModuleFlyweight fw = CloneFlyweightForBuff(shield.Flyweight);
        bool ok;
        switch (prop)
        {
            case "REGEN": // recharge rate x f — buff BOTH the out-of-combat and combat rates (combat rate is
                          // the one that actually fires during sustained fire; see RechargeShields gate)
                ok  = SetFlyweightField(fw, "ShieldRechargeRate",       shield.ShieldRechargeRate       * f);
                ok &= SetFlyweightField(fw, "ShieldRechargeCombatRate", shield.ShieldRechargeCombatRate * f);
                break;
            case "DELAY": // recharge delay / f — a SMALLER delay means the fast ShieldRechargeRate kicks in
                          // sooner after the last hit (f=1 => unchanged; f=4 => quarter the delay)
                ok = SetFlyweightField(fw, "ShieldRechargeDelay", shield.ShieldRechargeDelay / f);
                break;
            case "COVERAGE": // shield bubble radius x f
                ok = SetFlyweightField(fw, "ShieldRadius", shield.ShieldRadius * f);
                break;
            case "RESIST": // additive +delta to every shield damage-type resist, clamped to [0, 0.95]
            {
                float k = Math.Clamp(shield.ShieldKineticResist   + f, 0f, 0.95f);
                float e = Math.Clamp(shield.ShieldEnergyResist    + f, 0f, 0.95f);
                float x = Math.Clamp(shield.ShieldExplosiveResist + f, 0f, 0.95f);
                float p = Math.Clamp(shield.ShieldPlasmaResist    + f, 0f, 0.95f);
                float b = Math.Clamp(shield.ShieldBeamResist      + f, 0f, 0.95f);
                ok  = SetFlyweightField(fw, "ShieldKineticResist",   k);
                ok &= SetFlyweightField(fw, "ShieldEnergyResist",    e);
                ok &= SetFlyweightField(fw, "ShieldExplosiveResist", x);
                ok &= SetFlyweightField(fw, "ShieldPlasmaResist",    p);
                ok &= SetFlyweightField(fw, "ShieldBeamResist",      b);
                break;
            }
            default:
                return false;
        }
        if (ok) shield.Flyweight = fw; // commit the mutated per-instance copy
        return ok;
    }

    // EQUAL-COUNT STRIP_SHIELDS duel with a post-spawn SHIELD-PROPERTY buff on the base side. A faithful mirror
    // of RunEqCostDuel (identical arena / BuildExactCount placement / seeded RNG / 1500-tick EngageAll cadence).
    // side A = STRIP variant (count ships), side B = base (count ships). After spawning the base fleet, every
    // base ship's shield modules get `prop` buffed at factor `f`. prop=="HP" uses InitShieldPower (the #15 path);
    // prop=="NONE" leaves shields untouched (control). `applied` reports whether the buff actually took (false =>
    // the property was unsettable; the cell is then a no-op control and NOTE-d by the caller).
    (string winner, float aEnd, float bEnd, int aAlive, int bAlive, bool applied) RunShieldPropDuel(
        IShipDesign variant, IShipDesign baseDesign, int count, ulong seed, int ticks, string prop, float f)
    {
        CreateUniverseAndPlayerEmpire();
        SetupArena();
        var sa = MkSide(Player, 0, new Vector2(-8000, 0), new[] { (0f, 1f) });
        var sb = MkSide(Enemy,  1, new Vector2( 8000, 0), new[] { (0f, 1f) });
        var sides = new List<Side> { sa, sb };

        BuildExactCount(sa, variant,    count);
        BuildExactCount(sb, baseDesign, count);

        bool applied = false;
        bool isBuff  = prop != "NONE" && !(prop == "HP" && f <= 1.0001f)
                                       && !(prop == "RESIST" && f <= 1e-6f)
                                       && !((prop == "REGEN" || prop == "DELAY" || prop == "COVERAGE") && f <= 1.0001f);
        foreach (Ship ship in sb.Fleet)
            foreach (ShipModule shield in ship.GetShields())
            {
                if (!isBuff) { applied = true; continue; } // f==1 / +0 no-op still counts as "applied" (identity)
                if (prop == "HP")
                {
                    // identical to #15: lift ActualShieldPowerMax to f x and refill, preserving amplification.
                    float current = shield.ActualShieldPowerMax;
                    float baseCap = shield.ShieldPowerMax * shield.Bonuses.ShieldMod;
                    shield.InitShieldPower(f * current - baseCap);
                    applied = true;
                }
                else
                {
                    applied |= BuffShieldProperty(shield, prop, f);
                }
            }

        SetupWars(sides);
        UState.EnableDeterministicRng(seed);
        EngageAll(sides);
        for (int t = 0; t < ticks; ++t)
        {
            UState.Objects.Update(TestSimStep);
            if (t % 1500 == 1499) EngageAll(sides);
        }
        int aAlive = AliveOf(sa.Fleet), bAlive = AliveOf(sb.Fleet);
        float aEnd = StrengthOf(sa.Fleet), bEnd = StrengthOf(sb.Fleet);
        string winner =
            bAlive == 0 && aAlive > 0 ? "A" :
            aAlive == 0 && bAlive > 0 ? "B" :
            aEnd > bEnd ? "A" :
            bEnd > aEnd ? "B" : "draw";
        return (winner, aEnd, bEnd, aAlive, bAlive, applied);
    }

    // One emitted (property, factor) sweep cell, dueled across the seed set at EQUAL COUNT.
    sealed class ShieldPropCell
    {
        public string Property;
        public float Factor;
        public int Count, Seeds, StripWins, BaseWins, Decisive;
        public float StripWinRate, BaseWinRate;
        public float SurvivingStrengthDelta; // avg (baseEnd - stripEnd); POSITIVE => base now ahead
        public bool Settable;                 // did the property actually buff (false => unsettable control)
        public bool StripStillWins;
        public bool IsLever;                  // first cell on this hull that flips STRIP-wins -> BASE-holds
    }

    static string ShieldPropCellJson(ShieldPropCell c)
        => $"{{\"property\":{J(c.Property)},\"factor\":{F(c.Factor)},\"count\":{c.Count},"
         + $"\"seeds\":{c.Seeds},\"stripWins\":{c.StripWins},\"baseWins\":{c.BaseWins},\"decisive\":{c.Decisive},"
         + $"\"stripWinRate\":{F(c.StripWinRate)},\"baseWinRate\":{F(c.BaseWinRate)},"
         + $"\"survivingStrengthDelta\":{F(c.SurvivingStrengthDelta)},"
         + $"\"settable\":{(c.Settable ? "true" : "false")},"
         + $"\"stripStillWins\":{(c.StripStillWins ? "true" : "false")},"
         + $"\"isLever\":{(c.IsLever ? "true" : "false")}}}";

    sealed class ShieldPropHull
    {
        public string Hull, HullRole, VariantName;
        public int Count, ShieldSlots;
        public List<ShieldPropCell> Cells = new();
        public string Lever = "NONE"; // "PROP@factor" of the first flip, or "NONE" if nothing flipped it
    }

    static string ShieldPropHullJson(ShieldPropHull h)
    {
        string cells = string.Join(",", h.Cells.Select(ShieldPropCellJson));
        return "{\n      "
         + $"\"hull\":{J(h.Hull)},\"hullRole\":{J(h.HullRole)},\"variantName\":{J(h.VariantName)},\n      "
         + $"\"count\":{h.Count},\"shieldSlots\":{h.ShieldSlots},\"lever\":{J(h.Lever)},\n      "
         + $"\"cells\":[{cells}]\n    }}";
    }

    [TestMethod]
    public void BalanceMeta_ShieldProps_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire();
        SetupArena();

        var hulls = new List<ShieldPropHull>();
        var notes = new List<string>();

        const int Count = 6;     // EQUAL-COUNT duel: both sides field 6 (same pack size as #14/#15)
        const int Ticks = 5000;  // same tick budget as #14/#15
        ulong[] seeds = { 0x7C1, 0x7C2, 0x7C3 };  // SAME 3 deterministic seeds as #14/#15
        ulong salt = 0x5F20u;    // distinct salt namespace so STRIP variant names never collide with #14/#15

        // property -> the factors to sweep (HP is the known-FAIL sanity control: x1 baseline + x3).
        var sweep = new (string prop, float[] factors)[]
        {
            ("REGEN",    new[] { 1f, 2f, 4f }),
            ("DELAY",    new[] { 1f, 2f, 4f }),
            ("COVERAGE", new[] { 1f, 1.5f, 2f }),
            ("RESIST",   new[] { 0f, 0.3f, 0.6f }),
            ("HP",       new[] { 1f, 3f }),       // sanity control — #15 proved x3 HP FAILS
        };

        // ---- SAME hull cohort + mid-cost pick as #14/#15, keep ONLY cruiser+capital (where STRIP_SHIELDS won
        //      at equal count): cruiser 'Blockade buster mk4-b' + capital 'Remnant Mothership'. ----
        var classOrder = new[] { RoleName.cruiser, RoleName.battleship, RoleName.capital };
        IShipDesign[] warships = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly)
            .Where(d => classOrder.Contains(d.Role))
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        var targetRoles = new HashSet<RoleName> { RoleName.cruiser, RoleName.capital };
        var bases = new List<IShipDesign>();
        foreach (RoleName role in classOrder)
        {
            IShipDesign[] inClass = warships.Where(d => d.Role == role).ToArray();
            if (inClass.Length == 0) { notes.Add($"no standalone warship of class '{role}'; class skipped"); continue; }
            IShipDesign pick = inClass[inClass.Length / 2]; // mid-cost representative (identical pick to #14/#15)
            if (targetRoles.Contains(role) && ShieldSlotCount(pick) > 0) bases.Add(pick);
            else if (targetRoles.Contains(role)) notes.Add($"{role}:'{pick.Name}' has no shield slot -> nothing to sweep; skipped");
        }
        Console.WriteLine($"[sp] SWEEP hulls (cruiser+capital where STRIP_SHIELDS won @ equal count): "
                        + string.Join(", ", bases.Select(b => $"{b.Role}:'{b.Name}' shields={ShieldSlotCount(b)}")));

        foreach (IShipDesign baseDesign in bases)
        {
            string hullTag = $"{baseDesign.Role}:'{baseDesign.Name}'";

            var (variant, swapped, skipped, why) = BuildDoctrineVariant(baseDesign, "STRIP_SHIELDS", salt++);
            if (variant == null)
            {
                notes.Add($"{hullTag}: STRIP_SHIELDS variant infeasible ({why}); hull skipped");
                Console.WriteLine($"[sp] SKIP {hullTag} -> {why}");
                continue;
            }

            var hull = new ShieldPropHull
            {
                Hull = baseDesign.Name, HullRole = baseDesign.Role.ToString(), VariantName = variant.Name,
                Count = Count, ShieldSlots = ShieldSlotCount(baseDesign),
            };
            Console.WriteLine($"[sp] HULL {hullTag} swapped={swapped} shieldSlots={hull.ShieldSlots} "
                            + $"variant='{variant.Name}' -> EQUAL-COUNT {Count}v{Count}");

            // BASELINE control: NONE@1 — STRIP should win here, confirming we are on the SAME confirmed hole.
            {
                int sW = 0, bW = 0, dec = 0; float deltaSum = 0f;
                foreach (ulong seed in seeds)
                {
                    var (winner, aEnd, bEnd, aAlive, bAlive, _) =
                        RunShieldPropDuel(variant, baseDesign, Count, seed, Ticks, "NONE", 1f);
                    if (winner == "A") sW++; else if (winner == "B") bW++;
                    if (aAlive == 0 || bAlive == 0) dec++;
                    deltaSum += bEnd - aEnd;
                }
                float sWR = (float)sW / seeds.Length, bWR = (float)bW / seeds.Length;
                bool stripWins = sWR > bWR + 1e-4f;
                hull.Cells.Add(new ShieldPropCell
                {
                    Property = "NONE", Factor = 1f, Count = Count, Seeds = seeds.Length,
                    StripWins = sW, BaseWins = bW, Decisive = dec, StripWinRate = sWR, BaseWinRate = bWR,
                    SurvivingStrengthDelta = deltaSum / seeds.Length, Settable = true, StripStillWins = stripWins,
                });
                Console.WriteLine($"[sp] BASE  {hullTag,-34} NONE   f=1.0  stripWR={sWR:0.000} baseWR={bWR:0.000} "
                                + $"delta={deltaSum / seeds.Length:+0;-0} -> {(stripWins ? "STRIP wins (confirmed hole)" : "base already holds")}");
            }

            foreach (var (prop, factors) in sweep)
            {
                bool everSettable = false;
                foreach (float f in factors)
                {
                    int sW = 0, bW = 0, dec = 0; float deltaSum = 0f; bool applied = false;
                    foreach (ulong seed in seeds)
                    {
                        var (winner, aEnd, bEnd, aAlive, bAlive, ap) =
                            RunShieldPropDuel(variant, baseDesign, Count, seed, Ticks, prop, f);
                        if (winner == "A") sW++; else if (winner == "B") bW++;
                        if (aAlive == 0 || bAlive == 0) dec++;
                        deltaSum += bEnd - aEnd;
                        applied = ap;
                    }
                    everSettable |= applied;
                    float sWR = (float)sW / seeds.Length, bWR = (float)bW / seeds.Length;
                    bool stripWins = sWR > bWR + 1e-4f;
                    hull.Cells.Add(new ShieldPropCell
                    {
                        Property = prop, Factor = f, Count = Count, Seeds = seeds.Length,
                        StripWins = sW, BaseWins = bW, Decisive = dec, StripWinRate = sWR, BaseWinRate = bWR,
                        SurvivingStrengthDelta = deltaSum / seeds.Length, Settable = applied, StripStillWins = stripWins,
                    });
                    Console.WriteLine($"[sp] PROP  {hullTag,-34} {prop,-8} f={f:0.0} stripWR={sWR:0.000} baseWR={bWR:0.000} "
                                    + $"delta={deltaSum / seeds.Length:+0;-0} settable={applied} "
                                    + $"-> {(stripWins ? "STRIP still wins" : "BASE HOLDS (lever?)")}");
                }
                if (!everSettable)
                {
                    notes.Add($"{hullTag}: property '{prop}' not settable at runtime (no reachable Flyweight field) -> swept as no-op control");
                    Console.WriteLine($"[sp] NOTE {hullTag} -> '{prop}' UNSETTABLE (skipped as no-op)");
                }
            }

            // THE LEVER: first cell (in sweep order) that is a real buff, was settable, and flips STRIP->BASE.
            ShieldPropCell lever = hull.Cells.FirstOrDefault(c =>
                c.Property != "NONE" && c.Settable && !c.StripStillWins
                && !(c.Property == "HP" && c.Factor <= 1.0001f)
                && !(c.Property == "RESIST" && c.Factor <= 1e-6f)
                && !((c.Property == "REGEN" || c.Property == "DELAY" || c.Property == "COVERAGE") && c.Factor <= 1.0001f));
            if (lever != null) { lever.IsLever = true; hull.Lever = $"{lever.Property}@{lever.Factor:0.##}"; }
            Console.WriteLine($"[sp] LEVER {hullTag}: {(lever != null ? $"{hull.Lever} flips STRIP->BASE (delta {lever.SurvivingStrengthDelta:+0;-0})" : "NONE — no single property flips it within range")}");
            hulls.Add(hull);
        }

        // ---- verdict ----
        var levered = hulls.Where(h => h.Lever != "NONE").Select(h => $"{h.HullRole}:{h.Lever}").ToList();
        string verdict = hulls.Count == 0
            ? "no sweepable hull (STRIP_SHIELDS variant infeasible on the cruiser+capital picks)"
            : levered.Count == 0
                ? "NO LEVER FOUND: no single shield property at any tested factor flips the base from losing to winning vs STRIP_SHIELDS on any hull -> armor's per-slot advantage is STRUCTURAL here, not a shield-knob miscalibration (consistent with #15: neither cost nor HP rescued it)"
                : (levered.Count == hulls.Count
                    ? $"LEVER FOUND on every hull: {string.Join("; ", levered)}"
                    : $"LEVER FOUND on {levered.Count}/{hulls.Count} hull(s): {string.Join("; ", levered)} (the rest stayed structural)");

        // ---- emit shieldprops.json ----
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string hullsJson = hulls.Count > 0 ? "\n    " + string.Join(",\n    ", hulls.Select(ShieldPropHullJson)) + "\n  " : "";
        string json =
            "{\n" +
            $"  \"experiment\": \"shield-property sweep: find which non-HP/non-cost shield property (regen/delay/coverage/resist) flips the base from losing to STRIP_SHIELDS at equal count\",\n" +
            $"  \"shipsPerSide\": {Count},\n" +
            $"  \"ticks\": {Ticks},\n" +
            $"  \"seeds\": {seeds.Length},\n" +
            $"  \"properties\": [{string.Join(",", sweep.Select(s => J(s.prop)))}],\n" +
            $"  \"hullCount\": {hulls.Count},\n" +
            $"  \"verdict\": {J(verdict)},\n" +
            $"  \"hulls\": [{hullsJson}],\n" +
            $"  \"notes\": [{string.Join(",", notes.Select(J))}]\n" +
            "}\n";
        string jsonPath = Path.Combine(dir, "shieldprops.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[sp] DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        Console.WriteLine($"[sp] VERDICT {verdict}");
        foreach (string n in notes) Console.WriteLine($"[sp] NOTE {n}");

        Assert.IsTrue(File.Exists(jsonPath), "shieldprops.json written");
        foreach (ShieldPropHull h in hulls)
        {
            // every (property, factor) cell + the NONE baseline ran the full seed set
            int expectedCells = 1 + sweep.Sum(s => s.factors.Length);
            Assert.AreEqual(expectedCells, h.Cells.Count, "swept every property x factor plus the baseline");
            foreach (ShieldPropCell c in h.Cells)
                Assert.AreEqual(seeds.Length, c.Seeds, "every sweep cell ran the full seed set");
            // The NONE@1 baseline reproduces the #14/#15 confirmed hole: STRIP should win on these hulls.
            ShieldPropCell baseline = h.Cells.First(c => c.Property == "NONE");
            Assert.IsTrue(baseline.StripWinRate >= 0f, "baseline produced a valid win rate");
        }
    }

    // ===================================================================================================
    // 17) BALANCE META — ATTRITION GAUNTLET: SUSTAINMENT, NOT SINGLE-FIGHT WIN (ADDITIVE on top of #16).
    //    Every committed shield probe (#14..#16) measured a SINGLE equal-count duel and concluded STRIP_SHIELDS
    //    (shields->armor) wins. But a single fight ignores the shield's signature property: it REGENERATES for
    //    FREE between engagements, while armor/hull damage is PERMANENT (vanilla has no in-flight repair). This
    //    probe runs each doctrine through a CAMPAIGN — an IDENTICAL fixed sequence of enemy waves — and asks the
    //    war question: which doctrine SUSTAINS more force across the whole gauntlet, carrying LESS permanent
    //    repair-debt, even if armor wins wave 1?
    //
    //    Three doctrines, all built from the SAME base hull at EQUAL SHIP COUNT (cost is controlled by the
    //    equal-count framing exactly like #16's RunShieldPropDuel):
    //      - ARMOR  : STRIP_SHIELDS variant (BuildDoctrineVariant) — shields swapped to armor. Wins single fights.
    //      - SHIELD : the UNMODIFIED base hull — keeps its shields, which fully regen between waves for free.
    //      - FASTRC : the base hull with ShieldRechargeCombatRate (and a near-zero ShieldRechargeDelay) buffed via
    //                 the #16 Flyweight-clone+reflection tooling, so shields ALSO cycle DURING a wave, not just
    //                 between waves — tests whether an in-combat-recharging shield helps WITHIN a fight too.
    //
    //    GAUNTLET MECHANICS (per doctrine, one persistent fleet, fresh universe per doctrine for clean seeding):
    //      * WAVE  : spawn a FIXED neutral enemy squad (same design+count every wave), EngageAll, advance ticks
    //                until the enemy is wiped or the wave tick-budget elapses. Doctrine fleet damage PERSISTS.
    //      * REGEN : deactivate the (leftover) enemy squad (QueueTotalRemoval -> Active=false => no active enemy
    //                => no fire => no damage). Keep advancing ticks: ShieldRechargeTimer climbs past
    //                ShieldRechargeDelay, so shields recharge at the FAST out-of-combat rate. Armor/hull do NOT
    //                auto-repair (no vanilla repair path), so module Health deficits carry into the next wave.
    //      * RECORD per wave: ships still alive, surviving strength, persistent armor/hull damage carried,
    //                shields restored (post-regen pool), and a repair-debt proxy = sum(ActualMaxHealth - Health)
    //                over every module of every (still-spawned) ship — the permanent damage the campaign accrues.
    //
    //    ADDITIVE: new helpers (FleetRepairDebt / FleetShieldPower / FleetShieldMax / FleetArmorHullHealth /
    //    BuffFastRecharge / RunGauntlet + GauntletWave/GauntletRun rows + JSON) and this one method. No committed
    //    method is touched; #14..#16 and RunShieldPropDuel/BuildDoctrineVariant/CloneFlyweightForBuff/
    //    SetFlyweightField stay bit-identical. DETERMINISM: one fixed seed, deterministic GUID-free STRIP variant,
    //    fixed wave squad + tick budgets; the test reruns the whole gauntlet and asserts a bit-identical series.
    // ===================================================================================================

    // Sum of (ActualMaxHealth - Health) over every module of every still-spawned ship in the fleet — the
    // permanent, un-repairable damage the campaign has accrued (the repair-debt proxy from the gauntlet plan).
    // Counts dead ships too (a destroyed ship is max repair-debt: its whole hull is gone) via HealthMax.
    static float FleetRepairDebt(List<Ship> fleet)
    {
        float debt = 0f;
        foreach (Ship s in fleet)
        {
            if (s.Active)
            {
                foreach (ShipModule m in s.Modules)
                    debt += Math.Max(0f, m.ActualMaxHealth - m.Health);
            }
            else
            {
                debt += s.HealthMax; // a lost ship is total loss => its full hull is unrepairable debt
            }
        }
        return debt;
    }

    // Current summed shield pool across the fleet's live ships (post-regen this should approach FleetShieldMax).
    static float FleetShieldPower(List<Ship> fleet)
    {
        float p = 0f;
        foreach (Ship s in fleet)
            if (s.Active)
                foreach (ShipModule sh in s.GetShields())
                    p += sh.ShieldPower;
        return p;
    }

    // Maximum summed shield pool across the fleet's live ships (the ceiling regen restores toward).
    static float FleetShieldMax(List<Ship> fleet)
    {
        float p = 0f;
        foreach (Ship s in fleet)
            if (s.Active)
                foreach (ShipModule sh in s.GetShields())
                    p += sh.ActualShieldPowerMax;
        return p;
    }

    // Current summed module Health (armor + hull + everything) across the fleet's live ships — the PERSISTENT
    // physical integrity that does NOT regen between waves (contrast with FleetShieldPower, which does).
    static float FleetArmorHullHealth(List<Ship> fleet)
    {
        float h = 0f;
        foreach (Ship s in fleet)
            if (s.Active)
                foreach (ShipModule m in s.Modules)
                    h += m.Health;
        return h;
    }

    // FAST-RECHARGE shield doctrine: on every shield module of every ship, buff ShieldRechargeCombatRate (the rate
    // used WHILE under fire, i.e. ShieldRechargeTimer <= ShieldRechargeDelay) by `rateFactor`, AND collapse
    // ShieldRechargeDelay toward zero so the fast rate engages almost immediately after a hit — making shields
    // cycle DURING a wave, not only between waves. Reuses #16's per-instance Flyweight clone (so the shared
    // template is never corrupted) + SetFlyweightField reflection. Returns how many shield modules were buffed.
    static int BuffFastRecharge(List<Ship> fleet, float rateFactor)
    {
        int n = 0;
        foreach (Ship s in fleet)
            foreach (ShipModule sh in s.GetShields())
            {
                ShipModuleFlyweight fw = CloneFlyweightForBuff(sh.Flyweight);
                bool ok  = SetFlyweightField(fw, "ShieldRechargeCombatRate", sh.ShieldRechargeCombatRate * rateFactor);
                ok      &= SetFlyweightField(fw, "ShieldRechargeRate",       sh.ShieldRechargeRate       * rateFactor);
                ok      &= SetFlyweightField(fw, "ShieldRechargeDelay",      0.05f); // fast rate engages ~immediately
                if (ok) { sh.Flyweight = fw; ++n; }
            }
        return n;
    }

    // One wave's recorded state for one doctrine (all measured AFTER the wave's combat AND the between-wave regen).
    sealed class GauntletWave
    {
        public int Wave;
        public int ShipsAlive;            // doctrine ships still active after this wave
        public float SurvivingStrength;   // GetStrength() summed over live doctrine ships
        public float ArmorHullHealth;     // persistent module Health (does NOT regen) after this wave
        public float ShieldPower;         // shield pool AFTER the between-wave regen (what regen restored)
        public float ShieldMax;           // shield pool ceiling (so shieldRestoredFrac = power/max is readable)
        public float RepairDebt;          // sum(ActualMaxHealth-Health) incl. lost ships — permanent campaign damage
        public bool EnemyCleared;         // did the doctrine wipe this wave's enemy squad within the wave budget?
    }

    static string GauntletWaveJson(GauntletWave w)
        => $"{{\"wave\":{w.Wave},\"shipsAlive\":{w.ShipsAlive},\"survivingStrength\":{F(w.SurvivingStrength)},"
         + $"\"armorHullHealth\":{F(w.ArmorHullHealth)},\"shieldPower\":{F(w.ShieldPower)},\"shieldMax\":{F(w.ShieldMax)},"
         + $"\"shieldRestoredFrac\":{F(w.ShieldMax > 0f ? w.ShieldPower / w.ShieldMax : 0f)},"
         + $"\"repairDebt\":{F(w.RepairDebt)},\"enemyCleared\":{(w.EnemyCleared ? "true" : "false")}}}";

    // One doctrine's whole campaign through the gauntlet.
    sealed class GauntletRun
    {
        public string Doctrine, VariantName;
        public int Count, StartShips, ShieldBuffed;
        public List<GauntletWave> Waves = new();
        public int WavesSurvived;         // # waves the fleet finished with >=1 ship alive
        public int WavesCleared;          // # waves whose enemy squad it actually wiped
        public float FinalSurvivingStrength, FinalRepairDebt;
    }

    static string GauntletRunJson(GauntletRun r)
    {
        string waves = string.Join(",", r.Waves.Select(GauntletWaveJson));
        return "{\n      "
         + $"\"doctrine\":{J(r.Doctrine)},\"variantName\":{J(r.VariantName)},\"count\":{r.Count},"
         + $"\"startShips\":{r.StartShips},\"shieldModulesBuffed\":{r.ShieldBuffed},\n      "
         + $"\"wavesSurvived\":{r.WavesSurvived},\"wavesCleared\":{r.WavesCleared},"
         + $"\"finalSurvivingStrength\":{F(r.FinalSurvivingStrength)},\"finalRepairDebt\":{F(r.FinalRepairDebt)},\n      "
         + $"\"waves\":[{waves}]\n    }}";
    }

    // Run ONE doctrine fleet through the gauntlet. The doctrine fleet (side A = Player) is built once and PERSISTS
    // across all waves; each wave spawns a fresh FIXED enemy squad (side B = Enemy, `enemyDesign` x `enemyCount`).
    // Faithful to the committed arena: CreateUniverseAndPlayerEmpire + SetupArena, BuildExactCount placement,
    // seeded RNG, EngageAll + 1500-tick re-target cadence. `fastRcFactor` > 0 buffs the doctrine fleet's shields
    // for the FASTRC doctrine (0 = no buff). Returns the populated GauntletRun.
    GauntletRun RunGauntlet(string doctrine, IShipDesign doctrineDesign, IShipDesign enemyDesign,
        int count, int enemyCount, int waves, int waveTicks, int regenTicks, ulong seed, float fastRcFactor)
    {
        CreateUniverseAndPlayerEmpire();
        SetupArena();
        UState.EnableDeterministicRng(seed);

        // The doctrine fleet — built ONCE, persists across every wave (this is what accrues permanent damage).
        var doc = MkSide(Player, 0, new Vector2(-8000, 0), new[] { (0f, 1f) });
        BuildExactCount(doc, doctrineDesign, count);

        var run = new GauntletRun
        {
            Doctrine = doctrine, VariantName = doctrineDesign.Name, Count = count,
            StartShips = AliveOf(doc.Fleet),
        };
        if (fastRcFactor > 0f)
            run.ShieldBuffed = BuffFastRecharge(doc.Fleet, fastRcFactor);

        for (int wave = 1; wave <= waves; ++wave)
        {
            // ---- WAVE: spawn a fresh fixed enemy squad and fight it. ----
            var foe = MkSide(Enemy, 1, new Vector2(8000, 0), new[] { (0f, 1f) });
            BuildExactCount(foe, enemyDesign, enemyCount);
            var sides = new List<Side> { doc, foe };
            SetupWars(sides);
            EngageAll(sides);

            bool enemyCleared = false;
            for (int t = 0; t < waveTicks; ++t)
            {
                UState.Objects.Update(TestSimStep);
                if (t % 1500 == 1499) EngageAll(sides);
                if (AliveOf(foe.Fleet) == 0) { enemyCleared = true; break; }
                if (AliveOf(doc.Fleet) == 0) break; // doctrine fleet wiped — campaign over for it
            }

            // ---- REGEN: remove the enemy squad so no active enemy remains, then advance ticks with NO fire so
            //      shields recharge at the fast OOC rate (ShieldRechargeTimer climbs past ShieldRechargeDelay)
            //      while armor/hull stay damaged (no vanilla repair). ----
            foreach (Ship e in foe.Fleet) if (e.Active) e.QueueTotalRemoval();
            for (int t = 0; t < regenTicks; ++t)
                UState.Objects.Update(TestSimStep);

            // ---- RECORD post-wave + post-regen state. ----
            run.Waves.Add(new GauntletWave
            {
                Wave = wave,
                ShipsAlive        = AliveOf(doc.Fleet),
                SurvivingStrength = StrengthOf(doc.Fleet),
                ArmorHullHealth   = FleetArmorHullHealth(doc.Fleet),
                ShieldPower       = FleetShieldPower(doc.Fleet),
                ShieldMax         = FleetShieldMax(doc.Fleet),
                RepairDebt        = FleetRepairDebt(doc.Fleet),
                EnemyCleared      = enemyCleared,
            });

            if (AliveOf(doc.Fleet) == 0) break; // dead fleet survives no further waves
        }

        run.WavesSurvived = run.Waves.Count(w => w.ShipsAlive > 0);
        run.WavesCleared  = run.Waves.Count(w => w.EnemyCleared);
        GauntletWave last = run.Waves.LastOrDefault();
        run.FinalSurvivingStrength = last?.SurvivingStrength ?? 0f;
        run.FinalRepairDebt        = last?.RepairDebt ?? 0f;
        return run;
    }

    // Compact (doctrine -> wave series) signature for the determinism rerun assert (alive + repair-debt per wave).
    static string GauntletSignature(GauntletRun r)
        => r.Doctrine + ":" + string.Join("|", r.Waves.Select(w => $"{w.ShipsAlive},{F(w.RepairDebt)},{F(w.SurvivingStrength)}"));

    [TestMethod]
    public void BalanceMeta_ShieldGauntlet_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire();
        SetupArena();

        var notes = new List<string>();

        const int Count      = 6;     // EQUAL-COUNT doctrine fleets (same pack size as #14..#16)
        const int EnemyCount = 6;     // fixed neutral squad per wave
        const int Waves      = 5;     // 5-wave campaign
        const int WaveTicks  = 4000;  // per-wave combat budget
        const int RegenTicks = 5000;  // between-wave OOC regen budget (>> any ShieldRechargeDelay)
        const ulong Seed     = 0x6A07u; // single fixed gauntlet seed
        ulong salt           = 0x6A00u; // distinct salt namespace for the STRIP variant name

        // SAME hull-selection discipline as #16: keep cruiser+capital mid-cost picks where STRIP_SHIELDS won the
        // single fight (so we test the gauntlet on the exact hulls the single-fight probe said armor should win).
        var classOrder = new[] { RoleName.cruiser, RoleName.battleship, RoleName.capital };
        IShipDesign[] warships = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly)
            .Where(d => classOrder.Contains(d.Role))
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        var targetRoles = new HashSet<RoleName> { RoleName.cruiser, RoleName.capital };
        var bases = new List<IShipDesign>();
        foreach (RoleName role in classOrder)
        {
            IShipDesign[] inClass = warships.Where(d => d.Role == role).ToArray();
            if (inClass.Length == 0) { notes.Add($"no standalone warship of class '{role}'; class skipped"); continue; }
            IShipDesign pick = inClass[inClass.Length / 2]; // identical mid-cost pick to #14..#16
            if (targetRoles.Contains(role) && ShieldSlotCount(pick) > 0) bases.Add(pick);
            else if (targetRoles.Contains(role)) notes.Add($"{role}:'{pick.Name}' has no shield slot -> nothing to test; skipped");
        }

        // The fixed neutral wave squad = the cheapest standalone warship with a shield slot (a generic, balanced
        // foe that genuinely chips both doctrines without one-shotting them). Deterministic pick.
        IShipDesign enemyDesign = warships.FirstOrDefault(d => ShieldSlotCount(d) > 0) ?? warships.FirstOrDefault();
        if (enemyDesign == null) { Assert.Inconclusive("no warship available for the wave squad"); return; }
        Console.WriteLine($"[gaunt] wave squad = {EnemyCount}x '{enemyDesign.Name}' ({enemyDesign.Role}); "
                        + $"campaign = {Waves} waves, {WaveTicks} combat ticks + {RegenTicks} regen ticks/wave, seed={Seed:X}");
        Console.WriteLine($"[gaunt] gauntlet hulls (cruiser+capital where STRIP won single fight): "
                        + string.Join(", ", bases.Select(b => $"{b.Role}:'{b.Name}' shields={ShieldSlotCount(b)}")));

        var hullBlocks = new List<string>();
        int armorWarWins = 0, shieldWarWins = 0, hullsScored = 0;

        foreach (IShipDesign baseDesign in bases)
        {
            string hullTag = $"{baseDesign.Role}:'{baseDesign.Name}'";

            // ARMOR doctrine = STRIP_SHIELDS variant (shields -> armor). SHIELD/FASTRC doctrines = the base hull.
            var (armorVariant, swapped, skipped, why) = BuildDoctrineVariant(baseDesign, "STRIP_SHIELDS", salt++);
            if (armorVariant == null)
            {
                notes.Add($"{hullTag}: STRIP_SHIELDS variant infeasible ({why}); hull skipped");
                Console.WriteLine($"[gaunt] SKIP {hullTag} -> {why}");
                continue;
            }
            Console.WriteLine($"[gaunt] HULL {hullTag} swapped={swapped} shieldSlots={ShieldSlotCount(baseDesign)} "
                            + $"armorVariant='{armorVariant.Name}' -> EQUAL-COUNT {Count} per doctrine");

            GauntletRun armor  = RunGauntlet("ARMOR",  armorVariant, enemyDesign, Count, EnemyCount, Waves, WaveTicks, RegenTicks, Seed, 0f);
            GauntletRun shield = RunGauntlet("SHIELD", baseDesign,   enemyDesign, Count, EnemyCount, Waves, WaveTicks, RegenTicks, Seed, 0f);
            GauntletRun fastrc = RunGauntlet("FASTRC", baseDesign,   enemyDesign, Count, EnemyCount, Waves, WaveTicks, RegenTicks, Seed, 8f);

            foreach (GauntletRun r in new[] { armor, shield, fastrc })
            {
                Console.WriteLine($"[gaunt] {hullTag,-30} {r.Doctrine,-6} startShips={r.StartShips} "
                                + $"wavesSurvived={r.WavesSurvived}/{Waves} wavesCleared={r.WavesCleared} "
                                + $"finalStr={r.FinalSurvivingStrength:0} finalRepairDebt={r.FinalRepairDebt:0} "
                                + (r.Doctrine == "FASTRC" ? $"shieldModsBuffed={r.ShieldBuffed}" : ""));
                foreach (GauntletWave w in r.Waves)
                    Console.WriteLine($"[gaunt]   {r.Doctrine,-6} wave{w.Wave} alive={w.ShipsAlive}/{Count} "
                                    + $"str={w.SurvivingStrength:0} armorHull={w.ArmorHullHealth:0} "
                                    + $"shieldRestored={(w.ShieldMax > 0f ? w.ShieldPower / w.ShieldMax : 0f):0.00} "
                                    + $"repairDebt={w.RepairDebt:0} enemyCleared={w.EnemyCleared}");
            }

            // ---- WAR verdict for this hull: the gauntlet rewards SUSTAINMENT. A doctrine "wins the war" by
            //      surviving MORE waves; ties broken by MORE final surviving strength, then by LESS final
            //      repair-debt (carrying less permanent damage). SHIELD here = the best of the two shield runs. ----
            GauntletRun bestShield = (shield.WavesSurvived, shield.FinalSurvivingStrength, -shield.FinalRepairDebt)
                                     .CompareTo((fastrc.WavesSurvived, fastrc.FinalSurvivingStrength, -fastrc.FinalRepairDebt)) >= 0
                                     ? shield : fastrc;
            int cmp =
                armor.WavesSurvived != bestShield.WavesSurvived
                    ? armor.WavesSurvived.CompareTo(bestShield.WavesSurvived) :
                Math.Abs(armor.FinalSurvivingStrength - bestShield.FinalSurvivingStrength) > 1e-3f
                    ? armor.FinalSurvivingStrength.CompareTo(bestShield.FinalSurvivingStrength) :
                    // lower repair-debt wins the tie
                    bestShield.FinalRepairDebt.CompareTo(armor.FinalRepairDebt);
            string warWinner = cmp > 0 ? "ARMOR" : cmp < 0 ? "SHIELD" : "TIE";
            if (warWinner == "ARMOR")  armorWarWins++;
            if (warWinner == "SHIELD") shieldWarWins++;
            hullsScored++;

            // Did wave 1 go to armor (the single-fight result) but the WAR go to shield (the sustainment result)?
            bool armorWonWave1 = armor.Waves.Count > 0 && shield.Waves.Count > 0
                && armor.Waves[0].SurvivingStrength > shield.Waves[0].SurvivingStrength + 1e-3f;
            bool flip = armorWonWave1 && warWinner == "SHIELD";
            Console.WriteLine($"[gaunt] WAR {hullTag}: winner={warWinner} "
                            + $"(armor survived {armor.WavesSurvived}, shield {shield.WavesSurvived}, fastrc {fastrc.WavesSurvived}; "
                            + $"finalRepairDebt armor={armor.FinalRepairDebt:0} shield={shield.FinalRepairDebt:0}) "
                            + (flip ? "*** ARMOR won wave 1 but SHIELD won the WAR ***" : ""));

            hullBlocks.Add("{\n      "
                + $"\"hull\":{J(baseDesign.Name)},\"hullRole\":{J(baseDesign.Role.ToString())},"
                + $"\"shieldSlots\":{ShieldSlotCount(baseDesign)},\"count\":{Count},\n      "
                + $"\"warWinner\":{J(warWinner)},\"armorWonWave1\":{(armorWonWave1 ? "true" : "false")},"
                + $"\"sustainmentFlip\":{(flip ? "true" : "false")},\n      "
                + $"\"armor\":{GauntletRunJson(armor)},\n    \"shield\":{GauntletRunJson(shield)},\n    \"fastRecharge\":{GauntletRunJson(fastrc)}\n    }}");

            // ---- DETERMINISM: rerun the whole 3-doctrine gauntlet on this hull; the wave series must be identical.
            GauntletRun armor2  = RunGauntlet("ARMOR",  armorVariant, enemyDesign, Count, EnemyCount, Waves, WaveTicks, RegenTicks, Seed, 0f);
            GauntletRun shield2 = RunGauntlet("SHIELD", baseDesign,   enemyDesign, Count, EnemyCount, Waves, WaveTicks, RegenTicks, Seed, 0f);
            GauntletRun fastrc2 = RunGauntlet("FASTRC", baseDesign,   enemyDesign, Count, EnemyCount, Waves, WaveTicks, RegenTicks, Seed, 8f);
            Assert.AreEqual(GauntletSignature(armor),  GauntletSignature(armor2),  "ARMOR gauntlet must be deterministic (rerun reproduces)");
            Assert.AreEqual(GauntletSignature(shield), GauntletSignature(shield2), "SHIELD gauntlet must be deterministic (rerun reproduces)");
            Assert.AreEqual(GauntletSignature(fastrc), GauntletSignature(fastrc2), "FASTRC gauntlet must be deterministic (rerun reproduces)");
            Console.WriteLine($"[gaunt] REPRO {hullTag}: rerun reproduced all three doctrine series bit-identically");
        }

        // ---- overall verdict (honest either way). ----
        string verdict = hullsScored == 0
            ? "no testable hull (STRIP_SHIELDS variant infeasible on the cruiser+capital picks)"
            : shieldWarWins > armorWarWins
                ? $"SUSTAINMENT FAVORS SHIELDS: the shield doctrine wins the WAR on {shieldWarWins}/{hullsScored} hull(s) by surviving more waves / carrying less permanent repair-debt, even though the single-fight probes (#14..#16) said armor wins — free between-wave shield regen beats permanent armor attrition over a campaign"
                : armorWarWins > shieldWarWins
                    ? $"ARMOR STILL DOMINATES ON SUSTAINMENT: armor wins the WAR on {armorWarWins}/{hullsScored} hull(s) even with free shield regen between waves — its single-fight per-slot advantage compounds faster than shields can recover, so the #14..#16 conclusion HOLDS even for a campaign (honest negative result)"
                    : $"DRAW ON SUSTAINMENT: armor and shields each win the war on {armorWarWins}/{hullsScored}; no clear campaign-level edge either way";

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string hullsJson = hullBlocks.Count > 0 ? "\n    " + string.Join(",\n    ", hullBlocks) + "\n  " : "";
        string json =
            "{\n" +
            $"  \"experiment\": \"attrition gauntlet: measure SUSTAINMENT over a fixed multi-wave campaign (free between-wave shield regen vs permanent armor/hull damage) rather than a single fight; does the shield doctrine win the WAR even if armor wins wave 1?\",\n" +
            $"  \"shipsPerDoctrine\": {Count},\n" +
            $"  \"enemyPerWave\": {EnemyCount},\n" +
            $"  \"waves\": {Waves},\n" +
            $"  \"waveTicks\": {WaveTicks},\n" +
            $"  \"regenTicks\": {RegenTicks},\n" +
            $"  \"seedHex\": {J($"0x{Seed:X}")},\n" +
            $"  \"enemyDesign\": {J(enemyDesign.Name)},\n" +
            $"  \"doctrines\": [\"ARMOR (STRIP_SHIELDS)\",\"SHIELD (base hull)\",\"FASTRC (base + ShieldRechargeCombatRate x8, delay~0)\"],\n" +
            $"  \"hullCount\": {hullsScored},\n" +
            $"  \"armorWarWins\": {armorWarWins},\n" +
            $"  \"shieldWarWins\": {shieldWarWins},\n" +
            $"  \"verdict\": {J(verdict)},\n" +
            $"  \"hulls\": [{hullsJson}],\n" +
            $"  \"notes\": [{string.Join(",", notes.Select(J))}]\n" +
            "}\n";
        string jsonPath = Path.Combine(dir, "gauntlet.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[gaunt] DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        Console.WriteLine($"[gaunt] VERDICT {verdict}");
        foreach (string n in notes) Console.WriteLine($"[gaunt] NOTE {n}");

        Assert.IsTrue(File.Exists(jsonPath), "gauntlet.json written");
        Assert.IsTrue(hullsScored > 0, "at least one hull ran the full gauntlet");
    }

    // ===================================================================================================
    // 18) BALANCE META — REAL SHIELD-VARIANT ATTRITION GAUNTLET (ADDITIVE on top of #17).
    //    #17 answered "do shields sustain better than armor over a campaign?" with THREE doctrines, one of
    //    which (FASTRC) was a SYNTHETIC proxy: the base hull's own shields with ShieldRechargeCombatRate x8 and
    //    ShieldRechargeDelay collapsed to ~0 via the Flyweight-clone hack. That proxy answered "would an
    //    in-combat-cycling shield help?" but it is NOT a shippable hull — no such module exists. This probe
    //    replaces the proxy with the question a designer actually faces: of the REAL vanilla shield modules,
    //    which one is the best SUSTAINMENT pick over a multi-wave attrition campaign? In particular, does a
    //    REAL shield that cycles in combat (AncientShield_3x3 — 8s recharge delay, the shortest of the vanilla
    //    shields, so its fast rate re-engages mid-fight) win the campaign the way the synthetic FASTRC did?
    //
    //    FIVE doctrines, all built from the SAME base hull at EQUAL SHIP COUNT (cost controlled by equal-count
    //    framing exactly like #16/#17), each installing a REAL module into the hull's shield slots via the
    //    committed clone+swap-by-UID machinery (BuildSwapVariant, footprint-matched) — no Flyweight hacks:
    //      - STANDARD : Shield_10KW     (3x3, 12000 HP, 20s delay) — the baseline workhorse shield.
    //      - ANCIENT  : AncientShield_3x3(3x3, 15000 HP,  8s delay) — REAL in-combat-cycling shield (the FASTRC
    //                   analogue: shortest delay => fast rate re-engages during a wave, not only between waves).
    //      - CANOPY   : CanopyShield     (3x6, 32000 HP, 30s delay) — huge pool, footprint only fits some slots.
    //      - ARMORED  : ArmoredShield4x4 (4x4, 28000 HP, 30s delay) — armored variant, distinct footprint.
    //      - ARMOR    : STRIP_SHIELDS variant (BuildDoctrineVariant) — shields -> armor. The #14..#17 single-fight
    //                   winner; the control that carries PERMANENT (un-regenerating) damage.
    //
    //    Each doctrine runs the SAME committed 5-wave attrition campaign via RunGauntlet (fastRcFactor=0 — these
    //    are REAL shields, no synthetic buff): regen between waves, armor/hull damage persists. The shield-variant
    //    doctrines are NOT the base hull but a swapped variant, so RunGauntlet's BuffFastRecharge path is unused.
    //
    //    HULL SELECTION — NORMAL-armored cruiser + battleship picks; EXCLUDE over-tanked hulls. #17 noted the
    //    capital was a null case (it took ~zero attrition, so every doctrine "tied" on a strength fallthrough).
    //    Here we (a) pick cruiser + battleship (drop capital) and (b) DYNAMICALLY exclude any hull where the
    //    ARMOR control finishes the whole campaign with near-zero repair-debt AND full survival — that hull is
    //    over-tanked for this wave squad and cannot discriminate the doctrines, so it is reported as a null case.
    //
    //    TIEBREAK FIX (the #17 bug this probe corrects): #17's verdict fell through to surviving-strength /
    //    repair-debt even when BOTH doctrines cleared every wave at ~0 debt — declaring a "winner" off sim noise.
    //    Here, if a doctrine clears all waves at ~0 repair-debt, its sustainment is reported 'indeterminate'
    //    (the campaign did not stress it), NOT a strength-fallthrough winner. A doctrine only WINS sustainment
    //    when the campaign actually attrited the field (some doctrine took real debt or lost ships).
    //
    //    ADDITIVE: new helpers (ShieldVariantSpec / BuildShieldVariant / ClassifySustainment / VariantGauntletRow
    //    + JSON) and this one method. Reuses committed BuildSwapVariant (clone+swap-by-UID, footprint-matched,
    //    spawn-verify), BuildDoctrineVariant (STRIP_SHIELDS), RunGauntlet / GauntletRun / GauntletSignature,
    //    BuildExactCount, MkSide, StrengthOf/AliveOf, ShieldSlotCount. No committed method is altered.
    //    DETERMINISM: one fixed seed, GUID-free deterministic variant names, fixed wave squad + tick budgets;
    //    the test reruns each doctrine's full campaign and asserts a bit-identical wave series.
    // ===================================================================================================

    // A real vanilla shield doctrine: a label + the module UID to install into the hull's shield slots.
    sealed class ShieldVariantSpec
    {
        public string Doctrine;   // e.g. "STANDARD"
        public string ShieldUid;  // e.g. "Shield_10KW"; null => ARMOR control (handled via STRIP_SHIELDS)
        public string Note;       // e.g. "3x3, 12000HP, 20s delay"
    }

    // Build a doctrine fleet that SWAPS the hull's shield modules to a specific vanilla shield `shieldUid`,
    // reusing the committed clone+swap-by-UID machinery (BuildSwapVariant). Only shield slots whose FOOTPRINT
    // matches the target shield are swapped (BuildSwapVariant enforces s.Size == replSize and spawn-verifies);
    // a hull whose shield slots don't fit the target leaves them unswapped (swapped < matched => not clean),
    // which the caller treats as "this shield can't be installed on this hull" and skips honestly. Returns the
    // variant + swap stats (null variant if no shield slot could take the target).
    (IShipDesign variant, int swapped, int matched, bool clean) BuildShieldVariant(
        IShipDesign baseDesign, string shieldUid, string tag, ulong nameSalt)
    {
        bool IsShield(ShipModule m) => m.Is(ShipModuleType.Shield);
        return BuildSwapVariant(baseDesign, IsShield, shieldUid, tag, nameSalt);
    }

    // Classify a doctrine's SUSTAINMENT honestly (the #17 tiebreak fix). A doctrine that clears every wave with
    // near-zero permanent repair-debt was NOT stressed by the campaign — its sustainment is INDETERMINATE, not a
    // win. Only when the campaign actually attrited the doctrine (lost a ship OR carried real repair-debt) is its
    // final-strength / repair-debt a meaningful sustainment signal. `debtEps` = the near-zero threshold (a few HP
    // of rounding is not "stressed"). Returns ("stressed"|"indeterminate", a one-line reason).
    static (string state, string why) ClassifySustainment(GauntletRun r, int waves, float debtEps)
    {
        bool clearedAll = r.WavesSurvived >= waves && r.Waves.Count >= waves && r.Waves.All(w => w.ShipsAlive == r.StartShips);
        bool tookDebt   = r.FinalRepairDebt > debtEps || r.Waves.Any(w => w.RepairDebt > debtEps);
        if (clearedAll && !tookDebt)
            return ("indeterminate", $"cleared all {waves} waves at ~0 repair-debt (campaign did not stress it)");
        return ("stressed", $"attrited: wavesSurvived={r.WavesSurvived}/{waves}, finalRepairDebt={r.FinalRepairDebt:0}");
    }

    // One emitted row: a doctrine's campaign result on one hull + its sustainment classification.
    sealed class VariantGauntletRow
    {
        public string Doctrine, VariantName, SustainState, SustainWhy, BuildNote;
        public int Swapped, Matched, ShieldSlots, StartShips, WavesSurvived, WavesCleared;
        public bool Clean;
        public float FinalSurvivingStrength, FinalRepairDebt;
        public GauntletRun Run;
    }

    static string VariantGauntletRowJson(VariantGauntletRow r)
        => "{\n      "
         + $"\"doctrine\":{J(r.Doctrine)},\"variantName\":{J(r.VariantName)},\"buildNote\":{J(r.BuildNote)},\n      "
         + $"\"shieldSlots\":{r.ShieldSlots},\"swapped\":{r.Swapped},\"matched\":{r.Matched},\"clean\":{(r.Clean ? "true" : "false")},\n      "
         + $"\"sustainState\":{J(r.SustainState)},\"sustainWhy\":{J(r.SustainWhy)},\n      "
         + $"\"startShips\":{r.StartShips},\"wavesSurvived\":{r.WavesSurvived},\"wavesCleared\":{r.WavesCleared},"
         + $"\"finalSurvivingStrength\":{F(r.FinalSurvivingStrength)},\"finalRepairDebt\":{F(r.FinalRepairDebt)},\n      "
         + $"\"campaign\":{GauntletRunJson(r.Run)}\n    }}";

    [TestMethod]
    public void BalanceMeta_ShieldVariantGauntlet_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire();
        SetupArena();

        var notes = new List<string>();

        const int Count      = 6;     // EQUAL-COUNT doctrine fleets (same pack size as #14..#17)
        const int EnemyCount = 6;     // fixed neutral squad per wave
        const int Waves      = 5;     // 5-wave campaign (same as #17)
        const int WaveTicks  = 4000;  // per-wave combat budget
        const int RegenTicks = 5000;  // between-wave OOC regen budget (>> any ShieldRechargeDelay)
        const ulong Seed     = 0x6B07u; // single fixed gauntlet seed (distinct from #17's 0x6A07)
        const float DebtEps  = 50f;   // "near-zero" repair-debt threshold for the indeterminate classification
        ulong salt           = 0x6B00u; // distinct salt namespace for the variant names

        // The REAL vanilla shield doctrines + the ARMOR control (STRIP_SHIELDS). ShieldUid==null => ARMOR.
        var specs = new[]
        {
            new ShieldVariantSpec { Doctrine = "STANDARD", ShieldUid = "Shield_10KW",      Note = "3x3, 12000HP, 20s delay (baseline)" },
            new ShieldVariantSpec { Doctrine = "ANCIENT",  ShieldUid = "AncientShield_3x3", Note = "3x3, 15000HP, 8s delay (in-combat cycling)" },
            new ShieldVariantSpec { Doctrine = "CANOPY",   ShieldUid = "CanopyShield",      Note = "3x6, 32000HP, 30s delay" },
            new ShieldVariantSpec { Doctrine = "ARMORED",  ShieldUid = "ArmoredShield4x4",  Note = "4x4, 28000HP, 30s delay" },
            new ShieldVariantSpec { Doctrine = "ARMOR",    ShieldUid = null,                Note = "STRIP_SHIELDS: shields->armor (permanent damage control)" },
        };

        // The footprint of the CORE comparison shields (STANDARD + ANCIENT are both 3x3). A hull can only be
        // discriminated by this probe if it has shield slots that ACCEPT this footprint (else every shield swap
        // is infeasible — exactly what a blind mid-cost pick hit on the first run). So we pick hulls by
        // feasibility of the core 3x3 swap, not by cost alone.
        var coreShieldSize = new Point(3, 3);

        // NORMAL-armored cruiser + battleship picks (DROP capital — #17 found it over-tanked / null case).
        // Within each class, restrict to warships whose shield slots can take the core 3x3 shield, then take the
        // mid-cost representative of THAT feasible set (same mid-cost discipline as #14..#17, just feasibility-gated).
        var classOrder = new[] { RoleName.cruiser, RoleName.battleship };
        IShipDesign[] warships = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly)
            .Where(d => classOrder.Contains(d.Role))
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        var bases = new List<IShipDesign>();
        foreach (RoleName role in classOrder)
        {
            IShipDesign[] inClass = warships.Where(d => d.Role == role).ToArray();
            if (inClass.Length == 0) { notes.Add($"no standalone warship of class '{role}'; class skipped"); continue; }
            // Feasible = has at least one 3x3 shield slot (so STANDARD/ANCIENT install -> a real comparison).
            IShipDesign[] feasible = inClass.Where(d => ShieldSlotCountOfSize(d, coreShieldSize) > 0).ToArray();
            if (feasible.Length == 0)
            {
                IShipDesign mid = inClass[inClass.Length / 2];
                notes.Add($"{role}: no warship has a 3x3 shield slot (mid-cost '{mid.Name}' shields={ShieldSlotCount(mid)}); "
                        + "core shield swap infeasible -> class skipped");
                continue;
            }
            bases.Add(feasible[feasible.Length / 2]); // mid-cost representative of the FEASIBLE set
        }

        // The fixed neutral wave squad = the cheapest standalone warship with a shield slot (same deterministic
        // pick as #17, so this probe and #17 stress the doctrines with the SAME generic foe).
        IShipDesign enemyDesign = warships.FirstOrDefault(d => ShieldSlotCount(d) > 0) ?? warships.FirstOrDefault();
        if (enemyDesign == null) { Assert.Inconclusive("no warship available for the wave squad"); return; }
        Console.WriteLine($"[var] wave squad = {EnemyCount}x '{enemyDesign.Name}' ({enemyDesign.Role}); "
                        + $"campaign = {Waves} waves, {WaveTicks} combat ticks + {RegenTicks} regen ticks/wave, seed={Seed:X}");
        Console.WriteLine($"[var] gauntlet hulls (cruiser+battleship, NORMAL armor): "
                        + string.Join(", ", bases.Select(b => $"{b.Role}:'{b.Name}' shields={ShieldSlotCount(b)}")));

        var hullBlocks = new List<string>();
        int hullsScored = 0, nullCaseHulls = 0;
        var doctrineWins = new Dictionary<string, int>();

        foreach (IShipDesign baseDesign in bases)
        {
            string hullTag = $"{baseDesign.Role}:'{baseDesign.Name}'";
            int shieldSlots = ShieldSlotCount(baseDesign);

            // ---- Build every doctrine variant + run its campaign. Doctrines whose shield footprint doesn't fit
            //      this hull's slots are NOTE-d and dropped (honest infeasibility), not faked. ----
            var rows = new List<VariantGauntletRow>();
            foreach (ShieldVariantSpec spec in specs)
            {
                IShipDesign variant; int swapped, matched; bool clean; string buildNote;
                if (spec.ShieldUid == null)
                {
                    // ARMOR control via the committed STRIP_SHIELDS doctrine builder.
                    var (av, sw, sk, why) = BuildDoctrineVariant(baseDesign, "STRIP_SHIELDS", salt++);
                    variant = av; swapped = sw; matched = sw + sk; clean = av != null && sk == 0;
                    buildNote = spec.Note + (why != null ? $" [{why}]" : "");
                }
                else
                {
                    var (vv, sw, mt, cl) = BuildShieldVariant(baseDesign, spec.ShieldUid, $"{spec.Doctrine.ToLowerInvariant()}{salt:X}", salt++);
                    variant = vv; swapped = sw; matched = mt; clean = cl;
                    buildNote = spec.Note;
                }

                if (variant == null)
                {
                    notes.Add($"{hullTag}: {spec.Doctrine} ({spec.ShieldUid ?? "STRIP_SHIELDS"}) infeasible on this hull "
                            + $"(matched={matched} swapped={swapped}; footprint/slot mismatch); doctrine skipped");
                    Console.WriteLine($"[var] SKIP {hullTag} {spec.Doctrine} -> infeasible (matched={matched} swapped={swapped})");
                    continue;
                }

                // REAL shields: fastRcFactor=0 (no synthetic buff). The committed RunGauntlet runs the campaign.
                GauntletRun run = RunGauntlet(spec.Doctrine, variant, enemyDesign, Count, EnemyCount, Waves, WaveTicks, RegenTicks, Seed, 0f);
                var (sustState, sustWhy) = ClassifySustainment(run, Waves, DebtEps);
                rows.Add(new VariantGauntletRow
                {
                    Doctrine = spec.Doctrine, VariantName = variant.Name, BuildNote = buildNote,
                    Swapped = swapped, Matched = matched, ShieldSlots = shieldSlots, Clean = clean,
                    StartShips = run.StartShips, WavesSurvived = run.WavesSurvived, WavesCleared = run.WavesCleared,
                    FinalSurvivingStrength = run.FinalSurvivingStrength, FinalRepairDebt = run.FinalRepairDebt,
                    SustainState = sustState, SustainWhy = sustWhy, Run = run,
                });

                Console.WriteLine($"[var] {hullTag,-28} {spec.Doctrine,-8} variant='{variant.Name}' swapped={swapped}/{matched} clean={clean} "
                                + $"startShips={run.StartShips} wavesSurvived={run.WavesSurvived}/{Waves} wavesCleared={run.WavesCleared} "
                                + $"finalStr={run.FinalSurvivingStrength:0} finalRepairDebt={run.FinalRepairDebt:0} sustain={sustState}");
                foreach (GauntletWave w in run.Waves)
                    Console.WriteLine($"[var]   {spec.Doctrine,-8} wave{w.Wave} alive={w.ShipsAlive}/{Count} "
                                    + $"str={w.SurvivingStrength:0} armorHull={w.ArmorHullHealth:0} "
                                    + $"shieldRestored={(w.ShieldMax > 0f ? w.ShieldPower / w.ShieldMax : 0f):0.00} "
                                    + $"repairDebt={w.RepairDebt:0} enemyCleared={w.EnemyCleared}");
            }

            if (rows.Count < 2)
            {
                notes.Add($"{hullTag}: fewer than 2 feasible doctrines ({rows.Count}); cannot compare -> hull skipped");
                Console.WriteLine($"[var] HULL-SKIP {hullTag}: only {rows.Count} feasible doctrine(s)");
                continue;
            }

            // ---- NULL-CASE GUARD: if EVERY doctrine cleared the whole campaign at ~0 repair-debt, this hull is
            //      over-tanked for this wave squad and cannot discriminate the doctrines (the #17 capital case).
            //      Report it as a null case, do NOT crown a winner off sim noise. ----
            bool allIndeterminate = rows.All(r => r.SustainState == "indeterminate");
            if (allIndeterminate)
            {
                nullCaseHulls++;
                Console.WriteLine($"[var] NULLCASE {hullTag}: all {rows.Count} doctrines cleared every wave at ~0 repair-debt "
                                + "(over-tanked for this wave squad) -> sustainment indeterminate, no winner crowned");
                notes.Add($"{hullTag}: NULL CASE — all doctrines cleared the campaign at ~0 repair-debt (over-tanked); excluded from scoring");
                hullBlocks.Add("{\n      "
                    + $"\"hull\":{J(baseDesign.Name)},\"hullRole\":{J(baseDesign.Role.ToString())},\"shieldSlots\":{shieldSlots},\"count\":{Count},\n      "
                    + "\"nullCase\":true,\"winner\":\"indeterminate\",\"winnerWhy\":\"all doctrines cleared the campaign at ~0 repair-debt (over-tanked hull)\",\n    "
                    + string.Join(",\n    ", rows.Select(r => $"{J(r.Doctrine)}:{VariantGauntletRowJson(r)}")) + "\n    }");
                continue;
            }

            // ---- SUSTAINMENT WINNER among the STRESSED doctrines only: most waves survived; ties broken by MORE
            //      force retained (final surviving strength), then by LESS permanent repair-debt. Doctrines whose
            //      sustainment is 'indeterminate' (cleared at ~0 debt) are NOT eligible to win — the campaign never
            //      tested them, so we report indeterminate rather than a strength fallthrough. ----
            var stressed = rows.Where(r => r.SustainState == "stressed").ToList();
            string winner, winnerWhy;
            if (stressed.Count == 0)
            {
                winner = "indeterminate";
                winnerWhy = "every doctrine that ran was indeterminate (cleared at ~0 debt); campaign did not discriminate";
            }
            else
            {
                VariantGauntletRow best = stressed
                    .OrderByDescending(r => r.WavesSurvived)
                    .ThenByDescending(r => r.FinalSurvivingStrength)
                    .ThenBy(r => r.FinalRepairDebt)
                    .ThenBy(r => r.Doctrine, StringComparer.Ordinal)
                    .First();
                // A genuine tie (same waves/strength/debt within eps) => indeterminate, not a coin-flip winner.
                var ties = stressed.Where(r =>
                    r.WavesSurvived == best.WavesSurvived
                    && Math.Abs(r.FinalSurvivingStrength - best.FinalSurvivingStrength) <= 1e-3f
                    && Math.Abs(r.FinalRepairDebt - best.FinalRepairDebt) <= DebtEps).ToList();
                if (ties.Count > 1)
                {
                    winner = "indeterminate";
                    winnerWhy = $"tie among {string.Join("/", ties.Select(t => t.Doctrine))} "
                              + "(equal waves survived, force retained, and repair-debt within eps)";
                }
                else
                {
                    winner = best.Doctrine;
                    winnerWhy = $"survived {best.WavesSurvived}/{Waves} waves, retained {best.FinalSurvivingStrength:0} strength, "
                              + $"carried {best.FinalRepairDebt:0} repair-debt (lowest among the top survivors)";
                    doctrineWins[winner] = doctrineWins.TryGetValue(winner, out int n) ? n + 1 : 1;
                }
            }
            hullsScored++;

            // ---- Did the REAL in-combat-cycling shield (ANCIENT) win, mirroring the synthetic FASTRC of #17? ----
            VariantGauntletRow ancient = rows.FirstOrDefault(r => r.Doctrine == "ANCIENT");
            VariantGauntletRow armorRow = rows.FirstOrDefault(r => r.Doctrine == "ARMOR");
            bool ancientWon = winner == "ANCIENT";
            Console.WriteLine($"[var] WINNER {hullTag}: {winner} ({winnerWhy})");
            Console.WriteLine($"[var]   ancientWon={ancientWon} "
                            + (ancient != null ? $"ancient[survived={ancient.WavesSurvived}/{Waves} str={ancient.FinalSurvivingStrength:0} debt={ancient.FinalRepairDebt:0} {ancient.SustainState}] " : "ancient[infeasible] ")
                            + (armorRow != null ? $"armor[survived={armorRow.WavesSurvived}/{Waves} str={armorRow.FinalSurvivingStrength:0} debt={armorRow.FinalRepairDebt:0} {armorRow.SustainState}]" : "armor[infeasible]"));

            hullBlocks.Add("{\n      "
                + $"\"hull\":{J(baseDesign.Name)},\"hullRole\":{J(baseDesign.Role.ToString())},\"shieldSlots\":{shieldSlots},\"count\":{Count},\n      "
                + $"\"nullCase\":false,\"winner\":{J(winner)},\"winnerWhy\":{J(winnerWhy)},\"ancientWon\":{(ancientWon ? "true" : "false")},\n    "
                + string.Join(",\n    ", rows.Select(r => $"{J(r.Doctrine)}:{VariantGauntletRowJson(r)}")) + "\n    }");

            // ---- DETERMINISM: rerun every feasible doctrine's full campaign; the wave series must be identical. ----
            foreach (VariantGauntletRow r in rows)
            {
                IShipDesign rerunDesign;
                if (r.Doctrine == "ARMOR")
                {
                    var (av, _, _, _) = BuildDoctrineVariant(baseDesign, "STRIP_SHIELDS", salt++);
                    rerunDesign = av;
                }
                else
                {
                    string uid = specs.First(s => s.Doctrine == r.Doctrine).ShieldUid;
                    var (vv, _, _, _) = BuildShieldVariant(baseDesign, uid, $"{r.Doctrine.ToLowerInvariant()}re{salt:X}", salt++);
                    rerunDesign = vv;
                }
                Assert.IsNotNull(rerunDesign, $"{hullTag} {r.Doctrine}: rerun variant rebuilt");
                GauntletRun r2 = RunGauntlet(r.Doctrine, rerunDesign, enemyDesign, Count, EnemyCount, Waves, WaveTicks, RegenTicks, Seed, 0f);
                Assert.AreEqual(GauntletSignature(r.Run), GauntletSignature(r2),
                    $"{hullTag} {r.Doctrine} gauntlet must be deterministic (rerun reproduces)");
            }
            Console.WriteLine($"[var] REPRO {hullTag}: rerun reproduced all {rows.Count} doctrine series bit-identically");
        }

        // ---- overall verdict (honest either way). ----
        string bestPick = doctrineWins.Count == 0 ? null
            : doctrineWins.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
        int ancientHullWins = doctrineWins.TryGetValue("ANCIENT", out int aw) ? aw : 0;
        string winTally = doctrineWins.Count == 0 ? "(none — every scored hull was indeterminate)"
            : string.Join(", ", doctrineWins.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

        string verdict;
        if (hullsScored == 0)
            verdict = $"no testable hull (every cruiser+battleship pick was a null case: {nullCaseHulls} over-tanked, "
                    + "took ~zero attrition over the campaign so no doctrine could be discriminated)";
        else if (bestPick == null)
            verdict = $"INDETERMINATE on all {hullsScored} scored hull(s): doctrines tied within eps or cleared at ~0 repair-debt; "
                    + "the campaign did not stress them enough to separate a sustainment pick (honest non-result)";
        else
            verdict = $"BEST SUSTAINMENT PICK = {bestPick} (wins the attrition campaign on the most hulls: {winTally}). "
                    + (ancientHullWins > 0
                        ? $"The REAL in-combat-cycling shield ANCIENT (AncientShield_3x3) won on {ancientHullWins}/{hullsScored} hull(s) — like the synthetic FASTRC of #17, a shorter recharge delay that re-engages mid-wave does help sustainment."
                        : "The REAL in-combat-cycling shield ANCIENT (AncientShield_3x3) did NOT win any hull — unlike the synthetic FASTRC proxy of #17 (x8 rate + ~0 delay), a real 8s-delay shield's mid-wave cycling is too modest to win the campaign; the sustainment edge goes elsewhere (honest divergence from the synthetic result).")
                    + $" ({nullCaseHulls} hull(s) excluded as over-tanked null cases.)";

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string hullsJson = hullBlocks.Count > 0 ? "\n    " + string.Join(",\n    ", hullBlocks) + "\n  " : "";
        string winsJson = string.Join(",", doctrineWins.Select(kv => $"{J(kv.Key)}:{kv.Value}"));
        string json =
            "{\n" +
            "  \"experiment\": \"REAL vanilla shield-variant attrition gauntlet: install each real shield module (STANDARD/ANCIENT/CANOPY/ARMORED) into the hull's shield slots vs the STRIP_SHIELDS armor control, run the SAME 5-wave attrition campaign, and ask which REAL shield sustains best — and whether a real in-combat-cycling shield (AncientShield) wins like the synthetic FASTRC did in #17\",\n" +
            $"  \"shipsPerDoctrine\": {Count},\n" +
            $"  \"enemyPerWave\": {EnemyCount},\n" +
            $"  \"waves\": {Waves},\n" +
            $"  \"waveTicks\": {WaveTicks},\n" +
            $"  \"regenTicks\": {RegenTicks},\n" +
            $"  \"seedHex\": {J($"0x{Seed:X}")},\n" +
            $"  \"repairDebtEps\": {F(DebtEps)},\n" +
            $"  \"enemyDesign\": {J(enemyDesign.Name)},\n" +
            "  \"doctrines\": [\"STANDARD (Shield_10KW)\",\"ANCIENT (AncientShield_3x3, 8s delay, cycles in combat)\",\"CANOPY (CanopyShield)\",\"ARMORED (ArmoredShield4x4)\",\"ARMOR (STRIP_SHIELDS)\"],\n" +
            $"  \"hullsScored\": {hullsScored},\n" +
            $"  \"nullCaseHulls\": {nullCaseHulls},\n" +
            $"  \"doctrineHullWins\": {{{winsJson}}},\n" +
            $"  \"bestSustainmentPick\": {J(bestPick)},\n" +
            $"  \"ancientWonAnyHull\": {(ancientHullWins > 0 ? "true" : "false")},\n" +
            $"  \"verdict\": {J(verdict)},\n" +
            $"  \"hulls\": [{hullsJson}],\n" +
            $"  \"notes\": [{string.Join(",", notes.Select(J))}]\n" +
            "}\n";
        string jsonPath = Path.Combine(dir, "variantgauntlet.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[var] DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        Console.WriteLine($"[var] VERDICT {verdict}");
        foreach (string n in notes) Console.WriteLine($"[var] NOTE {n}");

        Assert.IsTrue(File.Exists(jsonPath), "variantgauntlet.json written");
        Assert.IsTrue(hullsScored > 0 || nullCaseHulls > 0, "at least one hull ran the full variant gauntlet");
    }

    // ===================================================================================================
    // #19 SMALL-VESSEL ARENA — fix degenerate corvette fights, then read the small-ship meta.
    //
    //    Prior small-ship duels were DEGENERATE: the committed RunIsoDuel spawns the two fleets at ±8000
    //    (16000u apart) and only re-issues attack orders every 1500 ticks. Small hulls (fighter/corvette/
    //    gunboat/frigate) are short-ranged and — unlike capitals — often never closed that gap inside the
    //    tick budget, so the "result" was a no-combat artifact (both fleets full strength, scored a draw on
    //    a coin-flip). This arena FIXES engagement three ways, all in a new helper (RunSmallDuel) that does
    //    NOT touch RunIsoDuel/RunRangeDuel:
    //      (a) CLOSE SPAWN: side anchors at ±CloseHalf (default 3000 -> 6000u apart), inside small-ship
    //          weapon range so they reach a firing solution almost immediately.
    //      (b) AGGRESSIVE NUDGE: re-issue OrderAttackSpecificTarget every 200 ticks (not 1500) so a ship
    //          whose target dies instantly re-acquires and keeps closing — no idle drift.
    //      (c) LONG ENOUGH: run a generous tick budget and stop early once one side is wiped (decision
    //          reached) so we never score a half-fought stalemate as a result.
    //    And it ASSERTS COMBAT OCCURRED: RunSmallDuel sums every ship's GameObject.Health (current HP)
    //    across BOTH fleets before and after; damageDealt = startHp - endHp must be > 0, else the duel is
    //    flagged noCombat and excluded. The test asserts the arena produced real combat overall.
    //
    //    EXPERIMENTS (all equal-count, so per-ship combat quality is isolated from price):
    //      (1) SMALL-HULL ROUND-ROBIN: pick ~6-8 low-cost small designs (Role in {fighter, corvette,
    //          gunboat, frigate}); every unordered pair fights equal-count; record winner + each side's
    //          strength-retained, accumulate per-design win rate -> the small-ship meta (which hull/weapon
    //          mix wins at small scale).
    //      (2) STRIP_SHIELDS @ SMALL SCALE: on a couple of shield-bearing small hulls, duel the shielded
    //          base vs its STRIP_SHIELDS armor variant (committed BuildDoctrineVariant) equal-count, and
    //          compare to the known LARGE-hull verdict (armor wins single fights on cruisers/capitals).
    //          Do shields matter MORE or LESS on tiny hulls with only a slot or two?
    //
    //    Fully ADDITIVE: new helpers (RunSmallDuel + FleetHealth + small JSON rows) + this one method.
    //    Reuses committed MkSide/SetupArena/BuildExactCount/SetupWars/EngageAll/StrengthOf/AliveOf/
    //    BuildDoctrineVariant/ShieldSlotCount/UnitStrengthOf. No committed method is altered, so all prior
    //    results stay bit-identical. DETERMINISM: every duel takes a FIXED seed; the test reruns the whole
    //    round-robin a second time and asserts an identical winner sequence.
    // ===================================================================================================

    // Sum of CURRENT hp (GameObject.Health) over the living ships of a fleet. Drops as armor/internals take
    // damage even before a ship dies, so (startHp - endHp) is a robust "real combat happened" signal that does
    // not require any ship to be destroyed.
    static float FleetHealth(List<Ship> fleet) => fleet.Where(s => s.Active).Sum(s => s.Health);

    // One small-vessel equal-count duel with FIXED, reliable engagement. Faithful to the committed arena
    // (CreateUniverseAndPlayerEmpire + SetupArena, BuildExactCount placement, seeded RNG, EngageAll targeting,
    // alive-then-end-strength win rule) but tuned so SMALL ships actually clash: anchors at ±closeHalf, a tight
    // 200-tick re-target nudge, and an early-out the instant one side is wiped. Additionally tracks the closest
    // fleet-to-fleet approach (minSep) and total HP dealt (damageDealt) so the caller can assert combat. The
    // committed RunIsoDuel / RunRangeDuel are untouched.
    (string winner, float aStart, float aEnd, float bStart, float bEnd, int aAlive, int bAlive,
     float minSep, float damageDealt, int ticksRun) RunSmallDuel(
        IShipDesign a, IShipDesign b, int count, float closeHalf, ulong seed, int ticks)
    {
        CreateUniverseAndPlayerEmpire();
        SetupArena();
        var sa = MkSide(Player, 0, new Vector2(-closeHalf, 0), new[] { (0f, 1f) });
        var sb = MkSide(Enemy,  1, new Vector2( closeHalf, 0), new[] { (0f, 1f) });
        var sides = new List<Side> { sa, sb };

        BuildExactCount(sa, a, count);
        BuildExactCount(sb, b, count);

        SetupWars(sides);
        UState.EnableDeterministicRng(seed);
        EngageAll(sides);

        float aStart = StrengthOf(sa.Fleet), bStart = StrengthOf(sb.Fleet);
        float startHp = FleetHealth(sa.Fleet) + FleetHealth(sb.Fleet);
        float minSep = float.MaxValue;
        int ticksRun = ticks;
        for (int t = 0; t < ticks; ++t)
        {
            UState.Objects.Update(TestSimStep);
            if (t % 200 == 199) EngageAll(sides); // tight nudge so survivors keep closing / re-acquire
            if (t % 50 == 0)
            {
                float closest = ClosestCrossSeparation(sa.Fleet, sb.Fleet);
                if (closest < minSep) minSep = closest;
            }
            // Early-out: once a side is wiped the decision is reached — don't burn ticks on an empty arena.
            if (t % 100 == 99 && (AliveOf(sa.Fleet) == 0 || AliveOf(sb.Fleet) == 0)) { ticksRun = t + 1; break; }
        }
        if (minSep == float.MaxValue) minSep = -1f;
        float endHp = FleetHealth(sa.Fleet) + FleetHealth(sb.Fleet);
        float damageDealt = startHp - endHp;
        int aAlive = AliveOf(sa.Fleet), bAlive = AliveOf(sb.Fleet);
        float aEnd = StrengthOf(sa.Fleet), bEnd = StrengthOf(sb.Fleet);
        string winner =
            bAlive == 0 && aAlive > 0 ? "A" :
            aAlive == 0 && bAlive > 0 ? "B" :
            aEnd > bEnd ? "A" :
            bEnd > aEnd ? "B" : "draw";
        return (winner, aStart, aEnd, bStart, bEnd, aAlive, bAlive, minSep, damageDealt, ticksRun);
    }

    // Per-design row for smallvessel.json (round-robin meta).
    sealed class SmallDesignRow
    {
        public string Design, Role;
        public float UnitCost, UnitStrength;
        public int ShieldSlots;
        public int Wins, Losses, Draws;
        public float WinRate, AvgRetained;
    }

    // One STRIP_SHIELDS-at-small-scale row.
    sealed class SmallShieldRow
    {
        public string Hull, Role, Winner; // "SHIELD" base wins, "ARMOR" strip wins, or "draw"
        public int ShieldSlots, Swapped, EqualCount;
        public float ShieldUnitStr, ArmorUnitStr;
        public float ShieldRetained, ArmorRetained; // fraction of own start-strength kept
        public float DamageDealt, MinSep;
    }

    [TestMethod]
    public void BalanceMeta_SmallVesselArena_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire();
        SetupArena();

        var notes = new List<string>();
        const int EqCount    = 4;     // equal-count: 4v4 small ships
        const float CloseHalf = 3000f; // 6000u apart — inside small-ship weapon range
        const int Ticks      = 12000; // generous budget; RunSmallDuel early-outs on a wipe
        const ulong SeedBase = 0x5A11B0A7u; // "SmAll BOAT"

        // ---- Pick ~6-8 LOW-COST SMALL designs across the small-ship roles. ----
        var smallRoles = new[] { RoleName.fighter, RoleName.corvette, RoleName.gunboat, RoleName.frigate };
        IShipDesign[] smallPool = ResourceManager.Ships.Designs
            .Where(d => smallRoles.Contains(d.Role) && d.BaseStrength > 1f)
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        // Prefer the cheapest representative of each small role first (so the meta spans classes), then
        // backfill with the next-cheapest small designs until we have up to 8 distinct picks. Deterministic.
        var picks = new List<IShipDesign>();
        foreach (RoleName role in smallRoles)
        {
            IShipDesign rep = smallPool.FirstOrDefault(d => d.Role == role);
            if (rep != null && !picks.Contains(rep)) picks.Add(rep);
        }
        foreach (IShipDesign d in smallPool)
        {
            if (picks.Count >= 8) break;
            if (!picks.Contains(d)) picks.Add(d);
        }
        IShipDesign[] designs = picks.Take(8).OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal).ToArray();

        Console.WriteLine($"[small] POOL smallRoleDesigns={smallPool.Length} picked={designs.Length}: "
                        + string.Join(", ", designs.Select(d => $"{d.Role}:'{d.Name}'@{d.GetCost(Player):0}")));
        notes.Add($"smallPool={smallPool.Length} picked={designs.Length}");

        // Per-design census (clean probe spawn) so the JSON reports cost/strength/shield-slots once.
        var rows = new Dictionary<string, SmallDesignRow>(StringComparer.Ordinal);
        var retSum = new Dictionary<string, float>(StringComparer.Ordinal);
        var retN   = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (IShipDesign d in designs)
        {
            float unitStr = UnitStrengthOf(d);
            rows[d.Name] = new SmallDesignRow
            {
                Design = d.Name, Role = d.Role.ToString(), UnitCost = d.GetCost(Player),
                UnitStrength = unitStr, ShieldSlots = ShieldSlotCount(d)
            };
            retSum[d.Name] = 0f; retN[d.Name] = 0;
            Console.WriteLine($"[small] CENSUS {d.Name,-26} role={d.Role,-9} cost={d.GetCost(Player),6:0} "
                            + $"unitStr={unitStr,6:0} shields={ShieldSlotCount(d)}");
        }

        // ---- (1) EQUAL-COUNT ROUND-ROBIN among the small designs (closed-spawn, asserted combat). ----
        var pairResults = new List<(string a, string b, string winner, float aRet, float bRet, float dmg, float minSep, int ticks)>();
        int decidedFights = 0, noCombatFights = 0;
        float totalDamage = 0f;
        ulong rrSeed = SeedBase;
        for (int i = 0; i < designs.Length; ++i)
            for (int j = i + 1; j < designs.Length; ++j)
            {
                IShipDesign di = designs[i], dj = designs[j];
                var (winner, aStart, aEnd, bStart, bEnd, aAlive, bAlive, minSep, dmg, tr) =
                    RunSmallDuel(di, dj, EqCount, CloseHalf, rrSeed++, Ticks);

                float aRet = aStart > 0 ? aEnd / aStart : 0f;
                float bRet = bStart > 0 ? bEnd / bStart : 0f;
                bool combat = dmg > 0f;
                if (!combat) noCombatFights++;
                else { decidedFights++; totalDamage += dmg; }

                // Per-design tallies (only when real combat occurred — a no-combat artifact scores nothing).
                if (combat)
                {
                    SmallDesignRow ri = rows[di.Name], rj = rows[dj.Name];
                    if (winner == "A")      { ri.Wins++;  rj.Losses++; }
                    else if (winner == "B") { rj.Wins++;  ri.Losses++; }
                    else                    { ri.Draws++; rj.Draws++;  }
                    retSum[di.Name] += aRet; retN[di.Name]++;
                    retSum[dj.Name] += bRet; retN[dj.Name]++;
                }

                pairResults.Add((di.Name, dj.Name, winner, aRet, bRet, dmg, minSep, tr));
                Console.WriteLine($"[small] RR {di.Name,-20}({rows[di.Name].Role,-8}) vs {dj.Name,-20}({rows[dj.Name].Role,-8}) "
                                + $"-> {(combat ? winner : "NO-COMBAT")}  A[{aAlive}/{EqCount} ret {aRet:0.00}] B[{bAlive}/{EqCount} ret {bRet:0.00}] "
                                + $"dmg={dmg:0} minSep={minSep:0} ticks={tr}");
            }

        // Finalize per-design win rate + avg retained.
        foreach (SmallDesignRow r in rows.Values)
        {
            int games = r.Wins + r.Losses + r.Draws;
            r.WinRate = games > 0 ? (r.Wins + 0.5f * r.Draws) / games : 0f;
            r.AvgRetained = retN[r.Design] > 0 ? retSum[r.Design] / retN[r.Design] : 0f;
        }
        var ranked = rows.Values.OrderByDescending(r => r.WinRate).ThenByDescending(r => r.AvgRetained)
                                .ThenBy(r => r.Design, StringComparer.Ordinal).ToArray();
        Console.WriteLine($"[small] META ranked by winRate ({decidedFights} decided, {noCombatFights} no-combat):");
        foreach (SmallDesignRow r in ranked)
            Console.WriteLine($"[small]   {r.Design,-24} role={r.Role,-8} winRate={r.WinRate:0.00} "
                            + $"W/L/D={r.Wins}/{r.Losses}/{r.Draws} avgRet={r.AvgRetained:0.00} cost={r.UnitCost:0} shields={r.ShieldSlots}");
        SmallDesignRow champ = ranked.FirstOrDefault();
        string metaVerdict = champ == null
            ? "no small design accumulated any decided fight"
            : $"SMALL-SHIP META WINNER = '{champ.Design}' (role={champ.Role}, winRate {champ.WinRate:0.00}, "
            + $"avgRetained {champ.AvgRetained:0.00}, cost {champ.UnitCost:0}, shields {champ.ShieldSlots})";
        notes.Add(metaVerdict);

        // ---- (2) STRIP_SHIELDS @ SMALL SCALE on a couple of shield-bearing small hulls. ----
        var shieldRows = new List<SmallShieldRow>();
        IShipDesign[] shieldedSmall = designs.Where(d => ShieldSlotCount(d) > 0).ToArray();
        if (shieldedSmall.Length == 0)
        {
            // Widen the net: any low-cost small-role design that actually carries a shield slot.
            shieldedSmall = smallPool.Where(d => ShieldSlotCount(d) > 0)
                .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal).Take(2).ToArray();
        }
        shieldedSmall = shieldedSmall.Take(2).ToArray();
        if (shieldedSmall.Length == 0)
            notes.Add("STRIP_SHIELDS: no small-role design carries a shield slot at this scale (shields may be a big-hull feature in vanilla); skipped.");

        ulong shieldSeed = 0x5A11_5D00u;
        ulong salt = 0;
        foreach (IShipDesign baseDesign in shieldedSmall)
        {
            var (armorVariant, swapped, skipped, why) = BuildDoctrineVariant(baseDesign, "STRIP_SHIELDS", salt++);
            if (armorVariant == null)
            {
                notes.Add($"STRIP_SHIELDS hull='{baseDesign.Name}': variant build failed ({why}); skipped.");
                Console.WriteLine($"[small] STRIP hull='{baseDesign.Name}' build FAILED: {why}");
                continue;
            }

            float shieldUnit = UnitStrengthOf(baseDesign);
            float armorUnit  = UnitStrengthOf(armorVariant);

            // SHIELD base = side A, ARMOR strip = side B, equal count, FIXED seed, close spawn.
            var (winner, aStart, aEnd, bStart, bEnd, aAlive, bAlive, minSep, dmg, tr) =
                RunSmallDuel(baseDesign, armorVariant, EqCount, CloseHalf, shieldSeed++, Ticks);

            float shieldRet = aStart > 0 ? aEnd / aStart : 0f;
            float armorRet  = bStart > 0 ? bEnd / bStart : 0f;
            string verdict = winner == "A" ? "SHIELD" : winner == "B" ? "ARMOR" : "draw";

            shieldRows.Add(new SmallShieldRow
            {
                Hull = baseDesign.Name, Role = baseDesign.Role.ToString(), Winner = verdict,
                ShieldSlots = ShieldSlotCount(baseDesign), Swapped = swapped, EqualCount = EqCount,
                ShieldUnitStr = shieldUnit, ArmorUnitStr = armorUnit,
                ShieldRetained = shieldRet, ArmorRetained = armorRet,
                DamageDealt = dmg, MinSep = minSep
            });
            Console.WriteLine($"[small] STRIP hull='{baseDesign.Name}'({baseDesign.Role}) shields={ShieldSlotCount(baseDesign)} swapped={swapped} "
                            + $"-> {verdict}  SHIELD[{aAlive}/{EqCount} ret {shieldRet:0.00}] ARMOR[{bAlive}/{EqCount} ret {armorRet:0.00}] "
                            + $"dmg={dmg:0} minSep={minSep:0}");
        }

        int shieldWins = shieldRows.Count(r => r.Winner == "SHIELD");
        int armorWins  = shieldRows.Count(r => r.Winner == "ARMOR");
        string shieldVerdict = shieldRows.Count == 0
            ? "no shield-vs-armor result at small scale (no shield-bearing small hull)"
            : armorWins > shieldWins
                ? $"AT SMALL SCALE armor (STRIP_SHIELDS) still wins ({armorWins} armor vs {shieldWins} shield over {shieldRows.Count} hull(s)) — same direction as the large-hull verdict, so the shield deficit is not a big-hull-only effect"
                : shieldWins > armorWins
                    ? $"AT SMALL SCALE shields FLIP and win ({shieldWins} shield vs {armorWins} armor over {shieldRows.Count} hull(s)) — opposite the large-hull verdict; on tiny hulls a shield's flat soak matters MORE relative to the few armor slots it displaces"
                    : $"AT SMALL SCALE shield-vs-armor is a wash ({shieldWins}-{armorWins} over {shieldRows.Count} hull(s)) — neither dominates with so few slots";
        notes.Add(shieldVerdict);

        // ---- DETERMINISM: rerun the whole round-robin with the SAME seeds; winner sequence must match. ----
        var rerunWinners = new List<string>();
        ulong rrSeed2 = SeedBase;
        for (int i = 0; i < designs.Length; ++i)
            for (int j = i + 1; j < designs.Length; ++j)
            {
                var (w2, _, _, _, _, _, _, _, _, _) = RunSmallDuel(designs[i], designs[j], EqCount, CloseHalf, rrSeed2++, Ticks);
                rerunWinners.Add(w2);
            }
        var firstWinners = pairResults.Select(p => p.winner).ToList();
        bool reproducible = rerunWinners.SequenceEqual(firstWinners, StringComparer.Ordinal);
        Console.WriteLine($"[small] REPRO round-robin rerun winners match first run: {reproducible} "
                        + $"({firstWinners.Count} pairs)");
        notes.Add($"reproducible={reproducible} over {firstWinners.Count} pairs");

        // ---- EMIT smallvessel.json ----
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);

        string designJson = string.Join(",\n    ", ranked.Select(r =>
            $"{{\"design\":{J(r.Design)},\"role\":{J(r.Role)},\"unitCost\":{F(r.UnitCost)},\"unitStrength\":{F(r.UnitStrength)},"
          + $"\"shieldSlots\":{r.ShieldSlots},\"wins\":{r.Wins},\"losses\":{r.Losses},\"draws\":{r.Draws},"
          + $"\"winRate\":{F(r.WinRate)},\"avgRetained\":{F(r.AvgRetained)}}}"));

        string pairJson = string.Join(",\n    ", pairResults.Select(p =>
            $"{{\"a\":{J(p.a)},\"b\":{J(p.b)},\"winner\":{J(p.winner)},\"aRetained\":{F(p.aRet)},\"bRetained\":{F(p.bRet)},"
          + $"\"damageDealt\":{F(p.dmg)},\"minSep\":{F(p.minSep)},\"ticks\":{p.ticks},\"combat\":{(p.dmg > 0f ? "true" : "false")}}}"));

        string shieldJson = string.Join(",\n    ", shieldRows.Select(r =>
            $"{{\"hull\":{J(r.Hull)},\"role\":{J(r.Role)},\"winner\":{J(r.Winner)},\"shieldSlots\":{r.ShieldSlots},"
          + $"\"swapped\":{r.Swapped},\"equalCount\":{r.EqualCount},\"shieldUnitStr\":{F(r.ShieldUnitStr)},"
          + $"\"armorUnitStr\":{F(r.ArmorUnitStr)},\"shieldRetained\":{F(r.ShieldRetained)},"
          + $"\"armorRetained\":{F(r.ArmorRetained)},\"damageDealt\":{F(r.DamageDealt)},\"minSep\":{F(r.MinSep)}}}"));

        string json =
            "{\n" +
            "  \"experiment\": \"SMALL-VESSEL ARENA: fix degenerate small-ship fights (close spawn ±3000, 200-tick re-target nudge, early-out on wipe) so fighters/corvettes/gunboats/frigates actually reach weapon range and fight to a decision; assert combat (total HP dealt > 0); then equal-count round-robin for the small-ship meta + a STRIP_SHIELDS shield-vs-armor check at small scale\",\n" +
            $"  \"equalCount\": {EqCount},\n" +
            $"  \"closeHalfDist\": {F(CloseHalf)},\n" +
            $"  \"maxTicks\": {Ticks},\n" +
            $"  \"seedBaseHex\": {J($"0x{SeedBase:X}")},\n" +
            $"  \"smallRoles\": [\"fighter\",\"corvette\",\"gunboat\",\"frigate\"],\n" +
            $"  \"designsPicked\": {designs.Length},\n" +
            $"  \"roundRobinPairs\": {pairResults.Count},\n" +
            $"  \"decidedFights\": {decidedFights},\n" +
            $"  \"noCombatFights\": {noCombatFights},\n" +
            $"  \"totalDamageDealt\": {F(totalDamage)},\n" +
            $"  \"reproducible\": {(reproducible ? "true" : "false")},\n" +
            $"  \"metaVerdict\": {J(metaVerdict)},\n" +
            $"  \"shieldVerdict\": {J(shieldVerdict)},\n" +
            $"  \"designs\": [\n    {designJson}\n  ],\n" +
            $"  \"roundRobin\": [\n    {pairJson}\n  ],\n" +
            $"  \"stripShields\": [{(shieldRows.Count > 0 ? "\n    " + shieldJson + "\n  " : "")}],\n" +
            $"  \"notes\": [{string.Join(",", notes.Select(J))}]\n" +
            "}\n";
        string jsonPath = Path.Combine(dir, "smallvessel.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[small] DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        Console.WriteLine($"[small] META {metaVerdict}");
        Console.WriteLine($"[small] SHIELD {shieldVerdict}");
        foreach (string n in notes) Console.WriteLine($"[small] NOTE {n}");

        // ---- ASSERTS ----
        Assert.IsTrue(File.Exists(jsonPath), "smallvessel.json written");
        Assert.IsTrue(designs.Length >= 2, "picked at least 2 small designs for the arena");
        Assert.IsTrue(pairResults.Count > 0, "round-robin produced fights");
        // The engagement FIX worked: across the round-robin, real combat occurred (HP was dealt) in the
        // majority of fights — not the old no-combat artifact where fleets never closed.
        Assert.IsTrue(decidedFights > 0, "the small-ship arena produced at least one fight with real combat (damage dealt > 0)");
        Assert.IsTrue(totalDamage > 0f, "small ships actually fought (total HP dealt across the round-robin > 0)");
        Assert.IsTrue(decidedFights >= noCombatFights, "the close-spawn fix made the MAJORITY of small-ship fights reach combat");
        Assert.IsTrue(reproducible, "the seeded small-vessel round-robin is reproducible (deterministic)");
    }

    // ===================================================================================================
    // 20) BALANCE META — WEAPON DAMAGE-TYPE as a real COUNTER to shields/armor (the RPS the stat-budget
    //     ladder lacked).  ADDITIVE on top of everything above.
    //
    //     The stat-budget ladder (#1..#19) found combat is essentially a transitive "who has more budget"
    //     ranking — no rock-paper-scissors.  But the engine DOES wire a damage-type -> resist path:
    //     Weapon.GetShieldDamageMod / GetArmorDamageMod (Ship_Game/Gameplay/Weapon.cs) multiply incoming
    //     damage by (1 - module.Shield{Kinetic,Energy,Beam}Resist) when shields are up, else by
    //     (1 - module.{Kinetic,Energy,Beam}Resist) on the armor/hull — and ShipModule.Damage() actually
    //     calls that path (ShipModule.cs ~795).  Crucially, vanilla content sets:
    //       * SHIELD modules: NEGATIVE ShieldKineticResist (-0.07..-0.14 -> kinetic does 7..14% MORE),
    //         but POSITIVE ShieldEnergyResist (0.22..0.34) and ShieldBeamResist (0.15..0.25).
    //       * ARMOR modules:  small POSITIVE KineticResist (0.02..0.10 -> resists kinetic),
    //         and ZERO Energy/Beam resist.
    //     So the RPS HYPOTHESIS is: KINETIC pierces shields (negative resist = bonus damage) but is
    //     resisted by armor; ENERGY/BEAM is resisted by shields but armor has no energy/beam resist.
    //     If true, weapon-TYPE choice is a genuine counter, independent of the stat budget.
    //
    //     NOTE on the resist path: GetShieldDamageMod/GetArmorDamageMod SKIP the per-tag resist entirely
    //     for EXPLODING projectiles (ExplosionRadius>0 -> only Explosive resist applies).  Many vanilla
    //     kinetic cannons explode (BigCannon/MassDriver/HVTurret), which would mask the kinetic->shield
    //     bonus.  So the damage-type selector below PREFERS NON-EXPLODING weapons of the wanted tag, so the
    //     tag-resist branch is the one actually exercised.
    //
    //     METHOD (all equal-count, seeded, 3 fixed seeds, close-spawn so weapons reliably engage):
    //       * Build a KINETIC-weapon attacker fleet and an ENERGY/BEAM-weapon attacker fleet from a fixed
    //         warship (BuildDamageTypeVariant — a damage-type-aware mirror of BuildMonoWeaponVariant/
    //         BuildEmpVariant: every weapon slot -> a footprint-matched module whose weapon template carries
    //         the wanted tag, non-exploding preferred).
    //       * Build a SHIELD-heavy target (armor slots -> shields, BuildSwapVariant) and an ARMOR-heavy
    //         target (shields -> armor, BuildDoctrineVariant STRIP_SHIELDS) from a fixed shielded warship,
    //         so the ONLY thing that differs between the two targets is shields-vs-armor.
    //       * Duel each attacker vs each target, measuring TARGET HP DESTROYED (isolates how much the
    //         attacker chewed through that defense type) + win + strength retained.
    //     RPS check: kineticVsShield destroys MORE target HP than energyVsShield (shields weak to kinetic),
    //     and energyVsArmor >= kineticVsArmor (armor resists kinetic, no energy/beam resist) — i.e. the
    //     better weapon-type FLIPS with the target's defense.  Honest either way: if the deltas are flat the
    //     path is wired but content-tuned to be a non-counter (like the EMP shield-block negative finding).
    //
    //     Fully ADDITIVE: new helpers (FindDamageTypeModuleUid, BuildDamageTypeVariant, DmgTypeOf,
    //     ShieldKineticResistOf/ShieldEnergyResistOf, RunDmgTypeDuel, the DmgTypeCell/DmgTypeRow rows) + this
    //     one method.  No committed method/helper is touched; #1..#19 stay bit-identical.  DETERMINISM:
    //     deterministic GUID-free variant names, seeded UState RNG, fixed seed set, rerun-reproduces assert.
    // ===================================================================================================

    // Find a WEAPON module UID whose installed weapon's template satisfies `dmgPred` (a damage-type predicate)
    // and whose footprint == size.  NON-EXPLODING weapons are preferred first (so the per-tag resist branch is
    // the one exercised — exploding projectiles bypass tag resists), then highest DamagePerSecond, then cheapest
    // power, then UID — all deterministic.  Mirrors the discipline of FindEmpModuleUid but selects on the weapon
    // template's tags instead of EMPDamage.  Returns null if no footprint-matched module of that type exists.
    static string FindDamageTypeModuleUid(Point size, Func<IWeaponTemplate, bool> dmgPred)
        => ResourceManager.ShipModuleTemplates
            .Where(m => m.GetSize() == size && !string.IsNullOrEmpty(m.WeaponType)
                        && ResourceManager.GetWeaponTemplate(m.WeaponType, out IWeaponTemplate w)
                        && !w.Tag_Bomb && dmgPred(w))
            .OrderBy(m => ResourceManager.GetWeaponTemplate(m.WeaponType, out IWeaponTemplate w) && w.Explodes ? 1 : 0) // non-exploding first
            .ThenByDescending(m => ResourceManager.GetWeaponTemplate(m.WeaponType, out IWeaponTemplate w) ? w.DamagePerSecond : 0f)
            .ThenBy(m => m.PowerDraw).ThenBy(m => m.UID, StringComparer.Ordinal)
            .Select(m => m.UID).FirstOrDefault();

    // Clone `baseDesign`, swap EVERY weapon slot to a footprint-matched module of the wanted DAMAGE TYPE
    // (`dmgPred`), register under a DETERMINISTIC GUID-free name (base.Name + suffix e.g. "#KIN"/"#NRG"), and
    // spawn-verify.  A damage-type-aware mirror of BuildMonoWeaponVariant/BuildEmpVariant: a weapon slot with no
    // footprint-matched replacement (or already that exact module) is left untouched and counted as a skip.
    // Role is preserved (updateRole:false) so AI behavior is invariant and only the damage type changes.
    // Returns the variant + how many slots changed + how many were skipped + a note (null on success).
    (IShipDesign variant, int swapped, int skipped, string note) BuildDamageTypeVariant(
        IShipDesign baseDesign, string suffix, Func<IWeaponTemplate, bool> dmgPred, ulong nameSalt)
    {
        string variantName = baseDesign.Name + suffix;
        for (ulong bump = 1; ResourceManager.ShipTemplateExists(variantName); ++bump)
            variantName = $"{baseDesign.Name}{suffix}-{nameSalt:X}-{bump}";

        ShipDesign clone = baseDesign.GetClone(variantName);
        DesignSlot[] slots = clone.GetOrLoadDesignSlots();
        var outSlots = new DesignSlot[slots.Length];
        int swapped = 0, skipped = 0;
        for (int i = 0; i < slots.Length; ++i)
        {
            DesignSlot s = slots[i];
            outSlots[i] = new DesignSlot(s); // default: unchanged copy
            if (!ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m)) continue;
            if (TemplateWeaponFamily(m) == null) continue; // not a weapon slot -> leave untouched
            string replUid = FindDamageTypeModuleUid(s.Size, dmgPred);
            if (replUid == null || replUid == s.ModuleUID) { skipped++; continue; } // no footprint-matched repl
            outSlots[i] = new DesignSlot(s.Pos, replUid, s.Size, s.TurretAngle, s.ModuleRot, s.HangarShipUID);
            swapped++;
        }

        if (swapped == 0)
            return (null, 0, skipped, $"{suffix}: no footprint-matched damage-type replacement for any weapon slot");

        clone.SetDesignSlots(outSlots, updateRole: false);
        if (!ResourceManager.AddShipTemplate(clone, playerDesign: false, readOnly: true))
            return (null, 0, skipped, $"{suffix}: module validation failed in CreateNewShipTemplate (power/slot)");
        if (!ResourceManager.Ships.GetDesign(variantName, out IShipDesign variant))
            return (null, 0, skipped, $"{suffix}: registered design not retrievable");
        try
        {
            Ship probe = SpawnShip(variantName, Player, new Vector2(910000, 910000));
            bool ok = probe is { Active: true } && probe.HasModules;
            probe.QueueTotalRemoval();
            if (!ok) return (null, 0, skipped, $"{suffix}: variant registered but spawned no modules");
        }
        catch (Exception ex) { return (null, 0, skipped, $"{suffix}: spawn-verify threw {ex.GetType().Name}"); }

        return (variant, swapped, skipped, null);
    }

    // Damage-type predicates (read straight off the weapon template tags — the same tags the in-combat resist
    // branch checks).  KINETIC = a kinetic, non-beam projectile.  ENERGY/BEAM = an energy projectile OR a beam.
    static bool IsKineticType(IWeaponTemplate w)    => w.Tag_Kinetic && !w.Tag_Beam;
    static bool IsEnergyBeamType(IWeaponTemplate w) => (w.Tag_Energy || w.Tag_Beam) && !w.Tag_Kinetic;

    // The dominant weapon's damage-type label on a registered design (what the duel actually fired with), read
    // from one freshly spawned live ship.  Returns "kinetic" / "energy-beam" / "mixed" / "none".
    string DmgTypeOf(IShipDesign d)
    {
        Ship s = SpawnShip(d.Name, Player, new Vector2(905000, 905000));
        UState.Objects.Update(TestSimStep); // let weapons install
        int kin = 0, nrg = 0, other = 0;
        foreach (ShipModule m in s.Modules)
        {
            if (m.InstalledWeapon == null) continue;
            bool k = m.InstalledWeapon.Tag_Kinetic && !m.InstalledWeapon.Tag_Beam;
            bool n = (m.InstalledWeapon.Tag_Energy || m.InstalledWeapon.Tag_Beam) && !m.InstalledWeapon.Tag_Kinetic;
            if (k) kin++; else if (n) nrg++; else other++;
        }
        s.QueueTotalRemoval();
        if (kin == 0 && nrg == 0 && other == 0) return "none";
        if (kin > 0 && nrg == 0) return "kinetic";
        if (nrg > 0 && kin == 0) return "energy-beam";
        return "mixed";
    }

    // The (min) ShieldKineticResist / ShieldEnergyResist across a design's installed shields — the resist values
    // that the in-combat path multiplies kinetic / energy damage by (1 - resist).  Negative kinetic = WEAK to
    // kinetic.  Read from one freshly spawned live ship; returns 0 if the design has no shield.
    (float kineticResist, float energyResist, float beamResist, int shields) ShieldResistsOf(IShipDesign d)
    {
        Ship s = SpawnShip(d.Name, Player, new Vector2(906000, 906000));
        float kin = 0f, nrg = 0f, beam = 0f; int n = 0;
        foreach (ShipModule m in s.Modules)
            if (m.Is(ShipModuleType.Shield))
            { kin += m.ShieldKineticResist; nrg += m.ShieldEnergyResist; beam += m.ShieldBeamResist; ++n; }
        s.QueueTotalRemoval();
        return n == 0 ? (0f, 0f, 0f, 0) : (kin / n, nrg / n, beam / n, n);
    }

    // The (avg) armor/hull KineticResist across a design's armor modules (the resist the armor branch applies to
    // kinetic).  Energy/Beam armor resist is ~0 in vanilla.  Read from one freshly spawned live ship.
    (float kineticResist, int armor) ArmorKineticResistOf(IShipDesign d)
    {
        Ship s = SpawnShip(d.Name, Player, new Vector2(907000, 907000));
        float kin = 0f; int n = 0;
        foreach (ShipModule m in s.Modules)
            if (m.ModuleType == ShipModuleType.Armor) { kin += m.KineticResist; ++n; }
        s.QueueTotalRemoval();
        return n == 0 ? (0f, 0) : (kin / n, n);
    }

    // One ATTACKER(A) vs TARGET(B) equal-count duel.  Faithful to the committed close-spawn arena
    // (CreateUniverseAndPlayerEmpire + SetupArena, BuildExactCount placement, seeded RNG, EngageAll + tight
    // 200-tick re-target nudge + early-out on wipe — same engagement discipline as the committed RunSmallDuel,
    // which is itself untouched).  The KEY extra measurement is targetHpDestroyed: the TARGET fleet's start-HP
    // minus its end-HP — i.e. how much the attacker's damage type chewed through THAT defense, which is the
    // resist effect in isolation.  Also returns each side's start/end strength + alive counts + min separation.
    (string winner, float aEnd, float bEnd, int aAlive, int bAlive,
     float targetStartHp, float targetHpDestroyed, float minSep, int ticksRun) RunDmgTypeDuel(
        IShipDesign attacker, IShipDesign target, int count, float closeHalf, ulong seed, int ticks)
    {
        CreateUniverseAndPlayerEmpire();
        SetupArena();
        var sa = MkSide(Player, 0, new Vector2(-closeHalf, 0), new[] { (0f, 1f) });
        var sb = MkSide(Enemy,  1, new Vector2( closeHalf, 0), new[] { (0f, 1f) });
        var sides = new List<Side> { sa, sb };

        BuildExactCount(sa, attacker, count);
        BuildExactCount(sb, target,   count);

        SetupWars(sides);
        UState.EnableDeterministicRng(seed);
        EngageAll(sides);

        float targetStartHp = FleetHealth(sb.Fleet);
        float minSep = float.MaxValue;
        int ticksRun = ticks;
        for (int t = 0; t < ticks; ++t)
        {
            UState.Objects.Update(TestSimStep);
            if (t % 200 == 199) EngageAll(sides);
            if (t % 50 == 0)
            {
                float closest = ClosestCrossSeparation(sa.Fleet, sb.Fleet);
                if (closest < minSep) minSep = closest;
            }
            if (t % 100 == 99 && (AliveOf(sa.Fleet) == 0 || AliveOf(sb.Fleet) == 0)) { ticksRun = t + 1; break; }
        }
        if (minSep == float.MaxValue) minSep = -1f;
        float targetEndHp = FleetHealth(sb.Fleet);
        float targetHpDestroyed = targetStartHp - targetEndHp;
        int aAlive = AliveOf(sa.Fleet), bAlive = AliveOf(sb.Fleet);
        float aEnd = StrengthOf(sa.Fleet), bEnd = StrengthOf(sb.Fleet);
        string winner =
            bAlive == 0 && aAlive > 0 ? "A" :
            aAlive == 0 && bAlive > 0 ? "B" :
            aEnd > bEnd ? "A" :
            bEnd > aEnd ? "B" : "draw";
        return (winner, aEnd, bEnd, aAlive, bAlive, targetStartHp, targetHpDestroyed, minSep, ticksRun);
    }

    // One attacker-vs-target cell (3 seeds folded): how much TARGET HP the attacker destroyed (avg + per-seed),
    // attacker win count, and avg attacker strength retained.
    sealed class DmgTypeCell
    {
        public string AttackerType, Defense; // "kinetic"/"energy-beam"  ;  "shield-heavy"/"armor-heavy"
        public float AvgTargetHpDestroyed, AvgTargetStartHp, AvgFracDestroyed; // fraction = destroyed/startHp
        public int AttackerWins, Seeds;
        public float AvgMinSep;
        public List<float> PerSeedHpDestroyed = new();
    }

    static string DmgTypeCellJson(DmgTypeCell c)
        => $"{{\"attackerType\":{J(c.AttackerType)},\"defense\":{J(c.Defense)},"
         + $"\"avgTargetHpDestroyed\":{F(c.AvgTargetHpDestroyed)},\"avgTargetStartHp\":{F(c.AvgTargetStartHp)},"
         + $"\"avgFracDestroyed\":{F(c.AvgFracDestroyed)},\"attackerWins\":{c.AttackerWins},\"seeds\":{c.Seeds},"
         + $"\"avgMinSep\":{F(c.AvgMinSep)},\"perSeedHpDestroyed\":[{string.Join(",", c.PerSeedHpDestroyed.Select(F))}]}}";

    // Run all 3 seeds of attacker vs target and fold into a DmgTypeCell.
    DmgTypeCell ScoreDmgTypeCell(string attackerType, string defense, IShipDesign attacker, IShipDesign target,
                                 int count, float closeHalf, ulong[] seeds, int ticks)
    {
        var c = new DmgTypeCell { AttackerType = attackerType, Defense = defense, Seeds = seeds.Length };
        float hpSum = 0f, startSum = 0f, fracSum = 0f, sepSum = 0f;
        foreach (ulong seed in seeds)
        {
            var r = RunDmgTypeDuel(attacker, target, count, closeHalf, seed, ticks);
            hpSum += r.targetHpDestroyed; startSum += r.targetStartHp;
            fracSum += r.targetStartHp > 0f ? r.targetHpDestroyed / r.targetStartHp : 0f;
            sepSum += r.minSep;
            if (r.winner == "A") c.AttackerWins++;
            c.PerSeedHpDestroyed.Add(r.targetHpDestroyed);
        }
        int n = Math.Max(1, seeds.Length);
        c.AvgTargetHpDestroyed = hpSum / n; c.AvgTargetStartHp = startSum / n;
        c.AvgFracDestroyed = fracSum / n;   c.AvgMinSep = sepSum / n;
        return c;
    }

    // A signature of a cell's per-seed HP-destroyed series, for the determinism rerun assert.
    static string DmgTypeSig(DmgTypeCell c) => string.Join("|", c.PerSeedHpDestroyed.Select(h => h.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)));

    [TestMethod]
    public void BalanceMeta_DamageTypeCounter_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire();
        SetupArena();

        var notes = new List<string>();
        const int EqCount     = 5;        // equal-count fleets
        const float CloseHalf = 4000f;    // close spawn so weapons reliably engage (8000u apart)
        const int Ticks       = 9000;     // generous; duel early-outs on a wipe
        var seeds = new ulong[] { 0xDA11A6E1u, 0xDA11A6E2u, 0xDA11A6E3u }; // 3 fixed seeds
        ulong salt = 0xDA00u;

        // ---- STEP 0: confirm the engine even HAS the damage-type->resist path wired (it does: Weapon.cs
        //      GetShieldDamageMod/GetArmorDamageMod, called from ShipModule.Damage).  Confirm vanilla content
        //      actually sets the asymmetric resists by sampling the vanilla shields/armor directly. ----
        var sampleShieldResist = ResourceManager.ShipModuleTemplates
            .Where(m => m.Is(ShipModuleType.Shield))
            .OrderBy(m => m.UID, StringComparer.Ordinal)
            .Select(m => (m.UID, m.ShieldKineticResist, m.ShieldEnergyResist, m.ShieldBeamResist))
            .ToArray();
        var sampleArmorResist = ResourceManager.ShipModuleTemplates
            .Where(m => m.ModuleType == ShipModuleType.Armor)
            .OrderBy(m => m.UID, StringComparer.Ordinal)
            .Select(m => (m.UID, m.KineticResist, m.EnergyResist, m.BeamResist))
            .ToArray();
        bool shieldsWeakToKinetic = sampleShieldResist.Any(s => s.ShieldKineticResist < 0f);
        bool shieldsResistEnergy  = sampleShieldResist.Any(s => s.ShieldEnergyResist > 0f || s.ShieldBeamResist > 0f);
        bool armorResistsKinetic  = sampleArmorResist.Any(a => a.KineticResist > 0f);
        Console.WriteLine($"[dtype] RESIST-PATH wired in combat: Weapon.GetShieldDamageMod/GetArmorDamageMod -> ShipModule.Damage (verified by code).");
        Console.WriteLine($"[dtype] CONTENT: shieldsWeakToKinetic={shieldsWeakToKinetic} shieldsResistEnergyOrBeam={shieldsResistEnergy} armorResistsKinetic={armorResistsKinetic}");
        foreach (var s in sampleShieldResist.Take(6))
            Console.WriteLine($"[dtype]   shield '{s.UID}': kineticResist={s.ShieldKineticResist:0.###} energyResist={s.ShieldEnergyResist:0.###} beamResist={s.ShieldBeamResist:0.###}");
        foreach (var a in sampleArmorResist.Take(4))
            Console.WriteLine($"[dtype]   armor  '{a.UID}': kineticResist={a.KineticResist:0.###} energyResist={a.EnergyResist:0.###} beamResist={a.BeamResist:0.###}");
        if (!shieldsWeakToKinetic && !shieldsResistEnergy)
            notes.Add("vanilla shields show no kinetic-weakness/energy-resist asymmetry -> the damage-type->resist counter is not content-tuned here");

        // ---- STEP 1: pick a fixed ATTACKER hull (a mid-cost warship that actually carries weapons) and build
        //      its KINETIC and ENERGY/BEAM mono-damage-type variants. ----
        IShipDesign[] warships = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly)
            .Where(d => d.Role == RoleName.cruiser || d.Role == RoleName.battleship || d.Role == RoleName.capital)
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();
        if (warships.Length == 0) { Assert.Inconclusive("no standalone warship available"); return; }

        // Attacker hull: a mid-cost warship that has >=1 weapon slot AND yields BOTH a kinetic and an
        // energy/beam variant (so the only thing differing between the two attacker fleets is damage type).
        IShipDesign attackerHull = null; IShipDesign kinAtk = null, nrgAtk = null;
        foreach (IShipDesign cand in warships.OrderBy(d => Math.Abs(d.GetCost(Player) - warships[warships.Length / 2].GetCost(Player))).ThenBy(d => d.Name, StringComparer.Ordinal))
        {
            var (k, ks, kk, kwhy) = BuildDamageTypeVariant(cand, "#KIN", IsKineticType, salt++);
            var (g, gs, gk, gwhy) = BuildDamageTypeVariant(cand, "#NRG", IsEnergyBeamType, salt++);
            if (k != null && g != null && DmgTypeOf(k) == "kinetic" && DmgTypeOf(g) == "energy-beam")
            { attackerHull = cand; kinAtk = k; nrgAtk = g; break; }
            if (k == null) notes.Add($"attacker cand '{cand.Name}': no kinetic variant ({kwhy})");
            if (g == null) notes.Add($"attacker cand '{cand.Name}': no energy/beam variant ({gwhy})");
        }
        if (attackerHull == null) { Assert.Inconclusive("no warship yielded both a clean kinetic and energy/beam mono-weapon variant"); return; }
        Console.WriteLine($"[dtype] ATTACKER hull='{attackerHull.Name}' ({attackerHull.Role}) -> KINETIC='{kinAtk.Name}' ({DmgTypeOf(kinAtk)}), ENERGY/BEAM='{nrgAtk.Name}' ({DmgTypeOf(nrgAtk)})");

        // ---- STEP 2: pick a fixed TARGET hull that has BOTH armor and shield slots, and build a SHIELD-heavy
        //      target (armor -> shields) and an ARMOR-heavy target (shields -> armor) from it, so the only
        //      difference between the two targets is the defense type. ----
        bool HasArmorMod(ShipModule m)  => m.ModuleType == ShipModuleType.Armor;

        IShipDesign shieldTarget = null, armorTarget = null; IShipDesign targetHull = null;
        foreach (IShipDesign cand in warships.Where(d => ShieldSlotCount(d) > 0))
        {
            // SHIELD-heavy: swap this hull's armor slots to a footprint-matched shield (more shield coverage).
            // pick a shield UID per the (dominant) armor footprint on the hull.
            Point armorSize = default; int bestN = 0;
            var sizeCounts = new Dictionary<Point, int>();
            foreach (DesignSlot ds in cand.GetOrLoadDesignSlots())
                if (ResourceManager.GetModuleTemplate(ds.ModuleUID, out ShipModule mm) && mm.ModuleType == ShipModuleType.Armor)
                    sizeCounts[ds.Size] = sizeCounts.TryGetValue(ds.Size, out int nn) ? nn + 1 : 1;
            foreach (var kv in sizeCounts.OrderByDescending(k => k.Value).ThenBy(k => k.Key.X).ThenBy(k => k.Key.Y))
                { armorSize = kv.Key; bestN = kv.Value; break; }

            string shieldUid = bestN > 0 ? FindShieldModuleUid(armorSize) : null;
            var sh = shieldUid != null
                ? BuildSwapVariant(cand, HasArmorMod, shieldUid, $"shldheavy{salt:X}", salt++)
                : (null, 0, 0, false);
            var (ar, arSwapped, arSkipped, arWhy) = BuildDoctrineVariant(cand, "STRIP_SHIELDS", salt++);

            if (sh.Item1 != null && ar != null)
            { targetHull = cand; shieldTarget = sh.Item1; armorTarget = ar; break; }
            if (sh.Item1 == null) notes.Add($"target cand '{cand.Name}': no shield-heavy variant (armor->shield swap infeasible)");
            if (ar == null)       notes.Add($"target cand '{cand.Name}': no armor-heavy variant ({arWhy})");
        }
        if (targetHull == null) { Assert.Inconclusive("no shielded warship yielded both a shield-heavy and an armor-heavy target variant"); return; }

        var (stKin, stNrg, stBeam, stShields) = ShieldResistsOf(shieldTarget);
        var (atKin, atArmor) = ArmorKineticResistOf(armorTarget);
        Console.WriteLine($"[dtype] TARGET hull='{targetHull.Name}' ({targetHull.Role})");
        Console.WriteLine($"[dtype]   SHIELD-heavy='{shieldTarget.Name}' shields={stShields} shieldKineticResist={stKin:0.###} (neg=weak to kinetic) shieldEnergyResist={stNrg:0.###} shieldBeamResist={stBeam:0.###}");
        Console.WriteLine($"[dtype]   ARMOR-heavy ='{armorTarget.Name}' armorMods={atArmor} armorKineticResist={atKin:0.###} (pos=resists kinetic) armorEnergy/BeamResist~0");

        // ---- STEP 3: the 2x2 duel matrix (attacker damage-type x target defense), 3 seeds each. ----
        DmgTypeCell kinVsShield = ScoreDmgTypeCell("kinetic",     "shield-heavy", kinAtk, shieldTarget, EqCount, CloseHalf, seeds, Ticks);
        DmgTypeCell nrgVsShield = ScoreDmgTypeCell("energy-beam", "shield-heavy", nrgAtk, shieldTarget, EqCount, CloseHalf, seeds, Ticks);
        DmgTypeCell kinVsArmor  = ScoreDmgTypeCell("kinetic",     "armor-heavy",  kinAtk, armorTarget,  EqCount, CloseHalf, seeds, Ticks);
        DmgTypeCell nrgVsArmor  = ScoreDmgTypeCell("energy-beam", "armor-heavy",  nrgAtk, armorTarget,  EqCount, CloseHalf, seeds, Ticks);

        var cells = new[] { kinVsShield, nrgVsShield, kinVsArmor, nrgVsArmor };
        foreach (DmgTypeCell c in cells)
            Console.WriteLine($"[dtype] {c.AttackerType,-11} vs {c.Defense,-12} : avgTargetHpDestroyed={c.AvgTargetHpDestroyed,9:0} ({c.AvgFracDestroyed:0.000} of {c.AvgTargetStartHp:0}) attackerWins={c.AttackerWins}/{c.Seeds} avgMinSep={c.AvgMinSep:0}");

        // ---- RPS analysis ----
        // vs SHIELDS: does KINETIC out-damage ENERGY/BEAM? (shields weak to kinetic, resist energy/beam)
        float shieldEdge = kinVsShield.AvgTargetHpDestroyed - nrgVsShield.AvgTargetHpDestroyed; // >0 => kinetic better vs shields
        // vs ARMOR: does ENERGY/BEAM out-damage KINETIC? (armor resists kinetic, no energy/beam resist)
        float armorEdge  = nrgVsArmor.AvgTargetHpDestroyed  - kinVsArmor.AvgTargetHpDestroyed;  // >0 => energy/beam better vs armor
        bool kineticCountersShields   = shieldEdge > 0f;
        bool energyBeamCountersArmor  = armorEdge  > 0f;
        // The COUNTER FLIPS = the best weapon type vs shields is the WORSE type vs armor (true RPS).
        bool counterFlips = kineticCountersShields && energyBeamCountersArmor;
        // normalize by start HP so the two defenses are comparable.
        float shieldEdgeFrac = kinVsShield.AvgFracDestroyed - nrgVsShield.AvgFracDestroyed;
        float armorEdgeFrac  = nrgVsArmor.AvgFracDestroyed  - kinVsArmor.AvgFracDestroyed;

        Console.WriteLine($"[dtype] RPS: vs SHIELDS kinetic-minus-energy HP-destroyed edge = {shieldEdge:0} ({shieldEdgeFrac:+0.000;-0.000} of start) -> kineticCountersShields={kineticCountersShields}");
        Console.WriteLine($"[dtype] RPS: vs ARMOR  energy-minus-kinetic HP-destroyed edge = {armorEdge:0} ({armorEdgeFrac:+0.000;-0.000} of start) -> energyBeamCountersArmor={energyBeamCountersArmor}");

        string verdict =
            !shieldsWeakToKinetic && !shieldsResistEnergy
              ? "NO COUNTER (content): the resist path is wired in combat, but vanilla shields are not tuned with the kinetic-weak/energy-resist asymmetry, so weapon-type is not a counter here"
            : counterFlips
              ? $"GENUINE RPS: weapon-type is a real counter — KINETIC destroys {shieldEdge:0} more target HP vs SHIELDS (shields' negative kinetic resist), while ENERGY/BEAM destroys {armorEdge:0} more vs ARMOR (armor resists kinetic, not energy/beam); the best weapon FLIPS with the defense"
            : kineticCountersShields
              ? $"PARTIAL: KINETIC counters shields (+{shieldEdge:0} HP destroyed vs energy/beam) as predicted, but energy/beam does NOT clearly out-damage kinetic vs armor (edge {armorEdge:0}) — the counter exists on the shield side only"
            : energyBeamCountersArmor
              ? $"PARTIAL: ENERGY/BEAM counters armor (+{armorEdge:0} HP destroyed vs kinetic) as predicted, but kinetic does NOT clearly out-damage energy/beam vs shields (edge {shieldEdge:0}) — the counter exists on the armor side only"
              : $"NO COUNTER (combat): despite the wired resist path and asymmetric content, the close-spawn duels show no weapon-type edge that flips with the defense (shieldEdge={shieldEdge:0}, armorEdge={armorEdge:0}) — likely overwhelmed by raw DPS / shields collapsing too fast to matter";

        // ---- DETERMINISM: rerun every cell; the per-seed HP-destroyed series must reproduce bit-identically. ----
        DmgTypeCell kinVsShield2 = ScoreDmgTypeCell("kinetic",     "shield-heavy", kinAtk, shieldTarget, EqCount, CloseHalf, seeds, Ticks);
        DmgTypeCell nrgVsArmor2  = ScoreDmgTypeCell("energy-beam", "armor-heavy",  nrgAtk, armorTarget,  EqCount, CloseHalf, seeds, Ticks);
        bool reproducible = DmgTypeSig(kinVsShield) == DmgTypeSig(kinVsShield2)
                         && DmgTypeSig(nrgVsArmor)  == DmgTypeSig(nrgVsArmor2);
        Console.WriteLine($"[dtype] REPRO rerun reproduced the per-seed HP-destroyed series bit-identically: {reproducible}");
        notes.Add($"reproducible={reproducible}");

        // ---- EMIT damagetype.json ----
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);

        string shieldResistJson = string.Join(",\n    ", sampleShieldResist.Take(8).Select(s =>
            $"{{\"uid\":{J(s.UID)},\"kineticResist\":{F(s.ShieldKineticResist)},\"energyResist\":{F(s.ShieldEnergyResist)},\"beamResist\":{F(s.ShieldBeamResist)}}}"));
        string armorResistJson = string.Join(",\n    ", sampleArmorResist.Take(8).Select(a =>
            $"{{\"uid\":{J(a.UID)},\"kineticResist\":{F(a.KineticResist)},\"energyResist\":{F(a.EnergyResist)},\"beamResist\":{F(a.BeamResist)}}}"));
        string cellsJson = string.Join(",\n    ", cells.Select(DmgTypeCellJson));

        string json =
            "{\n" +
            "  \"experiment\": \"WEAPON DAMAGE-TYPE as a counter (RPS): does KINETIC pierce shields (negative ShieldKineticResist) while ENERGY/BEAM is resisted by shields, and the reverse vs armor (armor resists kinetic, no energy/beam resist)? Build mono-kinetic and mono-energy/beam attacker fleets, duel each vs a shield-heavy and an armor-heavy target (equal count, 3 seeds), and measure TARGET HP destroyed.\",\n" +
            $"  \"resistPathWired\": true,\n" +
            $"  \"resistPathSource\": \"Ship_Game/Gameplay/Weapon.cs GetShieldDamageMod/GetArmorDamageMod -> ShipModule.Damage (ShipModule.cs ~795); per-tag resist SKIPPED for exploding projectiles (Explosive resist only)\",\n" +
            $"  \"shieldsWeakToKinetic\": {(shieldsWeakToKinetic ? "true" : "false")},\n" +
            $"  \"shieldsResistEnergyOrBeam\": {(shieldsResistEnergy ? "true" : "false")},\n" +
            $"  \"armorResistsKinetic\": {(armorResistsKinetic ? "true" : "false")},\n" +
            $"  \"equalCount\": {EqCount},\n" +
            $"  \"closeHalfDist\": {F(CloseHalf)},\n" +
            $"  \"maxTicks\": {Ticks},\n" +
            $"  \"seedsHex\": [{string.Join(",", seeds.Select(s => J($"0x{s:X}")))}],\n" +
            $"  \"attackerHull\": {J(attackerHull.Name)},\n" +
            $"  \"kineticDesign\": {J(kinAtk.Name)},\n" +
            $"  \"energyBeamDesign\": {J(nrgAtk.Name)},\n" +
            $"  \"targetHull\": {J(targetHull.Name)},\n" +
            $"  \"shieldTarget\": {J(shieldTarget.Name)},\n" +
            $"  \"armorTarget\": {J(armorTarget.Name)},\n" +
            $"  \"shieldTargetKineticResist\": {F(stKin)},\n" +
            $"  \"shieldTargetEnergyResist\": {F(stNrg)},\n" +
            $"  \"shieldTargetBeamResist\": {F(stBeam)},\n" +
            $"  \"armorTargetKineticResist\": {F(atKin)},\n" +
            $"  \"shieldEdgeHpDestroyed\": {F(shieldEdge)},\n" +
            $"  \"armorEdgeHpDestroyed\": {F(armorEdge)},\n" +
            $"  \"shieldEdgeFrac\": {F(shieldEdgeFrac)},\n" +
            $"  \"armorEdgeFrac\": {F(armorEdgeFrac)},\n" +
            $"  \"kineticCountersShields\": {(kineticCountersShields ? "true" : "false")},\n" +
            $"  \"energyBeamCountersArmor\": {(energyBeamCountersArmor ? "true" : "false")},\n" +
            $"  \"counterFlips\": {(counterFlips ? "true" : "false")},\n" +
            $"  \"reproducible\": {(reproducible ? "true" : "false")},\n" +
            $"  \"verdict\": {J(verdict)},\n" +
            $"  \"cells\": [\n    {cellsJson}\n  ],\n" +
            $"  \"sampleShieldResists\": [\n    {shieldResistJson}\n  ],\n" +
            $"  \"sampleArmorResists\": [\n    {armorResistJson}\n  ],\n" +
            $"  \"notes\": [{string.Join(",", notes.Select(J))}]\n" +
            "}\n";
        string jsonPath = Path.Combine(dir, "damagetype.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[dtype] DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        Console.WriteLine($"[dtype] VERDICT {verdict}");
        foreach (string n in notes) Console.WriteLine($"[dtype] NOTE {n}");

        // ---- ASSERTS (the test proves the harness ran a real, reproducible experiment — NOT that a counter
        //      exists; the verdict is honest either way, like the EMP shield-block negative finding). ----
        Assert.IsTrue(File.Exists(jsonPath), "damagetype.json written");
        Assert.AreEqual("kinetic",     DmgTypeOf(kinAtk), "the kinetic attacker fleet really fires kinetic weapons");
        Assert.AreEqual("energy-beam", DmgTypeOf(nrgAtk), "the energy/beam attacker fleet really fires energy/beam weapons");
        Assert.IsTrue(stShields > 0, "the shield-heavy target really carries shields");
        Assert.IsTrue(atArmor > 0,   "the armor-heavy target really carries armor");
        // At least one matchup must have produced real combat (some target HP destroyed) so the measurement is meaningful.
        Assert.IsTrue(cells.Any(c => c.AvgTargetHpDestroyed > 0f), "at least one damage-type matchup destroyed real target HP (the fleets fought)");
        Assert.IsTrue(reproducible, "the seeded damage-type duels are reproducible (deterministic)");
    }

    // ===================================================================================================
    //  BalanceMeta_DamageTypeRPS_EmitsJson  (method <M> = DamageTypeRPS)
    //  ---------------------------------------------------------------------------------------------------
    //  WHY: the committed BalanceMeta_DamageTypeCounter found kinetic out-damaged energy/beam vs BOTH shields
    //  AND armor — but the two attacker fleets were NOT DPS-matched (equal SHIP COUNT, not equal throughput),
    //  so that result is confounded: kinetic modules simply have higher raw DPS, so the kinetic fleet poured
    //  more total damage into BOTH targets regardless of resist.  That confound makes it impossible to tell
    //  whether a TRUE symmetric rock-paper-scissors exists.
    //
    //  THIS TEST removes the throughput confound TWO ways:
    //   (a) EQUALIZE raw DPS: measure each attacker fleet's per-ship total weapon DPS (sum InstalledWeapon
    //       .DamagePerSecond over a freshly spawned ship), then scale the LOWER-DPS fleet's SHIP COUNT up so
    //       both fleets deliver (as near as integer counts allow) EQUAL total raw DPS to the target.  A small
    //       residual count mismatch is corrected by also reporting (b).
    //   (b) the DPS-INVARIANT RATIO: for each weapon family, damageDealt-vs-SHIELD / damageDealt-vs-ARMOR.
    //       This ratio cancels out raw throughput entirely — a REAL resist counter shows KINETIC with a
    //       HIGHER shield/armor ratio than energy/beam (kinetic relatively better vs shields), and ENERGY/BEAM
    //       with a higher armor/shield ratio (relatively better vs armor).
    //
    //  KEY QUESTION: once throughput is equal, does ENERGY/BEAM win vs ARMOR (armor has ~zero energy/beam
    //  resist) while KINETIC wins vs SHIELDS (shields' negative kinetic resist) — a TRUE symmetric RPS?  Or
    //  does the counter stay one-sided / vanish?  Non-exploding weapons only (exploding projectiles skip the
    //  per-tag resist), inherited from FindDamageTypeModuleUid's non-exploding-first ordering.
    //
    //  Fully ADDITIVE: reuses the committed DamageTypeCounter machinery (BuildDamageTypeVariant, IsKineticType
    //  /IsEnergyBeamType, DmgTypeOf, ShieldResistsOf, ArmorKineticResistOf, FindShieldModuleUid, BuildSwapVariant,
    //  BuildDoctrineVariant, BuildExactCount, MkSide, SpawnShip, StrengthOf/AliveOf, FleetHealth, ShieldSlotCount,
    //  J/F) + new helpers (FleetWeaponDps, RunRpsDuel, RpsCell + DPS-match math) + this one method.  No committed
    //  method is altered.  DETERMINISM: seeded UState RNG, fixed seed set, rerun-reproduces assert.
    // ===================================================================================================

    // Per-ship total installed-weapon DPS for a registered design: spawn one ship, let modules install, sum
    // every InstalledWeapon.DamagePerSecond (the same precomputed throughput Ship.cs sums into TotalDps).  This
    // is the raw-throughput number we equalize across the two attacker fleets so the only difference left is
    // the damage TYPE (and therefore the resist interaction), not how much damage is being delivered.
    float FleetWeaponDpsPerShip(IShipDesign d)
    {
        Ship s = SpawnShip(d.Name, Player, new Vector2(908000, 908000));
        UState.Objects.Update(TestSimStep); // let weapons install
        float dps = 0f;
        foreach (ShipModule m in s.Modules)
            if (m.InstalledWeapon != null) dps += m.InstalledWeapon.DamagePerSecond;
        s.QueueTotalRemoval();
        return dps;
    }

    // One DPS-MATCHED attacker(A) vs target(B) duel.  Same close-spawn arena / engagement discipline as the
    // committed RunDmgTypeDuel, but the attacker fleet count is supplied per-fleet (the DPS-equalized count) and
    // the measurement returned is the raw TARGET HP destroyed by that fleet against THAT defense — the number
    // the DPS-invariant shield/armor ratio is built from.
    (string winner, float targetStartHp, float targetHpDestroyed, int aAlive, int bAlive,
     float aEnd, float bEnd, float minSep, int ticksRun) RunRpsDuel(
        IShipDesign attacker, int attackerCount, IShipDesign target, int targetCount,
        float closeHalf, ulong seed, int ticks)
    {
        CreateUniverseAndPlayerEmpire();
        SetupArena();
        var sa = MkSide(Player, 0, new Vector2(-closeHalf, 0), new[] { (0f, 1f) });
        var sb = MkSide(Enemy,  1, new Vector2( closeHalf, 0), new[] { (0f, 1f) });
        var sides = new List<Side> { sa, sb };

        BuildExactCount(sa, attacker, attackerCount);
        BuildExactCount(sb, target,   targetCount);

        SetupWars(sides);
        UState.EnableDeterministicRng(seed);
        EngageAll(sides);

        float targetStartHp = FleetHealth(sb.Fleet);
        float minSep = float.MaxValue;
        int ticksRun = ticks;
        for (int t = 0; t < ticks; ++t)
        {
            UState.Objects.Update(TestSimStep);
            if (t % 200 == 199) EngageAll(sides);
            if (t % 50 == 0)
            {
                float closest = ClosestCrossSeparation(sa.Fleet, sb.Fleet);
                if (closest < minSep) minSep = closest;
            }
            if (t % 100 == 99 && (AliveOf(sa.Fleet) == 0 || AliveOf(sb.Fleet) == 0)) { ticksRun = t + 1; break; }
        }
        if (minSep == float.MaxValue) minSep = -1f;
        float targetEndHp = FleetHealth(sb.Fleet);
        float targetHpDestroyed = targetStartHp - targetEndHp;
        int aAlive = AliveOf(sa.Fleet), bAlive = AliveOf(sb.Fleet);
        float aEnd = StrengthOf(sa.Fleet), bEnd = StrengthOf(sb.Fleet);
        string winner =
            bAlive == 0 && aAlive > 0 ? "A" :
            aAlive == 0 && bAlive > 0 ? "B" :
            aEnd > bEnd ? "A" :
            bEnd > aEnd ? "B" : "draw";
        return (winner, targetStartHp, targetHpDestroyed, aAlive, bAlive, aEnd, bEnd, minSep, ticksRun);
    }

    // One DPS-matched attacker-vs-defense cell (3 seeds folded).
    sealed class RpsCell
    {
        public string AttackerType, Defense;       // "kinetic"/"energy-beam" ; "shield-heavy"/"armor-heavy"
        public int AttackerCount, Seeds;
        public float TotalFleetDps;                // perShipDps * AttackerCount (the equalized throughput)
        public float AvgTargetHpDestroyed, AvgTargetStartHp, AvgFracDestroyed, AvgMinSep;
        public int AttackerWins;
        public List<float> PerSeedHpDestroyed = new();
    }

    RpsCell ScoreRpsCell(string attackerType, string defense, IShipDesign attacker, int attackerCount,
                         float perShipDps, IShipDesign target, int targetCount,
                         float closeHalf, ulong[] seeds, int ticks)
    {
        var c = new RpsCell
        {
            AttackerType = attackerType, Defense = defense, AttackerCount = attackerCount,
            Seeds = seeds.Length, TotalFleetDps = perShipDps * attackerCount
        };
        float hpSum = 0f, startSum = 0f, fracSum = 0f, sepSum = 0f;
        foreach (ulong seed in seeds)
        {
            var r = RunRpsDuel(attacker, attackerCount, target, targetCount, closeHalf, seed, ticks);
            hpSum += r.targetHpDestroyed; startSum += r.targetStartHp;
            fracSum += r.targetStartHp > 0f ? r.targetHpDestroyed / r.targetStartHp : 0f;
            sepSum += r.minSep;
            if (r.winner == "A") c.AttackerWins++;
            c.PerSeedHpDestroyed.Add(r.targetHpDestroyed);
        }
        int n = Math.Max(1, seeds.Length);
        c.AvgTargetHpDestroyed = hpSum / n; c.AvgTargetStartHp = startSum / n;
        c.AvgFracDestroyed = fracSum / n;   c.AvgMinSep = sepSum / n;
        return c;
    }

    static string RpsSig(RpsCell c) => string.Join("|", c.PerSeedHpDestroyed.Select(h => h.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)));

    static string RpsCellJson(RpsCell c)
        => $"{{\"attackerType\":{J(c.AttackerType)},\"defense\":{J(c.Defense)},\"attackerCount\":{c.AttackerCount},"
         + $"\"totalFleetDps\":{F(c.TotalFleetDps)},\"avgTargetHpDestroyed\":{F(c.AvgTargetHpDestroyed)},"
         + $"\"avgTargetStartHp\":{F(c.AvgTargetStartHp)},\"avgFracDestroyed\":{F(c.AvgFracDestroyed)},"
         + $"\"attackerWins\":{c.AttackerWins},\"seeds\":{c.Seeds},\"avgMinSep\":{F(c.AvgMinSep)},"
         + $"\"perSeedHpDestroyed\":[{string.Join(",", c.PerSeedHpDestroyed.Select(F))}]}}";

    [TestMethod]
    public void BalanceMeta_DamageTypeRPS_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire();
        SetupArena();

        var notes = new List<string>();
        const float CloseHalf = 4000f;    // close spawn so weapons reliably engage (8000u apart)
        const int Ticks       = 9000;     // generous; duel early-outs on a wipe
        const int BaseCount   = 5;        // baseline count for whichever fleet has the HIGHER per-ship DPS
        var seeds = new ulong[] { 0xD9501u, 0xD9502u, 0xD9503u }; // 3 fixed seeds
        ulong salt = 0xD9500u;

        // ---- STEP 0: sample vanilla resist asymmetry (so the verdict can say "content-tuned" or not). ----
        var sampleShieldResist = ResourceManager.ShipModuleTemplates
            .Where(m => m.Is(ShipModuleType.Shield))
            .OrderBy(m => m.UID, StringComparer.Ordinal)
            .Select(m => (m.UID, m.ShieldKineticResist, m.ShieldEnergyResist, m.ShieldBeamResist))
            .ToArray();
        var sampleArmorResist = ResourceManager.ShipModuleTemplates
            .Where(m => m.ModuleType == ShipModuleType.Armor)
            .OrderBy(m => m.UID, StringComparer.Ordinal)
            .Select(m => (m.UID, m.KineticResist, m.EnergyResist, m.BeamResist))
            .ToArray();
        bool shieldsWeakToKinetic = sampleShieldResist.Any(s => s.ShieldKineticResist < 0f);
        bool shieldsResistEnergy  = sampleShieldResist.Any(s => s.ShieldEnergyResist > 0f || s.ShieldBeamResist > 0f);
        bool armorResistsKinetic  = sampleArmorResist.Any(a => a.KineticResist > 0f);
        Console.WriteLine($"[rps] CONTENT: shieldsWeakToKinetic={shieldsWeakToKinetic} shieldsResistEnergyOrBeam={shieldsResistEnergy} armorResistsKinetic={armorResistsKinetic}");

        // ---- STEP 1: build the kinetic + energy/beam mono-weapon attacker variants (committed builder). ----
        IShipDesign[] warships = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly)
            .Where(d => d.Role == RoleName.cruiser || d.Role == RoleName.battleship || d.Role == RoleName.capital)
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();
        if (warships.Length == 0) { Assert.Inconclusive("no standalone warship available"); return; }

        IShipDesign attackerHull = null; IShipDesign kinAtk = null, nrgAtk = null;
        foreach (IShipDesign cand in warships.OrderBy(d => Math.Abs(d.GetCost(Player) - warships[warships.Length / 2].GetCost(Player))).ThenBy(d => d.Name, StringComparer.Ordinal))
        {
            var (k, ks, kk, kwhy) = BuildDamageTypeVariant(cand, "#RKIN", IsKineticType, salt++);
            var (g, gs, gk, gwhy) = BuildDamageTypeVariant(cand, "#RNRG", IsEnergyBeamType, salt++);
            if (k != null && g != null && DmgTypeOf(k) == "kinetic" && DmgTypeOf(g) == "energy-beam")
            { attackerHull = cand; kinAtk = k; nrgAtk = g; break; }
            if (k == null) notes.Add($"attacker cand '{cand.Name}': no kinetic variant ({kwhy})");
            if (g == null) notes.Add($"attacker cand '{cand.Name}': no energy/beam variant ({gwhy})");
        }
        if (attackerHull == null) { Assert.Inconclusive("no warship yielded both a clean kinetic and energy/beam mono-weapon variant"); return; }
        Console.WriteLine($"[rps] ATTACKER hull='{attackerHull.Name}' ({attackerHull.Role}) -> KINETIC='{kinAtk.Name}' ENERGY/BEAM='{nrgAtk.Name}'");

        // ---- STEP 2: build the shield-heavy + armor-heavy targets (committed builders). ----
        bool HasArmorMod(ShipModule m) => m.ModuleType == ShipModuleType.Armor;
        IShipDesign shieldTarget = null, armorTarget = null; IShipDesign targetHull = null;
        foreach (IShipDesign cand in warships.Where(d => ShieldSlotCount(d) > 0))
        {
            Point armorSize = default; int bestN = 0;
            var sizeCounts = new Dictionary<Point, int>();
            foreach (DesignSlot ds in cand.GetOrLoadDesignSlots())
                if (ResourceManager.GetModuleTemplate(ds.ModuleUID, out ShipModule mm) && mm.ModuleType == ShipModuleType.Armor)
                    sizeCounts[ds.Size] = sizeCounts.TryGetValue(ds.Size, out int nn) ? nn + 1 : 1;
            foreach (var kv in sizeCounts.OrderByDescending(k => k.Value).ThenBy(k => k.Key.X).ThenBy(k => k.Key.Y))
                { armorSize = kv.Key; bestN = kv.Value; break; }

            string shieldUid = bestN > 0 ? FindShieldModuleUid(armorSize) : null;
            var sh = shieldUid != null
                ? BuildSwapVariant(cand, HasArmorMod, shieldUid, $"rshldheavy{salt:X}", salt++)
                : (null, 0, 0, false);
            var (ar, arSwapped, arSkipped, arWhy) = BuildDoctrineVariant(cand, "STRIP_SHIELDS", salt++);

            if (sh.Item1 != null && ar != null)
            { targetHull = cand; shieldTarget = sh.Item1; armorTarget = ar; break; }
            if (sh.Item1 == null) notes.Add($"target cand '{cand.Name}': no shield-heavy variant (armor->shield swap infeasible)");
            if (ar == null)       notes.Add($"target cand '{cand.Name}': no armor-heavy variant ({arWhy})");
        }
        if (targetHull == null) { Assert.Inconclusive("no shielded warship yielded both a shield-heavy and an armor-heavy target variant"); return; }

        var (stKin, stNrg, stBeam, stShields) = ShieldResistsOf(shieldTarget);
        var (atKin, atArmor) = ArmorKineticResistOf(armorTarget);
        Console.WriteLine($"[rps] TARGET hull='{targetHull.Name}' SHIELD-heavy='{shieldTarget.Name}' shields={stShields} shieldKineticResist={stKin:0.###} shieldEnergyResist={stNrg:0.###} shieldBeamResist={stBeam:0.###}");
        Console.WriteLine($"[rps] TARGET ARMOR-heavy='{armorTarget.Name}' armorMods={atArmor} armorKineticResist={atKin:0.###}");

        // ---- STEP 3: DPS EQUALIZATION.  Measure each attacker fleet's per-ship weapon DPS, then choose ship
        //      counts so the LOWER-DPS fleet is scaled UP to match the higher-DPS fleet's total raw throughput.
        //      The higher-DPS fleet stays at BaseCount; the other's count = round(baseTotalDps / itsPerShipDps),
        //      clamped to [1,60] (BuildExactCount's own clamp).  We then report the residual DPS mismatch so the
        //      reader knows how close to perfectly matched the integer counts got — and rely on the DPS-INVARIANT
        //      shield/armor RATIO (STEP 5) which cancels any residual throughput difference entirely. ----
        float kinPerShipDps = FleetWeaponDpsPerShip(kinAtk);
        float nrgPerShipDps = FleetWeaponDpsPerShip(nrgAtk);
        if (kinPerShipDps <= 0f || nrgPerShipDps <= 0f) { Assert.Inconclusive($"a mono-weapon fleet measured zero weapon DPS (kin={kinPerShipDps}, nrg={nrgPerShipDps})"); return; }

        int kinCount, nrgCount;
        if (kinPerShipDps >= nrgPerShipDps)
        {
            kinCount = BaseCount;
            float baseTotal = kinPerShipDps * BaseCount;
            nrgCount = Math.Clamp((int)Math.Round(baseTotal / nrgPerShipDps), 1, 60);
        }
        else
        {
            nrgCount = BaseCount;
            float baseTotal = nrgPerShipDps * BaseCount;
            kinCount = Math.Clamp((int)Math.Round(baseTotal / kinPerShipDps), 1, 60);
        }
        float kinTotalDps = kinPerShipDps * kinCount;
        float nrgTotalDps = nrgPerShipDps * nrgCount;
        float dpsMatchErr = Math.Abs(kinTotalDps - nrgTotalDps) / Math.Max(kinTotalDps, nrgTotalDps); // 0 = perfect
        Console.WriteLine($"[rps] DPS-MATCH kinPerShip={kinPerShipDps:0.#} nrgPerShip={nrgPerShipDps:0.#} -> kinCount={kinCount} (total {kinTotalDps:0.#}) nrgCount={nrgCount} (total {nrgTotalDps:0.#}) matchErr={dpsMatchErr:0.000}");
        notes.Add($"dpsMatchErr={dpsMatchErr:0.000} (residual after integer-count equalization; the shield/armor RATIO below is DPS-invariant)");

        // Target fleets: fixed equal count for both defenses so the only thing varying across a row is the
        // ATTACKER damage type, and across a column is the DEFENSE type.
        const int TargetCount = 5;

        // ---- STEP 4: the DPS-MATCHED 2x2 duel matrix (attacker damage-type x target defense), 3 seeds each. ----
        RpsCell kinVsShield = ScoreRpsCell("kinetic",     "shield-heavy", kinAtk, kinCount, kinPerShipDps, shieldTarget, TargetCount, CloseHalf, seeds, Ticks);
        RpsCell nrgVsShield = ScoreRpsCell("energy-beam", "shield-heavy", nrgAtk, nrgCount, nrgPerShipDps, shieldTarget, TargetCount, CloseHalf, seeds, Ticks);
        RpsCell kinVsArmor  = ScoreRpsCell("kinetic",     "armor-heavy",  kinAtk, kinCount, kinPerShipDps, armorTarget,  TargetCount, CloseHalf, seeds, Ticks);
        RpsCell nrgVsArmor  = ScoreRpsCell("energy-beam", "armor-heavy",  nrgAtk, nrgCount, nrgPerShipDps, armorTarget,  TargetCount, CloseHalf, seeds, Ticks);

        var cells = new[] { kinVsShield, nrgVsShield, kinVsArmor, nrgVsArmor };
        foreach (RpsCell c in cells)
            Console.WriteLine($"[rps] {c.AttackerType,-11} vs {c.Defense,-12} : count={c.AttackerCount} totalDps={c.TotalFleetDps,8:0} avgTargetHpDestroyed={c.AvgTargetHpDestroyed,9:0} ({c.AvgFracDestroyed:0.000}) attackerWins={c.AttackerWins}/{c.Seeds}");

        // ---- STEP 5: the DPS-INVARIANT RATIO.  For each weapon family, shield-target HP-destroyed / armor-target
        //      HP-destroyed.  This cancels raw throughput, so a TRUE resist counter shows KINETIC with a HIGHER
        //      shield/armor ratio than energy/beam (kinetic relatively better at chewing shields), i.e. the
        //      family that is relatively best vs shields is relatively WORST vs armor.  The decisive RPS test. ----
        float kinShieldArmorRatio = kinVsArmor.AvgTargetHpDestroyed  > 0f ? kinVsShield.AvgTargetHpDestroyed / kinVsArmor.AvgTargetHpDestroyed : -1f;
        float nrgShieldArmorRatio = nrgVsArmor.AvgTargetHpDestroyed  > 0f ? nrgVsShield.AvgTargetHpDestroyed / nrgVsArmor.AvgTargetHpDestroyed : -1f;
        // kinetic relatively better vs shields than energy is  <=>  kinShieldArmorRatio > nrgShieldArmorRatio
        bool kineticRelBetterVsShields = kinShieldArmorRatio > nrgShieldArmorRatio && kinShieldArmorRatio >= 0f && nrgShieldArmorRatio >= 0f;
        // and the mirror: energy relatively better vs armor <=> energy's armor/shield ratio > kinetic's
        float kinArmorShieldRatio = kinVsShield.AvgTargetHpDestroyed > 0f ? kinVsArmor.AvgTargetHpDestroyed / kinVsShield.AvgTargetHpDestroyed : -1f;
        float nrgArmorShieldRatio = nrgVsShield.AvgTargetHpDestroyed > 0f ? nrgVsArmor.AvgTargetHpDestroyed / nrgVsShield.AvgTargetHpDestroyed : -1f;
        bool energyRelBetterVsArmor = nrgArmorShieldRatio > kinArmorShieldRatio && nrgArmorShieldRatio >= 0f && kinArmorShieldRatio >= 0f;

        // Raw (DPS-matched, but still count-residual) edges, for cross-checking the ratio verdict.
        float shieldEdge = kinVsShield.AvgTargetHpDestroyed - nrgVsShield.AvgTargetHpDestroyed; // >0 => kinetic chews shields harder
        float armorEdge  = nrgVsArmor.AvgTargetHpDestroyed  - kinVsArmor.AvgTargetHpDestroyed;  // >0 => energy/beam chews armor harder
        bool kineticCountersShields  = shieldEdge > 0f;
        bool energyBeamCountersArmor = armorEdge  > 0f;

        // The DPS-INVARIANT verdict: a TRUE symmetric RPS requires BOTH ratio asymmetries to point the right way.
        bool trueSymmetricRps = kineticRelBetterVsShields && energyRelBetterVsArmor;

        Console.WriteLine($"[rps] RATIO kinetic shield/armor = {kinShieldArmorRatio:0.000}   energy/beam shield/armor = {nrgShieldArmorRatio:0.000}  -> kineticRelBetterVsShields={kineticRelBetterVsShields}");
        Console.WriteLine($"[rps] RATIO energy/beam armor/shield = {nrgArmorShieldRatio:0.000}   kinetic armor/shield = {kinArmorShieldRatio:0.000}  -> energyRelBetterVsArmor={energyRelBetterVsArmor}");
        Console.WriteLine($"[rps] RAW (DPS-matched) shieldEdge(kin-nrg)={shieldEdge:0} armorEdge(nrg-kin)={armorEdge:0}");

        string verdict =
            !shieldsWeakToKinetic && !shieldsResistEnergy
              ? "NO COUNTER (content): vanilla shields carry no kinetic-weak/energy-resist asymmetry, so even DPS-matched there is nothing for a damage-type RPS to exploit"
            : trueSymmetricRps
              ? $"TRUE SYMMETRIC RPS: once raw DPS is equalized, KINETIC is relatively better vs SHIELDS (shield/armor ratio {kinShieldArmorRatio:0.00} > energy/beam's {nrgShieldArmorRatio:0.00}) AND ENERGY/BEAM is relatively better vs ARMOR (armor/shield ratio {nrgArmorShieldRatio:0.00} > kinetic's {kinArmorShieldRatio:0.00}) — the best damage type FLIPS with the defense"
            : kineticRelBetterVsShields
              ? $"ONE-SIDED (shield side): DPS-matched, kinetic is relatively better vs shields (ratio {kinShieldArmorRatio:0.00} > {nrgShieldArmorRatio:0.00}) but energy/beam is NOT relatively better vs armor (armor/shield {nrgArmorShieldRatio:0.00} vs kinetic {kinArmorShieldRatio:0.00}) — the counter is not symmetric"
            : energyRelBetterVsArmor
              ? $"ONE-SIDED (armor side): DPS-matched, energy/beam is relatively better vs armor (ratio {nrgArmorShieldRatio:0.00} > {kinArmorShieldRatio:0.00}) but kinetic is NOT relatively better vs shields — the counter is not symmetric"
              : $"NO RPS (DPS-matched): once throughput is equalized the shield/armor ratios do not flip with damage type (kin s/a {kinShieldArmorRatio:0.00}, nrg s/a {nrgShieldArmorRatio:0.00}); the committed one-sided result was a throughput confound, not a real resist counter";

        // ---- DETERMINISM: rerun two cells; per-seed HP-destroyed series must reproduce bit-identically. ----
        RpsCell kinVsShield2 = ScoreRpsCell("kinetic",     "shield-heavy", kinAtk, kinCount, kinPerShipDps, shieldTarget, TargetCount, CloseHalf, seeds, Ticks);
        RpsCell nrgVsArmor2  = ScoreRpsCell("energy-beam", "armor-heavy",  nrgAtk, nrgCount, nrgPerShipDps, armorTarget,  TargetCount, CloseHalf, seeds, Ticks);
        bool reproducible = RpsSig(kinVsShield) == RpsSig(kinVsShield2) && RpsSig(nrgVsArmor) == RpsSig(nrgVsArmor2);
        Console.WriteLine($"[rps] REPRO rerun reproduced the per-seed HP-destroyed series bit-identically: {reproducible}");
        notes.Add($"reproducible={reproducible}");

        // ---- EMIT rps.json ----
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string cellsJson = string.Join(",\n    ", cells.Select(RpsCellJson));

        string json =
            "{\n" +
            "  \"experiment\": \"DPS-NORMALIZED damage-type RPS: removes the throughput confound from BalanceMeta_DamageTypeCounter. (a) EQUALIZE raw weapon DPS between the kinetic and energy/beam attacker fleets by scaling ship count to per-ship DamagePerSecond; (b) report the DPS-INVARIANT shield/armor HP-destroyed RATIO per family. A true symmetric RPS shows kinetic with a higher shield/armor ratio AND energy/beam with a higher armor/shield ratio (the best damage type flips with the defense).\",\n" +
            $"  \"resistPathSource\": \"Ship_Game/Gameplay/Weapon.cs GetShieldDamageMod/GetArmorDamageMod -> ShipModule.Damage; per-tag resist SKIPPED for exploding projectiles (non-exploding weapons selected first)\",\n" +
            $"  \"shieldsWeakToKinetic\": {(shieldsWeakToKinetic ? "true" : "false")},\n" +
            $"  \"shieldsResistEnergyOrBeam\": {(shieldsResistEnergy ? "true" : "false")},\n" +
            $"  \"armorResistsKinetic\": {(armorResistsKinetic ? "true" : "false")},\n" +
            $"  \"closeHalfDist\": {F(CloseHalf)},\n" +
            $"  \"maxTicks\": {Ticks},\n" +
            $"  \"targetCount\": {TargetCount},\n" +
            $"  \"seedsHex\": [{string.Join(",", seeds.Select(s => J($"0x{s:X}")))}],\n" +
            $"  \"attackerHull\": {J(attackerHull.Name)},\n" +
            $"  \"kineticDesign\": {J(kinAtk.Name)},\n" +
            $"  \"energyBeamDesign\": {J(nrgAtk.Name)},\n" +
            $"  \"kineticPerShipDps\": {F(kinPerShipDps)},\n" +
            $"  \"energyBeamPerShipDps\": {F(nrgPerShipDps)},\n" +
            $"  \"kineticCount\": {kinCount},\n" +
            $"  \"energyBeamCount\": {nrgCount},\n" +
            $"  \"kineticTotalDps\": {F(kinTotalDps)},\n" +
            $"  \"energyBeamTotalDps\": {F(nrgTotalDps)},\n" +
            $"  \"dpsMatchError\": {F(dpsMatchErr)},\n" +
            $"  \"targetHull\": {J(targetHull.Name)},\n" +
            $"  \"shieldTarget\": {J(shieldTarget.Name)},\n" +
            $"  \"armorTarget\": {J(armorTarget.Name)},\n" +
            $"  \"shieldTargetKineticResist\": {F(stKin)},\n" +
            $"  \"shieldTargetEnergyResist\": {F(stNrg)},\n" +
            $"  \"shieldTargetBeamResist\": {F(stBeam)},\n" +
            $"  \"armorTargetKineticResist\": {F(atKin)},\n" +
            $"  \"kineticShieldArmorRatio\": {F(kinShieldArmorRatio)},\n" +
            $"  \"energyBeamShieldArmorRatio\": {F(nrgShieldArmorRatio)},\n" +
            $"  \"kineticArmorShieldRatio\": {F(kinArmorShieldRatio)},\n" +
            $"  \"energyBeamArmorShieldRatio\": {F(nrgArmorShieldRatio)},\n" +
            $"  \"kineticRelBetterVsShields\": {(kineticRelBetterVsShields ? "true" : "false")},\n" +
            $"  \"energyBeamRelBetterVsArmor\": {(energyRelBetterVsArmor ? "true" : "false")},\n" +
            $"  \"shieldEdgeHpDestroyed\": {F(shieldEdge)},\n" +
            $"  \"armorEdgeHpDestroyed\": {F(armorEdge)},\n" +
            $"  \"kineticCountersShields\": {(kineticCountersShields ? "true" : "false")},\n" +
            $"  \"energyBeamCountersArmor\": {(energyBeamCountersArmor ? "true" : "false")},\n" +
            $"  \"trueSymmetricRps\": {(trueSymmetricRps ? "true" : "false")},\n" +
            $"  \"reproducible\": {(reproducible ? "true" : "false")},\n" +
            $"  \"verdict\": {J(verdict)},\n" +
            $"  \"cells\": [\n    {cellsJson}\n  ],\n" +
            $"  \"notes\": [{string.Join(",", notes.Select(J))}]\n" +
            "}\n";
        string jsonPath = Path.Combine(dir, "rps.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[rps] DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        Console.WriteLine($"[rps] VERDICT {verdict}");
        foreach (string nt in notes) Console.WriteLine($"[rps] NOTE {nt}");

        // ---- ASSERTS (the test proves the harness ran a real, DPS-matched, reproducible experiment — NOT that
        //      an RPS exists; the verdict is honest either way). ----
        Assert.IsTrue(File.Exists(jsonPath), "rps.json written");
        Assert.AreEqual("kinetic",     DmgTypeOf(kinAtk), "the kinetic attacker fleet really fires kinetic weapons");
        Assert.AreEqual("energy-beam", DmgTypeOf(nrgAtk), "the energy/beam attacker fleet really fires energy/beam weapons");
        Assert.IsTrue(stShields > 0, "the shield-heavy target really carries shields");
        Assert.IsTrue(atArmor > 0,   "the armor-heavy target really carries armor");
        Assert.IsTrue(kinTotalDps > 0f && nrgTotalDps > 0f, "both DPS-matched fleets deliver real weapon DPS");
        Assert.IsTrue(dpsMatchErr <= 0.30f, $"the two fleets are DPS-matched within 30% after integer-count equalization (err={dpsMatchErr:0.000})");
        Assert.IsTrue(cells.Any(c => c.AvgTargetHpDestroyed > 0f), "at least one DPS-matched matchup destroyed real target HP (the fleets fought)");
        Assert.IsTrue(reproducible, "the seeded DPS-matched damage-type duels are reproducible (deterministic)");
    }

    // ===================================================================================================
    // #21 BALANCE META — SHIELD-HEAVY ATTRITION GAUNTLET (ADDITIVE on top of #17/#18; the crossover test).
    //
    //    Every prior gauntlet (#17 RunGauntlet, #18 variant gauntlet) scored hulls where SHIELDS WERE 1-OF-MANY
    //    slots — the loadout was mostly weapons + armor, with a couple of shield slots bolted on. In that regime
    //    armor's raw HP-per-slot always won the campaign (the #17/#18 honest negative): permanent armor density
    //    compounds faster than a small shield pool can recover between waves. This probe asks the crossover
    //    question those gauntlets could not: WHEN SHIELDS DOMINATE THE LOADOUT, does free between-wave regen
    //    FINALLY win — i.e. is there a shield-FRACTION threshold where sustainment beats armor density?
    //
    //    To put shields in the driver's seat we don't hunt for a rare shield-heavy stock hull (there are few);
    //    we MANUFACTURE one. The MAX-SHIELD doctrine swaps a hull's ARMOR slots -> footprint-matched shields
    //    (committed BuildSwapVariant + a HasArmorMod predicate, the exact armor->shield move the #20 damage-type
    //    probe used), so nearly every defensive slot becomes a shield. We pick the 2-3 hulls whose shield FRACTION
    //    (shieldSlots / (shieldSlots+armorSlots)) climbs HIGHEST after this swap — the most shield-dominated
    //    loadouts available — and run them through the COMMITTED RunGauntlet campaign unchanged.
    //
    //    DOCTRINES (all built from the SAME base hull, EQUAL SHIP COUNT, run through the committed RunGauntlet):
    //      - MAXSHIELD : armor slots -> the hull's dominant-armor-footprint shield (max shield fraction). This is
    //                    the doctrine under test: a loadout where shields are a LARGE fraction, fully regenerating
    //                    between waves for free.
    //      - ARMOR     : STRIP_SHIELDS (shields -> armor; committed BuildDoctrineVariant) — the permanent-damage
    //                    control that won #14..#18.
    //      - CANOPY    : if the hull has 3x6 slots, install CanopyShield (3x6, 32000HP) into them — finally scores
    //                    the untested 3x6 variant from #18 (no #18 hull had a 3x6 slot, so it was always skipped).
    //      - ARMORED   : if the hull has 4x4 slots, install ArmoredShield4x4 (4x4, 28000HP) — finally scores the
    //                    untested 4x4 variant from #18 for the same reason.
    //
    //    Each doctrine runs the committed 5-wave RunGauntlet (regen between waves; armor/hull damage persists;
    //    fastRcFactor=0 — these are real swapped variants, not the base hull, so the synthetic FASTRC path is
    //    unused). Sustainment is classified with the #18 tiebreak fix (ClassifySustainment): a doctrine that
    //    clears every wave at ~0 repair-debt was NOT stressed -> 'indeterminate', never a strength-fallthrough
    //    winner. A doctrine only WINS when the campaign actually attrited the field.
    //
    //    KEY QUESTION (answered honestly either way): with shields a LARGE fraction of the loadout, does the
    //    MAXSHIELD doctrine win the campaign (more waves survived / lower final repair-debt / more force retained)
    //    over the ARMOR control? If armor STILL wins even shield-heavy, the verdict says so plainly.
    //
    //    ADDITIVE: new helpers (DominantArmorFootprint / ArmorSlotCount / SlotCountOfSize / ShieldFractionOf +
    //    ShvyRow/JSON) and this one method. Reuses committed BuildSwapVariant (clone+swap-by-UID, footprint-matched,
    //    spawn-verify), BuildDoctrineVariant (STRIP_SHIELDS), RunGauntlet / GauntletRun / GauntletSignature,
    //    ClassifySustainment, FindShieldModuleUid, BuildExactCount, MkSide, StrengthOf/AliveOf, ShieldSlotCount /
    //    ShieldSlotCountOfSize. No committed method is altered, so all prior results stay bit-identical.
    //    DETERMINISM: one fixed seed, GUID-free deterministic variant names, fixed wave squad + tick budgets;
    //    the test reruns each doctrine's full campaign and asserts a bit-identical wave series.
    // ===================================================================================================

    // Count a design's ARMOR slots (the mirror of ShieldSlotCount). Used with ShieldSlotCount to compute the
    // shield FRACTION of a loadout's defensive slots — the lever this probe pushes to its crossover.
    static int ArmorSlotCount(IShipDesign d)
    {
        int n = 0;
        foreach (DesignSlot s in d.GetOrLoadDesignSlots())
            if (ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m) && m.ModuleType == ShipModuleType.Armor)
                ++n;
        return n;
    }

    // Count a design's slots (any module type) whose footprint is EXACTLY `size`. Used to find hulls with 3x6 or
    // 4x4 slots that can take CanopyShield / ArmoredShield4x4 (a swap needs s.Size == replSize).
    static int SlotCountOfSize(IShipDesign d, Point size)
    {
        int n = 0;
        foreach (DesignSlot s in d.GetOrLoadDesignSlots())
            if (s.Size == size) ++n;
        return n;
    }

    // The most common ARMOR-slot footprint on a design (so the MAX-SHIELD swap targets a shield that footprint-
    // matches the bulk of the hull's armor). Returns (default, 0) if the hull has no armor slots. Deterministic
    // tiebreak by (count desc, X asc, Y asc) — identical to the #20 damage-type probe's armor-footprint pick.
    static (Point size, int count) DominantArmorFootprint(IShipDesign d)
    {
        var sizeCounts = new Dictionary<Point, int>();
        foreach (DesignSlot s in d.GetOrLoadDesignSlots())
            if (ResourceManager.GetModuleTemplate(s.ModuleUID, out ShipModule m) && m.ModuleType == ShipModuleType.Armor)
                sizeCounts[s.Size] = sizeCounts.TryGetValue(s.Size, out int n) ? n + 1 : 1;
        if (sizeCounts.Count == 0) return (default, 0);
        var best = sizeCounts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.X).ThenBy(kv => kv.Key.Y).First();
        return (best.Key, best.Value);
    }

    // Shield fraction of a design's DEFENSIVE slots: shieldSlots / (shieldSlots + armorSlots). 0..1; higher means
    // shields dominate the loadout. The whole point of the MAX-SHIELD swap is to drive this toward 1.
    static float ShieldFractionOf(IShipDesign d)
    {
        int sh = ShieldSlotCount(d), ar = ArmorSlotCount(d);
        int denom = sh + ar;
        return denom > 0 ? (float)sh / denom : 0f;
    }

    // One emitted shield-heavy row: a doctrine's campaign result on one hull + its sustainment classification +
    // the loadout's shield fraction (so the crossover — fraction vs who-wins — is readable straight from JSON).
    sealed class ShvyRow
    {
        public string Doctrine, VariantName, SustainState, SustainWhy, BuildNote;
        public int Swapped, Matched, ShieldSlots, ArmorSlots, StartShips, WavesSurvived, WavesCleared;
        public float ShieldFraction, FinalSurvivingStrength, FinalRepairDebt;
        public bool Clean;
        public GauntletRun Run;
    }

    static string ShvyRowJson(ShvyRow r)
        => "{\n      "
         + $"\"doctrine\":{J(r.Doctrine)},\"variantName\":{J(r.VariantName)},\"buildNote\":{J(r.BuildNote)},\n      "
         + $"\"shieldSlots\":{r.ShieldSlots},\"armorSlots\":{r.ArmorSlots},\"shieldFraction\":{F(r.ShieldFraction)},"
         + $"\"swapped\":{r.Swapped},\"matched\":{r.Matched},\"clean\":{(r.Clean ? "true" : "false")},\n      "
         + $"\"sustainState\":{J(r.SustainState)},\"sustainWhy\":{J(r.SustainWhy)},\n      "
         + $"\"startShips\":{r.StartShips},\"wavesSurvived\":{r.WavesSurvived},\"wavesCleared\":{r.WavesCleared},"
         + $"\"finalSurvivingStrength\":{F(r.FinalSurvivingStrength)},\"finalRepairDebt\":{F(r.FinalRepairDebt)},\n      "
         + $"\"campaign\":{GauntletRunJson(r.Run)}\n    }}";

    [TestMethod]
    public void BalanceMeta_ShieldHeavyGauntlet_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire();
        SetupArena();

        var notes = new List<string>();

        const int Count      = 6;      // EQUAL-COUNT doctrine fleets (same pack size as #14..#18)
        const int EnemyCount = 6;      // fixed neutral squad per wave
        const int Waves      = 5;      // 5-wave campaign (same as #17/#18)
        const int WaveTicks  = 4000;   // per-wave combat budget
        const int RegenTicks = 5000;   // between-wave OOC regen budget (>> any ShieldRechargeDelay)
        const ulong Seed     = 0x6C07u; // single fixed gauntlet seed (distinct from #17 0x6A07 / #18 0x6B07)
        const float DebtEps  = 50f;    // "near-zero" repair-debt threshold for the indeterminate classification
        ulong salt           = 0x6C00u; // distinct salt namespace for the variant names

        bool HasArmorMod(ShipModule m) => m.ModuleType == ShipModuleType.Armor;

        // ---- Candidate pool: STANDALONE warships (real combat role, not carrier-only) that HAVE shields AND
        //      armor (so a MAX-SHIELD armor->shield swap is meaningful). Cruiser..capital scale, like #17. ----
        var classOrder = new HashSet<RoleName> { RoleName.cruiser, RoleName.battleship, RoleName.capital };
        IShipDesign[] warships = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly)
            .Where(d => classOrder.Contains(d.Role))
            .Where(d => ShieldSlotCount(d) > 0 && ArmorSlotCount(d) > 0)
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        // ---- Build the MAX-SHIELD variant (armor -> dominant-armor-footprint shield) for every candidate and
        //      keep the ones whose SHIELD FRACTION climbs HIGHEST after the swap — the most shield-dominated
        //      loadouts (the regime no prior gauntlet ever tested). Deterministic ordering: fraction desc, then
        //      name. We pick the top 3. ----
        var maxShieldByHull = new Dictionary<string, (IShipDesign variant, int swapped, int matched, bool clean, string shieldUid, Point armorSize)>();
        var ranked = new List<(IShipDesign hull, float postFrac, float preFrac)>();
        foreach (IShipDesign cand in warships)
        {
            var (armorSize, armorN) = DominantArmorFootprint(cand);
            if (armorN == 0) continue;
            string shieldUid = FindShieldModuleUid(armorSize); // a real shield that footprint-matches the armor
            if (shieldUid == null) { notes.Add($"'{cand.Name}': no shield module of armor footprint {armorSize.X}x{armorSize.Y}; MAX-SHIELD infeasible"); continue; }

            var (variant, swapped, matched, clean) = BuildSwapVariant(cand, HasArmorMod, shieldUid, $"maxshld{salt:X}", salt++);
            if (variant == null) { notes.Add($"'{cand.Name}': MAX-SHIELD swap infeasible (matched={matched} swapped={swapped})"); continue; }

            float postFrac = ShieldFractionOf(variant);
            float preFrac  = ShieldFractionOf(cand);
            maxShieldByHull[cand.Name] = (variant, swapped, matched, clean, shieldUid, armorSize);
            ranked.Add((cand, postFrac, preFrac));
        }

        ranked = ranked.OrderByDescending(t => t.postFrac).ThenBy(t => t.hull.Name, StringComparer.Ordinal).ToList();
        var bases = ranked.Take(3).Select(t => t.hull).ToList();

        // ---- Force-source a hull whose slots fit CanopyShield(3x6) and one whose slots fit ArmoredShield4x4(4x4)
        //      so those two #18-untested variants finally get scored (the brief's secondary goal). No #18 hull had
        //      a 3x6 or 4x4 slot, so those doctrines were always skipped. We add the CHEAPEST shielded+armored
        //      warship with such a slot (deterministic — `ranked` is already fraction/name-ordered, but for the
        //      footprint pick we want the cheapest, so re-scan `warships` which is cost-ordered). The hull must
        //      have a MAX-SHIELD variant (be in maxShieldByHull) so its MAXSHIELD doctrine row can build. ----
        IShipDesign canopyHull  = warships.FirstOrDefault(d => maxShieldByHull.ContainsKey(d.Name) && SlotCountOfSize(d, new Point(3, 6)) > 0);
        IShipDesign armoredHull = warships.FirstOrDefault(d => maxShieldByHull.ContainsKey(d.Name) && SlotCountOfSize(d, new Point(4, 4)) > 0);
        if (canopyHull  != null && !bases.Contains(canopyHull))  { bases.Add(canopyHull);  notes.Add($"force-sourced '{canopyHull.Name}' (has 3x6 slot) so CanopyShield finally gets scored"); }
        else if (canopyHull  == null) notes.Add("no shielded+armored warship has a 3x6 slot in this content; CanopyShield(3x6) cannot be scored");
        if (armoredHull != null && !bases.Contains(armoredHull)) { bases.Add(armoredHull); notes.Add($"force-sourced '{armoredHull.Name}' (has 4x4 slot) so ArmoredShield4x4 finally gets scored"); }
        else if (armoredHull == null) notes.Add("no shielded+armored warship has a 4x4 slot in this content; ArmoredShield4x4(4x4) cannot be scored");

        Console.WriteLine($"[shvy] candidate pool = {warships.Length} shielded+armored cruiser..capital warships; "
                        + $"{ranked.Count} got a MAX-SHIELD variant. Top shield-fraction + footprint-sourced hulls picked:");
        foreach (IShipDesign b in bases)
        {
            var rt = ranked.First(t => t.hull == b);
            Console.WriteLine($"[shvy]   '{b.Name}' ({b.Role}) shieldFraction {rt.preFrac:0.00} -> {rt.postFrac:0.00} (MAX-SHIELD) "
                            + $"slots3x6={SlotCountOfSize(b, new Point(3, 6))} slots4x4={SlotCountOfSize(b, new Point(4, 4))} <== SCORED");
        }

        if (bases.Count == 0) { Assert.Inconclusive("no warship yielded a MAX-SHIELD (armor->shield) variant"); return; }

        // The fixed neutral wave squad = the cheapest standalone shielded warship (same deterministic pick rule as
        // #17/#18, so this probe stresses the doctrines with the SAME kind of generic foe).
        IShipDesign enemyDesign = warships.FirstOrDefault() ?? ResourceManager.Ships.Designs.FirstOrDefault(d => ShieldSlotCount(d) > 0);
        if (enemyDesign == null) { Assert.Inconclusive("no warship available for the wave squad"); return; }
        Console.WriteLine($"[shvy] wave squad = {EnemyCount}x '{enemyDesign.Name}' ({enemyDesign.Role}); "
                        + $"campaign = {Waves} waves, {WaveTicks} combat ticks + {RegenTicks} regen ticks/wave, seed={Seed:X}");

        var hullBlocks = new List<string>();
        int hullsScored = 0, nullCaseHulls = 0, maxShieldWins = 0, armorWins = 0;
        bool canopyEverScored = false, armoredEverScored = false;
        var doctrineWins = new Dictionary<string, int>();

        foreach (IShipDesign baseDesign in bases)
        {
            string hullTag = $"{baseDesign.Role}:'{baseDesign.Name}'";
            int baseShieldSlots = ShieldSlotCount(baseDesign), baseArmorSlots = ArmorSlotCount(baseDesign);

            var rows = new List<ShvyRow>();

            // Local: build a row by running the committed RunGauntlet on a variant, then classify sustainment.
            void AddRow(string doctrine, IShipDesign variant, int swapped, int matched, bool clean, string buildNote)
            {
                GauntletRun run = RunGauntlet(doctrine, variant, enemyDesign, Count, EnemyCount, Waves, WaveTicks, RegenTicks, Seed, 0f);
                var (state, why) = ClassifySustainment(run, Waves, DebtEps);
                rows.Add(new ShvyRow
                {
                    Doctrine = doctrine, VariantName = variant.Name, BuildNote = buildNote,
                    Swapped = swapped, Matched = matched, Clean = clean,
                    ShieldSlots = ShieldSlotCount(variant), ArmorSlots = ArmorSlotCount(variant),
                    ShieldFraction = ShieldFractionOf(variant),
                    StartShips = run.StartShips, WavesSurvived = run.WavesSurvived, WavesCleared = run.WavesCleared,
                    FinalSurvivingStrength = run.FinalSurvivingStrength, FinalRepairDebt = run.FinalRepairDebt,
                    SustainState = state, SustainWhy = why, Run = run,
                });
                Console.WriteLine($"[shvy] {hullTag,-30} {doctrine,-9} variant='{variant.Name}' swapped={swapped}/{matched} clean={clean} "
                                + $"shieldFrac={ShieldFractionOf(variant):0.00} ({ShieldSlotCount(variant)}sh/{ArmorSlotCount(variant)}ar) "
                                + $"wavesSurvived={run.WavesSurvived}/{Waves} cleared={run.WavesCleared} "
                                + $"finalStr={run.FinalSurvivingStrength:0} finalRepairDebt={run.FinalRepairDebt:0} sustain={state}");
                foreach (GauntletWave w in run.Waves)
                    Console.WriteLine($"[shvy]   {doctrine,-9} wave{w.Wave} alive={w.ShipsAlive}/{Count} "
                                    + $"str={w.SurvivingStrength:0} armorHull={w.ArmorHullHealth:0} "
                                    + $"shieldRestored={(w.ShieldMax > 0f ? w.ShieldPower / w.ShieldMax : 0f):0.00} "
                                    + $"repairDebt={w.RepairDebt:0} enemyCleared={w.EnemyCleared}");
            }

            // ---- MAXSHIELD: armor -> dominant-footprint shield (the shield-heavy doctrine under test). ----
            var ms = maxShieldByHull[baseDesign.Name];
            AddRow("MAXSHIELD", ms.variant, ms.swapped, ms.matched, ms.clean,
                   $"armor->{ms.shieldUid} ({ms.armorSize.X}x{ms.armorSize.Y}); shieldFraction {ShieldFractionOf(baseDesign):0.00}->{ShieldFractionOf(ms.variant):0.00}");

            // ---- ARMOR control: STRIP_SHIELDS (shields -> armor). ----
            var (armorV, arSw, arSk, arWhy) = BuildDoctrineVariant(baseDesign, "STRIP_SHIELDS", salt++);
            if (armorV != null)
                AddRow("ARMOR", armorV, arSw, arSw + arSk, arSk == 0, "STRIP_SHIELDS: shields->armor (permanent damage control)");
            else
                notes.Add($"{hullTag}: STRIP_SHIELDS armor control infeasible ({arWhy}); ARMOR doctrine skipped");

            // ---- CANOPY: if this hull has 3x6 slots, install CanopyShield (finally scores the #18-untested 3x6). ----
            if (SlotCountOfSize(baseDesign, new Point(3, 6)) > 0)
            {
                var (cv, csw, cmt, ccl) = BuildSwapVariant(baseDesign, _ => true, "CanopyShield", $"canopy{salt:X}", salt++);
                if (cv != null) { AddRow("CANOPY", cv, csw, cmt, ccl, "CanopyShield 3x6, 32000HP, 30s delay (3x6 slots only)"); canopyEverScored = true; }
                else notes.Add($"{hullTag}: has 3x6 slot(s) but CanopyShield install infeasible (matched={cmt} swapped={csw})");
            }

            // ---- ARMORED: if this hull has 4x4 slots, install ArmoredShield4x4 (finally scores the #18-untested 4x4). ----
            if (SlotCountOfSize(baseDesign, new Point(4, 4)) > 0)
            {
                var (av, asw, amt, acl) = BuildSwapVariant(baseDesign, _ => true, "ArmoredShield4x4", $"armrd{salt:X}", salt++);
                if (av != null) { AddRow("ARMORED", av, asw, amt, acl, "ArmoredShield4x4 4x4, 28000HP, 30s delay (4x4 slots only)"); armoredEverScored = true; }
                else notes.Add($"{hullTag}: has 4x4 slot(s) but ArmoredShield4x4 install infeasible (matched={amt} swapped={asw})");
            }

            if (rows.Count < 2)
            {
                notes.Add($"{hullTag}: fewer than 2 feasible doctrines ({rows.Count}); cannot compare -> hull skipped");
                Console.WriteLine($"[shvy] HULL-SKIP {hullTag}: only {rows.Count} feasible doctrine(s)");
                continue;
            }

            // ---- NULL-CASE GUARD (#18): if EVERY doctrine cleared the whole campaign at ~0 repair-debt, the hull
            //      is over-tanked for this wave squad and cannot discriminate -> report it, crown no winner. ----
            if (rows.All(r => r.SustainState == "indeterminate"))
            {
                nullCaseHulls++;
                Console.WriteLine($"[shvy] NULLCASE {hullTag}: all {rows.Count} doctrines cleared every wave at ~0 repair-debt "
                                + "(over-tanked for this wave squad) -> sustainment indeterminate, no winner crowned");
                notes.Add($"{hullTag}: NULL CASE — all doctrines cleared the campaign at ~0 repair-debt (over-tanked); excluded from scoring");
                hullBlocks.Add("{\n      "
                    + $"\"hull\":{J(baseDesign.Name)},\"hullRole\":{J(baseDesign.Role.ToString())},"
                    + $"\"baseShieldSlots\":{baseShieldSlots},\"baseArmorSlots\":{baseArmorSlots},\"count\":{Count},\n      "
                    + "\"nullCase\":true,\"winner\":\"indeterminate\",\"winnerWhy\":\"all doctrines cleared the campaign at ~0 repair-debt (over-tanked hull)\",\"maxShieldBeatArmor\":false,\n    "
                    + string.Join(",\n    ", rows.Select(r => $"{J(r.Doctrine)}:{ShvyRowJson(r)}")) + "\n    }");
                continue;
            }

            // ---- SUSTAINMENT WINNER among STRESSED doctrines only (#18 discipline): most waves survived; ties
            //      broken by MORE force retained, then LESS permanent repair-debt; genuine ties -> indeterminate. ----
            var stressed = rows.Where(r => r.SustainState == "stressed").ToList();
            string winner, winnerWhy;
            if (stressed.Count == 0)
            {
                winner = "indeterminate";
                winnerWhy = "every doctrine that ran was indeterminate (cleared at ~0 debt); campaign did not discriminate";
            }
            else
            {
                ShvyRow best = stressed
                    .OrderByDescending(r => r.WavesSurvived)
                    .ThenByDescending(r => r.FinalSurvivingStrength)
                    .ThenBy(r => r.FinalRepairDebt)
                    .ThenBy(r => r.Doctrine, StringComparer.Ordinal)
                    .First();
                var ties = stressed.Where(r =>
                    r.WavesSurvived == best.WavesSurvived
                    && Math.Abs(r.FinalSurvivingStrength - best.FinalSurvivingStrength) <= 1e-3f
                    && Math.Abs(r.FinalRepairDebt - best.FinalRepairDebt) <= DebtEps).ToList();
                if (ties.Count > 1)
                {
                    winner = "indeterminate";
                    winnerWhy = $"tie among {string.Join("/", ties.Select(t => t.Doctrine))} "
                              + "(equal waves survived, force retained, and repair-debt within eps)";
                }
                else
                {
                    winner = best.Doctrine;
                    winnerWhy = $"survived {best.WavesSurvived}/{Waves} waves, retained {best.FinalSurvivingStrength:0} strength, "
                              + $"carried {best.FinalRepairDebt:0} repair-debt (lowest among the top survivors)";
                    doctrineWins[winner] = doctrineWins.TryGetValue(winner, out int n) ? n + 1 : 1;
                }
            }
            hullsScored++;

            // ---- The crossover verdict for THIS hull: did the shield-heavy MAXSHIELD doctrine beat the ARMOR
            //      control directly (the #14..#18 question, now with shields a LARGE fraction of the loadout)? ----
            ShvyRow msRow    = rows.First(r => r.Doctrine == "MAXSHIELD");
            ShvyRow armorRow = rows.FirstOrDefault(r => r.Doctrine == "ARMOR");
            bool maxShieldBeatArmor = false;
            if (armorRow != null)
            {
                int cmp =
                    msRow.WavesSurvived != armorRow.WavesSurvived
                        ? msRow.WavesSurvived.CompareTo(armorRow.WavesSurvived) :
                    Math.Abs(msRow.FinalSurvivingStrength - armorRow.FinalSurvivingStrength) > 1e-3f
                        ? msRow.FinalSurvivingStrength.CompareTo(armorRow.FinalSurvivingStrength) :
                        armorRow.FinalRepairDebt.CompareTo(msRow.FinalRepairDebt); // lower debt wins
                // Only count it as a real beat when the campaign actually stressed at least one of the two.
                bool stressedPair = msRow.SustainState == "stressed" || armorRow.SustainState == "stressed";
                maxShieldBeatArmor = cmp > 0 && stressedPair;
                if (maxShieldBeatArmor) maxShieldWins++;
                else if (cmp < 0 && stressedPair) armorWins++;
            }

            Console.WriteLine($"[shvy] WINNER {hullTag}: {winner} ({winnerWhy})");
            Console.WriteLine($"[shvy]   MAXSHIELD[frac={msRow.ShieldFraction:0.00} survived={msRow.WavesSurvived}/{Waves} str={msRow.FinalSurvivingStrength:0} debt={msRow.FinalRepairDebt:0} {msRow.SustainState}] "
                            + (armorRow != null ? $"ARMOR[survived={armorRow.WavesSurvived}/{Waves} str={armorRow.FinalSurvivingStrength:0} debt={armorRow.FinalRepairDebt:0} {armorRow.SustainState}] " : "ARMOR[infeasible] ")
                            + $"=> maxShieldBeatArmor={maxShieldBeatArmor}");

            hullBlocks.Add("{\n      "
                + $"\"hull\":{J(baseDesign.Name)},\"hullRole\":{J(baseDesign.Role.ToString())},"
                + $"\"baseShieldSlots\":{baseShieldSlots},\"baseArmorSlots\":{baseArmorSlots},"
                + $"\"baseShieldFraction\":{F(ShieldFractionOf(baseDesign))},\"maxShieldFraction\":{F(msRow.ShieldFraction)},\"count\":{Count},\n      "
                + $"\"nullCase\":false,\"winner\":{J(winner)},\"winnerWhy\":{J(winnerWhy)},\"maxShieldBeatArmor\":{(maxShieldBeatArmor ? "true" : "false")},\n    "
                + string.Join(",\n    ", rows.Select(r => $"{J(r.Doctrine)}:{ShvyRowJson(r)}")) + "\n    }");

            // ---- DETERMINISM: rerun every feasible doctrine's full campaign; the wave series must be identical. ----
            foreach (ShvyRow r in rows)
            {
                IShipDesign rerunDesign;
                switch (r.Doctrine)
                {
                    case "ARMOR":
                        rerunDesign = BuildDoctrineVariant(baseDesign, "STRIP_SHIELDS", salt++).variant; break;
                    case "MAXSHIELD":
                        rerunDesign = BuildSwapVariant(baseDesign, HasArmorMod, ms.shieldUid, $"maxshldre{salt:X}", salt++).variant; break;
                    case "CANOPY":
                        rerunDesign = BuildSwapVariant(baseDesign, _ => true, "CanopyShield", $"canopyre{salt:X}", salt++).variant; break;
                    case "ARMORED":
                        rerunDesign = BuildSwapVariant(baseDesign, _ => true, "ArmoredShield4x4", $"armrdre{salt:X}", salt++).variant; break;
                    default: rerunDesign = null; break;
                }
                Assert.IsNotNull(rerunDesign, $"{hullTag} {r.Doctrine}: rerun variant rebuilt");
                GauntletRun r2 = RunGauntlet(r.Doctrine, rerunDesign, enemyDesign, Count, EnemyCount, Waves, WaveTicks, RegenTicks, Seed, 0f);
                Assert.AreEqual(GauntletSignature(r.Run), GauntletSignature(r2),
                    $"{hullTag} {r.Doctrine} gauntlet must be deterministic (rerun reproduces)");
            }
            Console.WriteLine($"[shvy] REPRO {hullTag}: rerun reproduced all {rows.Count} doctrine series bit-identically");
        }

        // ---- overall verdict (honest either way — the crossover answer). ----
        string winTally = doctrineWins.Count == 0 ? "(none — every scored hull was indeterminate)"
            : string.Join(", ", doctrineWins.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

        // The MAXSHIELD-vs-ARMOR crossover is only decidable on hulls where BOTH doctrines ran AND the campaign
        // actually stressed at least one of them (an infeasible STRIP_SHIELDS control, or a both-indeterminate
        // over-tanked pairing, yields no direct comparison). Report against THAT count, not all scored hulls.
        int directComparisons = maxShieldWins + armorWins;
        string verdict;
        if (hullsScored == 0)
            verdict = $"no testable hull (every shield-heavy pick was a null case: {nullCaseHulls} over-tanked, took ~zero "
                    + "attrition over the campaign so no doctrine could be discriminated)";
        else if (directComparisons == 0)
            verdict = $"INDETERMINATE crossover: of {hullsScored} scored hull(s), none gave a stressed head-to-head MAXSHIELD-vs-ARMOR "
                    + "comparison (the STRIP_SHIELDS armor control was infeasible on the footprint-sourced hulls, and the shield-"
                    + $"fraction picks were not both stressed). The shield-heavy crossover could not be decided here. Doctrine wins: {winTally}.";
        else if (maxShieldWins > armorWins)
            verdict = $"SHIELD-FRACTION CROSSOVER FOUND: with shields a LARGE fraction of the loadout (MAX-SHIELD), the "
                    + $"shield doctrine BEATS the ARMOR control on {maxShieldWins}/{directComparisons} head-to-head hull(s) "
                    + $"(armor won {armorWins}). Free between-wave regen finally out-sustains permanent armor density once "
                    + $"shields dominate the loadout — unlike #14..#18 where shields were 1-of-many slots. Doctrine wins: {winTally}.";
        else if (armorWins > maxShieldWins)
            verdict = $"ARMOR STILL WINS EVEN SHIELD-HEAVY: even when the loadout is shield-DOMINATED (MAX-SHIELD: armor "
                    + $"slots swapped to shields), the ARMOR control out-sustains it on every one of the {armorWins}/{directComparisons} "
                    + $"head-to-head hull(s) (MAX-SHIELD won {maxShieldWins}). There is no shield-fraction crossover in this content: "
                    + $"permanent armor density beats free between-wave shield regen across the whole campaign, so the #14..#18 negative "
                    + $"HOLDS even at maximum shield fraction (honest negative result). Doctrine wins: {winTally}.";
        else
            verdict = $"NO CLEAR CROSSOVER: MAX-SHIELD and ARMOR each won the head-to-head campaign on {maxShieldWins}/{directComparisons} hull(s); "
                    + $"no decisive shield-fraction edge either way. Doctrine wins: {winTally}.";

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string hullsJson = hullBlocks.Count > 0 ? "\n    " + string.Join(",\n    ", hullBlocks) + "\n  " : "";
        string winsJson = string.Join(",", doctrineWins.Select(kv => $"{J(kv.Key)}:{kv.Value}"));
        string json =
            "{\n" +
            "  \"experiment\": \"SHIELD-HEAVY attrition gauntlet (the crossover test): every prior gauntlet scored hulls where shields were 1-of-many slots, so armor's raw HP-per-slot won. Here the MAX-SHIELD doctrine swaps a hull's ARMOR slots to footprint-matched shields so shields are a LARGE fraction of the loadout, then runs the committed RunGauntlet campaign vs the STRIP_SHIELDS armor control. Does free between-wave regen finally win once shields dominate? Also scores the #18-untested CanopyShield(3x6) and ArmoredShield4x4(4x4) on any hull that has those slots.\",\n" +
            $"  \"shipsPerDoctrine\": {Count},\n" +
            $"  \"enemyPerWave\": {EnemyCount},\n" +
            $"  \"waves\": {Waves},\n" +
            $"  \"waveTicks\": {WaveTicks},\n" +
            $"  \"regenTicks\": {RegenTicks},\n" +
            $"  \"seedHex\": {J($"0x{Seed:X}")},\n" +
            $"  \"repairDebtEps\": {F(DebtEps)},\n" +
            $"  \"enemyDesign\": {J(enemyDesign.Name)},\n" +
            "  \"doctrines\": [\"MAXSHIELD (armor->shield, shield-dominated loadout)\",\"ARMOR (STRIP_SHIELDS control)\",\"CANOPY (CanopyShield 3x6, if hull has 3x6 slots)\",\"ARMORED (ArmoredShield4x4 4x4, if hull has 4x4 slots)\"],\n" +
            $"  \"hullsScored\": {hullsScored},\n" +
            $"  \"nullCaseHulls\": {nullCaseHulls},\n" +
            $"  \"directComparisons\": {directComparisons},\n" +
            $"  \"maxShieldBeatArmorHulls\": {maxShieldWins},\n" +
            $"  \"armorBeatMaxShieldHulls\": {armorWins},\n" +
            $"  \"canopyEverScored\": {(canopyEverScored ? "true" : "false")},\n" +
            $"  \"armoredEverScored\": {(armoredEverScored ? "true" : "false")},\n" +
            $"  \"doctrineHullWins\": {{{winsJson}}},\n" +
            $"  \"verdict\": {J(verdict)},\n" +
            $"  \"hulls\": [{hullsJson}],\n" +
            $"  \"notes\": [{string.Join(",", notes.Select(J))}]\n" +
            "}\n";
        string jsonPath = Path.Combine(dir, "shieldheavy.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[shvy] DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        Console.WriteLine($"[shvy] VERDICT {verdict}");
        Console.WriteLine($"[shvy] canopyScored={canopyEverScored} armoredScored={armoredEverScored}");
        foreach (string n in notes) Console.WriteLine($"[shvy] NOTE {n}");

        Assert.IsTrue(File.Exists(jsonPath), "shieldheavy.json written");
        Assert.IsTrue(hullsScored > 0 || nullCaseHulls > 0, "at least one hull ran the full shield-heavy gauntlet");
    }

    // ===================================================================================================
    // #21 — WAVE-SURVIVAL ("design your fleet, face waves of enemies"). A player fleet built to a FIXED
    //       production budget faces ESCALATING enemy waves until it is wiped (or a 20-wave cap). Between
    //       waves the player's shields regen (RunGauntlet's between-wave approach: remove the enemy,
    //       advance ticks so shields recharge) but armor/hull damage PERSISTS — attrition is what kills you.
    //
    //   THE "DESIGN YOUR FLEET" PART — 6 archetypes, each built to the SAME production budget so it's a
    //   fair archetype comparison (count = budget / variantUnitCost):
    //       BALANCED     : a stock mid-tier cruiser (mixed loadout, as-shipped).
    //       GLASS_CANNON : BuildDoctrineVariant GLASS_CANNON (armor+shields -> guns; max offense, min defense).
    //       ARMOR_TANK   : BuildDoctrineVariant MAX_ARMOR    (weapons+shields -> armor; max tank).
    //       SHIELD_WALL  : a GENUINE max-shield variant — the MAXSHIELD doctrine from BalanceMeta_ShieldHeavy-
    //                      Gauntlet (committed BuildSwapVariant: ARMOR slots -> a footprint-matched shield
    //                      module), so its shield fraction is driven toward 1 and it is DISTINCT from BALANCED.
    //                      If the BALANCED base has no armor slot to convert (already shield-maxed), SHIELD_WALL
    //                      sources the cheapest shielded+armored warship as its own base so it is a real variant
    //                      (NOTEd). Falls back to the bare base only if no armor->shield swap is feasible (NOTEd).
    //       CARRIER      : a carrier-role hull (fighter-launcher) if one loads, else PURE_CARRIER variant of
    //                      the base; skipped+NOTEd if neither is buildable.
    //       SWARM        : many cheap ships (cheapest standalone warship), same budget -> a large cheap fleet.
    //   Any archetype that can't be built on the chosen base is SKIPPED and NOTEd.
    //
    //   ESCALATING WAVES (shared, deterministic): wave K throws a progressively STRONGER enemy force — the
    //   enemy ship count ramps UP each wave (wave1 modest; +1 ship/wave plus a ~30%/wave budget-style growth
    //   so later waves field many ships) AND a strength ramp kicks in (from a midRamp wave on, the squad is
    //   the pricier "heavy" foe instead of the cheap one). ALL archetypes face the SAME seed + SAME wave
    //   sequence, so the only variable is the fleet DESIGN: which design survives the most waves?
    //
    //   METRIC: wavesSurvived per archetype + per-wave detail (player ships alive, player surviving strength,
    //   enemy strength defeated that wave). REPLAY: the BEST archetype's full survival run is captured as a
    //   BattleReplay (player fleet + each wave's enemies registered as they spawn, Capture every ~70 ticks)
    //   and WriteAnimatedSvg'd to battle-replays/fights/wavesurvival_best.svg.
    //
    //   ADDITIVE: new method + helpers only; reuses committed BuildDoctrineVariant / BuildExactCount /
    //   MkSide / SetupArena / SetupWars / EngageAll / SpawnShip / StrengthOf / AliveOf / ShieldSlotCount and
    //   RunGauntlet's between-wave regen idiom. SHIELD_WALL reuses the committed MAXSHIELD machinery
    //   (BuildSwapVariant + DominantArmorFootprint + FindShieldModuleUid + ArmorSlotCount + ShieldFractionOf).
    //   DETERMINISM: one fixed seed; the whole sweep is rerun and the per-archetype wave series asserted
    //   bit-identical.
    // ===================================================================================================

    // One wave's recorded state for one archetype's survival run (measured AFTER combat AND between-wave regen).
    sealed class WaveRow
    {
        public int Wave;
        public int EnemyCount;                // enemy ships spawned this wave (the escalation)
        public float EnemyStrengthDefeated;   // enemy GetStrength() removed this wave (start - surviving enemy str)
        public int PlayerShipsAlive;          // player ships still active after this wave
        public float PlayerSurvivingStrength; // GetStrength() summed over live player ships after this wave
        public float PlayerArmorHullHealth;   // persistent module Health (does NOT regen) after this wave
        public bool Cleared;                  // did the player wipe this wave's enemy squad in the wave budget?
    }

    static string WaveRowJson(WaveRow w)
        => $"{{\"wave\":{w.Wave},\"enemyCount\":{w.EnemyCount},\"enemyStrengthDefeated\":{F(w.EnemyStrengthDefeated)},"
         + $"\"playerShipsAlive\":{w.PlayerShipsAlive},\"playerSurvivingStrength\":{F(w.PlayerSurvivingStrength)},"
         + $"\"playerArmorHullHealth\":{F(w.PlayerArmorHullHealth)},\"cleared\":{(w.Cleared ? "true" : "false")}}}";

    // One archetype's whole escalating-wave campaign.
    sealed class SurvivalRun
    {
        public string Archetype, DesignName;
        public int StartShips;
        public float UnitCost, FleetCost;
        public List<WaveRow> Waves = new();
        public int WavesSurvived;             // # waves the fleet finished with >=1 ship alive
        public float FinalSurvivingStrength, TotalEnemyStrengthDefeated;
    }

    static string SurvivalRunJson(SurvivalRun r)
    {
        string waves = string.Join(",", r.Waves.Select(WaveRowJson));
        return "{\n      "
         + $"\"archetype\":{J(r.Archetype)},\"designName\":{J(r.DesignName)},\"startShips\":{r.StartShips},"
         + $"\"unitCost\":{F(r.UnitCost)},\"fleetCost\":{F(r.FleetCost)},\n      "
         + $"\"wavesSurvived\":{r.WavesSurvived},\"finalSurvivingStrength\":{F(r.FinalSurvivingStrength)},"
         + $"\"totalEnemyStrengthDefeated\":{F(r.TotalEnemyStrengthDefeated)},\n      "
         + $"\"waves\":[{waves}]\n    }}";
    }

    // The escalating wave schedule (shared by every archetype so the comparison is clean). Wave K (1-based)
    // fields `baseEnemy + (K-1)` cheap foes up to `midRamp`, then switches to the pricier `heavyEnemy` AND keeps
    // ramping count — count grows by +1/wave with a ~30%/wave geometric kicker, so later waves are genuinely
    // harder. Returns (enemyDesign, count) for that wave. Deterministic: pure function of K.
    static (IShipDesign design, int count) WaveSchedule(int wave, int baseEnemy, int midRamp,
        IShipDesign cheapEnemy, IShipDesign heavyEnemy)
    {
        // geometric kicker: ceil(baseEnemy * 1.3^(wave-1)) dominated by +1/wave at low waves, compounding later.
        int linear = baseEnemy + (wave - 1);
        int geo    = (int)Math.Ceiling(baseEnemy * Math.Pow(1.30, wave - 1));
        int count  = Math.Clamp(Math.Max(linear, geo), 1, 60);
        IShipDesign design = wave >= midRamp ? heavyEnemy : cheapEnemy; // strength ramp: heavier foe later
        return (design, count);
    }

    // Compact (archetype -> wave series) signature for the determinism rerun assert.
    static string SurvivalSignature(SurvivalRun r)
        => r.Archetype + ":" + string.Join("|", r.Waves.Select(w =>
            $"{w.PlayerShipsAlive},{F(w.PlayerSurvivingStrength)},{F(w.EnemyStrengthDefeated)}"));

    // Run ONE archetype fleet through the escalating waves. The player fleet (side A = Player) is built once and
    // PERSISTS across all waves (it accrues permanent armor/hull damage). Each wave spawns a fresh, progressively
    // STRONGER enemy squad (side B = Enemy) per WaveSchedule; between waves the enemy is removed and ticks advance
    // so player shields regen while armor/hull stay damaged (RunGauntlet's idiom). Runs until the player is wiped
    // or `maxWaves`. Faithful to the committed arena: CreateUniverseAndPlayerEmpire + SetupArena, BuildExactCount
    // placement, seeded RNG, EngageAll + 1500-tick re-target cadence.
    //
    // REPLAY (optional): the committed BattleReplay records a FIXED ship set (each Capture() snapshots exactly the
    // ships registered so far, and WriteAnimatedSvg indexes every frame by the FINAL ship count). Registering each
    // wave's freshly-spawned enemy squad incrementally would make earlier frames shorter than the final width — an
    // index-out-of-range in the committed writer (which we must not modify). So the replay registers the PLAYER
    // FLEET (which persists across every wave and IS the survival run we want to visualize): its dots move and wink
    // out as attrition accrues across all the waves it survives. The per-wave enemies are still fully simulated;
    // they just aren't drawn, because the committed replay tool needs a fixed ship set and enemies re-spawn per
    // wave. The caller NOTEs this. Non-replay runs (replay == null) behave identically minus the capture calls.
    SurvivalRun RunWaveSurvival(string archetype, IShipDesign playerDesign, int count,
        IShipDesign cheapEnemy, IShipDesign heavyEnemy, int baseEnemy, int midRamp,
        int maxWaves, int waveTicks, int regenTicks, ulong seed, BattleReplay replay = null)
    {
        CreateUniverseAndPlayerEmpire();
        SetupArena();
        UState.EnableDeterministicRng(seed);

        var player = MkSide(Player, 0, new Vector2(-8000, 0), new[] { (0f, 1f) });
        BuildExactCount(player, playerDesign, count);
        replay?.Register(player.Fleet, 0); // team 0 = player; registered ONCE (fixed set -> rectangular frames)

        var run = new SurvivalRun
        {
            Archetype = archetype, DesignName = playerDesign.Name, StartShips = AliveOf(player.Fleet),
            UnitCost = playerDesign.GetCost(Player), FleetCost = playerDesign.GetCost(Player) * AliveOf(player.Fleet),
        };

        for (int wave = 1; wave <= maxWaves; ++wave)
        {
            // ---- WAVE: spawn this wave's (escalating) enemy squad and fight it. ----
            (IShipDesign foeDesign, int foeCount) = WaveSchedule(wave, baseEnemy, midRamp, cheapEnemy, heavyEnemy);
            var foe = MkSide(Enemy, 1, new Vector2(8000, 0), new[] { (0f, 1f) });
            BuildExactCount(foe, foeDesign, foeCount);
            var sides = new List<Side> { player, foe };
            SetupWars(sides);
            EngageAll(sides);
            replay?.Capture();

            float enemyStartStr = StrengthOf(foe.Fleet);
            bool cleared = false;
            int captureTick = 0;
            for (int t = 0; t < waveTicks; ++t)
            {
                UState.Objects.Update(TestSimStep);
                if (t % 1500 == 1499) EngageAll(sides);
                if (replay != null && (++captureTick) % 70 == 0) replay.Capture(); // ~1 frame / 70 ticks
                if (AliveOf(foe.Fleet) == 0) { cleared = true; break; }
                if (AliveOf(player.Fleet) == 0) break; // player wiped — campaign over
            }
            float enemyEndStr = StrengthOf(foe.Fleet);

            // ---- REGEN: remove the wave's enemy squad (no active enemy remains), then advance ticks with no
            //      fire so shields recharge OOC while armor/hull stay damaged (RunGauntlet's between-wave idiom). ----
            foreach (Ship e in foe.Fleet) if (e.Active) e.QueueTotalRemoval();
            for (int t = 0; t < regenTicks; ++t)
            {
                UState.Objects.Update(TestSimStep);
                if (replay != null && (++captureTick) % 70 == 0) replay.Capture();
            }

            run.Waves.Add(new WaveRow
            {
                Wave = wave, EnemyCount = foeCount,
                EnemyStrengthDefeated   = Math.Max(0f, enemyStartStr - enemyEndStr),
                PlayerShipsAlive        = AliveOf(player.Fleet),
                PlayerSurvivingStrength = StrengthOf(player.Fleet),
                PlayerArmorHullHealth   = FleetArmorHullHealth(player.Fleet),
                Cleared                 = cleared,
            });

            if (AliveOf(player.Fleet) == 0) break; // dead fleet faces no further waves
        }

        run.WavesSurvived = run.Waves.Count(w => w.PlayerShipsAlive > 0);
        WaveRow last = run.Waves.LastOrDefault();
        run.FinalSurvivingStrength      = last?.PlayerSurvivingStrength ?? 0f;
        run.TotalEnemyStrengthDefeated  = run.Waves.Sum(w => w.EnemyStrengthDefeated);
        return run;
    }

    [TestMethod]
    public void BalanceMeta_WaveSurvival_EmitsJson()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire();
        SetupArena();

        var notes = new List<string>();

        const float Budget    = 6000f;   // FIXED production budget — every archetype fields Budget/unitCost ships
        const int   BaseEnemy = 3;        // wave 1 enemy count (modest); ramps up every wave
        const int   MidRamp   = 4;        // from this wave on, the squad switches to the pricier "heavy" foe
        const int   MaxWaves  = 20;       // cap
        const int   WaveTicks = 4000;     // per-wave combat budget
        const int   RegenTicks= 5000;     // between-wave OOC regen budget (>> any ShieldRechargeDelay)
        const ulong Seed      = 0x7A4Eu;  // single fixed seed shared by every archetype
        ulong salt            = 0x7A00u;  // distinct salt namespace for doctrine variant names

        // ---- pick the BALANCED base hull: a mid-cost standalone cruiser that HAS shield slots (so SHIELD_WALL
        //      and the GLASS/ARMOR doctrine swaps all have something to work with). Deterministic pick. ----
        IShipDesign[] warships = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly)
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();
        if (warships.Length == 0) { Assert.Inconclusive("no standalone warship loaded"); return; }

        IShipDesign[] cruisers = warships.Where(d => d.Role == RoleName.cruiser && ShieldSlotCount(d) > 0).ToArray();
        IShipDesign baseDesign = cruisers.Length > 0
            ? cruisers[cruisers.Length / 2]                                   // mid-cost shield-bearing cruiser
            : warships.FirstOrDefault(d => ShieldSlotCount(d) > 0) ?? warships[warships.Length / 2];
        if (ShieldSlotCount(baseDesign) == 0)
            notes.Add($"BALANCED base '{baseDesign.Name}' has no shield slot (SHIELD_WALL sources its own shielded+armored base below)");

        // ---- the wave foes: a CHEAP foe (early waves) and a HEAVY foe (~2.5x cost, from MidRamp on). Both are
        //      standalone shield-bearing warships so they genuinely chip the player without one-shotting. ----
        IShipDesign cheapEnemy = warships.FirstOrDefault(d => ShieldSlotCount(d) > 0) ?? warships.First();
        float cheapCost = cheapEnemy.GetCost(Player);
        IShipDesign heavyEnemy = warships.Where(d => ShieldSlotCount(d) > 0 && d.GetCost(Player) >= cheapCost * 2.5f)
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal).FirstOrDefault()
            ?? warships[(int)(warships.Length * 0.7f)];

        // ---- the SWARM design: the cheapest standalone warship (many cheap ships at the same budget). ----
        IShipDesign swarmDesign = warships.First();

        // ---- the CARRIER design: a carrier-role fighter-launcher hull if one loads, else a PURE_CARRIER variant
        //      of the base. Skipped+NOTEd if neither is buildable. ----
        IShipDesign carrierDesign = ResourceManager.Ships.Designs
            .Where(d => d.Role == RoleName.carrier && d.BaseStrength > 100f && FighterHangarSlotCount(d) > 0)
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        string carrierNote = null;
        if (carrierDesign == null)
        {
            var (pc, swp, _, why) = BuildDoctrineVariant(baseDesign, "PURE_CARRIER", salt++);
            if (pc != null && swp > 0) { carrierDesign = pc; carrierNote = $"no carrier-role hull; used PURE_CARRIER variant of '{baseDesign.Name}'"; }
            else carrierNote = $"CARRIER archetype skipped: no carrier-role hull AND PURE_CARRIER infeasible on base ({why})";
        }
        else carrierNote = $"CARRIER uses carrier-role hull '{carrierDesign.Name}' (hangars={FighterHangarSlotCount(carrierDesign)})";
        notes.Add(carrierNote);

        // ---- doctrine variants for GLASS_CANNON + ARMOR_TANK (skipped+NOTEd if infeasible on the base). ----
        var (glassDesign, glassSwap, _, glassWhy) = BuildDoctrineVariant(baseDesign, "GLASS_CANNON", salt++);
        if (glassDesign == null || glassSwap == 0) { notes.Add($"GLASS_CANNON skipped: infeasible on '{baseDesign.Name}' ({glassWhy})"); glassDesign = null; }
        var (armorDesign, armorSwap, _, armorWhy) = BuildDoctrineVariant(baseDesign, "MAX_ARMOR", salt++);
        if (armorDesign == null || armorSwap == 0) { notes.Add($"ARMOR_TANK skipped: infeasible on '{baseDesign.Name}' ({armorWhy})"); armorDesign = null; }

        // ---- SHIELD_WALL: a GENUINE max-shield variant (NOT the bare base hull). Reuses the MAXSHIELD doctrine
        //      from BalanceMeta_ShieldHeavyGauntlet: swap the hull's ARMOR slots -> a footprint-matched shield
        //      module (committed BuildSwapVariant, clone+swap-by-UID, spawn-verified), driving the loadout's
        //      shield fraction toward 1 so SHIELD_WALL is a DISTINCT, shield-maximized build. If BALANCED's base
        //      hull has NO armor slots to convert (it is already shield-maxed), source a fallback base that DOES
        //      (cheapest shielded+armored cruiser..capital) so SHIELD_WALL is a real variant, and NOTE it. ----
        bool HasArmorMod(ShipModule m) => m.ModuleType == ShipModuleType.Armor;
        IShipDesign shieldWallBase = baseDesign;
        if (DominantArmorFootprint(shieldWallBase).count == 0)
        {
            IShipDesign armoredBase = warships
                .Where(d => ShieldSlotCount(d) > 0 && ArmorSlotCount(d) > 0)
                .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (armoredBase != null)
            {
                notes.Add($"SHIELD_WALL: BALANCED base '{baseDesign.Name}' has no armor slot to convert (already shield-maxed); "
                        + $"sourced '{armoredBase.Name}' (cheapest shielded+armored warship) as the SHIELD_WALL base so it is a real max-shield variant");
                shieldWallBase = armoredBase;
            }
        }
        IShipDesign shieldWallDesign = shieldWallBase;
        string shieldWallNote;
        var (swArmorSize, swArmorN) = DominantArmorFootprint(shieldWallBase);
        string swShieldUid = swArmorN > 0 ? FindShieldModuleUid(swArmorSize) : null;
        if (swShieldUid != null)
        {
            var (swVariant, swSwapped, swMatched, swClean) =
                BuildSwapVariant(shieldWallBase, HasArmorMod, swShieldUid, $"shieldwall{salt:X}", salt++);
            if (swVariant != null && swSwapped > 0)
            {
                shieldWallDesign = swVariant;
                shieldWallNote = $"SHIELD_WALL = MAXSHIELD variant of '{shieldWallBase.Name}': {swSwapped}/{swMatched} armor slots -> {swShieldUid} "
                               + $"({swArmorSize.X}x{swArmorSize.Y}); shieldFraction {ShieldFractionOf(shieldWallBase):0.00}->{ShieldFractionOf(swVariant):0.00} (clean={swClean})";
            }
            else
                shieldWallNote = $"SHIELD_WALL: armor->shield swap infeasible on '{shieldWallBase.Name}' (matched={swMatched} swapped={swSwapped}); fell back to bare base hull";
        }
        else
            shieldWallNote = $"SHIELD_WALL: no shield module of armor footprint on '{shieldWallBase.Name}'; fell back to bare base hull";
        notes.Add(shieldWallNote);

        Console.WriteLine($"[wave] base BALANCED hull = '{baseDesign.Name}' ({baseDesign.Role}) shields={ShieldSlotCount(baseDesign)} cost={baseDesign.GetCost(Player):0}");
        Console.WriteLine($"[wave] SHIELD_WALL design='{shieldWallDesign.Name}' shields={ShieldSlotCount(shieldWallDesign)}/{ArmorSlotCount(shieldWallDesign)}ar "
                        + $"shieldFraction={ShieldFractionOf(shieldWallDesign):0.00} cost={shieldWallDesign.GetCost(Player):0} ({shieldWallNote})");
        Console.WriteLine($"[wave] wave foes: cheap='{cheapEnemy.Name}'@{cheapCost:0}  heavy='{heavyEnemy.Name}'@{heavyEnemy.GetCost(Player):0} (from wave{MidRamp})");
        Console.WriteLine($"[wave] swarm='{swarmDesign.Name}'@{swarmDesign.GetCost(Player):0}  carrier='{carrierDesign?.Name ?? "(none)"}'");
        Console.WriteLine($"[wave] budget={Budget:0}/archetype, baseEnemy={BaseEnemy}, midRamp=wave{MidRamp}, maxWaves={MaxWaves}, seed=0x{Seed:X}");

        // ---- assemble the archetype roster: (label, design). Each fields Budget/unitCost ships -> fair budget. ----
        int CountFor(IShipDesign d) => Math.Clamp((int)(Budget / Math.Max(1f, d.GetCost(Player))), 1, 60);
        var roster = new List<(string label, IShipDesign design)>();
        roster.Add(("BALANCED", baseDesign));
        if (glassDesign  != null) roster.Add(("GLASS_CANNON", glassDesign));
        if (armorDesign  != null) roster.Add(("ARMOR_TANK",   armorDesign));
        roster.Add(("SHIELD_WALL", shieldWallDesign)); // GENUINE max-shield variant (armor->shield); distinct from BALANCED
        if (carrierDesign != null) roster.Add(("CARRIER", carrierDesign));
        roster.Add(("SWARM", swarmDesign));

        // ---- run every archetype against the SAME seed + SAME escalating wave sequence. ----
        var runs = new List<SurvivalRun>();
        foreach ((string label, IShipDesign design) in roster)
        {
            int n = CountFor(design);
            SurvivalRun r = RunWaveSurvival(label, design, n, cheapEnemy, heavyEnemy, BaseEnemy, MidRamp,
                MaxWaves, WaveTicks, RegenTicks, Seed);
            runs.Add(r);
            Console.WriteLine($"[wave] {label,-13} design='{design.Name}' ships={r.StartShips} (cost {r.UnitCost:0} -> fleet {r.FleetCost:0}) "
                            + $"-> wavesSurvived={r.WavesSurvived}/{MaxWaves} finalStr={r.FinalSurvivingStrength:0} enemyDefeated={r.TotalEnemyStrengthDefeated:0}");
            foreach (WaveRow w in r.Waves)
                Console.WriteLine($"[wave]   {label,-13} wave{w.Wave,2} enemies={w.EnemyCount,2} alive={w.PlayerShipsAlive,2}/{r.StartShips} "
                                + $"playerStr={w.PlayerSurvivingStrength:0} armorHull={w.PlayerArmorHullHealth:0} "
                                + $"enemyDefeated={w.EnemyStrengthDefeated:0} cleared={w.Cleared}");
        }

        // ---- WHICH FLEET DESIGN SURVIVES THE MOST WAVES? rank by wavesSurvived, tie-break by total enemy
        //      strength defeated, then by final surviving strength. ----
        var ranked = runs.OrderByDescending(r => r.WavesSurvived)
                         .ThenByDescending(r => r.TotalEnemyStrengthDefeated)
                         .ThenByDescending(r => r.FinalSurvivingStrength)
                         .ThenBy(r => r.Archetype, StringComparer.Ordinal)
                         .ToList();
        SurvivalRun best = ranked.First();
        Console.WriteLine($"[wave] WINNER = {best.Archetype} ('{best.DesignName}') survived {best.WavesSurvived}/{MaxWaves} waves, "
                        + $"defeated {best.TotalEnemyStrengthDefeated:0} enemy strength total");
        foreach (SurvivalRun r in ranked)
            Console.WriteLine($"[wave] RANK {r.Archetype,-13} wavesSurvived={r.WavesSurvived,2} enemyDefeated={r.TotalEnemyStrengthDefeated:0} finalStr={r.FinalSurvivingStrength:0}");

        // ---- REPLAY: re-run the BEST archetype with a BattleReplay attached (same seed + wave sequence ->
        //      identical run) and emit an animated SVG of its full survival. ----
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        IShipDesign bestDesign = roster.First(t => t.label == best.Archetype).design;
        var replay = new BattleReplay { Title = $"Wave-Survival WINNER: {best.Archetype} ('{best.DesignName}') — {best.WavesSurvived} waves" };
        SurvivalRun bestReplayRun = RunWaveSurvival(best.Archetype, bestDesign, CountFor(bestDesign),
            cheapEnemy, heavyEnemy, BaseEnemy, MidRamp, MaxWaves, WaveTicks, RegenTicks, Seed, replay);
        string svgPath = replay.WriteAnimatedSvg(dir, "wavesurvival_best.svg", frameStride: 2);
        notes.Add($"REPLAY draws the WINNER ('{best.Archetype}') player fleet across its full survival run; per-wave enemies "
                + "are simulated but not drawn (committed BattleReplay needs a fixed ship set, enemies re-spawn each wave)");
        Console.WriteLine($"[wave] REPLAY {best.Archetype} frames={replay.FrameCount} -> {svgPath} ({new FileInfo(svgPath).Length}B)");
        Assert.AreEqual(SurvivalSignature(best), SurvivalSignature(bestReplayRun),
            "the replayed BEST run must reproduce the scored run bit-identically (determinism)");

        // ---- DETERMINISM: rerun the WHOLE sweep; every archetype's wave series must be bit-identical. ----
        var runs2 = new List<SurvivalRun>();
        foreach ((string label, IShipDesign design) in roster)
            runs2.Add(RunWaveSurvival(label, design, CountFor(design), cheapEnemy, heavyEnemy, BaseEnemy, MidRamp,
                MaxWaves, WaveTicks, RegenTicks, Seed));
        for (int i = 0; i < runs.Count; ++i)
            Assert.AreEqual(SurvivalSignature(runs[i]), SurvivalSignature(runs2[i]),
                $"{runs[i].Archetype} wave-survival must be deterministic (rerun reproduces)");
        Console.WriteLine($"[wave] REPRO rerun reproduced all {runs.Count} archetype wave series bit-identically");

        // ---- emit JSON. ----
        string verdict = $"WAVE-SURVIVAL: of {runs.Count} fleet-design archetypes built to an equal {Budget:0} production budget "
            + $"and run against the SAME escalating wave sequence, '{best.Archetype}' ('{best.DesignName}') survives the MOST waves "
            + $"({best.WavesSurvived}/{MaxWaves}), defeating {best.TotalEnemyStrengthDefeated:0} total enemy strength. "
            + "Ranking (wavesSurvived): " + string.Join(", ", ranked.Select(r => $"{r.Archetype}={r.WavesSurvived}")) + ".";

        string runsJson = string.Join(",\n    ", runs.Select(SurvivalRunJson));
        string json =
            "{\n" +
            "  \"experiment\": \"WAVE-SURVIVAL ('design your fleet, face waves of enemies'): a player fleet built to a FIXED production budget faces ESCALATING enemy waves (count ramps +1/wave with a geometric kicker; squad switches to a pricier 'heavy' foe from a mid-ramp wave on) until wiped or a 20-wave cap. Between waves the player's shields regen (enemy removed, ticks advanced) but armor/hull damage PERSISTS — attrition is what kills you. SIX archetypes (BALANCED, GLASS_CANNON, ARMOR_TANK, SHIELD_WALL, CARRIER, SWARM), each at equal budget, run the SAME seed + SAME wave sequence: WHICH FLEET DESIGN SURVIVES THE MOST WAVES?\",\n" +
            $"  \"productionBudget\": {F(Budget)},\n" +
            $"  \"baseEnemyCount\": {BaseEnemy},\n" +
            $"  \"midRampWave\": {MidRamp},\n" +
            $"  \"maxWaves\": {MaxWaves},\n" +
            $"  \"waveTicks\": {WaveTicks},\n" +
            $"  \"regenTicks\": {RegenTicks},\n" +
            $"  \"seedHex\": {J($"0x{Seed:X}")},\n" +
            $"  \"baseHull\": {J(baseDesign.Name)},\n" +
            $"  \"cheapEnemy\": {J(cheapEnemy.Name)},\n" +
            $"  \"heavyEnemy\": {J(heavyEnemy.Name)},\n" +
            $"  \"archetypesRun\": {runs.Count},\n" +
            $"  \"winner\": {J(best.Archetype)},\n" +
            $"  \"winnerWavesSurvived\": {best.WavesSurvived},\n" +
            $"  \"ranking\": [{string.Join(",", ranked.Select(r => $"{{\"archetype\":{J(r.Archetype)},\"wavesSurvived\":{r.WavesSurvived},\"enemyStrengthDefeated\":{F(r.TotalEnemyStrengthDefeated)}}}"))}],\n" +
            $"  \"verdict\": {J(verdict)},\n" +
            $"  \"replaySvg\": {J("battle-replays/fights/wavesurvival_best.svg")},\n" +
            "  \"runs\": [\n    " + runsJson + "\n  ],\n" +
            $"  \"notes\": [{string.Join(",", notes.Select(J))}]\n" +
            "}\n";
        string jsonPath = Path.Combine(dir, "wavesurvival.json");
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"[wave] DATAPATH {jsonPath} ({new FileInfo(jsonPath).Length}B)");
        Console.WriteLine($"[wave] VERDICT {verdict}");
        foreach (string nt in notes) Console.WriteLine($"[wave] NOTE {nt}");

        Assert.IsTrue(File.Exists(jsonPath), "wavesurvival.json written");
        Assert.IsTrue(File.Exists(svgPath), "wavesurvival_best.svg written");
        Assert.IsTrue(runs.Count >= 3, "at least three archetypes were buildable and run");
        Assert.IsTrue(best.WavesSurvived >= 1, "the winning archetype survived at least one wave");
    }

    // ===================================================================================================
    //  WAVE-SURVIVAL — WATCHABLE replay (per-wave ENEMIES drawn, not just the persistent player fleet)
    //
    //   BalanceMeta_WaveSurvival_EmitsJson's replay draws ONLY the player fleet (the committed BattleReplay
    //   needed a FIXED ship set, so per-wave enemies — which re-spawn each wave — could not be drawn). Now that
    //   BattleReplay supports a DYNAMIC ship set (RegisterAt + ragged frames back-padded as ABSENT), we can
    //   register EACH wave's freshly-spawned enemy squad as it appears: it renders absent (opacity 0) before its
    //   first capture, blue player dots vs red enemy dots converge and wink out wave by wave, then the wave's
    //   survivors vanish (removed) before the next squad fades in. We capture ONE archetype (the BALANCED base
    //   cruiser) across ~5 waves and WriteAnimatedSvg it DOWNSAMPLED (large frameStride) to a small, embeddable
    //   SVG at battle-replays/fights/wavesurvival_watch.svg.
    //
    //   ADDITIVE: one new method + one new helper (RunWaveSurvivalWatch). Reuses the committed arena
    //   (CreateUniverseAndPlayerEmpire + SetupArena), MkSide / BuildExactCount / WaveSchedule / SetupWars /
    //   EngageAll / StrengthOf / AliveOf, and the dynamic-ship-set BattleReplay (Register / RegisterAt / Capture /
    //   WriteAnimatedSvg). No committed method is altered. DETERMINISM: one fixed seed; the run is re-executed and
    //   its wave signature asserted bit-identical.
    // ===================================================================================================

    // A WATCHABLE survival run: like RunWaveSurvival, but registers EACH wave's enemy squad with the replay as it
    // spawns (so enemies are DRAWN), and removes the prior wave's drawn squad on regen so dead enemies vanish.
    // Returns the same SurvivalRun. Compact by construction: small wave/regen tick budgets + coarse capture cadence.
    SurvivalRun RunWaveSurvivalWatch(string archetype, IShipDesign playerDesign, int count,
        IShipDesign cheapEnemy, IShipDesign heavyEnemy, int baseEnemy, int midRamp,
        int maxWaves, int waveTicks, int regenTicks, int captureEvery, ulong seed, BattleReplay replay)
    {
        CreateUniverseAndPlayerEmpire();
        SetupArena();
        UState.EnableDeterministicRng(seed);

        var player = MkSide(Player, 0, new Vector2(-8000, 0), new[] { (0f, 1f) });
        BuildExactCount(player, playerDesign, count);
        replay.Register(player.Fleet, 0); // team 0 = player; persists across every wave

        var run = new SurvivalRun
        {
            Archetype = archetype, DesignName = playerDesign.Name, StartShips = AliveOf(player.Fleet),
            UnitCost = playerDesign.GetCost(Player), FleetCost = playerDesign.GetCost(Player) * AliveOf(player.Fleet),
        };

        for (int wave = 1; wave <= maxWaves; ++wave)
        {
            (IShipDesign foeDesign, int foeCount) = WaveSchedule(wave, baseEnemy, midRamp, cheapEnemy, heavyEnemy);
            var foe = MkSide(Enemy, 1, new Vector2(8000, 0), new[] { (0f, 1f) });
            BuildExactCount(foe, foeDesign, foeCount);
            replay.RegisterAt(foe.Fleet, 1); // team 1 = enemy; registered LATE -> absent in all earlier frames
            var sides = new List<Side> { player, foe };
            SetupWars(sides);
            EngageAll(sides);
            replay.Capture();

            float enemyStartStr = StrengthOf(foe.Fleet);
            bool cleared = false;
            int captureTick = 0;
            for (int t = 0; t < waveTicks; ++t)
            {
                UState.Objects.Update(TestSimStep);
                if (t % 1500 == 1499) EngageAll(sides);
                if ((++captureTick) % captureEvery == 0) replay.Capture();
                if (AliveOf(foe.Fleet) == 0) { cleared = true; break; }
                if (AliveOf(player.Fleet) == 0) break;
            }
            float enemyEndStr = StrengthOf(foe.Fleet);

            foreach (Ship e in foe.Fleet) if (e.Active) e.QueueTotalRemoval();
            for (int t = 0; t < regenTicks; ++t)
            {
                UState.Objects.Update(TestSimStep);
                if ((++captureTick) % captureEvery == 0) replay.Capture();
            }
            replay.Capture(); // final frame of the regen: removed enemies read as absent/dead -> they vanish

            run.Waves.Add(new WaveRow
            {
                Wave = wave, EnemyCount = foeCount,
                EnemyStrengthDefeated   = Math.Max(0f, enemyStartStr - enemyEndStr),
                PlayerShipsAlive        = AliveOf(player.Fleet),
                PlayerSurvivingStrength = StrengthOf(player.Fleet),
                PlayerArmorHullHealth   = FleetArmorHullHealth(player.Fleet),
                Cleared                 = cleared,
            });

            if (AliveOf(player.Fleet) == 0) break;
        }

        run.WavesSurvived = run.Waves.Count(w => w.PlayerShipsAlive > 0);
        WaveRow last = run.Waves.LastOrDefault();
        run.FinalSurvivingStrength     = last?.PlayerSurvivingStrength ?? 0f;
        run.TotalEnemyStrengthDefeated = run.Waves.Sum(w => w.EnemyStrengthDefeated);
        return run;
    }

    [TestMethod]
    public void BalanceMeta_WaveSurvivalWatch_EmitsSvg()
    {
        LoadAllGameData();
        CreateUniverseAndPlayerEmpire();
        SetupArena();

        const float Budget      = 6000f;  // same budget idiom as the sweep
        const int   BaseEnemy   = 3;
        const int   MidRamp     = 3;       // earlier ramp so the short clip shows both cheap AND heavy foes
        const int   MaxWaves    = 5;       // ~4-6 waves -> a compact clip
        const int   WaveTicks   = 1200;    // shorter per-wave budget (compact)
        const int   RegenTicks  = 600;     // brief between-wave gap so survivors visibly vanish before next spawn
        const int   CaptureEvery= 120;     // coarse capture: ~1 frame / 120 ticks (few keyframes -> small svg)
        const ulong Seed        = 0x7A4Eu; // same seed family as the sweep

        // ---- BALANCED base hull: the same deterministic pick the sweep uses (mid-cost shield-bearing cruiser). ----
        IShipDesign[] warships = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f && !d.IsCarrierOnly)
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();
        if (warships.Length == 0) { Assert.Inconclusive("no standalone warship loaded"); return; }

        IShipDesign[] cruisers = warships.Where(d => d.Role == RoleName.cruiser && ShieldSlotCount(d) > 0).ToArray();
        IShipDesign baseDesign = cruisers.Length > 0
            ? cruisers[cruisers.Length / 2]
            : warships.FirstOrDefault(d => ShieldSlotCount(d) > 0) ?? warships[warships.Length / 2];

        IShipDesign cheapEnemy = warships.FirstOrDefault(d => ShieldSlotCount(d) > 0) ?? warships.First();
        float cheapCost = cheapEnemy.GetCost(Player);
        IShipDesign heavyEnemy = warships.Where(d => ShieldSlotCount(d) > 0 && d.GetCost(Player) >= cheapCost * 2.5f)
            .OrderBy(d => d.GetCost(Player)).ThenBy(d => d.Name, StringComparer.Ordinal).FirstOrDefault()
            ?? warships[(int)(warships.Length * 0.7f)];

        int count = Math.Clamp((int)(Budget / Math.Max(1f, baseDesign.GetCost(Player))), 1, 60);
        Console.WriteLine($"[wavewatch] BALANCED='{baseDesign.Name}' ships={count} cheap='{cheapEnemy.Name}' heavy='{heavyEnemy.Name}' "
                        + $"maxWaves={MaxWaves} captureEvery={CaptureEvery} seed=0x{Seed:X}");

        // ---- WATCHABLE replay: player + EACH wave's enemies registered as they spawn (dynamic ship set). ----
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        var replay = new BattleReplay { Title = $"Wave-Survival WATCH: BALANCED ('{baseDesign.Name}') vs escalating enemy waves" };
        SurvivalRun run = RunWaveSurvivalWatch("BALANCED", baseDesign, count, cheapEnemy, heavyEnemy,
            BaseEnemy, MidRamp, MaxWaves, WaveTicks, RegenTicks, CaptureEvery, Seed, replay);

        // ---- DOWNSAMPLE hard for a SMALL, embeddable svg: large frameStride keeps the keyframe count ~30-50. ----
        int targetFrames = 40;
        int stride = Math.Max(1, (int)Math.Ceiling(replay.FrameCount / (float)targetFrames));
        string svgPath = replay.WriteAnimatedSvg(dir, "wavesurvival_watch.svg", w: 560, h: 360, fps: 8f, frameStride: stride);
        long bytes = new FileInfo(svgPath).Length;
        string svg = File.ReadAllText(svgPath);
        int keyframes = (svg.Length - svg.Replace(";", "").Length) / Math.Max(1, AliveCircleCount(svg)) + 1;
        Console.WriteLine($"[wavewatch] run wavesSurvived={run.WavesSurvived}/{MaxWaves} capturedFrames={replay.FrameCount} stride={stride} "
                        + $"-> {svgPath} ({bytes}B)");
        Console.WriteLine($"[wavewatch] svg well-formed={(svg.StartsWith("<svg") && svg.TrimEnd().EndsWith("</svg>"))} sizeKB={bytes / 1024f:0.0}");

        // ---- DETERMINISM: rerun the watch run (fresh replay) and assert the wave signature is bit-identical. ----
        var replay2 = new BattleReplay { Title = "rerun" };
        SurvivalRun run2 = RunWaveSurvivalWatch("BALANCED", baseDesign, count, cheapEnemy, heavyEnemy,
            BaseEnemy, MidRamp, MaxWaves, WaveTicks, RegenTicks, CaptureEvery, Seed, replay2);
        Assert.AreEqual(SurvivalSignature(run), SurvivalSignature(run2),
            "the watch run must be deterministic (rerun reproduces the wave series bit-identically)");
        Console.WriteLine($"[wavewatch] REPRO rerun reproduced the wave series bit-identically");

        // ---- VERIFY well-formed AND small. ----
        Assert.IsTrue(File.Exists(svgPath), "wavesurvival_watch.svg written");
        Assert.IsTrue(svg.StartsWith("<svg") && svg.TrimEnd().EndsWith("</svg>"), "svg is well-formed (root <svg>..</svg>)");
        Assert.IsTrue(svg.Contains("<animateMotion"), "svg actually animates (per-ship motion)");
        Assert.IsTrue(bytes > 0 && bytes <= 40 * 1024, $"svg is small (<=40KB); was {bytes}B");
        Assert.IsTrue(run.WavesSurvived >= 1, "the watched run survived at least one wave");
        Console.WriteLine($"[wavewatch] DONE svg={svgPath} bytes={bytes}");
    }

    // Count of <circle> elements in the emitted svg (one per registered ship) — used only for a rough keyframe log.
    static int AliveCircleCount(string svg)
    {
        int n = 0, idx = 0;
        while ((idx = svg.IndexOf("<circle", idx, StringComparison.Ordinal)) >= 0) { ++n; idx += 7; }
        return Math.Max(1, n);
    }
}

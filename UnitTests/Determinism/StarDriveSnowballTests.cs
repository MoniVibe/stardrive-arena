using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;

namespace UnitTests.Determinism;

/// <summary>
/// SNOWBALL-LEAD COMPOUNDING probe (full-game, deterministic).
///
/// Runs a small seeded galaxy with a few AI-driven major empires for a bounded number of empire turns and
/// TRACKS over time the gap between the LEADING and TRAILING empire on:
///   - ECONOMY  (credits / net planet income / planets owned)
///   - RESEARCH (techs unlocked / current-tech progress points)
///
/// QUESTION: does an early lead COMPOUND (the leader/trailer ratio widens over turns -> snowball) or
/// CONVERGE (catch-up dynamics, ratio shrinks toward 1.0)?  We quantify by sampling the leader/trailer
/// ratio at fixed turn checkpoints (~turn 25 / 50 / 100-equivalent in this bounded run) and reporting the
/// trend per metric and per seed.
///
/// Determinism: generation is seeded (CreateSeededSandbox sets GenerationSeed) and the turn RNG is seeded
/// (EnableDeterministicRng), parallel object update is OFF. A rerun of the SAME seed reproduces the series
/// bit-for-bit (verified in Snowball_Rerun_Reproducible).
///
/// SCOPE CUTS (full games are slow):
///   - galaxy = Small, 2 opponents => 3 major empires (smallest count that gives a meaningful leader/trailer
///     spread; with 1 opponent the "trailer" is just the loser of a 2-body race).
///   - 3 seeds for the divergence study; 2 seeds for the reproducibility gate (it reruns each, so 2x cost).
///   - turn checkpoints are bounded: ~9000 ticks/run (TurnTimer=1s @ 1/60 => ~150 empire turns). The
///     "turn 100" checkpoint is the deepest we sample; we do NOT run thousands of turns to a real win/loss.
///   - we do NOT vary difficulty/personality; all empires use the generated defaults, so the divergence is
///     emergent from the seeded map + AI planners, not a hand-tuned head start.
/// </summary>
[TestClass]
public class StarDriveSnowballTests : StarDriveTest
{
    // Empire-turn checkpoints we sample the leader/trailer gap at. TurnTimer=1 => ~60 ticks per empire turn.
    const int TicksPerTurn = 60;
    static readonly int[] CheckpointTurns = { 10, 25, 50, 75, 100, 150 };

    struct EmpireSnap
    {
        public string Name;
        public bool IsPlayer;
        public float Money;
        public float Income;     // NetPlanetIncomes
        public int   Planets;
        public float Pop;        // TotalPopBillion
        public int   Techs;      // UnlockedTechs.Length
        public float Progress;   // current-tech progress points (a cheap "research momentum" proxy)
    }

    struct TurnSample
    {
        public int Turn;
        public float StarDate;
        public List<EmpireSnap> Empires;

        // leader/trailer ratios per metric (>=1; 1.0 == perfectly even). Capped to avoid div-by-zero blowups.
        public float EconRatio;     // by Money (credits)
        public float IncomeRatio;   // by net income
        public float PlanetRatio;   // by planets owned
        public float TechRatio;     // by techs unlocked
    }

    static float Ratio(float lead, float trail)
    {
        // guard tiny/zero trailers: clamp trailer to a small floor and cap the ratio so a single 0 doesn't
        // produce an "infinite snowball" artifact.
        float t = Math.Max(trail, 1f);
        float l = Math.Max(lead, 0f);
        return Math.Min(l / t, 1000f);
    }

    static TurnSample Sample(int turn, float starDate, IReadOnlyList<Empire> empires)
    {
        var snaps = new List<EmpireSnap>(empires.Count);
        foreach (Empire e in empires)
        {
            snaps.Add(new EmpireSnap
            {
                Name = e.Name,
                IsPlayer = e.isPlayer,
                Money = e.Money,
                Income = e.NetPlanetIncomes,
                Planets = e.NumPlanets,
                Pop = e.TotalPopBillion,
                Techs = e.UnlockedTechs.Length,
                Progress = e.Research?.Current?.Progress ?? 0f,
            });
        }

        float maxMoney = snaps.Max(s => s.Money), minMoney = snaps.Min(s => s.Money);
        float maxInc = snaps.Max(s => s.Income), minInc = snaps.Min(s => s.Income);
        int maxPl = snaps.Max(s => s.Planets), minPl = snaps.Min(s => s.Planets);
        int maxTe = snaps.Max(s => s.Techs), minTe = snaps.Min(s => s.Techs);

        return new TurnSample
        {
            Turn = turn,
            StarDate = starDate,
            Empires = snaps,
            EconRatio = Ratio(maxMoney, minMoney),
            IncomeRatio = Ratio(maxInc, minInc),
            PlanetRatio = Ratio(maxPl, minPl),
            TechRatio = Ratio(maxTe, minTe),
        };
    }

    // Advance the seeded game and capture a sample at each checkpoint turn. Returns the series.
    // ORIGINAL behavior (committed): Small Rare-stars galaxy, 150 turns, no player expansion-timer seed.
    List<TurnSample> RunSeries(int seed)
        => RunSeries(seed, GalSize.Small, CheckpointTurns, seedPlayerExpansionTimer: false);

    // Parameterized runner used by both the original probe and the bigger-galaxy variant below.
    //   galSize / checkpoints  -> scale + depth knobs (a bigger galaxy gives empires nearby planets to grab).
    //   seedPlayerExpansionTimer -> LEGACY harness-level workaround for the colonization stall, retained as
    //     an opt-in toggle. The underlying bug is now FIXED in game code: UniverseState_Empires.AddEmpire
    //     seeds InitExpansionIntervalTimer for EVERY empire (player included), so an AI-driven player already
    //     gets a fair first expansion check. With seedPlayerExpansionTimer=false the runner relies purely on
    //     that game-code fix; passing true just re-seeds the (already-seeded) timer, an idempotent no-op-ish
    //     belt-and-suspenders kept so the original Small-galaxy probe's behavior stays bit-stable.
    List<TurnSample> RunSeries(int seed, GalSize galSize, int[] checkpoints, bool seedPlayerExpansionTimer)
    {
        CreateSeededSandbox(seed, numOpponents: 2, galSize: galSize);
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;
        UState.EnableDeterministicRng(0xC0FFEEu);
        // Hand every major empire to the AI (player included) so all three are AI-driven and comparable.
        Player.EnableAISidekick();

        if (seedPlayerExpansionTimer)
            // Seed the player's expansion-interval timer the same way AddEmpire seeds every AI empire.
            // Without this the player's first colonization check never fires within a bounded run.
            Player.AI.ExpansionAI.InitExpansionIntervalTimer(Player.Id);

        Empire[] empires = UState.MajorEmpires;
        var series = new List<TurnSample>();

        int lastTurn = checkpoints[^1];
        int nextIdx = 0;
        for (int turn = 0; turn <= lastTurn; ++turn)
        {
            if (nextIdx < checkpoints.Length && turn == checkpoints[nextIdx])
            {
                series.Add(Sample(turn, UState.StarDate, empires));
                nextIdx++;
            }
            if (turn == lastTurn) break;
            for (int i = 0; i < TicksPerTurn; ++i)
                Universe.SingleSimulationStep(TestSimStep);
        }
        return series;
    }

    [TestMethod]
    public void SnowballLead_Compounds_Or_Converges()
    {
        int[] seeds = { 101, 606, 808 };
        var perSeed = new Dictionary<int, List<TurnSample>>();

        foreach (int seed in seeds)
        {
            List<TurnSample> series = RunSeries(seed);
            perSeed[seed] = series;

            Console.WriteLine($"[snow] === seed {seed} ===");
            foreach (TurnSample s in series)
            {
                // per-empire line
                foreach (EmpireSnap e in s.Empires.OrderByDescending(x => x.Money))
                    Console.WriteLine(
                        $"[snow] seed={seed} turn={s.Turn,3} SD={s.StarDate,7:0.0} {(e.IsPlayer ? "P*" : "AI")} {e.Name,-14} " +
                        $"money={e.Money,8:0} inc={e.Income,6:0} plnts={e.Planets,2} pop={e.Pop,5:0.0} techs={e.Techs,3} prog={e.Progress,6:0}");
                // gap line (leader/trailer ratios)
                Console.WriteLine(
                    $"[snow] seed={seed} turn={s.Turn,3} GAP econ={s.EconRatio,7:0.00}x income={s.IncomeRatio,6:0.00}x " +
                    $"planets={s.PlanetRatio,5:0.00}x techs={s.TechRatio,5:0.00}x");
            }

            // Trend verdict per metric: compare the ratio at the deepest checkpoint vs the first.
            TurnSample first = series.First();
            TurnSample last = series.Last();
            Console.WriteLine(
                $"[snow] seed={seed} VERDICT econ {Verdict(first.EconRatio, last.EconRatio)} " +
                $"income {Verdict(first.IncomeRatio, last.IncomeRatio)} " +
                $"planets {Verdict(first.PlanetRatio, last.PlanetRatio)} " +
                $"techs {Verdict(first.TechRatio, last.TechRatio)}");
        }

        // Aggregate verdict across seeds: for each metric, count seeds where the gap widened (snowball)
        // vs narrowed (converge) from first->last checkpoint.
        EmitAggregate(perSeed, seeds);
        WriteJson(perSeed, seeds);

        // Assertions: the series must be non-trivial (state evolved, checkpoints captured) — the SCIENCE
        // question (snowball vs converge) is reported, not asserted, since either outcome is a valid finding.
        foreach (int seed in seeds)
        {
            Assert.AreEqual(CheckpointTurns.Length, perSeed[seed].Count, $"seed {seed} captured all checkpoints");
            TurnSample last = perSeed[seed].Last();
            Assert.IsTrue(last.Empires.Sum(e => e.Planets) > 0, $"seed {seed}: empires own planets by turn {last.Turn}");
            Assert.IsTrue(last.Empires.Sum(e => e.Techs) > 0, $"seed {seed}: empires researched by turn {last.Turn}");
        }
    }

    // Checkpoints for the bigger-galaxy variant: deeper run (Medium galaxy is slower per tick, but we need
    // more turns for natural expansion to actually fire and compound). 250 turns max keeps it under timeout.
    static readonly int[] BigCheckpointTurns = { 10, 25, 50, 100, 150, 200, 250 };

    /// <summary>
    /// VARIANT (additive) — re-runs the snowball probe on a BIGGER galaxy (Medium vs Small) for MORE turns
    /// (250 vs 150). The player's expansion-interval timer is NO LONGER seeded by the harness: the game-code
    /// fix in UniverseState_Empires.AddEmpire now seeds InitExpansionIntervalTimer for EVERY empire (player
    /// included), so an AI-driven player gets a fair first expansion check straight from production code.
    /// This test therefore CONFIRMS the fix: with seedPlayerExpansionTimer=false the AI-driven player must
    /// still expand past 2 planets purely from the game-code change. HYPOTHESIS: the original
    /// Small/150-turn/timer-unset setup was too degenerate for empires to expand past their starting ~2
    /// planets, so the "economic snowball" was really just a no-colonization artifact. Here we give empires
    /// room + time + a fair first expansion check and ask:
    ///   (1) do empires now actually COLONIZE past 2 planets?  (verified, not just reported)
    ///   (2) does the ECONOMIC snowball magnitude GROW relative to the degenerate run, or does expansion
    ///       still stall (an honest negative is itself a finding)?
    /// Emits [col] lines (per-empire planet/economy trajectory) alongside the existing [snow] gap lines.
    /// </summary>
    [TestMethod]
    public void SnowballLead_BiggerGalaxy_DoesExpansionScale()
    {
        int[] seeds = { 101, 606, 808 };
        int maxPlanetsAnyEmpire = 0;        // did ANY empire break past the ~2-planet start?
        int seedsWithExpansion = 0;         // seeds where the top empire ended with >2 planets
        var perSeed = new Dictionary<int, List<TurnSample>>();

        foreach (int seed in seeds)
        {
            // seedPlayerExpansionTimer:false -> rely ENTIRELY on the game-code fix (AddEmpire now seeds
            // the player's expansion timer). If the AI-driven player still expands, the fix works.
            List<TurnSample> series = RunSeries(seed, GalSize.Medium, BigCheckpointTurns,
                                                seedPlayerExpansionTimer: false);
            perSeed[seed] = series;

            Console.WriteLine($"[col] === seed {seed} (Medium galaxy, expansion-timer seeded) ===");
            foreach (TurnSample s in series)
            {
                // colonization/economy trajectory: one [col] line per checkpoint, leader-first.
                foreach (EmpireSnap e in s.Empires.OrderByDescending(x => x.Planets).ThenByDescending(x => x.Money))
                    Console.WriteLine(
                        $"[col] seed={seed} turn={s.Turn,3} SD={s.StarDate,7:0.0} {(e.IsPlayer ? "P*" : "AI")} {e.Name,-14} " +
                        $"plnts={e.Planets,2} pop={e.Pop,5:0.0} money={e.Money,8:0} inc={e.Income,6:0} techs={e.Techs,3}");
                // reuse the [snow] gap line so the variant is comparable to the original probe.
                Console.WriteLine(
                    $"[snow] seed={seed} turn={s.Turn,3} GAP econ={s.EconRatio,7:0.00}x income={s.IncomeRatio,6:0.00}x " +
                    $"planets={s.PlanetRatio,5:0.00}x techs={s.TechRatio,5:0.00}x");
            }

            TurnSample last = series.Last();
            int topPlanets = last.Empires.Max(e => e.Planets);
            int totalPlanets = last.Empires.Sum(e => e.Planets);
            maxPlanetsAnyEmpire = Math.Max(maxPlanetsAnyEmpire, topPlanets);
            if (topPlanets > 2) seedsWithExpansion++;

            Console.WriteLine(
                $"[col] seed={seed} FINAL turn={last.Turn} topEmpirePlanets={topPlanets} totalPlanets={totalPlanets} " +
                $"econGap={last.EconRatio:0.00}x planetGap={last.PlanetRatio:0.00}x");
            Console.WriteLine(
                $"[snow] seed={seed} VERDICT econ {Verdict(series.First().EconRatio, last.EconRatio)} " +
                $"planets {Verdict(series.First().PlanetRatio, last.PlanetRatio)} " +
                $"techs {Verdict(series.First().TechRatio, last.TechRatio)}");
        }

        EmitAggregate(perSeed, seeds);
        Console.WriteLine($"[col] EXPANSION-SCALE: max planets any empire reached = {maxPlanetsAnyEmpire}; " +
                          $"seeds where top empire expanded past 2 planets = {seedsWithExpansion}/{seeds.Length}");
        Console.WriteLine("[col] NOTE bigger-galaxy variant (Medium, 3 empires, ~250 turns, 3 seeds) — directional, not a tournament.");

        // Structural assertions (kept conservative so an HONEST stall is still a green test, not a failure):
        foreach (int seed in seeds)
        {
            Assert.AreEqual(BigCheckpointTurns.Length, perSeed[seed].Count, $"seed {seed} captured all checkpoints");
            TurnSample last = perSeed[seed].Last();
            Assert.IsTrue(last.Empires.Sum(e => e.Planets) > 0, $"seed {seed}: empires own planets by turn {last.Turn}");
            Assert.IsTrue(last.Empires.Sum(e => e.Techs) > 0, $"seed {seed}: empires researched by turn {last.Turn}");
        }
        // The POINT of the variant: with the fix + scale, at least one empire on at least one seed must
        // actually expand past the ~2-planet start. If this fails, the stall is deeper than scale/timer.
        Assert.IsTrue(maxPlanetsAnyEmpire > 2,
            $"[col] expected at least one empire to expand past 2 planets with the fix+bigger galaxy; " +
            $"max reached was {maxPlanetsAnyEmpire} (expansion still stalls — deeper cause).");
    }

    // Determinism gate for the bigger-galaxy variant: same seed reruns bit-identical (planets included).
    [TestMethod]
    public void SnowballBigGalaxy_Rerun_Reproducible()
    {
        int seed = 101;
        List<TurnSample> a = RunSeries(seed, GalSize.Medium, BigCheckpointTurns, seedPlayerExpansionTimer: true);
        List<TurnSample> b = RunSeries(seed, GalSize.Medium, BigCheckpointTurns, seedPlayerExpansionTimer: true);

        Assert.AreEqual(a.Count, b.Count, $"seed {seed}: checkpoint count differs");
        for (int i = 0; i < a.Count; ++i)
        {
            TurnSample sa = a[i], sb = b[i];
            Assert.AreEqual(sa.Empires.Count, sb.Empires.Count, $"seed {seed} turn {sa.Turn}: empire count");
            for (int j = 0; j < sa.Empires.Count; ++j)
            {
                EmpireSnap ea = sa.Empires[j], eb = sb.Empires[j];
                Assert.AreEqual(ea.Money, eb.Money, $"seed {seed} turn {sa.Turn} {ea.Name}: Money diverged");
                Assert.AreEqual(ea.Planets, eb.Planets, $"seed {seed} turn {sa.Turn} {ea.Name}: Planets diverged");
                Assert.AreEqual(ea.Techs, eb.Techs, $"seed {seed} turn {sa.Turn} {ea.Name}: Techs diverged");
            }
        }
        Console.WriteLine($"[col] seed={seed} BIG-GALAXY RERUN REPRODUCIBLE over {a.Count} checkpoints "
                        + $"(final topPlanets {a.Last().Empires.Max(e => e.Planets)} == {b.Last().Empires.Max(e => e.Planets)})");
    }

    // Determinism gate: rerun two seeds and confirm the per-turn series is bit-identical.
    [TestMethod]
    public void Snowball_Rerun_Reproducible()
    {
        int[] seeds = { 101, 606 };
        foreach (int seed in seeds)
        {
            List<TurnSample> a = RunSeries(seed);
            List<TurnSample> b = RunSeries(seed);

            Assert.AreEqual(a.Count, b.Count, $"seed {seed}: checkpoint count differs");
            for (int i = 0; i < a.Count; ++i)
            {
                TurnSample sa = a[i], sb = b[i];
                Assert.AreEqual(sa.Empires.Count, sb.Empires.Count, $"seed {seed} turn {sa.Turn}: empire count");
                for (int j = 0; j < sa.Empires.Count; ++j)
                {
                    EmpireSnap ea = sa.Empires[j], eb = sb.Empires[j];
                    Assert.AreEqual(ea.Money, eb.Money, $"seed {seed} turn {sa.Turn} {ea.Name}: Money diverged");
                    Assert.AreEqual(ea.Planets, eb.Planets, $"seed {seed} turn {sa.Turn} {ea.Name}: Planets diverged");
                    Assert.AreEqual(ea.Techs, eb.Techs, $"seed {seed} turn {sa.Turn} {ea.Name}: Techs diverged");
                    Assert.AreEqual(ea.Income, eb.Income, $"seed {seed} turn {sa.Turn} {ea.Name}: Income diverged");
                }
            }
            Console.WriteLine($"[snow] seed={seed} RERUN REPRODUCIBLE over {a.Count} checkpoints "
                            + $"(final econ gap {a.Last().EconRatio:0.00}x == {b.Last().EconRatio:0.00}x)");
        }
    }

    static string Verdict(float firstRatio, float lastRatio)
    {
        float delta = lastRatio - firstRatio;
        if (Math.Abs(delta) < 0.15f) return $"FLAT({firstRatio:0.00}->{lastRatio:0.00})";
        return delta > 0
            ? $"SNOWBALL({firstRatio:0.00}->{lastRatio:0.00},+{delta:0.00})"
            : $"CONVERGE({firstRatio:0.00}->{lastRatio:0.00},{delta:0.00})";
    }

    void EmitAggregate(Dictionary<int, List<TurnSample>> perSeed, int[] seeds)
    {
        int econSnow = 0, econConv = 0, planetSnow = 0, planetConv = 0, techSnow = 0, techConv = 0;
        foreach (int seed in seeds)
        {
            List<TurnSample> series = perSeed[seed];
            float ef = series.First().EconRatio, el = series.Last().EconRatio;
            float pf = series.First().PlanetRatio, pl = series.Last().PlanetRatio;
            float tf = series.First().TechRatio, tl = series.Last().TechRatio;
            if (el - ef > 0.15f) econSnow++; else if (ef - el > 0.15f) econConv++;
            if (pl - pf > 0.15f) planetSnow++; else if (pf - pl > 0.15f) planetConv++;
            if (tl - tf > 0.15f) techSnow++; else if (tf - tl > 0.15f) techConv++;
        }
        Console.WriteLine($"[snow] AGGREGATE over {seeds.Length} seeds (snowball|converge|flat):");
        Console.WriteLine($"[snow]   economy(credits): snowball={econSnow} converge={econConv} flat={seeds.Length - econSnow - econConv}");
        Console.WriteLine($"[snow]   planets:          snowball={planetSnow} converge={planetConv} flat={seeds.Length - planetSnow - planetConv}");
        Console.WriteLine($"[snow]   research(techs):  snowball={techSnow} converge={techConv} flat={seeds.Length - techSnow - techConv}");
        Console.WriteLine("[snow] NOTE bounded run (Small galaxy, 3 empires, ~150 turns max, 3 seeds) — directional finding, not a tournament.");
    }

    static string J(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    void WriteJson(Dictionary<int, List<TurnSample>> perSeed, int[] seeds)
    {
        string EmpJson(EmpireSnap e) =>
            "{" + $"\"name\":{J(e.Name)},\"isPlayer\":{(e.IsPlayer ? "true" : "false")}," +
            $"\"money\":{F(e.Money)},\"income\":{F(e.Income)},\"planets\":{e.Planets}," +
            $"\"pop\":{F(e.Pop)},\"techs\":{e.Techs},\"progress\":{F(e.Progress)}" + "}";

        string SampleJson(TurnSample s) =>
            "{" + $"\"turn\":{s.Turn},\"starDate\":{F(s.StarDate)}," +
            $"\"econRatio\":{F(s.EconRatio)},\"incomeRatio\":{F(s.IncomeRatio)}," +
            $"\"planetRatio\":{F(s.PlanetRatio)},\"techRatio\":{F(s.TechRatio)}," +
            $"\"empires\":[{string.Join(",", s.Empires.Select(EmpJson))}]" + "}";

        string SeedJson(int seed)
        {
            List<TurnSample> series = perSeed[seed];
            string trend(Func<TurnSample, float> sel) =>
                $"{{\"first\":{F(sel(series.First()))},\"last\":{F(sel(series.Last()))}}}";
            return "{" + $"\"seed\":{seed}," +
                $"\"trend\":{{\"econ\":{trend(x => x.EconRatio)},\"income\":{trend(x => x.IncomeRatio)}," +
                $"\"planets\":{trend(x => x.PlanetRatio)},\"techs\":{trend(x => x.TechRatio)}}}," +
                $"\"series\":[{string.Join(",", series.Select(SampleJson))}]" + "}";
        }

        string json =
            "{\n" +
            "  \"experiment\": \"snowball-lead-compounding\",\n" +
            "  \"galaxy\": \"Small\", \"majorEmpires\": 3, \"opponents\": 2,\n" +
            "  \"deterministic\": true, \"turnRngSeed\": \"0xC0FFEE\",\n" +
            $"  \"checkpointTurns\": [{string.Join(",", CheckpointTurns)}],\n" +
            $"  \"seeds\": [{string.Join(",", seeds)}],\n" +
            $"  \"perSeed\": [\n    {string.Join(",\n    ", seeds.Select(SeedJson))}\n  ]\n" +
            "}\n";

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "snowball.json");
        File.WriteAllText(path, json);
        Console.WriteLine($"[snow] DATAPATH {path} ({new FileInfo(path).Length}B)");
    }
}

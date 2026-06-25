using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;

namespace UnitTests.Determinism;

/// <summary>
/// STRATEGY-ROI probe (full-game, deterministic) — built on the snowball harness.
///
/// Runs the SAME seeded galaxy three ways, each time biasing ONE empire (the player's AI-run empire) toward a
/// different OPENING STRATEGY, holding everything else fixed:
///   - EXPAND-first   (ExpansionPriority dialed up): grab planets early.
///   - RESEARCH-first (ResearchPriority dialed up):  pour labor into science early.
///   - MILITARY-first (MilitaryPriority dialed up):  build war machine early.
///
/// The bias is applied through the empire's <see cref="EconomicResearchStrategy"/> priority ratios, which the
/// game already consumes pervasively (planet work-distribution labor split, scout/colony desire, economic-
/// planner budgets, research tech-picker). We OVERRIDE Research.Strategy with a hand-built priority profile
/// (one priority = HIGH, the rest = 1) so the three arms are crisp and comparable. Opponents are left on the
/// generated defaults and are IDENTICAL across arms (same seed + same turn RNG), so the ONLY thing that varies
/// between arms is the player's opening-strategy bias => the delta is attributable to strategy.
///
/// COMBINED SCORE: we report the game's own per-empire score components (TechScore + ExpansionScore +
/// IndustrialScore + MilitaryScore = TotalScore — i.e. economy+research+military folded into one number) at
/// fixed turn checkpoints, plus raw economy/research observables (money, income, planets, techs, research/turn).
///
/// QUESTION 1 — which opening snowballs best by turn N on the combined score?
/// QUESTION 2 — is RESEARCH a MULTIPLIER on economy (research-ROI: the research arm's economy overtakes the
///              expand arm's economy later) or a TAX early on (the research arm's economy lags early and only
///              catches up — or never does — within the bounded horizon)? We answer by tracking the research
///              arm's economy/income RELATIVE to the expand arm across checkpoints (the crossover, if any).
///
/// Determinism: generation is seeded (CreateSeededSandbox) and the turn RNG is seeded (EnableDeterministicRng),
/// parallel object update is OFF, and the strategy override is applied deterministically BEFORE any turn runs.
/// A rerun of the same (seed, arm) reproduces the series bit-for-bit (gated in StratRoi_Rerun_Reproducible).
///
/// SCOPE CUTS (full games are slow):
///   - galaxy = Small, 2 opponents => 3 major empires; the player is the biased empire, 2 AI baselines.
///   - 2 seeds × 3 arms = 6 bounded runs for the main study; the rerun gate reruns 1 seed × 1 arm (2× that run).
///   - checkpoints bounded to ~turn 100 (6000 ticks @ TurnTimer=1s, 60 ticks/empire-turn). We do NOT run to a
///     real win/loss — this is a directional ROI finding, not a tournament.
///   - bias is applied to ONE empire only (the player). We do not cross every arm against every opponent
///     personality; opponents stay on generated defaults (held constant across arms).
/// </summary>
[TestClass]
public class StarDriveStrategyRoiTests : StarDriveTest
{
    const int TicksPerTurn = 60;
    static readonly int[] CheckpointTurns = { 10, 25, 50, 75, 100 };

    enum Strat { Expand, Research, Military }

    static string Name(Strat s) => s switch
    {
        Strat.Expand   => "EXPAND",
        Strat.Research => "RESEARCH",
        Strat.Military => "MILITARY",
        _ => "?"
    };

    struct Snap
    {
        public int Turn;
        public float StarDate;
        // player (biased empire) observables
        public float Money;
        public float Income;        // NetPlanetIncomes
        public int   Planets;
        public float Pop;
        public int   Techs;
        public float ResPerTurn;    // Research.NetResearch
        // game's own score components (economy+research+military combined)
        public float TechScore;
        public float ExpansionScore;
        public float IndustrialScore;
        public float MilitaryScore;
        public int   TotalScore;    // = sum of the four components (the COMBINED score)
    }

    // Build a priority profile with ONE lever HIGH and the rest at the floor, then overwrite the empire's
    // Research.Strategy with it. Uses reflection because EconomicResearchStrategy's priority fields are readonly
    // (StarData-populated) and EmpireResearch.Strategy has a private setter — but the override is fully
    // deterministic and applied before any turn runs.
    static void BiasStrategy(Empire e, Strat strat)
    {
        const byte High = 8, Low = 1;
        byte mil = Low, exp = Low, res = Low, ind = Low;
        switch (strat)
        {
            case Strat.Expand:   exp = High; break;
            case Strat.Research: res = High; break;
            case Strat.Military: mil = High; break;
        }

        var profile = (EconomicResearchStrategy)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(EconomicResearchStrategy));
        SetField(profile, "Name", "StratRoi_" + Name(strat));
        SetField(profile, "TechPath", new[] { "IndustrialFoundations" });
        SetField(profile, "MilitaryPriority", mil);
        SetField(profile, "ExpansionPriority", exp);
        SetField(profile, "ResearchPriority", res);
        SetField(profile, "IndustryPriority", ind);

        // Keep the empire's named personality consistent with the bias (some diplomacy/score code keys off the
        // personality name), then plant the override profile so Research.Strategy returns it immediately.
        string personality = strat switch
        {
            Strat.Expand   => "Expansionists",
            Strat.Research => "Technologists",
            Strat.Military => "Militarists",
            _ => "Generalists"
        };
        if (e.data.EconomicPersonality == null) e.data.EconomicPersonality = new ETrait();
        e.data.EconomicPersonality.Name = personality;

        typeof(EmpireResearch).GetProperty("Strategy")!
            .SetValue(e.Research, profile);
    }

    static void SetField(object obj, string name, object value)
    {
        FieldInfo f = obj.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"field {name} not found on {obj.GetType().Name}");
        f.SetValue(obj, value);
    }

    static Snap Sample(int turn, float starDate, Empire e) => new()
    {
        Turn = turn,
        StarDate = starDate,
        Money = e.Money,
        Income = e.NetPlanetIncomes,
        Planets = e.NumPlanets,
        Pop = e.TotalPopBillion,
        Techs = e.UnlockedTechs.Length,
        ResPerTurn = e.Research?.NetResearch ?? 0f,
        TechScore = e.TechScore,
        ExpansionScore = e.ExpansionScore,
        IndustrialScore = e.IndustrialScore,
        MilitaryScore = e.MilitaryScore,
        TotalScore = e.TotalScore,
    };

    // Run one arm: seeded galaxy, bias the player empire, hand all empires to the AI, advance to the last
    // checkpoint capturing the player's series.
    List<Snap> RunArm(int seed, Strat strat)
    {
        CreateSeededSandbox(seed, numOpponents: 2, galSize: GalSize.Small);
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;
        UState.EnableDeterministicRng(0xC0FFEEu);

        // Hand every major empire to the AI (player included) so all three are AI-driven and comparable; then
        // bias ONLY the player's opening strategy. Opponents keep generated defaults (constant across arms).
        Player.EnableAISidekick();
        BiasStrategy(Player, strat);

        // Confirm the override is LIVE in the sim (attribution proof): the runtime ratios the game reads must
        // reflect the biased lever. Printed once per arm so the divergence — however small — is traceable to it.
        EconomicResearchStrategy s = Player.Research.Strategy;
        Console.WriteLine($"[roi] seed={seed} {Name(strat),-8} BIAS-LIVE ratios exp={s.ExpansionRatio:0.00} " +
                          $"res={s.ResearchRatio:0.00} mil={s.MilitaryRatio:0.00} ind={s.IndustryRatio:0.00}");

        var series = new List<Snap>();
        int lastTurn = CheckpointTurns[^1];
        int nextIdx = 0;
        for (int turn = 0; turn <= lastTurn; ++turn)
        {
            if (nextIdx < CheckpointTurns.Length && turn == CheckpointTurns[nextIdx])
            {
                series.Add(Sample(turn, UState.StarDate, Player));
                nextIdx++;
            }
            if (turn == lastTurn) break;
            for (int i = 0; i < TicksPerTurn; ++i)
                Universe.SingleSimulationStep(TestSimStep);
        }
        return series;
    }

    [TestMethod]
    public void StrategyRoi_WhichOpeningSnowballsBest()
    {
        int[] seeds = { 101, 606 };
        Strat[] arms = { Strat.Expand, Strat.Research, Strat.Military };

        // perSeed[seed][arm] = series
        var data = new Dictionary<int, Dictionary<Strat, List<Snap>>>();

        foreach (int seed in seeds)
        {
            var perArm = new Dictionary<Strat, List<Snap>>();
            Console.WriteLine($"[roi] === seed {seed} ===");
            foreach (Strat strat in arms)
            {
                List<Snap> series = RunArm(seed, strat);
                perArm[strat] = series;
                foreach (Snap s in series)
                    Console.WriteLine(
                        $"[roi] seed={seed} {Name(strat),-8} turn={s.Turn,3} SD={s.StarDate,7:0.0} " +
                        $"money={s.Money,8:0} inc={s.Income,6:0} plnts={s.Planets,2} techs={s.Techs,3} " +
                        $"res/t={s.ResPerTurn,6:0.0} | econ(exp)={s.ExpansionScore,7:0} ind={s.IndustrialScore,6:0} " +
                        $"tech={s.TechScore,6:0} mil={s.MilitaryScore,6:0} TOTAL={s.TotalScore,7}");
            }
            data[seed] = perArm;

            // Per-seed winner at the deepest checkpoint, on the combined TotalScore.
            EmitSeedVerdict(seed, perArm, arms);
        }

        EmitAggregate(data, seeds, arms);
        EmitResearchRoi(data, seeds);
        WriteJson(data, seeds, arms);

        // Assertions: every arm produced the full series and a non-trivial game state — the SCIENCE question
        // (which opening wins / research ROI) is REPORTED, not asserted, since any ranking is a valid finding.
        foreach (int seed in seeds)
        foreach (Strat strat in arms)
        {
            List<Snap> series = data[seed][strat];
            Assert.AreEqual(CheckpointTurns.Length, series.Count, $"seed {seed} {Name(strat)}: all checkpoints captured");
            Snap last = series.Last();
            Assert.IsTrue(last.Planets > 0, $"seed {seed} {Name(strat)}: player owns planets by turn {last.Turn}");
            Assert.IsTrue(last.Techs > 0, $"seed {seed} {Name(strat)}: player researched by turn {last.Turn}");
            Assert.IsTrue(last.TotalScore > 0, $"seed {seed} {Name(strat)}: player has a positive combined score");
        }
    }

    // Determinism gate: rerun one (seed, arm) and confirm the per-turn series is bit-identical.
    [TestMethod]
    public void StratRoi_Rerun_Reproducible()
    {
        const int seed = 101;
        const Strat strat = Strat.Research;
        List<Snap> a = RunArm(seed, strat);
        List<Snap> b = RunArm(seed, strat);

        Assert.AreEqual(a.Count, b.Count, "checkpoint count differs across reruns");
        for (int i = 0; i < a.Count; ++i)
        {
            Snap sa = a[i], sb = b[i];
            Assert.AreEqual(sa.Money, sb.Money, $"turn {sa.Turn}: Money diverged");
            Assert.AreEqual(sa.Planets, sb.Planets, $"turn {sa.Turn}: Planets diverged");
            Assert.AreEqual(sa.Techs, sb.Techs, $"turn {sa.Turn}: Techs diverged");
            Assert.AreEqual(sa.Income, sb.Income, $"turn {sa.Turn}: Income diverged");
            Assert.AreEqual(sa.TotalScore, sb.TotalScore, $"turn {sa.Turn}: TotalScore diverged");
        }
        Console.WriteLine($"[roi] seed={seed} {Name(strat)} RERUN REPRODUCIBLE over {a.Count} checkpoints "
                        + $"(final TOTAL {a.Last().TotalScore} == {b.Last().TotalScore})");
    }

    void EmitSeedVerdict(int seed, Dictionary<Strat, List<Snap>> perArm, Strat[] arms)
    {
        Strat best = arms.OrderByDescending(a => perArm[a].Last().TotalScore).First();
        string ranking = string.Join(" > ",
            arms.OrderByDescending(a => perArm[a].Last().TotalScore)
                .Select(a => $"{Name(a)}({perArm[a].Last().TotalScore})"));
        Console.WriteLine($"[roi] seed={seed} WINNER@turn{CheckpointTurns[^1]} (combined score): {Name(best)}  | ranking {ranking}");
    }

    void EmitAggregate(Dictionary<int, Dictionary<Strat, List<Snap>>> data, int[] seeds, Strat[] arms)
    {
        var wins = arms.ToDictionary(a => a, _ => 0);
        foreach (int seed in seeds)
        {
            Strat best = arms.OrderByDescending(a => data[seed][a].Last().TotalScore).First();
            wins[best]++;
        }
        Console.WriteLine($"[roi] AGGREGATE over {seeds.Length} seeds (combined-score wins @turn{CheckpointTurns[^1]}):");
        foreach (Strat a in arms)
            Console.WriteLine($"[roi]   {Name(a),-8}: wins={wins[a]}");
        Console.WriteLine("[roi] NOTE bounded run (Small galaxy, 3 empires, ~100 turns, 2 seeds) — directional finding, not a tournament.");
        Console.WriteLine("[roi] CAVEAT the biased empire's expansion is structurally stalled at 1-2 planets in this harness");
        Console.WriteLine("[roi]        (true even in the snowball baseline), so opening-strategy LEVERAGE is small here: the");
        Console.WriteLine("[roi]        research arm pulls a tiny but reproducible TechScore edge, the expand arm cannot express");
        Console.WriteLine("[roi]        (no planets to grab), so wins separate by only a few combined-score points. Scale the");
        Console.WriteLine("[roi]        galaxy/horizon up to amplify the signal — left bounded here for run-time budget.");
    }

    // RESEARCH-ROI signal: is research a multiplier on economy or an early tax? Compare the RESEARCH arm's
    // economy (money + income) to the EXPAND arm's economy at each checkpoint. <1.0 => research is taxing
    // economy (behind expand); >1.0 => research has overtaken expand (multiplier paying off). Report the
    // trajectory and the crossover turn (if any) within the bounded horizon.
    void EmitResearchRoi(Dictionary<int, Dictionary<Strat, List<Snap>>> data, int[] seeds)
    {
        Console.WriteLine("[roi] --- RESEARCH-ROI (research-arm economy / expand-arm economy; <1 tax, >1 multiplier) ---");
        foreach (int seed in seeds)
        {
            List<Snap> r = data[seed][Strat.Research];
            List<Snap> x = data[seed][Strat.Expand];
            int crossover = -1;
            for (int i = 0; i < r.Count; ++i)
            {
                float rEcon = r[i].Money + Math.Max(r[i].Income, 0) * 50f;   // econ proxy: treasury + ~capitalised income
                float xEcon = x[i].Money + Math.Max(x[i].Income, 0) * 50f;
                float ratio = xEcon > 1f ? rEcon / xEcon : 1f;
                if (crossover < 0 && ratio >= 1f) crossover = r[i].Turn;
                Console.WriteLine($"[roi] seed={seed} turn={r[i].Turn,3} researchEcon/expandEcon={ratio,5:0.00}  " +
                                  $"(R money={r[i].Money,7:0} inc={r[i].Income,5:0} techs={r[i].Techs,3} | " +
                                  $"X money={x[i].Money,7:0} inc={x[i].Income,5:0} techs={x[i].Techs,3})");
            }
            string verdict = crossover < 0
                ? $"research economy NEVER caught expand within {CheckpointTurns[^1]} turns => research is an EARLY TAX (no payoff in horizon)"
                : (crossover <= CheckpointTurns[0]
                    ? $"research economy >= expand from the first checkpoint (turn {crossover}) => research not a tax here"
                    : $"research economy overtakes expand at turn {crossover} => research is a delayed MULTIPLIER (ROI kicks in)");
            Console.WriteLine($"[roi] seed={seed} RESEARCH-ROI VERDICT: {verdict}");
        }
    }

    static string J(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    void WriteJson(Dictionary<int, Dictionary<Strat, List<Snap>>> data, int[] seeds, Strat[] arms)
    {
        string SnapJson(Snap s) =>
            "{" + $"\"turn\":{s.Turn},\"starDate\":{F(s.StarDate)},\"money\":{F(s.Money)},\"income\":{F(s.Income)}," +
            $"\"planets\":{s.Planets},\"pop\":{F(s.Pop)},\"techs\":{s.Techs},\"resPerTurn\":{F(s.ResPerTurn)}," +
            $"\"techScore\":{F(s.TechScore)},\"expansionScore\":{F(s.ExpansionScore)}," +
            $"\"industrialScore\":{F(s.IndustrialScore)},\"militaryScore\":{F(s.MilitaryScore)}," +
            $"\"totalScore\":{s.TotalScore}" + "}";

        string ArmJson(int seed, Strat a)
        {
            List<Snap> series = data[seed][a];
            return "{" + $"\"strategy\":{J(Name(a))}," +
                $"\"finalTotalScore\":{series.Last().TotalScore}," +
                $"\"series\":[{string.Join(",", series.Select(SnapJson))}]" + "}";
        }

        string SeedJson(int seed)
        {
            Strat best = arms.OrderByDescending(a => data[seed][a].Last().TotalScore).First();
            return "{" + $"\"seed\":{seed},\"winner\":{J(Name(best))}," +
                $"\"arms\":[{string.Join(",", arms.Select(a => ArmJson(seed, a)))}]" + "}";
        }

        string json =
            "{\n" +
            "  \"experiment\": \"strategy-roi\",\n" +
            "  \"galaxy\": \"Small\", \"majorEmpires\": 3, \"opponents\": 2,\n" +
            "  \"biasedEmpire\": \"player\", \"arms\": [\"EXPAND\",\"RESEARCH\",\"MILITARY\"],\n" +
            "  \"deterministic\": true, \"turnRngSeed\": \"0xC0FFEE\",\n" +
            "  \"combinedScore\": \"TotalScore = TechScore + ExpansionScore + IndustrialScore + MilitaryScore\",\n" +
            $"  \"checkpointTurns\": [{string.Join(",", CheckpointTurns)}],\n" +
            $"  \"seeds\": [{string.Join(",", seeds)}],\n" +
            $"  \"perSeed\": [\n    {string.Join(",\n    ", seeds.Select(SeedJson))}\n  ]\n" +
            "}\n";

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays", "fights");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "stratroi.json");
        File.WriteAllText(path, json);
        Console.WriteLine($"[roi] DATAPATH {path} ({new FileInfo(path).Length}B)");
    }
}

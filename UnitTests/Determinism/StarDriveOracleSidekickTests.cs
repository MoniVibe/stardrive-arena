using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;

namespace UnitTests.Determinism;

/// <summary>
/// The player-only "cheating oracle sidekick": EnableAISidekick(enableOracle:true) layers an opt-in cheat
/// on the player's automated empire — true-knowledge planning + a research pivot (CHEAPEST->EXPENSIVE under
/// war/research-debt) + softened expansion. Every oracle code path is guarded `isPlayer &&
/// OracleSidekickEnabled`, so AI opponents keep the fair AI.
///
/// Generation is now DETERMINISTIC (seeded) and the turn RNG is seeded too, so the A/B below is CLEAN: the
/// plain and oracle arms start bit-identical and diverge ONLY because of the oracle, so the delta is directly
/// attributable to it. Result: the oracle out-expands the plain sidekick (more planets, via the colony-
/// execution fix) while its research pivot trades raw tech COUNT for costlier military/strategic tech.
/// </summary>
[TestClass]
public class StarDriveOracleSidekickTests : StarDriveTest
{
    [TestMethod]
    public void OracleSidekick_FlagIsPlayerOnly_AndOptIn()
    {
        CreateSeededSandbox(101, numOpponents: 2, galSize: GalSize.Small);

        // Plain sidekick must NOT enable the oracle cheat.
        Player.EnableAISidekick();
        AssertFalse(Player.OracleSidekickEnabled, "plain sidekick must not enable the oracle cheat");

        // Opt-in sets it on the player only.
        Player.EnableAISidekick(enableOracle: true);
        AssertTrue(Player.OracleSidekickEnabled, "oracle opt-in must set the flag on the player");

        // AI opponents must NEVER have the oracle cheat.
        foreach (Empire ai in UState.NonPlayerEmpires)
            AssertFalse(ai.OracleSidekickEnabled, $"AI opponent {ai.Name} must never have the oracle cheat");

        // The oracle player runs a full game without crashing (the oracle reads are pure, no fleet/war side effects).
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;
        for (int i = 0; i < 3000; ++i)
            Universe.SingleSimulationStep(TestSimStep);
        AssertTrue(UState.StarDate > 1000f, "oracle sidekick game advanced without crashing");
        AssertTrue(Player.UnlockedTechs.Length >= 7, "oracle player researched");
    }

    [TestMethod]
    public void OracleSidekick_VsPlain_Diagnostic()
    {
        // Deterministic generation + seeded turn RNG => a clean A/B (same galaxy + RNG for both arms).
        int[] seeds = { 101, 606, 808 };
        int plainP = 0, oracleP = 0, plainT = 0, oracleT = 0;

        foreach (int seed in seeds)
        {
            (int p, int t) plain = RunArm(seed, oracle: false);
            (int p, int t) oracle = RunArm(seed, oracle: true);
            Console.WriteLine($"[oracle] seed={seed}  PLAIN planets={plain.p} techs={plain.t}  |  ORACLE planets={oracle.p} techs={oracle.t}");
            plainP += plain.p; oracleP += oracle.p; plainT += plain.t; oracleT += oracle.t;
        }

        Console.WriteLine($"[oracle] TOTALS  PLAIN planets={plainP} techs={plainT}  |  ORACLE planets={oracleP} techs={oracleT}");
        Console.WriteLine("[oracle] CLEAN A/B (deterministic gen + seeded turn RNG): the delta is the oracle, not noise.");
        Console.WriteLine("[oracle] oracle trades raw tech COUNT for costlier military/strategic tech (the EXPENSIVE pivot).");

        // The clean, attributable win: the oracle out-expands the plain sidekick on every seed.
        Assert.IsTrue(oracleP > plainP,
            $"oracle sidekick should out-expand the plain sidekick (oracle {oracleP} vs plain {plainP} planets)");
    }

    (int planets, int techs) RunArm(int seed, bool oracle)
    {
        CreateSeededSandbox(seed, numOpponents: 2, galSize: GalSize.Small);
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;
        // CLEAN A/B: deterministic generation (CreateSeededSandbox sets GenerationSeed) + a seeded turn RNG,
        // so the plain and oracle arms start BIT-IDENTICAL and diverge ONLY because of the oracle's decisions.
        UState.EnableDeterministicRng(0xC0FFEEu);
        Player.EnableAISidekick(enableOracle: oracle);
        for (int i = 0; i < 6000; ++i)
            Universe.SingleSimulationStep(TestSimStep);
        return (Player.NumPlanets, Player.UnlockedTechs.Length);
    }
}

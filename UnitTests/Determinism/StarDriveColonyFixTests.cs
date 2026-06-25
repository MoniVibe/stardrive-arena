using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;

namespace UnitTests.Determinism;

/// <summary>
/// Verifies the oracle-sidekick colonization fix. ROOT CAUSE (found via StarDriveColonyDiagTests): the
/// player's automated empire creates colony goals and finds targets fine, but the colony SHIPS never get
/// built — they pile up on the home space port BEHIND auto-built warships/scouts (the player's queue was
/// never priority-sorted), and the planet hoards a large stored-production stockpile the per-turn throttle
/// barely spends, so even the front colony ship crawls. The player also never rushed colony ships. Result:
/// the oracle player capped at ~2 planets in 100+ turns.
///
/// FIX (player-only, guarded `isPlayer && OracleSidekickEnabled && AutoColonize`):
///   1. SBProduction.AddToQueueAndPrioritize — sort the oracle player's queue by the same AI priority the
///      fair AI uses, so colony ships reach the front instead of sitting behind warships.
///   2. SBProduction.TryPlayerRush — when a colony ship is at the front, rush it from the idle stored
///      production stockpile (done in the per-turn production update, NOT during goal evaluation, to avoid
///      re-entrancy that froze the sim).
/// AI opponents are untouched (they already take the !OwnerIsPlayer branch / never have the oracle flag).
/// </summary>
[TestClass]
public class StarDriveColonyFixTests : StarDriveTest
{
    [TestMethod]
    public void ColonyFix_OracleColonizesMoreThanPlain()
    {
        int[] seeds = { 101, 202, 303 };
        int plainTotal = 0, oracleTotal = 0;
        int oracleWins = 0;

        foreach (int seed in seeds)
        {
            int plain  = RunArm(seed, oracle: false);
            int oracle = RunArm(seed, oracle: true);
            Console.WriteLine($"[colonyfix] seed={seed}  PLAIN planets={plain}  |  ORACLE planets={oracle}");
            plainTotal += plain;
            oracleTotal += oracle;
            if (oracle > plain) oracleWins++;
        }

        Console.WriteLine($"[colonyfix] TOTALS  PLAIN planets={plainTotal}  |  ORACLE planets={oracleTotal}  (oracle won {oracleWins}/{seeds.Length} seeds)");

        // The oracle player must colonize clearly MORE in aggregate, and win the majority of seeds.
        Assert.IsTrue(oracleTotal > plainTotal,
            $"[colonyfix] oracle must colonize more in aggregate (oracle {oracleTotal} vs plain {plainTotal})");
        Assert.IsTrue(oracleWins >= 2,
            $"[colonyfix] oracle must win the majority of seeds (won {oracleWins}/{seeds.Length})");
    }

    int RunArm(int seed, bool oracle)
    {
        CreateSeededSandbox(seed, numOpponents: 2, galSize: GalSize.Small);
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;
        Player.EnableAISidekick(enableOracle: oracle);

        // ~150 empire turns (TurnTimer=1 => ~60 ticks/turn).
        for (int i = 0; i < 150 * 60; ++i)
            Universe.SingleSimulationStep(TestSimStep);

        return Player.NumPlanets;
    }
}

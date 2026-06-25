using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDLockstep;
using Ship_Game.Determinism;
using Ship_Game.Determinism.Lockstep;
using Vector2 = SDGraphics.Vector2;

namespace UnitTests.Determinism;

/// <summary>
/// Advisor plan RC5 — AttackTarget commands flow through the real StarDrive combat system
/// (ship.AI.OrderAttackSpecificTarget), runtime-verified headless. Two armed ships at war attack each
/// other by stable id; combat (weapons, damage, HP) is the RNG-heavy path most likely to expose
/// non-determinism, so this is the key bug-magnet for the VS2 RNG migration.
/// </summary>
[TestClass]
public class StarDriveLockstepCombatTests : StarDriveTest
{
    UniverseStateLockstepSimulation BuildCombat(out int shipA, out int shipB)
    {
        CreateUniverseAndPlayerEmpire();
        UState.P.GravityWellRange = 0;
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;

        var a = SpawnShip("Fang Strafer", Player, new Vector2(0, 0));
        var b = SpawnShip("Fang Strafer", Enemy, new Vector2(2500, 0));
        a.SensorRange = 40000;
        b.SensorRange = 40000;
        shipA = a.Id;
        shipB = b.Id;
        // RC7: put the match on reproducible RNG streams from a FIXED root seed (same for both runs)
        UState.EnableDeterministicRng(0x5EED1234u);
        return new UniverseStateLockstepSimulation(Universe, DeterminismProfile.ReplayWinX64Float);
    }

    static CommandFrame Frame(uint tick, int a, int b)
    {
        var f = new CommandFrame(tick);
        if (tick == 4) // both ships open fire on each other by stable id
        {
            f.Add(new SimCommand(tick, 1, 0, SimCommandKind.AttackTarget, a, b));
            f.Add(new SimCommand(tick, 2, 0, SimCommandKind.AttackTarget, b, a));
        }
        f.Sort();
        return f;
    }

    List<(ulong lo, ulong hi)> Run(UniverseStateLockstepSimulation sim, int a, int b, int n)
    {
        var traj = new List<(ulong lo, ulong hi)>(n + 1);
        traj.Add(sim.Hash());
        for (uint t = 0; t < n; ++t)
        {
            sim.Apply(Frame(sim.Tick + 1, a, b));
            traj.Add(sim.Hash());
        }
        return traj;
    }

    [TestMethod]
    public void Attack_CommandCausesCombatDamage()
    {
        UniverseStateLockstepSimulation sim = BuildCombat(out int a, out int b);
        float hpA0 = UState.Objects.FindShip(a).Health;
        float hpB0 = UState.Objects.FindShip(b).Health;
        Run(sim, a, b, 600);
        float hpA1 = UState.Objects.FindShip(a)?.Health ?? 0f; // null => destroyed => took damage
        float hpB1 = UState.Objects.FindShip(b)?.Health ?? 0f;
        Console.WriteLine($"[combat] cmds={sim.CommandStats} A {hpA0:0}->{hpA1:0} B {hpB0:0}->{hpB1:0}");
        Assert.IsTrue(hpA1 < hpA0 || hpB1 < hpB0, "AttackTarget command should cause combat damage");
    }

    [TestMethod]
    public void Attack_TwoRuns_DeterminismProbe()
    {
        UniverseStateLockstepSimulation sim1 = BuildCombat(out int a1, out int b1);
        List<(ulong lo, ulong hi)> t1 = Run(sim1, a1, b1, 600);

        UniverseStateLockstepSimulation sim2 = BuildCombat(out int a2, out int b2);
        List<(ulong lo, ulong hi)> t2 = Run(sim2, a2, b2, 600);

        int firstDiv = -1;
        for (int i = 0; i < t1.Count; ++i)
            if (t1[i] != t2[i]) { firstDiv = i; break; }

        Console.WriteLine(firstDiv < 0
            ? "[combat] two runs REPRODUCIBLE over 600 ticks"
            : $"[combat] two runs first DIVERGE at tick {firstDiv}/600");
        Assert.IsTrue(firstDiv < 0, $"combat must be deterministic; runs diverged at tick {firstDiv}");
    }
}

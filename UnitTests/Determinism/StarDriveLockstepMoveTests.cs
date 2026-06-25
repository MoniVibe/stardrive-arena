using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDLockstep;
using SDUtils.Deterministic;
using Ship_Game.AI;
using Ship_Game.Determinism;
using Ship_Game.Determinism.Lockstep;
using Vector2 = SDGraphics.Vector2;

namespace UnitTests.Determinism;

/// <summary>
/// Advisor plan RC4 — MoveShip commands flow through the real StarDrive order system
/// (UniverseStateLockstepSimulation → StarDriveCommandApplicator → ship.AI.OrderMoveTo), runtime-verified
/// headless. One combat-capable ship per empire, AI disabled, gravity-well routing off, single-threaded —
/// the minimal scenario in which a move command meaningfully mutates authoritative state (ship position),
/// so the determinism probe is non-trivial.
/// </summary>
[TestClass]
public class StarDriveLockstepMoveTests : StarDriveTest
{
    UniverseStateLockstepSimulation BuildScenario(out int playerShipId, out int enemyShipId)
    {
        // minimal proven-movement scenario (mirrors TestShipMove): the lockstep adapter drives
        // Objects.Update, so the empire-AI turn never runs and need not be disabled.
        CreateUniverseAndPlayerEmpire();
        UState.P.GravityWellRange = 0;          // disable gravity-well detour routing (non-determinism source)
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;

        var s1 = SpawnShip("Fang Strafer", Player, new Vector2(0, 0));
        var s2 = SpawnShip("Fang Strafer", Enemy, new Vector2(50_000, 0));
        s1.SensorRange = 40000;
        s2.SensorRange = 40000;
        playerShipId = s1.Id;
        enemyShipId = s2.Id;
        UState.EnableDeterministicRng(0x5EED1234u); // RC7: reproducible RNG streams

        return new UniverseStateLockstepSimulation(Universe, DeterminismProfile.ReplayWinX64Float);
    }

    static CommandFrame FrameFor(uint tick, int playerShip, int enemyShip)
    {
        var f = new CommandFrame(tick);
        if (tick == 6) // both ships receive a move order at tick 6
        {
            f.Add(new SimCommand(tick, 1, 0, SimCommandKind.MoveShip, playerShip, -1,
                Fixed64.FromInt(80_000).Raw, Fixed64.FromInt(80_000).Raw));
            f.Add(new SimCommand(tick, 2, 0, SimCommandKind.MoveShip, enemyShip, -1,
                Fixed64.FromInt(-80_000).Raw, Fixed64.FromInt(-80_000).Raw));
        }
        f.Sort();
        return f;
    }

    List<(ulong lo, ulong hi)> Run(UniverseStateLockstepSimulation sim, int playerShip, int enemyShip, int n)
    {
        var traj = new List<(ulong lo, ulong hi)>(n + 1);
        traj.Add(sim.Hash());
        for (uint t = 0; t < n; ++t)
        {
            sim.Apply(FrameFor(sim.Tick + 1, playerShip, enemyShip));
            traj.Add(sim.Hash());
        }
        return traj;
    }

    [TestMethod]
    public void MoveShip_CommandActuallyMovesShip()
    {
        UniverseStateLockstepSimulation sim = BuildScenario(out int p, out int e);
        Vector2 start = UState.Objects.FindShip(p).Position;
        Run(sim, p, e, 500);
        Vector2 end = UState.Objects.FindShip(p).Position;
        Console.WriteLine($"[move] cmds={sim.CommandStats} start={start} end={end} dist={start.Distance(end):0}");
        Assert.IsTrue(start.Distance(end) > 500f,
            $"MoveShip command should have moved the ship (dist {start.Distance(end):0})");
    }

    [TestMethod]
    public void MoveShip_TwoRuns_DeterminismProbe()
    {
        UniverseStateLockstepSimulation sim1 = BuildScenario(out int p1, out int e1);
        List<(ulong lo, ulong hi)> t1 = Run(sim1, p1, e1, 500);

        UniverseStateLockstepSimulation sim2 = BuildScenario(out int p2, out int e2);
        Assert.AreEqual(p1, p2, "ship ids must be deterministic across identical builds");
        List<(ulong lo, ulong hi)> t2 = Run(sim2, p2, e2, 500);

        int firstDiv = -1;
        for (int i = 0; i < t1.Count; ++i)
            if (t1[i] != t2[i]) { firstDiv = i; break; }

        Console.WriteLine(firstDiv < 0
            ? "[move] two runs REPRODUCIBLE over 500 ticks (Tier-A move determinism holds)"
            : $"[move] two runs first DIVERGE at tick {firstDiv}/500");
        Assert.IsTrue(firstDiv < 0, $"move must be deterministic; runs diverged at tick {firstDiv}");
    }
}

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDLockstep;
using Ship_Game.Determinism;
using Ship_Game.Determinism.Lockstep;
using Vector2 = SDGraphics.Vector2;

namespace UnitTests.Determinism;

/// <summary>
/// Fleet-vs-fleet AUTOBATTLE on real StarDrive (toward the autobattler goal). Each side gets a fleet;
/// ships are commanded to engage once, then the ship combat AI carries the fight (approach, fire,
/// re-target as ships die). With deterministic RNG (RC7) the question is whether AI target selection —
/// which uses the spatial FindNearby query — is also deterministic, or whether native result ordering
/// surfaces the next divergence (VS5).
/// </summary>
[TestClass]
public class StarDriveAutobattleTests : StarDriveTest
{
    readonly List<int> PlayerShips = new();
    readonly List<int> EnemyShips = new();

    UniverseStateLockstepSimulation BuildFleetBattle(int perSide)
    {
        PlayerShips.Clear();
        EnemyShips.Clear();
        CreateUniverseAndPlayerEmpire();
        UState.P.GravityWellRange = 0;
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;
        for (int i = 0; i < perSide; i++)
        {
            float y = (i - perSide / 2) * 1000f;
            var pa = SpawnShip("Fang Strafer", Player, new Vector2(-1500, y));
            var ea = SpawnShip("Fang Strafer", Enemy, new Vector2(1500, y));
            pa.SensorRange = 60000;
            ea.SensorRange = 60000;
            PlayerShips.Add(pa.Id);
            EnemyShips.Add(ea.Id);
        }
        UState.EnableDeterministicRng(0x5EED1234u);
        return new UniverseStateLockstepSimulation(Universe, DeterminismProfile.ReplayWinX64Float);
    }

    CommandFrame EngageFrame(uint tick)
    {
        var f = new CommandFrame(tick);
        if (tick == 4) // each ship is told to engage its counterpart; the AI carries the rest
        {
            for (int i = 0; i < PlayerShips.Count; ++i)
            {
                f.Add(new SimCommand(tick, 1, (uint)i, SimCommandKind.AttackTarget, PlayerShips[i], EnemyShips[i]));
                f.Add(new SimCommand(tick, 2, (uint)i, SimCommandKind.AttackTarget, EnemyShips[i], PlayerShips[i]));
            }
        }
        f.Sort();
        return f;
    }

    List<(ulong lo, ulong hi)> Run(UniverseStateLockstepSimulation sim, int n)
    {
        var traj = new List<(ulong lo, ulong hi)>(n + 1);
        traj.Add(sim.Hash());
        for (uint t = 0; t < n; ++t)
        {
            sim.Apply(EngageFrame(sim.Tick + 1));
            traj.Add(sim.Hash());
        }
        return traj;
    }

    // sum health of the known fleet ships by stable id (FindShip sees both buffers; dead ship => 0)
    float FleetHealth()
    {
        float h = 0;
        foreach (int id in PlayerShips) h += UState.Objects.FindShip(id)?.Health ?? 0f;
        foreach (int id in EnemyShips) h += UState.Objects.FindShip(id)?.Health ?? 0f;
        return h;
    }

    int FleetAlive()
    {
        int n = 0;
        foreach (int id in PlayerShips) if (UState.Objects.FindShip(id) != null) ++n;
        foreach (int id in EnemyShips) if (UState.Objects.FindShip(id) != null) ++n;
        return n;
    }

    [TestMethod]
    public void FleetAutobattle_CombatHappens()
    {
        UniverseStateLockstepSimulation sim = BuildFleetBattle(4);
        float hpBefore = FleetHealth();
        int aliveBefore = FleetAlive();
        Run(sim, 1500);
        Console.WriteLine($"[autobattle] alive {aliveBefore}->{FleetAlive()}, fleet HP {hpBefore:0}->{FleetHealth():0}");
        Assert.IsTrue(FleetHealth() < hpBefore, "fleet autobattle should produce combat damage");
    }

    [TestMethod]
    public void FleetAutobattle_TwoRuns_Deterministic()
    {
        UniverseStateLockstepSimulation sim1 = BuildFleetBattle(4);
        List<(ulong lo, ulong hi)> t1 = Run(sim1, 1500);
        UniverseStateLockstepSimulation sim2 = BuildFleetBattle(4);
        List<(ulong lo, ulong hi)> t2 = Run(sim2, 1500);

        int firstDiv = -1;
        for (int i = 0; i < t1.Count; ++i)
            if (t1[i] != t2[i]) { firstDiv = i; break; }

        Console.WriteLine(firstDiv < 0
            ? "[autobattle] two runs REPRODUCIBLE over 1500 ticks (fleet AI is deterministic, incl. re-targeting)"
            : $"[autobattle] two runs first DIVERGE at tick {firstDiv}/1500 (likely AI target selection / FindNearby ordering — VS5)");
        Assert.IsTrue(firstDiv < 0, $"fleet autobattle must be deterministic; diverged at tick {firstDiv}");
    }
}

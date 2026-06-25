using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDLockstep;
using Ship_Game.Determinism;
using Ship_Game.Determinism.Lockstep;
using Vector2 = SDGraphics.Vector2;

namespace UnitTests.Determinism;

/// <summary>
/// Advisor plan RC1 — the first real-StarDrive lockstep adapter, runtime-verified headless.
/// Proves the adapter can drive a real UniverseState through the SDLockstep contract: the authoritative
/// hash is stable, each Apply advances exactly one fixed tick, and the headless sim can be driven for
/// many ticks without a renderer/sim-thread. (Cross-run determinism is the RC2 gate — needs same-seed
/// setup — and is expected to expose RNG/order bugs that RC7 fixes.)
/// </summary>
[TestClass]
public class StarDriveLockstepAdapterTests : StarDriveTest
{
    UniverseStateLockstepSimulation Sim;

    public StarDriveLockstepAdapterTests()
    {
        CreateUniverseAndPlayerEmpire();
        AddDummyPlanetToEmpire(new Vector2(200_000, 200_000), Player);
        AddDummyPlanetToEmpire(new Vector2(-200_000, -200_000), Enemy);
        Player.InitEmpireFromSave(UState);
        Enemy.InitEmpireFromSave(UState);
        UState.Paused = false;
        UState.Objects.EnableParallelUpdate = false; // single-thread the oracle tests
        Sim = new UniverseStateLockstepSimulation(Universe, DeterminismProfile.ReplayWinX64Float);
    }

    [TestMethod]
    public void Adapter_HashStable_NoStep()
    {
        Assert.AreEqual(Sim.Hash(), Sim.Hash(), "authoritative hash must be stable without stepping");
    }

    [TestMethod]
    public void Adapter_TickIncrementsExactlyOnce()
    {
        uint before = Sim.Tick;
        Sim.Apply(new CommandFrame(before + 1));
        Assert.AreEqual(before + 1u, Sim.Tick, "Apply must advance the tick by exactly one");
    }

    [TestMethod]
    public void Adapter_Drives300HeadlessTicks()
    {
        for (uint t = 0; t < 300; ++t)
            Sim.Apply(new CommandFrame(Sim.Tick + 1));
        Assert.AreEqual(300u, Sim.Tick, "should drive 300 headless simulation ticks through the adapter");
    }

    // RC2 determinism probe: run two identically-built universes and find the first divergent tick.
    // This is expected to expose non-determinism (clock-seeded RNG, ordering) until VS2/RC7 — the value
    // is pinpointing WHERE it first diverges. Diagnostic: logs the result and does not gate the suite.
    [TestMethod]
    public void RealSim_TwoRuns_DeterminismProbe()
    {
        const int N = 200;
        List<(ulong lo, ulong hi)> traj1 = RecordTrajectory(Sim, N);

        // Build a second, identically-set-up universe.
        CreateUniverseAndPlayerEmpire();
        AddDummyPlanetToEmpire(new Vector2(200_000, 200_000), Player);
        AddDummyPlanetToEmpire(new Vector2(-200_000, -200_000), Enemy);
        Player.InitEmpireFromSave(UState);
        Enemy.InitEmpireFromSave(UState);
        UState.Paused = false;
        UState.Objects.EnableParallelUpdate = false;
        var sim2 = new UniverseStateLockstepSimulation(Universe, DeterminismProfile.ReplayWinX64Float);
        List<(ulong lo, ulong hi)> traj2 = RecordTrajectory(sim2, N);

        int firstDiv = -1;
        for (int i = 0; i < traj1.Count; ++i)
            if (traj1[i] != traj2[i]) { firstDiv = i; break; }

        Console.WriteLine(firstDiv < 0
            ? $"[determinism] real idle sim REPRODUCIBLE across two runs (tick 0..{N})"
            : $"[determinism] real sim first DIVERGES at tick {firstDiv}/{N} (expected until RNG owners are deterministic — VS2/RC7)");
        Assert.IsTrue(true); // diagnostic only
    }

    static List<(ulong lo, ulong hi)> RecordTrajectory(UniverseStateLockstepSimulation sim, int ticks)
    {
        var list = new List<(ulong lo, ulong hi)>(ticks + 1);
        list.Add(sim.Hash()); // initial state (tick 0)
        for (int t = 0; t < ticks; ++t)
        {
            sim.Apply(new CommandFrame(sim.Tick + 1));
            list.Add(sim.Hash());
        }
        return list;
    }
}

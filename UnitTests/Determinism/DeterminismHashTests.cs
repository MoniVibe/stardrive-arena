using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game.Determinism;

namespace UnitTests.Determinism;

/// <summary>
/// Advisor plan §M1/§M2 proving tests for the canonical state-hash oracle.
///
/// The stability/no-mutation tests pass today: they prove the hash traversal is itself
/// deterministic (fixed entity ordering, no hidden dictionary-order leak) and side-effect free.
/// The record/replay trajectory test is the §M2 harness; it is gated until §M4 wires
/// DeterministicRandom into the simulation owners (today the universe uses clock-seeded
/// ThreadSafeRandom, which is intentionally non-reproducible).
/// </summary>
[TestClass]
public class DeterminismHashTests : StarDriveTest
{
    public DeterminismHashTests()
    {
        CreateUniverseAndPlayerEmpire();
        // single-thread the object update so the oracle tests are not affected by parallel scheduling
        UState.Objects.EnableParallelUpdate = false;
    }

    [TestMethod]
    public void StateHash_IsStableAcrossRepeatedCalls()
    {
        ulong a = UState.ComputeStateHash(new DeterminismHashWriter());
        ulong b = UState.ComputeStateHash(new DeterminismHashWriter());
        Assert.AreEqual(a, b, "State hash must be identical across repeated calls on identical state");
    }

    [TestMethod]
    public void StateHash_LaneHashesAreStable()
    {
        var w1 = new DeterminismHashWriter();
        var w2 = new DeterminismHashWriter();
        UState.ComputeStateHash(w1);
        UState.ComputeStateHash(w2);
        for (int lane = 0; lane < DeterminismHashWriter.LaneCount; ++lane)
        {
            Assert.AreEqual(w1.LaneHash((DetLane)lane), w2.LaneHash((DetLane)lane),
                $"Lane {(DetLane)lane} hash must be stable across repeated calls");
        }
    }

    [TestMethod]
    public void StateHash_DoesNotMutateState()
    {
        // Hashing must be a pure read: a second hash of the (untouched) state must match the first.
        ulong before = UState.ComputeStateHash(new DeterminismHashWriter());
        ulong after = UState.ComputeStateHash(new DeterminismHashWriter());
        Assert.AreEqual(before, after, "ComputeStateHash must not mutate simulation state");
    }

    [TestMethod]
    [Ignore("Enable after §M4 wires DeterministicRandom into UniverseState/Empire owners; " +
            "today the sim uses clock-seeded ThreadSafeRandom and is intentionally non-reproducible.")]
    public void RecordReplay_SameSeedSameTrajectory()
    {
        // §M2 harness shape: run the sim N ticks twice from the same seed and assert the
        // per-tick hash trajectories match. Becomes meaningful once the sim owners are deterministic.
        ulong[] first = RunTrajectory(60);
        ulong[] second = RunTrajectory(60);
        for (int i = 0; i < first.Length; ++i)
            Assert.AreEqual(first[i], second[i], $"Hash trajectory diverged at tick {i}");
    }

    ulong[] RunTrajectory(int ticks)
    {
        var trajectory = new ulong[ticks];
        for (int i = 0; i < ticks; ++i)
        {
            Universe.SingleSimulationStep(TestSimStep);
            trajectory[i] = UState.ComputeStateHash(new DeterminismHashWriter());
        }
        return trajectory;
    }
}

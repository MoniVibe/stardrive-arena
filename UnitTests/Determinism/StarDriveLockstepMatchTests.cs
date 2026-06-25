using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDLockstep;
using Ship_Game.Determinism;
using Ship_Game.Determinism.Lockstep;
using Vector2 = SDGraphics.Vector2;

namespace UnitTests.Determinism;

/// <summary>
/// The autobattler match core (advisor "first playable proof", real-StarDrive): two real
/// StarDrive universes, each wrapped as an <see cref="UniverseStateLockstepSimulation"/>, run through
/// the actual host-authoritative <see cref="LockstepHost"/> / <see cref="LockstepClient"/> engine over a
/// fake transport. Both players issue Attack commands; with deterministic RNG (RC7) the two clients must
/// stay bit-identical every tick — that is a fair, replayable 1v1 match.
/// </summary>
[TestClass]
public class StarDriveLockstepMatchTests : StarDriveTest
{
    UniverseStateLockstepSimulation BuildMatch(out int shipA, out int shipB)
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
        UState.EnableDeterministicRng(0x5EED1234u);
        return new UniverseStateLockstepSimulation(Universe, DeterminismProfile.ReplayWinX64Float);
    }

    [TestMethod]
    public void TwoClientLockstep_RealStarDrive_StaysInSync()
    {
        UniverseStateLockstepSimulation simA = BuildMatch(out int a1, out int b1);
        UniverseStateLockstepSimulation simB = BuildMatch(out int a2, out int b2);
        Assert.AreEqual(a1, a2, "ship ids deterministic across the two clients");
        Assert.AreEqual(b1, b2, "ship ids deterministic across the two clients");

        var transport = new FakeTransport();
        var host = new LockstepHost(transport);
        var clientA = new LockstepClient(transport, 1, simA);
        var clientB = new LockstepClient(transport, 2, simB);
        host.AddClient(1);
        host.AddClient(2);

        const uint N = 400;
        const uint inputDelay = 2;
        for (uint t = 0; t < N; ++t)
        {
            if (t == 4) // both players open fire (each submits only its own command)
            {
                clientA.Submit(new SimCommand(t + inputDelay, 1, 0, SimCommandKind.AttackTarget, a1, b1));
                clientB.Submit(new SimCommand(t + inputDelay, 2, 0, SimCommandKind.AttackTarget, b1, a1));
            }
            transport.Poll();      // submissions -> host
            host.CommitTick(t);    // host broadcasts the committed frame to both clients
            transport.Poll();      // frames -> clients
            clientA.Pump();        // each real StarDrive sim applies tick t
            clientB.Pump();
            transport.Poll();      // checksums -> host
        }

        Assert.IsFalse(host.Desync.HasDesync,
            $"real-StarDrive 2-client match desynced at tick {host.Desync.FirstDivergentTick}");
        Assert.AreEqual(simA.Hash(), simB.Hash(), "final authoritative hash identical across both clients");
    }
}

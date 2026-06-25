using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StarDrive.Tools.ArenaLockstepProbe;

namespace UnitTests.Determinism;

[TestClass]
public class ArenaPortableLockstepProbeTests
{
    [TestMethod]
    public void ArenaPortableLockstepProbe_InProcessAndForcedDesync_Headless()
    {
        PortableLockstepOptions options = TestOptions();

        PortableLockstepResult clean = PortableLockstepRunner.RunInProcess(options);
        Assert.IsFalse(clean.Desynced, clean.DesyncReason);
        Assert.AreEqual(options.Turns, clean.TurnsCompleted);
        Assert.IsFalse(string.IsNullOrWhiteSpace(clean.FinalHash), "The probe should produce a final state hash.");
        Assert.IsTrue(clean.CommandsSubmitted >= options.Turns * 2,
            $"Expected one submitted command per peer per turn; saw {clean.CommandsSubmitted}.");

        PortableLockstepResult divergent = PortableLockstepRunner.RunInProcess(options, forceDivergenceTurn: 20);
        Assert.IsTrue(divergent.Desynced, "Forced divergence must trip the desync detector.");
        Assert.IsTrue(divergent.DesyncTurn >= 20,
            $"Forced divergence should report at or after the injected turn, saw {divergent.DesyncTurn}.");
    }

    [TestMethod]
    public void ArenaPortableLockstepProbe_LoopbackTcpMatches_Headless()
    {
        string root = Path.Combine(Path.GetTempPath(), $"arena_lockstep_probe_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            PortableLockstepOptions options = TestOptions();
            options.OutputPath = Path.Combine(root, "arena-lockstep-probe.txt");

            (PortableLockstepResult host, PortableLockstepResult join) = PortableLockstepRunner.RunLoopback(options);

            Assert.IsFalse(host.Desynced, host.DesyncReason);
            Assert.IsFalse(join.Desynced, join.DesyncReason);
            Assert.AreEqual(options.Turns, host.TurnsCompleted);
            Assert.AreEqual(options.Turns, join.TurnsCompleted);
            Assert.AreEqual(host.FinalHash, join.FinalHash,
                $"Loopback peers should finish bit-identically.\nhost={host.FinalHash}\njoin={join.FinalHash}");
            Assert.IsTrue(File.Exists(options.OutputPath), "Loopback host should write the portable probe artifact.");
            StringAssert.Contains(File.ReadAllText(options.OutputPath), "ContentMode=content-free synthetic arena lockstep");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best-effort cleanup only
            }
        }
    }

    static PortableLockstepOptions TestOptions() => new()
    {
        GenerationSeed = PortableLockstepOptions.DefaultGenerationSeed,
        RngSeed = PortableLockstepOptions.DefaultRngSeed,
        Turns = 80,
        InputDelay = PortableLockstepOptions.DefaultInputDelay,
        StepDt = PortableLockstepOptions.DefaultStepDt,
        OutputPath = Path.Combine(Path.GetTempPath(), $"arena_lockstep_probe_{Guid.NewGuid():N}.txt"),
    };
}

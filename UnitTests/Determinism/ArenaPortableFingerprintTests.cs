using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StarDrive.Tools.ArenaFingerprint;

namespace UnitTests.Determinism;

[TestClass]
public class ArenaPortableFingerprintTests
{
    [TestMethod]
    public void ArenaPortableFingerprintContentFreeSameSeed_Headless()
    {
        var options = PortableFingerprintOptions.Default(Path.GetTempPath());
        options.Steps = PortableFingerprintOptions.DefaultSteps;

        PortableFingerprintRun baseline = PortableFingerprintRunner.Run(options, "baseline");
        PortableFingerprintRun rerun = PortableFingerprintRunner.Run(options, "rerun");

        int divergence = PortableFingerprintRunner.FirstDivergence(baseline, rerun, out string reason);
        Assert.IsTrue(divergence < 0,
            $"Portable fingerprint diverged at step {divergence}: {reason}\n" +
            $"baseline={PortableFingerprintRunner.SafeStepLine(baseline, divergence)}\n" +
            $"rerun={PortableFingerprintRunner.SafeStepLine(rerun, divergence)}");

        var differentSeed = options.Clone();
        differentSeed.GenerationSeed += 1;
        differentSeed.RngSeed += 1;
        PortableFingerprintRun different = PortableFingerprintRunner.Run(differentSeed, "different");

        Assert.AreNotEqual(baseline.SequenceSha256, different.SequenceSha256,
            "A different portable fingerprint seed must produce a different sequence digest.");
        Assert.AreEqual(options.Steps + 1, baseline.StepLines.Count);
        CollectionAssert.Contains(baseline.HeaderLines, "ContentMode=content-free synthetic arena");
    }

    [TestMethod]
    public void ArenaPortableFingerprintWritesOutputFile_Headless()
    {
        string root = Path.Combine(Path.GetTempPath(), $"arena_portable_fp_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var options = PortableFingerprintOptions.Default(root);
            options.Steps = 128;

            PortableFingerprintSelfTestResult result = PortableFingerprintRunner.RunSelfTest(options);
            Assert.IsTrue(File.Exists(result.OutputPath), "Self-test should write a portable fingerprint file.");
            Assert.AreEqual(result.Baseline.SequenceSha256, result.Rerun.SequenceSha256);
            Assert.AreNotEqual(result.Baseline.SequenceSha256, result.DifferentSeed.SequenceSha256);

            string text = File.ReadAllText(result.OutputPath);
            StringAssert.Contains(text, "SequenceSha256=" + result.Baseline.SequenceSha256);
            StringAssert.Contains(text, "PrimaryHash=PortableSyntheticArenaStateHash");
            StringAssert.Contains(text, "step=0000 ");
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
}

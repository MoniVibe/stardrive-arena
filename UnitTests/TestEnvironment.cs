using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTests;

/// <summary>
/// Test environment helpers for selectively skipping flaky tests on CI.
/// </summary>
public static class TestEnvironment
{
    /// <summary>
    /// True when running under AppVeyor's CI runner. AppVeyor sets APPVEYOR=True
    /// in the build environment.
    /// </summary>
    public static bool IsAppVeyor =>
        string.Equals(Environment.GetEnvironmentVariable("APPVEYOR"), "True",
                      StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Skips the calling test (Assert.Inconclusive) when running on AppVeyor.
    /// Use at the top of wall-clock-based perf tests that are too noisy on the
    /// shared CI VM but still useful as local regression checks.
    /// Pair with [TestCategory("Performance")] so the same tests can also be
    /// excluded via runsettings filter when desired.
    /// </summary>
    public static void SkipOnAppVeyor()
    {
        if (IsAppVeyor)
            Assert.Inconclusive("Skipped on AppVeyor: shared-VM scheduler stalls poison wall-clock assertions. " +
                                "Run locally to validate.");
    }
}

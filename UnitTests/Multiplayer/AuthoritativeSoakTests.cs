using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTests.Multiplayer;

[TestClass]
public sealed class AuthoritativeSoakTests : StarDriveTest
{
    [TestMethod]
    public void Soak_Smoke()
    {
        AuthoritativeSoakMatrixResult matrix =
            AuthoritativeSoakHarness.RunMatrix(AuthoritativeSoakHarness.SmokeConfigFromEnvironment());
        AssertNoNewDivergence(matrix);
    }

    [TestMethod]
    [TestCategory("Performance")]
    public void Soak_Nightly()
    {
        AuthoritativeSoakMatrixResult matrix =
            AuthoritativeSoakHarness.RunMatrix(AuthoritativeSoakHarness.NightlyConfigFromEnvironment());
        AssertNoNewDivergence(matrix);
    }

    [TestMethod]
    [TestCategory("Performance")]
    public void Soak_Replay()
    {
        AuthoritativeSoakRunResult result =
            AuthoritativeSoakHarness.ExerciseReplayEntryPoint(AuthoritativeSoakHarness.NightlyConfigFromEnvironment());
        AssertNoNewDivergence(result);
    }

    static void AssertNoNewDivergence(AuthoritativeSoakMatrixResult matrix)
    {
        if (matrix.HasNewDivergence)
            Assert.Fail(matrix.FirstNewDivergence.Summary + " artifact=" + matrix.FirstNewDivergence.ArtifactPath);
    }

    static void AssertNoNewDivergence(AuthoritativeSoakRunResult result)
    {
        if (result.Outcome == AuthoritativeSoakOutcome.NewDivergence)
            Assert.Fail(result.Summary + " artifact=" + result.ArtifactPath);
    }
}

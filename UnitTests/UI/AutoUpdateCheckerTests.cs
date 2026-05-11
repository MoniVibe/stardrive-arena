using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game.GameScreens.MainMenu;
using static Ship_Game.GameScreens.MainMenu.AutoUpdateChecker.UpdateAvailability;

namespace UnitTests.UI
{
    [TestClass]
    public class AutoUpdateCheckerTests
    {
        [TestMethod]
        // older latest -> nothing (the bug that triggered the fix)
        [DataRow("1.51.15118", "1.60.00000", None)]
        [DataRow("1.51.15118", "1.60.00002", None)]
        [DataRow("1.59.99999", "1.60.00000", None)]
        // equal -> nothing
        [DataRow("1.60.00000", "1.60.00000", None)]
        // same major.minor, newer build -> in-game patch
        [DataRow("1.60.00001", "1.60.00000", InGamePatch)]
        [DataRow("1.60.00002", "1.60.00000", InGamePatch)]
        [DataRow("1.60.10000", "1.60.00000", InGamePatch)]
        // cross-major (newer) -> popup
        [DataRow("1.60.00000", "1.51.15118", CrossMajor)]
        [DataRow("2.0.0",      "1.60.00000", CrossMajor)]
        [DataRow("1.61.0",     "1.60.00002", CrossMajor)]
        // unparseable -> Unparseable (don't fire on garbage)
        [DataRow("not-a-version", "1.60.00000", Unparseable)]
        [DataRow("1.60.00000", "garbage",      Unparseable)]
        [DataRow("",          "1.60.00000",   Unparseable)]
        public void ClassifyVanillaUpdate_Cases(string latest, string current,
            AutoUpdateChecker.UpdateAvailability expected)
        {
            Assert.AreEqual(expected, AutoUpdateChecker.ClassifyVanillaUpdate(latest, current));
        }

        [TestMethod]
        public void DisplayLabel_TrimsToMajorMinor_WithCodename()
        {
            Assert.AreEqual("Jupiter 1.60",
                AutoUpdateChecker.BuildMajorUpgradeDisplayLabel("1.60.00002", "Jupiter"));
        }

        [TestMethod]
        public void DisplayLabel_TrimsToMajorMinor_WithoutBuildNumber()
        {
            // GitHub release-tag form ("jupiter-release-1.60") yields "1.60" already
            Assert.AreEqual("Jupiter 1.60",
                AutoUpdateChecker.BuildMajorUpgradeDisplayLabel("1.60", "Jupiter"));
        }

        [TestMethod]
        public void DisplayLabel_FallsBackToBlackBox_WhenCodenameMissing()
        {
            Assert.AreEqual("BlackBox 1.60",
                AutoUpdateChecker.BuildMajorUpgradeDisplayLabel("1.60.00002", null));
            Assert.AreEqual("BlackBox 1.60",
                AutoUpdateChecker.BuildMajorUpgradeDisplayLabel("1.60.00002", ""));
        }

        [TestMethod]
        [DataRow("jupiter-release-1.60",   "Jupiter")]
        [DataRow("jupiter-patch-1.60.00002", "Jupiter")]
        [DataRow("mars-patch-1.51.15118",  "Mars")]
        [DataRow("MARS-release-1.51",      "Mars")] // case-folding
        [DataRow("v1.60.00002",            null)]   // no codename token
        [DataRow("1.60",                   null)]   // numeric-only first segment
        [DataRow("",                       null)]
        [DataRow(null,                     null)]
        public void ExtractCodename_Cases(string tag, string expected)
        {
            Assert.AreEqual(expected, AutoUpdateChecker.ExtractCodenameFromTag(tag));
        }
    }
}

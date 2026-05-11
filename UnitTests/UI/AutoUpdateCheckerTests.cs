using System;
using System.Text.Json;
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

        [TestMethod]
        public void TrySelectMaxVersionRelease_SkipsPreReleases()
        {
            // Pre-release 1.60.00012 must NOT win against a stable 1.60.00009 —
            // matches GitHub's /releases/latest semantic and prevents staged
            // patches from auto-distributing.
            string json = """
            [
                { "tag_name": "jupiter-patch-1.60.00012", "prerelease": true },
                { "tag_name": "jupiter-patch-1.60.00009", "prerelease": false }
            ]
            """;
            using JsonDocument doc = JsonDocument.Parse(json);
            bool found = AutoUpdateChecker.TrySelectMaxVersionRelease(
                doc.RootElement, predicate: null,
                out _, out Version best);
            Assert.IsTrue(found, "Stable release should be selected when a higher pre-release exists");
            Assert.AreEqual(new Version(1, 60, 9), best);
        }

        [TestMethod]
        public void TrySelectMaxVersionRelease_AllStable_PicksHighest()
        {
            string json = """
            [
                { "tag_name": "jupiter-patch-1.60.00010", "prerelease": false },
                { "tag_name": "jupiter-patch-1.60.00012", "prerelease": false },
                { "tag_name": "jupiter-patch-1.60.00011", "prerelease": false }
            ]
            """;
            using JsonDocument doc = JsonDocument.Parse(json);
            bool found = AutoUpdateChecker.TrySelectMaxVersionRelease(
                doc.RootElement, predicate: null,
                out _, out Version best);
            Assert.IsTrue(found);
            Assert.AreEqual(new Version(1, 60, 12), best);
        }

        [TestMethod]
        public void TrySelectMaxVersionRelease_AllPreRelease_ReturnsFalse()
        {
            // If every release in the array is flagged pre-release, the scan
            // returns false and the caller falls back to no-update behavior —
            // never silently distributes a pre-release.
            string json = """
            [
                { "tag_name": "jupiter-patch-1.60.00011", "prerelease": true },
                { "tag_name": "jupiter-patch-1.60.00012", "prerelease": true }
            ]
            """;
            using JsonDocument doc = JsonDocument.Parse(json);
            bool found = AutoUpdateChecker.TrySelectMaxVersionRelease(
                doc.RootElement, predicate: null,
                out _, out _);
            Assert.IsFalse(found);
        }

        [TestMethod]
        public void TrySelectMaxVersionRelease_MissingPrereleaseField_TreatsAsStable()
        {
            // GitHub API responses always include `prerelease`, but be defensive
            // — if the field is missing for any reason, treat as stable so a
            // malformed payload can't silently hide releases.
            string json = """
            [
                { "tag_name": "jupiter-patch-1.60.00009" }
            ]
            """;
            using JsonDocument doc = JsonDocument.Parse(json);
            bool found = AutoUpdateChecker.TrySelectMaxVersionRelease(
                doc.RootElement, predicate: null,
                out _, out Version best);
            Assert.IsTrue(found);
            Assert.AreEqual(new Version(1, 60, 9), best);
        }
    }
}

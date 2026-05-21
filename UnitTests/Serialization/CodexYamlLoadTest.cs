using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDUtils;
using Ship_Game.Codex;
using Ship_Game.Data.Yaml;

namespace UnitTests.Serialization
{
    // Regression guard for the Codex.yaml schema: SDUtils' YamlParser silently
    // drops the first scalar value of an inline sequence item like
    // `- UID: foo\n  Children: ...`. Every sequence item must therefore start
    // with a bare dash on its own line. If this test ever fails on an inline
    // form, the parser has been fixed and Codex.yaml can be condensed.
    [TestClass]
    public class CodexYamlLoadTest : StarDriveTest
    {
        [TestMethod]
        public void DashOnOwnLine_DeserializesUidsAndChildren()
        {
            const string yaml = @"-
  UID: blackbox
  Children:
    -
      UID: blackbox_updates
    -
      UID: blackbox_test_download_link
      Link: ""https://example.com""
-
  UID: tutorials
  Children:
    -
      UID: tutorials_overview
      VideoPath: ""some video""
";
            using var parser = new YamlParser("test", new StringReader(yaml));
            Array<CodexEntry> entries = parser.DeserializeArray<CodexEntry>();

            AssertEqual(2, entries.Count);
            AssertEqual("blackbox", entries[0].UID);
            Assert.IsNotNull(entries[0].Children);
            AssertEqual(2, entries[0].Children.Count);
            AssertEqual("blackbox_updates", entries[0].Children[0].UID);
            AssertEqual("blackbox_test_download_link", entries[0].Children[1].UID);
            AssertEqual("https://example.com", entries[0].Children[1].Link);
            AssertEqual("tutorials", entries[1].UID);
            AssertEqual("some video", entries[1].Children[0].VideoPath);
        }

        [TestMethod]
        public void ResolveDefaults_DerivesNameIdsFromUid()
        {
            var entry = new CodexEntry
            {
                UID = "warfare_combat_basics",
                Children = new Array<CodexEntry>
                {
                    new() { UID = "warfare_combat_basics_dummy" },
                },
            };
            entry.ResolveDefaults();

            AssertEqual("CodexWarfareCombatBasics",       entry.TitleId);
            AssertEqual("CodexWarfareCombatBasicsShort",  entry.ShortDescId);
            AssertEqual("CodexWarfareCombatBasicsText",   entry.TextId);
            AssertEqual("CodexWarfareCombatBasicsDummy",  entry.Children[0].TitleId);
        }

        [TestMethod]
        public void ResolveDefaults_PreservesExplicitOverrides()
        {
            var entry = new CodexEntry
            {
                UID = "warfare_combat_basics",
                TitleId = "CustomTitle",
            };
            entry.ResolveDefaults();

            AssertEqual("CustomTitle",                    entry.TitleId);
            AssertEqual("CodexWarfareCombatBasicsShort",  entry.ShortDescId);
        }
    }
}

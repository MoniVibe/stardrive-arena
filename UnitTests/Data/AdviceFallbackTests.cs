using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;
using Ship_Game.Utils;
#pragma warning disable CA2213

namespace UnitTests.Data;

// Verifies ResourceManager.LoadRandomAdvice falls back to English when the active language
// has no Advice/<Language>/Advice.xml (e.g. Portuguese), instead of surfacing the raw
// "Advice.xml missing" placeholder on loading screens.
[TestClass]
public class AdviceFallbackTests : StarDriveTest
{
    [TestMethod]
    public void MissingLanguageFallsBackToEnglish()
    {
        Language saved = GlobalStats.Language;
        try
        {
            GlobalStats.Language = Language.Portuguese; // no Advice/Portuguese/Advice.xml exists
            string advice = ResourceManager.LoadRandomAdvice(new SeededRandom());
            Assert.AreNotEqual("Advice.xml missing", advice,
                "A language without its own Advice.xml should fall back to English");
            Assert.IsFalse(string.IsNullOrEmpty(advice), "Fallback advice should be real text");
        }
        finally
        {
            GlobalStats.Language = saved;
        }
    }

    [TestMethod]
    public void EnglishAdviceLoads()
    {
        Language saved = GlobalStats.Language;
        try
        {
            GlobalStats.Language = Language.English;
            string advice = ResourceManager.LoadRandomAdvice(new SeededRandom());
            Assert.AreNotEqual("Advice.xml missing", advice);
            Assert.IsFalse(string.IsNullOrEmpty(advice));
        }
        finally
        {
            GlobalStats.Language = saved;
        }
    }
}

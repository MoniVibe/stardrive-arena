using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;
using Vector2 = SDGraphics.Vector2;
#pragma warning disable CA2213

namespace UnitTests.Planets;

// Verifies that the auto-governor labor split adapts to sabotage:
// a sabotaged planet should NOT take the queue-biased "over-invest in
// production" branch, because SBProduction.AutoApplyProduction bails
// while IsSabotaged is true. See Planet_WorkDistribution.cs.
[TestClass]
public class TestSabotageWorkDistribution : StarDriveTest
{
    Planet Homeworld;
    Building NanoMine;

    public TestSabotageWorkDistribution()
    {
        CreateUniverseAndPlayerEmpire();
        Homeworld = AddHomeWorldToEmpire(new Vector2(2000), Player);
        NanoMine = ResourceManager.GetBuildingTemplate("Nano Mine");
        Assert.IsNotNull(NanoMine, "Nano Mine template missing from ResourceManager");
        // Seed a research topic so Res.AutoBalanceWorkers doesn't fall into
        // AutoBalanceWithZeroResearch (which dumps all leftover labor into
        // Prod and masks the difference between the work-distribution branches).
        Player.Research.SetTopic("FrigateConstruction");
        Assert.IsFalse(Player.Research.NoTopic, "Research topic should be set");
    }

    [TestMethod]
    public void IsSabotaged_TracksCrippledTurns()
    {
        Assert.IsFalse(Homeworld.IsSabotaged, "Fresh homeworld should not be sabotaged");
        Assert.IsFalse(Homeworld.IsCrippled, "Fresh homeworld should not be crippled");

        Homeworld.AddCrippledTurns(50);
        Assert.IsTrue(Homeworld.IsSabotaged, "Adding CrippledTurns should set IsSabotaged");
        Assert.IsTrue(Homeworld.IsCrippled, "IsCrippled includes the CrippledTurns case");
    }

    [TestMethod]
    public void AssignCoreWorldWorkers_SabotagedQueueBehavesLikeNoQueue()
    {
        Homeworld.CType = Planet.ColonyType.Core;
        FillProdStorage();

        // (1) No queue → storage-driven branch.
        Assert.IsTrue(Homeworld.ConstructionQueue.Count == 0, "Precondition: queue is empty");
        ResetLaborPercents();
        Homeworld.AssignCoreWorldWorkers();
        float noQueueProd = Homeworld.Prod.Percent;

        // (2) Queue + no sabotage → queue-biased branch.
        Homeworld.Construction.Enqueue(NanoMine);
        Assert.IsFalse(Homeworld.IsSabotaged, "Precondition (2): not sabotaged");
        FillProdStorage();
        ResetLaborPercents();
        Homeworld.AssignCoreWorldWorkers();
        float queuedNotSabotagedProd = Homeworld.Prod.Percent;

        string diag = $" [pop={Homeworld.PopulationBillion} prodMax={Homeworld.Prod.NetMaxPotential}" +
                      $" prodRatio={Homeworld.Storage.ProdRatio} resYPC={Homeworld.Res.YieldPerColonist}" +
                      $" noTopic={Player.Research.NoTopic}]";

        Assert.AreNotEqual(noQueueProd, queuedNotSabotagedProd, 0.0001f,
            "Sanity: with a queue and no sabotage, queue-biased branch should diverge " +
            $"from no-queue. noQueue={noQueueProd}, queued={queuedNotSabotagedProd}.{diag}");

        // (3) Queue + sabotage → should land back on the no-queue branch (gate skips bias).
        Homeworld.AddCrippledTurns(50);
        Assert.IsTrue(Homeworld.IsSabotaged, "Precondition (3): sabotaged");
        FillProdStorage();
        ResetLaborPercents();
        Homeworld.AssignCoreWorldWorkers();
        float sabotagedQueueProd = Homeworld.Prod.Percent;

        Assert.AreEqual(noQueueProd, sabotagedQueueProd, 0.0001f,
            "Sabotaged colony with a queued item should produce the same Prod.Percent " +
            "as a colony with no queue at all (storage-driven branch). " +
            $"noQueue={noQueueProd}, sabotagedQueue={sabotagedQueueProd}.");
    }

    void ResetLaborPercents()
    {
        Homeworld.Food.Percent = 0;
        Homeworld.Prod.Percent = 0;
        Homeworld.Res.Percent  = 0;
        // Refresh per-resource caches (NetMaxPotential, YieldPerColonist) — the
        // worker-assignment math reads them and they're 0-initialized until
        // UpdateIncomes() runs.
        Homeworld.UpdateIncomes();
    }

    // Fills ProdHere to cap. MinIncomePerTurn early-returns 0 once ratio > 0.9999,
    // so EstPercentForNetIncome(0) lands the storage-driven branch at its floor
    // (workers clamped to 0.1) — well below the queue-biased path.
    void FillProdStorage()
    {
        Homeworld.ProdHere = Homeworld.Storage.Max;
    }
}

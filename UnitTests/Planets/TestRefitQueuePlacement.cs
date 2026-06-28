using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;
using Ship_Game.Commands.Goals;
using Ship_Game.Ships;
using Vector2 = SDGraphics.Vector2;
#pragma warning disable CA2213

namespace UnitTests.Planets;

// Verifies SBProduction.AddToQueueAndPrioritize / PromoteRefitToFront: a refit job
// (RefitShip / RefitOrbital goal) jumps to the front of the construction queue so the
// ship gets back into service fast - unless the current front item is within
// 5 * ProductionPace turns of completion, in which case the refit slots in second so a
// nearly-finished build isn't bumped.
[TestClass]
public class TestRefitQueuePlacement : StarDriveTest
{
    Planet Homeworld;
    Building NanoMine;
    IShipDesign ScoutDesign;

    public TestRefitQueuePlacement()
    {
        CreateUniverseAndPlayerEmpire();
        Homeworld = AddHomeWorldToEmpire(new Vector2(2000), Player);
        NanoMine = ResourceManager.GetBuildingTemplate("Nano Mine");
        Assert.IsNotNull(NanoMine, "Nano Mine template missing from ResourceManager");
        ScoutDesign = ResourceManager.GetShipTemplate("Rocket Scout").ShipData;
        Assert.IsNotNull(ScoutDesign, "Rocket Scout design missing (did ReloadStarterShips run?)");
    }

    // Enqueues a plain building as the front item and overrides its cost/progress so the
    // test controls how many turns it is from completion. Returns the live QueueItem.
    QueueItem EnqueueFrontItem(float cost, float productionSpent)
    {
        Assert.IsTrue(Homeworld.Construction.Enqueue(NanoMine), "Failed to enqueue front item");
        var queue = Homeworld.Construction.GetConstructionQueue();
        QueueItem front = queue[queue.Count - 1];
        front.Cost = cost;
        front.ProductionSpent = productionSpent;
        return front;
    }

    QueueItem MakeRefit(float cost = 50f)
    {
        return new QueueItem(Homeworld)
        {
            isShip   = true,
            ShipData = ScoutDesign,
            Cost     = cost,
            QType    = QueueItemType.CombatShip,
            Goal     = new RefitShip(Player),
        };
    }

    [TestMethod]
    public void RefitJumpsToFront_WhenFrontItemIsFarFromCompletion()
    {
        Homeworld.ProdHere = 0; // no stockpile to dump into the front item
        QueueItem front = EnqueueFrontItem(cost: 1_000_000, productionSpent: 0); // many turns away

        QueueItem refit = MakeRefit();
        Homeworld.Construction.EnqueueRefitShip(refit);

        var queue = Homeworld.Construction.GetConstructionQueue();
        Assert.AreEqual(2, queue.Count);
        Assert.AreSame(refit, queue[0], "Refit should jump ahead of a front item that is many turns from completion");
        Assert.AreSame(front, queue[1]);
    }

    [TestMethod]
    public void RefitSlotsSecond_WhenFrontItemIsNearlyComplete()
    {
        Homeworld.ProdHere = 0;
        // ProductionNeeded == 0 -> 0 turns to finish, inside the 5 * ProductionPace window
        QueueItem front = EnqueueFrontItem(cost: 100, productionSpent: 100);

        QueueItem refit = MakeRefit();
        Homeworld.Construction.EnqueueRefitShip(refit);

        var queue = Homeworld.Construction.GetConstructionQueue();
        Assert.AreEqual(2, queue.Count);
        Assert.AreSame(front, queue[0], "A nearly-complete front item should not be bumped");
        Assert.AreSame(refit, queue[1], "Refit should slot in second behind the nearly-complete item");
    }

    [TestMethod]
    public void RefitOnEmptyQueue_StaysAtFront()
    {
        QueueItem refit = MakeRefit();
        Homeworld.Construction.EnqueueRefitShip(refit);

        var queue = Homeworld.Construction.GetConstructionQueue();
        Assert.AreEqual(1, queue.Count);
        Assert.AreSame(refit, queue[0]);
    }

    [TestMethod]
    public void NonRefitBuild_IsNotPromoted()
    {
        Homeworld.ProdHere = 0;
        QueueItem front = EnqueueFrontItem(cost: 1_000_000, productionSpent: 0);

        // A normal (non-refit) ship build runs through the same prioritize path but
        // must NOT jump the queue - only RefitShip / RefitOrbital goals do.
        Homeworld.Construction.Enqueue(ScoutDesign, QueueItemType.CombatShip);

        var queue = Homeworld.Construction.GetConstructionQueue();
        Assert.AreEqual(2, queue.Count);
        Assert.AreSame(front, queue[0], "Non-refit builds must not jump the queue");
    }

    [TestMethod]
    public void PlayerBuildingCompletion_DoesNotRequireNotificationManager()
    {
        Assert.IsTrue(Homeworld.Construction.Enqueue(NanoMine, playerAdded: true),
            "Failed to enqueue a manually-added building.");
        QueueItem item = Homeworld.Construction.GetConstructionQueue()[0];
        item.ProductionSpent = item.ActualCost;

        // Headless authoritative multiplayer hosts can complete queued player buildings
        // without a live notification manager attached.
        Universe.NotificationManager = null;
        Homeworld.ProdHere = 1f;

        Assert.IsTrue(Homeworld.Construction.RushProduction(0, 1f),
            "Completed building should be processed out of the queue.");
        Assert.AreEqual(0, Homeworld.Construction.Count,
            "Completed building should leave the construction queue.");
        Assert.IsTrue(Homeworld.BuildingBuilt(NanoMine.BID),
            "Completed building should be placed even when no notification manager exists.");
    }
}

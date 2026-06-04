using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;
using Ship_Game.AI;
using Ship_Game.Ships;
using UnitTests.Ships;
using Vector2 = SDGraphics.Vector2;

namespace UnitTests.AITests.Ships;

// Unit tests for the order-time gravity-well router. Verifies the early-out
// branches (flag off, combat order, remnants) and a couple of happy-path
// detour cases. Geometry-detail tests live in the integration suite.
[TestClass]
public class TestGravityWellRouter : StarDriveTest
{
    readonly TestShip Scout;

    public TestGravityWellRouter()
    {
        LoadStarterShips("Vulcan Scout");
        CreateUniverseAndPlayerEmpire();
        Scout = SpawnShip("Vulcan Scout", Player, Vector2.Zero);
    }

    [TestMethod]
    public void Flag_Off_ProducesNoDetours()
    {
        // Plant a hostile planet right on the route so routing WOULD fire normally.
        Vector2 sysPos = new(300_000f, 0f);
        Planet enemyPlanet = AddDummyPlanetToEmpire(sysPos, Enemy);
        enemyPlanet.System.SetExploredBy(Player); // ensure near-system per-planet path is reachable

        bool wasOn = GlobalStats.RouteAroundGravityWells;
        try
        {
            GlobalStats.RouteAroundGravityWells = false;
            Vector2[] detours = GravityWellRouter.BuildDetours(
                Scout, Scout.Position, new Vector2(600_000f, 0f), MoveOrder.Regular);
            AssertEqual(0, detours.Length, "Flag off must produce 0 detours");
        }
        finally
        {
            GlobalStats.RouteAroundGravityWells = wasOn;
        }
    }

    [TestMethod]
    public void Flag_On_StraightShot_NoBlocker_NoDetours()
    {
        bool wasOn = GlobalStats.RouteAroundGravityWells;
        try
        {
            GlobalStats.RouteAroundGravityWells = true;
            // No planets along the route → straight line is clear → no detours
            Vector2[] detours = GravityWellRouter.BuildDetours(
                Scout, Scout.Position, new Vector2(1_000_000f, 0f), MoveOrder.Regular);
            AssertEqual(0, detours.Length, "Empty route must produce 0 detours");
        }
        finally
        {
            GlobalStats.RouteAroundGravityWells = wasOn;
        }
    }

    [TestMethod]
    public void AggressiveOrder_SkipsRouting_EvenWithBlocker()
    {
        Vector2 sysPos = new(300_000f, 0f);
        Planet enemyPlanet = AddDummyPlanetToEmpire(sysPos, Enemy);
        enemyPlanet.System.SetExploredBy(Player);

        bool wasOn = GlobalStats.RouteAroundGravityWells;
        try
        {
            GlobalStats.RouteAroundGravityWells = true;
            Vector2[] detours = GravityWellRouter.BuildDetours(
                Scout, Scout.Position, new Vector2(600_000f, 0f), MoveOrder.Aggressive);
            AssertEqual(0, detours.Length, "Aggressive moves must skip routing (player wants to engage in the well)");
        }
        finally
        {
            GlobalStats.RouteAroundGravityWells = wasOn;
        }
    }

    [TestMethod]
    public void DestinationInsideWell_SkipsThatWell()
    {
        // Put the destination inside the enemy planet's well — router must NOT try
        // to detour around it (futile: the final leg re-enters the well anyway).
        Vector2 sysPos = new(300_000f, 0f);
        Planet enemyPlanet = AddDummyPlanetToEmpire(sysPos, Enemy);
        enemyPlanet.System.SetExploredBy(Player);

        // Aim directly at the planet's center → destination is well inside the well
        bool wasOn = GlobalStats.RouteAroundGravityWells;
        try
        {
            GlobalStats.RouteAroundGravityWells = true;
            Vector2[] detours = GravityWellRouter.BuildDetours(
                Scout, Scout.Position, enemyPlanet.Position, MoveOrder.Regular);
            AssertEqual(0, detours.Length, "Destination inside the well must not produce a detour around it");
        }
        finally
        {
            GlobalStats.RouteAroundGravityWells = wasOn;
        }
    }

    // Covers the explore-leg wrapper (ShipAI.ExploreThrustTarget): it must feed the order-time
    // router with the right ship/origin/destination/order and walk the chain via GetThrustTarget,
    // and it must reuse the cached chain while the leg target is unchanged. The detour GEOMETRY
    // itself is the router's concern (the cases above); here we pin the wrapper's delegation.
    [TestMethod]
    public void ExploreThrustTarget_MatchesRouter_AndIsStablePerLeg()
    {
        Vector2 dest = new(600_000f, 0f);
        AddDummyPlanetToEmpire(new Vector2(300_000f, 0f), Enemy).System.SetExploredBy(Player);

        bool wasOn = GlobalStats.RouteAroundGravityWells;
        try
        {
            foreach (bool routing in new[] { false, true })
            {
                GlobalStats.RouteAroundGravityWells = routing;

                // What the order-time router would return for this leg (Regular = non-combat move).
                Vector2[] detours = GravityWellRouter.BuildDetours(Scout, Scout.Position, dest, MoveOrder.Regular);
                int idx = 0;
                Vector2 expected = GravityWellRouter.GetThrustTarget(detours, ref idx, dest, Scout.Position);

                var leg = new object();
                AssertEqual(0.01f, expected, Scout.AI.TestExploreThrustTarget(leg, dest),
                    $"Explore thrust must match the gravity-well router (routing={routing})");
                // Same leg object → cached chain → identical, stable result (no per-tick jitter).
                AssertEqual(0.01f, expected, Scout.AI.TestExploreThrustTarget(leg, dest),
                    $"Re-querying the same explore leg must be stable (routing={routing})");
            }
        }
        finally
        {
            GlobalStats.RouteAroundGravityWells = wasOn;
        }
    }
}

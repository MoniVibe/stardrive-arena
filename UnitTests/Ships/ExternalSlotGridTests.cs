using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
using Vector2 = SDGraphics.Vector2;
using Point = SDGraphics.Point;

namespace UnitTests.Ships
{
    [TestClass]
    public class ExternalSlotGridTests : StarDriveTest
    {
        public ExternalSlotGridTests()
        {
            CreateUniverseAndPlayerEmpire();
            LoadStarterShips("TEST_Spearhead mk1-a");
        }

        [TestMethod]
        public void FrigateExternals()
        {
            Ship ship = SpawnShip("TEST_Spearhead mk1-a", Player, Vector2.Zero);
            AssertEqual(new Point(6, 18), new Point(ship.Grid.Width, ship.Grid.Height));
            var gs = ship.GetGridState();

            ship.Externals.DebugDump("TEST_Spearhead mk1-a", gs);

            // External slots under the orthogonal-only rule (E = external, . = internal/empty).
            // Note vs the old diagonal rule: the inner column-1 and column-4 cells (e.g. 1,2 / 4,2)
            // are now INTERNAL — their only empty neighbor is a diagonal corner, which no longer
            // counts. Only cells with an orthogonally-adjacent empty/edge slot stay external.
            //      0  1  2  3  4  5
            //  0   .  .  E  E  .  .
            //  1   .  E  E  E  E  .
            //  2   E  .  .  .  .  E
            //  3   E  .  .  .  .  E
            //  4   E  .  .  .  .  E
            //  5   .  E  .  .  E  .
            //  6   E  .  .  .  .  E
            //  7   E  .  .  .  .  E
            //  8   E  .  .  .  .  E
            //  9   E  .  .  .  .  E
            // 10   .  E  .  .  E  .
            // 11   .  E  .  .  E  .
            // 12   E  .  .  .  .  E
            // 13   E  .  .  .  .  E
            // 14   E  .  .  .  .  E
            // 15   E  .  .  .  .  E
            // 16   E  .  .  .  .  E
            // 17   .  E  E  E  E  .

            ShipModule At(int x, int y) => ship.GetModuleAt(x,y);

            AssertEqual(null,    ship.Externals.Get(gs, 0,0)); // top row
            AssertEqual(null,    ship.Externals.Get(gs, 1,0));
            AssertEqual(At(2,0), ship.Externals.Get(gs, 2,0));
            AssertEqual(At(3,0), ship.Externals.Get(gs, 3,0));
            AssertEqual(null,    ship.Externals.Get(gs, 4,0));
            AssertEqual(null,    ship.Externals.Get(gs, 5,0));

            AssertEqual(null,    ship.Externals.Get(gs, 0,1)); // second row
            AssertEqual(At(1,1), ship.Externals.Get(gs, 1,1), "must be external - NW,N,W empty");

            //ship.Externals.UpdateSlotsUnderModule(gs, At(2,1));
            AssertEqual(At(2,1), ship.Externals.Get(gs, 2,1), "must be external - top edge at hull front");
            AssertEqual(At(3,1), ship.Externals.Get(gs, 3,1), "must be external - top edge at hull front");
            AssertEqual(At(4,1), ship.Externals.Get(gs, 4,1), "must be external - N,E empty");
            AssertEqual(null,    ship.Externals.Get(gs, 5,1));

            // edges of the front section
            AssertEqual(At(0,2), ship.Externals.Get(gs, 0,2));
            AssertEqual(At(0,3), ship.Externals.Get(gs, 0,3));
            AssertEqual(At(0,4), ship.Externals.Get(gs, 0,4));
            AssertEqual(At(5,2), ship.Externals.Get(gs, 5,2));
            AssertEqual(At(5,3), ship.Externals.Get(gs, 5,3));
            AssertEqual(At(5,4), ship.Externals.Get(gs, 5,4));

            // inner corners of the front section are now INTERNAL: their only empty neighbor is a
            // diagonal corner, and orthogonally-sealed modules are protected (no corner exposure).
            AssertEqual(null, ship.Externals.Get(gs, 1,2));
            AssertEqual(null, ship.Externals.Get(gs, 1,3)); // this is a 1x2 module
            AssertEqual(null, ship.Externals.Get(gs, 1,4)); // this is a 1x2 module

            AssertEqual(null, ship.Externals.Get(gs, 4,2));
            AssertEqual(null, ship.Externals.Get(gs, 4,4)); // this is a 1x2 module
            AssertEqual(null, ship.Externals.Get(gs, 4,3)); // this is a 1x2 module

            AssertEqual(37, ship.Externals.NumModules);

            // 13 |E1x1|E1x2 1x1  1x1 |E1x2|E1x1
            // 14 |E1x1 1x1  2x2  2x2  1x1 |E1x1
            // 15 |E1x1 1x1  2x2  2x2  1x1 |E1x1
            // 16 |E1x1|E1x1 1x1  1x1 |E1x1|E1x1
            // 17 |____|E1x1|E1x1|E1x1|E1x1|____

            // kill a few engine modules, which should trigger an update to external slots
            ship.GetModuleAt(2, 17).SetHealth(0, "Test"); // this is a 1x1 engine slot
            // -1 (the dead slot) and +1 (the module above it is now orthogonally exposed) = net 0.
            // Under the old diagonal rule this exposed +2; corner-only neighbors no longer count.
            AssertEqual(37, ship.Externals.NumModules);

            // and if we resurrect the module, it should go back to previous value
            ship.GetModuleAt(2, 17).SetHealth(100, "Test");
            AssertEqual(37, ship.Externals.NumModules);

            // kill two 1x1 modules, exposing the 2x2 reactor
            ship.GetModuleAt(3, 17).SetHealth(0, "Test");
            ship.GetModuleAt(3, 16).SetHealth(0, "Test");
            AssertEqual(At(3,15), ship.Externals.Get(gs, 3,15));
            // two slots killed, but their orthogonal neighbors get exposed: net 37 -> 39
            AssertEqual(39, ship.Externals.NumModules);
        }
    }
}

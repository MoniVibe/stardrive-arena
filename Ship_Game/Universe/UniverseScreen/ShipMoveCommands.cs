using SDGraphics;
using SDUtils;
using Ship_Game.AI;
using Ship_Game.Audio;
using Ship_Game.Commands.Goals;
using Ship_Game.Fleets;
using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.Ships;
using System.Linq;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.Universe
{
    // Helper class for encapsulating Ship Movement commands
    public class ShipMoveCommands
    {
        readonly UniverseScreen Universe;
        readonly InputState Input;
        public ShipMoveCommands(UniverseScreen universe)
        {
            Universe = universe;
            Input = universe.Input;
        }

        /// Depending on User Input: Aggressive, Defensive, StandGround Movement Types
        public AI.MoveOrder GetMoveOrderType()
        {
            AI.MoveOrder addWayPoint = Input.QueueAction ? AI.MoveOrder.AddWayPoint : AI.MoveOrder.Regular;
            return addWayPoint|GetStanceType();
        }

        public AI.MoveOrder GetStanceType()
        {
            if (Input.IsCtrlKeyDown) return AI.MoveOrder.Aggressive;
            if (Input.IsAltKeyDown)  return AI.MoveOrder.StandGround;
            return AI.MoveOrder.Regular;
        }

        public bool RightClickOnShip(Ship selectedShip, Ship targetShip)
        {
            if (targetShip == null 
                || selectedShip == targetShip 
                || !Universe.LocalShipCanTakeFleetOrders(selectedShip, forAttack: true))
            {
                return false; 
            }

            AuthoritativeShipTargetOrderType targetOrder = ShipTargetOrderFor(selectedShip, targetShip);
            switch (Authoritative4XClientContext.TrySubmitShipTargetOrder(selectedShip, targetShip,
                        targetOrder, targetOrder == AuthoritativeShipTargetOrderType.Attack && Input.QueueAction))
            {
                case Authoritative4XUiCommandResult.Submitted:
                    return true;
                case Authoritative4XUiCommandResult.Blocked:
                    GameAudio.NegativeClick();
                    return false;
            }

            if (Universe.IsLocalShipForUi(targetShip))
            {
                if (!HelperFunctions.CanExitWarpForChangingDirectionByCommand([selectedShip], selectedShip.AI.PotentialTargets))
                {
                    GameAudio.NegativeClick();
                    return false;
                }
                if (selectedShip.DesignRole == RoleName.troop)
                {
                    if (targetShip.TroopCount < targetShip.TroopCapacity)
                        selectedShip.AI.OrderTroopToShip(targetShip);
                    else
                        selectedShip.AI.AddEscortGoal(targetShip);
                }
                else
                {
                    selectedShip.AI.AddEscortGoal(targetShip);
                }
            }
            else if (selectedShip.DesignRole == RoleName.troop)
                selectedShip.AI.OrderTroopToBoardShip(targetShip);
            else if (Input.QueueAction)
                selectedShip.AI.OrderQueueSpecificTarget(targetShip);
            else
                selectedShip.AI.OrderAttackSpecificTarget(targetShip);

            return true;
        }

        public void RightClickOnPlanet(Ship ship, Planet planet, bool audio = false)
        {
            Log.Assert(planet != null, "RightClickOnPlanet: planet cannot be null!");
            if (ship.IsConstructor 
                || ship.IsPlatformOrStation 
                || ship.IsSubspaceProjector 
                || !HelperFunctions.CanExitWarpForChangingDirectionByCommand([ship], ship.AI.PotentialTargets))
            {
                if (audio)
                    GameAudio.NegativeClick();

                return;
            }

            if (audio)
                GameAudio.AffirmativeClick();

            bool clearOrders = !Input.IsShiftKeyDown;
            MoveOrder moveOrder = GetStanceType();

            switch (Authoritative4XClientContext.TrySubmitShipPlanetOrder(ship, planet,
                        PlanetOrderFor(ship, planet), clearOrders, moveOrder))
            {
                case Authoritative4XUiCommandResult.Submitted:
                    return;
                case Authoritative4XUiCommandResult.Blocked:
                    if (audio)
                        GameAudio.NegativeClick();
                    return;
            }

            // if ALT key is down, always Orbit the planet
            if (Input.IsAltKeyDown)
                ship.OrderToOrbit(planet, clearOrders, moveOrder);
            else if (ship.ShipData.IsColonyShip)
                PlanetRightClickColonyShip(ship, planet, clearOrders); // This ship can colonize planets
            else if (ship.Carrier.AnyAssaultOpsAvailable)
                PlanetRightClickTroopShip(ship, planet, clearOrders, AI.MoveOrder.Regular); // This ship can assault planets
            else if (ship.HasBombs)
                PlanetRightClickBomber(ship, planet, clearOrders); // This ship can bomb planets
            else
                ship.OrderToOrbit(planet, clearOrders, moveOrder); // Default logic of right clicking
        }

        AuthoritativeShipPlanetOrderType PlanetOrderFor(Ship ship, Planet planet)
        {
            if (Input.IsAltKeyDown)
                return AuthoritativeShipPlanetOrderType.Orbit;
            if (ship.ShipData.IsColonyShip)
                return planet.Owner == null && planet.Habitable
                    ? AuthoritativeShipPlanetOrderType.Colonize
                    : AuthoritativeShipPlanetOrderType.Orbit;
            if (ship.Carrier.AnyAssaultOpsAvailable)
            {
                if (planet.Owner == ship.Loyalty)
                    return planet.ForeignTroopHere(ship.Loyalty)
                        ? AuthoritativeShipPlanetOrderType.LandTroops
                        : AuthoritativeShipPlanetOrderType.Orbit;
                if (planet.Habitable && (planet.Owner == null || ship.Loyalty.IsAtWarWith(planet.Owner)))
                    return AuthoritativeShipPlanetOrderType.LandTroops;
                return AuthoritativeShipPlanetOrderType.Orbit;
            }
            if (ship.HasBombs)
            {
                if (planet.Owner != null && planet.Owner != ship.Loyalty
                                         && ship.Loyalty.IsEmpireAttackable(planet.Owner))
                {
                    return AuthoritativeShipPlanetOrderType.Bombard;
                }
                return AuthoritativeShipPlanetOrderType.Orbit;
            }
            return AuthoritativeShipPlanetOrderType.Orbit;
        }

        void PlanetRightClickColonyShip(Ship ship, Planet planet, bool clearOrders)
        {
            if (planet.Owner == null && planet.Habitable)
            {
                Universe.Player.AI.AddGoalAndEvaluate(new MarkForColonization(ship, planet, Universe.Player));
            }
            else
            {
                ship.OrderToOrbit(planet, clearOrders);
            }
        }

        void PlanetRightClickTroopShip(Ship ship, Planet planet, bool clearOrders, AI.MoveOrder order)
        {
            if (planet.Owner != null && planet.Owner == Universe.Player)
            {
                if (ship.IsDefaultTroopTransport)
                    // Rebase to this planet if it is ours and this is a single troop transport
                    ship.AI.OrderRebase(planet, clearOrders);
                else if (planet.ForeignTroopHere(ship.Loyalty))
                    // If our planet is being invaded, land the troops there
                    ship.AI.OrderLandAllTroops(planet, clearOrders);
                else
                    ship.OrderToOrbit(planet, clearOrders, order); // Just orbit
            }
            else if (planet.Habitable)
            {
                if (planet.Owner == null || ship.Loyalty.IsAtWarWith(planet.Owner))
                {
                    // Land troops on unclaimed planets or enemy planets
                    ship.AI.OrderLandAllTroops(planet, clearOrders, Input.CursorPosition);
                }
            }
            else
            {
                ship.OrderToOrbit(planet, clearOrders, order);
            }
        }

        void PlanetRightClickBomber(Ship ship, Planet planet, bool clearOrders)
        {
            if (ship?.Active != true) return;

            if (planet.Owner != Universe.Player)
            {
                if (Universe.Player.IsEmpireAttackable(planet.Owner))
                    ship.AI.OrderBombardPlanet(planet, clearOrders);
                else
                    ship.OrderToOrbit(planet, clearOrders);
            }
            else if (Input.IsShiftKeyDown) // Owner is player
            {
                ship.AI.OrderBombardPlanet(planet, clearOrders);
            }
            else
            {
                ship.OrderToOrbit(planet, clearOrders);
            }
        }

        bool MoveFleetToPlanet(Planet planetClicked, ShipGroup fleet)
        {
            if (planetClicked == null || fleet == null)
                return false;

            Ship[] actionable = fleet.Ships.Where(s => Universe.LocalShipCanTakeFleetOrders(s, forAttack: false)).ToArray();
            bool clearOrders = !Input.IsShiftKeyDown;
            MoveOrder moveOrder = GetStanceType();
            switch (Authoritative4XClientContext.TrySubmitShipPlanetOrders(actionable, planetClicked,
                        clearOrders, moveOrder, ship => PlanetOrderFor(ship, planetClicked)))
            {
                case Authoritative4XUiCommandResult.Submitted:
                    GameAudio.AffirmativeClick();
                    return true;
                case Authoritative4XUiCommandResult.Blocked:
                    GameAudio.NegativeClick();
                    return false;
            }

            fleet.FinalPosition = planetClicked.Position; //fbedard: center fleet on planet
            foreach (Ship ship in fleet.Ships)
            {
                ResetShipsTargetAndPriorityOrders(ship);
                RightClickOnPlanet(ship, planetClicked, false);
            }

            GameAudio.AffirmativeClick();
            return true;
        }

        public bool AttackSpecificShip(Ship ship, Ship target)
        {
            if (ship.IsConstructor || ship.IsSupplyShuttle)
            {
                GameAudio.NegativeClick();
                return false;
            }

            AuthoritativeShipTargetOrderType targetOrder = ShipTargetOrderFor(ship, target);
            switch (Authoritative4XClientContext.TrySubmitShipTargetOrder(ship, target,
                        targetOrder, targetOrder == AuthoritativeShipTargetOrderType.Attack && Input.QueueAction))
            {
                case Authoritative4XUiCommandResult.Submitted:
                    GameAudio.AffirmativeClick();
                    return true;
                case Authoritative4XUiCommandResult.Blocked:
                    GameAudio.NegativeClick();
                    return false;
            }

            GameAudio.AffirmativeClick();
            if (target.Loyalty == Universe.Player)
            {
                if (ship.ShipData.Role == RoleName.troop)
                {
                    if (ship.TroopCount < ship.TroopCapacity)
                        ship.AI.OrderTroopToShip(target);
                    else
                        ship.AI.AddEscortGoal(target);
                }
                else
                    ship.AI.AddEscortGoal(target);
                return true;
            }

            {
                if (ship.ShipData.Role == RoleName.troop)
                    ship.AI.OrderTroopToBoardShip(target);
                else if (Input.QueueAction)
                    ship.AI.OrderQueueSpecificTarget(target);
                else
                    ship.AI.OrderAttackSpecificTarget(target);
            }
            return true;
        }

        bool TryFleetAttackShip(ShipGroup fleet, Ship shipToAttack)
        {
            Ship[] actionable = fleet.Ships.Where(s => Universe.LocalShipCanTakeFleetOrders(s, forAttack: true)).ToArray();
            switch (Authoritative4XClientContext.TrySubmitShipTargetOrders(actionable, shipToAttack,
                        Input.QueueAction, ship => ShipTargetOrderFor(ship, shipToAttack)))
            {
                case Authoritative4XUiCommandResult.Submitted:
                    GameAudio.AffirmativeClick();
                    return true;
                case Authoritative4XUiCommandResult.Blocked:
                    GameAudio.NegativeClick();
                    return false;
            }

            fleet.FinalPosition = shipToAttack.Position;
            fleet.AssignPositions(Vectors.Up);
            if (fleet.Ships.Any(s => s.Active
                   && s.Position.Distance(shipToAttack.Position) > 7500f
                   && !HelperFunctions.CanExitWarpForChangingDirectionByCommand([s], s.AI.PotentialTargets)))
            {
                GameAudio.NegativeClick();
                return false;
            }

            foreach (Ship fleetShip in fleet.Ships)
            {
                if (Universe.LocalShipCanTakeFleetOrders(fleetShip, forAttack: true))
                {
                    ResetShipsTargetAndPriorityOrders(fleetShip);
                    AttackSpecificShip(fleetShip, shipToAttack);
                }
            }

            GameAudio.AffirmativeClick();
            return true;
        }

        bool QueueFleetMovement(Vector2 movePosition, Vector2 direction, ShipGroup fleet)
        {
            if (Input.QueueAction && fleet.Ships[0].AI.HasWayPoints)
            {
                foreach (Ship ship in fleet.Ships)
                {
                    ResetShipsTargetAndPriorityOrders(ship);
                    ship.AI.ClearOrdersIfCombat();
                }

                fleet.MoveTo(movePosition, direction, GetMoveOrderType());
                GameAudio.AffirmativeClick();
                return true;
            }

            return false;
        }

        public void MoveFleetToLocation(Ship[] enemyShips, Ship shipClicked, Planet planetClicked,
            Vector2 movePosition, Vector2 facingDir, ShipGroup fleet = null)
        {
            fleet = fleet ?? Universe.SelectedFleet;
            if (shipClicked != null && !Universe.IsLocalShipForUi(shipClicked))
            {
                TryFleetAttackShip(fleet, shipClicked);
                return;
            }

            if (MoveFleetToPlanet(planetClicked, fleet))
                return;

            Vector2 corrected = HelperFunctions.GetCorrectedMovePosWithAudio(fleet.Ships, enemyShips, movePosition);
            AI.MoveOrder shipOrder = GetMoveOrderType();
            AI.MoveOrder fleetOrder = shipOrder;
            if (!(Input.QueueAction && fleet.Ships[0].AI.HasWayPoints))
                fleetOrder |= AI.MoveOrder.ForceReassembly;

            Authoritative4XUiCommandResult authoritative = fleet is Fleet assignedFleet
                ? Authoritative4XClientContext.TrySubmitMoveFleet(assignedFleet, corrected, facingDir, fleetOrder)
                : Authoritative4XUiCommandResult.NotActive;
            if (authoritative == Authoritative4XUiCommandResult.NotActive)
            {
                Ship[] actionable = fleet.Ships.Where(s => Universe.LocalShipCanTakeFleetOrders(s, forAttack: false)).ToArray();
                authoritative = Authoritative4XClientContext.TrySubmitMoveShips(actionable, corrected, shipOrder);
            }
            switch (authoritative)
            {
                case Authoritative4XUiCommandResult.Submitted:
                    GameAudio.AffirmativeClick();
                    return;
                case Authoritative4XUiCommandResult.Blocked:
                    GameAudio.NegativeClick();
                    return;
            }

            GameAudio.AffirmativeClick();
            if (QueueFleetMovement(movePosition, facingDir, fleet))
                return;

            // ForceReassembly so AssembleFleet rotates every ship's FleetOffset to the
            // new facingDir even for ships already moving; without it, AssembleFleet
            // only touches AwaitingOrders ships and the fleet keeps its old formation
            // orientation after a right-drag rotate.
            fleet.MoveTo(corrected, facingDir, fleetOrder);
        }

        void ResetShipsTargetAndPriorityOrders(Ship ship)
        {
            ship.AI.Target = null;
            if (Universe.LocalShipCanTakeFleetOrders(ship))
                ship.AI.ResetPriorityOrder(!Input.QueueAction);
        }

        public void MoveShipToLocation(Ship[] enemyShips, Vector2 pos, Vector2 direction, Ship ship)
        {
            if (ship.IsPlatformOrStation || !HelperFunctions.CanExitWarpForChangingDirectionByCommand([ship], enemyShips))
            {
                GameAudio.NegativeClick();
                return;
            }

            Vector2 corrected = HelperFunctions.GetCorrectedMovePosWithAudio([ship], enemyShips, pos);
            MoveOrder order = GetMoveOrderType();
            switch (Authoritative4XClientContext.TrySubmitMoveShip(ship, corrected, order))
            {
                case Authoritative4XUiCommandResult.Submitted:
                    return;
                case Authoritative4XUiCommandResult.Blocked:
                    GameAudio.NegativeClick();
                    return;
            }

            ship.AI.OrderMoveTo(corrected, direction, order);
        }

        AuthoritativeShipTargetOrderType ShipTargetOrderFor(Ship ship, Ship target)
        {
            if (Universe.IsLocalShipForUi(target))
            {
                return IsSingleTroopTargetOrderShip(ship)
                       && ship.TroopCount > 0
                       && target.TroopCapacity > target.TroopCount
                    ? AuthoritativeShipTargetOrderType.TransferTroops
                    : AuthoritativeShipTargetOrderType.Escort;
            }

            return IsSingleTroopTargetOrderShip(ship)
                ? AuthoritativeShipTargetOrderType.Board
                : AuthoritativeShipTargetOrderType.Attack;
        }

        static bool IsSingleTroopTargetOrderShip(Ship ship)
            => ship?.DesignRole == RoleName.troop || ship?.ShipData.Role == RoleName.troop;
    }
}

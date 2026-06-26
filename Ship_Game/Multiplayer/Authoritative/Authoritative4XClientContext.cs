using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Ship_Game;
using Ship_Game.AI;
using Ship_Game.Commands.Goals;
using Ship_Game.Fleets;
using Ship_Game.Ships;
using Ship_Game.Ships.AI;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.Multiplayer.Authoritative;

public enum Authoritative4XUiCommandResult
{
    NotActive,
    Submitted,
    Blocked,
}

/// <summary>
/// Runtime seam used by live UI screens when they are rendering a passive authoritative
/// multiplayer client. In single-player this is inactive, so screens keep their existing
/// direct-mutation behavior.
/// </summary>
public sealed class Authoritative4XClientContext : IDisposable
{
    static Authoritative4XClientContext Active;

    readonly Authoritative4XClientContext Previous;
    readonly Action<AuthoritativePlayerCommand> SubmitCommand;
    int NextSequence;
    bool Disposed;

    public readonly int PeerId;
    public readonly int EmpireId;

    Authoritative4XClientContext(int peerId, int empireId, int firstSequence,
        Action<AuthoritativePlayerCommand> submitCommand)
    {
        PeerId = peerId;
        EmpireId = empireId;
        NextSequence = firstSequence;
        SubmitCommand = submitCommand ?? throw new ArgumentNullException(nameof(submitCommand));
        Previous = Active;
        Active = this;
    }

    public static bool IsActive => Active != null;

    public static Authoritative4XClientContext Begin(int peerId, int empireId,
        Action<AuthoritativePlayerCommand> submitCommand, int firstSequence = 1)
    {
        return new Authoritative4XClientContext(peerId, empireId, firstSequence, submitCommand);
    }

    public static bool TrySubmitSetColonyType(Planet planet, Planet.ColonyType type)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return false;

        context.Submit(AuthoritativePlayerCommand.SetColonyType(context.Next(), context.EmpireId, planet.Id, type));
        return true;
    }

    public static Authoritative4XUiCommandResult TrySubmitSetColonizationGoal(Empire empire, Planet planet,
        bool enabled)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (planet == null || planet.Owner != null || !planet.Habitable)
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.SetColonizationGoal(context.Next(), context.EmpireId,
            planet.Id, enabled));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitQueueDeepSpaceBuild(Empire empire, IShipDesign design,
        Vector2 buildPosition, Planet targetPlanet, SolarSystem targetSystem, Vector2 tetherOffset)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (design == null || string.IsNullOrWhiteSpace(design.Name) || !empire.CanBuildStation(design))
            return Authoritative4XUiCommandResult.Blocked;
        if (!float.IsFinite(buildPosition.X) || !float.IsFinite(buildPosition.Y)
            || !float.IsFinite(tetherOffset.X) || !float.IsFinite(tetherOffset.Y))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }
        if (targetPlanet != null && targetSystem != null && targetPlanet.System != targetSystem)
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.QueueDeepSpaceBuild(context.Next(), context.EmpireId,
            design.Name, buildPosition, targetPlanet?.Id ?? 0, targetSystem?.Id ?? 0, tetherOffset));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitQueuePlanetOrbitalBuild(Planet planet, IShipDesign design)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!CanQueuePlanetOrbitalBuild(planet.Owner, planet, design))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.QueuePlanetOrbitalBuild(context.Next(), context.EmpireId,
            planet.Id, design.Name));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitApplyColonyBlueprints(Planet planet,
        BlueprintsTemplate template)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!CanSubmitColonyBlueprints(template))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.ApplyColonyBlueprints(context.Next(), context.EmpireId,
            planet.Id, template));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitClearColonyBlueprints(Planet planet)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;

        context.Submit(AuthoritativePlayerCommand.ClearColonyBlueprints(context.Next(), context.EmpireId,
            planet.Id));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitScrapColonyBuilding(Planet planet,
        PlanetGridSquare tile, string buildingName)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (tile == null || tile.P != planet || tile.Building == null || !tile.Building.Scrappable
            || string.IsNullOrWhiteSpace(buildingName)
            || !string.Equals(tile.Building.Name, buildingName, StringComparison.Ordinal))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.ScrapColonyTile(context.Next(), context.EmpireId,
            planet.Id, tile.X, tile.Y, AuthoritativeColonyTileScrapKind.Building, buildingName));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitScrapColonyBiosphere(Planet planet,
        PlanetGridSquare tile)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (tile == null || tile.P != planet || !tile.Biosphere)
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.ScrapColonyTile(context.Next(), context.EmpireId,
            planet.Id, tile.X, tile.Y, AuthoritativeColonyTileScrapKind.Biosphere));
        return Authoritative4XUiCommandResult.Submitted;
    }

    static bool CanQueuePlanetOrbitalBuild(Empire empire, Planet planet, IShipDesign design)
    {
        if (empire == null || planet?.Owner != empire || design == null || string.IsNullOrWhiteSpace(design.Name))
            return false;
        if (planet.IsOutOfOrbitalsLimit(design))
            return false;
        if (design.IsShipyard)
            return empire.CanBuildShipyards && empire.CanBuildShip(design);
        return design.IsPlatformOrStation && empire.CanBuildStation(design);
    }

    static bool CanSubmitColonyBlueprints(BlueprintsTemplate template)
    {
        if (template == null
            || string.IsNullOrWhiteSpace(template.Name)
            || !string.IsNullOrEmpty(template.LinkTo)
            || template.PlannedBuildings == null
            || template.PlannedBuildings.Count == 0
            || template.PlannedBuildings.Count > AuthoritativePlayerCommand.MaxColonyBlueprintBuildings
            || !Enum.IsDefined(typeof(Planet.ColonyType), template.ColonyType)
            || template.ColonyType == Planet.ColonyType.TradeHub)
        {
            return false;
        }

        return template.PlannedBuildings.All(name => !string.IsNullOrWhiteSpace(name)
            && ResourceManager.GetBuilding(name, out Building building)
            && building.IsSuitableForBlueprints);
    }

    public static Authoritative4XUiCommandResult TrySubmitCancelDeepSpaceBuild(Empire empire, Goal goal)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!IsDeepSpaceBuildStateGoal(goal)
            || string.IsNullOrWhiteSpace(goal.ToBuild?.Name)
            || !float.IsFinite(goal.BuildPosition.X)
            || !float.IsFinite(goal.BuildPosition.Y))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.CancelDeepSpaceBuild(context.Next(), context.EmpireId,
            goal.ToBuild.Name, goal.Type, goal.BuildPosition, goal.TargetPlanet?.Id ?? 0,
            DeepSpaceGoalSystem(goal)?.Id ?? 0));
        return Authoritative4XUiCommandResult.Submitted;
    }

    static bool IsDeepSpaceBuildStateGoal(Goal goal)
        => goal is DeepSpaceBuildGoal or ProcessResearchStation or MiningOps;

    static SolarSystem DeepSpaceGoalSystem(Goal goal)
        => goal switch
        {
            DeepSpaceBuildGoal deepSpace => deepSpace.TargetSystem,
            ProcessResearchStation research => research.TargetSystem,
            MiningOps mining => mining.TargetSystem,
            _ => null,
        };

    public static bool TrySubmitSetColonyLabor(Planet planet, float food, float production, float research,
        bool foodLocked, bool productionLocked, bool researchLocked)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return false;

        context.Submit(AuthoritativePlayerCommand.SetColonyLabor(context.Next(), context.EmpireId, planet.Id,
            food, production, research, foodLocked, productionLocked, researchLocked));
        return true;
    }

    public static bool TrySubmitCurrentColonyLabor(Planet planet)
    {
        if (planet == null)
            return false;
        return TrySubmitSetColonyLabor(planet, planet.Food.Percent, planet.Prod.Percent, planet.Res.Percent,
            planet.Food.PercentLock, planet.Prod.PercentLock, planet.Res.PercentLock);
    }

    public static bool TrySubmitQueueBuilding(Planet planet, string buildingName, PlanetGridSquare where = null)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return false;
        if (string.IsNullOrEmpty(buildingName))
            return false;

        AuthoritativePlayerCommand command = where != null
            ? AuthoritativePlayerCommand.QueueBuilding(context.Next(), context.EmpireId, planet.Id,
                buildingName, where.X, where.Y)
            : AuthoritativePlayerCommand.QueueBuilding(context.Next(), context.EmpireId, planet.Id, buildingName);
        context.Submit(command);
        return true;
    }

    public static Authoritative4XUiCommandResult TrySubmitQueueShip(Planet planet, IShipDesign ship, int repeat)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Authoritative4XUiCommandResult.NotActive;
        if (ship == null || ship.IsPlatformOrStation || ship.IsShipyard)
            return Authoritative4XUiCommandResult.Blocked;

        int count = Math.Max(1, repeat);
        for (int i = 0; i < count; ++i)
            context.Submit(AuthoritativePlayerCommand.QueueBuild(context.Next(), context.EmpireId, planet.Id, ship.Name));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitMoveShip(Ship ship, Vector2 destination, MoveOrder order)
    {
        if (!TryGetFor(ship?.Loyalty, out Authoritative4XClientContext context))
            return Authoritative4XUiCommandResult.NotActive;
        if (ship?.Active != true || ship.IsPlatformOrStation)
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.MoveShip(context.Next(), context.EmpireId, ship.Id,
            destination, order));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitMoveShips(Ship[] ships, Vector2 destination, MoveOrder order)
    {
        if (!TryGetForBatch(ships, out Authoritative4XClientContext context, out Ship[] ownedShips))
            return Authoritative4XUiCommandResult.NotActive;
        if (ownedShips.Length == 0 || ownedShips.Any(s => !CanSubmitShipMove(s)))
            return Authoritative4XUiCommandResult.Blocked;

        foreach (Ship ship in ownedShips)
            context.Submit(AuthoritativePlayerCommand.MoveShip(context.Next(), context.EmpireId, ship.Id,
            destination, order));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitMoveFleet(Fleet fleet, Vector2 destination,
        Vector2 direction, MoveOrder order)
    {
        if (!TryGetFor(fleet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (fleet.Key is < Empire.FirstFleetKey or > Empire.LastFleetKey
            || fleet.Ships.Count == 0
            || fleet.Ships.All(s => !CanSubmitFleetMoveShip(s)))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.MoveFleet(context.Next(), context.EmpireId,
            fleet.Key, destination, direction, order));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitRenameFleet(Fleet fleet, string name)
    {
        if (!TryGetFor(fleet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (fleet.Key is < Empire.FirstFleetKey or > Empire.LastFleetKey || !IsLegalFleetName(name))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.RenameFleet(context.Next(), context.EmpireId,
            fleet.Key, name.Trim()));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitAutoArrangeFleet(Fleet fleet)
    {
        if (!TryGetFor(fleet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (fleet.Key is < Empire.FirstFleetKey or > Empire.LastFleetKey || fleet.Ships.Count == 0)
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.AutoArrangeFleet(context.Next(), context.EmpireId, fleet.Key));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitSetFleetLayout(Fleet fleet,
        IEnumerable<FleetDataNode> nodes)
    {
        if (!TryGetFor(fleet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (fleet.Key is < Empire.FirstFleetKey or > Empire.LastFleetKey)
            return Authoritative4XUiCommandResult.Blocked;

        FleetDataNode[] layout = (nodes ?? Array.Empty<FleetDataNode>()).ToArray();
        if (layout.Length > 200
            || layout.Any(n => n == null || n.Goal != null || string.IsNullOrWhiteSpace(n.ShipName ?? n.Ship?.Name)))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        Ship[] ships = layout.Select(n => n.Ship).Where(s => s != null).ToArray();
        if (ships.Any(s => s.Active != true || s.Loyalty?.Id != context.EmpireId || !s.CanBeAddedToFleets())
            || ships.Select(s => s.Id).Distinct().Count() != ships.Length)
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        if (layout.Any(n => !IsFiniteFleetLayoutNode(n) || !Enum.IsDefined(typeof(CombatState), n.CombatState)))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.SetFleetLayout(context.Next(), context.EmpireId,
            fleet.Key, layout));
        return Authoritative4XUiCommandResult.Submitted;
    }

    static bool IsFiniteFleetLayoutNode(FleetDataNode node)
        => float.IsFinite(node.RelativeFleetOffset.X)
           && float.IsFinite(node.RelativeFleetOffset.Y)
           && float.IsFinite(node.VultureWeight)
           && float.IsFinite(node.AttackShieldedWeight)
           && float.IsFinite(node.AssistWeight)
           && float.IsFinite(node.DefenderWeight)
           && float.IsFinite(node.DPSWeight)
           && float.IsFinite(node.SizeWeight)
           && float.IsFinite(node.ArmoredWeight)
           && float.IsFinite(node.OrdersRadius);

    public static Authoritative4XUiCommandResult TrySubmitLoadFleetPatrol(Fleet fleet, FleetPatrol patrol)
    {
        if (!TryGetFor(fleet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (fleet.Key is < Empire.FirstFleetKey or > Empire.LastFleetKey
            || fleet.Ships.Count == 0
            || patrol == null
            || string.IsNullOrWhiteSpace(patrol.Name)
            || !fleet.Owner.FleetPatrols.Any(p => string.Equals(p.Name, patrol.Name, StringComparison.Ordinal)))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.LoadFleetPatrol(context.Next(), context.EmpireId,
            fleet.Key, patrol.Name));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitRenameFleetPatrol(Empire empire,
        FleetPatrol patrol, string newName)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (patrol == null
            || !empire.FleetPatrols.Any(p => ReferenceEquals(p, patrol))
            || !AuthoritativePlayerCommand.IsLegalPatrolName(newName)
            || string.Equals(patrol.Name, newName, StringComparison.Ordinal)
            || empire.FleetPatrols.Any(p => !ReferenceEquals(p, patrol)
                                            && string.Equals(p.Name, newName, StringComparison.Ordinal)))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.RenameFleetPatrol(context.Next(), context.EmpireId,
            patrol.Name, newName));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitDeleteFleetPatrol(Empire empire, FleetPatrol patrol)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (patrol == null
            || string.IsNullOrWhiteSpace(patrol.Name)
            || !empire.FleetPatrols.Any(p => ReferenceEquals(p, patrol)))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.DeleteFleetPatrol(context.Next(), context.EmpireId, patrol.Name));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitClearFleetPatrol(Fleet fleet)
    {
        if (!TryGetFor(fleet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (fleet.Key is < Empire.FirstFleetKey or > Empire.LastFleetKey
            || fleet.Ships.Count == 0
            || !fleet.HasPatrolPlan)
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.ClearFleetPatrol(context.Next(), context.EmpireId, fleet.Key));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitCreateFleetPatrol(Fleet fleet, WayPoint[] waypoints)
    {
        if (!TryGetFor(fleet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (fleet.Key is < Empire.FirstFleetKey or > Empire.LastFleetKey
            || fleet.Ships.Count == 0
            || fleet.HasPatrolPlan
            || waypoints == null
            || !AuthoritativePlayerCommand.ArePatrolWaypointCountsValid(waypoints.Length)
            || waypoints.Any(w => !float.IsFinite(w.Position.X) || !float.IsFinite(w.Position.Y)
                                  || !float.IsFinite(w.Direction.X) || !float.IsFinite(w.Direction.Y)))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.CreateFleetPatrol(context.Next(), context.EmpireId,
            fleet.Key, waypoints));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitSetFleetAssignment(Empire empire, int fleetKey,
        AuthoritativeFleetAssignmentMode mode, Ship[] ships)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (fleetKey is < Empire.FirstFleetKey or > Empire.LastFleetKey
            || !Enum.IsDefined(typeof(AuthoritativeFleetAssignmentMode), mode))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        Ship[] selected = ships ?? Array.Empty<Ship>();
        if (mode == AuthoritativeFleetAssignmentMode.Add && selected.Length == 0)
            return Authoritative4XUiCommandResult.Blocked;
        if ((mode is AuthoritativeFleetAssignmentMode.Replace or AuthoritativeFleetAssignmentMode.Add)
            && selected.Any(s => s?.Active != true || s.Loyalty?.Id != context.EmpireId || !s.CanBeAddedToFleets()))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.SetFleetAssignment(context.Next(), context.EmpireId,
            fleetKey, mode, selected.Select(s => s.Id).Distinct()));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitShipSpecialOrder(Ship ship,
        AuthoritativeShipSpecialOrderType orderType)
    {
        if (!TryGetFor(ship?.Loyalty, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!Enum.IsDefined(typeof(AuthoritativeShipSpecialOrderType), orderType)
            || !CanSubmitShipSpecialOrder(ship, orderType))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.ShipSpecialOrder(context.Next(), context.EmpireId,
            ship.Id, orderType));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitShipLifecycleOrder(Ship ship,
        AuthoritativeShipLifecycleOrderType orderType)
    {
        if (!TryGetFor(ship?.Loyalty, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!Enum.IsDefined(typeof(AuthoritativeShipLifecycleOrderType), orderType)
            || !CanSubmitShipLifecycleOrder(ship, orderType))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.ShipLifecycleOrder(context.Next(), context.EmpireId,
            ship.Id, orderType));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitShipLifecycleOrder(Ship[] ships,
        AuthoritativeShipLifecycleOrderType orderType)
    {
        if (!TryGetForBatch(ships, out Authoritative4XClientContext context, out Ship[] ownedShips))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!Enum.IsDefined(typeof(AuthoritativeShipLifecycleOrderType), orderType)
            || ownedShips.Length == 0
            || ownedShips.Any(s => !CanSubmitShipLifecycleOrder(s, orderType)))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        foreach (Ship ship in ownedShips)
            context.Submit(AuthoritativePlayerCommand.ShipLifecycleOrder(context.Next(), context.EmpireId,
                ship.Id, orderType));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitSetShipCombatStance(Ship ship, CombatState stance)
    {
        if (!TryGetFor(ship?.Loyalty, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!IsLegalCombatStance(stance) || !CanSubmitShipCombatStance(ship))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.SetShipCombatStance(context.Next(), context.EmpireId,
            ship.Id, stance));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitSetShipCombatStance(Ship[] ships, CombatState stance)
    {
        if (!TryGetForBatch(ships, out Authoritative4XClientContext context, out Ship[] ownedShips))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!IsLegalCombatStance(stance) || ownedShips.Length == 0 || ownedShips.Any(s => !CanSubmitShipCombatStance(s)))
            return Authoritative4XUiCommandResult.Blocked;

        foreach (Ship ship in ownedShips)
            context.Submit(AuthoritativePlayerCommand.SetShipCombatStance(context.Next(), context.EmpireId,
                ship.Id, stance));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitAttackShip(Ship ship, Ship target, bool queue)
    {
        if (!TryGetFor(ship?.Loyalty, out Authoritative4XClientContext context))
            return Authoritative4XUiCommandResult.NotActive;
        if (ship?.Active != true || target?.Active != true || ship == target
            || ship.IsPlatformOrStation || ship.ShipData.Role == RoleName.troop
            || target.Loyalty == null || target.Loyalty == ship.Loyalty)
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.AttackShip(context.Next(), context.EmpireId, ship.Id,
            target.Id, queue));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitAttackShips(Ship[] ships, Ship target, bool queue)
    {
        if (!TryGetForBatch(ships, out Authoritative4XClientContext context, out Ship[] ownedShips))
            return Authoritative4XUiCommandResult.NotActive;
        if (target?.Active != true || target.Loyalty == null || ownedShips.Length == 0
            || ownedShips.Any(s => !CanSubmitShipAttack(s, target)))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        foreach (Ship ship in ownedShips)
            context.Submit(AuthoritativePlayerCommand.AttackShip(context.Next(), context.EmpireId, ship.Id,
                target.Id, queue));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitShipPlanetOrder(Ship ship, Planet planet,
        AuthoritativeShipPlanetOrderType orderType, bool clearOrders, MoveOrder moveOrder)
    {
        if (!TryGetFor(ship?.Loyalty, out Authoritative4XClientContext context))
            return Authoritative4XUiCommandResult.NotActive;
        if (ship?.Active != true || planet == null
            || ship.IsConstructor || ship.IsPlatformOrStation || ship.IsSubspaceProjector)
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.ShipPlanetOrder(context.Next(), context.EmpireId, ship.Id,
            planet.Id, orderType, clearOrders, moveOrder));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitShipPlanetOrders(Ship[] ships, Planet planet,
        bool clearOrders, MoveOrder moveOrder, Func<Ship, AuthoritativeShipPlanetOrderType> orderFor)
    {
        if (!TryGetForBatch(ships, out Authoritative4XClientContext context, out Ship[] ownedShips))
            return Authoritative4XUiCommandResult.NotActive;
        if (planet == null || ownedShips.Length == 0 || orderFor == null
            || ownedShips.Any(s => !CanSubmitShipPlanetOrder(s)))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        foreach (Ship ship in ownedShips)
            context.Submit(AuthoritativePlayerCommand.ShipPlanetOrder(context.Next(), context.EmpireId,
                ship.Id, planet.Id, orderFor(ship), clearOrders, moveOrder));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static bool TrySubmitSetResearchTopic(Empire empire, string techUid)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return false;
        if (string.IsNullOrEmpty(techUid))
            return false;

        context.Submit(AuthoritativePlayerCommand.SetResearchTopic(context.Next(), context.EmpireId, techUid));
        return true;
    }

    public static Authoritative4XUiCommandResult TrySubmitQueueResearch(Empire empire, string techUid)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (string.IsNullOrEmpty(techUid))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.QueueResearch(context.Next(), context.EmpireId, techUid));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitRemoveResearchQueueItem(Empire empire, string techUid)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (string.IsNullOrEmpty(techUid))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.RemoveResearchQueueItem(context.Next(), context.EmpireId, techUid));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitMoveResearchQueueItem(Empire empire, string techUid,
        AuthoritativeResearchQueueMove move)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (string.IsNullOrEmpty(techUid))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.MoveResearchQueueItem(context.Next(), context.EmpireId,
            techUid, move));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static bool TrySubmitSetEmpireBudget(Empire empire, float taxRate, float treasuryGoal, bool autoTaxes)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return false;

        context.Submit(AuthoritativePlayerCommand.SetEmpireBudget(context.Next(), context.EmpireId,
            taxRate, treasuryGoal, autoTaxes));
        return true;
    }

    public static Authoritative4XUiCommandResult TrySubmitDesignShip(Empire empire, ShipDesign design)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Authoritative4XUiCommandResult.NotActive;
        if (design == null || string.IsNullOrEmpty(design.Name))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.DesignShip(context.Next(), context.EmpireId,
            design.GetBase64DesignString()));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitDiplomacyProposal(Empire target,
        AuthoritativeDiplomacyProposalType proposalType, string terms = "")
    {
        if (Active == null)
            return Authoritative4XUiCommandResult.NotActive;
        if (target == null || target.Id == Active.EmpireId)
            return Authoritative4XUiCommandResult.Blocked;

        Active.Submit(AuthoritativePlayerCommand.DiplomacyProposal(Active.Next(), Active.EmpireId,
            target.Id, proposalType, terms));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitDiplomacyResponse(int proposalId,
        AuthoritativeDiplomacyResponseKind response, string terms = "")
    {
        if (Active == null)
            return Authoritative4XUiCommandResult.NotActive;
        if (proposalId <= 0)
            return Authoritative4XUiCommandResult.Blocked;

        Active.Submit(AuthoritativePlayerCommand.DiplomacyResponse(Active.Next(), Active.EmpireId,
            proposalId, response, terms));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitQueueTroop(Planet planet, Troop troop, int repeat)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Authoritative4XUiCommandResult.NotActive;
        if (troop == null || string.IsNullOrEmpty(troop.Name))
            return Authoritative4XUiCommandResult.Blocked;

        int count = Math.Max(1, repeat);
        for (int i = 0; i < count; ++i)
            context.Submit(AuthoritativePlayerCommand.QueueTroop(context.Next(), context.EmpireId, planet.Id, troop.Name));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitCancelConstructionQueueItem(Planet planet, QueueItem item)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;

        int queueIndex = QueueIndexOf(planet, item);
        if (queueIndex < 0 || item.IsComplete)
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.CancelConstructionQueueItem(context.Next(), context.EmpireId,
            planet.Id, queueIndex));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitReorderConstructionQueueItem(Planet planet,
        QueueItem item, int moveToIndex)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;

        int queueIndex = QueueIndexOf(planet, item);
        if (queueIndex < 0 || (uint)moveToIndex >= planet.ConstructionQueue.Count)
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.ReorderConstructionQueueItem(context.Next(), context.EmpireId,
            planet.Id, queueIndex, moveToIndex));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitReorderConstructionQueueItemRelative(Planet planet,
        QueueItem item, int relativeChange)
    {
        int queueIndex = QueueIndexOf(planet, item);
        if (queueIndex < 0)
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        return TrySubmitReorderConstructionQueueItem(planet, item, queueIndex + relativeChange);
    }

    public static Authoritative4XUiCommandResult TrySubmitRushConstructionQueueItem(Planet planet,
        QueueItem item, float maxAmount)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;

        int queueIndex = QueueIndexOf(planet, item);
        if (queueIndex < 0 || item.IsComplete || float.IsNaN(maxAmount) || float.IsInfinity(maxAmount) || maxAmount <= 0f)
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.RushConstructionQueueItem(context.Next(), context.EmpireId,
            planet.Id, queueIndex, maxAmount));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitToggleConstructionRush(Planet planet, QueueItem item)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;

        int queueIndex = QueueIndexOf(planet, item);
        if (queueIndex < 0 || item.IsComplete)
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.ToggleConstructionRush(context.Next(), context.EmpireId,
            planet.Id, queueIndex));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitSetPlanetGoodsState(Planet planet,
        AuthoritativePlanetGoodsKind goods, Planet.GoodState state)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!Enum.IsDefined(typeof(AuthoritativePlanetGoodsKind), goods)
            || !Enum.IsDefined(typeof(Planet.GoodState), state)
            || goods == AuthoritativePlanetGoodsKind.Food && !planet.NonCybernetic)
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.SetPlanetGoodsState(context.Next(), context.EmpireId,
            planet.Id, goods, state));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitSetPlanetPrioritizedPort(Planet planet,
        bool prioritized)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (prioritized && !planet.HasSpacePort)
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.SetPlanetPrioritizedPort(context.Next(), context.EmpireId,
            planet.Id, prioritized));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitSetPlanetManualBudget(Planet planet,
        AuthoritativePlanetBudgetKind budget, float value)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!Enum.IsDefined(typeof(AuthoritativePlanetBudgetKind), budget) || !float.IsFinite(value) || value < 0f)
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.SetPlanetManualBudget(context.Next(), context.EmpireId,
            planet.Id, budget, value));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitSetPlanetGovernorOptions(Planet planet)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;

        context.Submit(AuthoritativePlayerCommand.SetPlanetGovernorOptions(context.Next(), context.EmpireId,
            planet.Id, PlanetGovernorOptions(planet)));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitSetPlanetManualTradeSlots(Planet planet,
        int foodImport, int prodImport, int coloImport, int foodExport, int prodExport, int coloExport)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!AuthoritativePlayerCommand.AreManualTradeSlotsValid(foodImport, prodImport, coloImport,
                foodExport, prodExport, coloExport))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.SetPlanetManualTradeSlots(context.Next(), context.EmpireId,
            planet.Id, foodImport, prodImport, coloImport, foodExport, prodExport, coloExport));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitSetPlanetDefenseTargets(Planet planet,
        int garrisonSize, int wantedPlatforms, int wantedShipyards, int wantedStations)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!AuthoritativePlayerCommand.ArePlanetDefenseTargetsValid(garrisonSize, wantedPlatforms,
                wantedShipyards, wantedStations))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.SetPlanetDefenseTargets(context.Next(), context.EmpireId,
            planet.Id, garrisonSize, wantedPlatforms, wantedShipyards, wantedStations));
        return Authoritative4XUiCommandResult.Submitted;
    }

    static AuthoritativePlanetGovernorOptions PlanetGovernorOptions(Planet planet)
    {
        AuthoritativePlanetGovernorOptions options = AuthoritativePlanetGovernorOptions.None;
        if (planet.GovOrbitals) options |= AuthoritativePlanetGovernorOptions.GovOrbitals;
        if (planet.AutoBuildTroops) options |= AuthoritativePlanetGovernorOptions.AutoBuildTroops;
        if (planet.DontScrapBuildings) options |= AuthoritativePlanetGovernorOptions.DontScrapBuildings;
        if (planet.Quarantine) options |= AuthoritativePlanetGovernorOptions.Quarantine;
        if (planet.ManualOrbitals) options |= AuthoritativePlanetGovernorOptions.ManualOrbitals;
        if (planet.GovGroundDefense) options |= AuthoritativePlanetGovernorOptions.GovGroundDefense;
        if (planet.SpecializedTradeHub) options |= AuthoritativePlanetGovernorOptions.SpecializedTradeHub;
        return options;
    }

    public static bool IsActiveFor(Empire empire)
        => TryGetFor(empire, out _);

    static bool TryGetFor(Empire empire, out Authoritative4XClientContext context)
    {
        context = Active;
        return context != null && empire != null && empire.Id == context.EmpireId;
    }

    static bool TryGetForBatch(Ship[] ships, out Authoritative4XClientContext context, out Ship[] ownedShips)
    {
        context = null;
        ownedShips = Array.Empty<Ship>();
        if (Active == null || ships == null || ships.Length == 0)
            return false;

        ownedShips = ships.Where(s => s?.Loyalty != null && s.Loyalty.Id == Active.EmpireId).ToArray();
        if (ownedShips.Length == 0)
            return false;

        context = Active;
        return true;
    }

    static bool CanSubmitShipMove(Ship ship)
        => ship?.Active == true && !ship.IsPlatformOrStation;

    static bool CanSubmitFleetMoveShip(Ship ship)
        => ship?.Active == true && ship.PlayerShipCanTakeFleetOrders();

    static bool IsLegalFleetName(string name)
    {
        string trimmed = name?.Trim() ?? "";
        return trimmed.Length is > 0 and <= 40
            && trimmed.All(c => !char.IsControl(c));
    }

    static bool CanSubmitShipSpecialOrder(Ship ship, AuthoritativeShipSpecialOrderType orderType)
    {
        if (ship?.Active != true)
            return false;

        return orderType switch
        {
            AuthoritativeShipSpecialOrderType.ClearOrders => true,
            AuthoritativeShipSpecialOrderType.Explore => ship.PlayerShipCanTakeFleetOrders()
                && !ship.IsPlatformOrStation
                && !ship.IsSubspaceProjector
                && ship.ShipData.Role != RoleName.troop,
            _ => false,
        };
    }

    static bool CanSubmitShipLifecycleOrder(Ship ship, AuthoritativeShipLifecycleOrderType orderType)
    {
        if (ship?.Active != true || !ship.CanBeScrapped)
            return false;

        return orderType switch
        {
            AuthoritativeShipLifecycleOrderType.Scrap => !ship.IsPlatformOrStation,
            AuthoritativeShipLifecycleOrderType.Scuttle => ship.IsPlatformOrStation,
            AuthoritativeShipLifecycleOrderType.CancelScuttle => ship.IsPlatformOrStation,
            _ => false,
        };
    }

    static bool CanSubmitShipCombatStance(Ship ship)
        => ship?.Active == true
           && !ship.IsConstructor
           && !ship.IsMiningShip
           && !ship.IsSupplyShuttle
           && ship.DesignRole != RoleName.ssp;

    static bool IsLegalCombatStance(CombatState stance)
        => Enum.IsDefined(typeof(CombatState), stance);

    static bool CanSubmitShipAttack(Ship ship, Ship target)
        => CanSubmitShipMove(ship) && ship.ShipData.Role != RoleName.troop && ship != target
           && target.Loyalty != ship.Loyalty;

    static bool CanSubmitShipPlanetOrder(Ship ship)
        => ship?.Active == true && !ship.IsConstructor && !ship.IsPlatformOrStation && !ship.IsSubspaceProjector;

    static int QueueIndexOf(Planet planet, QueueItem item)
    {
        if (planet == null || item == null)
            return -1;

        var queue = planet.ConstructionQueue;
        for (int i = 0; i < queue.Count; ++i)
        {
            if (ReferenceEquals(queue[i], item))
                return i;
        }
        return -1;
    }

    int Next() => Interlocked.Increment(ref NextSequence) - 1;

    void Submit(AuthoritativePlayerCommand command) => SubmitCommand(command);

    public void Dispose()
    {
        if (Disposed)
            return;
        Disposed = true;
        if (Active == this)
            Active = Previous;
    }
}

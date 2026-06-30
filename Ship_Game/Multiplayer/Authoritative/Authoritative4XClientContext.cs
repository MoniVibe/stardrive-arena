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
using Ship_Game.Universe;
using Vector2 = SDGraphics.Vector2;
using Rectangle = SDGraphics.Rectangle;

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

    Authoritative4XClientContext Previous;
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

    public static bool ShouldBlockLocalMutation(UniverseScreen universe)
        => IsActive || universe?.IsAuthoritative4XMultiplayer == true;

    public static bool ShouldBlockLocalMutation(UniverseState universe)
        => IsActive || universe?.Screen?.IsAuthoritative4XMultiplayer == true;

    public static bool ShouldBlockLocalMutation(Empire empire)
        => IsActive || empire?.Universe?.Screen?.IsAuthoritative4XMultiplayer == true;

    public static bool ShouldBlockLocalMutation(Planet planet)
        => IsActive || planet?.Universe?.Screen?.IsAuthoritative4XMultiplayer == true;

    public static bool ShouldBlockLocalMutation(Ship ship)
        => IsActive || ship?.Universe?.Screen?.IsAuthoritative4XMultiplayer == true;

    public static bool ShouldBlockLocalMutation(Fleet fleet)
        => IsActive || fleet?.Owner?.Universe?.Screen?.IsAuthoritative4XMultiplayer == true;

    public static bool ShouldBlockLocalMutation(ShipGroup group)
        => IsActive || group?.Owner?.Universe?.Screen?.IsAuthoritative4XMultiplayer == true;

    public static Authoritative4XClientContext Begin(int peerId, int empireId,
        Action<AuthoritativePlayerCommand> submitCommand, int firstSequence = 1)
    {
        return new Authoritative4XClientContext(peerId, empireId, firstSequence, submitCommand);
    }

    public void Activate()
    {
        if (Disposed || Active == this)
            return;
        Previous = Active;
        Active = this;
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

    public static Authoritative4XUiCommandResult TrySubmitBuildCapitalHere(Planet planet)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;

        context.Submit(AuthoritativePlayerCommand.BuildCapitalHere(context.Next(), context.EmpireId,
            planet.Id));
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
        if (!CanSubmitFleetKey(fleet) || !IsLegalFleetName(name))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.RenameFleet(context.Next(), context.EmpireId,
            fleet.Key, name.Trim()));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitRenameShip(Ship ship, string name)
    {
        if (!TryGetFor(ship?.Loyalty, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (ship?.Active != true || !AuthoritativePlayerCommand.IsLegalShipRename(name))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.RenameShip(context.Next(), context.EmpireId,
            ship.Id, name.Trim()));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitRenamePlanet(Planet planet, string name)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!AuthoritativePlayerCommand.IsLegalPlanetRename(name))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.RenamePlanet(context.Next(), context.EmpireId,
            planet.Id, name.Trim()));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitSetFleetIcon(Fleet fleet, int iconIndex)
    {
        if (!TryGetFor(fleet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!CanSubmitFleetKey(fleet) || !IsLegalFleetIconIndex(iconIndex))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.SetFleetIcon(context.Next(), context.EmpireId,
            fleet.Key, iconIndex));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitAutoArrangeFleet(Fleet fleet)
    {
        if (!TryGetFor(fleet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!CanSubmitFleetKey(fleet) || fleet.Ships.Count == 0)
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.AutoArrangeFleet(context.Next(), context.EmpireId, fleet.Key));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitSetFleetLayout(Fleet fleet,
        IEnumerable<FleetDataNode> nodes)
    {
        if (!TryGetFor(fleet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!CanSubmitFleetKey(fleet))
            return Authoritative4XUiCommandResult.Blocked;

        FleetDataNode[] layout = (nodes ?? Array.Empty<FleetDataNode>()).ToArray();
        if (!CanSubmitFleetLayout(context, layout))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.SetFleetLayout(context.Next(), context.EmpireId,
            fleet.Key, layout));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitLoadFleetDesign(Fleet fleet, FleetDesign design)
    {
        if (!TryGetFor(fleet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!CanSubmitFleetKey(fleet) || design == null || !IsLegalFleetName(design.Name)
            || !IsLegalFleetIconIndex(design.FleetIconIndex))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        FleetDataNode[] layout = design.Nodes?.Select(n => n != null ? new FleetDataNode(n) : null).ToArray()
                                 ?? Array.Empty<FleetDataNode>();
        if (layout.Length == 0 || !CanSubmitFleetLayout(context, layout)
            || !CanSubmitSavedFleetDesignLayout(fleet.Owner, layout))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.SetFleetLayout(context.Next(), context.EmpireId,
            fleet.Key, layout));
        context.Submit(AuthoritativePlayerCommand.RenameFleet(context.Next(), context.EmpireId,
            fleet.Key, design.Name.Trim()));
        context.Submit(AuthoritativePlayerCommand.SetFleetIcon(context.Next(), context.EmpireId,
            fleet.Key, design.FleetIconIndex));
        return Authoritative4XUiCommandResult.Submitted;
    }

    static bool CanSubmitFleetLayout(Authoritative4XClientContext context, FleetDataNode[] layout)
    {
        if (layout.Length > 200
            || layout.Any(n => n == null || n.Goal != null || string.IsNullOrWhiteSpace(n.ShipName ?? n.Ship?.Name)))
        {
            return false;
        }

        Ship[] ships = layout.Select(n => n.Ship).Where(s => s != null).ToArray();
        if (ships.Any(s => s.Active != true || s.Loyalty?.Id != context.EmpireId || !s.CanBeAddedToFleets())
            || ships.Select(s => s.Id).Distinct().Count() != ships.Length)
        {
            return false;
        }

        return layout.All(n => IsFiniteFleetLayoutNode(n) && Enum.IsDefined(typeof(CombatState), n.CombatState));
    }

    static bool CanSubmitSavedFleetDesignLayout(Empire empire, FleetDataNode[] layout)
    {
        foreach (FleetDataNode node in layout)
        {
            if (node.Ship != null)
                continue;
            if (!ResourceManager.Ships.GetDesign(node.ShipName, out IShipDesign design)
                || !empire.CanBuildShip(design))
            {
                return false;
            }
        }
        return true;
    }

    public static Authoritative4XUiCommandResult TrySubmitQueueFleetRequisition(Fleet fleet, bool rush,
        IEnumerable<int> nodeIndices = null)
    {
        if (!TryGetFor(fleet?.Owner, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (fleet.Key is < Empire.FirstFleetKey or > Empire.LastFleetKey)
            return Authoritative4XUiCommandResult.Blocked;

        int[] requested = (nodeIndices ?? Array.Empty<int>()).ToArray();
        if (requested.Length > 0 && requested.Distinct().Count() != requested.Length)
            return Authoritative4XUiCommandResult.Blocked;

        int[] targetIndices = requested.Length > 0
            ? requested
            : Enumerable.Range(0, fleet.DataNodes.Count)
                .Where(i => CanQueueFleetRequisitionNode(fleet.Owner, fleet.DataNodes[i]))
                .ToArray();
        if (targetIndices.Length == 0)
            return Authoritative4XUiCommandResult.Blocked;

        foreach (int index in targetIndices)
        {
            if ((uint)index >= fleet.DataNodes.Count
                || !CanQueueFleetRequisitionNode(fleet.Owner, fleet.DataNodes[index]))
            {
                return Authoritative4XUiCommandResult.Blocked;
            }
        }

        context.Submit(AuthoritativePlayerCommand.QueueFleetRequisition(context.Next(), context.EmpireId,
            fleet.Key, rush, requested));
        return Authoritative4XUiCommandResult.Submitted;
    }

    static bool CanQueueFleetRequisitionNode(Empire empire, FleetDataNode node)
    {
        if (empire == null || node?.Ship != null || node?.Goal != null || string.IsNullOrWhiteSpace(node?.ShipName))
            return false;
        return ResourceManager.Ships.GetDesign(node.ShipName, out IShipDesign design)
               && !design.IsPlatformOrStation
               && empire.CanBuildShip(design);
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

    public static Authoritative4XUiCommandResult TrySubmitSetShipTradePolicy(Ship ship,
        AuthoritativeShipTradePolicyKind policy, bool enabled)
    {
        if (!TryGetFor(ship?.Loyalty, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!Enum.IsDefined(typeof(AuthoritativeShipTradePolicyKind), policy)
            || !CanSubmitShipTradePolicy(ship))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.SetShipTradePolicy(context.Next(), context.EmpireId,
            ship.Id, policy, enabled));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitSetShipCarrierPolicy(Ship ship,
        AuthoritativeShipCarrierPolicyKind policy, bool enabled)
    {
        if (!TryGetFor(ship?.Loyalty, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!Enum.IsDefined(typeof(AuthoritativeShipCarrierPolicyKind), policy)
            || !CanSubmitShipCarrierPolicy(ship, policy))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.SetShipCarrierPolicy(context.Next(), context.EmpireId,
            ship.Id, policy, enabled));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitSetShipTradeRoute(Ship ship, Planet planet,
        bool enabled)
    {
        if (!TryGetFor(ship?.Loyalty, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!CanSubmitShipTradeRoute(ship, planet, enabled))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.SetShipTradeRoute(context.Next(), context.EmpireId,
            ship.Id, planet.Id, enabled));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitSetShipAreaOfOperation(Ship ship,
        AuthoritativeShipAreaOfOperationAction action, Rectangle areaOrPoint)
    {
        if (!TryGetFor(ship?.Loyalty, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!Enum.IsDefined(typeof(AuthoritativeShipAreaOfOperationAction), action)
            || !CanSubmitShipAreaOfOperation(ship, action, areaOrPoint))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.SetShipAreaOfOperation(context.Next(), context.EmpireId,
            ship.Id, action, areaOrPoint));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitShipRefit(Ship ship, IShipDesign design,
        AuthoritativeShipRefitMode mode, bool rush)
    {
        if (!TryGetFor(ship?.Loyalty, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!Enum.IsDefined(typeof(AuthoritativeShipRefitMode), mode)
            || !CanSubmitShipRefit(ship, design, mode))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.RefitShip(context.Next(), context.EmpireId,
            ship.Id, design.Name, mode, rush));
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

    public static Authoritative4XUiCommandResult TrySubmitShipTargetOrder(Ship ship, Ship target,
        AuthoritativeShipTargetOrderType orderType, bool queue)
    {
        if (!TryGetFor(ship?.Loyalty, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!CanSubmitShipTargetOrder(ship, target, orderType, queue))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.ShipTargetOrder(context.Next(), context.EmpireId,
            ship.Id, target.Id, orderType, queue));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitShipTargetOrders(Ship[] ships, Ship target,
        bool queue, Func<Ship, AuthoritativeShipTargetOrderType> orderFor)
    {
        if (!TryGetForBatch(ships, out Authoritative4XClientContext context, out Ship[] ownedShips))
            return Authoritative4XUiCommandResult.NotActive;
        if (target?.Active != true || target.Loyalty == null || ownedShips.Length == 0 || orderFor == null)
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        (Ship Ship, AuthoritativeShipTargetOrderType Order)[] orders = ownedShips
            .Select(s => (s, orderFor(s)))
            .ToArray();
        if (orders.Any(o => !CanSubmitShipTargetOrder(o.Ship, target, o.Order,
                o.Order == AuthoritativeShipTargetOrderType.Attack && queue)))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        foreach ((Ship ship, AuthoritativeShipTargetOrderType orderType) in orders)
        {
            context.Submit(AuthoritativePlayerCommand.ShipTargetOrder(context.Next(), context.EmpireId,
                ship.Id, target.Id, orderType, orderType == AuthoritativeShipTargetOrderType.Attack && queue));
        }
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitShipPlanetOrder(Ship ship, Planet planet,
        AuthoritativeShipPlanetOrderType orderType, bool clearOrders, MoveOrder moveOrder)
    {
        if (!TryGetFor(ship?.Loyalty, out Authoritative4XClientContext context))
            return Authoritative4XUiCommandResult.NotActive;
        if (!CanSubmitShipPlanetOrder(ship, planet, orderType, moveOrder))
            return Authoritative4XUiCommandResult.Blocked;

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
            || ownedShips.Any(s => !CanSubmitShipPlanetOrder(s, planet, orderFor(s), moveOrder)))
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

    public static Authoritative4XUiCommandResult TrySubmitEmpireAutomation(Empire empire,
        AuthoritativeEmpireAutomationFlags flags, string freighter, string colony, string scout,
        string constructor, string researchStation, string miningStation)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if ((flags & ~AuthoritativeEmpireAutomationFlags.All) != 0
            || !AuthoritativePlayerCommand.IsLegalAutomationDesignName(freighter)
            || !AuthoritativePlayerCommand.IsLegalAutomationDesignName(colony)
            || !AuthoritativePlayerCommand.IsLegalAutomationDesignName(scout)
            || !AuthoritativePlayerCommand.IsLegalAutomationDesignName(constructor)
            || !AuthoritativePlayerCommand.IsLegalAutomationDesignName(researchStation)
            || !AuthoritativePlayerCommand.IsLegalAutomationDesignName(miningStation))
        {
            return Authoritative4XUiCommandResult.Blocked;
        }

        context.Submit(AuthoritativePlayerCommand.SetEmpireAutomation(context.Next(), context.EmpireId,
            flags, freighter, colony, scout, constructor, researchStation, miningStation));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitUniversePreferences(Empire empire,
        AuthoritativeUniversePreferenceFlags flags)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if ((flags & ~AuthoritativeUniversePreferenceFlags.All) != 0)
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.SetUniversePreferences(context.Next(),
            context.EmpireId, flags));
        return Authoritative4XUiCommandResult.Submitted;
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

    public static Authoritative4XUiCommandResult TrySubmitLaunchGroundTroop(Empire empire, Planet planet,
        PlanetGridSquare tile, Troop troop)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!CanSubmitLaunchGroundTroop(empire, planet, tile, troop, out int troopIndex))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.GroundTroopOrder(context.Next(), context.EmpireId,
            planet.Id, AuthoritativeGroundTroopOrderType.LaunchOne, tile.X, tile.Y, troopIndex,
            expectedTroopName: troop.Name));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitLaunchGroundTroops(Empire empire, Planet planet)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (planet?.Habitable != true || planet.Troops.NumTroopsCanLaunchFor(empire) <= 0)
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.GroundTroopOrder(context.Next(), context.EmpireId,
            planet.Id, AuthoritativeGroundTroopOrderType.LaunchAll));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitRecallGroundTroops(Empire empire, Planet planet)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (planet?.Habitable != true || planet.Troops.NumTroopsCanLaunchFor(empire) <= 0)
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.GroundTroopOrder(context.Next(), context.EmpireId,
            planet.Id, AuthoritativeGroundTroopOrderType.RecallAll));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitMoveGroundTroop(Empire empire, Planet planet,
        PlanetGridSquare sourceTile, Troop troop, PlanetGridSquare targetTile)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!CanSubmitMoveGroundTroop(empire, planet, sourceTile, troop, targetTile, out int troopIndex))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.GroundTroopOrder(context.Next(), context.EmpireId,
            planet.Id, AuthoritativeGroundTroopOrderType.Move, sourceTile.X, sourceTile.Y, troopIndex,
            targetTile.X, targetTile.Y, troop.Name));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitAttackGroundTroop(Empire empire, Planet planet,
        PlanetGridSquare sourceTile, Troop troop, PlanetGridSquare targetTile)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!CanSubmitAttackGroundTroop(empire, planet, sourceTile, troop, targetTile, out int troopIndex))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.GroundTroopOrder(context.Next(), context.EmpireId,
            planet.Id, AuthoritativeGroundTroopOrderType.AttackTroop, sourceTile.X, sourceTile.Y, troopIndex,
            targetTile.X, targetTile.Y, troop.Name));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitAttackGroundBuilding(Empire empire, Planet planet,
        PlanetGridSquare sourceTile, Troop troop, PlanetGridSquare targetTile)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!CanSubmitAttackGroundBuilding(empire, planet, sourceTile, troop, targetTile, out int troopIndex))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.GroundTroopOrder(context.Next(), context.EmpireId,
            planet.Id, AuthoritativeGroundTroopOrderType.AttackBuilding, sourceTile.X, sourceTile.Y, troopIndex,
            targetTile.X, targetTile.Y, troop.Name));
        return Authoritative4XUiCommandResult.Submitted;
    }

    public static Authoritative4XUiCommandResult TrySubmitBuildingAttackGroundTroop(Empire empire, Planet planet,
        PlanetGridSquare buildingTile, PlanetGridSquare targetTile)
    {
        if (!TryGetFor(empire, out Authoritative4XClientContext context))
            return Active != null ? Authoritative4XUiCommandResult.Blocked : Authoritative4XUiCommandResult.NotActive;
        if (!CanSubmitBuildingAttackGroundTroop(empire, planet, buildingTile, targetTile, out int targetTroopIndex))
            return Authoritative4XUiCommandResult.Blocked;

        context.Submit(AuthoritativePlayerCommand.GroundTroopOrder(context.Next(), context.EmpireId,
            planet.Id, AuthoritativeGroundTroopOrderType.BuildingAttackTroop, buildingTile.X, buildingTile.Y,
            targetTroopIndex, targetTile.X, targetTile.Y));
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

    static bool IsLegalFleetIconIndex(int iconIndex)
        => iconIndex is >= 1 and <= 30;

    static bool CanSubmitFleetKey(Fleet fleet)
        => fleet != null && fleet.Key is >= Empire.FirstFleetKey and <= Empire.LastFleetKey;

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
            AuthoritativeShipSpecialOrderType.Resupply => true,
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

    static bool CanSubmitShipTradePolicy(Ship ship)
        => ship?.Active == true && ship.IsFreighter;

    static bool CanSubmitShipTradeRoute(Ship ship, Planet planet, bool enabled)
    {
        if (ship?.Active != true || !ship.IsFreighter || planet == null || ship.Loyalty == null)
            return false;
        if (!enabled)
            return true;

        return planet.Owner == ship.Loyalty
               || ship.Loyalty.IsTradeTreaty(planet.Owner)
               || planet.IsMineable
               || planet.IsResearchable;
    }

    static bool CanSubmitShipAreaOfOperation(Ship ship, AuthoritativeShipAreaOfOperationAction action,
        Rectangle areaOrPoint)
    {
        if (ship?.Active != true || !ship.IsFreighter)
            return false;

        return action switch
        {
            AuthoritativeShipAreaOfOperationAction.AddRectangle =>
                areaOrPoint.Width >= 5000 && areaOrPoint.Height >= 5000,
            AuthoritativeShipAreaOfOperationAction.RemoveAtPoint => true,
            _ => false,
        };
    }

    static bool CanSubmitShipRefit(Ship ship, IShipDesign design, AuthoritativeShipRefitMode mode)
    {
        if (ship?.Active != true || !ship.CanBeRefitted || ship.IsSubspaceProjector || design == null
            || ship.AI.State == AIState.Scrap || ship.AI.State == AIState.Scuttle || ship.ScuttleTimer >= 0f)
            return false;
        if (mode == AuthoritativeShipRefitMode.Fleet && ship.Fleet == null)
            return false;

        return (design.Hull == ship.ShipData.Hull || ship.IsResearchStation || ship.IsMiningStation)
               && !string.Equals(design.Name, ship.ShipData.Name, StringComparison.Ordinal)
               && !design.ShipRole.Protected
               && ship.IsResearchStation == design.IsResearchStation
               && ship.IsMiningStation == design.IsMiningStation
               && ship.Loyalty.CanBuildShip(design)
               && ship.Loyalty.ShipsWeCanBuildSnapshot.Any(d =>
                   string.Equals(d.Name, design.Name, StringComparison.Ordinal));
    }

    static bool CanSubmitShipCarrierPolicy(Ship ship, AuthoritativeShipCarrierPolicyKind policy)
    {
        if (ship?.Active != true || ship.Carrier == null)
            return false;

        return policy switch
        {
            AuthoritativeShipCarrierPolicyKind.FightersOut => ship.Carrier.HasFighterBays,
            AuthoritativeShipCarrierPolicyKind.TroopsOut => ship.Carrier.HasTroopBays,
            AuthoritativeShipCarrierPolicyKind.SendTroopsToShip => ship.Carrier.HasTroopBays,
            AuthoritativeShipCarrierPolicyKind.AllowBoardShip => ship.Carrier.HasTroopBays,
            AuthoritativeShipCarrierPolicyKind.RecallFightersBeforeFTL =>
                ship.ShipData.Role != RoleName.station
                && (ship.Carrier.HasFighterBays || ship.Carrier.HasTroopBays),
            _ => false,
        };
    }

    static bool IsLegalCombatStance(CombatState stance)
        => Enum.IsDefined(typeof(CombatState), stance);

    static bool CanSubmitShipAttack(Ship ship, Ship target)
        => CanSubmitShipMove(ship) && ship.ShipData.Role != RoleName.troop && ship != target
           && target.Loyalty != ship.Loyalty;

    static bool CanSubmitShipTargetOrder(Ship ship, Ship target, AuthoritativeShipTargetOrderType orderType,
        bool queue)
    {
        if (ship?.Active != true || target?.Active != true || ship == target
            || ship.Loyalty == null || target.Loyalty == null
            || !Enum.IsDefined(typeof(AuthoritativeShipTargetOrderType), orderType)
            || queue && orderType != AuthoritativeShipTargetOrderType.Attack)
        {
            return false;
        }

        return orderType switch
        {
            AuthoritativeShipTargetOrderType.Attack =>
                !ship.IsPlatformOrStation
                && ship.ShipData.Role != RoleName.troop
                && CanSubmitHostileShipTarget(ship, target),
            AuthoritativeShipTargetOrderType.Escort =>
                target.Loyalty == ship.Loyalty,
            AuthoritativeShipTargetOrderType.TransferTroops =>
                target.Loyalty == ship.Loyalty
                && IsSingleTroopTargetOrderShip(ship)
                && ship.TroopCount > 0
                && target.TroopCapacity > target.TroopCount,
            AuthoritativeShipTargetOrderType.Board =>
                CanSubmitHostileShipTarget(ship, target)
                && IsSingleTroopTargetOrderShip(ship)
                && ship.TroopCount > 0,
            _ => false,
        };
    }

    static bool CanSubmitHostileShipTarget(Ship ship, Ship target)
    {
        Empire owner = ship?.Loyalty;
        Empire targetOwner = target?.Loyalty;
        return owner != null
               && targetOwner != null
               && targetOwner != owner
               && (owner.IsEmpireAttackable(targetOwner, target)
                   || AuthoritativeHumanPlayers.IsHumanVsHuman(owner, targetOwner));
    }

    static bool IsSingleTroopTargetOrderShip(Ship ship)
        => ship?.DesignRole == RoleName.troop || ship?.ShipData.Role == RoleName.troop;

    static bool CanSubmitShipPlanetOrder(Ship ship)
        => ship?.Active == true && !ship.IsConstructor && !ship.IsPlatformOrStation && !ship.IsSubspaceProjector;

    static bool CanSubmitShipPlanetOrder(Ship ship, Planet planet, AuthoritativeShipPlanetOrderType orderType,
        MoveOrder moveOrder)
    {
        if (!CanSubmitShipPlanetOrder(ship) || planet == null || ship.Loyalty == null)
            return false;

        const MoveOrder Allowed = MoveOrder.Regular | MoveOrder.Aggressive | MoveOrder.StandGround;
        if ((moveOrder & ~Allowed) != 0)
            return false;

        return orderType switch
        {
            AuthoritativeShipPlanetOrderType.Orbit => true,
            AuthoritativeShipPlanetOrderType.Colonize =>
                ship.ShipData.IsColonyShip && planet.Habitable && planet.Owner == null,
            AuthoritativeShipPlanetOrderType.Bombard =>
                ship.HasBombs && planet.Owner != null && planet.Owner != ship.Loyalty
                              && ship.Loyalty.IsEmpireAttackable(planet.Owner),
            AuthoritativeShipPlanetOrderType.LandTroops =>
                (ship.Carrier.AnyAssaultOpsAvailable || IsSingleTroopTargetOrderShip(ship)
                                                       || ship.IsDefaultAssaultShuttle)
                                                   && planet.Habitable
                                                   && (planet.Owner == null
                                                       || planet.Owner == ship.Loyalty
                                                       || planet.Owner != ship.Loyalty
                                                       && ship.Loyalty.IsAtWarWith(planet.Owner)),
            _ => false,
        };
    }

    static bool CanSubmitLaunchGroundTroop(Empire empire, Planet planet, PlanetGridSquare tile, Troop troop,
        out int troopIndex)
    {
        troopIndex = GroundTroopIndex(tile, troop);
        return empire != null
               && planet?.Habitable == true
               && tile?.P == planet
               && troop?.Loyalty == empire
               && troopIndex >= 0
               && troop.CanLaunch;
    }

    static bool CanSubmitMoveGroundTroop(Empire empire, Planet planet, PlanetGridSquare sourceTile,
        Troop troop, PlanetGridSquare targetTile, out int troopIndex)
    {
        troopIndex = GroundTroopIndex(sourceTile, troop);
        if (empire == null
            || planet?.Habitable != true
            || sourceTile?.P != planet
            || targetTile?.P != planet
            || sourceTile == targetTile
            || troop?.Loyalty != empire
            || troopIndex < 0
            || !troop.CanMove
            || !targetTile.IsTileFree(empire))
        {
            return false;
        }

        return Math.Abs(targetTile.X - sourceTile.X) <= troop.ActualRange
               && Math.Abs(targetTile.Y - sourceTile.Y) <= troop.ActualRange;
    }

    static bool CanSubmitAttackGroundTroop(Empire empire, Planet planet, PlanetGridSquare sourceTile,
        Troop troop, PlanetGridSquare targetTile, out int troopIndex)
    {
        troopIndex = GroundTroopIndex(sourceTile, troop);
        return CanSubmitGroundTroopAttack(empire, planet, sourceTile, troop, targetTile, troopIndex)
               && targetTile.LockOnEnemyTroop(empire, out _);
    }

    static bool CanSubmitAttackGroundBuilding(Empire empire, Planet planet, PlanetGridSquare sourceTile,
        Troop troop, PlanetGridSquare targetTile, out int troopIndex)
    {
        troopIndex = GroundTroopIndex(sourceTile, troop);
        return CanSubmitGroundTroopAttack(empire, planet, sourceTile, troop, targetTile, troopIndex)
               && targetTile.CombatBuildingOnTile
               && planet.Owner != empire;
    }

    static bool CanSubmitGroundTroopAttack(Empire empire, Planet planet, PlanetGridSquare sourceTile,
        Troop troop, PlanetGridSquare targetTile, int troopIndex)
    {
        return empire != null
               && planet?.Habitable == true
               && sourceTile?.P == planet
               && targetTile?.P == planet
               && sourceTile != targetTile
               && troop?.Loyalty == empire
               && troopIndex >= 0
               && troop.CanAttack
               && Math.Abs(targetTile.X - sourceTile.X) <= troop.ActualRange
               && Math.Abs(targetTile.Y - sourceTile.Y) <= troop.ActualRange;
    }

    static bool CanSubmitBuildingAttackGroundTroop(Empire empire, Planet planet, PlanetGridSquare buildingTile,
        PlanetGridSquare targetTile, out int targetTroopIndex)
    {
        targetTroopIndex = -1;
        if (empire == null
            || planet?.Habitable != true
            || planet.Owner != empire
            || buildingTile?.P != planet
            || targetTile?.P != planet
            || buildingTile == targetTile
            || !buildingTile.CombatBuildingOnTile
            || !buildingTile.Building.CanAttack
            || Math.Abs(targetTile.X - buildingTile.X) > 1
            || Math.Abs(targetTile.Y - buildingTile.Y) > 1)
        {
            return false;
        }

        for (int i = 0; i < targetTile.TroopsHere.Count; ++i)
        {
            if (targetTile.TroopsHere[i]?.Loyalty?.IsAtWarWith(empire) == true)
            {
                targetTroopIndex = i;
                return true;
            }
        }
        return false;
    }

    static int GroundTroopIndex(PlanetGridSquare tile, Troop troop)
    {
        if (tile == null || troop == null)
            return -1;
        for (int i = 0; i < tile.TroopsHere.Count; ++i)
            if (ReferenceEquals(tile.TroopsHere[i], troop))
                return i;
        return -1;
    }

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

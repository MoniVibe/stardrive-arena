using System;
using System.Collections.Generic;
using System.Linq;
using Ship_Game.AI;
using Ship_Game.Commands.Goals;
using Ship_Game.Determinism.Lockstep;
using Ship_Game.Fleets;
using Ship_Game.Ships;
using Ship_Game.Ships.AI;
using Ship_Game.Universe;
using SDUtils;
using Vector2 = SDGraphics.Vector2;
using Rectangle = SDGraphics.Rectangle;

namespace Ship_Game.Multiplayer.Authoritative;

/// <summary>
/// Host-side validator/applicator for the first authoritative 4X command slice.
/// Invalid requests become deterministic no-ops with an explicit rejection reason.
/// </summary>
public sealed class Authoritative4XCommandApplicator
{
    readonly UniverseState UState;
    readonly AuthoritativeDiplomacyManager Diplomacy;

    public Authoritative4XCommandApplicator(UniverseState universe, AuthoritativeDiplomacyManager diplomacy = null)
    {
        UState = universe;
        Diplomacy = diplomacy;
    }

    public AuthoritativeCommandResult Apply(AuthoritativePlayerCommand command, uint tick)
    {
        var result = new AuthoritativeCommandResult { Sequence = command.Sequence, Tick = tick };
        Empire empire = UState.GetEmpireById(command.EmpireId);
        if (empire == null)
            return Reject(result, $"Empire {command.EmpireId} not found.");

        using (StarDriveCommandContext.Enter(tick, empire.Id))
        {
            return command.Kind switch
            {
                AuthoritativePlayerCommandKind.NoOp => Accept(result),
                AuthoritativePlayerCommandKind.MoveShip => ApplyMove(command, empire, result),
                AuthoritativePlayerCommandKind.SetColonyType => ApplyColonyType(command, empire, result),
                AuthoritativePlayerCommandKind.SetColonizationGoal => ApplyColonizationGoal(command, empire, result),
                AuthoritativePlayerCommandKind.SetColonyLabor => ApplyColonyLabor(command, empire, result),
                AuthoritativePlayerCommandKind.SetResearchTopic => ApplyResearchTopic(command, empire, result),
                AuthoritativePlayerCommandKind.QueueResearch => ApplyQueueResearch(command, empire, result),
                AuthoritativePlayerCommandKind.RemoveResearchQueueItem => ApplyRemoveResearchQueueItem(command, empire, result),
                AuthoritativePlayerCommandKind.MoveResearchQueueItem => ApplyMoveResearchQueueItem(command, empire, result),
                AuthoritativePlayerCommandKind.SetEmpireBudget => ApplyEmpireBudget(command, empire, result),
                AuthoritativePlayerCommandKind.SetEmpireAutomation => ApplyEmpireAutomation(command, empire, result),
                AuthoritativePlayerCommandKind.DiplomacyProposal => ApplyDiplomacy(command, empire, result),
                AuthoritativePlayerCommandKind.DiplomacyResponse => ApplyDiplomacy(command, empire, result),
                AuthoritativePlayerCommandKind.DesignShip => ApplyDesignShip(command, empire, result),
                AuthoritativePlayerCommandKind.QueueBuild => ApplyQueueBuild(command, empire, result),
                AuthoritativePlayerCommandKind.QueueBuilding => ApplyQueueBuilding(command, empire, result),
                AuthoritativePlayerCommandKind.QueueTroop => ApplyQueueTroop(command, empire, result),
                AuthoritativePlayerCommandKind.CancelConstructionQueueItem => ApplyCancelConstructionQueueItem(command, empire, result),
                AuthoritativePlayerCommandKind.ReorderConstructionQueueItem => ApplyReorderConstructionQueueItem(command, empire, result),
                AuthoritativePlayerCommandKind.RushConstructionQueueItem => ApplyRushConstructionQueueItem(command, empire, result),
                AuthoritativePlayerCommandKind.ToggleConstructionRush => ApplyToggleConstructionRush(command, empire, result),
                AuthoritativePlayerCommandKind.SetPlanetGoodsState => ApplyPlanetGoodsState(command, empire, result),
                AuthoritativePlayerCommandKind.SetPlanetPrioritizedPort => ApplyPlanetPrioritizedPort(command, empire, result),
                AuthoritativePlayerCommandKind.SetPlanetManualBudget => ApplyPlanetManualBudget(command, empire, result),
                AuthoritativePlayerCommandKind.SetPlanetGovernorOptions => ApplyPlanetGovernorOptions(command, empire, result),
                AuthoritativePlayerCommandKind.SetPlanetManualTradeSlots => ApplyPlanetManualTradeSlots(command, empire, result),
                AuthoritativePlayerCommandKind.SetPlanetDefenseTargets => ApplyPlanetDefenseTargets(command, empire, result),
                AuthoritativePlayerCommandKind.SetFleetAssignment => ApplyFleetAssignment(command, empire, result),
                AuthoritativePlayerCommandKind.MoveFleet => ApplyMoveFleet(command, empire, result),
                AuthoritativePlayerCommandKind.RenameFleet => ApplyRenameFleet(command, empire, result),
                AuthoritativePlayerCommandKind.RenameShip => ApplyRenameShip(command, empire, result),
                AuthoritativePlayerCommandKind.RenamePlanet => ApplyRenamePlanet(command, empire, result),
                AuthoritativePlayerCommandKind.GroundTroopOrder => ApplyGroundTroopOrder(command, empire, result),
                AuthoritativePlayerCommandKind.SetFleetIcon => ApplySetFleetIcon(command, empire, result),
                AuthoritativePlayerCommandKind.AutoArrangeFleet => ApplyAutoArrangeFleet(command, empire, result),
                AuthoritativePlayerCommandKind.LoadFleetPatrol => ApplyLoadFleetPatrol(command, empire, result),
                AuthoritativePlayerCommandKind.RenameFleetPatrol => ApplyRenameFleetPatrol(command, empire, result),
                AuthoritativePlayerCommandKind.DeleteFleetPatrol => ApplyDeleteFleetPatrol(command, empire, result),
                AuthoritativePlayerCommandKind.ClearFleetPatrol => ApplyClearFleetPatrol(command, empire, result),
                AuthoritativePlayerCommandKind.CreateFleetPatrol => ApplyCreateFleetPatrol(command, empire, result),
                AuthoritativePlayerCommandKind.SetFleetLayout => ApplyFleetLayout(command, empire, result),
                AuthoritativePlayerCommandKind.QueueFleetRequisition => ApplyQueueFleetRequisition(command, empire, result),
                AuthoritativePlayerCommandKind.QueueDeepSpaceBuild => ApplyDeepSpaceBuild(command, empire, result),
                AuthoritativePlayerCommandKind.CancelDeepSpaceBuild => ApplyCancelDeepSpaceBuild(command, empire, result),
                AuthoritativePlayerCommandKind.QueuePlanetOrbitalBuild => ApplyPlanetOrbitalBuild(command, empire, result),
                AuthoritativePlayerCommandKind.BuildCapitalHere => ApplyBuildCapitalHere(command, empire, result),
                AuthoritativePlayerCommandKind.ApplyColonyBlueprints => ApplyColonyBlueprints(command, empire, result),
                AuthoritativePlayerCommandKind.ClearColonyBlueprints => ApplyClearColonyBlueprints(command, empire, result),
                AuthoritativePlayerCommandKind.ScrapColonyTile => ApplyScrapColonyTile(command, empire, result),
                AuthoritativePlayerCommandKind.ShipSpecialOrder => ApplyShipSpecialOrder(command, empire, result),
                AuthoritativePlayerCommandKind.ShipLifecycleOrder => ApplyShipLifecycleOrder(command, empire, result),
                AuthoritativePlayerCommandKind.SetShipCombatStance => ApplyShipCombatStance(command, empire, result),
                AuthoritativePlayerCommandKind.SetShipTradePolicy => ApplyShipTradePolicy(command, empire, result),
                AuthoritativePlayerCommandKind.SetShipCarrierPolicy => ApplyShipCarrierPolicy(command, empire, result),
                AuthoritativePlayerCommandKind.SetShipTradeRoute => ApplyShipTradeRoute(command, empire, result),
                AuthoritativePlayerCommandKind.SetShipAreaOfOperation => ApplyShipAreaOfOperation(command, empire, result),
                AuthoritativePlayerCommandKind.RefitShip => ApplyShipRefit(command, empire, result),
                AuthoritativePlayerCommandKind.AttackShip => ApplyAttackShip(command, empire, result),
                AuthoritativePlayerCommandKind.ShipTargetOrder => ApplyShipTargetOrder(command, empire, result),
                AuthoritativePlayerCommandKind.ShipPlanetOrder => ApplyShipPlanetOrder(command, empire, result),
                _ => Reject(result, $"Unsupported command kind {command.Kind}."),
            };
        }
    }

    AuthoritativeCommandResult ApplyDiplomacy(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        return Diplomacy != null
            ? Diplomacy.Apply(command, empire, result)
            : Reject(result, "Authoritative diplomacy is not enabled for this session.");
    }

    AuthoritativeCommandResult ApplyMove(AuthoritativePlayerCommand command, Empire empire, AuthoritativeCommandResult result)
    {
        Ship ship = UState.Objects.FindShip(command.SubjectId);
        if (ship == null)
            return Reject(result, $"Ship {command.SubjectId} not found.");
        if (!ship.Active)
            return Reject(result, $"Ship {command.SubjectId} is inactive.");
        if (ship.Loyalty != empire)
            return Reject(result, $"Ship {command.SubjectId} is not owned by empire {empire.Id}.");

        Vector2 delta = command.Position - ship.Position;
        Vector2 dir = delta.Length() > 0f ? delta.Normalized() : new Vector2(1f, 0f);
        MoveOrder order = command.TargetId != 0 ? (MoveOrder)command.TargetId : MoveOrder.Regular;
        const MoveOrder Allowed = MoveOrder.Regular | MoveOrder.Aggressive | MoveOrder.StandGround | MoveOrder.AddWayPoint;
        if ((order & ~Allowed) != 0)
            return Reject(result, $"Move order {order} is not supported by authoritative MP.");

        ship.AI.OrderMoveTo(command.Position, dir, order);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyEmpireAutomation(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        var flags = (AuthoritativeEmpireAutomationFlags)command.TargetId;
        if ((flags & ~AuthoritativeEmpireAutomationFlags.All) != 0)
            return Reject(result, $"Unsupported empire automation flags {command.TargetId}.");

        if (!AuthoritativePlayerCommand.TryParseEmpireAutomationPayload(command.Text,
                out string freighter, out string colony, out string scout, out string constructor,
                out string researchStation, out string miningStation))
        {
            return Reject(result, $"Invalid empire automation payload '{command.Text}'.");
        }

        if (!IsAutomationDesignValid(empire, freighter, d => d.IsFreighter, "freighter", out string reason)
            || !IsAutomationDesignValid(empire, colony, d => d.IsColonyShip, "colony ship", out reason)
            || !IsAutomationDesignValid(empire, scout,
                d => d.Role == RoleName.scout || d.Role == RoleName.fighter || d.ShipCategory == ShipCategory.Recon,
                "scout", out reason)
            || !IsAutomationDesignValid(empire, constructor, d => d.IsConstructor, "constructor", out reason)
            || !IsAutomationDesignValid(empire, researchStation, d => d.IsResearchStation, "research station", out reason)
            || !IsAutomationDesignValid(empire, miningStation, d => d.IsMiningStation, "mining station", out reason))
        {
            return Reject(result, reason);
        }

        empire.AutoPickConstructors = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickConstructors);
        empire.AutoPickBestColonizer = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestColonizer);
        empire.AutoPickBestFreighter = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestFreighter);
        empire.AutoResearch = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoResearch);
        empire.AutoBuildTerraformers = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildTerraformers);
        empire.AutoTaxes = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoTaxes);
        empire.AutoPickBestResearchStation = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestResearchStation);
        empire.AutoPickBestMiningStation = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestMiningStation);
        empire.AutoExplore = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoExplore);
        empire.AutoColonize = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoColonize);
        empire.AutoBuildSpaceRoads = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildSpaceRoads);
        empire.AutoFreighters = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoFreighters);
        empire.AutoBuildResearchStations = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildResearchStations);
        empire.AutoBuildMiningStations = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildMiningStations);
        empire.AutoMilitary = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoMilitary);

        bool rushAll = flags.HasFlag(AuthoritativeEmpireAutomationFlags.RushAllConstruction);
        empire.RushAllConstruction = rushAll;
        empire.SwitchRushAllConstruction(rushAll);

        empire.data.CurrentAutoFreighter = freighter;
        empire.data.CurrentAutoColony = colony;
        empire.data.CurrentAutoScout = scout;
        empire.data.CurrentConstructor = constructor;
        empire.data.CurrentResearchStation = researchStation;
        empire.data.CurrentMiningStation = miningStation;
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyMoveFleet(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.SubjectId is < Empire.FirstFleetKey or > Empire.LastFleetKey)
            return Reject(result, $"Fleet key {command.SubjectId} is outside the player fleet range.");

        Fleet fleet = empire.GetFleetOrNull(command.SubjectId);
        if (fleet == null || fleet.Ships.Count == 0)
            return Reject(result, $"Fleet {command.SubjectId} not found or empty.");
        if (!IsFinite(command.Position))
            return Reject(result, "Fleet destination must be finite.");
        if (!AuthoritativePlayerCommand.TryParseVectorPayload(command.Text, out Vector2 direction))
            return Reject(result, $"Invalid fleet direction payload '{command.Text}'.");
        if (!IsFinite(direction) || direction.Length() <= 0f)
            return Reject(result, "Fleet direction must be a non-zero finite vector.");

        MoveOrder order = command.TargetId != 0 ? (MoveOrder)command.TargetId : MoveOrder.Regular;
        const MoveOrder Allowed = MoveOrder.Regular | MoveOrder.Aggressive | MoveOrder.StandGround
                                | MoveOrder.AddWayPoint | MoveOrder.ForceReassembly;
        if ((order & ~Allowed) != 0)
            return Reject(result, $"Fleet move order {order} is not supported by authoritative MP.");

        if (!AnyFleetShipCanMove(fleet))
            return Reject(result, $"Fleet {fleet.Key} has no ships that can receive fleet movement orders.");

        if (fleet.HasPatrolPlan)
            fleet.ClearPatrol(clearOrders: false);
        fleet.MoveTo(command.Position, direction.Normalized(), order);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyShipSpecialOrder(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.TargetId < byte.MinValue || command.TargetId > byte.MaxValue
            || !Enum.IsDefined(typeof(AuthoritativeShipSpecialOrderType),
                (AuthoritativeShipSpecialOrderType)(byte)command.TargetId))
        {
            return Reject(result, $"Unsupported ship special order {command.TargetId}.");
        }

        Ship ship = UState.Objects.FindShip(command.SubjectId);
        if (ship == null)
            return Reject(result, $"Ship {command.SubjectId} not found.");
        if (!ship.Active)
            return Reject(result, $"Ship {command.SubjectId} is inactive.");
        if (ship.Loyalty != empire)
            return Reject(result, $"Ship {command.SubjectId} is not owned by empire {empire.Id}.");

        switch ((AuthoritativeShipSpecialOrderType)command.TargetId)
        {
            case AuthoritativeShipSpecialOrderType.ClearOrders:
                ship.AI.ClearOrders();
                return Accept(result);

            case AuthoritativeShipSpecialOrderType.Explore:
                if (!ship.PlayerShipCanTakeFleetOrders()
                    || ship.IsPlatformOrStation
                    || ship.IsSubspaceProjector
                    || ship.ShipData.Role == RoleName.troop)
                {
                    return Reject(result, $"Ship {ship.Id} cannot receive an explore order.");
                }
                ship.AI.OrderExplore();
                return Accept(result);

            case AuthoritativeShipSpecialOrderType.Resupply:
                ship.Supply.ResupplyFromButton();
                return Accept(result);

            default:
                return Reject(result, $"Unsupported ship special order {command.TargetId}.");
        }
    }

    AuthoritativeCommandResult ApplyShipLifecycleOrder(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.TargetId < byte.MinValue || command.TargetId > byte.MaxValue
            || !Enum.IsDefined(typeof(AuthoritativeShipLifecycleOrderType),
                (AuthoritativeShipLifecycleOrderType)(byte)command.TargetId))
        {
            return Reject(result, $"Unsupported ship lifecycle order {command.TargetId}.");
        }

        Ship ship = UState.Objects.FindShip(command.SubjectId);
        if (ship == null)
            return Reject(result, $"Ship {command.SubjectId} not found.");
        if (!ship.Active)
            return Reject(result, $"Ship {command.SubjectId} is inactive.");
        if (ship.Loyalty != empire)
            return Reject(result, $"Ship {command.SubjectId} is not owned by empire {empire.Id}.");
        if (!ship.CanBeScrapped)
            return Reject(result, $"Ship {command.SubjectId} cannot be scrapped or scuttled.");

        switch ((AuthoritativeShipLifecycleOrderType)command.TargetId)
        {
            case AuthoritativeShipLifecycleOrderType.Scrap:
                if (ship.IsPlatformOrStation)
                    return Reject(result, $"Ship {ship.Id} must be scuttled instead of scrapped.");
                ship.AI.OrderScrapShip();
                return Accept(result);

            case AuthoritativeShipLifecycleOrderType.Scuttle:
                if (!ship.IsPlatformOrStation)
                    return Reject(result, $"Ship {ship.Id} is not a platform or station.");
                ship.ScuttleTimer = 10f;
                return Accept(result);

            case AuthoritativeShipLifecycleOrderType.CancelScuttle:
                if (!ship.IsPlatformOrStation)
                    return Reject(result, $"Ship {ship.Id} is not a platform or station.");
                ship.ScuttleTimer = -1f;
                ship.AI.ClearOrders();
                return Accept(result);

            default:
                return Reject(result, $"Unsupported ship lifecycle order {command.TargetId}.");
        }
    }

    AuthoritativeCommandResult ApplyShipCombatStance(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.TargetId < 0 || !Enum.IsDefined(typeof(CombatState), (CombatState)command.TargetId))
            return Reject(result, $"Unsupported ship combat stance {command.TargetId}.");

        Ship ship = UState.Objects.FindShip(command.SubjectId);
        if (ship == null)
            return Reject(result, $"Ship {command.SubjectId} not found.");
        if (!ship.Active)
            return Reject(result, $"Ship {command.SubjectId} is inactive.");
        if (ship.Loyalty != empire)
            return Reject(result, $"Ship {command.SubjectId} is not owned by empire {empire.Id}.");
        if (ship.IsConstructor || ship.IsMiningShip || ship.IsSupplyShuttle || ship.DesignRole == RoleName.ssp)
            return Reject(result, $"Ship {ship.Id} cannot receive combat stance orders.");

        ship.SetCombatStance((CombatState)command.TargetId);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyShipTradePolicy(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.TargetId < byte.MinValue || command.TargetId > byte.MaxValue
            || !Enum.IsDefined(typeof(AuthoritativeShipTradePolicyKind),
                (AuthoritativeShipTradePolicyKind)(byte)command.TargetId))
        {
            return Reject(result, $"Unsupported ship trade policy {command.TargetId}.");
        }
        if (command.Text is not ("0" or "1"))
            return Reject(result, "Ship trade policy enabled flag must be 0 or 1.");

        Ship ship = UState.Objects.FindShip(command.SubjectId);
        if (ship == null)
            return Reject(result, $"Ship {command.SubjectId} not found.");
        if (!ship.Active)
            return Reject(result, $"Ship {command.SubjectId} is inactive.");
        if (ship.Loyalty != empire)
            return Reject(result, $"Ship {command.SubjectId} is not owned by empire {empire.Id}.");
        if (!ship.IsFreighter)
            return Reject(result, $"Ship {ship.Id} is not a freighter.");

        bool enabled = command.Text == "1";
        switch ((AuthoritativeShipTradePolicyKind)command.TargetId)
        {
            case AuthoritativeShipTradePolicyKind.Food:
                ship.TransportingFood = enabled;
                return Accept(result);
            case AuthoritativeShipTradePolicyKind.Production:
                ship.TransportingProduction = enabled;
                return Accept(result);
            case AuthoritativeShipTradePolicyKind.Colonists:
                ship.TransportingColonists = enabled;
                return Accept(result);
            case AuthoritativeShipTradePolicyKind.InterEmpire:
                ship.AllowInterEmpireTrade = enabled;
                return Accept(result);
            default:
                return Reject(result, $"Unsupported ship trade policy {command.TargetId}.");
        }
    }

    AuthoritativeCommandResult ApplyShipRefit(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.TargetId < byte.MinValue || command.TargetId > byte.MaxValue
            || !Enum.IsDefined(typeof(AuthoritativeShipRefitMode),
                (AuthoritativeShipRefitMode)(byte)command.TargetId))
        {
            return Reject(result, $"Unsupported ship refit mode {command.TargetId}.");
        }

        if (!AuthoritativePlayerCommand.TryParseShipRefitPayload(command.Text,
                out string designName, out bool rush))
        {
            return Reject(result, $"Invalid ship refit payload '{command.Text}'.");
        }

        Ship ship = UState.Objects.FindShip(command.SubjectId);
        if (ship == null)
            return Reject(result, $"Ship {command.SubjectId} not found.");
        if (!ship.Active)
            return Reject(result, $"Ship {command.SubjectId} is inactive.");
        if (ship.Loyalty != empire)
            return Reject(result, $"Ship {command.SubjectId} is not owned by empire {empire.Id}.");
        if (!CanApplyShipRefit(ship))
            return Reject(result, $"Ship {ship.Id} cannot be refitted.");

        IShipDesign design = FindRefitDesign(ship, empire, designName);
        if (design == null)
            return Reject(result, $"Design '{designName}' is not a valid refit target for ship {ship.Id}.");
        if (!CanFindRefitPlanet(ship, empire, design))
            return Reject(result, $"Empire {empire.Id} has no valid refit yard for ship {ship.Id}.");

        var mode = (AuthoritativeShipRefitMode)command.TargetId;
        if (mode == AuthoritativeShipRefitMode.Fleet && ship.Fleet == null)
            return Reject(result, $"Ship {ship.Id} is not assigned to a fleet.");

        switch (mode)
        {
            case AuthoritativeShipRefitMode.One:
                empire.AI.AddGoalAndEvaluate(CreateRefitGoal(ship, design, empire, rush));
                return Accept(result);

            case AuthoritativeShipRefitMode.All:
                ApplyRefitAll(empire, ship, design, rush, specificFleet: null);
                foreach (Fleet fleet in empire.AllFleets)
                    fleet.RefitNodeName(ship.Name, design.Name);
                return Accept(result);

            case AuthoritativeShipRefitMode.Fleet:
                Fleet refitFleet = ship.Fleet;
                refitFleet.RefitNodeName(ship.Name, design.Name);
                ApplyRefitAll(empire, ship, design, rush, refitFleet);
                return Accept(result);

            default:
                return Reject(result, $"Unsupported ship refit mode {command.TargetId}.");
        }
    }

    static bool CanApplyShipRefit(Ship ship)
        => ship?.Active == true
           && ship.CanBeRefitted
           && !ship.IsSubspaceProjector
           && ship.AI.State != AIState.Scrap
           && ship.AI.State != AIState.Scuttle
           && ship.ScuttleTimer < 0f;

    static IShipDesign FindRefitDesign(Ship ship, Empire empire, string designName)
    {
        if (ship == null || empire == null || string.IsNullOrWhiteSpace(designName))
            return null;

        return empire.ShipsWeCanBuildSnapshot
            .Where(design => empire.CanBuildShip(design) && IsValidRefitDesign(ship, design))
            .OrderBy(design => design.Name, StringComparer.Ordinal)
            .FirstOrDefault(design => string.Equals(design.Name, designName, StringComparison.Ordinal));
    }

    static bool IsValidRefitDesign(Ship ship, IShipDesign design)
        => design != null
           && (design.Hull == ship.ShipData.Hull || ship.IsResearchStation || ship.IsMiningStation)
           && !string.Equals(design.Name, ship.ShipData.Name, StringComparison.Ordinal)
           && !design.ShipRole.Protected
           && ship.IsResearchStation == design.IsResearchStation
           && ship.IsMiningStation == design.IsMiningStation;

    static bool CanFindRefitPlanet(Ship ship, Empire empire, IShipDesign design)
    {
        float cost = ship.RefitCost(design);
        return ship.IsPlatformOrStation
            ? empire.FindPlanetToRefitAt(empire.SafeSpacePorts, cost, design, out _)
            : empire.FindPlanetToRefitAt(empire.SafeSpacePorts, cost, ship, design,
                ship.Fleet != null, out _);
    }

    static Goal CreateRefitGoal(Ship ship, IShipDesign design, Empire empire, bool rush)
        => ship.IsPlatformOrStation
            ? new RefitOrbital(ship, design, empire, rush)
            : new RefitShip(ship, design, empire, rush);

    void ApplyRefitAll(Empire empire, Ship templateShip, IShipDesign design, bool rush, Fleet specificFleet)
    {
        var queuedShipIds = new HashSet<int>();
        if ((specificFleet == null || templateShip.Fleet == specificFleet)
            && queuedShipIds.Add(templateShip.Id))
        {
            empire.AI.AddGoalAndEvaluate(CreateRefitGoal(templateShip, design, empire, rush));
        }

        foreach (Ship ship in UState.Objects.GetShips())
        {
            if (ship.Active
                && ship.Loyalty == empire
                && ship.Name == templateShip.Name
                && (specificFleet == null || ship.Fleet == specificFleet)
                && queuedShipIds.Add(ship.Id))
            {
                empire.AI.AddGoalAndEvaluate(CreateRefitGoal(ship, design, empire, rush));
            }
        }

        foreach (Ship ship in empire.OwnedShips)
        {
            if (ship.Name == templateShip.Name
                && (specificFleet == null || ship.Fleet == specificFleet)
                && queuedShipIds.Add(ship.Id))
            {
                empire.AI.AddGoalAndEvaluate(CreateRefitGoal(ship, design, empire, rush));
            }
        }

        foreach (Planet planet in empire.GetPlanets())
            planet.Construction.RefitShipsBeingBuilt(templateShip, design);
    }

    AuthoritativeCommandResult ApplyShipTradeRoute(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.Text is not ("0" or "1"))
            return Reject(result, "Ship trade-route enabled flag must be 0 or 1.");

        Ship ship = UState.Objects.FindShip(command.SubjectId);
        if (ship == null)
            return Reject(result, $"Ship {command.SubjectId} not found.");
        if (!ship.Active)
            return Reject(result, $"Ship {command.SubjectId} is inactive.");
        if (ship.Loyalty != empire)
            return Reject(result, $"Ship {command.SubjectId} is not owned by empire {empire.Id}.");
        if (!ship.IsFreighter)
            return Reject(result, $"Ship {ship.Id} is not a freighter.");

        Planet planet = UState.GetPlanet(command.TargetId);
        if (planet == null)
            return Reject(result, $"Planet {command.TargetId} not found.");

        bool enabled = command.Text == "1";
        if (enabled)
        {
            if (!CanApplyTradeRoute(ship, planet))
                return Reject(result, $"Planet {planet.Id} is not a valid trade route for ship {ship.Id}.");

            ship.AddTradeRoute(planet);
        }
        else
        {
            ship.RemoveTradeRoute(planet);
        }

        return Accept(result);
    }

    static bool CanApplyTradeRoute(Ship ship, Planet planet)
        => ship?.Active == true
           && ship.IsFreighter
           && planet != null
           && (planet.Owner == ship.Loyalty
               || ship.Loyalty.IsTradeTreaty(planet.Owner)
               || planet.IsMineable
               || planet.IsResearchable);

    AuthoritativeCommandResult ApplyShipAreaOfOperation(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.TargetId < byte.MinValue || command.TargetId > byte.MaxValue
            || !Enum.IsDefined(typeof(AuthoritativeShipAreaOfOperationAction),
                (AuthoritativeShipAreaOfOperationAction)(byte)command.TargetId))
        {
            return Reject(result, $"Unsupported ship area-of-operation action {command.TargetId}.");
        }

        if (!AuthoritativePlayerCommand.TryParseRectanglePayload(command.Text, out Rectangle areaOrPoint))
            return Reject(result, $"Invalid area-of-operation payload '{command.Text}'.");

        Ship ship = UState.Objects.FindShip(command.SubjectId);
        if (ship == null)
            return Reject(result, $"Ship {command.SubjectId} not found.");
        if (!ship.Active)
            return Reject(result, $"Ship {command.SubjectId} is inactive.");
        if (ship.Loyalty != empire)
            return Reject(result, $"Ship {command.SubjectId} is not owned by empire {empire.Id}.");
        if (!ship.IsFreighter)
            return Reject(result, $"Ship {ship.Id} is not a freighter.");

        ship.AreaOfOperation ??= new Array<Rectangle>();
        switch ((AuthoritativeShipAreaOfOperationAction)command.TargetId)
        {
            case AuthoritativeShipAreaOfOperationAction.AddRectangle:
                if (areaOrPoint.Width < 5000 || areaOrPoint.Height < 5000)
                    return Reject(result, "Area-of-operation rectangles must be at least 5000x5000.");

                ship.AreaOfOperation.Add(areaOrPoint);
                return Accept(result);

            case AuthoritativeShipAreaOfOperationAction.RemoveAtPoint:
                ship.AreaOfOperation.RemoveFirst(ao => ao.HitTest(new Vector2(areaOrPoint.X, areaOrPoint.Y)));
                return Accept(result);

            default:
                return Reject(result, $"Unsupported ship area-of-operation action {command.TargetId}.");
        }
    }

    AuthoritativeCommandResult ApplyShipCarrierPolicy(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.TargetId < byte.MinValue || command.TargetId > byte.MaxValue
            || !Enum.IsDefined(typeof(AuthoritativeShipCarrierPolicyKind),
                (AuthoritativeShipCarrierPolicyKind)(byte)command.TargetId))
        {
            return Reject(result, $"Unsupported ship carrier policy {command.TargetId}.");
        }
        if (command.Text is not ("0" or "1"))
            return Reject(result, "Ship carrier policy enabled flag must be 0 or 1.");

        Ship ship = UState.Objects.FindShip(command.SubjectId);
        if (ship == null)
            return Reject(result, $"Ship {command.SubjectId} not found.");
        if (!ship.Active)
            return Reject(result, $"Ship {command.SubjectId} is inactive.");
        if (ship.Loyalty != empire)
            return Reject(result, $"Ship {command.SubjectId} is not owned by empire {empire.Id}.");

        var policy = (AuthoritativeShipCarrierPolicyKind)command.TargetId;
        if (!CanApplyShipCarrierPolicy(ship, policy))
            return Reject(result, $"Ship {ship.Id} cannot receive carrier policy {policy}.");

        bool enabled = command.Text == "1";
        switch (policy)
        {
            case AuthoritativeShipCarrierPolicyKind.FightersOut:
                ship.Carrier.FightersOut = enabled;
                return Accept(result);
            case AuthoritativeShipCarrierPolicyKind.TroopsOut:
                ship.Carrier.TroopsOut = enabled;
                return Accept(result);
            case AuthoritativeShipCarrierPolicyKind.RecallFightersBeforeFTL:
                ship.Carrier.SetRecallFightersBeforeFTL(enabled);
                ship.ManualHangarOverride = !enabled;
                return Accept(result);
            case AuthoritativeShipCarrierPolicyKind.SendTroopsToShip:
                ship.Carrier.SetSendTroopsToShip(enabled);
                return Accept(result);
            case AuthoritativeShipCarrierPolicyKind.AllowBoardShip:
                ship.Carrier.AllowBoardShip = enabled;
                return Accept(result);
            default:
                return Reject(result, $"Unsupported ship carrier policy {command.TargetId}.");
        }
    }

    static bool CanApplyShipCarrierPolicy(Ship ship, AuthoritativeShipCarrierPolicyKind policy)
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

    AuthoritativeCommandResult ApplyAttackShip(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Ship ship = UState.Objects.FindShip(command.SubjectId);
        if (ship == null)
            return Reject(result, $"Ship {command.SubjectId} not found.");
        if (!ship.Active)
            return Reject(result, $"Ship {command.SubjectId} is inactive.");
        if (ship.Loyalty != empire)
            return Reject(result, $"Ship {command.SubjectId} is not owned by empire {empire.Id}.");
        if (ship.ShipData.Role == RoleName.troop)
            return Reject(result, "Authoritative attack MVP does not support troop boarding orders.");

        Ship target = UState.Objects.FindShip(command.TargetId);
        if (target == null)
            return Reject(result, $"Target ship {command.TargetId} not found.");
        if (!target.Active)
            return Reject(result, $"Target ship {command.TargetId} is inactive.");
        if (target == ship)
            return Reject(result, "A ship cannot attack itself.");
        if (!TryEnsureHostileShipTarget(empire, target, "attack", result, out AuthoritativeCommandResult reject))
            return reject;

        bool queue = string.Equals(command.Text, "queue", StringComparison.Ordinal);
        if (queue)
            ship.AI.OrderQueueSpecificTarget(target);
        else
            ship.AI.OrderAttackSpecificTarget(target);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyShipTargetOrder(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (!AuthoritativePlayerCommand.TryParseShipTargetOrderPayload(command.Text,
                out AuthoritativeShipTargetOrderType orderType, out bool queue))
        {
            return Reject(result, $"Invalid ship target order payload '{command.Text}'.");
        }

        Ship ship = UState.Objects.FindShip(command.SubjectId);
        if (ship == null)
            return Reject(result, $"Ship {command.SubjectId} not found.");
        if (!ship.Active)
            return Reject(result, $"Ship {command.SubjectId} is inactive.");
        if (ship.Loyalty != empire)
            return Reject(result, $"Ship {command.SubjectId} is not owned by empire {empire.Id}.");

        Ship target = UState.Objects.FindShip(command.TargetId);
        if (target == null)
            return Reject(result, $"Target ship {command.TargetId} not found.");
        if (!target.Active)
            return Reject(result, $"Target ship {command.TargetId} is inactive.");
        if (target == ship)
            return Reject(result, "A ship cannot target itself.");
        if (target.Loyalty == null)
            return Reject(result, $"Target ship {target.Id} has no owning empire.");
        if (queue && orderType != AuthoritativeShipTargetOrderType.Attack)
            return Reject(result, $"Ship target order {orderType} does not support queueing.");

        switch (orderType)
        {
            case AuthoritativeShipTargetOrderType.Attack:
                if (ship.IsPlatformOrStation || ship.ShipData.Role == RoleName.troop)
                    return Reject(result, $"Ship {ship.Id} cannot receive authoritative attack target orders.");
                if (!TryEnsureHostileShipTarget(empire, target, "attack", result,
                        out AuthoritativeCommandResult rejectedAttack))
                    return rejectedAttack;
                if (queue)
                    ship.AI.OrderQueueSpecificTarget(target);
                else
                    ship.AI.OrderAttackSpecificTarget(target);
                return Accept(result);

            case AuthoritativeShipTargetOrderType.Escort:
                if (target.Loyalty != empire)
                    return Reject(result, $"Ship {target.Id} is not a friendly escort target.");
                ship.AI.AddEscortGoal(target);
                return Accept(result);

            case AuthoritativeShipTargetOrderType.TransferTroops:
                if (target.Loyalty != empire)
                    return Reject(result, $"Ship {target.Id} is not a friendly troop transfer target.");
                if (!IsSingleTroopTargetOrderShip(ship) || ship.TroopCount == 0)
                    return Reject(result, $"Ship {ship.Id} has no transferable troop.");
                if (target.TroopCapacity <= target.TroopCount)
                    return Reject(result, $"Ship {target.Id} has no troop capacity available.");
                ship.AI.OrderTroopToShip(target);
                return Accept(result);

            case AuthoritativeShipTargetOrderType.Board:
                if (!TryEnsureHostileShipTarget(empire, target, "board", result,
                        out AuthoritativeCommandResult rejectedBoard))
                    return rejectedBoard;
                if (!IsSingleTroopTargetOrderShip(ship) || ship.TroopCount == 0)
                    return Reject(result, $"Ship {ship.Id} has no boarding troop.");
                ship.AI.OrderTroopToBoardShip(target);
                return Accept(result);

            default:
                return Reject(result, $"Unsupported ship target order {orderType}.");
        }
    }

    bool TryEnsureHostileShipTarget(Empire empire, Ship target, string verb, AuthoritativeCommandResult result,
        out AuthoritativeCommandResult reject)
    {
        reject = null;
        if (target?.Loyalty == null)
        {
            reject = Reject(result, $"Target ship {target?.Id ?? 0} has no owning empire.");
            return false;
        }

        Empire targetEmpire = target.Loyalty;
        if (targetEmpire == empire)
        {
            reject = Reject(result, $"Empire {empire.Id} cannot {verb} ship {target.Id}.");
            return false;
        }

        if (empire.IsEmpireAttackable(targetEmpire, target))
            return true;

        string reason = "";
        if (Diplomacy != null
            && AuthoritativeHumanPlayers.IsHumanVsHuman(empire, targetEmpire)
            && Diplomacy.TryDeclareWarForHostileHumanAction(empire, targetEmpire,
                $"{verb} ship {target.Id}", out reason)
            && empire.IsEmpireAttackable(targetEmpire, target))
        {
            return true;
        }

        reject = Reject(result, reason.NotEmpty()
            ? reason
            : $"Empire {empire.Id} cannot {verb} ship {target.Id}.");
        return false;
    }

    AuthoritativeCommandResult ApplyShipPlanetOrder(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (!TryParsePlanetOrder(command.Text, out AuthoritativeShipPlanetOrderType orderType,
                out bool clearOrders, out MoveOrder moveOrder))
        {
            return Reject(result, $"Invalid ship planet order payload '{command.Text}'.");
        }

        Ship ship = UState.Objects.FindShip(command.SubjectId);
        if (ship == null)
            return Reject(result, $"Ship {command.SubjectId} not found.");
        if (!ship.Active)
            return Reject(result, $"Ship {command.SubjectId} is inactive.");
        if (ship.Loyalty != empire)
            return Reject(result, $"Ship {command.SubjectId} is not owned by empire {empire.Id}.");
        if (ship.IsConstructor || ship.IsPlatformOrStation || ship.IsSubspaceProjector)
            return Reject(result, $"Ship {command.SubjectId} cannot receive authoritative planet orders.");

        Planet planet = UState.GetPlanet(command.TargetId);
        if (planet == null)
            return Reject(result, $"Planet {command.TargetId} not found.");

        const MoveOrder Allowed = MoveOrder.Regular | MoveOrder.Aggressive | MoveOrder.StandGround;
        if ((moveOrder & ~Allowed) != 0)
            return Reject(result, $"Planet move order {moveOrder} is not supported by authoritative MP.");

        switch (orderType)
        {
            case AuthoritativeShipPlanetOrderType.Orbit:
                ship.OrderToOrbit(planet, clearOrders, moveOrder);
                return Accept(result);

            case AuthoritativeShipPlanetOrderType.Colonize:
                if (!ship.ShipData.IsColonyShip)
                    return Reject(result, $"Ship {ship.Id} is not a colony ship.");
                if (!planet.Habitable || planet.Owner != null)
                    return Reject(result, $"Planet {planet.Id} is not a legal colonization target.");
                empire.AI.AddGoalAndEvaluate(new MarkForColonization(ship, planet, empire));
                return Accept(result);

            case AuthoritativeShipPlanetOrderType.Bombard:
                if (!ship.HasBombs)
                    return Reject(result, $"Ship {ship.Id} cannot bombard planets.");
                if (planet.Owner == null || planet.Owner == empire || !empire.IsEmpireAttackable(planet.Owner))
                    return Reject(result, $"Empire {empire.Id} cannot bombard planet {planet.Id}.");
                ship.AI.OrderBombardPlanet(planet, clearOrders);
                return Accept(result);

            case AuthoritativeShipPlanetOrderType.LandTroops:
                if (!ship.Carrier.AnyAssaultOpsAvailable && !IsSingleTroopTargetOrderShip(ship)
                                                        && !ship.IsDefaultAssaultShuttle)
                    return Reject(result, $"Ship {ship.Id} has no assault troops available.");
                if (!planet.Habitable)
                    return Reject(result, $"Planet {planet.Id} is not habitable.");
                bool legalLanding = planet.Owner == null
                                    || planet.Owner == empire
                                    || planet.Owner != empire && empire.IsAtWarWith(planet.Owner);
                if (!legalLanding)
                    return Reject(result, $"Empire {empire.Id} cannot land troops on planet {planet.Id}.");
                return ship.AI.OrderLandAllTroops(planet, clearOrders)
                    ? Accept(result)
                    : Reject(result, $"Ship {ship.Id} could not receive troop landing order.");

            default:
                return Reject(result, $"Unsupported planet order {orderType}.");
        }
    }

    static bool IsSingleTroopTargetOrderShip(Ship ship)
        => ship?.DesignRole == RoleName.troop || ship?.ShipData.Role == RoleName.troop;

    AuthoritativeCommandResult ApplyColonyType(AuthoritativePlayerCommand command, Empire empire, AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");
        if (!System.Enum.IsDefined(typeof(Planet.ColonyType), command.TargetId))
            return Reject(result, $"Invalid colony type {command.TargetId}.");

        var type = (Planet.ColonyType)command.TargetId;
        planet.CType = type;
        if (type is Planet.ColonyType.Colony or Planet.ColonyType.TradeHub)
        {
            planet.RemoveBlueprints();
            planet.SetSpecializedTradeHub(false);
        }
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyColonizationGoal(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} not found.");
        if (planet.Owner != null)
            return Reject(result, $"Planet {planet.Id} is already owned.");
        if (!planet.Habitable)
            return Reject(result, $"Planet {planet.Id} is not habitable.");
        if (command.TargetId is not (0 or 1))
            return Reject(result, $"Unsupported colonization goal state {command.TargetId}.");

        bool enabled = command.TargetId == 1;
        bool alreadyMarked = empire.AI.HasGoal(g => g.IsColonizationGoal(planet));
        if (enabled)
        {
            if (!alreadyMarked)
                empire.AI.AddGoalAndEvaluate(new MarkForColonization(planet, empire, isManual: true));
            return Accept(result);
        }

        if (alreadyMarked)
            empire.AI.CancelColonization(planet);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyDeepSpaceBuild(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (!AuthoritativePlayerCommand.TryParseDeepSpaceBuildPayload(command.Text,
                out string designName, out Vector2 tetherOffset))
        {
            return Reject(result, "Invalid deep-space build payload.");
        }
        if (!float.IsFinite(command.Position.X) || !float.IsFinite(command.Position.Y))
            return Reject(result, "Deep-space build position is not finite.");
        if (!ResourceManager.Ships.GetDesign(designName, out IShipDesign design))
            return Reject(result, $"Deep-space build design '{designName}' was not found.");
        if (!empire.CanBuildStation(design))
            return Reject(result, $"Empire {empire.Id} cannot build deep-space design '{designName}'.");

        Planet targetPlanet = command.SubjectId == 0 ? null : UState.GetPlanet(command.SubjectId);
        if (command.SubjectId != 0 && targetPlanet == null)
            return Reject(result, $"Deep-space target planet {command.SubjectId} was not found.");

        SolarSystem targetSystem = command.TargetId == 0 ? null : UState.Systems.FirstOrDefault(s => s.Id == command.TargetId);
        if (command.TargetId != 0 && targetSystem == null)
            return Reject(result, $"Deep-space target system {command.TargetId} was not found.");
        targetSystem ??= targetPlanet?.System;
        if (targetPlanet != null && targetSystem != targetPlanet.System)
            return Reject(result, $"Deep-space target planet {targetPlanet.Id} is not in system {targetSystem?.Id ?? 0}.");

        if (!CanQueueDeepSpaceBuild(empire, design, command.Position, targetPlanet, targetSystem))
            return Reject(result, $"Deep-space build placement is not legal for '{designName}'.");

        if (design.IsResearchStation)
        {
            if (targetPlanet != null)
                empire.AI.AddGoalAndEvaluate(new ProcessResearchStation(empire, targetPlanet, design, tetherOffset));
            else
                empire.AI.AddGoalAndEvaluate(new ProcessResearchStation(empire, targetSystem, command.Position, design));
        }
        else if (design.IsMiningStation)
        {
            empire.AI.AddGoalAndEvaluate(new MiningOps(empire, targetPlanet, design, tetherOffset));
        }
        else if (targetPlanet != null)
        {
            empire.AI.AddGoalAndEvaluate(new BuildConstructionShip(command.Position, design.Name, empire,
                targetPlanet, tetherOffset));
        }
        else
        {
            empire.AI.AddGoalAndEvaluate(new BuildConstructionShip(command.Position, design.Name, empire,
                manualPlacement: true));
        }

        return Accept(result);
    }

    AuthoritativeCommandResult ApplyCancelDeepSpaceBuild(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (!AuthoritativePlayerCommand.TryParseDeepSpaceCancelPayload(command.Text,
                out string designName, out GoalType goalType))
        {
            return Reject(result, "Invalid deep-space cancel payload.");
        }
        if (!float.IsFinite(command.Position.X) || !float.IsFinite(command.Position.Y))
            return Reject(result, "Deep-space cancel position is not finite.");

        Goal goal = empire.AI.Goals.FirstOrDefault(g => IsDeepSpaceBuildStateGoal(g)
            && g.Type == goalType
            && string.Equals(g.ToBuild?.Name, designName, StringComparison.Ordinal)
            && (g.TargetPlanet?.Id ?? 0) == command.SubjectId
            && (DeepSpaceGoalSystem(g)?.Id ?? 0) == command.TargetId
            && g.BuildPosition.AlmostEqual(command.Position, 1f));
        if (goal == null)
            return Reject(result, $"Deep-space build goal '{designName}' was not found.");

        CancelDeepSpaceBuildGoal(empire, goal);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyPlanetOrbitalBuild(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} was not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");

        string designName = command.Text?.Trim() ?? "";
        if (!ResourceManager.Ships.GetDesign(designName, out IShipDesign design))
            return Reject(result, $"Orbital design '{designName}' was not found.");
        if (!CanQueuePlanetOrbitalBuild(empire, planet, design))
            return Reject(result, $"Empire {empire.Id} cannot build orbital '{designName}' at planet {planet.Id}.");

        planet.AddOrbital(design);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyBuildCapitalHere(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} was not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");

        planet.BuildCapitalHere();
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyColonyBlueprints(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} was not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");
        if (!AuthoritativePlayerCommand.TryParseBlueprintsTemplate(command.Text, out BlueprintsTemplate template))
            return Reject(result, "Invalid colony blueprints payload.");
        if (!CanApplyColonyBlueprints(template, out string reason))
            return Reject(result, reason);

        planet.DontScrapBuildings = false;
        planet.SetSpecializedTradeHub(false);
        planet.AddBlueprints(template, empire);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyClearColonyBlueprints(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} was not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");

        planet.RemoveBlueprints();
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyScrapColonyTile(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} was not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");
        if (!AuthoritativePlayerCommand.TryParseTileCoordinates(command.Position, out int tileX, out int tileY))
            return Reject(result, "Invalid colony tile coordinate payload.");
        if (command.TargetId is < byte.MinValue or > byte.MaxValue
            || !Enum.IsDefined(typeof(AuthoritativeColonyTileScrapKind), (byte)command.TargetId))
            return Reject(result, $"Unsupported colony tile scrap kind {command.TargetId}.");

        PlanetGridSquare tile = planet.TilesList.FirstOrDefault(t => t.X == tileX && t.Y == tileY);
        if (tile == null)
            return Reject(result, $"Planet {planet.Id} has no tile at {tileX},{tileY}.");

        switch ((AuthoritativeColonyTileScrapKind)command.TargetId)
        {
            case AuthoritativeColonyTileScrapKind.Building:
                return ScrapBuildingTile(planet, tile, command.Text?.Trim() ?? "", result);
            case AuthoritativeColonyTileScrapKind.Biosphere:
                return ScrapBiosphereTile(planet, tile, result);
            default:
                return Reject(result, $"Unsupported colony tile scrap kind {command.TargetId}.");
        }
    }

    AuthoritativeCommandResult ScrapBuildingTile(Planet planet, PlanetGridSquare tile, string expectedBuildingName,
        AuthoritativeCommandResult result)
    {
        Building building = tile.Building;
        if (building == null)
            return Reject(result, $"Planet {planet.Id} tile {tile.X},{tile.Y} has no building to scrap.");
        if (string.IsNullOrWhiteSpace(expectedBuildingName)
            || !string.Equals(building.Name, expectedBuildingName, StringComparison.Ordinal))
        {
            return Reject(result,
                $"Planet {planet.Id} tile {tile.X},{tile.Y} no longer contains building '{expectedBuildingName}'.");
        }
        if (!building.Scrappable)
            return Reject(result, $"Building '{building.Name}' on planet {planet.Id} is not scrappable.");

        planet.ScrapBuilding(building);
        planet.RefreshBuildingsWeCanBuildHere();
        return Accept(result);
    }

    AuthoritativeCommandResult ScrapBiosphereTile(Planet planet, PlanetGridSquare tile,
        AuthoritativeCommandResult result)
    {
        if (!tile.Biosphere)
            return Reject(result, $"Planet {planet.Id} tile {tile.X},{tile.Y} has no biosphere to scrap.");

        bool destroyBuilding = !tile.Building?.CanBuildAnywhere == true;
        planet.DestroyBioSpheres(tile, destroyBuilding);
        planet.RefreshBuildingsWeCanBuildHere();
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyGroundTroopOrder(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.TargetId < byte.MinValue || command.TargetId > byte.MaxValue
            || !Enum.IsDefined(typeof(AuthoritativeGroundTroopOrderType),
                (AuthoritativeGroundTroopOrderType)(byte)command.TargetId))
        {
            return Reject(result, $"Unsupported ground troop order {command.TargetId}.");
        }

        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} was not found.");
        if (!planet.Habitable)
            return Reject(result, $"Planet {planet.Id} cannot host ground troops.");
        if (!AuthoritativePlayerCommand.TryParseGroundTroopOrderPayload(command.Text,
                out int troopIndex, out int targetTileX, out int targetTileY, out string expectedTroopName))
        {
            return Reject(result, "Invalid ground troop payload.");
        }

        return (AuthoritativeGroundTroopOrderType)(byte)command.TargetId switch
        {
            AuthoritativeGroundTroopOrderType.LaunchOne =>
                ApplyLaunchOneGroundTroop(command, empire, planet, troopIndex, expectedTroopName, result),
            AuthoritativeGroundTroopOrderType.LaunchAll =>
                ApplyLaunchGroundTroops(empire, planet, recall: false, result),
            AuthoritativeGroundTroopOrderType.RecallAll =>
                ApplyLaunchGroundTroops(empire, planet, recall: true, result),
            AuthoritativeGroundTroopOrderType.Move =>
                ApplyMoveGroundTroop(command, empire, planet, troopIndex, expectedTroopName,
                    targetTileX, targetTileY, result),
            AuthoritativeGroundTroopOrderType.AttackTroop =>
                ApplyGroundTroopAttackTroop(command, empire, planet, troopIndex, expectedTroopName,
                    targetTileX, targetTileY, result),
            AuthoritativeGroundTroopOrderType.AttackBuilding =>
                ApplyGroundTroopAttackBuilding(command, empire, planet, troopIndex, expectedTroopName,
                    targetTileX, targetTileY, result),
            AuthoritativeGroundTroopOrderType.BuildingAttackTroop =>
                ApplyBuildingAttackGroundTroop(command, empire, planet, troopIndex,
                    targetTileX, targetTileY, result),
            _ => Reject(result, $"Unsupported ground troop order {command.TargetId}."),
        };
    }

    AuthoritativeCommandResult ApplyLaunchOneGroundTroop(AuthoritativePlayerCommand command, Empire empire,
        Planet planet, int troopIndex, string expectedTroopName, AuthoritativeCommandResult result)
    {
        if (!TryGetOwnedGroundTroop(command, empire, planet, troopIndex, expectedTroopName,
                result, out PlanetGridSquare tile, out Troop troop, out AuthoritativeCommandResult rejected))
        {
            return rejected;
        }
        if (!troop.CanLaunch)
            return Reject(result, $"Troop {troop.Name} at planet {planet.Id} tile {tile.X},{tile.Y} cannot launch.");

        Ship troopShip = troop.Launch(tile);
        return troopShip != null
            ? Accept(result)
            : Reject(result, $"Troop {troop.Name} at planet {planet.Id} failed to launch.");
    }

    AuthoritativeCommandResult ApplyLaunchGroundTroops(Empire empire, Planet planet, bool recall,
        AuthoritativeCommandResult result)
    {
        Troop[] troops = planet.Troops.GetLaunchableTroops(empire).ToArray();
        if (troops.Length == 0)
            return Reject(result, $"Planet {planet.Id} has no launchable troops for empire {empire.Id}.");

        bool launched = false;
        foreach (Troop troop in troops)
        {
            Ship troopShip = troop.Launch();
            if (troopShip == null)
                continue;

            launched = true;
            if (recall)
                troopShip.AI.OrderRebaseToNearest();
        }

        return launched
            ? Accept(result)
            : Reject(result, $"Planet {planet.Id} could not launch any troops for empire {empire.Id}.");
    }

    AuthoritativeCommandResult ApplyMoveGroundTroop(AuthoritativePlayerCommand command, Empire empire,
        Planet planet, int troopIndex, string expectedTroopName, int targetTileX, int targetTileY,
        AuthoritativeCommandResult result)
    {
        if (!TryGetOwnedGroundTroop(command, empire, planet, troopIndex, expectedTroopName,
                result, out PlanetGridSquare sourceTile, out Troop troop, out AuthoritativeCommandResult rejected))
        {
            return rejected;
        }

        PlanetGridSquare targetTile = FindTile(planet, targetTileX, targetTileY);
        if (targetTile == null)
            return Reject(result, $"Planet {planet.Id} has no target tile at {targetTileX},{targetTileY}.");
        if (sourceTile == targetTile)
            return Reject(result, "Ground troop move target must differ from the source tile.");
        if (!troop.CanMove)
            return Reject(result, $"Troop {troop.Name} at planet {planet.Id} cannot move.");
        if (!targetTile.IsTileFree(empire))
            return Reject(result, $"Planet {planet.Id} target tile {targetTile.X},{targetTile.Y} is not free.");

        int dx = Math.Abs(targetTile.X - sourceTile.X);
        int dy = Math.Abs(targetTile.Y - sourceTile.Y);
        if (dx > troop.ActualRange || dy > troop.ActualRange)
            return Reject(result, $"Planet {planet.Id} target tile {targetTile.X},{targetTile.Y} is out of troop range.");

        troop.facingRight = targetTile.X > sourceTile.X;
        planet.Troops.MoveTowardsTarget(troop, sourceTile, targetTile);
        if (sourceTile.TroopsHere.ContainsRef(troop))
            return Reject(result, $"Troop {troop.Name} at planet {planet.Id} could not move toward {targetTile.X},{targetTile.Y}.");

        planet.SetInGroundCombat(troop.Loyalty);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyGroundTroopAttackTroop(AuthoritativePlayerCommand command, Empire empire,
        Planet planet, int troopIndex, string expectedTroopName, int targetTileX, int targetTileY,
        AuthoritativeCommandResult result)
    {
        if (!TryGetOwnedGroundTroop(command, empire, planet, troopIndex, expectedTroopName,
                result, out PlanetGridSquare sourceTile, out Troop troop, out AuthoritativeCommandResult rejected))
        {
            return rejected;
        }
        if (!TryGetTargetTileInTroopRange(planet, sourceTile, troop.ActualRange, targetTileX, targetTileY,
                result, out PlanetGridSquare targetTile, out rejected))
        {
            return rejected;
        }
        if (!troop.CanAttack)
            return Reject(result, $"Troop {troop.Name} at planet {planet.Id} tile {sourceTile.X},{sourceTile.Y} cannot attack.");
        if (!targetTile.LockOnEnemyTroop(empire, out Troop enemyTroop))
            return Reject(result, $"Planet {planet.Id} target tile {targetTile.X},{targetTile.Y} has no attackable enemy troop.");

        troop.FaceEnemy(targetTile, sourceTile);
        troop.UpdateAttackActions(-1);
        troop.ResetAttackTimer();
        troop.UpdateMoveActions(-1);
        troop.ResetMoveTimer();
        CombatScreen.StartCombat(troop, enemyTroop, targetTile, planet);
        planet.SetInGroundCombat(troop.Loyalty);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyGroundTroopAttackBuilding(AuthoritativePlayerCommand command, Empire empire,
        Planet planet, int troopIndex, string expectedTroopName, int targetTileX, int targetTileY,
        AuthoritativeCommandResult result)
    {
        if (!TryGetOwnedGroundTroop(command, empire, planet, troopIndex, expectedTroopName,
                result, out PlanetGridSquare sourceTile, out Troop troop, out AuthoritativeCommandResult rejected))
        {
            return rejected;
        }
        if (!TryGetTargetTileInTroopRange(planet, sourceTile, troop.ActualRange, targetTileX, targetTileY,
                result, out PlanetGridSquare targetTile, out rejected))
        {
            return rejected;
        }
        if (!troop.CanAttack)
            return Reject(result, $"Troop {troop.Name} at planet {planet.Id} tile {sourceTile.X},{sourceTile.Y} cannot attack.");
        if (!targetTile.CombatBuildingOnTile || planet.Owner == empire)
            return Reject(result, $"Planet {planet.Id} target tile {targetTile.X},{targetTile.Y} has no attackable enemy building.");

        troop.FaceEnemy(targetTile, sourceTile);
        CombatScreen.StartCombat(troop, targetTile.Building, targetTile, planet);
        planet.SetInGroundCombat(troop.Loyalty);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyBuildingAttackGroundTroop(AuthoritativePlayerCommand command, Empire empire,
        Planet planet, int targetTroopIndex, int targetTileX, int targetTileY, AuthoritativeCommandResult result)
    {
        if (!AuthoritativePlayerCommand.TryParseTileCoordinates(command.Position, out int sourceTileX, out int sourceTileY))
            return Reject(result, "Invalid ground building tile coordinate payload.");

        PlanetGridSquare sourceTile = FindTile(planet, sourceTileX, sourceTileY);
        if (sourceTile == null)
            return Reject(result, $"Planet {planet.Id} has no tile at {sourceTileX},{sourceTileY}.");
        if (planet.Owner != empire || !sourceTile.CombatBuildingOnTile || !sourceTile.Building.CanAttack)
            return Reject(result, $"Planet {planet.Id} tile {sourceTile.X},{sourceTile.Y} has no owned building that can attack.");

        PlanetGridSquare targetTile = FindTile(planet, targetTileX, targetTileY);
        if (targetTile == null)
            return Reject(result, $"Planet {planet.Id} has no target tile at {targetTileX},{targetTileY}.");
        if (Math.Abs(targetTile.X - sourceTile.X) > 1 || Math.Abs(targetTile.Y - sourceTile.Y) > 1)
            return Reject(result, $"Planet {planet.Id} target tile {targetTile.X},{targetTile.Y} is out of building range.");
        if ((uint)targetTroopIndex >= targetTile.TroopsHere.Count
            || targetTile.TroopsHere[targetTroopIndex]?.Loyalty?.IsAtWarWith(empire) != true)
        {
            return Reject(result, $"Planet {planet.Id} target tile {targetTile.X},{targetTile.Y} has no attackable troop index {targetTroopIndex}.");
        }

        sourceTile.Building.UpdateAttackActions(-1);
        sourceTile.Building.ResetAttackTimer();
        CombatScreen.StartCombat(sourceTile.Building, targetTile.TroopsHere[targetTroopIndex], targetTile, planet);
        planet.SetInGroundCombat(empire);
        return Accept(result);
    }

    bool TryGetTargetTileInTroopRange(Planet planet, PlanetGridSquare sourceTile, int range,
        int targetTileX, int targetTileY, AuthoritativeCommandResult result,
        out PlanetGridSquare targetTile, out AuthoritativeCommandResult rejected)
    {
        rejected = null;
        targetTile = FindTile(planet, targetTileX, targetTileY);
        if (targetTile == null)
        {
            rejected = Reject(result, $"Planet {planet.Id} has no target tile at {targetTileX},{targetTileY}.");
            return false;
        }
        if (sourceTile == targetTile)
        {
            rejected = Reject(result, "Ground troop attack target must differ from the source tile.");
            return false;
        }
        if (Math.Abs(targetTile.X - sourceTile.X) > range || Math.Abs(targetTile.Y - sourceTile.Y) > range)
        {
            rejected = Reject(result, $"Planet {planet.Id} target tile {targetTile.X},{targetTile.Y} is out of troop range.");
            return false;
        }
        return true;
    }

    bool TryGetOwnedGroundTroop(AuthoritativePlayerCommand command, Empire empire, Planet planet, int troopIndex,
        string expectedTroopName, AuthoritativeCommandResult result, out PlanetGridSquare tile, out Troop troop,
        out AuthoritativeCommandResult rejected)
    {
        tile = null;
        troop = null;
        rejected = null;

        if (!AuthoritativePlayerCommand.TryParseTileCoordinates(command.Position, out int tileX, out int tileY))
        {
            rejected = Reject(result, "Invalid ground troop tile coordinate payload.");
            return false;
        }

        tile = FindTile(planet, tileX, tileY);
        if (tile == null)
        {
            rejected = Reject(result, $"Planet {planet.Id} has no tile at {tileX},{tileY}.");
            return false;
        }
        if ((uint)troopIndex >= tile.TroopsHere.Count)
        {
            rejected = Reject(result, $"Planet {planet.Id} tile {tileX},{tileY} has no troop index {troopIndex}.");
            return false;
        }

        troop = tile.TroopsHere[troopIndex];
        if (troop?.Loyalty != empire)
        {
            rejected = Reject(result, $"Planet {planet.Id} tile {tileX},{tileY} troop {troopIndex} is not owned by empire {empire.Id}.");
            return false;
        }
        if (!string.IsNullOrEmpty(expectedTroopName)
            && !string.Equals(troop.Name, expectedTroopName, StringComparison.Ordinal))
        {
            rejected = Reject(result, $"Planet {planet.Id} tile {tileX},{tileY} troop {troopIndex} no longer matches {expectedTroopName}.");
            return false;
        }

        return true;
    }

    static PlanetGridSquare FindTile(Planet planet, int x, int y)
        => planet.TilesList.FirstOrDefault(t => t.X == x && t.Y == y);

    static bool CanApplyColonyBlueprints(BlueprintsTemplate template, out string reason)
    {
        reason = "";
        if (template == null)
        {
            reason = "Blueprints payload was empty.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(template.Name))
        {
            reason = "Blueprints name was empty.";
            return false;
        }
        if (!string.IsNullOrEmpty(template.LinkTo))
        {
            reason = "Linked colony blueprints are not supported in authoritative multiplayer.";
            return false;
        }
        if (!Enum.IsDefined(typeof(Planet.ColonyType), template.ColonyType)
            || template.ColonyType == Planet.ColonyType.TradeHub)
        {
            reason = $"Blueprints colony type '{template.ColonyType}' is not valid.";
            return false;
        }
        if (template.PlannedBuildings == null || template.PlannedBuildings.Count == 0)
        {
            reason = "Blueprints must include at least one building.";
            return false;
        }
        if (template.PlannedBuildings.Count > AuthoritativePlayerCommand.MaxColonyBlueprintBuildings)
        {
            reason = "Blueprints include too many buildings.";
            return false;
        }

        foreach (string buildingName in template.PlannedBuildings)
        {
            if (string.IsNullOrWhiteSpace(buildingName)
                || !ResourceManager.GetBuilding(buildingName, out Building building))
            {
                reason = $"Blueprints building '{buildingName}' was not found.";
                return false;
            }
            if (!building.IsSuitableForBlueprints)
            {
                reason = $"Building '{buildingName}' is not suitable for colony blueprints.";
                return false;
            }
        }

        return true;
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

    static void CancelDeepSpaceBuildGoal(Empire empire, Goal goal)
    {
        empire.AI.RemoveGoal(goal);

        bool foundConstructor = false;
        foreach (Ship ship in empire.OwnedShips)
        {
            if (!ship.IsConstructor || ship.AI.OrderQueue.IsEmpty)
                continue;

            for (int i = 0; i < ship.AI.OrderQueue.Count; ++i)
            {
                if (ship.AI.OrderQueue[i].Goal != goal)
                    continue;

                foundConstructor = true;
                ship.AI.OrderScrapShip();
                break;
            }
        }

        if (foundConstructor)
            return;

        foreach (Planet planet in empire.GetPlanets())
            foreach (QueueItem item in planet.ConstructionQueue)
                if (item.Goal == goal)
                    item.IsCancelled = true;
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

    static bool CanQueueDeepSpaceBuild(Empire empire, IShipDesign design, Vector2 worldPos,
        Planet targetPlanet, SolarSystem targetSystem)
    {
        const float MinimumBuildDistanceFromSun = 20_000f;

        if (design == null)
            return false;
        if (targetSystem != null && (worldPos.InRadius(targetSystem.Position, MinimumBuildDistanceFromSun)
                                     || !targetSystem.InSafeDistanceFromRadiation(worldPos)))
        {
            return false;
        }
        if (targetSystem != null && targetPlanet == null && design.IsMiningStation)
            return false;

        if (targetPlanet != null)
        {
            if (targetPlanet.IsOutOfOrbitalsLimit(design))
                return false;
            if (design.IsResearchStation && !targetPlanet.CanBeResearchedBy(empire))
                return false;
            if (design.IsMiningStation && (!targetPlanet.IsMineable || !targetPlanet.Mining.CanAddMiningStationFor(empire)))
                return false;
        }
        else
        {
            if (design.IsShipyard || design.IsMiningStation)
                return false;
            if (targetSystem != null && design.IsResearchStation)
            {
                if (worldPos.OutsideRadius(targetSystem.Position, targetSystem.Radius * 0.3f))
                    return false;
                if (!targetSystem.CanBeResearchedBy(empire))
                    return false;
            }
            if (targetSystem == null && design.IsResearchStation)
                return false;
        }

        return true;
    }

    AuthoritativeCommandResult ApplyColonyLabor(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");
        if (planet.CType is not (Planet.ColonyType.Colony or Planet.ColonyType.TradeHub))
            return Reject(result, $"Planet {command.SubjectId} is governed by {planet.CType} and cannot receive manual labor.");
        if (!AuthoritativePlayerCommand.TryParseColonyLaborPayload(command.Text, out float food,
                out float production, out float research, out bool foodLocked, out bool productionLocked,
                out bool researchLocked))
        {
            return Reject(result, $"Invalid colony labor payload '{command.Text}'.");
        }
        if (!IsValidLaborPercent(food) || !IsValidLaborPercent(production) || !IsValidLaborPercent(research))
            return Reject(result, "Colony labor percentages must be finite values between 0 and 1.");

        float sum = food + production + research;
        if (Math.Abs(sum - 1f) > 0.0001f)
            return Reject(result, $"Colony labor percentages must sum to 1, got {sum}.");
        if (planet.IsCybernetic && Math.Abs(food) > 0.0001f)
            return Reject(result, "Cybernetic colonies cannot assign food labor.");

        planet.Food.Percent = food;
        planet.Prod.Percent = production;
        planet.Res.Percent = research;
        planet.Food.PercentLock = foodLocked || planet.IsCybernetic;
        planet.Prod.PercentLock = productionLocked;
        planet.Res.PercentLock = researchLocked;
        planet.UpdateIncomes();
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyResearchTopic(AuthoritativePlayerCommand command, Empire empire, AuthoritativeCommandResult result)
    {
        string techUid = command.Text ?? "";
        if (techUid.IsEmpty())
            return Reject(result, "Research topic is empty.");
        if (!ResourceManager.TryGetTech(techUid, out _))
            return Reject(result, $"Tech {techUid} not found.");
        if (!empire.TryGetTechEntry(techUid, out TechEntry entry))
            return Reject(result, $"Empire {empire.Id} has no tech entry for {techUid}.");
        if (entry.Unlocked)
            return Reject(result, $"Tech {techUid} is already unlocked.");

        empire.Research.SetTopic(techUid);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyQueueResearch(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (!TryGetResearchEntry(command.Text, empire, result, out string techUid, out TechEntry entry,
                out AuthoritativeCommandResult rejected))
        {
            return rejected;
        }
        if (!entry.Discovered)
            return Reject(result, $"Tech {techUid} is not discovered by empire {empire.Id}.");
        if (!entry.CanBeResearched)
            return Reject(result, $"Tech {techUid} cannot be researched by empire {empire.Id}.");

        empire.Research.AddTechToQueue(techUid);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyRemoveResearchQueueItem(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (!TryGetResearchEntry(command.Text, empire, result, out string techUid, out _,
                out AuthoritativeCommandResult rejected))
        {
            return rejected;
        }
        if (!empire.Research.IsQueued(techUid))
            return Reject(result, $"Tech {techUid} is not queued for empire {empire.Id}.");

        empire.Research.RemoveTechFromQueue(techUid);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyMoveResearchQueueItem(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (!TryGetResearchEntry(command.Text, empire, result, out string techUid, out _,
                out AuthoritativeCommandResult rejected))
        {
            return rejected;
        }
        if (command.TargetId < byte.MinValue || command.TargetId > byte.MaxValue
            || !Enum.IsDefined(typeof(AuthoritativeResearchQueueMove),
                (AuthoritativeResearchQueueMove)(byte)command.TargetId))
        {
            return Reject(result, $"Unsupported research queue move {command.TargetId}.");
        }

        int index = empire.Research.IndexInQueue(techUid);
        if (index < 0)
            return Reject(result, $"Tech {techUid} is not queued for empire {empire.Id}.");

        switch ((AuthoritativeResearchQueueMove)command.TargetId)
        {
            case AuthoritativeResearchQueueMove.Up:
                if (!empire.Research.CanMoveUp(index))
                    return Reject(result, $"Tech {techUid} cannot move up in the research queue.");
                empire.Research.MoveUp(index);
                return Accept(result);

            case AuthoritativeResearchQueueMove.Down:
                if (!empire.Research.CanMoveDown(index))
                    return Reject(result, $"Tech {techUid} cannot move down in the research queue.");
                empire.Research.MoveDown(index);
                return Accept(result);

            case AuthoritativeResearchQueueMove.ToTopOrPrereq:
                if (empire.Research.MoveToTopOrPreReq(index) == 0)
                    return Reject(result, $"Tech {techUid} could not move toward the top of the research queue.");
                return Accept(result);

            case AuthoritativeResearchQueueMove.ToTopWithPrereqs:
                if (empire.Research.MoveToTopWithPreReqs(index) == 0)
                    return Reject(result, $"Tech {techUid} could not move to the top of the research queue.");
                return Accept(result);

            default:
                return Reject(result, $"Unsupported research queue move {command.TargetId}.");
        }
    }

    AuthoritativeCommandResult ApplyEmpireBudget(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (!AuthoritativePlayerCommand.TryParseEmpireBudgetPayload(command.Text, out float taxRate,
                out float treasuryGoal, out bool autoTaxes))
        {
            return Reject(result, $"Invalid empire budget payload '{command.Text}'.");
        }
        if (!IsUnitPercent(taxRate) || !IsUnitPercent(treasuryGoal))
            return Reject(result, "Empire budget tax and treasury values must be finite values between 0 and 1.");

        empire.AutoTaxes = autoTaxes;
        empire.data.TaxRate = taxRate;
        empire.data.treasuryGoal = treasuryGoal;
        if (autoTaxes)
            empire.AI.RunEconomicPlanner();
        empire.UpdateNetPlanetIncomes();
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyDesignShip(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.Text.IsEmpty())
            return Reject(result, "Ship design payload is empty.");

        ShipDesign design;
        try
        {
            design = ShipDesign.FromBytes(Convert.FromBase64String(command.Text));
        }
        catch (Exception e)
        {
            return Reject(result, $"Ship design payload is invalid: {e.Message}");
        }

        if (design == null || design.Name.IsEmpty())
            return Reject(result, "Ship design payload did not produce a named design.");
        if (design.Hull.IsEmpty() || !ResourceManager.Hull(design.Hull, out _))
            return Reject(result, $"Ship design {design.Name} uses unknown hull {design.Hull}.");
        if (!empire.IsHullUnlocked(design.Hull))
            return Reject(result, $"Empire {empire.Id} has not unlocked hull {design.Hull}.");
        if (!design.IsValidDesign)
            return Reject(result, $"Ship design {design.Name} is not a valid buildable design.");
        if (design.IsPlatformOrStation)
            return Reject(result, "Authoritative shipyard MVP supports mobile ship designs only.");
        if (!CanBeAddedToHumanBuildables(design, empire))
            return Reject(result, $"Ship design {design.Name} is not legal for empire {empire.Id}.");
        if (!empire.WeCanBuildThis(design))
            return Reject(result, $"Empire {empire.Id} lacks technology or modules for design {design.Name}.");

        IShipDesign registered = RegisterPlayerDesign(design, out string conflictReason);
        if (registered == null)
            return Reject(result, conflictReason);

        empire.AddBuildableShip(registered);
        if (!empire.CanBuildShip(registered))
            return Reject(result, $"Empire {empire.Id} could not register buildable design {registered.Name}.");

        return Accept(result);
    }

    AuthoritativeCommandResult ApplyQueueBuild(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");
        if (!planet.HasSpacePort)
            return Reject(result, $"Planet {command.SubjectId} has no spaceport or shipyard.");

        string designName = command.Text ?? "";
        if (designName.IsEmpty())
            return Reject(result, "Build design name is empty.");
        if (!ResourceManager.Ships.GetDesign(designName, out IShipDesign design))
            return Reject(result, $"Ship design {designName} not found.");
        if (design.IsPlatformOrStation)
            return Reject(result, "Authoritative shipyard MVP queues mobile ships only.");
        if (!empire.CanBuildShip(design))
            return Reject(result, $"Empire {empire.Id} cannot build design {designName}.");

        planet.Construction.Enqueue(design, QueueTypeFor(design), notifyOnEmpty: false);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyQueueBuilding(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");

        string buildingName = command.Text ?? "";
        if (buildingName.IsEmpty())
            return Reject(result, "Building name is empty.");
        if (!ResourceManager.GetBuilding(buildingName, out Building template))
            return Reject(result, $"Building {buildingName} not found.");

        planet.RefreshBuildingsWeCanBuildHere();
        Building buildable = planet.GetBuildingsCanBuild()
            .FirstOrDefault(b => b.BID == template.BID || string.Equals(b.Name, buildingName, StringComparison.Ordinal));
        if (buildable == null)
            return Reject(result, $"Empire {empire.Id} cannot build {buildingName} at planet {planet.Id}.");

        PlanetGridSquare where = null;
        if (command.TargetId == 1)
        {
            int tileX = (int)command.Position.X;
            int tileY = (int)command.Position.Y;
            where = planet.TilesList.FirstOrDefault(tile => tile.X == tileX && tile.Y == tileY);
            if (where == null)
                return Reject(result, $"Tile {tileX},{tileY} was not found at planet {planet.Id}.");
            if (!where.CanEnqueueBuildingHere(buildable))
                return Reject(result, $"Tile {tileX},{tileY} cannot queue {buildingName} at planet {planet.Id}.");
        }
        else if (command.TargetId != 0)
        {
            return Reject(result, $"Unsupported building placement mode {command.TargetId}.");
        }

        return planet.Construction.Enqueue(buildable, where, playerAdded: true)
            ? Accept(result)
            : Reject(result, $"No valid tile was available for {buildingName} at planet {planet.Id}.");
    }

    AuthoritativeCommandResult ApplyQueueTroop(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");
        if (!planet.HasSpacePort)
            return Reject(result, $"Planet {command.SubjectId} has no spaceport for troop training.");

        string troopName = command.Text ?? "";
        if (troopName.IsEmpty())
            return Reject(result, "Troop name is empty.");
        if (!ResourceManager.GetTroopTemplate(troopName, out Troop template))
            return Reject(result, $"Troop {troopName} not found.");
        if (!empire.WeCanBuildTroop(template.Name))
            return Reject(result, $"Empire {empire.Id} cannot build troop {template.Name}.");

        planet.Construction.Enqueue(template, QueueItemType.Troop);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyCancelConstructionQueueItem(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (!TryGetOwnedQueueItem(command, empire, result, out Planet planet, out QueueItem item, out AuthoritativeCommandResult rejected))
            return rejected;
        if (item.IsComplete)
            return Reject(result, $"Construction queue item {command.TargetId} at planet {planet.Id} is already complete.");

        planet.Construction.Cancel(item);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyReorderConstructionQueueItem(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (!TryGetOwnedQueueItem(command, empire, result, out Planet planet, out _, out AuthoritativeCommandResult rejected))
            return rejected;

        int currentIndex = command.TargetId;
        int moveToIndex = (int)command.Position.X;
        int queueCount = planet.ConstructionQueue.Count;
        if ((uint)moveToIndex >= queueCount)
            return Reject(result, $"Construction queue target index {moveToIndex} is outside planet {planet.Id} queue.");
        if (currentIndex == moveToIndex)
            return Accept(result);

        planet.Construction.MoveTo(moveToIndex, currentIndex);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyRushConstructionQueueItem(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (!TryGetOwnedQueueItem(command, empire, result, out Planet planet, out QueueItem item,
                out AuthoritativeCommandResult rejected))
        {
            return rejected;
        }
        if (item.IsComplete)
            return Reject(result, $"Construction queue item {command.TargetId} at planet {planet.Id} is already complete.");

        float maxAmount = command.Position.X;
        if (!IsFinitePositive(maxAmount))
            return Reject(result, $"Rush production amount {maxAmount} is not valid.");

        return planet.Construction.RushProduction(command.TargetId, maxAmount, rushButton: true)
            ? Accept(result)
            : Reject(result, $"Planet {planet.Id} could not rush construction queue item {command.TargetId}.");
    }

    AuthoritativeCommandResult ApplyToggleConstructionRush(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (!TryGetOwnedQueueItem(command, empire, result, out Planet planet, out QueueItem item,
                out AuthoritativeCommandResult rejected))
        {
            return rejected;
        }
        if (item.IsComplete)
            return Reject(result, $"Construction queue item {command.TargetId} at planet {planet.Id} is already complete.");

        item.Rush = !item.Rush;
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyPlanetGoodsState(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");
        if (command.TargetId < byte.MinValue || command.TargetId > byte.MaxValue
            || !Enum.IsDefined(typeof(AuthoritativePlanetGoodsKind),
                (AuthoritativePlanetGoodsKind)(byte)command.TargetId))
        {
            return Reject(result, $"Unsupported planet goods kind {command.TargetId}.");
        }

        int stateValue = (int)command.Position.X;
        if (!Enum.IsDefined(typeof(Planet.GoodState), stateValue))
            return Reject(result, $"Unsupported planet goods state {stateValue}.");

        var state = (Planet.GoodState)stateValue;
        switch ((AuthoritativePlanetGoodsKind)command.TargetId)
        {
            case AuthoritativePlanetGoodsKind.Food:
                if (!planet.NonCybernetic)
                    return Reject(result, $"Planet {planet.Id} cannot set food trade policy.");
                planet.FS = state;
                return Accept(result);

            case AuthoritativePlanetGoodsKind.Production:
                planet.PS = state;
                return Accept(result);

            default:
                return Reject(result, $"Unsupported planet goods kind {command.TargetId}.");
        }
    }

    AuthoritativeCommandResult ApplyPlanetPrioritizedPort(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");

        bool prioritized = command.TargetId != 0;
        if (prioritized && !planet.HasSpacePort)
            return Reject(result, $"Planet {planet.Id} cannot be a prioritized port without a space port.");

        planet.SetPrioritizedPort(prioritized);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyPlanetManualBudget(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");
        if (command.TargetId < byte.MinValue || command.TargetId > byte.MaxValue
            || !Enum.IsDefined(typeof(AuthoritativePlanetBudgetKind),
                (AuthoritativePlanetBudgetKind)(byte)command.TargetId))
        {
            return Reject(result, $"Unsupported planet budget kind {command.TargetId}.");
        }

        float value = command.Position.X;
        if (!float.IsFinite(value) || value < 0f)
            return Reject(result, $"Planet manual budget must be a finite non-negative value, got {value}.");

        switch ((AuthoritativePlanetBudgetKind)command.TargetId)
        {
            case AuthoritativePlanetBudgetKind.Civilian:
                planet.SetManualCivBudget(value);
                return Accept(result);
            case AuthoritativePlanetBudgetKind.GroundDefense:
                planet.SetManualGroundDefBudget(value);
                return Accept(result);
            case AuthoritativePlanetBudgetKind.SpaceDefense:
                planet.SetManualSpaceDefBudget(value);
                return Accept(result);
            default:
                return Reject(result, $"Unsupported planet budget kind {command.TargetId}.");
        }
    }

    AuthoritativeCommandResult ApplyPlanetGovernorOptions(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");

        var options = (AuthoritativePlanetGovernorOptions)command.TargetId;
        if ((options & ~AuthoritativePlanetGovernorOptions.All) != 0)
            return Reject(result, $"Unsupported planet governor option flags {command.TargetId}.");

        planet.GovOrbitals = options.HasFlag(AuthoritativePlanetGovernorOptions.GovOrbitals);
        planet.AutoBuildTroops = options.HasFlag(AuthoritativePlanetGovernorOptions.AutoBuildTroops);
        planet.DontScrapBuildings = options.HasFlag(AuthoritativePlanetGovernorOptions.DontScrapBuildings);
        planet.Quarantine = options.HasFlag(AuthoritativePlanetGovernorOptions.Quarantine);
        planet.ManualOrbitals = options.HasFlag(AuthoritativePlanetGovernorOptions.ManualOrbitals);
        planet.GovGroundDefense = options.HasFlag(AuthoritativePlanetGovernorOptions.GovGroundDefense);
        planet.SetSpecializedTradeHub(options.HasFlag(AuthoritativePlanetGovernorOptions.SpecializedTradeHub));
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyPlanetManualTradeSlots(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");
        if (!AuthoritativePlayerCommand.TryParseManualTradeSlotsPayload(command.Text,
                out int foodImport, out int prodImport, out int coloImport,
                out int foodExport, out int prodExport, out int coloExport))
        {
            return Reject(result, $"Invalid planet manual trade slot payload '{command.Text}'.");
        }

        planet.ManualFoodImportSlots = foodImport;
        planet.ManualProdImportSlots = prodImport;
        planet.ManualColoImportSlots = coloImport;
        planet.ManualFoodExportSlots = foodExport;
        planet.ManualProdExportSlots = prodExport;
        planet.ManualColoExportSlots = coloExport;
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyPlanetDefenseTargets(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");
        if (!AuthoritativePlayerCommand.TryParsePlanetDefenseTargetsPayload(command.Text,
                out int garrisonSize, out int wantedPlatforms, out int wantedShipyards, out int wantedStations))
        {
            return Reject(result, $"Invalid planet defense target payload '{command.Text}'.");
        }

        planet.GarrisonSize = garrisonSize;
        planet.SetWantedPlatforms((byte)wantedPlatforms);
        planet.SetWantedShipyards((byte)wantedShipyards);
        planet.SetWantedStations((byte)wantedStations);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyFleetAssignment(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.SubjectId is < Empire.FirstFleetKey or > Empire.LastFleetKey)
            return Reject(result, $"Fleet key {command.SubjectId} is outside the player fleet range.");
        if (command.TargetId < byte.MinValue || command.TargetId > byte.MaxValue
            || !Enum.IsDefined(typeof(AuthoritativeFleetAssignmentMode),
                (AuthoritativeFleetAssignmentMode)(byte)command.TargetId))
        {
            return Reject(result, $"Unsupported fleet assignment mode {command.TargetId}.");
        }
        if (!AuthoritativePlayerCommand.TryParseIdList(command.Text, out int[] shipIds))
            return Reject(result, $"Invalid fleet ship id payload '{command.Text}'.");
        if (new HashSet<int>(shipIds).Count != shipIds.Length)
            return Reject(result, "Fleet assignment ship ids must be unique.");

        var mode = (AuthoritativeFleetAssignmentMode)command.TargetId;
        if (mode == AuthoritativeFleetAssignmentMode.Add && shipIds.Length == 0)
            return Reject(result, "Adding to a fleet requires at least one ship.");

        Ship[] ships = Array.Empty<Ship>();
        if (shipIds.Length > 0)
        {
            var resolved = new Ship[shipIds.Length];
            for (int i = 0; i < shipIds.Length; ++i)
            {
                Ship ship = UState.Objects.FindShip(shipIds[i]);
                if (ship == null)
                    return Reject(result, $"Ship {shipIds[i]} not found.");
                if (!ship.Active)
                    return Reject(result, $"Ship {ship.Id} is inactive.");
                if (ship.Loyalty != empire)
                    return Reject(result, $"Ship {ship.Id} is not owned by empire {empire.Id}.");
                if (!ship.CanBeAddedToFleets())
                    return Reject(result, $"Ship {ship.Id} cannot be assigned to fleets.");
                resolved[i] = ship;
            }
            ships = resolved;
        }

        Fleet existing = empire.GetFleetOrNull(command.SubjectId);
        switch (mode)
        {
            case AuthoritativeFleetAssignmentMode.Clear:
                ResetAndRemoveFleet(empire, existing, clearOrders: true);
                return Accept(result);

            case AuthoritativeFleetAssignmentMode.Replace:
                ResetAndRemoveFleet(empire, existing, clearOrders: ships.Length == 0);
                if (ships.Length == 0)
                    return Accept(result);
                Fleet replacement = empire.CreateFleet(command.SubjectId, null);
                AddShipsToFleet(replacement, ships);
                return Accept(result);

            case AuthoritativeFleetAssignmentMode.Add:
                Fleet fleet = existing?.Ships.Count > 0 ? existing : empire.CreateFleet(command.SubjectId, null);
                Ship[] newShips = ships.Where(s => s.Fleet != fleet).ToArray();
                if (newShips.Length == 0)
                    return Reject(result, "No selected ships can be added to the target fleet.");
                AddShipsToFleet(fleet, newShips);
                if (fleet.Name.IsEmpty() || fleet.Name.Contains("Fleet"))
                    fleet.Name = Fleet.GetDefaultFleetName(command.SubjectId);
                fleet.Update(FixedSimTime.Zero);
                return Accept(result);

            default:
                return Reject(result, $"Unsupported fleet assignment mode {command.TargetId}.");
        }
    }

    AuthoritativeCommandResult ApplyRenameFleet(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.SubjectId is < Empire.FirstFleetKey or > Empire.LastFleetKey)
            return Reject(result, $"Fleet key {command.SubjectId} is outside the player fleet range.");

        Fleet fleet = empire.GetFleetOrNull(command.SubjectId);
        if (fleet == null)
            return Reject(result, $"Fleet {command.SubjectId} not found.");

        string name = command.Text?.Trim() ?? "";
        if (name.Length == 0)
            return Reject(result, "Fleet name cannot be empty.");
        if (name.Length > 40)
            return Reject(result, $"Fleet name is too long ({name.Length}/40).");
        if (name.Any(char.IsControl))
            return Reject(result, "Fleet name cannot contain control characters.");

        fleet.Name = name;
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyRenameShip(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Ship ship = UState.Objects.FindShip(command.SubjectId);
        if (ship == null)
            return Reject(result, $"Ship {command.SubjectId} not found.");
        if (!ship.Active)
            return Reject(result, $"Ship {command.SubjectId} is inactive.");
        if (ship.Loyalty != empire)
            return Reject(result, $"Ship {command.SubjectId} is not owned by empire {empire.Id}.");

        if (!TryValidateRename(command.Text, AuthoritativePlayerCommand.MaxShipRenameLength,
                "Ship", out string name, out string reason))
        {
            return Reject(result, reason);
        }

        ship.VanityName = name;
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyRenamePlanet(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        Planet planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
            return Reject(result, $"Planet {command.SubjectId} not found.");
        if (planet.Owner != empire)
            return Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");

        if (!TryValidateRename(command.Text, AuthoritativePlayerCommand.MaxPlanetRenameLength,
                "Planet", out string name, out string reason))
        {
            return Reject(result, reason);
        }

        planet.Name = name;
        return Accept(result);
    }

    AuthoritativeCommandResult ApplySetFleetIcon(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.SubjectId is < Empire.FirstFleetKey or > Empire.LastFleetKey)
            return Reject(result, $"Fleet key {command.SubjectId} is outside the player fleet range.");
        if (command.TargetId is < 1 or > 30)
            return Reject(result, $"Fleet icon index {command.TargetId} is outside the supported range.");

        Fleet fleet = empire.GetFleetOrNull(command.SubjectId);
        if (fleet == null)
            return Reject(result, $"Fleet {command.SubjectId} not found.");

        fleet.FleetIconIndex = command.TargetId;
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyAutoArrangeFleet(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.SubjectId is < Empire.FirstFleetKey or > Empire.LastFleetKey)
            return Reject(result, $"Fleet key {command.SubjectId} is outside the player fleet range.");

        Fleet fleet = empire.GetFleetOrNull(command.SubjectId);
        if (fleet == null || fleet.Ships.Count == 0)
            return Reject(result, $"Fleet {command.SubjectId} not found or empty.");

        fleet.AutoArrange();
        fleet.Update(FixedSimTime.Zero);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyQueueFleetRequisition(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.SubjectId is < Empire.FirstFleetKey or > Empire.LastFleetKey)
            return Reject(result, $"Fleet key {command.SubjectId} is outside the player fleet range.");
        if (command.TargetId is not (0 or 1))
            return Reject(result, $"Unsupported fleet requisition rush flag {command.TargetId}.");
        if (!AuthoritativePlayerCommand.TryParseIdList(command.Text, out int[] requestedIndices))
            return Reject(result, "Invalid fleet requisition node payload.");
        if (requestedIndices.Length > 0 && requestedIndices.Distinct().ToArray().Length != requestedIndices.Length)
            return Reject(result, "Fleet requisition node indices must be unique.");

        Fleet fleet = empire.GetFleetOrNull(command.SubjectId);
        if (fleet == null)
            return Reject(result, $"Fleet {command.SubjectId} not found.");
        if (fleet.Owner != empire)
            return Reject(result, $"Fleet {command.SubjectId} is not owned by empire {empire.Id}.");

        int[] targetIndices = requestedIndices.Length > 0
            ? requestedIndices
            : Enumerable.Range(0, fleet.DataNodes.Count)
                .Where(i => IsEmptyRequisitionNode(fleet.DataNodes[i]))
                .ToArray();
        if (targetIndices.Length == 0)
            return Reject(result, $"Fleet {command.SubjectId} has no empty requisition nodes.");

        foreach (int index in targetIndices)
        {
            if ((uint)index >= fleet.DataNodes.Count)
                return Reject(result, $"Fleet requisition node {index} was not found.");

            FleetDataNode node = fleet.DataNodes[index];
            if (!IsEmptyRequisitionNode(node))
                return Reject(result, $"Fleet requisition node {index} is already filled or queued.");
            if (string.IsNullOrWhiteSpace(node.ShipName))
                return Reject(result, $"Fleet requisition node {index} has no ship design.");
            if (!ResourceManager.Ships.GetDesign(node.ShipName, out IShipDesign design))
                return Reject(result, $"Fleet requisition design '{node.ShipName}' was not found.");
            if (design.IsPlatformOrStation)
                return Reject(result, $"Fleet requisition design '{node.ShipName}' is not a mobile ship.");
            if (!empire.CanBuildShip(design))
                return Reject(result, $"Empire {empire.Id} cannot build fleet requisition design '{node.ShipName}'.");
        }

        bool rush = command.TargetId == 1;
        foreach (int index in targetIndices)
        {
            FleetDataNode node = fleet.DataNodes[index];
            var goal = new FleetRequisition(node.ShipName, empire, fleet, rush);
            node.Goal = goal;
            empire.AI.AddGoalAndEvaluate(goal);
        }
        return Accept(result);
    }

    static bool IsEmptyRequisitionNode(FleetDataNode node)
        => node?.Ship == null && node?.Goal == null;

    AuthoritativeCommandResult ApplyFleetLayout(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.SubjectId is < Empire.FirstFleetKey or > Empire.LastFleetKey)
            return Reject(result, $"Fleet key {command.SubjectId} is outside the player fleet range.");
        if (!AuthoritativePlayerCommand.TryParseFleetLayout(command.Text, out AuthoritativeFleetLayoutNode[] nodes))
            return Reject(result, "Invalid fleet layout payload.");

        var shipsById = new Dictionary<int, Ship>();
        foreach (AuthoritativeFleetLayoutNode node in nodes)
        {
            if (node.ShipId == 0)
            {
                if (!ResourceManager.Ships.GetDesign(node.ShipName, out IShipDesign design))
                    return Reject(result, $"Fleet layout design '{node.ShipName}' was not found.");
                if (!empire.CanBuildShip(design))
                    return Reject(result, $"Empire {empire.Id} cannot build fleet layout design '{node.ShipName}'.");
                continue;
            }

            Ship ship = UState.Objects.FindShip(node.ShipId);
            if (ship == null)
                return Reject(result, $"Fleet layout ship {node.ShipId} was not found.");
            if (!ship.Active)
                return Reject(result, $"Fleet layout ship {ship.Id} is inactive.");
            if (ship.Loyalty != empire)
                return Reject(result, $"Fleet layout ship {ship.Id} is not owned by empire {empire.Id}.");
            if (!ship.CanBeAddedToFleets())
                return Reject(result, $"Fleet layout ship {ship.Id} cannot be assigned to fleets.");
            shipsById[node.ShipId] = ship;
        }

        ResetAndRemoveFleet(empire, empire.GetFleetOrNull(command.SubjectId), clearOrders: true);
        if (nodes.Length == 0)
            return Accept(result);

        Fleet fleet = empire.CreateFleet(command.SubjectId, null);
        fleet.DataNodes.Clear();
        foreach (AuthoritativeFleetLayoutNode layout in nodes)
        {
            var node = new FleetDataNode
            {
                ShipName = layout.ShipName,
                RelativeFleetOffset = layout.Offset,
                VultureWeight = layout.VultureWeight,
                AttackShieldedWeight = layout.AttackShieldedWeight,
                AssistWeight = layout.AssistWeight,
                DefenderWeight = layout.DefenderWeight,
                DPSWeight = layout.DpsWeight,
                SizeWeight = layout.SizeWeight,
                ArmoredWeight = layout.ArmoredWeight,
                CombatState = layout.CombatState,
                OrdersRadius = layout.OrdersRadius,
            };
            fleet.DataNodes.Add(node);
            if (layout.ShipId == 0)
                continue;

            Ship ship = shipsById[layout.ShipId];
            ship.Fleet?.DataNodes.RemoveFirst(n => n.Ship == ship);
            ship.ClearFleet(returnToManagedPools: false, clearOrders: true);
            fleet.AddExistingShip(ship, node);
        }

        if (fleet.Name.IsEmpty() || fleet.Name.Contains("Fleet"))
            fleet.Name = Fleet.GetDefaultFleetName(command.SubjectId);
        fleet.SetCommandShip(null);
        fleet.Update(FixedSimTime.Zero);
        return Accept(result);
    }

    static void ResetAndRemoveFleet(Empire empire, Fleet fleet, bool clearOrders)
    {
        if (fleet == null)
            return;

        fleet.Reset(fleeIfInCombat: false, clearOrders: clearOrders);
        empire.RemoveFleet(fleet);
    }

    AuthoritativeCommandResult ApplyLoadFleetPatrol(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.SubjectId is < Empire.FirstFleetKey or > Empire.LastFleetKey)
            return Reject(result, $"Fleet key {command.SubjectId} is outside the player fleet range.");

        Fleet fleet = empire.GetFleetOrNull(command.SubjectId);
        if (fleet == null || fleet.Ships.Count == 0)
            return Reject(result, $"Fleet {command.SubjectId} not found or empty.");

        string patrolName = command.Text?.Trim() ?? "";
        if (patrolName.Length == 0)
            return Reject(result, "Patrol name cannot be empty.");

        FleetPatrol patrol = empire.FleetPatrols.FirstOrDefault(p =>
            string.Equals(p.Name, patrolName, StringComparison.Ordinal));
        if (patrol == null)
            return Reject(result, $"Patrol plan '{patrolName}' not found.");

        fleet.LoadPatrol(patrol);
        fleet.Update(FixedSimTime.Zero);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyRenameFleetPatrol(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (!AuthoritativePlayerCommand.TryParsePatrolRenamePayload(command.Text,
                out string oldName, out string newName))
        {
            return Reject(result, $"Invalid fleet patrol rename payload '{command.Text}'.");
        }
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return Reject(result, "Fleet patrol rename must change the name.");

        FleetPatrol patrol = empire.FleetPatrols.FirstOrDefault(p =>
            string.Equals(p.Name, oldName, StringComparison.Ordinal));
        if (patrol == null)
            return Reject(result, $"Patrol plan '{oldName}' not found.");
        if (empire.FleetPatrols.Any(p => !ReferenceEquals(p, patrol)
                                        && string.Equals(p.Name, newName, StringComparison.Ordinal)))
        {
            return Reject(result, $"Patrol plan '{newName}' already exists.");
        }

        patrol.ChangeName(newName);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyDeleteFleetPatrol(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        string patrolName = command.Text?.Trim() ?? "";
        if (!AuthoritativePlayerCommand.IsLegalPatrolName(patrolName))
            return Reject(result, $"Invalid fleet patrol name '{command.Text}'.");

        FleetPatrol patrol = empire.FleetPatrols.FirstOrDefault(p =>
            string.Equals(p.Name, patrolName, StringComparison.Ordinal));
        if (patrol == null)
            return Reject(result, $"Patrol plan '{patrolName}' not found.");

        foreach (Fleet fleet in empire.AllFleets)
        {
            if (fleet.HasPatrolPlan && string.Equals(fleet.Patrol.Name, patrolName, StringComparison.Ordinal))
            {
                fleet.ClearPatrol();
                fleet.Update(FixedSimTime.Zero);
            }
        }

        empire.FleetPatrols.Remove(patrol);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyClearFleetPatrol(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.SubjectId is < Empire.FirstFleetKey or > Empire.LastFleetKey)
            return Reject(result, $"Fleet key {command.SubjectId} is outside the player fleet range.");

        Fleet fleet = empire.GetFleetOrNull(command.SubjectId);
        if (fleet == null || fleet.Ships.Count == 0)
            return Reject(result, $"Fleet {command.SubjectId} not found or empty.");
        if (!fleet.HasPatrolPlan)
            return Reject(result, $"Fleet {command.SubjectId} has no active patrol.");

        fleet.ClearPatrol();
        fleet.Update(FixedSimTime.Zero);
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyCreateFleetPatrol(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        if (command.SubjectId is < Empire.FirstFleetKey or > Empire.LastFleetKey)
            return Reject(result, $"Fleet key {command.SubjectId} is outside the player fleet range.");

        Fleet fleet = empire.GetFleetOrNull(command.SubjectId);
        if (fleet == null || fleet.Ships.Count == 0)
            return Reject(result, $"Fleet {command.SubjectId} not found or empty.");
        if (fleet.HasPatrolPlan)
            return Reject(result, $"Fleet {command.SubjectId} already has an active patrol.");
        if (!AuthoritativePlayerCommand.TryParsePatrolWaypoints(command.Text, out WayPoint[] points))
            return Reject(result, "Invalid fleet patrol waypoint payload.");

        var waypoints = new WayPoints();
        waypoints.Set(points);
        fleet.CreatePatrol(waypoints);
        fleet.Update(FixedSimTime.Zero);
        return Accept(result);
    }

    static void AddShipsToFleet(Fleet fleet, IReadOnlyList<Ship> ships)
    {
        ClearShipFleetsWithDataNodes(ships);
        fleet.AddShips(ships);
        fleet.SetCommandShip(null);
        fleet.AutoArrange();
        fleet.Update(FixedSimTime.Zero);
    }

    static void ClearShipFleetsWithDataNodes(IReadOnlyList<Ship> ships)
    {
        foreach (Ship ship in ships.Where(s => s.CanBeAddedToFleets()))
        {
            ship.Fleet?.DataNodes.RemoveFirst(n => n.Ship == ship);
            ship.ClearFleet(returnToManagedPools: false, clearOrders: false);
        }
    }

    static IShipDesign RegisterPlayerDesign(ShipDesign design, out string rejectReason)
    {
        if (ResourceManager.Ships.GetDesign(design.Name, out IShipDesign existing))
        {
            if (existing.IsPlayerDesign && existing.AreModulesEqual(design))
            {
                rejectReason = "";
                return existing;
            }

            rejectReason = $"A different ship design named {design.Name} already exists.";
            return null;
        }

        if (!ResourceManager.AddShipTemplate(design, playerDesign: true, readOnly: false))
        {
            rejectReason = $"Failed to register ship design {design.Name}.";
            return null;
        }

        rejectReason = "";
        return ResourceManager.Ships.GetDesign(design.Name, out IShipDesign registered) ? registered : design;
    }

    static bool CanBeAddedToHumanBuildables(ShipDesign design, Empire empire)
    {
        bool wasPlayerDesign = design.IsPlayerDesign;
        bool wasReadonly = design.IsReadonlyDesign;
        bool wasEmpirePlayer = empire.isPlayer;
        try
        {
            design.IsPlayerDesign = false;
            design.IsReadonlyDesign = false;
            empire.isPlayer = true;
            return design.CanBeAddedToBuildableShips(empire);
        }
        finally
        {
            empire.isPlayer = wasEmpirePlayer;
            design.IsPlayerDesign = wasPlayerDesign;
            design.IsReadonlyDesign = wasReadonly;
        }
    }

    static QueueItemType QueueTypeFor(IShipDesign design)
    {
        if (design.IsColonyShip) return QueueItemType.ColonyShip;
        if (design.IsFreighter) return QueueItemType.Freighter;
        if (design.Role == RoleName.scout) return QueueItemType.Scout;
        return QueueItemType.CombatShip;
    }

    static bool IsValidLaborPercent(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;

    static bool IsUnitPercent(float value) => IsValidLaborPercent(value);

    static bool IsFinitePositive(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;

    static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    static bool AnyFleetShipCanMove(Fleet fleet)
    {
        foreach (Ship ship in fleet.Ships)
            if (ship?.Loyalty == fleet.Owner && ship.PlayerShipCanTakeFleetOrders())
                return true;
        return false;
    }

    bool TryGetOwnedQueueItem(AuthoritativePlayerCommand command, Empire empire, AuthoritativeCommandResult result,
        out Planet planet, out QueueItem item, out AuthoritativeCommandResult rejected)
    {
        planet = null;
        item = null;
        rejected = null;

        planet = UState.GetPlanet(command.SubjectId);
        if (planet == null)
        {
            rejected = Reject(result, $"Planet {command.SubjectId} not found.");
            return false;
        }
        if (planet.Owner != empire)
        {
            rejected = Reject(result, $"Planet {command.SubjectId} is not owned by empire {empire.Id}.");
            return false;
        }

        int queueIndex = command.TargetId;
        if ((uint)queueIndex >= planet.ConstructionQueue.Count)
        {
            rejected = Reject(result, $"Construction queue index {queueIndex} is outside planet {planet.Id} queue.");
            return false;
        }

        item = planet.ConstructionQueue[queueIndex];
        return true;
    }

    static bool TryGetResearchEntry(string requestedTechUid, Empire empire, AuthoritativeCommandResult result,
        out string techUid, out TechEntry entry, out AuthoritativeCommandResult rejected)
    {
        techUid = requestedTechUid ?? "";
        entry = null;
        rejected = null;

        if (techUid.IsEmpty())
        {
            rejected = Reject(result, "Research tech UID is empty.");
            return false;
        }
        if (!ResourceManager.TryGetTech(techUid, out _))
        {
            rejected = Reject(result, $"Tech {techUid} not found.");
            return false;
        }
        if (!empire.TryGetTechEntry(techUid, out entry))
        {
            rejected = Reject(result, $"Empire {empire.Id} has no tech entry for {techUid}.");
            return false;
        }
        return true;
    }

    static bool TryParsePlanetOrder(string text, out AuthoritativeShipPlanetOrderType orderType,
        out bool clearOrders, out MoveOrder moveOrder)
    {
        orderType = default;
        clearOrders = true;
        moveOrder = MoveOrder.Regular;
        string[] parts = (text ?? "").Split('|');
        if (parts.Length != 3)
            return false;
        if (!int.TryParse(parts[0], out int orderValue)
            || orderValue < byte.MinValue || orderValue > byte.MaxValue
            || !Enum.IsDefined(typeof(AuthoritativeShipPlanetOrderType),
                (AuthoritativeShipPlanetOrderType)orderValue))
        {
            return false;
        }
        if (!int.TryParse(parts[1], out int clearValue) || clearValue is not (0 or 1))
            return false;
        if (!int.TryParse(parts[2], out int moveValue))
            return false;

        orderType = (AuthoritativeShipPlanetOrderType)orderValue;
        clearOrders = clearValue == 1;
        moveOrder = (MoveOrder)moveValue;
        return true;
    }

    static bool IsAutomationDesignValid(Empire empire, string designName, Func<IShipDesign, bool> predicate,
        string label, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(designName))
            return true;
        if (!ResourceManager.Ships.GetDesign(designName, out IShipDesign design))
        {
            reason = $"Automation {label} design '{designName}' was not found.";
            return false;
        }
        if (!design.IsShipGoodToBuild(empire) || !predicate(design))
        {
            reason = $"Automation {label} design '{designName}' is not buildable by empire {empire.Id}.";
            return false;
        }
        return true;
    }

    static bool TryValidateRename(string rawName, int maxLength, string label, out string name, out string reason)
    {
        name = rawName?.Trim() ?? "";
        reason = "";
        if (name.Length == 0)
        {
            reason = $"{label} name cannot be empty.";
            return false;
        }
        if (name.Length > maxLength)
        {
            reason = $"{label} name is too long ({name.Length}/{maxLength}).";
            return false;
        }
        if (name.Any(char.IsControl))
        {
            reason = $"{label} name cannot contain control characters.";
            return false;
        }
        return true;
    }

    static AuthoritativeCommandResult Accept(AuthoritativeCommandResult result)
    {
        result.Accepted = true;
        result.Reason = "";
        return result;
    }

    static AuthoritativeCommandResult Reject(AuthoritativeCommandResult result, string reason)
    {
        result.Accepted = false;
        result.Reason = reason;
        return result;
    }
}

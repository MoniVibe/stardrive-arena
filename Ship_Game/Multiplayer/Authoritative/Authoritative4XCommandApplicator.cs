using System;
using System.Linq;
using Ship_Game.AI;
using Ship_Game.Commands.Goals;
using Ship_Game.Determinism.Lockstep;
using Ship_Game.Ships;
using Ship_Game.Universe;
using SDUtils;
using Vector2 = SDGraphics.Vector2;

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
                AuthoritativePlayerCommandKind.SetColonyLabor => ApplyColonyLabor(command, empire, result),
                AuthoritativePlayerCommandKind.SetResearchTopic => ApplyResearchTopic(command, empire, result),
                AuthoritativePlayerCommandKind.QueueResearch => ApplyQueueResearch(command, empire, result),
                AuthoritativePlayerCommandKind.RemoveResearchQueueItem => ApplyRemoveResearchQueueItem(command, empire, result),
                AuthoritativePlayerCommandKind.MoveResearchQueueItem => ApplyMoveResearchQueueItem(command, empire, result),
                AuthoritativePlayerCommandKind.SetEmpireBudget => ApplyEmpireBudget(command, empire, result),
                AuthoritativePlayerCommandKind.DiplomacyProposal => ApplyDiplomacy(command, empire, result),
                AuthoritativePlayerCommandKind.DiplomacyResponse => ApplyDiplomacy(command, empire, result),
                AuthoritativePlayerCommandKind.DesignShip => ApplyDesignShip(command, empire, result),
                AuthoritativePlayerCommandKind.QueueBuild => ApplyQueueBuild(command, empire, result),
                AuthoritativePlayerCommandKind.QueueBuilding => ApplyQueueBuilding(command, empire, result),
                AuthoritativePlayerCommandKind.QueueTroop => ApplyQueueTroop(command, empire, result),
                AuthoritativePlayerCommandKind.CancelConstructionQueueItem => ApplyCancelConstructionQueueItem(command, empire, result),
                AuthoritativePlayerCommandKind.ReorderConstructionQueueItem => ApplyReorderConstructionQueueItem(command, empire, result),
                AuthoritativePlayerCommandKind.AttackShip => ApplyAttackShip(command, empire, result),
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

        ship.AI.OrderMoveTo(command.Position, dir, AIState.AwaitingOrders, order);
        return Accept(result);
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
        if (target.Loyalty == null || !empire.IsEmpireAttackable(target.Loyalty, target))
            return Reject(result, $"Empire {empire.Id} cannot attack ship {target.Id}.");

        bool queue = string.Equals(command.Text, "queue", StringComparison.Ordinal);
        if (queue)
            ship.AI.OrderQueueSpecificTarget(target);
        else
            ship.AI.OrderAttackSpecificTarget(target);
        return Accept(result);
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
                if (!ship.Carrier.AnyAssaultOpsAvailable)
                    return Reject(result, $"Ship {ship.Id} has no assault troops available.");
                if (!planet.Habitable)
                    return Reject(result, $"Planet {planet.Id} is not habitable.");
                bool legalLanding = planet.Owner == null
                                    || planet.Owner == empire && planet.ForeignTroopHere(empire)
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

        return planet.Construction.Enqueue(buildable, where: null, playerAdded: true)
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

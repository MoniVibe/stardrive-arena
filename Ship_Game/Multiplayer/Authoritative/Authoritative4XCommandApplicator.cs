using System;
using System.Linq;
using Ship_Game.AI;
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
                AuthoritativePlayerCommandKind.SetResearchTopic => ApplyResearchTopic(command, empire, result),
                AuthoritativePlayerCommandKind.DiplomacyProposal => ApplyDiplomacy(command, empire, result),
                AuthoritativePlayerCommandKind.DiplomacyResponse => ApplyDiplomacy(command, empire, result),
                AuthoritativePlayerCommandKind.DesignShip => ApplyDesignShip(command, empire, result),
                AuthoritativePlayerCommandKind.QueueBuild => ApplyQueueBuild(command, empire, result),
                AuthoritativePlayerCommandKind.QueueBuilding => ApplyQueueBuilding(command, empire, result),
                AuthoritativePlayerCommandKind.QueueTroop => ApplyQueueTroop(command, empire, result),
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

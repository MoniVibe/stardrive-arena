using System;
using System.Linq;
using System.Threading;
using Ship_Game;
using Ship_Game.AI;
using Ship_Game.Ships;
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

    public static bool TrySubmitQueueBuilding(Planet planet, string buildingName)
    {
        if (!TryGetFor(planet?.Owner, out Authoritative4XClientContext context))
            return false;
        if (string.IsNullOrEmpty(buildingName))
            return false;

        context.Submit(AuthoritativePlayerCommand.QueueBuilding(context.Next(), context.EmpireId, planet.Id, buildingName));
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

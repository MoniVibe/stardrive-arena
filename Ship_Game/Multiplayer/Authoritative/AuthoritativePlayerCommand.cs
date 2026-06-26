using System;
using System.Collections.Generic;
using System.Globalization;
using SDLockstep;
using Ship_Game.AI;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.Multiplayer.Authoritative;

public enum AuthoritativePlayerCommandKind : byte
{
    NoOp = 0,
    MoveShip = 1,
    SetColonyType = 2,
    SetResearchTopic = 3,
    DiplomacyProposal = 4,
    DiplomacyResponse = 5,
    DesignShip = 6,
    QueueBuild = 7,
    QueueBuilding = 8,
    QueueTroop = 9,
    AttackShip = 10,
    ShipPlanetOrder = 11,
    SetColonyLabor = 12,
    CancelConstructionQueueItem = 13,
    ReorderConstructionQueueItem = 14,
    SetEmpireBudget = 15,
    QueueResearch = 16,
    RemoveResearchQueueItem = 17,
    MoveResearchQueueItem = 18,
    RushConstructionQueueItem = 19,
    ToggleConstructionRush = 20,
    SetPlanetGoodsState = 21,
    SetPlanetPrioritizedPort = 22,
    SetPlanetManualBudget = 23,
    SetFleetAssignment = 24,
    MoveFleet = 25,
    ShipSpecialOrder = 26,
    SetShipCombatStance = 27,
    RenameFleet = 28,
    AutoArrangeFleet = 29,
    ShipLifecycleOrder = 30,
    LoadFleetPatrol = 31,
}

public enum AuthoritativeShipPlanetOrderType : byte
{
    Orbit = 1,
    Colonize = 2,
    Bombard = 3,
    LandTroops = 4,
}

public enum AuthoritativeDiplomacyProposalType : byte
{
    DeclareWar = 1,
    Alliance = 2,
    Peace = 3,
    TradeDeal = 4,
    NonAggression = 5,
}

public enum AuthoritativeDiplomacyResponseKind : byte
{
    Accept = 1,
    Reject = 2,
    Counter = 3,
}

public enum AuthoritativeResearchQueueMove : byte
{
    Up = 1,
    Down = 2,
    ToTopOrPrereq = 3,
    ToTopWithPrereqs = 4,
}

public enum AuthoritativePlanetGoodsKind : byte
{
    Food = 1,
    Production = 2,
}

public enum AuthoritativePlanetBudgetKind : byte
{
    Civilian = 1,
    GroundDefense = 2,
    SpaceDefense = 3,
}

public enum AuthoritativeFleetAssignmentMode : byte
{
    Replace = 1,
    Add = 2,
    Clear = 3,
}

public enum AuthoritativeShipSpecialOrderType : byte
{
    Explore = 1,
}

public enum AuthoritativeShipLifecycleOrderType : byte
{
    Scrap = 1,
    Scuttle = 2,
}

/// <summary>
/// Phase-A authoritative 4X command request. It intentionally carries only primitive ids/args so it
/// can cross the existing SDLockstep transport without engine object references.
/// </summary>
public sealed class AuthoritativePlayerCommand
{
    public int Sequence;
    public int EmpireId;
    public AuthoritativePlayerCommandKind Kind;
    public int SubjectId;
    public int TargetId;
    public Vector2 Position;
    public string Text = "";

    public static AuthoritativePlayerCommand NoOp(int sequence, int empireId)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.NoOp,
        };

    public static AuthoritativePlayerCommand MoveShip(int sequence, int empireId, int shipId, Vector2 destination,
        MoveOrder order = MoveOrder.Regular)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.MoveShip,
            SubjectId = shipId,
            TargetId = (int)order,
            Position = destination,
        };

    public static AuthoritativePlayerCommand SetColonyType(int sequence, int empireId, int planetId, Planet.ColonyType type)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.SetColonyType,
            SubjectId = planetId,
            TargetId = (int)type,
        };

    public static AuthoritativePlayerCommand SetColonyLabor(int sequence, int empireId, int planetId,
        float food, float production, float research, bool foodLocked, bool productionLocked, bool researchLocked)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.SetColonyLabor,
            SubjectId = planetId,
            Text = EncodeColonyLaborPayload(food, production, research, foodLocked, productionLocked, researchLocked),
        };

    public static AuthoritativePlayerCommand SetResearchTopic(int sequence, int empireId, string techUid)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.SetResearchTopic,
            Text = techUid ?? "",
        };

    public static AuthoritativePlayerCommand QueueResearch(int sequence, int empireId, string techUid)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.QueueResearch,
            Text = techUid ?? "",
        };

    public static AuthoritativePlayerCommand RemoveResearchQueueItem(int sequence, int empireId, string techUid)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.RemoveResearchQueueItem,
            Text = techUid ?? "",
        };

    public static AuthoritativePlayerCommand MoveResearchQueueItem(int sequence, int empireId, string techUid,
        AuthoritativeResearchQueueMove move)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.MoveResearchQueueItem,
            TargetId = (int)move,
            Text = techUid ?? "",
        };

    public static AuthoritativePlayerCommand SetEmpireBudget(int sequence, int empireId, float taxRate,
        float treasuryGoal, bool autoTaxes)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.SetEmpireBudget,
            Text = EncodeEmpireBudgetPayload(taxRate, treasuryGoal, autoTaxes),
        };

    public static AuthoritativePlayerCommand DiplomacyProposal(int sequence, int proposerEmpireId, int targetEmpireId,
        AuthoritativeDiplomacyProposalType proposalType, string terms = "")
        => new()
        {
            Sequence = sequence,
            EmpireId = proposerEmpireId,
            Kind = AuthoritativePlayerCommandKind.DiplomacyProposal,
            SubjectId = targetEmpireId,
            TargetId = (int)proposalType,
            Text = terms ?? "",
        };

    public static AuthoritativePlayerCommand DiplomacyResponse(int sequence, int responderEmpireId, int proposalId,
        AuthoritativeDiplomacyResponseKind response, string terms = "")
        => new()
        {
            Sequence = sequence,
            EmpireId = responderEmpireId,
            Kind = AuthoritativePlayerCommandKind.DiplomacyResponse,
            SubjectId = proposalId,
            TargetId = (int)response,
            Text = terms ?? "",
        };

    public static AuthoritativePlayerCommand DesignShip(int sequence, int empireId, string base64Design)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.DesignShip,
            Text = base64Design ?? "",
        };

    public static AuthoritativePlayerCommand QueueBuild(int sequence, int empireId, int planetId, string designName)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.QueueBuild,
            SubjectId = planetId,
            Text = designName ?? "",
        };

    public static AuthoritativePlayerCommand QueueBuilding(int sequence, int empireId, int planetId, string buildingName)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.QueueBuilding,
            SubjectId = planetId,
            Text = buildingName ?? "",
        };

    public static AuthoritativePlayerCommand QueueTroop(int sequence, int empireId, int planetId, string troopName)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.QueueTroop,
            SubjectId = planetId,
            Text = troopName ?? "",
        };

    public static AuthoritativePlayerCommand CancelConstructionQueueItem(int sequence, int empireId, int planetId,
        int queueIndex)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.CancelConstructionQueueItem,
            SubjectId = planetId,
            TargetId = queueIndex,
        };

    public static AuthoritativePlayerCommand ReorderConstructionQueueItem(int sequence, int empireId, int planetId,
        int currentIndex, int moveToIndex)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.ReorderConstructionQueueItem,
            SubjectId = planetId,
            TargetId = currentIndex,
            Position = new Vector2(moveToIndex, 0f),
        };

    public static AuthoritativePlayerCommand RushConstructionQueueItem(int sequence, int empireId, int planetId,
        int queueIndex, float maxAmount)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.RushConstructionQueueItem,
            SubjectId = planetId,
            TargetId = queueIndex,
            Position = new Vector2(maxAmount, 0f),
        };

    public static AuthoritativePlayerCommand ToggleConstructionRush(int sequence, int empireId, int planetId,
        int queueIndex)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.ToggleConstructionRush,
            SubjectId = planetId,
            TargetId = queueIndex,
        };

    public static AuthoritativePlayerCommand SetPlanetGoodsState(int sequence, int empireId, int planetId,
        AuthoritativePlanetGoodsKind goods, Planet.GoodState state)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.SetPlanetGoodsState,
            SubjectId = planetId,
            TargetId = (int)goods,
            Position = new Vector2((int)state, 0f),
        };

    public static AuthoritativePlayerCommand SetPlanetPrioritizedPort(int sequence, int empireId, int planetId,
        bool prioritized)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.SetPlanetPrioritizedPort,
            SubjectId = planetId,
            TargetId = prioritized ? 1 : 0,
        };

    public static AuthoritativePlayerCommand SetPlanetManualBudget(int sequence, int empireId, int planetId,
        AuthoritativePlanetBudgetKind budget, float value)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.SetPlanetManualBudget,
            SubjectId = planetId,
            TargetId = (int)budget,
            Position = new Vector2(value, 0f),
        };

    public static AuthoritativePlayerCommand SetFleetAssignment(int sequence, int empireId, int fleetKey,
        AuthoritativeFleetAssignmentMode mode, IEnumerable<int> shipIds)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.SetFleetAssignment,
            SubjectId = fleetKey,
            TargetId = (int)mode,
            Text = EncodeIdList(shipIds),
        };

    public static AuthoritativePlayerCommand MoveFleet(int sequence, int empireId, int fleetKey,
        Vector2 destination, Vector2 direction, MoveOrder order = MoveOrder.Regular)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.MoveFleet,
            SubjectId = fleetKey,
            TargetId = (int)order,
            Position = destination,
            Text = EncodeVectorPayload(direction),
        };

    public static AuthoritativePlayerCommand RenameFleet(int sequence, int empireId, int fleetKey, string name)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.RenameFleet,
            SubjectId = fleetKey,
            Text = name ?? "",
        };

    public static AuthoritativePlayerCommand AutoArrangeFleet(int sequence, int empireId, int fleetKey)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.AutoArrangeFleet,
            SubjectId = fleetKey,
        };

    public static AuthoritativePlayerCommand LoadFleetPatrol(int sequence, int empireId, int fleetKey, string patrolName)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.LoadFleetPatrol,
            SubjectId = fleetKey,
            Text = patrolName ?? "",
        };

    public static AuthoritativePlayerCommand ShipSpecialOrder(int sequence, int empireId, int shipId,
        AuthoritativeShipSpecialOrderType orderType)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.ShipSpecialOrder,
            SubjectId = shipId,
            TargetId = (int)orderType,
        };

    public static AuthoritativePlayerCommand ShipLifecycleOrder(int sequence, int empireId, int shipId,
        AuthoritativeShipLifecycleOrderType orderType)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.ShipLifecycleOrder,
            SubjectId = shipId,
            TargetId = (int)orderType,
        };

    public static AuthoritativePlayerCommand SetShipCombatStance(int sequence, int empireId, int shipId,
        CombatState stance)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.SetShipCombatStance,
            SubjectId = shipId,
            TargetId = (int)stance,
        };

    public static AuthoritativePlayerCommand AttackShip(int sequence, int empireId, int shipId, int targetShipId,
        bool queue = false)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.AttackShip,
            SubjectId = shipId,
            TargetId = targetShipId,
            Text = queue ? "queue" : "",
        };

    public static AuthoritativePlayerCommand ShipPlanetOrder(int sequence, int empireId, int shipId, int planetId,
        AuthoritativeShipPlanetOrderType orderType, bool clearOrders = true, MoveOrder moveOrder = MoveOrder.Regular)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.ShipPlanetOrder,
            SubjectId = shipId,
            TargetId = planetId,
            Text = $"{(int)orderType}|{(clearOrders ? 1 : 0)}|{(int)moveOrder}",
        };

    public AuthoritativeCommandRequestMessage ToMessage(int fromPeer)
        => new()
        {
            FromPeer = fromPeer,
            Sequence = Sequence,
            EmpireId = EmpireId,
            Kind = (byte)Kind,
            SubjectId = SubjectId,
            TargetId = TargetId,
            X = Position.X,
            Y = Position.Y,
            Text = Text ?? "",
        };

    public static AuthoritativePlayerCommand FromMessage(AuthoritativeCommandRequestMessage message)
        => new()
        {
            Sequence = message.Sequence,
            EmpireId = message.EmpireId,
            Kind = (AuthoritativePlayerCommandKind)message.Kind,
            SubjectId = message.SubjectId,
            TargetId = message.TargetId,
            Position = new Vector2(message.X, message.Y),
            Text = message.Text ?? "",
        };

    public static string EncodeColonyLaborPayload(float food, float production, float research,
        bool foodLocked, bool productionLocked, bool researchLocked)
    {
        int locks = (foodLocked ? 1 : 0) | (productionLocked ? 2 : 0) | (researchLocked ? 4 : 0);
        return string.Create(CultureInfo.InvariantCulture,
            $"{FloatBits(food):X8}|{FloatBits(production):X8}|{FloatBits(research):X8}|{locks}");
    }

    public static bool TryParseColonyLaborPayload(string payload, out float food, out float production,
        out float research, out bool foodLocked, out bool productionLocked, out bool researchLocked)
    {
        food = production = research = 0f;
        foodLocked = productionLocked = researchLocked = false;

        string[] parts = (payload ?? "").Split('|');
        if (parts.Length != 4)
            return false;
        if (!uint.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint foodBits)
            || !uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint prodBits)
            || !uint.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint resBits)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int locks)
            || locks < 0 || locks > 7)
        {
            return false;
        }

        food = FloatFromBits(foodBits);
        production = FloatFromBits(prodBits);
        research = FloatFromBits(resBits);
        foodLocked = (locks & 1) != 0;
        productionLocked = (locks & 2) != 0;
        researchLocked = (locks & 4) != 0;
        return true;
    }

    public static string EncodeEmpireBudgetPayload(float taxRate, float treasuryGoal, bool autoTaxes)
        => string.Create(CultureInfo.InvariantCulture,
            $"{FloatBits(taxRate):X8}|{FloatBits(treasuryGoal):X8}|{(autoTaxes ? 1 : 0)}");

    public static bool TryParseEmpireBudgetPayload(string payload, out float taxRate,
        out float treasuryGoal, out bool autoTaxes)
    {
        taxRate = treasuryGoal = 0f;
        autoTaxes = false;

        string[] parts = (payload ?? "").Split('|');
        if (parts.Length != 3)
            return false;
        if (!uint.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint taxBits)
            || !uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint treasuryBits)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int autoValue)
            || autoValue is not (0 or 1))
        {
            return false;
        }

        taxRate = FloatFromBits(taxBits);
        treasuryGoal = FloatFromBits(treasuryBits);
        autoTaxes = autoValue == 1;
        return true;
    }

    public static string EncodeIdList(IEnumerable<int> ids)
        => ids == null ? "" : string.Join(",", ids);

    public static bool TryParseIdList(string payload, out int[] ids)
    {
        ids = Array.Empty<int>();
        if (string.IsNullOrWhiteSpace(payload))
            return true;

        string[] parts = payload.Split(',');
        var parsed = new int[parts.Length];
        for (int i = 0; i < parts.Length; ++i)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                return false;
            parsed[i] = id;
        }
        ids = parsed;
        return true;
    }

    public static string EncodeVectorPayload(Vector2 vector)
        => string.Create(CultureInfo.InvariantCulture, $"{FloatBits(vector.X):X8}|{FloatBits(vector.Y):X8}");

    public static bool TryParseVectorPayload(string payload, out Vector2 vector)
    {
        vector = default;
        string[] parts = (payload ?? "").Split('|');
        if (parts.Length != 2)
            return false;
        if (!uint.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint xBits)
            || !uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint yBits))
        {
            return false;
        }

        vector = new Vector2(FloatFromBits(xBits), FloatFromBits(yBits));
        return true;
    }

    static uint FloatBits(float value) => BitConverter.SingleToUInt32Bits(value);
    static float FloatFromBits(uint value) => BitConverter.UInt32BitsToSingle(value);
}

public sealed class AuthoritativeCommandResult
{
    public int Sequence;
    public int OriginPeer;
    public bool Accepted;
    public uint Tick;
    public string Reason = "";

    public AuthoritativeCommandResultMessage ToMessage(int fromPeer)
        => new()
        {
            FromPeer = fromPeer,
            Sequence = Sequence,
            OriginPeer = OriginPeer,
            Accepted = Accepted,
            Tick = Tick,
            Reason = Reason ?? "",
        };
}

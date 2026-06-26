using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
    SetColonizationGoal = 32,
    SetFleetLayout = 33,
    QueueDeepSpaceBuild = 34,
    CancelDeepSpaceBuild = 35,
    SetPlanetGovernorOptions = 36,
    SetPlanetManualTradeSlots = 37,
    SetPlanetDefenseTargets = 38,
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

[Flags]
public enum AuthoritativePlanetGovernorOptions
{
    None = 0,
    GovOrbitals = 1 << 0,
    AutoBuildTroops = 1 << 1,
    DontScrapBuildings = 1 << 2,
    Quarantine = 1 << 3,
    ManualOrbitals = 1 << 4,
    GovGroundDefense = 1 << 5,
    SpecializedTradeHub = 1 << 6,
    All = GovOrbitals | AutoBuildTroops | DontScrapBuildings | Quarantine
        | ManualOrbitals | GovGroundDefense | SpecializedTradeHub,
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
    ClearOrders = 2,
}

public enum AuthoritativeShipLifecycleOrderType : byte
{
    Scrap = 1,
    Scuttle = 2,
    CancelScuttle = 3,
}

public readonly struct AuthoritativeFleetLayoutNode
{
    public readonly int ShipId;
    public readonly string ShipName;
    public readonly Vector2 Offset;
    public readonly float VultureWeight;
    public readonly float AttackShieldedWeight;
    public readonly float AssistWeight;
    public readonly float DefenderWeight;
    public readonly float DpsWeight;
    public readonly float SizeWeight;
    public readonly float ArmoredWeight;
    public readonly CombatState CombatState;
    public readonly float OrdersRadius;

    public AuthoritativeFleetLayoutNode(int shipId, string shipName, Vector2 offset,
        float vultureWeight, float attackShieldedWeight, float assistWeight, float defenderWeight,
        float dpsWeight, float sizeWeight, float armoredWeight, CombatState combatState,
        float ordersRadius)
    {
        ShipId = shipId;
        ShipName = shipName ?? "";
        Offset = offset;
        VultureWeight = vultureWeight;
        AttackShieldedWeight = attackShieldedWeight;
        AssistWeight = assistWeight;
        DefenderWeight = defenderWeight;
        DpsWeight = dpsWeight;
        SizeWeight = sizeWeight;
        ArmoredWeight = armoredWeight;
        CombatState = combatState;
        OrdersRadius = ordersRadius;
    }
}

/// <summary>
/// Phase-A authoritative 4X command request. It intentionally carries only primitive ids/args so it
/// can cross the existing SDLockstep transport without engine object references.
/// </summary>
public sealed class AuthoritativePlayerCommand
{
    public const int MaxManualImportTradeSlots = 20;
    public const int MaxManualExportTradeSlots = 25;
    public const int MaxPlanetGarrisonSize = 25;
    public const int MaxWantedPlatforms = 15;
    public const int MaxWantedShipyards = 3;
    public const int MaxWantedStations = 10;

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

    public static AuthoritativePlayerCommand SetColonizationGoal(int sequence, int empireId, int planetId,
        bool enabled)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.SetColonizationGoal,
            SubjectId = planetId,
            TargetId = enabled ? 1 : 0,
        };

    public static AuthoritativePlayerCommand QueueDeepSpaceBuild(int sequence, int empireId, string designName,
        Vector2 buildPosition, int targetPlanetId = 0, int targetSystemId = 0, Vector2 tetherOffset = default)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.QueueDeepSpaceBuild,
            SubjectId = targetPlanetId,
            TargetId = targetSystemId,
            Position = buildPosition,
            Text = EncodeDeepSpaceBuildPayload(designName, tetherOffset),
        };

    public static AuthoritativePlayerCommand CancelDeepSpaceBuild(int sequence, int empireId, string designName,
        GoalType goalType, Vector2 buildPosition, int targetPlanetId = 0, int targetSystemId = 0)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.CancelDeepSpaceBuild,
            SubjectId = targetPlanetId,
            TargetId = targetSystemId,
            Position = buildPosition,
            Text = EncodeDeepSpaceCancelPayload(designName, goalType),
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

    public static AuthoritativePlayerCommand QueueBuilding(int sequence, int empireId, int planetId,
        string buildingName, int tileX, int tileY)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.QueueBuilding,
            SubjectId = planetId,
            TargetId = 1,
            Position = new Vector2(tileX, tileY),
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

    public static AuthoritativePlayerCommand SetPlanetGovernorOptions(int sequence, int empireId, int planetId,
        AuthoritativePlanetGovernorOptions options)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.SetPlanetGovernorOptions,
            SubjectId = planetId,
            TargetId = (int)options,
        };

    public static AuthoritativePlayerCommand SetPlanetManualTradeSlots(int sequence, int empireId, int planetId,
        int foodImport, int prodImport, int coloImport, int foodExport, int prodExport, int coloExport)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.SetPlanetManualTradeSlots,
            SubjectId = planetId,
            Text = EncodeManualTradeSlotsPayload(foodImport, prodImport, coloImport,
                foodExport, prodExport, coloExport),
        };

    public static AuthoritativePlayerCommand SetPlanetDefenseTargets(int sequence, int empireId, int planetId,
        int garrisonSize, int wantedPlatforms, int wantedShipyards, int wantedStations)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.SetPlanetDefenseTargets,
            SubjectId = planetId,
            Text = EncodePlanetDefenseTargetsPayload(garrisonSize, wantedPlatforms, wantedShipyards, wantedStations),
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

    public static AuthoritativePlayerCommand SetFleetLayout(int sequence, int empireId, int fleetKey,
        IEnumerable<FleetDataNode> nodes)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.SetFleetLayout,
            SubjectId = fleetKey,
            Text = EncodeFleetLayout(nodes),
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

    public static string EncodeManualTradeSlotsPayload(int foodImport, int prodImport, int coloImport,
        int foodExport, int prodExport, int coloExport)
        => string.Create(CultureInfo.InvariantCulture,
            $"{foodImport}|{prodImport}|{coloImport}|{foodExport}|{prodExport}|{coloExport}");

    public static bool TryParseManualTradeSlotsPayload(string payload, out int foodImport, out int prodImport,
        out int coloImport, out int foodExport, out int prodExport, out int coloExport)
    {
        foodImport = prodImport = coloImport = foodExport = prodExport = coloExport = 0;

        string[] parts = (payload ?? "").Split('|');
        if (parts.Length != 6)
            return false;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out foodImport)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out prodImport)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out coloImport)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out foodExport)
            || !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out prodExport)
            || !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out coloExport))
        {
            return false;
        }

        return AreManualTradeSlotsValid(foodImport, prodImport, coloImport, foodExport, prodExport, coloExport);
    }

    public static bool AreManualTradeSlotsValid(int foodImport, int prodImport, int coloImport,
        int foodExport, int prodExport, int coloExport)
        => IsInRange(foodImport, 0, MaxManualImportTradeSlots)
           && IsInRange(prodImport, 0, MaxManualImportTradeSlots)
           && IsInRange(coloImport, 0, MaxManualImportTradeSlots)
           && IsInRange(foodExport, 0, MaxManualExportTradeSlots)
           && IsInRange(prodExport, 0, MaxManualExportTradeSlots)
           && IsInRange(coloExport, 0, MaxManualExportTradeSlots);

    static bool IsInRange(int value, int min, int max)
        => value >= min && value <= max;

    public static string EncodePlanetDefenseTargetsPayload(int garrisonSize, int wantedPlatforms,
        int wantedShipyards, int wantedStations)
        => string.Create(CultureInfo.InvariantCulture,
            $"{garrisonSize}|{wantedPlatforms}|{wantedShipyards}|{wantedStations}");

    public static bool TryParsePlanetDefenseTargetsPayload(string payload, out int garrisonSize,
        out int wantedPlatforms, out int wantedShipyards, out int wantedStations)
    {
        garrisonSize = wantedPlatforms = wantedShipyards = wantedStations = 0;

        string[] parts = (payload ?? "").Split('|');
        if (parts.Length != 4)
            return false;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out garrisonSize)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out wantedPlatforms)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out wantedShipyards)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out wantedStations))
        {
            return false;
        }

        return ArePlanetDefenseTargetsValid(garrisonSize, wantedPlatforms, wantedShipyards, wantedStations);
    }

    public static bool ArePlanetDefenseTargetsValid(int garrisonSize, int wantedPlatforms,
        int wantedShipyards, int wantedStations)
        => IsInRange(garrisonSize, 0, MaxPlanetGarrisonSize)
           && IsInRange(wantedPlatforms, 0, MaxWantedPlatforms)
           && IsInRange(wantedShipyards, 0, MaxWantedShipyards)
           && IsInRange(wantedStations, 0, MaxWantedStations);

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

    public static string EncodeDeepSpaceBuildPayload(string designName, Vector2 tetherOffset)
        => string.Join("|",
            EncodeText(designName ?? ""),
            Hex(FloatBits(tetherOffset.X)),
            Hex(FloatBits(tetherOffset.Y)));

    public static bool TryParseDeepSpaceBuildPayload(string payload, out string designName, out Vector2 tetherOffset)
    {
        designName = "";
        tetherOffset = default;
        string[] parts = (payload ?? "").Split('|');
        if (parts.Length != 3
            || !TryDecodeText(parts[0], out designName)
            || string.IsNullOrWhiteSpace(designName)
            || !uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint xBits)
            || !uint.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint yBits))
        {
            return false;
        }

        tetherOffset = new Vector2(FloatFromBits(xBits), FloatFromBits(yBits));
        return float.IsFinite(tetherOffset.X) && float.IsFinite(tetherOffset.Y);
    }

    public static string EncodeDeepSpaceCancelPayload(string designName, GoalType goalType)
        => string.Join("|",
            EncodeText(designName ?? ""),
            ((int)goalType).ToString(CultureInfo.InvariantCulture));

    public static bool TryParseDeepSpaceCancelPayload(string payload, out string designName, out GoalType goalType)
    {
        designName = "";
        goalType = default;
        string[] parts = (payload ?? "").Split('|');
        if (parts.Length != 2
            || !TryDecodeText(parts[0], out designName)
            || string.IsNullOrWhiteSpace(designName)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type)
            || !Enum.IsDefined(typeof(GoalType), (GoalType)type))
        {
            return false;
        }

        goalType = (GoalType)type;
        return true;
    }

    public static string EncodeFleetLayout(IEnumerable<FleetDataNode> nodes)
    {
        if (nodes == null)
            return "";

        return string.Join(";", nodes.Select(node =>
        {
            string name = node?.ShipName ?? node?.Ship?.Name ?? "";
            Vector2 offset = node?.RelativeFleetOffset ?? Vector2.Zero;
            return string.Join("|",
                (node?.Ship?.Id ?? 0).ToString(CultureInfo.InvariantCulture),
                EncodeText(name),
                Hex(FloatBits(offset.X)),
                Hex(FloatBits(offset.Y)),
                Hex(FloatBits(node?.VultureWeight ?? 0.5f)),
                Hex(FloatBits(node?.AttackShieldedWeight ?? 0.5f)),
                Hex(FloatBits(node?.AssistWeight ?? 0.5f)),
                Hex(FloatBits(node?.DefenderWeight ?? 0.5f)),
                Hex(FloatBits(node?.DPSWeight ?? 0.5f)),
                Hex(FloatBits(node?.SizeWeight ?? 0.5f)),
                Hex(FloatBits(node?.ArmoredWeight ?? 0.5f)),
                ((int)(node?.CombatState ?? CombatState.Artillery)).ToString(CultureInfo.InvariantCulture),
                Hex(FloatBits(node?.OrdersRadius ?? 500000f)));
        }));
    }

    public static bool TryParseFleetLayout(string payload, out AuthoritativeFleetLayoutNode[] nodes)
    {
        nodes = Array.Empty<AuthoritativeFleetLayoutNode>();
        if (string.IsNullOrWhiteSpace(payload))
            return true;

        string[] entries = payload.Split(';');
        if (entries.Length > 200)
            return false;

        var parsed = new AuthoritativeFleetLayoutNode[entries.Length];
        var shipIds = new HashSet<int>();
        for (int i = 0; i < entries.Length; ++i)
        {
            string[] parts = entries[i].Split('|');
            if (parts.Length != 13
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int shipId)
                || shipId < 0
                || !TryDecodeText(parts[1], out string shipName)
                || string.IsNullOrWhiteSpace(shipName)
                || !uint.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint xBits)
                || !uint.TryParse(parts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint yBits)
                || !uint.TryParse(parts[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint vultureBits)
                || !uint.TryParse(parts[5], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint shieldedBits)
                || !uint.TryParse(parts[6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint assistBits)
                || !uint.TryParse(parts[7], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint defenderBits)
                || !uint.TryParse(parts[8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint dpsBits)
                || !uint.TryParse(parts[9], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint sizeBits)
                || !uint.TryParse(parts[10], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint armoredBits)
                || !int.TryParse(parts[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out int combatState)
                || !Enum.IsDefined(typeof(CombatState), (CombatState)combatState)
                || !uint.TryParse(parts[12], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint ordersRadiusBits))
            {
                return false;
            }

            Vector2 offset = new(FloatFromBits(xBits), FloatFromBits(yBits));
            if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
                return false;
            float vultureWeight = FloatFromBits(vultureBits);
            float attackShieldedWeight = FloatFromBits(shieldedBits);
            float assistWeight = FloatFromBits(assistBits);
            float defenderWeight = FloatFromBits(defenderBits);
            float dpsWeight = FloatFromBits(dpsBits);
            float sizeWeight = FloatFromBits(sizeBits);
            float armoredWeight = FloatFromBits(armoredBits);
            float ordersRadius = FloatFromBits(ordersRadiusBits);
            if (!IsFinite(vultureWeight, attackShieldedWeight, assistWeight, defenderWeight,
                    dpsWeight, sizeWeight, armoredWeight, ordersRadius))
            {
                return false;
            }
            if (shipId != 0 && !shipIds.Add(shipId))
                return false;

            parsed[i] = new AuthoritativeFleetLayoutNode(shipId, shipName, offset,
                vultureWeight, attackShieldedWeight, assistWeight, defenderWeight,
                dpsWeight, sizeWeight, armoredWeight, (CombatState)combatState, ordersRadius);
        }

        nodes = parsed;
        return true;
    }

    static uint FloatBits(float value) => BitConverter.SingleToUInt32Bits(value);
    static float FloatFromBits(uint value) => BitConverter.UInt32BitsToSingle(value);
    static string Hex(uint value) => value.ToString("X8", CultureInfo.InvariantCulture);

    static bool IsFinite(params float[] values)
    {
        for (int i = 0; i < values.Length; ++i)
            if (!float.IsFinite(values[i]))
                return false;
        return true;
    }

    static string EncodeText(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));

    static bool TryDecodeText(string value, out string decoded)
    {
        decoded = "";
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value ?? ""));
            return true;
        }
        catch
        {
            return false;
        }
    }
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

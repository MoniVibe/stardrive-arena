using System;
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

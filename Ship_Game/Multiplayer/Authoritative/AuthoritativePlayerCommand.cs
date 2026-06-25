using SDLockstep;
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

    public static AuthoritativePlayerCommand MoveShip(int sequence, int empireId, int shipId, Vector2 destination)
        => new()
        {
            Sequence = sequence,
            EmpireId = empireId,
            Kind = AuthoritativePlayerCommandKind.MoveShip,
            SubjectId = shipId,
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
}

public sealed class AuthoritativeCommandResult
{
    public int Sequence;
    public bool Accepted;
    public uint Tick;
    public string Reason = "";

    public AuthoritativeCommandResultMessage ToMessage(int fromPeer)
        => new()
        {
            FromPeer = fromPeer,
            Sequence = Sequence,
            Accepted = Accepted,
            Tick = Tick,
            Reason = Reason ?? "",
        };
}

using System.Collections.Generic;
using SDLockstep;
using Ship_Game.AI.StrategyAI;
using Ship_Game.Gameplay;
using Ship_Game.Universe;

namespace Ship_Game.Multiplayer.Authoritative;

public sealed class AuthoritativeDiplomacyPopup
{
    public int ProposalId;
    public int ProposerEmpireId;
    public int TargetEmpireId;
    public AuthoritativeDiplomacyProposalType ProposalType;
    public string Terms = "";
    public bool RequiresResponse;
    public string Message = "";

    public AuthoritativeDiplomacyPopupMessage ToMessage(int fromPeer)
        => new()
        {
            FromPeer = fromPeer,
            ProposalId = ProposalId,
            ProposerEmpireId = ProposerEmpireId,
            TargetEmpireId = TargetEmpireId,
            ProposalType = (byte)ProposalType,
            Terms = Terms ?? "",
            RequiresResponse = RequiresResponse,
            Message = Message ?? "",
        };

    public static AuthoritativeDiplomacyPopup FromMessage(AuthoritativeDiplomacyPopupMessage message)
        => new()
        {
            ProposalId = message.ProposalId,
            ProposerEmpireId = message.ProposerEmpireId,
            TargetEmpireId = message.TargetEmpireId,
            ProposalType = (AuthoritativeDiplomacyProposalType)message.ProposalType,
            Terms = message.Terms ?? "",
            RequiresResponse = message.RequiresResponse,
            Message = message.Message ?? "",
        };
}

sealed class AuthoritativeDiplomacyProposalRecord
{
    public int Id;
    public int ProposerEmpireId;
    public int TargetEmpireId;
    public AuthoritativeDiplomacyProposalType Type;
    public string Terms = "";
}

public sealed class AuthoritativeDiplomacyManager
{
    readonly UniverseState UState;
    readonly Dictionary<int, AuthoritativeDiplomacyProposalRecord> Pending = new();
    readonly List<AuthoritativeDiplomacyPopup> Popups = new();
    int NextProposalId = 1;

    public AuthoritativeDiplomacyManager(UniverseState universe)
    {
        UState = universe;
    }

    public AuthoritativeDiplomacyPopup[] DrainPopups()
    {
        var popups = Popups.ToArray();
        Popups.Clear();
        return popups;
    }

    public AuthoritativeCommandResult Apply(AuthoritativePlayerCommand command, Empire empire,
        AuthoritativeCommandResult result)
    {
        return command.Kind switch
        {
            AuthoritativePlayerCommandKind.DiplomacyProposal => ApplyProposal(command, empire, result),
            AuthoritativePlayerCommandKind.DiplomacyResponse => ApplyResponse(command, empire, result),
            _ => Reject(result, $"Unsupported diplomacy command {command.Kind}."),
        };
    }

    AuthoritativeCommandResult ApplyProposal(AuthoritativePlayerCommand command, Empire proposer,
        AuthoritativeCommandResult result)
    {
        Empire target = UState.GetEmpireById(command.SubjectId);
        if (target == null)
            return Reject(result, $"Target empire {command.SubjectId} not found.");
        if (target == proposer)
            return Reject(result, "Diplomacy proposal target cannot be self.");
        if (command.TargetId < byte.MinValue || command.TargetId > byte.MaxValue
            || !System.Enum.IsDefined(typeof(AuthoritativeDiplomacyProposalType), (byte)command.TargetId))
            return Reject(result, $"Invalid diplomacy proposal type {command.TargetId}.");

        var type = (AuthoritativeDiplomacyProposalType)command.TargetId;
        if (!AuthoritativeHumanPlayers.IsHumanVsHuman(proposer, target))
            return Reject(result, "Authoritative human diplomacy only handles human-to-human proposals.");

        if (type == AuthoritativeDiplomacyProposalType.DeclareWar)
        {
            ApplyDeclareWar(proposer, target);
            QueuePopup(0, proposer, target, type, command.Text, requiresResponse: false, "War declared.");
            return Accept(result);
        }

        int id = NextProposalId++;
        Pending[id] = new AuthoritativeDiplomacyProposalRecord
        {
            Id = id,
            ProposerEmpireId = proposer.Id,
            TargetEmpireId = target.Id,
            Type = type,
            Terms = command.Text ?? "",
        };
        QueuePopup(id, proposer, target, type, command.Text, requiresResponse: true, "Diplomacy proposal received.");
        return Accept(result);
    }

    AuthoritativeCommandResult ApplyResponse(AuthoritativePlayerCommand command, Empire responder,
        AuthoritativeCommandResult result)
    {
        int proposalId = command.SubjectId;
        if (!Pending.TryGetValue(proposalId, out AuthoritativeDiplomacyProposalRecord proposal))
            return Reject(result, $"Diplomacy proposal {proposalId} not found.");
        if (proposal.TargetEmpireId != responder.Id)
            return Reject(result, $"Empire {responder.Id} cannot answer proposal {proposalId}.");
        if (command.TargetId < byte.MinValue || command.TargetId > byte.MaxValue
            || !System.Enum.IsDefined(typeof(AuthoritativeDiplomacyResponseKind), (byte)command.TargetId))
            return Reject(result, $"Invalid diplomacy response {command.TargetId}.");

        Empire proposer = UState.GetEmpireById(proposal.ProposerEmpireId);
        if (proposer == null)
            return Reject(result, $"Proposal source empire {proposal.ProposerEmpireId} not found.");

        var response = (AuthoritativeDiplomacyResponseKind)command.TargetId;
        Pending.Remove(proposalId);
        switch (response)
        {
            case AuthoritativeDiplomacyResponseKind.Accept:
                ApplyAgreement(proposal.Type, proposer, responder);
                QueuePopup(proposal.Id, responder, proposer, proposal.Type, proposal.Terms, requiresResponse: false,
                    "Diplomacy proposal accepted.");
                return Accept(result);
            case AuthoritativeDiplomacyResponseKind.Reject:
                QueuePopup(proposal.Id, responder, proposer, proposal.Type, proposal.Terms, requiresResponse: false,
                    "Diplomacy proposal rejected.");
                return Accept(result);
            case AuthoritativeDiplomacyResponseKind.Counter:
                int counterId = NextProposalId++;
                string terms = command.Text ?? "";
                Pending[counterId] = new AuthoritativeDiplomacyProposalRecord
                {
                    Id = counterId,
                    ProposerEmpireId = responder.Id,
                    TargetEmpireId = proposer.Id,
                    Type = proposal.Type,
                    Terms = terms,
                };
                QueuePopup(counterId, responder, proposer, proposal.Type, terms, requiresResponse: true,
                    "Counter-proposal received.");
                return Accept(result);
            default:
                return Reject(result, $"Unsupported diplomacy response {response}.");
        }
    }

    void ApplyAgreement(AuthoritativeDiplomacyProposalType type, Empire proposer, Empire target)
    {
        switch (type)
        {
            case AuthoritativeDiplomacyProposalType.NonAggression:
                proposer.SignTreatyWith(target, TreatyType.NonAggression);
                break;
            case AuthoritativeDiplomacyProposalType.TradeDeal:
                proposer.SignTreatyWith(target, TreatyType.Trade);
                break;
            case AuthoritativeDiplomacyProposalType.Peace:
                EndWarBetween(proposer, target);
                proposer.SignTreatyWith(target, TreatyType.Peace);
                break;
            case AuthoritativeDiplomacyProposalType.Alliance:
                if (proposer.IsAtWarWith(target))
                    EndWarBetween(proposer, target);
                proposer.SignAllianceWith(target);
                break;
            case AuthoritativeDiplomacyProposalType.DeclareWar:
                ApplyDeclareWar(proposer, target);
                break;
        }

        Empire.UpdateBilateralRelations(proposer, target);
    }

    static void ApplyDeclareWar(Empire proposer, Empire target)
    {
        if (!proposer.IsAtWarWith(target))
            proposer.AI.DeclareWarOn(target, WarType.ImperialistWar);
        Empire.UpdateBilateralRelations(proposer, target);
    }

    static void EndWarBetween(Empire a, Empire b)
    {
        EndWarOneWay(a, b);
        EndWarOneWay(b, a);
        Empire.UpdateBilateralRelations(a, b);
    }

    static void EndWarOneWay(Empire us, Empire them)
    {
        Relationship rel = us.GetRelations(them);
        rel.AtWar = false;
        rel.CancelPrepareForWar();
        if (rel.ActiveWar != null)
        {
            rel.ActiveWar.EndStarDate = us.Universe.StarDate;
            rel.WarHistory.Add(rel.ActiveWar);
            rel.ActiveWar = null;
        }
        rel.ResetAngerMilitaryConflict();
        rel.WarnedAboutShips = false;
        rel.WarnedAboutColonizing = false;
        rel.HaveRejectedDemandTech = false;
        rel.HaveRejected_OpenBorders = false;
        rel.HaveRejected_TRADE = false;
    }

    void QueuePopup(int proposalId, Empire proposer, Empire target, AuthoritativeDiplomacyProposalType type,
        string terms, bool requiresResponse, string message)
    {
        Popups.Add(new AuthoritativeDiplomacyPopup
        {
            ProposalId = proposalId,
            ProposerEmpireId = proposer.Id,
            TargetEmpireId = target.Id,
            ProposalType = type,
            Terms = terms ?? "",
            RequiresResponse = requiresResponse,
            Message = message ?? "",
        });
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

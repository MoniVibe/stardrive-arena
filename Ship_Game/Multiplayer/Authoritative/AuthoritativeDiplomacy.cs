using System.Collections.Generic;
using SDLockstep;
using Ship_Game.AI.StrategyAI;
using Ship_Game.AI.StrategyAI.WarGoals;
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

    public bool TryDeclareWarForHostileHumanAction(Empire proposer, Empire target, string terms,
        out string reason)
    {
        reason = "";
        if (!AuthoritativeHumanPlayers.IsHumanVsHuman(proposer, target))
        {
            reason = "Hostile-action auto war only applies between human-controlled empires.";
            return false;
        }

        if (!proposer.IsAtWarWith(target))
        {
            ApplyDeclareWar(proposer, target);
            QueuePopup(0, proposer, target, AuthoritativeDiplomacyProposalType.DeclareWar,
                terms ?? "", requiresResponse: false, "War declared.");
        }

        if (proposer.IsAtWarWith(target))
            return true;

        reason = $"Empire {proposer.Id} could not declare war on empire {target.Id}.";
        return false;
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

        string terms = command.Text ?? "";
        if (type == AuthoritativeDiplomacyProposalType.TechnologyTrade)
        {
            terms = terms.Trim();
            if (!CanOfferTechnology(proposer, target, terms, out string reason))
                return Reject(result, reason);
        }

        if (type == AuthoritativeDiplomacyProposalType.DeclareWar)
        {
            ApplyDeclareWar(proposer, target);
            QueuePopup(0, proposer, target, type, terms, requiresResponse: false, "War declared.");
            return Accept(result);
        }

        int id = NextProposalId++;
        Pending[id] = new AuthoritativeDiplomacyProposalRecord
        {
            Id = id,
            ProposerEmpireId = proposer.Id,
            TargetEmpireId = target.Id,
            Type = type,
            Terms = terms,
        };
        QueuePopup(id, proposer, target, type, terms, requiresResponse: true, "Diplomacy proposal received.");
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
        switch (response)
        {
            case AuthoritativeDiplomacyResponseKind.Accept:
                if (proposal.Type == AuthoritativeDiplomacyProposalType.TechnologyTrade
                    && !CanOfferTechnology(proposer, responder, proposal.Terms, out string reason))
                {
                    return Reject(result, reason);
                }
                Pending.Remove(proposalId);
                ApplyAgreement(proposal.Type, proposer, responder, proposal.Terms);
                QueuePopup(proposal.Id, responder, proposer, proposal.Type, proposal.Terms, requiresResponse: false,
                    "Diplomacy proposal accepted.");
                return Accept(result);
            case AuthoritativeDiplomacyResponseKind.Reject:
                Pending.Remove(proposalId);
                QueuePopup(proposal.Id, responder, proposer, proposal.Type, proposal.Terms, requiresResponse: false,
                    "Diplomacy proposal rejected.");
                return Accept(result);
            case AuthoritativeDiplomacyResponseKind.Counter:
                Pending.Remove(proposalId);
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

    void ApplyAgreement(AuthoritativeDiplomacyProposalType type, Empire proposer, Empire target, string terms)
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
            case AuthoritativeDiplomacyProposalType.TechnologyTrade:
                ApplyTechnologyTrade(proposer, target, terms);
                break;
        }

        Empire.UpdateBilateralRelations(proposer, target);
    }

    static bool CanOfferTechnology(Empire proposer, Empire target, string techUid, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(techUid))
        {
            reason = "Technology trade requires a tech UID.";
            return false;
        }
        techUid = techUid.Trim();
        if (!proposer.TryGetTechEntry(techUid, out TechEntry proposerTech))
        {
            reason = $"Proposer empire {proposer.Id} has no tech entry for {techUid}.";
            return false;
        }
        if (!proposerTech.Unlocked)
        {
            reason = $"Proposer empire {proposer.Id} has not unlocked {techUid}.";
            return false;
        }
        if (proposerTech.IsMultiLevel)
        {
            reason = $"Technology {techUid} is multi-level and cannot be traded authoritatively yet.";
            return false;
        }
        if (!target.TryGetTechEntry(techUid, out TechEntry targetTech))
        {
            reason = $"Target empire {target.Id} has no tech entry for {techUid}.";
            return false;
        }
        if (targetTech.Unlocked)
        {
            reason = $"Target empire {target.Id} already has {techUid}.";
            return false;
        }
        if (!proposerTech.TheyCanUseThis(proposer, target))
        {
            reason = $"Target empire {target.Id} cannot use {techUid}.";
            return false;
        }
        return true;
    }

    static void ApplyTechnologyTrade(Empire proposer, Empire target, string techUid)
    {
        techUid = (techUid ?? "").Trim();
        target.UnlockTech(techUid, TechUnlockType.Diplomacy, proposer);
        proposer.GetRelations(target).NumTechsWeGave += 1;
    }

    static void ApplyDeclareWar(Empire proposer, Empire target)
    {
        if (proposer.IsAtWarWith(target))
            return;

        Relationship usToThem = proposer.GetRelations(target);
        usToThem.CancelPrepareForWar();
        if (proposer.IsFaction || proposer.IsDefeated || target.IsFaction || target.IsDefeated)
            return;

        usToThem.FedQuest = null;
        if (usToThem.Treaty_Alliance)
            MarkDeclaredWarOnAlly(proposer);

        usToThem.AtWar = true;
        usToThem.ChangeToHostile();
        usToThem.ActiveWar = War.CreateInstance(proposer, target, WarType.ImperialistWar);
        usToThem.Trust = 0f;
        proposer.BreakAllTreatiesWith(target, includingPeace: true);
        target.AI.GetWarDeclaredOnUs(proposer, WarType.ImperialistWar);
        Empire.UpdateBilateralRelations(proposer, target);
    }

    static void MarkDeclaredWarOnAlly(Empire proposer)
    {
        foreach (Empire empire in proposer.Universe.ActiveMajorEmpires)
        {
            if (empire != proposer)
                empire.GetRelations(proposer).SetDeclaredWarOnAlly();
        }
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

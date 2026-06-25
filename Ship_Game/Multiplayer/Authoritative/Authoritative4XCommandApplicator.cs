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

    public Authoritative4XCommandApplicator(UniverseState universe) => UState = universe;

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
                _ => Reject(result, $"Unsupported command kind {command.Kind}."),
            };
        }
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
        ship.AI.OrderMoveTo(command.Position, dir, AIState.AwaitingOrders, MoveOrder.Regular);
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

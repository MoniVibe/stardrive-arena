using Ship_Game.AI;
using Ship_Game.Ships;
using Ship_Game.Universe;
using SDLockstep;
using SDUtils.Deterministic;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.Determinism.Lockstep;

/// <summary>
/// Translates authoritative <see cref="SimCommand"/>s into real StarDrive orders (advisor plan
/// RC3–RC5). This is the ONLY path that mutates the sim from player/network intent — each command is
/// applied inside a <see cref="StarDriveCommandContext"/> scope so stray direct-mutation paths can be
/// caught. Every command either applies deterministically or becomes a deterministic no-op
/// (CommandsRejected++); no command partially mutates then fails. Commands name targets by stable id —
/// there is no spatial/nearest search in command application (sim/AI may use that later, sorted).
/// </summary>
public sealed class StarDriveCommandApplicator
{
    readonly StarDriveEntityResolver Resolver;

    public long CommandsApplied;
    public long CommandsRejected;

    public StarDriveCommandApplicator(UniverseState universe) => Resolver = new StarDriveEntityResolver(universe);

    public void Apply(CommandFrame frame)
    {
        for (int i = 0; i < frame.Commands.Count; ++i)
        {
            SimCommand c = frame.Commands[i];
            using (StarDriveCommandContext.Enter(c.Tick, c.PlayerId))
            {
                if (ApplyOne(c)) CommandsApplied++;
                else CommandsRejected++;
            }
        }
    }

    bool ApplyOne(in SimCommand c)
    {
        switch (c.Kind)
        {
            case SimCommandKind.NoOp:         return true;
            case SimCommandKind.MoveShip:     return ApplyMove(c);
            case SimCommandKind.AttackTarget: return ApplyAttack(c);
            case SimCommandKind.StopShip:     return ApplyStop(c);
            case SimCommandKind.Forfeit:      return ApplyForfeit(c);
            default:                          return false;
        }
    }

    // Arena 8-player teams — C9 forfeit apply (ruling C9). SubjectId carries the forfeiting EMPIRE id (universe-
    // resolvable; a slot id would have no universe referent, breaking the applicator's universe-only contract).
    // Set the deterministic per-empire ArenaForfeited marker (read only by the fight screen's presentation-side
    // barrier, never hashed), then kill every one of that empire's ACTIVE ships. The kill set is snapshotted and
    // SORTED by ascending Ship.Id so the death sequence — and therefore the digest fold — is identical on every
    // peer regardless of the empire's live-ship list ordering. cleanupOnly:true bypasses the RNG-driven death
    // roll (WillShipDieNow / explosion chance) so no order-dependent RNG side effect can diverge the digest —
    // the exact removal B0's RemoveMultiplayerShips already uses.
    bool ApplyForfeit(in SimCommand c)
    {
        Empire empire = Resolver.ResolveEmpire(c.SubjectId);
        if (empire == null)
            return false;
        empire.ArenaForfeited = true;
        var doomed = new System.Collections.Generic.List<Ship>();
        foreach (Ship ship in empire.OwnedShips)
            if (ship != null && ship.Active)
                doomed.Add(ship);
        doomed.Sort((a, b) => a.Id.CompareTo(b.Id));
        foreach (Ship ship in doomed)
            ship.Die(null, cleanupOnly: true);
        return true;
    }

    bool ApplyMove(in SimCommand c)
    {
        Empire empire = Resolver.ResolveEmpire(c.PlayerId);
        Ship ship = Resolver.ResolveShip(c.SubjectId);
        if (empire == null || ship == null || !ship.Active || !Resolver.Owns(empire, ship))
            return false;

        var dest = new Vector2(Fixed64.FromRaw(c.PosXRaw).ToFloat(), Fixed64.FromRaw(c.PosYRaw).ToFloat());
        Vector2 delta = dest - ship.Position;
        Vector2 dir = delta.Length() > 0f ? delta.Normalized() : new Vector2(1f, 0f);
        ship.AI.OrderMoveTo(dest, dir, AIState.AwaitingOrders, MoveOrder.Regular);
        return true;
    }

    bool ApplyAttack(in SimCommand c)
    {
        Empire empire = Resolver.ResolveEmpire(c.PlayerId);
        Ship ship = Resolver.ResolveShip(c.SubjectId);
        Ship target = Resolver.ResolveShip(c.TargetId);
        if (empire == null || ship == null || target == null || !ship.Active || ship == target || !Resolver.Owns(empire, ship))
            return false;

        ship.AI.OrderAttackSpecificTarget(target);
        return true;
    }

    bool ApplyStop(in SimCommand c)
    {
        Empire empire = Resolver.ResolveEmpire(c.PlayerId);
        Ship ship = Resolver.ResolveShip(c.SubjectId);
        if (empire == null || ship == null || !Resolver.Owns(empire, ship))
            return false;

        ship.AI.OrderHoldPosition(MoveOrder.HoldPosition | MoveOrder.StandGround);
        return true;
    }
}

using System;
using System.Threading;
using Ship_Game;
using Ship_Game.Ships;

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

    public static bool IsActiveFor(Empire empire)
        => TryGetFor(empire, out _);

    static bool TryGetFor(Empire empire, out Authoritative4XClientContext context)
    {
        context = Active;
        return context != null && empire != null && empire.Id == context.EmpireId;
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

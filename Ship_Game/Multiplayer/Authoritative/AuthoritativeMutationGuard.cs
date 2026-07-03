using System;
using System.Diagnostics;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
using Ship_Game.Universe;

namespace Ship_Game.Multiplayer.Authoritative;

public enum AuthoritativeMutationFamily
{
    PlanetRuntime,
    TroopRuntime,
    ShipRuntime,
    Diplomacy,
    EmpireAutomation,
}

public readonly struct AuthoritativeMutationScope : IDisposable
{
#if DEBUG
    readonly Action DisposeAction;

    internal AuthoritativeMutationScope(Action disposeAction)
    {
        DisposeAction = disposeAction;
    }

    public void Dispose()
    {
        DisposeAction?.Invoke();
    }
#else
    public void Dispose()
    {
    }
#endif
}

public static class AuthoritativeMutationGuard
{
#if DEBUG
    [ThreadStatic] static int ReplayApplyDepth;
    [ThreadStatic] static int AcceptedCommandDepth;

    public static AuthoritativeMutationScope EnterReplayApply()
    {
        ++ReplayApplyDepth;
        return new AuthoritativeMutationScope(static () => --ReplayApplyDepth);
    }

    public static AuthoritativeMutationScope EnterAcceptedCommandApply()
    {
        ++AcceptedCommandDepth;
        return new AuthoritativeMutationScope(static () => --AcceptedCommandDepth);
    }

    static bool IsSanctionedPath => ReplayApplyDepth > 0 || AcceptedCommandDepth > 0;
#endif

    public static void ResetForTests()
    {
#if DEBUG
        ReplayApplyDepth = 0;
        AcceptedCommandDepth = 0;
#endif
    }

    [Conditional("DEBUG")]
    public static void AssertCanMutate(Planet planet, AuthoritativeMutationFamily family, string field)
    {
#if DEBUG
        if (IsSanctionedPath || !Authoritative4XClientContext.ShouldTripMutationGuard(planet))
            return;
        Throw(family, field);
#endif
    }

    [Conditional("DEBUG")]
    public static void AssertCanMutate(Ship ship, AuthoritativeMutationFamily family, string field)
    {
#if DEBUG
        if (IsSanctionedPath || !Authoritative4XClientContext.ShouldTripMutationGuard(ship))
            return;
        Throw(family, field);
#endif
    }

    [Conditional("DEBUG")]
    public static void AssertCanMutate(Empire empire, AuthoritativeMutationFamily family, string field)
    {
#if DEBUG
        if (IsSanctionedPath || !Authoritative4XClientContext.ShouldTripMutationGuard(empire))
            return;
        Throw(family, field);
#endif
    }

    [Conditional("DEBUG")]
    public static void AssertCanMutate(UniverseState universe, AuthoritativeMutationFamily family, string field)
    {
#if DEBUG
        if (IsSanctionedPath || !Authoritative4XClientContext.ShouldTripMutationGuard(universe))
            return;
        Throw(family, field);
#endif
    }

    [Conditional("DEBUG")]
    public static void AssertCanMutate(Relationship relationship, Empire owner, AuthoritativeMutationFamily family,
        string field)
    {
#if DEBUG
        if (IsSanctionedPath || !Authoritative4XClientContext.ShouldTripMutationGuard(owner))
            return;
        string target = relationship?.Them?.Name ?? "";
        Throw(family, $"{field}{(!string.IsNullOrEmpty(target) ? $"->{target}" : "")}");
#endif
    }

#if DEBUG
    static void Throw(AuthoritativeMutationFamily family, string field)
    {
        throw new InvalidOperationException(
            $"Passive authoritative client attempted local replicated-state mutation: family={family} field={field ?? ""}");
    }
#endif
}

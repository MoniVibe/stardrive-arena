using System;
using System.Diagnostics;
using Ship_Game.Fleets;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
using Ship_Game.Universe;

namespace Ship_Game.Multiplayer.Authoritative;

public enum AuthoritativeMutationFamily
{
    PlanetRuntime,
    TroopRuntime,
    ShipRuntime,
    ShipPresence,
    GroundCombat,
    EmpireRuntime,
    ConstructionQueue,
    FleetRuntime,
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
    [ThreadStatic] static int UniverseInitDepth;

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

    // Sanctions the one-time initial universe build (LoadContent -> InitializeUniverse:
    // CreateStartingShips, solar-system/empire init, ship warmup). A passive joiner
    // constructs a local universe that the host's first snapshot then reconciles;
    // that construction is not passive-sim mutation. The live Update loop is NOT
    // wrapped, so genuine passive-sim leaks are still caught.
    public static AuthoritativeMutationScope EnterUniverseInitialization()
    {
        ++UniverseInitDepth;
        return new AuthoritativeMutationScope(static () => --UniverseInitDepth);
    }

    static bool IsSanctionedPath => ReplayApplyDepth > 0 || AcceptedCommandDepth > 0
        || UniverseInitDepth > 0;
#endif

    public static void ResetForTests()
    {
#if DEBUG
        ReplayApplyDepth = 0;
        AcceptedCommandDepth = 0;
        UniverseInitDepth = 0;
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
    public static void AssertCanMutate(Fleet fleet, AuthoritativeMutationFamily family, string field)
    {
#if DEBUG
        if (IsSanctionedPath || !Authoritative4XClientContext.ShouldTripMutationGuard(fleet))
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
    static readonly System.Collections.Generic.HashSet<string> LoggedLiveTrips = new();

    static void Throw(AuthoritativeMutationFamily family, string field)
    {
        string message =
            $"Passive authoritative client attempted local replicated-state mutation: family={family} field={field ?? ""}";

        // In the automated test suite a guard trip is a genuine defect: fail the test hard.
        if (GlobalStats.IsUnitTest)
            throw new InvalidOperationException(message);

        // In the live game (Debug QA build) a guard trip is a DIAGNOSTIC signal, not a
        // crash. The offending mutation is benign in a Release build (the next host
        // snapshot reconciles it), so a false positive must not end the joiner's session.
        // Log it once per unique family+field, with a stack trace so the source is as
        // traceable as the old crash dump was, then let the game continue.
        string key = $"{family}:{field}";
        lock (LoggedLiveTrips)
        {
            if (!LoggedLiveTrips.Add(key))
                return;
        }
        Log.Error($"{message}\n{new StackTrace(true)}");
    }
#endif
}

using System;
using System.Reflection;
using SDUtils;
using Ship_Game.Data;
using Ship_Game.Ships;
using Ship_Game.Universe;
using Ship_Game.Universe.SolarBodies;

namespace Ship_Game.GameScreens.Arena;

public static class ArenaEngineCapabilities
{
    static readonly MethodInfo EnableDeterministicRngMethod =
        typeof(UniverseState).GetMethod("EnableDeterministicRng",
            BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(ulong) }, null);

    static readonly PropertyInfo SpatialDeterministicCollisionsProperty =
        typeof(ISpatial).GetProperty("DeterministicCollisions", BindingFlags.Instance | BindingFlags.Public);

    static readonly MethodInfo SeedDeterministicGenerationMethod =
        typeof(PlanetTypes).GetMethod("SeedDeterministicGeneration",
            BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int) }, null);

    static readonly MethodInfo UseDeterministicRandomMethod =
        typeof(Empire).GetMethod("UseDeterministicRandom",
            BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(ulong) }, null);

    static readonly FieldInfo EnableParallelUpdateField =
        typeof(UniverseObjectManager).GetField("EnableParallelUpdate",
            BindingFlags.Instance | BindingFlags.Public);

    static readonly MethodInfo RecalculateMaxHealthMethod =
        typeof(Ship).GetMethod("RecalculateMaxHealth",
            BindingFlags.Instance | BindingFlags.NonPublic);

    static bool ForceDeterminismUnavailable;

    public static bool IsDeterminismAvailable
        => !ForceDeterminismUnavailable && EnableDeterministicRngMethod != null;

    public static bool IsGenerationSeedingAvailable
        => !ForceDeterminismUnavailable && SeedDeterministicGenerationMethod != null;

    public static IDisposable ForceDeterminismUnavailableForTests()
    {
        bool previous = ForceDeterminismUnavailable;
        ForceDeterminismUnavailable = true;
        return new RestoreForcedDeterminism(previous);
    }

    public static bool TryEnableSeededRng(UniverseState state, ulong seed)
    {
        if (state == null || ForceDeterminismUnavailable || EnableDeterministicRngMethod == null)
            return false;
        try
        {
            EnableDeterministicRngMethod.Invoke(state, new object[] { seed });
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"ArenaEngineCapabilities: deterministic RNG unavailable: {Message(e)}");
            return false;
        }
    }

    public static bool TrySetStableCollisions(object spatial, bool enabled)
    {
        if (spatial == null || ForceDeterminismUnavailable || SpatialDeterministicCollisionsProperty == null)
            return false;
        try
        {
            SpatialDeterministicCollisionsProperty.SetValue(spatial, enabled);
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"ArenaEngineCapabilities: deterministic collisions unavailable: {Message(e)}");
            return false;
        }
    }

    public static bool TrySeedGeneration(int seed)
    {
        PlanetTypes planets = ResourceManager.Planets;
        if (planets == null || ForceDeterminismUnavailable || SeedDeterministicGenerationMethod == null)
            return false;
        try
        {
            SeedDeterministicGenerationMethod.Invoke(planets, new object[] { seed });
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"ArenaEngineCapabilities: generation seeding unavailable: {Message(e)}");
            return false;
        }
    }

    public static bool TryUseSeededEmpireRng(Empire empire, ulong seed)
    {
        if (empire == null || ForceDeterminismUnavailable || UseDeterministicRandomMethod == null)
            return false;
        try
        {
            UseDeterministicRandomMethod.Invoke(empire, new object[] { seed });
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"ArenaEngineCapabilities: empire deterministic RNG unavailable: {Message(e)}");
            return false;
        }
    }

    public static bool TrySetParallelUpdate(UniverseObjectManager objects, bool enabled)
    {
        if (objects == null || EnableParallelUpdateField == null)
            return false;
        try
        {
            EnableParallelUpdateField.SetValue(objects, enabled);
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"ArenaEngineCapabilities: parallel update flag unavailable: {Message(e)}");
            return false;
        }
    }

    public static void RepairArenaFully(Ship ship)
    {
        if (ship == null)
            return;

        foreach (ShipModule module in ship.Modules ?? Empty<ShipModule>.Array)
            module?.SetHealth(module.ActualMaxHealth, "ArenaRepair");

        float maxHealth = RecalculateMaxHealth(ship);
        ship.Health = maxHealth > 0f ? maxHealth : ship.HealthMax;
    }

    public static void RechargeArenaShields(Ship ship)
    {
        if (ship == null)
            return;

        foreach (ShipModule shield in ship.GetShields())
            shield?.InitShieldPower(0f);
        ship.UpdateShields();
    }

    static float RecalculateMaxHealth(Ship ship)
    {
        try
        {
            if (RecalculateMaxHealthMethod?.Invoke(ship, Array.Empty<object>()) is float reflected)
                return reflected;
        }
        catch (Exception e)
        {
            Log.Warning($"ArenaEngineCapabilities: ship health recalculation unavailable: {Message(e)}");
        }

        float max = 0f;
        foreach (ShipModule module in ship.Modules ?? Empty<ShipModule>.Array)
            if (module != null)
                max += module.ActualMaxHealth;
        return max > 0f ? max : ship.HealthMax;
    }

    static string Message(Exception e)
    {
        if (e is TargetInvocationException tie && tie.InnerException != null)
            return tie.InnerException.Message;
        return e.Message;
    }

    sealed class RestoreForcedDeterminism : IDisposable
    {
        readonly bool Previous;
        bool Disposed;

        public RestoreForcedDeterminism(bool previous) => Previous = previous;

        public void Dispose()
        {
            if (Disposed)
                return;
            ForceDeterminismUnavailable = Previous;
            Disposed = true;
        }
    }
}

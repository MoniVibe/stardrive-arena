using System.Collections.Generic;
using Ship_Game.Universe;

namespace Ship_Game.Multiplayer.Authoritative;

/// <summary>
/// Runtime-only registry for authoritative multiplayer empires controlled by humans.
/// Empty registry means stock single-player behavior.
/// </summary>
public static class AuthoritativeHumanPlayers
{
    static readonly object Sync = new();
    static readonly Dictionary<UniverseState, HashSet<int>> HumansByUniverse = new();

    public static void SetHumanControlledEmpires(UniverseState universe, params int[] empireIds)
    {
        if (universe == null)
            return;

        lock (Sync)
        {
            var set = new HashSet<int>();
            if (empireIds != null)
                foreach (int id in empireIds)
                    if (id > 0)
                        set.Add(id);
            HumansByUniverse[universe] = set;
        }
    }

    public static void Clear(UniverseState universe)
    {
        if (universe == null)
            return;
        lock (Sync)
            HumansByUniverse.Remove(universe);
    }

    public static bool IsHumanControlled(Empire empire)
    {
        if (empire == null)
            return false;
        if (empire.isPlayer)
            return true;

        UniverseState universe = empire.Universe;
        if (universe == null)
            return false;

        lock (Sync)
            return HumansByUniverse.TryGetValue(universe, out HashSet<int> humans)
                   && humans.Contains(empire.Id);
    }

    public static bool IsHumanVsHuman(Empire a, Empire b)
    {
        return a != null && b != null && a != b && IsHumanControlled(a) && IsHumanControlled(b);
    }
}

using Ship_Game.Universe;

namespace Ship_Game.Multiplayer.Authoritative;

public sealed partial class AuthoritativeStateSnapshot
{
    internal static void ReplayUnlockedTechPayload(UniverseState universe, string[] lines)
        => ApplyUnlockedTechPayload(universe, lines);

    internal static void ReplayPlayerDesignPayload(UniverseState universe, string[] lines)
        => ApplyLines(universe, lines, "D", ApplyPlayerDesignLine);

    internal static void ReplayShipPresencePayload(UniverseState universe, string[] lines)
        => ApplyShipPresencePayload(universe, lines);

    internal static void ReplayShipRuntimePayload(UniverseState universe, string[] lines)
        => ApplyShipRuntimePayload(universe, lines);

    internal static void ReplayShipTroopPayload(UniverseState universe, string[] lines)
        => ApplyShipTroopPayload(universe, lines);

    internal static void ReplayColonyTilePayload(UniverseState universe, string[] lines)
        => ApplyColonyTilePayload(universe, lines);

    internal static void ReplayGroundTroopPayload(UniverseState universe, string[] lines)
        => ApplyGroundTroopPayload(universe, lines);

    internal static void ReplayGroundCombatPayload(UniverseState universe, string[] lines)
        => ApplyGroundCombatPayload(universe, lines);

    internal static void ReplayFleetRuntimePayload(UniverseState universe, string[] lines)
        => ApplyFleetRuntimePayload(universe, lines);

    internal static void ReplayConstructionQueuePayload(UniverseState universe, string[] lines)
        => ApplyConstructionQueuePayload(universe, lines);

    internal static void ReplayColonizationGoalPayload(UniverseState universe, string[] lines)
        => ApplyColonizationGoalPayload(universe, lines);

    internal static void ReplayDeepSpaceGoalPayload(UniverseState universe, string[] lines)
        => ApplyDeepSpaceGoalPayload(universe, lines);

    static void ApplyLines(UniverseState universe, string[] lines, string prefix,
        AuthoritativeReplicationLineApply applyLine)
    {
        string match = prefix + "|";
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith(match, System.StringComparison.Ordinal))
                applyLine(universe, line);
        }
    }
}

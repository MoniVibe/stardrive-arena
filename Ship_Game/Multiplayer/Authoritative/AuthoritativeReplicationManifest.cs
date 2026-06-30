using System;
using System.Collections.Generic;
using System.Linq;

namespace Ship_Game.Multiplayer.Authoritative;

public enum AuthoritativeReplicationApplyMode
{
    DirectReplay,
    BatchReplay,
    DigestOnly,
}

public sealed class AuthoritativeReplicationRow
{
    public readonly string Prefix;
    public readonly string Owner;
    public readonly AuthoritativeReplicationApplyMode ApplyMode;
    public readonly string Notes;

    public AuthoritativeReplicationRow(string prefix, string owner,
        AuthoritativeReplicationApplyMode applyMode, string notes)
    {
        Prefix = prefix;
        Owner = owner;
        ApplyMode = applyMode;
        Notes = notes;
    }
}

public static class AuthoritativeReplicationManifest
{
    static readonly AuthoritativeReplicationRow[] Rows =
    {
        new("V", "UniversePreferences", AuthoritativeReplicationApplyMode.DirectReplay,
            "Host-owned global multiplayer preference flags."),
        new("E", "EmpireRuntime", AuthoritativeReplicationApplyMode.DirectReplay,
            "Cash, research queue, taxes, automation flags, and auto-design selections."),
        new("U", "UnlockedTech", AuthoritativeReplicationApplyMode.DirectReplay,
            "Unlocked empire technology rows."),
        new("D", "PlayerDesign", AuthoritativeReplicationApplyMode.DirectReplay,
            "Player ship designs registered for an empire."),
        new("R", "Relationship", AuthoritativeReplicationApplyMode.DirectReplay,
            "Diplomacy and treaty state."),
        new("P", "PlanetRuntime", AuthoritativeReplicationApplyMode.DirectReplay,
            "Colony type, labor, import/export, governor, budgets, and queue count."),
        new("S", "ShipRuntime", AuthoritativeReplicationApplyMode.DirectReplay,
            "Ship AI state, targets, durable orders, trade policy, carrier flags, and operation area. Freight pickup/dropoff goals are authority-only volatile AI plans and are not replayed."),
        new("Q", "ConstructionQueue", AuthoritativeReplicationApplyMode.BatchReplay,
            "Planet construction queue shape and runtime progress."),
        new("G", "EmpireGoal", AuthoritativeReplicationApplyMode.BatchReplay,
            "MarkForColonization goals are replayed; other goal kinds are currently digest-observed."),
        new("FP", "FleetPatrol", AuthoritativeReplicationApplyMode.DigestOnly,
            "Fleet patrol definitions are covered by the digest but not replay-patched."),
        new("F", "FleetRuntime", AuthoritativeReplicationApplyMode.DigestOnly,
            "Fleet membership/layout/patrol state is covered by the digest but not replay-patched."),
        new("BP", "Blueprint", AuthoritativeReplicationApplyMode.DigestOnly,
            "Colony blueprints are covered by the digest but not replay-patched."),
        new("T", "ColonyTile", AuthoritativeReplicationApplyMode.DigestOnly,
            "Tile buildings/biospheres are covered by the digest but not replay-patched."),
        new("GT", "GroundTroop", AuthoritativeReplicationApplyMode.DigestOnly,
            "Planet troop rows are covered by the digest but not replay-patched."),
        new("GC", "GroundCombat", AuthoritativeReplicationApplyMode.DigestOnly,
            "Ground-combat rows are covered by the digest but not replay-patched."),
        new("ST", "ShipTroop", AuthoritativeReplicationApplyMode.DigestOnly,
            "Ship troop rows are covered by the digest but not replay-patched."),
    };

    static readonly Dictionary<string, AuthoritativeReplicationRow> ByPrefix =
        Rows.ToDictionary(row => row.Prefix, StringComparer.Ordinal);

    public static IReadOnlyList<AuthoritativeReplicationRow> AllRows => Rows;

    public static bool TryGetRow(string prefix, out AuthoritativeReplicationRow row)
        => ByPrefix.TryGetValue(prefix ?? "", out row);

    public static string PrefixForLine(string line)
    {
        if (string.IsNullOrEmpty(line) || string.Equals(line, "<missing>", StringComparison.Ordinal))
            return "";
        int pipe = line.IndexOf('|');
        return pipe > 0 ? line.Substring(0, pipe) : line;
    }

    public static string[] UnknownPrefixesForPayload(string payload)
        => (payload ?? "").Split('\n')
            .Select(line => PrefixForLine(line.TrimEnd('\r')))
            .Where(prefix => !string.IsNullOrEmpty(prefix) && !ByPrefix.ContainsKey(prefix))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(prefix => prefix, StringComparer.Ordinal)
            .ToArray();

    public static string DescribeLine(string line)
    {
        string prefix = PrefixForLine(line);
        if (string.IsNullOrEmpty(prefix))
            return "prefix=<none> owner=<none> apply=<none>";
        return TryGetRow(prefix, out AuthoritativeReplicationRow row)
            ? $"prefix={row.Prefix} owner={row.Owner} apply={row.ApplyMode}"
            : $"prefix={prefix} owner=<unknown> apply=<unknown>";
    }

    public static string DescribeDiff(string authorityLine, string clientLine)
    {
        string line = !string.Equals(authorityLine, "<missing>", StringComparison.Ordinal)
            ? authorityLine
            : clientLine;
        return DescribeLine(line);
    }
}

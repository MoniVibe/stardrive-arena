using System;
using System.Collections.Generic;
using System.Linq;
using Ship_Game.Ships;
using FleetDesignT = global::Ship_Game.FleetDesign;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// P1 roster scoping for the Arena MP fleet-setup step (plan Part 2a). Resolves the buildable design
/// set for a ruleset — Career mode is locked to the career's fielded vessels; Sandbox is all legal
/// combat designs — and builds the canonical fleet bundle from a chosen name list.
///
/// This is the determinism-foundation half of Part 2. The FleetDesignScreen drag-drop editor is
/// UniverseScreen-coupled; per the plan's documented fallback, P1 emits a zero-offset column bundle
/// from the scoped name list (still hashed + exchanged), so steps 1-5 land playtestable. The real
/// FleetDesignScreen setup is deferred (see the report), and slots in here later by projecting a
/// live Fleet via ArenaFleetBundle.FromFleet instead of FromDesignNames.
/// </summary>
public static class ArenaFleetSetupScope
{
    /// <summary>
    /// The legal roster (design names) a side may pick from, given the ruleset.
    /// Career: the career's fielded vessels' designs (plus its starting roster fallback).
    /// Sandbox: all legal combat, stock-content designs, ordered deterministically.
    /// </summary>
    public static string[] ResolveRoster(ArenaMultiplayerRuleset ruleset, ArenaCareer career)
    {
        ruleset ??= new ArenaMultiplayerRuleset();
        if (ruleset.Mode == ArenaMatchMode.Career)
            return CareerRoster(career);
        return SandboxRoster();
    }

    public static string[] CareerRoster(ArenaCareer career)
    {
        if (career == null)
            return Array.Empty<string>();
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (OwnedVessel v in career.FieldedFleetVessels())
        {
            string dn = v?.DesignName;
            if (dn.NotEmpty() && seen.Add(dn)
                && ResourceManager.Ships.GetDesign(dn, out IShipDesign d)
                && ArenaFightScreen.IsLegalCombatCraft(d))
                names.Add(dn);
        }
        return names.ToArray();
    }

    public static string[] SandboxRoster()
        => ResourceManager.Ships.Designs
            .Where(ArenaFightScreen.IsLegalCombatCraft)
            .Where(ArenaFightScreen.IsStockContentDesign)
            .OrderBy(d => d.BaseStrength)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .Select(d => d.Name)
            .ToArray();

    /// <summary>
    /// Builds the canonical (zero-offset column) bundle for a chosen name list, clamped to the
    /// ruleset's MaxFleetShipsPerSide. This is the P1 fallback fleet projection.
    /// </summary>
    public static FleetDesignT BuildBundle(ArenaMultiplayerRuleset ruleset, string[] chosenNames, string name = "")
    {
        int cap = Math.Max(1, (ruleset ?? new ArenaMultiplayerRuleset()).MaxFleetShipsPerSide);
        string[] clamped = ArenaMultiplayerSettings.NormalizeFleet(chosenNames).Take(cap).ToArray();
        return ArenaFleetBundle.FromDesignNames(clamped, name);
    }

    /// <summary>
    /// True if the chosen fleet is legal for the ruleset (Sandbox budget cap; Career roster membership).
    /// Returns the reason string on failure ("" when acceptable). Both peers can run this locally.
    /// </summary>
    public static string ValidateChosenFleet(ArenaMultiplayerRuleset ruleset, string[] chosenNames,
        ArenaCareer career)
    {
        ruleset ??= new ArenaMultiplayerRuleset();
        string[] names = ArenaMultiplayerSettings.NormalizeFleet(chosenNames);
        if (names.Length == 0)
            return "Fleet must contain at least one ship.";
        if (names.Length > Math.Max(1, ruleset.MaxFleetShipsPerSide))
            return $"Fleet exceeds the {ruleset.MaxFleetShipsPerSide}-ship cap.";

        foreach (string dn in names)
            if (!ResourceManager.Ships.GetDesign(dn, out IShipDesign d) || !ArenaFightScreen.IsLegalCombatCraft(d))
                return $"Design '{dn}' is not a legal combat craft.";

        if (ruleset.Mode == ArenaMatchMode.Career)
        {
            var roster = new HashSet<string>(CareerRoster(career), StringComparer.Ordinal);
            foreach (string dn in names)
                if (!roster.Contains(dn))
                    return $"Design '{dn}' is not in the career roster.";
        }
        else if (ruleset.Mode == ArenaMatchMode.Sandbox && ruleset.BudgetModel == ArenaBudgetModel.Cap)
        {
            int cost = names.Sum(dn => ResourceManager.Ships.GetDesign(dn, out IShipDesign d)
                ? (int)MathF.Round(d.BaseStrength) : 0);
            if (cost > ruleset.BudgetCredits)
                return $"Fleet cost {cost} exceeds budget {ruleset.BudgetCredits}.";
        }
        return "";
    }
}

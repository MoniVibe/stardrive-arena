using System;
using SDUtils;
using Ship_Game.Ships;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// Effect channel of a v0 pilot trait. Each value maps 1:1 to ONE additive per-<see cref="Ship"/>
/// bonus field (Ship.Pilot*Bonus) that the existing weapon/evade math already reads. No kind may
/// read empire state, RNG, target family/type, or wall-clock — the family-blind doctrine and the
/// determinism contract both depend on this. Adding a kind requires a new headless proof.
/// </summary>
public enum PilotTraitKind
{
    Accuracy, // -target-error fraction  -> Ship.PilotAccuracyBonus
    Damage,   // +weapon-damage fraction -> Ship.PilotDamageBonus
    Tracking, // +tracked targets (int)  -> Ship.PilotTrackingBonus
    Evade,    // +explosion-evade points  -> Ship.PilotEvadeBonus
}

/// <summary>
/// A composed, additive per-<see cref="Ship"/> effect for a pilot's auto-granted traits. This is a
/// PURE function of (pilot record, granted trait ids) + the static <see cref="PilotTraitV0.Catalog"/>
/// — identical inputs always yield an identical value, so it is safe to apply on both peers.
/// </summary>
public readonly struct ShipTraitEffect
{
    public static readonly ShipTraitEffect None = new(0f, 0f, 0, 0f);

    public readonly float Accuracy; // fraction, folded as (1 - Accuracy) on weapon target error
    public readonly float Damage;   // fraction, folded as damage * (1 + Damage)
    public readonly int   Tracking; // additive tracked-target count
    public readonly float Evade;    // additive explosion-evade points

    public ShipTraitEffect(float accuracy, float damage, int tracking, float evade)
    {
        Accuracy = accuracy;
        Damage   = damage;
        Tracking = tracking;
        Evade    = evade;
    }

    public bool IsZero => Accuracy == 0f && Damage == 0f && Tracking == 0 && Evade == 0f;
}

/// <summary>
/// A single data-driven pilot trait definition. Mirrors <see cref="ArenaPerkDefinition"/> (readonly
/// struct: Id/Name/Description/Kind/Value) and adds a level gate. The DEFERRED Layer-3 fields
/// (Branch/Excludes/PointCost) exist in the schema per the advisor ruling but are ignored by the v0
/// auto-grant path so there is no player-choice state to hash.
/// </summary>
public readonly struct PilotTraitDefinition
{
    public readonly string Id;
    public readonly string Name;
    public readonly string Description;
    public readonly int LevelReq;   // granted when pilot Level >= this (0..10 crew-level scale)
    public readonly PilotTraitKind Kind;
    public readonly float Value;

    // ---- DEFERRED-to-Layer-3 fields (present in schema, ignored by v0 auto-grant) ----
    public readonly string Branch;   // mutual-exclusion group; empty = no branch
    public readonly string[] Excludes;
    public readonly int PointCost;

    public PilotTraitDefinition(string id, string name, string description, int levelReq,
        PilotTraitKind kind, float value, string branch = "", string[] excludes = null, int pointCost = 0)
    {
        Id = id ?? "";
        Name = name ?? "";
        Description = description ?? "";
        LevelReq = levelReq;
        Kind = kind;
        Value = value;
        Branch = branch ?? "";
        Excludes = excludes ?? Empty<string>.Array;
        PointCost = pointCost;
    }
}

/// <summary>
/// The v0 pilot-trait catalog (Layer 1). Mirrors the <see cref="ArenaPerks"/> static-array idiom —
/// content is a hardcoded, Ordinal-sorted catalog rather than an external file, matching how the
/// sibling arena content (perks) already loads. Four traits, ONE per already-live mechanical channel
/// (accuracy / damage / tracking / evade) so each is independently headless-verifiable. Auto-granted
/// at level thresholds; point-buy / branching / mutual-exclusion are DEFERRED to Layer 3.
///
/// Determinism: the catalog is sorted by Id (Ordinal); grant + compose are pure functions of the
/// pilot's Level and the static catalog — no empire state, RNG, or wall-clock. Values are placeholders
/// per the director's no-locked-balance mandate.
/// </summary>
public static class PilotTraitV0
{
    public const string EagleEyeId           = "eagle_eye";
    public const string GunneryDrillId       = "gunnery_drill";
    public const string PredictiveTrackingId = "predictive_tracking";
    public const string EvasiveAceId         = "evasive_ace";

    // Ordinal-sorted by Id so peer ordering is stable when hashed (Layer 2).
    public static readonly PilotTraitDefinition[] Catalog =
    {
        new(EagleEyeId, "Eagle Eye", "-15% weapon target error.",
            levelReq: 2, PilotTraitKind.Accuracy, 0.15f),
        new(EvasiveAceId, "Evasive Ace", "+10 explosion-evade chance.",
            levelReq: 5, PilotTraitKind.Evade, 10f),
        new(GunneryDrillId, "Gunnery Drill", "+8% weapon damage (separate channel from crew level).",
            levelReq: 3, PilotTraitKind.Damage, 0.08f),
        new(PredictiveTrackingId, "Predictive Tracking", "+1 weapon tracking (helps hit fast targets).",
            levelReq: 4, PilotTraitKind.Tracking, 1f),
    };

    public static bool TryGet(string id, out PilotTraitDefinition definition)
    {
        if (id.NotEmpty())
        {
            foreach (PilotTraitDefinition trait in Catalog)
            {
                if (string.Equals(trait.Id, id, StringComparison.Ordinal))
                {
                    definition = trait;
                    return true;
                }
            }
        }
        definition = default;
        return false;
    }

    public static bool IsKnown(string id) => TryGet(id, out _);

    /// <summary>
    /// Keep only known trait ids (trimmed), de-duplicated, sorted Ordinal. Mirrors
    /// <see cref="ArenaPerks.Normalize"/> but also de-dupes and sorts so the granted set is a stable,
    /// canonical list (important once it is hashed into the MP fingerprint in Layer 2).
    /// </summary>
    public static string[] Normalize(string[] traitIds)
    {
        if (traitIds == null || traitIds.Length == 0)
            return Empty<string>.Array;

        var seen = new Array<string>();
        foreach (string raw in traitIds)
        {
            string id = raw?.Trim();
            if (IsKnown(id) && !Contains(seen, id))
                seen.Add(id);
        }
        if (seen.Count == 0)
            return Empty<string>.Array;

        string[] result = seen.ToArray();
        Array.Sort(result, StringComparer.Ordinal);
        return result;
    }

    static bool Contains(Array<string> list, string id)
    {
        foreach (string s in list)
            if (string.Equals(s, id, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>
    /// The trait ids AUTO-GRANTED to a pilot at the given crew level: every catalog trait whose
    /// LevelReq &lt;= level. Pure function of (level, static catalog); Ordinal-sorted, canonical.
    /// This is the v0 grant rule — no player choice, no respec.
    /// </summary>
    public static string[] GrantedTraitsForLevel(int level)
    {
        var granted = new Array<string>();
        foreach (PilotTraitDefinition trait in Catalog)
            if (level >= trait.LevelReq)
                granted.Add(trait.Id);

        if (granted.Count == 0)
            return Empty<string>.Array;

        string[] result = granted.ToArray();
        Array.Sort(result, StringComparer.Ordinal);
        return result;
    }

    /// <summary>
    /// Compose the additive per-Ship effect from an explicit set of granted trait ids. Pure function
    /// of (traitIds, static catalog): sums each known trait's Value into its channel. Unknown ids are
    /// ignored. Deterministic and order-independent (addition commutes; sorted input regardless).
    /// </summary>
    public static ShipTraitEffect ComposeShipEffects(string[] traitIds)
    {
        if (traitIds == null || traitIds.Length == 0)
            return ShipTraitEffect.None;

        float accuracy = 0f, damage = 0f, evade = 0f;
        int tracking = 0;
        foreach (string id in traitIds)
        {
            if (!TryGet(id, out PilotTraitDefinition trait))
                continue;
            switch (trait.Kind)
            {
                case PilotTraitKind.Accuracy: accuracy += trait.Value;         break;
                case PilotTraitKind.Damage:   damage   += trait.Value;         break;
                case PilotTraitKind.Tracking: tracking += (int)trait.Value;    break;
                case PilotTraitKind.Evade:    evade    += trait.Value;         break;
            }
        }
        return new ShipTraitEffect(accuracy, damage, tracking, evade);
    }

    /// <summary>
    /// Convenience: compose the effect for the traits a pilot auto-earns at <paramref name="level"/>.
    /// This is the exact channel used at the choke point when no persisted granted-id list is present.
    /// </summary>
    public static ShipTraitEffect ComposeForLevel(int level)
        => ComposeShipEffects(GrantedTraitsForLevel(level));

    /// <summary>
    /// Write a composed effect onto a spawned ship's additive Pilot*Bonus fields. Idempotent for a
    /// given effect (it OVERWRITES, it does not accumulate), so re-running the choke point is safe.
    /// </summary>
    public static void ApplyToShip(Ship ship, in ShipTraitEffect effect)
    {
        if (ship == null)
            return;
        ship.PilotAccuracyBonus = effect.Accuracy;
        ship.PilotDamageBonus   = effect.Damage;
        ship.PilotTrackingBonus = effect.Tracking;
        ship.PilotEvadeBonus    = effect.Evade;
    }
}

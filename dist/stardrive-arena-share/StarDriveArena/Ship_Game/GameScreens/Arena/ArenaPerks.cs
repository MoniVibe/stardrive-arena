using System;
using System.Globalization;
using System.IO;
using System.Linq;
using SDUtils;
using Ship_Game.Gameplay;
using Ship_Game.Ships;

namespace Ship_Game.GameScreens.Arena;

public enum ArenaPerkKind
{
    WeaponDamage,
    HullHealth,
    CashPerClear,
    RepairDiscount,
    FleetSlot,
    FightChoice,
    Scout,
    Research,
}

public readonly struct ArenaPerkDefinition
{
    public readonly string Id;
    public readonly string Name;
    public readonly string Description;
    public readonly ArenaPerkKind Kind;
    public readonly float Value;

    public ArenaPerkDefinition(string id, string name, string description, ArenaPerkKind kind, float value)
    {
        Id = id ?? "";
        Name = name ?? "";
        Description = description ?? "";
        Kind = kind;
        Value = value;
    }
}

public readonly struct ArenaBossEncounter
{
    public static readonly ArenaBossEncounter None = new(false, "", "", "", 0f, 1f, 1f);

    public readonly bool Active;
    public readonly string Name;
    public readonly string DesignName;
    public readonly string RoleClass;
    public readonly float BaseStrength;
    public readonly float StrengthMultiplier;
    public readonly float HealthMultiplier;

    public ArenaBossEncounter(bool active, string name, string designName, string roleClass,
        float baseStrength, float strengthMultiplier, float healthMultiplier)
    {
        Active = active;
        Name = name ?? "";
        DesignName = designName ?? "";
        RoleClass = roleClass ?? "";
        BaseStrength = Math.Max(0f, baseStrength);
        StrengthMultiplier = Math.Max(1f, strengthMultiplier);
        HealthMultiplier = Math.Max(1f, healthMultiplier);
    }

    public float EffectiveStrength => BaseStrength * StrengthMultiplier;
}

public static class ArenaPerks
{
    public const string WeaponDamageId = "weapon_matrix";
    public const string HullHealthId = "remnant_plating";
    public const string CashPerClearId = "salvage_rights";
    public const string RepairDiscountId = "repair_crews";
    public const string FleetSlotId = "fleet_commission";
    public const string FightChoiceId = "fight_broker";
    public const string ScoutId = "arena_scout";
    public const string ResearchId = "research_grant";
    public const int MaxFleetSizeCap = 100;

    public static readonly ArenaPerkDefinition[] Catalog =
    {
        new(WeaponDamageId, "Weapon Matrix", "+10% damage for all weapon tags.",
            ArenaPerkKind.WeaponDamage, 0.10f),
        new(HullHealthId, "Remnant Plating", "+10% module and hull health.",
            ArenaPerkKind.HullHealth, 0.10f),
        new(CashPerClearId, "Salvage Rights", "+20% cash from every cleared round.",
            ArenaPerkKind.CashPerClear, 0.20f),
        new(RepairDiscountId, "Repair Crews", "Repairs cost 25% less.",
            ArenaPerkKind.RepairDiscount, 0.25f),
        new(FleetSlotId, "Fleet Commission", "+1 active Arena fleet slot.",
            ArenaPerkKind.FleetSlot, 1f),
        new(FightChoiceId, "Fight Broker", "+1 fight option on the Arena slate.",
            ArenaPerkKind.FightChoice, 1f),
        new(ScoutId, "Scout Network", "Preview more detail before committing to a bout.",
            ArenaPerkKind.Scout, 1f),
        new(ResearchId, "Research Grant", "+1 Arena module-shop tech tier.",
            ArenaPerkKind.Research, 1f),
    };

    public static bool TryGet(string id, out ArenaPerkDefinition definition)
    {
        if (id.NotEmpty())
        {
            foreach (ArenaPerkDefinition perk in Catalog)
            {
                if (string.Equals(perk.Id, id, StringComparison.Ordinal))
                {
                    definition = perk;
                    return true;
                }
            }
        }
        definition = default;
        return false;
    }

    public static bool IsKnown(string id) => TryGet(id, out _);

    public static string[] Normalize(string[] perkIds)
    {
        if (perkIds == null || perkIds.Length == 0)
            return Empty<string>.Array;

        var normalized = new Array<string>();
        foreach (string raw in perkIds)
        {
            string id = raw?.Trim();
            if (IsKnown(id))
                normalized.Add(id);
        }
        return normalized.Count > 0 ? normalized.ToArray() : Empty<string>.Array;
    }

    public static int Count(string[] perkIds, string id)
    {
        if (perkIds == null || id.IsEmpty())
            return 0;
        int count = 0;
        foreach (string perkId in perkIds)
            if (string.Equals(perkId, id, StringComparison.Ordinal))
                ++count;
        return count;
    }

    public static float TotalValue(string[] perkIds, ArenaPerkKind kind)
    {
        if (perkIds == null || perkIds.Length == 0)
            return 0f;

        float total = 0f;
        foreach (string id in perkIds)
            if (TryGet(id, out ArenaPerkDefinition perk) && perk.Kind == kind)
                total += perk.Value;
        return total;
    }

    public static void ApplyToEmpire(Empire empire, string[] perkIds)
    {
        if (empire?.data == null || perkIds == null || perkIds.Length == 0)
            return;
        foreach (string id in perkIds)
            ApplySingleToEmpire(empire, id);
    }

    public static bool ApplySingleToEmpire(Empire empire, string perkId)
    {
        if (empire?.data == null || !TryGet(perkId, out ArenaPerkDefinition perk))
            return false;

        switch (perk.Kind)
        {
            case ArenaPerkKind.WeaponDamage:
                foreach (WeaponTag tag in WeaponTemplate.TagValues)
                    empire.data.WeaponTags[tag].Damage += perk.Value;
                return true;
            case ArenaPerkKind.HullHealth:
                empire.data.Traits.ModHpModifier += perk.Value;
                EmpireHullBonuses.RefreshBonuses(empire);
                empire.ApplyModuleHealthTechBonus(perk.Value);
                return true;
            default:
                return true;
        }
    }

    public static int CashPerClear(int baseCash, string[] perkIds)
    {
        float bonus = TotalValue(perkIds, ArenaPerkKind.CashPerClear);
        return Math.Max(1, (int)Math.Round(baseCash * (1f + bonus)));
    }

    public static int RepairCost(int baseCost, string[] perkIds)
    {
        float discount = Math.Clamp(TotalValue(perkIds, ArenaPerkKind.RepairDiscount), 0f, 0.90f);
        return Math.Max(1, (int)Math.Round(baseCost * (1f - discount)));
    }

    public static int MaxFleetSize(int baseSize, string[] perkIds)
        => Math.Clamp(baseSize + Count(perkIds, FleetSlotId), 1, MaxFleetSizeCap);

    public static int ExtraFightChoices(string[] perkIds)
        => Math.Clamp(Count(perkIds, FightChoiceId), 0, 3);

    public static int ResearchTierBonus(string[] perkIds)
        => Math.Clamp(Count(perkIds, ResearchId), 0, 2);

    public static ArenaPerkDefinition[] DeterministicOffer(ArenaCareer career, int count = 3)
    {
        int take = Math.Clamp(count, 1, Catalog.Length);
        string[] perks = Normalize(career?.Perks);
        int level = Math.Max(0, career?.CareerLevel ?? 0);
        int cash = Math.Max(0, career?.Cash ?? 0);
        ulong seed = 0xB055_CAFE_A11E_0001ul
                   ^ ((ulong)level << 32)
                   ^ (uint)cash
                   ^ ((ulong)perks.Length * 0x9E37_79B9ul);

        return Catalog
            .OrderBy(p => Mix(seed ^ StableHash(p.Id) ^ (ulong)(Count(perks, p.Id) * 0x51ED)))
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .Take(take)
            .ToArray();
    }

    static ulong StableHash(string value)
    {
        ulong h = 1469598103934665603ul;
        if (value != null)
            foreach (char c in value)
                h = (h ^ c) * 1099511628211ul;
        return h;
    }

    static ulong Mix(ulong x)
    {
        x ^= x >> 30;
        x *= 0xbf58476d1ce4e5b9ul;
        x ^= x >> 27;
        x *= 0x94d049bb133111ebul;
        return x ^ (x >> 31);
    }
}

public sealed class ArenaBossPerkReport
{
    public readonly ArenaBossEncounter Boss;
    public readonly string NormalDesign;
    public readonly string NormalRole;
    public readonly float NormalStrength;
    public readonly ArenaPerkDefinition[] Perks;
    public readonly string Verdict;

    public ArenaBossPerkReport(ArenaBossEncounter boss, string normalDesign, string normalRole,
        float normalStrength, ArenaPerkDefinition[] perks, string verdict)
    {
        Boss = boss;
        NormalDesign = normalDesign ?? "";
        NormalRole = normalRole ?? "";
        NormalStrength = Math.Max(0f, normalStrength);
        Perks = perks ?? Array.Empty<ArenaPerkDefinition>();
        Verdict = verdict ?? "";
    }
}

public static class ArenaBossPerkSimulator
{
    public const string ReportFileName = "arena-boss-perks.json";

    public static ArenaBossPerkReport Run(int careerLevel = 7)
    {
        ArenaBossEncounter boss = ArenaFightScreen.PickBossEncounter(null,
            ArenaFightScreen.BossEncounterRound, careerLevel);
        IShipDesign normal = ArenaFightScreen.PickEnemyEscort(null,
            ArenaFightScreen.TotalRounds, careerLevel);
        float ratio = normal != null && normal.BaseStrength > 0f
            ? boss.EffectiveStrength / normal.BaseStrength
            : 0f;
        string verdict = boss.Active
            ? $"tier-3 boss '{boss.Name}' uses {boss.DesignName} at {F(boss.EffectiveStrength)} effective strength, {F(ratio)}x normal late escort"
            : "no boss encounter available at sampled career level";

        return new ArenaBossPerkReport(boss, normal?.Name ?? "", normal?.Role.ToString() ?? "",
            normal?.BaseStrength ?? 0f, ArenaPerks.Catalog, verdict);
    }

    public static string WriteReport(ArenaBossPerkReport report, string outputDir)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (outputDir.IsEmpty())
            throw new ArgumentException("Output directory is required.", nameof(outputDir));

        Directory.CreateDirectory(outputDir);
        string path = Path.Combine(outputDir, ReportFileName);
        File.WriteAllText(path, ToJson(report));
        return path;
    }

    public static string ToJson(ArenaBossPerkReport report)
    {
        string perks = string.Join(",\n    ", report.Perks.Select(p =>
            $"{{\"id\":{J(p.Id)},\"name\":{J(p.Name)},\"kind\":{J(p.Kind.ToString())}," +
            $"\"value\":{F(p.Value)},\"description\":{J(p.Description)}}}"));

        return "{\n" +
               "  \"experiment\": \"ARENA BOSS PERKS: tier-3 boss encounter sample plus fixed persistent perk catalog\",\n" +
               $"  \"verdict\": {J(report.Verdict)},\n" +
               $"  \"boss\": {{\"active\":{(report.Boss.Active ? "true" : "false")},\"name\":{J(report.Boss.Name)}," +
               $"\"design\":{J(report.Boss.DesignName)},\"role\":{J(report.Boss.RoleClass)}," +
               $"\"baseStrength\":{F(report.Boss.BaseStrength)},\"strengthMultiplier\":{F(report.Boss.StrengthMultiplier)}," +
               $"\"healthMultiplier\":{F(report.Boss.HealthMultiplier)},\"effectiveStrength\":{F(report.Boss.EffectiveStrength)}}},\n" +
               $"  \"normalLateEnemy\": {{\"design\":{J(report.NormalDesign)},\"role\":{J(report.NormalRole)}," +
               $"\"baseStrength\":{F(report.NormalStrength)}}},\n" +
               $"  \"perks\": [\n    {perks}\n  ]\n" +
               "}\n";
    }

    static string J(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

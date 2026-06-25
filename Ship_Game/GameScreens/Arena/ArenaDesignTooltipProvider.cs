using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Ship_Game.Debug;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
using ShipDesignData = Ship_Game.Ships.ShipDesign;

namespace Ship_Game.GameScreens.Arena;

public sealed class ArenaDesignTooltipModuleRow
{
    public readonly string ModuleUid;
    public readonly string DisplayName;
    public readonly string Summary;
    public readonly int Count;

    public ArenaDesignTooltipModuleRow(string moduleUid, string displayName, string summary, int count)
    {
        ModuleUid = moduleUid ?? "";
        DisplayName = displayName ?? ModuleUid;
        Summary = summary ?? DisplayName;
        Count = Math.Max(0, count);
    }
}

public sealed class ArenaDesignTooltipData
{
    public readonly string DesignName;
    public readonly string Role;
    public readonly float HullPercent;
    public readonly int DestroyedModuleSlots;
    public readonly ArenaDesignTooltipModuleRow[] Modules;

    public int TotalModuleCount => Modules?.Sum(m => m.Count) ?? 0;

    public ArenaDesignTooltipData(string designName, string role, float hullPercent,
        int destroyedModuleSlots, ArenaDesignTooltipModuleRow[] modules)
    {
        DesignName = designName ?? "";
        Role = role ?? "";
        HullPercent = Math.Clamp(hullPercent, 0f, 1f);
        DestroyedModuleSlots = Math.Max(0, destroyedModuleSlots);
        Modules = modules ?? Array.Empty<ArenaDesignTooltipModuleRow>();
    }

    public string ToTooltipText(int maxRows = 8)
    {
        var text = new StringBuilder();
        text.AppendLine($"{DesignName} ({Role})");
        text.AppendLine($"Hull: {HullPercent * 100f:0}%");
        if (DestroyedModuleSlots > 0)
            text.AppendLine($"Destroyed module slots: {DestroyedModuleSlots}");
        text.AppendLine($"Modules: {TotalModuleCount}");
        foreach (ArenaDesignTooltipModuleRow row in Modules.Take(Math.Max(1, maxRows)))
            text.AppendLine($"{row.DisplayName} x{row.Count}");
        if (Modules.Length > maxRows)
            text.AppendLine($"+{Modules.Length - maxRows} more module types");
        return text.ToString().TrimEnd();
    }
}

public static class ArenaDesignTooltipProvider
{
    static readonly MethodInfo ModuleSummaryMethod = typeof(ShipModuleInfoPanel)
        .GetMethod("ModuleSummary", BindingFlags.Static | BindingFlags.Public);

    public static ArenaDesignTooltipData ForDesign(IShipDesign design)
        => Build(design, hullPercent: 1f, destroyedSlots: Array.Empty<int>(),
            overrides: Array.Empty<ModuleSlotOverride>());

    public static ArenaDesignTooltipData ForOwnedVessel(OwnedVessel vessel)
    {
        if (vessel == null || vessel.DesignName.IsEmpty()
            || !ResourceManager.Ships.GetDesign(vessel.DesignName, out IShipDesign design))
        {
            return new ArenaDesignTooltipData(vessel?.DesignName, "", 1f, 0,
                Array.Empty<ArenaDesignTooltipModuleRow>());
        }

        DestroyedModuleSlot[] destroyed = ArenaCareer.NormalizeDestroyedModules(vessel.DestroyedModules);
        ModuleSlotOverride[] overrides = ArenaCareer.NormalizeModuleOverrides(vessel.ModuleOverrides);
        float hullPercent = vessel.CurrentHullHealth > 0f && vessel.MaxHullHealth > 0f
            ? vessel.CurrentHullHealth / Math.Max(1f, vessel.MaxHullHealth)
            : 1f;
        return Build(design, hullPercent, destroyed.Select(s => s.SlotIndex).ToArray(), overrides);
    }

    static ArenaDesignTooltipData Build(IShipDesign design, float hullPercent, int[] destroyedSlots,
        ModuleSlotOverride[] overrides)
    {
        if (design == null)
            return new ArenaDesignTooltipData("", "", hullPercent, destroyedSlots?.Length ?? 0,
                Array.Empty<ArenaDesignTooltipModuleRow>());

        var destroyed = new HashSet<int>(destroyedSlots ?? Array.Empty<int>());
        var overrideBySlot = (overrides ?? Array.Empty<ModuleSlotOverride>())
            .Where(o => o.ModuleUid.NotEmpty())
            .ToDictionary(o => o.SlotIndex, o => o.ModuleUid);

        IReadOnlyList<string> moduleUids = EffectiveModuleUids(design, destroyed, overrideBySlot);
        ArenaDesignTooltipModuleRow[] rows = moduleUids
            .Where(uid => uid.NotEmpty())
            .GroupBy(uid => uid, StringComparer.Ordinal)
            .Select(g => BuildRow(g.Key, g.Count()))
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.DisplayName, StringComparer.Ordinal)
            .ThenBy(r => r.ModuleUid, StringComparer.Ordinal)
            .ToArray();

        return new ArenaDesignTooltipData(design.Name, design.Role.ToString(),
            hullPercent, destroyed.Count, rows);
    }

    static IReadOnlyList<string> EffectiveModuleUids(IShipDesign design, HashSet<int> destroyed,
        Dictionary<int, string> overrideBySlot)
    {
        try
        {
            DesignSlot[] slots = (design as ShipDesignData)?.GetOrLoadDesignSlots();
            if (slots == null || slots.Length == 0)
                return design.UniqueModuleUIDs ?? Array.Empty<string>();

            var uids = new List<string>(slots.Length);
            for (int i = 0; i < slots.Length; ++i)
            {
                if (overrideBySlot.TryGetValue(i, out string overrideUid))
                {
                    uids.Add(overrideUid);
                    continue;
                }
                if (destroyed.Contains(i))
                    continue;
                DesignSlot slot = slots[i];
                if (slot != null && slot.ModuleUID.NotEmpty())
                    uids.Add(slot.ModuleUID);
            }
            return uids;
        }
        catch
        {
            return design.UniqueModuleUIDs ?? Array.Empty<string>();
        }
    }

    static ArenaDesignTooltipModuleRow BuildRow(string uid, int count)
    {
        string display = uid;
        string summary = uid;
        if (uid.NotEmpty() && ResourceManager.GetModuleTemplate(uid, out ShipModule module) && module != null)
        {
            string name = module.NameText.Text;
            if (name.NotEmpty())
                display = name;
            summary = ModuleSummary(module);
        }
        return new ArenaDesignTooltipModuleRow(uid, display, summary, count);
    }

    static string ModuleSummary(ShipModule module)
    {
        if (module == null)
            return "";
        try
        {
            if (ModuleSummaryMethod?.Invoke(null, new object[] { module }) is string summary
                && summary.NotEmpty())
                return summary;
        }
        catch
        {
            // Stock engines may not expose the debug panel helper; keep tooltips functional.
        }
        string name = module.NameText.Text.NotEmpty() ? module.NameText.Text : module.UID;
        return $"{name} [{module.ModuleType}] HP {module.ActualMaxHealth:0}";
    }
}

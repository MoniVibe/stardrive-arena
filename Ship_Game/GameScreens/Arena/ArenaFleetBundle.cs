using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SDGraphics;
using SDUtils;
using SDUtils.Deterministic;
using Ship_Game.AI;
using Ship_Game.Fleets;
using Vector2 = SDGraphics.Vector2;
// Disambiguate the FleetDesignT TYPE (Ship_Game.FleetDesign) from the child NAMESPACE
// Ship_Game.GameScreens.FleetDesign, which otherwise shadows it in this namespace.
using FleetDesignT = global::Ship_Game.FleetDesign;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// P1 canonical fleet-design bundle for Arena multiplayer (STARDRIVE_ARENA_P1_FLEETSETUP_EXEC_PLAN_20260705).
///
/// A <see cref="FleetDesignT"/> (ship names + per-ship RelativeFleetOffset + targeting weights) is the
/// canonical design bundle exchanged in the lobby and folded into SettingsHash. This static owns the
/// single deterministic projection/hash/encode/decode path so BOTH the hash AND the formation spawn
/// iterate nodes in the SAME stable order — if the two orders ever diverge, ship IDs desync silently
/// (plan Part 6 risk 4). <see cref="StableNodeOrder"/> is that single shared ordering.
/// </summary>
public static class ArenaFleetBundle
{
    // Bump only on a wire/hash-layout change to the bundle serialization.
    public const int BundleVersion = 0;
    // Ruling-2 default; also matches ArenaMultiplayerSettings.NormalizeFleet Take(32).
    public const int MaxNodes = 32;
    const char NodeSeparator = '';   // record separator
    const char FieldSeparator = '';  // unit separator
    // Guard the wire against a pathological bundle. 32 nodes * ~12 fields is far below this.
    public const int MaxEncodedChars = 16 * 1024;

    /// <summary>
    /// The ONE canonical node ordering used by BOTH the hash and the spawn. Insertion order is the
    /// editor drag sequence and is NOT deterministic across peers, so we sort by (ShipName ordinal,
    /// offset.X bits, offset.Y bits). Never trust <see cref="FleetDesignT.Nodes"/> insertion order.
    /// </summary>
    public static IReadOnlyList<FleetDataDesignNode> StableNodeOrder(FleetDesignT design)
    {
        if (design?.Nodes == null || design.Nodes.Count == 0)
            return Array.Empty<FleetDataDesignNode>();
        // Stable, total order. Compare offsets by exact IEEE754 bits so the sort is bit-portable
        // and consistent with the AddFloat hashing below.
        return design.Nodes
            .Where(n => n != null && (n.ShipName ?? "").NotEmpty())
            .OrderBy(n => n.ShipName ?? "", StringComparer.Ordinal)
            .ThenBy(n => BitConverter.SingleToUInt32Bits(n.RelativeFleetOffset.X))
            .ThenBy(n => BitConverter.SingleToUInt32Bits(n.RelativeFleetOffset.Y))
            .ToArray();
    }

    /// <summary>
    /// Shared "live Fleet -> serializable FleetDesignT" projection. Mirrors
    /// SaveFleetDesignScreen.DoSave (GetDesignOnly strips the live Ship/Goal), so the arena setup and
    /// the fleet-save screen produce byte-identical bundles from the same fleet.
    /// </summary>
    public static FleetDesignT FromFleet(Fleet fleet, string name = null)
    {
        var design = new FleetDesignT
        {
            Name = name ?? fleet?.Name ?? "",
            FleetIconIndex = fleet?.FleetIconIndex ?? 0,
        };
        if (fleet?.DataNodes != null)
            foreach (FleetDataNode node in fleet.DataNodes)
                if (node != null)
                    design.Nodes.Add(node.GetDesignOnly());
        return design;
    }

    /// <summary>
    /// Builds a zero-offset column bundle from a plain design-name list (P1 fallback / name-list path).
    /// Offsets are all zero so spawn falls back to the column layout, but the bundle is still hashed and
    /// exchanged, so it rides the exact same determinism machinery as an authored formation.
    /// </summary>
    public static FleetDesignT FromDesignNames(string[] designNames, string name = "")
    {
        var design = new FleetDesignT { Name = name ?? "", FleetIconIndex = 0 };
        foreach (string dn in ArenaMultiplayerSettings.NormalizeFleet(designNames))
            design.Nodes.Add(new FleetDataDesignNode { ShipName = dn, RelativeFleetOffset = Vector2.Zero });
        return design;
    }

    /// <summary>
    /// Canonical deterministic bytes of a bundle, FIXED field order, nodes in <see cref="StableNodeOrder"/>.
    /// DetHash.AddFloat hashes IEEE754 bits and AddString hashes UTF-16 code units, so this is portable.
    /// Do NOT hash the YAML text (float formatting/culture/node order are not canonical there).
    /// </summary>
    public static DetHash CanonicalBytes(FleetDesignT design)
    {
        var h = DetHash.New();
        IReadOnlyList<FleetDataDesignNode> nodes = StableNodeOrder(design);
        h.AddInt(BundleVersion);
        h.AddString(design?.Name ?? "");
        h.AddInt(design?.FleetIconIndex ?? 0);
        h.AddInt(nodes.Count);
        foreach (FleetDataDesignNode node in nodes)
        {
            h.AddString(node.ShipName ?? "");
            h.AddFloat(node.RelativeFleetOffset.X);
            h.AddFloat(node.RelativeFleetOffset.Y);
            h.AddFloat(node.VultureWeight);
            h.AddFloat(node.AttackShieldedWeight);
            h.AddFloat(node.AssistWeight);
            h.AddFloat(node.DefenderWeight);
            h.AddFloat(node.DPSWeight);
            h.AddFloat(node.SizeWeight);
            h.AddFloat(node.ArmoredWeight);
            h.AddInt((int)node.CombatState);
            h.AddFloat(node.OrdersRadius);
        }
        return h;
    }

    /// <summary>The "0xXXXXXXXXXXXXXXXX" design-bundle hash string folded into SettingsHash.</summary>
    public static string DesignBundleHash(FleetDesignT design)
        => "0x" + CanonicalBytes(design).Value.ToString("X16", CultureInfo.InvariantCulture);

    /// <summary>The bundle hash of a plain name list (the P1 zero-offset fallback bundle).</summary>
    public static string DesignBundleHashForNames(string[] designNames)
        => DesignBundleHash(FromDesignNames(designNames));

    /// <summary>
    /// Deterministic string encoding of a bundle for the wire (size-capped). Nodes are emitted in
    /// <see cref="StableNodeOrder"/> so the encoded text itself is canonical.
    /// </summary>
    public static string Encode(FleetDesignT design)
    {
        if (design == null)
            return "";
        IReadOnlyList<FleetDataDesignNode> nodes = StableNodeOrder(design);
        if (nodes.Count > MaxNodes)
            throw new InvalidOperationException(
                $"Arena fleet bundle exceeds the {MaxNodes}-ship cap (had {nodes.Count}).");

        var sb = new System.Text.StringBuilder();
        sb.Append(BundleVersion.ToString(CultureInfo.InvariantCulture));
        sb.Append(FieldSeparator);
        sb.Append(Escape(design.Name ?? ""));
        sb.Append(FieldSeparator);
        sb.Append(design.FleetIconIndex.ToString(CultureInfo.InvariantCulture));
        foreach (FleetDataDesignNode node in nodes)
        {
            sb.Append(NodeSeparator);
            AppendField(sb, Escape(node.ShipName ?? ""));
            AppendField(sb, F(node.RelativeFleetOffset.X));
            AppendField(sb, F(node.RelativeFleetOffset.Y));
            AppendField(sb, F(node.VultureWeight));
            AppendField(sb, F(node.AttackShieldedWeight));
            AppendField(sb, F(node.AssistWeight));
            AppendField(sb, F(node.DefenderWeight));
            AppendField(sb, F(node.DPSWeight));
            AppendField(sb, F(node.SizeWeight));
            AppendField(sb, F(node.ArmoredWeight));
            AppendField(sb, ((int)node.CombatState).ToString(CultureInfo.InvariantCulture));
            AppendField(sb, F(node.OrdersRadius), last: true);
        }
        string encoded = sb.ToString();
        if (encoded.Length > MaxEncodedChars)
            throw new InvalidOperationException(
                $"Arena fleet bundle encoding exceeds the {MaxEncodedChars}-char wire cap.");
        return encoded;
    }

    /// <summary>
    /// Decodes an encoded bundle back to a <see cref="FleetDesignT"/>. Returns an empty design for an
    /// empty/malformed string (callers fall back to the name-list column layout on empty).
    /// </summary>
    public static FleetDesignT Decode(string text)
    {
        var design = new FleetDesignT { Name = "", FleetIconIndex = 0 };
        if (string.IsNullOrEmpty(text))
            return design;
        if (text.Length > MaxEncodedChars)
            throw new InvalidOperationException("Arena fleet bundle exceeds the wire cap on decode.");

        string[] records = text.Split(NodeSeparator);
        string[] header = records[0].Split(FieldSeparator);
        if (header.Length >= 3)
        {
            design.Name = Unescape(header[1]);
            int.TryParse(header[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int icon);
            design.FleetIconIndex = icon;
        }
        for (int r = 1; r < records.Length && design.Nodes.Count < MaxNodes; ++r)
        {
            string[] f = records[r].Split(FieldSeparator);
            if (f.Length < 12)
                continue;
            var node = new FleetDataDesignNode
            {
                ShipName = Unescape(f[0]),
                RelativeFleetOffset = new Vector2(P(f[1]), P(f[2])),
                VultureWeight = P(f[3]),
                AttackShieldedWeight = P(f[4]),
                AssistWeight = P(f[5]),
                DefenderWeight = P(f[6]),
                DPSWeight = P(f[7]),
                SizeWeight = P(f[8]),
                ArmoredWeight = P(f[9]),
                CombatState = (CombatState)PI(f[10]),
                OrdersRadius = P(f[11]),
            };
            if ((node.ShipName ?? "").NotEmpty())
                design.Nodes.Add(node);
        }
        return design;
    }

    static void AppendField(System.Text.StringBuilder sb, string value, bool last = false)
    {
        sb.Append(value);
        if (!last)
            sb.Append(FieldSeparator);
    }

    // Round-trip-exact float text: "R" preserves the exact IEEE754 value so Decode reproduces the
    // bit pattern the hash was taken over.
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static float P(string s)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
    static int PI(string s)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    // Ship names cannot contain separators in practice; escape defensively so a stray separator in
    // a name can never corrupt the record framing.
    static string Escape(string s)
        => (s ?? "").Replace("\\", "\\\\").Replace(NodeSeparator.ToString(), "\\r").Replace(FieldSeparator.ToString(), "\\u");
    static string Unescape(string s)
        => (s ?? "").Replace("\\u", FieldSeparator.ToString()).Replace("\\r", NodeSeparator.ToString()).Replace("\\\\", "\\");
}

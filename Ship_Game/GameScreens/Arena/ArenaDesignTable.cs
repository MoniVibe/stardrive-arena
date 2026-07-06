using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SDGraphics;
using SDUtils;
using SDUtils.Deterministic;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
// Disambiguate the ShipDesign TYPE (Ship_Game.Ships.ShipDesign) from the child NAMESPACE
// Ship_Game.GameScreens.ShipDesign, which otherwise shadows it in this namespace.
using ShipDesignT = global::Ship_Game.Ships.ShipDesign;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// Arena custom-fleet EXCHANGE KERNEL (STARDRIVE_ARENA_CUSTOM_FLEET_PROGRAM_PLAN_20260706, Phase 0).
///
/// The <see cref="ArenaFleetBundle"/> stays a lean names+offsets manifest. This static owns the PARALLEL
/// design TABLE: the full custom-design payloads that let a peer reconstruct a design it has never seen,
/// BYTE-IDENTICALLY, and reject any mismatch at the handshake. The load-bearing trick is content-hash-as-name:
/// each custom design is registered under a name DERIVED from its canonical-bytes hash (<c>@arena/&lt;hash16&gt;</c>),
/// so the module-level content folds into the existing <see cref="ArenaFleetBundle.DesignBundleHash"/> (which
/// hashes <c>node.ShipName</c>) with zero new hashing surface — the receiver RE-DERIVES the name from the
/// RECEIVED bytes (never trusts a sender-supplied name), which closes the tamper hole.
///
/// The 7 binding kernel-review amendments are satisfied here:
///  (1) FALSE-GREEN proof — the reconstruction gate of record is a PURE payload round-trip (see the unit tests),
///      not the shared-global digest match; this class exposes <see cref="CanonicalPayload"/> for that gate.
///  (2) The emitter imposes its OWN total ORDINAL order on the module-UID table (never the non-stable
///      HashSet order from ShipDesign.SetDesignSlots, nor the file order from the parse path).
///  (3) ALL floats are forced through InvariantCulture (we emit our own text; we never route FixedUpkeep
///      through the base ShipDesignWriter.Write&lt;T&gt; culture hole).
///  (4) Transient teardown is the caller's job via <see cref="RegisterTransient"/> returning the exact set to
///      undo in a try/finally (see ArenaMultiplayerSession).
///  (5) Parse is exception-SAFE: <see cref="Decode"/> wraps every reconstruction so a crafted/oversized/
///      malformed payload REJECTS cleanly (returns an error) rather than throwing uncaught.
///  (6) Bidirectional: <see cref="Decode"/>/<see cref="RegisterTransient"/> generalize to N peers' tables.
///  (7) Custom/nested CARRIER designs (any non-empty HangarShipUID) are rejected at validation — BaseStrength
///      is impure for carriers (ShipModule.CalculateModuleOffense pulls the hangar ship's live strength).
/// </summary>
public static class ArenaDesignTable
{
    // Bump only on a wire/canonical-layout change to the design payload or container serialization.
    public const int PayloadVersion = 0;
    public const int ContainerVersion = 0;

    // The content-derived registration namespace. Collision-free by construction: it can never shadow a stock
    // or player design, and ResourceManager.Ships.Add would otherwise OVERWRITE a same-named design.
    public const string NamePrefix = "@arena/";

    // Per-design and per-table hard DoS ceilings (base64 chars). A ~32-module ship is a few KB, so these are
    // generous headroom with a hard cap. Enforced on BOTH encode (throw) and decode (reject).
    public const int MaxDesignBase64Chars = 64 * 1024;
    public const int MaxTableChars = 512 * 1024;
    // A crafted payload could claim a huge Modules= count; cap module count independent of byte size so a
    // ushort UID index can never run away before we even reach FromBytes.
    public const int MaxModulesPerDesign = 4096;

    const char DesignSeparator = '';  // RS: separates designs in the container
    const char HeaderSeparator = '';  // US: separates the container header fields

    // ---------------------------------------------------------------------------------------------------
    // (1)/(2)/(3) Canonical single-design payload — our OWN byte-exact serializer.
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// The canonical, byte-exact payload for a single design. Emits ONLY sim-load-bearing fields in a FIXED
    /// order, strips all editor/cosmetic metadata, imposes a self-contained ORDINAL module-UID order, and
    /// forces InvariantCulture on every number. Two functionally-identical designs authored in different drag
    /// orders (or one authored live vs one parsed from file) produce BYTE-IDENTICAL payloads.
    ///
    /// There is deliberately NO <c>Name=</c> line: the design identity IS its content hash, and the
    /// registration name (<c>@arena/&lt;hash&gt;</c>) is RE-DERIVED from these bytes at registration time. With no
    /// name on the wire there is no sender-supplied name to accidentally trust, so amendment-1's tamper-close
    /// invariant is structural, and the hash is a pure function of content with no name/no-name duality.
    /// (There is also no <c>FixedUpkeep</c>: it is the only culture-sensitive float in the base format and is
    /// irrelevant to the arena sim — cost is BaseStrength — so dropping it deletes the culture hazard entirely.)
    /// </summary>
    public static byte[] CanonicalPayload(IShipDesign design)
    {
        if (design == null)
            throw new ArgumentNullException(nameof(design));

        DesignSlot[] slots = SortedSlots(design);
        string[] moduleUIDs = OrdinalModuleUIDs(slots);
        var uidIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < moduleUIDs.Length; ++i)
            uidIndex[moduleUIDs[i]] = i;

        var sb = new StringBuilder(256 + slots.Length * 24);
        WriteKV(sb, "Version", PayloadVersion.ToString(CultureInfo.InvariantCulture));
        WriteKV(sb, "Hull", design.Hull ?? "");
        WriteKV(sb, "ModName", design.ModName ?? "");
        WriteKV(sb, "Role", design.Role.ToString());
        WriteKV(sb, "ShipCategory", design.ShipCategory.ToString());
        WriteKV(sb, "HangarDesignation", design.HangarDesignation.ToString());
        // GridInfo (Size + Center) is load-bearing for slot-grid RECONSTRUCTION (ModuleGridFlyweight sizes its
        // index grid from Size; omitting it IndexOutOfRanges FromBytes for any ship with modules). It is inert
        // for BaseStrength (SurfaceArea comes from the hull), but mandatory for reconstruction — emit as ints.
        WriteKV(sb, "Size", $"{design.GridInfo.Size.X.ToString(CultureInfo.InvariantCulture)},{design.GridInfo.Size.Y.ToString(CultureInfo.InvariantCulture)}");
        WriteKV(sb, "GridCenter", $"{design.GridInfo.Center.X.ToString(CultureInfo.InvariantCulture)},{design.GridInfo.Center.Y.ToString(CultureInfo.InvariantCulture)}");
        WriteKV(sb, "IsShipyard", BoolStr(design.IsShipyard));
        WriteKV(sb, "IsOrbitalDefense", BoolStr(design.IsOrbitalDefense));
        WriteKV(sb, "IsCarrierOnly", BoolStr(design.IsCarrierOnly));
        WriteKV(sb, "EventOnDeath", design.EventOnDeath ?? "");
        // FixedCost/FixedUpkeep are deliberately omitted: they are economy fields irrelevant to the arena sim
        // (cost = BaseStrength), FixedUpkeep is the only culture-sensitive float, and neither is exposed on
        // IShipDesign. Dropping them shrinks the payload and deletes the culture hazard.

        WriteKV(sb, "ModuleUIDs", string.Join(";", moduleUIDs));
        WriteKV(sb, "Modules", slots.Length.ToString(CultureInfo.InvariantCulture));
        foreach (DesignSlot slot in slots)
            WriteSlotLine(sb, slot, (ushort)uidIndex[slot.ModuleUID]);

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Content hash of a design: DetHash over the canonical payload, as "0x"+16 hex.
    /// This IS the design identity; two byte-identical payloads hash identically (dedup + tamper-detect).
    /// </summary>
    public static string DesignContentHash(IShipDesign design)
        => DesignContentHash(CanonicalPayload(design));

    public static string DesignContentHash(byte[] canonicalBytes)
    {
        var h = DetHash.New();
        // Hash the raw bytes so the hash is a pure function of the canonical UTF-8 payload.
        for (int i = 0; i < canonicalBytes.Length; ++i)
            h.AddByte(canonicalBytes[i]);
        return "0x" + h.Value.ToString("X16", CultureInfo.InvariantCulture);
    }

    /// <summary>The @arena/&lt;hash16&gt; registration name derived from a design's content hash.</summary>
    public static string ContentName(IShipDesign design) => ContentName(DesignContentHash(design));

    public static string ContentName(string contentHash)
    {
        // contentHash is "0x" + 16 hex; take the 16 hex chars (drop the "0x") for a compact, collision-free id.
        string hex = contentHash.StartsWith("0x", StringComparison.Ordinal) ? contentHash.Substring(2) : contentHash;
        return NamePrefix + hex;
    }

    // The one canonical slot ordering (amendment 2): scanline (Y then X) via DesignSlot.Sorter, tie-broken by
    // the ORDINAL module UID so a ship with two modules at the identical grid cell is still totally ordered.
    static DesignSlot[] SortedSlots(IShipDesign design)
    {
        DesignSlot[] src = design.GetOrLoadDesignSlots();
        var slots = new DesignSlot[src.Length];
        Array.Copy(src, slots, src.Length);
        Array.Sort(slots, (a, b) =>
        {
            int c = DesignSlot.Sorter(a, b);
            if (c != 0) return c;
            return string.CompareOrdinal(a.ModuleUID ?? "", b.ModuleUID ?? "");
        });
        return slots;
    }

    // The self-contained total UID order (amendment 2): ordinal sort of the unique UIDs, matching
    // Authoritative4XSession.cs:1531. Never trust ShipDesign.UniqueModuleUIDs (HashSet order in the authoring
    // path) nor SlotModuleUIDMapping (file order in the parse path).
    static string[] OrdinalModuleUIDs(DesignSlot[] slots)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (DesignSlot s in slots)
            set.Add(s.ModuleUID ?? "");
        return set.ToArray();
    }

    static void WriteKV(StringBuilder sb, string key, string value)
    {
        sb.Append(key).Append('=').Append(value ?? "").Append('\n');
    }

    static string BoolStr(bool b) => b ? "True" : "False";

    // Mirror the base WriteDesignSlotString wire shape so FromBytes' ParseDesignSlot reads it back exactly:
    // gridX,gridY;moduleUIDIndex[;sizeX,sizeY[;turretAngle[;moduleRot[;hangarShipUID]]]]
    // Trailing optional fields are omitted when default, but we ALWAYS emit them deterministically the same way.
    static void WriteSlotLine(StringBuilder sb, DesignSlot slot, ushort moduleIdx)
    {
        Point gp = slot.Pos;
        Point sz = slot.Size;
        int ta = slot.TurretAngle;
        int mr = (int)slot.ModuleRot;
        bool gotSize = sz.X != 1 || sz.Y != 1;

        int lastValid = 0;
        if (slot.HangarShipUID.NotEmpty()) lastValid = 4;
        else if (mr != 0) lastValid = 3;
        else if (ta != 0) lastValid = 2;
        else if (gotSize) lastValid = 1;

        sb.Append(gp.X.ToString(CultureInfo.InvariantCulture)).Append(',')
          .Append(gp.Y.ToString(CultureInfo.InvariantCulture));
        sb.Append(';').Append(moduleIdx.ToString(CultureInfo.InvariantCulture));
        if (lastValid >= 1)
        {
            sb.Append(';');
            if (gotSize) sb.Append(sz.X.ToString(CultureInfo.InvariantCulture)).Append(',').Append(sz.Y.ToString(CultureInfo.InvariantCulture));
        }
        if (lastValid >= 2)
        {
            sb.Append(';');
            if (ta != 0) sb.Append(ta.ToString(CultureInfo.InvariantCulture));
        }
        if (lastValid >= 3)
        {
            sb.Append(';');
            if (mr != 0) sb.Append(mr.ToString(CultureInfo.InvariantCulture));
        }
        if (lastValid >= 4)
        {
            sb.Append(';').Append(slot.HangarShipUID);
        }
        sb.Append('\n');
    }

    // ---------------------------------------------------------------------------------------------------
    // (4) Content availability + carrier rejection (amendment 7) — pre-registration validation.
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Verifies a design can be reconstructed and priced deterministically on THIS machine, and rejects the
    /// carrier case (amendment 7). Returns "" when acceptable, else a precise handshake error. Never throws.
    /// </summary>
    public static string ValidateContentAvailable(IShipDesign design)
    {
        if (design == null)
            return "custom design payload was null.";
        if (design.Role == RoleName.disabled)
            return "custom design decoded as disabled (mod/content gap).";
        if ((design.Hull ?? "").IsEmpty() || !ResourceManager.Hull(design.Hull, out _))
            return $"custom design hull '{design.Hull}' is not available on this machine.";

        DesignSlot[] slots;
        try { slots = design.GetOrLoadDesignSlots(); }
        catch (Exception e) { return $"custom design slots failed to load: {e.Message}"; }
        if (slots == null || slots.Length == 0)
            return "custom design has no modules.";
        if (slots.Length > MaxModulesPerDesign)
            return $"custom design module count {slots.Length} exceeds cap {MaxModulesPerDesign}.";

        foreach (DesignSlot slot in slots)
        {
            if ((slot.ModuleUID ?? "").IsEmpty() || !ResourceManager.GetModuleTemplate(slot.ModuleUID, out _))
                return $"custom design module '{slot.ModuleUID}' is not available on this machine.";
            // Amendment 7: reject ANY hangar reference for v0 — carrier BaseStrength is impure
            // (ShipModule.CalculateModuleOffense pulls the referenced ship's live strength / +100 fallback),
            // so it is deterministic only if the referenced ship loads identically on both peers. Defer.
            if (slot.HangarShipUID.NotEmpty() && slot.HangarShipUID != "NotApplicable")
                return $"custom CARRIER designs are not supported in this build (hangar '{slot.HangarShipUID}').";
        }
        return "";
    }

    // ---------------------------------------------------------------------------------------------------
    // (3)/(5)/(6) Multi-design container — size-capped, append-tolerant, exception-safe decode.
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Encodes a set of designs into the wire container: each design's STORABLE canonical payload (base64),
    /// separated by RS, prefixed by a US-delimited header (version, count). Throws on any cap overflow.
    /// Designs are emitted in ORDINAL content-name order so the container itself is order-independent.
    /// </summary>
    public static string Encode(IReadOnlyList<IShipDesign> designs)
    {
        if (designs == null || designs.Count == 0)
            return "";

        // Dedup by content name (identical designs collapse) and impose a stable ordinal order.
        var byName = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (IShipDesign d in designs)
        {
            if (d == null) continue;
            byte[] payload = CanonicalPayload(d);
            string b64 = Convert.ToBase64String(payload, Base64FormattingOptions.None);
            if (b64.Length > MaxDesignBase64Chars)
                throw new InvalidOperationException(
                    $"Arena custom design payload exceeds the {MaxDesignBase64Chars}-char per-design cap.");
            byName[ContentName(d)] = b64;
        }
        if (byName.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.Append(ContainerVersion.ToString(CultureInfo.InvariantCulture));
        sb.Append(HeaderSeparator);
        sb.Append(byName.Count.ToString(CultureInfo.InvariantCulture));
        foreach (KeyValuePair<string, string> kv in byName)
        {
            sb.Append(DesignSeparator);
            sb.Append(kv.Value);
        }
        string text = sb.ToString();
        if (text.Length > MaxTableChars)
            throw new InvalidOperationException(
                $"Arena custom design table exceeds the {MaxTableChars}-char wire cap.");
        return text;
    }

    /// <summary>Result of decoding + reconstructing a design table. Never throws for malformed input.</summary>
    public sealed class DecodeResult
    {
        // Successfully reconstructed designs, keyed by their RE-DERIVED @arena/ content name (never the
        // sender-supplied name — amendment 1 tamper-close).
        public readonly Dictionary<string, ShipDesignT> Designs = new(StringComparer.Ordinal);
        // Non-empty => the whole table is rejected at the handshake with this precise error.
        public string Error = "";
        public bool Ok => Error.IsEmpty();
    }

    /// <summary>
    /// Decodes the container and reconstructs every design from BYTES, RE-DERIVING each name from the received
    /// bytes (never trusting a sender-supplied name). ANY malformed/oversized/carrier/mod-gap payload rejects
    /// cleanly via <see cref="DecodeResult.Error"/> — this method never throws for hostile input (amendment 5).
    /// </summary>
    public static DecodeResult Decode(string text)
    {
        var result = new DecodeResult();
        if (text.IsEmpty())
            return result; // empty table is legal (a match with no custom designs)

        if (text.Length > MaxTableChars)
        {
            result.Error = $"custom design table exceeds the {MaxTableChars}-char wire cap.";
            return result;
        }

        string[] records = text.Split(DesignSeparator);
        // records[0] is the US-delimited header; records[1..] are base64 design payloads.
        for (int r = 1; r < records.Length; ++r)
        {
            string b64 = records[r];
            if (b64.IsEmpty())
                continue;
            if (b64.Length > MaxDesignBase64Chars)
            {
                result.Error = $"custom design payload exceeds the {MaxDesignBase64Chars}-char per-design cap.";
                return result;
            }

            byte[] bytes;
            try { bytes = Convert.FromBase64String(b64); }
            catch (Exception) { result.Error = "malformed custom design payload (base64)."; return result; }

            // Guard the module-count claim BEFORE FromBytes so a crafted Modules= near the byte cap, or a
            // ushort UID index that would IndexOutOfRange in ParseDesignSlot, can never throw uncaught.
            string guard = GuardModuleCount(bytes);
            if (guard.NotEmpty()) { result.Error = guard; return result; }

            ShipDesignT design;
            try { design = ShipDesignT.FromBytes(bytes); }
            catch (Exception e) { result.Error = $"malformed custom design payload: {e.Message}"; return result; }
            if (design == null)
            {
                result.Error = "custom design payload failed to reconstruct (hull/mod gap).";
                return result;
            }

            string availErr = ValidateContentAvailable(design);
            if (availErr.NotEmpty()) { result.Error = availErr; return result; }

            // RE-DERIVE the name from what we ACTUALLY reconstructed (the canonical form of the decoded design),
            // never a sender-supplied name — the payload carries no Name line, so this is the only name source.
            // A tampered payload reconstructs to a different canonical form -> a different @arena/ name -> the
            // bundle's referenced name fails to resolve at the handshake (amendment 1 tamper-close).
            string name = ContentName(design);
            design.Name = name;
            result.Designs[name] = design;
        }
        return result;
    }

    // Peek the "Modules=" and "ModuleUIDs=" header lines and reject a runaway/crafted count before FromBytes.
    static string GuardModuleCount(byte[] bytes)
    {
        string text;
        try { text = Encoding.UTF8.GetString(bytes); }
        catch (Exception) { return "malformed custom design payload (utf8)."; }

        int modules = -1;
        int uidCount = -1;
        using var reader = new System.IO.StringReader(text);
        for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
        {
            if (line.StartsWith("Modules=", StringComparison.Ordinal))
            {
                if (!int.TryParse(line.Substring("Modules=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out modules))
                    return "malformed custom design payload (Modules count).";
            }
            else if (line.StartsWith("ModuleUIDs=", StringComparison.Ordinal))
            {
                string list = line.Substring("ModuleUIDs=".Length);
                uidCount = list.IsEmpty() ? 0 : list.Split(';').Length;
            }
            if (modules >= 0 && uidCount >= 0)
                break;
        }
        if (modules > MaxModulesPerDesign)
            return $"custom design module count {modules} exceeds cap {MaxModulesPerDesign}.";
        if (uidCount > MaxModulesPerDesign)
            return $"custom design UID table size {uidCount} exceeds cap {MaxModulesPerDesign}.";
        return "";
    }

    // ---------------------------------------------------------------------------------------------------
    // (4)/(6) Transient registration + teardown — caller wraps in try/finally on every exit path.
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Registers each reconstructed design under its RE-DERIVED @arena/ name via
    /// ResourceManager.AddShipTemplate(playerDesign:false, readOnly:true) — transient, in-memory, never
    /// written to disk, never in the player's Saved Designs enumeration, collision-free namespace.
    /// Returns the EXACT set of names this call registered (idempotent dedup handled by ShipsManager.Add) so
    /// the caller can undo precisely in a finally — NEVER blanket-delete @arena/* (a concurrent match may share it).
    /// </summary>
    public static IReadOnlyList<string> RegisterTransient(IEnumerable<ShipDesignT> designs)
    {
        var registered = new List<string>();
        if (designs == null)
            return registered;
        foreach (ShipDesignT d in designs)
        {
            if (d == null) continue;
            // Defensive: the name MUST be an @arena/ content name (Decode guarantees this).
            if (!(d.Name ?? "").StartsWith(NamePrefix, StringComparison.Ordinal))
                continue;
            if (ResourceManager.AddShipTemplate(d, playerDesign: false, readOnly: true))
                registered.Add(d.Name);
        }
        return registered;
    }

    /// <summary>Undoes exactly the set returned by <see cref="RegisterTransient"/>. Safe to call on partial/empty sets.</summary>
    public static void UnregisterTransient(IReadOnlyList<string> registeredNames)
    {
        if (registeredNames == null)
            return;
        foreach (string name in registeredNames)
        {
            if ((name ?? "").StartsWith(NamePrefix, StringComparison.Ordinal))
                ResourceManager.Ships.Delete(name);
        }
    }
}

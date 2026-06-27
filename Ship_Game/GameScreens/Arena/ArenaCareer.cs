using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SDUtils;
using Ship_Game.Data.Serialization;
using Ship_Game.Data.Yaml;
using Ship_Game.Data.YamlSerializer;
using Ship_Game.Ships;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// PHASE A — PERSISTENCE FOUNDATION for the arena gladiator CAREER.
///
/// An arena CAREER survives a restart: the run banks its <see cref="Cash"/>, its
/// owned vessels (with persisted VETERANCY — Experience/Level/Kills), the foreign
/// chassis styles it unlocked, and how many wins it has (<see cref="CareerLevel"/>)
/// to disk via the proven [StarData] YAML system (mirrors RaceSave.cs). Next session
/// the run reloads them and re-applies: starting cash, re-granted chassis, the veteran
/// gladiator (re-spawned from its DesignName with its earned veterancy re-applied), and
/// a difficulty that scales with the career's win count.
///
/// IMPORTANT: we DO NOT persist live <see cref="Ship_Game.Ships.Ship"/> objects — they
/// are huge, engine-bound, and not round-trippable as a stable record. Instead we persist
/// VALUE records (<see cref="OwnedVessel"/>: DesignName + Experience/Level/Kills) and
/// re-spawn from the DesignName + re-apply the veterancy after spawn. This is the same
/// "persist values, re-spawn" discipline RaceSave uses for races.
///
/// Saved to {AppData}/StarDrive/Arena Career/career.yaml (a new "/Arena Career/" subdir
/// mirroring the "/Saved Races/" convention). See <see cref="CareerManager"/>.
/// </summary>
[StarDataType]
public sealed class ArenaCareer
{
    // Save-format version so a future field change can migrate / reject an old file.
    public const int CurrentVersion = 1;

    [StarData] public int Version = CurrentVersion;

    // The banked run currency. Re-applied as the run's STARTING cash on reload.
    [StarData] public int Cash;

    // How many runs/wins the career has banked. Drives the persisted difficulty scaling
    // (the more you've won, the harder the next run starts). 0 == fresh career == today's
    // default run.
    [StarData] public int CareerLevel;

    // Public renown in the Star Gladiator circuit. Fame grows the fight-option slate,
    // unlocks special contract rows, and scales rewards. It is independent from
    // CareerLevel so a player can be popular without silently advancing field tier.
    [StarData] public int Fame;

    // The persisted owned roster — VALUE records (DesignName + veterancy), NOT live ships.
    // Re-spawned + re-veteraned on reload. Never null (an empty career has an empty array).
    [StarData] public OwnedVessel[] OwnedVessels = Empty<OwnedVessel>.Array;

    // CAPTAINS — persistent pilot identities assigned to owned vessels. Old saves may not
    // have them; normalization synthesizes stable captains for owned vessels that lack one.
    [StarData] public ArenaCaptain[] Captains = Empty<ArenaCaptain>.Array;

    // The foreign chassis faction STYLES the career has unlocked (e.g. "Kulrathi").
    // Re-granted (UnlockEmpireHull) to the player empire on reload so the veteran keeps
    // access to the hulls it earned. Never null.
    [StarData] public string[] UnlockedChassisStyles = Empty<string>.Array;

    // PHASE B — OWNED INVENTORY: the VesselId of the OWNED vessel that is the ACTIVE
    // gladiator (the one the next run fields). The GARAGE sets this when the player picks a
    // different owned vessel; the DEALERSHIP can set it on a fresh buy. Empty/unmatched falls
    // back to the first owned vessel (so the gladiator is ALWAYS a finite owned vessel). Empty
    // for a fresh career until a starter vessel is granted.
    [StarData] public string ActiveVesselId = "";

    // BOSS PERKS — persistent career modifiers granted from tier-3 boss victories. Value ids,
    // never null; duplicates are allowed because perks stack.
    [StarData] public string[] Perks = Empty<string>.Array;

    // RESEARCHED MODULES — permanent module research/unlocks bought in the Arena shop or
    // migrated from old finite-salvage saves. This is a SET of module UIDs, never finite counts.
    [StarData] public string[] ResearchedModules = Empty<string>.Array;

    // LEGACY MIGRATION INPUT: old saves persisted finite Salvage UID->count records. Keep this
    // [StarData] field only so those UIDs can be migrated into ResearchedModules on load/save.
    // Live gameplay should not add to or consume from it; NormalizeForPersistence clears it.
    [StarData] public SalvageRecord[] Salvage = Empty<SalvageRecord>.Array;

    // FLEET COMPOSITION — the VesselIds of the OWNED vessels the player has put in the ACTIVE
    // FLEET (the multi-ship squad the next run FIELDS). Persisted. The ACTIVE vessel
    // (ActiveVesselId) is the FLAGSHIP and is always part of the fleet. Empty/fresh => just the
    // active (or first) owned vessel, so a fresh career fields EXACTLY ONE ship (no regression).
    // Capped at <see cref="MaxFleetSize"/>. Re-spawned each round (each vessel by its
    // DesignName, with its persisted veterancy re-applied), arranged in a formation. The FLEET
    // popup (ArenaFleetScreen) toggles vessels in/out of this list. Never null.
    [StarData] public string[] FleetVesselIds = Empty<string>.Array;

    // RIVAL DOSSIERS — stable seed for deterministic contender identities, plus a retired
    // ledger so retired rivals keep their identity after rookie replacement.
    [StarData] public string RivalIdentitySeed = ArenaRivalDossiers.DefaultCareerSeed;
    [StarData] public ContenderRecord[] RetiredContenders = Empty<ContenderRecord>.Array;

    // The base number of owned vessels that can fight together in one fleet. Meta upgrades
    // can raise the effective cap, bounded by ArenaPerks.MaxFleetSizeCap.
    public const int ArenaMaxFleetSize = 10;

    public int MaxFleetSize => ArenaPerks.MaxFleetSize(ArenaMaxFleetSize, Perks);

    // PHASE C — CONTENDER LADDER: persistent AI gladiators that fight each other between
    // player bouts and can later be challenged from the hub. This is value data only:
    // each record names a real ship design plus its ladder rating and W/L history. Never null.
    [StarData] public ContenderRecord[] Contenders = Empty<ContenderRecord>.Array;

    // TEAM LEAGUES — persistent groupings of contender names. The league bracket is derived
    // from Members.Length, so 1v1 through 5v5 can coexist without a separate schema field.
    [StarData] public ArenaTeam[] Teams = Empty<ArenaTeam>.Array;

    // PERMADEATH SLIDER — chance [0..1] that a downed loser in a team duel is permanently
    // replaced by a rookie. Zero preserves current behavior for fresh/old careers.
    [StarData] public float PermadeathChance;

    // LOSS CONSEQUENCE — default false keeps the career open-ended: a lost bout banks
    // progress and returns to the hub instead of ending the run. Future hardcore modes can
    // opt into the old hard defeat screen.
    [StarData] public bool HardLossEndsRun;

    // PLAYER-FLEET STAKES — default ON: a fielded owned vessel that is destroyed in a live
    // arena bout is removed from the garage permanently. Losses can still keep the career
    // open-ended via HardLossEndsRun=false; this controls ship ownership, not career end.
    [StarData] public bool PlayerShipsPermadeath = true;

    // BETTING — an open wager on the exact queued next fight option. The stake is deducted
    // immediately; resolving the chosen bout clears this slip and pays only on a win.
    [StarData] public ArenaBetSlip PendingBet;

    // MEMORY LEDGERS — append-only deterministic career memory. Chronicle records notable
    // career events; memorials preserve permanently lost owned ships.
    [StarData] public ArenaChronicleEvent[] Chronicle = Empty<ArenaChronicleEvent>.Array;
    [StarData] public ArenaMemorialRecord[] Memorials = Empty<ArenaMemorialRecord>.Array;

    [StarDataConstructor] public ArenaCareer() { }

    /// <summary>True for a brand-new career (no progress) — behaves like today's default run.</summary>
    public bool IsFresh => CareerLevel == 0 && Fame == 0 && (OwnedVessels == null || OwnedVessels.Length == 0);

    /// <summary>
    /// The persisted gladiator the player returns AS: the ACTIVE owned vessel (the one the
    /// GARAGE selected via <see cref="ActiveVesselId"/>), falling back to the FIRST owned
    /// vessel when no/unknown active id is set, or null for a fresh career with no vessels
    /// (then the run auto-picks the default). PHASE B makes the gladiator a FINITE owned
    /// vessel: the run fields what you OWN, not the unlimited auto-pick.
    /// </summary>
    public OwnedVessel Gladiator => ActiveVessel ?? FirstVessel;

    /// <summary>The first owned vessel (or null), used as the fallback gladiator.</summary>
    public OwnedVessel FirstVessel
        => OwnedVessels != null && OwnedVessels.Length > 0 ? OwnedVessels[0] : null;

    /// <summary>
    /// The owned vessel matching <see cref="ActiveVesselId"/>, or null when no id is set or it
    /// doesn't match any owned vessel (then <see cref="Gladiator"/> falls back to the first).
    /// </summary>
    public OwnedVessel ActiveVessel
    {
        get
        {
            if (ActiveVesselId.IsEmpty() || OwnedVessels == null)
                return null;
            foreach (OwnedVessel v in OwnedVessels)
                if (v != null && string.Equals(v.VesselId, ActiveVesselId, StringComparison.Ordinal))
                    return v;
            return null;
        }
    }

    /// <summary>
    /// Set the ACTIVE gladiator to the owned vessel with this id (the GARAGE "select" action).
    /// No-op (returns false) if no owned vessel has the id, so the active gladiator is always a
    /// vessel the career actually owns.
    /// </summary>
    public bool SetActiveVessel(string vesselId)
    {
        if (vesselId.IsEmpty() || OwnedVessels == null)
            return false;
        foreach (OwnedVessel v in OwnedVessels)
            if (v != null && string.Equals(v.VesselId, vesselId, StringComparison.Ordinal))
            {
                ActiveVesselId = vesselId;
                return true;
            }
        return false;
    }

    /// <summary>
    /// Normalize data that comes from disk or from a partially constructed test career. This
    /// keeps malformed current-version saves from leaking null vessels, duplicate ids, stale
    /// active ids, or stale fleet ids into spawn/banking code.
    /// </summary>
    public void NormalizeForPersistence()
    {
        UnlockedChassisStyles ??= Empty<string>.Array;
        Captains ??= Empty<ArenaCaptain>.Array;
        Contenders ??= Empty<ContenderRecord>.Array;
        Teams ??= Empty<ArenaTeam>.Array;
        if (Fame < 0) Fame = 0;
        Perks = ArenaPerks.Normalize(Perks);
        ResearchedModules = NormalizeResearchedModules(ResearchedModules, Salvage);
        Salvage = Empty<SalvageRecord>.Array;
        PendingBet = ArenaBetting.NormalizePendingBet(PendingBet);
        Chronicle = NormalizeChronicle(Chronicle);
        Memorials = NormalizeMemorials(Memorials);
        PermadeathChance = Math.Clamp(PermadeathChance, 0f, 1f);
        ActiveVesselId ??= "";
        RivalIdentitySeed = ArenaRivalDossiers.NormalizeCareerSeed(RivalIdentitySeed);
        NormalizeContenders();
        RetiredContenders = ArenaRivalDossiers.NormalizeRetired(RetiredContenders, RivalIdentitySeed);
        NormalizeTeams();

        if (OwnedVessels == null || OwnedVessels.Length == 0)
        {
            OwnedVessels = Empty<OwnedVessel>.Array;
            ActiveVesselId = "";
            FleetVesselIds = Empty<string>.Array;
            return;
        }

        var owned = new List<OwnedVessel>(OwnedVessels.Length);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (OwnedVessel v in OwnedVessels)
        {
            if (v == null || v.DesignName.IsEmpty())
                continue;

            string preferredId = v.VesselId.NotEmpty() ? v.VesselId : v.DesignName;
            v.VesselId = UniqueLoadedVesselId(preferredId, seenIds);
            seenIds.Add(v.VesselId);
            v.Name ??= "";
            v.CaptainId ??= "";
            if (v.Experience < 0f) v.Experience = 0f;
            if (v.Level < 0) v.Level = 0;
            if (v.Kills < 0) v.Kills = 0;
            v.CurrentHullHealth = Math.Max(0f, v.CurrentHullHealth);
            v.MaxHullHealth = Math.Max(0f, v.MaxHullHealth);
            if (v.MaxHullHealth > 0f && v.CurrentHullHealth >= v.MaxHullHealth - 0.5f)
                v.CurrentHullHealth = 0f; // zero means fully repaired/unscarred
            v.DestroyedModules = NormalizeDestroyedModules(v.DestroyedModules);
            v.ModuleOverrides = NormalizeModuleOverrides(v.ModuleOverrides);
            owned.Add(v);
        }

        OwnedVessels = owned.Count > 0 ? owned.ToArray() : Empty<OwnedVessel>.Array;
        if (OwnedVessels.Length == 0)
        {
            ActiveVesselId = "";
            FleetVesselIds = Empty<string>.Array;
            Captains = NormalizeCaptains(Captains);
            return;
        }

        Captains = NormalizeCaptains(Captains);
        EnsureCaptainsForOwnedVessels();

        if (ActiveVesselId.IsEmpty() || FindOwnedVessel(ActiveVesselId) == null)
            ActiveVesselId = OwnedVessels[0].VesselId;

        NormalizeFleetIds();
    }

    public bool AddChronicleEvent(string kind, string subject, string summary, ulong fightSeed = 0)
    {
        kind = CleanLedgerText(kind, 40);
        subject = CleanLedgerText(subject, 80);
        summary = CleanLedgerText(summary, 180);
        if (kind.IsEmpty())
            return false;

        string seedText = LedgerSeed(fightSeed);
        string id = LedgerId("evt", kind, subject, seedText);
        Chronicle = NormalizeChronicle(Chronicle);
        if (Chronicle.Any(e => e != null && string.Equals(e.EventId, id, StringComparison.Ordinal)))
            return false;

        int order = Chronicle.Length == 0 ? 1 : Chronicle.Max(e => e?.Order ?? 0) + 1;
        var list = Chronicle.ToList();
        list.Add(new ArenaChronicleEvent(id, order, kind, subject, summary, fightSeed));
        Chronicle = NormalizeChronicle(list.ToArray());
        return true;
    }

    public bool AddMemorial(OwnedVessel vessel, string killer, string cause, ulong fightSeed = 0)
    {
        if (vessel == null || vessel.VesselId.IsEmpty())
            return false;

        string seedText = LedgerSeed(fightSeed);
        string id = LedgerId("mem", vessel.VesselId, cause, seedText);
        Memorials = NormalizeMemorials(Memorials);
        if (Memorials.Any(m => m != null && string.Equals(m.MemorialId, id, StringComparison.Ordinal)))
            return false;

        int order = Memorials.Length == 0 ? 1 : Memorials.Max(m => m?.Order ?? 0) + 1;
        var list = Memorials.ToList();
        list.Add(new ArenaMemorialRecord(id, order, vessel.VesselId, vessel.Name, vessel.DesignName,
            vessel.Kills, vessel.Level, killer, cause, fightSeed, "Ship", vessel.CaptainId));
        Memorials = NormalizeMemorials(list.ToArray());
        AddChronicleEvent("ship_destroyed", vessel.VesselId,
            $"{DisplayVesselName(vessel)} was lost: {CleanLedgerText(cause, 80)}", fightSeed);
        PromoteNemesisFromMemorial(vessel, killer, fightSeed);
        return true;
    }

    public bool MarkCaptainSurvivedHullLoss(OwnedVessel vessel, string killer, string cause, ulong fightSeed = 0)
    {
        ArenaCaptain captain = CaptainForVessel(vessel);
        if (captain == null || !captain.Alive)
            return false;

        captain.SurvivedHullLosses += 1;
        AddChronicleEvent("captain_ejected", captain.CaptainId,
            $"{captain.Name} ejected from {DisplayVesselName(vessel)} after {CleanLedgerText(cause, 80)}.",
            fightSeed);
        PromoteNemesisFromMemorial(vessel, killer, fightSeed);
        return true;
    }

    public bool AddCaptainDeathMemorial(OwnedVessel vessel, string killer, string cause, ulong fightSeed = 0)
    {
        ArenaCaptain captain = CaptainForVessel(vessel);
        if (captain == null || !captain.Alive)
            return false;

        captain.Alive = false;
        captain.DeathCause = CleanLedgerText(cause, 120);
        captain.Killer = CleanLedgerText(killer, 80);
        string id = LedgerId("cap", captain.CaptainId, captain.DeathCause, LedgerSeed(fightSeed));
        Memorials = NormalizeMemorials(Memorials);
        if (Memorials.Any(m => m != null && string.Equals(m.MemorialId, id, StringComparison.Ordinal)))
            return false;

        int order = Memorials.Length == 0 ? 1 : Memorials.Max(m => m?.Order ?? 0) + 1;
        var list = Memorials.ToList();
        list.Add(new ArenaMemorialRecord(id, order, vessel?.VesselId ?? "", captain.Name, vessel?.DesignName ?? "",
            captain.Kills, captain.Level, killer, cause, fightSeed, "Captain", captain.CaptainId));
        Memorials = NormalizeMemorials(list.ToArray());
        AddChronicleEvent("captain_killed", captain.CaptainId,
            $"{captain.Name} was killed: {captain.DeathCause}.", fightSeed);
        PromoteNemesisFromMemorial(vessel, killer, fightSeed);
        return true;
    }

    public ArenaCaptain CaptainForVessel(OwnedVessel vessel)
    {
        if (vessel == null || vessel.CaptainId.IsEmpty())
            return null;
        foreach (ArenaCaptain captain in Captains ?? Empty<ArenaCaptain>.Array)
            if (captain != null && string.Equals(captain.CaptainId, vessel.CaptainId, StringComparison.Ordinal))
                return captain;
        return null;
    }

    public ContenderRecord[] PinnedNemeses()
        => (Contenders ?? Empty<ContenderRecord>.Array)
            .Where(c => c != null && c.NemesisOfVesselId.NotEmpty())
            .OrderByDescending(c => c.Grudge)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToArray();

    public bool ClearNemesis(string contenderNameOrId, string vesselId, ulong fightSeed = 0)
    {
        ContenderRecord contender = FindContender(contenderNameOrId);
        if (contender == null || contender.NemesisOfVesselId.IsEmpty())
            return false;
        if (vesselId.NotEmpty() && !string.Equals(contender.NemesisOfVesselId, vesselId, StringComparison.Ordinal))
            return false;

        string oldVessel = contender.NemesisOfVesselId;
        contender.NemesisOfVesselId = "";
        contender.Grudge = 0;
        AddChronicleEvent("nemesis_cleared", contender.Name,
            $"{contender.Name}'s grudge against {oldVessel} was settled.", fightSeed);
        return true;
    }

    void PromoteNemesisFromMemorial(OwnedVessel vessel, string killer, ulong fightSeed)
    {
        if (vessel == null || killer.IsEmpty())
            return;
        ContenderRecord contender = FindContender(killer);
        if (contender == null)
            return;

        contender.NemesisOfVesselId = vessel.VesselId ?? "";
        contender.Grudge = Math.Max(1, contender.Grudge + 1);
        AddChronicleEvent("nemesis", contender.Name,
            $"{contender.Name} became a nemesis after destroying {DisplayVesselName(vessel)}.",
            fightSeed);
    }

    ContenderRecord FindContender(string nameOrId)
    {
        if (nameOrId.IsEmpty())
            return null;
        foreach (ContenderRecord c in Contenders ?? Empty<ContenderRecord>.Array)
        {
            if (c == null)
                continue;
            if (string.Equals(c.Name, nameOrId, StringComparison.Ordinal)
                || string.Equals(c.ContenderId, nameOrId, StringComparison.Ordinal)
                || string.Equals(c.DesignName, nameOrId, StringComparison.Ordinal))
                return c;
        }
        return null;
    }

    static string DisplayVesselName(OwnedVessel vessel)
        => vessel?.Name.NotEmpty() == true ? vessel.Name : vessel?.DesignName ?? "";

    public static ArenaChronicleEvent[] NormalizeChronicle(ArenaChronicleEvent[] events)
    {
        if (events == null || events.Length == 0)
            return Empty<ArenaChronicleEvent>.Array;

        var byId = new Dictionary<string, ArenaChronicleEvent>(StringComparer.Ordinal);
        int order = 1;
        foreach (ArenaChronicleEvent e in events)
        {
            if (e == null)
                continue;
            e.Kind = CleanLedgerText(e.Kind, 40);
            e.Subject = CleanLedgerText(e.Subject, 80);
            e.Summary = CleanLedgerText(e.Summary, 180);
            e.FightSeed = CleanLedgerText(e.FightSeed, 32);
            if (e.FightSeed.IsEmpty())
                e.FightSeed = LedgerSeed(0);
            if (e.EventId.IsEmpty())
                e.EventId = LedgerId("evt", e.Kind, e.Subject, e.FightSeed);
            if (e.Kind.IsEmpty() || byId.ContainsKey(e.EventId))
                continue;
            if (e.Order <= 0)
                e.Order = order;
            order = Math.Max(order, e.Order + 1);
            byId[e.EventId] = e;
        }

        return byId.Values
            .OrderBy(e => e.Order)
            .ThenBy(e => e.EventId, StringComparer.Ordinal)
            .ToArray();
    }

    public static ArenaMemorialRecord[] NormalizeMemorials(ArenaMemorialRecord[] records)
    {
        if (records == null || records.Length == 0)
            return Empty<ArenaMemorialRecord>.Array;

        var byId = new Dictionary<string, ArenaMemorialRecord>(StringComparer.Ordinal);
        int order = 1;
        foreach (ArenaMemorialRecord m in records)
        {
            if (m == null || m.VesselId.IsEmpty())
                continue;
            m.Name = CleanLedgerText(m.Name, 80);
            m.DesignName = CleanLedgerText(m.DesignName, 80);
            m.Killer = CleanLedgerText(m.Killer, 80);
            m.Cause = CleanLedgerText(m.Cause, 120);
            m.Kind = CleanLedgerText(m.Kind, 20);
            if (m.Kind.IsEmpty())
                m.Kind = "Ship";
            m.CaptainId = CleanLedgerText(m.CaptainId, 64);
            if (m.Kills < 0) m.Kills = 0;
            if (m.Level < 0) m.Level = 0;
            m.FightSeed = CleanLedgerText(m.FightSeed, 32);
            if (m.FightSeed.IsEmpty())
                m.FightSeed = LedgerSeed(0);
            if (m.MemorialId.IsEmpty())
                m.MemorialId = LedgerId("mem", m.VesselId, m.Cause, m.FightSeed);
            if (byId.ContainsKey(m.MemorialId))
                continue;
            if (m.Order <= 0)
                m.Order = order;
            order = Math.Max(order, m.Order + 1);
            byId[m.MemorialId] = m;
        }

        return byId.Values
            .OrderBy(m => m.Order)
            .ThenBy(m => m.MemorialId, StringComparer.Ordinal)
            .ToArray();
    }

    internal static string LedgerSeed(ulong seed)
        => seed.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);

    static string LedgerId(string prefix, string a, string b, string seed)
    {
        string payload = $"{a ?? ""}\n{b ?? ""}";
        return $"{prefix}:{seed}:{StableLedgerHash(payload):X16}";
    }

    static ulong StableLedgerHash(string text)
    {
        unchecked
        {
            ulong h = 14695981039346656037ul;
            foreach (char c in text ?? "")
            {
                h ^= c;
                h *= 1099511628211ul;
            }
            return h;
        }
    }

    static string CleanLedgerText(string value, int max)
    {
        string clean = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length <= max ? clean : clean.Substring(0, max);
    }

    public bool IsModuleResearched(string moduleUid)
        => moduleUid.NotEmpty()
        && (ResearchedModules ?? Empty<string>.Array)
            .Any(uid => string.Equals(uid, moduleUid, StringComparison.Ordinal));

    public bool ResearchModule(string moduleUid)
    {
        if (moduleUid.IsEmpty() || IsModuleResearched(moduleUid))
            return false;
        var researched = new List<string>(ResearchedModules ?? Empty<string>.Array) { moduleUid };
        ResearchedModules = NormalizeResearchedModules(researched.ToArray());
        return true;
    }

    // Legacy compatibility shims. Old tests/saves may still call these; they now map to the
    // permanent research set instead of finite counts.
    public int SalvageCount(string moduleUid) => IsModuleResearched(moduleUid) ? 1 : 0;
    public void AddSalvage(string moduleUid, int count)
    {
        if (count > 0)
            ResearchModule(moduleUid);
    }
    public bool ConsumeSalvage(string moduleUid, int count = 1) => false;

    public static string[] NormalizeResearchedModules(string[] modules, SalvageRecord[] legacySalvage = null)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string uid in modules ?? Empty<string>.Array)
            if (uid.NotEmpty())
                set.Add(uid);
        foreach (SalvageRecord r in NormalizeSalvage(legacySalvage))
            if (r?.ModuleUid.NotEmpty() == true)
                set.Add(r.ModuleUid);
        return set.Count == 0 ? Empty<string>.Array : set.ToArray();
    }

    public static SalvageRecord[] NormalizeSalvage(SalvageRecord[] salvage)
    {
        if (salvage == null || salvage.Length == 0)
            return Empty<SalvageRecord>.Array;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (SalvageRecord r in salvage)
        {
            if (r == null || r.ModuleUid.IsEmpty() || r.Count <= 0)
                continue;
            counts.TryGetValue(r.ModuleUid, out int count);
            counts[r.ModuleUid] = count + r.Count;
        }

        if (counts.Count == 0)
            return Empty<SalvageRecord>.Array;

        var records = new List<SalvageRecord>(counts.Count);
        foreach (KeyValuePair<string, int> kv in counts)
            records.Add(new SalvageRecord(kv.Key, kv.Value));
        records.Sort((a, b) => string.Compare(a.ModuleUid, b.ModuleUid, StringComparison.Ordinal));
        return records.ToArray();
    }

    public static DestroyedModuleSlot[] NormalizeDestroyedModules(DestroyedModuleSlot[] slots)
    {
        if (slots == null || slots.Length == 0)
            return Empty<DestroyedModuleSlot>.Array;

        var bySlot = new Dictionary<int, DestroyedModuleSlot>();
        foreach (DestroyedModuleSlot slot in slots)
        {
            if (slot == null || slot.SlotIndex < 0)
                continue;
            bySlot[slot.SlotIndex] = new DestroyedModuleSlot(slot.SlotIndex, slot.ModuleUid);
        }

        if (bySlot.Count == 0)
            return Empty<DestroyedModuleSlot>.Array;
        var result = bySlot.Values.ToList();
        result.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
        return result.ToArray();
    }

    public static ModuleSlotOverride[] NormalizeModuleOverrides(ModuleSlotOverride[] overrides)
    {
        if (overrides == null || overrides.Length == 0)
            return Empty<ModuleSlotOverride>.Array;

        var bySlot = new Dictionary<int, ModuleSlotOverride>();
        foreach (ModuleSlotOverride o in overrides)
        {
            if (o == null || o.SlotIndex < 0 || o.ModuleUid.IsEmpty())
                continue;
            bySlot[o.SlotIndex] = new ModuleSlotOverride(o.SlotIndex, o.ModuleUid);
        }

        if (bySlot.Count == 0)
            return Empty<ModuleSlotOverride>.Array;
        var result = bySlot.Values.ToList();
        result.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
        return result.ToArray();
    }

    public static ArenaCaptain[] NormalizeCaptains(ArenaCaptain[] captains)
    {
        if (captains == null || captains.Length == 0)
            return Empty<ArenaCaptain>.Array;

        var result = new List<ArenaCaptain>(captains.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ArenaCaptain captain in captains)
        {
            if (captain == null)
                continue;
            captain.CaptainId = CleanLedgerText(captain.CaptainId, 64);
            if (captain.CaptainId.IsEmpty())
                captain.CaptainId = UniqueCaptainId("captain", seen);
            else if (seen.Contains(captain.CaptainId))
                captain.CaptainId = UniqueCaptainId(captain.CaptainId, seen);
            else
                seen.Add(captain.CaptainId);

            captain.Name = CleanLedgerText(captain.Name, 80);
            if (captain.Name.IsEmpty())
                captain.Name = "Arena Captain";
            captain.Epithet = CleanLedgerText(captain.Epithet, 48);
            if (captain.Epithet.IsEmpty())
                captain.Epithet = "Unproven";
            if (captain.Level < 0) captain.Level = 0;
            if (captain.Kills < 0) captain.Kills = 0;
            if (captain.SurvivedHullLosses < 0) captain.SurvivedHullLosses = 0;
            captain.Killer = CleanLedgerText(captain.Killer, 80);
            captain.DeathCause = CleanLedgerText(captain.DeathCause, 120);
            result.Add(captain);
        }
        return result.Count > 0 ? result.ToArray() : Empty<ArenaCaptain>.Array;
    }

    void EnsureCaptainsForOwnedVessels()
    {
        var captains = new List<ArenaCaptain>(Captains ?? Empty<ArenaCaptain>.Array);
        var byId = new Dictionary<string, ArenaCaptain>(StringComparer.Ordinal);
        foreach (ArenaCaptain captain in captains)
            if (captain?.CaptainId.NotEmpty() == true && !byId.ContainsKey(captain.CaptainId))
                byId[captain.CaptainId] = captain;

        var seen = new HashSet<string>(byId.Keys, StringComparer.Ordinal);
        foreach (OwnedVessel vessel in OwnedVessels ?? Empty<OwnedVessel>.Array)
        {
            if (vessel == null)
                continue;
            if (vessel.CaptainId.NotEmpty() && byId.ContainsKey(vessel.CaptainId))
                continue;

            ArenaCaptain captain = CreateCaptainForVessel(vessel, seen);
            captains.Add(captain);
            byId[captain.CaptainId] = captain;
            vessel.CaptainId = captain.CaptainId;
        }
        Captains = NormalizeCaptains(captains.ToArray());
    }

    static ArenaCaptain CreateCaptainForVessel(OwnedVessel vessel, HashSet<string> seen)
    {
        string basis = $"{vessel?.VesselId}|{vessel?.DesignName}|{vessel?.Name}";
        ulong hash = StableLedgerHash(basis);
        string[] first = { "Vale", "Mira", "Kade", "Nyx", "Orion", "Lyra", "Juno", "Vega" };
        string[] last = { "Ash", "Rook", "Sable", "Knox", "Iris", "Talon", "Nova", "Hex" };
        string[] epithets = { "Steady Hand", "Cold Vector", "Last Burn", "Iron Nerve", "Amber Wake", "Black Orbit" };
        string idRoot = $"cap-{hash:X12}".ToLowerInvariant();
        return new ArenaCaptain
        {
            CaptainId = UniqueCaptainId(idRoot, seen),
            Name = $"{first[(int)(hash % (ulong)first.Length)]} {last[(int)((hash >> 8) % (ulong)last.Length)]}",
            Epithet = epithets[(int)((hash >> 16) % (ulong)epithets.Length)],
            Alive = true,
        };
    }

    static string UniqueCaptainId(string root, HashSet<string> seen)
    {
        root = CleanLedgerText(root, 48);
        string id = root.NotEmpty() ? root : "captain";
        for (int n = 2; seen != null && seen.Contains(id); ++n)
            id = CleanLedgerText($"{root}-{n}", 64);
        seen?.Add(id);
        return id;
    }

    void NormalizeContenders()
    {
        if (Contenders == null || Contenders.Length == 0)
        {
            Contenders = Empty<ContenderRecord>.Array;
            return;
        }

        var contenders = new List<ContenderRecord>(Contenders.Length);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ContenderRecord c in Contenders)
        {
            if (c == null || c.DesignName.IsEmpty())
                continue;

            string preferredName = c.Name.NotEmpty() ? c.Name : c.DesignName;
            c.Name = UniqueLoadedName(preferredName, seenNames);
            seenNames.Add(c.Name);
            c.RoleClass ??= "";
            if (c.Rating < 1) c.Rating = 1;
            if (c.Wins < 0) c.Wins = 0;
            if (c.Losses < 0) c.Losses = 0;
            if (c.Seasons < 0) c.Seasons = 0;
            if (c.Experience < 0f) c.Experience = 0f;
            if (c.Level < 0) c.Level = 0;
            if (c.Evolutions < 0) c.Evolutions = 0;
            c.RivalName ??= "";
            if (c.RivalWins < 0) c.RivalWins = 0;
            if (c.RivalLosses < 0) c.RivalLosses = 0;
            c.NemesisOfVesselId = CleanLedgerText(c.NemesisOfVesselId, 80);
            if (c.Grudge < 0) c.Grudge = 0;
            if (c.NemesisOfVesselId.IsEmpty())
                c.Grudge = 0;
            ArenaRivalDossiers.NormalizeDossier(c, RivalIdentitySeed, seenIds);
            contenders.Add(c);
        }

        Contenders = contenders.Count > 0 ? contenders.ToArray() : Empty<ContenderRecord>.Array;
    }

    void NormalizeTeams()
    {
        if (Teams == null || Teams.Length == 0 || Contenders == null || Contenders.Length == 0)
        {
            Teams = Empty<ArenaTeam>.Array;
            return;
        }

        var validMembers = new HashSet<string>(
            Contenders.Where(c => c != null && c.Name.NotEmpty()).Select(c => c.Name),
            StringComparer.Ordinal);
        var teams = new List<ArenaTeam>(Teams.Length);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (ArenaTeam team in Teams)
        {
            if (team == null)
                continue;

            string preferredName = team.Name.NotEmpty() ? team.Name : "Team";
            string name = UniqueLoadedName(preferredName, seenNames);
            var members = new List<string>();
            var seenMembers = new HashSet<string>(StringComparer.Ordinal);
            foreach (string member in team.Members ?? Empty<string>.Array)
            {
                if (member.IsEmpty() || !validMembers.Contains(member) || !seenMembers.Add(member))
                    continue;
                members.Add(member);
            }

            if (members.Count == 0)
                continue;

            seenNames.Add(name);
            teams.Add(new ArenaTeam(name, members.ToArray()));
        }

        Teams = teams.Count > 0 ? teams.ToArray() : Empty<ArenaTeam>.Array;
    }

    static string UniqueLoadedName(string preferredName, HashSet<string> seenNames)
    {
        string root = preferredName.NotEmpty() ? preferredName : "Contender";
        string name = root;
        for (int n = 2; name.IsEmpty() || seenNames.Contains(name); ++n)
            name = $"{root} #{n}";
        return name;
    }

    static string UniqueLoadedVesselId(string preferredId, HashSet<string> seenIds)
    {
        string root = preferredId.NotEmpty() ? preferredId : "vessel";
        string id = root;
        for (int n = 2; id.IsEmpty() || seenIds.Contains(id); ++n)
            id = $"{root}#{n}";
        return id;
    }

    void NormalizeFleetIds()
    {
        if (FleetVesselIds == null || FleetVesselIds.Length == 0)
        {
            FleetVesselIds = Empty<string>.Array;
            return;
        }

        OwnedVessel flagship = ActiveVessel ?? FirstVessel;
        int maxFleetSize = MaxFleetSize;
        var ids = new List<string>(Math.Min(FleetVesselIds.Length, maxFleetSize - 1));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in FleetVesselIds)
        {
            if (id.IsEmpty() || seen.Contains(id))
                continue;
            if (flagship != null && string.Equals(id, flagship.VesselId, StringComparison.Ordinal))
                continue;
            if (FindOwnedVessel(id) == null)
                continue;

            ids.Add(id);
            seen.Add(id);
            if (ids.Count >= maxFleetSize - 1)
                break;
        }

        FleetVesselIds = ids.Count > 0 ? ids.ToArray() : Empty<string>.Array;
    }

    /// <summary>
    /// The OWNED vessels currently FIELDED in the active fleet, RESOLVED + SANITIZED: the
    /// flagship (active, else first owned vessel) is ALWAYS first; then every distinct
    /// <see cref="FleetVesselIds"/> entry that still maps to an owned vessel (stale ids dropped),
    /// clamped to <see cref="ArenaMaxFleetSize"/>. Empty <see cref="FleetVesselIds"/> (a fresh
    /// career) yields EXACTLY the flagship — one ship, today's default. Never null; the run
    /// SPAWNS exactly these vessels (flagship first), so this is the single source of truth for
    /// "who fights".
    /// </summary>
    public OwnedVessel[] FieldedFleetVessels()
    {
        if (OwnedVessels == null || OwnedVessels.Length == 0)
            return Empty<OwnedVessel>.Array;

        OwnedVessel flagship = ActiveVessel ?? FirstVessel;
        int maxFleetSize = MaxFleetSize;
        var result = new System.Collections.Generic.List<OwnedVessel>(maxFleetSize);
        var seen   = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        // The flagship is ALWAYS fielded and is ALWAYS the first ship (the formation lead).
        if (flagship != null && flagship.VesselId.NotEmpty())
        {
            result.Add(flagship);
            seen.Add(flagship.VesselId);
        }

        // Then the rest of the fleet (in the player's chosen order), skipping the flagship
        // (already first), duplicates, and ids that no longer map to an owned vessel.
        if (FleetVesselIds != null)
        {
            foreach (string id in FleetVesselIds)
            {
                if (id.IsEmpty() || seen.Contains(id))
                    continue;
                OwnedVessel v = FindOwnedVessel(id);
                if (v == null)
                    continue; // stale id (vessel no longer owned) — drop it
                result.Add(v);
                seen.Add(id);
                if (result.Count >= maxFleetSize)
                    break; // enforce the hard cap
            }
        }

        return result.ToArray();
    }

    /// <summary>The owned vessel with this VesselId, or null when no owned vessel has it.</summary>
    public OwnedVessel FindOwnedVessel(string vesselId)
    {
        if (vesselId.IsEmpty() || OwnedVessels == null)
            return null;
        foreach (OwnedVessel v in OwnedVessels)
            if (v != null && string.Equals(v.VesselId, vesselId, StringComparison.Ordinal))
                return v;
        return null;
    }

    /// <summary>True when the owned vessel with this id is currently FIELDED in the fleet
    /// (the flagship is always fielded; others are fielded iff listed in FleetVesselIds).</summary>
    public bool IsInFleet(string vesselId)
    {
        if (vesselId.IsEmpty())
            return false;
        OwnedVessel flagship = ActiveVessel ?? FirstVessel;
        if (flagship != null && string.Equals(flagship.VesselId, vesselId, StringComparison.Ordinal))
            return true; // the flagship is always part of the fleet
        if (FleetVesselIds == null)
            return false;
        foreach (string id in FleetVesselIds)
            if (string.Equals(id, vesselId, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>
    /// TOGGLE an owned vessel in/out of the active fleet (the FLEET popup's IN-FLEET button).
    /// The FLAGSHIP (active vessel) can never be removed (it's always the run's lead), so a
    /// toggle on it is a no-op that returns true. Adding is rejected (returns false) when the
    /// fleet is already at <see cref="ArenaMaxFleetSize"/>. Returns true on a successful change
    /// (or the flagship no-op). Does NOT save — the caller persists. Sanitizes FleetVesselIds
    /// (drops stale ids, the flagship, and dupes) on the way out.
    /// </summary>
    public bool ToggleFleetVessel(string vesselId)
    {
        if (vesselId.IsEmpty() || FindOwnedVessel(vesselId) == null)
            return false; // not an owned vessel

        OwnedVessel flagship = ActiveVessel ?? FirstVessel;
        if (flagship != null && string.Equals(flagship.VesselId, vesselId, StringComparison.Ordinal))
            return true; // flagship is permanent; toggling it is a harmless no-op

        // Rebuild the fleet id set: keep only distinct ids that still map to a non-flagship
        // owned vessel, then add or remove the toggled id.
        var ids = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        bool wasIn = false;
        if (FleetVesselIds != null)
        {
            foreach (string id in FleetVesselIds)
            {
                if (id.IsEmpty() || seen.Contains(id)) continue;
                if (flagship != null && string.Equals(id, flagship.VesselId, StringComparison.Ordinal)) continue;
                if (FindOwnedVessel(id) == null) continue; // stale
                if (string.Equals(id, vesselId, StringComparison.Ordinal)) { wasIn = true; continue; } // remove the toggled id
                ids.Add(id);
                seen.Add(id);
            }
        }

        if (!wasIn)
        {
            // ADDING: enforce the cap. The fielded count is the flagship (1) + the listed ids;
            // reject the add if that would exceed the max.
            int fielded = 1 + ids.Count; // +1 flagship
            if (fielded >= MaxFleetSize)
            {
                FleetVesselIds = ids.ToArray();
                return false; // at cap — can't add another
            }
            ids.Add(vesselId);
        }

        FleetVesselIds = ids.ToArray();
        return true;
    }

    /// <summary>The number of owned vessels currently FIELDED (flagship + fleet ids, clamped).</summary>
    public int FleetSize => FieldedFleetVessels().Length;

    /// <summary>
    /// Add a newly OWNED vessel to the roster (DEALERSHIP buy / starter grant) and return it.
    /// Generates a UNIQUE VesselId (so a career can own two of the same design) and, when
    /// <paramref name="makeActive"/> (or the roster was empty), sets it as the active gladiator.
    /// Does NOT save — the caller persists via <see cref="CareerManager.Save"/>.
    /// </summary>
    public OwnedVessel AddOwnedVessel(string designName, string name = null, bool makeActive = false)
    {
        if (designName.IsEmpty())
            return null;

        NormalizeForPersistence();

        var vessel = new OwnedVessel(designName, 0f, 0, 0, name)
        {
            VesselId = NewVesselId(designName),
        };
        var captainList = new List<ArenaCaptain>(NormalizeCaptains(Captains));
        var captainSeen = new HashSet<string>(
            captainList.Where(c => c?.CaptainId.NotEmpty() == true).Select(c => c.CaptainId),
            StringComparer.Ordinal);
        ArenaCaptain captain = CreateCaptainForVessel(vessel, captainSeen);
        vessel.CaptainId = captain.CaptainId;
        captainList.Add(captain);
        Captains = NormalizeCaptains(captainList.ToArray());

        var list = new System.Collections.Generic.List<OwnedVessel>(OwnedVessels ?? Empty<OwnedVessel>.Array)
        {
            vessel
        };
        OwnedVessels = list.ToArray();

        if (makeActive || ActiveVesselId.IsEmpty() || ActiveVessel == null)
            ActiveVesselId = vessel.VesselId;

        return vessel;
    }

    /// <summary>
    /// A stable, UNIQUE VesselId for a new owned vessel: the design name plus a short unique
    /// suffix so two owned vessels of the same design never collide. Deterministic in shape
    /// (designName#nnnn) but unique per vessel.
    /// </summary>
    string NewVesselId(string designName)
    {
        int n = (OwnedVessels?.Length ?? 0) + 1;
        string id = $"{designName}#{n}";
        // Ensure uniqueness even if the roster has gaps from prior removes.
        while (HasVesselId(id))
        {
            ++n;
            id = $"{designName}#{n}";
        }
        return id;
    }

    bool HasVesselId(string id)
    {
        if (OwnedVessels == null) return false;
        foreach (OwnedVessel v in OwnedVessels)
            if (v != null && string.Equals(v.VesselId, id, StringComparison.Ordinal))
                return true;
        return false;
    }

    // =====================================================================================
    // OWNED-MODULES CENSUS (ACCESS LAYER) — "what items do I actually have access to?"
    //
    // A DERIVED tally of every module across ALL the career's owned vessels' DESIGNS: each
    // module UID -> total count, summed across the whole owned roster (a fleet of ships whose
    // designs share a module sums that module's count). Recomputed on demand from the owned
    // roster, so it is ALWAYS consistent with what the player owns — NO separate persistence.
    //
    // This is the ACCESS/VIEW layer the playtester asked for ("i dont seem to have access to the
    // items that were on the starting vessel or bought ships"): it lets a view SHOW the modules
    // the player owns across their fleet. It does NOT restrict the designer palette, consume
    // modules, or build an item economy — those are deferred follow-ups.
    //
    // Pure + headless-testable: takes a DESIGN LOOKUP delegate (designName -> the design's module
    // UIDs, or null when the design no longer resolves) so the census has NO engine dependency and
    // a missing design is simply SKIPPED (null/exception-safe, never throws). The optional
    // displayName delegate resolves a UID's friendly name (falls back to the UID).
    // =====================================================================================

    /// <summary>One row of the owned-modules census: a module UID, its display name, and the
    /// total count of that module across all owned vessels' designs.</summary>
    public readonly struct ModuleCensusEntry
    {
        public readonly string ModuleUid;
        public readonly string DisplayName;
        public readonly int Count;

        public ModuleCensusEntry(string moduleUid, string displayName, int count)
        {
            ModuleUid   = moduleUid;
            DisplayName = displayName.NotEmpty() ? displayName : moduleUid;
            Count       = count;
        }

        public override string ToString() => $"{DisplayName} x{Count}";
    }

    /// <summary>
    /// Build the OWNED-MODULES census for this career's owned roster. For each owned vessel, the
    /// <paramref name="designModuleUids"/> delegate returns that vessel's design's module UIDs
    /// (one entry PER MODULE INSTANCE — so a design with two of a module yields that UID twice),
    /// or null/empty when the design no longer resolves (the vessel is SKIPPED, never throws).
    /// Counts are summed across the whole roster. <paramref name="displayName"/> (optional)
    /// resolves a UID's friendly name. Returns rows sorted by COUNT desc, then DISPLAY NAME asc,
    /// then UID asc (stable, deterministic). Never null.
    /// </summary>
    public ModuleCensusEntry[] BuildOwnedModuleCensus(
        Func<string, IReadOnlyList<string>> designModuleUids,
        Func<string, string> displayName = null)
        => BuildResearchedModuleCensus(ResearchedModules, displayName);

    public static ModuleCensusEntry[] BuildResearchedModuleCensus(
        string[] researchedModules,
        Func<string, string> displayName = null)
    {
        string[] modules = NormalizeResearchedModules(researchedModules);
        if (modules.Length == 0)
            return Array.Empty<ModuleCensusEntry>();

        var rows = new List<ModuleCensusEntry>(modules.Length);
        foreach (string uid in modules)
        {
            string name = null;
            if (displayName != null)
            {
                try { name = displayName(uid); } catch { name = null; }
            }
            rows.Add(new ModuleCensusEntry(uid, name, 1));
        }
        SortModuleCensus(rows);
        return rows.ToArray();
    }

    public static ModuleCensusEntry[] BuildModuleCensusWithSalvage(
        ModuleCensusEntry[] ownedRows,
        SalvageRecord[] salvage,
        Func<string, string> displayName = null)
    {
        bool hasOwned = ownedRows != null && ownedRows.Length > 0;
        salvage = NormalizeSalvage(salvage);
        if (salvage.Length == 0)
            return hasOwned ? ownedRows : Array.Empty<ModuleCensusEntry>();

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (hasOwned)
        {
            foreach (ModuleCensusEntry row in ownedRows)
            {
                if (row.ModuleUid.IsEmpty() || row.Count <= 0)
                    continue;
                counts.TryGetValue(row.ModuleUid, out int count);
                counts[row.ModuleUid] = count + row.Count;
                if (row.DisplayName.NotEmpty())
                    names[row.ModuleUid] = row.DisplayName;
            }
        }

        foreach (SalvageRecord r in salvage)
        {
            counts.TryGetValue(r.ModuleUid, out int count);
            counts[r.ModuleUid] = count + r.Count;
            if (!names.ContainsKey(r.ModuleUid))
            {
                string name = null;
                if (displayName != null)
                {
                    try { name = displayName(r.ModuleUid); } catch { name = null; }
                }
                names[r.ModuleUid] = name.NotEmpty() ? name : r.ModuleUid;
            }
        }

        var rows = new List<ModuleCensusEntry>(counts.Count);
        foreach (KeyValuePair<string, int> kv in counts)
        {
            names.TryGetValue(kv.Key, out string name);
            rows.Add(new ModuleCensusEntry(kv.Key, name, kv.Value));
        }
        SortModuleCensus(rows);
        return rows.ToArray();
    }

    /// <summary>
    /// Static census over an explicit owned roster (used by the screen accessor + headless proof).
    /// Same contract as the instance method: skips null vessels and vessels whose design no longer
    /// resolves (the delegate returns null), sums each module UID across the roster, and returns
    /// rows sorted by count desc, then name asc, then UID asc. Null/exception-safe; never null.
    /// </summary>
    public static ModuleCensusEntry[] BuildOwnedModuleCensus(
        OwnedVessel[] owned,
        Func<string, IReadOnlyList<string>> designModuleUids,
        Func<string, string> displayName = null)
    {
        if (owned == null || owned.Length == 0 || designModuleUids == null)
            return Array.Empty<ModuleCensusEntry>();

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (OwnedVessel v in owned)
        {
            if (v == null || v.DesignName.IsEmpty())
                continue;

            IReadOnlyList<string> uids;
            try
            {
                uids = designModuleUids(v.DesignName);
            }
            catch
            {
                continue; // a lookup that throws (e.g. a corrupt design) skips the vessel safely
            }

            if (uids == null)
                continue; // design no longer resolves — skip this vessel (no throw)

            foreach (string uid in uids)
            {
                if (uid.IsEmpty())
                    continue;
                counts.TryGetValue(uid, out int c);
                counts[uid] = c + 1;
            }
        }

        var rows = new List<ModuleCensusEntry>(counts.Count);
        foreach (KeyValuePair<string, int> kv in counts)
        {
            string name = null;
            if (displayName != null)
            {
                try { name = displayName(kv.Key); } catch { name = null; }
            }
            rows.Add(new ModuleCensusEntry(kv.Key, name, kv.Value));
        }

        SortModuleCensus(rows);

        return rows.ToArray();
    }

    public static ModuleCensusEntry[] BuildOwnedModuleCensus(
        OwnedVessel[] owned,
        Func<OwnedVessel, IReadOnlyList<string>> vesselModuleUids,
        Func<string, string> displayName = null)
    {
        if (owned == null || owned.Length == 0 || vesselModuleUids == null)
            return Array.Empty<ModuleCensusEntry>();

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (OwnedVessel v in owned)
        {
            if (v == null || v.DesignName.IsEmpty())
                continue;

            IReadOnlyList<string> uids;
            try
            {
                uids = vesselModuleUids(v);
            }
            catch
            {
                continue;
            }

            if (uids == null)
                continue;
            foreach (string uid in uids)
            {
                if (uid.IsEmpty())
                    continue;
                counts.TryGetValue(uid, out int c);
                counts[uid] = c + 1;
            }
        }

        var rows = new List<ModuleCensusEntry>(counts.Count);
        foreach (KeyValuePair<string, int> kv in counts)
        {
            string name = null;
            if (displayName != null)
            {
                try { name = displayName(kv.Key); } catch { name = null; }
            }
            rows.Add(new ModuleCensusEntry(kv.Key, name, kv.Value));
        }

        SortModuleCensus(rows);
        return rows.ToArray();
    }

    static void SortModuleCensus(List<ModuleCensusEntry> rows)
    {
        // Sort: most-owned first, then friendly name, then UID — stable + deterministic.
        rows.Sort((a, b) =>
        {
            int byCount = b.Count.CompareTo(a.Count);
            if (byCount != 0) return byCount;
            int byName = string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            if (byName != 0) return byName;
            return string.Compare(a.ModuleUid, b.ModuleUid, StringComparison.Ordinal);
        });
    }
}

[StarDataType]
public sealed class SalvageRecord
{
    [StarData] public string ModuleUid = "";
    [StarData] public int Count;

    [StarDataConstructor] public SalvageRecord() { }

    public SalvageRecord(string moduleUid, int count)
    {
        ModuleUid = moduleUid ?? "";
        Count = Math.Max(0, count);
    }
}

[StarDataType]
public sealed class DestroyedModuleSlot
{
    [StarData] public int SlotIndex = -1;
    [StarData] public string ModuleUid = "";

    [StarDataConstructor] public DestroyedModuleSlot() { }

    public DestroyedModuleSlot(int slotIndex, string moduleUid)
    {
        SlotIndex = slotIndex;
        ModuleUid = moduleUid ?? "";
    }
}

[StarDataType]
public sealed class ModuleSlotOverride
{
    [StarData] public int SlotIndex = -1;
    [StarData] public string ModuleUid = "";

    [StarDataConstructor] public ModuleSlotOverride() { }

    public ModuleSlotOverride(int slotIndex, string moduleUid)
    {
        SlotIndex = slotIndex;
        ModuleUid = moduleUid ?? "";
    }
}

/// <summary>
/// A persisted owned vessel — a VALUE record of a gladiator the career owns. We persist the
/// DesignName (so it can be re-spawned from ResourceManager.Ships) plus the earned VETERANCY
/// (Experience/Level/Kills) so the veteran returns exactly as strong as it was banked. We do
/// NOT persist the live Ship; on reload the run re-spawns this DesignName and re-applies the
/// veterancy values onto the fresh ship.
/// </summary>
[StarDataType]
public sealed class OwnedVessel
{
    // A stable id for the owned slot (so a future UI can reference vessels). Defaults to the
    // DesignName but can be made unique if a career ever owns two of the same design.
    [StarData] public string VesselId;

    // The design name to re-spawn from (must exist in ResourceManager.Ships on reload).
    [StarData] public string DesignName;

    // Optional vanity/display name; falls back to DesignName when empty.
    [StarData] public string Name;

    // Persistent pilot identity assigned to this owned vessel.
    [StarData] public string CaptainId = "";

    // ---- VETERANCY (the carried progress) — re-applied after the ship is re-spawned. ----
    [StarData] public float Experience;
    [StarData] public int Level;
    [StarData] public int Kills;

    // ---- COMBAT SCARS ----
    // Zero CurrentHullHealth means fully repaired/unscarred. A positive value carries hull
    // damage into the next spawn; MaxHullHealth records the max from the fight that banked it
    // so repair pricing can scale by actual damage.
    [StarData] public float CurrentHullHealth;
    [StarData] public float MaxHullHealth;

    // Destroyed module slots are removed from the next spawn until refit. SlotIndex is the
    // original design-slot index, stable across skipped destroyed slots.
    [StarData] public DestroyedModuleSlot[] DestroyedModules = Empty<DestroyedModuleSlot>.Array;

    // Refit overrides replace an original design slot with a salvaged module UID. Overrides
    // survive future spawns and are consumed before destroyed-slot skipping.
    [StarData] public ModuleSlotOverride[] ModuleOverrides = Empty<ModuleSlotOverride>.Array;

    [StarDataConstructor] public OwnedVessel() { }

    public OwnedVessel(string designName, float experience, int level, int kills, string name = null)
    {
        VesselId   = designName;
        DesignName = designName;
        Name       = name;
        Experience = experience;
        Level      = level;
        Kills      = kills;
    }
}

/// <summary>
/// A persistent AI ladder contender. Like <see cref="OwnedVessel"/>, this is a value record:
/// the live ship is re-spawned from <see cref="DesignName"/> when simulated or challenged.
/// </summary>
[StarDataType]
public sealed class ContenderRecord
{
    [StarData] public string Name;
    [StarData] public string ContenderId;
    [StarData] public string DesignName;
    [StarData] public string RoleClass;
    [StarData] public string Epithet;
    [StarData] public string ArenaPersona;
    [StarData] public string OriginHook;
    [StarData] public string PreferredFleetScale;
    [StarData] public string Bio;
    [StarData] public int Rating;
    [StarData] public int Wins;
    [StarData] public int Losses;
    [StarData] public int Seasons;
    [StarData] public float Experience;
    [StarData] public int Level;
    [StarData] public int Evolutions;
    [StarData] public string RivalName;
    [StarData] public int RivalWins;
    [StarData] public int RivalLosses;
    [StarData] public string NemesisOfVesselId;
    [StarData] public int Grudge;

    [StarDataConstructor] public ContenderRecord() { }

    public ContenderRecord(string name, string designName, string roleClass, int rating)
    {
        Name       = name;
        DesignName = designName;
        RoleClass  = roleClass;
        Rating     = rating;
    }
}

[StarDataType]
public sealed class ArenaCaptain
{
    [StarData] public string CaptainId = "";
    [StarData] public string Name = "";
    [StarData] public string Epithet = "";
    [StarData] public int Level;
    [StarData] public int Kills;
    [StarData] public bool Alive = true;
    [StarData] public int SurvivedHullLosses;
    [StarData] public string Killer = "";
    [StarData] public string DeathCause = "";

    [StarDataConstructor] public ArenaCaptain() { }
}

[StarDataType]
public sealed class ArenaTeam
{
    [StarData] public string Name;
    [StarData] public string[] Members = Empty<string>.Array;

    [StarDataConstructor] public ArenaTeam() { }

    public ArenaTeam(string name, string[] members)
    {
        Name = name ?? "";
        Members = members ?? Empty<string>.Array;
    }

    public int Size => Members?.Length ?? 0;
}

[StarDataType]
public sealed class ArenaChronicleEvent
{
    [StarData] public string EventId = "";
    [StarData] public int Order;
    [StarData] public string Kind = "";
    [StarData] public string Subject = "";
    [StarData] public string Summary = "";
    [StarData] public string FightSeed = "";

    [StarDataConstructor] public ArenaChronicleEvent() { }

    public ArenaChronicleEvent(string eventId, int order, string kind, string subject,
        string summary, ulong fightSeed)
    {
        EventId = eventId ?? "";
        Order = Math.Max(1, order);
        Kind = kind ?? "";
        Subject = subject ?? "";
        Summary = summary ?? "";
        FightSeed = ArenaCareer.LedgerSeed(fightSeed);
    }
}

[StarDataType]
public sealed class ArenaMemorialRecord
{
    [StarData] public string MemorialId = "";
    [StarData] public int Order;
    [StarData] public string VesselId = "";
    [StarData] public string Name = "";
    [StarData] public string DesignName = "";
    [StarData] public int Kills;
    [StarData] public int Level;
    [StarData] public string Killer = "";
    [StarData] public string Cause = "";
    [StarData] public string FightSeed = "";
    [StarData] public string Kind = "Ship";
    [StarData] public string CaptainId = "";

    [StarDataConstructor] public ArenaMemorialRecord() { }

    public ArenaMemorialRecord(string memorialId, int order, string vesselId, string name,
        string designName, int kills, int level, string killer, string cause, ulong fightSeed,
        string kind = "Ship", string captainId = "")
    {
        MemorialId = memorialId ?? "";
        Order = Math.Max(1, order);
        VesselId = vesselId ?? "";
        Name = name ?? "";
        DesignName = designName ?? "";
        Kills = Math.Max(0, kills);
        Level = Math.Max(0, level);
        Killer = killer ?? "";
        Cause = cause ?? "";
        FightSeed = ArenaCareer.LedgerSeed(fightSeed);
        Kind = kind ?? "Ship";
        CaptainId = captainId ?? "";
    }
}

public enum ArenaStartArchetype
{
    Ace,
    Wingmates,
    Swarm,
}

public readonly struct CareerSlotMetadata
{
    public readonly int Slot;
    public readonly bool Exists;
    public readonly string Path;
    public readonly string Summary;
    public readonly int CareerLevel;
    public readonly int Cash;
    public readonly int Fame;
    public readonly int VesselCount;
    public readonly long LastPlayedUtcTicks;

    public CareerSlotMetadata(int slot, bool exists, string path, string summary,
        int careerLevel, int cash, int fame, int vesselCount, long lastPlayedUtcTicks)
    {
        Slot = slot;
        Exists = exists;
        Path = path ?? "";
        Summary = summary ?? "";
        CareerLevel = careerLevel;
        Cash = cash;
        Fame = fame;
        VesselCount = vesselCount;
        LastPlayedUtcTicks = Math.Max(0, lastPlayedUtcTicks);
    }
}

public readonly struct ArenaNewCareerResult
{
    public readonly bool Success;
    public readonly string Message;
    public readonly int Slot;
    public readonly ArenaCareer Career;

    public ArenaNewCareerResult(bool success, string message, int slot, ArenaCareer career)
    {
        Success = success;
        Message = message ?? "";
        Slot = slot;
        Career = career;
    }
}

/// <summary>
/// Loads/saves an <see cref="ArenaCareer"/> to {AppData}/StarDrive/Arena Career/career.yaml
/// via the proven [StarData] YAML system (YamlSerializer.SerializeOne / YamlParser.DeserializeOne,
/// the exact calls RaceSave uses). Null/exception-safe: a missing or corrupt file yields a fresh
/// empty career (logged), never a throw. The save path is overridable so a test can target a temp
/// path and never touch the user's real career file.
/// </summary>
public static class CareerManager
{
    public const int MinSlot = 1;
    public const int MaxSlot = 3;

    // The "/Arena Career/" subdir mirrors the "/Saved Races/" convention (SaveRaceScreen.cs).
    public static string CareerDir => Dir.StarDriveAppData + "/Arena Career/";

    /// <summary>The real career file the live game saves/loads.</summary>
    public static string DefaultPath => CareerDir + "career.yaml";

    /// <summary>
    /// Test hook for slot saves. Null/empty means the live Arena Career directory; tests can
    /// redirect all slotN.yaml paths to a temp directory and restore this in finally.
    /// </summary>
    public static string SlotDirectoryOverride;

    /// <summary>
    /// Unit-test guard root. When set, Arena career save/load paths must resolve under this
    /// temp root. Live Arena Career paths are redirected under the root; other non-temp paths
    /// are rejected. Null/empty keeps production behavior unchanged.
    /// </summary>
    public static string TestSaveIsolationRoot;
    public static string LastResolvedCareerPath;
    public static string LastRedirectedCareerPathFrom;
    public static string LastBlockedCareerPath;

    public static int NormalizeSlotIndex(int slot) => Math.Clamp(slot, MinSlot, MaxSlot);

    static string ActiveSlotDir => SlotDirectoryOverride.NotEmpty() ? SlotDirectoryOverride : CareerDir;

    public static string SlotPath(int slot)
        => ResolveCareerPath(Path.Combine(ActiveSlotDir, $"slot{NormalizeSlotIndex(slot)}.yaml"));

    public static void ClearTestSaveIsolationAudit()
    {
        LastResolvedCareerPath = null;
        LastRedirectedCareerPathFrom = null;
        LastBlockedCareerPath = null;
    }

    public static string ResolveCareerPathForTest(string path = null)
        => ResolveCareerPath(path ?? DefaultPath);

    static string ResolveCareerPath(string path)
    {
        path ??= DefaultPath;
        if (TestSaveIsolationRoot.IsEmpty())
            return path;

        string fullPath = Path.GetFullPath(path);
        string guardRoot = Path.GetFullPath(TestSaveIsolationRoot);
        string careerRoot = Path.GetFullPath(CareerDir);
        if (IsSameOrUnder(fullPath, guardRoot))
        {
            LastResolvedCareerPath = fullPath;
            return fullPath;
        }

        if (IsSameOrUnder(fullPath, careerRoot))
        {
            string relative = Path.GetRelativePath(careerRoot, fullPath);
            string redirected = Path.GetFullPath(Path.Combine(guardRoot, "Arena Career", relative));
            LastRedirectedCareerPathFrom = fullPath;
            LastResolvedCareerPath = redirected;
            return redirected;
        }

        LastBlockedCareerPath = fullPath;
        throw new InvalidOperationException(
            $"Arena career save isolation guard blocked path '{fullPath}'. Allowed temp root: '{guardRoot}'.");
    }

    static bool IsSameOrUnder(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.GetFullPath(root);
        string rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString())
            || fullRoot.EndsWith(Path.AltDirectorySeparatorChar.ToString())
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;
        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    public static bool SaveSlot(ArenaCareer career, int slot)
        => Save(career, SlotPath(slot));

    public static ArenaCareer LoadSlot(int slot)
        => Load(SlotPath(slot));

    public static bool IsSlotOccupied(int slot)
        => File.Exists(SlotPath(slot));

    public static CareerSlotMetadata GetSlotMetadata(int slot)
    {
        int normalized = NormalizeSlotIndex(slot);
        string path = SlotPath(normalized);
        var fi = new FileInfo(path);
        if (!fi.Exists)
            return new CareerSlotMetadata(normalized, false, path, "Empty slot",
                0, 0, 0, 0, 0);

        ArenaCareer career = Load(path);
        int vessels = career.OwnedVessels?.Length ?? 0;
        string summary = $"Level {career.CareerLevel} | Cash ${career.Cash} | Fame {career.Fame} | Vessels {vessels}";
        return new CareerSlotMetadata(normalized, true, path, summary,
            career.CareerLevel, career.Cash, career.Fame, vessels, fi.LastWriteTimeUtc.Ticks);
    }

    public static CareerSlotMetadata[] GetAllSlotMetadata()
    {
        var slots = new CareerSlotMetadata[MaxSlot - MinSlot + 1];
        for (int i = 0; i < slots.Length; ++i)
            slots[i] = GetSlotMetadata(MinSlot + i);
        return slots;
    }

    public static CareerSlotMetadata MostRecentSlot()
    {
        CareerSlotMetadata[] slots = GetAllSlotMetadata();
        CareerSlotMetadata best = default;
        foreach (CareerSlotMetadata slot in slots)
        {
            if (!slot.Exists)
                continue;
            if (!best.Exists || slot.LastPlayedUtcTicks > best.LastPlayedUtcTicks
                || (slot.LastPlayedUtcTicks == best.LastPlayedUtcTicks && slot.Slot < best.Slot))
                best = slot;
        }
        return best;
    }

    public static IShipDesign[] StartingRosterDesigns(ArenaStartArchetype archetype)
        => PickStartingDesigns(archetype, DefaultStartingLoadoutSeed(archetype));

    public static ArenaCareer CreateNewCareer(ArenaStartArchetype archetype)
        => CreateNewCareer(archetype, DefaultStartingLoadoutSeed(archetype));

    public static IShipDesign[] StartingRosterDesigns(ArenaStartArchetype archetype, ulong seed)
        => PickStartingDesigns(archetype, seed);

    public static ArenaCareer CreateNewCareer(ArenaStartArchetype archetype, ulong seed)
    {
        var career = new ArenaCareer
        {
            Cash = StartingCash(archetype),
            PlayerShipsPermadeath = true,
        };

        IShipDesign[] designs = PickStartingDesigns(archetype, seed);
        for (int i = 0; i < designs.Length; ++i)
            career.AddOwnedVessel(designs[i].Name, $"{archetype} {i + 1}", makeActive: i == 0);

        career.FleetVesselIds = career.OwnedVessels
            .Skip(1)
            .Take(Math.Max(0, career.MaxFleetSize - 1))
            .Select(v => v.VesselId)
            .ToArray();
        career.NormalizeForPersistence();
        return career;
    }

    public static ArenaNewCareerResult TryCreateNewSlot(int slot, ArenaStartArchetype archetype,
        bool confirmOverwrite)
    {
        int normalized = NormalizeSlotIndex(slot);
        string path = SlotPath(normalized);
        if (File.Exists(path) && !confirmOverwrite)
            return new ArenaNewCareerResult(false,
                $"Slot {normalized} already has a career. Confirm overwrite to start over.",
                normalized, null);

        ArenaCareer career = CreateNewCareer(archetype, StartingLoadoutSeedForSlot(normalized, archetype));
        if (!SaveSlot(career, normalized))
            return new ArenaNewCareerResult(false,
                $"Could not save new {archetype} career to slot {normalized}.",
                normalized, career);

        return new ArenaNewCareerResult(true,
            $"Started {archetype} career in slot {normalized}.",
            normalized, career);
    }

    static int StartingCash(ArenaStartArchetype archetype) => archetype switch
    {
        ArenaStartArchetype.Ace       => 1000,
        ArenaStartArchetype.Wingmates => 700,
        ArenaStartArchetype.Swarm     => 400,
        _                             => 700,
    };

    public static ulong DefaultStartingLoadoutSeed(ArenaStartArchetype archetype)
        => MixStartingSeed(0xA7E2_A5E5_1A2D_0001ul ^ ((ulong)(int)archetype + 1ul) * 0x9E37_79B9_7F4A_7C15ul);

    public static ulong StartingLoadoutSeedForSlot(int slot, ArenaStartArchetype archetype)
        => MixStartingSeed(DefaultStartingLoadoutSeed(archetype)
                         ^ ((ulong)NormalizeSlotIndex(slot) * 0xBF58_476D_1CE4_E5B9ul));

    static IShipDesign[] PickStartingDesigns(ArenaStartArchetype archetype, ulong seed)
    {
        IShipDesign[] pool = StartingStockTier1Pool();
        if (pool.Length == 0)
            return Empty<IShipDesign>.Array;

        IShipDesign[][] candidates = BuildStartingLoadoutCandidates(archetype, pool);
        if (candidates.Length == 0)
            return Empty<IShipDesign>.Array;

        int index = (int)(MixStartingSeed(seed ^ ((ulong)(int)archetype + 1ul) * 0x94D0_49BB_1331_11EBul)
                    % (ulong)candidates.Length);
        return candidates[index];
    }

    static IShipDesign[] StartingStockTier1Pool()
    {
        IShipDesign[] pool = ResourceManager.Ships.Designs
            .Filter(d => ArenaFightScreen.IsStockContentDesign(d)
                         && ArenaFightScreen.IsLegalCombatCraft(d)
                         && ArenaFightScreen.IsDesignAllowedForCareerLevel(d, careerLevel: 0));
        Array.Sort(pool, (a, b) =>
        {
            int c = a.BaseStrength.CompareTo(b.BaseStrength);
            return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
        });
        return pool;
    }

    static IShipDesign[][] BuildStartingLoadoutCandidates(ArenaStartArchetype archetype, IShipDesign[] pool)
    {
        var candidates = new List<IShipDesign[]>();
        IShipDesign[] light = RolePool(pool, RoleName.fighter);
        IShipDesign[] ace = RolePool(pool, RoleName.gunboat, RoleName.corvette);
        if (ace.Length == 0)
            ace = pool;

        switch (archetype)
        {
            case ArenaStartArchetype.Ace:
                AddStartCandidate(candidates, PickByPercentiles(ace, new[] { 0.65f }, 1), 1, 1);
                AddStartCandidate(candidates, PickByPercentiles(ace, new[] { 0.80f }, 1), 1, 1);
                AddStartCandidate(candidates, PickByPercentiles(ace, new[] { 0.95f }, 1), 1, 1);
                AddStartCandidate(candidates, PickByPercentiles(ace, new[] { 1.00f }, 1), 1, 1);
                break;
            case ArenaStartArchetype.Wingmates:
                AddStartCandidate(candidates, PickByPercentiles(pool, new[] { 0.30f, 0.50f }, 2), 2, 3);
                AddStartCandidate(candidates, PickByPercentiles(pool, new[] { 0.25f, 0.45f, 0.65f }, 3), 2, 3);
                AddStartCandidate(candidates, PickByPercentiles(pool, new[] { 0.40f, 0.60f, 0.75f }, 3), 2, 3);
                AddStartCandidate(candidates, RoleBlend(pool,
                    new[] { RoleName.fighter, RoleName.gunboat, RoleName.corvette },
                    new[] { 0.50f, 0.50f, 0.35f }), 2, 3);
                break;
            case ArenaStartArchetype.Swarm:
                AddStartCandidate(candidates, PickByPercentiles(light.Length >= 4 ? light : pool,
                    new[] { 0.00f, 0.12f, 0.24f, 0.36f }, 4), 4, 5);
                AddStartCandidate(candidates, PickByPercentiles(pool,
                    new[] { 0.00f, 0.10f, 0.20f, 0.30f, 0.40f }, 5), 4, 5);
                AddStartCandidate(candidates, PickByPercentiles(pool,
                    new[] { 0.08f, 0.18f, 0.28f, 0.38f }, 4), 4, 5);
                AddStartCandidate(candidates, RoleBlend(pool,
                    new[] { RoleName.fighter, RoleName.fighter, RoleName.gunboat, RoleName.corvette },
                    new[] { 0.10f, 0.35f, 0.20f, 0.10f }), 4, 5);
                break;
            default:
                AddStartCandidate(candidates, PickByPercentiles(pool, new[] { 0.50f }, 1), 1, 1);
                break;
        }

        if (candidates.Count == 0)
            AddStartCandidate(candidates, PickByPercentiles(pool, new[] { 0.50f }, 1), 1, Math.Max(1, pool.Length));
        return candidates.ToArray();
    }

    static IShipDesign[] RolePool(IShipDesign[] pool, params RoleName[] roles)
    {
        var wanted = new HashSet<RoleName>(roles ?? Empty<RoleName>.Array);
        return pool
            .Where(d => wanted.Contains(d.Role) || wanted.Contains(d.HullRole))
            .OrderBy(d => d.BaseStrength)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();
    }

    static IShipDesign[] RoleBlend(IShipDesign[] pool, RoleName[] roles, float[] percentiles)
    {
        var picks = new List<IShipDesign>(roles?.Length ?? 0);
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < (roles?.Length ?? 0); ++i)
        {
            IShipDesign[] rolePool = RolePool(pool, roles[i]);
            if (rolePool.Length == 0)
                rolePool = pool;
            float p = i < (percentiles?.Length ?? 0) ? percentiles[i] : 0.5f;
            int center = (int)Math.Round((rolePool.Length - 1) * Math.Clamp(p, 0f, 1f));
            IShipDesign pick = PickNearestUnused(rolePool, center, used);
            if (pick == null)
                continue;
            picks.Add(pick);
            used.Add(pick.Name);
        }
        return picks.ToArray();
    }

    static void AddStartCandidate(List<IShipDesign[]> candidates, IShipDesign[] picks, int min, int max)
    {
        if (picks == null || picks.Length == 0)
            return;

        IShipDesign[] unique = picks
            .Where(d => d != null)
            .GroupBy(d => d.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToArray();
        if (unique.Length < min || unique.Length > max)
            return;
        if (unique.Any(d => !ArenaFightScreen.IsStockContentDesign(d)
                         || !ArenaFightScreen.IsLegalCombatCraft(d)
                         || ArenaFightScreen.CombatTierForDesign(d) != 1))
            return;

        string signature = string.Join("|", unique.Select(d => d.Name));
        if (candidates.Any(c => string.Equals(signature, string.Join("|", c.Select(d => d.Name)),
                StringComparison.Ordinal)))
            return;
        candidates.Add(unique);
    }

    static IShipDesign[] PickByPercentiles(IShipDesign[] pool, float[] percentiles, int desiredCount)
    {
        if (pool == null || pool.Length == 0 || desiredCount <= 0)
            return Empty<IShipDesign>.Array;

        var picks = new List<IShipDesign>(desiredCount);
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < desiredCount; ++i)
        {
            float p = i < percentiles.Length ? percentiles[i] : percentiles[percentiles.Length - 1];
            int center = (int)Math.Round((pool.Length - 1) * Math.Clamp(p, 0f, 1f));
            IShipDesign pick = PickNearestUnused(pool, center, used) ?? pool[i % pool.Length];
            picks.Add(pick);
            if (pick?.Name != null)
                used.Add(pick.Name);
        }
        return picks.ToArray();
    }

    static IShipDesign PickNearestUnused(IShipDesign[] pool, int center, HashSet<string> used)
    {
        for (int radius = 0; radius < pool.Length; ++radius)
        {
            int lo = center - radius;
            if (lo >= 0 && !used.Contains(pool[lo].Name))
                return pool[lo];
            int hi = center + radius;
            if (hi < pool.Length && !used.Contains(pool[hi].Name))
                return pool[hi];
        }
        return null;
    }

    static ulong MixStartingSeed(ulong z)
    {
        unchecked
        {
            z += 0x9E37_79B9_7F4A_7C15ul;
            z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9ul;
            z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EBul;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Save the career to <paramref name="path"/> (defaults to the real career file).
    /// Creates the directory if missing. Exception-safe (logs + returns false on failure).</summary>
    public static bool Save(ArenaCareer career, string path = null)
    {
        if (career == null)
            return false;
        path = ResolveCareerPath(path ?? DefaultPath);
        try
        {
            career.Version = ArenaCareer.CurrentVersion;
            career.NormalizeForPersistence();

            string dir = Path.GetDirectoryName(path);
            if (dir.NotEmpty())
                Directory.CreateDirectory(dir);

            YamlSerializer.SerializeOne(path, career);
            Log.Info($"ArenaCareer saved: cash={career.Cash} careerLevel={career.CareerLevel} fame={career.Fame} " +
                     $"vessels={career.OwnedVessels.Length} chassis={career.UnlockedChassisStyles.Length} -> {path}");
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"ArenaCareer save FAILED ({path}): {e.Message}");
            return false;
        }
    }

    /// <summary>Load the career from <paramref name="path"/> (defaults to the real career file).
    /// A missing or corrupt file yields a FRESH empty career (logged), never null.</summary>
    public static ArenaCareer Load(string path = null)
    {
        path = ResolveCareerPath(path ?? DefaultPath);
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists)
            {
                Log.Info($"ArenaCareer: no career file at {path}; starting a fresh career.");
                return new ArenaCareer();
            }

            ArenaCareer career = YamlParser.DeserializeOne<ArenaCareer>(fi);
            if (career == null)
            {
                Log.Warning($"ArenaCareer: career file {path} deserialized to null; starting fresh.");
                return new ArenaCareer();
            }
            if (career.Version != ArenaCareer.CurrentVersion)
            {
                Log.Warning($"ArenaCareer: career file {path} version {career.Version} != current " +
                            $"{ArenaCareer.CurrentVersion}; starting fresh.");
                return new ArenaCareer();
            }

            career.NormalizeForPersistence();
            Log.Info($"ArenaCareer loaded: cash={career.Cash} careerLevel={career.CareerLevel} fame={career.Fame} " +
                     $"vessels={career.OwnedVessels.Length} chassis={career.UnlockedChassisStyles.Length} <- {path}");
            return career;
        }
        catch (Exception e)
        {
            Log.Warning($"ArenaCareer load FAILED ({path}); starting a fresh career: {e.Message}");
            return new ArenaCareer();
        }
    }
}

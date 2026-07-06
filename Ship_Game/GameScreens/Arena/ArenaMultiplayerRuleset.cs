using SDUtils.Deterministic;

namespace Ship_Game.GameScreens.Arena;

public enum ArenaMatchMode
{
    Career = 0,   // roster locked to the career's fielded vessels
    Sandbox = 1,  // all legal combat content, within a budget (the "Creative/AllContent" mode)
    Coop = 2,     // reserved (P4) — rejected in P1
}

public enum ArenaBudgetModel
{
    Cap = 0,        // total design cost must be <= BudgetCredits
    Unlimited = 1,  // no budget check
    // FixedBudget reserved (deferred within P1)
}

public enum ArenaRosterSource
{
    CareerLocked = 0, // designs must come from the career's fielded roster
    AllContent = 1,   // any legal combat design
}

/// <summary>
/// RulesetV0 (STARDRIVE_ARENA_MP_MODES_PLAN_20260705 Part C). Authored by the host, carried in the
/// Arena SessionStartMessage, and folded into ArenaMultiplayerSettings.SettingsHash / StartFingerprint
/// in a FIXED field order via DetHash. Every field is flag/YAML-tunable; no balance is locked here.
///
/// P1 populates Career/Sandbox; Coop is reserved and rejected at validation. Wagers stay 0 (P2).
/// </summary>
public sealed class ArenaMultiplayerRuleset
{
    public int Version = 0;                               // distinct from ProtocolVersion
    public ArenaMatchMode Mode = ArenaMatchMode.Career;
    public ArenaBudgetModel BudgetModel = ArenaBudgetModel.Unlimited;
    public int BudgetCredits;                             // meaning depends on BudgetModel; 0 when Unlimited
    public ArenaRosterSource RosterSource = ArenaRosterSource.CareerLocked;
    public int CountdownSeconds = 3;                      // deterministic spawn->engage countdown
    public int MaxMatchSeconds = 600;                     // ruling-2 knob, tunable
    public int MaxFleetShipsPerSide = 32;                 // ruling-2 knob, tunable
    public int WagerCredits;                              // 0 in P1 (P2); non-zero rejected
    public string RosterCommitmentHash = "";             // reserved (honor-system, Q3)
    public string ContentFingerprint = "";               // resolved design/module content fingerprint

    // Host-authored opt-in (custom-fleet UI wiring): when true AND EnableArenaCustomFleet is on, the fight
    // screen enters the in-arena PRE-MATCH SETUP phase (design/import ships + arrange a formation) before the
    // fight spawns. Carried in the authoritative start so BOTH peers agree to enter setup (a divergent value
    // rejects cleanly at the handshake via AppendTo below — never a one-sided setup->spawn desync). Default
    // false => flag-off / legacy launch is unchanged (spawns immediately, as today).
    public bool SetupPhase;

    // Persistent ammo economy (STARDRIVE_ARENA_AMMO_ECONOMY_EXEC_PLAN_20260706). When true (DEFAULT), arena
    // ships spawn full AND regen ordnance exactly as trunk does today — so a flag-off / default match is
    // byte-identical to trunk (the no-op default). When false, ordnance is a FINITE MAGAZINE: regen is
    // suppressed for arena combatants (Ship.ArenaFiniteAmmo) and spent ammo persists + costs cash to rearm.
    // Carried in the authoritative start so BOTH peers agree; folded into SettingsHash via AppendTo below
    // (appended LAST) so a divergent toggle rejects cleanly at the handshake instead of a one-sided
    // finite/infinite desync. Default true keeps the append-only fingerprint stable for legacy launches.
    public bool UnlimitedAmmo = true;

    // Resolved countdown length in SIM TICKS (never wall-clock). 60 Hz. Stored so no float->tick
    // conversion happens per frame.
    public uint CountdownTicks => (uint)(CountdownSeconds < 0 ? 0 : CountdownSeconds) * 60u;

    /// <summary>
    /// Folds the ruleset into a DetHash in a FIXED, documented field order (plan Part 4b). Any field
    /// change alters the hash, so a divergent ruleset fails ValidateStartMessage's SettingsHash check.
    /// </summary>
    public void AppendTo(ref DetHash h)
    {
        h.AddInt(Version);
        h.AddInt((int)Mode);
        h.AddInt((int)BudgetModel);
        h.AddInt(BudgetCredits);
        h.AddInt((int)RosterSource);
        h.AddInt(CountdownSeconds);
        h.AddInt(MaxMatchSeconds);
        h.AddInt(MaxFleetShipsPerSide);
        h.AddInt(WagerCredits);
        h.AddString(RosterCommitmentHash ?? "");
        h.AddString(ContentFingerprint ?? "");
        // Appended LAST (matches the append-only wire/exporter ordering). Folding SetupPhase means two peers
        // that disagree on the setup opt-in fail ValidateStartMessage's SettingsHash check up front instead of
        // one entering setup while the other spawns. Constant-false for flag-off matches (both peers agree).
        h.AddInt(SetupPhase ? 1 : 0);
        // Appended LAST after SetupPhase (append-only ordering, no protocol bump). Folding UnlimitedAmmo
        // means two peers that disagree on the finite-ammo toggle fail ValidateStartMessage's SettingsHash
        // check up front instead of one running a finite magazine while the other regens ordnance. The
        // toggle rides SettingsHash only (like SetupPhase) — the SIM digest is unchanged when UnlimitedAmmo
        // is on (regen still fires), so a default match reproduces trunk's lockstep fingerprint exactly.
        h.AddInt(UnlimitedAmmo ? 1 : 0);
    }

    public ArenaMultiplayerRuleset Clone() => new()
    {
        Version = Version,
        Mode = Mode,
        BudgetModel = BudgetModel,
        BudgetCredits = BudgetCredits,
        RosterSource = RosterSource,
        CountdownSeconds = CountdownSeconds,
        MaxMatchSeconds = MaxMatchSeconds,
        MaxFleetShipsPerSide = MaxFleetShipsPerSide,
        WagerCredits = WagerCredits,
        RosterCommitmentHash = RosterCommitmentHash ?? "",
        ContentFingerprint = ContentFingerprint ?? "",
        SetupPhase = SetupPhase,
        UnlimitedAmmo = UnlimitedAmmo,
    };
}

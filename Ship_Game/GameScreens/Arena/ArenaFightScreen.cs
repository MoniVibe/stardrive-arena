using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using SDUtils;
using Ship_Game.AI;
using Ship_Game.Fleets;
using Ship_Game.Gameplay;
using Ship_Game.Graphics;
using Ship_Game.GameScreens.NewGame;
using Ship_Game.Ships;
using Ship_Game.Universe;
using Ship_Game.Utils;
using SynapseGaming.LightingSystem.Core;
using SynapseGaming.LightingSystem.Lights;
using SynapseGaming.LightingSystem.Rendering;
using Vector2 = SDGraphics.Vector2;
using Rectangle = SDGraphics.Rectangle;
using Matrix = Microsoft.Xna.Framework.Matrix;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using ShipDesignData = Ship_Game.Ships.ShipDesign;

namespace Ship_Game.GameScreens.Arena;

public readonly struct ArenaRepairResult
{
    public readonly bool Success;
    public readonly string Message;
    public readonly int CashBefore;
    public readonly int CashAfter;
    public readonly int RepairedShips;
    public readonly int RestoredModules;

    public ArenaRepairResult(bool success, string message, int cashBefore, int cashAfter,
        int repairedShips, int restoredModules = 0)
    {
        Success = success;
        Message = message ?? "";
        CashBefore = cashBefore;
        CashAfter = cashAfter;
        RepairedShips = Math.Max(0, repairedShips);
        RestoredModules = Math.Max(0, restoredModules);
    }
}

public readonly struct ArenaVesselActivationResult
{
    public readonly bool Success;
    public readonly string Message;
    public readonly string VesselId;
    public readonly string DesignName;
    public readonly int RequiredTier;
    public readonly int CurrentTier;

    public ArenaVesselActivationResult(bool success, string message, string vesselId,
        string designName = "", int requiredTier = 0, int currentTier = 0)
    {
        Success = success;
        Message = message ?? "";
        VesselId = vesselId ?? "";
        DesignName = designName ?? "";
        RequiredTier = Math.Max(0, requiredTier);
        CurrentTier = Math.Max(0, currentTier);
    }
}

public readonly struct ArenaRefitResult
{
    public readonly bool Success;
    public readonly string Message;
    public readonly string VesselId;
    public readonly int SlotIndex;
    public readonly string ModuleUid;
    public readonly int Cost;
    public readonly int CashBefore;
    public readonly int CashAfter;
    public readonly int SalvageBefore;
    public readonly int SalvageAfter;

    public ArenaRefitResult(bool success, string message, string vesselId,
        int slotIndex = -1, string moduleUid = "", int cost = 0, int cashBefore = 0, int cashAfter = 0)
    {
        Success = success;
        Message = message ?? "";
        VesselId = vesselId ?? "";
        SlotIndex = slotIndex;
        ModuleUid = moduleUid ?? "";
        Cost = Math.Max(0, cost);
        CashBefore = Math.Max(0, cashBefore);
        CashAfter = Math.Max(0, cashAfter);
        SalvageBefore = 0;
        SalvageAfter = 0;
    }
}

public readonly struct ArenaDesignInventoryResult
{
    public readonly bool Success;
    public readonly string Message;
    public readonly string DesignName;
    public readonly string MissingModuleUid;
    public readonly int MissingCount;

    public ArenaDesignInventoryResult(bool success, string message, string designName,
        string missingModuleUid = "", int missingCount = 0)
    {
        Success = success;
        Message = message ?? "";
        DesignName = designName ?? "";
        MissingModuleUid = missingModuleUid ?? "";
        MissingCount = Math.Max(0, missingCount);
    }
}

/// <summary>
/// ARENA / gladiator mode screen — a roguelike 3-ROUND RUN rendered through the
/// game's REAL 3D universe view, staged in CLEAN EMPTY SPACE (no stars, no planets
/// in frame).
///
/// PHASE 1 RUN LOOP: the old single duel is now an escalating run. The player
/// gladiator fights round 1 -> 2 -> 3; each round spawns a TOUGHER enemy squad
/// (EnemyCountForRound grows with the round). Clearing a round awards flat cash
/// (CashPerClear) and — if the run isn't over — opens a MINIMAL SHOP (Repair Hull /
/// Hire Wingman / Next Fight) before the next, harder round. Clearing round 3 wins
/// the run (legendary gladiator -> OnPlayerWon); losing the player ship at any point
/// ends the run (OnPlayerDefeated). Hull attrition PERSISTS between rounds (your ship
/// carries its damage forward) but shields recharge to full each round — so the
/// Repair / Wingman shop buys are what keep a long run survivable.
///
/// The per-round enemy spec + cash reward are exposed as STATIC/shared members
/// (EnemyCountForRound, CashPerClear, TotalRounds, RepairCost, WingmanCost) so the
/// headless run test (ArenaRoguelikeRun_Headless) exercises the REAL escalation, not
/// a copy.
///
/// This is the interactive game-client counterpart to the headless arena render +
/// run smoke tests (UnitTests/Graphics/ArenaRenderSmokeTests.cs). The smoke tests
/// proved the render path works headless and the run resolves; this screen wires the
/// SAME run into the live client so it can be SEEN and played:
///
///   1. Build a sandbox universe (CreateEmpire per race + a SolarSystem per empire)
///      so the empires get HOME PLANETS — UniverseScreen.InitializeCamera and
///      CreateStartingShips both index empire.GetPlanets()[0], so a player home
///      system MUST exist or base.LoadContent() throws. Those systems live in the
///      central random-placement box (±(Size-100k) per axis); we never frame them.
///   2. In LoadContent (AFTER base.LoadContent() set up lighting) stage the run in
///      DEEP SPACE — a fixed ARENA_CENTER far OUTSIDE the system box (a corner beyond
///      ±(Size-100k)), so NO solar system can ever fall inside the camera frustum and
///      no star/planet is visible. Systems render only when InFrustum + explored, so
///      out-of-frame == invisible.
///   3. LIGHT THE ARENA WITHOUT A STAR: the per-system Key/LocalFill PointLights are
///      hundreds of thousands of units away from the deep-space arena, so they don't
///      reach the hulls. Instead we (a) raise the scene Environment ambient and
///      (b) add a synthetic Static PointLight at ARENA_CENTER. LightingEffectBinder
///      picks the closest Static PointLight (radius in [1k,1M)) as the scene "sun"
///      and binds its 3 point-light slots — so the synthetic light lights the ships
///      exactly like a system sun would, with real directional shading. Proven
///      non-black by ArenaEmptySpace_Render_ToPng_NonBlack in the smoke tests.
///   4. Make the two empires hostile (SetRelationsAsKnown + DeclareWarOn) and order
///      every ship to attack the nearest hostile (ShipAI.OrderAttackSpecificTarget).
///   5. Run via the NORMAL UniverseScreen.Update (base.Update ticks the sim and the
///      3D render). Each frame we run the RUN STATE MACHINE: all enemies dead ->
///      round cleared (award cash, open shop or win); player ship dead -> run over.
///
/// NOT RUNTIME-VERIFIED: a live interactive GameScreen can only be confirmed by
/// launching the actual game and watching the run resolve. This file is verified to
/// COMPILE and the wiring (universe creation, ship spawn, deep-space lighting,
/// hostility, attack orders, round escalation, shop, win/lose detection) is sound
/// against the current engine APIs, and the run loop's escalation + resolution is
/// proven headless by ArenaRoguelikeRun_Headless.
///
/// HOW TO REACH IT FROM A MENU: ArenaFightScreen.Create() returns a ready screen.
/// The main menu wires this up next to the Developer Sandbox button:
/// MainMenuScreen.LoadContent does `if (list.Find("arena", out UIButton arena))
/// arena.OnClick = Arena_Clicked;` and Arena_Clicked => ScreenManager.GoToScreen(
/// ArenaFightScreen.Create(), clear3DObjects: true). The "arena" button ENTRY lives in
/// the layout YAML (MMenu.*.yaml) mirroring the "sandbox" entry; if that entry is
/// absent (e.g. a fresh checkout without local content) list.Find returns false and
/// no arena button appears — harmless.
/// </summary>
public sealed partial class ArenaFightScreen : UniverseScreen
{
    static readonly MethodInfo LoadDesignOnOpenMethod = typeof(ShipDesignScreen)
        .GetMethod("LoadDesignOnOpen", BindingFlags.Instance | BindingFlags.Public,
            null, new[] { typeof(IShipDesign) }, null);

    // The two sides of the run. We hold the live Ship refs so we can re-issue
    // attack orders as ships die and detect the round-clear / run-over conditions.
    // PlayerShips PERSISTS across rounds (the gladiator carries hull attrition
    // forward); EnemyShips is rebuilt each round with a tougher squad.
    readonly List<Ship> PlayerShips = new();
    readonly List<Ship> EnemyShips  = new();

    Empire ArenaPlayer;
    Empire ArenaEnemy;

    // The arena gladiator roster as a real Fleet (built from the hub's FLEET button) so the
    // persistent roster spawns as a formation next round. Fixed fleet slot, arena-scoped.
    Fleet PlayerFleet;
    const int ArenaFleetId = 1;

    // ---- RUN STATE MACHINE ---------------------------------------------------------
    // Round is an open-ended bout counter. Cash is the run currency spent in the shop.
    // The run is in exactly one phase at a time: Fighting (sim runs, win/lose checked)
    // or Shopping (sim paused, hub/shop shown between bouts).
    enum RunPhase { Idle, Fighting, Shopping, Over }
    int Round = 1;
    int Cash;
    int PendingWingmen; // extra player warships bought in the shop, spawned next round
    RunPhase Phase = RunPhase.Fighting;
    bool RunStarted;
    bool AdvanceRoundOnNextFight;
    readonly bool StartAtHub;
    float RetargetTimer;

    // The chosen designs are picked ONCE at run start and reused every round so the
    // player keeps fighting "the same" gladiator and the enemy escalation is purely
    // in COUNT (squad size grows with the round), not in random design churn.
    IShipDesign PlayerDesign;
    IShipDesign EnemyDesign;
    string PendingChallengeContenderName;
    string PendingChallengeDesignName;
    FightOption PendingFightOption;
    FightOption ActiveFightOption;
    ArenaPerkDefinition[] PendingBossPerkChoices = Array.Empty<ArenaPerkDefinition>();
    ulong FightModifierSeed;

    // ---- PLAYER-CHOSEN GLADIATOR ---------------------------------------------------
    // When the player customizes/picks a gladiator design (via the 'Customize Gladiator'
    // button -> designer/picker), its design Name is captured here. At player-pick time
    // PickPlayerWarship honors this: if it names a REAL warship that exists in
    // ResourceManager.Ships, THAT design becomes the gladiator; otherwise we fall back to
    // the deterministic auto-pick (so a missing/illegal choice is safe and the unaided
    // headless run stays deterministic).
    //
    // ChosenPlayerDesignName is the live instance field the screen consults.
    // PendingPlayerDesignName is a static "intended design" a HEADLESS TEST (or an
    // external caller) can set BEFORE LoadContent so the very first gladiator pick honors
    // it — LoadContent copies it into ChosenPlayerDesignName, then clears it.
    public string ChosenPlayerDesignName;
    public static string PendingPlayerDesignName;

    // ---- SHARED / STATIC ESCALATION SPEC (the headless run test drives THESE) ------
    // Legacy escalation horizon used by the old helper curves. It no longer ends a run:
    // the live career is an open-ended roulette of selected/scaled fights.
    public const int TotalRounds = 3;
    // Flat cash awarded for clearing any round. Sized so an INVESTED run can field a few
    // wingmen per intermission (WingmanCost) to meet the escalating threat — enough to bring
    // a wing big enough to take down the round-3 mini-boss and win the run — while a
    // no-investment run that banks the cash still loses the round-3 boss.
    public const int CashPerClear = 300;
    // Shop costs.
    public const int RepairCost  = 50;
    public const int WingmanCost = 100;
    public const int FleetSlotUpgradeBaseCost = 500;
    public const int FleetSlotUpgradeStepCost = 150;
    // 'Unlock Chassis' shop buy: spend this to unlock the NEXT foreign faction's
    // warship hulls (they then appear in the Customize designer and spawn fine).
    public const int UnlockChassisCost = 150;

    // ---- FOREIGN CHASSIS UNLOCK (run-scoped) ---------------------------------------
    // The set of foreign faction styles already granted to the player this run. Used so
    // GrantNextChassis advances through ForeignChassisFactions() one faction at a time
    // and the shop button can show the NEXT faction (or "all unlocked").
    readonly HashSet<string> GrantedChassisFactions = new();

    // ---- CAREER PERSISTENCE (Phase A) ----------------------------------------------
    // The CAREER loaded on Create()/LoadContent() — banked cash, owned vessels (with
    // veterancy), unlocked chassis styles, and a win count (CareerLevel) that scales
    // difficulty. A fresh career (level 0, no vessels) makes the run behave like today's
    // default. The run snapshots back into Career on win/defeat (BankCareer) and
    // CareerManager.Save() persists it. CareerSavePath lets a TEST redirect the save to a
    // temp path so the real career file is never touched; null => the real career file.
    ArenaCareer Career = new();
    public static string CareerSavePath; // null => CareerManager.DefaultPath (real file)
    public static int ActiveCareerSlot = CareerManager.MinSlot;

    public static void UseCareerSlot(int slot)
    {
        ActiveCareerSlot = CareerManager.NormalizeSlotIndex(slot);
        CareerSavePath = CareerManager.SlotPath(ActiveCareerSlot);
    }

    public static ArenaFightScreen CreateForCareerSlot(int slot, string playerPreference = "United",
        int generationSeed = 0, bool startAtHub = true)
    {
        UseCareerSlot(slot);
        return Create(playerPreference, generationSeed, startAtHub);
    }

    // The career's persisted win count drives a per-level difficulty bump applied in
    // LoadContent: each banked win adds enemies + boss strength to the run.
    int CareerLevel => Career?.CareerLevel ?? 0;

    public ArenaFightModifier CurrentFightModifier { get; private set; } = ArenaFightModifier.None;
    public ArenaBossEncounter CurrentBossEncounter { get; private set; } = ArenaBossEncounter.None;
    public FightOption CurrentFightOption => ActiveFightOption;
    public FightOption QueuedFightOption => PendingFightOption;
    bool CareerPerksApplied;

    // Hull Roles that count as a real combat WARSHIP class (size-ordered subset of
    // RoleName). Non-warship hull roles (fighter/freighter/platform/station/etc.) are
    // excluded so 'Unlock Chassis' only ever grants spawnable warship hulls.
    static readonly RoleName[] WarshipHullRoles =
    {
        RoleName.gunboat, RoleName.corvette, RoleName.frigate,
        RoleName.destroyer, RoleName.cruiser, RoleName.battleship, RoleName.capital,
    };

    static readonly RoleName[] Tier1CombatRoles =
    {
        RoleName.fighter, RoleName.gunboat, RoleName.corvette,
    };

    static readonly RoleName[] Tier2CombatRoles =
    {
        RoleName.frigate, RoleName.destroyer,
    };

    static readonly RoleName[] Tier3CombatRoles =
    {
        RoleName.cruiser, RoleName.battleship, RoleName.capital,
    };

    public const int MinCombatTier = 1;
    public const int MaxCombatTier = 3;

    // PUBLIC static so the headless dealership-catalog guard asserts against the SAME role
    // whitelist the live catalog/picks use (gunboat..capital; excludes fighter/scout/etc.).
    public static bool IsWarshipHullRole(RoleName role)
    {
        for (int i = 0; i < WarshipHullRoles.Length; ++i)
            if (WarshipHullRoles[i] == role) return true;
        return false;
    }

    public static int CombatTierForRole(RoleName role)
    {
        if (role == RoleName.fighter || role == RoleName.gunboat || role == RoleName.corvette)
            return 1;
        if (role == RoleName.frigate || role == RoleName.destroyer)
            return 2;
        if (role == RoleName.cruiser || role == RoleName.battleship || role == RoleName.capital)
            return 3;
        return 0;
    }

    public static int MaxAllowedCombatTierForCareerLevel(int careerLevel)
    {
        int level = Math.Max(0, careerLevel);
        if (level <= 2) return 1;
        if (level <= 6) return 2;
        return 3;
    }

    public static bool IsTierAllowedForCareerLevel(int tier, int careerLevel)
        => tier >= MinCombatTier && tier <= MaxAllowedCombatTierForCareerLevel(careerLevel);

    public static int CombatTierForDesign(IShipDesign design)
    {
        if (design == null)
            return 0;
        return Math.Max(CombatTierForRole(design.Role), CombatTierForRole(design.HullRole));
    }

    public static bool IsDesignAllowedForCareerLevel(IShipDesign design, int careerLevel)
        => design != null && IsTierAllowedForCareerLevel(CombatTierForDesign(design), careerLevel);

    public static RoleName[] CombatRolesUpToTier(int maxTier)
    {
        int tier = Math.Clamp(maxTier, MinCombatTier, MaxCombatTier);
        var roles = new List<RoleName>(Tier1CombatRoles);
        if (tier >= 2) roles.AddRange(Tier2CombatRoles);
        if (tier >= 3) roles.AddRange(Tier3CombatRoles);
        return roles.ToArray();
    }

    // Shared/non-faction hull styles that are NOT a buyable "foreign chassis": the
    // cross-faction pools (Platforms/Misc), the neutral/test/environment styles, and
    // anything empty. The player's OWN style (Terran/Human) is excluded separately,
    // against the live player empire, so it's never offered as an unlock.
    static readonly HashSet<string> SharedOrNonFactionStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "Platforms", "Misc", "Meteors", "Shared", "Test", "Remnant",
    };

    /// <summary>
    /// CORE LOGIC (headless-testable): the ORDERED list of FOREIGN faction styles whose
    /// WARSHIP chassis the player can unlock — i.e. every distinct hull Style present in
    /// ResourceManager.Hulls that (a) has at least one real WARSHIP hull, (b) is NOT a
    /// shared/non-faction/test/environment style, and (c) is NOT the PLAYER's own style.
    /// Deterministic order (ordinal by style name) so the test and the live game agree on
    /// which faction "comes next". Static so the headless proof drives the REAL list.
    /// </summary>
    public static string[] ForeignChassisFactions(Empire player)
    {
        var styles = new SortedSet<string>(StringComparer.Ordinal);
        foreach (ShipHull hull in ResourceManager.Hulls)
        {
            if (hull == null) continue;
            string style = hull.Style;
            if (style.IsEmpty()) continue;
            if (!IsWarshipHullRole(hull.Role)) continue;       // only warship chassis
            if (SharedOrNonFactionStyles.Contains(style)) continue; // skip shared/neutral
            if (player != null && player.ShipStyleMatch(style)) continue; // skip player's own
            styles.Add(style);
        }
        var result = new string[styles.Count];
        styles.CopyTo(result);
        return result;
    }

    /// <summary>
    /// Grant EVERY warship hull of a specific foreign faction style to the empire so the
    /// hulls appear in GetUnlockedHulls() (the live Customize designer reads that) and are
    /// spawnable. Uses the unlock primitive UnlockEmpireHull then UpdateShipsWeCanBuild so
    /// the hulls are both unlocked AND buildable. Public static so the headless test drives
    /// the REAL grant. Returns the number of warship hulls granted for that faction.
    /// </summary>
    public static int GrantChassis(Empire empire, string factionStyle)
    {
        if (empire == null || factionStyle.IsEmpty())
            return 0;

        var granted = new Array<string>();
        foreach (ShipHull hull in ResourceManager.Hulls)
        {
            if (hull == null) continue;
            if (!IsWarshipHullRole(hull.Role)) continue;
            if (!string.Equals(hull.Style, factionStyle, StringComparison.Ordinal)) continue;
            empire.UnlockEmpireHull(hull.HullName);
            granted.Add(hull.HullName);
        }
        if (granted.Count > 0)
            empire.UpdateShipsWeCanBuild(granted);
        return granted.Count;
    }

    /// <summary>
    /// Run-scoped unlock action: grant the NEXT not-yet-unlocked foreign faction's warship
    /// hulls to the empire, track it as granted, and return the faction style granted (or
    /// null when every foreign faction is already unlocked). The live shop calls this; the
    /// headless proof drives GrantChassis directly with a chosen faction.
    /// </summary>
    public string GrantNextChassis(Empire empire)
    {
        if (empire == null)
            return null;
        foreach (string style in ForeignChassisFactions(empire))
        {
            if (GrantedChassisFactions.Contains(style))
                continue;
            GrantChassis(empire, style);
            GrantedChassisFactions.Add(style);
            return style;
        }
        return null;
    }

    // The next foreign faction the player WOULD unlock (or null when all done) — used to
    // label the shop button. Does not mutate state.
    string NextChassisFaction(Empire empire)
    {
        if (empire == null)
            return null;
        foreach (string style in ForeignChassisFactions(empire))
            if (!GrantedChassisFactions.Contains(style))
                return style;
        return null;
    }

    /// <summary>
    /// ESCALATION (COUNT): total enemy squad size for a given round (1-based). The curve is
    /// tuned so the run can actually be LOST against the tier-ramped default, while
    /// staying winnable with investment:
    ///   round 1 = 3   (comfortable: a few squishy light craft)
    ///   round 2 = 4   (real: a stronger pack that bloodies an un-upgraded craft)
    ///   round 3 = 6   (dangerous: a MINI-BOSS + 5 escorts — a worn, un-repaired craft is
    ///                  OVERWHELMED and wiped here; an invested
    ///                  run that brought wingmen + repairs wins)
    /// Strictly growing, so each round is provably harder than the last. On round 3 ONE slot
    /// is a mini-boss (see MiniBossCountForRound); the rest are escorts. Exposed as a static
    /// so the headless run test exercises the REAL escalation curve.
    /// </summary>
    public static int EnemyCountForRound(int round) => round <= 1 ? 3 : (round == 2 ? 4 : 6);

    /// <summary>
    /// PERSISTED DIFFICULTY SCALING: the per-round squad size for a run launched from a
    /// career with <paramref name="careerLevel"/> banked wins. A FRESH career (level 0)
    /// returns exactly <see cref="EnemyCountForRound"/> (today's default run, what the
    /// existing tests assert). Each banked win adds one enemy per round, so a veteran's run
    /// is a persisted, escalating challenge. Static + pure so a test drives the REAL curve.
    /// </summary>
    public static int CareerEnemyCountForRound(int round, int careerLevel)
        => EnemyCountForRound(round) + Math.Max(0, careerLevel);

    /// <summary>
    /// PERSISTED DIFFICULTY SCALING (boss strength): the boss strength percentile for a run
    /// launched from a career with <paramref name="careerLevel"/> wins. Level 0 returns the
    /// default <see cref="BossStrengthPercentile"/> (1.0); the curve is already at its peak
    /// percentile so this is a clamp-safe pass-through that a future tuning pass can lower
    /// the base of. Kept as a named hook so the difficulty lever is explicit and testable.
    /// </summary>
    public static float CareerBossStrengthPercentile(int careerLevel)
        => Math.Clamp(BossStrengthPercentile, 0f, 1f);

    /// <summary>
    /// ESCALATION (CLASS): how many of a round's squad are MINI-BOSS hulls — a bigger,
    /// tougher warship (cruiser-tier, see PickEnemyBoss) rather than a small escort. Only
    /// the FINAL round fields a mini-boss (1), so round 3 is a genuine "boss + escorts"
    /// fight that endangers a no-investment cruiser default. Earlier rounds field 0 (all
    /// escorts). Static so the headless run test drives the REAL boss escalation.
    /// </summary>
    public static int MiniBossCountForRound(int round) => MiniBossCountForRound(round, careerLevel: 0);

    public static int MiniBossCountForRound(int round, int careerLevel)
        => round >= TotalRounds && MaxAllowedCombatTierForCareerLevel(careerLevel) >= 3 ? 1 : 0;

    /// <summary>
    /// ESCALATION (ESCORT CLASS): the priority list of escort hull classes for a round.
    /// The escort class ITSELF escalates: round 1 = corvettes (squishy), round 2 = frigates
    /// (a real step up), round 3 = destroyers (so even the escorts around the mini-boss are
    /// dangerous). Each tier is a priority list so PickEnemyEscort falls back to a smaller
    /// class on a sparse roster. Role-based, so it holds for vanilla AND Combined Arms.
    /// </summary>
    public static RoleName[] EnemyEscortRolesForRound(int round)
        => EnemyEscortRolesForRound(round, careerLevel: 0);

    public static RoleName[] EnemyEscortRolesForRound(int round, int careerLevel)
    {
        int tier = MaxAllowedCombatTierForCareerLevel(careerLevel);
        if (tier <= 1)
        {
            if (round <= 1)
                return new[] { RoleName.fighter, RoleName.gunboat, RoleName.corvette };
            if (round == 2)
                return new[] { RoleName.fighter, RoleName.corvette, RoleName.gunboat };
            return new[] { RoleName.gunboat, RoleName.corvette, RoleName.fighter };
        }

        if (tier == 2)
        {
            if (round <= 1)
                return new[] { RoleName.corvette, RoleName.gunboat, RoleName.fighter };
            if (round == 2)
                return new[] { RoleName.frigate, RoleName.corvette, RoleName.gunboat };
            return new[] { RoleName.destroyer, RoleName.frigate, RoleName.corvette };
        }

        if (round <= 1)
            return new[] { RoleName.frigate, RoleName.destroyer, RoleName.corvette };
        if (round == 2)
            return new[] { RoleName.cruiser, RoleName.destroyer, RoleName.frigate };
        return new[] { RoleName.cruiser, RoleName.destroyer, RoleName.battleship, RoleName.capital };
    }

    // The MINI-BOSS class priority (round 3, tier 3+ careers): a peak tier-3 combat craft
    // so a no-investment run is in real danger. Role-based, so it holds data-agnostically.
    static readonly RoleName[] MiniBossRoles =
    {
        RoleName.cruiser, RoleName.battleship, RoleName.capital, RoleName.destroyer,
    };

    // The boss is the PEAK hull of its tier (100th-strength-percentile) — the strongest
    // cruiser/battleship the roster offers — so it decisively OVERWHELMS a worn, un-repaired
    // lone ship and a no-investment run LOSES round 3. An invested run that banked the
    // (richer) clear cash into wingmen + full repairs brings enough hulls to still win.
    public const float BossStrengthPercentile = 1.0f;

    // Distinct boss ENCOUNTERS are tier-3-only payoff fights: a named single capital/remnant
    // threat staged mid-run so the hub can offer a deterministic perk reward after it falls.
    public const int BossEncounterRound = 2;
    public const int BossEncounterCareerLevelPeriod = 3;
    public const float BossEncounterStrengthPercentile = 0.65f;
    public const float BossEncounterStrengthMultiplier = 2.25f;
    public const float BossEncounterHealthMultiplier = 1.0f;

    // How far apart the two sides spawn, and how the enemy squad is spread out.
    const float Gap = 3500f;
    const float RowSpan = 1400f;
    const float RetargetInterval = 2f; // seconds between re-issuing attack orders
    const float ArenaRearmFractionPerSecond = 0.35f;
    const float ArenaRearmMinOrdnancePerSecond = 25f;

    public const float ArenaLocalRearmRadius = 12_000f;
    public const float ArenaHardBoundsRadius = 18_000f;

    // Radius of the universe we build (matches the value handed to base()).
    const float UniverseRadius = 1_000_000f;

    // DEEP-SPACE arena center. Solar systems are random-placed in the box
    // [-(Size-100k), +(Size-100k)] per axis (DeveloperUniverse/GenerateRandomSysPos),
    // i.e. within ±900k. Camera XY is clamped to ±Size (±1,000,000) in
    // UniverseScreen.Camera. So a corner at ±950k is (a) inside the camera clamp and
    // (b) GUARANTEED ≥50k from any possible system on each axis — far outside any
    // realistic camera frustum at our ~12k cam height. Result: no star/planet ever
    // falls in frame. We stage the whole run around this point.
    static readonly Vector2 ArenaCenter = new(UniverseRadius - 50_000f, UniverseRadius - 50_000f);

    // Synthetic "arena sun": a Static PointLight at the arena center. With no nearby
    // solar system, the per-system Key/LocalFill PointLights are hundreds of thousands
    // of units away and never reach the hulls. LightingEffectBinder selects the
    // closest Static PointLight whose Radius is in [1000, 1_000_000) as the scene
    // "sun" and binds its slots — so this single light lights the duel exactly like a
    // system sun would (per-pixel falloff + directional shading), with no star in view.
    const float ArenaLightRadius    = 120_000f; // well within the binder's [1k,1M) window
    const float ArenaLightIntensity = 2.2f;     // bright enough to clearly light the hulls
    const float ArenaLightZ         = -50_000f; // overhead, mirroring the system Key light Z

    // ---- MINIMAL SHOP / HUD (between rounds) ----------------------------------------
    // HUD labels are DynamicText so they always reflect the live Round/Cash. The shop
    // panel + its buttons are created once and toggled Visible between rounds.
    UILabel RoundLabel;
    UILabel CashLabel;
    UILabel GladiatorLabel;
    UIPanel ShopPanel;
    UILabel ShopTitle;
    UIButton RepairButton;
    UIButton WingmanButton;
    UIButton UnlockChassisButton; // 'Unlock Chassis' buy (unlocks a foreign faction's hulls)
    UIButton CustomizeButton;     // in the shop panel (between rounds)
    UIButton CustomizeHudButton;  // always-on HUD button (reachable before round 1)
    UIButton NextFightButton;
    UILabel ChassisLabel;         // HUD: count of foreign chassis unlocked this run

    ArenaFightScreen(UniverseParams settings, float universeSize, bool startAtHub) : base(settings, universeSize)
    {
        StartAtHub = startAtHub;
        UState.NoEliminationVictory = true; // we drive win/lose ourselves
        UState.Paused = false;
    }

    /// <summary>
    /// Builds the sandbox universe for the arena and returns a ready-to-show screen.
    /// Mirrors DeveloperUniverse.Create — same empire/system generation — so the
    /// normal universe update + 3D render path is fully wired.
    ///
    /// We STILL generate one SolarSystem per empire here even though the arena is
    /// fought in empty space: UniverseScreen.InitializeCamera and CreateStartingShips
    /// (both run inside base.LoadContent) index empire.GetPlanets()[0], so the player
    /// empire MUST own a home planet or base.LoadContent throws. Those systems are
    /// placed in the central random box (±(Size-100k)); the arena is staged far away
    /// at ArenaCenter so none of them is ever in frame. (Truly zero-systems would
    /// require patching the private InitializeCamera/CreateStartingShips paths; the
    /// far-deep-space staging gets the same clean-empty-space result with no risk.)
    /// </summary>
    /// <param name="playerPreference">archetype/name to pick the player race by (e.g. "United").</param>
    /// <param name="generationSeed">
    /// OPTIONAL reproducibility seed. 0 (default) keeps the live behavior: the AI opponent race and
    /// its home-system generation are clock-seeded, so every arena run is fresh. A NON-ZERO seed makes
    /// the WHOLE Create() generation deterministic — the opponent shuffle AND the system-generation
    /// random are seeded from it — so the enemy empire (and therefore the enemy ship build / HP /
    /// strength) is bit-reproducible run-to-run. Headless tests pass a fixed seed so the round-1
    /// matchup always resolves within budget regardless of test order; production passes 0.
    /// </param>
    public static ArenaFightScreen Create(string playerPreference = "United", int generationSeed = 0,
        bool startAtHub = false, string opponentPreference = "")
    {
        var s = Stopwatch.StartNew();
        ScreenManager.Instance.ClearScene();

        var settings = new UniverseParams { GenerationSeed = generationSeed };
        var arena = new ArenaFightScreen(settings, UniverseRadius, startAtHub);
        UniverseState us = arena.UState;

        // Reproducible generation is an optional engine capability. Patched engines get seeded
        // arena setup; stock engines simply keep their normal RNG and still run the Arena.
        if (generationSeed != 0)
            ArenaEngineCapabilities.TryEnableSeededRng(us,
                (ulong)(uint)generationSeed | 0xA12E_0000_0000_0000ul);
        ArenaEngineCapabilities.TrySeedGeneration(generationSeed);

        IEmpireData[] candidates = ResourceManager.MajorRaces.Filter(d => PlayerFilter(d, playerPreference));
        IEmpireData player = candidates[0];
        IEmpireData[] opponents = ResourceManager.MajorRaces.Filter(d => d.ArchetypeName != player.ArchetypeName);
        if (opponentPreference.NotEmpty())
        {
            IEmpireData[] preferredOpponents = opponents.Filter(d => PlayerFilter(d, opponentPreference));
            if (preferredOpponents.Length > 0)
                opponents = preferredOpponents;
        }

        var races = new Array<IEmpireData>(opponents);
        races.Shuffle(generationSeed); // seed==0 -> clock-random (live); non-zero -> reproducible opponent
        races.Resize(Math.Min(races.Count, 1)); // exactly one AI opponent for the run
        races.Insert(0, player);

        // seed==0 -> clock-seeded (live, fresh each run); non-zero -> deterministic system generation,
        // so the enemy empire + its ship build is reproducible (kills the headless round-1 flake).
        var random = new SeededRandom(generationSeed);
        foreach (IEmpireData data in races)
        {
            Empire e = us.CreateEmpire(data, isPlayer: (data == player), difficulty: GameDifficulty.Hard);
            e.data.CurrentAutoScout       = e.data.ScoutShip;
            e.data.CurrentAutoColony      = e.data.ColonyShip;
            e.data.CurrentAutoFreighter   = e.data.FreighterShip;
            e.data.CurrentConstructor     = e.data.ConstructorShip;
            e.data.CurrentResearchStation = e.data.ResearchStation;
            e.data.CurrentMiningStation   = e.data.MiningStation;

            var system = new SolarSystem(us, GenerateRandomSysPos(us, random));
            system.GenerateRandomSystem(us, random, e.data.Traits.HomeSystemName, e);
            system.OwnerList.Add(e);
            us.AddSolarSystem(system);
        }

        foreach (IEmpireData data in ResourceManager.MinorRaces)
            us.CreateEmpire(data, isPlayer: false, difficulty: GameDifficulty.Hard);

        foreach (SolarSystem system in us.Systems)
            system.FiveClosestSystems = us.GetFiveClosestSystems(system);

        ShipDesignUtils.MarkDesignsUnlockable();
        Log.Info($"ArenaFightScreen.Create elapsed:{s.Elapsed.TotalMilliseconds}");
        return arena;
    }

    public override void LoadContent()
    {
        // base.LoadContent() runs ResetLighting() + sets up the scene Environment,
        // so the spawned ship meshes light correctly. Spawn AFTER it.
        base.LoadContent();

        // Honor an externally-supplied intended gladiator design (set by a headless
        // test or a caller BEFORE the screen loads). Once consumed it's cleared so it
        // can't leak into a later run.
        if (PendingPlayerDesignName.NotEmpty())
        {
            ChosenPlayerDesignName = PendingPlayerDesignName;
            PendingPlayerDesignName = null;
        }

        ArenaPlayer = UState.Player;
        ArenaEnemy  = UState.NonPlayerEmpires.Length > 0 ? UState.NonPlayerEmpires[0] : null;
        if (ArenaPlayer == null || ArenaEnemy == null)
        {
            Log.Warning("ArenaFightScreen: missing player or enemy empire; cannot start arena.");
            return;
        }

        // CAREER RELOAD (Phase A): load the persisted arena career and apply it to this run —
        // starting cash, re-granted unlocked chassis, and (if the career owns a gladiator) the
        // veteran's design as the player's gladiator. A FRESH career applies nothing, so the
        // unaided run stays today's default. An explicitly pre-set Career (e.g. by a headless
        // test) is honored as-is (we don't clobber it by re-loading from disk). Done BEFORE
        // PickPlayerWarship so the adopted gladiator design is honored at pick time.
        if (Career == null || Career.IsFresh)
            Career = CareerManager.Load(CareerSavePath);
        CareerLadder.EnsureContenders(Career);
        FightModifierSeed = ModifierSeedForCareer(Career);

        // PHASE B — FINITE OWNED ROSTER: a FRESH career (no owned vessels) is granted a STARTER
        // owned vessel — the deterministic tier-1 light craft the run would auto-pick — made
        // ACTIVE, so the gladiator is ALWAYS one the player OWNS. The starter design IS the
        // auto-pick, so a fresh career still fields the same sensible craft (default behavior
        // + the existing tests are preserved), it's just now a tracked owned vessel.
        EnsureStarterVessel();
        ApplyCareer();

        // CLEAN EMPTY SPACE: stage the run at ArenaCenter, a deep-space corner FAR
        // outside the random system-placement box, so no star/planet is ever in frame.
        Vector2 center = ArenaCenter;

        // LIGHT WITHOUT A STAR. base.LoadContent()'s ResetLighting only set up the
        // dim Global Fill/Back lights + a small scene ambient + per-system suns (which
        // are ~900k+ units away from our deep-space arena and never reach the hulls).
        // So we light the arena ourselves: drop a synthetic Static PointLight at the
        // arena center. LightingEffectBinder picks the closest Static PointLight
        // (radius in [1000, 1_000_000)) as the scene "sun" and binds its slots, so
        // this single light shades the hulls exactly like a system sun would (and the
        // binder's MinAmbient=0.3 floor keeps shadow-side faces from going pure black).
        // This is the same proven lever as the smoke test's "Spike Preview Light".
        var arenaSun = new PointLight
        {
            Name            = "Arena Sun",
            DiffuseColor    = Color.White.ToVector3(),
            Intensity       = ArenaLightIntensity,
            ObjectType      = ObjectType.Static, // binder only picks Static lights as the sun
            FillLight       = false,
            Radius          = ArenaLightRadius,
            Position        = new XnaVector3(center.X, center.Y, ArenaLightZ),
            Enabled         = true,
            FalloffStrength = 1f,
        };
        arenaSun.World = Matrix.CreateTranslation(arenaSun.Position);
        AddLight(arenaSun, dynamic: false);

        // Gladiator run: the PLAYER gets one RIGHT-SIZED cruiser-tier warship (a solid
        // heavy fighter with headroom). The ENEMY escalates in BOTH count AND class per
        // round — round 1 a few small escorts, round 2 a bigger frigate pack, round 3 a
        // MINI-BOSS bigger warship + destroyer escorts — so a no-investment run is in real
        // danger by round 3 (LOSABLE) while an invested run wins. EnemyDesign holds the
        // round-1 escort design for HUD/log; per-round escort + boss are resolved in
        // SpawnEnemyRound. Proven headless by ArenaRoguelikeRun_Headless.
        PlayerDesign = PickPlayerWarship(ArenaPlayer);
        EnemyDesign  = PickEnemyEscort(ArenaEnemy, round: 1, CareerLevel);
        if (PlayerDesign == null || EnemyDesign == null)
        {
            Log.Warning("ArenaFightScreen: no suitable warship designs found; cannot start arena.");
            return;
        }

        // Make the two sides hostile ONCE for the whole run.
        Empire.SetRelationsAsKnown(ArenaPlayer, ArenaEnemy);
        if (!ArenaPlayer.IsAtWarWith(ArenaEnemy))
            ArenaPlayer.AI.DeclareWarOn(ArenaEnemy, WarType.ImperialistWar);

        // FIX: NO PEACE / DIPLOMACY OFFERS IN THE ARENA. The unwanted peace popup comes
        // from the AI enemy's per-turn diplomacy: Empire.UpdateRelationships runs
        // Relationship.AdvanceRelationshipTurn for each empire ONLY when `!IsFaction`
        // (Empire_Relationship.cs), and that turn is what drives RequestPeace ->
        // OfferPeace -> DiplomacyScreen.Show. Marking the arena enemy as a FACTION is the
        // cleanest lever: it skips the enemy's diplomacy turn entirely (so it can NEVER
        // generate a peace/diplomacy offer to the player) while KEEPING combat fully
        // intact — factions are hostile-by-default and CanWeAttackThem returns true for a
        // faction (Relationship.cs), and the AtWar relationship + ActiveWar we just
        // declared persist regardless of the flag (IsAtWarWith reads AtWar, not IsFaction).
        // We set the flag AFTER DeclareWarOn because DeclareWarOn early-returns when the
        // target is already a faction (EmpireAI.RunWarPlanner.cs); declaring first then
        // flagging gives us both: a real active war AND a silenced diplomacy turn.
        ApplyArenaNoDiplomacy(ArenaEnemy);
        DisableArenaStrategicAI();

        // Frame the camera on the run, in the deep-space arena corner. CamPos.XY is
        // clamped to ±Size in UniverseScreen.Camera; ArenaCenter (±950k) is inside
        // that clamp, and no system sits within the ~12k cam frustum here, so the view
        // is clean empty space. Pin CamDestination to the same point so the view holds.
        UState.CamPos = new SDGraphics.Vector3d(center.X, center.Y, 12000.0);
        CamDestination = UState.CamPos;

        BuildHudAndShop();

        Round = 1;
        PendingWingmen = 0;
        if (HasPendingMultiplayerPvPSetup)
        {
            // §2.3 setup-phase gate: while the pre-match SETUP phase is active (custom-fleet authoring in the
            // arena), do NOT spawn yet — the spawn happens after the setup->fight rebuild + exchange completes
            // (driven per-frame by UpdateMultiplayerSetup). Default/flag-off: the phase is terminal Fight, so
            // this spawns immediately exactly as before (a true no-op for the legacy duel).
            if (!ArenaSetupActiveForHeadless)
            {
                StartMultiplayerPvPMatch();
                InitializeMultiplayerLiveIfNeeded();
            }
        }
        else if (StartAtHub)
        {
            Phase = RunPhase.Idle;
            RunStarted = true;
            UState.Paused = true;
            if (ShopPanel != null) ShopPanel.Visible = false;
            ScreenManager?.AddScreen(new ArenaHubScreen(this));
        }
        else
        {
            StartFirstBout();
        }

        Log.Info($"ArenaFightScreen ready (roguelike run): player='{PlayerDesign.Name}' (role {PlayerDesign.Role}) " +
                 $"enemy='{EnemyDesign.Name}' (role {EnemyDesign.Role}) startAtHub={StartAtHub} " +
                 $"round 1 enemyCount={CareerEnemyCountForRound(1, CareerLevel)} careerLevel={CareerLevel} " +
                 $"cash={Cash} player={PlayerShips.Count} enemy={EnemyShips.Count} center={center}");
    }

    // ---- NO-DIPLOMACY LEVER (headless-verifiable) ----------------------------------
    // Mark an arena empire as a FACTION so the sim SKIPS its per-turn diplomacy
    // (AdvanceRelationshipTurn runs only for non-factions in Empire.UpdateRelationships),
    // which is the code path that generates peace/diplomacy offers to the player. The
    // empire stays a hostile, attackable combatant (factions are hostile-by-default and
    // CanWeAttackThem returns true for a faction). Returns true once the empire is a
    // faction. Public static so the headless test can assert the lever's STATE on the
    // arena enemy without launching the live client.
    public static bool ApplyArenaNoDiplomacy(Empire arenaEmpire)
    {
        if (arenaEmpire == null)
            return false;
        arenaEmpire.IsFaction = true; // skips this empire's diplomacy turn => no peace offers
        return arenaEmpire.IsFaction;
    }

    public bool IsIdleHubPhase => Phase == RunPhase.Idle;

    public bool StartBout()
    {
        if (Phase != RunPhase.Idle)
            return false;
        StartFirstBout();
        return RunStarted && Phase == RunPhase.Fighting;
    }

    void StartFirstBout()
    {
        Round = 1;
        AdvanceRoundOnNextFight = false;
        Phase = RunPhase.Fighting;
        UState.Paused = false;
        SpawnPlayerShips(firstRound: true);
        // CAREER VETERANCY: re-apply the persisted gladiator's earned Experience/Level/Kills
        // onto the freshly spawned veteran (no-op for a fresh career).
        ReapplyVeterancy();
        QueueDefaultFightOption();
        SpawnEnemyRound(Round);
        RetargetTimer = 0f;
        EngageAll();
        RunStarted = PlayerShips.Count > 0 && EnemyShips.Count > 0;
        Career?.AddChronicleEvent("first_fight", $"round:{Round}",
            $"First fight launched with {Career.FieldedFleetVessels().Length} fielded ship(s).",
            ChronicleSeed("first_fight"));
    }

    // The arena is a closed combat sandbox. Empire-level 4X planners (expansion,
    // research stations, mining ops, space roads) otherwise keep adding background goals
    // as StarDate advances, which is both irrelevant to gladiator fights and a source of
    // arena determinism drift. ShipAI still runs normally on the spawned arena ships.
    void DisableArenaStrategicAI()
    {
        foreach (Empire e in UState.Empires)
        {
            if (e?.AI != null)
                e.AI.Disabled = true;
        }
    }

    // True once the arena enemy has had the no-diplomacy lever applied (it's a faction,
    // so its diplomacy turn — and thus all peace/diplomacy offers — is suppressed).
    public bool ArenaDiplomacyDisabled => ArenaEnemy != null && ArenaEnemy.IsFaction;

    // ---- CAREER SNAPSHOT (run-end) --------------------------------------------------

    /// <summary>
    /// Snapshot the run into the persisted <see cref="Career"/> and SAVE it. Banks the run
    /// <see cref="Cash"/>; on a WIN increments the career win count (CareerLevel); persists
    /// every LIVING PlayerShip as an <see cref="OwnedVessel"/> capturing its DesignName +
    /// current Experience/Level/Kills (the earned veterancy); persists the foreign chassis
    /// styles unlocked this run; then CareerManager.Save() writes it to disk. A roguelike
    /// keeps progress, so this runs on BOTH win and defeat (only the win bumps the level).
    /// Called BEFORE the base OnPlayerWon/OnPlayerDefeated so the career is banked before
    /// the screen tears down. Returns the saved career (also exposed for the headless proof).
    /// </summary>
    public ArenaCareer BankCareer(bool won)
    {
        Career ??= new ArenaCareer();

        // Bank the run cash and (on a win) the new career level.
        Career.Cash = Cash;
        if (won)
            Career.CareerLevel += 1;

        // PHASE B — PRESERVE THE FINITE OWNED ROSTER, MERGE EARNED VETERANCY. The owned roster
        // (with its stable VesselIds + the garage/dealership selection) is authoritative
        // and MUST survive the fight — we do NOT rebuild it from the live ships (that would wipe
        // owned vessels the player didn't field + lose ActiveVesselId). Instead we MERGE each
        // FIELDED fleet vessel's earned Experience/Level/Kills back onto ITS owned vessel,
        // keeping the rest of the roster intact.
        Career.OwnedVessels ??= Empty<OwnedVessel>.Array;
        OwnedVessel active = Career.ActiveVessel ?? Career.FirstVessel;
        if (Career.OwnedVessels.Length > 0)
        {
            int banked = BankOwnedVesselCombatState();
            // Fallback when no fleet ship was associated (e.g. an external pre-set career that
            // never spawned through SpawnPlayerShips): merge the active gladiator's earned
            // veterancy onto the active vessel.
            if (banked == 0 && active != null)
            {
                Ship glad = FindGladiatorShip(active.DesignName);
                if (glad != null)
                {
                    active.Experience = glad.Experience;
                    active.Level      = glad.Level;
                    active.Kills      = glad.Kills;
                    RecordSurvivingVesselScars(active, glad);
                }
            }
        }
        else if (Career.OwnedVessels.Length == 0)
        {
            // Safety net: a banked run with NO owned roster (shouldn't happen post-starter-grant)
            // — persist each living player ship so progress isn't lost (legacy behavior).
            var vessels = new Array<OwnedVessel>();
            foreach (Ship s in PlayerShips)
            {
                if (s == null || !s.Active)
                    continue;
                string designName = s.ShipData?.Name ?? s.Name;
                if (designName.IsEmpty())
                    continue;
                var v = new OwnedVessel(designName, s.Experience, s.Level, s.Kills, s.VanityName);
                vessels.Add(v);
            }
            Career.OwnedVessels = vessels.ToArray();
            if (Career.OwnedVessels.Length > 0)
                Career.ActiveVesselId = Career.OwnedVessels[0].VesselId;
        }

        // Persist the foreign chassis styles unlocked this run.
        var styles = new Array<string>();
        foreach (string style in GrantedChassisFactions)
            styles.Add(style);
        Career.UnlockedChassisStyles = styles.ToArray();
        Career.AddChronicleEvent(won ? "win" : "loss", $"round:{Round}",
            won ? $"Won arena bout {Round}." : $"Lost arena bout {Round}.",
            ChronicleSeed(won ? "win" : "loss"));

        CareerManager.Save(Career, CareerSavePath);
        Log.Info($"ArenaFightScreen: banked career (won={won}) cash={Career.Cash} careerLevel={Career.CareerLevel} " +
                 $"vessels={Career.OwnedVessels.Length} chassis={Career.UnlockedChassisStyles.Length}");
        return Career;
    }

    int BankOwnedVesselCombatState()
    {
        if (Career?.OwnedVessels == null || Career.OwnedVessels.Length == 0)
            return 0;

        int banked = 0;
        var destroyedVesselIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<Ship, OwnedVessel> link in FleetShipVessel)
        {
            Ship s = link.Key;
            OwnedVessel v = link.Value;
            if (s == null || v == null)
                continue;

            v.Experience = s.Experience;
            v.Level      = s.Level;
            v.Kills      = s.Kills;
            ++banked;

            if (!s.IsAlive)
            {
                if (Career.PlayerShipsPermadeath && v.VesselId.NotEmpty())
                    destroyedVesselIds.Add(v.VesselId);
                continue;
            }

            RecordSurvivingVesselScars(v, s);
        }

        if (destroyedVesselIds.Count > 0)
            RemoveDestroyedOwnedVessels(destroyedVesselIds);

        return banked;
    }

    void RemoveDestroyedOwnedVessels(HashSet<string> destroyedVesselIds)
    {
        if (destroyedVesselIds == null || destroyedVesselIds.Count == 0 || Career?.OwnedVessels == null)
            return;

        OwnedVessel[] lost = Career.OwnedVessels
            .Where(v => v != null && destroyedVesselIds.Contains(v.VesselId))
            .ToArray();
        foreach (OwnedVessel vessel in lost)
        {
            Career.AddMemorial(vessel, CurrentArenaThreatName(),
                "Destroyed in arena bout", ChronicleSeed($"memorial:{vessel.VesselId}"),
                Round, CurrentFame);
        }

        int removed = lost.Length;
        Career.OwnedVessels = Career.OwnedVessels
            .Where(v => v != null && !destroyedVesselIds.Contains(v.VesselId))
            .ToArray();
        Career.FleetVesselIds = (Career.FleetVesselIds ?? Empty<string>.Array)
            .Where(id => id.NotEmpty() && !destroyedVesselIds.Contains(id))
            .ToArray();

        if (Career.OwnedVessels.Length == 0)
        {
            Career.ActiveVesselId = "";
            OwnedVessel starter = EnsureStarterVessel();
            Log.Info($"ArenaFightScreen: player fleet permadeath removed all fielded vessels; " +
                     $"granted replacement starter '{starter?.DesignName}'.");
        }
        else if (Career.ActiveVesselId.IsEmpty()
                 || destroyedVesselIds.Contains(Career.ActiveVesselId)
                 || Career.FindOwnedVessel(Career.ActiveVesselId) == null)
        {
            Career.ActiveVesselId = Career.OwnedVessels[0].VesselId;
        }

        Career.NormalizeForPersistence();
        Log.Info($"ArenaFightScreen: player fleet permadeath removed {removed} vessel(s); " +
                 $"owned={Career.OwnedVessels.Length} active={Career.ActiveVesselId}.");
    }

    void RecordSurvivingVesselScars(OwnedVessel vessel, Ship ship)
    {
        if (vessel == null || ship == null)
            return;

        float repairableMax = RepairableHullMax(ship);
        vessel.MaxHullHealth = repairableMax;
        vessel.CurrentHullHealth = ship.Health < repairableMax - 0.5f
            ? Math.Max(1f, ship.Health.UpperBound(repairableMax))
            : 0f;

        int[] baseSlotIndices = FleetShipBaseSlotIndices.TryGetValue(ship, out int[] mapped)
            ? mapped
            : null;

        var destroyed = new Dictionary<int, DestroyedModuleSlot>();
        foreach (DestroyedModuleSlot slot in vessel.DestroyedModules ?? Empty<DestroyedModuleSlot>.Array)
            if (slot != null && slot.SlotIndex >= 0)
                destroyed[slot.SlotIndex] = new DestroyedModuleSlot(slot.SlotIndex, slot.ModuleUid);

        ShipModule[] modules = ship.Modules ?? Empty<ShipModule>.Array;
        for (int i = 0; i < modules.Length; ++i)
        {
            ShipModule module = modules[i];
            if (module == null || module.Active)
                continue;

            int baseSlot = baseSlotIndices != null && i < baseSlotIndices.Length ? baseSlotIndices[i] : i;
            if (baseSlot < 0)
                continue;
            destroyed[baseSlot] = new DestroyedModuleSlot(baseSlot, module.UID);
        }

        vessel.DestroyedModules = ArenaCareer.NormalizeDestroyedModules(destroyed.Values.ToArray());
        DropOverridesForDestroyedSlots(vessel);
        if (vessel.CurrentHullHealth > 0f || vessel.DestroyedModules.Length > 0)
        {
            Career?.AddChronicleEvent("ship_scarred", vessel.VesselId,
                $"{(vessel.Name.NotEmpty() ? vessel.Name : vessel.DesignName)} carried battle scars.",
                ChronicleSeed($"scar:{vessel.VesselId}:{vessel.CurrentHullHealth:0}:{vessel.DestroyedModules.Length}"));
        }
    }

    ulong ChronicleSeed(string subject)
        => ArenaFightOptions.SeedForCareer(Career, Phase == RunPhase.Fighting ? Round : HubRound)
           ^ FightModifierSeed
           ^ StableNameHash(subject ?? "");

    string CurrentArenaThreatName()
    {
        if (PendingChallengeContenderName.NotEmpty())
            return PendingChallengeContenderName;
        if (ActiveFightOption?.BossDesignName.NotEmpty() == true)
            return ActiveFightOption.BossDesignName;
        if (ActiveFightOption?.EscortDesignName.NotEmpty() == true)
            return ActiveFightOption.EscortDesignName;
        return "Arena";
    }

    static void DropOverridesForDestroyedSlots(OwnedVessel vessel)
    {
        if (vessel?.ModuleOverrides == null || vessel.ModuleOverrides.Length == 0
            || vessel.DestroyedModules == null || vessel.DestroyedModules.Length == 0)
            return;

        var destroyed = new HashSet<int>(vessel.DestroyedModules.Select(s => s.SlotIndex));
        vessel.ModuleOverrides = ArenaCareer.NormalizeModuleOverrides(
            vessel.ModuleOverrides.Where(o => o != null && !destroyed.Contains(o.SlotIndex)).ToArray());
    }

    int SyncOwnedVesselVeterancyFromLiveShips()
    {
        if (Career?.OwnedVessels == null || Career.OwnedVessels.Length == 0)
            return 0;

        OwnedVessel active = Career.ActiveVessel ?? Career.FirstVessel;
        int banked = 0;
        foreach (KeyValuePair<Ship, OwnedVessel> link in FleetShipVessel)
        {
            Ship s = link.Key;
            OwnedVessel v = link.Value;
            if (s == null || v == null)
                continue;
            MergeOwnedVesselVeterancyMonotonic(v, s);
            MirrorPilotVeterancyToCaptain(v);
            ++banked;
        }

        if (banked == 0 && active != null)
        {
            Ship glad = FindGladiatorShip(active.DesignName);
            if (glad != null)
            {
                MergeOwnedVesselVeterancyMonotonic(active, glad);
                banked = 1;
            }
        }

        return banked;
    }

    static void MergeOwnedVesselVeterancyMonotonic(OwnedVessel vessel, Ship ship)
    {
        if (vessel == null || ship == null)
            return;
        vessel.Experience = Math.Max(vessel.Experience, ship.Experience);
        vessel.Level      = Math.Max(vessel.Level, ship.Level);
        vessel.Kills      = Math.Max(vessel.Kills, ship.Kills);
    }

    /// <summary>
    /// ARENA PILOT TRAITS (Layer 1): mirror the fielded vessel's crew Level into its captain record
    /// (monotonic, no parallel counter) and record the pilot's auto-granted trait ids for display /
    /// future MP hashing. Reuses the existing vessel-&gt;captain link and the engine's 0..10 Level scale;
    /// safe/no-op when there is no captain. Runs regardless of the flag so the derived record stays
    /// consistent — the flag only gates whether the traits have any MECHANICAL effect at spawn.
    /// </summary>
    void MirrorPilotVeterancyToCaptain(OwnedVessel vessel)
    {
        ArenaCaptain captain = Career?.CaptainForVessel(vessel);
        if (captain == null)
            return;
        if (vessel.Level > captain.Level)
            captain.Level = vessel.Level;
        captain.GrantedTraits = PilotTraitV0.GrantedTraitsForLevel(captain.Level);
    }

    public int RefreshOwnedVesselVeterancyForUi() => SyncOwnedVesselVeterancyFromLiveShips();

    /// <summary>
    /// Apply the loaded <see cref="Career"/> to this run during LoadContent: set the starting
    /// Cash; re-grant every persisted unlocked chassis style to the player empire; and, if the
    /// career owns a gladiator, adopt that DesignName as the player's gladiator so the veteran
    /// returns (its earned veterancy is re-applied AFTER the ship spawns, in
    /// <see cref="ReapplyVeterancy"/>). A FRESH career applies nothing, so the run is today's
    /// default. The CareerLevel difficulty bump is applied at spawn time via
    /// <see cref="CareerEnemyCountForRound"/>.
    /// </summary>
    void ApplyCareer()
    {
        if (Career == null || Career.IsFresh)
            return;

        // Banked cash becomes the run's starting cash.
        Cash = Career.Cash;

        // PILOT TRAITS PLAYTEST TOGGLE (Layer 1, SP-only): re-apply the career's persisted choice onto
        // the live GamePlayGlobals flags the fight reads, so a re-opened career restores its setting.
        // A fresh career never reaches here (early-out above), so the default stays today's OFF no-op.
        if (GlobalStats.Defaults != null)
        {
            GlobalStats.Defaults.EnablePilotTraits    = Career.EnablePilotTraits;
            GlobalStats.Defaults.PilotTraitScopeVessel = Career.PilotTraitScopeVessel;
        }

        if (!CareerPerksApplied)
        {
            ArenaPerks.ApplyToEmpire(ArenaPlayer, Career.Perks);
            CareerPerksApplied = true;
        }

        // PHASE B: re-grant the HULL of every owned vessel so the whole owned roster is
        // buildable/spawnable (the active gladiator + any garage/dealership vessels), even
        // foreign-style hulls the player bought. UnlockEmpireHull is idempotent.
        if (ArenaPlayer != null && Career.OwnedVessels != null)
        {
            foreach (OwnedVessel v in Career.OwnedVessels)
            {
                if (v == null || v.DesignName.IsEmpty())
                    continue;
                if (ResourceManager.Ships.GetDesign(v.DesignName, out IShipDesign d)
                    && d.BaseHull != null)
                {
                    ArenaPlayer.UnlockEmpireHull(d.BaseHull.HullName);
                    UnlockDesignModulesForArenaDesigner(ArenaPlayer, d);
                    UnlockVesselOverrideModulesForArenaDesigner(ArenaPlayer, v);
                }
            }
        }

        UnlockResearchedModulesForArenaDesigner(ArenaPlayer, Career);

        // Re-grant the persisted unlocked chassis styles to the player empire so the veteran
        // keeps the hulls it earned (and they reappear in the Customize designer).
        if (ArenaPlayer != null && Career.UnlockedChassisStyles != null)
        {
            foreach (string style in Career.UnlockedChassisStyles)
            {
                if (style.IsEmpty() || GrantedChassisFactions.Contains(style))
                    continue;
                if (GrantChassis(ArenaPlayer, style) > 0)
                    GrantedChassisFactions.Add(style);
            }
        }

        // Adopt the career gladiator's design if it is still legal and career-tier allowed,
        // so the run spawns the veteran's hull. Veterancy is re-applied after spawn.
        OwnedVessel glad = Career.Gladiator;
        if (TryResolveOwnedVessel(glad, out _))
            ChosenPlayerDesignName = glad.DesignName;

        Log.Info($"ArenaFightScreen: applied career cash={Cash} careerLevel={Career.CareerLevel} " +
                 $"gladiator='{(Career.Gladiator?.DesignName ?? "—")}' " +
                 $"chassis={GrantedChassisFactions.Count} perks={(Career.Perks?.Length ?? 0)}");
    }

    static void UnlockResearchedModulesForArenaDesigner(Empire empire, ArenaCareer career)
    {
        if (empire == null || career == null)
            return;
        foreach (string uid in ArenaCareer.NormalizeResearchedModules(career.ResearchedModules, career.Salvage))
            UnlockModuleForArenaDesigner(empire, uid);
    }

    static void UnlockDesignModulesForArenaDesigner(Empire empire, IShipDesign design)
    {
        if (empire == null || design == null)
            return;
        IReadOnlyList<string> uids = DesignModuleUidsForCensus(design.Name);
        if (uids == null)
            return;
        foreach (string uid in uids)
            if (uid.NotEmpty())
                UnlockModuleForArenaDesigner(empire, uid);
    }

    static void UnlockVesselOverrideModulesForArenaDesigner(Empire empire, OwnedVessel vessel)
    {
        if (empire == null || vessel?.ModuleOverrides == null)
            return;
        foreach (ModuleSlotOverride o in ArenaCareer.NormalizeModuleOverrides(vessel.ModuleOverrides))
            if (o != null && o.ModuleUid.NotEmpty())
                UnlockModuleForArenaDesigner(empire, o.ModuleUid);
    }

    static void UnlockModuleForArenaDesigner(Empire empire, string moduleUid)
    {
        if (empire != null && moduleUid.NotEmpty())
            empire.UnlockEmpireShipModule(moduleUid);
    }

    bool ResearchArenaModule(string moduleUid)
    {
        if (moduleUid.IsEmpty())
            return false;
        Career ??= new ArenaCareer();
        bool added = Career.ResearchModule(moduleUid);
        UnlockModuleForArenaDesigner(ArenaPlayer, moduleUid);
        return added;
    }

    /// <summary>
    /// Re-apply the career gladiator's earned VETERANCY (Experience/Level/Kills) onto the
    /// freshly spawned player ship that matches the career gladiator's design, so the veteran
    /// returns exactly as experienced as it was banked. Re-applies onto the FIRST living
    /// player ship of the gladiator's design. No-op for a fresh career. Public so the headless
    /// proof can drive the live "fight -> bank -> reload -> veteran carry" path.
    /// </summary>
    public void ReapplyVeterancy()
    {
        // FLEET VETERANCY (STABLE KEY): re-apply EACH fielded owned vessel's own
        // Experience/Level/Kills onto the spawned ship it was associated with at spawn time, via
        // the FleetShipVessel map — NOT a positional index — so a multi-vessel fleet carries every
        // veteran's progress onto the RIGHT ship even if PlayerShips order shifted. Wingmen aren't
        // in the map and keep fresh veterancy. A fresh single-vessel career re-applies just the one
        // flagship (a no-op when its veterancy is 0).
        int applied = 0;
        foreach (KeyValuePair<Ship, OwnedVessel> link in FleetShipVessel)
        {
            Ship s = link.Key;
            OwnedVessel v = link.Value;
            if (s == null || v == null || !s.Active)
                continue;
            s.Experience = v.Experience;
            s.Level      = v.Level;
            s.Kills      = v.Kills;
            ApplyPilotTraits(s, v);
            ++applied;
        }
        if (applied > 0)
        {
            OwnedVessel lead = FieldedFleet.Count > 0 ? FieldedFleet[0] : null;
            Log.Info($"ArenaFightScreen: re-applied veterancy to {applied} fleet vessel(s) " +
                     $"(flagship '{lead?.DesignName}' exp={lead?.Experience:0} level={lead?.Level} kills={lead?.Kills}).");
            return;
        }

        // Legacy fallback (no fielded fleet — a bare run with no owned roster): re-apply the
        // single career gladiator's veterancy onto the first matching spawned ship.
        OwnedVessel glad = Career?.Gladiator;
        if (glad == null || glad.DesignName.IsEmpty())
            return;
        foreach (Ship s in PlayerShips)
        {
            if (s == null || !s.Active)
                continue;
            string designName = s.ShipData?.Name ?? s.Name;
            if (!string.Equals(designName, glad.DesignName, StringComparison.Ordinal))
                continue;
            s.Experience = glad.Experience;
            s.Level      = glad.Level;
            s.Kills      = glad.Kills;
            ApplyPilotTraits(s, glad);
            Log.Info($"ArenaFightScreen: re-applied veterancy to gladiator '{designName}' " +
                     $"exp={s.Experience:0} level={s.Level} kills={s.Kills}.");
            return; // first matching gladiator only
        }
    }

    /// <summary>
    /// ARENA PILOT TRAITS (Layer 1, SP-only, flag-gated). Compose the pilot's auto-granted traits into
    /// an ADDITIVE per-Ship bonus channel and write it onto the freshly spawned ship, in the SAME loop
    /// that just wrote veterancy. Gated on <see cref="GamePlayGlobals.EnablePilotTraits"/> — default
    /// false means this is a pure no-op (the fields stay 0, matching today's behavior exactly).
    ///
    /// The granted set is a PURE function of the pilot's crew level (v0 auto-grant) resolved from the
    /// captain record when present (so an ace who ejects and re-crews keeps its skill), else the
    /// vessel's own Level. No empire mutation, no RNG — identical inputs always yield identical fields.
    /// </summary>
    void ApplyPilotTraits(Ship s, OwnedVessel v)
    {
        if (s == null || v == null || !GlobalStats.Defaults.EnablePilotTraits)
            return;

        // PilotTraitScope selects which record supplies the crew Level traits grant from: Captain
        // (default) = transferable pilot skill (an ace who ejects re-crews at level); Vessel = ship-
        // bound veterancy. Falls back to the vessel Level when no captain is linked.
        int pilotLevel;
        if (GlobalStats.Defaults.PilotTraitScopeVessel)
        {
            pilotLevel = v.Level;
        }
        else
        {
            ArenaCaptain captain = Career?.CaptainForVessel(v);
            pilotLevel = captain?.Level > 0 ? captain.Level : v.Level;
        }
        ShipTraitEffect effect = PilotTraitV0.ComposeForLevel(pilotLevel);
        PilotTraitV0.ApplyToShip(s, effect);
    }

    // The first LIVING player ship whose design matches `designName` (the gladiator), or null.
    // Used by BankCareer to merge the gladiator's earned veterancy back onto its owned vessel.
    Ship FindGladiatorShip(string designName)
    {
        if (designName.IsEmpty())
            return null;
        foreach (Ship s in PlayerShips)
        {
            if (s == null || !s.Active)
                continue;
            string n = s.ShipData?.Name ?? s.Name;
            if (string.Equals(n, designName, StringComparison.Ordinal))
                return s;
        }
        return null;
    }

    // =================================================================================
    // PHASE B — OWNED INVENTORY + DEALERSHIP + GARAGE
    //
    // The gladiator is now a FINITE owned vessel. The career owns a roster of vessels
    // (each carrying its veterancy); the ACTIVE one is the gladiator the run fields. A
    // DEALERSHIP buys more (cash -> a new owned vessel); a GARAGE lists what you own,
    // picks the active gladiator, and opens the (owned-restricted) designer.
    // =================================================================================

    // Price of a vessel in the dealership: its intrinsic hull value (combat class + BaseStrength)
    // plus a cached, combat-honest FairDuel win-rate bonus. The intrinsic spine keeps larger
    // classes and stronger variants from collapsing into the same flat price bucket, while the
    // duel lab still nudges overperformers upward without re-running every frame.
    public const int MinVesselPrice = 50;
    const int DealershipPricingReferenceCount = 2;
    const int DealershipPricingAnchorCount = 7;
    const int DealershipPricingDuelTicks = 600;
    const float DealershipPricingSpawnOffset = 1200f;
    const int DealershipPricingWinRatePriceRange = 650;
    const int DealershipPricingRoleStep = 300;
    const float DealershipPricingStrengthDivisor = 1f;
    const ulong DealershipPricingSeed = 0xA11E_9100ul;
    static readonly object DealershipPricingLock = new();
    static readonly Dictionary<string, DealershipPricingSample> DealershipPricingCache = new(StringComparer.Ordinal);
    static int DealershipPricingContentId = -1;
    static string[] DealershipPricingReferenceNames = Empty<string>.Array;

    public static int VesselPrice(IShipDesign design, Empire forEmpire)
    {
        if (design == null) return int.MaxValue;
        DealershipPricingSample pricing = DealershipPricingFor(design);
        int intrinsicValue = VesselIntrinsicValueScore(design);
        int realizedBonus = pricing.Games > 0
            ? (int)Math.Round(pricing.WinRate * DealershipPricingWinRatePriceRange)
            : 0;

        return MinVesselPrice + intrinsicValue + realizedBonus;
    }

    public static int VesselIntrinsicValueScore(IShipDesign design)
    {
        if (design == null)
            return 0;

        int roleRank = Math.Max(CombatRolePriceRank(design.Role), CombatRolePriceRank(design.HullRole));
        int classValue = roleRank * DealershipPricingRoleStep;
        int strengthValue = (int)Math.Ceiling(Math.Max(0f, design.BaseStrength) / DealershipPricingStrengthDivisor);
        return Math.Max(0, classValue + strengthValue);
    }

    static int CombatRolePriceRank(RoleName role)
    {
        switch (role)
        {
            case RoleName.fighter:    return 1;
            case RoleName.gunboat:    return 2;
            case RoleName.corvette:   return 3;
            case RoleName.frigate:    return 4;
            case RoleName.destroyer:  return 5;
            case RoleName.cruiser:    return 6;
            case RoleName.battleship: return 7;
            case RoleName.capital:    return 8;
            default:                  return 0;
        }
    }

    public static float VesselRealizedWinRate(IShipDesign design)
        => design == null ? 0f : DealershipPricingFor(design).WinRate;

    public static int VesselRealizedWinRateGames(IShipDesign design)
        => design == null ? 0 : DealershipPricingFor(design).Games;

    static DealershipPricingSample DealershipPricingFor(IShipDesign design)
    {
        if (design == null || !IsLegalCombatCraft(design))
            return new DealershipPricingSample(0f, 0);

        int contentId = ResourceManager.ContentId;
        lock (DealershipPricingLock)
        {
            EnsureDealershipPricingCache(contentId);
            if (DealershipPricingCache.TryGetValue(design.Name, out DealershipPricingSample cached))
                return cached;
        }
        return new DealershipPricingSample(0f, 0);
    }

    static void EnsureDealershipPricingCache(int contentId)
    {
        if (DealershipPricingContentId == contentId)
            return;
        DealershipPricingCache.Clear();
        IShipDesign[] legal = DealershipPricingLegalDesigns();
        DealershipPricingReferenceNames = PickDealershipPricingReferences(legal);
        BuildDealershipPricingCurve(legal, DealershipPricingReferenceNames);
        DealershipPricingContentId = contentId;
    }

    static void BuildDealershipPricingCurve(IShipDesign[] legal, string[] references)
    {
        if (legal == null || legal.Length == 0)
            return;

        IShipDesign[] anchors = PickDealershipPricingAnchors(legal);
        var anchorRows = new List<DealershipPricingAnchor>(anchors.Length);
        foreach (IShipDesign anchor in anchors)
        {
            DealershipPricingSample sample = ComputeDealershipPricingSample(anchor, references);
            DealershipPricingCache[anchor.Name] = sample;
            if (sample.Games > 0)
                anchorRows.Add(new DealershipPricingAnchor(anchor.Name, anchor.BaseStrength, sample));
        }

        DealershipPricingAnchor[] curve = anchorRows
            .OrderBy(a => a.BaseStrength)
            .ThenBy(a => a.Name, StringComparer.Ordinal)
            .ToArray();
        foreach (IShipDesign design in legal)
        {
            if (DealershipPricingCache.ContainsKey(design.Name))
                continue;
            DealershipPricingCache[design.Name] = EstimateDealershipPricingSample(design, curve);
        }
    }

    static string[] PickDealershipPricingReferences(IShipDesign[] legal)
    {
        if (legal == null || legal.Length == 0)
            return Empty<string>.Array;

        int count = Math.Min(DealershipPricingReferenceCount, legal.Length);
        var refs = new List<string>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; ++i)
        {
            int index = count == 1 ? 0 : i * (legal.Length - 1) / (count - 1);
            for (int offset = 0; offset < legal.Length; ++offset)
            {
                IShipDesign candidate = legal[(index + offset) % legal.Length];
                if (!seen.Add(candidate.Name))
                    continue;
                refs.Add(candidate.Name);
                break;
            }
        }
        return refs.ToArray();
    }

    static IShipDesign[] PickDealershipPricingAnchors(IShipDesign[] legal)
    {
        if (legal == null || legal.Length == 0)
            return Empty<IShipDesign>.Array;

        int count = Math.Min(DealershipPricingAnchorCount, legal.Length);
        var anchors = new List<IShipDesign>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; ++i)
        {
            int index = count == 1 ? 0 : i * (legal.Length - 1) / (count - 1);
            for (int offset = 0; offset < legal.Length; ++offset)
            {
                IShipDesign candidate = legal[(index + offset) % legal.Length];
                if (!seen.Add(candidate.Name))
                    continue;
                anchors.Add(candidate);
                break;
            }
        }
        return anchors.ToArray();
    }

    static IShipDesign[] DealershipPricingLegalDesigns()
        => ResourceManager.Ships.Designs
            .Where(IsLegalCombatCraft)
            .OrderBy(d => d.BaseStrength)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

    static DealershipPricingSample ComputeDealershipPricingSample(IShipDesign design, string[] references)
    {
        int wins = 0;
        int games = 0;
        foreach (string referenceName in references ?? Empty<string>.Array)
        {
            if (string.Equals(referenceName, design.Name, StringComparison.Ordinal))
                continue;
            if (!ResourceManager.Ships.GetDesign(referenceName, out IShipDesign reference)
                || !IsLegalCombatCraft(reference))
                continue;

            ulong seed = DealershipPricingSeed
                       ^ StableNameHash(design.Name)
                       ^ (StableNameHash(reference.Name) << 1);
            try
            {
                FairDuelResult duel = CareerLadder.FairDuel(design, reference, seed,
                    DealershipPricingDuelTicks, DealershipPricingSpawnOffset);
                wins += duel.WinsA;
                games += duel.Games;
            }
            catch (Exception e)
            {
                Log.Warning($"ArenaFightScreen: skipped dealership pricing duel '{design.Name}' vs " +
                            $"'{reference.Name}' ({e.GetType().Name}: {e.Message}).");
            }
        }

        float winRate = games > 0 ? wins / (float)games : 0f;
        return new DealershipPricingSample(winRate, games);
    }

    static DealershipPricingSample EstimateDealershipPricingSample(IShipDesign design, DealershipPricingAnchor[] curve)
    {
        if (design == null || curve == null || curve.Length == 0)
            return new DealershipPricingSample(0f, 0);
        if (curve.Length == 1 || design.BaseStrength <= curve[0].BaseStrength)
            return curve[0].Sample;
        DealershipPricingAnchor last = curve[curve.Length - 1];
        if (design.BaseStrength >= last.BaseStrength)
            return last.Sample;

        DealershipPricingAnchor lower = curve[0];
        DealershipPricingAnchor upper = last;
        for (int i = 1; i < curve.Length; ++i)
        {
            if (design.BaseStrength > curve[i].BaseStrength)
                continue;
            lower = curve[i - 1];
            upper = curve[i];
            break;
        }

        float span = Math.Max(1f, upper.BaseStrength - lower.BaseStrength);
        float t = Math.Max(0f, Math.Min(1f, (design.BaseStrength - lower.BaseStrength) / span));
        float winRate = lower.Sample.WinRate + (upper.Sample.WinRate - lower.Sample.WinRate) * t;
        int games = Math.Min(lower.Sample.Games, upper.Sample.Games);
        return new DealershipPricingSample(winRate, games);
    }

    static ulong StableNameHash(string text)
    {
        const ulong Offset = 14695981039346656037ul;
        const ulong Prime = 1099511628211ul;
        ulong hash = Offset;
        foreach (char c in text ?? "")
        {
            hash ^= c;
            hash *= Prime;
        }
        return hash;
    }

    readonly struct DealershipPricingSample
    {
        public readonly float WinRate;
        public readonly int Games;

        public DealershipPricingSample(float winRate, int games)
        {
            WinRate = Math.Max(0f, Math.Min(1f, winRate));
            Games = Math.Max(0, games);
        }
    }

    readonly struct DealershipPricingAnchor
    {
        public readonly string Name;
        public readonly float BaseStrength;
        public readonly DealershipPricingSample Sample;

        public DealershipPricingAnchor(string name, float baseStrength, DealershipPricingSample sample)
        {
            Name = name ?? "";
            BaseStrength = baseStrength;
            Sample = sample;
        }
    }

    /// <summary>
    /// PHASE B starter grant: when the career owns NO vessels (a fresh career), grant a STARTER
    /// owned vessel — the deterministic tier-1 light craft the run auto-picks — and make it the
    /// active gladiator, so the run always fields a vessel the player OWNS. No-op once the career
    /// owns at least one vessel. The starter design IS the auto-pick, so the fresh-career default
    /// is unchanged (just now tracked as an owned vessel). Returns the granted starter (or null).
    /// </summary>
    public OwnedVessel EnsureStarterVessel()
    {
        Career ??= new ArenaCareer();
        bool hadOwnedRoster = Career.OwnedVessels != null && Career.OwnedVessels.Length > 0;
        if (Career.OwnedVessels != null && Career.OwnedVessels.Length > 0)
        {
            OwnedVessel valid = EnsureValidActiveOwnedVessel();
            if (valid != null)
                return valid; // already has a usable roster
            Log.Warning("ArenaFightScreen: owned roster has no currently spawnable vessels; granting a starter.");
        }

        bool emergencyReplacement = hadOwnedRoster || CareerLevel > 0;
        IShipDesign starter = emergencyReplacement
            ? PickHumbleTier1Starter(ArenaPlayer)
            : AutoPickPlayerWarship(ArenaPlayer, CareerLevel);
        if (starter == null)
            return null;

        OwnedVessel granted = Career.AddOwnedVessel(starter.Name, name: null, makeActive: true);
        Log.Info($"ArenaFightScreen: granted STARTER owned vessel '{starter.Name}' " +
                 $"(id={granted?.VesselId}) and set it ACTIVE.");
        return granted;
    }

    public static IShipDesign PickHumbleTier1Starter(Empire forEmpire)
    {
        IShipDesign pick = PickByRoleStrength(forEmpire,
            new[] { RoleName.fighter, RoleName.gunboat, RoleName.corvette },
            strengthPercentile: 0.0f, maxTier: 1);
        return pick ?? PickFallback(forEmpire, priciest: false, maxTier: 1);
    }

    OwnedVessel EnsureValidActiveOwnedVessel()
    {
        OwnedVessel active = Career.ActiveVessel;
        if (TryResolveOwnedVessel(active, out _))
        {
            DropInvalidFleetIds();
            return active;
        }

        foreach (OwnedVessel v in Career.OwnedVessels ?? Empty<OwnedVessel>.Array)
        {
            if (!TryResolveOwnedVessel(v, out _))
                continue;
            Career.ActiveVesselId = v.VesselId;
            DropInvalidFleetIds();
            Log.Warning($"ArenaFightScreen: active owned vessel was missing/invalid; " +
                        $"falling back to owned vessel id={v.VesselId} design='{v.DesignName}'.");
            return v;
        }

        Career.ActiveVesselId = "";
        Career.FleetVesselIds = Empty<string>.Array;
        return null;
    }

    bool TryResolveOwnedVessel(OwnedVessel vessel, out IShipDesign design)
    {
        design = null;
        if (vessel == null || vessel.DesignName.IsEmpty())
            return false;
        if (!ResourceManager.Ships.GetDesign(vessel.DesignName, out design)
            || !IsLegalCombatCraft(design)
            || !IsDesignAllowedForCareerLevel(design, CareerLevel))
        {
            design = null;
            return false;
        }
        return true;
    }

    void DropInvalidFleetIds()
    {
        if (Career?.FleetVesselIds == null || Career.FleetVesselIds.Length == 0)
            return;

        int maxFleetSize = CurrentMaxFleetSize;
        var ids = new List<string>(Math.Min(Career.FleetVesselIds.Length, maxFleetSize - 1));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in Career.FleetVesselIds)
        {
            if (id.IsEmpty() || seen.Contains(id))
                continue;
            if (string.Equals(id, Career.ActiveVesselId, StringComparison.Ordinal))
                continue;
            if (!TryResolveOwnedVessel(Career.FindOwnedVessel(id), out _))
                continue;

            ids.Add(id);
            seen.Add(id);
            if (ids.Count >= maxFleetSize - 1)
                break;
        }
        Career.FleetVesselIds = ids.Count > 0 ? ids.ToArray() : Empty<string>.Array;
    }

    OwnedVessel[] ValidFieldedFleetVessels()
    {
        var valid = new List<OwnedVessel>(CurrentMaxFleetSize);
        foreach (OwnedVessel v in Career?.FieldedFleetVessels() ?? Empty<OwnedVessel>.Array)
            if (TryResolveOwnedVessel(v, out _))
                valid.Add(v);
        return valid.Count > 0 ? valid.ToArray() : Empty<OwnedVessel>.Array;
    }

    /// <summary>
    /// The list of vessel designs the DEALERSHIP offers: real combat craft, priced and
    /// (optionally) gated to those the player can AFFORD with <paramref name="cash"/>. Sorted by
    /// hull class first (fighter -> capital), then price/strength/name, so the dealership reads
    /// as a class progression while remaining deterministic. Static + pure so the dealership UI
    /// and the headless proof share ONE catalog.
    /// </summary>
    public static IShipDesign[] DealershipCatalog(Empire forEmpire, int cash, bool affordableOnly)
        => DealershipCatalog(forEmpire, cash, affordableOnly, careerLevel: 0);

    public static IShipDesign[] DealershipCatalog(Empire forEmpire, int cash, bool affordableOnly, int careerLevel)
    {
        IShipDesign[] pool = ResourceManager.Ships.Designs
            .Filter(d => IsLegalCombatCraft(d)
                         && IsDesignAllowedForCareerLevel(d, careerLevel)
                         && (!affordableOnly || VesselPrice(d, forEmpire) <= cash));
        Array.Sort(pool, (a, b) =>
        {
            int c = DealershipHullClassRank(a).CompareTo(DealershipHullClassRank(b));
            if (c != 0) return c;
            c = VesselPrice(a, forEmpire).CompareTo(VesselPrice(b, forEmpire));
            if (c != 0) return c;
            c = a.BaseStrength.CompareTo(b.BaseStrength);
            return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
        });
        return pool;
    }

    public static int DealershipHullClassRank(IShipDesign design)
    {
        if (design == null)
            return int.MaxValue;

        int hull = DealershipHullClassRank(design.HullRole);
        if (hull < 1000)
            return hull;

        int role = DealershipHullClassRank(design.Role);
        if (role < 1000)
            return role;

        return Math.Clamp(CombatTierForDesign(design), 1, 3) * 100;
    }

    public static int DealershipHullClassRank(RoleName role)
    {
        switch (role)
        {
            case RoleName.fighter:    return 10;
            case RoleName.gunboat:
            case RoleName.corvette:   return 20;
            case RoleName.frigate:    return 30;
            case RoleName.destroyer:  return 40;
            case RoleName.cruiser:    return 50;
            case RoleName.battleship: return 60;
            case RoleName.capital:    return 70;
            default:                  return 1000;
        }
    }

    /// <summary>
    /// DEALERSHIP BUY (the load-bearing "cash -> a new OWNED vessel" action): if the player can
    /// afford <paramref name="designName"/> (a legal combat craft within the career tier), deduct its price from
    /// Cash, add a new OwnedVessel (fresh VesselId, 0 veterancy) to the career, unlock its hull
    /// on the player empire, persist the career, and return the bought vessel. Returns null
    /// (charges nothing) when the design is illegal or unaffordable. Reuses the shop cash-gate
    /// pattern; finite cash => finite buys. PUBLIC so the dealership UI and the headless proof
    /// drive the REAL buy.
    /// </summary>
    public OwnedVessel BuyVessel(string designName, bool makeActive = false)
    {
        Career ??= new ArenaCareer();
        if (designName.IsEmpty())
            return null;
        if (!ResourceManager.Ships.GetDesign(designName, out IShipDesign design)
            || !IsLegalCombatCraft(design)
            || !IsDesignAllowedForCareerLevel(design, CareerLevel))
            return null;

        int price = VesselPrice(design, ArenaPlayer);
        if (Cash < price) // cash-gate (same pattern as the shop buys) => finite buys
            return null;

        Cash -= price;
        OwnedVessel bought = Career.AddOwnedVessel(design.Name, name: null, makeActive: makeActive);

        // Unlock the bought hull so it's usable/buildable (foreign-style hulls included), and
        // bank the run's current cash so the deduction persists with the new owned vessel.
        if (ArenaPlayer != null && design.BaseHull != null)
        {
            ArenaPlayer.UnlockEmpireHull(design.BaseHull.HullName);
            UnlockDesignModulesForArenaDesigner(ArenaPlayer, design);
            ArenaPlayer.UpdateShipsWeCanBuild(new Array<string> { design.BaseHull.HullName });
        }
        Career.Cash = Cash;
        CareerManager.Save(Career, CareerSavePath);
        RefreshShop();

        Log.Info($"ArenaFightScreen: DEALERSHIP bought vessel '{design.Name}' for ${price} " +
                 $"(id={bought?.VesselId}); cash now ${Cash}; owned={Career.OwnedVessels.Length}.");
        return bought;
    }

    /// <summary>
    /// GARAGE SELECT (the load-bearing "pick which owned vessel is the gladiator" action): set
    /// the career's ACTIVE vessel to the owned vessel with <paramref name="vesselId"/>, adopt its
    /// design as the live gladiator (respawning it so it fights next round), persist the career,
    /// and return true. No-op (returns false) if the id isn't owned. PUBLIC so the garage UI and
    /// the headless proof drive the REAL selection.
    /// </summary>
    public bool SetActiveOwnedVessel(string vesselId)
    {
        Career ??= new ArenaCareer();
        ArenaVesselActivationResult result = GetOwnedVesselActivationStatus(vesselId);
        LastGarageActivationMessage = result.Message;
        if (!result.Success)
            return false;
        if (!Career.SetActiveVessel(vesselId))
            return false;

        OwnedVessel active = Career.ActiveVessel;
        if (TryResolveOwnedVessel(active, out _))
            SetChosenGladiator(active.DesignName);

        Career.ActiveVesselId = vesselId;
        CareerManager.Save(Career, CareerSavePath);
        Log.Info($"ArenaFightScreen: GARAGE set active owned vessel id={vesselId} " +
                 $"design='{active?.DesignName}'.");
        return true;
    }

    public string LastGarageActivationMessage { get; private set; } = "";

    public ArenaVesselActivationResult GetOwnedVesselActivationStatus(string vesselId)
    {
        Career ??= new ArenaCareer();
        if (vesselId.IsEmpty())
            return new ArenaVesselActivationResult(false, "No vessel selected.", vesselId);

        OwnedVessel requested = Career.FindOwnedVessel(vesselId);
        if (requested == null)
            return new ArenaVesselActivationResult(false, "That vessel is not owned.", vesselId);
        if (requested.DesignName.IsEmpty())
            return new ArenaVesselActivationResult(false, "Owned vessel has no saved design.", vesselId);
        if (!ResourceManager.Ships.GetDesign(requested.DesignName, out IShipDesign design))
            return new ArenaVesselActivationResult(false,
                $"Missing design '{requested.DesignName}'.", vesselId, requested.DesignName);
        if (!IsLegalCombatCraft(design))
            return new ArenaVesselActivationResult(false,
                $"{requested.DesignName} is not a legal arena combat craft.", vesselId, requested.DesignName);

        int requiredTier = CombatTierForDesign(design);
        int currentTier = CurrentAllowedCombatTier;
        if (requiredTier > currentTier)
            return new ArenaVesselActivationResult(false,
                $"Requires Tier {requiredTier} (current Tier {currentTier}).",
                vesselId, requested.DesignName, requiredTier, currentTier);

        return new ArenaVesselActivationResult(true, "Ready.", vesselId,
            requested.DesignName, requiredTier, currentTier);
    }

    /// <summary>The career's owned roster (never null) — the GARAGE/DEALERSHIP UI reads this.</summary>
    public OwnedVessel[] OwnedVessels => Career?.OwnedVessels ?? Empty<OwnedVessel>.Array;

    /// <summary>The active owned vessel id (the current gladiator) — the GARAGE highlights it.</summary>
    public string ActiveVesselId => Career?.ActiveVesselId ?? "";

    /// <summary>The player empire (for the dealership/garage UI to price + restrict by).</summary>
    public Empire ArenaPlayerEmpire => ArenaPlayer;

    /// <summary>The career level currently gating arena combat classes.</summary>
    public int CurrentCareerLevel => CareerLevel;

    /// <summary>The max combat size tier currently fieldable/fightable by this career.</summary>
    public int CurrentAllowedCombatTier => MaxAllowedCombatTierForCareerLevel(CareerLevel);

    /// <summary>The run's current spendable cash (the dealership gates buys against this).</summary>
    public int CurrentCash => Cash;
    public int CurrentFame => Math.Max(0, Career?.Fame ?? 0);

    public int CurrentCashPerClear => ArenaPerks.CashPerClear(CashPerClear, Career?.Perks);
    public int CurrentGeneratedFightCash
        => ArenaFightOptions.RewardCashForEnemyStrength(Career, CurrentEnemyTotalStrength());
    public int CurrentRepairCost => CurrentRepairAllCost();
    public int CurrentMaxFleetSize => Career?.MaxFleetSize ?? ArenaCareer.ArenaMaxFleetSize;
    public int CurrentFleetSlotUpgradeCost => FleetSlotUpgradeCost(CurrentMaxFleetSize);
    public bool CanBuyFleetSlotUpgrade
        => CurrentMaxFleetSize < ArenaPerks.MaxFleetSizeCap && Cash >= CurrentFleetSlotUpgradeCost;
    public int CurrentModuleShopTier => ModuleShopTierForCareer(Career);
    public int CurrentPerkCount => Career?.Perks?.Length ?? 0;
    public int CurrentResearchedModuleCount => Career?.ResearchedModules?.Length ?? 0;
    public int CurrentTeamCount => Career?.Teams?.Length ?? 0;
    public int CurrentBetHistoryCount => Career?.SettledBets?.Length ?? 0;
    public float CurrentPermadeathChance => Career?.PermadeathChance ?? 0f;
    public bool CurrentHardLossEndsRun => Career?.HardLossEndsRun ?? false;
    public bool CurrentPlayerShipsPermadeath => Career?.PlayerShipsPermadeath ?? true;
    public bool HasPendingBossReward => PendingBossPerkChoices != null && PendingBossPerkChoices.Length > 0;
    public bool HasQueuedFightOption => PendingFightOption != null;
    public bool HasPendingBet => Career?.PendingBet != null;
    public ArenaBetSlip PendingBet => Career?.PendingBet;
    public ArenaSettledBet[] SettledBets => Career?.SettledBets ?? Empty<ArenaSettledBet>.Array;
    public ArenaSettledBet LatestSettledBet
        => SettledBets.OrderByDescending(b => b?.Order ?? 0).FirstOrDefault();
    public ArenaMemorialRecord[] Memorials => Career?.Memorials ?? Empty<ArenaMemorialRecord>.Array;
    public ArenaCaptain[] Captains => Career?.Captains ?? Empty<ArenaCaptain>.Array;
    public ArenaChronicleEvent[] Chronicle => Career?.Chronicle ?? Empty<ArenaChronicleEvent>.Array;
    public string[] CurrentPerks => Career?.Perks ?? Empty<string>.Array;
    public ArenaTeam[] Teams => Career?.Teams ?? Empty<ArenaTeam>.Array;
    public OwnedVessel[] FieldedVessels => Career?.FieldedFleetVessels() ?? Empty<OwnedVessel>.Array;

    public static ulong ModifierSeedForCareer(ArenaCareer career)
    {
        int level = Math.Max(0, career?.CareerLevel ?? 0);
        int cash = Math.Max(0, career?.Cash ?? 0);
        return 0xA12E_F17E_0000_0000ul ^ ((ulong)(uint)level << 32) ^ (uint)cash;
    }

    public override void Update(float fixedDeltaTime)
    {
        if (MultiplayerLiveActive)
        {
            UState.Paused = true;
            base.Update(fixedDeltaTime);
            // §2.3: while the pre-match SETUP phase is active, drive the setup->fight handshake (publish Ready,
            // rebuild+broadcast the authoritative start from the setup scratch set, validate+register, advance to
            // Fight, then spawn). A no-op when the phase is terminal Fight (the legacy/flag-off duel).
            UpdateMultiplayerSetup(fixedDeltaTime);
            UpdateMultiplayerLive(fixedDeltaTime);
            UState.Paused = MultiplayerLiveDisplayPaused;
            return;
        }

        // base.Update ticks the sim AND drives the 3D render — the normal path.
        base.Update(fixedDeltaTime);

        if (!RunStarted || Phase == RunPhase.Over)
            return;

        if (Phase == RunPhase.Idle)
            return;

        // While the shop is open between rounds, the sim is paused and we just wait
        // for the player to press a buy / Next Fight. No win/lose checks run.
        if (Phase == RunPhase.Shopping)
            return;

        // ---- FIGHTING: re-issue attack orders + check the round resolution. ----
        MaintainArenaIsolation(fixedDeltaTime);

        RetargetTimer -= fixedDeltaTime;
        if (RetargetTimer <= 0f)
        {
            RetargetTimer = RetargetInterval;
            EngageAll();
        }

        bool anyPlayerAlive = false;
        foreach (Ship s in PlayerShips) if (s.IsAlive) { anyPlayerAlive = true; break; }
        bool anyEnemyAlive = false;
        foreach (Ship s in EnemyShips) if (s.IsAlive) { anyEnemyAlive = true; break; }

        if (!anyPlayerAlive)
        {
            // Default arena careers are open-ended: a lost bout banks what was earned and
            // returns to the hub. Hardcore careers can opt into the old hard defeat screen.
            HandleFightLost();
            return;
        }

        if (!anyEnemyAlive)
        {
            // BOUT CLEARED: award rewards, bank the career level, then reroll from the hub.
            if (CurrentBossEncounter.Active)
            {
                PendingBossPerkChoices = OfferBossPerks();
                Log.Info($"ArenaFightScreen: boss defeated '{CurrentBossEncounter.Name}' " +
                         $"choices=[{string.Join(",", PendingBossPerkChoices.Select(p => p.Id))}]");
            }

            ResolvePendingFightOptionBet(playerWon: true);

            if (ActiveFightOption != null)
                GrantFightOptionRewards(ActiveFightOption);
            else
                Cash += CurrentGeneratedFightCash;

            ActiveFightOption = null;
            BankCareer(won: true);
            FightModifierSeed = ModifierSeedForCareer(Career);
            AdvanceRoundOnNextFight = true;

            if (IsVictoryMilestoneReached())
            {
                Phase = RunPhase.Over;
                OnPlayerWon();
                return;
            }

            EnterShop();
        }
    }

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        base.Draw(batch, elapsed);
        DrawArenaModuleOverlayFallback(batch);
        DrawPilotTraitReadouts(batch);
    }

    /// <summary>
    /// IN-FIGHT PILOT-TRAIT READOUT (Layer 1, SP-only, read-only display). When
    /// <see cref="GamePlayGlobals.EnablePilotTraits"/> is ON, draw each managed arena ship's crew
    /// Level and its active pilot-trait NAMES as a small line under the hull, so the flag-gated
    /// mechanic is legible during playtest. When the flag is OFF this is a pure no-op — zero extra
    /// draw calls, zero visual change. Purely a display of already-computed state (the same
    /// level-derived trait set ApplyPilotTraits composes); it never mutates the sim.
    /// </summary>
    void DrawPilotTraitReadouts(SpriteBatch batch)
    {
        if (GlobalStats.Defaults?.EnablePilotTraits != true)
            return; // OFF = true no-op: nothing new is drawn.
        if (LookingAtPlanet || viewState > UnivScreenState.DetailView)
            return;

        Font font = Fonts.Arial12Bold;
        bool began = false;
        DrawPilotTraitReadouts(batch, PlayerShips, font, ref began);
        DrawPilotTraitReadouts(batch, EnemyShips, font, ref began);
        if (began)
            batch.SafeEnd();
    }

    void DrawPilotTraitReadouts(SpriteBatch batch, List<Ship> ships, Font font, ref bool began)
    {
        if (ships == null)
            return;

        foreach (Ship ship in ships)
        {
            if (!ShouldDrawArenaModuleOverlayFallback(ship))
                continue;

            int level = ResolvePilotDisplayLevel(ship);
            string readout = PilotTraitV0.DescribeForLevel(level);

            // Project the hull's world position to screen and place the line just under it, centered.
            Vector2 screen = ProjectToScreenPosition(ship.Position).ToVec2f();
            float radius = ship.Radius > 0 ? ship.Radius : 64f;
            var pos = new Vector2(screen.X - font.TextWidth(readout) / 2f, screen.Y + radius * 0.35f + 6f);

            if (!began)
            {
                batch.SafeBegin();
                began = true;
            }
            batch.DrawString(font, readout, pos, ArenaTheme.TextPrimary);
        }
    }

    // Resolve the pilot Level the readout shows, mirroring ApplyPilotTraits' scope selection so the
    // displayed level matches the level the effect was composed from. Player ships resolve their
    // OwnedVessel (captain vs vessel per the scope flag); enemy/wingman ships fall back to Ship.Level.
    int ResolvePilotDisplayLevel(Ship ship)
    {
        if (ship == null)
            return 0;
        if (FleetShipVessel.TryGetValue(ship, out OwnedVessel v) && v != null)
        {
            if (GlobalStats.Defaults?.PilotTraitScopeVessel == true)
                return v.Level;
            ArenaCaptain captain = Career?.CaptainForVessel(v);
            return captain?.Level > 0 ? captain.Level : v.Level;
        }
        return ship.Level;
    }

    public bool WouldDrawArenaModuleOverlay(Ship ship)
        => ShouldDrawArenaModuleOverlayFallback(ship);

    void DrawArenaModuleOverlayFallback(SpriteBatch batch)
    {
        if (!ShowShipNames && !AnyArenaShipMissingMeshes())
            return;
        if (LookingAtPlanet || viewState > UnivScreenState.DetailView)
            return;

        bool began = false;
        DrawArenaModuleOverlayFallback(batch, PlayerShips, ref began);
        DrawArenaModuleOverlayFallback(batch, EnemyShips, ref began);
        if (began)
            batch.SafeEnd();
    }

    void DrawArenaModuleOverlayFallback(SpriteBatch batch, List<Ship> ships, ref bool began)
    {
        if (ships == null)
            return;

        foreach (Ship ship in ships)
        {
            if (!ShouldDrawArenaModuleOverlayFallback(ship))
                continue;
            if (IsBaseOverlayPathLikelyToDraw(ship))
                continue;

            if (!began)
            {
                RenderStates.BasicBlendMode(Device, additive: false, depthWrite: false);
                batch.SafeBegin();
                began = true;
            }

            ship.DrawModulesOverlay(this, CamPos.Z, showDebugSelect: false, showDebugStats: false);
        }
    }

    bool ShouldDrawArenaModuleOverlayFallback(Ship ship)
    {
        return ship != null && ship.Active && !ship.Dying && !LookingAtPlanet
            && viewState <= UnivScreenState.DetailView
            && !ship.IsLaunching
            && ship.InPlayerSensorRange
            && (ShowShipNames || ship.GetSO()?.HasMeshes == false)
            && IsArenaManagedShip(ship);
    }

    bool IsBaseOverlayPathLikelyToDraw(Ship ship)
        => ship.InFrustum && UState.Objects.VisibleShips.Contains(ship);

    bool IsArenaManagedShip(Ship ship)
        => PlayerShips.Contains(ship) || EnemyShips.Contains(ship);

    bool AnyArenaShipMissingMeshes()
    {
        return AnyMissing(PlayerShips) || AnyMissing(EnemyShips);

        static bool AnyMissing(List<Ship> ships)
        {
            if (ships == null)
                return false;
            foreach (Ship ship in ships)
                if (ship != null && ship.Active && ship.GetSO()?.HasMeshes == false)
                    return true;
            return false;
        }
    }

    void MaintainArenaIsolation(float fixedDeltaTime)
    {
        MaintainArenaShips(PlayerShips, EnemyShips, fixedDeltaTime);
        MaintainArenaShips(EnemyShips, PlayerShips, fixedDeltaTime);
    }

    float CurrentEnemyTotalStrength()
        => Math.Max(1f, EnemyShips?.Where(s => s != null).Sum(s => Math.Max(1f, s.BaseStrength)) ?? 1f);

    void MaintainArenaShips(List<Ship> ships, List<Ship> enemies, float fixedDeltaTime)
    {
        foreach (Ship ship in ships)
        {
            if (ship == null || !ship.Active)
                continue;

            RearmArenaShip(ship, fixedDeltaTime);
            SuppressArenaEscapeOrder(ship, enemies);
            ContainArenaShip(ship, enemies);
        }
    }

    static void RearmArenaShip(Ship ship, float fixedDeltaTime)
    {
        if (ship.OrdinanceMax <= 0f || ship.Ordinance >= ship.OrdinanceMax)
            return;
        if (!ship.Position.InRadius(ArenaCenter, ArenaLocalRearmRadius))
            return;

        float rate = Math.Max(ArenaRearmMinOrdnancePerSecond,
            ship.OrdinanceMax * ArenaRearmFractionPerSecond);
        ship.ChangeOrdnance(rate * Math.Max(0f, fixedDeltaTime));
    }

    void SuppressArenaEscapeOrder(Ship ship, List<Ship> enemies)
    {
        if (ship.AI.State is not (AIState.Resupply or AIState.ResupplyEscort or AIState.Flee
                                  or AIState.ReturnHome or AIState.ReturnToHangar
                                  or AIState.SupplyReturnHome))
        {
            return;
        }

        Ship nearest = NearestLivingEnemy(ship, enemies);
        if (nearest != null)
            ship.AI.OrderAttackSpecificTarget(nearest);
        else
            ship.AI.OrderHoldPosition(MoveOrder.HoldPosition);
    }

    void ContainArenaShip(Ship ship, List<Ship> enemies)
    {
        Vector2 delta = ship.Position - ArenaCenter;
        float distance = delta.Length();
        if (distance <= ArenaHardBoundsRadius)
            return;

        Vector2 dir = distance > 0.001f ? delta / distance : Vector2.UnitX;
        ship.Position = ArenaCenter + dir * ArenaHardBoundsRadius;
        ship.Velocity = Vector2.Zero;

        Ship nearest = NearestLivingEnemy(ship, enemies);
        if (nearest != null)
            ship.AI.OrderAttackSpecificTarget(nearest);
        else
            ship.AI.OrderHoldPosition(MoveOrder.HoldPosition);
    }

    static Ship NearestLivingEnemy(Ship ship, List<Ship> enemies)
    {
        Ship nearest = null;
        float best = float.MaxValue;
        foreach (Ship enemy in enemies)
        {
            if (enemy == null || !enemy.Active)
                continue;
            float d = ship.Position.SqDist(enemy.Position);
            if (d < best)
            {
                best = d;
                nearest = enemy;
            }
        }
        return nearest;
    }

    void HandleFightLost()
    {
        if (CurrentHardLossEndsRun)
        {
            Phase = RunPhase.Over;
            ApplyFightLossFamePenalty();
            ResolvePendingFightOptionBet(playerWon: false);
            BankCareer(won: false);
            OnPlayerDefeated();
            return;
        }

        ApplyFightLossFamePenalty();
        ResolvePendingFightOptionBet(playerWon: false);
        BankCareer(won: false);
        FightModifierSeed = ModifierSeedForCareer(Career);
        ActiveFightOption = null;
        PendingFightOption = null;
        AdvanceRoundOnNextFight = false;
        CurrentBossEncounter = ArenaBossEncounter.None;
        CurrentFightModifier = ArenaFightModifier.None;
        ClearArenaShipsAfterLoss();
        EnterShop();
    }

    // Championship/endless-mode milestones can be layered here later. For this batch,
    // there is deliberately no round-count victory trigger.
    bool IsVictoryMilestoneReached() => false;

    void ClearArenaShipsAfterLoss()
    {
        static void RemoveShips(List<Ship> ships)
        {
            foreach (Ship s in ships)
                if (s != null && s.Active)
                    s.Die(null, cleanupOnly: true);
            ships.Clear();
        }

        RemoveShips(PlayerShips);
        RemoveShips(EnemyShips);
        FieldedFleet.Clear();
        FleetShipVessel.Clear();
        FleetShipBaseSlotIndices.Clear();
        PendingWingmen = 0;
    }

    // ---- SHOP / INTERMISSION --------------------------------------------------------

    // Open the between-fights downtime: pause the fight, recharge player shields (hull
    // attrition persists), and PUSH the STAGES HUB (ArenaHubScreen). The hub surfaces
    // in-place repair, FLEET, HANGAR, and market actions + a round-keyed dialogue beat,
    // then NEXT FIGHT resumes the run. The legacy inline ShopPanel remains for old
    // headless/input coverage but is no longer opened by the hub.
    void EnterShop()
    {
        Phase = RunPhase.Shopping;
        UState.Paused = true;
        SyncOwnedVesselVeterancyFromLiveShips();
        foreach (Ship s in PlayerShips)
            if (s.IsAlive) ArenaEngineCapabilities.RechargeArenaShields(s);
        RefreshShop();
        // Keep the legacy inline panel hidden; the hub handles Repair All in-place.
        if (ShopPanel != null) ShopPanel.Visible = false;
        // Push the hub over the paused arena. (ScreenManager may be null in some headless
        // paths; the hub is then driven directly by the test.)
        ScreenManager?.AddScreen(new ArenaHubScreen(this));
        if (HasPendingBossReward)
            OpenBossReward();
    }

    // ---- STAGES HUB CALLBACKS (the hub buttons drive these) -------------------------

    // The round the player is about to fight NEXT — used to key the hub's dialogue line.
    // The hub opens after clearing `Round`, before NextFight() increments it, so the next
    // fight is Round + 1.
    public int HubRound => Phase == RunPhase.Idle ? 1 : Round + (AdvanceRoundOnNextFight ? 1 : 0);

    // Legacy hook retained for older tests/debugging; the hub no longer opens this panel.
    public void ShowShopPanel()
    {
        RefreshShop();
        if (ShopPanel != null) ShopPanel.Visible = true;
    }

    // HUB: FLEET — turn the persistent PlayerShips roster into a REAL Fleet with assigned
    // formation offsets, so the roster spawns as a formation next round. This is a
    // SPAWN-TIME LAYOUT ONLY: we assign FleetOffsets (AssignPositions) so SpawnPlayerShips
    // can place ships in formation, but we do NOT run fleet-hold AI (which would force the
    // wing to close to point-blank). Returns the built Fleet (or null if no live ships).
    public Fleet BuildArenaFleet()
    {
        if (ArenaPlayer == null)
            return null;

        // Collect the live roster.
        var roster = new Array<Ship>();
        foreach (Ship s in PlayerShips)
            if (s != null && s.Active) roster.Add(s);
        if (roster.Count == 0)
            return null;

        // Build a fresh fleet in a fixed arena slot and add every live player ship.
        Fleet fleet = ArenaPlayer.CreateFleet(ArenaFleetId, "Arena Gladiators");
        foreach (Ship s in roster)
        {
            if (s.Fleet != null && s.Fleet != fleet) // detach from any prior fleet first
                s.Fleet.RemoveShip(s, clearOrders: false);
            fleet.AddShip(s);
        }

        // AutoArrange computes the real per-ship formation grid (RelativeFleetOffset on each
        // node/ship); AssignPositions(Vectors.Up) then rotates those offsets to a forward-
        // facing formation. This is the formation LAYOUT the next round spawns into.
        fleet.AutoArrange();
        fleet.AssignPositions(Vectors.Up);

        // SPAWN-TIME LAYOUT ONLY: AutoArrange also queued move orders to pull the wing into
        // formation (fleet-hold AI, which force-closes to point-blank). We DON'T want that —
        // clear those orders so the offsets are used purely as a spawn-time layout and the
        // next round's EngageAll re-issues attack orders cleanly.
        fleet.ClearOrders();

        PlayerFleet = fleet;
        int withOffsets = 0;
        foreach (Ship s in fleet.Ships) if (s.FleetOffset != Vector2.Zero) ++withOffsets;
        Log.Info($"ArenaFightScreen: built arena fleet '{fleet.Name}' with {fleet.Ships.Count} ships " +
                 $"({withOffsets} with assigned formation offsets; spawn-time layout, AssignPositions Up).");
        return fleet;
    }

    // HUB: HANGAR — reuse the existing Customize-Gladiator ShipDesignScreen flow (the GARAGE's
    // CUSTOMIZE button routes here; the designer reads the player empire's unlocked hulls, which
    // the owned roster re-granted, so it's owned-restricted).
    public void OpenHangar() => OpenGladiatorCustomizer();

    // HUB: DEALERSHIP (Phase B) — open the dealership popup to BUY more owned vessels with cash.
    public void OpenDealership()
    {
        ScreenManager?.AddScreen(new ArenaDealershipScreen(this));
    }

    // HUB: GARAGE (Phase B) — open the garage popup to manage the OWNED fleet (pick the active
    // gladiator + open the owned-restricted designer).
    public void OpenGarage()
    {
        ScreenManager?.AddScreen(new ArenaGarageScreen(this));
    }

    // HUB: FLEET SETUP — open the FLEET COMPOSER popup (ArenaFleetScreen): pick which OWNED
    // vessels FIELD together as a fleet (toggle in/out, capped at ArenaMaxFleetSize), then the
    // next round spawns ALL of them in a formation. This REPLACES the hub FLEET button's old
    // bare BuildArenaFleet() call (which only laid out the single existing gladiator) — the
    // missing "set up the fleet" UI the playtester asked for.
    public void OpenFleet()
    {
        ScreenManager?.AddScreen(new ArenaFleetScreen(this));
    }

    // HUB/GARAGE: ITEMS — open the INVENTORY popup (ArenaInventoryScreen): a derived census of
    // every MODULE across the player's owned-vessel designs (UID -> total count), so the player
    // can SEE/access the items on their owned + bought ships. This is the ACCESS/VIEW layer only
    // (the playtester's "i dont seem to have access to the items..." gap); restricting the designer
    // palette to owned modules + strip/transfer are deferred follow-ups.
    public void OpenInventory()
    {
        ScreenManager?.AddScreen(new ArenaInventoryScreen(this));
    }

    // HUB: LADDER — open the contender leaderboard popup. The popup queues an optional
    // challenge; SpawnEnemyRound consumes it on the next fight and spawns that contender's
    // real design instead of the normal generated squad.
    public void OpenLeaderboard()
    {
        ScreenManager?.AddScreen(new ArenaLeaderboardScreen(this));
    }

    public void OpenLeagueSeason()
    {
        ScreenManager?.AddScreen(new ArenaLeagueSeasonScreen(this));
    }

    public void OpenBossChallenges()
    {
        ScreenManager?.AddScreen(new ArenaBossChallengeScreen(this));
    }

    public void OpenFightOptions()
    {
        ScreenManager?.AddScreen(new ArenaFightOptionsScreen(this));
    }

    public void OpenModuleShop()
    {
        ScreenManager?.AddScreen(new ArenaModuleShopScreen(this));
    }

    public void OpenBetting()
    {
        ScreenManager?.AddScreen(new ArenaBettingScreen(this));
    }

    public void OpenPilotSoul()
    {
        ScreenManager?.AddScreen(new ArenaPilotSoulScreen(this));
    }

    public void OpenMemorial()
    {
        ScreenManager?.AddScreen(new ArenaMemorialScreen(this));
    }

    public void OpenBossReward()
    {
        if (!HasPendingBossReward)
            return;
        ScreenManager?.AddScreen(new ArenaBossRewardScreen(this, PendingBossPerkChoices));
    }

    public void OpenConfig()
    {
        ScreenManager?.AddScreen(new ArenaConfigScreen(this));
    }

    public void OpenCareerNewGame()
    {
        ScreenManager?.GoToScreen(new ArenaCareerMenuScreen(ArenaCareerMenuMode.NewGame), clear3DObjects: true);
    }

    public void OpenCareerLoad()
    {
        ScreenManager?.GoToScreen(new ArenaCareerMenuScreen(ArenaCareerMenuMode.Load), clear3DObjects: true);
    }

    public void ExitToMainMenu()
    {
        ScreenManager?.GoToScreen(new Ship_Game.GameScreens.MainMenu.MainMenuScreen(), clear3DObjects: true);
    }

    public bool ManualSaveCareer()
    {
        Career ??= new ArenaCareer();
        Career.Cash = Cash;
        Career.NormalizeForPersistence();
        return CareerManager.Save(Career, CareerSavePath);
    }

    public bool SetPlayerShipsPermadeath(bool enabled)
    {
        Career ??= new ArenaCareer();
        Career.PlayerShipsPermadeath = enabled;
        return ManualSaveCareer();
    }

    public bool SetHardLossEndsRun(bool enabled)
    {
        Career ??= new ArenaCareer();
        Career.HardLossEndsRun = enabled;
        return ManualSaveCareer();
    }

    public bool SetContenderPermadeathChance(float chance)
    {
        Career ??= new ArenaCareer();
        Career.PermadeathChance = Math.Clamp(chance, 0f, 1f);
        return ManualSaveCareer();
    }

    // PILOT TRAITS PLAYTEST TOGGLE (Layer 1, SP-only). The live flag the fight reads is
    // GlobalStats.Defaults.EnablePilotTraits; the career persists the choice so it survives save/load
    // like the other run settings (ApplyCareer re-applies it on load). Both are written together here.
    public bool CurrentEnablePilotTraits => GlobalStats.Defaults?.EnablePilotTraits ?? false;
    public bool CurrentPilotTraitScopeVessel => GlobalStats.Defaults?.PilotTraitScopeVessel ?? false;

    public bool SetEnablePilotTraits(bool enabled)
    {
        Career ??= new ArenaCareer();
        Career.EnablePilotTraits = enabled;
        if (GlobalStats.Defaults != null)
            GlobalStats.Defaults.EnablePilotTraits = enabled;
        return ManualSaveCareer();
    }

    public bool SetPilotTraitScopeVessel(bool shipBound)
    {
        Career ??= new ArenaCareer();
        Career.PilotTraitScopeVessel = shipBound;
        if (GlobalStats.Defaults != null)
            GlobalStats.Defaults.PilotTraitScopeVessel = shipBound;
        return ManualSaveCareer();
    }

    public static int FleetSlotUpgradeCost(int currentMaxFleetSize)
    {
        int purchased = Math.Max(0, currentMaxFleetSize - ArenaCareer.ArenaMaxFleetSize);
        return FleetSlotUpgradeBaseCost + purchased * FleetSlotUpgradeStepCost;
    }

    public bool BuyFleetSlotUpgrade()
    {
        Career ??= new ArenaCareer();
        if (CurrentMaxFleetSize >= ArenaPerks.MaxFleetSizeCap)
            return false;

        int cost = CurrentFleetSlotUpgradeCost;
        if (Cash < cost)
            return false;

        Cash -= cost;
        string[] existing = ArenaPerks.Normalize(Career.Perks);
        var perks = new string[existing.Length + 1];
        Array.Copy(existing, perks, existing.Length);
        perks[existing.Length] = ArenaPerks.FleetSlotId;
        Career.Perks = perks;
        Career.Cash = Cash;
        Career.NormalizeForPersistence();
        CareerManager.Save(Career, CareerSavePath);
        RefreshShop();
        return true;
    }

    public ArenaPerkDefinition[] OfferBossPerks()
    {
        PendingBossPerkChoices = OfferBossPerks(Career);
        return PendingBossPerkChoices.ToArray();
    }

    public static ArenaPerkDefinition[] OfferBossPerks(ArenaCareer career)
        => ArenaPerks.DeterministicOffer(career, count: 3);

    public bool GrantPerk(string perkId)
    {
        Career ??= new ArenaCareer();
        if (!ArenaPerks.TryGet(perkId, out ArenaPerkDefinition perk))
            return false;

        string[] existing = ArenaPerks.Normalize(Career.Perks);
        var perks = new string[existing.Length + 1];
        Array.Copy(existing, perks, existing.Length);
        perks[existing.Length] = perk.Id;
        Career.Perks = perks;
        Career.Cash = Cash;
        Career.NormalizeForPersistence();
        Dictionary<Ship, float> liveHullBefore = null;
        if (perk.Kind == ArenaPerkKind.HullHealth)
        {
            liveHullBefore = new Dictionary<Ship, float>();
            foreach (Ship s in PlayerShips)
                if (s != null && s.Active)
                    liveHullBefore[s] = s.HealthMax;
        }

        ArenaPerks.ApplySingleToEmpire(ArenaPlayer, perk.Id);
        if (perk.Kind == ArenaPerkKind.HullHealth)
        {
            foreach (Ship s in PlayerShips)
                if (s != null && s.Active)
                {
                    float before = liveHullBefore != null && liveHullBefore.TryGetValue(s, out float value) ? value : s.HealthMax;
                    if (s.HealthMax <= before + 0.01f)
                        s.ApplyModuleHealthTechBonus(perk.Value);
                }
        }
        PendingBossPerkChoices = Array.Empty<ArenaPerkDefinition>();
        Career.AddChronicleEvent("boss_win", perk.Id, $"Claimed boss perk {perk.Name}.",
            ChronicleSeed($"boss:{perk.Id}"));
        CareerManager.Save(Career, CareerSavePath);
        RefreshShop();
        Log.Info($"ArenaFightScreen: granted boss perk '{perk.Id}' ({perk.Name}); totalPerks={Career.Perks.Length}");
        return true;
    }

    public FightOption[] GenerateCurrentFightOptions()
        => GenerateFightOptions(Career, ArenaFightOptions.SeedForCareer(Career, FightOptionRound));

    public ArenaBossChallengeOption[] GenerateCurrentBossChallengeOptions(int count = ArenaBossChallengeOptions.DefaultCount)
        => ArenaBossChallengeOptions.Generate(Career, CareerLevel, count);

    public ArenaScoutIntelReport ScoutIntelFor(FightOption option)
        => ArenaScoutIntel.Generate(Career, option, ArenaFightOptions.SeedForCareer(Career, FightOptionRound));

    int FightOptionRound => Phase == RunPhase.Fighting ? Round : HubRound;

    public static FightOption[] GenerateFightOptions(ArenaCareer career, ulong seed)
        => ArenaFightOptions.GenerateFightOptions(career, seed);

    public ArenaBetQuote[] CurrentFightOptionBetQuotes(int stake = ArenaBetting.DefaultStake)
        => GenerateCurrentFightOptions()
            .Where(IsFightOptionLegal)
            .Select(o => ArenaBetting.QuoteFightOption(o, Career, stake))
            .Where(q => q != null)
            .ToArray();

    public ArenaBetQuote[] CurrentContenderBetQuotes(int stake = ArenaBetting.DefaultStake)
        => ArenaBetting.ContenderDuelQuotes(Career, stake, ArenaFightOptions.SeedForCareer(Career, HubRound) ^ 0xB37B37ul);

    public ArenaBetResult PlaceFightOptionBet(string optionId, int stake = ArenaBetting.DefaultStake)
    {
        if ((Phase != RunPhase.Shopping && Phase != RunPhase.Idle) || optionId.IsEmpty())
            return new ArenaBetResult(false, false, "Bets can only be placed from the hub.",
                CurrentCash, CurrentCash);
        if (HasPendingBet)
            return new ArenaBetResult(false, false, "Resolve the open betting slip first.",
                CurrentCash, CurrentCash);

        FightOption option = GenerateCurrentFightOptions()
            .FirstOrDefault(o => string.Equals(o.OptionId, optionId, StringComparison.Ordinal));
        if (option == null || !IsFightOptionLegal(option))
            return new ArenaBetResult(false, false, "That fight option is no longer available.",
                CurrentCash, CurrentCash);
        if (stake <= 0)
            return new ArenaBetResult(false, false, "Stake must be positive.",
                CurrentCash, CurrentCash);
        if (Cash < stake)
            return new ArenaBetResult(false, false, $"Need ${stake} to place this bet.",
                CurrentCash, CurrentCash);

        // Freeze the exact selected fight before deducting cash. Fight-option seeds include
        // cash, so deducting first would re-roll the option set and make the slip ambiguous.
        PendingFightOption = option;
        PendingChallengeContenderName = null;
        PendingChallengeDesignName = null;

        Career ??= new ArenaCareer();
        Career.Cash = Cash;
        ArenaBetResult result = ArenaBetting.PlaceFightOptionBet(Career, option, stake);
        Cash = Career.Cash;
        Career.NormalizeForPersistence();
        CareerManager.Save(Career, CareerSavePath);
        RefreshShop();
        Log.Info($"ArenaFightScreen: fight-option bet result success={result.Success} " +
                 $"stake=${result.Stake} odds={result.Odds:0.00} cash={Cash} option={option.OptionId}.");
        return result;
    }

    public ArenaBetResult PlaceContenderBet(string contenderName, int stake = ArenaBetting.DefaultStake)
    {
        if ((Phase != RunPhase.Shopping && Phase != RunPhase.Idle) || contenderName.IsEmpty())
            return new ArenaBetResult(false, false, "Bets can only be placed from the hub.",
                CurrentCash, CurrentCash);

        Career ??= new ArenaCareer();
        Career.Cash = Cash;
        ulong seed = ArenaFightOptions.SeedForCareer(Career, HubRound) ^ 0xB37B37ul;
        ArenaBetResult result = ArenaBetting.PlaceContenderDuelBet(Career, contenderName, stake, seed);
        Cash = Career.Cash;
        Career.NormalizeForPersistence();
        CareerManager.Save(Career, CareerSavePath);
        RefreshShop();
        Log.Info($"ArenaFightScreen: contender bet result success={result.Success} won={result.Won} " +
                 $"stake=${result.Stake} payout=${result.Payout} cash={Cash} pick='{contenderName}'.");
        return result;
    }

    ArenaBetResult ResolvePendingFightOptionBet(bool playerWon)
    {
        if (!HasPendingBet)
            return new ArenaBetResult(false, false, "No open betting slip.", CurrentCash, CurrentCash);

        Career ??= new ArenaCareer();
        Career.Cash = Cash;
        ArenaBetResult result = ArenaBetting.ResolvePendingFightOptionBet(Career, playerWon);
        Cash = Career.Cash;
        Career.NormalizeForPersistence();
        CareerManager.Save(Career, CareerSavePath);
        RefreshShop();
        Log.Info($"ArenaFightScreen: resolved pending bet won={result.Won} " +
                 $"stake=${result.Stake} payout=${result.Payout} cash={Cash}.");
        return result;
    }

    public bool QueueEliteBossFight()
    {
        if (Phase != RunPhase.Shopping && Phase != RunPhase.Idle)
            return false;

        foreach (FightOption option in GenerateCurrentFightOptions())
        {
            if (option != null && option.RiskTier == FightRiskTier.Elite)
                return SelectFightOption(option.OptionId);
        }
        return false;
    }

    public ArenaTeam BuildPlayerLeagueTeam(int teamSize)
        => ArenaLeagues.BuildPlayerTeam(Career, "PLAYER", teamSize,
            ArenaFightOptions.SeedForCareer(Career, HubRound));

    public ArenaTeam PickEnemyLeagueTeam(int teamSize)
    {
        ArenaLeagues.EnsureTeams(Career, teamSize);
        return ArenaLeagues.PickEnemyTeam(Career, teamSize, ArenaFightOptions.SeedForCareer(Career, HubRound));
    }

    public Task<ArenaBigLeagueReport> RunLeagueSeasonAsync(ArenaLeagueSeasonOptions options)
        => ArenaLeagues.RunLeagueSeasonAsync(Career, options);

    public Task<ArenaBigLeagueReport> RunLeagueSeasonAndApplyAsync(ArenaLeagueSeasonOptions options)
        => ArenaLeagues.RunLeagueSeasonAndApplyAsync(Career, options);

    public ArenaLeagueSeasonOptions BuildLeagueSeasonOptionsForUi()
    {
        const int teamSize = 3;
        const int maxTeams = 4;
        const int matchupBudget = 3;
        const int duelTicks = 240;
        ulong seed = ArenaFightOptions.SeedForCareer(Career, HubRound) ^ 0x1EA6_C11B_2026ul;
        return new ArenaLeagueSeasonOptions(teamSize, matchupBudget, duelTicks, seed,
            runParallel: false, maxTeams: maxTeams, spawnOffset: 2200f);
    }

    public ArenaBigLeagueReport RunLeagueSeasonAndApplyForUi()
    {
        Career ??= new ArenaCareer();
        ArenaBigLeagueReport report = RunLeagueSeasonAndApplyAsync(BuildLeagueSeasonOptionsForUi())
            .GetAwaiter().GetResult();
        ManualSaveCareer();
        return report;
    }

    public bool SelectFightOption(string optionId)
    {
        if ((Phase != RunPhase.Shopping && Phase != RunPhase.Idle) || optionId.IsEmpty())
            return false;
        if (Career?.PendingBet != null
            && !string.Equals(Career.PendingBet.OptionId, optionId, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (FightOption option in GenerateCurrentFightOptions())
        {
            if (!string.Equals(option.OptionId, optionId, StringComparison.Ordinal))
                continue;
            if (!IsFightOptionLegal(option))
                return false;

            PendingFightOption = option;
            PendingChallengeContenderName = null;
            PendingChallengeDesignName = null;
            Log.Info($"ArenaFightScreen: queued fight option {option.FightType}/{option.DifficultyTier}/{option.RiskTier} id={option.OptionId} " +
                     $"cash=${option.RewardCash} loot={option.LootRolls}.");
            return true;
        }
        return false;
    }

    public bool QueueDefaultFightOption()
    {
        if (PendingFightOption != null)
            return true;
        if (PendingChallengeDesignName.NotEmpty())
            return false;

        FightOption[] options = GenerateCurrentFightOptions();
        FightOption option = options
            .OrderBy(o => ArenaFightOptions.DifficultyRank(o.DifficultyTier))
            .ThenBy(o => ArenaFightOptions.RiskRank(o.RiskTier))
            .FirstOrDefault();
        if (option == null || !IsFightOptionLegal(option))
            return false;

        PendingFightOption = option;
        Log.Info($"ArenaFightScreen: queued default roulette fight {option.FightType}/{option.DifficultyTier}/{option.RiskTier} " +
                 $"id={option.OptionId} level={CareerLevel} tier={CurrentAllowedCombatTier}.");
        return true;
    }

    bool IsFightOptionLegal(FightOption option)
    {
        if (option == null)
            return false;
        if (option.RiskTier == FightRiskTier.Elite
            && MaxAllowedCombatTierForCareerLevel(CareerLevel) < MaxCombatTier)
            return false;
        if (option.MaxEnemyTier > MaxAllowedCombatTierForCareerLevel(CareerLevel))
            return false;
        if (option.EscortDesignName.NotEmpty()
            && (!ResourceManager.Ships.GetDesign(option.EscortDesignName, out IShipDesign escort)
                || !IsLegalCombatCraft(escort)
                || !IsDesignAllowedForCareerLevel(escort, CareerLevel)))
            return false;
        if (option.BossDesignName.NotEmpty()
            && (!ResourceManager.Ships.GetDesign(option.BossDesignName, out IShipDesign boss)
                || !IsLegalCombatCraft(boss)
                || !IsDesignAllowedForCareerLevel(boss, CareerLevel)))
            return false;
        return true;
    }

    public int GrantFightOptionRewards(FightOption option)
    {
        Career ??= new ArenaCareer();
        if (option == null)
            return 0;

        int cashBefore = Cash;
        Cash += option.RewardCash;
        Career.Fame = Math.Max(0, Career.Fame + option.RewardFame);
        foreach (LootReward reward in option.PreviewLoot)
        {
            switch (reward.Kind)
            {
                case ArenaLootKind.ResearchUnlock:
                    if (reward.ModuleUid.NotEmpty())
                        ResearchArenaModule(reward.ModuleUid);
                    break;
                case ArenaLootKind.ChassisUnlock:
                    GrantLootChassis(reward);
                    break;
                case ArenaLootKind.Perk:
                    if (reward.PerkId.NotEmpty())
                        AddPerkReward(reward.PerkId);
                    break;
                case ArenaLootKind.BonusCash:
                    Cash += reward.Cash;
                    break;
            }
        }

        Career.Cash = Cash;
        Career.NormalizeForPersistence();
        CareerManager.Save(Career, CareerSavePath);
        RefreshShop();
        int delta = Cash - cashBefore;
        Log.Info($"ArenaFightScreen: granted fight-option reward {option.RiskTier} id={option.OptionId} " +
                 $"cashDelta=${delta} fame+={option.RewardFame} fame={Career.Fame} " +
                 $"researchedModules={Career.ResearchedModules.Length} perks={Career.Perks.Length}.");
        return delta;
    }

    void ApplyFightLossFamePenalty()
    {
        Career ??= new ArenaCareer();
        int penalty = ArenaFightOptions.FameLossPenalty(ActiveFightOption);
        if (penalty <= 0)
            return;

        int before = Career.Fame;
        Career.Fame = Math.Max(0, Career.Fame - penalty);
        if (Career.Fame != before)
            Log.Info($"ArenaFightScreen: loss dented fame {before}->{Career.Fame} " +
                     $"option={ActiveFightOption?.DifficultyTier}/{ActiveFightOption?.RiskTier}.");
    }

    public ArenaModuleShopItem[] CurrentModuleShopCatalog(bool affordableOnly = true)
        => ModuleShopCatalog(CurrentCareerLevel, CurrentCash, CurrentFame, Career?.Perks,
            Career?.ResearchedModules, affordableOnly);

    public ArenaMetaShopItem[] CurrentMetaShopCatalog(bool includeLocked = false)
        => MetaShopCatalog(Career, CurrentCash, includeLocked);

    public static ArenaModuleShopItem[] ModuleShopCatalog(int careerLevel, int cash, bool affordableOnly)
        => ModuleShopCatalog(careerLevel, cash, fame: 0, Empty<string>.Array, Empty<string>.Array,
            affordableOnly);

    public static ArenaModuleShopItem[] ModuleShopCatalog(int careerLevel, int cash, int fame,
        string[] perks, bool affordableOnly)
        => ModuleShopCatalog(careerLevel, cash, fame, perks, Empty<string>.Array, affordableOnly);

    public static ArenaModuleShopItem[] ModuleShopCatalog(int careerLevel, int cash, int fame,
        string[] perks, string[] researchedModules, bool affordableOnly)
    {
        var byUid = new Dictionary<string, ArenaModuleShopItem>(StringComparer.Ordinal);
        int allowedTier = ModuleShopTierForFameAndPerks(fame, perks);
        var researched = new HashSet<string>(ArenaCareer.NormalizeResearchedModules(researchedModules),
            StringComparer.Ordinal);
        foreach (IShipDesign design in ResourceManager.Ships.Designs)
        {
            if (!IsLegalCombatCraft(design) || !IsDesignAllowedForCareerLevel(design, careerLevel))
                continue;

            IReadOnlyList<string> uids = DesignModuleUidsForCensus(design.Name);
            if (uids == null)
                continue;

            foreach (string uid in uids)
            {
                if (uid.IsEmpty() || byUid.ContainsKey(uid))
                    continue;
                if (researched.Contains(uid))
                    continue;
                if (!ResourceManager.GetModuleTemplate(uid, out ShipModule module)
                    || !IsArenaModuleShopItem(module))
                    continue;

                int price = ModuleResearchCost(module);
                int tier = ModuleShopTier(module);
                if (tier > allowedTier)
                    continue;
                if (affordableOnly && price > cash)
                    continue;
                byUid[uid] = new ArenaModuleShopItem(uid, ModuleDisplayNameForCensus(uid),
                    price, module.ModuleType.ToString(), tier, RequiredFameForModuleShopTier(tier));
            }
        }

        return byUid.Values
            .OrderBy(i => ModuleShopCategoryRank(ModuleTemplateForShopItem(i)))
            .ThenBy(i => ModuleShopDesignerSortType(ModuleTemplateForShopItem(i)))
            .ThenBy(i => ModuleShopDesignerSortArea(ModuleTemplateForShopItem(i)))
            .ThenBy(i => i.Price)
            .ThenBy(i => i.DisplayName, StringComparer.Ordinal)
            .ThenBy(i => i.ModuleUid, StringComparer.Ordinal)
            .ToArray();
    }

    public static int ModuleShopTierForFame(int fame)
    {
        fame = Math.Max(0, fame);
        if (fame >= 50) return 3;
        if (fame >= 20) return 2;
        return 1;
    }

    public static int ModuleShopTierForCareer(ArenaCareer career)
        => ModuleShopTierForFameAndPerks(career?.Fame ?? 0, career?.Perks);

    public static int ModuleShopTierForFameAndPerks(int fame, string[] perks)
        => Math.Clamp(ModuleShopTierForFame(fame) + ArenaPerks.ResearchTierBonus(perks), 1, 3);

    public static int RequiredFameForModuleShopTier(int tier) => Math.Clamp(tier, 1, 3) switch
    {
        1 => 0,
        2 => 20,
        _ => 50,
    };

    public static ArenaMetaShopItem[] MetaShopCatalog(ArenaCareer career, int cash, bool includeLocked)
    {
        career ??= new ArenaCareer();
        int fame = Math.Max(0, career.Fame);
        string[] perks = ArenaPerks.Normalize(career.Perks);
        var items = new List<ArenaMetaShopItem>(4)
        {
            MetaItem(ArenaPerks.FleetSlotId, "Fleet Commission",
                "+1 fieldable fleet slot, up to 100.", ArenaMetaUpgradeKind.FleetSlot,
                FleetSlotUpgradeCost(career.MaxFleetSize), requiredFame: 0,
                purchased: Math.Max(0, career.MaxFleetSize - ArenaCareer.ArenaMaxFleetSize),
                maxPurchases: ArenaPerks.MaxFleetSizeCap - ArenaCareer.ArenaMaxFleetSize,
                fame, cash),
            MetaItem(ArenaPerks.FightChoiceId, "Fight Broker",
                "+1 generated fight option on future BOUT slates.", ArenaMetaUpgradeKind.FightChoice,
                650 + ArenaPerks.Count(perks, ArenaPerks.FightChoiceId) * 350, requiredFame: 20,
                purchased: ArenaPerks.Count(perks, ArenaPerks.FightChoiceId), maxPurchases: 3,
                fame, cash),
            MetaItem(ArenaPerks.ResearchId, "Research Grant",
                "+1 effective module-shop tech tier.", ArenaMetaUpgradeKind.Research,
                800 + ArenaPerks.Count(perks, ArenaPerks.ResearchId) * 500, requiredFame: 20,
                purchased: ArenaPerks.Count(perks, ArenaPerks.ResearchId), maxPurchases: 2,
                fame, cash),
            MetaItem(ArenaPerks.ScoutId, "Scout Network",
                "Unlocks richer pre-fight scouting readouts.", ArenaMetaUpgradeKind.Scout,
                900, requiredFame: 50,
                purchased: ArenaPerks.Count(perks, ArenaPerks.ScoutId), maxPurchases: 1,
                fame, cash),
        };

        return items
            .Where(i => includeLocked || i.IsUnlockedByFame && !i.IsSoldOut)
            .OrderBy(i => i.RequiredFame)
            .ThenBy(i => i.Kind)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToArray();
    }

    static ArenaMetaShopItem MetaItem(string id, string name, string description,
        ArenaMetaUpgradeKind kind, int cost, int requiredFame, int purchased, int maxPurchases,
        int fame, int cash)
    {
        bool unlocked = fame >= requiredFame;
        return new ArenaMetaShopItem(id, name, description, kind, cost, requiredFame,
            purchased, maxPurchases, unlocked, cash >= cost);
    }

    public bool BuyMetaUpgrade(string id)
    {
        if ((Phase != RunPhase.Shopping && Phase != RunPhase.Idle) || id.IsEmpty())
            return false;

        Career ??= new ArenaCareer();
        ArenaMetaShopItem item = MetaShopCatalog(Career, Cash, includeLocked: true)
            .FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));
        if (item == null || !item.CanPurchase)
            return false;

        if (item.Kind == ArenaMetaUpgradeKind.FleetSlot)
            return BuyFleetSlotUpgrade();

        if (!ArenaPerks.IsKnown(item.Id))
            return false;

        Cash -= item.Cost;
        string[] existing = ArenaPerks.Normalize(Career.Perks);
        var perks = new string[existing.Length + 1];
        Array.Copy(existing, perks, existing.Length);
        perks[existing.Length] = item.Id;
        Career.Perks = perks;
        Career.Cash = Cash;
        Career.NormalizeForPersistence();
        CareerManager.Save(Career, CareerSavePath);
        RefreshShop();
        Log.Info($"ArenaFightScreen: bought meta upgrade '{item.Id}' for ${item.Cost}; " +
                 $"cash=${Cash} fame={Career.Fame} perks={Career.Perks.Length}.");
        return true;
    }

    public bool BuyArenaModule(string moduleUid)
    {
        if ((Phase != RunPhase.Shopping && Phase != RunPhase.Idle) || moduleUid.IsEmpty())
            return false;
        foreach (ArenaModuleShopItem item in ModuleShopCatalog(CurrentCareerLevel, CurrentCash,
                     CurrentFame, Career?.Perks, affordableOnly: false))
        {
            if (!string.Equals(item.ModuleUid, moduleUid, StringComparison.Ordinal))
                continue;
            if (item.Price > Cash)
                return false;
            if (Career?.IsModuleResearched(item.ModuleUid) == true)
                return false;

            Career ??= new ArenaCareer();
            Cash -= item.Price;
            Career.Cash = Cash;
            ResearchArenaModule(item.ModuleUid);
            Career.NormalizeForPersistence();
            CareerManager.Save(Career, CareerSavePath);
            RefreshShop();
            Log.Info($"ArenaFightScreen: researched arena module '{item.ModuleUid}' for ${item.Price}; " +
                     $"cash=${Cash} researched={Career.ResearchedModules.Length}.");
            return true;
        }
        return false;
    }

    static bool IsArenaModuleShopItem(ShipModule module)
    {
        if (module == null || module.UID.IsEmpty() || module.Cost <= 0f)
            return false;
        if (module.Is(ShipModuleType.Colony) || module.Is(ShipModuleType.Construction))
            return false;
        if (module.IsSupplyBay || module.IsTroopBay)
            return false;
        if (module.Is(ShipModuleType.Hangar) && module.InstalledWeapon == null)
            return false;
        return module.InstalledWeapon != null
            || module.Is(ShipModuleType.Armor)
            || module.Is(ShipModuleType.Shield)
            || module.Is(ShipModuleType.Engine)
            || module.Is(ShipModuleType.PowerPlant)
            || module.Is(ShipModuleType.PowerConduit)
            || module.IsCommandModule
            || module.SensorRange > 0f
            || module.OrdinanceCapacity > 0;
    }

    static ShipModule ModuleTemplateForShopItem(ArenaModuleShopItem item)
        => item != null && ResourceManager.GetModuleTemplate(item.ModuleUid, out ShipModule module)
            ? module
            : null;

    public static int ModuleShopCategoryRank(ShipModule module)
    {
        if (module == null)
            return 4;
        if (IsDesignerWeaponModule(module))
            return 0;
        if (IsDesignerDefenseModule(module))
            return 1;
        if (IsDesignerPowerModule(module))
            return 2;
        if (IsDesignerSpecialModule(module))
            return 3;
        return 4;
    }

    public static int ModuleShopDesignerSortType(ShipModule module)
        => module == null ? int.MaxValue
            : module.ModuleType == ShipModuleType.PowerConduit ? 0 : (int)module.ModuleType;

    public static int ModuleShopDesignerSortArea(ShipModule module)
        => module?.Area ?? int.MaxValue;

    static bool IsDesignerWeaponModule(ShipModule module)
        => module.IsWeapon || module.ModuleType == ShipModuleType.Bomb;

    static bool IsDesignerPowerModule(ShipModule module)
    {
        ShipModuleType type = module.ModuleType == ShipModuleType.PowerConduit
            ? ShipModuleType.PowerPlant
            : module.ModuleType;
        return type == ShipModuleType.PowerPlant
            || type == ShipModuleType.Engine
            || type == ShipModuleType.FuelCell;
    }

    static bool IsDesignerDefenseModule(ShipModule module)
    {
        ShipModuleType type = module.ModuleType;
        return type == ShipModuleType.Shield
            || type == ShipModuleType.Countermeasure
            || type == ShipModuleType.Armor;
    }

    static bool IsDesignerSpecialModule(ShipModule module)
    {
        ShipModuleType type = module.ModuleType;
        return type == ShipModuleType.Troop
            || type == ShipModuleType.Colony
            || type == ShipModuleType.Command
            || type == ShipModuleType.Storage
            || type == ShipModuleType.Hangar
            || type == ShipModuleType.Sensors
            || type == ShipModuleType.Special
            || type == ShipModuleType.Transporter
            || type == ShipModuleType.Ordnance
            || type == ShipModuleType.Construction;
    }

    static Dictionary<string, int> ModuleShopTierCache;

    public static int ModuleShopTier(ShipModule module)
    {
        if (module == null || module.UID.IsEmpty())
            return 1;
        Dictionary<string, int> tiers = ModuleShopTiersByUid();
        int techTier = tiers.TryGetValue(module.UID, out int tier) ? tier : 1;
        return Math.Clamp(Math.Max(Math.Max(techTier, ModuleShopTierFromPrice(ModuleShopPrice(module))),
            ModuleShopPowerTier(module)), 1, 3);
    }

    public static int ModuleShopPowerTier(ShipModule module)
    {
        float score = ModuleShopPowerScore(module);
        if (score >= 18f) return 3;
        if (score >= 6f) return 2;
        return 1;
    }

    public static float ModuleShopPowerScore(ShipModule module)
    {
        if (module == null)
            return 0f;

        int slots = Math.Max(1, module.Area);
        float calculated = module.CalculateModuleOffenseDefense(slots, forceRecalculate: true);
        float rawCombat =
            Math.Max(0f, module.CalculateModuleOffense()) +
            Math.Max(0f, module.ActualMaxHealth) * 0.01f +
            Math.Max(0f, module.ShieldPowerMax) * 0.025f +
            Math.Max(0f, module.ShieldRechargeRate) * 0.15f +
            Math.Max(0f, module.ShieldRechargeCombatRate) * 0.25f +
            Math.Max(0f, module.Deflection) * 0.04f +
            Math.Max(0, module.APResist) * 0.75f +
            Math.Max(0f, module.KineticResist + module.EnergyResist + module.PlasmaResist
                          + module.BeamResist + module.ExplosiveResist) * 0.4f +
            Math.Max(0f, module.ShieldKineticResist + module.ShieldEnergyResist
                          + module.ShieldPlasmaResist + module.ShieldBeamResist
                          + module.ShieldExplosiveResist) * 0.4f +
            Math.Max(0, module.ExplosionDamage) * 0.01f +
            Math.Max(0f, module.OrdinanceCapacity) * 0.01f;

        return Math.Max(calculated, rawCombat);
    }

    static Dictionary<string, int> ModuleShopTiersByUid()
    {
        if (ModuleShopTierCache != null)
            return ModuleShopTierCache;

        var tiers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Technology tech in ResourceManager.TechsList)
        {
            if (tech == null || tech.ModulesUnlocked == null)
                continue;
            int tier = TechShopTier(tech);
            foreach (Technology.UnlockedMod unlocked in tech.ModulesUnlocked)
            {
                string uid = unlocked.ModuleUID;
                if (uid.IsEmpty())
                    continue;
                if (!tiers.TryGetValue(uid, out int existing) || tier < existing)
                    tiers[uid] = tier;
            }
        }
        ModuleShopTierCache = tiers;
        return tiers;
    }

    static int TechShopTier(Technology tech)
    {
        if (tech == null || tech.IsRootNode)
            return 1;
        int depth = Math.Max(0, tech.Parents?.Length ?? 0);
        if (depth <= 1 && tech.Cost <= 750f)
            return 1;
        if (depth <= 3 && tech.Cost <= 2500f)
            return 2;
        return 3;
    }

    static int ModuleShopTierFromPrice(int price)
    {
        if (price <= 150) return 1;
        if (price <= 450) return 2;
        return 3;
    }

    static int ModuleShopPrice(ShipModule module)
    {
        float raw = Math.Max(25f, module.Cost * 0.5f);
        if (module.InstalledWeapon != null)
            raw *= 1.2f;
        return (int)Math.Ceiling(raw / 5f) * 5;
    }

    public static int ModuleResearchCost(ShipModule module)
    {
        if (module == null)
            return int.MaxValue;
        int tier = ModuleShopTier(module);
        float power = ModuleShopPowerScore(module);
        int basePrice = ModuleShopPrice(module);
        int tierBase = tier switch
        {
            1 => 150,
            2 => 600,
            _ => 1600,
        };
        int powerCost = (int)Math.Ceiling(power * (tier == 1 ? 20f : tier == 2 ? 35f : 55f));
        int cost = tierBase + basePrice * (tier + 1) + powerCost;
        return (int)Math.Ceiling(cost / 25f) * 25;
    }

    public static int ModuleRefitCost(string moduleUid)
    {
        if (moduleUid.IsEmpty() || !ResourceManager.GetModuleTemplate(moduleUid, out ShipModule module))
            return 0;
        return Math.Max(25, (int)Math.Ceiling(ModuleResearchCost(module) * 0.20f / 5f) * 5);
    }

    void GrantLootChassis(LootReward reward)
    {
        if (reward.HullName.NotEmpty() && ArenaPlayer != null)
        {
            ArenaPlayer.UnlockEmpireHull(reward.HullName);
            ArenaPlayer.UpdateShipsWeCanBuild(new Array<string> { reward.HullName });
        }

        if (reward.ChassisStyle.NotEmpty())
        {
            GrantChassis(ArenaPlayer, reward.ChassisStyle);
            GrantedChassisFactions.Add(reward.ChassisStyle);
            var styles = new HashSet<string>(Career.UnlockedChassisStyles ?? Empty<string>.Array, StringComparer.Ordinal)
            {
                reward.ChassisStyle
            };
            Career.UnlockedChassisStyles = styles.ToArray();
        }
    }

    bool AddPerkReward(string perkId)
    {
        if (!ArenaPerks.TryGet(perkId, out ArenaPerkDefinition perk))
            return false;

        string[] existing = ArenaPerks.Normalize(Career.Perks);
        var perks = new string[existing.Length + 1];
        Array.Copy(existing, perks, existing.Length);
        perks[existing.Length] = perk.Id;
        Career.Perks = perks;

        Dictionary<Ship, float> liveHullBefore = null;
        if (perk.Kind == ArenaPerkKind.HullHealth)
        {
            liveHullBefore = new Dictionary<Ship, float>();
            foreach (Ship s in PlayerShips)
                if (s != null && s.Active)
                    liveHullBefore[s] = s.HealthMax;
        }

        ArenaPerks.ApplySingleToEmpire(ArenaPlayer, perk.Id);
        if (perk.Kind == ArenaPerkKind.HullHealth)
        {
            foreach (Ship s in PlayerShips)
                if (s != null && s.Active)
                {
                    float before = liveHullBefore != null && liveHullBefore.TryGetValue(s, out float value) ? value : s.HealthMax;
                    if (s.HealthMax <= before + 0.01f)
                        s.ApplyModuleHealthTechBonus(perk.Value);
                }
        }
        return true;
    }

    /// <summary>The persistent AI contenders for the current career, seeded on demand.</summary>
    public ContenderRecord[] Contenders
    {
        get
        {
            Career ??= new ArenaCareer();
            CareerLadder.EnsureContenders(Career);
            return Career.Contenders ?? Empty<ContenderRecord>.Array;
        }
    }

    /// <summary>The contender currently queued for the next fight, or empty when none.</summary>
    public string QueuedChallengeName => PendingChallengeContenderName ?? "";

    /// <summary>
    /// Queue a specific contender as the NEXT fight. The contender can be identified by display
    /// name or design name. Returns false unless the run is in the between-fights shopping phase,
    /// the contender belongs to the career roster, and its design resolves to a legal arena warship.
    /// </summary>
    public bool ChallengeContender(string contenderNameOrDesignName)
    {
        if ((Phase != RunPhase.Shopping && Phase != RunPhase.Idle) || contenderNameOrDesignName.IsEmpty())
            return false;

        foreach (ContenderRecord c in Contenders)
        {
            if (c == null)
                continue;
            bool match = string.Equals(c.Name, contenderNameOrDesignName, StringComparison.Ordinal)
                      || string.Equals(c.DesignName, contenderNameOrDesignName, StringComparison.Ordinal);
            if (!match)
                continue;
            if (c.DesignName.IsEmpty()
                || !ResourceManager.Ships.GetDesign(c.DesignName, out IShipDesign design)
                || !IsLegalCombatCraft(design)
                || !IsDesignAllowedForCareerLevel(design, CareerLevel))
                return false;

            PendingChallengeContenderName = c.Name;
            PendingChallengeDesignName = design.Name;
            Log.Info($"ArenaFightScreen: queued ladder challenge '{c.Name}' design='{design.Name}' " +
                     $"rating={c.Rating} W/L={c.Wins}/{c.Losses}.");
            return true;
        }
        return false;
    }

    // =====================================================================================
    // RESEARCHED-MODULES CENSUS — the ACCESS layer's data source. A DERIVED view of every module
    // UID permanently researched for this career. Old finite-salvage saves migrate into this set.
    // =====================================================================================

    /// <summary>
    /// The researched module census for the current career: each permanently researched module
    /// UID appears once with a friendly display name. Never null. PUBLIC + headless-testable.
    /// </summary>
    public ArenaCareer.ModuleCensusEntry[] GetOwnedModuleCensus()
        => ArenaCareer.BuildResearchedModuleCensus(Career?.ResearchedModules, ModuleDisplayNameForCensus);

    public static string[] DesignerAvailableModuleUids(Empire player, IShipDesign currentDesign)
    {
        if (player == null || currentDesign == null)
            return Empty<string>.Array;

        var uids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (ShipModule template in ResourceManager.ShipModuleTemplates)
        {
            if (template == null || template.UID.IsEmpty())
                continue;
            if (!player.IsModuleUnlocked(template.UID))
                continue;
            if (!IsModuleAvailableForArenaDesignerHullRole(currentDesign.Role, template))
                continue;
            uids.Add(template.UID);
        }
        return uids.ToArray();
    }

    public static bool IsModuleAvailableForArenaDesignerHullRole(RoleName role, ShipModule mod)
    {
        if (mod == null)
            return false;
        switch (role)
        {
            case RoleName.drone      when mod.DroneModule      == false:
            case RoleName.scout      when mod.FighterModule    == false:
            case RoleName.fighter    when mod.FighterModule    == false:
            case RoleName.corvette   when mod.CorvetteModule   == false:
            case RoleName.gunboat    when mod.CorvetteModule   == false:
            case RoleName.frigate    when mod.FrigateModule    == false:
            case RoleName.destroyer  when mod.DestroyerModule  == false:
            case RoleName.cruiser    when mod.CruiserModule    == false:
            case RoleName.battleship when mod.BattleshipModule == false:
            case RoleName.capital    when mod.CapitalModule    == false:
            case RoleName.freighter  when mod.FreighterModule  == false:
            case RoleName.platform   when mod.PlatformModule   == false:
            case RoleName.station    when mod.StationModule    == false:
                return false;
        }
        return true;
    }

    public DestroyedModuleSlot[] DestroyedModuleSlots(string vesselId)
    {
        OwnedVessel vessel = Career?.FindOwnedVessel(vesselId);
        return ArenaCareer.NormalizeDestroyedModules(vessel?.DestroyedModules);
    }

    public ArenaRefitResult RefitFirstDestroyedModuleForCash(string vesselId)
    {
        DestroyedModuleSlot[] slots = DestroyedModuleSlots(vesselId);
        if (slots.Length == 0)
            return new ArenaRefitResult(false, "No destroyed module slots to refit.", vesselId);

        foreach (DestroyedModuleSlot slot in slots)
        {
            ArenaRefitResult result = RefitDestroyedModule(vesselId, slot.SlotIndex, slot.ModuleUid);
            if (result.Success)
                return result;
        }

        return new ArenaRefitResult(false, "No restorable destroyed modules.", vesselId);
    }

    public ArenaRefitResult RefitFirstDestroyedModuleFromSalvage(string vesselId)
        => RefitFirstDestroyedModuleForCash(vesselId);

    public ArenaRefitResult RefitDestroyedModule(string vesselId, int slotIndex, string moduleUid)
    {
        Career ??= new ArenaCareer();
        OwnedVessel vessel = Career.FindOwnedVessel(vesselId);
        if (vessel == null)
            return new ArenaRefitResult(false, "That vessel is not owned.", vesselId, slotIndex, moduleUid);
        if (slotIndex < 0)
            return new ArenaRefitResult(false, "No destroyed module slot selected.", vesselId, slotIndex, moduleUid);
        DestroyedModuleSlot[] destroyed = ArenaCareer.NormalizeDestroyedModules(vessel.DestroyedModules);
        DestroyedModuleSlot destroyedSlot = destroyed.FirstOrDefault(s => s.SlotIndex == slotIndex);
        if (destroyedSlot == null)
            return new ArenaRefitResult(false, "That module slot is not destroyed.", vesselId, slotIndex, moduleUid);
        if (!TryGetDesignSlot(vessel.DesignName, slotIndex, out DesignSlot baseSlot))
            return new ArenaRefitResult(false, "The vessel design no longer has that slot.", vesselId, slotIndex, moduleUid);

        string restoreUid = destroyedSlot.ModuleUid.NotEmpty() ? destroyedSlot.ModuleUid : baseSlot.ModuleUID;
        if (moduleUid.NotEmpty() && !string.Equals(moduleUid, restoreUid, StringComparison.Ordinal))
            return new ArenaRefitResult(false, "Destroyed slots restore their researched original module.", vesselId, slotIndex, moduleUid);
        moduleUid = restoreUid;
        if (moduleUid.IsEmpty() || !ResourceManager.GetModuleTemplate(moduleUid, out ShipModule module))
            return new ArenaRefitResult(false, "Destroyed module cannot be restored.", vesselId, slotIndex, moduleUid);
        if (!Career.IsModuleResearched(moduleUid))
            Career.ResearchModule(moduleUid);
        UnlockModuleForArenaDesigner(ArenaPlayer, moduleUid);
        if (!CanRefitModuleIntoSlot(module, baseSlot))
            return new ArenaRefitResult(false, "That module no longer fits this slot.", vesselId, slotIndex, moduleUid);

        int cost = ModuleRefitCost(moduleUid);
        if (Cash < cost)
            return new ArenaRefitResult(false, $"Need ${cost} to refit.", vesselId, slotIndex, moduleUid,
                cost, Cash, Cash);

        int cashBefore = Cash;
        Cash -= cost;
        vessel.DestroyedModules = ArenaCareer.NormalizeDestroyedModules(
            destroyed.Where(s => s.SlotIndex != slotIndex).ToArray());
        vessel.ModuleOverrides = ArenaCareer.NormalizeModuleOverrides(
            (vessel.ModuleOverrides ?? Empty<ModuleSlotOverride>.Array)
            .Where(o => o != null && o.SlotIndex != slotIndex)
            .ToArray());
        Career.Cash = Cash;
        Career.AddChronicleEvent("refit", $"{vesselId}:{slotIndex}",
            $"Restored slot {slotIndex} ({ModuleDisplayNameForCensus(moduleUid)}) for ${cost}.",
            ChronicleSeed($"refit:{vesselId}:{slotIndex}:{moduleUid}:{cost}"));
        CareerManager.Save(Career, CareerSavePath);

        Log.Info($"ArenaFightScreen: refit vessel id={vesselId} slot={slotIndex} " +
                 $"module={moduleUid} cash ${cashBefore}->{Cash}.");
        return new ArenaRefitResult(true, $"Restored slot {slotIndex} with {ModuleDisplayNameForCensus(moduleUid)}.",
            vesselId, slotIndex, moduleUid, cost, cashBefore, Cash);
    }

    static bool TryGetDesignSlot(string designName, int slotIndex, out DesignSlot slot)
    {
        slot = null;
        if (designName.IsEmpty() || slotIndex < 0
            || !ResourceManager.Ships.GetDesign(designName, out IShipDesign design)
            || design is not ShipDesignData shipDesign)
            return false;
        DesignSlot[] slots = shipDesign.GetOrLoadDesignSlots();
        if (slots == null || slotIndex >= slots.Length)
            return false;
        slot = slots[slotIndex];
        return slot != null;
    }

    static bool CanRefitModuleIntoSlot(ShipModule module, DesignSlot slot)
    {
        if (module == null || slot == null)
            return false;
        int moduleArea = Math.Max(1, module.XSize * module.YSize);
        int slotArea = Math.Max(1, slot.Size.X * slot.Size.Y);
        return moduleArea == slotArea;
    }

    /// <summary>
    /// Census design-lookup: resolve <paramref name="designName"/> to its design's module UIDs —
    /// one entry PER MODULE INSTANCE (so a design with two of a module yields that UID twice).
    /// Returns null when the design doesn't resolve (the census SKIPS that vessel) and is fully
    /// exception-safe. Enumerates the design's slots via GetOrLoadDesignSlots() (the same accessor
    /// the ship designer/spawn path uses), reading each slot's ModuleUID.
    /// </summary>
    static IReadOnlyList<string> DesignModuleUidsForCensus(string designName)
    {
        try
        {
            if (designName.IsEmpty()
                || !ResourceManager.Ships.GetDesign(designName, out IShipDesign design)
                || design == null)
                return null; // design no longer resolves — skip this vessel

            DesignSlot[] slots = (design as Ship_Game.Ships.ShipDesign)?.GetOrLoadDesignSlots();
            if (slots == null || slots.Length == 0)
            {
                // Fallback: distinct module UIDs (no per-instance counts) if slots are unavailable.
                string[] unique = design.UniqueModuleUIDs;
                return unique != null && unique.Length > 0 ? unique : null;
            }

            var uids = new List<string>(slots.Length);
            foreach (DesignSlot s in slots)
                if (s != null && s.ModuleUID.NotEmpty())
                    uids.Add(s.ModuleUID);
            return uids;
        }
        catch
        {
            return null; // any failure resolving/enumerating the design safely skips the vessel
        }
    }

    static IReadOnlyList<string> VesselModuleUidsForCensus(OwnedVessel vessel)
    {
        if (vessel == null)
            return null;
        try
        {
            if (vessel.DesignName.IsEmpty()
                || !ResourceManager.Ships.GetDesign(vessel.DesignName, out IShipDesign design)
                || design is not ShipDesignData shipDesign)
                return null;

            DesignSlot[] slots = shipDesign.GetOrLoadDesignSlots();
            if (slots == null || slots.Length == 0)
                return DesignModuleUidsForCensus(vessel.DesignName);

            var destroyed = new HashSet<int>(
                ArenaCareer.NormalizeDestroyedModules(vessel.DestroyedModules).Select(s => s.SlotIndex));
            var overrides = ArenaCareer.NormalizeModuleOverrides(vessel.ModuleOverrides)
                .ToDictionary(o => o.SlotIndex, o => o.ModuleUid);

            var uids = new List<string>(slots.Length);
            for (int i = 0; i < slots.Length; ++i)
            {
                if (overrides.TryGetValue(i, out string overrideUid) && overrideUid.NotEmpty())
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
            return null;
        }
    }

    /// <summary>Census display-name lookup: a module UID's friendly name via its template
    /// (ResourceManager.GetModuleTemplate -> NameText), falling back to the UID. Exception-safe.</summary>
    static string ModuleDisplayNameForCensus(string uid)
    {
        try
        {
            if (uid.NotEmpty() && ResourceManager.GetModuleTemplate(uid, out ShipModule m) && m != null)
            {
                string name = m.NameText.Text;
                if (name.NotEmpty())
                    return name;
            }
        }
        catch { /* fall through to the UID */ }
        return uid;
    }

    // ---- FLEET COMPOSITION ACCESSORS (the FLEET popup reads/drives THESE) ------------

    /// <summary>The number of owned vessels currently FIELDED in the active fleet (flagship +
    /// fleet ids, clamped to the cap). The FLEET popup shows "N / MAX in fleet".</summary>
    public int FleetSize => ValidFieldedFleetVessels().Length;

    /// <summary>True when the owned vessel with this id is currently FIELDED in the fleet
    /// (the active flagship is always fielded). The FLEET popup marks the toggle state.</summary>
    public bool IsInFleet(string vesselId)
        => TryResolveOwnedVessel(Career?.FindOwnedVessel(vesselId), out _) && (Career?.IsInFleet(vesselId) ?? false);

    /// <summary>
    /// FLEET TOGGLE (the load-bearing "compose the fleet" action): add/remove the owned vessel
    /// with <paramref name="vesselId"/> from the active fleet, enforcing the max-size cap, and
    /// persist the career. The active vessel is the flagship and can never be removed (a toggle
    /// on it is a no-op success). Returns true on a successful add/remove (or the flagship
    /// no-op); false when the id isn't owned or the fleet is already at the cap. PUBLIC so the
    /// FLEET popup and the headless proof drive the REAL composition.
    /// </summary>
    public bool ToggleFleetVessel(string vesselId)
    {
        Career ??= new ArenaCareer();
        if (!TryResolveOwnedVessel(Career.FindOwnedVessel(vesselId), out _))
            return false;
        bool changed = Career.ToggleFleetVessel(vesselId);
        if (changed)
        {
            CareerManager.Save(Career, CareerSavePath);
            Log.Info($"ArenaFightScreen: FLEET toggled vessel id={vesselId} " +
                     $"-> fleet now {Career.FleetSize}/{CurrentMaxFleetSize} " +
                     $"[{string.Join(",", Career.FleetVesselIds ?? Empty<string>.Array)}].");
        }
        return changed;
    }

    // 'Repair All' buy: spend cash, restore surviving hull damage and destroyed module scars.
    void BuyRepair()
        => RepairAllFromHub();

    int CurrentRepairAllCost()
    {
        int baseCost = ArenaPerks.RepairCost(RepairCost, Career?.Perks);
        float damage = CurrentRepairDamageFraction();
        int hullCost = damage <= 0f ? 0 : Math.Max(1, (int)Math.Ceiling(baseCost * damage));
        return hullCost + CurrentDestroyedModuleRestoreCost();
    }

    int CurrentDestroyedModuleRestoreCost()
    {
        int cost = 0;
        foreach (OwnedVessel v in Career?.OwnedVessels ?? Empty<OwnedVessel>.Array)
            cost += DestroyedModuleRestoreCost(v);
        return cost;
    }

    static int CountDestroyedModules(OwnedVessel[] owned)
        => (owned ?? Empty<OwnedVessel>.Array)
           .Sum(v => ArenaCareer.NormalizeDestroyedModules(v?.DestroyedModules).Length);

    static int DestroyedModuleRestoreCost(OwnedVessel vessel)
    {
        if (vessel == null)
            return 0;

        int cost = 0;
        foreach (DestroyedModuleSlot slot in ArenaCareer.NormalizeDestroyedModules(vessel.DestroyedModules))
        {
            string uid = slot.ModuleUid;
            if (uid.IsEmpty() && TryGetDesignSlot(vessel.DesignName, slot.SlotIndex, out DesignSlot baseSlot))
                uid = baseSlot.ModuleUID;
            cost += ModuleRefitCost(uid);
        }
        return cost;
    }

    float CurrentRepairDamageFraction()
    {
        float totalMax = 0f;
        float totalDamage = 0f;
        var liveVessels = new HashSet<string>(StringComparer.Ordinal);

        foreach (KeyValuePair<Ship, OwnedVessel> link in FleetShipVessel)
        {
            Ship s = link.Key;
            OwnedVessel v = link.Value;
            if (s == null || v == null || !s.IsAlive)
                continue;
            if (v.VesselId.NotEmpty())
                liveVessels.Add(v.VesselId);
            float repairableMax = RepairableHullMax(s);
            totalMax += repairableMax;
            totalDamage += Math.Max(0f, repairableMax - s.Health);
        }

        foreach (OwnedVessel v in Career?.OwnedVessels ?? Empty<OwnedVessel>.Array)
        {
            if (v == null || v.VesselId.IsEmpty() || liveVessels.Contains(v.VesselId))
                continue;
            if (v.CurrentHullHealth <= 0f || v.MaxHullHealth <= v.CurrentHullHealth)
                continue;
            totalMax += v.MaxHullHealth;
            totalDamage += v.MaxHullHealth - v.CurrentHullHealth;
        }

        return totalMax > 0f ? (totalDamage / totalMax).Clamped(0f, 1f) : 0f;
    }

    int CountRepairableOwnedVessels()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<Ship, OwnedVessel> link in FleetShipVessel)
        {
            Ship s = link.Key;
            OwnedVessel v = link.Value;
            if (s == null || v == null || !s.IsAlive)
                continue;
            if (s.Health < RepairableHullMax(s) - 0.5f && v.VesselId.NotEmpty())
                ids.Add(v.VesselId);
        }
        foreach (OwnedVessel v in Career?.OwnedVessels ?? Empty<OwnedVessel>.Array)
        {
            if (v == null || v.VesselId.IsEmpty())
                continue;
            if (v.CurrentHullHealth > 0f && v.MaxHullHealth > v.CurrentHullHealth + 0.5f)
                ids.Add(v.VesselId);
            if (ArenaCareer.NormalizeDestroyedModules(v.DestroyedModules).Length > 0)
                ids.Add(v.VesselId);
        }
        return ids.Count;
    }

    public ArenaRepairResult RepairAllFromHub()
    {
        int damaged = CountRepairableOwnedVessels();
        int restoredModules = CountDestroyedModules(Career?.OwnedVessels);
        if (damaged == 0 && restoredModules == 0)
            return new ArenaRepairResult(false, "No damaged surviving ships.", Cash, Cash, 0, 0);

        int cost = CurrentRepairCost;
        if ((Phase != RunPhase.Shopping && Phase != RunPhase.Idle) || Cash < cost)
            return new ArenaRepairResult(false,
                Cash < cost ? $"Need ${cost} to repair." : "Repairs are only available between fights.",
                Cash, Cash, 0, 0);

        int cashBefore = Cash;
        Cash -= cost;
        foreach (Ship s in PlayerShips)
            if (s != null && s.IsAlive) RepairHullOnly(s);
        Career ??= new ArenaCareer();
        foreach (OwnedVessel v in Career.OwnedVessels ?? Empty<OwnedVessel>.Array)
        {
            if (v == null)
                continue;
            v.CurrentHullHealth = 0f;
            v.MaxHullHealth = 0f;
            v.DestroyedModules = Empty<DestroyedModuleSlot>.Array;
            v.ModuleOverrides = Empty<ModuleSlotOverride>.Array;
        }
        Career.Cash = Cash;
        Career.AddChronicleEvent("repair", "fleet",
            $"Repaired {damaged} ship(s) and restored {restoredModules} module(s) for ${cost}.",
            ChronicleSeed($"repair:{damaged}:{restoredModules}:{cost}"));
        CareerManager.Save(Career, CareerSavePath);
        RefreshShop();
        return new ArenaRepairResult(true, $"Repaired {damaged} ship(s), restored {restoredModules} module(s) for ${cost}.",
            cashBefore, Cash, damaged, restoredModules);
    }

    static void RepairHullOnly(Ship ship)
    {
        if (ship == null)
            return;
        foreach (ShipModule module in ship.Modules ?? Empty<ShipModule>.Array)
        {
            if (module == null)
                continue;
            module.SetHealth(module.ActualMaxHealth, "ArenaHullRepair");
        }
        ship.Health = RepairableHullMax(ship);
    }

    static float RepairableHullMax(Ship ship)
    {
        if (ship == null)
            return 0f;
        float max = 0f;
        foreach (ShipModule module in ship.Modules ?? Empty<ShipModule>.Array)
            if (module != null && module.Active)
                max += module.ActualMaxHealth;
        return max > 0f ? max : ship.HealthMax;
    }

    // 'Hire Wingman' buy: spend WingmanCost, queue one extra player warship to be
    // spawned alongside the gladiator at the start of the next round.
    void BuyWingman()
    {
        if (Phase != RunPhase.Shopping || Cash < WingmanCost)
            return;
        Cash -= WingmanCost;
        Career ??= new ArenaCareer();
        Career.Cash = Cash;
        CareerManager.Save(Career, CareerSavePath);
        PendingWingmen += 1;
        RefreshShop();
    }

    // 'Unlock Chassis' buy: spend UnlockChassisCost and grant the next foreign faction's
    // warship hulls to the player. The cash-gate + "a faction remains" guard live here
    // (the existing in-handler pattern). On success the granted hulls appear in the
    // Customize designer automatically (it reads GetUnlockedHulls).
    void BuyUnlockChassis()
    {
        if (Phase != RunPhase.Shopping || Cash < UnlockChassisCost)
            return;
        if (NextChassisFaction(ArenaPlayer) == null) // all foreign chassis already unlocked
            return;
        string granted = GrantNextChassis(ArenaPlayer);
        if (granted == null) // nothing left to grant — don't charge
            return;
        Cash -= UnlockChassisCost;
        Career ??= new ArenaCareer();
        Career.Cash = Cash;
        CareerManager.Save(Career, CareerSavePath);
        Log.Info($"ArenaFightScreen: unlocked foreign chassis '{granted}' " +
                 $"(total unlocked={GrantedChassisFactions.Count}); now buildable in the Customize designer.");
        RefreshShop();
    }

    // 'Next Fight': close the shop, advance the round, respawn a tougher enemy squad
    // (and any newly hired wingmen), and resume the fight. The player ship is KEPT with
    // its current hull damage; shields were already recharged when the shop opened.
    // PUBLIC so the STAGES HUB's NEXT FIGHT button (and the headless proof) can resume the run.
    public void NextFight()
    {
        if (Phase == RunPhase.Idle)
        {
            StartBout();
            return;
        }
        if (Phase != RunPhase.Shopping)
            return;
        if (ShopPanel != null) ShopPanel.Visible = false;

        if (AdvanceRoundOnNextFight)
            Round += 1;
        AdvanceRoundOnNextFight = false;
        ClearPlayerShipsForFreshBout();
        SpawnPlayerShips(firstRound: false);
        QueueDefaultFightOption();
        SpawnEnemyRound(Round);

        Phase = RunPhase.Fighting;
        UState.Paused = false;
        RetargetTimer = 0f;
        EngageAll();

        Log.Info($"ArenaFightScreen: round {Round} begins — enemyCount={EnemyShips.Count} " +
                 $"player={PlayerShips.Count} cash={Cash}");
    }

    void ClearPlayerShipsForFreshBout()
    {
        foreach (Ship s in PlayerShips)
            if (s != null && s.Active)
                s.QueueTotalRemoval();
        PlayerShips.Clear();
        FieldedFleet.Clear();
        FleetShipVessel.Clear();
        FleetShipBaseSlotIndices.Clear();
    }

    // ---- SPAWNING -------------------------------------------------------------------

    // Spawn (or top up) the player side. On the first round this creates the lone
    // gladiator; on later rounds it only spawns NEWLY hired wingmen (existing player
    // ships persist with their accumulated hull damage). Player ships are arranged in a
    // short vertical column on the left of the arena center.
    // The owned vessels FIELDED in the player fleet, captured at spawn time so each spawned
    // player ship knows which vessel's DESIGN to use and which vessel's VETERANCY to carry.
    // FieldedFleet[i] is the i'th fleet SLOT's intended vessel; it drives WHICH design to spawn.
    // Wingmen (the shop 'Hire Wingman' buys) spawn AFTER the fleet, using the flagship design, and
    // have no owned vessel (null veterancy). Rebuilt every SpawnPlayerShips so a garage/fleet
    // change is honored.
    readonly List<OwnedVessel> FieldedFleet = new();

    // STABLE SHIP -> OWNED-VESSEL ASSOCIATION (the veterancy-banking key). Populated at SPAWN time
    // in SpawnPlayerShips: each FLEET ship is linked to the OwnedVessel it was spawned FOR. This is
    // what BankCareer / ReapplyVeterancy key on — NOT a positional index into PlayerShips — so the
    // ship->vessel link is correct even if PlayerShips reorders or a fielded ship DIES and the list
    // is later compacted (a positional FieldedFleet[i] <-> PlayerShips[i] map would silently bank
    // the WRONG ship's — or a wingman's — stats onto a vessel once order shifts). WINGMEN are never
    // in this map (they bank to nobody). Rebuilt every SpawnPlayerShips (existing entries are kept
    // so survivors stay linked) and fully cleared on a gladiator respawn, so it never leaks stale
    // Ship refs. A dead ship's entry is RETAINED so its last-known veterancy still banks to its
    // vessel (roguelike: a vessel KEEPS what it earned this run even if its ship died).
    readonly Dictionary<Ship, OwnedVessel> FleetShipVessel = new();

    // For scarred/refit vessels, the spawned Ship's live ModuleSlotList can omit destroyed
    // design slots. This map translates live module index -> original design slot index so
    // post-fight banking records newly destroyed modules against stable persisted slots.
    readonly Dictionary<Ship, int[]> FleetShipBaseSlotIndices = new();

    static readonly Vector2 PlayerSpawnFacing = new(1f, 0f);
    static readonly Vector2 EnemySpawnFacing = new(-1f, 0f);

    static Ship CreateArenaShipAtPoint(UniverseState state, string designName,
        Empire owner, Vector2 position, Vector2 facing)
    {
        Ship ship = Ship.CreateShipAtPoint(state, designName, owner, position);
        FaceArenaShip(ship, facing);
        ArenaCombatTuning.ApplyAntiKiteDefaults(ship);
        return ship;
    }

    static Ship CreateArenaShipAtPoint(UniverseState state, Ship template,
        Empire owner, Vector2 position, Vector2 facing)
    {
        Ship ship = Ship.CreateShipAtPoint(state, template, owner, position);
        FaceArenaShip(ship, facing);
        ArenaCombatTuning.ApplyAntiKiteDefaults(ship);
        return ship;
    }

    static void FaceArenaShip(Ship ship, Vector2 facing)
    {
        if (ship == null || facing == Vector2.Zero)
            return;
        ship.Rotation = facing.ToRadians();
    }

    void SpawnPlayerShips(bool firstRound)
    {
        Vector2 center = ArenaCenter;

        // FLEET COMPOSITION: the run fields ALL owned vessels in the active fleet (flagship
        // first), then the hired wingmen on top. A FRESH single-vessel career fields exactly the
        // one flagship (FieldedFleetVessels() returns just it), so the unaided run is unchanged —
        // ONE player ship + wingmen, exactly as before. Each fleet vessel spawns by its OWN
        // DesignName (so a multi-design fleet fields its real hulls); wingmen use the flagship
        // design (the existing wingman behavior).
        FieldedFleet.Clear();
        foreach (OwnedVessel v in ValidFieldedFleetVessels())
            FieldedFleet.Add(v);

        // PRUNE stale ship->vessel links: drop any mapped Ship that is no longer in PlayerShips
        // (e.g. fully removed from the universe) so the map can't leak dead Ship refs across rounds.
        // Dead-but-still-listed ships are KEPT (their last-known veterancy still banks to their
        // vessel). Survivors that ARE still in PlayerShips keep their existing vessel link, so we
        // never re-key a survivor to the wrong slot on a later round's top-up.
        if (FleetShipVessel.Count > 0)
        {
            var live = new HashSet<Ship>(PlayerShips);
            var stale = new List<Ship>();
            foreach (Ship mapped in FleetShipVessel.Keys)
                if (!live.Contains(mapped))
                    stale.Add(mapped);
            foreach (Ship dead in stale)
            {
                FleetShipVessel.Remove(dead);
                FleetShipBaseSlotIndices.Remove(dead);
            }
        }

        int fleetCount = FieldedFleet.Count;
        // Field-what-you-own only kicks in when the career actually owns a fleet; a bare run
        // with no owned roster (legacy/edge) still fields the single PlayerDesign gladiator.
        int gladiators = fleetCount > 0 ? fleetCount : 1;
        int desired  = gladiators + PendingWingmen; // fleet + all wingmen hired so far
        int existing = PlayerShips.Count;
        for (int i = existing; i < desired; ++i)
        {
            // Newly spawned ships (the round-1 fleet + any hired wingmen) drop into a
            // formation on the left of the arena center. Existing player ships are LEFT
            // where they are (carrying their hull damage and battlefield position forward) —
            // we don't teleport live ships, which would fight the spatial grid.
            //
            // SPAWN-TIME FLEET LAYOUT: if the hub built an arena Fleet, the i'th ship spawns
            // at its assigned FleetOffset (a real formation), so a built roster comes in as a
            // formation rather than a plain column. We use the offset as a spawn-time layout
            // only (the fleet AI isn't run, so the wing never force-closes to point-blank).
            Vector2 slot = FleetSpawnSlot(i, desired);

            // Each FLEET slot spawns its OWN owned-vessel design (flagship first); slots past
            // the fleet (the wingmen) use the flagship/PlayerDesign. A fleet vessel whose design
            // no longer resolves falls back to the flagship design so the slot still fills.
            // `slotVessel` is the OwnedVessel this slot is fielded FOR (null for a wingman slot).
            string designName = PlayerDesign.Name;
            Ship spawnTemplate = null;
            int[] baseSlotIndices = null;
            OwnedVessel slotVessel = i < fleetCount ? FieldedFleet[i] : null;
            if (TryResolveOwnedVessel(slotVessel, out IShipDesign fd))
            {
                designName = fd.Name;
                spawnTemplate = BuildScarredSpawnTemplate(slotVessel, fd, out baseSlotIndices);
            }
            else
            {
                slotVessel = null;
            }

            Ship s = spawnTemplate != null
                ? CreateArenaShipAtPoint(UState, spawnTemplate, ArenaPlayer, center + slot, PlayerSpawnFacing)
                : CreateArenaShipAtPoint(UState, designName, ArenaPlayer, center + slot, PlayerSpawnFacing);
            if (s != null)
            {
                s.SensorRange = 400000;
                PlayerShips.Add(s);
                // STABLE ASSOCIATION: link this FLEET ship to its OwnedVessel by the ship ref
                // (the banking key). Wingman slots (slotVessel == null) are intentionally NOT
                // mapped — they bank to nobody. This link survives reorder/compaction/death.
                if (slotVessel != null)
                {
                    FleetShipVessel[s] = slotVessel;
                    if (baseSlotIndices != null)
                        FleetShipBaseSlotIndices[s] = baseSlotIndices;
                    ApplyCarriedVesselHullState(s, slotVessel);
                }
            }
        }
        _ = firstRound; // (kept for call-site clarity; spawn logic is the same either way)
    }

    Ship BuildScarredSpawnTemplate(OwnedVessel vessel, IShipDesign design, out int[] baseSlotIndices)
    {
        baseSlotIndices = null;
        if (vessel == null || design is not ShipDesignData baseDesign)
            return null;

        DestroyedModuleSlot[] destroyed = ArenaCareer.NormalizeDestroyedModules(vessel.DestroyedModules);
        ModuleSlotOverride[] overrides = ArenaCareer.NormalizeModuleOverrides(vessel.ModuleOverrides);
        if (destroyed.Length == 0 && overrides.Length == 0)
            return null;

        DesignSlot[] baseSlots = baseDesign.GetOrLoadDesignSlots();
        if (baseSlots == null || baseSlots.Length == 0)
            return null;

        var destroyedSlots = new HashSet<int>(destroyed.Select(s => s.SlotIndex));
        var overrideBySlot = overrides.ToDictionary(o => o.SlotIndex, o => o.ModuleUid);
        var slots = new List<DesignSlot>(baseSlots.Length);
        var map = new List<int>(baseSlots.Length);

        for (int i = 0; i < baseSlots.Length; ++i)
        {
            DesignSlot source = baseSlots[i];
            if (source == null)
                continue;

            bool hasOverride = overrideBySlot.TryGetValue(i, out string moduleUid)
                               && ResourceManager.ModuleExists(moduleUid);
            if (destroyedSlots.Contains(i) && !hasOverride)
                continue;

            var slot = new DesignSlot(source);
            if (hasOverride)
                slot.ModuleUID = moduleUid;
            slots.Add(slot);
            map.Add(i);
        }

        if (slots.Count == 0)
            return null;

        string name = $"ArenaRefit:{vessel.VesselId}:{StableRefitHash(vessel):X8}";
        ShipDesignData clone = baseDesign.GetClone(name);
        clone.SetDesignSlots(slots.ToArray(), updateRole: false);
        baseSlotIndices = map.ToArray();
        return Ship.CreateNewShipTemplate(ArenaPlayer, clone);
    }

    static uint StableRefitHash(OwnedVessel vessel)
    {
        const uint Offset = 2166136261u;
        const uint Prime = 16777619u;
        uint hash = Offset;
        void Add(string text)
        {
            if (text == null) return;
            for (int i = 0; i < text.Length; ++i)
            {
                hash ^= text[i];
                hash *= Prime;
            }
        }
        void AddInt(int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= Prime;
            }
        }

        Add(vessel?.VesselId);
        foreach (DestroyedModuleSlot slot in ArenaCareer.NormalizeDestroyedModules(vessel?.DestroyedModules))
        {
            AddInt(slot.SlotIndex);
            Add(slot.ModuleUid);
        }
        foreach (ModuleSlotOverride o in ArenaCareer.NormalizeModuleOverrides(vessel?.ModuleOverrides))
        {
            AddInt(o.SlotIndex);
            Add(o.ModuleUid);
        }
        return hash;
    }

    static void ApplyCarriedVesselHullState(Ship ship, OwnedVessel vessel)
    {
        if (ship == null || vessel == null || vessel.CurrentHullHealth <= 0f)
            return;
        ship.Health = vessel.CurrentHullHealth.Clamped(1f, ship.HealthMax);
    }

    // The spawn-time layout slot (relative to ArenaCenter) for the i'th player ship of
    // `count`. When the hub built a PlayerFleet AND the i'th ship already has an assigned
    // FleetOffset, that formation offset is used (left-shifted by Gap so the wing stays on
    // the player's side); otherwise it falls back to the plain vertical column. SPAWN-TIME
    // ONLY — no fleet-hold AI runs.
    Vector2 FleetSpawnSlot(int i, int count)
    {
        if (PlayerFleet != null && i < PlayerFleet.Ships.Count)
        {
            Vector2 off = PlayerFleet.Ships[i].FleetOffset;
            if (off != Vector2.Zero || i == 0)
                return new Vector2(-Gap, 0) + off;
        }
        float y = (i - (count - 1) / 2f) * RowSpan;
        return new Vector2(-Gap, y);
    }

    // Spawn a fresh enemy squad for the given round. Squad SIZE escalates via the shared
    // EnemyCountForRound, and CLASS escalates too: the escort class grows round-to-round
    // (EnemyEscortRolesForRound) and the FINAL round fields MiniBossCountForRound mini-boss
    // hulls (a bigger warship that out-classes the player's cruiser default) alongside the
    // escorts. Any leftover enemy refs (all dead) are cleared first so EnemyShips only ever
    // holds THIS round's squad. The boss slots take the OUTER positions so they front the
    // line; escorts fill the rest.
    void SpawnEnemyRound(int round)
    {
        Vector2 center = ArenaCenter;
        EnemyShips.Clear();
        CurrentBossEncounter = ArenaBossEncounter.None;
        ActiveFightOption = null;
        UState.P.GravityWellRange = 0f;

        if (TrySpawnQueuedChallenge(center))
            return;

        if (TrySpawnSelectedFightOption(center))
            return;

        if (TrySpawnBossEncounter(round, center))
            return;

        // PERSISTED DIFFICULTY: a veteran career (CareerLevel > 0) fields a bigger squad each
        // round. A fresh career (level 0) gets exactly EnemyCountForRound — today's default run.
        CurrentFightModifier = ArenaFightModifier.ForRound(CareerLevel, round, FightModifierSeed);
        int count = CurrentFightModifier.EnemyCount(CareerEnemyCountForRound(round, CareerLevel));
        int bossCount = CurrentFightModifier.BossCount(MiniBossCountForRound(round, CareerLevel), count);

        IShipDesign escort = PickEnemyEscort(ArenaEnemy, round, CareerLevel) ?? EnemyDesign;
        IShipDesign boss   = bossCount > 0 ? PickEnemyBoss(ArenaEnemy, CareerLevel) : null;
        if (boss == null) bossCount = 0; // no boss hull on this roster -> all escorts

        for (int i = 0; i < count; ++i)
        {
            float y = (i - (count - 1) / 2f) * RowSpan;
            // First `bossCount` slots are the mini-boss; the rest are escorts.
            IShipDesign design = i < bossCount ? boss : escort;
            Ship e = CreateArenaShipAtPoint(UState, design.Name, ArenaEnemy, center + new Vector2(+Gap, y), EnemySpawnFacing);
            if (e != null)
            {
                e.SensorRange = 400000;
                CurrentFightModifier.ApplyToEnemy(e);
                EnemyShips.Add(e);
            }
        }

        Log.Info($"ArenaFightScreen: round {round} enemy squad — escort='{escort.Name}' " +
                 $"(role {escort.Role}) x{count - bossCount}" +
                 (boss != null ? $" + boss='{boss.Name}' (role {boss.Role}) x{bossCount}" : "") +
                 $" modifier={CurrentFightModifier.Name}");
    }

    bool TrySpawnBossEncounter(int round, Vector2 center)
    {
        CurrentBossEncounter = PickBossEncounter(ArenaEnemy, round, CareerLevel);
        if (!CurrentBossEncounter.Active)
            return false;

        if (!ResourceManager.Ships.GetDesign(CurrentBossEncounter.DesignName, out IShipDesign design))
        {
            CurrentBossEncounter = ArenaBossEncounter.None;
            return false;
        }

        Ship boss = CreateArenaShipAtPoint(UState, design.Name, ArenaEnemy, center + new Vector2(+Gap, 0f), EnemySpawnFacing);
        if (boss == null)
        {
            CurrentBossEncounter = ArenaBossEncounter.None;
            return false;
        }

        boss.SensorRange = 400000;
        boss.BaseStrength *= CurrentBossEncounter.StrengthMultiplier;
        boss.Health = Math.Max(boss.Health, boss.HealthMax * CurrentBossEncounter.HealthMultiplier);
        EnemyShips.Add(boss);
        EnemyDesign = design;
        CurrentFightModifier = ArenaFightModifier.None;
        Log.Info($"ArenaFightScreen: spawned BOSS encounter '{CurrentBossEncounter.Name}' " +
                 $"design='{design.Name}' role={design.Role} strengthX={CurrentBossEncounter.StrengthMultiplier:0.##} " +
                 $"healthX={CurrentBossEncounter.HealthMultiplier:0.##}");
        return true;
    }

    bool TrySpawnSelectedFightOption(Vector2 center)
    {
        FightOption option = PendingFightOption;
        PendingFightOption = null;
        if (option == null)
            return false;
        if (!IsFightOptionLegal(option))
        {
            Log.Warning($"ArenaFightScreen: queued fight option '{option.OptionId}' is no longer legal; falling back to normal spawn.");
            return false;
        }

        if (!ResolveFightOptionDesigns(option, out IShipDesign escort, out IShipDesign boss))
        {
            Log.Warning($"ArenaFightScreen: queued fight option '{option.OptionId}' cannot resolve designs; falling back to normal spawn.");
            return false;
        }

        CurrentFightModifier = option.Modifier;
        CurrentFightModifier.ApplyGravityWell(UState, center);
        CurrentBossEncounter = option.BossEncounter.Active ? option.BossEncounter : ArenaBossEncounter.None;
        int bossCount = Math.Clamp(option.BossCount, 0, option.EnemyCount);

        for (int i = 0; i < option.EnemyCount; ++i)
        {
            float y = (i - (option.EnemyCount - 1) / 2f) * RowSpan;
            IShipDesign design = i < bossCount ? boss : escort;
            Ship e = CreateArenaShipAtPoint(UState, design.Name, ArenaEnemy, center + new Vector2(+Gap, y), EnemySpawnFacing);
            if (e == null)
                continue;
            e.SensorRange = 400000;
            if (CurrentBossEncounter.Active && i < bossCount)
            {
                e.BaseStrength *= CurrentBossEncounter.StrengthMultiplier;
                e.Health = Math.Max(e.Health, e.HealthMax * CurrentBossEncounter.HealthMultiplier);
            }
            else
            {
                option.Modifier.ApplyToEnemy(e);
            }
            EnemyShips.Add(e);
        }

        if (EnemyShips.Count == 0)
        {
            CurrentBossEncounter = ArenaBossEncounter.None;
            CurrentFightModifier = ArenaFightModifier.None;
            return false;
        }

        ActiveFightOption = option;
        EnemyDesign = escort ?? boss;
        Log.Info($"ArenaFightScreen: spawned fight option {option.FightType}/{option.RiskTier} id={option.OptionId} " +
                 $"escort='{escort?.Name}' boss='{boss?.Name}' enemies={EnemyShips.Count} cash=${option.RewardCash}");
        return true;
    }

    bool ResolveFightOptionDesigns(FightOption option, out IShipDesign escort, out IShipDesign boss)
    {
        escort = null;
        boss = null;
        if (option.EscortDesignName.NotEmpty())
            ResourceManager.Ships.GetDesign(option.EscortDesignName, out escort);
        if (option.BossDesignName.NotEmpty())
            ResourceManager.Ships.GetDesign(option.BossDesignName, out boss);
        if (option.HasBoss && boss == null)
            return false;
        if (escort == null)
            escort = boss ?? PickEnemyEscort(ArenaEnemy, round: 1, CareerLevel) ?? EnemyDesign;
        return escort != null;
    }

    bool TrySpawnQueuedChallenge(Vector2 center)
    {
        if (PendingChallengeDesignName.IsEmpty())
            return false;

        string contender = PendingChallengeContenderName;
        string designName = PendingChallengeDesignName;
        PendingChallengeContenderName = null;
        PendingChallengeDesignName = null;

        if (!ResourceManager.Ships.GetDesign(designName, out IShipDesign design)
            || !IsLegalCombatCraft(design)
            || !IsDesignAllowedForCareerLevel(design, CareerLevel))
        {
            Log.Warning($"ArenaFightScreen: queued ladder challenge design '{designName}' is no longer legal; falling back to normal spawn.");
            return false;
        }

        Ship e = CreateArenaShipAtPoint(UState, design.Name, ArenaEnemy, center + new Vector2(+Gap, 0f), EnemySpawnFacing);
        if (e == null)
        {
            Log.Warning($"ArenaFightScreen: failed to spawn queued ladder challenge '{design.Name}'; falling back to normal spawn.");
            return false;
        }

        e.SensorRange = 400000;
        EnemyShips.Add(e);
        EnemyDesign = design;
        CurrentFightModifier = ArenaFightModifier.None;
        CurrentBossEncounter = ArenaBossEncounter.None;
        Log.Info($"ArenaFightScreen: spawned ladder challenge '{contender}' design='{design.Name}' role={design.Role}.");
        return true;
    }

    // Each living player ship targets the nearest living enemy and vice-versa.
    void EngageAll()
    {
        Order(PlayerShips, EnemyShips);
        Order(EnemyShips, PlayerShips);

        static void Order(List<Ship> attackers, List<Ship> targets)
        {
            foreach (Ship a in attackers)
            {
                if (!a.IsAlive) continue;
                Ship nearest = null;
                float best = float.MaxValue;
                foreach (Ship t in targets)
                {
                    if (!t.IsAlive) continue;
                    float d = a.Position.SqDist(t.Position);
                    if (d < best) { best = d; nearest = t; }
                }
                if (nearest != null)
                    a.AI.OrderAttackSpecificTarget(nearest);
            }
        }
    }

    // ---- HUD + SHOP UI --------------------------------------------------------------
    // Mirrors the standard UIElementContainer Label/Panel/Button helpers. The HUD
    // labels are DynamicText so they always show the live Round/Cash; the shop panel is
    // built once and toggled Visible between rounds.
    void BuildHudAndShop()
    {
        // HUD: top-left open-ended bout counter and cash.
        RoundLabel = Add(new UILabel(new Vector2(20, 60), "", Fonts.Arial20Bold)
        {
            DynamicText = _ => $"Bout {Round}  |  Level {CareerLevel}"
        });
        CashLabel = Add(new UILabel(new Vector2(20, 86), "", Fonts.Arial20Bold)
        {
            DynamicText = _ => $"Cash: ${Cash}"
        });
        // Shows which gladiator design the player is currently fighting as (chosen or
        // the deterministic auto-pick), so customizing has visible feedback.
        GladiatorLabel = Add(new UILabel(new Vector2(20, 112), "", Fonts.Arial14Bold)
        {
            DynamicText = _ => $"Gladiator: {(PlayerDesign != null ? PlayerDesign.Name : "—")}"
        });
        // HUD: how many FOREIGN faction chassis the player has unlocked this run (the
        // newly unlocked hulls show up in the Customize designer automatically).
        ChassisLabel = Add(new UILabel(new Vector2(20, 134), "", Fonts.Arial14Bold)
        {
            DynamicText = _ => $"Foreign Chassis: {GrantedChassisFactions.Count}"
        });

        // Always-on HUD button to customize/choose the gladiator. Reachable BEFORE round
        // 1 (and any time during the run); opens the designer/picker. Sits under the HUD
        // labels, top-left.
        CustomizeHudButton = Add(new UIButton(ButtonStyle.Medium,
            new Vector2(20, 160), "Customize Gladiator") { OnClick = _ => OpenGladiatorCustomizer() });

        // SHOP: a simple centered panel with a title, the buys + customize + a continue.
        // Hidden until a round is cleared. Tall enough for all five rows.
        var panelRect = new Rectangle(ScreenWidth / 2 - 170, ScreenHeight / 2 - 170, 340, 350);
        ShopPanel = Add(new UIPanel(panelRect, new Color(8, 12, 22).Alpha(0.92f)));
        ShopPanel.Visible = false;

        ShopTitle = ShopPanel.Add(new UILabel(new Vector2(panelRect.X + 20, panelRect.Y + 14),
            "ROUND CLEARED — SHOP", Fonts.Arial20Bold));

        float bx = panelRect.X + 20;
        float by = panelRect.Y + 54;
        RepairButton = ShopPanel.Add(new UIButton(ButtonStyle.Medium,
            new Vector2(bx, by), $"Repair All (${CurrentRepairCost})") { OnClick = _ => BuyRepair() });
        WingmanButton = ShopPanel.Add(new UIButton(ButtonStyle.Medium,
            new Vector2(bx, by + 52), $"Hire Wingman (${WingmanCost})") { OnClick = _ => BuyWingman() });
        // 'Unlock Chassis': spend UnlockChassisCost to unlock the NEXT foreign faction's
        // warship hulls (they then appear in the Customize designer + spawn fine). Label
        // shows the next faction; disabled/relabelled when all chassis are unlocked.
        UnlockChassisButton = ShopPanel.Add(new UIButton(ButtonStyle.Medium,
            new Vector2(bx, by + 104), $"Unlock Chassis (${UnlockChassisCost})") { OnClick = _ => BuyUnlockChassis() });
        CustomizeButton = ShopPanel.Add(new UIButton(ButtonStyle.Medium,
            new Vector2(bx, by + 156), "Customize Gladiator") { OnClick = _ => OpenGladiatorCustomizer() });
        NextFightButton = ShopPanel.Add(new UIButton(ButtonStyle.Medium,
            new Vector2(bx, by + 208), "Next Fight") { OnClick = _ => NextFight() });
    }

    // ---- CUSTOMIZE GLADIATOR (live layer) -------------------------------------------
    // Open the full ship designer over the paused arena so the player can build/edit a
    // gladiator. When they SAVE a design, ShipDesignScreenInput.SaveShipDesign writes it
    // AND calls ResourceManager.AddShipTemplate, making it LIVE in
    // ResourceManager.Ships.Designs — the same collection we spawn from. When the
    // designer screen exits we adopt the last-saved/last-edited design name as the chosen
    // gladiator and respawn the player ship with it for the next round.
    //
    // EmpireUI (the player's EmpireUIOverlay) is set up by UniverseScreen.LoadContent, so
    // it's available here. If it's somehow missing we fall back to the lighter picker.
    void OpenGladiatorCustomizer() => OpenCustomizerForActiveVessel();

    /// <summary>
    /// BUGFIX (per-vessel customize): open the ship designer for the ACTIVE OWNED VESSEL.
    ///
    /// The old <see cref="OpenGladiatorCustomizer"/> opened a fresh <see cref="ShipDesignScreen"/>
    /// with NO design loaded, so the editor always started on the default hull (the cruiser) and,
    /// on close, set a GLOBAL chosen gladiator — so you could never edit a bought/selected vessel
    /// and edits didn't stick to a specific vessel. This version:
    ///   1. LOADS the active owned vessel's CURRENT design into the editor (ChangeHull) so it opens
    ///      on THAT vessel's ship, not a blank/cruiser default; and
    ///   2. on EXIT maps the resulting design back to the ACTIVE OWNED VESSEL specifically
    ///      (<see cref="ApplyCustomizedDesignToActiveVessel"/>).
    /// If there is NO active owned vessel (fresh pre-career state) it falls back to the legacy
    /// behavior (blank designer + global SetChosenGladiator) so nothing crashes.
    /// The GARAGE CUSTOMIZE button and <see cref="OpenHangar"/> both route here.
    /// </summary>
    public void OpenCustomizerForActiveVessel()
    {
        if (EmpireUI == null)
        {
            Log.Warning("ArenaFightScreen: EmpireUI null; cannot open ship designer for customization.");
            return;
        }

        var designer = new ShipDesignScreen(this, EmpireUI);

        // Load the ACTIVE owned vessel's current design INTO the editor so it opens on THAT
        // vessel's ship (the bug: it used to open on a blank/cruiser default). Guard for a
        // fresh pre-career state with no active owned vessel.
        OwnedVessel active = Career?.ActiveVessel ?? Career?.FirstVessel;
        if (active != null && active.DesignName.NotEmpty()
            && ResourceManager.Ships.GetDesign(active.DesignName, out IShipDesign current))
        {
            TryLoadDesignerOpeningDesign(designer, current);
            Log.Info($"ArenaFightScreen: opening customizer on ACTIVE owned vessel " +
                     $"id={active.VesselId} design='{active.DesignName}'.");
        }

        // When the designer closes, map whatever design the player ended on back to the
        // ACTIVE owned vessel (or, with no active vessel, the legacy global adopt).
        designer.OnExit += () => AdoptDesignerChoice(designer);
        ScreenManager.AddScreen(designer);
    }

    // PRE-MATCH SETUP PHASE — BUILD-ANEW (§1.2): launch the REAL base ShipDesignScreen against the arena
    // universe (identical ctor to OpenCustomizerForActiveVessel above — no fork), but REROUTE the capture seam
    // to CaptureSetupDesign (NOT AdoptDesignerChoice, which writes the career vessel record + CareerManager.Save
    // = 4X/career pollution). The captured in-memory design is canonicalized into the sandbox scratch set under
    // its @arena/<hash> name (playerDesign:false, readOnly:true) — never Saved Designs / the 4X.
    public void OpenArenaSetupDesigner(IShipDesign seed = null)
    {
        if (EmpireUI == null)
        {
            Log.Warning("ArenaFightScreen: EmpireUI null; cannot open the arena setup ship designer.");
            return;
        }
        var designer = new ShipDesignScreen(this, EmpireUI);
        if (seed != null)
            TryLoadDesignerOpeningDesign(designer, seed);
        // REROUTE: capture into the arena scratch set, NOT the career vessel. The base save may still write the
        // player-typed .design into SP Saved Designs (resolution (A) quarantine-in-place, see the report) — that
        // template is a harmless legal stock-namespace design; the arena sim only ever references the @arena/ name.
        designer.OnExit += () => CaptureSetupDesign(designer.CurrentDesign as IShipDesign);
        ScreenManager.AddScreen(designer);
    }

    // PRE-MATCH SETUP PHASE — PLACE-FORMATION (§1.4): launch the REAL base FleetDesignScreen against the arena
    // universe (identical ctor to UniverseScreen.HandleInput.cs:89 — no fork), roster PRE-SCOPED to the
    // budget-affordable scratch designs (Player.ShipsWeCanBuild seeded under the @arena/<hash> wire names by
    // CaptureSetupDesign's UnlockScratchDesignForArenaDesigner). On exit, project the authored Fleet to the
    // canonical bundle via CaptureSetupFormation (ArenaFleetBundle.FromFleet — the SAME projection the base
    // fleet-save uses), captured IN-MEMORY so no .fleet is written to the 4X fleet-designs dir.
    public void OpenArenaSetupFormation()
    {
        if (EmpireUI == null)
        {
            Log.Warning("ArenaFightScreen: EmpireUI null; cannot open the arena setup formation editor.");
            return;
        }
        // Refresh the buildable roster to the current scratch set before constructing the editor (the ctor reads
        // Player.ShipsWeCanBuild). The scratch designs were already unlocked per-design at capture time.
        ArenaPlayer?.UpdateShipsWeCanBuild(new Array<string>());
        var editor = new FleetDesignScreen(this, EmpireUI);
        editor.OnExit += () =>
        {
            if (editor.SelectedFleet != null)
                CaptureSetupFormation(editor.SelectedFleet);
        };
        ScreenManager.AddScreen(editor);
    }

    static bool TryLoadDesignerOpeningDesign(ShipDesignScreen designer, IShipDesign design)
    {
        if (designer == null || design == null || LoadDesignOnOpenMethod == null)
            return false;
        try
        {
            LoadDesignOnOpenMethod.Invoke(designer, new object[] { design });
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"ArenaFightScreen: designer preload unavailable: {e.Message}");
            return false;
        }
    }

    // After the designer closes, read the design the player last loaded/saved/edited and,
    // if it's a legal real warship, map it back to the ACTIVE OWNED VESSEL specifically
    // (updates ONLY that vessel's record + persists), then make it the live gladiator and
    // respawn the player ship with it. With no active owned vessel (fresh pre-career state)
    // we fall back to the legacy GLOBAL adopt so the live ship still updates.
    void AdoptDesignerChoice(ShipDesignScreen designer)
    {
        // The design the player ended on in the editor. SaveShipDesign already made it
        // LIVE in ResourceManager.Ships (writes the .design AND calls AddShipTemplate),
        // so a GetDesign lookup by this name succeeds for a saved design.
        string name = designer.CurrentDesign?.Name;
        if (name.IsEmpty())
            return;

        // Per-vessel mapping: when the career owns an active vessel, the edited design belongs
        // to THAT vessel — update its record + persist. Otherwise keep the legacy global behavior.
        if ((Career?.ActiveVessel ?? Career?.FirstVessel) != null)
            ApplyCustomizedDesignToActiveVessel(name);
        else if (ResourceManager.Ships.GetDesign(name, out IShipDesign design)
                 && IsLegalCombatCraft(design)
                 && IsDesignAllowedForCareerLevel(design, CareerLevel))
            SetChosenGladiator(name);
    }

    /// <summary>
    /// THE BUGFIX SEAM (testable headless): map an edited/saved design name onto the ACTIVE
    /// OWNED VESSEL specifically. Updates ONLY that owned vessel's <see cref="OwnedVessel.DesignName"/>
    /// in the career (NOT the cruiser, NOT a global), persists via <see cref="CareerManager.Save"/>,
    /// and makes it the live gladiator (<see cref="SetChosenGladiator"/> respawns it). No-op
    /// (returns false) when the design name is empty/illegal or there is no active owned vessel.
    /// PUBLIC so the GARAGE customize path and the headless proof both drive the REAL mapping.
    /// </summary>
    public bool ApplyCustomizedDesignToActiveVessel(string designName)
        => ApplyCustomizedDesignToActiveVesselWithInventory(designName).Success;

    public ArenaDesignInventoryResult ApplyCustomizedDesignToActiveVesselWithInventory(string designName)
    {
        if (designName.IsEmpty())
            return new ArenaDesignInventoryResult(false, "No customized design selected.", designName);
        if (!ResourceManager.Ships.GetDesign(designName, out IShipDesign design)
            || !IsLegalCombatCraft(design)
            || !IsDesignAllowedForCareerLevel(design, CareerLevel))
            return new ArenaDesignInventoryResult(false, "Customized design is not legal for this career.", designName);

        OwnedVessel active = Career?.ActiveVessel ?? Career?.FirstVessel;
        if (active == null)
            return new ArenaDesignInventoryResult(false, "No active owned vessel to customize.", designName);

        // Customizer adoption is free blueprinting: if the saved design is legal for the career
        // tier, map it onto THIS vessel. Battle refit remains a separate cash repair path.
        active.DesignName = designName;
        active.CurrentHullHealth = 0f;
        active.MaxHullHealth = 0f;
        active.DestroyedModules = Empty<DestroyedModuleSlot>.Array;
        active.ModuleOverrides = Empty<ModuleSlotOverride>.Array;
        CareerManager.Save(Career, CareerSavePath);
        SetChosenGladiator(designName);

        Log.Info($"ArenaFightScreen: customized ACTIVE owned vessel id={active.VesselId} " +
                 $"-> design='{designName}' (mapped to that vessel + saved + respawned).");
        return new ArenaDesignInventoryResult(true, $"Customized vessel saved as {designName}.", designName);
    }

    // Adopt a chosen gladiator design by name and respawn the player gladiator with it
    // (so the new hull is what fights the next round; hull attrition resets for the new
    // design). Safe to call mid-shop; the new ship engages when Next Fight resumes.
    public void SetChosenGladiator(string designName)
    {
        if (designName.IsEmpty())
            return;
        if (!ResourceManager.Ships.GetDesign(designName, out IShipDesign design)
            || !IsLegalCombatCraft(design)
            || !IsDesignAllowedForCareerLevel(design, CareerLevel))
            return;

        ChosenPlayerDesignName = designName;
        PlayerDesign = design;

        // Respawn the player side with the new design: remove existing player ships and
        // spawn a fresh gladiator (+ any hired wingmen) so the chosen hull is the one
        // that fights next round. Clear the stable ship->vessel map too — every prior link
        // refers to a ship we're about to remove, so re-key from scratch in SpawnPlayerShips
        // (prevents a stale Ship ref from ever banking onto the wrong vessel).
        ClearPlayerShipsForFreshBout();
        SpawnPlayerShips(firstRound: true);

        Log.Info($"ArenaFightScreen: gladiator customized -> '{designName}' (role {design.Role}); respawned player gladiator.");
    }

    // Enable/disable the buy buttons based on current Cash so a buy the player can't
    // afford is ignored. Next Fight is always available so the run is always completable.
    void RefreshShop()
    {
        if (ShopTitle != null)
            ShopTitle.Text = $"ROUND {Round} CLEARED — Cash ${Cash}";
        if (RepairButton != null)
        {
            int cost = CurrentRepairCost;
            RepairButton.Text = $"Repair All (${cost})";
            RepairButton.Enabled = cost > 0 && Cash >= cost;
        }
        if (WingmanButton != null)
            WingmanButton.Enabled = Cash >= WingmanCost;
        // Unlock Chassis: label the NEXT faction (or "All Chassis Unlocked"); enable only
        // when the player can afford it AND a foreign faction remains to unlock.
        if (UnlockChassisButton != null)
        {
            string next = NextChassisFaction(ArenaPlayer);
            if (next == null)
            {
                UnlockChassisButton.Text = "All Chassis Unlocked";
                UnlockChassisButton.Enabled = false;
            }
            else
            {
                UnlockChassisButton.Text = $"Unlock Chassis: {next} (${UnlockChassisCost})";
                UnlockChassisButton.Enabled = Cash >= UnlockChassisCost;
            }
        }
        // NextFightButton stays enabled — buying nothing must still let the run continue.
    }

    // ---- Deterministic gladiator matchup picks. ------------------------------------
    // The RoleName enum is size-ordered (… corvette < frigate < destroyer < cruiser <
    // battleship < capital), so we pick by combat CLASS, not raw cost: the player gets
    // a solid MID warship and the enemy squad gets SMALLER, squishier hulls. That
    // asymmetry (one chunky vs several small) is what lets each round resolve quickly to
    // a player-favored win instead of the old never-dying capital-vs-capital stalemate.
    // We key off Design.Role (the design's actual combat class, what the balance-lab
    // sweeps use) rather than the raw hull template role.

    // Only "real", spawnable warships: has a hull, meaningful offensive strength, and
    // is not a station/platform/carrier-only/support/civilian role. The >100 strength
    // floor is the same warship threshold used across the headless balance lab.
    // PUBLIC so the headless arena tests can use the SAME legality gate the screen uses.
    public static bool IsRealWarship(IShipDesign d)
    {
        if (d == null || d.BaseHull == null) return false;
        if (!d.IsValidDesign) return false;
        if (d.BaseStrength <= 100f) return false;
        if (d.IsPlatformOrStation || d.IsStation || d.IsConstructor || d.IsSubspaceProjector) return false;
        if (d.IsColonyShip || d.IsFreighter || d.IsTroopShip || d.IsSingleTroopShip) return false;
        if (d.IsCarrierOnly || d.IsResearchStation || d.IsMiningStation) return false;
        return true;
    }

    public static bool IsLegalCombatCraft(IShipDesign d)
    {
        if (d == null || d.BaseHull == null) return false;
        if (!d.IsValidDesign) return false;
        if (IsDevTestDesign(d)) return false;
        if (d.BaseStrength <= 0f) return false;
        if (CombatTierForRole(d.Role) == 0) return false;
        if (CombatTierForRole(d.HullRole) == 0) return false;
        if (d.IsPlatformOrStation || d.IsStation || d.IsConstructor || d.IsSubspaceProjector) return false;
        if (d.IsColonyShip || d.IsFreighter || d.IsTroopShip || d.IsSingleTroopShip) return false;
        if (d.IsResearchStation || d.IsMiningStation) return false;
        if (IsCarrierClass(d)) return false;
        if (!HasDirectFireWeapons(d)) return false;
        string designName = d.Name ?? "";
        string hullName = d.BaseHull.HullName ?? "";
        if (designName.IndexOf("shuttle", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (hullName.IndexOf("shuttle", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        return true;
    }

    // Automatic starter/default picks must never draw from the player's global saved-design pool.
    // Saved/custom designs are still legal when explicitly chosen by an existing career, but a new
    // career or wipe replacement should start from stock vanilla/mod content only.
    public static bool IsStockContentDesign(IShipDesign d)
        => d != null
        && !d.IsPlayerDesign
        && !d.IsFromSave
        && !d.IsAnExistingSavedDesign;

    // ---- CARRIER EXCLUSION + DIRECT-FIRE REQUIREMENT (data-agnostic) ----------------
    // The default gladiator must be a HEAVY GUN WARSHIP, never a carrier. Carrier-class
    // is detected from ROLE + MODULE data (NOT design names), so it holds for BOTH
    // vanilla and Combined Arms data sets:
    //   - Role == carrier              : the design's actual combat class is Carrier.
    //   - IsCarrierOnly                : the design is flagged carrier-restricted.
    //   - hangar-dominated armament    : it fields fighter-launch hangars but has no real
    //                                    direct-fire weapon (its "offense" is all fighters).
    // PUBLIC static so the headless proof exercises the SAME classifier the screen uses.
    public static bool IsCarrierClass(IShipDesign d)
    {
        if (d == null) return false;
        if (d.Role == RoleName.carrier) return true;
        if (d.IsCarrierOnly) return true;
        // Hangar-dominated: has fighter-launch bays and NO real direct-fire weapon.
        // (A gun-cruiser that also carries a small fighter bay still has direct-fire
        // weapons, so it is NOT excluded — only carriers whose armament is the fighters.)
        bool hasFighterBays = d.AllFighterHangars != null && d.AllFighterHangars.Length > 0;
        return hasFighterBays && !HasDirectFireWeapons(d);
    }

    // A "real direct-fire weapon" = a ship-targeting damage weapon: it does damage,
    // is not a repair beam, and is not a true point-defense weapon (TruePD weapons
    // cannot target ships). REQUIRED for a heavy-gun gladiator so the default is never a
    // hangar/fighter platform. PUBLIC static so the headless proof uses the SAME gate.
    public static bool HasDirectFireWeapons(IShipDesign d)
    {
        if (d?.Weapons == null) return false;
        foreach (Weapon w in d.Weapons)
        {
            if (w == null) continue;
            if (w.DamageAmount >= 1f && !w.IsRepairBeam && !w.TruePD)
                return true;
        }
        return false;
    }

    // A HEAVY GUN WARSHIP gladiator: a real, spawnable warship that is NOT carrier-class
    // and DOES carry real direct-fire weaponry. This is the gate the default player pick
    // (and the enemy pick) require so neither side ever defaults to a carrier.
    // IsWarshipHullRole(d.Role) is the load-bearing class gate: IsRealWarship only floors on
    // strength + station/civilian/carrier, so a fighter/scout/support design with a laser and
    // >100 strength would otherwise slip through. Restricting to the warship-hull roles
    // (gunboat..capital) excludes fighters/scouts/freighters/platforms/stations from EVERY
    // consumer (the dealership catalog, BuyVessel, the enemy/default picks).
    public static bool IsHeavyGunWarship(IShipDesign d)
        => IsRealWarship(d) && IsWarshipHullRole(d.Role) && !IsCarrierClass(d) && HasDirectFireWeapons(d);

    // Dev/TEST scaffolding designs (Content\ShipDesigns\TEST_*.design) must never be offered
    // for sale. The engine's IsUnitTestShip flag is suppressed while GlobalStats.IsUnitTest is
    // set (so headless tests can still SPAWN TEST_ ships), so we gate the dealership on the
    // design NAME directly — TEST_ designs are excluded in BOTH the live game and headless.
    // PUBLIC static so the headless guard asserts the catalog against the SAME predicate.
    public static bool IsDevTestDesign(IShipDesign d)
        => d == null || d.Name.IsEmpty() || d.Name.StartsWith("TEST_", StringComparison.Ordinal) || d.IsUnitTestShip;

    // Deterministic pick: among legal combat craft (armed, non-carrier, non-junk) whose
    // Role is in `roles` and whose tier is allowed, return the one nearest the chosen cost
    // percentile. Stable tiebreak on Name so the pick is reproducible run-to-run.
    static IShipDesign PickByRole(Empire forEmpire, RoleName[] roles, float costPercentile, int maxTier)
    {
        foreach (RoleName role in roles)
        {
            if (CombatTierForRole(role) > maxTier)
                continue;
            IShipDesign[] pool = ResourceManager.Ships.Designs
                .Filter(d => IsLegalCombatCraft(d)
                             && IsStockContentDesign(d)
                             && d.Role == role
                             && CombatTierForDesign(d) <= maxTier);
            if (pool.Length == 0)
                continue;

            // Sort by empire cost when an empire exists; headless/global callers use
            // BaseStrength to avoid cost paths that require empire tech context.
            Array.Sort(pool, (a, b) =>
            {
                float costA = forEmpire != null ? a.GetCost(forEmpire) : a.BaseStrength;
                float costB = forEmpire != null ? b.GetCost(forEmpire) : b.BaseStrength;
                int c = costA.CompareTo(costB);
                return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
            });
            int idx = (int)Math.Round((pool.Length - 1) * costPercentile);
            idx = Math.Clamp(idx, 0, pool.Length - 1);
            return pool[idx];
        }
        return null;
    }

    // Deterministic STRENGTH-PERCENTILE pick: among legal combat craft whose Role is in
    // `roles` (tried in priority order), sort the class by BaseStrength (stable Name
    // tiebreak) and return the hull at `strengthPercentile`. percentile 1.0 == strongest,
    // 0.5 == the solid median representative of the class, 0.0 == weakest.
    static IShipDesign PickByRoleStrength(Empire forEmpire, RoleName[] roles, float strengthPercentile, int maxTier)
    {
        _ = forEmpire;
        foreach (RoleName role in roles)
        {
            if (CombatTierForRole(role) > maxTier)
                continue;
            IShipDesign[] pool = ResourceManager.Ships.Designs
                .Filter(d => IsLegalCombatCraft(d)
                             && IsStockContentDesign(d)
                             && d.Role == role
                             && CombatTierForDesign(d) <= maxTier);
            if (pool.Length == 0)
                continue;

            // Sort ascending by strength (stable Name tiebreak) and pick at the percentile.
            Array.Sort(pool, (a, b) =>
            {
                int c = a.BaseStrength.CompareTo(b.BaseStrength);
                return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
            });
            int idx = (int)Math.Round((pool.Length - 1) * strengthPercentile);
            idx = Math.Clamp(idx, 0, pool.Length - 1);
            return pool[idx];
        }
        return null;
    }

    // PLAYER: honor a PLAYER-CHOSEN gladiator design if one is set, legal, and within the
    // career tier gate; otherwise use the shared tier-ramped deterministic auto-pick.
    //
    // The chosen-design honoring is the load-bearing "spawn YOUR design" hook: if
    // ChosenPlayerDesignName names a design that EXISTS in ResourceManager.Ships AND
    // passes IsRealWarship (real, spawnable, not a station/civilian), THAT design is the
    // gladiator. A missing or illegal choice safely falls through to the auto-pick, so
    // the deterministic unaided run is preserved.
    IShipDesign PickPlayerWarship(Empire forEmpire)
        => ResolvePlayerGladiator(forEmpire, ChosenPlayerDesignName, CareerLevel);

    // The chosen-name honoring rule, as a pure static so the headless test exercises
    // the REAL gate (not a copy): a non-empty name that EXISTS in ResourceManager.Ships
    // AND passes IsRealWarship resolves to that design; anything else -> null.
    public static IShipDesign ResolveChosenDesignByName(string chosenName)
    {
        if (chosenName.IsEmpty())
            return null;
        if (!ResourceManager.Ships.GetDesign(chosenName, out IShipDesign design))
            return null;
        if (!IsLegalCombatCraft(design))
            return null;
        return design;
    }

    // The COMPLETE player-gladiator pick the live screen uses, as a public static so the
    // headless "spawn YOUR design" proof drives the real selection: if `chosenName` names
    // a legal real warship, THAT design is the gladiator; otherwise fall back to the
    // deterministic auto-pick (a real warship, never null when any warship exists). This
    // is what guarantees "chosen design spawns" AND "illegal/missing falls back".
    public static IShipDesign ResolvePlayerGladiator(Empire forEmpire, string chosenName)
        => ResolvePlayerGladiator(forEmpire, chosenName, careerLevel: 0);

    public static IShipDesign ResolvePlayerGladiator(Empire forEmpire, string chosenName, int careerLevel)
    {
        IShipDesign chosen = ResolveChosenDesignByName(chosenName);
        if (chosen != null && IsDesignAllowedForCareerLevel(chosen, careerLevel))
            return chosen;
        return AutoPickPlayerWarship(forEmpire, careerLevel);
    }

    // The deterministic auto-pick (no player choice) follows the shared career tier ramp:
    // fresh careers get armed light craft, mid careers get frigate/destroyer hulls, and
    // late careers get representative tier-3 line ships. Within the chosen class we take
    // the median-strength hull so the default stays useful without jumping straight to the
    // strongest dreadnought. Static so the instance path and the headless tests share one
    // auto-pick.
    public const float PlayerStrengthPercentile = 0.5f;

    public static IShipDesign AutoPickPlayerWarship(Empire forEmpire)
        => AutoPickPlayerWarship(forEmpire, careerLevel: 0);

    public static IShipDesign AutoPickPlayerWarship(Empire forEmpire, int careerLevel)
    {
        int maxTier = MaxAllowedCombatTierForCareerLevel(careerLevel);
        RoleName[] roles = maxTier switch
        {
            1 => new[] { RoleName.fighter, RoleName.gunboat, RoleName.corvette },
            2 => new[] { RoleName.frigate, RoleName.destroyer, RoleName.corvette, RoleName.gunboat, RoleName.fighter },
            _ => new[] { RoleName.battleship, RoleName.cruiser, RoleName.capital, RoleName.destroyer, RoleName.frigate },
        };
        IShipDesign pick = PickByRoleStrength(forEmpire, roles, PlayerStrengthPercentile, maxTier);
        // Last resort on a sparse roster: cheapest legal combat craft within the tier gate.
        return pick ?? PickFallback(forEmpire, priciest: false, maxTier);
    }

    // ENEMY ESCORT (per round): the small/escort hull for the given round. The role list
    // escalates both by round and by career tier. Within the class we take the cheaper
    // (25th-percentile cost) hull so escorts stay squad-fodder, not bosses.
    public static IShipDesign PickEnemyEscort(Empire forEmpire, int round)
        => PickEnemyEscort(forEmpire, round, careerLevel: 0);

    public static IShipDesign PickEnemyEscort(Empire forEmpire, int round, int careerLevel)
    {
        int maxTier = MaxAllowedCombatTierForCareerLevel(careerLevel);
        IShipDesign pick = PickByRole(forEmpire, EnemyEscortRolesForRound(round, careerLevel),
            costPercentile: 0.25f, maxTier);
        return pick ?? PickFallback(forEmpire, priciest: false, maxTier);
    }

    // ENEMY MINI-BOSS (final round, tier 3+ careers only): a peak legal combat craft from
    // the boss role list. This is the load-bearing "round 3 is genuinely dangerous /
    // losable without investment" lever.
    public static IShipDesign PickEnemyBoss(Empire forEmpire)
        => PickEnemyBoss(forEmpire, careerLevel: 0);

    public static IShipDesign PickEnemyBoss(Empire forEmpire, int careerLevel)
    {
        int maxTier = MaxAllowedCombatTierForCareerLevel(careerLevel);
        if (maxTier < 3)
            return null;
        IShipDesign pick = PickByRoleStrength(forEmpire, MiniBossRoles, BossStrengthPercentile, maxTier);
        return pick ?? PickFallback(forEmpire, priciest: true, maxTier);
    }

    public static bool IsBossEncounterRound(int round, int careerLevel)
    {
        if (round != BossEncounterRound)
            return false;
        if (MaxAllowedCombatTierForCareerLevel(careerLevel) < 3)
            return false;
        int level = Math.Max(0, careerLevel);
        return (level - 7) % BossEncounterCareerLevelPeriod == 0;
    }

    public static ArenaBossEncounter PickBossEncounter(Empire forEmpire, int round, int careerLevel)
    {
        if (!IsBossEncounterRound(round, careerLevel))
            return ArenaBossEncounter.None;

        return PickEliteBossEncounter(forEmpire, careerLevel);
    }

    public static ArenaBossEncounter PickEliteBossEncounter(Empire forEmpire, int careerLevel)
    {
        IShipDesign design = PickBossEncounterDesign(forEmpire, careerLevel);
        if (design == null)
            return ArenaBossEncounter.None;

        return new ArenaBossEncounter(
            active: true,
            name: "Remnant Arena Warden",
            designName: design.Name,
            roleClass: design.Role.ToString().ToUpperInvariant(),
            baseStrength: design.BaseStrength,
            strengthMultiplier: BossEncounterStrengthMultiplier,
            healthMultiplier: BossEncounterHealthMultiplier);
    }

    static IShipDesign PickBossEncounterDesign(Empire forEmpire, int careerLevel)
    {
        _ = forEmpire;
        if (MaxAllowedCombatTierForCareerLevel(careerLevel) < 3)
            return null;

        IShipDesign pick = PickByRoleStrength(forEmpire,
            new[] { RoleName.cruiser, RoleName.battleship, RoleName.capital },
            BossEncounterStrengthPercentile, maxTier: 3);
        return pick ?? PickFallback(forEmpire, priciest: true, maxTier: 3);
    }

    // Fallback when no class-tagged hulls exist: cheapest / priciest legal combat craft
    // inside the career tier gate. Never returns a carrier, weaponless design, or junk.
    static IShipDesign PickFallback(Empire forEmpire, bool priciest, int maxTier)
    {
        IShipDesign best = null;
        float bestCost = priciest ? -1f : float.MaxValue;
        foreach (IShipDesign d in ResourceManager.Ships.Designs)
        {
            if (!IsLegalCombatCraft(d)) continue;
            if (!IsStockContentDesign(d)) continue;
            if (CombatTierForDesign(d) > maxTier) continue;
            float cost = forEmpire != null ? d.GetCost(forEmpire) : d.BaseStrength;
            bool better;
            if (best == null)
                better = true;
            else if (priciest)
                better = cost > bestCost
                    || (cost == bestCost && d.BaseStrength > best.BaseStrength);
            else
                better = cost < bestCost
                    || (cost == bestCost && d.BaseStrength < best.BaseStrength);
            if (better) { bestCost = cost; best = d; }
        }
        return best;
    }

    static bool PlayerFilter(IEmpireData d, string playerPreference)
    {
        if (playerPreference.NotEmpty())
            return d.ArchetypeName.Contains(playerPreference) || d.Name.Contains(playerPreference);
        return true;
    }

    static Vector2 GenerateRandomSysPos(UniverseState us, RandomBase random)
    {
        Vector2 sysPos = Vector2.Zero;
        for (int i = 0; i < 20; ++i)
        {
            sysPos = random.Vector2D(us.Size - 100_000);
            if (us.FindSolarSystemAt(sysPos, hitRadius: 100_000) == null)
                return sysPos;
        }
        return sysPos;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using SDLockstep;
using SDGraphics;
using SDUtils.Deterministic;
using Ship_Game.Determinism;
using Ship_Game.Determinism.Lockstep;
using Ship_Game.AI;
using Ship_Game.Fleets;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
using Ship_Game.UI;
using Vector2 = SDGraphics.Vector2;
using FleetDesignT = global::Ship_Game.FleetDesign;

namespace Ship_Game.GameScreens.Arena;

public readonly struct ArenaMultiplayerShipSnapshot
{
    public readonly int PlayerEmpireId;
    public readonly int EnemyEmpireId;
    public readonly int[] PlayerShipIds;
    public readonly int[] EnemyShipIds;
    public readonly string[] PlayerFleetDesigns;
    public readonly string[] EnemyFleetDesigns;
    public readonly string PlayerDesign;
    public readonly string EnemyDesign;

    public ArenaMultiplayerShipSnapshot(int playerEmpireId, int enemyEmpireId,
        int[] playerShipIds, int[] enemyShipIds, string[] playerFleetDesigns,
        string[] enemyFleetDesigns, string playerDesign, string enemyDesign)
    {
        PlayerEmpireId = playerEmpireId;
        EnemyEmpireId = enemyEmpireId;
        PlayerShipIds = playerShipIds ?? Array.Empty<int>();
        EnemyShipIds = enemyShipIds ?? Array.Empty<int>();
        PlayerFleetDesigns = playerFleetDesigns ?? Array.Empty<string>();
        EnemyFleetDesigns = enemyFleetDesigns ?? Array.Empty<string>();
        PlayerDesign = playerDesign ?? "";
        EnemyDesign = enemyDesign ?? "";
    }
}

public readonly struct ArenaMultiplayerMatchStatus
{
    public readonly int PlayerAlive;
    public readonly int EnemyAlive;
    public readonly bool Ended;
    public readonly int WinnerPeerId;

    public ArenaMultiplayerMatchStatus(int playerAlive, int enemyAlive)
    {
        PlayerAlive = playerAlive;
        EnemyAlive = enemyAlive;
        Ended = playerAlive == 0 || enemyAlive == 0;
        WinnerPeerId = !Ended ? 0
            : playerAlive > 0 && enemyAlive == 0 ? ArenaMultiplayerSession.HostPlayerPeerId
            : enemyAlive > 0 && playerAlive == 0 ? ArenaMultiplayerSession.JoinPlayerPeerId
            : 0;
    }
}

public sealed class ArenaMultiplayerLiveSession : IDisposable
{
    public readonly ArenaMultiplayerRole Role;
    public readonly TcpLockstepTransport Transport;
    public readonly ArenaMultiplayerSettings Settings;

    public ArenaMultiplayerLiveSession(ArenaMultiplayerRole role, TcpLockstepTransport transport,
        ArenaMultiplayerSettings settings)
    {
        Role = role;
        Transport = transport;
        Settings = (settings ?? new ArenaMultiplayerSettings()).WithResolvedFleets();
    }

    public void Dispose() => Transport?.Dispose();
}

public sealed partial class ArenaFightScreen
{
    string[] MultiplayerHostFleetDesigns = Array.Empty<string>();
    string[] MultiplayerJoinFleetDesigns = Array.Empty<string>();
    bool MultiplayerPvPMode;
    ArenaMultiplayerLiveSession MultiplayerLiveSession;
    LockstepHost MultiplayerLiveHost;
    LockstepClient MultiplayerLiveClient;
    UniverseStateLockstepSimulation MultiplayerLiveSim;
    ArenaMultiplayerRunResult MultiplayerLiveResult;
    ArenaMultiplayerTelemetry MultiplayerTelemetry;
    readonly Dictionary<uint, HashSet<int>> MultiplayerSubmittedInputs = new();
    uint MultiplayerLiveTurn;
    float MultiplayerLiveAccumulator;
    long MultiplayerRemoteChecksumTick = -1;
    // Live arm/ack gate (advisor plan A.3 rule 1): the host must not commit ANY command frame
    // until the join peer has registered its lockstep receiver and said so, because the transport
    // drops messages delivered while no receiver is registered and CommitTick never retransmits.
    bool MultiplayerRemoteArmed;   // host: join's live armed ack observed
    bool MultiplayerHostSeen;      // join: any host message observed since arming
    float MultiplayerHandshakeResendTimer;
    // No-progress watchdog (advisor plan A.3 rule 4): a live match must surface a visible halt
    // instead of silently idling when the lockstep barrier never clears.
    float MultiplayerNoProgressSeconds;
    long MultiplayerLastProgressTurn = -1;
    long MultiplayerLastProgressSimTick = -1;
    long MultiplayerLastSubmitTurn = -1;
    // Engagement liveness (ruling 2): frame-pump liveness alone is not enough — within a bounded
    // sim-tick window after spawn at least one engagement signal (target acquired, ship in combat,
    // weapon fire attempted) must occur, else halt with ARENA_LIVENESS_FAIL instead of idling.
    bool MultiplayerEngagementSeen;
    const float MultiplayerHandshakeResendInterval = 0.5f;
    const float MultiplayerStallWarnSeconds = 5f;
    public const float MultiplayerStallHaltSeconds = 30f;
    public const uint MultiplayerEngagementWindowTicks = 300; // 5 sim-seconds after ENGAGE (rebased)

    // ArenaMatchPhase (plan Part 3): Spawn -> Countdown -> Engage -> Fight -> Resolve, driven by SIM
    // TICKS (never wall-clock), evaluated inside the already-lockstepped tick advance so both peers
    // fire EngageAll on the SAME tick. The countdown is a tick threshold: EngageAll fires exactly at
    // spawnTick + CountdownTicks. Default 180 ticks = CountdownSeconds(3) * 60.
    public enum ArenaMatchPhase { Spawn, Countdown, Engage, Fight, Resolve }
    ArenaMatchPhase MultiplayerPhase = ArenaMatchPhase.Spawn;

    // PRE-MATCH SETUP PHASE (STARDRIVE_ARENA_SETUP_PHASE_EXEC_PLAN_20260706 §2). SEPARATE from the sim-tick
    // ArenaMatchPhase above (which stays deterministic and lockstep-driven). This machine is PRE-lockstep:
    // it runs on the reused ArenaFightScreen instance after ArmMultiplayerLive but BEFORE the sim spawns, and
    // is UI/handshake-driven (wall-clock is fine — nothing has entered the sim yet). Its terminal state (Fight)
    // gates InitializeMultiplayerLiveIfNeeded so the fight cannot spawn until authoring + exchange complete.
    //
    // DEFAULT = Fight: with the custom-fleet flag OFF (or any legacy launch that never enters setup), the
    // machine is already terminal, so InitializeMultiplayerLiveIfNeeded runs exactly as today — a TRUE no-op
    // for the current duel. Only EnterMultiplayerSetupPhase (custom-fleet path) rewinds it to Setup.
    public enum ArenaSetupPhase { Setup, LocalReady, AwaitingPeers, Exchange, Countdown, Fight }
    ArenaSetupPhase MultiplayerSetupPhase = ArenaSetupPhase.Fight;
    public ArenaSetupPhase MultiplayerSetupPhaseForHeadless => MultiplayerSetupPhase;
    // The per-screen sandbox scratch set authored IN the setup phase (build-anew + import), mirroring the
    // lobby's SandboxDisplayToWire/SandboxRegisteredNames. Torn down on every exit alongside the live match set.
    readonly List<IShipDesign> SetupScratchDesigns = new();
    readonly Dictionary<string, string> SetupDisplayToWire = new(StringComparer.Ordinal);
    IReadOnlyList<string> SetupRegisteredNames = Array.Empty<string>();
    string SetupLocalFleetBundle = "";
    string SetupHudError = "";
    public string SetupHudErrorForHeadless => SetupHudError;
    public IReadOnlyList<string> SetupScratchWireNamesForHeadless
        => SetupScratchDesigns.Select(d => ArenaDesignTable.ContentName(d)).ToArray();

    // §2.3 SETUP -> FIGHT AUTHORITATIVE-START REBUILD. When both peers reach setup-Ready, the HOST rebuilds the
    // authoritative SessionStartMessage from the SETUP scratch tables/bundles (NOT the lobby-time ones), broadcasts
    // it over the CURRENT (fight/setup) transport, both peers validate + register the setup tables, then advance
    // to Fight and spawn the setup-authored fleets. This REUSES the proven Phase A exchange machinery
    // (SessionLobbyMessage carries the setup DesignTable+Fleet; SessionStartMessage is the authoritative rebuild),
    // just sourced from the setup scratch set and run at the setup->fight transition rather than lobby LOCK.
    bool SetupLocalReadySent;                              // this peer published its setup-Ready lobby message
    bool SetupRemoteReady;                                 // host: the join peer's setup-Ready lobby message arrived
    string SetupRemoteDesignTable = "";                   // host: the join peer's setup design table (from the wire)
    string SetupRemoteFleetBundle = "";                   // host: the join peer's setup fleet bundle (from the wire)
    bool SetupStartBroadcast;                              // host: the rebuilt authoritative start was broadcast
    bool SetupStartReceived;                               // join: the rebuilt authoritative start arrived + validated
    float SetupHandshakeResendTimer;                      // periodic re-send of setup-Ready / rebuilt-start
    const float SetupHandshakeResendInterval = 0.5f;
    public bool SetupLocalReadySentForHeadless => SetupLocalReadySent;
    public bool SetupRemoteReadyForHeadless => SetupRemoteReady;
    public bool SetupStartBroadcastForHeadless => SetupStartBroadcast;
    public bool SetupStartReceivedForHeadless => SetupStartReceived;
    public const uint DefaultCountdownTicks = 180;
    uint MultiplayerCountdownTicks = DefaultCountdownTicks;
    long MultiplayerEngageAtTick = -1;          // set once at first tick: spawnTick + CountdownTicks
    string MultiplayerEndReason = "";
    public ArenaMatchPhase MultiplayerPhaseForHeadless => MultiplayerPhase;
    public long MultiplayerEngageAtTickForHeadless => MultiplayerEngageAtTick;
    public string MultiplayerEndReasonForHeadless => MultiplayerEndReason;
    bool MultiplayerLivePaused;
    float MultiplayerLiveSpeed = 1f;
    string MultiplayerLiveStatus = "";
    bool MultiplayerLiveInitialized;
    bool MultiplayerLiveComplete;
    UIPanel MultiplayerEndPanel;
    // Arena custom-fleet exchange kernel: the EXACT set of transient @arena/<hash> designs registered for the
    // CURRENT live match, so teardown undoes precisely this set on EVERY exit path — match end, lobby exit,
    // disconnect, and rematch re-registration (amendment 4). Never blanket-delete @arena/* (a concurrent
    // side-orchestrator match may share the process-global ResourceManager.Ships namespace).
    IReadOnlyList<string> MultiplayerRegisteredDesigns = Array.Empty<string>();

    public bool MultiplayerLiveActive => MultiplayerLiveSession != null;
    // Phase A proof seam: the settings this peer was armed with (reconstructed from the RECEIVED start message),
    // so a test can assert the join-side design table survived the full wire round-trip and was accepted here.
    public ArenaMultiplayerSettings MultiplayerLiveSettingsForHeadless => MultiplayerLiveSession?.Settings;
    public bool MultiplayerLiveDisplayPaused => MultiplayerLiveActive && MultiplayerLivePaused;
    public string MultiplayerLiveStatusText => MultiplayerLiveStatus ?? "";
    public bool MultiplayerEndPanelVisibleForHeadless => MultiplayerEndPanel?.Visible == true;
    public ArenaMultiplayerRunResult MultiplayerLiveResultForHeadless => MultiplayerLiveResult;
    public long MultiplayerLiveSimTickForHeadless => MultiplayerLiveSim != null ? MultiplayerLiveSim.Tick : -1;
    public bool MultiplayerEngagementSeenForHeadless => MultiplayerEngagementSeen;

    public bool HasPendingMultiplayerPvPSetup => MultiplayerPvPMode;

    // Headless proof seam (mirrors ArenaMultiplayerLobbyScreen.LaunchScreenOverrideForHeadless):
    // captures the rematch fight screen instead of swapping the shared ScreenManager stack, which
    // would tear down the other in-process peer in a two-peer headless test.
    public Action<GameScreen> MultiplayerGoToScreenOverrideForHeadless;

    public override bool HandleInput(InputState input)
    {
        if (!MultiplayerLiveActive)
            return base.HandleInput(input);

        // In network lockstep, local world input must not mutate UniverseScreen state directly.
        // Only screen UI controls are allowed here; gameplay commands enter through SimCommand frames.
        Input = input;
        return HandleScreenInputOnly(input);
    }

    public ArenaMultiplayerShipSnapshot MultiplayerSnapshot()
        => new(
            ArenaPlayer?.Id ?? 1,
            ArenaEnemy?.Id ?? 2,
            PlayerShips.Where(s => s?.Active == true).Select(s => s.Id).OrderBy(id => id).ToArray(),
            EnemyShips.Where(s => s?.Active == true).Select(s => s.Id).OrderBy(id => id).ToArray(),
            MultiplayerHostFleetDesigns,
            MultiplayerJoinFleetDesigns,
            PlayerDesign?.Name ?? "",
            EnemyDesign?.Name ?? "");

    public void ConfigureMultiplayerPvP(ArenaMultiplayerSettings settings)
    {
        ArenaMultiplayerSettings resolved = (settings ?? new ArenaMultiplayerSettings()).WithResolvedFleets();
        MultiplayerHostFleetDesigns = ArenaMultiplayerSettings.NormalizeFleet(resolved.HostFleetDesignNames);
        MultiplayerJoinFleetDesigns = ArenaMultiplayerSettings.NormalizeFleet(resolved.JoinFleetDesignNames);
        MultiplayerPvPMode = MultiplayerHostFleetDesigns.Length > 0 && MultiplayerJoinFleetDesigns.Length > 0;
    }

    // Register the current match's custom-design tables (idempotent for rematch), tearing down any set left
    // over from a previous match first so re-registration is clean and nothing accumulates across rematches.
    void RegisterMultiplayerCustomDesigns(ArenaMultiplayerSettings settings)
    {
        TeardownMultiplayerCustomDesigns();
        IReadOnlyList<string> registered = ArenaMultiplayerSession.RegisterPeerDesignTables(settings, out string tableError);
        if (tableError.NotEmpty())
        {
            ArenaMultiplayerSession.UnregisterPeerDesignTables(registered);
            throw new InvalidOperationException(tableError);
        }
        MultiplayerRegisteredDesigns = registered;
    }

    // Tear down exactly the current match's transient @arena/ designs. Safe to call repeatedly / when empty.
    void TeardownMultiplayerCustomDesigns()
    {
        if (MultiplayerRegisteredDesigns.Count > 0)
            ArenaMultiplayerSession.UnregisterPeerDesignTables(MultiplayerRegisteredDesigns);
        MultiplayerRegisteredDesigns = Array.Empty<string>();
        // The setup-phase scratch set shares the exit paths with the live-match set (§1.5 one teardown, both lists).
        TeardownSetupScratchDesigns();
    }

    // ===================================================================================================
    // PRE-MATCH SETUP PHASE (§2) — the state machine + the BUILD-ANEW / IMPORT / PLACE-FORMATION capture seams.
    // All flag-gated behind EnableArenaCustomFleet; a flag-off launch never enters setup (stays terminal Fight).
    // ===================================================================================================

    // Enter the setup phase (custom-fleet path only). Called from the launch flow AFTER ArmMultiplayerLive but
    // before the sim spawns; rewinds the machine to Setup so InitializeMultiplayerLiveIfNeeded early-returns until
    // authoring + exchange complete. A true no-op (and never entered) when the flag is off.
    public void EnterMultiplayerSetupPhase()
    {
        if (!GlobalStats.Defaults.EnableArenaCustomFleet)
            return;
        MultiplayerSetupPhase = ArenaSetupPhase.Setup;
        SetupHudError = "";
    }

    public bool ArenaSetupActiveForHeadless => MultiplayerSetupPhase != ArenaSetupPhase.Fight;

    // The SHARED capture entry point for BUILD-ANEW (§1.2) and IMPORT (§4): both converge here so an imported
    // design is byte-indistinguishable from an authored one. Validates + canonicalizes the in-memory design via
    // ArenaDesignTable.ContentName/RegisterTransient (playerDesign:false, readOnly:true) into the sandbox scratch
    // set — NEVER AdoptDesignerChoice/CareerManager.Save (that is the 4X/career pollution we must avoid). Returns
    // the @arena/<hash> wire name on success, or "" with SetupHudError set on rejection (carrier/mod-gap/null).
    public string CaptureSetupDesign(IShipDesign design)
    {
        SetupHudError = "";
        if (!GlobalStats.Defaults.EnableArenaCustomFleet)
        {
            SetupHudError = "Custom fleet authoring is disabled.";
            return "";
        }
        if (design == null)
        {
            SetupHudError = "No design to capture.";
            return "";
        }
        // Reject null/illegal/carrier at the SAME gate the handshake uses, surfaced to the setup HUD.
        string err = ArenaDesignTable.ValidateContentAvailable(design);
        if (err.NotEmpty())
        {
            SetupHudError = err;
            return "";
        }

        string wire = ArenaDesignTable.ContentName(design);
        // Already captured (identical content) — idempotent, no duplicate registration.
        if (SetupDisplayToWire.Values.Contains(wire, StringComparer.Ordinal))
            return wire;

        // Clone + rename to the @arena/<hash> content name before registering (RegisterTransient only accepts
        // @arena/-prefixed designs — same canonicalization the JOIN side does when reconstructing from bytes).
        var scratch = ((Ship_Game.Ships.ShipDesign)design).GetClone(wire);
        scratch.Name = wire;
        IReadOnlyList<string> justRegistered = ArenaDesignTable.RegisterTransient(new[] { scratch });
        if (justRegistered.Count == 0 || !ResourceManager.Ships.GetDesign(wire, out IShipDesign registered))
        {
            SetupHudError = "Failed to register the captured design.";
            return "";
        }

        SetupDisplayToWire[design.Name ?? wire] = wire;
        SetupScratchDesigns.Add(registered);
        // Track for precise teardown (mirrors MultiplayerRegisteredDesigns).
        var names = new List<string>(SetupRegisteredNames) { wire };
        SetupRegisteredNames = names;
        // Make the scratch design buildable by ArenaPlayer so the formation editor's roster can offer it (§1.4).
        UnlockScratchDesignForArenaDesigner(registered);
        return wire;
    }

    // IMPORT (§4): load a saved SP .design from BYTES and feed it through the SAME CaptureSetupDesign seam, so a
    // build-anew and an import of the same ship produce an IDENTICAL @arena/<hash> transient. Reuses the base
    // ShipDesign.FromBytes codec the kernel round-trips. Returns the wire name or "" with SetupHudError set.
    public string ImportSetupDesignFromBytes(byte[] designBytes)
    {
        SetupHudError = "";
        Ship_Game.Ships.ShipDesign imported;
        try { imported = Ship_Game.Ships.ShipDesign.FromBytes(designBytes); }
        catch (Exception e) { SetupHudError = $"Import failed: {e.Message}"; return ""; }
        if (imported == null)
        {
            SetupHudError = "Import failed: the design could not be reconstructed.";
            return "";
        }
        return CaptureSetupDesign(imported);
    }

    // IMPORT (§4) convenience: import an already-loaded design by name (ResourceManager.Ships.GetDesign) — a stock
    // design or one already in the templates table — through the same capture seam.
    public string ImportSetupDesignByName(string designName)
    {
        SetupHudError = "";
        if (!ResourceManager.Ships.GetDesign(designName, out IShipDesign design))
        {
            SetupHudError = $"Import failed: design '{designName}' not found.";
            return "";
        }
        return CaptureSetupDesign(design);
    }

    // PLACE-FORMATION capture (§1.4 CaptureSetupFormation core): project an authored Fleet to the canonical
    // ArenaFleetBundle via the SAME FromFleet projection SaveFleetDesignScreen.DoSave uses, so setup and the base
    // fleet-save produce byte-identical bundles. The bundle's node ship-names ARE the @arena/<hash> wire names
    // (the roster was seeded under those names), so the existing SpawnMultiplayerFormation path works unchanged.
    public string CaptureSetupFormation(Fleet fleet)
    {
        SetupHudError = "";
        if (fleet == null)
        {
            SetupHudError = "No formation to capture.";
            return "";
        }
        FleetDesignT bundle = ArenaFleetBundle.FromFleet(fleet);
        SetupLocalFleetBundle = ArenaFleetBundle.Encode(bundle);
        return SetupLocalFleetBundle;
    }

    // Directly capture a pre-built canonical bundle (headless PLACE-FORMATION seam — the CAPTURE is what we prove;
    // the FleetDesignScreen GUI itself is not unit-tested, per the plan's Phase D note).
    public void SetSetupFleetBundleForHeadless(string encodedBundle) => SetupLocalFleetBundle = encodedBundle ?? "";
    public string SetupLocalFleetBundleForHeadless => SetupLocalFleetBundle;
    public string CaptureSetupDesignForHeadless(IShipDesign design) => CaptureSetupDesign(design);
    public string ImportSetupDesignByNameForHeadless(string name) => ImportSetupDesignByName(name);
    public string ImportSetupDesignFromBytesForHeadless(byte[] bytes) => ImportSetupDesignFromBytes(bytes);
    public IReadOnlyList<string> AffordableScratchWireNamesForHeadless(int budgetCredits)
        => AffordableScratchWireNames(budgetCredits);

    // Roster scoping (§1.4): make a captured scratch design buildable by ArenaPlayer so the formation editor's
    // roster (Player.ShipsWeCanBuild) can offer it — the exact trio at ArenaFightScreen.cs:1923-1927, but under
    // the @arena/<hash> wire name. Roster scope is UX only; the handshake stays the budget/content enforcement.
    void UnlockScratchDesignForArenaDesigner(IShipDesign design)
    {
        if (ArenaPlayer == null || design?.BaseHull == null)
            return;
        ArenaPlayer.UnlockEmpireHull(design.BaseHull.HullName);
        UnlockDesignModulesForArenaDesigner(ArenaPlayer, design);
        ArenaPlayer.UpdateShipsWeCanBuild(new SDUtils.Array<string> { design.Name });
    }

    // The affordable subset of the scratch set under a budget cap (BaseStrength currency, mirroring SumBundleCost).
    // Used to scope the formation roster to designs the player can actually field (§1.4).
    IReadOnlyList<string> AffordableScratchWireNames(int budgetCredits)
    {
        var affordable = new List<string>();
        foreach (IShipDesign d in SetupScratchDesigns)
        {
            int cost = (int)MathF.Round(d.BaseStrength);
            if (budgetCredits <= 0 || cost <= budgetCredits)
                affordable.Add(ArenaDesignTable.ContentName(d));
        }
        return affordable;
    }

    // The local design TABLE for the setup scratch set (the full canonical payloads the far peer reconstructs).
    // Mirrors ArenaMultiplayerLobbyScreen.BuildLocalDesignTable. "" when nothing custom is fielded.
    public string BuildSetupLocalDesignTable()
        => SetupScratchDesigns.Count > 0 ? ArenaDesignTable.Encode(SetupScratchDesigns) : "";

    // SETUP-READY (§2.2): the local peer finished authoring. In v0 this advances the LOCAL machine; the per-peer
    // exchange is carried by the same lobby/lockstep transport already proven in Phase A. Headless proofs drive
    // AdvanceSetupPhaseToFight to complete the handshake deterministically.
    public void MarkSetupLocalReady()
    {
        if (MultiplayerSetupPhase == ArenaSetupPhase.Setup)
            MultiplayerSetupPhase = ArenaSetupPhase.LocalReady;
    }

    // Advance the setup machine to its terminal Fight state (both peers ready + exchange validated). Once here,
    // InitializeMultiplayerLiveIfNeeded is free to spawn using the SAME reused screen + already-registered designs.
    public void AdvanceSetupPhaseToFight()
    {
        MultiplayerSetupPhase = ArenaSetupPhase.Fight;
    }

    // Tear down exactly the setup-phase scratch @arena/ designs. Wired into TeardownMultiplayerCustomDesigns so it
    // runs on EVERY exit path (match end, lobby exit, disconnect, rematch, ExitScreen catch-all). Never a blanket
    // @arena/* delete (a concurrent match shares the process-global namespace).
    void TeardownSetupScratchDesigns()
    {
        if (SetupRegisteredNames.Count > 0)
            ArenaDesignTable.UnregisterTransient(SetupRegisteredNames);
        SetupRegisteredNames = Array.Empty<string>();
        SetupScratchDesigns.Clear();
        SetupDisplayToWire.Clear();
        SetupLocalFleetBundle = "";
    }

    // ===================================================================================================
    // IN-ARENA SETUP HUD (custom-fleet UI wiring). A lean on-screen control set shown ONLY while the pre-match
    // SETUP phase is active (ArenaSetupPhase.Setup). Every button routes to an ALREADY-PROVEN endpoint — the real
    // base editors (OpenArenaSetupDesigner / OpenArenaSetupFormation), the import flow, and MarkSetupLocalReady.
    // The FLEET PAGE (the real FleetDesignScreen) is the primary UI per the director; the others are entry points
    // to it. Built once in LoadContent (BuildArenaSetupHud); visibility/labels refreshed per frame. Flag-off / a
    // legacy launch never enters setup, so these controls stay hidden — a true no-op.
    // ===================================================================================================
    UIButton SetupDesignButton;
    UIButton SetupImportButton;
    UIButton SetupFormationButton;
    UIButton SetupReadyButton;
    UILabel SetupTitleLabel;
    UILabel SetupBudgetLabel;
    UILabel SetupStatusLabel;

    void BuildArenaSetupHud()
    {
        // Centered-top control cluster, sitting clear of the top-left bout/cash HUD. Built unconditionally but
        // hidden until the setup phase is active (RefreshArenaSetupHud toggles Visible).
        float cx = ScreenWidth * 0.5f - 190f;
        float y = 70f;
        SetupTitleLabel = Add(new UILabel(new Vector2(cx, y), "STAR GLADIATOR — PRE-MATCH SETUP", Fonts.Arial20Bold));
        SetupBudgetLabel = Add(new UILabel(new Vector2(cx, y + 28f), "", Fonts.Arial14Bold)
        {
            DynamicText = _ => SetupBudgetReadout()
        });
        SetupStatusLabel = Add(new UILabel(new Vector2(cx, y + 50f), "", Fonts.Arial12Bold)
        {
            DynamicText = _ => SetupHudError.NotEmpty() ? SetupHudError : SetupReadyStatusReadout()
        });

        float bx = cx;
        float by = y + 76f;
        // [Design Ship] -> the REAL base ShipDesignScreen against the arena universe (build a custom anew).
        SetupDesignButton = Add(new UIButton(ButtonStyle.Medium, new Vector2(bx, by), "Design Ship")
        { OnClick = _ => OpenArenaSetupDesigner() });
        // [Import Design] -> a design-load list (reused ArenaFleetPickerScreen) feeding ImportSetupDesignByName.
        SetupImportButton = Add(new UIButton(ButtonStyle.Medium, new Vector2(bx + 190f, by), "Import Design")
        { OnClick = _ => OpenArenaSetupImportPicker() });
        // [Fleet / Formation] -> THE fleet page (real FleetDesignScreen), the PRIMARY setup UI per the director.
        SetupFormationButton = Add(new UIButton(ButtonStyle.Medium, new Vector2(bx, by + 44f), "Fleet / Formation")
        { OnClick = _ => OpenArenaSetupFormation() });
        // [Ready] -> MarkSetupLocalReady; when both peers Ready, the already-built rebuild+broadcast advances to Fight.
        SetupReadyButton = Add(new UIButton(ButtonStyle.Medium, new Vector2(bx + 190f, by + 44f), "Ready")
        { OnClick = _ => OnArenaSetupReadyClicked() });

        RefreshArenaSetupHud();
    }

    // Show/hide the setup HUD to track the setup phase (called per frame from UpdateMultiplayerSetup, and once at
    // build). Visible only while authoring (ArenaSetupPhase.Setup); once Ready is pressed the button set locks so a
    // second press can't republish mid-exchange. A no-op when the controls were never built (headless).
    void RefreshArenaSetupHud()
    {
        bool inSetup = MultiplayerLiveActive && MultiplayerSetupPhase != ArenaSetupPhase.Fight;
        bool authoring = MultiplayerLiveActive && MultiplayerSetupPhase == ArenaSetupPhase.Setup;
        if (SetupTitleLabel != null) SetupTitleLabel.Visible = inSetup;
        if (SetupBudgetLabel != null) SetupBudgetLabel.Visible = inSetup;
        if (SetupStatusLabel != null) SetupStatusLabel.Visible = inSetup;
        // Authoring buttons enabled only in Setup; after Ready they stay visible (context) but disabled.
        if (SetupDesignButton != null) { SetupDesignButton.Visible = inSetup; SetupDesignButton.Enabled = authoring; }
        if (SetupImportButton != null) { SetupImportButton.Visible = inSetup; SetupImportButton.Enabled = authoring; }
        if (SetupFormationButton != null) { SetupFormationButton.Visible = inSetup; SetupFormationButton.Enabled = authoring; }
        if (SetupReadyButton != null) { SetupReadyButton.Visible = inSetup; SetupReadyButton.Enabled = authoring; }
    }

    // Budget readout: the authored fleet's total BaseStrength cost vs the host budget cap (SumBundleCost is the SAME
    // currency the handshake enforces). Unlimited when no cap is set.
    string SetupBudgetReadout()
    {
        ArenaMultiplayerRuleset ruleset = MultiplayerLiveSession?.Settings?.Ruleset;
        int cost = ArenaMultiplayerSettings.SumBundleCost(SetupLocalFleetBundle, Array.Empty<string>());
        if (ruleset == null || ruleset.BudgetModel != ArenaBudgetModel.Cap || ruleset.BudgetCredits <= 0)
            return $"Fleet cost {cost}  |  Budget UNLIMITED  |  Designs: {SetupScratchDesigns.Count}";
        return $"Fleet cost {cost} / {ruleset.BudgetCredits}  |  Designs: {SetupScratchDesigns.Count}";
    }

    string SetupReadyStatusReadout()
    {
        switch (MultiplayerSetupPhase)
        {
            case ArenaSetupPhase.Setup:      return "Design or import ships, arrange your fleet, then press Ready.";
            case ArenaSetupPhase.LocalReady: return SetupRemoteReady
                ? "Both ready — building the match..."
                : "You are READY. Waiting for the other gladiator...";
            default:                          return "Starting the fight...";
        }
    }

    // [Import Design] flow: a design-load list (reused ArenaFleetPickerScreen — the SAME modal the lobby fleet
    // picker uses) over the legal arena combat designs; each picked name is imported into the @arena/<hash> scratch
    // set via the proven ImportSetupDesignByName seam (so an import is byte-indistinguishable from a build-anew).
    void OpenArenaSetupImportPicker()
    {
        if (MultiplayerSetupPhase != ArenaSetupPhase.Setup)
            return;
        string[] options;
        try
        {
            options = ResourceManager.Ships.Designs
                .Where(d => d?.Name != null && !d.Name.StartsWith("@arena/", StringComparison.Ordinal))
                .Select(d => d.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
        }
        catch { options = Array.Empty<string>(); }
        if (options.Length == 0)
        {
            SetupHudError = "No designs available to import.";
            return;
        }
        ArenaMultiplayerRuleset ruleset = MultiplayerLiveSession?.Settings?.Ruleset;
        int budget = ruleset != null && ruleset.BudgetModel == ArenaBudgetModel.Cap ? ruleset.BudgetCredits : 0;
        ScreenManager.AddScreen(new ArenaFleetPickerScreen(this, options, Array.Empty<string>(), budget,
            picked =>
            {
                foreach (string name in picked ?? Array.Empty<string>())
                    ImportSetupDesignByName(name);
            }));
    }

    // [Ready] click: finalize local authoring. Publishing + the setup->fight rebuild are driven by the existing
    // per-frame UpdateMultiplayerSetup; this just flips the local machine Setup -> LocalReady.
    void OnArenaSetupReadyClicked()
    {
        if (MultiplayerSetupPhase != ArenaSetupPhase.Setup)
            return;
        MarkSetupLocalReady();
        RefreshArenaSetupHud();
    }

    // ---------------------------------------------------------------------------------------------------
    // §2.3 SETUP -> FIGHT AUTHORITATIVE-START REBUILD + RE-BROADCAST over the CURRENT (fight/setup) transport.
    // Reuses the Phase A exchange: each peer publishes its setup DesignTable+Fleet via SessionLobbyMessage; the
    // HOST rebuilds the authoritative SessionStartMessage from the SETUP scratch set and broadcasts it; both
    // peers validate + register the setup tables before spawn (a divergent/overspent setup fleet rejects at
    // ValidateStartMessage, never mid-match — content-hash-as-name already covers it). Then AdvanceSetupPhaseToFight
    // -> InitializeMultiplayerLiveIfNeeded spawns the SETUP-authored fleets.
    // ---------------------------------------------------------------------------------------------------

    // Per-frame setup-phase driver (called from ArenaFightScreen.Update while the setup phase is active). Polls the
    // transport, publishes this peer's setup-Ready, and — once both peers are ready — the host rebuilds+broadcasts
    // the authoritative start and both peers advance to Fight. No-op when the phase is terminal Fight.
    public void UpdateMultiplayerSetup(float dt)
    {
        if (MultiplayerLiveSession == null || MultiplayerSetupPhase == ArenaSetupPhase.Fight)
        {
            RefreshArenaSetupHud(); // hides the setup controls once the phase advances to Fight (or was never in setup)
            return;
        }

        // Keep the on-screen setup controls (buttons/labels) in sync with the phase every frame.
        RefreshArenaSetupHud();

        MultiplayerLiveSession.Transport.Poll();

        SetupHandshakeResendTimer -= Math.Max(0f, dt);
        bool resend = SetupHandshakeResendTimer <= 0f;
        if (resend)
            SetupHandshakeResendTimer = SetupHandshakeResendInterval;

        // Publish this peer's setup-Ready (its scratch DesignTable + fleet bundle) once local authoring is marked
        // Ready. Re-sent periodically until the exchange completes (a copy may be consumed before the far peer's
        // observer registers — same idiom as the arm handshake).
        if (MultiplayerSetupPhase == ArenaSetupPhase.LocalReady && resend)
            PublishSetupReady();

        // Host: once the remote peer's setup-Ready has arrived AND we are locally Ready, rebuild+broadcast the
        // authoritative start from the SETUP scratch set, then advance to Fight.
        if (MultiplayerLiveSession.Role == ArenaMultiplayerRole.Host
            && MultiplayerSetupPhase == ArenaSetupPhase.LocalReady
            && SetupRemoteReady && !SetupStartBroadcast)
        {
            TryRebuildAndBroadcastSetupStart();
        }
    }

    // Publish this peer's setup design table + fleet bundle over the live transport, as a SessionLobbyMessage
    // (the SAME message Phase A proved carries DesignTable). Host addresses the join peer id; join addresses host.
    void PublishSetupReady()
    {
        int fromPeer = MultiplayerLiveSession.Role == ArenaMultiplayerRole.Host
            ? ArenaMultiplayerSession.HostPlayerPeerId
            : ArenaMultiplayerSession.JoinPlayerPeerId;
        int toPeer = MultiplayerLiveSession.Role == ArenaMultiplayerRole.Host
            ? ArenaMultiplayerSession.JoinPlayerPeerId
            : LockstepHost.HostPeerId;
        MultiplayerLiveSession.Transport.Send(toPeer, new SessionLobbyMessage
        {
            FromPeer = fromPeer,
            PeerId = fromPeer,
            Ready = true,
            DesignTable = BuildSetupLocalDesignTable(),
            Fleet = SetupLocalFleetBundle, // the authored formation bundle rides the Fleet field for the setup exchange
            BuildHash = ArenaMultiplayerPeerSignature.EnvironmentHash(),
            BuildSummary = ArenaMultiplayerPeerSignature.EnvironmentSummary(),
        });
        SetupLocalReadySent = true;
    }

    // The setup-exchange message observer (registered in ArmMultiplayerLive while the setup phase is active).
    // Host handles the join's setup-Ready SessionLobbyMessage; join handles the host's rebuilt SessionStartMessage.
    void OnMultiplayerSetupMessage(LockstepMessage message)
    {
        if (MultiplayerLiveSession == null || MultiplayerSetupPhase == ArenaSetupPhase.Fight)
            return;

        // HOST: the join peer published its setup design table + fleet bundle.
        if (MultiplayerLiveSession.Role == ArenaMultiplayerRole.Host
            && message is SessionLobbyMessage lobby
            && lobby.PeerId == ArenaMultiplayerSession.JoinPlayerPeerId && lobby.Ready)
        {
            SetupRemoteDesignTable = lobby.DesignTable ?? "";
            SetupRemoteFleetBundle = lobby.Fleet ?? "";
            if (!SetupRemoteReady)
                MultiplayerTelemetry?.Event("SETUP_REMOTE_READY", $"peer={lobby.PeerId}");
            SetupRemoteReady = true;
        }

        // JOIN: the host broadcast the rebuilt authoritative start (built from the setup scratch set).
        if (MultiplayerLiveSession.Role == ArenaMultiplayerRole.Join
            && message is SessionStartMessage start && !SetupStartReceived)
        {
            ApplyRebuiltSetupStartOnJoin(start);
        }
    }

    // HOST: rebuild the authoritative SessionStartMessage from the SETUP scratch tables/bundles (NOT the lobby-time
    // ones), broadcast it to the join peer, register the setup tables locally, and advance to Fight.
    void TryRebuildAndBroadcastSetupStart()
    {
        ArenaMultiplayerSettings rebuilt = BuildSetupAuthoritativeSettings();
        // Register the setup tables locally BEFORE spawn (idempotent, content-hash dedup) so the @arena/<hash>
        // names resolve; re-registering replaces the lobby-time set with the setup set.
        RegisterMultiplayerCustomDesigns(rebuilt);
        string err = ArenaMultiplayerSettings.ValidateStartMessage(rebuilt.ToStartMessage(), out _);
        if (err.NotEmpty())
        {
            // A divergent/overspent setup fleet rejects HERE (never mid-match). Surface + halt cleanly.
            SetupHudError = err;
            MultiplayerTelemetry?.Event("SETUP_START_REJECT", err);
            MultiplayerLiveSession.Transport.Send(ArenaMultiplayerSession.JoinPlayerPeerId,
                new SessionErrorMessage { FromPeer = ArenaMultiplayerSession.HostPlayerPeerId, Error = err });
            return;
        }

        // Rewire the live session settings to the rebuilt (setup-authored) settings, then broadcast the start.
        RebindMultiplayerLiveSettings(rebuilt);
        MultiplayerLiveSession.Transport.Send(ArenaMultiplayerSession.JoinPlayerPeerId,
            rebuilt.ToStartMessage(ArenaMultiplayerSession.HostPlayerPeerId));
        SetupStartBroadcast = true;
        MultiplayerTelemetry?.Event("SETUP_START_BROADCAST",
            $"hostTableLen={rebuilt.HostDesignTable?.Length ?? 0} joinTableLen={rebuilt.JoinDesignTable?.Length ?? 0}");
        AdvanceSetupPhaseToFight();
        SpawnAfterSetup();
    }

    // JOIN: validate + register the host's rebuilt authoritative start, rebind the live settings to it, and advance
    // to Fight. A mismatch rejects at ValidateStartMessage -> the match aborts cleanly (never a mid-match desync).
    void ApplyRebuiltSetupStartOnJoin(SessionStartMessage start)
    {
        // Register the peer design tables carried in the rebuilt start BEFORE validation (bidirectional; the join's
        // own customs + the host's customs both reconstruct from the received bytes — real reconstruction, not the
        // shared-static shortcut). RegisterMultiplayerCustomDesigns re-registers (replaces the lobby-time set).
        ArenaMultiplayerSettings received = ArenaMultiplayerSettings.FromStartMessage(start).WithResolvedFleets();
        RegisterMultiplayerCustomDesigns(received);
        string err = ArenaMultiplayerSettings.ValidateStartMessage(start, out ArenaMultiplayerSettings validated);
        if (err.NotEmpty())
        {
            SetupHudError = err;
            MultiplayerTelemetry?.Event("SETUP_START_VALIDATE_REJECT", err);
            MultiplayerLiveSession.Transport.Send(LockstepHost.HostPeerId,
                new SessionErrorMessage { FromPeer = ArenaMultiplayerSession.JoinPlayerPeerId, Error = err });
            return;
        }
        RebindMultiplayerLiveSettings(validated);
        SetupStartReceived = true;
        MultiplayerTelemetry?.Event("SETUP_START_ACCEPTED",
            $"hostTableLen={validated.HostDesignTable?.Length ?? 0} joinTableLen={validated.JoinDesignTable?.Length ?? 0}");
        AdvanceSetupPhaseToFight();
        SpawnAfterSetup();
    }

    // After the setup phase reaches terminal Fight, run the SAME spawn chain LoadContent would have run (it was
    // gated out while setup was active). Idempotent: StartMultiplayerPvPMatch / InitializeMultiplayerLiveIfNeeded
    // guard against re-entry, and the setup gate in InitializeMultiplayerLiveIfNeeded is now open (phase == Fight).
    void SpawnAfterSetup()
    {
        if (MultiplayerSetupPhase != ArenaSetupPhase.Fight)
            return;
        StartMultiplayerPvPMatch();
        InitializeMultiplayerLiveIfNeeded();
    }

    // Build the authoritative settings from the SETUP scratch set (§2.3). Host side: HostDesignTable = this peer's
    // setup table, JoinDesignTable = the remote peer's setup table (arrived over the wire); host/join fleet bundles
    // = the authored setup bundles. Mirrors the lobby's BuildArenaSettings but sourced from the setup scratch set.
    ArenaMultiplayerSettings BuildSetupAuthoritativeSettings()
    {
        ArenaMultiplayerSettings baseSettings = MultiplayerLiveSession.Settings;
        string localTable = BuildSetupLocalDesignTable();
        string localBundle = SetupLocalFleetBundle.NotEmpty()
            ? SetupLocalFleetBundle
            : baseSettings.HostFleetBundle ?? "";
        string remoteTable = SetupRemoteDesignTable ?? "";
        string remoteBundle = SetupRemoteFleetBundle.NotEmpty()
            ? SetupRemoteFleetBundle
            : baseSettings.JoinFleetBundle ?? "";

        // Derive the fleet NAME lists from the bundles so the names match the @arena/<hash> designs the tables carry.
        string[] hostNames = BundleShipNames(ResolveMultiplayerBundle(localBundle, baseSettings.HostFleetDesignNames));
        string[] joinNames = BundleShipNames(ResolveMultiplayerBundle(remoteBundle, baseSettings.JoinFleetDesignNames));

        return new ArenaMultiplayerSettings
        {
            MatchSeed = baseSettings.MatchSeed,
            RngSeed = baseSettings.RngSeed,
            InputDelay = baseSettings.InputDelay,
            MaxTurns = baseSettings.MaxTurns,
            CommandEveryTurns = baseSettings.CommandEveryTurns,
            GameSpeed = baseSettings.GameSpeed,
            StartPaused = baseSettings.StartPaused,
            HostRacePreference = baseSettings.HostRacePreference,
            JoinRacePreference = baseSettings.JoinRacePreference,
            PlayerPreference = baseSettings.PlayerPreference,
            HostLoadoutTrait = baseSettings.HostLoadoutTrait,
            JoinLoadoutTrait = baseSettings.JoinLoadoutTrait,
            HostFleetDesignNames = hostNames.Length > 0 ? hostNames : baseSettings.HostFleetDesignNames,
            JoinFleetDesignNames = joinNames.Length > 0 ? joinNames : baseSettings.JoinFleetDesignNames,
            Ruleset = (baseSettings.Ruleset ?? new ArenaMultiplayerRuleset()).Clone(),
            HostFleetBundle = localBundle,
            JoinFleetBundle = remoteBundle,
            HostDesignTable = localTable,
            JoinDesignTable = remoteTable,
        }.WithResolvedFleets();
    }

    // Replace the live session's settings with the rebuilt (setup-authored) settings so InitializeMultiplayerLive /
    // StartMultiplayerPvPMatch spawn the SETUP fleets. MultiplayerLiveSession.Settings is readonly, so swap the
    // session object (same role + transport). ConfigureMultiplayerPvP re-derives the spawn name lists.
    void RebindMultiplayerLiveSettings(ArenaMultiplayerSettings settings)
    {
        ArenaMultiplayerLiveSession old = MultiplayerLiveSession;
        MultiplayerLiveSession = new ArenaMultiplayerLiveSession(old.Role, old.Transport, settings);
        MultiplayerLiveSession.Transport.RoutingAlarm = old.Transport.RoutingAlarm;
        ConfigureMultiplayerPvP(settings);
    }

    public void ArmMultiplayerLive(ArenaMultiplayerLiveSession session)
    {
        MultiplayerLiveSession = session ?? throw new ArgumentNullException(nameof(session));
        ArenaMultiplayerSettings settings = session.Settings.WithResolvedFleets();
        // Register this match's custom designs before spawn so the @arena/<hash> names resolve (amendment 6);
        // teardown is wired into BackToMultiplayerLobby / StartMultiplayerRematch / disconnect (amendment 4).
        RegisterMultiplayerCustomDesigns(settings);
        MultiplayerTelemetry?.Dispose();
        MultiplayerTelemetry = ArenaMultiplayerTelemetry.Start(session.Role.ToString(), "live-arena", settings);
        // A silent send-to-nowhere must never again be silent: surface transport routing
        // failures (unroutable destination peer, throwing observer) in telemetry and the log.
        session.Transport.RoutingAlarm = warning =>
        {
            MultiplayerTelemetry?.Event("TRANSPORT_ROUTING_ALARM", warning);
            Log.Warning($"Arena MP transport routing alarm role={session.Role}: {warning}");
        };
        ConfigureMultiplayerPvP(settings);
        CreateSimThread = false;
        ArenaEngineCapabilities.TrySetParallelUpdate(UState.Objects, false);
        ArenaEngineCapabilities.TryEnableSeededRng(UState, settings.RngSeed);
        MultiplayerLivePaused = settings.StartPaused;
        MultiplayerLiveSpeed = ArenaMultiplayerSettings.ClampGameSpeed(settings.GameSpeed);
        MultiplayerLiveStatus = $"{session.Role.ToString().ToUpperInvariant()} armed";
        MultiplayerTelemetry.Event("ARMED", $"paused={MultiplayerLivePaused} speed={MultiplayerLiveSpeed:0.###}");

        // §2.3: if the pre-match SETUP phase is active, register the setup-exchange observer NOW (the transport
        // is live and peer-routed by LaunchVisibleArena). It coexists with the Fight observer added later at
        // InitializeMultiplayerLiveIfNeeded (AddObserver appends per peer). A no-op when the phase is terminal.
        if (MultiplayerSetupPhase != ArenaSetupPhase.Fight)
        {
            int observePeer = session.Role == ArenaMultiplayerRole.Host
                ? LockstepHost.HostPeerId
                : ArenaMultiplayerSession.JoinPlayerPeerId;
            session.Transport.AddObserver(observePeer, OnMultiplayerSetupMessage);
        }
    }

    public void InitializeMultiplayerLiveIfNeeded()
    {
        if (MultiplayerLiveSession == null || MultiplayerLiveInitialized)
            return;
        // Setup-phase gate (§2.3): the sim must not spawn until authoring + the design-table exchange are done.
        // Default MultiplayerSetupPhase=Fight makes this a no-op for the legacy/flag-off duel (spawns as today);
        // when the custom-fleet setup phase is active it early-returns until AdvanceSetupPhase reaches Fight.
        if (MultiplayerSetupPhase != ArenaSetupPhase.Fight)
            return;

        ArenaMultiplayerSettings settings = MultiplayerLiveSession.Settings.WithResolvedFleets();
        PrepareForMultiplayerLockstep(settings.RngSeed);
        MultiplayerLiveSim = CreateMultiplayerLockstepSimulation();
        MultiplayerLiveResult = new ArenaMultiplayerRunResult
        {
            HostSnapshot = MultiplayerSnapshot(),
            JoinSnapshot = MultiplayerSnapshot(),
        };

        if (MultiplayerLiveSession.Role == ArenaMultiplayerRole.Host)
        {
            MultiplayerLiveHost = new LockstepHost(MultiplayerLiveSession.Transport);
            MultiplayerLiveClient = new LockstepClient(MultiplayerLiveSession.Transport,
                ArenaMultiplayerSession.HostPlayerPeerId, MultiplayerLiveSim);
            MultiplayerLiveHost.AddClient(ArenaMultiplayerSession.HostPlayerPeerId);
            MultiplayerLiveHost.AddClient(ArenaMultiplayerSession.JoinPlayerPeerId);
            MultiplayerLiveSession.Transport.AddObserver(LockstepHost.HostPeerId, OnMultiplayerHostMessage);
            SendMultiplayerControl();
        }
        else
        {
            MultiplayerLiveClient = new LockstepClient(MultiplayerLiveSession.Transport,
                ArenaMultiplayerSession.JoinPlayerPeerId, MultiplayerLiveSim);
            MultiplayerLiveSession.Transport.AddObserver(ArenaMultiplayerSession.JoinPlayerPeerId, OnMultiplayerJoinMessage);
            // Receiver is registered NOW, so it is safe for the host to start committing frames.
            // Re-sent periodically until the host is heard from (the first ack can be consumed by
            // a transport poll that happens before the host screen registers its observer).
            SendMultiplayerLiveArmedAck();
        }

        BuildMultiplayerLiveHud();
        MultiplayerLiveStatus = $"{MultiplayerLiveSession.Role.ToString().ToUpperInvariant()} match started";
        MultiplayerLiveInitialized = true;
        UState.Paused = MultiplayerLivePaused;
        MultiplayerTelemetry.Event("MATCH_STARTED",
            $"snapshotPlayerShips={MultiplayerLiveResult.HostSnapshot.PlayerShipIds.Length} "
            + $"snapshotEnemyShips={MultiplayerLiveResult.HostSnapshot.EnemyShipIds.Length}");
    }

    public void StartMultiplayerPvPMatch()
    {
        if (!MultiplayerPvPMode)
            return;

        // LAYER-2 TRIPWIRE: the MP spawn path (SpawnMultiplayerFleet) deliberately does NOT call
        // ReapplyVeterancy/ApplyPilotTraits, so MP ships are Level 0 with no pilot traits — that is
        // the deterministic default both peers agree on. Pilot traits must NOT enter an MP match
        // until the pilot loadout is serialized into the fleet manifest AND folded into the match
        // fingerprint (PilotTraitV0.CatalogHash + per-slot level/trait ids), or the peers desync
        // Level-0-vs-veteran. Fail loud if someone flips the MP flag before that Layer-2 wiring lands.
        if (GlobalStats.Defaults.EnableArenaPilotTraitsInMultiplayer)
            throw new InvalidOperationException(
                "EnableArenaPilotTraitsInMultiplayer is set, but MP pilot-trait fingerprinting "
                + "(Layer 2) is not implemented. Enabling pilot traits in MP without hashing the "
                + "pilot loadout into the match fingerprint would desync the peers. Keep it false "
                + "until Layer 2 lands.");

        Round = 1;
        AdvanceRoundOnNextFight = false;
        Phase = RunPhase.Fighting;
        UState.Paused = false;
        CurrentFightModifier = ArenaFightModifier.None;
        CurrentBossEncounter = ArenaBossEncounter.None;
        ActiveFightOption = null;
        PendingFightOption = null;
        FieldedFleet.Clear();
        FleetShipVessel.Clear();
        FleetShipBaseSlotIndices.Clear();
        RemoveMultiplayerShips(PlayerShips);
        RemoveMultiplayerShips(EnemyShips);

        // Formation-aware spawn (plan Part 3d): the canonical FleetDesignT bundle is the source of
        // truth for placement AND stable ship-id order. Fall back to the name-list column when no
        // bundle is present (legacy path) — FromDesignNames yields a zero-offset column.
        ArenaMultiplayerSettings liveSettings = MultiplayerLiveSession?.Settings;
        FleetDesignT hostBundle = ResolveMultiplayerBundle(liveSettings?.HostFleetBundle, MultiplayerHostFleetDesigns);
        FleetDesignT joinBundle = ResolveMultiplayerBundle(liveSettings?.JoinFleetBundle, MultiplayerJoinFleetDesigns);
        IShipDesign[] hostDesigns = ResolveMultiplayerFleet(BundleShipNames(hostBundle));
        IShipDesign[] joinDesigns = ResolveMultiplayerFleet(BundleShipNames(joinBundle));
        if (hostDesigns.Length == 0 || joinDesigns.Length == 0)
            throw new InvalidOperationException("Arena PvP lockstep requires at least one legal design on each side.");

        PlayerDesign = hostDesigns[0];
        EnemyDesign = joinDesigns[0];
        // sideMirror mirrors the join side across the arena centerline so both fleets face each
        // other; BOTH peers apply the SAME rule so ship i lands at the same absolute position.
        SpawnMultiplayerFormation(PlayerShips, ArenaPlayer, hostBundle, -Gap, +1f, PlayerSpawnFacing);
        SpawnMultiplayerFormation(EnemyShips, ArenaEnemy, joinBundle, +Gap, -1f, EnemySpawnFacing);

        // Deterministic countdown: resolve tick length from the ruleset once (never per-frame).
        MultiplayerCountdownTicks = (liveSettings?.Ruleset ?? new ArenaMultiplayerRuleset()).CountdownTicks;
        MultiplayerPhase = ArenaMatchPhase.Spawn;
        MultiplayerEngageAtTick = -1;
        // Freeze both fleets during the countdown. ShipAI autonomously acquires sensor targets, so
        // a combat stance would start the fight immediately; CombatState.None ("take no action in
        // combat") holds fire deterministically on both peers until the Engage tick restores the
        // stance. Do NOT call EngageAll() here — the phase machine issues it at MultiplayerEngageAtTick.
        FreezeMultiplayerFleet(PlayerShips);
        FreezeMultiplayerFleet(EnemyShips);
        RetargetTimer = 0f;
        RunStarted = PlayerShips.Count > 0 && EnemyShips.Count > 0;
        if (!RunStarted)
            throw new InvalidOperationException("Arena PvP lockstep failed to spawn both fleets.");
        StabilizeMultiplayerArenaViewAndVisibility();
        MultiplayerTelemetry?.Event("PVP_SPAWNED",
            $"hostDesigns=[{string.Join(",", hostDesigns.Select(d => d.Name))}] "
            + $"joinDesigns=[{string.Join(",", joinDesigns.Select(d => d.Name))}] "
            + $"playerShips={PlayerShips.Count} enemyShips={EnemyShips.Count}");
    }

    public void PrepareForMultiplayerLockstep(uint rngSeed)
    {
        CreateSimThread = false;
        UState.Paused = false;
        UState.P.GravityWellRange = 0;
        UState.Objects.EnableParallelUpdate = false;
        ArenaEngineCapabilities.TryEnableSeededRng(UState, rngSeed);
    }

    public void UpdateMultiplayerLive(float dt)
    {
        if (!MultiplayerLiveInitialized || MultiplayerLiveComplete)
            return;

        MultiplayerLiveSession.Transport.Poll();
        if (MultiplayerLiveSession.Transport.LastError.NotEmpty())
        {
            HaltMultiplayerForDisconnect(MultiplayerLiveSession.Transport.LastError);
            return;
        }
        if (!MultiplayerLiveSession.Transport.IsConnected)
        {
            HaltMultiplayerForDisconnect("Peer disconnected.");
            return;
        }
        if (MultiplayerLivePaused)
        {
            MultiplayerLiveStatus = "PAUSED";
            return;
        }

        MaintainMultiplayerLiveHandshake(Math.Max(0f, dt));

        MultiplayerLiveAccumulator += Math.Max(0f, dt) * MultiplayerLiveSpeed;
        // Clamp so a long barrier stall doesn't turn into a catch-up spiral afterwards.
        MultiplayerLiveAccumulator = Math.Min(MultiplayerLiveAccumulator, 0.25f);
        int steps = 0;
        while (MultiplayerLiveAccumulator >= 1f / 60f && steps++ < 8 && !MultiplayerLiveComplete)
        {
            if (!AdvanceMultiplayerLiveTurn())
                break;
            MultiplayerLiveAccumulator -= 1f / 60f;
        }
        CheckMultiplayerEngagementLiveness();
        UpdateMultiplayerLiveWatchdog(Math.Max(0f, dt));
        UState.Paused = MultiplayerLivePaused;
    }

    // Ruling 2 liveness predicate: once the sim is ticking, at least one of {target acquired,
    // ship in combat, weapon fire attempted} must occur within the bounded post-spawn window,
    // else the match halts visibly (void result) and telemetry records ARENA_LIVENESS_FAIL with
    // a deterministic cannot-engage reason. Frame-pump starvation before the first tick is the
    // no-progress watchdog's job, not this predicate's.
    void CheckMultiplayerEngagementLiveness()
    {
        if (MultiplayerEngagementSeen || MultiplayerLiveComplete)
            return;
        long simTick = MultiplayerLiveSimTickForHeadless;
        if (simTick <= 0)
            return;

        if (AnyMultiplayerEngagementEvidence())
        {
            MultiplayerEngagementSeen = true;
            MultiplayerTelemetry?.Event("ENGAGEMENT_SEEN", $"simTick={simTick}");
            return;
        }

        // Rebase the liveness clock to the ENGAGE tick (plan Part 3c): the deterministic countdown
        // legitimately holds fire for CountdownTicks, so the engagement window must start at
        // MultiplayerEngageAtTick, not at spawn — otherwise the countdown eats the liveness budget
        // and a slow matchup false-fails. Until the engage tick is known/reached, don't judge.
        if (MultiplayerEngageAtTick < 0 || simTick < MultiplayerEngageAtTick)
            return;

        if (simTick >= MultiplayerEngageAtTick + MultiplayerEngagementWindowTicks)
        {
            ArenaMultiplayerMatchStatus status = MultiplayerMatchStatus();
            string reason = $"No engagement within {MultiplayerEngagementWindowTicks} sim ticks of engage: "
                            + "no target acquired, no ship in combat, no weapon fire "
                            + $"(playerAlive={status.PlayerAlive} enemyAlive={status.EnemyAlive}).";
            MultiplayerTelemetry?.Event("ARENA_LIVENESS_FAIL", reason);
            MultiplayerLiveResult.Disconnected = true;
            MultiplayerLiveResult.DisconnectReason = reason;
            CompleteMultiplayerLive("LIVENESS: " + reason);
        }
    }

    bool AnyMultiplayerEngagementEvidence()
    {
        if (HasEngagedShip(PlayerShips) || HasEngagedShip(EnemyShips))
            return true;

        Projectile[] projectiles = UState?.Objects?.GetProjectiles() ?? Array.Empty<Projectile>();
        for (int i = 0; i < projectiles.Length; ++i)
            if (projectiles[i] != null && projectiles[i].Active)
                return true; // weapon fire attempted
        return false;
    }

    static bool HasEngagedShip(List<Ship> ships)
    {
        if (ships == null)
            return false;
        for (int i = 0; i < ships.Count; ++i)
        {
            Ship s = ships[i];
            if (s != null && s.Active && s.IsAlive && (s.InCombat || s.AI?.Target != null))
                return true;
        }
        return false;
    }

    // Advisor plan A.3 rule 1: retransmit the arm handshake until both sides have seen each
    // other. The host's SessionControlMessage and the join's armed ack each have exactly one
    // in-flight copy otherwise, and either can be consumed by a transport poll that runs before
    // the other peer's fight screen registers its observer (the live lobby polls the shared
    // transport during the launch handoff).
    void MaintainMultiplayerLiveHandshake(float dt)
    {
        bool waiting = MultiplayerLiveSession.Role == ArenaMultiplayerRole.Host
            ? !MultiplayerRemoteArmed
            : !MultiplayerHostSeen;
        if (!waiting)
            return;

        MultiplayerHandshakeResendTimer -= dt;
        if (MultiplayerHandshakeResendTimer > 0f)
            return;
        MultiplayerHandshakeResendTimer = MultiplayerHandshakeResendInterval;
        if (MultiplayerLiveSession.Role == ArenaMultiplayerRole.Host)
            SendMultiplayerControl();
        else
            SendMultiplayerLiveArmedAck();
    }

    void SendMultiplayerLiveArmedAck()
        => MultiplayerLiveSession?.Transport?.Send(LockstepHost.HostPeerId,
            new SessionReadyMessage
            {
                FromPeer = ArenaMultiplayerSession.JoinPlayerPeerId,
                PeerId = ArenaMultiplayerSession.JoinPlayerPeerId,
                Ready = true,
                BuildHash = ArenaMultiplayerPeerSignature.EnvironmentHash(),
                BuildSummary = ArenaMultiplayerPeerSignature.EnvironmentSummary(),
            });

    // Advisor plan A.3 rule 4: liveness or explicit halt. If neither the local turn counter nor
    // the sim tick moves within the watchdog window, surface a visible stalled status and, past
    // the hard deadline, halt with a void result — never silently idle.
    void UpdateMultiplayerLiveWatchdog(float dt)
    {
        if (MultiplayerLiveComplete)
            return;

        long simTick = MultiplayerLiveSimTickForHeadless;
        if (MultiplayerLiveTurn != MultiplayerLastProgressTurn || simTick != MultiplayerLastProgressSimTick)
        {
            MultiplayerLastProgressTurn = MultiplayerLiveTurn;
            MultiplayerLastProgressSimTick = simTick;
            MultiplayerNoProgressSeconds = 0f;
            return;
        }

        MultiplayerNoProgressSeconds += dt;
        if (MultiplayerNoProgressSeconds >= MultiplayerStallHaltSeconds)
        {
            MultiplayerLiveResult.Disconnected = true;
            MultiplayerLiveResult.DisconnectReason =
                $"No lockstep progress for {(int)MultiplayerNoProgressSeconds}s (peer stalled or unreachable).";
            MultiplayerTelemetry?.NetworkError(MultiplayerLiveResult.DisconnectReason);
            CompleteMultiplayerLive("STALLED: " + MultiplayerLiveResult.DisconnectReason);
        }
        else if (MultiplayerNoProgressSeconds >= MultiplayerStallWarnSeconds)
        {
            MultiplayerLiveStatus = $"STALLED {(int)MultiplayerNoProgressSeconds}s — {MultiplayerLiveStatus}";
        }
    }

    bool AdvanceMultiplayerLiveTurn()
    {
        ArenaMultiplayerSettings settings = MultiplayerLiveSession.Settings;
        uint turn = MultiplayerLiveTurn;
        // Custom-fleet program §5.2: the REAL match cap derives from RulesetV0.MaxMatchSeconds (host-settable,
        // already hashed), with MaxTurns as an absolute safety ceiling. Previously ended at MaxTurns only.
        if (turn >= settings.EffectiveMaxTurns)
        {
            MultiplayerLiveResult.MatchEnded = true;
            MultiplayerLiveResult.MatchEndedTurn = turn;
            MultiplayerLiveResult.WinnerPeerId = 0;
            CompleteMultiplayerLive("time limit");
            return false;
        }

        // ARM before frames (advisor plan A.3 rule 1). The host must not commit tick 0 until the
        // join peer's lockstep receiver is registered and acknowledged; the join must not submit
        // input until the host has been heard from. Frames/submits sent before the far side is
        // armed are dropped by the transport with no retransmit — the live "spawns then idles"
        // deadlock. Both sides keep pumping the handshake via MaintainMultiplayerLiveHandshake.
        if (MultiplayerLiveSession.Role == ArenaMultiplayerRole.Host && !MultiplayerRemoteArmed)
        {
            MultiplayerLiveStatus = "waiting for peer to arm";
            return false;
        }
        if (MultiplayerLiveSession.Role == ArenaMultiplayerRole.Join && !MultiplayerHostSeen)
        {
            MultiplayerLiveStatus = "waiting for host to arm";
            return false;
        }

        int peerId = MultiplayerLiveSession.Role == ArenaMultiplayerRole.Host
            ? ArenaMultiplayerSession.HostPlayerPeerId
            : ArenaMultiplayerSession.JoinPlayerPeerId;
        // Submit exactly once per turn: a turn blocked on the lockstep barrier re-enters this
        // method every rendered frame, and re-submitting piles duplicate commands into the frame.
        if (ShouldSubmitMultiplayer(settings, turn) && MultiplayerLastSubmitTurn < turn)
        {
            uint execTick = turn + (uint)Math.Max(0, settings.InputDelay);
            // During Spawn/Countdown, submit a NoOp so the lockstep barrier still advances but no
            // attack order (Target) is issued — the fleets stay frozen until the engage tick. The
            // phase is a pure function of the committed sim tick, so this gate is identical on both
            // peers (deterministic input stream).
            SimCommand cmd = MultiplayerPhase == ArenaMatchPhase.Fight || MultiplayerPhase == ArenaMatchPhase.Engage
                ? BuildMultiplayerFocusCommand(peerId, execTick, turn)
                : new SimCommand(execTick, peerId, turn, SimCommandKind.NoOp);
            MultiplayerLiveClient.Submit(cmd);
            MultiplayerLastSubmitTurn = turn;
            MultiplayerLiveResult.CommandsSubmitted++;
        }

        MultiplayerLiveSession.Transport.Poll();

        if (MultiplayerLiveSession.Role == ArenaMultiplayerRole.Host)
        {
            if (ShouldHaveSubmittedForExecTick(settings, turn) && !HasBothInputsForTurn(turn))
            {
                // Drain-before-starve (A.3 rule 3): the peer's submit may already be sitting in
                // the transport; drain once more before yielding to the next rendered frame.
                MultiplayerLiveSession.Transport.Poll();
                if (!HasBothInputsForTurn(turn))
                {
                    MultiplayerLiveStatus = $"waiting for turn {turn} input";
                    MultiplayerTelemetry?.Event("WAIT_INPUT", $"turn={turn}");
                    return false;
                }
            }

            MultiplayerLiveHost.CommitTick(turn);
            MultiplayerLiveSession.Transport.Poll();
            StabilizeMultiplayerArenaViewAndVisibility();
            MultiplayerLiveClient.Pump();
            StabilizeMultiplayerArenaViewAndVisibility();
            MultiplayerLiveSession.Transport.Poll();

            RecordMultiplayerLiveTurn(turn, MultiplayerLiveSim.Hash(), MultiplayerLiveHost.Desync);
        }
        else
        {
            StabilizeMultiplayerArenaViewAndVisibility();
            MultiplayerLiveClient.Pump();
            StabilizeMultiplayerArenaViewAndVisibility();
            MultiplayerLiveSession.Transport.Poll();
            if (MultiplayerLiveSim.Tick <= turn)
            {
                // Drain-before-starve: the host frame may have arrived on the poll above.
                StabilizeMultiplayerArenaViewAndVisibility();
                MultiplayerLiveClient.Pump();
                StabilizeMultiplayerArenaViewAndVisibility();
            }
            if (MultiplayerLiveSim.Tick <= turn)
            {
                MultiplayerLiveStatus = $"waiting for host turn {turn}";
                MultiplayerTelemetry?.Event("WAIT_HOST_FRAME", $"turn={turn} simTick={MultiplayerLiveSim.Tick}");
                return false;
            }

            RecordMultiplayerLiveTurn(turn, MultiplayerLiveSim.Hash(), null);
        }

        // Drive the ArenaMatchPhase machine at the single point where simTick is known and identical
        // on both peers (the committed tick just advanced). This is where the deterministic countdown
        // fires EngageAll — a tick threshold, never a wall-clock dt.
        AdvanceMultiplayerPhase(MultiplayerLiveSim.Tick);

        ArenaMultiplayerMatchStatus status = MultiplayerMatchStatus();
        if (status.Ended)
        {
            MultiplayerLiveResult.MatchEnded = true;
            MultiplayerLiveResult.MatchEndedTurn = turn;
            MultiplayerLiveResult.WinnerPeerId = status.WinnerPeerId;
            CompleteMultiplayerLive(status.WinnerPeerId == 0 ? "draw" : $"winner peer {status.WinnerPeerId}");
            return true;
        }

        if (MultiplayerLiveResult.Desynced)
        {
            CompleteMultiplayerLive($"DESYNC turn {MultiplayerLiveResult.DesyncTurn}: {MultiplayerLiveResult.DesyncReason}");
            return false;
        }

        MultiplayerLiveTurn++;
        if (MultiplayerPhase != ArenaMatchPhase.Countdown)
            MultiplayerLiveStatus = $"turn {MultiplayerLiveTurn} hash {MultiplayerLiveResult.FinalHash}";
        return true;
    }

    // ArenaMatchPhase machine (plan Part 3c). Driven by the committed sim tick, so the countdown
    // threshold and EngageAll fire on the SAME tick on both peers — the determinism guarantee.
    void AdvanceMultiplayerPhase(long simTick)
    {
        if (MultiplayerLiveComplete || simTick < 0)
            return;

        switch (MultiplayerPhase)
        {
            case ArenaMatchPhase.Spawn:
                // The engage tick is an ABSOLUTE sim tick, not firstSeenTick + countdown: the host
                // reaches its first committed tick at simTick=1 while the join lags by InputDelay, so
                // a relative baseline would give the peers different engage ticks. Both peers spawn at
                // tick 0 conceptually, so engage at the absolute tick = CountdownTicks; both evaluate
                // "simTick >= CountdownTicks" against the SAME lockstepped tick and engage together.
                MultiplayerEngageAtTick = MultiplayerCountdownTicks;
                MultiplayerPhase = simTick >= MultiplayerEngageAtTick ? ArenaMatchPhase.Engage : ArenaMatchPhase.Countdown;
                MultiplayerTelemetry?.Event("PHASE_COUNTDOWN",
                    $"firstTick={simTick} engageAtTick={MultiplayerEngageAtTick} countdownTicks={MultiplayerCountdownTicks}");
                if (MultiplayerPhase == ArenaMatchPhase.Countdown)
                    UpdateCountdownStatus(simTick);
                else
                    goto case ArenaMatchPhase.Engage;
                break;

            case ArenaMatchPhase.Countdown:
                if (simTick >= MultiplayerEngageAtTick)
                {
                    MultiplayerPhase = ArenaMatchPhase.Engage;
                    goto case ArenaMatchPhase.Engage;
                }
                UpdateCountdownStatus(simTick);
                break;

            case ArenaMatchPhase.Engage:
                // Restore the combat stance (frozen during countdown) then issue attack orders on
                // BOTH peers at the SAME tick.
                UnfreezeMultiplayerFleet(PlayerShips);
                UnfreezeMultiplayerFleet(EnemyShips);
                EngageAll();
                MultiplayerPhase = ArenaMatchPhase.Fight;
                MultiplayerTelemetry?.Event("PHASE_ENGAGE", $"simTick={simTick}");
                MultiplayerLiveStatus = "ENGAGE";
                break;

            case ArenaMatchPhase.Fight:
                // Win/turn-limit detection stays in AdvanceMultiplayerLiveTurn; nothing to do here.
                break;
        }
    }

    void UpdateCountdownStatus(long simTick)
    {
        long remaining = MultiplayerEngageAtTick - simTick;
        if (remaining < 0) remaining = 0;
        int seconds = (int)((remaining + 59) / 60);
        MultiplayerLiveStatus = $"ENGAGE IN {seconds}s";
    }

    void OnMultiplayerHostMessage(LockstepMessage message)
    {
        if (MultiplayerLiveSession == null || MultiplayerLiveComplete)
            return;
        if (message is SessionReadyMessage armed
            && armed.PeerId == ArenaMultiplayerSession.JoinPlayerPeerId && armed.Ready)
        {
            if (!MultiplayerRemoteArmed)
                MultiplayerTelemetry?.Event("REMOTE_ARMED", $"peer={armed.PeerId}");
            MultiplayerRemoteArmed = true;
            // Answer every ack: the join keeps re-arming until it hears the host, and the host's
            // initial SessionControlMessage may have been consumed before the join registered.
            SendMultiplayerControl();
        }
        if (message is ChecksumMessage c && c.FromPeer == ArenaMultiplayerSession.JoinPlayerPeerId)
        {
            MultiplayerRemoteChecksumTick = Math.Max(MultiplayerRemoteChecksumTick, c.Tick);
            if (c.Tick <= 5 || c.Tick % 60 == 0)
                MultiplayerTelemetry?.Event("REMOTE_CHECKSUM",
                    $"turn={c.Tick} peer={c.FromPeer} hash=0x{c.Hi:X16}:0x{c.Lo:X16}");
        }
        if (message is SubmitCommandMessage s)
        {
            if (!MultiplayerSubmittedInputs.TryGetValue(s.Command.Tick, out HashSet<int> peers))
            {
                peers = new HashSet<int>();
                MultiplayerSubmittedInputs[s.Command.Tick] = peers;
            }
            peers.Add(s.Command.PlayerId);
            if (s.Command.Tick <= 5 || s.Command.Tick % 60 == 0)
                MultiplayerTelemetry?.Event("REMOTE_SUBMIT",
                    $"execTick={s.Command.Tick} player={s.Command.PlayerId} kind={s.Command.Kind}");
        }
    }

    void OnMultiplayerJoinMessage(LockstepMessage message)
    {
        if (MultiplayerLiveSession == null || MultiplayerLiveComplete)
            return;
        if (!MultiplayerHostSeen)
        {
            // Any observed host message (control, command frame, error) proves the host's live
            // session is armed; the join may start submitting/advancing turns.
            MultiplayerHostSeen = true;
            MultiplayerTelemetry?.Event("HOST_SEEN", message.GetType().Name);
        }
        if (message is SessionControlMessage c)
        {
            MultiplayerLivePaused = c.Paused;
            MultiplayerLiveSpeed = ArenaMultiplayerSettings.ClampGameSpeed(c.GameSpeed);
            UState.GameSpeed = MultiplayerLiveSpeed;
            MultiplayerTelemetry?.Event("CONTROL",
                $"paused={MultiplayerLivePaused} speed={MultiplayerLiveSpeed:0.###}");
        }
        if (message is SessionErrorMessage e)
        {
            MultiplayerTelemetry?.Event("SESSION_ERROR", e.Error);
            CompleteMultiplayerLive(e.Error.NotEmpty() ? e.Error : "session error");
        }
    }

    void ToggleMultiplayerPause()
    {
        if (MultiplayerLiveSession?.Role != ArenaMultiplayerRole.Host || MultiplayerLiveComplete)
            return;
        MultiplayerLivePaused = !MultiplayerLivePaused;
        MultiplayerTelemetry?.Event("HOST_PAUSE", $"paused={MultiplayerLivePaused}");
        SendMultiplayerControl();
    }

    void CycleMultiplayerSpeed()
    {
        if (MultiplayerLiveSession?.Role != ArenaMultiplayerRole.Host || MultiplayerLiveComplete)
            return;
        MultiplayerLiveSpeed = MultiplayerLiveSpeed < 0.75f ? 1f
            : MultiplayerLiveSpeed < 1.5f ? 2f
            : MultiplayerLiveSpeed < 3.5f ? 4f
            : 0.5f;
        MultiplayerTelemetry?.Event("HOST_SPEED", $"speed={MultiplayerLiveSpeed:0.###}");
        SendMultiplayerControl();
    }

    void SendMultiplayerControl()
    {
        UState.GameSpeed = MultiplayerLiveSpeed;
        MultiplayerLiveSession?.Transport?.Send(ArenaMultiplayerSession.JoinPlayerPeerId,
            new SessionControlMessage
            {
                FromPeer = LockstepHost.HostPeerId,
                Paused = MultiplayerLivePaused,
                GameSpeed = MultiplayerLiveSpeed,
            });
    }

    public void StabilizeMultiplayerArenaViewAndVisibility()
    {
        if (UState?.Objects == null || UState.Player == null)
            return;

        // The base engine uses screen-local frustum/visibility flags inside a few combat and FX
        // branches. In network lockstep those flags must not depend on each peer's resolution,
        // camera, or local role. Keep the Arena view deterministic and reveal Arena objects to the
        // authoritative player empire on both peers.
        UState.CamPos = new Vector3d(ArenaCenter.X, ArenaCenter.Y, 12000.0);
        CamDestination = UState.CamPos;
        LookingAtPlanet = false;

        Empire viewer = UState.Player;
        StabilizeMultiplayerShips(PlayerShips, viewer);
        StabilizeMultiplayerShips(EnemyShips, viewer);

        Ship[] ships = UState.Objects.GetShips();
        for (int i = 0; i < ships.Length; ++i)
        {
            Ship ship = ships[i];
            if (ship == null || !ship.Active)
                continue;

            StabilizeMultiplayerShip(ship, viewer);
        }

        Projectile[] projectiles = UState.Objects.GetProjectiles();
        for (int i = 0; i < projectiles.Length; ++i)
        {
            Projectile projectile = projectiles[i];
            if (projectile == null || !projectile.Active)
                continue;

            projectile.InFrustum = true;
        }
    }

    static void StabilizeMultiplayerShips(List<Ship> ships, Empire viewer)
    {
        if (ships == null)
            return;
        for (int i = 0; i < ships.Count; ++i)
            StabilizeMultiplayerShip(ships[i], viewer);
    }

    static void StabilizeMultiplayerShip(Ship ship, Empire viewer)
    {
        if (ship == null || !ship.Active)
            return;
        ship.InFrustum = true;
        ship.KnownByEmpires?.SetSeen(viewer);
    }

    void RecordMultiplayerLiveTurn(uint turn, (ulong lo, ulong hi) hash, DesyncDetector desync)
    {
        MultiplayerLiveResult.TurnHashes.Add(new ArenaMultiplayerTurnHash(turn, hash, hash));
        MultiplayerLiveResult.FinalHash = MultiplayerHashText(hash);
        ArenaMultiplayerMatchStatus status = MultiplayerMatchStatus();
        MultiplayerTelemetry?.Turn(turn, MultiplayerLiveSession.Role, MultiplayerLiveResult.FinalHash,
            MultiplayerLiveSim?.Tick ?? 0, MultiplayerRemoteChecksumTick,
            MultiplayerLiveResult.CommandsSubmitted, status.PlayerAlive, status.EnemyAlive,
            forced: desync?.HasDesync == true);
        if (desync != null && desync.HasDesync)
        {
            MultiplayerLiveResult.Desynced = true;
            MultiplayerLiveResult.DesyncTurn = desync.FirstDivergentTick;
            MultiplayerLiveResult.DesyncReason = ArenaMultiplayerSession.DesyncSummary(desync);
            MultiplayerTelemetry?.Desync(turn, MultiplayerLiveResult, desync);
            Log.Warning($"Arena MP DESYNC live role={MultiplayerLiveSession.Role} turn={turn}: "
                        + MultiplayerLiveResult.DesyncReason);
        }
    }

    void CompleteMultiplayerLive(string reason)
    {
        if (MultiplayerLiveComplete)
            return;
        MultiplayerLiveComplete = true;
        MultiplayerPhase = ArenaMatchPhase.Resolve;
        MultiplayerEndReason = reason ?? "";
        MultiplayerLivePaused = true;
        UState.Paused = true;
        MultiplayerLiveStatus = $"COMPLETE {reason}\nturns {MultiplayerLiveResult.TurnsCompleted}\nfinal {MultiplayerLiveResult.FinalHash}";
        MultiplayerTelemetry?.Event("COMPLETE",
            $"reason='{reason}' turns={MultiplayerLiveResult.TurnsCompleted} final={MultiplayerLiveResult.FinalHash}");
        Log.Warning($"Arena MP COMPLETE role={MultiplayerLiveSession.Role} reason='{reason}' "
                    + $"turns={MultiplayerLiveResult.TurnsCompleted} final={MultiplayerLiveResult.FinalHash}");
        ShowMultiplayerEndPanel();
    }

    void HaltMultiplayerForDisconnect(string reason)
    {
        MultiplayerLiveResult.Disconnected = true;
        MultiplayerLiveResult.DisconnectReason = reason.NotEmpty() ? reason : "Peer disconnected.";
        MultiplayerTelemetry?.NetworkError(MultiplayerLiveResult.DisconnectReason);
        CompleteMultiplayerLive("NETWORK: " + MultiplayerLiveResult.DisconnectReason);
    }

    void ShowMultiplayerEndPanel()
    {
        if (MultiplayerEndPanel != null)
        {
            MultiplayerEndPanel.Visible = true;
            return;
        }

        var panel = new RectF(ScreenCenter.X - 250, ScreenCenter.Y - 150, 500, 300);
        MultiplayerEndPanel = ArenaTheme.Card(panel);
        MultiplayerEndPanel.Name = "arena_mp_end_panel";
        Add(MultiplayerEndPanel);
        MultiplayerEndPanel.Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 22, panel.Y + 18), "MATCH COMPLETE"));
        MultiplayerEndPanel.Add(new UILabel(new Vector2(panel.X + 22, panel.Y + 52), "", ArenaTheme.BodyFont, ArenaTheme.TextPrimary)
        {
            Name = "arena_mp_end_winner",
            DynamicText = _ => MultiplayerEndWinnerText(),
        });
        MultiplayerEndPanel.Add(new UILabel(new Vector2(panel.X + 22, panel.Y + 84), "", ArenaTheme.BodySmallFont, ArenaTheme.TextSecondary)
        {
            Name = "arena_mp_end_losses",
            DynamicText = _ => MultiplayerEndLossText(),
        });
        MultiplayerEndPanel.Add(new UILabel(new Vector2(panel.X + 22, panel.Y + 112), "", ArenaTheme.BodySmallFont, ArenaTheme.TextSecondary)
        {
            Name = "arena_mp_end_turns",
            DynamicText = _ => $"Turns: {MultiplayerLiveResult?.TurnsCompleted ?? 0} | Final: {MultiplayerLiveResult?.FinalHash ?? ""}",
        });
        MultiplayerEndPanel.Add(new UILabel(new Vector2(panel.X + 22, panel.Y + 140), "", ArenaTheme.BodySmallFont, ArenaTheme.TextSecondary)
        {
            Name = "arena_mp_end_flags",
            DynamicText = _ => MultiplayerEndFlagText(),
        });
        MultiplayerEndPanel.Add(new UILabel(new Vector2(panel.X + 22, panel.Y + 168), "", ArenaTheme.BodySmallFont, ArenaTheme.TextSecondary)
        {
            Name = "arena_mp_end_reason",
            DynamicText = _ => $"End: {(MultiplayerEndReason.NotEmpty() ? MultiplayerEndReason : "—")}",
        });

        UIList actions = AddList(new Vector2(panel.X + 22, panel.Bottom - 54));
        actions.Direction = new Vector2(1f, 0f);
        actions.Padding = new Vector2(10f, 10f);
        actions.LayoutStyle = ListLayoutStyle.ResizeList;
        UIButton rematch = ArenaTheme.AddPrimaryButton(actions, "REMATCH", _ => StartMultiplayerRematch(), 120f);
        rematch.Name = "arena_mp_end_rematch";
        UIButton lobby = ArenaTheme.AddPillButton(actions, "LOBBY", _ => BackToMultiplayerLobby(), 100f);
        lobby.Name = "arena_mp_end_lobby";
    }

    string MultiplayerEndWinnerText()
    {
        if (MultiplayerLiveResult == null)
            return "No result.";
        if (MultiplayerLiveResult.Disconnected)
            return "Match halted: peer disconnected.";
        if (MultiplayerLiveResult.Desynced)
            return "Match void: lockstep desync.";
        return MultiplayerLiveResult.WinnerPeerId switch
        {
            ArenaMultiplayerSession.HostPlayerPeerId => "Winner: Host fleet",
            ArenaMultiplayerSession.JoinPlayerPeerId => "Winner: Join fleet",
            _ => "Result: Draw",
        };
    }

    string MultiplayerEndLossText()
    {
        int hostStart = MultiplayerLiveResult?.HostSnapshot.PlayerShipIds.Length ?? 0;
        int joinStart = MultiplayerLiveResult?.HostSnapshot.EnemyShipIds.Length ?? 0;
        int hostAlive = AliveCount(PlayerShips);
        int joinAlive = AliveCount(EnemyShips);
        return $"Losses: Host {Math.Max(0, hostStart - hostAlive)}/{hostStart} | "
               + $"Join {Math.Max(0, joinStart - joinAlive)}/{joinStart}";
    }

    string MultiplayerEndFlagText()
    {
        if (MultiplayerLiveResult == null)
            return "";
        if (MultiplayerLiveResult.Desynced)
            return $"DESYNC: {MultiplayerLiveResult.DesyncReason}";
        if (MultiplayerLiveResult.Disconnected)
            return $"DISCONNECT: {MultiplayerLiveResult.DisconnectReason}";
        return "DESYNC: none";
    }

    void BackToMultiplayerLobby()
    {
        // Amendment 4: tear down this match's transient @arena/ designs so the global table returns to its
        // pre-match state on lobby exit (BackToMultiplayerLobby didn't do this before).
        TeardownMultiplayerCustomDesigns();
        MultiplayerLiveSession?.Dispose();
        MultiplayerLiveSession = null;
        MultiplayerTelemetry?.Dispose();
        MultiplayerTelemetry = null;
        ScreenManager.GoToScreen(new ArenaMultiplayerLobbyScreen(), clear3DObjects: true);
    }

    void StartMultiplayerRematch()
    {
        if (MultiplayerLiveSession == null)
            return;

        // Amendment 4: tear down THIS screen's registration before handing the transport to the rematch screen.
        // The rematch's ArmMultiplayerLive re-registers the (same) custom designs idempotently, so nothing
        // accumulates across rematches and the rematch never finds them missing.
        TeardownMultiplayerCustomDesigns();
        TcpLockstepTransport transport = MultiplayerLiveSession.Transport;
        ArenaMultiplayerRole role = MultiplayerLiveSession.Role;
        ArenaMultiplayerSettings settings = MultiplayerLiveSession.Settings.WithRematchSeed();
        MultiplayerLiveSession = null;
        MultiplayerTelemetry?.Dispose();
        MultiplayerTelemetry = null;

        ArenaFightScreen screen = Create(settings.HostRacePreference, settings.MatchSeed,
            startAtHub: false, opponentPreference: settings.JoinRacePreference);
        screen.MultiplayerGoToScreenOverrideForHeadless = MultiplayerGoToScreenOverrideForHeadless;
        screen.ArmMultiplayerLive(new ArenaMultiplayerLiveSession(role, transport, settings));
        if (MultiplayerGoToScreenOverrideForHeadless != null)
            MultiplayerGoToScreenOverrideForHeadless(screen);
        else
            ScreenManager.GoToScreen(screen, clear3DObjects: true);
    }

    bool HasBothInputsForTurn(uint turn)
        => MultiplayerSubmittedInputs.TryGetValue(turn, out HashSet<int> peers)
           && peers.Contains(ArenaMultiplayerSession.HostPlayerPeerId)
           && peers.Contains(ArenaMultiplayerSession.JoinPlayerPeerId);

    static bool ShouldSubmitMultiplayer(ArenaMultiplayerSettings settings, uint turn)
        => settings.CommandEveryTurns <= 1 || turn % (uint)settings.CommandEveryTurns == 0;

    static bool ShouldHaveSubmittedForExecTick(ArenaMultiplayerSettings settings, uint turn)
    {
        uint delay = (uint)Math.Max(0, settings.InputDelay);
        return turn >= delay && ShouldSubmitMultiplayer(settings, turn - delay);
    }

    static string MultiplayerHashText((ulong lo, ulong hi) hash)
        => $"0x{hash.hi:X16}:0x{hash.lo:X16}";

    void BuildMultiplayerLiveHud()
    {
        var panel = new RectF(18, 72, 340, MultiplayerLiveSession.Role == ArenaMultiplayerRole.Host ? 142 : 104);
        Add(ArenaTheme.Card(panel));
        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 12, panel.Y + 10), "MULTIPLAYER"));
        Add(new UILabel(new Vector2(panel.X + 12, panel.Y + 34), "", ArenaTheme.BodySmallFont, ArenaTheme.TextSecondary)
        {
            DynamicText = _ => MultiplayerLiveStatusText,
        });

        UIList controls = AddList(new Vector2(panel.X + 12, panel.Bottom - 42));
        controls.Direction = new Vector2(1f, 0f);
        controls.Padding = new Vector2(8f, 8f);
        controls.LayoutStyle = ListLayoutStyle.ResizeList;
        if (MultiplayerLiveSession.Role == ArenaMultiplayerRole.Host)
        {
            UIButton pause = ArenaTheme.AddPrimaryButton(controls,
                MultiplayerLivePaused ? "RESUME" : "PAUSE", _ => ToggleMultiplayerPause(), 94f);
            pause.Name = "arena_mp_live_pause";
            pause.DynamicText = () => MultiplayerLivePaused ? "RESUME" : "PAUSE";
            UIButton speed = ArenaTheme.AddPillButton(controls,
                $"SPEED {MultiplayerLiveSpeed:0.#}X", _ => CycleMultiplayerSpeed(), 112f);
            speed.Name = "arena_mp_live_speed";
            speed.DynamicText = () => $"SPEED {MultiplayerLiveSpeed:0.#}X";
        }
    }

    public override void ExitScreen()
    {
        // Amendment 4 catch-all: guarantee no transient @arena/ design leaks on ANY screen exit — disconnect,
        // force-close, match-end-then-exit. Idempotent with the lobby/rematch teardown paths.
        TeardownMultiplayerCustomDesigns();
        MultiplayerLiveSession?.Dispose();
        MultiplayerLiveSession = null;
        MultiplayerTelemetry?.Dispose();
        MultiplayerTelemetry = null;
        base.ExitScreen();
    }

    public UniverseStateLockstepSimulation CreateMultiplayerLockstepSimulation(
        DeterminismProfile profile = DeterminismProfile.ReplayWinX64Float,
        float dt = 1f / 60f)
        => new(this, profile, dt);

    public SimCommand BuildMultiplayerFocusCommand(int peerId, uint tick, uint localSequence)
    {
        bool peerIsPlayer = ArenaPlayer != null && peerId == ArenaPlayer.Id;
        bool peerIsEnemy = ArenaEnemy != null && peerId == ArenaEnemy.Id;
        if (!peerIsPlayer && !peerIsEnemy)
            return new SimCommand(tick, peerId, localSequence, SimCommandKind.NoOp);

        Ship subject = FirstAlive(peerIsPlayer ? PlayerShips : EnemyShips);
        Ship target = FirstAlive(peerIsPlayer ? EnemyShips : PlayerShips);
        if (subject == null || target == null)
            return new SimCommand(tick, peerId, localSequence, SimCommandKind.NoOp);

        return new SimCommand(tick, peerId, localSequence, SimCommandKind.AttackTarget, subject.Id, target.Id);
    }

    public (ulong lo, ulong hi, string algorithm) MultiplayerStateHash(
        DeterminismProfile profile = DeterminismProfile.ReplayWinX64Float)
        => UState.ComputeAuthoritativeStateHash(profile);

    public void ForceMultiplayerDesyncForTest()
    {
        Ship ship = FirstAlive(PlayerShips);
        if (ship != null)
            ship.Position = ship.Position + new Vector2(3f, 0f);
    }

    public ArenaMultiplayerMatchStatus MultiplayerMatchStatus()
        => new(AliveCount(PlayerShips), AliveCount(EnemyShips));

    // Resolve the exchanged canonical bundle; fall back to a zero-offset column bundle from the
    // name list if no bundle was carried (legacy path). Both peers resolve the SAME bundle bytes.
    static FleetDesignT ResolveMultiplayerBundle(string bundle, string[] names)
    {
        if (bundle.NotEmpty())
        {
            FleetDesignT decoded = ArenaFleetBundle.Decode(bundle);
            if (decoded.Nodes.Count > 0)
                return decoded;
        }
        return ArenaFleetBundle.FromDesignNames(names);
    }

    // Ship names in the bundle's STABLE order (the same order used for the hash and the spawn), so a
    // legality/first-design pick and the spawn agree on ordering.
    static string[] BundleShipNames(FleetDesignT bundle)
        => ArenaFleetBundle.StableNodeOrder(bundle).Select(n => n.ShipName).ToArray();

    // Formation-aware spawn (plan Part 3d). Iterates nodes in the SINGLE shared StableNodeOrder so
    // ship-id assignment order is identical on both peers (MultiplayerSnapshot id equality holds).
    // Placement: ArenaCenter + (sideX,0) + offset*sideMirror on X. Only legal combat designs spawn;
    // an illegal node is skipped (matching ResolveMultiplayerFleet), keeping both peers in agreement.
    static void SpawnMultiplayerFormation(List<Ship> ships, Empire owner, FleetDesignT bundle,
        float sideX, float sideMirror, Vector2 facing)
    {
        Vector2 center = ArenaCenter;
        var nodes = ArenaFleetBundle.StableNodeOrder(bundle);
        int index = 0;
        foreach (FleetDataDesignNode node in nodes)
        {
            if (!ResourceManager.Ships.GetDesign(node.ShipName, out IShipDesign design)
                || !IsLegalCombatCraft(design))
                continue;
            Vector2 offset = node.RelativeFleetOffset;
            // Mirror the join side across the centerline on X; if the authored offset is zero
            // (name-list fallback) fan out into the legacy column so ships don't stack.
            Vector2 placed = offset == Vector2.Zero
                ? new Vector2(sideX, (index - (nodes.Count - 1) / 2f) * RowSpan)
                : new Vector2(sideX + offset.X * sideMirror, offset.Y);
            Ship ship = CreateArenaShipAtPoint(owner.Universe, node.ShipName, owner, center + placed, facing);
            if (ship == null)
                throw new InvalidOperationException($"Failed to spawn Arena PvP ship '{node.ShipName}'.");
            ship.SensorRange = 400000f;
            ships.Add(ship);
            ++index;
        }
    }

    // Countdown freeze/unfreeze. ShipAI autonomously acquires sensor targets and fires regardless of
    // CombatState, so the true "hold fire" gate is AI.IgnoreCombat (checked in both the retarget and
    // the weapon-fire paths). ClearOrders resets IgnoreCombat, so set it true AFTER clearing. This is
    // deterministic (same on both peers, applied at spawn and lifted at the shared engage tick).
    static void FreezeMultiplayerFleet(List<Ship> ships)
    {
        if (ships == null) return;
        for (int i = 0; i < ships.Count; ++i)
        {
            Ship s = ships[i];
            if (s == null || !s.Active || s.AI == null) continue;
            s.AI.ClearOrders();
            s.SetCombatStance(CombatState.None);
            s.AI.IgnoreCombat = true;
            s.AI.ArenaHoldFire = true; // hard fire gate honored by FireOnTarget/SelectCombatTarget
        }
    }

    static void UnfreezeMultiplayerFleet(List<Ship> ships)
    {
        if (ships == null) return;
        for (int i = 0; i < ships.Count; ++i)
        {
            Ship s = ships[i];
            if (s == null || !s.Active || s.AI == null) continue;
            s.AI.ArenaHoldFire = false; // lift hold-fire on the shared engage tick (one-way flip)
            s.AI.IgnoreCombat = false;
            s.SetCombatStance(CombatState.Artillery);
        }
    }

    static IShipDesign[] ResolveMultiplayerFleet(string[] designNames)
    {
        var designs = new List<IShipDesign>();
        foreach (string name in ArenaMultiplayerSettings.NormalizeFleet(designNames))
        {
            if (!ResourceManager.Ships.GetDesign(name, out IShipDesign design)
                || !IsLegalCombatCraft(design))
                continue;
            designs.Add(design);
        }
        return designs.ToArray();
    }

    static void RemoveMultiplayerShips(List<Ship> ships)
    {
        foreach (Ship ship in ships)
            if (ship != null && ship.Active)
                ship.Die(null, cleanupOnly: true);
        ships.Clear();
    }

    static int AliveCount(List<Ship> ships)
        => ships?.Count(s => s != null && s.IsAlive) ?? 0;

    static Ship FirstAlive(System.Collections.Generic.List<Ship> ships)
    {
        if (ships == null)
            return null;
        return ships
            .Where(s => s != null && s.Active && s.IsAlive)
            .OrderBy(s => s.Id)
            .FirstOrDefault();
    }
}

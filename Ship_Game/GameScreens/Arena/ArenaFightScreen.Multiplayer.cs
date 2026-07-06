using System;
using System.Collections.Generic;
using System.Linq;
using SDLockstep;
using SDGraphics;
using SDUtils.Deterministic;
using Ship_Game.Determinism;
using Ship_Game.Determinism.Lockstep;
using Ship_Game.AI;
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
    }

    public void InitializeMultiplayerLiveIfNeeded()
    {
        if (MultiplayerLiveSession == null || MultiplayerLiveInitialized)
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

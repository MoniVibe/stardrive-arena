using System;
using System.Collections.Generic;
using System.Linq;
using SDLockstep;
using SDGraphics;
using SDUtils.Deterministic;
using Ship_Game.Determinism;
using Ship_Game.Determinism.Lockstep;
using Ship_Game.Ships;
using Ship_Game.UI;
using Vector2 = SDGraphics.Vector2;

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
    readonly Dictionary<uint, HashSet<int>> MultiplayerSubmittedInputs = new();
    uint MultiplayerLiveTurn;
    float MultiplayerLiveAccumulator;
    long MultiplayerRemoteChecksumTick = -1;
    bool MultiplayerLivePaused;
    float MultiplayerLiveSpeed = 1f;
    string MultiplayerLiveStatus = "";
    bool MultiplayerLiveInitialized;
    bool MultiplayerLiveComplete;

    public bool MultiplayerLiveActive => MultiplayerLiveSession != null;
    public bool MultiplayerLiveDisplayPaused => MultiplayerLiveActive && MultiplayerLivePaused;
    public string MultiplayerLiveStatusText => MultiplayerLiveStatus ?? "";

    public bool HasPendingMultiplayerPvPSetup => MultiplayerPvPMode;

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

    public void ArmMultiplayerLive(ArenaMultiplayerLiveSession session)
    {
        MultiplayerLiveSession = session ?? throw new ArgumentNullException(nameof(session));
        ArenaMultiplayerSettings settings = session.Settings.WithResolvedFleets();
        ConfigureMultiplayerPvP(settings);
        CreateSimThread = false;
        ArenaEngineCapabilities.TrySetParallelUpdate(UState.Objects, false);
        ArenaEngineCapabilities.TryEnableSeededRng(UState, settings.RngSeed);
        MultiplayerLivePaused = settings.StartPaused;
        MultiplayerLiveSpeed = ArenaMultiplayerSettings.ClampGameSpeed(settings.GameSpeed);
        MultiplayerLiveStatus = $"{session.Role.ToString().ToUpperInvariant()} armed";
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
        }

        BuildMultiplayerLiveHud();
        MultiplayerLiveStatus = $"{MultiplayerLiveSession.Role.ToString().ToUpperInvariant()} match started";
        MultiplayerLiveInitialized = true;
        UState.Paused = MultiplayerLivePaused;
    }

    public void StartMultiplayerPvPMatch()
    {
        if (!MultiplayerPvPMode)
            return;

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

        IShipDesign[] hostDesigns = ResolveMultiplayerFleet(MultiplayerHostFleetDesigns);
        IShipDesign[] joinDesigns = ResolveMultiplayerFleet(MultiplayerJoinFleetDesigns);
        if (hostDesigns.Length == 0 || joinDesigns.Length == 0)
            throw new InvalidOperationException("Arena PvP lockstep requires at least one legal design on each side.");

        PlayerDesign = hostDesigns[0];
        EnemyDesign = joinDesigns[0];
        SpawnMultiplayerFleet(PlayerShips, ArenaPlayer, hostDesigns, -Gap, PlayerSpawnFacing);
        SpawnMultiplayerFleet(EnemyShips, ArenaEnemy, joinDesigns, +Gap, EnemySpawnFacing);
        RetargetTimer = 0f;
        EngageAll();
        RunStarted = PlayerShips.Count > 0 && EnemyShips.Count > 0;
        if (!RunStarted)
            throw new InvalidOperationException("Arena PvP lockstep failed to spawn both fleets.");
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
            MultiplayerLiveStatus = "NETWORK: " + MultiplayerLiveSession.Transport.LastError;
            return;
        }
        if (MultiplayerLivePaused)
        {
            MultiplayerLiveStatus = "PAUSED";
            return;
        }

        MultiplayerLiveAccumulator += Math.Max(0f, dt) * MultiplayerLiveSpeed;
        int steps = 0;
        while (MultiplayerLiveAccumulator >= 1f / 60f && steps++ < 8 && !MultiplayerLiveComplete)
        {
            if (!AdvanceMultiplayerLiveTurn())
                break;
            MultiplayerLiveAccumulator -= 1f / 60f;
        }
        UState.Paused = MultiplayerLivePaused;
    }

    bool AdvanceMultiplayerLiveTurn()
    {
        ArenaMultiplayerSettings settings = MultiplayerLiveSession.Settings;
        uint turn = MultiplayerLiveTurn;
        if (turn >= settings.MaxTurns)
        {
            CompleteMultiplayerLive("turn limit");
            return false;
        }

        int peerId = MultiplayerLiveSession.Role == ArenaMultiplayerRole.Host
            ? ArenaMultiplayerSession.HostPlayerPeerId
            : ArenaMultiplayerSession.JoinPlayerPeerId;
        if (ShouldSubmitMultiplayer(settings, turn))
        {
            MultiplayerLiveClient.Submit(BuildMultiplayerFocusCommand(peerId,
                turn + (uint)Math.Max(0, settings.InputDelay), turn));
            MultiplayerLiveResult.CommandsSubmitted++;
        }

        MultiplayerLiveSession.Transport.Poll();

        if (MultiplayerLiveSession.Role == ArenaMultiplayerRole.Host)
        {
            if (ShouldHaveSubmittedForExecTick(settings, turn) && !HasBothInputsForTurn(turn))
            {
                MultiplayerLiveStatus = $"waiting for turn {turn} input";
                return false;
            }

            MultiplayerLiveHost.CommitTick(turn);
            MultiplayerLiveSession.Transport.Poll();
            MultiplayerLiveClient.Pump();
            MultiplayerLiveSession.Transport.Poll();

            RecordMultiplayerLiveTurn(turn, MultiplayerLiveSim.Hash(), MultiplayerLiveHost.Desync);
        }
        else
        {
            MultiplayerLiveClient.Pump();
            MultiplayerLiveSession.Transport.Poll();
            if (MultiplayerLiveSim.Tick <= turn)
            {
                MultiplayerLiveStatus = $"waiting for host turn {turn}";
                return false;
            }

            RecordMultiplayerLiveTurn(turn, MultiplayerLiveSim.Hash(), null);
        }

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
            MultiplayerLiveComplete = true;
            MultiplayerLiveStatus = $"DESYNC turn {MultiplayerLiveResult.DesyncTurn}: {MultiplayerLiveResult.DesyncReason}";
            return false;
        }

        MultiplayerLiveTurn++;
        MultiplayerLiveStatus = $"turn {MultiplayerLiveTurn} hash {MultiplayerLiveResult.FinalHash}";
        return true;
    }

    void OnMultiplayerHostMessage(LockstepMessage message)
    {
        if (message is ChecksumMessage c && c.FromPeer == ArenaMultiplayerSession.JoinPlayerPeerId)
            MultiplayerRemoteChecksumTick = Math.Max(MultiplayerRemoteChecksumTick, c.Tick);
        if (message is SubmitCommandMessage s)
        {
            if (!MultiplayerSubmittedInputs.TryGetValue(s.Command.Tick, out HashSet<int> peers))
            {
                peers = new HashSet<int>();
                MultiplayerSubmittedInputs[s.Command.Tick] = peers;
            }
            peers.Add(s.Command.PlayerId);
        }
    }

    void OnMultiplayerJoinMessage(LockstepMessage message)
    {
        if (message is SessionControlMessage c)
        {
            MultiplayerLivePaused = c.Paused;
            MultiplayerLiveSpeed = ArenaMultiplayerSettings.ClampGameSpeed(c.GameSpeed);
            UState.GameSpeed = MultiplayerLiveSpeed;
        }
        if (message is SessionErrorMessage e)
        {
            MultiplayerLiveComplete = true;
            MultiplayerLiveStatus = e.Error;
        }
    }

    void ToggleMultiplayerPause()
    {
        if (MultiplayerLiveSession?.Role != ArenaMultiplayerRole.Host || MultiplayerLiveComplete)
            return;
        MultiplayerLivePaused = !MultiplayerLivePaused;
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

    void RecordMultiplayerLiveTurn(uint turn, (ulong lo, ulong hi) hash, DesyncDetector desync)
    {
        MultiplayerLiveResult.TurnHashes.Add(new ArenaMultiplayerTurnHash(turn, hash, hash));
        MultiplayerLiveResult.FinalHash = MultiplayerHashText(hash);
        if (desync != null && desync.HasDesync)
        {
            MultiplayerLiveResult.Desynced = true;
            MultiplayerLiveResult.DesyncTurn = desync.FirstDivergentTick;
            MultiplayerLiveResult.DesyncReason = ArenaMultiplayerSession.DesyncSummary(desync);
            Log.Warning($"Arena MP DESYNC live role={MultiplayerLiveSession.Role} turn={turn}: "
                        + MultiplayerLiveResult.DesyncReason);
        }
    }

    void CompleteMultiplayerLive(string reason)
    {
        MultiplayerLiveComplete = true;
        MultiplayerLiveStatus = $"COMPLETE {reason}\nturns {MultiplayerLiveResult.TurnsCompleted}\nfinal {MultiplayerLiveResult.FinalHash}";
        Log.Warning($"Arena MP COMPLETE role={MultiplayerLiveSession.Role} reason='{reason}' "
                    + $"turns={MultiplayerLiveResult.TurnsCompleted} final={MultiplayerLiveResult.FinalHash}");
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
        MultiplayerLiveSession?.Dispose();
        MultiplayerLiveSession = null;
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

    static void SpawnMultiplayerFleet(List<Ship> ships, Empire owner, IShipDesign[] designs, float x, Vector2 facing)
    {
        Vector2 center = ArenaCenter;
        for (int i = 0; i < designs.Length; ++i)
        {
            float y = (i - (designs.Length - 1) / 2f) * RowSpan;
            Ship ship = CreateArenaShipAtPoint(owner.Universe, designs[i].Name, owner, center + new Vector2(x, y), facing);
            if (ship == null)
                throw new InvalidOperationException($"Failed to spawn Arena PvP ship '{designs[i].Name}'.");
            ship.SensorRange = 400000f;
            ships.Add(ship);
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using SDLockstep;
using SDUtils.Deterministic;
using Ship_Game.Determinism;
using Ship_Game.Determinism.Lockstep;
using Ship_Game.Ships;

namespace Ship_Game.GameScreens.Arena;

public enum ArenaMultiplayerRole
{
    Host = 0,
    Join = 1,
}

public sealed class ArenaMultiplayerSettings
{
    public const int ProtocolVersion = 1;
    const char FleetSeparator = '\u001f';

    public int MatchSeed = 0x5EED;
    public uint RngSeed = 0xA12EA000u;
    public int InputDelay = 3;
    public int MaxTurns = 420;
    public int CommandEveryTurns = 1;
    public string PlayerPreference = "United";
    public string[] HostFleetDesignNames = Array.Empty<string>();
    public string[] JoinFleetDesignNames = Array.Empty<string>();

    public string SettingsHash
    {
        get
        {
            var h = DetHash.New();
            h.AddInt(ProtocolVersion);
            h.AddInt(MatchSeed);
            h.AddUInt(RngSeed);
            h.AddInt(InputDelay);
            h.AddInt(MaxTurns);
            h.AddInt(CommandEveryTurns);
            h.AddString(PlayerPreference);
            AddFleet(ref h, HostFleetDesignNames);
            AddFleet(ref h, JoinFleetDesignNames);
            return "0x" + h.Value.ToString("X16", CultureInfo.InvariantCulture);
        }
    }

    public SessionStartMessage ToStartMessage(int fromPeer = LockstepHost.HostPeerId)
        => new()
        {
            FromPeer = fromPeer,
            ProtocolVersion = ProtocolVersion,
            MatchSeed = MatchSeed,
            RngSeed = RngSeed,
            InputDelay = InputDelay,
            MaxTurns = MaxTurns,
            SettingsHash = SettingsHash,
            BuildHash = ArenaMultiplayerPeerSignature.Hash(this),
            BuildSummary = ArenaMultiplayerPeerSignature.Summary(this),
            HostFleet = EncodeFleet(HostFleetDesignNames),
            JoinFleet = EncodeFleet(JoinFleetDesignNames),
        };

    public static ArenaMultiplayerSettings FromStartMessage(SessionStartMessage message)
        => new()
        {
            MatchSeed = message.MatchSeed,
            RngSeed = message.RngSeed,
            InputDelay = Math.Max(0, message.InputDelay),
            MaxTurns = Math.Max(1, message.MaxTurns),
            CommandEveryTurns = 1,
            PlayerPreference = "United",
            HostFleetDesignNames = DecodeFleet(message.HostFleet),
            JoinFleetDesignNames = DecodeFleet(message.JoinFleet),
        };

    public ArenaMultiplayerSettings WithResolvedFleets()
    {
        var copy = new ArenaMultiplayerSettings
        {
            MatchSeed = MatchSeed,
            RngSeed = RngSeed,
            InputDelay = Math.Max(0, InputDelay),
            MaxTurns = Math.Max(1, MaxTurns),
            CommandEveryTurns = Math.Max(1, CommandEveryTurns),
            PlayerPreference = PlayerPreference.NotEmpty() ? PlayerPreference : "United",
            HostFleetDesignNames = NormalizeFleet(HostFleetDesignNames),
            JoinFleetDesignNames = NormalizeFleet(JoinFleetDesignNames),
        };

        if (copy.HostFleetDesignNames.Length == 0)
            copy.HostFleetDesignNames = DefaultFleetForSeed((ulong)(uint)copy.MatchSeed ^ 0xA12E_0001ul);
        if (copy.JoinFleetDesignNames.Length == 0)
            copy.JoinFleetDesignNames = DefaultFleetForSeed((ulong)(uint)copy.MatchSeed ^ 0xA12E_0002ul);
        return copy;
    }

    public static string EncodeFleet(string[] names)
        => string.Join(FleetSeparator, NormalizeFleet(names));

    public static string[] DecodeFleet(string text)
        => string.IsNullOrEmpty(text)
            ? Array.Empty<string>()
            : NormalizeFleet(text.Split(FleetSeparator));

    public static string[] NormalizeFleet(string[] names)
        => (names ?? Array.Empty<string>())
            .Where(n => n.NotEmpty())
            .Select(n => n.Trim())
            .Where(n => n.NotEmpty())
            .Take(32)
            .ToArray();

    static string[] DefaultFleetForSeed(ulong seed)
    {
        IShipDesign[] designs = CareerManager.StartingRosterDesigns(ArenaStartArchetype.Wingmates, seed);
        if (designs == null || designs.Length == 0)
        {
            IShipDesign fallback = ArenaFightScreen.AutoPickPlayerWarship(null, careerLevel: 0);
            return fallback != null ? new[] { fallback.Name } : Array.Empty<string>();
        }
        return designs.Select(d => d.Name).ToArray();
    }

    static void AddFleet(ref DetHash hash, string[] names)
    {
        string[] normalized = NormalizeFleet(names);
        hash.AddInt(normalized.Length);
        for (int i = 0; i < normalized.Length; ++i)
            hash.AddString(normalized[i]);
    }
}

public static class ArenaMultiplayerPeerSignature
{
    public static string EnvironmentHash()
    {
        var h = DetHash.New();
        h.AddString(GlobalStats.ExtendedVersionNoHash);
        h.AddString(GlobalStats.ExtendedVersion);
        h.AddString(GlobalStats.ModName);
        h.AddString(GlobalStats.ModVersion);
        h.AddString(typeof(ArenaPlugin).Assembly.GetName().Version?.ToString() ?? "");
        h.AddString(typeof(LockstepHost).Assembly.GetName().Version?.ToString() ?? "");
        h.AddULong(BuildFingerprint.Compute(DeterminismProfile.MPSamePlatformPinnedFloat));
        return "0x" + h.Value.ToString("X16", CultureInfo.InvariantCulture);
    }

    public static string Hash(ArenaMultiplayerSettings settings)
    {
        settings ??= new ArenaMultiplayerSettings();
        var h = DetHash.New();
        h.AddString(EnvironmentHash());
        h.AddString(settings.SettingsHash);
        return "0x" + h.Value.ToString("X16", CultureInfo.InvariantCulture);
    }

    public static string EnvironmentSummary()
    {
        string game = GlobalStats.ExtendedVersionNoHash.NotEmpty()
            ? GlobalStats.ExtendedVersionNoHash
            : GlobalStats.ExtendedVersion.NotEmpty() ? GlobalStats.ExtendedVersion : "unknown-game";
        string mod = GlobalStats.HasMod ? $"{GlobalStats.ModName} {GlobalStats.ModVersion}" : "Vanilla";
        return $"{game}; {mod}; env {EnvironmentHash()}";
    }

    public static string Summary(ArenaMultiplayerSettings settings)
    {
        settings ??= new ArenaMultiplayerSettings();
        return $"{EnvironmentSummary()}; settings {settings.SettingsHash}; build {Hash(settings)}";
    }

    public static string ValidateEnvironment(string remoteHash, string remoteSummary, string remoteLabel)
    {
        string localHash = EnvironmentHash();
        if (remoteHash.IsEmpty())
            return $"{remoteLabel} did not send an Arena multiplayer environment fingerprint.";
        if (string.Equals(remoteHash, localHash, StringComparison.Ordinal))
            return "";

        string remote = remoteSummary.NotEmpty() ? remoteSummary : remoteHash;
        return "Arena multiplayer environment mismatch.\n"
               + $"Local {EnvironmentSummary()}\n"
               + $"{remoteLabel} {remote}";
    }

    public static string ValidateSession(string remoteHash, string remoteSummary,
        ArenaMultiplayerSettings settings, string remoteLabel)
    {
        string localHash = Hash(settings);
        if (remoteHash.IsEmpty())
            return $"{remoteLabel} did not send an Arena multiplayer session fingerprint.";
        if (string.Equals(remoteHash, localHash, StringComparison.Ordinal))
            return "";

        string remote = remoteSummary.NotEmpty() ? remoteSummary : remoteHash;
        return "Arena multiplayer session mismatch.\n"
               + $"Local {Summary(settings)}\n"
               + $"{remoteLabel} {remote}";
    }
}

public readonly struct ArenaMultiplayerTurnHash
{
    public readonly uint Turn;
    public readonly ulong HostLo;
    public readonly ulong HostHi;
    public readonly ulong JoinLo;
    public readonly ulong JoinHi;

    public ArenaMultiplayerTurnHash(uint turn, (ulong lo, ulong hi) host, (ulong lo, ulong hi) join)
    {
        Turn = turn;
        HostLo = host.lo;
        HostHi = host.hi;
        JoinLo = join.lo;
        JoinHi = join.hi;
    }

    public bool Match => HostLo == JoinLo && HostHi == JoinHi;
}

public sealed class ArenaMultiplayerRunResult
{
    public readonly List<ArenaMultiplayerTurnHash> TurnHashes = new();
    public bool Desynced;
    public long DesyncTurn = -1;
    public string DesyncReason = "";
    public bool MatchEnded;
    public int WinnerPeerId;
    public long MatchEndedTurn = -1;
    public string FinalHash = "";
    public ArenaMultiplayerShipSnapshot HostSnapshot;
    public ArenaMultiplayerShipSnapshot JoinSnapshot;
    public int CommandsSubmitted;
    public int TurnsCompleted => TurnHashes.Count;
}

/// <summary>
/// Phase-1, 2-player Arena lockstep session harness. Single-player Arena never calls this; it is
/// a separate multiplayer path that creates two deterministic Arena peers, exchanges canonical
/// command frames, and halts on checksum divergence.
/// </summary>
public static class ArenaMultiplayerSession
{
    public const int HostPlayerPeerId = 1;
    public const int JoinPlayerPeerId = 2;
    public const int DefaultPort = 47377;

    public static ArenaMultiplayerRunResult RunInProcess(ArenaMultiplayerSettings settings,
        int forceDesyncAfterTurn = -1)
    {
        settings = (settings ?? new ArenaMultiplayerSettings()).WithResolvedFleets();
        ArenaFightScreen hostScreen = BuildPeerScreen(settings);
        ArenaFightScreen joinScreen = BuildPeerScreen(settings);
        return RunTwoPeerLockstep(settings, hostScreen, joinScreen, new FakeTransport(), forceDesyncAfterTurn);
    }

    public static ArenaMultiplayerRunResult RunNetworkHost(ArenaMultiplayerSettings settings, int port,
        Action<string> log = null)
    {
        settings = (settings ?? new ArenaMultiplayerSettings()).WithResolvedFleets();
        using TcpLockstepTransport transport = TcpLockstepTransport.Host(port, JoinPlayerPeerId);
        log?.Invoke($"HOST listening on port {port}");
        if (!transport.WaitForConnection(TimeSpan.FromSeconds(60)))
            throw new TimeoutException("Timed out waiting for Arena multiplayer client.");

        int remoteReadyCount = 0;
        string handshakeError = "";
        transport.AddObserver(LockstepHost.HostPeerId, m =>
        {
            if (m is SessionHelloMessage h && h.PeerId == JoinPlayerPeerId)
            {
                if (h.ProtocolVersion != ArenaMultiplayerSettings.ProtocolVersion)
                    handshakeError = $"Arena multiplayer protocol mismatch. Local {ArenaMultiplayerSettings.ProtocolVersion}, remote {h.ProtocolVersion}.";
                else
                    handshakeError = ArenaMultiplayerPeerSignature.ValidateEnvironment(
                        h.BuildHash, h.BuildSummary, "remote");
            }
            if (m is SessionReadyMessage r && r.PeerId == JoinPlayerPeerId && r.Ready)
            {
                string readyError = ArenaMultiplayerPeerSignature.ValidateEnvironment(
                    r.BuildHash, r.BuildSummary, "remote");
                if (readyError.NotEmpty())
                    handshakeError = readyError;
                remoteReadyCount++;
            }
        });

        DateTime readyDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (remoteReadyCount < 1 && handshakeError.IsEmpty() && DateTime.UtcNow < readyDeadline)
        {
            transport.Poll();
            Thread.Sleep(5);
        }
        if (handshakeError.NotEmpty())
        {
            transport.Send(JoinPlayerPeerId,
                new SessionErrorMessage { FromPeer = LockstepHost.HostPeerId, Error = handshakeError });
            throw new InvalidOperationException(handshakeError);
        }
        if (remoteReadyCount < 1)
            throw new TimeoutException("Client connected but did not ready-up.");

        transport.Send(JoinPlayerPeerId, settings.ToStartMessage());
        ArenaFightScreen screen = BuildPeerScreen(settings);
        return RunHostNetworkLoop(settings, screen, transport, () => remoteReadyCount >= 2, log);
    }

    public static ArenaMultiplayerRunResult RunNetworkJoin(string host, int port,
        Action<string> log = null)
    {
        using TcpLockstepTransport transport = TcpLockstepTransport.Join(host, port, LockstepHost.HostPeerId);
        log?.Invoke($"JOIN connected to {host}:{port}");

        SessionStartMessage start = null;
        string sessionError = "";
        transport.AddObserver(JoinPlayerPeerId, m =>
        {
            if (m is SessionStartMessage s)
                start = s;
            if (m is SessionErrorMessage e)
                sessionError = e.Error;
        });
        transport.Send(LockstepHost.HostPeerId,
            new SessionHelloMessage
            {
                FromPeer = JoinPlayerPeerId,
                PeerId = JoinPlayerPeerId,
                ProtocolVersion = ArenaMultiplayerSettings.ProtocolVersion,
                PlayerName = "Arena Join",
                BuildHash = ArenaMultiplayerPeerSignature.EnvironmentHash(),
                BuildSummary = ArenaMultiplayerPeerSignature.EnvironmentSummary(),
            });
        transport.Send(LockstepHost.HostPeerId,
            new SessionReadyMessage
            {
                FromPeer = JoinPlayerPeerId,
                PeerId = JoinPlayerPeerId,
                Ready = true,
                BuildHash = ArenaMultiplayerPeerSignature.EnvironmentHash(),
                BuildSummary = ArenaMultiplayerPeerSignature.EnvironmentSummary(),
            });

        DateTime startDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (start == null && sessionError.IsEmpty() && DateTime.UtcNow < startDeadline)
        {
            transport.Poll();
            Thread.Sleep(5);
        }
        if (sessionError.NotEmpty())
            throw new InvalidOperationException(sessionError);
        if (start == null)
            throw new TimeoutException("Host did not send Arena multiplayer start settings.");

        ArenaMultiplayerSettings settings = ArenaMultiplayerSettings.FromStartMessage(start).WithResolvedFleets();
        if (start.ProtocolVersion != ArenaMultiplayerSettings.ProtocolVersion)
            throw new InvalidOperationException(
                $"Arena multiplayer protocol mismatch. Local {ArenaMultiplayerSettings.ProtocolVersion}, host {start.ProtocolVersion}.");
        if (!string.Equals(start.SettingsHash, settings.SettingsHash, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Arena multiplayer settings mismatch. Host {start.SettingsHash}, local {settings.SettingsHash}.");
        string buildError = ArenaMultiplayerPeerSignature.ValidateSession(
            start.BuildHash, start.BuildSummary, settings, "host");
        if (buildError.NotEmpty())
            throw new InvalidOperationException(buildError);

        ArenaFightScreen screen = BuildPeerScreen(settings);
        return RunJoinNetworkLoop(settings, screen, transport, log);
    }

    static ArenaFightScreen BuildPeerScreen(ArenaMultiplayerSettings settings)
    {
        settings = (settings ?? new ArenaMultiplayerSettings()).WithResolvedFleets();
        ArenaFightScreen screen = ArenaFightScreen.Create(settings.PlayerPreference, settings.MatchSeed, startAtHub: false);
        screen.ConfigureMultiplayerPvP(settings);
        screen.CreateSimThread = false;
        screen.UState.Objects.EnableParallelUpdate = false;
        ArenaEngineCapabilities.TryEnableSeededRng(screen.UState, settings.RngSeed);
        screen.LoadContent();
        screen.PrepareForMultiplayerLockstep(settings.RngSeed);
        return screen;
    }

    static ArenaMultiplayerRunResult RunTwoPeerLockstep(ArenaMultiplayerSettings settings,
        ArenaFightScreen hostScreen, ArenaFightScreen joinScreen, FakeTransport transport,
        int forceDesyncAfterTurn)
    {
        var host = new LockstepHost(transport);
        var hostSim = hostScreen.CreateMultiplayerLockstepSimulation();
        var joinSim = joinScreen.CreateMultiplayerLockstepSimulation();
        var hostClient = new LockstepClient(transport, HostPlayerPeerId, hostSim);
        var joinClient = new LockstepClient(transport, JoinPlayerPeerId, joinSim);
        host.AddClient(HostPlayerPeerId);
        host.AddClient(JoinPlayerPeerId);

        var result = NewResult(hostScreen, joinScreen);
        ValidateSnapshots(result);

        for (uint turn = 0; turn < settings.MaxTurns; ++turn)
        {
            SubmitTurnCommands(settings, turn, hostClient, joinClient, hostScreen, ref result.CommandsSubmitted);
            transport.Poll();
            host.CommitTick(turn);
            transport.Poll();
            hostClient.Pump();
            joinClient.Pump();
            if (forceDesyncAfterTurn >= 0 && turn == forceDesyncAfterTurn)
                joinScreen.ForceMultiplayerDesyncForTest();
            transport.Poll();

            RecordTurn(result, turn, hostSim.Hash(), joinSim.Hash(), host.Desync);
            UpdateMatchOutcome(result, turn, hostScreen.MultiplayerMatchStatus(), hostSim.Hash());
            if (result.Desynced)
                break;
            if (result.MatchEnded)
                break;
        }

        return result;
    }

    static ArenaMultiplayerRunResult RunHostNetworkLoop(ArenaMultiplayerSettings settings,
        ArenaFightScreen screen, TcpLockstepTransport transport, Func<bool> remoteArmed, Action<string> log)
    {
        long remoteChecksumTick = -1;
        var submittedInputs = new Dictionary<uint, HashSet<int>>();
        transport.AddObserver(LockstepHost.HostPeerId, m =>
        {
            if (m is ChecksumMessage c && c.FromPeer == JoinPlayerPeerId)
                remoteChecksumTick = Math.Max(remoteChecksumTick, c.Tick);
            if (m is SubmitCommandMessage s)
            {
                if (!submittedInputs.TryGetValue(s.Command.Tick, out HashSet<int> peers))
                {
                    peers = new HashSet<int>();
                    submittedInputs[s.Command.Tick] = peers;
                }
                peers.Add(s.Command.PlayerId);
            }
        });

        var host = new LockstepHost(transport);
        var sim = screen.CreateMultiplayerLockstepSimulation();
        var client = new LockstepClient(transport, HostPlayerPeerId, sim);
        host.AddClient(HostPlayerPeerId);
        host.AddClient(JoinPlayerPeerId);
        WaitFor(remoteArmed, transport, TimeSpan.FromSeconds(30),
            "client received Arena settings but did not arm the simulation");

        var result = NewResult(screen, screen);
        for (uint turn = 0; turn < settings.MaxTurns; ++turn)
        {
            if (ShouldSubmit(settings, turn))
            {
                client.Submit(screen.BuildMultiplayerFocusCommand(HostPlayerPeerId, turn + (uint)settings.InputDelay, turn));
                result.CommandsSubmitted++;
            }
            transport.Poll();
            if (ShouldHaveSubmittedForExecTick(settings, turn))
            {
                WaitFor(() => HasBothInputs(submittedInputs, turn), transport, TimeSpan.FromSeconds(10),
                    $"both peers did not submit input for turn {turn}");
            }
            host.CommitTick(turn);
            transport.Poll();
            client.Pump();
            transport.Poll();
            WaitFor(() => sim.Tick > turn, transport, TimeSpan.FromSeconds(5),
                $"host local sim did not apply turn {turn}");
            WaitFor(() => remoteChecksumTick >= turn || host.Desync.HasDesync, transport, TimeSpan.FromSeconds(10),
                $"remote peer did not report checksum for turn {turn}");
            RecordSinglePeerTurn(result, turn, sim.Hash(), host.Desync);
            UpdateMatchOutcome(result, turn, screen.MultiplayerMatchStatus(), sim.Hash());
            if (result.Desynced)
            {
                log?.Invoke($"DESYNC at turn {result.DesyncTurn}: {result.DesyncReason}");
                break;
            }
            if (result.MatchEnded)
                break;
        }
        log?.Invoke($"HOST completed turns={result.TurnsCompleted} desynced={result.Desynced}");
        return result;
    }

    static ArenaMultiplayerRunResult RunJoinNetworkLoop(ArenaMultiplayerSettings settings,
        ArenaFightScreen screen, TcpLockstepTransport transport, Action<string> log)
    {
        var sim = screen.CreateMultiplayerLockstepSimulation();
        var client = new LockstepClient(transport, JoinPlayerPeerId, sim);
        transport.Send(LockstepHost.HostPeerId,
            new SessionReadyMessage
            {
                FromPeer = JoinPlayerPeerId,
                PeerId = JoinPlayerPeerId,
                Ready = true,
                BuildHash = ArenaMultiplayerPeerSignature.EnvironmentHash(),
                BuildSummary = ArenaMultiplayerPeerSignature.EnvironmentSummary(),
            });
        var result = NewResult(screen, screen);

        for (uint turn = 0; turn < settings.MaxTurns; ++turn)
        {
            if (ShouldSubmit(settings, turn))
            {
                client.Submit(screen.BuildMultiplayerFocusCommand(JoinPlayerPeerId, turn + (uint)settings.InputDelay, turn));
                result.CommandsSubmitted++;
            }
            transport.Poll();
            client.Pump();
            transport.Poll();
            WaitForClientTick(client, sim, transport, turn, TimeSpan.FromSeconds(10),
                $"join sim did not receive/apply turn {turn}");
            RecordSinglePeerTurn(result, turn, sim.Hash(), null);
            UpdateMatchOutcome(result, turn, screen.MultiplayerMatchStatus(), sim.Hash());
            if (result.MatchEnded)
                break;
            Thread.Sleep(1);
        }
        log?.Invoke($"JOIN completed turns={result.TurnsCompleted}");
        return result;
    }

    static ArenaMultiplayerRunResult NewResult(ArenaFightScreen hostScreen, ArenaFightScreen joinScreen)
        => new()
        {
            HostSnapshot = hostScreen.MultiplayerSnapshot(),
            JoinSnapshot = joinScreen.MultiplayerSnapshot(),
        };

    static void ValidateSnapshots(ArenaMultiplayerRunResult result)
    {
        if (!SameIds(result.HostSnapshot.PlayerShipIds, result.JoinSnapshot.PlayerShipIds)
            || !SameIds(result.HostSnapshot.EnemyShipIds, result.JoinSnapshot.EnemyShipIds))
            throw new InvalidOperationException("Arena multiplayer peers did not spawn identical stable ship IDs.");
        if (!SameStrings(result.HostSnapshot.PlayerFleetDesigns, result.JoinSnapshot.PlayerFleetDesigns)
            || !SameStrings(result.HostSnapshot.EnemyFleetDesigns, result.JoinSnapshot.EnemyFleetDesigns))
            throw new InvalidOperationException("Arena multiplayer peers did not spawn identical fleet manifests.");
    }

    static bool SameIds(int[] a, int[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; ++i)
            if (a[i] != b[i])
                return false;
        return true;
    }

    static void SubmitTurnCommands(ArenaMultiplayerSettings settings, uint turn,
        LockstepClient hostClient, LockstepClient joinClient, ArenaFightScreen commandSource,
        ref int commandsSubmitted)
    {
        if (!ShouldSubmit(settings, turn))
            return;

        uint execTick = turn + (uint)Math.Max(0, settings.InputDelay);
        hostClient.Submit(commandSource.BuildMultiplayerFocusCommand(HostPlayerPeerId, execTick, turn));
        joinClient.Submit(commandSource.BuildMultiplayerFocusCommand(JoinPlayerPeerId, execTick, turn));
        commandsSubmitted += 2;
    }

    static bool ShouldSubmit(ArenaMultiplayerSettings settings, uint turn)
        => settings.CommandEveryTurns <= 1 || turn % (uint)settings.CommandEveryTurns == 0;

    static bool ShouldHaveSubmittedForExecTick(ArenaMultiplayerSettings settings, uint turn)
    {
        uint delay = (uint)Math.Max(0, settings.InputDelay);
        if (turn < delay)
            return false;
        return ShouldSubmit(settings, turn - delay);
    }

    static bool HasBothInputs(Dictionary<uint, HashSet<int>> submittedInputs, uint turn)
        => submittedInputs.TryGetValue(turn, out HashSet<int> peers)
           && peers.Contains(HostPlayerPeerId)
           && peers.Contains(JoinPlayerPeerId);

    static void RecordTurn(ArenaMultiplayerRunResult result, uint turn,
        (ulong lo, ulong hi) hostHash, (ulong lo, ulong hi) joinHash, DesyncDetector desync)
    {
        result.TurnHashes.Add(new ArenaMultiplayerTurnHash(turn, hostHash, joinHash));
        result.FinalHash = HashText(hostHash);
        if (desync.HasDesync || hostHash != joinHash)
        {
            result.Desynced = true;
            result.DesyncTurn = desync.HasDesync ? desync.FirstDivergentTick : turn;
            result.DesyncReason = desync.HasDesync
                ? $"peer {desync.DivergentPeer} diverged"
                : "local hash comparison diverged";
        }
    }

    static void RecordSinglePeerTurn(ArenaMultiplayerRunResult result, uint turn,
        (ulong lo, ulong hi) hash, DesyncDetector desync)
    {
        result.TurnHashes.Add(new ArenaMultiplayerTurnHash(turn, hash, hash));
        result.FinalHash = HashText(hash);
        if (desync != null && desync.HasDesync)
        {
            result.Desynced = true;
            result.DesyncTurn = desync.FirstDivergentTick;
            result.DesyncReason = $"peer {desync.DivergentPeer} diverged";
        }
    }

    static void UpdateMatchOutcome(ArenaMultiplayerRunResult result, uint turn,
        ArenaMultiplayerMatchStatus status, (ulong lo, ulong hi) hash)
    {
        result.FinalHash = HashText(hash);
        if (!status.Ended || result.MatchEnded)
            return;
        result.MatchEnded = true;
        result.MatchEndedTurn = turn;
        result.WinnerPeerId = status.WinnerPeerId;
    }

    static string HashText((ulong lo, ulong hi) hash)
        => $"0x{hash.hi:X16}:0x{hash.lo:X16}";

    static bool SameStrings(string[] a, string[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; ++i)
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                return false;
        return true;
    }

    static void WaitFor(Func<bool> done, ILockstepTransport transport, TimeSpan timeout, string error)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!done() && DateTime.UtcNow < deadline)
        {
            transport.Poll();
            Thread.Sleep(1);
        }
        if (!done())
            throw new TimeoutException(error);
    }

    static void WaitForClientTick(LockstepClient client, UniverseStateLockstepSimulation sim,
        ILockstepTransport transport, uint turn, TimeSpan timeout, string error)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (sim.Tick <= turn && DateTime.UtcNow < deadline)
        {
            transport.Poll();
            client.Pump();
            transport.Poll();
            Thread.Sleep(1);
        }
        if (sim.Tick <= turn)
            throw new TimeoutException(error);
    }
}

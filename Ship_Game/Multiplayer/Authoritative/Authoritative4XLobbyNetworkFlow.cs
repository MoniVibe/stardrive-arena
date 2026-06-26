using System;
using System.Linq;
using System.Threading;
using SDLockstep;

namespace Ship_Game.Multiplayer.Authoritative;

public sealed class Authoritative4XLobbySelfTestResult
{
    public bool Passed;
    public string FailureReason = "";
    public string SettingsHash = "";
    public string AuthorityDigest = "";
    public string ClientDigest = "";
    public string FinalHash = "";
    public int MaxTurns;
    public int Port;
    public int HostPeerId;
    public int JoinPeerId;
    public int HostEmpireId;
    public int JoinEmpireId;
    public int CommandSequence;
    public uint CommandTick;
    public int CommandPlanetId;
    public bool CommandSubmitted;
    public bool CommandAccepted;
    public Planet.ColonyType CommandColonyType;

    public bool SnapshotsSynced
        => Passed
           && AuthorityDigest.NotEmpty()
           && string.Equals(AuthorityDigest, ClientDigest, StringComparison.Ordinal);

    public string Summary
        => Passed
            ? $"4X LOOPBACK OK tick {CommandTick} seq {CommandSequence}\n"
              + $"snapshot {FinalHash}\nsettings {SettingsHash}"
            : $"4X LOOPBACK FAILED\n{FailureReason}\nsettings {SettingsHash}";
}

/// <summary>
/// Protocol helper for the authoritative 4X lobby. It keeps the host/join/start
/// handoff out of the Arena UI so the real network contract can be tested headlessly.
/// </summary>
public sealed class Authoritative4XLobbyNetworkFlow
{
    public readonly int HostPeerId;
    public readonly int JoinPeerId;
    public readonly int AuthorityPeerId;

    public Authoritative4XLobbyNetworkFlow(int hostPeerId = 2, int joinPeerId = 3,
        int authorityPeerId = Authoritative4XLobby.AuthorityPeerId)
    {
        if (hostPeerId == authorityPeerId || joinPeerId == authorityPeerId || hostPeerId == joinPeerId)
            throw new ArgumentException("Authoritative 4X lobby peers must be distinct and cannot use the authority peer id.");
        HostPeerId = hostPeerId;
        JoinPeerId = joinPeerId;
        AuthorityPeerId = authorityPeerId;
    }

    public SessionLobbyMessage BuildLobbyMessage(Authoritative4XLobby lobby, int peerId,
        string buildHash = "", string buildSummary = "")
    {
        Authoritative4XLobbyPlayer player = lobby?.Roster.FirstOrDefault(p => p.PeerId == peerId)
            ?? throw new ArgumentException($"Peer {peerId} is not in the lobby.", nameof(peerId));
        return new SessionLobbyMessage
        {
            FromPeer = peerId,
            PeerId = peerId,
            Ready = player.Ready,
            PlayerName = player.PlayerName,
            RacePreference = player.RaceName,
            TraitOptions = string.Join('|', player.TraitOptions ?? Array.Empty<string>()),
            BuildHash = buildHash ?? "",
            BuildSummary = buildSummary ?? "",
        };
    }

    public Authoritative4XLobbyValidation ApplyLobbyMessage(Authoritative4XLobby lobby,
        SessionLobbyMessage message)
    {
        if (lobby == null)
            return Authoritative4XLobbyValidation.Fail("Lobby was missing.");
        if (message == null)
            return Authoritative4XLobbyValidation.Fail("Lobby message was missing.");
        if (message.PeerId == AuthorityPeerId)
            return Authoritative4XLobbyValidation.Fail("Authority peer cannot join as a player.");

        lobby.Join(message.PeerId, message.PlayerName);
        Authoritative4XLobbyValidation selection = lobby.SetPlayerSelection(message.PeerId,
            message.RacePreference, SplitTraits(message.TraitOptions));
        if (!selection.Valid)
            return selection;
        return lobby.SetReady(message.PeerId, message.Ready);
    }

    public SessionStartMessage BuildStartMessage(Authoritative4XLobby lobby, int protocolVersion,
        string buildHash = "", string buildSummary = "", int maxTurns = 0)
    {
        if (lobby == null)
            throw new ArgumentNullException(nameof(lobby));
        Authoritative4XLobbyValidation canStart = lobby.CanStart();
        if (!canStart.Valid)
            throw new InvalidOperationException(canStart.Reason);

        Authoritative4XLobbyPlayer host = FindPlayer(lobby, HostPeerId);
        Authoritative4XLobbyPlayer join = FindPlayer(lobby, JoinPeerId);
        Authoritative4XGameSettings settings = lobby.Settings.Normalized(2);
        return new SessionStartMessage
        {
            FromPeer = AuthorityPeerId,
            ProtocolVersion = protocolVersion,
            MatchSeed = settings.GenerationSeed,
            RngSeed = (uint)settings.GenerationSeed ^ 0x4D505547u,
            InputDelay = 0,
            MaxTurns = maxTurns,
            CommandEveryTurns = 1,
            GameSpeed = settings.GameSpeed,
            StartPaused = settings.StartPaused,
            SettingsHash = settings.SettingsHash,
            BuildHash = buildHash ?? "",
            BuildSummary = buildSummary ?? "",
            HostRacePreference = host.RaceName,
            JoinRacePreference = join.RaceName,
            HostTraitOptions = string.Join('|', host.TraitOptions ?? Array.Empty<string>()),
            JoinTraitOptions = string.Join('|', join.TraitOptions ?? Array.Empty<string>()),
            IsAuthoritative4X = true,
            AuthoritativeHostPeerId = HostPeerId,
            AuthoritativeJoinPeerId = JoinPeerId,
            GenerationSeed = settings.GenerationSeed,
            GalaxySize = (int)settings.GalaxySize,
            StarsCount = (int)settings.StarsCount,
            GameMode = (int)settings.Mode,
            Difficulty = (int)settings.Difficulty,
            NumOpponents = settings.NumOpponents,
            Pace = settings.Pace,
            TurnTimer = settings.TurnTimer,
            ExtraPlanets = settings.ExtraPlanets,
            StartingPlanetRichnessBonus = settings.StartingPlanetRichnessBonus,
        };
    }

    public string ValidateStartMessage(SessionStartMessage start, int expectedProtocolVersion,
        string expectedBuildHash = "")
    {
        if (start == null)
            return "Host sent no session start.";
        if (!start.IsAuthoritative4X)
            return "Host sent a non-4X session start.";
        if (start.ProtocolVersion != expectedProtocolVersion)
            return $"Authoritative 4X protocol mismatch. Local {expectedProtocolVersion}, host {start.ProtocolVersion}.";
        if (!string.IsNullOrEmpty(expectedBuildHash)
            && !string.Equals(start.BuildHash, expectedBuildHash, StringComparison.Ordinal))
        {
            return $"Authoritative 4X build mismatch. Local {expectedBuildHash}, host {start.BuildHash}.";
        }
        if (start.AuthoritativeHostPeerId != HostPeerId || start.AuthoritativeJoinPeerId != JoinPeerId)
            return $"Authoritative peer mismatch. Host {start.AuthoritativeHostPeerId}, join {start.AuthoritativeJoinPeerId}.";

        Authoritative4XGameSettings settings = SettingsFromStart(start).Normalized(2);
        return string.Equals(start.SettingsHash, settings.SettingsHash, StringComparison.Ordinal)
            ? ""
            : $"Authoritative 4X settings mismatch. Host {start.SettingsHash}, local {settings.SettingsHash}.";
    }

    public Authoritative4XGeneratedGameStart CreateGeneratedGame(SessionStartMessage start)
    {
        string error = ValidateStartMessage(start, start?.ProtocolVersion ?? 0, start?.BuildHash ?? "");
        if (!string.IsNullOrEmpty(error))
            throw new InvalidOperationException(error);

        Authoritative4XGameSettings settings = SettingsFromStart(start).Normalized(2);
        var lobby = new Authoritative4XLobby(HostPeerId, "Host");
        lobby.Join(JoinPeerId, "Join");
        Authoritative4XLobbyValidation set = lobby.SetSettings(HostPeerId, settings);
        if (!set.Valid)
            throw new InvalidOperationException(set.Reason);
        Authoritative4XLobbyValidation host = lobby.SetPlayerSelection(HostPeerId,
            start.HostRacePreference, SplitTraits(start.HostTraitOptions));
        if (!host.Valid)
            throw new InvalidOperationException(host.Reason);
        Authoritative4XLobbyValidation join = lobby.SetPlayerSelection(JoinPeerId,
            start.JoinRacePreference, SplitTraits(start.JoinTraitOptions));
        if (!join.Valid)
            throw new InvalidOperationException(join.Reason);
        lobby.SetReady(HostPeerId, true);
        lobby.SetReady(JoinPeerId, true);
        return lobby.StartGeneratedGame();
    }

    public Authoritative4XLiveSession AttachLiveSession(Authoritative4XGeneratedGameStart generated,
        TcpLockstepTransport transport, int localPeerId, Authoritative4XLiveRole role)
    {
        if (generated == null)
            throw new ArgumentNullException(nameof(generated));
        if (role == Authoritative4XLiveRole.Host)
        {
            Authoritative4XLiveSession live = Authoritative4XLiveSession.HostGame(generated.AuthorityUniverse,
                transport, localPeerId, generated.EmpireIdByPeer, generated.HumanEmpireIds);
            generated.AuthorityUniverse.AttachAuthoritative4XMultiplayer(live);
            return live;
        }

        Authoritative4XLiveSession client = Authoritative4XLiveSession.ClientGame(generated.AuthorityUniverse,
            transport, localPeerId, generated.EmpireIdForPeer(localPeerId), generated.HumanEmpireIds);
        generated.AuthorityUniverse.AttachAuthoritative4XMultiplayer(client);
        return client;
    }

    public Authoritative4XLobbySelfTestResult RunLoopbackSelfTest(Authoritative4XGameSettings settings,
        string hostRace, string[] hostTraits, string joinRace, string[] joinTraits,
        int protocolVersion, string buildHash = "", string buildSummary = "", int maxTurns = 0,
        int port = 0)
    {
        var result = new Authoritative4XLobbySelfTestResult
        {
            MaxTurns = maxTurns,
            Port = port,
            HostPeerId = HostPeerId,
            JoinPeerId = JoinPeerId,
        };

        TcpLockstepTransport hostTransport = null;
        TcpLockstepTransport joinTransport = null;
        Authoritative4XGeneratedGameStart hostGenerated = null;
        Authoritative4XGeneratedGameStart joinGenerated = null;
        Authoritative4XLiveSession liveHost = null;
        Authoritative4XLiveSession liveJoin = null;
        string step = "setup";

        try
        {
            step = "build lobby";
            settings = (settings ?? new Authoritative4XGameSettings()).Normalized(2);
            result.SettingsHash = settings.SettingsHash;
            if (port <= 0)
            {
                port = FreeTcpPort();
                result.Port = port;
            }
            var hostLobby = new Authoritative4XLobby(HostPeerId, "Host");
            hostLobby.Join(JoinPeerId, "Join");
            RequireValid(hostLobby.SetSettings(HostPeerId, settings), step);
            RequireValid(hostLobby.SetPlayerSelection(HostPeerId, hostRace, hostTraits), step);
            RequireValid(hostLobby.SetReady(HostPeerId, true), step);

            var joinLobby = new Authoritative4XLobby(JoinPeerId, "Join");
            RequireValid(joinLobby.SetPlayerSelection(JoinPeerId, joinRace, joinTraits), step);
            RequireValid(joinLobby.SetReady(JoinPeerId, true), step);

            step = "connect lobby TCP";
            hostTransport = TcpLockstepTransport.HostMulti(port);
            joinTransport = TcpLockstepTransport.JoinAsPeer("127.0.0.1", port, JoinPeerId, AuthorityPeerId);
            if (!hostTransport.WaitForConnections(1, TimeSpan.FromSeconds(3)))
                throw new TimeoutException("Lobby TCP host did not accept the joiner.");

            SessionLobbyMessage receivedJoinLobby = null;
            SessionStartMessage receivedStart = null;
            hostTransport.Register(AuthorityPeerId, message =>
            {
                if (message is SessionLobbyMessage lobby)
                    receivedJoinLobby = lobby;
            });
            joinTransport.Register(JoinPeerId, message =>
            {
                if (message is SessionStartMessage start)
                    receivedStart = start;
            });

            step = "exchange lobby payload";
            SessionLobbyMessage joinMessage = BuildLobbyMessage(joinLobby, JoinPeerId,
                buildHash, buildSummary);
            joinTransport.Send(AuthorityPeerId, joinMessage);
            PumpTransportUntil(() => receivedJoinLobby != null, hostTransport, joinTransport);
            RequireValid(ApplyLobbyMessage(hostLobby, receivedJoinLobby), step);

            step = "build start payload";
            SessionStartMessage start = BuildStartMessage(hostLobby, protocolVersion,
                buildHash, buildSummary, maxTurns);
            result.SettingsHash = start.SettingsHash;
            hostTransport.Send(JoinPeerId, start);
            PumpTransportUntil(() => receivedStart != null, hostTransport, joinTransport);
            string startError = ValidateStartMessage(receivedStart, protocolVersion, buildHash);
            if (startError.NotEmpty())
                throw new InvalidOperationException(startError);

            step = "generate games";
            hostGenerated = CreateGeneratedGame(start);
            joinGenerated = CreateGeneratedGame(receivedStart);
            result.HostEmpireId = hostGenerated.EmpireIdForPeer(HostPeerId);
            result.JoinEmpireId = hostGenerated.EmpireIdForPeer(JoinPeerId);

            step = "attach live sessions";
            liveHost = AttachLiveSession(hostGenerated, hostTransport, HostPeerId,
                Authoritative4XLiveRole.Host);
            liveJoin = AttachLiveSession(joinGenerated, joinTransport, JoinPeerId,
                Authoritative4XLiveRole.Client);

            step = "stabilize live sessions";
            if (!hostGenerated.AuthorityUniverse.UState.Paused)
            {
                liveHost.TryApplyHostControl(paused: true, hostGenerated.AuthorityUniverse.UState.GameSpeed);
                PumpLiveUntil(() => joinGenerated.AuthorityUniverse.UState.Paused, liveHost, liveJoin);
            }

            step = "submit UI command";
            Planet clientPlanet = FirstPlanetForPeer(joinGenerated, JoinPeerId);
            Planet authorityPlanet = hostGenerated.AuthorityUniverse.UState.GetPlanet(clientPlanet.Id);
            if (authorityPlanet == null)
                throw new InvalidOperationException($"Authority game cannot resolve planet {clientPlanet.Id}.");

            Planet.ColonyType targetType = clientPlanet.CType == Planet.ColonyType.Research
                ? Planet.ColonyType.Military
                : Planet.ColonyType.Research;
            result.CommandPlanetId = clientPlanet.Id;
            result.CommandColonyType = targetType;
            result.CommandSubmitted = Authoritative4XClientContext.TrySubmitSetColonyType(clientPlanet, targetType);
            if (!result.CommandSubmitted)
                throw new InvalidOperationException("Client UI command context rejected SetColonyType.");

            PumpLiveUntil(() => liveJoin.LastResult?.OriginPeer == JoinPeerId
                                && liveJoin.LastResult.Sequence == 1
                                && liveJoin.LastSnapshot != null
                                && liveHost.LastSnapshot != null,
                liveHost, liveJoin);

            result.CommandSequence = liveJoin.LastResult.Sequence;
            result.CommandTick = liveJoin.LastResult.Tick;
            result.CommandAccepted = liveJoin.LastResult.Accepted;
            if (!result.CommandAccepted)
                throw new InvalidOperationException(liveJoin.LastResult.Reason);
            if (authorityPlanet.CType != targetType || clientPlanet.CType != targetType)
            {
                throw new InvalidOperationException(
                    $"UI command did not apply on both games. authority={authorityPlanet.CType} client={clientPlanet.CType}");
            }

            AuthoritativeStateSnapshot authoritySnapshot = liveHost.LastSnapshot;
            AuthoritativeStateSnapshot clientSnapshot = liveJoin.LastSnapshot;
            result.AuthorityDigest = authoritySnapshot.SyncDigest;
            result.ClientDigest = clientSnapshot.SyncDigest;
            result.FinalHash = SnapshotHash(authoritySnapshot);
            if (authoritySnapshot.HashLo != clientSnapshot.HashLo
                || authoritySnapshot.HashHi != clientSnapshot.HashHi
                || !string.Equals(authoritySnapshot.SyncDigest, clientSnapshot.SyncDigest, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Snapshot mismatch. authority={SnapshotHash(authoritySnapshot)} client={SnapshotHash(clientSnapshot)}");
            }

            result.Passed = true;
            return result;
        }
        catch (Exception e)
        {
            result.Passed = false;
            result.FailureReason = $"{step}: {e.Message}";
            return result;
        }
        finally
        {
            liveJoin?.Dispose();
            liveHost?.Dispose();
            joinGenerated?.Dispose();
            hostGenerated?.Dispose();
            joinTransport?.Dispose();
            hostTransport?.Dispose();
        }
    }

    public static Authoritative4XGameSettings SettingsFromStart(SessionStartMessage start)
        => new()
        {
            GenerationSeed = start.GenerationSeed,
            Mode = (RaceDesignScreen.GameMode)start.GameMode,
            StarsCount = (RaceDesignScreen.StarsAbundance)start.StarsCount,
            GalaxySize = (GalSize)start.GalaxySize,
            Difficulty = (GameDifficulty)start.Difficulty,
            NumOpponents = start.NumOpponents,
            Pace = start.Pace,
            TurnTimer = start.TurnTimer,
            ExtraPlanets = start.ExtraPlanets,
            StartingPlanetRichnessBonus = start.StartingPlanetRichnessBonus,
            GameSpeed = start.GameSpeed,
            StartPaused = start.StartPaused,
        };

    public static string[] SplitTraitOptions(string traits)
        => traits.IsEmpty()
            ? Array.Empty<string>()
            : traits.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static Authoritative4XLobbyPlayer FindPlayer(Authoritative4XLobby lobby, int peerId)
        => lobby.Roster.FirstOrDefault(p => p.PeerId == peerId)
           ?? throw new InvalidOperationException($"Peer {peerId} is not in the lobby.");

    static string[] SplitTraits(string traits)
        => SplitTraitOptions(traits);

    static void RequireValid(Authoritative4XLobbyValidation validation, string step)
    {
        if (validation == null)
            throw new InvalidOperationException($"{step}: validation was missing.");
        if (!validation.Valid)
            throw new InvalidOperationException(validation.Reason);
    }

    static Planet FirstPlanetForPeer(Authoritative4XGeneratedGameStart generated, int peerId)
    {
        int empireId = generated.EmpireIdForPeer(peerId);
        Empire empire = generated.AuthorityUniverse.UState.GetEmpireById(empireId);
        Planet planet = empire?.GetPlanets().OrderBy(p => p.Id).FirstOrDefault();
        return planet ?? throw new InvalidOperationException($"Peer {peerId} empire {empireId} has no planets.");
    }

    static void PumpTransportUntil(Func<bool> done, params TcpLockstepTransport[] transports)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!done() && DateTime.UtcNow < deadline)
        {
            foreach (TcpLockstepTransport transport in transports)
                transport.Poll();
            Thread.Sleep(5);
        }
        if (!done())
            throw new TimeoutException($"Timed out waiting for lobby transport. errors='{TransportErrors(transports)}'");
    }

    static void PumpLiveUntil(Func<bool> done, Authoritative4XLiveSession host,
        Authoritative4XLiveSession client)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!done() && DateTime.UtcNow < deadline)
        {
            host.Poll();
            client.Poll();
            Thread.Sleep(5);
        }
        if (!done())
            throw new TimeoutException(
                $"Timed out waiting for live authoritative sessions. host='{host.LastError}' client='{client.LastError}'");
    }

    static string TransportErrors(TcpLockstepTransport[] transports)
        => string.Join("; ", transports.Select(t => t.LastError));

    static string SnapshotHash(AuthoritativeStateSnapshot snapshot)
        => snapshot == null ? "" : $"0x{snapshot.HashHi:X16}:0x{snapshot.HashLo:X16}";

    static int FreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

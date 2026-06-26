using System;
using System.Linq;
using SDLockstep;

namespace Ship_Game.Multiplayer.Authoritative;

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

    static Authoritative4XLobbyPlayer FindPlayer(Authoritative4XLobby lobby, int peerId)
        => lobby.Roster.FirstOrDefault(p => p.PeerId == peerId)
           ?? throw new InvalidOperationException($"Peer {peerId} is not in the lobby.");

    static string[] SplitTraits(string traits)
        => traits.IsEmpty()
            ? Array.Empty<string>()
            : traits.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

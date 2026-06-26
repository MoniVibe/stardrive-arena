using System;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using SDLockstep;
using Ship_Game.Audio;
using Ship_Game.Data;
using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.UI;
using Ship_Game.Universe;
using Vector2 = SDGraphics.Vector2;
using Rectangle = SDGraphics.Rectangle;

namespace Ship_Game.GameScreens.Arena;

public sealed class ArenaMultiplayerLobbyScreen : GameScreen
{
    public const int DefaultPort = 47377;
    public const int DefaultTurns = 600;
    const int AuthorityPeerId = Authoritative4XLobby.AuthorityPeerId;
    const int HostPlayerPeerId4X = 2;
    const int JoinPlayerPeerId4X = 3;
    const string DefaultHost = "127.0.0.1";
    static readonly GalSize[] GalaxyOptions =
    {
        GalSize.Tiny,
        GalSize.Small,
        GalSize.Medium,
    };
    static readonly RaceDesignScreen.StarsAbundance[] StarOptions =
    {
        RaceDesignScreen.StarsAbundance.Rare,
        RaceDesignScreen.StarsAbundance.Normal,
        RaceDesignScreen.StarsAbundance.Abundant,
        RaceDesignScreen.StarsAbundance.Crowded,
    };
    static readonly GameDifficulty[] DifficultyOptions =
    {
        GameDifficulty.Normal,
        GameDifficulty.Hard,
        GameDifficulty.Brutal,
        GameDifficulty.Insane,
    };
    static readonly int[] TurnTimerOptions = { 1, 3, 5, 10 };
    static readonly float[] RichnessOptions = { 0f, 1f, 2f, 3f, 5f };

    readonly object Sync = new();
    UITextEntry HostEntry;
    UITextEntry PortEntry;
    UITextEntry TurnsEntry;
    UITextEntry SeedEntry;
    UITextEntry SpeedEntry;
    TcpLockstepTransport Transport;
    ArenaMultiplayerTelemetry LobbyTelemetry;
    ArenaMultiplayerRole? LocalRole;
    LobbyPeer LocalPeer = new() { PlayerName = "Local", RacePreference = "United", LoadoutTrait = "Wingmates" };
    LobbyPeer RemotePeer = new() { PlayerName = "Remote", RacePreference = "-", LoadoutTrait = "-", Ready = false };
    SessionStartMessage Pending4XStart;
    ArenaMultiplayerRunResult LastResult;
    string StatusText = "Idle. Host, join, ready up, then host launches.";
    string[] RaceOptions = Array.Empty<string>();
    string[] TraitOptions = Array.Empty<string>();
    int RaceIndex;
    int TraitIndex;
    int GalaxyIndex;
    int StarsIndex;
    int DifficultyIndex;
    int TurnTimerIndex = 2;
    int RichnessIndex;
    bool StartPaused;
    bool Launching;
    ArenaRegularMultiplayerSettings RegularSettings = new();

    public ArenaMultiplayerLobbyScreen() : base(null, toPause: null)
    {
        CanEscapeFromScreen = true;
        TransitionOnTime  = 0.2f;
        TransitionOffTime = 0.2f;
    }

    public string CurrentStatus
    {
        get { lock (Sync) return StatusText; }
    }

    public bool IsRunning => Launching;
    public ArenaMultiplayerRunResult LatestResult => LastResult;
    public bool LocalReady => LocalPeer.Ready;
    public bool RemoteReady => RemotePeer.Ready;
    public string LocalRace => LocalPeer.RacePreference;
    public string LocalLoadoutTrait => LocalPeer.LoadoutTrait;
    public string LocalTraitOptions => LocalPeer.TraitOptions;
    public string RemoteRace => RemotePeer.RacePreference;
    public string RemoteLoadoutTrait => RemotePeer.LoadoutTrait;
    public string RemoteTraitOptions => RemotePeer.TraitOptions;
    public Authoritative4XGameSettings Current4XSettingsForHeadless => Build4XSettings();
    public SessionStartMessage Build4XStartForHeadless() => Build4XStartMessage();
    public Authoritative4XGeneratedGameStart CreateGenerated4XGameForHeadless(SessionStartMessage start)
        => CreateGenerated4XGame(start);

    public void Configure4XForHeadless(Authoritative4XGameSettings settings, string localRace, string localTraits,
        string remoteRace, string remoteTraits)
    {
        Authoritative4XGameSettings s = (settings ?? new Authoritative4XGameSettings()).Normalized(2);
        RegularSettings = new ArenaRegularMultiplayerSettings
        {
            GenerationSeed = s.GenerationSeed,
            Mode = s.Mode,
            StarsCount = s.StarsCount,
            GalaxySize = s.GalaxySize,
            Difficulty = s.Difficulty,
            NumOpponents = s.NumOpponents,
            Pace = s.Pace,
            TurnTimer = s.TurnTimer,
            ExtraPlanets = s.ExtraPlanets,
            StartingPlanetRichnessBonus = s.StartingPlanetRichnessBonus,
            GameSpeed = s.GameSpeed,
            StartPaused = s.StartPaused,
        };
        LocalPeer.RacePreference = localRace;
        LocalPeer.TraitOptions = localTraits ?? "";
        LocalPeer.LoadoutTrait = ArenaStartArchetype.Wingmates.ToString();
        RemotePeer.RacePreference = remoteRace;
        RemotePeer.TraitOptions = remoteTraits ?? "";
        RemotePeer.LoadoutTrait = ArenaStartArchetype.Wingmates.ToString();
        StartPaused = s.StartPaused;
        if (SeedEntry != null)
            SeedEntry.Text = s.GenerationSeed.ToString(CultureInfo.InvariantCulture);
        if (SpeedEntry != null)
            SpeedEntry.Text = s.GameSpeed.ToString(CultureInfo.InvariantCulture);
    }

    public override void LoadContent()
    {
        RemoveAll();
        RaceOptions = AvailableRacePreferences();
        TraitOptions = AvailableTraitOptions();
        RaceIndex = Math.Max(0, Array.IndexOf(RaceOptions, LocalPeer.RacePreference));
        TraitIndex = Math.Max(0, Array.IndexOf(TraitOptions, LocalPeer.TraitOptions ?? ""));
        GalaxyIndex = Math.Max(0, Array.IndexOf(GalaxyOptions, RegularSettings.GalaxySize));
        StarsIndex = Math.Max(0, Array.IndexOf(StarOptions, RegularSettings.StarsCount));
        DifficultyIndex = Math.Max(0, Array.IndexOf(DifficultyOptions, RegularSettings.Difficulty));
        TurnTimerIndex = Math.Max(0, Array.IndexOf(TurnTimerOptions, RegularSettings.TurnTimer));
        RichnessIndex = Math.Max(0, Array.IndexOf(RichnessOptions, RegularSettings.StartingPlanetRichnessBonus));
        ApplyLocalSelection();

        Vector2 c = ScreenCenter;
        var panel = new RectF(c.X - 440, c.Y - 295, 880, 590);
        Add(ArenaTheme.Panel(panel));
        Add(ArenaTheme.ArenaTitle(new Vector2(panel.X + 24, panel.Y + 24), "STAR GLADIATOR"));
        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 24, panel.Y + 74), "MULTIPLAYER LOBBY"));

        AddField(panel.X + 24, panel.Y + 108, "HOST", DefaultHost, out HostEntry, allowPeriod: true, maxChars: 64, "arena_mp_host_entry");
        AddField(panel.X + 24, panel.Y + 164, "PORT", DefaultPort.ToString(), out PortEntry, allowPeriod: false, maxChars: 6, "arena_mp_port_entry");
        AddField(panel.X + 210, panel.Y + 164, "TURNS", DefaultTurns.ToString(), out TurnsEntry, allowPeriod: false, maxChars: 6, "arena_mp_turns_entry");
        AddField(panel.X + 396, panel.Y + 164, "SEED", "24237", out SeedEntry, allowPeriod: false, maxChars: 9, "arena_mp_seed_entry");
        AddField(panel.X + 582, panel.Y + 164, "SPEED", "1", out SpeedEntry, allowPeriod: true, maxChars: 4, "arena_mp_speed_entry");

        AddPeerCard(new RectF(panel.X + 24, panel.Y + 222, 392, 126), "YOU", () => LocalPeer);
        AddPeerCard(new RectF(panel.X + 440, panel.Y + 222, 392, 126), "REMOTE", () => RemotePeer);

        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 24, panel.Y + 364), "SETUP"));
        UIList setup = AddList(new Vector2(panel.X + 24, panel.Y + 392));
        setup.Direction = new Vector2(1f, 0f);
        setup.Padding = new Vector2(8f, 8f);
        setup.LayoutStyle = ListLayoutStyle.ResizeList;
        UIButton race = ArenaTheme.AddPillButton(setup, "", _ => CycleRace(), 152f);
        race.Name = "arena_mp_race";
        race.DynamicText = () => $"RACE {LocalPeer.RacePreference}";
        UIButton trait = ArenaTheme.AddPillButton(setup, "", _ => CycleTrait(), 176f);
        trait.Name = "arena_mp_trait";
        trait.DynamicText = () => $"TRAIT {TraitLabel(LocalPeer.TraitOptions)}";
        UIButton galaxy = ArenaTheme.AddPillButton(setup, "", _ => CycleGalaxy(), 126f);
        galaxy.Name = "arena_mp_regular_settings";
        galaxy.DynamicText = () => $"MAP {RegularSettings.GalaxySize.ToString().ToUpperInvariant()}";
        UIButton stars = ArenaTheme.AddPillButton(setup, "", _ => CycleStars(), 142f);
        stars.Name = "arena_mp_stars";
        stars.DynamicText = () => $"STARS {RegularSettings.StarsCount.ToString().ToUpperInvariant()}";
        UIButton difficulty = ArenaTheme.AddPillButton(setup, "", _ => CycleDifficulty(), 126f);
        difficulty.Name = "arena_mp_difficulty";
        difficulty.DynamicText = () => $"DIFF {RegularSettings.Difficulty.ToString().ToUpperInvariant()}";

        UIList setup2 = AddList(new Vector2(panel.X + 24, panel.Y + 430));
        setup2.Direction = new Vector2(1f, 0f);
        setup2.Padding = new Vector2(8f, 8f);
        setup2.LayoutStyle = ListLayoutStyle.ResizeList;
        UIButton ai = ArenaTheme.AddPillButton(setup2, "", _ => CycleOpponents(), 88f);
        ai.Name = "arena_mp_opponents";
        ai.DynamicText = () => $"AI {RegularSettings.NumOpponents}";
        UIButton richness = ArenaTheme.AddPillButton(setup2, "", _ => CycleRichness(), 142f);
        richness.Name = "arena_mp_richness";
        richness.DynamicText = () => $"RICH {RegularSettings.StartingPlanetRichnessBonus:0.#}";
        UIButton turn = ArenaTheme.AddPillButton(setup2, "", _ => CycleTurnTimer(), 126f);
        turn.Name = "arena_mp_turn_timer";
        turn.DynamicText = () => $"TURN {RegularSettings.TurnTimer}S";
        UIButton pace = ArenaTheme.AddPillButton(setup2, "", _ => CyclePace(), 112f);
        pace.Name = "arena_mp_pace";
        pace.DynamicText = () => $"PACE {RegularSettings.Pace:0.#}X";
        UIButton pause = ArenaTheme.AddPillButton(setup2, "", _ => ToggleStartPaused(), 126f);
        pause.Name = "arena_mp_start_paused";
        pause.DynamicText = () => StartPaused ? "START PAUSED" : "START LIVE";

        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 24, panel.Y + 466), "STATUS"));
        for (int i = 0; i < 3; ++i)
        {
            int line = i;
            Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 492 + i * 18),
                StatusLine(line), ArenaTheme.BodySmallFont, ArenaTheme.TextSecondary)
            {
                DynamicText = _ => StatusLine(line),
            });
        }

        UIList actions = AddList(new Vector2(panel.X + 24, panel.Bottom - 52));
        actions.Direction = new Vector2(1f, 0f);
        actions.Padding = new Vector2(8f, 8f);
        actions.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPrimaryButton(actions, "HOST", _ => StartHost(), 96f).Name = "arena_mp_host";
        ArenaTheme.AddPillButton(actions, "JOIN", _ => StartJoin(), 96f).Name = "arena_mp_join";
        ArenaTheme.AddPillButton(actions, "READY", _ => ToggleReady(), 96f).Name = "arena_mp_ready";
        ArenaTheme.AddPrimaryButton(actions, "LAUNCH", _ => LaunchAsHost(), 112f).Name = "arena_mp_launch";
        ArenaTheme.AddPillButton(actions, "SELF TEST", _ => StartSelfTest(), 126f).Name = "arena_mp_self_test";
        ArenaTheme.AddPillButton(actions, "BACK", _ => ExitScreen(), 90f).Name = "arena_mp_back";
    }

    void AddField(float x, float y, string label, string value, out UITextEntry entry,
        bool allowPeriod, int maxChars, string name)
    {
        Add(ArenaTheme.Label(new Vector2(x, y), label));
        var bg = new Submenu(new RectF(x, y + 18, label == "HOST" ? 340 : 142, 26));
        entry = new UITextEntry(new Rectangle((int)x + 6, (int)y + 20, (int)bg.Width - 12, 24),
            ArenaTheme.BodyFont, value)
        {
            Name = name,
            AllowPeriod = allowPeriod,
            MaxCharacters = maxChars,
            DrawUnderline = true,
            AutoCaptureOnHover = true,
            Color = ArenaTheme.TextPrimary,
            HoverColor = ArenaTheme.AmberBright,
            InputColor = ArenaTheme.Cyan,
            Background = bg,
        };
        Add(entry);
    }

    void AddPeerCard(RectF rect, string title, Func<LobbyPeer> peer)
    {
        Add(ArenaTheme.Card(rect));
        Add(ArenaTheme.SectionHeader(new Vector2(rect.X + 14, rect.Y + 12), title));
        Add(new UILabel(new Vector2(rect.X + 14, rect.Y + 42), "", ArenaTheme.BodyFont, ArenaTheme.TextPrimary)
        {
            DynamicText = _ => $"{peer().PlayerName} | {(peer().Ready ? "READY" : "NOT READY")}",
        });
        Add(new UILabel(new Vector2(rect.X + 14, rect.Y + 70), "", ArenaTheme.BodySmallFont, ArenaTheme.TextSecondary)
        {
            DynamicText = _ => $"Race: {peer().RacePreference} | Trait: {TraitLabel(peer().TraitOptions)}",
        });
        Add(new UILabel(new Vector2(rect.X + 14, rect.Y + 94), "", ArenaTheme.BodySmallFont, ArenaTheme.TextMuted)
        {
            DynamicText = _ => peer().Summary,
        });
    }

    void StartSelfTest()
    {
        LastResult = RunLocalSelfTestForHeadless(ParseTurns());
        SetStatus(Summarize(LastResult));
        GameAudio.AffirmativeClick();
    }

    void StartHost()
    {
        if (Transport != null)
        {
            SetStatus("Already hosting or joined. Back out to reset the socket.");
            GameAudio.NegativeClick();
            return;
        }

        LocalRole = ArenaMultiplayerRole.Host;
        LocalPeer.PlayerName = "Host";
        ApplyLocalSelection();
        LobbyTelemetry?.Dispose();
        LobbyTelemetry = ArenaMultiplayerTelemetry.Start("Host", "lobby", CreateDefaultSettings(ParseTurns()));
        Transport = TcpLockstepTransport.HostMulti(ParsePort());
        Transport.AddObserver(AuthorityPeerId, OnHostMessage);
        SendLocalLobby();
        SetStatus($"HOST listening on port {ParsePort()}\nPick setup, ready, then launch when remote is ready.");
        LobbyTelemetry.Event("HOST_LISTEN", $"port={ParsePort()}");
        GameAudio.AffirmativeClick();
    }

    void StartJoin()
    {
        if (Transport != null)
        {
            SetStatus("Already hosting or joined. Back out to reset the socket.");
            GameAudio.NegativeClick();
            return;
        }

        string host = HostEntry?.Text?.Trim();
        if (host.IsEmpty())
            host = DefaultHost;
        LocalRole = ArenaMultiplayerRole.Join;
        LocalPeer.PlayerName = "Join";
        ApplyLocalSelection();
        LobbyTelemetry?.Dispose();
        LobbyTelemetry = ArenaMultiplayerTelemetry.Start("Join", "lobby", CreateDefaultSettings(ParseTurns()));
        Transport = TcpLockstepTransport.JoinAsPeer(host, ParsePort(), JoinPlayerPeerId4X, AuthorityPeerId);
        Transport.AddObserver(JoinPlayerPeerId4X, OnJoinMessage);
        Transport.Send(AuthorityPeerId, new SessionHelloMessage
        {
            FromPeer = JoinPlayerPeerId4X,
            PeerId = JoinPlayerPeerId4X,
            ProtocolVersion = ArenaMultiplayerSettings.ProtocolVersion,
            PlayerName = LocalPeer.PlayerName,
            BuildHash = ArenaMultiplayerPeerSignature.EnvironmentHash(),
            BuildSummary = ArenaMultiplayerPeerSignature.EnvironmentSummary(),
        });
        SendLocalLobby();
        SetStatus($"JOIN connected to {host}:{ParsePort()}\nPress ready; host launches the match.");
        LobbyTelemetry.Event("JOIN_CONNECT", $"host={host} port={ParsePort()}");
        GameAudio.AffirmativeClick();
    }

    void ToggleReady()
    {
        if (LocalRole == null || Transport == null)
        {
            SetStatus("Host or join first, then ready up.");
            GameAudio.NegativeClick();
            return;
        }
        LocalPeer.Ready = !LocalPeer.Ready;
        SendLocalLobby();
        SetStatus(LocalPeer.Ready ? "Local player ready." : "Local player un-ready.");
        LobbyTelemetry?.Event("LOCAL_READY", $"ready={LocalPeer.Ready}");
        GameAudio.AffirmativeClick();
    }

    void LaunchAsHost()
    {
        if (LocalRole != ArenaMultiplayerRole.Host || Transport == null)
        {
            SetStatus("Only the host can launch.");
            GameAudio.NegativeClick();
            return;
        }
        if (!Transport.IsConnected)
        {
            SetStatus("Waiting for a client connection.");
            GameAudio.NegativeClick();
            return;
        }
        if (!LocalPeer.Ready || !RemotePeer.Ready)
        {
            SetStatus("Both players must be ready before launch.");
            GameAudio.NegativeClick();
            return;
        }

        string error = ArenaMultiplayerPeerSignature.ValidateEnvironment(RemotePeer.BuildHash, RemotePeer.BuildSummary, "remote");
        if (error.NotEmpty())
        {
            Transport.Send(JoinPlayerPeerId4X,
                new SessionErrorMessage { FromPeer = AuthorityPeerId, Error = error });
            SetStatus(error);
            LobbyTelemetry?.Event("PREFLIGHT_REJECT", error);
            GameAudio.NegativeClick();
            return;
        }

        SessionStartMessage start = Build4XStartMessage();
        LobbyTelemetry?.Event("LAUNCH_HOST",
            $"settingsHash={start.SettingsHash} mode=4x");
        Transport.Send(JoinPlayerPeerId4X, start);
        LaunchVisible4X(Authoritative4XLiveRole.Host, start);
    }

    void OnHostMessage(LockstepMessage message)
    {
        if (message is SessionHelloMessage h && h.PeerId == JoinPlayerPeerId4X)
        {
            string error = h.ProtocolVersion != ArenaMultiplayerSettings.ProtocolVersion
                ? $"Arena multiplayer protocol mismatch. Local {ArenaMultiplayerSettings.ProtocolVersion}, remote {h.ProtocolVersion}."
                : ArenaMultiplayerPeerSignature.ValidateEnvironment(h.BuildHash, h.BuildSummary, "remote");
            if (error.NotEmpty())
            {
                SetStatus(error);
                LobbyTelemetry?.Event("HELLO_REJECT", error);
            }
            else
            {
                LobbyTelemetry?.Event("HELLO", $"peer={h.PeerId} summary='{h.BuildSummary}'");
            }
        }
        if (message is SessionLobbyMessage lobby && lobby.PeerId == JoinPlayerPeerId4X)
        {
            RemotePeer = LobbyPeer.From(lobby, "Join");
            SetStatus($"Remote lobby updated.\n{RemotePeer.Summary}");
            LobbyTelemetry?.Event("REMOTE_LOBBY",
                $"ready={RemotePeer.Ready} race='{RemotePeer.RacePreference}' summary='{RemotePeer.BuildSummary}'");
        }
        if (message is SessionErrorMessage e)
        {
            SetStatus(e.Error);
            LobbyTelemetry?.Event("SESSION_ERROR", e.Error);
        }
    }

    void OnJoinMessage(LockstepMessage message)
    {
        if (message is SessionLobbyMessage lobby && lobby.PeerId == HostPlayerPeerId4X)
            RemotePeer = LobbyPeer.From(lobby, "Host");
        if (message is SessionStartMessage start)
        {
            string error = Validate4XStart(start);
            if (error.NotEmpty())
            {
                SetStatus(error);
                LobbyTelemetry?.Event("START_REJECT", error);
                return;
            }
            LobbyTelemetry?.Event("START_RECEIVED",
                $"settingsHash={start.SettingsHash} mode=4x");
            Pending4XStart = start;
        }
        if (message is SessionErrorMessage e)
        {
            SetStatus(e.Error);
            LobbyTelemetry?.Event("SESSION_ERROR", e.Error);
        }
    }

    void SendLocalLobby()
    {
        if (Transport == null || LocalRole == null)
            return;
        int peerId = LocalRole == ArenaMultiplayerRole.Host
            ? HostPlayerPeerId4X
            : JoinPlayerPeerId4X;
        int toPeer = LocalRole == ArenaMultiplayerRole.Host
            ? JoinPlayerPeerId4X
            : AuthorityPeerId;
        Transport.Send(toPeer, new SessionLobbyMessage
        {
            FromPeer = peerId,
            PeerId = peerId,
            Ready = LocalPeer.Ready,
            PlayerName = LocalPeer.PlayerName,
            RacePreference = LocalPeer.RacePreference,
            LoadoutTrait = LocalPeer.LoadoutTrait,
            TraitOptions = LocalPeer.TraitOptions,
            BuildHash = ArenaMultiplayerPeerSignature.EnvironmentHash(),
            BuildSummary = ArenaMultiplayerPeerSignature.EnvironmentSummary(),
        });
    }

    void LaunchVisible4X(Authoritative4XLiveRole role, SessionStartMessage start)
    {
        if (Transport == null)
            return;
        Launching = true;
        TcpLockstepTransport transport = Transport;
        Transport = null;
        LobbyTelemetry?.Event("LAUNCH_VISIBLE_4X", $"role={role}");
        LobbyTelemetry?.Dispose();
        LobbyTelemetry = null;

        Authoritative4XGeneratedGameStart generated = CreateGenerated4XGame(start);
        int localPeer = role == Authoritative4XLiveRole.Host ? HostPlayerPeerId4X : JoinPlayerPeerId4X;
        Authoritative4XLiveSession live = role == Authoritative4XLiveRole.Host
            ? Authoritative4XLiveSession.HostGame(generated.AuthorityUniverse, transport, localPeer,
                generated.EmpireIdByPeer, generated.HumanEmpireIds)
            : Authoritative4XLiveSession.ClientGame(generated.AuthorityUniverse, transport, localPeer,
                generated.EmpireIdForPeer(localPeer), generated.HumanEmpireIds);
        generated.AuthorityUniverse.AttachAuthoritative4XMultiplayer(live);
        ScreenManager.GoToScreen(generated.AuthorityUniverse, clear3DObjects: true);
    }

    ArenaMultiplayerSettings BuildHostSettings()
        => new()
        {
            MatchSeed = ParseSeed(),
            RngSeed = (uint)ParseSeed() ^ 0xA12EA000u,
            InputDelay = 3,
            MaxTurns = ParseTurns(),
            CommandEveryTurns = 1,
            HostRacePreference = LocalPeer.RacePreference,
            JoinRacePreference = RemotePeer.RacePreference == "-" ? "" : RemotePeer.RacePreference,
            PlayerPreference = LocalPeer.RacePreference,
            HostLoadoutTrait = LocalPeer.LoadoutTrait,
            JoinLoadoutTrait = RemotePeer.LoadoutTrait == "-" ? ArenaStartArchetype.Wingmates.ToString() : RemotePeer.LoadoutTrait,
            GameSpeed = ParseSpeed(),
            StartPaused = StartPaused,
        };

    SessionStartMessage Build4XStartMessage()
    {
        Authoritative4XGameSettings settings = Build4XSettings();
        return new SessionStartMessage
        {
            FromPeer = AuthorityPeerId,
            ProtocolVersion = ArenaMultiplayerSettings.ProtocolVersion,
            MatchSeed = settings.GenerationSeed,
            RngSeed = (uint)settings.GenerationSeed ^ 0x4D505547u,
            InputDelay = 0,
            MaxTurns = ParseTurns(),
            CommandEveryTurns = 1,
            GameSpeed = settings.GameSpeed,
            StartPaused = settings.StartPaused,
            SettingsHash = settings.SettingsHash,
            BuildHash = ArenaMultiplayerPeerSignature.EnvironmentHash(),
            BuildSummary = ArenaMultiplayerPeerSignature.EnvironmentSummary(),
            HostRacePreference = LocalPeer.RacePreference,
            JoinRacePreference = RemotePeer.RacePreference == "-" ? "" : RemotePeer.RacePreference,
            HostTraitOptions = LocalPeer.TraitOptions,
            JoinTraitOptions = RemotePeer.TraitOptions,
            IsAuthoritative4X = true,
            AuthoritativeHostPeerId = HostPlayerPeerId4X,
            AuthoritativeJoinPeerId = JoinPlayerPeerId4X,
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

    Authoritative4XGameSettings Build4XSettings()
        => new Authoritative4XGameSettings
        {
            GenerationSeed = ParseSeed(),
            Mode = RegularSettings.Mode,
            StarsCount = RegularSettings.StarsCount,
            GalaxySize = RegularSettings.GalaxySize,
            Difficulty = RegularSettings.Difficulty,
            NumOpponents = RegularSettings.NumOpponents,
            Pace = RegularSettings.Pace,
            TurnTimer = RegularSettings.TurnTimer,
            ExtraPlanets = RegularSettings.ExtraPlanets,
            StartingPlanetRichnessBonus = RegularSettings.StartingPlanetRichnessBonus,
            GameSpeed = ParseSpeed(),
            StartPaused = StartPaused,
        }.Normalized(2);

    string Validate4XStart(SessionStartMessage start)
    {
        if (!start.IsAuthoritative4X)
            return "Host sent a non-4X session start.";
        if (start.ProtocolVersion != ArenaMultiplayerSettings.ProtocolVersion)
            return $"Arena multiplayer protocol mismatch. Local {ArenaMultiplayerSettings.ProtocolVersion}, host {start.ProtocolVersion}.";
        string error = ArenaMultiplayerPeerSignature.ValidateEnvironment(start.BuildHash, start.BuildSummary, "host");
        if (error.NotEmpty())
            return error;
        if (start.AuthoritativeHostPeerId != HostPlayerPeerId4X || start.AuthoritativeJoinPeerId != JoinPlayerPeerId4X)
            return $"Authoritative peer mismatch. Host {start.AuthoritativeHostPeerId}, join {start.AuthoritativeJoinPeerId}.";
        Authoritative4XGameSettings settings = SettingsFrom4XStart(start).Normalized(2);
        return string.Equals(start.SettingsHash, settings.SettingsHash, StringComparison.Ordinal)
            ? ""
            : $"Authoritative 4X settings mismatch. Host {start.SettingsHash}, local {settings.SettingsHash}.";
    }

    Authoritative4XGeneratedGameStart CreateGenerated4XGame(SessionStartMessage start)
    {
        Authoritative4XGameSettings settings = SettingsFrom4XStart(start).Normalized(2);
        var lobby = new Authoritative4XLobby(HostPlayerPeerId4X, "Host");
        lobby.Join(JoinPlayerPeerId4X, "Join");
        Authoritative4XLobbyValidation set = lobby.SetSettings(HostPlayerPeerId4X, settings);
        if (!set.Valid)
            throw new InvalidOperationException(set.Reason);
        Authoritative4XLobbyValidation host = lobby.SetPlayerSelection(HostPlayerPeerId4X,
            start.HostRacePreference, SplitTraits(start.HostTraitOptions));
        if (!host.Valid)
            throw new InvalidOperationException(host.Reason);
        Authoritative4XLobbyValidation join = lobby.SetPlayerSelection(JoinPlayerPeerId4X,
            start.JoinRacePreference, SplitTraits(start.JoinTraitOptions));
        if (!join.Valid)
            throw new InvalidOperationException(join.Reason);
        lobby.SetReady(HostPlayerPeerId4X, true);
        lobby.SetReady(JoinPlayerPeerId4X, true);
        return lobby.StartGeneratedGame();
    }

    static Authoritative4XGameSettings SettingsFrom4XStart(SessionStartMessage start)
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

    static string[] SplitTraits(string traits)
        => traits.IsEmpty()
            ? Array.Empty<string>()
            : traits.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public override void Update(float fixedDeltaTime)
    {
        base.Update(fixedDeltaTime);
        if (Transport != null)
        {
            Transport.Poll();
            if (Transport.LastError.NotEmpty())
            {
                SetStatus("NETWORK: " + Transport.LastError);
                LobbyTelemetry?.NetworkError(Transport.LastError);
            }
            if (LocalRole == ArenaMultiplayerRole.Host && Transport.IsConnected && CurrentStatus.StartsWith("HOST listening", StringComparison.Ordinal))
            {
                SetStatus("Client connected. Ready up and launch when both sides are ready.");
                LobbyTelemetry?.Event("CLIENT_CONNECTED");
            }
        }
        if (Pending4XStart != null)
        {
            SessionStartMessage start = Pending4XStart;
            Pending4XStart = null;
            LaunchVisible4X(Authoritative4XLiveRole.Client, start);
        }
    }

    void CycleRace()
    {
        if (RaceOptions.Length == 0)
            return;
        RaceIndex = (RaceIndex + 1) % RaceOptions.Length;
        ApplyLocalSelection();
        LocalPeer.Ready = false;
        SendLocalLobby();
    }

    void CycleTrait()
    {
        if (TraitOptions.Length == 0)
            return;
        TraitIndex = (TraitIndex + 1) % TraitOptions.Length;
        ApplyLocalSelection();
        LocalPeer.Ready = false;
        SendLocalLobby();
    }

    void CycleGalaxy()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        GalaxyIndex = (GalaxyIndex + 1) % GalaxyOptions.Length;
        RegularSettings.GalaxySize = GalaxyOptions[GalaxyIndex];
        LocalPeer.Ready = false;
        SendLocalLobby();
    }

    void CycleStars()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        StarsIndex = (StarsIndex + 1) % StarOptions.Length;
        RegularSettings.StarsCount = StarOptions[StarsIndex];
        LocalPeer.Ready = false;
        SendLocalLobby();
    }

    void CycleDifficulty()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        DifficultyIndex = (DifficultyIndex + 1) % DifficultyOptions.Length;
        RegularSettings.Difficulty = DifficultyOptions[DifficultyIndex];
        LocalPeer.Ready = false;
        SendLocalLobby();
    }

    void CycleOpponents()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        int max = Math.Max(1, ResourceManager.MajorRaces.Count - 1);
        RegularSettings.NumOpponents = RegularSettings.NumOpponents >= max ? 1 : RegularSettings.NumOpponents + 1;
        LocalPeer.Ready = false;
        SendLocalLobby();
    }

    void CycleRichness()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        RichnessIndex = (RichnessIndex + 1) % RichnessOptions.Length;
        RegularSettings.StartingPlanetRichnessBonus = RichnessOptions[RichnessIndex];
        LocalPeer.Ready = false;
        SendLocalLobby();
    }

    void CycleTurnTimer()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        TurnTimerIndex = (TurnTimerIndex + 1) % TurnTimerOptions.Length;
        RegularSettings.TurnTimer = TurnTimerOptions[TurnTimerIndex];
        LocalPeer.Ready = false;
        SendLocalLobby();
    }

    void CyclePace()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        RegularSettings.Pace += 0.5f;
        if (RegularSettings.Pace > 4f)
            RegularSettings.Pace = 1f;
        LocalPeer.Ready = false;
        SendLocalLobby();
    }

    void ToggleStartPaused()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        StartPaused = !StartPaused;
        RegularSettings.StartPaused = StartPaused;
        SendLocalLobby();
    }

    void ApplyLocalSelection()
    {
        LocalPeer.RacePreference = RaceOptions.Length == 0 ? "United" : RaceOptions[RaceIndex.Clamped(0, RaceOptions.Length - 1)];
        LocalPeer.LoadoutTrait = ArenaStartArchetype.Wingmates.ToString();
        LocalPeer.TraitOptions = TraitOptions.Length == 0 ? "" : TraitOptions[TraitIndex.Clamped(0, TraitOptions.Length - 1)];
        RegularSettings.HostRacePreference = LocalPeer.RacePreference;
        RegularSettings.JoinRacePreference = RemotePeer.RacePreference == "-" ? "" : RemotePeer.RacePreference;
    }

    bool HostSettingsAreLockedToRemote()
    {
        if (LocalRole != ArenaMultiplayerRole.Join)
            return false;
        SetStatus("Host controls game settings.");
        GameAudio.NegativeClick();
        return true;
    }

    int ParsePort()
        => int.TryParse(PortEntry?.Text, out int port) ? port.Clamped(1, 65535) : DefaultPort;

    int ParseTurns()
        => int.TryParse(TurnsEntry?.Text, out int turns) ? turns.Clamped(30, 2000) : DefaultTurns;

    int ParseSeed()
        => int.TryParse(SeedEntry?.Text, out int seed)
            ? seed
            : RegularSettings.GenerationSeed != 0 ? RegularSettings.GenerationSeed : 0x5EED;

    float ParseSpeed()
        => float.TryParse(SpeedEntry?.Text, out float speed)
            ? ArenaMultiplayerSettings.ClampGameSpeed(speed)
            : ArenaMultiplayerSettings.ClampGameSpeed(RegularSettings.GameSpeed);

    void SetStatus(string text)
    {
        lock (Sync)
            StatusText = text ?? "";
    }

    string StatusLine(int index)
    {
        string text;
        lock (Sync)
            text = StatusText ?? "";
        string[] lines = text.Split('\n');
        return index >= 0 && index < lines.Length ? lines[index] : "";
    }

    static string Summarize(ArenaMultiplayerRunResult result)
    {
        if (result == null)
            return "No result.";
        if (result.Desynced)
            return $"DESYNC turn {result.DesyncTurn}\n{result.DesyncReason}\nfinal {result.FinalHash}";
        string winner = result.MatchEnded ? result.WinnerPeerId.ToString() : "none";
        return $"COMPLETE turns {result.TurnsCompleted}\nwinner {winner} ended {result.MatchEnded}\nfinal {result.FinalHash}";
    }

    public static ArenaMultiplayerSettings CreateDefaultSettings(int turns = DefaultTurns)
        => new()
        {
            MatchSeed = 0x5EED,
            RngSeed = 0xA12EA000u,
            InputDelay = 3,
            MaxTurns = turns.Clamped(30, 2000),
            CommandEveryTurns = 1,
            HostRacePreference = "United",
            JoinRacePreference = "",
            HostLoadoutTrait = ArenaStartArchetype.Wingmates.ToString(),
            JoinLoadoutTrait = ArenaStartArchetype.Wingmates.ToString(),
            GameSpeed = 1f,
        };

    public static ArenaMultiplayerRunResult RunLocalSelfTestForHeadless(int turns = 90)
        => ArenaMultiplayerSession.RunInProcess(CreateDefaultSettings(turns));

    static string[] AvailableRacePreferences()
    {
        try
        {
            string[] races = ResourceManager.MajorRaces
                .Select(r => r.ArchetypeName.NotEmpty() ? r.ArchetypeName : r.Name)
                .Where(r => r.NotEmpty())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToArray();
            return races.Length > 0 ? races : new[] { "United" };
        }
        catch
        {
            return new[] { "United" };
        }
    }

    static string[] AvailableTraitOptions()
    {
        try
        {
            int points = new UniverseParams().RacialTraitPoints;
            string[] traits = ResourceManager.RaceTraits.TraitList
                .Where(t => t != null && t.TraitName.NotEmpty() && t.Cost <= points)
                .Select(t => t.TraitName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToArray();
            return new[] { "" }.Concat(traits).ToArray();
        }
        catch
        {
            return new[] { "" };
        }
    }

    static string TraitLabel(string trait)
        => trait.NotEmpty() ? trait.ToUpperInvariant() : "NONE";

    public override void ExitScreen()
    {
        Transport?.Dispose();
        Transport = null;
        LobbyTelemetry?.Dispose();
        LobbyTelemetry = null;
        base.ExitScreen();
        ScreenManager.GoToScreen(new Ship_Game.GameScreens.MainMenu.MainMenuScreen(), clear3DObjects: true);
    }

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        ScreenManager.FadeBackBufferToBlack(TransitionAlpha * 2 / 3);
        batch.SafeBegin();
        base.Draw(batch, elapsed);
        batch.SafeEnd();
    }

    sealed class LobbyPeer
    {
        public bool Ready;
        public string PlayerName = "";
        public string RacePreference = "";
        public string LoadoutTrait = "";
        public string TraitOptions = "";
        public string BuildHash = "";
        public string BuildSummary = "";

        public string Summary => BuildHash.NotEmpty()
            ? $"{BuildHash}"
            : "No peer data yet.";

        public static LobbyPeer From(SessionLobbyMessage message, string fallbackName)
            => new()
            {
                Ready = message.Ready,
                PlayerName = message.PlayerName.NotEmpty() ? message.PlayerName : fallbackName,
                RacePreference = message.RacePreference.NotEmpty() ? message.RacePreference : "United",
                LoadoutTrait = ArenaMultiplayerSettings.NormalizeLoadoutTrait(message.LoadoutTrait),
                TraitOptions = message.TraitOptions ?? "",
                BuildHash = message.BuildHash ?? "",
                BuildSummary = message.BuildSummary ?? "",
            };
    }
}

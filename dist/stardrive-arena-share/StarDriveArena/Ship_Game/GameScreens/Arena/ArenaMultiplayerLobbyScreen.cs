using System;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using SDLockstep;
using Ship_Game.Audio;
using Ship_Game.UI;
using Vector2 = SDGraphics.Vector2;
using Rectangle = SDGraphics.Rectangle;

namespace Ship_Game.GameScreens.Arena;

public sealed class ArenaMultiplayerLobbyScreen : GameScreen
{
    public const int DefaultPort = 47377;
    public const int DefaultTurns = 600;
    const string DefaultHost = "127.0.0.1";
    static readonly GalSize[] GalaxyOptions =
    {
        GalSize.Tiny,
        GalSize.Small,
        GalSize.Medium,
    };

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
    ArenaMultiplayerSettings PendingLaunchSettings;
    ArenaMultiplayerRunResult LastResult;
    string StatusText = "Idle. Host, join, ready up, then host launches.";
    string[] RaceOptions = Array.Empty<string>();
    int RaceIndex;
    int GalaxyIndex;
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
    public string RemoteRace => RemotePeer.RacePreference;
    public string RemoteLoadoutTrait => RemotePeer.LoadoutTrait;

    public override void LoadContent()
    {
        RemoveAll();
        RaceOptions = AvailableRacePreferences();
        RaceIndex = Math.Max(0, Array.IndexOf(RaceOptions, LocalPeer.RacePreference));
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

        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 24, panel.Y + 372), "SETUP"));
        UIList setup = AddList(new Vector2(panel.X + 24, panel.Y + 400));
        setup.Direction = new Vector2(1f, 0f);
        setup.Padding = new Vector2(8f, 8f);
        setup.LayoutStyle = ListLayoutStyle.ResizeList;
        UIButton race = ArenaTheme.AddPillButton(setup, "", _ => CycleRace(), 176f);
        race.Name = "arena_mp_race";
        race.DynamicText = () => $"RACE {LocalPeer.RacePreference}";
        UIButton galaxy = ArenaTheme.AddPillButton(setup, "", _ => CycleGalaxy(), 176f);
        galaxy.Name = "arena_mp_regular_settings";
        galaxy.DynamicText = () => $"MAP {RegularSettings.GalaxySize.ToString().ToUpperInvariant()}";
        UIButton pause = ArenaTheme.AddPillButton(setup, "", _ => ToggleStartPaused(), 126f);
        pause.Name = "arena_mp_start_paused";
        pause.DynamicText = () => StartPaused ? "START PAUSED" : "START LIVE";

        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 24, panel.Y + 458), "STATUS"));
        for (int i = 0; i < 3; ++i)
        {
            int line = i;
            Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 486 + i * 20),
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
            DynamicText = _ => $"Race: {peer().RacePreference}",
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
        Transport = TcpLockstepTransport.Host(ParsePort(), ArenaMultiplayerSession.JoinPlayerPeerId);
        Transport.AddObserver(LockstepHost.HostPeerId, OnHostMessage);
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
        Transport = TcpLockstepTransport.Join(host, ParsePort(), LockstepHost.HostPeerId);
        Transport.AddObserver(ArenaMultiplayerSession.JoinPlayerPeerId, OnJoinMessage);
        Transport.Send(LockstepHost.HostPeerId, new SessionHelloMessage
        {
            FromPeer = ArenaMultiplayerSession.JoinPlayerPeerId,
            PeerId = ArenaMultiplayerSession.JoinPlayerPeerId,
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

        ArenaMultiplayerSettings settings = BuildHostSettings().WithResolvedFleets();
        string error = ArenaMultiplayerPeerSignature.ValidateEnvironment(RemotePeer.BuildHash, RemotePeer.BuildSummary, "remote");
        if (error.NotEmpty())
        {
            Transport.Send(ArenaMultiplayerSession.JoinPlayerPeerId,
                new SessionErrorMessage { FromPeer = LockstepHost.HostPeerId, Error = error });
            SetStatus(error);
            LobbyTelemetry?.Event("PREFLIGHT_REJECT", error);
            GameAudio.NegativeClick();
            return;
        }

        LobbyTelemetry?.Event("LAUNCH_HOST",
            $"settingsHash={settings.SettingsHash} sessionHash={ArenaMultiplayerPeerSignature.Hash(settings)}");
        Transport.Send(ArenaMultiplayerSession.JoinPlayerPeerId, settings.ToStartMessage());
        LaunchVisibleMatch(ArenaMultiplayerRole.Host, settings);
    }

    void OnHostMessage(LockstepMessage message)
    {
        if (message is SessionHelloMessage h && h.PeerId == ArenaMultiplayerSession.JoinPlayerPeerId)
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
        if (message is SessionLobbyMessage lobby && lobby.PeerId == ArenaMultiplayerSession.JoinPlayerPeerId)
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
        if (message is SessionLobbyMessage lobby && lobby.PeerId == ArenaMultiplayerSession.HostPlayerPeerId)
            RemotePeer = LobbyPeer.From(lobby, "Host");
        if (message is SessionStartMessage start)
        {
            ArenaMultiplayerSettings settings = ArenaMultiplayerSettings.FromStartMessage(start).WithResolvedFleets();
            string error = start.ProtocolVersion != ArenaMultiplayerSettings.ProtocolVersion
                ? $"Arena multiplayer protocol mismatch. Local {ArenaMultiplayerSettings.ProtocolVersion}, host {start.ProtocolVersion}."
                : !string.Equals(start.SettingsHash, settings.SettingsHash, StringComparison.Ordinal)
                    ? $"Arena multiplayer settings mismatch. Host {start.SettingsHash}, local {settings.SettingsHash}."
                    : ArenaMultiplayerPeerSignature.ValidateSession(start.BuildHash, start.BuildSummary, settings, "host");
            if (error.NotEmpty())
            {
                SetStatus(error);
                LobbyTelemetry?.Event("START_REJECT", error);
                return;
            }
            LobbyTelemetry?.Event("START_RECEIVED",
                $"settingsHash={settings.SettingsHash} sessionHash={ArenaMultiplayerPeerSignature.Hash(settings)}");
            PendingLaunchSettings = settings;
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
            ? ArenaMultiplayerSession.HostPlayerPeerId
            : ArenaMultiplayerSession.JoinPlayerPeerId;
        int toPeer = LocalRole == ArenaMultiplayerRole.Host
            ? ArenaMultiplayerSession.JoinPlayerPeerId
            : LockstepHost.HostPeerId;
        Transport.Send(toPeer, new SessionLobbyMessage
        {
            FromPeer = peerId,
            PeerId = peerId,
            Ready = LocalPeer.Ready,
            PlayerName = LocalPeer.PlayerName,
            RacePreference = LocalPeer.RacePreference,
            LoadoutTrait = LocalPeer.LoadoutTrait,
            BuildHash = ArenaMultiplayerPeerSignature.EnvironmentHash(),
            BuildSummary = ArenaMultiplayerPeerSignature.EnvironmentSummary(),
        });
    }

    void LaunchVisibleMatch(ArenaMultiplayerRole role, ArenaMultiplayerSettings settings)
    {
        if (Transport == null)
            return;
        Launching = true;
        TcpLockstepTransport transport = Transport;
        Transport = null;
        LobbyTelemetry?.Event("LAUNCH_VISIBLE", $"role={role}");
        LobbyTelemetry?.Dispose();
        LobbyTelemetry = null;
        var live = new ArenaMultiplayerLiveSession(role, transport, settings);
        ArenaFightScreen screen = ArenaFightScreen.Create(settings.HostRacePreference, settings.MatchSeed,
            startAtHub: false, opponentPreference: settings.JoinRacePreference);
        screen.ArmMultiplayerLive(live);
        ScreenManager.GoToScreen(screen, clear3DObjects: true);
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
        if (PendingLaunchSettings != null)
        {
            ArenaMultiplayerSettings settings = PendingLaunchSettings;
            PendingLaunchSettings = null;
            LaunchVisibleMatch(ArenaMultiplayerRole.Join, settings);
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

    void CycleGalaxy()
    {
        GalaxyIndex = (GalaxyIndex + 1) % GalaxyOptions.Length;
        RegularSettings.GalaxySize = GalaxyOptions[GalaxyIndex];
        LocalPeer.Ready = false;
        SendLocalLobby();
    }

    void ToggleStartPaused()
    {
        StartPaused = !StartPaused;
        SendLocalLobby();
    }

    void ApplyLocalSelection()
    {
        LocalPeer.RacePreference = RaceOptions.Length == 0 ? "United" : RaceOptions[RaceIndex.Clamped(0, RaceOptions.Length - 1)];
        LocalPeer.LoadoutTrait = ArenaStartArchetype.Wingmates.ToString();
        RegularSettings.HostRacePreference = LocalPeer.RacePreference;
        RegularSettings.JoinRacePreference = RemotePeer.RacePreference == "-" ? "" : RemotePeer.RacePreference;
    }

    int ParsePort()
        => int.TryParse(PortEntry?.Text, out int port) ? port.Clamped(1, 65535) : DefaultPort;

    int ParseTurns()
        => int.TryParse(TurnsEntry?.Text, out int turns) ? turns.Clamped(30, 2000) : DefaultTurns;

    int ParseSeed()
        => int.TryParse(SeedEntry?.Text, out int seed) ? seed : 0x5EED;

    float ParseSpeed()
        => float.TryParse(SpeedEntry?.Text, out float speed)
            ? ArenaMultiplayerSettings.ClampGameSpeed(speed)
            : 1f;

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
                BuildHash = message.BuildHash ?? "",
                BuildSummary = message.BuildSummary ?? "",
            };
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using SDLockstep;
using Ship_Game.Audio;
using Ship_Game.Data;
using Ship_Game.Gameplay;
using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.UI;
using Ship_Game.Universe;
using Vector2 = SDGraphics.Vector2;
using Rectangle = SDGraphics.Rectangle;

namespace Ship_Game.GameScreens.Arena;

public enum ArenaMultiplayerLobbySurface
{
    StarGladiator,
    Authoritative4X,
}

enum ArenaMultiplayerSlotMode
{
    Human,
    AI,
    Closed,
}

public sealed class ArenaMultiplayerLobbyScreen : GameScreen
{
    public const int DefaultPort = 47377;
    public const int DefaultTurns = 600;
    public const int LiveAuthoritative4XMaxTurns = 0;
    public const int DefaultJoinPeerSlot = 3;
    public const int LastJoinPeerSlot = 9;
    const int AuthorityPeerId = Authoritative4XLobby.AuthorityPeerId;
    const int HostPlayerPeerId4X = 2;
    const string DefaultHost = "127.0.0.1";
    readonly Authoritative4XLobbyNetworkFlow LobbyFlow =
        new(HostPlayerPeerId4X, DefaultJoinPeerSlot, AuthorityPeerId);
    static readonly RaceDesignScreen.GameMode[] ModeOptions =
    {
        RaceDesignScreen.GameMode.Sandbox,
        RaceDesignScreen.GameMode.Corners,
        RaceDesignScreen.GameMode.Ring,
        RaceDesignScreen.GameMode.SmallClusters,
        RaceDesignScreen.GameMode.BigClusters,
        RaceDesignScreen.GameMode.SpiralTwoArm,
        RaceDesignScreen.GameMode.SpiralFourArm,
        RaceDesignScreen.GameMode.SpiralBarred,
        RaceDesignScreen.GameMode.SpiralMagellanic,
        RaceDesignScreen.GameMode.Elimination,
        RaceDesignScreen.GameMode.Random,
    };
    static readonly GalSize[] GalaxyOptions =
    {
        GalSize.Tiny,
        GalSize.Small,
        GalSize.Medium,
        GalSize.Large,
        GalSize.Huge,
        GalSize.Epic,
        GalSize.TrulyEpic,
    };
    static readonly RaceDesignScreen.StarsAbundance[] StarOptions =
    {
        RaceDesignScreen.StarsAbundance.VeryRare,
        RaceDesignScreen.StarsAbundance.Rare,
        RaceDesignScreen.StarsAbundance.Uncommon,
        RaceDesignScreen.StarsAbundance.Normal,
        RaceDesignScreen.StarsAbundance.Abundant,
        RaceDesignScreen.StarsAbundance.Crowded,
        RaceDesignScreen.StarsAbundance.Packed,
        RaceDesignScreen.StarsAbundance.SuperPacked,
    };
    static readonly GameDifficulty[] DifficultyOptions =
    {
        GameDifficulty.Normal,
        GameDifficulty.Hard,
        GameDifficulty.Brutal,
        GameDifficulty.Insane,
    };
    static readonly int[] TurnTimerOptions = { 1, 3, 5, 10 };
    static readonly int[] ExtraPlanetOptions = { 0, 1, 2, 3 };
    static readonly float[] RichnessOptions = { 0f, 1f, 2f, 3f, 5f };
    static readonly float[] DecayOptions = { 0.2f, 0.5f, 1f, 1.5f, 2f, 3f };
    static readonly float[] VolcanicOptions = { 0f, 0.5f, 1f, 1.5f, 2f, 3f };
    static readonly float[] MaintenanceOptions = { 1f, 1.2f, 1.5f, 2f };
    static readonly float[] GravityOptions = { 0f, 4000f, 8000f, 12000f, 16000f };
    static readonly float[] FtlOptions = { 0.25f, 0.5f, 0.75f, 1f };
    static readonly ExtraRemnantPresence[] RemnantOptions =
    {
        ExtraRemnantPresence.VeryRare,
        ExtraRemnantPresence.Rare,
        ExtraRemnantPresence.Normal,
        ExtraRemnantPresence.More,
        ExtraRemnantPresence.MuchMore,
        ExtraRemnantPresence.Everywhere,
    };

    readonly object Sync = new();
    readonly ArenaMultiplayerLobbySurface Surface;
    UITextEntry HostEntry;
    UITextEntry PortEntry;
    UITextEntry SeedEntry;
    UITextEntry SpeedEntry;
    TcpLockstepTransport Transport;
    ArenaMultiplayerTelemetry LobbyTelemetry;
    ArenaMultiplayerRole? LocalRole;
    LobbyPeer LocalPeer = new() { PlayerName = "Local", RacePreference = "United", LoadoutTrait = "Wingmates" };
    LobbyPeer RemotePeer = new() { PlayerName = "Remote", RacePreference = "-", LoadoutTrait = "-", Ready = false };
    readonly Dictionary<int, LobbyPeer> RemotePeers = new();
    readonly Dictionary<int, ArenaMultiplayerSlotMode> SlotModes = new();
    readonly HashSet<int> AcceptedStartPeers = new();
    int JoinPeerSlot = DefaultJoinPeerSlot;
    SessionStartMessage Pending4XStart;
    SessionStartMessage PendingHostStart;
    Action PendingLobbyAction;
    ArenaMultiplayerRunResult LastResult;
    Authoritative4XLobbySelfTestResult Last4XSelfTestResult;
    string StatusText;
    string[] RaceOptions = Array.Empty<string>();
    string[] TraitOptions = Array.Empty<string>();
    int RaceIndex;
    int TraitIndex;
    int ModeIndex;
    int GalaxyIndex;
    int StarsIndex;
    int DifficultyIndex;
    int TurnTimerIndex = 2;
    int ExtraPlanetsIndex;
    int RichnessIndex;
    int DecayIndex = 2;
    int VolcanicIndex = 2;
    int MaintenanceIndex;
    int GravityIndex = 2;
    int FtlIndex = 3;
    int ExtraRemnantIndex = 2;
    bool StartPaused;
    bool JoinInProgress;
    bool ScreenExiting;
    bool Launching;
    ArenaRegularMultiplayerSettings RegularSettings = new();
    ArenaMultiplayerLobbyConfig PersistentConfig;

    public ArenaMultiplayerLobbyScreen(
        ArenaMultiplayerLobbySurface surface = ArenaMultiplayerLobbySurface.StarGladiator)
        : base(null, toPause: null)
    {
        Surface = surface;
        StatusText = surface == ArenaMultiplayerLobbySurface.Authoritative4X
            ? "Idle. Host or join an authoritative 4X game."
            : "Idle. Host, join, ready up, then host launches.";
        CanEscapeFromScreen = true;
        TransitionOnTime  = 0.2f;
        TransitionOffTime = 0.2f;
        ApplyPersistentConfig(ArenaMultiplayerLobbyConfig.Load());
        ApplySlotModesFromConfig(PersistentConfig?.SlotModes, RegularSettings.NumOpponents);
    }

    public ArenaMultiplayerLobbySurface SurfaceMode => Surface;
    public string HeaderTitleForHeadless => Surface == ArenaMultiplayerLobbySurface.Authoritative4X
        ? "STARDIVE MULTIPLAYER"
        : "STAR GLADIATOR";
    public string HeaderSubtitleForHeadless => Surface == ArenaMultiplayerLobbySurface.Authoritative4X
        ? "AUTHORITATIVE 4X LOBBY"
        : "MULTIPLAYER LOBBY";
    public string CurrentStatus
    {
        get { lock (Sync) return StatusText; }
    }

    public bool IsRunning => Launching;
    public ArenaMultiplayerRunResult LatestResult => LastResult;
    public Authoritative4XLobbySelfTestResult Latest4XSelfTestResult => Last4XSelfTestResult;
    public bool LocalReady => LocalPeer.Ready;
    public bool RemoteReady => RemotePeer.Ready;
    public string LocalRace => LocalPeer.RacePreference;
    public string LocalLoadoutTrait => LocalPeer.LoadoutTrait;
    public string LocalTraitOptions => LocalPeer.TraitOptions;
    public string RemoteRace => RemotePeer.RacePreference;
    public string RemoteLoadoutTrait => RemotePeer.LoadoutTrait;
    public string RemoteTraitOptions => RemotePeer.TraitOptions;
    public string HostForHeadless => HostEntry?.Text?.Trim().NotEmpty() == true
        ? HostEntry.Text.Trim()
        : PersistentConfig?.Host ?? DefaultHost;
    public int PortForHeadless => ParsePort();
    public int SeedForHeadless => ParseSeed();
    public float SpeedForHeadless => ParseSpeed();
    public int JoinPeerSlotForHeadless => JoinPeerSlot;
    public int ConnectedPlayerCountForHeadless => 1 + RemotePeers.Count;
    public int EffectiveOpponentCountForHeadless => EffectiveOpponentCountForStart();
    public string SlotModeForHeadless(int peerId) => SlotModeLabel(SlotMode(peerId));
    public string SlotModesForHeadless => EncodeSlotModes();
    public bool HasTurnsFieldForHeadless => false;
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
            ExtraRemnant = s.ExtraRemnant,
            Difficulty = s.Difficulty,
            NumOpponents = s.NumOpponents,
            Pace = s.Pace,
            TurnTimer = s.TurnTimer,
            ExtraPlanets = s.ExtraPlanets,
            CustomMineralDecay = s.CustomMineralDecay,
            VolcanicActivity = s.VolcanicActivity,
            StartingPlanetRichnessBonus = s.StartingPlanetRichnessBonus,
            ShipMaintenanceMultiplier = s.ShipMaintenanceMultiplier,
            FTLModifier = s.FTLModifier,
            EnemyFTLModifier = s.EnemyFTLModifier,
            GravityWellRange = s.GravityWellRange,
            GameSpeed = s.GameSpeed,
            StartPaused = s.StartPaused,
            AIUsesPlayerDesigns = s.AIUsesPlayerDesigns,
            UseUpkeepByHullSize = s.UseUpkeepByHullSize,
            DisableRemnantStory = s.DisableRemnantStory,
            EnableRandomizedAIFleetSizes = s.EnableRandomizedAIFleetSizes,
            DisableAlternateAITraits = s.DisableAlternateAITraits,
            DisablePirates = s.DisablePirates,
            DisableResearchStations = s.DisableResearchStations,
            DisableMiningOps = s.DisableMiningOps,
        };
        LocalPeer.RacePreference = localRace;
        LocalPeer.TraitOptions = NormalizeTraitSelection(localTraits);
        LocalPeer.LoadoutTrait = ArenaStartArchetype.Wingmates.ToString();
        RemotePeer.RacePreference = remoteRace;
        RemotePeer.TraitOptions = NormalizeTraitSelection(remoteTraits);
        RemotePeer.LoadoutTrait = ArenaStartArchetype.Wingmates.ToString();
        RemotePeer.PlayerName = "Join";
        RemotePeer.Ready = true;
        RemotePeers.Clear();
        RemotePeers[DefaultJoinPeerSlot] = RemotePeer;
        InitializeSlotModesFromOpponentCount(s.NumOpponents);
        SlotModes[DefaultJoinPeerSlot] = ArenaMultiplayerSlotMode.Human;
        StartPaused = s.StartPaused;
        if (SeedEntry != null)
            SeedEntry.Text = s.GenerationSeed.ToString(CultureInfo.InvariantCulture);
        if (SpeedEntry != null)
            SpeedEntry.Text = s.GameSpeed.ToString(CultureInfo.InvariantCulture);
        SavePersistentConfig();
    }

    public override void LoadContent()
    {
        RemoveAll();
        RaceOptions = AvailableRacePreferences();
        TraitOptions = AvailableTraitOptions();
        RaceIndex = Math.Max(0, Array.IndexOf(RaceOptions, LocalPeer.RacePreference));
        TraitIndex = TraitCursorIndex(LocalPeer.TraitOptions);
        ModeIndex = Math.Max(0, Array.IndexOf(ModeOptions, RegularSettings.Mode));
        GalaxyIndex = Math.Max(0, Array.IndexOf(GalaxyOptions, RegularSettings.GalaxySize));
        StarsIndex = Math.Max(0, Array.IndexOf(StarOptions, RegularSettings.StarsCount));
        DifficultyIndex = Math.Max(0, Array.IndexOf(DifficultyOptions, RegularSettings.Difficulty));
        TurnTimerIndex = Math.Max(0, Array.IndexOf(TurnTimerOptions, RegularSettings.TurnTimer));
        ExtraPlanetsIndex = Math.Max(0, Array.IndexOf(ExtraPlanetOptions, RegularSettings.ExtraPlanets));
        RichnessIndex = Math.Max(0, Array.IndexOf(RichnessOptions, RegularSettings.StartingPlanetRichnessBonus));
        DecayIndex = Math.Max(0, Array.IndexOf(DecayOptions, RegularSettings.CustomMineralDecay));
        VolcanicIndex = Math.Max(0, Array.IndexOf(VolcanicOptions, RegularSettings.VolcanicActivity));
        MaintenanceIndex = Math.Max(0, Array.IndexOf(MaintenanceOptions, RegularSettings.ShipMaintenanceMultiplier));
        GravityIndex = Math.Max(0, Array.IndexOf(GravityOptions, RegularSettings.GravityWellRange));
        FtlIndex = Math.Max(0, Array.IndexOf(FtlOptions, RegularSettings.FTLModifier));
        ExtraRemnantIndex = Math.Max(0, Array.IndexOf(RemnantOptions, RegularSettings.ExtraRemnant));
        ApplyLocalSelection();

        Vector2 c = ScreenCenter;
        var panel = new RectF(c.X - 440, c.Y - 330, 880, 660);
        Add(ArenaTheme.Panel(panel));
        Add(ArenaTheme.ArenaTitle(new Vector2(panel.X + 24, panel.Y + 24), HeaderTitleForHeadless));
        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 24, panel.Y + 74), HeaderSubtitleForHeadless));

        AddField(panel.X + 24, panel.Y + 108, "HOST", HostForHeadless, out HostEntry, allowPeriod: true, maxChars: 64, "arena_mp_host_entry");
        AddField(panel.X + 24, panel.Y + 164, "PORT", ParsePort().ToString(CultureInfo.InvariantCulture), out PortEntry, allowPeriod: false, maxChars: 6, "arena_mp_port_entry");
        AddField(panel.X + 210, panel.Y + 164, "SEED", ParseSeed().ToString(CultureInfo.InvariantCulture), out SeedEntry, allowPeriod: false, maxChars: 9, "arena_mp_seed_entry");
        AddField(panel.X + 396, panel.Y + 164, "SPEED", ParseSpeed().ToString(CultureInfo.InvariantCulture), out SpeedEntry, allowPeriod: true, maxChars: 4, "arena_mp_speed_entry");

        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 24, panel.Y + 206), "PLAYER SLOTS"));
        for (int peerSlot = HostPlayerPeerId4X; peerSlot <= LastJoinPeerSlot; ++peerSlot)
        {
            int index = peerSlot - HostPlayerPeerId4X;
            int col = index % 4;
            int row = index / 4;
            AddSlotCard(new RectF(panel.X + 24 + col * 208f, panel.Y + 232 + row * 62f, 198, 56), peerSlot);
        }

        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 24, panel.Y + 364), "SETUP"));
        UIList setup = AddList(new Vector2(panel.X + 24, panel.Y + 392));
        setup.Direction = new Vector2(1f, 0f);
        setup.Padding = new Vector2(8f, 8f);
        setup.LayoutStyle = ListLayoutStyle.ResizeList;
        UIButton race = ArenaTheme.AddPillButton(setup, "", _ => CycleRace(), 152f);
        race.Name = "arena_mp_race";
        race.DynamicText = () => $"RACE {LocalPeer.RacePreference}";
        UIButton trait = ArenaTheme.AddPillButton(setup, "", _ => CycleTrait(), 148f);
        trait.Name = "arena_mp_trait";
        trait.DynamicText = () => $"TRAIT {CurrentTraitLabel()}";
        UIButton traitToggle = ArenaTheme.AddPillButton(setup, "", _ => ToggleTrait(), 142f);
        traitToggle.Name = "arena_mp_trait_toggle";
        traitToggle.DynamicText = TraitToggleLabel;
        UIButton mode = ArenaTheme.AddPillButton(setup, "", _ => CycleMode(), 122f);
        mode.Name = "arena_mp_mode";
        mode.DynamicText = () => $"MODE {ShortModeName(RegularSettings.Mode)}";
        UIButton galaxy = ArenaTheme.AddPillButton(setup, "", _ => CycleGalaxy(), 96f);
        galaxy.Name = "arena_mp_regular_settings";
        galaxy.DynamicText = () => $"SIZE {RegularSettings.GalaxySize.ToString().ToUpperInvariant()}";
        UIButton stars = ArenaTheme.AddPillButton(setup, "", _ => CycleStars(), 118f);
        stars.Name = "arena_mp_stars";
        stars.DynamicText = () => $"STARS {RegularSettings.StarsCount.ToString().ToUpperInvariant()}";

        UIList setup2 = AddList(new Vector2(panel.X + 24, panel.Y + 430));
        setup2.Direction = new Vector2(1f, 0f);
        setup2.Padding = new Vector2(8f, 8f);
        setup2.LayoutStyle = ListLayoutStyle.ResizeList;
        UIButton difficulty = ArenaTheme.AddPillButton(setup2, "", _ => CycleDifficulty(), 110f);
        difficulty.Name = "arena_mp_difficulty";
        difficulty.DynamicText = () => $"DIFF {RegularSettings.Difficulty.ToString().ToUpperInvariant()}";
        UIButton ai = ArenaTheme.AddPillButton(setup2, "", _ => CycleOpponents(), 88f);
        ai.Name = "arena_mp_opponents";
        ai.DynamicText = () => $"EMP {RegularSettings.NumOpponents + 1}";
        UIButton richness = ArenaTheme.AddPillButton(setup2, "", _ => CycleRichness(), 142f);
        richness.Name = "arena_mp_richness";
        richness.DynamicText = () => $"RICH {RegularSettings.StartingPlanetRichnessBonus:0.#}";
        UIButton extraPlanets = ArenaTheme.AddPillButton(setup2, "", _ => CycleExtraPlanets(), 104f);
        extraPlanets.Name = "arena_mp_extra_planets";
        extraPlanets.DynamicText = () => $"EXTRA {RegularSettings.ExtraPlanets}";
        UIButton remnants = ArenaTheme.AddPillButton(setup2, "", _ => CycleRemnants(), 128f);
        remnants.Name = "arena_mp_remnants";
        remnants.DynamicText = () => $"REM {ShortRemnantName(RegularSettings.ExtraRemnant)}";
        UIButton pace = ArenaTheme.AddPillButton(setup2, "", _ => CyclePace(), 112f);
        pace.Name = "arena_mp_pace";
        pace.DynamicText = () => $"PACE {RegularSettings.Pace:0.#}X";

        UIList setup3 = AddList(new Vector2(panel.X + 24, panel.Y + 468));
        setup3.Direction = new Vector2(1f, 0f);
        setup3.Padding = new Vector2(8f, 8f);
        setup3.LayoutStyle = ListLayoutStyle.ResizeList;
        UIButton turn = ArenaTheme.AddPillButton(setup3, "", _ => CycleTurnTimer(), 104f);
        turn.Name = "arena_mp_turn_timer";
        turn.DynamicText = () => $"TURN {RegularSettings.TurnTimer}S";
        UIButton decay = ArenaTheme.AddPillButton(setup3, "", _ => CycleDecay(), 112f);
        decay.Name = "arena_mp_decay";
        decay.DynamicText = () => $"DECAY {RegularSettings.CustomMineralDecay:0.#}";
        UIButton volcano = ArenaTheme.AddPillButton(setup3, "", _ => CycleVolcanos(), 112f);
        volcano.Name = "arena_mp_volcanos";
        volcano.DynamicText = () => $"VOLC {RegularSettings.VolcanicActivity:0.#}";
        UIButton maint = ArenaTheme.AddPillButton(setup3, "", _ => CycleMaintenance(), 118f);
        maint.Name = "arena_mp_maintenance";
        maint.DynamicText = () => $"MAINT {RegularSettings.ShipMaintenanceMultiplier:0.#}";
        UIButton ftl = ArenaTheme.AddPillButton(setup3, "", _ => CycleFtl(), 110f);
        ftl.Name = "arena_mp_ftl";
        ftl.DynamicText = () => $"FTL {RegularSettings.FTLModifier:0.##}";
        UIButton gravity = ArenaTheme.AddPillButton(setup3, "", _ => CycleGravity(), 110f);
        gravity.Name = "arena_mp_gravity";
        gravity.DynamicText = () => $"GW {RegularSettings.GravityWellRange:0}";

        UIList setup4 = AddList(new Vector2(panel.X + 24, panel.Y + 506));
        setup4.Direction = new Vector2(1f, 0f);
        setup4.Padding = new Vector2(8f, 8f);
        setup4.LayoutStyle = ListLayoutStyle.ResizeList;
        UIButton pause = ArenaTheme.AddPillButton(setup4, "", _ => ToggleStartPaused(), 126f);
        pause.Name = "arena_mp_start_paused";
        pause.DynamicText = () => StartPaused ? "START PAUSED" : "START LIVE";
        UIButton slot = ArenaTheme.AddPillButton(setup4, "", _ => CycleJoinSlot(), 92f);
        slot.Name = "arena_mp_peer_slot";
        slot.DynamicText = () => LocalRole == ArenaMultiplayerRole.Host ? "SLOT HOST" : $"SLOT P{JoinPeerSlot}";
        UIButton pirates = ArenaTheme.AddPillButton(setup4, "", _ => TogglePirates(), 118f);
        pirates.Name = "arena_mp_pirates";
        pirates.DynamicText = () => RegularSettings.DisablePirates ? "PIRATES OFF" : "PIRATES ON";
        UIButton story = ArenaTheme.AddPillButton(setup4, "", _ => ToggleRemnantStory(), 118f);
        story.Name = "arena_mp_remnant_story";
        story.DynamicText = () => RegularSettings.DisableRemnantStory ? "STORY OFF" : "STORY ON";
        UIButton ops = ArenaTheme.AddPillButton(setup4, "", _ => ToggleStationOps(), 118f);
        ops.Name = "arena_mp_station_ops";
        ops.DynamicText = StationOpsLabel;
        UIButton rules = ArenaTheme.AddPillButton(setup4, "", _ => CycleAIRules(), 126f);
        rules.Name = "arena_mp_ai_rules";
        rules.DynamicText = AIRulesLabel;

        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 24, panel.Y + 534), "STATUS"));
        for (int i = 0; i < 3; ++i)
        {
            int line = i;
            Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 560 + i * 18),
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
        entry.OnTextChanged = _ => SavePersistentConfig();
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

    void AddSlotCard(RectF rect, int peerId)
    {
        UIPanel card = ArenaTheme.Card(rect);
        card.Name = $"arena_mp_slot_{peerId}";
        Add(card);
        Add(new UILabel(new Vector2(rect.X + 8, rect.Y + 7), "", ArenaTheme.BodySmallFont, ArenaTheme.Amber)
        {
            DynamicText = _ => SlotTitle(peerId),
        });
        Add(new UILabel(new Vector2(rect.X + 8, rect.Y + 25), "", ArenaTheme.BodySmallFont, ArenaTheme.TextPrimary)
        {
            DynamicText = _ => SlotStatus(peerId),
        });
        Add(new UILabel(new Vector2(rect.X + 8, rect.Y + 41), "", ArenaTheme.BodySmallFont, ArenaTheme.TextMuted)
        {
            DynamicText = _ => SlotDetail(peerId),
        });
        if (peerId == HostPlayerPeerId4X)
            return;

        UIButton mode = ArenaTheme.PillButton("", _ => CycleSlotMode(peerId), 58f, 20f);
        mode.Name = $"arena_mp_slot_mode_{peerId}";
        mode.Pos = new Vector2(rect.Right - 64, rect.Y + 6);
        mode.DynamicText = () => SlotModeLabel(SlotMode(peerId));
        Add(mode);

        UIButton kick = ArenaTheme.PillButton("KICK", _ => KickSlot(peerId), 58f, 20f);
        kick.Name = $"arena_mp_slot_kick_{peerId}";
        kick.Pos = new Vector2(rect.Right - 64, rect.Y + 30);
        kick.DynamicText = () => RemotePeers.ContainsKey(peerId) ? "KICK" : "-";
        Add(kick);
    }

    void StartSelfTest()
    {
        if (Surface == ArenaMultiplayerLobbySurface.Authoritative4X)
        {
            Last4XSelfTestResult = RunAuthoritative4XSelfTestForHeadless(ParseTurns());
            LastResult = null;
            SetStatus(Last4XSelfTestResult.Summary);
            if (Last4XSelfTestResult.Passed)
                GameAudio.AffirmativeClick();
            else
                GameAudio.NegativeClick();
            return;
        }

        LastResult = RunLocalSelfTestForHeadless(ParseTurns());
        Last4XSelfTestResult = null;
        SetStatus(Summarize(LastResult));
        GameAudio.AffirmativeClick();
    }

    void StartHost()
    {
        if (Transport != null || JoinInProgress)
        {
            SetStatus("Already hosting or joined. Back out to reset the socket.");
            GameAudio.NegativeClick();
            return;
        }

        LocalRole = ArenaMultiplayerRole.Host;
        LocalPeer.PlayerName = "Host";
        RemotePeers.Clear();
        RefreshPrimaryRemotePeer();
        ApplyLocalSelection();
        SavePersistentConfig();
        LobbyTelemetry?.Dispose();
        LobbyTelemetry = ArenaMultiplayerTelemetry.Start("Host", "lobby", CreateDefaultSettings(ParseTurns()));
        int port = ParsePort();
        try
        {
            Transport = TcpLockstepTransport.HostMulti(port);
        }
        catch (Exception ex)
        {
            LocalRole = null;
            LobbyTelemetry?.NetworkError(ex.Message);
            LobbyTelemetry?.Dispose();
            LobbyTelemetry = null;
            SetStatus($"HOST failed on port {port}: {ex.Message}");
            GameAudio.NegativeClick();
            return;
        }
        Transport.AddObserver(AuthorityPeerId, OnHostMessage);
        SendLocalLobby();
        SetStatus($"HOST listening on port {port}\nPick setup, ready, then launch when remote is ready.");
        LobbyTelemetry.Event("HOST_LISTEN", $"port={port}");
        GameAudio.AffirmativeClick();
    }

    void StartJoin()
    {
        if (Transport != null && LocalRole == ArenaMultiplayerRole.Join && !JoinInProgress)
            ResetJoinAttemptForRetry();

        if (Transport != null || JoinInProgress)
        {
            SetStatus("Already hosting or joined. Back out to reset the socket.");
            GameAudio.NegativeClick();
            return;
        }

        string host = HostForHeadless;
        LocalRole = ArenaMultiplayerRole.Join;
        LocalPeer.PlayerName = $"P{JoinPeerSlot}";
        RemotePeers.Clear();
        RefreshPrimaryRemotePeer();
        ApplyLocalSelection();
        SavePersistentConfig();
        LobbyTelemetry?.Dispose();
        LobbyTelemetry = ArenaMultiplayerTelemetry.Start("Join", "lobby", CreateDefaultSettings(ParseTurns()));
        int port = ParsePort();
        JoinInProgress = true;
        SetStatus($"JOIN connecting to {host}:{port}...");
        LobbyTelemetry.Event("JOIN_START", $"host={host} port={port}");
        Task.Run(() =>
        {
            if (TryCreateJoinTransport(host, port, JoinPeerSlot, AuthorityPeerId,
                    out TcpLockstepTransport joinedTransport, out string error))
            {
                QueueLobbyAction(() => CompleteJoin(host, port, joinedTransport));
            }
            else
            {
                QueueLobbyAction(() => FailJoin(host, port, error));
            }
        });
    }

    public static bool TryCreateJoinTransport(string host, int port, int localPeerId, int remotePeerId,
        out TcpLockstepTransport transport, out string error)
    {
        try
        {
            transport = TcpLockstepTransport.JoinAsPeer(host, port, localPeerId, remotePeerId);
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            transport = null;
            error = ex.Message;
            return false;
        }
    }

    void CompleteJoin(string host, int port, TcpLockstepTransport joinedTransport)
    {
        JoinInProgress = false;
        if (ScreenExiting || Transport != null)
        {
            joinedTransport?.Dispose();
            return;
        }

        Transport = joinedTransport;
        Transport.AddObserver(JoinPeerSlot, OnJoinMessage);
        Transport.Send(AuthorityPeerId, new SessionHelloMessage
        {
            FromPeer = JoinPeerSlot,
            PeerId = JoinPeerSlot,
            ProtocolVersion = ArenaMultiplayerSettings.ProtocolVersion,
            PlayerName = LocalPeer.PlayerName,
            BuildHash = ArenaMultiplayerPeerSignature.EnvironmentHash(),
            BuildSummary = ArenaMultiplayerPeerSignature.EnvironmentSummary(),
        });
        SendLocalLobby();
        SetStatus($"JOIN connected to {host}:{port} as P{JoinPeerSlot}\nPress ready; host launches the match.");
        LobbyTelemetry?.Event("JOIN_CONNECT", $"host={host} port={port} peer={JoinPeerSlot}");
        GameAudio.AffirmativeClick();
    }

    void FailJoin(string host, int port, string error)
    {
        JoinInProgress = false;
        LocalRole = null;
        Transport?.Dispose();
        Transport = null;
        LobbyTelemetry?.NetworkError(error);
        LobbyTelemetry?.Dispose();
        LobbyTelemetry = null;
        SetStatus($"FAILED: {error}\nNo response from {host}:{port}. Check host is listening, VPN/IP, and firewall.\nPress JOIN to retry.");
        GameAudio.NegativeClick();
    }

    void ResetJoinAttemptForRetry()
    {
        Transport?.Dispose();
        Transport = null;
        JoinInProgress = false;
        LocalRole = null;
        RemotePeers.Clear();
        RefreshPrimaryRemotePeer();
        LobbyTelemetry?.Dispose();
        LobbyTelemetry = null;
    }

    void QueueLobbyAction(Action action)
    {
        if (action == null)
            return;
        lock (Sync)
        {
            if (ScreenExiting)
                return;
            PendingLobbyAction += action;
        }
    }

    void DrainPendingLobbyActions()
    {
        Action action;
        lock (Sync)
        {
            action = PendingLobbyAction;
            PendingLobbyAction = null;
        }
        action?.Invoke();
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
        if (PendingHostStart != null)
        {
            SetStatus("Launch already sent. Waiting for client start acknowledgements.");
            GameAudio.NegativeClick();
            return;
        }
        if (!Transport.IsConnected && AISlotCount() == 0)
        {
            SetStatus("Waiting for a client connection.");
            GameAudio.NegativeClick();
            return;
        }
        if (EffectiveOpponentCountForStart() == 0)
        {
            SetStatus("Open at least one human or AI slot before launch.");
            GameAudio.NegativeClick();
            return;
        }
        if (!LocalPeer.Ready || RemotePeers.Any(kv => SlotMode(kv.Key) == ArenaMultiplayerSlotMode.Human && !kv.Value.Ready))
        {
            SetStatus("All connected players must be ready before launch.");
            GameAudio.NegativeClick();
            return;
        }

        foreach (KeyValuePair<int, LobbyPeer> remote in RemotePeers.OrderBy(kv => kv.Key))
        {
            string error = ArenaMultiplayerPeerSignature.ValidateEnvironment(remote.Value.BuildHash,
                remote.Value.BuildSummary, $"peer {remote.Key}");
            if (error.NotEmpty())
            {
                Transport.Send(remote.Key, new SessionErrorMessage { FromPeer = AuthorityPeerId, Error = error });
                SetStatus(error);
                LobbyTelemetry?.Event("PREFLIGHT_REJECT", error);
                GameAudio.NegativeClick();
                return;
            }
        }

        SessionStartMessage start;
        try
        {
            SavePersistentConfig();
            start = Build4XStartMessage();
        }
        catch (Exception e)
        {
            string startError = $"4X start failed: {e.Message}\n{Build4XStartDiagnostics()}";
            foreach (int peer in RemotePeers.Keys.OrderBy(p => p))
                Transport.Send(peer, new SessionErrorMessage { FromPeer = AuthorityPeerId, Error = startError });
            SetStatus(startError);
            LobbyTelemetry?.Event("START_BUILD_REJECT", startError);
            GameAudio.NegativeClick();
            return;
        }
        LobbyTelemetry?.Event("LAUNCH_HOST",
            $"mode=4x {Authoritative4XLobbyNetworkFlow.StartTelemetrySummary(start)}");
        PendingHostStart = start;
        AcceptedStartPeers.Clear();
        foreach (int peer in RemotePeers.Keys.OrderBy(p => p))
            Transport.Send(peer, start);
        if (RemotePeers.Count == 0)
        {
            LaunchVisible4X(Authoritative4XLiveRole.Host, start);
            return;
        }
        SetStatus("Launch sent. Waiting for client start acknowledgements.");
    }

    void OnHostMessage(LockstepMessage message)
    {
        if (message is SessionHelloMessage h && IsRemotePlayerPeer(h.PeerId))
        {
            if (!SlotAcceptsHuman(h.PeerId))
            {
                string slotError = $"Slot P{h.PeerId} is {SlotModeLabel(SlotMode(h.PeerId))}; choose an open HUMAN slot.";
                SetStatus(slotError);
                LobbyTelemetry?.Event("HELLO_SLOT_REJECT", slotError);
                Transport?.Send(h.PeerId, new SessionErrorMessage { FromPeer = AuthorityPeerId, Error = slotError });
                return;
            }
            string error = h.ProtocolVersion != ArenaMultiplayerSettings.ProtocolVersion
                ? $"Arena multiplayer protocol mismatch. Local {ArenaMultiplayerSettings.ProtocolVersion}, remote {h.ProtocolVersion}."
                : ArenaMultiplayerPeerSignature.ValidateEnvironment(h.BuildHash, h.BuildSummary, $"peer {h.PeerId}");
            if (error.NotEmpty())
            {
                SetStatus(error);
                LobbyTelemetry?.Event("HELLO_REJECT", error);
                Transport?.Send(h.PeerId, new SessionErrorMessage { FromPeer = AuthorityPeerId, Error = error });
            }
            else
            {
                LobbyPeer peer = EnsureRemotePeer(h.PeerId);
                peer.PlayerName = h.PlayerName.NotEmpty() ? h.PlayerName : $"P{h.PeerId}";
                peer.BuildHash = h.BuildHash ?? "";
                peer.BuildSummary = h.BuildSummary ?? "";
                LobbyTelemetry?.Event("HELLO", $"peer={h.PeerId} summary='{h.BuildSummary}'");
            }
        }
        if (message is SessionLobbyMessage lobby && IsRemotePlayerPeer(lobby.PeerId))
        {
            if (!SlotAcceptsHuman(lobby.PeerId))
            {
                string slotError = $"Slot P{lobby.PeerId} is {SlotModeLabel(SlotMode(lobby.PeerId))}; lobby update ignored.";
                SetStatus(slotError);
                LobbyTelemetry?.Event("REMOTE_LOBBY_SLOT_REJECT", slotError);
                Transport?.Send(lobby.PeerId, new SessionErrorMessage { FromPeer = AuthorityPeerId, Error = slotError });
                return;
            }
            RemotePeers[lobby.PeerId] = LobbyPeer.From(lobby, $"P{lobby.PeerId}");
            RefreshPrimaryRemotePeer();
            SyncOpponentCountFromSlots();
            SetStatus($"P{lobby.PeerId} lobby updated.\n{RemotePeers.Count + 1} players, {ReadyPlayerCount()}/{RemotePeers.Count + 1} ready.");
            LobbyTelemetry?.Event("REMOTE_LOBBY",
                $"peer={lobby.PeerId} ready={RemotePeers[lobby.PeerId].Ready} race='{RemotePeers[lobby.PeerId].RacePreference}' summary='{RemotePeers[lobby.PeerId].BuildSummary}'");
            SendLocalLobby();
        }
        if (message is SessionStartAckMessage ack && IsRemotePlayerPeer(ack.PeerId))
            HandleStartAck(ack);
        if (message is SessionErrorMessage e)
        {
            SetStatus(e.Error);
            LobbyTelemetry?.Event("SESSION_ERROR", e.Error);
        }
    }

    void HandleStartAck(SessionStartAckMessage ack)
    {
        if (PendingHostStart == null)
            return;
        string expected = Authoritative4XLobbyNetworkFlow.StartFingerprint(PendingHostStart);
        if (!string.Equals(ack.StartFingerprint, expected, StringComparison.Ordinal))
        {
            string error = $"P{ack.PeerId} acknowledged a different start payload.";
            SetStatus(error);
            LobbyTelemetry?.Event("START_ACK_REJECT", $"{error} expected={expected} actual={ack.StartFingerprint}");
            return;
        }
        if (!ack.Accepted)
        {
            string error = $"P{ack.PeerId} rejected launch: {ack.Error}";
            SetStatus(error);
            LobbyTelemetry?.Event("START_ACK_REJECT", error);
            return;
        }

        AcceptedStartPeers.Add(ack.PeerId);
        int[] required = RemotePeers.Keys.OrderBy(p => p).ToArray();
        LobbyTelemetry?.Event("START_ACK", $"peer={ack.PeerId} accepted={AcceptedStartPeers.Count}/{required.Length}");
        if (required.All(peer => AcceptedStartPeers.Contains(peer)))
        {
            SessionStartMessage start = PendingHostStart;
            PendingHostStart = null;
            SetStatus("All clients accepted launch. Starting authoritative game.");
            LaunchVisible4X(Authoritative4XLiveRole.Host, start);
        }
        else
        {
            SetStatus($"Launch accepted by {AcceptedStartPeers.Count}/{required.Length} clients.");
        }
    }

    void OnJoinMessage(LockstepMessage message)
    {
        if (message is SessionLobbyMessage lobby && lobby.PeerId == HostPlayerPeerId4X)
            RemotePeer = LobbyPeer.From(lobby, "Host");
        if (message is SessionStartMessage start)
        {
            string fingerprint = Authoritative4XLobbyNetworkFlow.StartFingerprint(start);
            string error = Validate4XStart(start);
            if (error.NotEmpty())
            {
                SetStatus(error);
                LobbyTelemetry?.Event("START_REJECT", error);
                Transport?.Send(AuthorityPeerId, new SessionStartAckMessage
                {
                    FromPeer = JoinPeerSlot,
                    PeerId = JoinPeerSlot,
                    Accepted = false,
                    StartFingerprint = fingerprint,
                    Error = error,
                });
                return;
            }
            LobbyTelemetry?.Event("START_RECEIVED",
                $"mode=4x {Authoritative4XLobbyNetworkFlow.StartTelemetrySummary(start)}");
            Transport?.Send(AuthorityPeerId, new SessionStartAckMessage
            {
                FromPeer = JoinPeerSlot,
                PeerId = JoinPeerSlot,
                Accepted = true,
                StartFingerprint = fingerprint,
            });
            Pending4XStart = start;
        }
        if (message is SessionErrorMessage e)
        {
            SetStatus(e.Error);
            LobbyTelemetry?.Event("SESSION_ERROR", e.Error);
            if (e.Error.StartsWith("Slot P", StringComparison.Ordinal))
            {
                Transport?.Dispose();
                Transport = null;
                LocalRole = null;
                JoinInProgress = false;
            }
        }
    }

    void SendLocalLobby()
    {
        if (Transport == null || LocalRole == null)
            return;
        int peerId = LocalRole == ArenaMultiplayerRole.Host
            ? HostPlayerPeerId4X
            : JoinPeerSlot;
        int[] toPeers = LocalRole == ArenaMultiplayerRole.Host
            ? RemotePeers.Keys.OrderBy(p => p).ToArray()
            : new[] { AuthorityPeerId };
        var message = new SessionLobbyMessage
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
        };
        foreach (int toPeer in toPeers)
            Transport.Send(toPeer, message);
    }

    void LaunchVisible4X(Authoritative4XLiveRole role, SessionStartMessage start)
    {
        if (Transport == null)
            return;
        Launching = true;
        PendingHostStart = null;
        AcceptedStartPeers.Clear();
        TcpLockstepTransport transport = Transport;
        Transport = null;
        LobbyTelemetry?.Event("LAUNCH_VISIBLE_4X",
            $"role={role} {Authoritative4XLobbyNetworkFlow.StartTelemetrySummary(start)}");

        Authoritative4XGeneratedGameStart generated = CreateGenerated4XGame(start);
        int localPeer = role == Authoritative4XLiveRole.Host ? HostPlayerPeerId4X : JoinPeerSlot;
        LobbyTelemetry?.Event("LIVE_ATTACH_4X",
            $"role={role} localPeer={localPeer} localEmpire={generated.EmpireIdForPeer(localPeer)} "
            + $"{Authoritative4XLobbyNetworkFlow.StartTelemetrySummary(start)} "
            + $"{Authoritative4XLobbyNetworkFlow.EmpireMapTelemetrySummary(generated.EmpireIdByPeer, generated.HumanEmpireIds)}");
        LobbyFlow.AttachLiveSession(generated, transport, localPeer, role, start);
        LobbyTelemetry?.Dispose();
        LobbyTelemetry = null;
        ScreenManager.GoToScreen(generated.AuthorityUniverse, clear3DObjects: true);
    }

    ArenaMultiplayerSettings BuildHostSettings()
        => new()
        {
            MatchSeed = ParseSeed(),
            RngSeed = (uint)ParseSeed() ^ 0xA12EA000u,
            InputDelay = 3,
            MaxTurns = LiveAuthoritative4XMaxTurns,
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
        Authoritative4XLobby lobby = Build4XLobbyForStart();
        return LobbyFlow.BuildStartMessage(lobby, ArenaMultiplayerSettings.ProtocolVersion,
            ArenaMultiplayerPeerSignature.EnvironmentHash(),
            ArenaMultiplayerPeerSignature.EnvironmentSummary(),
            maxTurns: LiveAuthoritative4XMaxTurns);
    }

    Authoritative4XGameSettings Build4XSettings()
        => new Authoritative4XGameSettings
        {
            GenerationSeed = ParseSeed(),
            Mode = RegularSettings.Mode,
            StarsCount = RegularSettings.StarsCount,
            GalaxySize = RegularSettings.GalaxySize,
            ExtraRemnant = RegularSettings.ExtraRemnant,
            Difficulty = RegularSettings.Difficulty,
            NumOpponents = Math.Max(RegularSettings.NumOpponents, EffectiveOpponentCountForStart()),
            Pace = RegularSettings.Pace,
            TurnTimer = RegularSettings.TurnTimer,
            ExtraPlanets = RegularSettings.ExtraPlanets,
            CustomMineralDecay = RegularSettings.CustomMineralDecay,
            VolcanicActivity = RegularSettings.VolcanicActivity,
            StartingPlanetRichnessBonus = RegularSettings.StartingPlanetRichnessBonus,
            ShipMaintenanceMultiplier = RegularSettings.ShipMaintenanceMultiplier,
            FTLModifier = RegularSettings.FTLModifier,
            EnemyFTLModifier = RegularSettings.EnemyFTLModifier,
            GravityWellRange = RegularSettings.GravityWellRange,
            GameSpeed = ParseSpeed(),
            StartPaused = StartPaused,
            AIUsesPlayerDesigns = RegularSettings.AIUsesPlayerDesigns,
            UseUpkeepByHullSize = RegularSettings.UseUpkeepByHullSize,
            DisableRemnantStory = RegularSettings.DisableRemnantStory,
            EnableRandomizedAIFleetSizes = RegularSettings.EnableRandomizedAIFleetSizes,
            DisableAlternateAITraits = RegularSettings.DisableAlternateAITraits,
            DisablePirates = RegularSettings.DisablePirates,
            DisableResearchStations = RegularSettings.DisableResearchStations,
            DisableMiningOps = RegularSettings.DisableMiningOps,
        }.Normalized(PlayerCountForStart());

    Authoritative4XLobby Build4XLobbyForStart()
    {
        var lobby = new Authoritative4XLobby(HostPlayerPeerId4X,
            LocalPeer.PlayerName.NotEmpty() ? LocalPeer.PlayerName : "Host");
        foreach (KeyValuePair<int, LobbyPeer> remote in RemotePeers
                     .Where(kv => SlotMode(kv.Key) == ArenaMultiplayerSlotMode.Human)
                     .OrderBy(kv => kv.Key))
            lobby.Join(remote.Key,
                remote.Value.PlayerName.NotEmpty() && remote.Value.PlayerName != "-"
                    ? remote.Value.PlayerName
                    : $"P{remote.Key}");
        RequireLobbyValid(lobby.SetSettings(HostPlayerPeerId4X, Build4XSettings()));
        RequireLobbyValid(lobby.SetPlayerSelection(HostPlayerPeerId4X, LocalPeer.RacePreference,
            Authoritative4XLobbyNetworkFlow.SplitTraitOptions(LocalPeer.TraitOptions)));
        foreach (KeyValuePair<int, LobbyPeer> remote in RemotePeers
                     .Where(kv => SlotMode(kv.Key) == ArenaMultiplayerSlotMode.Human)
                     .OrderBy(kv => kv.Key))
            RequireLobbyValid(lobby.SetPlayerSelection(remote.Key,
                remote.Value.RacePreference == "-" ? "" : remote.Value.RacePreference,
                Authoritative4XLobbyNetworkFlow.SplitTraitOptions(remote.Value.TraitOptions)));
        RequireLobbyValid(lobby.SetReady(HostPlayerPeerId4X, true));
        foreach (int peer in RemotePeers.Keys
                     .Where(peer => SlotMode(peer) == ArenaMultiplayerSlotMode.Human)
                     .OrderBy(p => p))
            RequireLobbyValid(lobby.SetReady(peer, true));
        return lobby;
    }

    static void RequireLobbyValid(Authoritative4XLobbyValidation validation)
    {
        if (validation == null)
            throw new InvalidOperationException("Lobby validation was missing.");
        if (!validation.Valid)
            throw new InvalidOperationException(validation.Reason);
    }

    string Validate4XStart(SessionStartMessage start)
    {
        string flowError = LobbyFlow.ValidateStartMessage(start, ArenaMultiplayerSettings.ProtocolVersion,
            localPeerId: JoinPeerSlot);
        if (flowError.NotEmpty())
            return $"{flowError}\n{Build4XStartDiagnostics(start)}";
        string error = ArenaMultiplayerPeerSignature.ValidateEnvironment(start.BuildHash, start.BuildSummary, "host");
        return error.NotEmpty() ? $"{error}\n{Build4XStartDiagnostics(start)}" : "";
    }

    Authoritative4XGeneratedGameStart CreateGenerated4XGame(SessionStartMessage start)
        => LobbyFlow.CreateGeneratedGame(start);

    string Build4XStartDiagnostics(SessionStartMessage start = null)
    {
        Authoritative4XGameSettings settings = start != null
            ? Authoritative4XLobbyNetworkFlow.SettingsFromStart(start)
                .Normalized(Authoritative4XLobbyNetworkFlow.PlayerCountFromStart(start))
            : Build4XSettings();
        string settingsHash = start?.SettingsHash ?? settings.SettingsHash;
        string hostRace = start?.HostRacePreference ?? LocalPeer.RacePreference;
        string joinRace = start?.JoinRacePreference ?? (RemotePeer.RacePreference == "-" ? "" : RemotePeer.RacePreference);
        int turns = start?.MaxTurns ?? LiveAuthoritative4XMaxTurns;
        string turnText = turns == 0 ? "indefinite" : turns.ToString(CultureInfo.InvariantCulture);
        return $"seed={settings.GenerationSeed} settings={settingsHash} "
               + $"host='{hostRace}' join='{joinRace}' turns={turnText}";
    }

    int PlayerCountForStart()
        => HumanPlayerCountForStart();

    public override void Update(float fixedDeltaTime)
    {
        base.Update(fixedDeltaTime);
        DrainPendingLobbyActions();
        TcpLockstepTransport transport = Transport;
        if (transport != null)
        {
            transport.Poll();
            if (!ReferenceEquals(Transport, transport))
                return;
            if (transport.LastError.NotEmpty())
            {
                string lastError = transport.LastError;
                if (LocalRole == ArenaMultiplayerRole.Join)
                {
                    ResetJoinAttemptForRetry();
                    SetStatus("NETWORK: " + lastError + "\nPress JOIN to retry.");
                    return;
                }

                SetStatus("NETWORK: " + lastError);
                LobbyTelemetry?.NetworkError(transport.LastError);
            }
            if (LocalRole == ArenaMultiplayerRole.Host && transport.IsConnected && CurrentStatus.StartsWith("HOST listening", StringComparison.Ordinal))
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
        SavePersistentConfig();
        SendLocalLobby();
    }

    void CycleTrait()
    {
        if (TraitOptions.Length == 0)
            return;
        TraitIndex = (TraitIndex + 1) % TraitOptions.Length;
    }

    void CycleMode()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        ModeIndex = (ModeIndex + 1) % ModeOptions.Length;
        RegularSettings.Mode = ModeOptions[ModeIndex];
        HostSettingChanged();
    }

    void ToggleTrait()
    {
        if (TraitOptions.Length == 0)
            return;
        string trait = CurrentTraitName();
        string updated = ToggleTraitSelection(LocalPeer.TraitOptions, trait, out bool accepted);
        if (!accepted)
        {
            SetStatus($"Trait budget/exclusion blocks {trait}.");
            GameAudio.NegativeClick();
            return;
        }
        LocalPeer.TraitOptions = updated;
        ApplyLocalSelection();
        LocalPeer.Ready = false;
        SavePersistentConfig();
        SendLocalLobby();
    }

    void CycleGalaxy()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        GalaxyIndex = (GalaxyIndex + 1) % GalaxyOptions.Length;
        RegularSettings.GalaxySize = GalaxyOptions[GalaxyIndex];
        HostSettingChanged();
    }

    void CycleStars()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        StarsIndex = (StarsIndex + 1) % StarOptions.Length;
        RegularSettings.StarsCount = StarOptions[StarsIndex];
        HostSettingChanged();
    }

    void CycleDifficulty()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        DifficultyIndex = (DifficultyIndex + 1) % DifficultyOptions.Length;
        RegularSettings.Difficulty = DifficultyOptions[DifficultyIndex];
        HostSettingChanged();
    }

    void CycleOpponents()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        int max = Authoritative4XGameSettings.MaxOpponentsAllowed();
        RegularSettings.NumOpponents = RegularSettings.NumOpponents >= max ? 1 : RegularSettings.NumOpponents + 1;
        InitializeSlotModesFromOpponentCount(RegularSettings.NumOpponents);
        foreach (int peer in RemotePeers.Keys)
            SlotModes[peer] = ArenaMultiplayerSlotMode.Human;
        HostSettingChanged();
    }

    void CycleRichness()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        RichnessIndex = (RichnessIndex + 1) % RichnessOptions.Length;
        RegularSettings.StartingPlanetRichnessBonus = RichnessOptions[RichnessIndex];
        HostSettingChanged();
    }

    void CycleExtraPlanets()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        ExtraPlanetsIndex = (ExtraPlanetsIndex + 1) % ExtraPlanetOptions.Length;
        RegularSettings.ExtraPlanets = ExtraPlanetOptions[ExtraPlanetsIndex];
        HostSettingChanged();
    }

    void CycleRemnants()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        ExtraRemnantIndex = (ExtraRemnantIndex + 1) % RemnantOptions.Length;
        RegularSettings.ExtraRemnant = RemnantOptions[ExtraRemnantIndex];
        HostSettingChanged();
    }

    void CycleTurnTimer()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        TurnTimerIndex = (TurnTimerIndex + 1) % TurnTimerOptions.Length;
        RegularSettings.TurnTimer = TurnTimerOptions[TurnTimerIndex];
        HostSettingChanged();
    }

    void CyclePace()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        RegularSettings.Pace += 0.5f;
        if (RegularSettings.Pace > 4f)
            RegularSettings.Pace = 1f;
        HostSettingChanged();
    }

    void CycleDecay()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        DecayIndex = (DecayIndex + 1) % DecayOptions.Length;
        RegularSettings.CustomMineralDecay = DecayOptions[DecayIndex];
        HostSettingChanged();
    }

    void CycleVolcanos()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        VolcanicIndex = (VolcanicIndex + 1) % VolcanicOptions.Length;
        RegularSettings.VolcanicActivity = VolcanicOptions[VolcanicIndex];
        HostSettingChanged();
    }

    void CycleMaintenance()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        MaintenanceIndex = (MaintenanceIndex + 1) % MaintenanceOptions.Length;
        RegularSettings.ShipMaintenanceMultiplier = MaintenanceOptions[MaintenanceIndex];
        HostSettingChanged();
    }

    void CycleFtl()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        FtlIndex = (FtlIndex + 1) % FtlOptions.Length;
        RegularSettings.FTLModifier = FtlOptions[FtlIndex];
        RegularSettings.EnemyFTLModifier = Math.Min(RegularSettings.FTLModifier, 0.5f);
        HostSettingChanged();
    }

    void CycleGravity()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        GravityIndex = (GravityIndex + 1) % GravityOptions.Length;
        RegularSettings.GravityWellRange = GravityOptions[GravityIndex];
        HostSettingChanged();
    }

    void TogglePirates()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        RegularSettings.DisablePirates = !RegularSettings.DisablePirates;
        HostSettingChanged();
    }

    void ToggleRemnantStory()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        RegularSettings.DisableRemnantStory = !RegularSettings.DisableRemnantStory;
        HostSettingChanged();
    }

    void ToggleStationOps()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        if (!RegularSettings.DisableResearchStations && !RegularSettings.DisableMiningOps)
            RegularSettings.DisableResearchStations = true;
        else if (RegularSettings.DisableResearchStations && !RegularSettings.DisableMiningOps)
            RegularSettings.DisableMiningOps = true;
        else
        {
            RegularSettings.DisableResearchStations = false;
            RegularSettings.DisableMiningOps = false;
        }
        HostSettingChanged();
    }

    void CycleAIRules()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        if (RegularSettings.AIUsesPlayerDesigns && !RegularSettings.DisableAlternateAITraits && !RegularSettings.EnableRandomizedAIFleetSizes && !RegularSettings.UseUpkeepByHullSize)
            RegularSettings.AIUsesPlayerDesigns = false;
        else if (!RegularSettings.AIUsesPlayerDesigns && !RegularSettings.DisableAlternateAITraits)
            RegularSettings.DisableAlternateAITraits = true;
        else if (!RegularSettings.EnableRandomizedAIFleetSizes)
            RegularSettings.EnableRandomizedAIFleetSizes = true;
        else if (!RegularSettings.UseUpkeepByHullSize)
            RegularSettings.UseUpkeepByHullSize = true;
        else
        {
            RegularSettings.AIUsesPlayerDesigns = true;
            RegularSettings.DisableAlternateAITraits = false;
            RegularSettings.EnableRandomizedAIFleetSizes = false;
            RegularSettings.UseUpkeepByHullSize = false;
        }
        HostSettingChanged();
    }

    void HostSettingChanged()
    {
        LocalPeer.Ready = false;
        SavePersistentConfig();
        SendLocalLobby();
    }

    void ToggleStartPaused()
    {
        if (HostSettingsAreLockedToRemote())
            return;
        StartPaused = !StartPaused;
        RegularSettings.StartPaused = StartPaused;
        HostSettingChanged();
    }

    void CycleJoinSlot()
    {
        if (Transport != null || JoinInProgress)
        {
            SetStatus("Peer slot is locked after hosting or joining.");
            GameAudio.NegativeClick();
            return;
        }
        JoinPeerSlot++;
        if (JoinPeerSlot > LastJoinPeerSlot)
            JoinPeerSlot = DefaultJoinPeerSlot;
        SavePersistentConfig();
        SetStatus($"Join slot set to P{JoinPeerSlot}. Each remote player needs a unique slot.");
        GameAudio.AffirmativeClick();
    }

    void ApplyLocalSelection()
    {
        LocalPeer.RacePreference = RaceOptions.Length == 0 ? "United" : RaceOptions[RaceIndex.Clamped(0, RaceOptions.Length - 1)];
        LocalPeer.LoadoutTrait = ArenaStartArchetype.Wingmates.ToString();
        LocalPeer.TraitOptions = NormalizeTraitSelection(LocalPeer.TraitOptions);
        RegularSettings.HostRacePreference = LocalPeer.RacePreference;
        RegularSettings.JoinRacePreference = RemotePeer.RacePreference == "-" ? "" : RemotePeer.RacePreference;
    }

    void InitializeSlotModesFromOpponentCount(int opponents)
    {
        ApplySlotModesFromConfig(DefaultSlotModesForOpponentCount(opponents), opponents);
    }

    void ApplySlotModesFromConfig(string encoded, int opponents)
    {
        string modes = NormalizeSlotModesForConfig(encoded, opponents);
        SlotModes.Clear();
        for (int peer = DefaultJoinPeerSlot; peer <= LastJoinPeerSlot; ++peer)
        {
            char code = modes[peer - DefaultJoinPeerSlot];
            SlotModes[peer] = code == 'A'
                ? ArenaMultiplayerSlotMode.AI
                : code == 'C'
                    ? ArenaMultiplayerSlotMode.Closed
                    : ArenaMultiplayerSlotMode.Human;
        }
    }

    public static string NormalizeSlotModesForConfig(string encoded, int opponents)
    {
        if (encoded.IsEmpty())
            return DefaultSlotModesForOpponentCount(opponents);

        char[] modes = new char[LastJoinPeerSlot - DefaultJoinPeerSlot + 1];
        for (int i = 0; i < modes.Length; ++i)
        {
            char code = i < encoded.Length ? char.ToUpperInvariant(encoded[i]) : 'C';
            modes[i] = code is 'H' or 'A' or 'C' ? code : 'C';
        }
        return new string(modes);
    }

    static string DefaultSlotModesForOpponentCount(int opponents)
    {
        int clampedOpponents = Math.Clamp(opponents, 1, LastJoinPeerSlot - HostPlayerPeerId4X);
        char[] modes = Enumerable.Repeat('C', LastJoinPeerSlot - DefaultJoinPeerSlot + 1).ToArray();
        modes[0] = 'H';
        for (int i = 1; i < clampedOpponents && i < modes.Length; ++i)
            modes[i] = 'A';
        return new string(modes);
    }

    string EncodeSlotModes()
    {
        char[] modes = new char[LastJoinPeerSlot - DefaultJoinPeerSlot + 1];
        for (int peer = DefaultJoinPeerSlot; peer <= LastJoinPeerSlot; ++peer)
        {
            modes[peer - DefaultJoinPeerSlot] = SlotMode(peer) switch
            {
                ArenaMultiplayerSlotMode.AI => 'A',
                ArenaMultiplayerSlotMode.Closed => 'C',
                _ => 'H',
            };
        }
        return new string(modes);
    }

    ArenaMultiplayerSlotMode SlotMode(int peerId)
    {
        if (peerId == HostPlayerPeerId4X)
            return ArenaMultiplayerSlotMode.Human;
        if (RemotePeers.ContainsKey(peerId))
            return ArenaMultiplayerSlotMode.Human;
        return SlotModes.TryGetValue(peerId, out ArenaMultiplayerSlotMode mode)
            ? mode
            : peerId == DefaultJoinPeerSlot
                ? ArenaMultiplayerSlotMode.Human
                : ArenaMultiplayerSlotMode.Closed;
    }

    int ConnectedHumanRemoteCount()
        => RemotePeers.Keys.Count(peer => SlotMode(peer) == ArenaMultiplayerSlotMode.Human);

    int AISlotCount()
        => Enumerable.Range(DefaultJoinPeerSlot, LastJoinPeerSlot - DefaultJoinPeerSlot + 1)
            .Count(peer => !RemotePeers.ContainsKey(peer) && SlotMode(peer) == ArenaMultiplayerSlotMode.AI);

    int EffectiveOpponentCountForStart()
        => Math.Clamp(ConnectedHumanRemoteCount() + AISlotCount(), 0,
            Authoritative4XGameSettings.MaxTotalMajorEmpires - 1);

    int HumanPlayerCountForStart()
        => 1 + ConnectedHumanRemoteCount();

    void SyncOpponentCountFromSlots()
    {
        int opponents = EffectiveOpponentCountForStart();
        RegularSettings.NumOpponents = Math.Max(1, opponents);
    }

    void CycleSlotMode(int peerId)
    {
        if (peerId == HostPlayerPeerId4X)
            return;
        if (LocalRole == ArenaMultiplayerRole.Join)
        {
            SetStatus("Host controls lobby slots.");
            GameAudio.NegativeClick();
            return;
        }

        ArenaMultiplayerSlotMode next = SlotMode(peerId) switch
        {
            ArenaMultiplayerSlotMode.Human => ArenaMultiplayerSlotMode.AI,
            ArenaMultiplayerSlotMode.AI => ArenaMultiplayerSlotMode.Closed,
            _ => ArenaMultiplayerSlotMode.Human,
        };
        SlotModes[peerId] = next;
        if (next != ArenaMultiplayerSlotMode.Human)
            KickSlot(peerId, next == ArenaMultiplayerSlotMode.AI ? "Slot converted to AI." : "Slot closed.");
        SyncOpponentCountFromSlots();
        LocalPeer.Ready = false;
        SavePersistentConfig();
        SendLocalLobby();
        SetStatus($"P{peerId} set to {SlotModeLabel(next)}.");
        GameAudio.AffirmativeClick();
    }

    void KickSlot(int peerId, string reason = "Kicked by host.")
    {
        if (peerId == HostPlayerPeerId4X)
            return;
        if (LocalRole == ArenaMultiplayerRole.Join)
        {
            SetStatus("Only the host can manage slots.");
            GameAudio.NegativeClick();
            return;
        }
        if (RemotePeers.Remove(peerId))
        {
            if (reason == "Kicked by host.")
                SlotModes[peerId] = ArenaMultiplayerSlotMode.Closed;
            Transport?.Send(peerId, new SessionErrorMessage
            {
                FromPeer = AuthorityPeerId,
                Error = $"Slot P{peerId}: {reason}",
            });
            AcceptedStartPeers.Remove(peerId);
            RefreshPrimaryRemotePeer();
            SyncOpponentCountFromSlots();
            SavePersistentConfig();
            SetStatus($"P{peerId} removed. Slot is {SlotModeLabel(SlotMode(peerId))}.");
            LobbyTelemetry?.Event("SLOT_KICK", $"peer={peerId} reason='{reason}' mode={SlotMode(peerId)}");
            GameAudio.AffirmativeClick();
        }
    }

    bool SlotAcceptsHuman(int peerId)
        => IsRemotePlayerPeer(peerId) && SlotMode(peerId) == ArenaMultiplayerSlotMode.Human;

    string SlotTitle(int peerId)
        => peerId == HostPlayerPeerId4X ? "P2 HOST" : $"P{peerId} {SlotModeLabel(SlotMode(peerId))}";

    string SlotStatus(int peerId)
    {
        if (peerId == HostPlayerPeerId4X)
            return $"{LocalPeer.PlayerName} | {(LocalPeer.Ready ? "READY" : "NOT READY")}";
        if (RemotePeers.TryGetValue(peerId, out LobbyPeer peer))
            return $"{peer.PlayerName} | {(peer.Ready ? "READY" : "NOT READY")}";
        return SlotMode(peerId) switch
        {
            ArenaMultiplayerSlotMode.AI => "AI empire",
            ArenaMultiplayerSlotMode.Closed => "Closed",
            _ => "Open",
        };
    }

    string SlotDetail(int peerId)
    {
        if (peerId == HostPlayerPeerId4X)
            return $"{LocalPeer.RacePreference} | {TraitLabel(LocalPeer.TraitOptions)}";
        if (RemotePeers.TryGetValue(peerId, out LobbyPeer peer))
            return $"{peer.RacePreference} | {TraitLabel(peer.TraitOptions)}";
        return SlotMode(peerId) switch
        {
            ArenaMultiplayerSlotMode.AI => "Counts as one AI opponent",
            ArenaMultiplayerSlotMode.Closed => "Not used at launch",
            _ => "Waiting for player",
        };
    }

    static string SlotModeLabel(ArenaMultiplayerSlotMode mode)
        => mode switch
        {
            ArenaMultiplayerSlotMode.AI => "AI",
            ArenaMultiplayerSlotMode.Closed => "CLOSED",
            _ => "HUMAN",
        };

    bool HostSettingsAreLockedToRemote()
    {
        if (LocalRole != ArenaMultiplayerRole.Join)
            return false;
        SetStatus("Host controls game settings.");
        GameAudio.NegativeClick();
        return true;
    }

    static bool IsRemotePlayerPeer(int peerId)
        => peerId >= DefaultJoinPeerSlot && peerId <= LastJoinPeerSlot;

    LobbyPeer EnsureRemotePeer(int peerId)
    {
        if (!RemotePeers.TryGetValue(peerId, out LobbyPeer peer))
        {
            SlotModes[peerId] = ArenaMultiplayerSlotMode.Human;
            peer = new LobbyPeer
            {
                PlayerName = $"P{peerId}",
                RacePreference = "-",
                LoadoutTrait = "-",
            };
            RemotePeers[peerId] = peer;
            RefreshPrimaryRemotePeer();
        }
        return peer;
    }

    void RefreshPrimaryRemotePeer()
    {
        RemotePeer = RemotePeers.OrderBy(kv => kv.Key).Select(kv => kv.Value).FirstOrDefault()
                     ?? new LobbyPeer
                     {
                         PlayerName = "Remote",
                         RacePreference = "-",
                         LoadoutTrait = "-",
                         Ready = false,
                     };
    }

    int ReadyPlayerCount()
        => (LocalPeer.Ready ? 1 : 0) + RemotePeers.Values.Count(p => p.Ready);

    int ParsePort()
    {
        if (int.TryParse(PortEntry?.Text, out int port))
            return port.Clamped(1, 65535);
        return (PersistentConfig?.Port ?? DefaultPort).Clamped(1, 65535);
    }

    int ParseTurns()
        => DefaultTurns;

    int ParseSeed()
        => int.TryParse(SeedEntry?.Text, out int seed)
            ? seed
            : RegularSettings.GenerationSeed != 0 ? RegularSettings.GenerationSeed
            : PersistentConfig?.Seed ?? 0x5EED;

    float ParseSpeed()
        => float.TryParse(SpeedEntry?.Text, out float speed)
            ? ArenaMultiplayerSettings.ClampGameSpeed(speed)
            : ArenaMultiplayerSettings.ClampGameSpeed(RegularSettings.GameSpeed);

    void ApplyPersistentConfig(ArenaMultiplayerLobbyConfig config)
    {
        PersistentConfig = (config ?? new ArenaMultiplayerLobbyConfig()).Normalized();
        RegularSettings = PersistentConfig.ToRegularSettings();
        LocalPeer.RacePreference = PersistentConfig.RacePreference;
        try
        {
            LocalPeer.TraitOptions = NormalizeTraitSelection(PersistentConfig.TraitOptions);
        }
        catch
        {
            LocalPeer.TraitOptions = PersistentConfig.TraitOptions ?? "";
        }
        LocalPeer.LoadoutTrait = ArenaStartArchetype.Wingmates.ToString();
        StartPaused = PersistentConfig.StartPaused;
        JoinPeerSlot = PersistentConfig.PeerSlot.Clamped(DefaultJoinPeerSlot, LastJoinPeerSlot);
    }

    void SavePersistentConfig()
    {
        PersistentConfig = ArenaMultiplayerLobbyConfig.FromScreen(this);
        ArenaMultiplayerLobbyConfig.Save(PersistentConfig);
    }

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

    public static Authoritative4XLobbySelfTestResult RunAuthoritative4XSelfTestForHeadless(int turns = 90)
    {
        string[] races = AvailableSelfTestRacePreferences();
        if (races.Length < 2)
        {
            return new Authoritative4XLobbySelfTestResult
            {
                Passed = false,
                FailureReason = "setup: authoritative 4X self-test needs two playable major races.",
                MaxTurns = turns.Clamped(30, 2000),
            };
        }

        var settings = new Authoritative4XGameSettings
        {
            GenerationSeed = 0x4A11E45,
            GalaxySize = GalSize.Tiny,
            StarsCount = RaceDesignScreen.StarsAbundance.Rare,
            Mode = RaceDesignScreen.GameMode.Sandbox,
            Difficulty = GameDifficulty.Normal,
            NumOpponents = 1,
            Pace = 1f,
            TurnTimer = 3,
            ExtraPlanets = 1,
            StartingPlanetRichnessBonus = 1f,
            GameSpeed = 1f,
            StartPaused = false,
        }.Normalized(2);

        var flow = new Authoritative4XLobbyNetworkFlow(HostPlayerPeerId4X, DefaultJoinPeerSlot, AuthorityPeerId);
        return flow.RunLoopbackSelfTest(settings, races[0], OneAffordableTraitOrEmpty(),
            races[1], Array.Empty<string>(), ArenaMultiplayerSettings.ProtocolVersion,
            ArenaMultiplayerPeerSignature.EnvironmentHash(),
            ArenaMultiplayerPeerSignature.EnvironmentSummary(),
            maxTurns: turns.Clamped(30, 2000));
    }

    static string[] AvailableSelfTestRacePreferences()
    {
        try
        {
            return ResourceManager.MajorRaces
                .Where(r => !r.IsFactionOrMinorRace)
                .Select(r => r.ArchetypeName.NotEmpty() ? r.ArchetypeName : r.Name)
                .Where(r => r.NotEmpty())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(r => r, StringComparer.Ordinal)
                .Take(2)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    static string[] OneAffordableTraitOrEmpty()
    {
        try
        {
            int points = new UniverseParams().RacialTraitPoints;
            string trait = ResourceManager.RaceTraits.TraitList
                .Where(t => t.Cost > 0 && t.Cost <= points)
                .OrderBy(t => t.Cost)
                .ThenBy(t => t.TraitName, StringComparer.Ordinal)
                .Select(t => t.TraitName)
                .FirstOrDefault();
            return trait.NotEmpty() ? new[] { trait } : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

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
            string[] traits = ResourceManager.RaceTraits.TraitList
                .Where(t => t != null && t.TraitName.NotEmpty())
                .Select(t => t.TraitName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToArray();
            return traits;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string[] AvailableTraitOptionsForHeadless()
        => AvailableTraitOptions();

    public static int TraitBudgetForHeadless()
        => TraitBudget();

    public static int TraitSelectionCostForHeadless(string traits)
        => TraitSelectionCost(traits);

    public static string ToggleTraitSelectionForHeadless(string selectedTraits, string trait, out bool accepted)
        => ToggleTraitSelection(selectedTraits, trait, out accepted);

    static int TraitBudget()
        => new UniverseParams().RacialTraitPoints;

    int TraitCursorIndex(string selectedTraits)
    {
        if (TraitOptions.Length == 0)
            return 0;
        string first = SplitTraitSelection(selectedTraits).FirstOrDefault();
        int index = first.NotEmpty() ? Array.IndexOf(TraitOptions, first) : -1;
        return index >= 0 ? index : 0;
    }

    string CurrentTraitName()
        => TraitOptions.Length == 0 ? "" : TraitOptions[TraitIndex.Clamped(0, TraitOptions.Length - 1)];

    string CurrentTraitLabel()
    {
        string trait = CurrentTraitName();
        if (trait.IsEmpty())
            return "NONE";
        return $"{ShortTraitName(trait)} {SignedTraitCost(trait)}".ToUpperInvariant();
    }

    string TraitToggleLabel()
    {
        string trait = CurrentTraitName();
        if (trait.IsEmpty())
            return "NO TRAITS";
        string verb = TraitSelectionContains(LocalPeer.TraitOptions, trait) ? "DROP"
            : CanAddTrait(LocalPeer.TraitOptions, trait) ? "ADD"
            : "FULL";
        return $"{verb} {TraitSelectionCost(LocalPeer.TraitOptions)}/{TraitBudget()}";
    }

    static string TraitLabel(string traits)
    {
        string[] selected = SplitTraitSelection(traits);
        if (selected.Length == 0)
            return $"NONE (0/{TraitBudget()})";
        string names = string.Join("+", selected.Select(ShortTraitName));
        return $"{names.ToUpperInvariant()} ({TraitSelectionCost(traits)}/{TraitBudget()})";
    }

    static string[] SplitTraitSelection(string traits)
        => Authoritative4XLobbyNetworkFlow.SplitTraitOptions(traits)
            .Where(t => t.NotEmpty())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(TraitOrder)
            .ThenBy(t => t, StringComparer.Ordinal)
            .ToArray();

    static string NormalizeTraitSelection(string traits)
    {
        string accepted = "";
        foreach (string trait in SplitTraitSelection(traits))
            accepted = ToggleTraitSelection(accepted, trait, out bool added);
        return accepted;
    }

    static string ToggleTraitSelection(string selectedTraits, string trait, out bool accepted)
    {
        accepted = false;
        RacialTraitOption option = FindTrait(trait);
        if (option == null)
            return NormalizeKnownTraitSelection(selectedTraits);

        string[] selected = SplitTraitSelection(selectedTraits)
            .Where(t => FindTrait(t) != null)
            .ToArray();
        if (selected.Contains(option.TraitName, StringComparer.Ordinal))
        {
            accepted = true;
            return JoinTraitSelection(selected.Where(t => !string.Equals(t, option.TraitName, StringComparison.Ordinal)));
        }

        string[] candidate = selected.Concat(new[] { option.TraitName }).ToArray();
        if (!TraitSelectionIsValid(candidate))
            return JoinTraitSelection(selected);

        accepted = true;
        return JoinTraitSelection(candidate);
    }

    static string NormalizeKnownTraitSelection(string traits)
        => JoinTraitSelection(SplitTraitSelection(traits).Where(t => FindTrait(t) != null));

    static bool CanAddTrait(string selectedTraits, string trait)
    {
        if (TraitSelectionContains(selectedTraits, trait))
            return true;
        string[] candidate = SplitTraitSelection(selectedTraits).Concat(new[] { trait }).ToArray();
        return TraitSelectionIsValid(candidate);
    }

    static bool TraitSelectionContains(string selectedTraits, string trait)
        => SplitTraitSelection(selectedTraits).Contains(trait, StringComparer.Ordinal);

    static bool TraitSelectionIsValid(string[] traits)
    {
        string[] selected = traits
            .Where(t => FindTrait(t) != null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (TraitSelectionCost(selected) > TraitBudget())
            return false;
        foreach (string name in selected)
        {
            RacialTraitOption trait = FindTrait(name);
            if (trait?.Excludes == null)
                continue;
            foreach (string excluded in trait.Excludes)
                if (selected.Contains(excluded, StringComparer.Ordinal))
                    return false;
        }
        return true;
    }

    static int TraitSelectionCost(string traits)
        => TraitSelectionCost(SplitTraitSelection(traits));

    static int TraitSelectionCost(IEnumerable<string> traits)
        => traits.Select(FindTrait).Where(t => t != null).Distinct()
            .Sum(t => t.Cost);

    static string JoinTraitSelection(IEnumerable<string> traits)
        => string.Join('|', traits
            .Where(t => FindTrait(t) != null)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(TraitOrder)
            .ThenBy(t => t, StringComparer.Ordinal));

    static RacialTraitOption FindTrait(string trait)
        => trait.IsEmpty() ? null : ResourceManager.RaceTraits.TraitList
            .FirstOrDefault(t => t != null && string.Equals(t.TraitName, trait, StringComparison.Ordinal));

    static int TraitOrder(string trait)
        => Array.IndexOf(AvailableTraitOptions(), trait) is int index && index >= 0 ? index : int.MaxValue;

    static string ShortTraitName(string trait)
        => trait.Length <= 12 ? trait : trait.Substring(0, 9) + "...";

    static string ShortModeName(RaceDesignScreen.GameMode mode)
        => mode switch
        {
            RaceDesignScreen.GameMode.SpiralTwoArm => "2ARM",
            RaceDesignScreen.GameMode.SpiralFourArm => "4ARM",
            RaceDesignScreen.GameMode.SpiralBarred => "BARRED",
            RaceDesignScreen.GameMode.SpiralMagellanic => "MAGELL",
            RaceDesignScreen.GameMode.SmallClusters => "SCLUST",
            RaceDesignScreen.GameMode.BigClusters => "BCLUST",
            RaceDesignScreen.GameMode.Elimination => "ELIM",
            _ => mode.ToString().ToUpperInvariant(),
        };

    static string ShortRemnantName(ExtraRemnantPresence presence)
        => presence switch
        {
            ExtraRemnantPresence.VeryRare => "VRARE",
            ExtraRemnantPresence.MuchMore => "MUCH",
            _ => presence.ToString().ToUpperInvariant(),
        };

    string StationOpsLabel()
    {
        if (RegularSettings.DisableResearchStations && RegularSettings.DisableMiningOps)
            return "OPS OFF";
        if (RegularSettings.DisableResearchStations)
            return "NO RST";
        if (RegularSettings.DisableMiningOps)
            return "NO MIN";
        return "OPS ON";
    }

    string AIRulesLabel()
    {
        if (!RegularSettings.AIUsesPlayerDesigns)
            return "NO AI DES";
        if (RegularSettings.DisableAlternateAITraits)
            return "AI BASE";
        if (RegularSettings.EnableRandomizedAIFleetSizes)
            return "AI FLEET";
        if (RegularSettings.UseUpkeepByHullSize)
            return "HULL UPK";
        return "AI RULES";
    }

    static string SignedTraitCost(string trait)
    {
        int cost = FindTrait(trait)?.Cost ?? 0;
        return cost >= 0 ? $"+{cost}" : cost.ToString(CultureInfo.InvariantCulture);
    }

    public override void ExitScreen()
    {
        lock (Sync)
        {
            ScreenExiting = true;
            JoinInProgress = false;
            PendingLobbyAction = null;
        }
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

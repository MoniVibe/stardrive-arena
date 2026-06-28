using System;
using System.IO;
using System.Text;
using SDUtils;
using Ship_Game.Data.Serialization;
using Ship_Game.Data.Yaml;
using Ship_Game.Data.YamlSerializer;
using Ship_Game.Multiplayer.Authoritative;

namespace Ship_Game.GameScreens.Arena;

[StarDataType]
public sealed class ArenaMultiplayerLobbyConfig
{
    public static string ConfigPathOverride;

    public const int CurrentVersion = 1;

    [StarData] public int Version = CurrentVersion;
    public string Host = "127.0.0.1";
    [StarData] public string HostEncoded = "";
    [StarData] public int Port = ArenaMultiplayerLobbyScreen.DefaultPort;
    [StarData] public int PeerSlot = ArenaMultiplayerLobbyScreen.DefaultJoinPeerSlot;
    [StarData] public string SlotModes = "";
    [StarData] public int Seed = 24237;
    [StarData] public float GameSpeed = 1f;
    [StarData] public string RacePreference = "United";
    [StarData] public string TraitOptions = "";
    [StarData] public RaceDesignScreen.GameMode Mode = RaceDesignScreen.GameMode.Sandbox;
    [StarData] public RaceDesignScreen.StarsAbundance StarsCount = RaceDesignScreen.StarsAbundance.Rare;
    [StarData] public GalSize GalaxySize = GalSize.Tiny;
    [StarData] public ExtraRemnantPresence ExtraRemnant = ExtraRemnantPresence.Normal;
    [StarData] public GameDifficulty Difficulty = GameDifficulty.Normal;
    [StarData] public int NumOpponents = 1;
    [StarData] public float Pace = 1f;
    [StarData] public int TurnTimer = 5;
    [StarData] public int ExtraPlanets;
    [StarData] public float CustomMineralDecay = 1f;
    [StarData] public float VolcanicActivity = 1f;
    [StarData] public float StartingPlanetRichnessBonus;
    [StarData] public float ShipMaintenanceMultiplier = 1f;
    [StarData] public float FTLModifier = 1f;
    [StarData] public float EnemyFTLModifier = 0.5f;
    [StarData] public float GravityWellRange = 8000f;
    [StarData] public bool StartPaused;
    [StarData] public bool AIUsesPlayerDesigns = true;
    [StarData] public bool UseUpkeepByHullSize;
    [StarData] public bool DisableRemnantStory;
    [StarData] public bool EnableRandomizedAIFleetSizes;
    [StarData] public bool DisableAlternateAITraits;
    [StarData] public bool DisablePirates;
    [StarData] public bool DisableResearchStations;
    [StarData] public bool DisableMiningOps;

    public static string ConfigPath => ConfigPathOverride.NotEmpty()
        ? ConfigPathOverride
        : Path.Combine(SavedGame.DefaultSaveGameFolder, "Arena Multiplayer Lobby.yaml");

    public ArenaMultiplayerLobbyConfig Normalized()
    {
        Version = CurrentVersion;
        if (HostEncoded.NotEmpty())
            Host = DecodeHost(HostEncoded);
        if (Host.IsEmpty())
            Host = "127.0.0.1";
        HostEncoded = EncodeHost(Host);
        Port = Math.Clamp(Port, 1, 65535);
        PeerSlot = Math.Clamp(PeerSlot, ArenaMultiplayerLobbyScreen.DefaultJoinPeerSlot,
            ArenaMultiplayerLobbyScreen.LastJoinPeerSlot);
        SlotModes = ArenaMultiplayerLobbyScreen.NormalizeSlotModesForConfig(SlotModes, NumOpponents);
        if (Seed == 0)
            Seed = 24237;
        GameSpeed = ArenaMultiplayerSettings.ClampGameSpeed(GameSpeed);
        if (RacePreference.IsEmpty())
            RacePreference = "United";
        NumOpponents = Math.Clamp(NumOpponents, 1, Authoritative4XGameSettings.MaxOpponentsAllowed());
        Pace = Math.Clamp(Pace, 1f, 10f);
        TurnTimer = Math.Clamp(TurnTimer, 1, 30);
        ExtraPlanets = Math.Clamp(ExtraPlanets, 0, 3);
        CustomMineralDecay = Math.Clamp(CustomMineralDecay, 0.2f, 3f);
        VolcanicActivity = Math.Clamp(VolcanicActivity, 0f, 3f);
        StartingPlanetRichnessBonus = Math.Clamp(StartingPlanetRichnessBonus, 0f, 5f);
        ShipMaintenanceMultiplier = Math.Clamp(ShipMaintenanceMultiplier, 1f, 2f);
        FTLModifier = Math.Clamp(FTLModifier, 0.1f, 1f);
        EnemyFTLModifier = Math.Clamp(EnemyFTLModifier, 0.1f, 1f);
        GravityWellRange = Math.Clamp(GravityWellRange, 0f, 16000f);
        TraitOptions ??= "";
        return this;
    }

    public ArenaRegularMultiplayerSettings ToRegularSettings()
        => new ArenaRegularMultiplayerSettings
        {
            GenerationSeed = Seed,
            HostRacePreference = RacePreference,
            Mode = Mode,
            StarsCount = StarsCount,
            GalaxySize = GalaxySize,
            ExtraRemnant = ExtraRemnant,
            Difficulty = Difficulty,
            NumOpponents = NumOpponents,
            Pace = Pace,
            TurnTimer = TurnTimer,
            ExtraPlanets = ExtraPlanets,
            CustomMineralDecay = CustomMineralDecay,
            VolcanicActivity = VolcanicActivity,
            StartingPlanetRichnessBonus = StartingPlanetRichnessBonus,
            ShipMaintenanceMultiplier = ShipMaintenanceMultiplier,
            FTLModifier = FTLModifier,
            EnemyFTLModifier = EnemyFTLModifier,
            GravityWellRange = GravityWellRange,
            GameSpeed = GameSpeed,
            StartPaused = StartPaused,
            AIUsesPlayerDesigns = AIUsesPlayerDesigns,
            UseUpkeepByHullSize = UseUpkeepByHullSize,
            DisableRemnantStory = DisableRemnantStory,
            EnableRandomizedAIFleetSizes = EnableRandomizedAIFleetSizes,
            DisableAlternateAITraits = DisableAlternateAITraits,
            DisablePirates = DisablePirates,
            DisableResearchStations = DisableResearchStations,
            DisableMiningOps = DisableMiningOps,
        }.Normalized();

    public static ArenaMultiplayerLobbyConfig FromScreen(ArenaMultiplayerLobbyScreen screen)
    {
        Authoritative4XGameSettings settings = screen?.Current4XSettingsForHeadless
                                               ?? new Authoritative4XGameSettings();
        return new ArenaMultiplayerLobbyConfig
        {
            Host = screen?.HostForHeadless ?? "127.0.0.1",
            Port = screen?.PortForHeadless ?? ArenaMultiplayerLobbyScreen.DefaultPort,
            PeerSlot = screen?.JoinPeerSlotForHeadless ?? ArenaMultiplayerLobbyScreen.DefaultJoinPeerSlot,
            SlotModes = screen?.SlotModesForHeadless ?? "",
            Seed = settings.GenerationSeed,
            GameSpeed = settings.GameSpeed,
            RacePreference = screen?.LocalRace ?? "United",
            TraitOptions = screen?.LocalTraitOptions ?? "",
            Mode = settings.Mode,
            StarsCount = settings.StarsCount,
            GalaxySize = settings.GalaxySize,
            ExtraRemnant = settings.ExtraRemnant,
            Difficulty = settings.Difficulty,
            NumOpponents = settings.NumOpponents,
            Pace = settings.Pace,
            TurnTimer = settings.TurnTimer,
            ExtraPlanets = settings.ExtraPlanets,
            CustomMineralDecay = settings.CustomMineralDecay,
            VolcanicActivity = settings.VolcanicActivity,
            StartingPlanetRichnessBonus = settings.StartingPlanetRichnessBonus,
            ShipMaintenanceMultiplier = settings.ShipMaintenanceMultiplier,
            FTLModifier = settings.FTLModifier,
            EnemyFTLModifier = settings.EnemyFTLModifier,
            GravityWellRange = settings.GravityWellRange,
            StartPaused = settings.StartPaused,
            AIUsesPlayerDesigns = settings.AIUsesPlayerDesigns,
            UseUpkeepByHullSize = settings.UseUpkeepByHullSize,
            DisableRemnantStory = settings.DisableRemnantStory,
            EnableRandomizedAIFleetSizes = settings.EnableRandomizedAIFleetSizes,
            DisableAlternateAITraits = settings.DisableAlternateAITraits,
            DisablePirates = settings.DisablePirates,
            DisableResearchStations = settings.DisableResearchStations,
            DisableMiningOps = settings.DisableMiningOps,
        }.Normalized();
    }

    public static ArenaMultiplayerLobbyConfig Load()
    {
        try
        {
            var fi = new FileInfo(ConfigPath);
            if (!fi.Exists)
                return new ArenaMultiplayerLobbyConfig().Normalized();

            ArenaMultiplayerLobbyConfig config = YamlParser.DeserializeOne<ArenaMultiplayerLobbyConfig>(fi);
            return (config ?? new ArenaMultiplayerLobbyConfig()).Normalized();
        }
        catch (Exception e)
        {
            Log.Warning($"Arena multiplayer lobby config load failed: {e.Message}");
            return new ArenaMultiplayerLobbyConfig().Normalized();
        }
    }

    public static bool Save(ArenaMultiplayerLobbyConfig config)
    {
        try
        {
            config ??= new ArenaMultiplayerLobbyConfig();
            config.Normalize();
            string path = ConfigPath;
            string dir = Path.GetDirectoryName(path);
            if (dir.NotEmpty())
                Directory.CreateDirectory(dir);
            YamlSerializer.SerializeOne(path, config);
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"Arena multiplayer lobby config save failed: {e.Message}");
            return false;
        }
    }

    void Normalize() => Normalized();

    static string EncodeHost(string host)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(host ?? ""));

    static string DecodeHost(string hostEncoded)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(hostEncoded ?? ""));
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}

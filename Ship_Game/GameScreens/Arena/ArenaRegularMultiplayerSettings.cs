using System;
using System.Globalization;
using System.Linq;
using SDUtils;
using SDUtils.Deterministic;
using Ship_Game.Determinism;
using Ship_Game.Determinism.Lockstep;
using Ship_Game.Gameplay;
using Ship_Game.GameScreens.NewGame;
using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.Universe;
using Ship_Game.Utils;
using static Ship_Game.RaceDesignScreen;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// Host-authored regular-game multiplayer setup. This is deliberately separate
/// from the Arena PvP fleet presets: it mirrors the normal new-game generator
/// knobs and creates a real BlackBox/Combined Arms sandbox.
/// </summary>
public sealed class ArenaRegularMultiplayerSettings
{
    public int GenerationSeed = 24237;
    public string HostRacePreference = "United";
    public string JoinRacePreference = "";
    public GameMode Mode = GameMode.Sandbox;
    public StarsAbundance StarsCount = StarsAbundance.Rare;
    public GalSize GalaxySize = GalSize.Tiny;
    public ExtraRemnantPresence ExtraRemnant = ExtraRemnantPresence.Normal;
    public GameDifficulty Difficulty = GameDifficulty.Normal;
    public int NumOpponents = 1;
    public float Pace = 1f;
    public int TurnTimer = 5;
    public int ExtraPlanets;
    public float CustomMineralDecay = 1f;
    public float VolcanicActivity = 1f;
    public float StartingPlanetRichnessBonus;
    public float ShipMaintenanceMultiplier = 1f;
    public float FTLModifier = 1f;
    public float EnemyFTLModifier = 0.5f;
    public float GravityWellRange = 8000f;
    public float GameSpeed = 1f;
    public bool StartPaused;
    public bool AIUsesPlayerDesigns = true;
    public bool UseUpkeepByHullSize;
    public bool DisableRemnantStory;
    public bool EnableRandomizedAIFleetSizes;
    public bool DisableAlternateAITraits;
    public bool DisablePirates;
    public bool DisableResearchStations;
    public bool DisableMiningOps;

    public string SettingsHash
    {
        get
        {
            var h = DetHash.New();
            h.AddInt(GenerationSeed);
            h.AddString(HostRacePreference ?? "");
            h.AddString(JoinRacePreference ?? "");
            h.AddInt((int)Mode);
            h.AddInt((int)StarsCount);
            h.AddInt((int)GalaxySize);
            h.AddInt((int)ExtraRemnant);
            h.AddInt((int)Difficulty);
            h.AddInt(NumOpponents);
            h.AddInt((int)(Pace * 1000f));
            h.AddInt(TurnTimer);
            h.AddInt(ExtraPlanets);
            h.AddInt((int)(CustomMineralDecay * 1000f));
            h.AddInt((int)(VolcanicActivity * 1000f));
            h.AddInt((int)(StartingPlanetRichnessBonus * 1000f));
            h.AddInt((int)(ShipMaintenanceMultiplier * 1000f));
            h.AddInt((int)(FTLModifier * 1000f));
            h.AddInt((int)(EnemyFTLModifier * 1000f));
            h.AddInt((int)(GravityWellRange * 1000f));
            h.AddInt((int)(ArenaMultiplayerSettings.ClampGameSpeed(GameSpeed) * 1000f));
            h.AddBool(StartPaused);
            h.AddBool(AIUsesPlayerDesigns);
            h.AddBool(UseUpkeepByHullSize);
            h.AddBool(DisableRemnantStory);
            h.AddBool(EnableRandomizedAIFleetSizes);
            h.AddBool(DisableAlternateAITraits);
            h.AddBool(DisablePirates);
            h.AddBool(DisableResearchStations);
            h.AddBool(DisableMiningOps);
            return "0x" + h.Value.ToString("X16", CultureInfo.InvariantCulture);
        }
    }

    public ArenaRegularMultiplayerSettings Normalized()
        => new()
        {
            GenerationSeed = GenerationSeed == 0 ? 24237 : GenerationSeed,
            HostRacePreference = ResolveRaceName(HostRacePreference, "United"),
            JoinRacePreference = ResolveRaceName(JoinRacePreference, ""),
            Mode = Mode,
            StarsCount = StarsCount,
            GalaxySize = GalaxySize,
            ExtraRemnant = ExtraRemnant,
            Difficulty = Difficulty,
            NumOpponents = Math.Clamp(NumOpponents, 1, Authoritative4XGameSettings.MaxOpponentsAllowed()),
            Pace = Math.Clamp(Pace, 1f, 10f),
            TurnTimer = Math.Clamp(TurnTimer, 1, 30),
            ExtraPlanets = Math.Clamp(ExtraPlanets, 0, 3),
            CustomMineralDecay = Math.Clamp(CustomMineralDecay, 0.2f, 3f),
            VolcanicActivity = Math.Clamp(VolcanicActivity, 0f, 3f),
            StartingPlanetRichnessBonus = Math.Clamp(StartingPlanetRichnessBonus, 0f, 5f),
            ShipMaintenanceMultiplier = Math.Clamp(ShipMaintenanceMultiplier, 1f, 2f),
            FTLModifier = Math.Clamp(FTLModifier, 0.1f, 1f),
            EnemyFTLModifier = Math.Clamp(EnemyFTLModifier, 0.1f, 1f),
            GravityWellRange = Math.Clamp(GravityWellRange, 0f, 16000f),
            GameSpeed = ArenaMultiplayerSettings.ClampGameSpeed(GameSpeed),
            StartPaused = StartPaused,
            AIUsesPlayerDesigns = AIUsesPlayerDesigns,
            UseUpkeepByHullSize = UseUpkeepByHullSize,
            DisableRemnantStory = DisableRemnantStory,
            EnableRandomizedAIFleetSizes = EnableRandomizedAIFleetSizes,
            DisableAlternateAITraits = DisableAlternateAITraits,
            DisablePirates = DisablePirates,
            DisableResearchStations = DisableResearchStations,
            DisableMiningOps = DisableMiningOps,
        };

    public UniverseParams ToUniverseParams()
    {
        ArenaRegularMultiplayerSettings s = Normalized();
        IEmpireData hostRace = FindRace(s.HostRacePreference) ?? ResourceManager.MajorRaces.FirstOrDefault();
        if (hostRace == null)
            throw new InvalidOperationException("No major races are loaded for regular multiplayer setup.");

        IEmpireData joinRace = FindRace(s.JoinRacePreference);
        var selected = new SDUtils.Array<IEmpireData>();
        if (joinRace != null && !SameRace(hostRace, joinRace))
            selected.Add(joinRace);

        (int numStars, float starMod) = GetNumStars(s.StarsCount, s.GalaxySize, s.NumOpponents);
        EmpireData playerData = hostRace.CreateInstance();
        playerData.DiplomaticPersonality ??= new DTrait();

        return new UniverseParams
        {
            PlayerData = playerData,
            Mode = s.Mode,
            StarsCount = s.StarsCount,
            GalaxySize = s.GalaxySize,
            ExtraRemnant = s.ExtraRemnant,
            Difficulty = s.Difficulty,
            NumSystems = numStars,
            NumOpponents = Math.Max(s.NumOpponents, selected.Count),
            StarsModifier = starMod,
            Pace = s.Pace,
            GenerationSeed = s.GenerationSeed,
            TurnTimer = s.TurnTimer,
            ExtraPlanets = s.ExtraPlanets,
            CustomMineralDecay = s.CustomMineralDecay,
            VolcanicActivity = s.VolcanicActivity,
            StartingPlanetRichnessBonus = s.StartingPlanetRichnessBonus,
            ShipMaintenanceMultiplier = s.ShipMaintenanceMultiplier,
            FTLModifier = s.FTLModifier,
            EnemyFTLModifier = s.EnemyFTLModifier,
            GravityWellRange = s.GravityWellRange,
            AIUsesPlayerDesigns = s.AIUsesPlayerDesigns,
            UseUpkeepByHullSize = s.UseUpkeepByHullSize,
            DisableRemnantStory = s.DisableRemnantStory,
            EnableRandomizedAIFleetSizes = s.EnableRandomizedAIFleetSizes,
            DisableAlternateAITraits = s.DisableAlternateAITraits,
            DisablePirates = s.DisablePirates,
            DisableResearchStations = s.DisableResearchStations,
            DisableMiningOps = s.DisableMiningOps,
            SelectedOpponents = selected,
        };
    }

    public UniverseScreen CreateUniverse(bool loadContent = true)
    {
        ArenaRegularMultiplayerSettings s = Normalized();
        ArenaEngineCapabilities.TrySeedGeneration(s.GenerationSeed);
        UniverseScreen screen = new UniverseGenerator(s.ToUniverseParams()).Generate();
        screen.CreateSimThread = false;
        screen.UState.Objects.EnableParallelUpdate = false;
        ArenaEngineCapabilities.TryEnableSeededRng(screen.UState, (uint)s.GenerationSeed ^ 0x4D505547u);
        if (loadContent)
            screen.LoadContent();
        ArenaEngineCapabilities.TryEnableSeededRng(screen.UState, (uint)s.GenerationSeed ^ 0x4D505547u);
        screen.UState.Paused = s.StartPaused;
        screen.UState.GameSpeed = s.GameSpeed;
        return screen;
    }

    public UniverseScreenLockstepSimulation CreateFullGameLockstepSimulation(
        DeterminismProfile profile = DeterminismProfile.ReplayWinX64Float)
        => new(CreateUniverse(), profile);

    public static string[] AvailableRacePreferences()
        => ResourceManager.MajorRaces
            .Select(r => r.ArchetypeName.NotEmpty() ? r.ArchetypeName : r.Name)
            .Where(r => r.NotEmpty())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();

    static string ResolveRaceName(string preference, string fallback)
    {
        IEmpireData race = FindRace(preference) ?? FindRace(fallback) ?? ResourceManager.MajorRaces.FirstOrDefault();
        return race?.ArchetypeName.NotEmpty() == true ? race.ArchetypeName : race?.Name ?? fallback;
    }

    static IEmpireData FindRace(string preference)
    {
        if (preference.IsEmpty())
            return null;
        return ResourceManager.MajorRaces.FirstOrDefault(r => SameName(r.ArchetypeName, preference) || SameName(r.Name, preference))
               ?? ResourceManager.FindEmpire(preference);
    }

    static bool SameRace(IEmpireData a, IEmpireData b)
        => a != null && b != null
           && (SameName(a.ArchetypeName, b.ArchetypeName) || SameName(a.Name, b.Name));

    static bool SameName(string a, string b)
        => a.NotEmpty() && b.NotEmpty() && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

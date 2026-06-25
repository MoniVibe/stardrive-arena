using System;
using System.Globalization;
using System.Linq;
using SDUtils;
using SDUtils.Deterministic;
using Ship_Game.Determinism;
using Ship_Game.Determinism.Lockstep;
using Ship_Game.Gameplay;
using Ship_Game.GameScreens.NewGame;
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
    public GameDifficulty Difficulty = GameDifficulty.Normal;
    public int NumOpponents = 1;
    public float Pace = 1f;
    public int TurnTimer = 5;
    public int ExtraPlanets;
    public float StartingPlanetRichnessBonus;
    public float GameSpeed = 1f;
    public bool StartPaused;

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
            h.AddInt((int)Difficulty);
            h.AddInt(NumOpponents);
            h.AddInt((int)(Pace * 1000f));
            h.AddInt(TurnTimer);
            h.AddInt(ExtraPlanets);
            h.AddInt((int)(StartingPlanetRichnessBonus * 1000f));
            h.AddInt((int)(ArenaMultiplayerSettings.ClampGameSpeed(GameSpeed) * 1000f));
            h.AddBool(StartPaused);
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
            Difficulty = Difficulty,
            NumOpponents = Math.Clamp(NumOpponents, 1, Math.Max(1, ResourceManager.MajorRaces.Count - 1)),
            Pace = Math.Clamp(Pace, 1f, 10f),
            TurnTimer = Math.Clamp(TurnTimer, 1, 30),
            ExtraPlanets = Math.Clamp(ExtraPlanets, 0, 3),
            StartingPlanetRichnessBonus = Math.Clamp(StartingPlanetRichnessBonus, 0f, 5f),
            GameSpeed = ArenaMultiplayerSettings.ClampGameSpeed(GameSpeed),
            StartPaused = StartPaused,
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
            Difficulty = s.Difficulty,
            NumSystems = numStars,
            NumOpponents = Math.Max(s.NumOpponents, selected.Count),
            StarsModifier = starMod,
            Pace = s.Pace,
            GenerationSeed = s.GenerationSeed,
            TurnTimer = s.TurnTimer,
            ExtraPlanets = s.ExtraPlanets,
            StartingPlanetRichnessBonus = s.StartingPlanetRichnessBonus,
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SDUtils;
using SDUtils.Deterministic;
using Ship_Game.Data;
using Ship_Game.Determinism;
using Ship_Game.GameScreens.NewGame;
using Ship_Game.Ships;
using Ship_Game.Universe;
using static Ship_Game.RaceDesignScreen;

namespace Ship_Game.Multiplayer.Authoritative;

/// <summary>
/// Host-authored 4X multiplayer game settings. These mirror the single-player new-game
/// knobs that flow into <see cref="UniverseParams"/> and <see cref="UniverseGenerator"/>.
/// </summary>
public sealed class Authoritative4XGameSettings
{
    public int GenerationSeed = 54545;
    public GameMode Mode = GameMode.Sandbox;
    public StarsAbundance StarsCount = StarsAbundance.Rare;
    public GalSize GalaxySize = GalSize.Tiny;
    public GameDifficulty Difficulty = GameDifficulty.Normal;
    public int NumOpponents = 1; // total non-player-one major empires: human opponents plus any AI
    public float Pace = 1f;
    public int TurnTimer = 5;
    public int ExtraPlanets;
    public float StartingPlanetRichnessBonus;
    public float GameSpeed = 1f;
    public bool StartPaused;
    public bool EliminationMode;

    public string SettingsHash
    {
        get
        {
            var h = DetHash.New();
            h.AddInt(GenerationSeed);
            h.AddInt((int)Mode);
            h.AddInt((int)StarsCount);
            h.AddInt((int)GalaxySize);
            h.AddInt((int)Difficulty);
            h.AddInt(NumOpponents);
            h.AddInt((int)(Pace * 1000f));
            h.AddInt(TurnTimer);
            h.AddInt(ExtraPlanets);
            h.AddInt((int)(StartingPlanetRichnessBonus * 1000f));
            h.AddInt((int)(ClampGameSpeed(GameSpeed) * 1000f));
            h.AddBool(StartPaused);
            h.AddBool(EliminationMode);
            return "0x" + h.Value.ToString("X16", CultureInfo.InvariantCulture);
        }
    }

    public Authoritative4XGameSettings Normalized(int humanPlayerCount)
        => new()
        {
            GenerationSeed = GenerationSeed == 0 ? 54545 : GenerationSeed,
            Mode = Mode,
            StarsCount = StarsCount,
            GalaxySize = GalaxySize,
            Difficulty = Difficulty,
            NumOpponents = Math.Clamp(NumOpponents, Math.Max(1, humanPlayerCount - 1),
                Math.Max(1, ResourceManager.MajorRaces.Count - 1)),
            Pace = Math.Clamp(Pace, 1f, 10f),
            TurnTimer = Math.Clamp(TurnTimer, 1, 30),
            ExtraPlanets = Math.Clamp(ExtraPlanets, 0, 3),
            StartingPlanetRichnessBonus = Math.Clamp(StartingPlanetRichnessBonus, 0f, 5f),
            GameSpeed = ClampGameSpeed(GameSpeed),
            StartPaused = StartPaused,
            EliminationMode = EliminationMode || Mode == GameMode.Elimination,
        };

    static float ClampGameSpeed(float speed) => Math.Clamp(speed, 0.25f, 8f);
}

public sealed class Authoritative4XLobbyValidation
{
    public bool Valid;
    public string Reason = "";
    public int RemainingTraitPoints;

    public static Authoritative4XLobbyValidation Ok(int remaining)
        => new() { Valid = true, RemainingTraitPoints = remaining };

    public static Authoritative4XLobbyValidation Fail(string reason, int remaining = 0)
        => new() { Valid = false, Reason = reason ?? "", RemainingTraitPoints = remaining };
}

public sealed class Authoritative4XLobbyPlayer
{
    public readonly int PeerId;
    public string PlayerName;
    public string RaceName;
    public string[] TraitOptions = System.Array.Empty<string>();
    public bool Ready;
    public Authoritative4XLobbyValidation Validation = Authoritative4XLobbyValidation.Fail("No race selected.");

    public Authoritative4XLobbyPlayer(int peerId, string playerName)
    {
        PeerId = peerId;
        PlayerName = playerName ?? "";
    }
}

public sealed class Authoritative4XGeneratedGameStart : IDisposable
{
    public readonly UniverseScreen AuthorityUniverse;
    public readonly int[] HumanEmpireIds;
    public readonly IReadOnlyDictionary<int, int> EmpireIdByPeer;
    public readonly Authoritative4XGameSettings Settings;

    public Authoritative4XGeneratedGameStart(UniverseScreen authorityUniverse,
        int[] humanEmpireIds, IReadOnlyDictionary<int, int> empireIdByPeer,
        Authoritative4XGameSettings settings)
    {
        AuthorityUniverse = authorityUniverse;
        HumanEmpireIds = humanEmpireIds;
        EmpireIdByPeer = empireIdByPeer;
        Settings = settings;
    }

    public int EmpireIdForPeer(int peerId) => EmpireIdByPeer[peerId];

    public void Dispose()
    {
        AuthoritativeHumanPlayers.Clear(AuthorityUniverse.UState);
        AuthorityUniverse.Dispose();
    }
}

public sealed class Authoritative4XLobbyStartResult : IDisposable
{
    public readonly Authoritative4XGeneratedGameStart GeneratedGame;
    public UniverseScreen AuthorityUniverse => GeneratedGame.AuthorityUniverse;
    public int[] HumanEmpireIds => GeneratedGame.HumanEmpireIds;
    public IReadOnlyDictionary<int, int> EmpireIdByPeer => GeneratedGame.EmpireIdByPeer;

    public readonly Authoritative4XInProcessMultiClientSession Session;
    public readonly Authoritative4XClientSpec[] Clients;

    public Authoritative4XLobbyStartResult(Authoritative4XGeneratedGameStart generatedGame,
        Authoritative4XInProcessMultiClientSession session, Authoritative4XClientSpec[] clients)
    {
        GeneratedGame = generatedGame;
        Session = session;
        Clients = clients;
    }

    public int EmpireIdForPeer(int peerId) => EmpireIdByPeer[peerId];

    public void Dispose()
    {
        foreach (Authoritative4XClientSpec client in Clients)
            AuthoritativeHumanPlayers.Clear(client.Universe.UState);
        GeneratedGame.Dispose();
        foreach (Authoritative4XClientSpec client in Clients)
            client.Universe.Dispose();
    }
}

/// <summary>
/// Core Phase-B lobby state. The live lobby UI should mutate this model; the model owns
/// validation and the deterministic handoff into the Phase-A authoritative session.
/// </summary>
public sealed class Authoritative4XLobby
{
    public const int AuthorityPeerId = 1;
    public const int MaxHumanPlayers = 8;

    readonly int HostPlayerPeerId;
    readonly Dictionary<int, Authoritative4XLobbyPlayer> Players = new();

    public Authoritative4XGameSettings Settings { get; private set; } = new();

    public Authoritative4XLobby(int hostPlayerPeerId, string hostName)
    {
        if (hostPlayerPeerId == AuthorityPeerId)
            throw new ArgumentException("Peer 1 is reserved for the authoritative server endpoint.", nameof(hostPlayerPeerId));

        HostPlayerPeerId = hostPlayerPeerId;
        Join(hostPlayerPeerId, hostName);
    }

    public Authoritative4XLobbyPlayer[] Roster
        => Players.Values.OrderBy(p => p.PeerId).ToArray();

    public Authoritative4XLobbyPlayer Join(int peerId, string playerName)
    {
        if (peerId == AuthorityPeerId)
            throw new ArgumentException("Peer 1 is reserved for the authoritative server endpoint.", nameof(peerId));
        if (Players.TryGetValue(peerId, out Authoritative4XLobbyPlayer existing))
        {
            existing.PlayerName = playerName ?? existing.PlayerName;
            return existing;
        }
        if (Players.Count >= MaxHumanPlayers)
            throw new InvalidOperationException($"Authoritative 4X supports up to {MaxHumanPlayers} human players.");

        var player = new Authoritative4XLobbyPlayer(peerId, playerName);
        Players[peerId] = player;
        return player;
    }

    public bool Leave(int peerId) => peerId != HostPlayerPeerId && Players.Remove(peerId);

    public Authoritative4XLobbyValidation SetSettings(int peerId, Authoritative4XGameSettings settings)
    {
        if (peerId != HostPlayerPeerId)
            return Authoritative4XLobbyValidation.Fail("Only the host can change game settings.");
        Settings = (settings ?? new Authoritative4XGameSettings()).Normalized(Players.Count);
        return Authoritative4XLobbyValidation.Ok(0);
    }

    public Authoritative4XLobbyValidation SetPlayerSelection(int peerId, string raceName, IEnumerable<string> traitOptions)
    {
        if (!Players.TryGetValue(peerId, out Authoritative4XLobbyPlayer player))
            return Authoritative4XLobbyValidation.Fail($"Peer {peerId} is not in the lobby.");

        string[] traits = NormalizeTraitNames(traitOptions).ToArray();
        Authoritative4XLobbyValidation validation = ValidateRaceAndTraits(raceName, traits, Settings);
        player.RaceName = ResolveRaceName(raceName);
        player.TraitOptions = traits;
        player.Validation = validation;
        player.Ready = false;
        return validation;
    }

    public Authoritative4XLobbyValidation SetReady(int peerId, bool ready)
    {
        if (!Players.TryGetValue(peerId, out Authoritative4XLobbyPlayer player))
            return Authoritative4XLobbyValidation.Fail($"Peer {peerId} is not in the lobby.");
        if (ready && !player.Validation.Valid)
        {
            player.Ready = false;
            return player.Validation;
        }

        player.Ready = ready;
        return Authoritative4XLobbyValidation.Ok(player.Validation.RemainingTraitPoints);
    }

    public Authoritative4XLobbyValidation CanStart()
    {
        if (Players.Count == 0)
            return Authoritative4XLobbyValidation.Fail("No players are in the lobby.");
        foreach (Authoritative4XLobbyPlayer player in Roster)
        {
            if (!player.Validation.Valid)
                return Authoritative4XLobbyValidation.Fail($"Peer {player.PeerId}: {player.Validation.Reason}");
            if (!player.Ready)
                return Authoritative4XLobbyValidation.Fail($"Peer {player.PeerId} is not ready.");
        }

        var selectedRaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Authoritative4XLobbyPlayer player in Roster)
        {
            string key = RaceKey(player.RaceName);
            if (key.NotEmpty() && !selectedRaces.Add(key))
                return Authoritative4XLobbyValidation.Fail($"Duplicate race selection: {key}.");
        }

        return Authoritative4XLobbyValidation.Ok(0);
    }

    public Authoritative4XLobbyStartResult StartInProcess()
    {
        Authoritative4XGeneratedGameStart generated = StartGeneratedGame();
        try
        {
            Authoritative4XLobbyPlayer[] players = Roster;
            Authoritative4XGameSettings settings = generated.Settings;
            int[] humanEmpireIds = generated.HumanEmpireIds;
            IReadOnlyDictionary<int, int> empireIdByPeer = generated.EmpireIdByPeer;

            var specs = new Authoritative4XClientSpec[players.Length];
            for (int i = 0; i < players.Length; ++i)
            {
                UniverseScreen clientUniverse = CreateUniverse(players, settings);
                AuthoritativeHumanPlayers.SetHumanControlledEmpires(clientUniverse.UState, humanEmpireIds);
                ConfigureHumanEmpires(clientUniverse.UState, humanEmpireIds);
                specs[i] = new Authoritative4XClientSpec(players[i].PeerId, empireIdByPeer[players[i].PeerId], clientUniverse);
            }

            var session = new Authoritative4XInProcessMultiClientSession(generated.AuthorityUniverse, specs);
            return new Authoritative4XLobbyStartResult(generated, session, specs);
        }
        catch
        {
            generated.Dispose();
            throw;
        }
    }

    public Authoritative4XGeneratedGameStart StartGeneratedGame()
    {
        Authoritative4XLobbyValidation canStart = CanStart();
        if (!canStart.Valid)
            throw new InvalidOperationException(canStart.Reason);

        Authoritative4XLobbyPlayer[] players = Roster;
        Authoritative4XGameSettings settings = Settings.Normalized(players.Length);
        UniverseScreen authority = CreateUniverse(players, settings);
        Dictionary<int, int> empireIdByPeer = MapHumanEmpires(players, authority);
        int[] humanEmpireIds = players.Select(p => empireIdByPeer[p.PeerId]).ToArray();
        AuthoritativeHumanPlayers.SetHumanControlledEmpires(authority.UState, humanEmpireIds);
        ConfigureHumanEmpires(authority.UState, humanEmpireIds);
        return new Authoritative4XGeneratedGameStart(authority, humanEmpireIds, empireIdByPeer, settings);
    }

    public static Authoritative4XLobbyValidation ValidateRaceAndTraits(string raceName,
        IEnumerable<string> traitOptions, Authoritative4XGameSettings settings = null)
    {
        if (FindRace(raceName) == null)
            return Authoritative4XLobbyValidation.Fail($"Race '{raceName}' was not found.");

        int points = new UniverseParams().RacialTraitPoints;
        string[] traits = NormalizeTraitNames(traitOptions).ToArray();
        int cost = 0;
        var selected = new HashSet<string>(StringComparer.Ordinal);

        foreach (string name in traits)
        {
            RacialTraitOption trait = FindTrait(name);
            if (trait == null)
                return Authoritative4XLobbyValidation.Fail($"Trait '{name}' was not found.");
            if (!selected.Add(trait.TraitName))
                continue;
            cost += trait.Cost;
        }

        int remaining = points - cost;
        if (remaining < 0)
            return Authoritative4XLobbyValidation.Fail($"Trait selection exceeds the {points}-point budget.", remaining);

        foreach (string name in selected)
        {
            RacialTraitOption trait = FindTrait(name);
            if (trait?.Excludes == null)
                continue;
            foreach (string excluded in trait.Excludes)
                if (selected.Contains(excluded))
                    return Authoritative4XLobbyValidation.Fail($"{trait.TraitName} excludes {excluded}.", remaining);
        }

        return Authoritative4XLobbyValidation.Ok(remaining);
    }

    static UniverseScreen CreateUniverse(Authoritative4XLobbyPlayer[] players, Authoritative4XGameSettings settings)
    {
        UniverseParams p = ToUniverseParams(players, settings);
        ResourceManager.Planets?.SeedDeterministicGeneration(settings.GenerationSeed);
        UniverseScreen screen = new UniverseGenerator(p).Generate();
        screen.CreateSimThread = false;
        screen.UState.Objects.EnableParallelUpdate = false;
        screen.UState.EnableDeterministicRng((uint)settings.GenerationSeed ^ 0x4D503458u);
        screen.UState.Paused = settings.StartPaused;
        screen.UState.GameSpeed = settings.GameSpeed;
        return screen;
    }

    static UniverseParams ToUniverseParams(Authoritative4XLobbyPlayer[] players, Authoritative4XGameSettings settings)
    {
        Authoritative4XGameSettings s = settings.Normalized(players.Length);
        Authoritative4XLobbyPlayer host = players[0];
        IEmpireData hostRace = FindRace(host.RaceName)
                               ?? throw new InvalidOperationException($"Race '{host.RaceName}' was not found.");

        EmpireData playerData = CreateEmpireData(hostRace, host.TraitOptions, player: true);
        var selected = new Array<IEmpireData>();
        var usedRaceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { RaceKey(hostRace) };

        foreach (Authoritative4XLobbyPlayer player in players.Skip(1))
        {
            IEmpireData race = FindRace(player.RaceName)
                               ?? throw new InvalidOperationException($"Race '{player.RaceName}' was not found.");
            selected.Add(CreateEmpireData(race, player.TraitOptions, player: false));
            usedRaceKeys.Add(RaceKey(race));
        }

        int totalOpponents = Math.Max(s.NumOpponents, selected.Count);
        foreach (IEmpireData race in ResourceManager.MajorRaces
                     .OrderBy(r => RaceKey(r), StringComparer.OrdinalIgnoreCase))
        {
            if (selected.Count >= totalOpponents)
                break;
            string key = RaceKey(race);
            if (key.IsEmpty() || usedRaceKeys.Contains(key))
                continue;
            selected.Add(race);
            usedRaceKeys.Add(key);
        }

        (int numStars, float starMod) = GetNumStars(s.StarsCount, s.GalaxySize, totalOpponents);
        return new UniverseParams
        {
            PlayerData = playerData,
            Mode = s.Mode,
            StarsCount = s.StarsCount,
            GalaxySize = s.GalaxySize,
            Difficulty = s.Difficulty,
            NumSystems = numStars,
            NumOpponents = totalOpponents,
            StarsModifier = starMod,
            Pace = s.Pace,
            GenerationSeed = s.GenerationSeed,
            TurnTimer = s.TurnTimer,
            ExtraPlanets = s.ExtraPlanets,
            StartingPlanetRichnessBonus = s.StartingPlanetRichnessBonus,
            EliminationMode = s.EliminationMode,
            SelectedOpponents = selected,
        };
    }

    static Dictionary<int, int> MapHumanEmpires(Authoritative4XLobbyPlayer[] players, UniverseScreen universe)
    {
        Empire[] majors = universe.UState.MajorEmpires.OrderBy(e => e.Id).ToArray();
        if (majors.Length < players.Length)
            throw new InvalidOperationException($"Generated only {majors.Length} major empires for {players.Length} players.");

        var map = new Dictionary<int, int>();
        for (int i = 0; i < players.Length; ++i)
            map[players[i].PeerId] = majors[i].Id;
        return map;
    }

    static void ConfigureHumanEmpires(UniverseState universe, int[] humanEmpireIds)
    {
        if (universe == null || humanEmpireIds == null)
            return;

        foreach (int empireId in humanEmpireIds)
            ConfigureHumanEmpire(universe.GetEmpireById(empireId));
    }

    internal static void DisableHumanEmpireAutomation(Empire empire)
    {
        if (empire == null)
            return;

        empire.AISidekickEnabled = false;
        empire.OracleSidekickEnabled = false;
        empire.AutoPickConstructors = false;
        empire.AutoPickBestColonizer = false;
        empire.AutoPickBestFreighter = false;
        empire.AutoResearch = false;
        empire.AutoBuildTerraformers = false;
        empire.AutoTaxes = false;
        empire.AutoPickBestResearchStation = false;
        empire.AutoPickBestMiningStation = false;
        empire.AutoExplore = false;
        empire.AutoColonize = false;
        empire.AutoBuildSpaceRoads = false;
        empire.AutoFreighters = false;
        empire.AutoBuildResearchStations = false;
        empire.AutoBuildMiningStations = false;
        empire.AutoMilitary = false;
        empire.AutoSpy = false;
        empire.RushAllConstruction = false;
        empire.SwitchRushAllConstruction(false);
        empire.data.CurrentAutoFreighter = "";
        empire.data.CurrentAutoColony = "";
        empire.data.CurrentAutoScout = "";
        empire.data.CurrentConstructor = "";
        empire.data.CurrentResearchStation = "";
        empire.data.CurrentMiningStation = "";
    }

    static void ConfigureHumanEmpire(Empire empire)
    {
        DisableHumanEmpireAutomation(empire);
        if (empire == null)
            return;

        ConfigureGeneratedHumanPlanets(empire);
        empire.Research?.Reset();
        RemoveMachineLocalPlayerDesigns(empire);
    }

    static void ConfigureGeneratedHumanPlanets(Empire empire)
    {
        foreach (Planet planet in empire.GetPlanets())
        {
            planet.CType = Planet.ColonyType.Colony;
            planet.GovOrbitals = false;
            planet.GovGroundDefense = false;
            planet.AutoBuildTroops = false;
            planet.ManualOrbitals = false;
            planet.Construction.ClearQueue();
        }
    }

    internal static void RemoveMachineLocalPlayerDesigns(Empire empire)
    {
        foreach (IShipDesign design in empire.ShipsWeCanBuildSnapshot.Where(d => d.IsPlayerDesign).ToArray())
            empire.RemoveBuildableShip(design);
    }

    static EmpireData CreateEmpireData(IEmpireData race, string[] traitOptions, bool player)
    {
        EmpireData data = race.CreateInstance(copyTraits: false);
        data.Traits = CreateTraitSummary(race, traitOptions);
        if (player)
            data.DiplomaticPersonality = new DTrait();
        return data;
    }

    static RacialTrait CreateTraitSummary(IEmpireData race, string[] traitOptions)
    {
        RacialTrait source = race.Traits;
        var summary = new RacialTrait
        {
            Name = source.Name,
            Singular = source.Singular,
            Plural = source.Plural,
            HomeSystemName = source.HomeSystemName,
            HomeworldName = source.HomeworldName,
            ShipType = source.ShipType,
            VideoPath = source.VideoPath,
            FlagIndex = source.FlagIndex,
            Adj1 = source.Adj1,
            Adj2 = source.Adj2,
            Color = source.Color,
        };

        var set = new TraitSet { TraitOptions = new Array<string>() };
        foreach (string traitName in NormalizeTraitNames(traitOptions))
        {
            RacialTraitOption trait = FindTrait(traitName)
                                      ?? throw new InvalidOperationException($"Trait '{traitName}' was not found.");
            set.TraitOptions.Add(trait.TraitName);
            ApplyTrait(summary, trait);
        }
        summary.TraitSets.Add(set);
        return summary;
    }

    static void ApplyTrait(RacialTrait summary, RacialTraitOption trait)
    {
        summary.ConsumptionModifier    += trait.ConsumptionModifier;
        summary.DiplomacyMod           += trait.DiplomacyMod;
        summary.TargetingModifier      += trait.TargetingModifier;
        summary.MaintMod               += trait.MaintMod;
        summary.ReproductionMod        += trait.ReproductionMod;
        summary.PopGrowthMax           += trait.PopGrowthMax;
        summary.PopGrowthMin           += trait.PopGrowthMin;
        summary.ResearchMod            += trait.ResearchMod;
        summary.ShipCostMod            += trait.ShipCostMod;
        summary.TaxMod                 += trait.TaxMod;
        summary.ProductionMod          += trait.ProductionMod;
        summary.ModHpModifier          += trait.ModHpModifier;
        summary.Mercantile             += trait.Mercantile;
        summary.GroundCombatModifier   += trait.GroundCombatModifier;
        summary.Cybernetic             += trait.Cybernetic;
        summary.Blind                  += trait.Blind;
        summary.DodgeMod               += trait.DodgeMod;
        summary.HomeworldFertMod       += trait.HomeworldFertMod;
        summary.HomeworldRichMod       += trait.HomeworldRichMod;
        summary.HomeworldSizeMod       += trait.HomeworldSizeMod;
        summary.Militaristic           += trait.Militaristic;
        summary.BonusExplored          += trait.BonusExplored;
        summary.Prototype              += trait.Prototype;
        summary.Spiritual              += trait.Spiritual;
        summary.SpyMultiplier          += trait.SpyMultiplier;
        summary.RepairMod              += trait.RepairMod;
        summary.PassengerModifier      += trait.PassengerBonus;
        summary.Pack                   += trait.Pack;
        summary.Aquatic                += trait.Aquatic;
        summary.CreditsPerKilledSlot   += trait.CreditsPerKilledSlot;
        summary.PenaltyPerKilledSlot   += trait.PenaltyPerKilledSlot;
        summary.ExtraStartingScouts    += trait.ExtraStartingScouts;
        summary.ResearchBenefitFromAlliance += trait.ResearchBenefitFromAlliance;
        summary.ExploreDistanceMultiplier *= trait.ExploreDistanceMultiplier;
        summary.ConstructionRateMultiplier *= trait.ConstructionRateMultiplier;
        summary.BuilderShipConstructionMultiplier *= trait.BuilderShipConstructionMultiplier;
        summary.EnvTerran   *= trait.EnvTerranMultiplier;
        summary.EnvOceanic  *= trait.EnvOceanicMultiplier;
        summary.EnvSteppe   *= trait.EnvSteppeMultiplier;
        summary.EnvTundra   *= trait.EnvTundraMultiplier;
        summary.EnvSwamp    *= trait.EnvSwampMultiplier;
        summary.EnvDesert   *= trait.EnvDesertMultiplier;
        summary.EnvIce      *= trait.EnvIceMultiplier;
        summary.EnvBarren   *= trait.EnvBarrenMultiplier;
        summary.EnvVolcanic *= trait.EnvVolcanicMultiplier;
        if (trait.PreferredEnv != PlanetCategory.Terran)
            summary.PreferredEnv = trait.PreferredEnv;
    }

    static IEnumerable<string> NormalizeTraitNames(IEnumerable<string> traitOptions)
        => (traitOptions ?? System.Array.Empty<string>())
            .Where(t => t.NotEmpty())
            .Select(t => t.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal);

    static string ResolveRaceName(string raceName)
    {
        IEmpireData race = FindRace(raceName);
        return race?.ArchetypeName.NotEmpty() == true ? race.ArchetypeName : race?.Name ?? raceName ?? "";
    }

    static IEmpireData FindRace(string raceName)
    {
        if (raceName.IsEmpty())
            return null;
        return ResourceManager.MajorRaces.FirstOrDefault(r => SameName(r.ArchetypeName, raceName) || SameName(r.Name, raceName))
               ?? ResourceManager.FindEmpire(raceName);
    }

    static RacialTraitOption FindTrait(string traitName)
        => ResourceManager.RaceTraits.TraitList.FirstOrDefault(t => SameName(t.TraitName, traitName));

    static string RaceKey(string raceName)
        => RaceKey(FindRace(raceName));

    static string RaceKey(IEmpireData race)
        => race?.ArchetypeName.NotEmpty() == true ? race.ArchetypeName : race?.Name ?? "";

    static bool SameName(string a, string b)
        => a.NotEmpty() && b.NotEmpty() && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

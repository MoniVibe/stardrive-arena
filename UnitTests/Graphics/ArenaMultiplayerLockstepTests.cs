using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDLockstep;
using Ship_Game;
using Ship_Game.Determinism;
using Ship_Game.Determinism.Lockstep;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
using Ship_Game.GameScreens.Arena;
using Ship_Game.Universe;
using SynapseGaming.LightingSystem.Core;

namespace UnitTests.Graphics;

[TestClass]
public class ArenaMultiplayerLockstepTests : StarDriveTest
{
    [TestMethod]
    public void ArenaMultiplayerLobbyEntryAndSelfTest_Headless()
    {
        LoadAllGameData();

        string dir = Path.Combine(Path.GetTempPath(), $"arena_mp_lobby_{Guid.NewGuid():N}");
        string savedSlotDir = CareerManager.SlotDirectoryOverride;
        string savedStaticPath = ArenaFightScreen.CareerSavePath;
        string tempPath = Path.Combine(dir, "lobby.yaml");
        ScreenManager sm = ScreenManager.Instance;

        try
        {
            Directory.CreateDirectory(dir);
            CareerManager.SlotDirectoryOverride = dir;
            ArenaFightScreen.CareerSavePath = tempPath;
            ArenaFightScreen.PendingPlayerDesignName = null;
            sm.ExitAll(clear3DObjects: true);

            var menu = new ArenaCareerMenuScreen();
            sm.GoToScreen(menu, clear3DObjects: true);
            Assert.IsTrue(menu.Find("arena_continue", out UIButton _),
                "The Arena career entry screen must expose Continue in the main action row.");
            Assert.IsTrue(menu.Find("arena_new_game", out UIButton _),
                "The Arena career entry screen must expose New Game in the main action row.");
            Assert.IsTrue(menu.Find("arena_multiplayer", out UIButton _),
                "The Arena career entry screen must expose Multiplayer in the main action row.");
            Assert.IsTrue(menu.Find("arena_back", out UIButton _),
                "The Arena career entry screen must expose Back in the main action row.");

            var lobby = new ArenaMultiplayerLobbyScreen();
            sm.GoToScreen(lobby, clear3DObjects: true);
            Assert.IsTrue(lobby.Find("arena_mp_host", out UIButton _),
                "The multiplayer lobby must expose a Host action.");
            Assert.IsTrue(lobby.Find("arena_mp_join", out UIButton _),
                "The multiplayer lobby must expose a Join action.");
            Assert.IsTrue(lobby.Find("arena_mp_ready", out UIButton _),
                "The multiplayer lobby must expose an explicit Ready action.");
            Assert.IsTrue(lobby.Find("arena_mp_launch", out UIButton _),
                "The multiplayer lobby must expose a host Launch action.");
            Assert.IsTrue(lobby.Find("arena_mp_self_test", out UIButton _),
                "The multiplayer lobby must expose a local self-test action.");
            Assert.IsTrue(lobby.Find("arena_mp_race", out UIButton _),
                "The multiplayer lobby must expose a race selector.");
            Assert.IsFalse(lobby.Find("arena_mp_trait", out UIButton _),
                "The multiplayer lobby must not expose the old Arena Ace/Wingmates/Swarm loadout selector.");
            Assert.IsTrue(lobby.Find("arena_mp_regular_settings", out UIButton _),
                "The multiplayer lobby must expose regular-game map settings instead of Arena loadouts.");
            Assert.IsTrue(lobby.Find("arena_mp_host_entry", out UITextEntry _),
                "The multiplayer lobby must expose a host/IP entry.");
            Assert.IsTrue(lobby.Find("arena_mp_port_entry", out UITextEntry _),
                "The multiplayer lobby must expose a port entry.");
            Assert.IsTrue(lobby.Find("arena_mp_seed_entry", out UITextEntry _),
                "The multiplayer lobby must expose a host-controlled match seed.");
            Assert.IsTrue(lobby.Find("arena_mp_speed_entry", out UITextEntry _),
                "The multiplayer lobby must expose a host-controlled game speed.");

            ArenaMultiplayerSettings defaultSettings = ArenaMultiplayerLobbyScreen.CreateDefaultSettings(60).WithResolvedFleets();
            Assert.AreEqual("United", defaultSettings.HostRacePreference,
                "The lobby default settings should make host race explicit for preflight hashing.");
            Assert.AreEqual(ArenaStartArchetype.Wingmates.ToString(), defaultSettings.HostLoadoutTrait,
                "The lobby default settings should include the host loadout trait.");
            Assert.AreEqual(ArenaStartArchetype.Wingmates.ToString(), defaultSettings.JoinLoadoutTrait,
                "The lobby default settings should include the join loadout trait.");
            Assert.AreEqual(1f, defaultSettings.GameSpeed,
                "The lobby default settings should include the synchronized starting speed.");
            Assert.IsFalse(defaultSettings.StartPaused,
                "The lobby default settings should include the synchronized pause state.");

            ArenaMultiplayerRunResult result = ArenaMultiplayerLobbyScreen.RunLocalSelfTestForHeadless(60);
            Assert.IsFalse(result.Desynced,
                $"Lobby local self-test desynced at turn {result.DesyncTurn}: {result.DesyncReason}");
            Assert.AreEqual(60, result.TurnsCompleted,
                "The lobby self-test must run through the requested turn count.");
            Assert.IsTrue(result.TurnHashes.All(h => h.Match),
                "The lobby self-test must preserve identical peer hashes.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.FinalHash),
                "The lobby self-test must produce a final digest for PC/laptop comparison.");
        }
        finally
        {
            try { sm.ExitAll(clear3DObjects: true); } catch { }
            CareerManager.SlotDirectoryOverride = savedSlotDir;
            ArenaFightScreen.CareerSavePath = savedStaticPath;
            ArenaFightScreen.PendingPlayerDesignName = null;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void ArenaMultiplayerLockstep_InProcessStaysInSync_Headless()
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_lockstep_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;

        try
        {
            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED,
                RngSeed = 0xA12EA000u,
                InputDelay = 3,
                MaxTurns = 180,
                CommandEveryTurns = 1,
                HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul),
                JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul),
            };
            settings = settings.WithResolvedFleets();

            ArenaMultiplayerRunResult result = ArenaMultiplayerSession.RunInProcess(settings);

            Assert.IsFalse(result.Desynced,
                $"In-process Arena lockstep desynced at turn {result.DesyncTurn}: {result.DesyncReason}");
            Assert.AreEqual(settings.MaxTurns, result.TurnsCompleted);
            Assert.IsTrue(result.CommandsSubmitted >= settings.MaxTurns,
                "Phase-2 lockstep should submit a real per-turn focus/heartbeat command stream.");
            Assert.IsTrue(result.HostSnapshot.PlayerShipIds.Length > 0, "Host peer must spawn player ships.");
            Assert.IsTrue(result.HostSnapshot.EnemyShipIds.Length > 0, "Host peer must spawn enemy ships.");
            CollectionAssert.AreEqual(settings.HostFleetDesignNames, result.HostSnapshot.PlayerFleetDesigns,
                "The host-contributed fleet manifest must become side 1 in the real arena.");
            CollectionAssert.AreEqual(settings.JoinFleetDesignNames, result.HostSnapshot.EnemyFleetDesigns,
                "The join-contributed fleet manifest must become side 2 in the real arena.");
            Assert.AreEqual(settings.HostFleetDesignNames.Length, result.HostSnapshot.PlayerShipIds.Length,
                "The real arena should spawn one side-1 ship per host fleet design.");
            Assert.AreEqual(settings.JoinFleetDesignNames.Length, result.HostSnapshot.EnemyShipIds.Length,
                "The real arena should spawn one side-2 ship per join fleet design.");
            CollectionAssert.AreEqual(result.HostSnapshot.PlayerShipIds, result.JoinSnapshot.PlayerShipIds,
                "Both peers must spawn identical player ship IDs from the shared match seed.");
            CollectionAssert.AreEqual(result.HostSnapshot.EnemyShipIds, result.JoinSnapshot.EnemyShipIds,
                "Both peers must spawn identical enemy ship IDs from the shared match seed.");
            Assert.IsTrue(result.TurnHashes.All(h => h.Match),
                "Every in-process Arena lockstep turn hash must match.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.FinalHash), "The real-arena lockstep match should report a final state digest.");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    [TestMethod]
    public void ArenaMultiplayerLockstep_ForcedDivergenceTripsDetector_Headless()
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_desync_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;

        try
        {
            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED,
                RngSeed = 0xA12EA000u,
                InputDelay = 3,
                MaxTurns = 90,
                CommandEveryTurns = 1,
                HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul),
                JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul),
            };

            ArenaMultiplayerRunResult result = ArenaMultiplayerSession.RunInProcess(settings, forceDesyncAfterTurn: 30);

            Assert.IsTrue(result.Desynced, "Forced local-only state mutation must trip desync detection.");
            Assert.IsTrue(result.DesyncTurn >= 30,
                $"Desync should be reported after the forced divergence turn, got {result.DesyncTurn}.");
            Assert.IsTrue(result.TurnHashes.Any(h => !h.Match),
                "Forced divergence must produce at least one mismatched per-turn hash.");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    [TestMethod]
    public void ArenaMultiplayerLockstep_PvPMatchEndDetectsWinner_Headless()
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_winner_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;

        try
        {
            IShipDesign weak = LegalPvPDesigns().First();
            IShipDesign strong = LegalPvPDesigns().Last();
            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x6EED,
                RngSeed = 0xB12EA000u,
                InputDelay = 3,
                MaxTurns = 900,
                CommandEveryTurns = 1,
                HostFleetDesignNames = new[] { strong.Name, strong.Name },
                JoinFleetDesignNames = new[] { weak.Name },
            };

            ArenaMultiplayerRunResult result = ArenaMultiplayerSession.RunInProcess(settings);

            Assert.IsFalse(result.Desynced,
                $"PvP winner proof desynced at turn {result.DesyncTurn}: {result.DesyncReason}");
            Assert.IsTrue(result.MatchEnded,
                $"Expected asymmetric PvP brawl to end by elimination within {settings.MaxTurns} turns; final={result.FinalHash}");
            Assert.AreEqual(ArenaMultiplayerSession.HostPlayerPeerId, result.WinnerPeerId,
                $"The stronger host fleet should win. strong={strong.Name} weak={weak.Name}");
            Assert.IsTrue(result.MatchEndedTurn >= 0 && result.MatchEndedTurn < result.TurnsCompleted);
            Assert.IsTrue(result.TurnHashes.All(h => h.Match),
                "Winner detection should not require divergent peer state.");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    [TestMethod]
    public void ArenaRegularMultiplayerSettings_GeneratesCombinedArmsFullGame_Headless()
    {
        LoadCombinedArmsForArenaMultiplayer();

        try
        {
            string[] races = ArenaRegularMultiplayerSettings.AvailableRacePreferences();
            Assert.IsTrue(races.Length >= 2, "Combined Arms multiplayer setup needs at least two major races.");

            var settings = new ArenaRegularMultiplayerSettings
            {
                GenerationSeed = 460242,
                HostRacePreference = races[0],
                JoinRacePreference = races[1],
                NumOpponents = 1,
                GalaxySize = GalSize.Tiny,
                StarsCount = RaceDesignScreen.StarsAbundance.Rare,
                Mode = RaceDesignScreen.GameMode.Sandbox,
                Difficulty = GameDifficulty.Normal,
                Pace = 2f,
                TurnTimer = 4,
                ExtraPlanets = 2,
                StartingPlanetRichnessBonus = 1.5f,
                GameSpeed = 2f,
                StartPaused = false,
            };

            UniverseParams p = settings.ToUniverseParams();
            Assert.AreEqual(settings.GenerationSeed, p.GenerationSeed);
            Assert.AreEqual(settings.GalaxySize, p.GalaxySize);
            Assert.AreEqual(settings.StarsCount, p.StarsCount);
            Assert.AreEqual(settings.Mode, p.Mode);
            Assert.AreEqual(settings.Pace, p.Pace);
            Assert.AreEqual(settings.TurnTimer, p.TurnTimer);
            Assert.AreEqual(settings.ExtraPlanets, p.ExtraPlanets);
            Assert.AreEqual(settings.StartingPlanetRichnessBonus, p.StartingPlanetRichnessBonus);
            Assert.IsTrue(p.SelectedOpponents.Count >= 1,
                "The remote player's race must be represented as the first regular-game opponent until multi-human perspective is implemented.");

            using UniverseScreen game = settings.CreateUniverse();
            Empire[] majors = game.UState.MajorEmpires;
            Assert.IsTrue(majors.Length >= 2, "A regular multiplayer sandbox must create more than one major empire/homeworld.");
            Assert.IsTrue(majors.All(e => e.GetPlanets().Count > 0),
                "Every major empire in the generated regular game should receive a homeworld.");
            Assert.AreEqual(settings.GameSpeed, game.UState.GameSpeed);
            Assert.IsFalse(game.UState.Paused);
        }
        finally
        {
            RestoreVanillaAfterCombinedArms();
        }
    }

    [TestMethod]
    public void ArenaRegularMultiplayerSettings_FullGameLockstepDeterministic_Headless()
    {
        LoadCombinedArmsForArenaMultiplayer();

        try
        {
            string[] races = ArenaRegularMultiplayerSettings.AvailableRacePreferences();
            var settings = new ArenaRegularMultiplayerSettings
            {
                GenerationSeed = 461460,
                HostRacePreference = races[0],
                JoinRacePreference = races[1],
                NumOpponents = 1,
                GalaxySize = GalSize.Tiny,
                StarsCount = RaceDesignScreen.StarsAbundance.Rare,
                Mode = RaceDesignScreen.GameMode.Sandbox,
                TurnTimer = 1,
                StartPaused = false,
            };

            using UniverseScreen gameA = settings.CreateUniverse();
            using UniverseScreen gameB = settings.CreateUniverse();
            var simA = new UniverseScreenLockstepSimulation(gameA, Ship_Game.Determinism.DeterminismProfile.ReplayWinX64Float);
            var simB = new UniverseScreenLockstepSimulation(gameB, Ship_Game.Determinism.DeterminismProfile.ReplayWinX64Float);
            (ulong lo, ulong hi) initial = simA.Hash();
            Assert.AreEqual(simA.Hash(), simB.Hash(),
                $"Seeded regular-game generation must be identical before lockstep. {LaneDiff(gameA, gameB)}");

            for (uint turn = 0; turn < 240; ++turn)
            {
                var frame = new CommandFrame(turn);
                simA.Apply(frame);
                simB.Apply(frame);
                Assert.AreEqual(simA.Hash(), simB.Hash(), $"Full regular-game lockstep diverged at turn {turn}.");
            }

            Assert.AreNotEqual(initial, simA.Hash(), "The full regular-game sim should evolve under lockstep.");

            var differentSeed = new ArenaRegularMultiplayerSettings
            {
                GenerationSeed = settings.GenerationSeed + 1,
                HostRacePreference = settings.HostRacePreference,
                JoinRacePreference = settings.JoinRacePreference,
                NumOpponents = settings.NumOpponents,
                GalaxySize = settings.GalaxySize,
                StarsCount = settings.StarsCount,
                Mode = settings.Mode,
                TurnTimer = settings.TurnTimer,
                StartPaused = false,
            };
            using UniverseScreen gameC = differentSeed.CreateUniverse();
            var simC = new UniverseScreenLockstepSimulation(gameC, Ship_Game.Determinism.DeterminismProfile.ReplayWinX64Float);
            Assert.AreNotEqual(simA.Hash(), simC.Hash(),
                "Changing the regular-game generation seed should produce a different full-game fingerprint.");
        }
        finally
        {
            RestoreVanillaAfterCombinedArms();
        }
    }

    static string[] FleetNames(ArenaStartArchetype archetype, ulong seed)
        => CareerManager.StartingRosterDesigns(archetype, seed)
            .Select(d => d.Name)
            .ToArray();

    static IShipDesign[] LegalPvPDesigns()
        => ResourceManager.Ships.Designs
            .Where(ArenaFightScreen.IsLegalCombatCraft)
            .Where(ArenaFightScreen.IsStockContentDesign)
            .OrderBy(d => d.BaseStrength)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

    static string LaneDiff(UniverseScreen a, UniverseScreen b)
    {
        DeterminismHashWriter lanesA = a.UState.ComputeDebugLaneHashes(DeterminismProfile.ReplayWinX64Float);
        DeterminismHashWriter lanesB = b.UState.ComputeDebugLaneHashes(DeterminismProfile.ReplayWinX64Float);
        for (int lane = 0; lane < DeterminismHashWriter.LaneCount; ++lane)
        {
            var detLane = (DetLane)lane;
            ulong hashA = lanesA.LaneHash(detLane);
            ulong hashB = lanesB.LaneHash(detLane);
            if (hashA != hashB)
                return $"{detLane}:0x{hashA:X16}!=0x{hashB:X16}";
        }
        return "no differing lane";
    }

    void LoadCombinedArmsForArenaMultiplayer()
    {
        GlobalStats.LoadModInfo("Mods/Combined Arms");
        Assert.IsTrue(GlobalStats.HasMod, "Combined Arms must be installed at game/Mods/Combined Arms for the multiplayer compatibility proof.");

        LoadedExtraData = true;
        Directory.CreateDirectory(SavedGame.DefaultSaveGameFolder);
        ScreenManager.Instance.UpdateGraphicsDevice();
        GlobalStats.AsteroidVisibility = ObjectVisibility.None;
        ResourceManager.UnloadAllData(ScreenManager.Instance);
        ResourceManager.LoadItAll(ScreenManager.Instance, GlobalStats.ActiveMod);
    }

    void RestoreVanillaAfterCombinedArms()
    {
        if (!GlobalStats.HasMod)
            return;
        GlobalStats.SetActiveModNoSave(null);
        ResourceManager.InitContentDir();
    }
}

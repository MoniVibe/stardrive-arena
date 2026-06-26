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
using Ship_Game.Multiplayer.Authoritative;
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
            Assert.IsTrue(lobby.Find("arena_mp_trait", out UIButton _),
                "The multiplayer lobby must expose a racial trait selector for 4X starts.");
            Assert.IsTrue(lobby.Find("arena_mp_regular_settings", out UIButton _),
                "The multiplayer lobby must expose regular-game map settings instead of Arena loadouts.");
            Assert.IsTrue(lobby.Find("arena_mp_stars", out UIButton _),
                "The multiplayer lobby must expose host-controlled star density.");
            Assert.IsTrue(lobby.Find("arena_mp_difficulty", out UIButton _),
                "The multiplayer lobby must expose host-controlled difficulty.");
            Assert.IsTrue(lobby.Find("arena_mp_opponents", out UIButton _),
                "The multiplayer lobby must expose host-controlled AI opponent count.");
            Assert.IsTrue(lobby.Find("arena_mp_richness", out UIButton _),
                "The multiplayer lobby must expose host-controlled planet richness.");
            Assert.IsTrue(lobby.Find("arena_mp_turn_timer", out UIButton _),
                "The multiplayer lobby must expose host-controlled seconds-per-turn.");
            Assert.IsTrue(lobby.Find("arena_mp_pace", out UIButton _),
                "The multiplayer lobby must expose host-controlled game pacing.");
            Assert.IsTrue(lobby.Find("arena_mp_host_entry", out UITextEntry _),
                "The multiplayer lobby must expose a host/IP entry.");
            Assert.IsTrue(lobby.Find("arena_mp_port_entry", out UITextEntry _),
                "The multiplayer lobby must expose a port entry.");
            Assert.IsTrue(lobby.Find("arena_mp_seed_entry", out UITextEntry _),
                "The multiplayer lobby must expose a host-controlled match seed.");
            Assert.IsTrue(lobby.Find("arena_mp_speed_entry", out UITextEntry _),
                "The multiplayer lobby must expose a host-controlled game speed.");

            string[] races = ResourceManager.MajorRaces
                .Select(r => r.ArchetypeName.NotEmpty() ? r.ArchetypeName : r.Name)
                .Where(r => r.NotEmpty())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(r => r, StringComparer.Ordinal)
                .Take(2)
                .ToArray();
            Assert.IsTrue(races.Length >= 2, "The 4X lobby proof needs at least two major races.");
            int traitBudget = new UniverseParams().RacialTraitPoints;
            string[] traits = ResourceManager.RaceTraits.TraitList
                .Where(t => t.TraitName.NotEmpty() && t.Cost <= traitBudget)
                .Select(t => t.TraitName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .Take(2)
                .ToArray();
            string hostTrait = traits.Length > 0 ? traits[0] : "";
            string joinTrait = traits.Length > 1 ? traits[1] : hostTrait;
            var settings = new Authoritative4XGameSettings
            {
                GenerationSeed = 424242,
                GalaxySize = GalSize.Small,
                StarsCount = RaceDesignScreen.StarsAbundance.Normal,
                Difficulty = GameDifficulty.Hard,
                NumOpponents = 2,
                Pace = 2f,
                TurnTimer = 3,
                ExtraPlanets = 1,
                StartingPlanetRichnessBonus = 2f,
                GameSpeed = 0.5f,
                StartPaused = true,
            }.Normalized(2);
            lobby.Configure4XForHeadless(settings, races[0], hostTrait, races[1], joinTrait);
            SessionStartMessage start = lobby.Build4XStartForHeadless();
            Assert.IsTrue(start.IsAuthoritative4X, "The visible MP lobby should now launch the authoritative 4X path.");
            Assert.AreEqual(races[0], start.HostRacePreference);
            Assert.AreEqual(races[1], start.JoinRacePreference);
            Assert.AreEqual(hostTrait, start.HostTraitOptions);
            Assert.AreEqual(joinTrait, start.JoinTraitOptions);
            Assert.AreEqual((int)settings.GalaxySize, start.GalaxySize);
            Assert.AreEqual((int)settings.StarsCount, start.StarsCount);
            Assert.AreEqual((int)settings.Difficulty, start.Difficulty);
            Assert.AreEqual(settings.NumOpponents, start.NumOpponents);
            Assert.AreEqual(settings.Pace, start.Pace);
            Assert.AreEqual(settings.TurnTimer, start.TurnTimer);
            Assert.AreEqual(settings.ExtraPlanets, start.ExtraPlanets);
            Assert.AreEqual(settings.StartingPlanetRichnessBonus, start.StartingPlanetRichnessBonus);

            using (Authoritative4XGeneratedGameStart generated = lobby.CreateGenerated4XGameForHeadless(start))
            {
                UniverseState us = generated.AuthorityUniverse.UState;
                Assert.AreEqual(settings.GalaxySize, us.P.GalaxySize);
                Assert.AreEqual(settings.StarsCount, us.P.StarsCount);
                Assert.AreEqual(settings.Difficulty, us.P.Difficulty);
                Assert.AreEqual(settings.Pace, us.P.Pace);
                Assert.AreEqual(settings.TurnTimer, us.P.TurnTimer);
                Assert.AreEqual(settings.ExtraPlanets, us.P.ExtraPlanets);
                Assert.AreEqual(settings.StartingPlanetRichnessBonus, us.P.StartingPlanetRichnessBonus);
                Assert.AreEqual(2, generated.HumanEmpireIds.Length);
                Empire hostEmpire = us.GetEmpireById(generated.EmpireIdForPeer(2));
                Empire joinEmpire = us.GetEmpireById(generated.EmpireIdForPeer(3));
                if (hostTrait.NotEmpty())
                    Assert.IsTrue(hostEmpire.data.Traits.PlayerTraitOptions.Contains(hostTrait),
                        "The generated host empire must include the host's chosen trait.");
                if (joinTrait.NotEmpty())
                    Assert.IsTrue(joinEmpire.data.Traits.PlayerTraitOptions.Contains(joinTrait),
                        "The generated join empire must include the joiner's chosen trait.");
            }

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
    public void ArenaMultiplayerLiveStabilizesVisibilityForBothFleets_Headless()
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_visibility_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        ArenaFightScreen screen = null;

        try
        {
            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED,
                RngSeed = 0xA12EA000u,
                InputDelay = 3,
                MaxTurns = 60,
                CommandEveryTurns = 1,
                HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul),
                JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul),
            }.WithResolvedFleets();

            screen = ArenaFightScreen.Create(settings.HostRacePreference, settings.MatchSeed,
                startAtHub: false, opponentPreference: settings.JoinRacePreference);
            screen.ConfigureMultiplayerPvP(settings);
            screen.LoadContent();

            ArenaMultiplayerShipSnapshot snapshot = screen.MultiplayerSnapshot();
            int[] ids = snapshot.PlayerShipIds.Concat(snapshot.EnemyShipIds).ToArray();
            Assert.IsTrue(ids.Length >= 2, "The live PvP setup must spawn both fleets.");

            foreach (int id in ids)
            {
                Ship ship = screen.UState.Objects.FindShip(id);
                Assert.IsNotNull(ship, $"Spawned ship id {id} should resolve in the universe.");
                ship.InFrustum = false;
            }

            screen.StabilizeMultiplayerArenaViewAndVisibility();

            foreach (int id in ids)
            {
                Ship ship = screen.UState.Objects.FindShip(id);
                Assert.IsTrue(ship.InFrustum,
                    $"Arena multiplayer stabilization must not let local camera/resolution hide ship {id} from the sim.");
                Assert.IsTrue(ship.InPlayerSensorRange,
                    $"Arena multiplayer stabilization must make ship {id} visible to the shared viewer for icons/rendering.");
            }
        }
        finally
        {
            try { screen?.ExitScreen(); } catch { }
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

    [TestMethod]
    public void ArenaMultiplayerTelemetryWritesSessionArtifacts_Headless()
    {
        LoadAllGameData();
        string dir = Path.Combine(Path.GetTempPath(), $"arena_mp_telemetry_{Guid.NewGuid():N}");
        string savedDir = ArenaMultiplayerTelemetry.OutputDirectoryOverride;

        try
        {
            Directory.CreateDirectory(dir);
            ArenaMultiplayerTelemetry.OutputDirectoryOverride = dir;
            ArenaMultiplayerSettings settings = ArenaMultiplayerLobbyScreen.CreateDefaultSettings(90).WithResolvedFleets();

            string sessionPath;
            string lastPath;
            using (ArenaMultiplayerTelemetry telemetry = ArenaMultiplayerTelemetry.Start("Host", "headless-proof", settings))
            {
                sessionPath = telemetry.SessionPath;
                lastPath = telemetry.LastSessionPath;
                telemetry.Event("PROOF", "hello");
                telemetry.Turn(60, ArenaMultiplayerRole.Host, "0x1111111111111111:0x2222222222222222",
                    simTick: 61, remoteChecksumTick: 60, commandsSubmitted: 120,
                    playerAlive: 2, enemyAlive: 2, forced: true);
            }

            Assert.IsTrue(File.Exists(sessionPath), "Telemetry should write a unique session artifact.");
            Assert.IsTrue(File.Exists(lastPath), "Telemetry should update the stable last-session artifact.");
            string text = File.ReadAllText(lastPath);
            StringAssert.Contains(text, "BEGIN");
            StringAssert.Contains(text, "ENV");
            StringAssert.Contains(text, "SETTINGS");
            StringAssert.Contains(text, settings.SettingsHash);
            StringAssert.Contains(text, "FLEETS");
            StringAssert.Contains(text, "PROOF hello");
            StringAssert.Contains(text, "TURN turn=60");
            StringAssert.Contains(text, "END");
        }
        finally
        {
            ArenaMultiplayerTelemetry.OutputDirectoryOverride = savedDir;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
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

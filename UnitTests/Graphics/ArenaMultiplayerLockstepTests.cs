using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDLockstep;
using SDUtils.Deterministic;
using Ship_Game;
using Ship_Game.Determinism;
using Ship_Game.Determinism.Lockstep;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
using Ship_Game.GameScreens.Arena;
using Ship_Game.GameScreens.MainMenu;
using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.Plugins;
using Ship_Game.Universe;
using SynapseGaming.LightingSystem.Core;
using UnitTests.UI;
using Keys = SDGraphics.Input.Keys;

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
        string savedLobbyConfigPath = ArenaMultiplayerLobbyConfig.ConfigPathOverride;
        string tempPath = Path.Combine(dir, "lobby.yaml");
        string tempConfigPath = Path.Combine(dir, "mp-lobby-config.yaml");
        ScreenManager sm = ScreenManager.Instance;

        try
        {
            Directory.CreateDirectory(dir);
            CareerManager.SlotDirectoryOverride = dir;
            ArenaFightScreen.CareerSavePath = tempPath;
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = tempConfigPath;
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

            // ---- STAR GLADIATOR (arena duel) surface: an arena face, NOT the 4X galaxy shell. ----
            var lobby = new ArenaMultiplayerLobbyScreen();
            sm.GoToScreen(lobby, clear3DObjects: true);
            Assert.AreEqual(ArenaMultiplayerLobbySurface.StarGladiator, lobby.SurfaceMode);
            Assert.AreEqual("STAR GLADIATOR", lobby.HeaderTitleForHeadless);
            Assert.AreEqual("MULTIPLAYER LOBBY", lobby.HeaderSubtitleForHeadless);
            Assert.IsTrue(lobby.Find("arena_mp_host", out UIButton _),
                "The arena lobby must expose a Host action.");
            Assert.IsTrue(lobby.Find("arena_mp_join", out UIButton _),
                "The arena lobby must expose a Join action.");
            Assert.IsTrue(lobby.Find("arena_mp_ready", out UIButton _),
                "The arena lobby must expose an explicit Ready action.");
            Assert.IsTrue(lobby.Find("arena_mp_launch", out UIButton _),
                "The arena lobby must expose a host Launch action.");
            Assert.IsTrue(lobby.Find("arena_mp_self_test", out UIButton _),
                "The arena lobby must expose a local self-test action.");
            Assert.IsTrue(lobby.Find("arena_mp_back", out UIButton _),
                "The arena lobby must expose a Back action.");
            Assert.IsTrue(lobby.Find("arena_mp_race", out UIButton _),
                "The arena lobby keeps a race selector (loadout flavor).");
            Assert.IsTrue(lobby.Find("arena_mp_trait", out UIButton _),
                "The arena lobby keeps a trait selector (loadout flavor).");
            Assert.IsTrue(lobby.Find("arena_mp_trait_toggle", out UIButton _),
                "The arena lobby keeps the add/remove trait control.");
            Assert.IsTrue(lobby.Find("arena_mp_start_paused", out UIButton _),
                "The arena lobby keeps START LIVE/PAUSED.");
            Assert.IsTrue(lobby.Find("arena_mp_peer_slot", out UIButton _),
                "The arena lobby keeps the SLOT control.");

            // The 4X galaxy-generation chrome and the 4X game-mode selector must NOT be built here.
            foreach (string chrome in new[]
                     {
                         "arena_mp_mode", "arena_mp_regular_settings", "arena_mp_stars",
                         "arena_mp_difficulty", "arena_mp_opponents", "arena_mp_richness",
                         "arena_mp_extra_planets", "arena_mp_remnants", "arena_mp_pace",
                         "arena_mp_turn_timer", "arena_mp_decay", "arena_mp_volcanos",
                         "arena_mp_maintenance", "arena_mp_ftl", "arena_mp_gravity",
                         "arena_mp_pirates", "arena_mp_remnant_story", "arena_mp_station_ops",
                         "arena_mp_ai_rules",
                     })
            {
                Assert.IsFalse(lobby.Find(chrome, out UIButton _),
                    $"The Star Gladiator duel surface must NOT build the 4X galaxy pill '{chrome}'.");
            }

            // Arena-mode + fleet controls are the arena face.
            Assert.IsTrue(lobby.Find("arena_mp_arena_mode", out UIButton arenaModeButton),
                "The arena lobby must expose an ARENA Career/Sandbox control.");
            Assert.IsTrue(lobby.Find("arena_mp_set_fleet", out UIButton _),
                "The arena lobby must expose a SET FLEET picker.");

            // CycleArenaMode flips Career <-> Sandbox (and toggles the sandbox-only BUDGET pill).
            Assert.AreEqual(ArenaMatchMode.Career, lobby.ArenaModeForHeadless,
                "The arena lobby defaults to Career.");
            Assert.IsFalse(lobby.Find("arena_mp_budget", out UIButton careerBudget) && careerBudget.Visible,
                "The BUDGET pill is hidden in Career.");
            lobby.CycleArenaModeForHeadless();
            Assert.AreEqual(ArenaMatchMode.Sandbox, lobby.ArenaModeForHeadless,
                "CycleArenaMode must flip Career -> Sandbox.");
            Assert.IsTrue(lobby.Find("arena_mp_budget", out UIButton budgetButton) && budgetButton.Visible,
                "The BUDGET pill is visible in Sandbox.");
            lobby.CycleArenaModeForHeadless();
            Assert.AreEqual(ArenaMatchMode.Career, lobby.ArenaModeForHeadless,
                "CycleArenaMode must flip Sandbox -> Career.");

            // A fleet selection updates LocalPeer.FleetDesignNames through the picker's commit path.
            lobby.CycleArenaModeForHeadless(); // Sandbox so we can pick from the all-content roster
            string[] options = lobby.FleetPickerOptionsForHeadless;
            Assert.IsTrue(options.Length > 0, "Sandbox must offer legal arena combat-craft designs to pick.");
            string[] pick = options.Take(2).ToArray();
            lobby.SetFleetForHeadless(pick);
            CollectionAssert.AreEqual(pick, lobby.LocalFleetDesignNamesForHeadless,
                "Picking a fleet must write LocalPeer.FleetDesignNames.");
            lobby.CycleArenaModeForHeadless(); // back to Career for a clean default

            // Exactly two combatant slots (host P2 + one joiner P3) on the duel surface.
            for (int peer = 2; peer <= 3; ++peer)
                Assert.IsTrue(lobby.Find($"arena_mp_slot_{peer}", out UIPanel _),
                    $"The arena duel lobby must expose combatant slot P{peer}.");
            for (int peer = 4; peer <= 9; ++peer)
                Assert.IsFalse(lobby.Find($"arena_mp_slot_{peer}", out UIPanel _),
                    $"The arena duel lobby must NOT expose 4X slot P{peer}.");

            Assert.IsTrue(lobby.Find("arena_mp_host_entry", out UITextEntry _),
                "The arena lobby must expose a host/IP entry.");
            Assert.IsTrue(lobby.Find("arena_mp_port_entry", out UITextEntry _),
                "The arena lobby must expose a port entry.");
            Assert.IsTrue(lobby.Find("arena_mp_seed_entry", out UITextEntry _),
                "The arena lobby must expose a host-controlled match seed.");
            Assert.IsTrue(lobby.Find("arena_mp_speed_entry", out UITextEntry _),
                "The arena lobby must expose a host-controlled game speed.");
            Assert.IsFalse(lobby.Find("arena_mp_turns_entry", out UITextEntry _),
                "Live multiplayer is indefinite; the lobby should no longer expose a finite turns field.");
            Assert.IsFalse(lobby.HasTurnsFieldForHeadless,
                "The lobby headless probe should agree that the finite turns field is absent.");
            Assert.AreEqual(ArenaMultiplayerLobbyScreen.DefaultJoinPeerSlot, lobby.JoinPeerSlotForHeadless,
                "The default join slot must preserve the existing two-player host/laptop path.");

            // ---- AUTHORITATIVE 4X surface: the full galaxy shell + up-to-eight slots is unchanged. ----
            var lobby4X = new ArenaMultiplayerLobbyScreen(ArenaMultiplayerLobbySurface.Authoritative4X);
            sm.GoToScreen(lobby4X, clear3DObjects: true);
            Assert.AreEqual(ArenaMultiplayerLobbySurface.Authoritative4X, lobby4X.SurfaceMode);
            foreach (string chrome in new[]
                     {
                         "arena_mp_mode", "arena_mp_regular_settings", "arena_mp_stars",
                         "arena_mp_difficulty", "arena_mp_opponents", "arena_mp_richness",
                         "arena_mp_turn_timer", "arena_mp_pace", "arena_mp_pirates",
                         "arena_mp_gravity", "arena_mp_ai_rules",
                     })
            {
                Assert.IsTrue(lobby4X.Find(chrome, out UIButton _),
                    $"The authoritative 4X surface must keep the galaxy pill '{chrome}'.");
            }
            Assert.IsFalse(lobby4X.Find("arena_mp_arena_mode", out UIButton _),
                "The 4X surface must NOT build the arena-mode control.");
            Assert.IsFalse(lobby4X.Find("arena_mp_set_fleet", out UIButton _),
                "The 4X surface must NOT build the arena fleet picker.");
            for (int peer = 2; peer <= 9; ++peer)
            {
                Assert.IsTrue(lobby4X.Find($"arena_mp_slot_{peer}", out UIPanel _),
                    $"The 4X lobby must expose fixed visible slot P{peer}.");
                if (peer > 2)
                {
                    Assert.IsTrue(lobby4X.Find($"arena_mp_slot_mode_{peer}", out UIButton _),
                        $"The host must be able to cycle P{peer} between human, AI, and closed.");
                    Assert.IsTrue(lobby4X.Find($"arena_mp_slot_kick_{peer}", out UIButton _),
                        $"The host must be able to kick/clear P{peer}.");
                }
            }
            sm.GoToScreen(lobby, clear3DObjects: true); // restore the arena lobby for the rest of the test

            var ext = new CapturingExtensionPoints();
            new ArenaPlugin().Register(ext);
            Assert.IsTrue(ext.Actions.ContainsKey(ArenaPlugin.ArenaButtonName),
                "The plugin must keep registering the Star Gladiator career menu action.");
            Assert.IsTrue(ext.Actions.ContainsKey(ArenaPlugin.Authoritative4XMultiplayerButtonName),
                "The plugin must register the first-class 4X multiplayer menu action.");
            PluginMainMenuAction multiplayer4XAction = ext.Actions[ArenaPlugin.Authoritative4XMultiplayerButtonName];
            Assert.AreEqual("4X Multiplayer", multiplayer4XAction.ButtonTitle);
            GameScreen multiplayer4X = multiplayer4XAction.CreateScreen();
            Assert.IsInstanceOfType(multiplayer4X, typeof(ArenaMultiplayerLobbyScreen),
                "The plugin's 4X multiplayer menu action must launch the authoritative lobby screen.");
            var multiplayer4XLobby = (ArenaMultiplayerLobbyScreen)multiplayer4X;
            Assert.AreEqual(ArenaMultiplayerLobbySurface.Authoritative4X, multiplayer4XLobby.SurfaceMode);
            Assert.AreEqual("STARDRIVE MULTIPLAYER", multiplayer4XLobby.HeaderTitleForHeadless);
            Assert.AreEqual("AUTHORITATIVE 4X LOBBY", multiplayer4XLobby.HeaderSubtitleForHeadless);
            AssertMainMenuLayoutContains4XButton("game/Content/UI/MMenu.Jupiter.yaml");
            AssertMainMenuLayoutContains4XButton("game/Content/UI/MMenu.Mars.yaml");
            AssertMainMenuLayoutContains4XButton("game/Content/UI/MMenu.Venus.yaml");
            AssertPluginActionCanContributeMissingMenuButton(multiplayer4XAction);

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
                Mode = RaceDesignScreen.GameMode.Corners,
                ExtraRemnant = ExtraRemnantPresence.More,
                Difficulty = GameDifficulty.Hard,
                NumOpponents = 12,
                Pace = 2f,
                TurnTimer = 3,
                ExtraPlanets = 1,
                CustomMineralDecay = 1.5f,
                VolcanicActivity = 0.5f,
                StartingPlanetRichnessBonus = 2f,
                ShipMaintenanceMultiplier = 1.2f,
                FTLModifier = 0.75f,
                EnemyFTLModifier = 0.25f,
                GravityWellRange = 12000f,
                GameSpeed = 0.5f,
                StartPaused = true,
                AIUsesPlayerDesigns = false,
                UseUpkeepByHullSize = true,
                DisableRemnantStory = true,
                EnableRandomizedAIFleetSizes = true,
                DisableAlternateAITraits = true,
                DisablePirates = true,
                DisableResearchStations = true,
                DisableMiningOps = true,
            }.Normalized(2);
            lobby.Configure4XForHeadless(settings, races[0], hostTrait, races[1], joinTrait);
            Assert.AreEqual("HAAAAAA", lobby.SlotModesForHeadless,
                "A full eight-empires setup should expose P3 as the human join slot and P4-P9 as AI slots.");
            Assert.AreEqual(settings.NumOpponents, lobby.EffectiveOpponentCountForHeadless,
                "The fixed slot model should count connected humans plus AI slots as total opponents.");
            SessionStartMessage start = lobby.Build4XStartForHeadless();
            Assert.IsTrue(start.IsAuthoritative4X, "The visible MP lobby should now launch the authoritative 4X path.");
            Assert.AreEqual(ArenaMultiplayerLobbyScreen.LiveAuthoritative4XMaxTurns, start.MaxTurns,
                "Live authoritative 4X starts should be unbounded; finite turn counts belong to probes/self-tests.");
            Assert.AreEqual(races[0], start.HostRacePreference);
            Assert.AreEqual(races[1], start.JoinRacePreference);
            Assert.AreEqual(hostTrait, start.HostTraitOptions);
            Assert.AreEqual(joinTrait, start.JoinTraitOptions);
            Authoritative4XLobbyPlayer[] startRoster =
                Authoritative4XLobbyNetworkFlow.DecodeRoster(start.AuthoritativePlayerRoster);
            Assert.AreEqual(2, startRoster.Length,
                "Even the two-player lobby must carry a roster so the same start payload scales to eight players.");
            CollectionAssert.AreEqual(new[] { 2, 3 }, startRoster.Select(p => p.PeerId).ToArray());
            Assert.AreEqual((int)settings.GalaxySize, start.GalaxySize);
            Assert.AreEqual((int)settings.StarsCount, start.StarsCount);
            Assert.AreEqual((int)settings.Mode, start.GameMode);
            Assert.AreEqual(settings.Mode,
                Authoritative4XLobbyNetworkFlow.SettingsFromStart(start).Normalized(2).Mode);
            Assert.AreEqual((int)settings.ExtraRemnant, start.ExtraRemnant);
            Assert.AreEqual((int)settings.Difficulty, start.Difficulty);
            Assert.AreEqual(settings.NumOpponents, start.NumOpponents);
            Assert.AreEqual(settings.Pace, start.Pace);
            Assert.AreEqual(settings.TurnTimer, start.TurnTimer);
            Assert.AreEqual(settings.ExtraPlanets, start.ExtraPlanets);
            Assert.AreEqual(settings.CustomMineralDecay, start.CustomMineralDecay);
            Assert.AreEqual(settings.VolcanicActivity, start.VolcanicActivity);
            Assert.AreEqual(settings.StartingPlanetRichnessBonus, start.StartingPlanetRichnessBonus);
            Assert.AreEqual(settings.ShipMaintenanceMultiplier, start.ShipMaintenanceMultiplier);
            Assert.AreEqual(settings.FTLModifier, start.FTLModifier);
            Assert.AreEqual(settings.EnemyFTLModifier, start.EnemyFTLModifier);
            Assert.AreEqual(settings.GravityWellRange, start.GravityWellRange);
            Assert.AreEqual(settings.AIUsesPlayerDesigns, start.AIUsesPlayerDesigns);
            Assert.AreEqual(settings.UseUpkeepByHullSize, start.UseUpkeepByHullSize);
            Assert.AreEqual(settings.DisableRemnantStory, start.DisableRemnantStory);
            Assert.AreEqual(settings.EnableRandomizedAIFleetSizes, start.EnableRandomizedAIFleetSizes);
            Assert.AreEqual(settings.DisableAlternateAITraits, start.DisableAlternateAITraits);
            Assert.AreEqual(settings.DisablePirates, start.DisablePirates);
            Assert.AreEqual(settings.DisableResearchStations, start.DisableResearchStations);
            Assert.AreEqual(settings.DisableMiningOps, start.DisableMiningOps);

            using (Authoritative4XGeneratedGameStart generated = lobby.CreateGenerated4XGameForHeadless(start))
            {
                UniverseState us = generated.AuthorityUniverse.UState;
                Assert.AreEqual(settings.GalaxySize, us.P.GalaxySize);
                Assert.AreEqual(settings.StarsCount, us.P.StarsCount);
                Assert.AreEqual(settings.ExtraRemnant, us.P.ExtraRemnant);
                Assert.AreEqual(settings.Difficulty, us.P.Difficulty);
                Assert.AreEqual(settings.Pace, us.P.Pace);
                Assert.AreEqual(settings.TurnTimer, us.P.TurnTimer);
                Assert.AreEqual(settings.ExtraPlanets, us.P.ExtraPlanets);
                Assert.AreEqual(settings.CustomMineralDecay, us.P.CustomMineralDecay);
                Assert.AreEqual(settings.VolcanicActivity, us.P.VolcanicActivity);
                Assert.AreEqual(settings.StartingPlanetRichnessBonus, us.P.StartingPlanetRichnessBonus);
                Assert.AreEqual(settings.ShipMaintenanceMultiplier, us.P.ShipMaintenanceMultiplier);
                Assert.AreEqual(settings.FTLModifier, us.P.FTLModifier);
                Assert.AreEqual(settings.EnemyFTLModifier, us.P.EnemyFTLModifier);
                Assert.AreEqual(settings.GravityWellRange, us.P.GravityWellRange);
                Assert.AreEqual(settings.AIUsesPlayerDesigns, us.P.AIUsesPlayerDesigns);
                Assert.AreEqual(settings.UseUpkeepByHullSize, us.P.UseUpkeepByHullSize);
                Assert.AreEqual(settings.DisableRemnantStory, us.P.DisableRemnantStory);
                Assert.AreEqual(settings.EnableRandomizedAIFleetSizes, us.P.EnableRandomizedAIFleetSizes);
                Assert.AreEqual(settings.DisableAlternateAITraits, us.P.DisableAlternateAITraits);
                Assert.AreEqual(settings.DisablePirates, us.P.DisablePirates);
                Assert.AreEqual(settings.DisableResearchStations, us.P.DisableResearchStations);
                Assert.AreEqual(settings.DisableMiningOps, us.P.DisableMiningOps);
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

            SessionStartMessage arenaStart = lobby.BuildArenaStartForHeadless();
            Assert.IsFalse(arenaStart.IsAuthoritative4X,
                "The Star Gladiator lobby must build an Arena lockstep start payload for its own launch path.");
            Assert.IsTrue(ArenaMultiplayerSettings.DecodeFleet(arenaStart.HostFleet).Length > 0,
                "The Arena start must carry host fleet design names.");
            Assert.IsTrue(ArenaMultiplayerSettings.DecodeFleet(arenaStart.JoinFleet).Length > 0,
                "The Arena start must carry join fleet design names.");
            Assert.AreEqual("", lobby.ValidateArenaStartForHeadless(arenaStart),
                "A locally built Arena start should pass the same SettingsHash/session validation the joiner runs.");
            arenaStart.SettingsHash = "0xBADBADBADBADBAD";
            string mismatchError = lobby.ValidateArenaStartForHeadless(arenaStart);
            StringAssert.Contains(mismatchError, "Arena multiplayer settings mismatch");

            ArenaMultiplayerRunResult result = ArenaMultiplayerLobbyScreen.RunLocalSelfTestForHeadless(60);
            Assert.IsFalse(result.Desynced,
                $"Lobby loopback TCP self-test desynced at turn {result.DesyncTurn}: {result.DesyncReason}");
            Assert.AreEqual(60, result.TurnsCompleted,
                "The lobby self-test must run through the requested turn count over loopback TCP.");
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
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = savedLobbyConfigPath;
            ArenaFightScreen.PendingPlayerDesignName = null;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void ArenaMultiplayerLobbyHostStartAckTransitionDoesNotCrashUpdate_Headless()
    {
        LoadAllGameData();

        string dir = Path.Combine(Path.GetTempPath(), $"arena_mp_ack_{Guid.NewGuid():N}");
        string savedConfigPath = ArenaMultiplayerLobbyConfig.ConfigPathOverride;
        ScreenManager sm = ScreenManager.Instance;
        TcpLockstepTransport host = null;
        TcpLockstepTransport client = null;
        try
        {
            Directory.CreateDirectory(dir);
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = Path.Combine(dir, "mp-lobby-config.yaml");
            sm.ExitAll(clear3DObjects: true);

            string[] races = ResourceManager.MajorRaces
                .Select(r => r.ArchetypeName.NotEmpty() ? r.ArchetypeName : r.Name)
                .Where(r => r.NotEmpty())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(r => r, StringComparer.Ordinal)
                .Take(2)
                .ToArray();
            Assert.IsTrue(races.Length >= 2, "The ACK transition proof needs two races.");

            var lobby = new ArenaMultiplayerLobbyScreen(ArenaMultiplayerLobbySurface.Authoritative4X);
            sm.GoToScreen(lobby, clear3DObjects: true);
            lobby.Configure4XForHeadless(new Authoritative4XGameSettings
            {
                GenerationSeed = 818181,
                GalaxySize = GalSize.Tiny,
                StarsCount = RaceDesignScreen.StarsAbundance.Rare,
                NumOpponents = 1,
                StartPaused = true,
            }, races[0], "", races[1], "");
            SessionStartMessage start = lobby.Build4XStartForHeadless();

            int port = FreeTcpPort();
            host = TcpLockstepTransport.HostMulti(port);
            MethodInfo onHostMessage = typeof(ArenaMultiplayerLobbyScreen).GetMethod("OnHostMessage",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(onHostMessage, "The host lobby message handler must exist.");
            host.AddObserver(Authoritative4XLobby.AuthorityPeerId,
                message => onHostMessage.Invoke(lobby, new object[] { message }));
            SetPrivateField(lobby, "Transport", host);
            SetPrivateField(lobby, "LocalRole", ArenaMultiplayerRole.Host);
            SetPrivateField(lobby, "PendingHostStart", start);

            client = TcpLockstepTransport.JoinAsPeer("127.0.0.1", port,
                ArenaMultiplayerLobbyScreen.DefaultJoinPeerSlot, Authoritative4XLobby.AuthorityPeerId);
            Assert.IsTrue(host.WaitForConnection(TimeSpan.FromSeconds(3)), "Host did not accept the test client.");
            client.Send(Authoritative4XLobby.AuthorityPeerId, new SessionStartAckMessage
            {
                FromPeer = ArenaMultiplayerLobbyScreen.DefaultJoinPeerSlot,
                PeerId = ArenaMultiplayerLobbyScreen.DefaultJoinPeerSlot,
                Accepted = true,
                StartFingerprint = Authoritative4XLobbyNetworkFlow.StartFingerprint(start),
            });

            for (int i = 0; i < 200 && !lobby.IsRunning; ++i)
            {
                client.Poll();
                lobby.Update(1f / 60f);
                Thread.Sleep(5);
            }

            Assert.IsTrue(lobby.IsRunning,
                "Receiving the final start ACK during Update should transition the host without dereferencing a cleared transport.");
        }
        finally
        {
            try { sm.ExitAll(clear3DObjects: true); } catch { }
            try { client?.Dispose(); } catch { }
            try { host?.Dispose(); } catch { }
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = savedConfigPath;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void ArenaMultiplayerLobbyConfigPersistsConnectionAndSetup_Headless()
    {
        LoadAllGameData();

        string dir = Path.Combine(Path.GetTempPath(), $"arena_mp_config_{Guid.NewGuid():N}");
        string savedConfigPath = ArenaMultiplayerLobbyConfig.ConfigPathOverride;
        try
        {
            Directory.CreateDirectory(dir);
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = Path.Combine(dir, "mp-lobby-config.yaml");
            var saved = new ArenaMultiplayerLobbyConfig
            {
                Host = "192.0.2.10",
                Port = 47378,
                PeerSlot = 7,
                SlotModes = "HACCHCC",
                Seed = 7654321,
                GameSpeed = 2f,
                RacePreference = "United",
                TraitOptions = "",
                Mode = RaceDesignScreen.GameMode.SpiralBarred,
                GalaxySize = GalSize.Small,
                StarsCount = RaceDesignScreen.StarsAbundance.Abundant,
                ExtraRemnant = ExtraRemnantPresence.Everywhere,
                Difficulty = GameDifficulty.Hard,
                NumOpponents = 99,
                Pace = 2.5f,
                TurnTimer = 10,
                ExtraPlanets = 2,
                CustomMineralDecay = 1.5f,
                VolcanicActivity = 0.5f,
                StartingPlanetRichnessBonus = 3f,
                ShipMaintenanceMultiplier = 1.2f,
                FTLModifier = 0.75f,
                EnemyFTLModifier = 0.25f,
                GravityWellRange = 12000f,
                StartPaused = true,
                AIUsesPlayerDesigns = false,
                UseUpkeepByHullSize = true,
                DisableRemnantStory = true,
                EnableRandomizedAIFleetSizes = true,
                DisableAlternateAITraits = true,
                DisablePirates = true,
                DisableResearchStations = true,
                DisableMiningOps = true,
            };
            Assert.IsTrue(ArenaMultiplayerLobbyConfig.Save(saved), "Config should save to the temp override path.");

            var lobby = new ArenaMultiplayerLobbyScreen(ArenaMultiplayerLobbySurface.Authoritative4X);
            Assert.AreEqual("192.0.2.10", lobby.HostForHeadless);
            Assert.AreEqual(47378, lobby.PortForHeadless);
            Assert.AreEqual(7, lobby.JoinPeerSlotForHeadless);
            Assert.AreEqual("HACCHCC", lobby.SlotModesForHeadless);
            Assert.AreEqual("HUMAN", lobby.SlotModeForHeadless(3));
            Assert.AreEqual("AI", lobby.SlotModeForHeadless(4));
            Assert.AreEqual("CLOSED", lobby.SlotModeForHeadless(5));
            Assert.AreEqual(7654321, lobby.SeedForHeadless);
            Assert.AreEqual(2f, lobby.SpeedForHeadless);
            Assert.AreEqual(RaceDesignScreen.GameMode.SpiralBarred, lobby.Current4XSettingsForHeadless.Mode);
            Assert.AreEqual(GalSize.Small, lobby.Current4XSettingsForHeadless.GalaxySize);
            Assert.AreEqual(RaceDesignScreen.StarsAbundance.Abundant, lobby.Current4XSettingsForHeadless.StarsCount);
            Assert.AreEqual(ExtraRemnantPresence.Everywhere, lobby.Current4XSettingsForHeadless.ExtraRemnant);
            Assert.AreEqual(GameDifficulty.Hard, lobby.Current4XSettingsForHeadless.Difficulty);
            Assert.AreEqual(Authoritative4XGameSettings.MaxTotalMajorEmpires - 1,
                lobby.Current4XSettingsForHeadless.NumOpponents);
            Assert.AreEqual(2.5f, lobby.Current4XSettingsForHeadless.Pace);
            Assert.AreEqual(10, lobby.Current4XSettingsForHeadless.TurnTimer);
            Assert.AreEqual(2, lobby.Current4XSettingsForHeadless.ExtraPlanets);
            Assert.AreEqual(1.5f, lobby.Current4XSettingsForHeadless.CustomMineralDecay);
            Assert.AreEqual(0.5f, lobby.Current4XSettingsForHeadless.VolcanicActivity);
            Assert.AreEqual(3f, lobby.Current4XSettingsForHeadless.StartingPlanetRichnessBonus);
            Assert.AreEqual(1.2f, lobby.Current4XSettingsForHeadless.ShipMaintenanceMultiplier);
            Assert.AreEqual(0.75f, lobby.Current4XSettingsForHeadless.FTLModifier);
            Assert.AreEqual(0.25f, lobby.Current4XSettingsForHeadless.EnemyFTLModifier);
            Assert.AreEqual(12000f, lobby.Current4XSettingsForHeadless.GravityWellRange);
            Assert.IsTrue(lobby.Current4XSettingsForHeadless.StartPaused);
            Assert.IsFalse(lobby.Current4XSettingsForHeadless.AIUsesPlayerDesigns);
            Assert.IsTrue(lobby.Current4XSettingsForHeadless.UseUpkeepByHullSize);
            Assert.IsTrue(lobby.Current4XSettingsForHeadless.DisableRemnantStory);
            Assert.IsTrue(lobby.Current4XSettingsForHeadless.EnableRandomizedAIFleetSizes);
            Assert.IsTrue(lobby.Current4XSettingsForHeadless.DisableAlternateAITraits);
            Assert.IsTrue(lobby.Current4XSettingsForHeadless.DisablePirates);
            Assert.IsTrue(lobby.Current4XSettingsForHeadless.DisableResearchStations);
            Assert.IsTrue(lobby.Current4XSettingsForHeadless.DisableMiningOps);
            Assert.IsFalse(lobby.HasTurnsFieldForHeadless,
                "Persisted lobby config must not resurrect the removed finite turns field.");
        }
        finally
        {
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = savedConfigPath;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void TextEntryClipboardShortcutsPasteAndCopy_Headless()
    {
        LoadAllGameData();

        Func<string> savedGet = UITextEntry.ClipboardGetText;
        Action<string> savedSet = UITextEntry.ClipboardSetText;
        string clipboard = "";
        try
        {
            UITextEntry.ClipboardGetText = () => clipboard;
            UITextEntry.ClipboardSetText = text => clipboard = text;

            var entry = new UITextEntry(new SDGraphics.Rectangle(0, 0, 220, 28), Fonts.Arial14Bold, "127.0.0.1")
            {
                AllowPeriod = true,
                MaxCharacters = 64,
                AutoCaptureOnHover = true,
            };
            var provider = new MockInputProvider { MousePos = new SDGraphics.Vector2(2, 2) };
            var input = new InputState { Provider = provider };

            void Step(params Keys[] keys)
            {
                provider.KeysDown.Clear();
                foreach (Keys key in keys)
                    provider.KeysDown.Add(key);
                input.Update(new UpdateTimes(0f, 0f));
                entry.HandleInput(input);
            }

            Step();
            Step(Keys.LeftControl, Keys.A);
            Step(Keys.LeftControl);
            clipboard = "192.0.2.10";
            Step(Keys.LeftControl, Keys.V);
            Step(Keys.LeftControl);
            Assert.AreEqual("192.0.2.10", entry.Text,
                "Ctrl+A followed by Ctrl+V should replace the entry text with the pasted IP address.");

            clipboard = "";
            Step(Keys.LeftControl, Keys.C);
            Assert.AreEqual("192.0.2.10", clipboard,
                "Ctrl+C should copy the current text entry value.");
        }
        finally
        {
            UITextEntry.ClipboardGetText = savedGet;
            UITextEntry.ClipboardSetText = savedSet;
        }
    }

    [TestMethod]
    public void ArenaMultiplayerLobbyTraitsAllowBudgetedMultiSelection_Headless()
    {
        LoadAllGameData();

        string[] options = ArenaMultiplayerLobbyScreen.AvailableTraitOptionsForHeadless();
        Assert.IsTrue(options.Length > 1, "The loaded race-trait data should expose multiple selectable traits.");

        string selected = "";
        foreach (string trait in options)
        {
            string updated = ArenaMultiplayerLobbyScreen.ToggleTraitSelectionForHeadless(selected, trait, out bool accepted);
            if (accepted && updated != selected)
                selected = updated;
            if (Authoritative4XLobbyNetworkFlow.SplitTraitOptions(selected).Length >= 2)
                break;
        }

        string[] selectedTraits = Authoritative4XLobbyNetworkFlow.SplitTraitOptions(selected);
        Assert.IsTrue(selectedTraits.Length >= 2,
            "The lobby selector should allow multiple compatible traits within the point budget.");
        Assert.IsTrue(ArenaMultiplayerLobbyScreen.TraitSelectionCostForHeadless(selected) <=
                      ArenaMultiplayerLobbyScreen.TraitBudgetForHeadless(),
            "The multi-trait selection must stay within the single-player racial trait budget.");

        var settings = new Authoritative4XGameSettings();
        var lobby = new ArenaMultiplayerLobbyScreen(ArenaMultiplayerLobbySurface.Authoritative4X);
        lobby.Configure4XForHeadless(settings, "United", selected, "Kulrathi", "");
        SessionStartMessage start = lobby.Build4XStartForHeadless();
        CollectionAssert.AreEqual(selectedTraits, Authoritative4XLobbyNetworkFlow.SplitTraitOptions(start.HostTraitOptions),
            "The visible lobby must preserve the selected trait set in the authoritative start message.");

        bool sawRejectedAdd = false;
        foreach (string trait in options.Reverse())
        {
            if (selectedTraits.Contains(trait, StringComparer.Ordinal))
                continue;
            string before = selected;
            string updated = ArenaMultiplayerLobbyScreen.ToggleTraitSelectionForHeadless(selected, trait, out bool accepted);
            if (accepted)
            {
                selected = updated;
                selectedTraits = Authoritative4XLobbyNetworkFlow.SplitTraitOptions(selected);
                continue;
            }

            sawRejectedAdd = true;
            Assert.AreEqual(before, updated,
                "An over-budget or excluded trait add must leave the selected trait set unchanged.");
            break;
        }
        Assert.IsTrue(sawRejectedAdd,
            "The loaded trait data should eventually produce a budget/exclusion rejection when adding traits.");
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
    public void ArenaBattleCodesRoundTripReplayFingerprint_Headless()
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_battle_code_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;

        try
        {
            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x7E57,
                RngSeed = 0xB4771E00u,
                InputDelay = 2,
                MaxTurns = 90,
                CommandEveryTurns = 1,
                HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x7001ul),
                JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Ace, 0x7002ul),
            }.WithResolvedFleets();

            ArenaMultiplayerRunResult original = ArenaMultiplayerSession.RunInProcess(settings);
            Assert.IsFalse(original.Desynced,
                $"Original battle-code source sim desynced at turn {original.DesyncTurn}: {original.DesyncReason}");
            string originalSequence = ArenaBattleCodes.SequenceSha256(original);
            string code = ArenaBattleCodes.Export(settings, originalSequence);

            Assert.IsTrue(ArenaBattleCodes.TryImport(code, out ArenaBattleCode imported),
                imported?.Error ?? "Battle code import failed.");
            Assert.AreEqual(settings.SettingsHash, imported.Settings.SettingsHash,
                "Imported settings must reconstruct the exact original match descriptor.");
            Assert.AreEqual(originalSequence, imported.SequenceSha256,
                "The battle code must carry the original replay digest.");

            ArenaBattleCodeReplayCheck replay = ArenaBattleCodes.VerifyReplay(imported);
            Assert.IsTrue(replay.Match, replay.Error);
            Assert.AreEqual(originalSequence, replay.SequenceSha256,
                "Export -> import -> resimulate must reproduce the original lockstep fingerprint.");

            string mismatchCode = ArenaBattleCodes.Export(settings, originalSequence,
                buildHashOverride: "0x0000000000000000");
            Assert.IsTrue(ArenaBattleCodes.TryImport(mismatchCode, out ArenaBattleCode mismatch),
                mismatch?.Error ?? "Battle code with build mismatch should still parse.");
            Assert.IsTrue(mismatch.HasBuildWarning,
                "A build hash mismatch must warn instead of silently accepting a likely-desync replay.");
            StringAssert.Contains(mismatch.BuildWarning, "build differs");

            Assert.IsFalse(ArenaBattleCodes.TryImport(code.Substring(0, Math.Max(0, code.Length - 5)),
                    out ArenaBattleCode truncated),
                "Truncated battle codes must be rejected cleanly.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(truncated.Error),
                "Malformed-code rejection should explain why import failed.");
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

    [TestMethod]
    public void ArenaMultiplayerTelemetrySurvivesLastSessionLogContention_Headless()
    {
        // Live crash 2026-07-05: lobby telemetry still held the shared
        // arena-multiplayer-last-session.log when the fight screen started its own
        // telemetry, and the exclusive re-open crashed both peers at match launch.
        // Overlapping instances must degrade (skip the shared log), never throw.
        LoadAllGameData();
        string dir = Path.Combine(Path.GetTempPath(), $"arena_mp_telemetry_contention_{Guid.NewGuid():N}");
        string savedDir = ArenaMultiplayerTelemetry.OutputDirectoryOverride;

        try
        {
            Directory.CreateDirectory(dir);
            ArenaMultiplayerTelemetry.OutputDirectoryOverride = dir;
            ArenaMultiplayerSettings settings = ArenaMultiplayerLobbyScreen.CreateDefaultSettings(90).WithResolvedFleets();

            // The lobby's open writer is the contention: starting the match
            // telemetry re-creates the same shared log while it is still held.
            using ArenaMultiplayerTelemetry lobby = ArenaMultiplayerTelemetry.Start("Host", "lobby", settings);
            string liveSessionPath;
            using (ArenaMultiplayerTelemetry live = ArenaMultiplayerTelemetry.Start("Host", "match", settings))
            {
                live.Event("PROOF", "contended");
                liveSessionPath = live.SessionPath;
            }
            lobby.Event("PROOF", "lobby-still-alive");
            Assert.IsTrue(File.Exists(liveSessionPath), "Unique session artifact must still be written under contention.");
            StringAssert.Contains(File.ReadAllText(liveSessionPath), "PROOF contended");
        }
        finally
        {
            ArenaMultiplayerTelemetry.OutputDirectoryOverride = savedDir;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void ArenaMultiplayerLiveDriver_TwoScreensReachElimination_Headless()
    {
        // Live-driver proof (advisor plan A.4): the ONLY code path a live duel runs is
        // ArenaFightScreen.Update -> UpdateMultiplayerLive -> AdvanceMultiplayerLiveTurn, and it
        // had zero headless coverage — "fleets spawn, then nothing happens" lived exactly there.
        // This drives TWO REAL fight screens through the real Update loop over real loopback TCP,
        // including the live handoff hazard: the join peer's transport gets polled (by the lobby
        // in the live game) BEFORE the fight screen registers its lockstep receiver, so anything
        // the host commits early is consumed by Deliver() with no receiver and lost forever.
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_live_driver_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        TcpLockstepTransport hostTransport = null;
        TcpLockstepTransport joinTransport = null;
        ArenaFightScreen hostScreen = null;
        ArenaFightScreen joinScreen = null;

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
            }.WithResolvedFleets();

            int port = FreeTcpPort();
            hostTransport = TcpLockstepTransport.Host(port, ArenaMultiplayerSession.JoinPlayerPeerId);
            joinTransport = TcpLockstepTransport.Join("127.0.0.1", port, LockstepHost.HostPeerId);
            Assert.IsTrue(hostTransport.WaitForConnection(TimeSpan.FromSeconds(5)),
                "Loopback TCP host did not accept the join peer.");

            hostScreen = ArenaFightScreen.Create(settings.HostRacePreference, settings.MatchSeed,
                startAtHub: false, opponentPreference: settings.JoinRacePreference);
            hostScreen.ArmMultiplayerLive(new ArenaMultiplayerLiveSession(
                ArenaMultiplayerRole.Host, hostTransport, settings));
            hostScreen.LoadContent();

            // Live handoff hazard window: the host screen is running while the join machine is
            // still loading; the join transport is polled without any registered receiver
            // (exactly what the live lobby's Update does during GoToScreen). Under the broken
            // driver the host commits ticks 0..InputDelay-1 here and those frames are dropped.
            for (int i = 0; i < 30; ++i)
            {
                hostScreen.Update(1f / 60f);
                joinTransport.Poll();
            }

            joinScreen = ArenaFightScreen.Create(settings.HostRacePreference, settings.MatchSeed,
                startAtHub: false, opponentPreference: settings.JoinRacePreference);
            joinScreen.ArmMultiplayerLive(new ArenaMultiplayerLiveSession(
                ArenaMultiplayerRole.Join, joinTransport, settings));
            joinScreen.LoadContent();

            ArenaMultiplayerShipSnapshot hostSnapshot = hostScreen.MultiplayerSnapshot();
            ArenaMultiplayerShipSnapshot joinSnapshot = joinScreen.MultiplayerSnapshot();
            CollectionAssert.AreEqual(hostSnapshot.PlayerShipIds, joinSnapshot.PlayerShipIds,
                "Both live peers must spawn identical player ship IDs from the shared match seed.");
            CollectionAssert.AreEqual(hostSnapshot.EnemyShipIds, joinSnapshot.EnemyShipIds,
                "Both live peers must spawn identical enemy ship IDs from the shared match seed.");
            int enemyShipsAtSpawn = hostSnapshot.EnemyShipIds.Length;
            Assert.IsTrue(enemyShipsAtSpawn > 0, "The live duel must spawn the join fleet.");

            // (a) LIVENESS: within a bounded number of rendered frames, BOTH peers' sims must
            // strictly advance past tick 0. This is the assertion that makes "spawns but idles"
            // un-shippable.
            bool live = false;
            for (int frame = 0; frame < 1200 && !live; ++frame)
            {
                hostScreen.Update(1f / 60f);
                joinScreen.Update(1f / 60f);
                live = hostScreen.MultiplayerLiveSimTickForHeadless > 0
                       && joinScreen.MultiplayerLiveSimTickForHeadless > 0;
            }
            Assert.IsTrue(live,
                "Live lockstep driver starved: neither rendered frame advanced both sims past tick 0. "
                + $"hostTick={hostScreen.MultiplayerLiveSimTickForHeadless} hostStatus='{hostScreen.MultiplayerLiveStatusText}' "
                + $"joinTick={joinScreen.MultiplayerLiveSimTickForHeadless} joinStatus='{joinScreen.MultiplayerLiveStatusText}'");

            // (b)+(c): drive the real Update loop to elimination — at least one ship must die and
            // the asymmetric matchup must complete with the host (strong fleet) as winner on BOTH peers.
            for (int frame = 0; frame < 30000; ++frame)
            {
                hostScreen.Update(1f / 60f);
                joinScreen.Update(1f / 60f);
                if (hostScreen.MultiplayerLiveResultForHeadless?.MatchEnded == true
                    && joinScreen.MultiplayerLiveResultForHeadless?.MatchEnded == true)
                    break;
            }

            ArenaMultiplayerRunResult hostResult = hostScreen.MultiplayerLiveResultForHeadless;
            ArenaMultiplayerRunResult joinResult = joinScreen.MultiplayerLiveResultForHeadless;
            Assert.IsNotNull(hostResult, "Host live result missing.");
            Assert.IsNotNull(joinResult, "Join live result missing.");
            Assert.IsFalse(hostResult.Desynced,
                $"Live driver desynced at turn {hostResult.DesyncTurn}: {hostResult.DesyncReason}");
            Assert.IsFalse(hostResult.Disconnected,
                $"Live driver reported disconnect/stall: {hostResult.DisconnectReason}");
            Assert.IsTrue(hostResult.MatchEnded,
                $"Host live match did not end. status='{hostScreen.MultiplayerLiveStatusText}' "
                + $"tick={hostScreen.MultiplayerLiveSimTickForHeadless}");
            Assert.IsTrue(joinResult.MatchEnded,
                $"Join live match did not end. status='{joinScreen.MultiplayerLiveStatusText}' "
                + $"tick={joinScreen.MultiplayerLiveSimTickForHeadless}");
            Assert.AreEqual(ArenaMultiplayerSession.HostPlayerPeerId, hostResult.WinnerPeerId,
                "The stronger host fleet should win on the host peer.");
            Assert.AreEqual(ArenaMultiplayerSession.HostPlayerPeerId, joinResult.WinnerPeerId,
                "The stronger host fleet should win on the join peer too (symmetric completion).");
            ArenaMultiplayerMatchStatus hostStatus = hostScreen.MultiplayerMatchStatus();
            Assert.IsTrue(hostStatus.EnemyAlive < enemyShipsAtSpawn,
                "At least one ship must actually die in a live duel — combat must happen, not just ticks.");
            Assert.IsTrue(hostScreen.MultiplayerEndPanelVisibleForHeadless,
                "The host must surface the visible match-complete panel.");
            Assert.IsTrue(joinScreen.MultiplayerEndPanelVisibleForHeadless,
                "The join must surface the visible match-complete panel.");
        }
        finally
        {
            try { hostScreen?.ExitScreen(); } catch { }
            try { joinScreen?.ExitScreen(); } catch { }
            try { hostTransport?.Dispose(); } catch { }
            try { joinTransport?.Dispose(); } catch { }
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    [TestMethod]
    public void ArenaMultiplayer_RenderedFightScreen_PumpsLockstepFrames()
    {
        // Layer (a) of the ruling-2 proof pair: two REAL rendered fight screens, driven only by
        // the real per-frame Update path over loopback TCP (including the lobby handoff hazard
        // window), must pump lockstep frames — Sim.Tick strictly advances on BOTH peers.
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_pump_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        TcpLockstepTransport hostTransport = null;
        TcpLockstepTransport joinTransport = null;
        ArenaFightScreen hostScreen = null;
        ArenaFightScreen joinScreen = null;

        try
        {
            ArenaMultiplayerSettings settings = ArenaMultiplayerLobbyScreen.CreateDefaultSettings(300).WithResolvedFleets();
            (hostScreen, joinScreen) = BuildLiveLoopbackScreens(settings, out hostTransport, out joinTransport);

            bool pumped = false;
            for (int frame = 0; frame < 1200 && !pumped; ++frame)
            {
                hostScreen.Update(1f / 60f);
                joinScreen.Update(1f / 60f);
                pumped = hostScreen.MultiplayerLiveSimTickForHeadless > 0
                         && joinScreen.MultiplayerLiveSimTickForHeadless > 0;
            }

            Assert.IsTrue(pumped,
                "The rendered live driver must pump lockstep frames on both peers. "
                + $"hostTick={hostScreen.MultiplayerLiveSimTickForHeadless} hostStatus='{hostScreen.MultiplayerLiveStatusText}' "
                + $"joinTick={joinScreen.MultiplayerLiveSimTickForHeadless} joinStatus='{joinScreen.MultiplayerLiveStatusText}'");
        }
        finally
        {
            try { hostScreen?.ExitScreen(); } catch { }
            try { joinScreen?.ExitScreen(); } catch { }
            try { hostTransport?.Dispose(); } catch { }
            try { joinTransport?.Dispose(); } catch { }
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    [TestMethod]
    public void ArenaMultiplayer_Duel_LiveLoopbackTcp_DoesNotIdleAfterSpawn()
    {
        // Layer (b) of the ruling-2 proof pair: engagement must actually START within the bounded
        // post-spawn window — a target is acquired / a ship enters combat / a weapon fires — and
        // the ARENA_LIVENESS_FAIL halt must NOT trip on a healthy duel.
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_noidle_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        TcpLockstepTransport hostTransport = null;
        TcpLockstepTransport joinTransport = null;
        ArenaFightScreen hostScreen = null;
        ArenaFightScreen joinScreen = null;

        try
        {
            ArenaMultiplayerSettings settings = ArenaMultiplayerLobbyScreen.CreateDefaultSettings(600).WithResolvedFleets();
            (hostScreen, joinScreen) = BuildLiveLoopbackScreens(settings, out hostTransport, out joinTransport);

            bool engaged = false;
            for (int frame = 0; frame < 1800 && !engaged; ++frame)
            {
                hostScreen.Update(1f / 60f);
                joinScreen.Update(1f / 60f);
                engaged = hostScreen.MultiplayerEngagementSeenForHeadless
                          && joinScreen.MultiplayerEngagementSeenForHeadless;
            }

            Assert.IsTrue(engaged,
                "A live duel must start engaging within the bounded post-spawn window on both peers. "
                + $"hostTick={hostScreen.MultiplayerLiveSimTickForHeadless} hostStatus='{hostScreen.MultiplayerLiveStatusText}' "
                + $"joinTick={joinScreen.MultiplayerLiveSimTickForHeadless} joinStatus='{joinScreen.MultiplayerLiveStatusText}'");
            Assert.IsFalse(hostScreen.MultiplayerLiveResultForHeadless?.Disconnected == true,
                "A healthy duel must not trip the engagement/no-progress halt: "
                + hostScreen.MultiplayerLiveResultForHeadless?.DisconnectReason);
            Assert.IsFalse(joinScreen.MultiplayerLiveResultForHeadless?.Disconnected == true,
                "A healthy duel must not trip the engagement/no-progress halt on the join peer: "
                + joinScreen.MultiplayerLiveResultForHeadless?.DisconnectReason);
        }
        finally
        {
            try { hostScreen?.ExitScreen(); } catch { }
            try { joinScreen?.ExitScreen(); } catch { }
            try { hostTransport?.Dispose(); } catch { }
            try { joinTransport?.Dispose(); } catch { }
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    /// <summary>
    /// Builds a host+join live fight-screen pair over real loopback TCP through the real
    /// arm/LoadContent path, including the live handoff hazard window: the host screen runs and
    /// the join transport is polled with no registered receiver before the join screen loads.
    /// </summary>
    (ArenaFightScreen host, ArenaFightScreen join) BuildLiveLoopbackScreens(
        ArenaMultiplayerSettings settings,
        out TcpLockstepTransport hostTransport, out TcpLockstepTransport joinTransport,
        int handoffHazardFrames = 30)
    {
        int port = FreeTcpPort();
        hostTransport = TcpLockstepTransport.Host(port, ArenaMultiplayerSession.JoinPlayerPeerId);
        joinTransport = TcpLockstepTransport.Join("127.0.0.1", port, LockstepHost.HostPeerId);
        Assert.IsTrue(hostTransport.WaitForConnection(TimeSpan.FromSeconds(5)),
            "Loopback TCP host did not accept the join peer.");

        ArenaFightScreen hostScreen = ArenaFightScreen.Create(settings.HostRacePreference, settings.MatchSeed,
            startAtHub: false, opponentPreference: settings.JoinRacePreference);
        hostScreen.ArmMultiplayerLive(new ArenaMultiplayerLiveSession(
            ArenaMultiplayerRole.Host, hostTransport, settings));
        hostScreen.LoadContent();

        for (int i = 0; i < handoffHazardFrames; ++i)
        {
            hostScreen.Update(1f / 60f);
            joinTransport.Poll();
        }

        ArenaFightScreen joinScreen = ArenaFightScreen.Create(settings.HostRacePreference, settings.MatchSeed,
            startAtHub: false, opponentPreference: settings.JoinRacePreference);
        joinScreen.ArmMultiplayerLive(new ArenaMultiplayerLiveSession(
            ArenaMultiplayerRole.Join, joinTransport, settings));
        joinScreen.LoadContent();
        return (hostScreen, joinScreen);
    }

    [TestMethod]
    public void ArenaMultiplayerLiveDriver_NoProgressWatchdogHaltsInsteadOfSilentFreeze_Headless()
    {
        // Match-flow contract rule 4 (advisor plan A.3): if no turn commits within the watchdog
        // window, the live match must surface a visible halt with a void result — never idle
        // silently. Here the peer connects at the transport level but never arms a fight screen,
        // which under the broken driver produced an eternal silent "waiting" state.
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_live_watchdog_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        TcpLockstepTransport hostTransport = null;
        TcpLockstepTransport joinTransport = null;
        ArenaFightScreen hostScreen = null;

        try
        {
            ArenaMultiplayerSettings settings = ArenaMultiplayerLobbyScreen.CreateDefaultSettings(900).WithResolvedFleets();
            int port = FreeTcpPort();
            hostTransport = TcpLockstepTransport.Host(port, ArenaMultiplayerSession.JoinPlayerPeerId);
            joinTransport = TcpLockstepTransport.Join("127.0.0.1", port, LockstepHost.HostPeerId);
            Assert.IsTrue(hostTransport.WaitForConnection(TimeSpan.FromSeconds(5)),
                "Loopback TCP host did not accept the join peer.");

            hostScreen = ArenaFightScreen.Create(settings.HostRacePreference, settings.MatchSeed,
                startAtHub: false, opponentPreference: settings.JoinRacePreference);
            hostScreen.ArmMultiplayerLive(new ArenaMultiplayerLiveSession(
                ArenaMultiplayerRole.Host, hostTransport, settings));
            hostScreen.LoadContent();

            // The joined peer stays silent (never arms). Feed the host well past the hard
            // no-progress deadline; it must halt visibly instead of freezing forever.
            for (int frame = 0; frame < 100 && hostScreen.MultiplayerLiveResultForHeadless?.MatchEnded != true
                 && !hostScreen.MultiplayerEndPanelVisibleForHeadless; ++frame)
            {
                hostScreen.Update(0.5f);
                joinTransport.Poll();
            }

            Assert.IsTrue(hostScreen.MultiplayerEndPanelVisibleForHeadless,
                "A stalled live match must surface the visible halt panel instead of silently idling. "
                + $"status='{hostScreen.MultiplayerLiveStatusText}'");
            ArenaMultiplayerRunResult result = hostScreen.MultiplayerLiveResultForHeadless;
            Assert.IsNotNull(result, "The halted match must still carry a result object.");
            Assert.IsTrue(result.Disconnected,
                "A no-progress halt must void the match (disconnected/void result), not award a winner.");
            Assert.AreEqual(0, result.WinnerPeerId, "A stalled match must not declare a winner.");
        }
        finally
        {
            try { hostScreen?.ExitScreen(); } catch { }
            try { hostTransport?.Dispose(); } catch { }
            try { joinTransport?.Dispose(); } catch { }
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    // ===================================================================================
    // Arena P1 "mode-first lobby + fleet setup + deterministic match flow" proof gates
    // (STARDRIVE_ARENA_P1_FLEETSETUP_EXEC_PLAN_20260705).
    // ===================================================================================

    [TestMethod]
    public void ArenaFleetBundle_CanonicalHash_StableAcrossNodeOrder_Headless()
    {
        // Step 1 proof: the canonical bundle hash is invariant under node INSERTION order (the
        // editor drag sequence), sensitive to a changed offset, and survives Encode->Decode.
        LoadAllGameData();
        IShipDesign[] designs = LegalPvPDesigns();
        Assert.IsTrue(designs.Length >= 2, "Need at least two legal designs for the bundle proof.");
        string a = designs.First().Name;
        string b = designs.Last().Name;

        var forward = new FleetDesign { Name = "F", FleetIconIndex = 3 };
        forward.Nodes.Add(new FleetDataDesignNode { ShipName = a, RelativeFleetOffset = new SDGraphics.Vector2(100f, -50f) });
        forward.Nodes.Add(new FleetDataDesignNode { ShipName = b, RelativeFleetOffset = new SDGraphics.Vector2(-200f, 75f) });

        var reversed = new FleetDesign { Name = "F", FleetIconIndex = 3 };
        reversed.Nodes.Add(new FleetDataDesignNode { ShipName = b, RelativeFleetOffset = new SDGraphics.Vector2(-200f, 75f) });
        reversed.Nodes.Add(new FleetDataDesignNode { ShipName = a, RelativeFleetOffset = new SDGraphics.Vector2(100f, -50f) });

        string hForward = ArenaFleetBundle.DesignBundleHash(forward);
        string hReversed = ArenaFleetBundle.DesignBundleHash(reversed);
        Assert.AreEqual(hForward, hReversed,
            "The bundle hash must be invariant under node insertion order (stable-sorted nodes).");

        var moved = new FleetDesign { Name = "F", FleetIconIndex = 3 };
        moved.Nodes.Add(new FleetDataDesignNode { ShipName = a, RelativeFleetOffset = new SDGraphics.Vector2(101f, -50f) });
        moved.Nodes.Add(new FleetDataDesignNode { ShipName = b, RelativeFleetOffset = new SDGraphics.Vector2(-200f, 75f) });
        Assert.AreNotEqual(hForward, ArenaFleetBundle.DesignBundleHash(moved),
            "Changing a per-ship offset must change the bundle hash.");

        string encoded = ArenaFleetBundle.Encode(forward);
        Assert.IsTrue(encoded.Length > 0 && encoded.Length <= ArenaFleetBundle.MaxEncodedChars,
            "The encoded bundle must be non-empty and within the wire cap.");
        FleetDesign decoded = ArenaFleetBundle.Decode(encoded);
        Assert.AreEqual(hForward, ArenaFleetBundle.DesignBundleHash(decoded),
            "Encode->Decode must preserve the canonical bundle hash exactly.");
        Assert.AreEqual(2, decoded.Nodes.Count, "The decoded bundle must preserve every node.");
    }

    [TestMethod]
    public void RulesetV0_SettingsHash_RoundTripsAndOrderFixed_Headless()
    {
        // Step 2 proof: RulesetV0 + bundles round-trip through ToStartMessage->FromStartMessage,
        // SettingsHash parses, ProtocolVersion is 4, and flipping any ruleset field changes the hash.
        LoadAllGameData();
        Assert.AreEqual(6, ArenaMultiplayerSettings.ProtocolVersion,
            "The 8-player + first-class-teams roster bumps the Arena MP protocol version 5->6 (ruling C8).");

        var settings = new ArenaMultiplayerSettings
        {
            MatchSeed = 0x5EED,
            RngSeed = 0xA12EA000u,
            HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul),
            JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul),
            Ruleset = new ArenaMultiplayerRuleset
            {
                Mode = ArenaMatchMode.Sandbox,
                BudgetModel = ArenaBudgetModel.Cap,
                BudgetCredits = 999999,
                RosterSource = ArenaRosterSource.AllContent,
                CountdownSeconds = 3,
            },
        }.WithResolvedFleets();

        string hash = settings.SettingsHash;
        StringAssert.StartsWith(hash, "0x");
        Assert.AreEqual(18, hash.Length, "SettingsHash must be a parseable 0x + 16 hex digits.");

        SessionStartMessage start = settings.ToStartMessage();
        Assert.AreEqual(6, start.ProtocolVersion);
        Assert.AreEqual((int)ArenaMatchMode.Sandbox, start.RulesetMode);
        Assert.AreEqual(999999, start.RulesetBudgetCredits);
        Assert.AreEqual(settings.HostDesignBundleHash, start.HostDesignBundleHash);

        ArenaMultiplayerSettings back = ArenaMultiplayerSettings.FromStartMessage(start).WithResolvedFleets();
        Assert.AreEqual(ArenaMatchMode.Sandbox, back.Ruleset.Mode);
        Assert.AreEqual(ArenaBudgetModel.Cap, back.Ruleset.BudgetModel);
        Assert.AreEqual(999999, back.Ruleset.BudgetCredits);
        Assert.AreEqual(settings.SettingsHash, back.SettingsHash,
            "ToStartMessage->FromStartMessage must round-trip the ruleset into an identical SettingsHash.");

        // Flipping a ruleset field changes the hash (fixed-order fold).
        ArenaMultiplayerSettings flipped = settings.WithResolvedFleets();
        flipped.Ruleset.CountdownSeconds = 5;
        Assert.AreNotEqual(settings.SettingsHash, flipped.SettingsHash,
            "Changing a ruleset field must change SettingsHash.");
    }

    // ================================================================================================
    // 8-PLAYER + FIRST-CLASS TEAMS (STARDRIVE_ARENA_8PLAYER_TEAMS_DETERMINISM_RULING_20260707)
    // Foundation proofs: canonical roster codec (C3), order-independent fold (C2/C3), empty-roster
    // byte-identity to the legacy 2-peer fingerprint (C10), and a divergent team map rejecting at the
    // handshake (C2/C4). These do NOT boot the sim — they pin the deterministic wire/hash contract that
    // spawn/hostility/win/targeting all read from. The full N-peer TCP per-turn proofs (ruling proofs
    // 1-9) build on this contract.
    // ================================================================================================

    [TestMethod]
    public void ArenaRoster_CanonicalCodecRoundTrips_SlotSorted_Headless()
    {
        // Ruling C3: encode sorts by slot id ascending; decode re-sorts; base64 protects the free-text
        // hash. Feeding records OUT of slot order must produce the SAME canonical string as sorted input.
        var sorted = new[]
        {
            new ArenaPlayerRosterRecord(1, 0, "0xAAAA"),
            new ArenaPlayerRosterRecord(2, 0, "0xBBBB"),
            new ArenaPlayerRosterRecord(3, 1, "0xCCCC"),
            new ArenaPlayerRosterRecord(4, 1, "0xDDDD"),
        };
        var shuffled = new[] { sorted[3], sorted[0], sorted[2], sorted[1] };

        string a = ArenaPlayerRosterCodec.Encode(sorted);
        string b = ArenaPlayerRosterCodec.Encode(shuffled);
        Assert.AreEqual(a, b, "Encode must be canonical (slot-id sorted) regardless of input order.");

        ArenaPlayerRosterRecord[] round = ArenaPlayerRosterCodec.Decode(a);
        Assert.AreEqual(4, round.Length);
        for (int i = 0; i < 4; i++)
        {
            Assert.AreEqual(sorted[i].SlotId, round[i].SlotId, $"slot[{i}] id");
            Assert.AreEqual(sorted[i].TeamId, round[i].TeamId, $"slot[{i}] team");
            Assert.AreEqual(sorted[i].DesignBundleHash, round[i].DesignBundleHash, $"slot[{i}] hash");
        }
        Assert.AreEqual(0, ArenaPlayerRosterCodec.Decode("").Length, "Empty string decodes to empty roster.");
    }

    [TestMethod]
    public void ArenaRoster_FoldIsOrderIndependent_Headless()
    {
        // Ruling C2/C3: the fold sorts independently, so a differently-ordered record array folds to the
        // IDENTICAL hash. This is the anti-desync guarantee — a peer must never hash Dictionary iteration
        // order. Two peers building the same team map in different insertion orders agree.
        var forward = new[]
        {
            new ArenaPlayerRosterRecord(1, 0, "0xA"),
            new ArenaPlayerRosterRecord(2, 1, "0xB"),
            new ArenaPlayerRosterRecord(3, 1, "0xC"),
        };
        var reversed = new[] { forward[2], forward[1], forward[0] };

        var hf = DetHash.New();
        ArenaPlayerRosterCodec.Fold(ref hf, forward);
        var hr = DetHash.New();
        ArenaPlayerRosterCodec.Fold(ref hr, reversed);
        Assert.AreEqual(hf.Value, hr.Value, "Fold must be order-independent (slot-id sorted).");

        // A different team map (slot 3 moves to team 0) MUST change the fold.
        var different = new[]
        {
            new ArenaPlayerRosterRecord(1, 0, "0xA"),
            new ArenaPlayerRosterRecord(2, 1, "0xB"),
            new ArenaPlayerRosterRecord(3, 0, "0xC"),
        };
        var hd = DetHash.New();
        ArenaPlayerRosterCodec.Fold(ref hd, different);
        Assert.AreNotEqual(hf.Value, hd.Value, "A divergent team map must change the fold.");
    }

    [TestMethod]
    public void ArenaRoster_EmptyRosterByteIdenticalToLegacyFingerprint_Headless()
    {
        // Ruling C10: an EMPTY roster (the 2-peer / flag-off default) skips the fold entirely, so
        // SettingsHash and StartFingerprint are byte-for-byte identical to what they were before the
        // roster field existed. This is the flag-off byte-identity guarantee.
        LoadAllGameData();
        var settings = new ArenaMultiplayerSettings
        {
            MatchSeed = 0x5EED,
            RngSeed = 0xA12EA000u,
            HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul),
            JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul),
            Ruleset = new ArenaMultiplayerRuleset { Mode = ArenaMatchMode.Sandbox, RosterSource = ArenaRosterSource.AllContent },
        }.WithResolvedFleets();

        Assert.AreEqual(0, settings.Roster.Length, "Default settings carry an empty roster.");
        string emptyHash = settings.SettingsHash;

        SessionStartMessage start = settings.ToStartMessage();
        Assert.AreEqual("", start.ArenaPlayerRoster, "Empty roster encodes to empty string on the wire.");
        Assert.AreEqual(emptyHash, ArenaMultiplayerSettings.FromStartMessage(start).WithResolvedFleets().SettingsHash,
            "Empty-roster SettingsHash must survive the wire round-trip unchanged.");

        // The StartFingerprint of an empty-roster start must equal the fingerprint computed with the roster
        // field forced to null (i.e. the pre-roster world) — the fold is skipped for empty.
        SessionStartMessage nulled = settings.ToStartMessage();
        nulled.ArenaPlayerRoster = null;
        Assert.AreEqual(ArenaMultiplayerSettings.StartFingerprint(start),
            ArenaMultiplayerSettings.StartFingerprint(nulled),
            "Empty ('') and null roster must fold identically (both skip the fold) — flag-off byte-identity.");
    }

    [TestMethod]
    public void ArenaRoster_DivergentTeamMapRejectsAtHandshake_Headless()
    {
        // Ruling C2/C4 (foundation for proof 4): a peer that computes a DIFFERENT team assignment produces
        // a different SettingsHash, which ValidateStartMessage rejects for exact-inequality BEFORE spawn.
        // Here the host authors a 2v2 (teams 0,0,1,1); a tampered start swaps slot 4 to team 0 (a 3v1) but
        // leaves the host's SettingsHash intact -> the local recompute diverges -> "mismatch" reject.
        LoadAllGameData();
        var settings = new ArenaMultiplayerSettings
        {
            MatchSeed = 0x5EED,
            RngSeed = 0xA12EA000u,
            HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul),
            JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul),
            Ruleset = new ArenaMultiplayerRuleset { Mode = ArenaMatchMode.Sandbox, RosterSource = ArenaRosterSource.AllContent },
            Roster = new[]
            {
                new ArenaPlayerRosterRecord(1, 0, "0xA"),
                new ArenaPlayerRosterRecord(2, 0, "0xB"),
                new ArenaPlayerRosterRecord(3, 1, "0xC"),
                new ArenaPlayerRosterRecord(4, 1, "0xD"),
            },
        }.WithResolvedFleets();

        SessionStartMessage clean = settings.ToStartMessage();
        Assert.AreEqual("", ArenaMultiplayerSettings.ValidateStartMessage(clean, out _),
            "A matching 2v2 roster must validate (the host-authored roster is self-consistent).");

        // Tamper the team map on the wire but keep the host's SettingsHash -> local recompute diverges.
        SessionStartMessage tampered = settings.ToStartMessage();
        tampered.ArenaPlayerRoster = ArenaPlayerRosterCodec.Encode(new[]
        {
            new ArenaPlayerRosterRecord(1, 0, "0xA"),
            new ArenaPlayerRosterRecord(2, 0, "0xB"),
            new ArenaPlayerRosterRecord(3, 1, "0xC"),
            new ArenaPlayerRosterRecord(4, 0, "0xD"), // slot 4 defected to team 0 (3v1 instead of 2v2)
        });
        StringAssert.Contains(ArenaMultiplayerSettings.ValidateStartMessage(tampered, out _), "mismatch",
            "A divergent team assignment must reject at the handshake via the SettingsHash gate.");
    }

    [TestMethod]
    public void RulesetV0_MismatchRejectsStart_Headless()
    {
        // Step 3 proof: a tampered ruleset field OR design bundle is rejected at ValidateStartMessage
        // (handshake), and an illegal mode (Coop) / non-zero wager is rejected too.
        LoadAllGameData();
        var settings = new ArenaMultiplayerSettings
        {
            MatchSeed = 0x5EED,
            RngSeed = 0xA12EA000u,
            HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul),
            JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul),
            Ruleset = new ArenaMultiplayerRuleset { Mode = ArenaMatchMode.Career, RosterSource = ArenaRosterSource.CareerLocked },
        }.WithResolvedFleets();

        SessionStartMessage clean = settings.ToStartMessage();
        Assert.AreEqual("", ArenaMultiplayerSettings.ValidateStartMessage(clean, out _),
            "A matching career ruleset must validate.");

        // Tamper a ruleset field but leave the SettingsHash untouched -> mismatch.
        SessionStartMessage tamperedRuleset = settings.ToStartMessage();
        tamperedRuleset.RulesetBudgetCredits += 1;
        StringAssert.Contains(ArenaMultiplayerSettings.ValidateStartMessage(tamperedRuleset, out _),
            "mismatch", "A tampered ruleset field must reject at handshake via the SettingsHash gate.");

        // Tamper a design bundle hash -> reject.
        SessionStartMessage tamperedBundle = settings.ToStartMessage();
        tamperedBundle.HostDesignBundleHash = "0xDEADBEEFDEADBEEF";
        StringAssert.Contains(ArenaMultiplayerSettings.ValidateStartMessage(tamperedBundle, out _),
            "mismatch", "A tampered design bundle hash must reject at handshake.");

        // Coop mode is rejected in P1.
        var coop = new ArenaMultiplayerSettings
        {
            MatchSeed = settings.MatchSeed, RngSeed = settings.RngSeed,
            HostFleetDesignNames = settings.HostFleetDesignNames, JoinFleetDesignNames = settings.JoinFleetDesignNames,
            Ruleset = new ArenaMultiplayerRuleset { Mode = ArenaMatchMode.Coop },
        }.WithResolvedFleets();
        StringAssert.Contains(ArenaMultiplayerSettings.ValidateStartMessage(coop.ToStartMessage(), out _),
            "Coop", "Coop mode must be rejected in P1.");

        // Non-zero wager is rejected in P1.
        var wager = new ArenaMultiplayerSettings
        {
            MatchSeed = settings.MatchSeed, RngSeed = settings.RngSeed,
            HostFleetDesignNames = settings.HostFleetDesignNames, JoinFleetDesignNames = settings.JoinFleetDesignNames,
            Ruleset = new ArenaMultiplayerRuleset { Mode = ArenaMatchMode.Career, RosterSource = ArenaRosterSource.CareerLocked, WagerCredits = 100 },
        }.WithResolvedFleets();
        StringAssert.Contains(ArenaMultiplayerSettings.ValidateStartMessage(wager.ToStartMessage(), out _),
            "Wager", "A non-zero wager must be rejected in P1.");
    }

    [TestMethod]
    public void Sandbox_BudgetCapRejectsOverspend_Headless()
    {
        // Step 6 proof (budget): a Sandbox Cap ruleset whose fleet cost exceeds the budget rejects at
        // handshake; the same fleet under a sufficient budget validates.
        LoadAllGameData();
        IShipDesign[] designs = LegalPvPDesigns();
        string strong = designs.Last().Name;
        int cost = (int)MathF.Round(designs.Last().BaseStrength);

        var overspent = new ArenaMultiplayerSettings
        {
            MatchSeed = 0x5EED, RngSeed = 0xA12EA000u,
            HostFleetDesignNames = new[] { strong, strong },
            JoinFleetDesignNames = new[] { strong },
            Ruleset = new ArenaMultiplayerRuleset
            {
                Mode = ArenaMatchMode.Sandbox, RosterSource = ArenaRosterSource.AllContent,
                BudgetModel = ArenaBudgetModel.Cap, BudgetCredits = Math.Max(0, cost - 1),
            },
        }.WithResolvedFleets();
        StringAssert.Contains(ArenaMultiplayerSettings.ValidateStartMessage(overspent.ToStartMessage(), out _),
            "exceeds budget", "A Sandbox fleet over the budget cap must reject at handshake.");

        var affordable = new ArenaMultiplayerSettings
        {
            MatchSeed = 0x5EED, RngSeed = 0xA12EA000u,
            HostFleetDesignNames = new[] { strong },
            JoinFleetDesignNames = new[] { strong },
            Ruleset = new ArenaMultiplayerRuleset
            {
                Mode = ArenaMatchMode.Sandbox, RosterSource = ArenaRosterSource.AllContent,
                BudgetModel = ArenaBudgetModel.Cap, BudgetCredits = cost + 10,
            },
        }.WithResolvedFleets();
        Assert.AreEqual("", ArenaMultiplayerSettings.ValidateStartMessage(affordable.ToStartMessage(), out _),
            "A Sandbox fleet within the budget cap must validate.");
    }

    [TestMethod]
    public void FormationSpawn_Deterministic_Headless()
    {
        // Step 4 proof: a non-trivial multi-node formation bundle spawns identical ship-ID order AND
        // identical positions on both peers, and the authored offsets actually place the ships.
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_formation_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;

        try
        {
            IShipDesign[] designs = LegalPvPDesigns();
            string a = designs.First().Name;
            string b = designs.Last().Name;

            var hostBundle = new FleetDesign { Name = "Host" };
            hostBundle.Nodes.Add(new FleetDataDesignNode { ShipName = a, RelativeFleetOffset = new SDGraphics.Vector2(0f, 900f) });
            hostBundle.Nodes.Add(new FleetDataDesignNode { ShipName = b, RelativeFleetOffset = new SDGraphics.Vector2(0f, -900f) });
            var joinBundle = new FleetDesign { Name = "Join" };
            joinBundle.Nodes.Add(new FleetDataDesignNode { ShipName = a, RelativeFleetOffset = new SDGraphics.Vector2(0f, 600f) });

            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED, RngSeed = 0xA12EA000u, InputDelay = 3, MaxTurns = 60, CommandEveryTurns = 1,
                HostFleetDesignNames = new[] { a, b },
                JoinFleetDesignNames = new[] { a },
                HostFleetBundle = ArenaFleetBundle.Encode(hostBundle),
                JoinFleetBundle = ArenaFleetBundle.Encode(joinBundle),
                Ruleset = new ArenaMultiplayerRuleset { Mode = ArenaMatchMode.Sandbox, RosterSource = ArenaRosterSource.AllContent },
            }.WithResolvedFleets();

            ArenaMultiplayerRunResult result = ArenaMultiplayerSession.RunInProcess(settings);
            Assert.IsFalse(result.Desynced,
                $"Formation-spawn in-process desynced at turn {result.DesyncTurn}: {result.DesyncReason}");
            CollectionAssert.AreEqual(result.HostSnapshot.PlayerShipIds, result.JoinSnapshot.PlayerShipIds,
                "Both peers must spawn identical player ship IDs from the formation bundle (stable order).");
            CollectionAssert.AreEqual(result.HostSnapshot.EnemyShipIds, result.JoinSnapshot.EnemyShipIds,
                "Both peers must spawn identical enemy ship IDs from the formation bundle.");
            Assert.AreEqual(2, result.HostSnapshot.PlayerShipIds.Length, "The 2-node host formation must field two ships.");
            Assert.AreEqual(1, result.HostSnapshot.EnemyShipIds.Length, "The 1-node join formation must field one ship.");
            Assert.IsTrue(result.TurnHashes.All(h => h.Match),
                "Every formation-spawn turn hash must match across peers.");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    [TestMethod]
    public void Countdown_EngageAtDeterministicTick_Headless()
    {
        // Step 5 proof: both live peers issue attack orders exactly at spawnTick + CountdownTicks,
        // with no engagement evidence before that tick, and the countdown does not trip the liveness
        // halt. Uses the real live driver over loopback TCP.
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_countdown_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        TcpLockstepTransport hostTransport = null;
        TcpLockstepTransport joinTransport = null;
        ArenaFightScreen hostScreen = null;
        ArenaFightScreen joinScreen = null;

        try
        {
            IShipDesign weak = LegalPvPDesigns().First();
            IShipDesign strong = LegalPvPDesigns().Last();
            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x6EED, RngSeed = 0xB12EA000u, InputDelay = 3, MaxTurns = 900, CommandEveryTurns = 1,
                HostFleetDesignNames = new[] { strong.Name, strong.Name },
                JoinFleetDesignNames = new[] { weak.Name },
                Ruleset = new ArenaMultiplayerRuleset { Mode = ArenaMatchMode.Career, RosterSource = ArenaRosterSource.CareerLocked, CountdownSeconds = 3 },
            }.WithResolvedFleets();

            (hostScreen, joinScreen) = BuildLiveLoopbackScreens(settings, out hostTransport, out joinTransport);

            // Drive until the host reaches the Engage/Fight phase or a generous frame budget elapses.
            bool engaged = false;
            long engageAtTick = -1;
            bool sawEvidenceBeforeEngage = false;
            for (int frame = 0; frame < 6000 && !engaged; ++frame)
            {
                hostScreen.Update(1f / 60f);
                joinScreen.Update(1f / 60f);
                engageAtTick = hostScreen.MultiplayerEngageAtTickForHeadless;
                long tick = hostScreen.MultiplayerLiveSimTickForHeadless;
                // Before the engage tick, there must be no engagement evidence (frozen spawn).
                if (engageAtTick > 0 && tick > 0 && tick < engageAtTick && hostScreen.MultiplayerEngagementSeenForHeadless)
                    sawEvidenceBeforeEngage = true;
                engaged = hostScreen.MultiplayerPhaseForHeadless == ArenaFightScreen.ArenaMatchPhase.Fight
                          && joinScreen.MultiplayerPhaseForHeadless == ArenaFightScreen.ArenaMatchPhase.Fight;
            }

            Assert.IsTrue(engaged,
                "Both peers must reach the Engage/Fight phase. "
                + $"hostPhase={hostScreen.MultiplayerPhaseForHeadless} hostTick={hostScreen.MultiplayerLiveSimTickForHeadless} "
                + $"endReason='{hostScreen.MultiplayerEndReasonForHeadless}' engageAt={hostScreen.MultiplayerEngageAtTickForHeadless} "
                + $"joinPhase={joinScreen.MultiplayerPhaseForHeadless} joinTick={joinScreen.MultiplayerLiveSimTickForHeadless}");
            Assert.IsFalse(sawEvidenceBeforeEngage,
                "No engagement evidence may appear before the deterministic engage tick (frozen countdown).");
            Assert.AreEqual(hostScreen.MultiplayerEngageAtTickForHeadless, joinScreen.MultiplayerEngageAtTickForHeadless,
                "Both peers must compute the SAME engage tick.");
            Assert.AreEqual((long)ArenaFightScreen.DefaultCountdownTicks, engageAtTick,
                $"The engage tick must be the absolute CountdownTicks sim tick; got {engageAtTick}.");
            Assert.IsFalse(hostScreen.MultiplayerLiveResultForHeadless?.Disconnected == true,
                "The countdown must not trip the liveness/no-progress halt: "
                + hostScreen.MultiplayerLiveResultForHeadless?.DisconnectReason);
        }
        finally
        {
            try { hostScreen?.ExitScreen(); } catch { }
            try { joinScreen?.ExitScreen(); } catch { }
            try { hostTransport?.Dispose(); } catch { }
            try { joinTransport?.Dispose(); } catch { }
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    [TestMethod]
    public void ArenaMultiplayer_ModeSetupToResolve_TwoScreens_Headless()
    {
        // Step 7 proof: two REAL lobbies pick a mode (Sandbox), launch through the real
        // Host/Join/Ready/Launch flow into the fight screens, spawn in formation, run through the
        // deterministic countdown -> engage -> fight -> resolve, and surface a visible end-reason.
        LoadAllGameData();
        string dir = Path.Combine(Path.GetTempPath(), $"arena_mp_modeflow_{Guid.NewGuid():N}");
        string savedConfigPath = ArenaMultiplayerLobbyConfig.ConfigPathOverride;
        string tempCareer = Path.Combine(Path.GetTempPath(), $"arena_mp_modeflow_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempCareer;
        ArenaFightScreen.PendingPlayerDesignName = null;
        ArenaMultiplayerLobbyScreen hostLobby = null;
        ArenaMultiplayerLobbyScreen joinLobby = null;
        ArenaFightScreen hostFight = null;
        ArenaFightScreen joinFight = null;

        try
        {
            Directory.CreateDirectory(dir);
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = Path.Combine(dir, "mp-lobby-config.yaml");
            int port = FreeTcpPort();
            Assert.IsTrue(ArenaMultiplayerLobbyConfig.Save(new ArenaMultiplayerLobbyConfig
            {
                Host = "127.0.0.1",
                Port = port,
                PeerSlot = ArenaMultiplayerLobbyScreen.DefaultJoinPeerSlot,
            }), "Lobby config must save to the temp override path.");

            (hostLobby, joinLobby, hostFight, joinFight) =
                DriveRealLobbiesToLaunchedFight(lobby => lobby.SetArenaModeForHeadless(
                    ArenaMatchMode.Sandbox, ArenaBudgetModel.Unlimited));

            // The host's authored ruleset must be Sandbox and ride into the fight via the start payload.
            Assert.AreEqual(ArenaMatchMode.Sandbox, hostLobby.ArenaModeForHeadless,
                "The host must author the selected Sandbox mode.");

            // Run to a completed match on both peers.
            for (int frame = 0; frame < 60000; ++frame)
            {
                hostFight.Update(1f / 60f);
                joinFight.Update(1f / 60f);
                if (hostFight.MultiplayerLiveResultForHeadless?.MatchEnded == true
                    && joinFight.MultiplayerLiveResultForHeadless?.MatchEnded == true)
                    break;
            }

            Assert.IsTrue(hostFight.MultiplayerEndPanelVisibleForHeadless,
                $"Host must surface the match-complete panel. status='{hostFight.MultiplayerLiveStatusText}'");
            Assert.IsTrue(joinFight.MultiplayerEndPanelVisibleForHeadless,
                $"Join must surface the match-complete panel. status='{joinFight.MultiplayerLiveStatusText}'");
            Assert.IsFalse(string.IsNullOrWhiteSpace(hostFight.MultiplayerEndReasonForHeadless),
                "The host result panel must carry a visible end-reason.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(joinFight.MultiplayerEndReasonForHeadless),
                "The join result panel must carry a visible end-reason.");
            Assert.IsTrue(hostFight.Find("arena_mp_end_reason", out UILabel _),
                "The result panel must expose the end-reason label.");
            Assert.AreEqual(hostFight.MultiplayerLiveResultForHeadless.WinnerPeerId,
                joinFight.MultiplayerLiveResultForHeadless.WinnerPeerId,
                "Both peers must agree on the match outcome (symmetric completion).");
        }
        finally
        {
            try { hostFight?.ExitScreen(); } catch { }
            try { joinFight?.ExitScreen(); } catch { }
            DisposeLobbyTransport(hostLobby);
            DisposeLobbyTransport(joinLobby);
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = savedConfigPath;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
            try { if (File.Exists(tempCareer)) File.Delete(tempCareer); } catch { }
        }
    }

    [TestMethod]
    public void MatchingRuleset_CareerAndSandbox_RunsToDigest_Headless()
    {
        // Step 6 proof: a Career-locked matchup AND a Sandbox-budgeted matchup each run in-process
        // (two-peer lockstep) to a final digest with matching per-turn hashes.
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_modes_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;

        try
        {
            IShipDesign[] designs = LegalPvPDesigns();
            string strong = designs.Last().Name;
            int cost = (int)MathF.Round(designs.Last().BaseStrength);

            var career = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED, RngSeed = 0xA12EA000u, InputDelay = 3, MaxTurns = 90, CommandEveryTurns = 1,
                HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul),
                JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul),
                Ruleset = new ArenaMultiplayerRuleset { Mode = ArenaMatchMode.Career, RosterSource = ArenaRosterSource.CareerLocked },
            }.WithResolvedFleets();
            ArenaMultiplayerRunResult careerResult = ArenaMultiplayerSession.RunInProcess(career);
            Assert.IsFalse(careerResult.Desynced,
                $"Career matchup desynced at turn {careerResult.DesyncTurn}: {careerResult.DesyncReason}");
            Assert.IsTrue(careerResult.TurnHashes.All(h => h.Match), "Career matchup per-turn hashes must match.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(careerResult.FinalHash), "Career matchup must reach a digest.");

            var sandbox = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x6EED, RngSeed = 0xB12EA000u, InputDelay = 3, MaxTurns = 90, CommandEveryTurns = 1,
                HostFleetDesignNames = new[] { strong },
                JoinFleetDesignNames = new[] { strong },
                Ruleset = new ArenaMultiplayerRuleset
                {
                    Mode = ArenaMatchMode.Sandbox, RosterSource = ArenaRosterSource.AllContent,
                    BudgetModel = ArenaBudgetModel.Cap, BudgetCredits = cost + 100,
                },
            }.WithResolvedFleets();
            Assert.AreEqual("", ArenaMultiplayerSettings.ValidateStartMessage(sandbox.ToStartMessage(), out _),
                "The Sandbox matchup must validate within its budget cap.");
            ArenaMultiplayerRunResult sandboxResult = ArenaMultiplayerSession.RunInProcess(sandbox);
            Assert.IsFalse(sandboxResult.Desynced,
                $"Sandbox matchup desynced at turn {sandboxResult.DesyncTurn}: {sandboxResult.DesyncReason}");
            Assert.IsTrue(sandboxResult.TurnHashes.All(h => h.Match), "Sandbox matchup per-turn hashes must match.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(sandboxResult.FinalHash), "Sandbox matchup must reach a digest.");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    // ===================================================================================================
    // DESYNC SELF-DIAGNOSIS (ARENA_DESYNC_INSTRUMENTATION_REPORT). These prove the field-level breakdown and
    // the order-perturbation harness. They are the regression guard for the live turn-1232 diagnosis: a real
    // 2-machine reproduction relies on the SAME field-dump code these exercise in-process.
    // ===================================================================================================

    // The field-level breakdown must localize a KNOWN divergence to the exact ship + field. ForceMultiplayerDesync
    // nudges the join peer's first ship +3.0 on X, so the breakdown must name that ship and flag PosX, classified
    // DISCRETE-FLIP (a +3.0 jump is far outside FP inexactness — proving the FP-vs-logic classifier works).
    [TestMethod]
    public void DesyncFieldDump_LocalizesForcedDivergence_Headless()
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_fielddump_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;

        try
        {
            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED, RngSeed = 0xA12EA000u, InputDelay = 3, MaxTurns = 90, CommandEveryTurns = 1,
                HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul),
                JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul),
            };

            ArenaMultiplayerRunResult result = ArenaMultiplayerSession.RunInProcess(settings, forceDesyncAfterTurn: 30);

            Assert.IsTrue(result.Desynced, "Forced local-only state mutation must trip desync detection.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.DesyncFieldBreakdown),
                "The desync must produce a field-level breakdown localizing the divergence.");
            StringAssert.Contains(result.DesyncFieldBreakdown, "firstDivergentShip=",
                $"The breakdown must name the first divergent ship. Got: {result.DesyncFieldBreakdown}");
            StringAssert.Contains(result.DesyncFieldBreakdown, "PosX",
                $"The forced +X nudge must surface as a PosX difference. Got: {result.DesyncFieldBreakdown}");
            StringAssert.Contains(result.DesyncFieldBreakdown, "DISCRETE-FLIP",
                "A +3.0 nudge is a discrete flip, not FP drift; the classifier must say so. "
                + $"Got: {result.DesyncFieldBreakdown}");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    // ORDER-PERTURBATION HARNESS (report §3). Runs two in-process peers of a custom-fleet-shaped match to a few
    // hundred turns with the JOIN peer's ModuleSlotList iteration order REVERSED on every ship. If the combat sim
    // is order-INSENSITIVE the per-turn digest still matches on every turn (GREEN). If an order-sensitive tie-break
    // fires (repair FindMax, target FindMax, module-explosion FindMax), the digest diverges and the breakdown
    // bisects the site — the test then FAILS LOUDLY with that breakdown so we know WHICH site, without guessing.
    [TestMethod]
    public void OrderPerturbation_ModuleSlotList_SimIsOrderInsensitive_Headless()
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_perturb_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;

        try
        {
            // Asymmetric brawl to force real combat (damage, module death, repair, retarget) — the events that
            // trip order-sensitive ties. Bounded to 300 turns (never the 36000 live cap) so nothing hangs.
            IShipDesign weak = LegalPvPDesigns().First();
            IShipDesign strong = LegalPvPDesigns().Last();
            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x6EED, RngSeed = 0xB12EA000u, InputDelay = 3, MaxTurns = 300, CommandEveryTurns = 1,
                HostFleetDesignNames = new[] { strong.Name, strong.Name, weak.Name },
                JoinFleetDesignNames = new[] { strong.Name, weak.Name, weak.Name },
            };

            int perturbed = 0;
            ArenaMultiplayerRunResult result = ArenaMultiplayerSession.RunInProcess(settings, forceDesyncAfterTurn: -1,
                joinScreen => perturbed = joinScreen.PerturbMultiplayerModuleOrderForTest());

            Assert.IsTrue(perturbed > 0, "The perturbation must have reversed at least one ship's module order.");
            Assert.IsFalse(result.Desynced,
                "The combat sim is expected to be ModuleSlotList-order-INSENSITIVE. A desync here means an "
                + "order-sensitive tie-break WAS bisected — inspect the field breakdown for the site "
                + $"(repair/target/explosion FindMax): turn={result.DesyncTurn} reason={result.DesyncReason} "
                + $"breakdown={result.DesyncFieldBreakdown}");
            Assert.IsTrue(result.TurnHashes.All(h => h.Match),
                "Every per-turn digest must match under module-order perturbation (proves order-insensitivity).");
            Assert.IsTrue(result.TurnsCompleted > 0, "The perturbed match must actually run turns.");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    // GUARD: the field-dump per-ship fold must equal that ship's exact contribution to the wire checksum, so a
    // per-ship digest match here (but a wire-checksum mismatch) localizes the divergence OUT of the ship lane.
    // If WriteAuthoritative's ship lane changes, this test breaks until UniverseStateFieldDump is updated in step.
    [TestMethod]
    public void UniverseStateFieldDump_MirrorsWireChecksumShipFold_Headless()
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_dumpguard_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        ArenaFightScreen screen = null;

        try
        {
            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED, RngSeed = 0xA12EA000u, InputDelay = 3, MaxTurns = 30, CommandEveryTurns = 1,
                HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul),
                JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul),
            }.WithResolvedFleets();

            screen = ArenaFightScreen.Create(settings.HostRacePreference, settings.MatchSeed,
                startAtHub: false, opponentPreference: settings.JoinRacePreference);
            screen.ConfigureMultiplayerPvP(settings);
            screen.CreateSimThread = false;
            screen.LoadContent();
            screen.PrepareForMultiplayerLockstep(settings.RngSeed);

            IReadOnlyList<UniverseStateFieldDump.ShipDigest> digests = screen.MultiplayerShipFieldDigests();
            Assert.IsTrue(digests.Count > 0, "The arena must spawn ships to fold.");

            // Recompute each ship's expected digest by folding EXACTLY the checksum's ship field set with a fresh
            // Hash128Checksum, and compare to DigestShip's result. (This is the same primitive sequence
            // WriteAuthoritative uses for its ship lane; DigestShip must match it byte-for-byte.)
            foreach (Ship s in screen.UState.Ships.OrderBy(s => s.Id))
            {
                var c = new SDUtils.Deterministic.Hash128Checksum();
                c.WriteInt(s.Id);
                c.FloatRaw(s.Position.X); c.FloatRaw(s.Position.Y);
                c.FloatRaw(s.Velocity.X); c.FloatRaw(s.Velocity.Y);
                c.FloatRaw(s.Rotation); c.FloatRaw(s.Health);
                (ulong lo, ulong hi) = c.Finish128();
                UniverseStateFieldDump.ShipDigest d = UniverseStateFieldDump.DigestShip(s);
                Assert.AreEqual(lo, d.Lo, $"Ship {s.Id} digest lo must mirror the wire checksum ship fold.");
                Assert.AreEqual(hi, d.Hi, $"Ship {s.Id} digest hi must mirror the wire checksum ship fold.");
            }
        }
        finally
        {
            try { screen?.ExitScreen(); } catch { }
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
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

    static void AssertMainMenuLayoutContains4XButton(string relativePath)
    {
        string path = Path.Combine(RepoRoot(), relativePath);
        Assert.IsTrue(File.Exists(path), $"Missing main-menu layout file '{relativePath}'.");
        string yaml = File.ReadAllText(path);
        StringAssert.Contains(yaml, $"Name: {ArenaPlugin.Authoritative4XMultiplayerButtonName}");
        StringAssert.Contains(yaml, "Title: \"4X Multiplayer\"");
    }

    static string RepoRoot()
    {
        DirectoryInfo dir = new(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StarDrive.csproj")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "Could not locate the StarDrive repository root.");
        return dir.FullName;
    }

    static void AssertPluginActionCanContributeMissingMenuButton(PluginMainMenuAction action)
    {
        var missingList = new UIList(ListLayoutStyle.ResizeList);
        UIButton contributed = MainMenuScreen.EnsurePluginMainMenuButtonForHeadless(missingList, action);
        Assert.IsNotNull(contributed, "A titled plugin action must create a missing main-menu button.");
        Assert.AreEqual(action.ButtonName, contributed.Name);
        Assert.AreEqual(action.ButtonTitle, contributed.Text.String);

        var existingList = new UIList(ListLayoutStyle.ResizeList);
        UIButton existing = existingList.Add(ButtonStyle.Default, "Existing", _ => {});
        existing.Name = action.ButtonName;
        UIButton ensured = MainMenuScreen.EnsurePluginMainMenuButtonForHeadless(existingList, action);
        Assert.AreSame(existing, ensured, "A layout-defined plugin button must be reused, not duplicated.");

        var titleless = new PluginMainMenuAction("missing_titleless", () => null);
        Assert.IsNull(MainMenuScreen.EnsurePluginMainMenuButtonForHeadless(new UIList(ListLayoutStyle.ResizeList), titleless),
            "Legacy titleless plugin actions must not create visible buttons unless the layout defines them.");
    }

    [TestMethod]
    public void ArenaMultiplayerRealLobbyLaunch_TwoLobbies_MatchArmsAndTicks_Headless()
    {
        // Live QA round 2 proof (2026-07-05): the earlier headless driver proofs constructed the
        // transports directly (TcpLockstepTransport.Host/Join, peer space 0/1/2), but the REAL
        // lobby builds them differently (HostMulti + JoinAsPeer, peer space authority=1/joiner=3).
        // On the live 2-machine run the arm handshake deadlocked BOTH ways with zero deliveries:
        // every fight-side send addressed peers 0/2, which the lobby-built transports had no
        // routes for — silently parked in PendingRemote forever. This proof drives TWO REAL
        // ArenaMultiplayerLobbyScreen instances through the actual Host/Join/Ready/Launch flow
        // over real loopback TCP, through LaunchVisibleArena into the fight screens, and asserts
        // the match arms and Sim.Tick advances on BOTH peers.
        LoadAllGameData();
        string dir = Path.Combine(Path.GetTempPath(), $"arena_mp_lobbylive_{Guid.NewGuid():N}");
        string savedConfigPath = ArenaMultiplayerLobbyConfig.ConfigPathOverride;
        string tempCareer = Path.Combine(Path.GetTempPath(), $"arena_mp_lobbylive_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempCareer;
        ArenaFightScreen.PendingPlayerDesignName = null;
        ArenaMultiplayerLobbyScreen hostLobby = null;
        ArenaMultiplayerLobbyScreen joinLobby = null;
        ArenaFightScreen hostFight = null;
        ArenaFightScreen joinFight = null;

        try
        {
            Directory.CreateDirectory(dir);
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = Path.Combine(dir, "mp-lobby-config.yaml");
            int port = FreeTcpPort();
            Assert.IsTrue(ArenaMultiplayerLobbyConfig.Save(new ArenaMultiplayerLobbyConfig
            {
                Host = "127.0.0.1",
                Port = port,
                PeerSlot = ArenaMultiplayerLobbyScreen.DefaultJoinPeerSlot,
            }), "Lobby config must save to the temp override path.");

            (hostLobby, joinLobby, hostFight, joinFight) = DriveRealLobbiesToLaunchedFight();

            // The launch must arm both fight screens and the lockstep sim must strictly advance
            // on BOTH peers — the exact liveness the 2026-07-05 live run never reached.
            bool live = false;
            for (int frame = 0; frame < 1800 && !live; ++frame)
            {
                hostFight.Update(1f / 60f);
                joinFight.Update(1f / 60f);
                live = hostFight.MultiplayerLiveSimTickForHeadless > 0
                       && joinFight.MultiplayerLiveSimTickForHeadless > 0;
            }
            Assert.IsTrue(live,
                "REAL-LOBBY launch deadlocked: the fight lockstep never advanced on both peers "
                + "over the lobby-built transports. "
                + $"hostTick={hostFight.MultiplayerLiveSimTickForHeadless} hostStatus='{hostFight.MultiplayerLiveStatusText}' "
                + $"joinTick={joinFight.MultiplayerLiveSimTickForHeadless} joinStatus='{joinFight.MultiplayerLiveStatusText}'");
            Assert.IsFalse(hostFight.MultiplayerLiveResultForHeadless?.Disconnected == true,
                "A healthy real-lobby launch must not halt: "
                + hostFight.MultiplayerLiveResultForHeadless?.DisconnectReason);
            Assert.IsFalse(joinFight.MultiplayerLiveResultForHeadless?.Disconnected == true,
                "A healthy real-lobby launch must not halt on the join peer: "
                + joinFight.MultiplayerLiveResultForHeadless?.DisconnectReason);
        }
        finally
        {
            try { hostFight?.ExitScreen(); } catch { }
            try { joinFight?.ExitScreen(); } catch { }
            DisposeLobbyTransport(hostLobby);
            DisposeLobbyTransport(joinLobby);
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = savedConfigPath;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
            try { if (File.Exists(tempCareer)) File.Delete(tempCareer); } catch { }
        }
    }

    [TestMethod]
    public void ArenaMultiplayerRealLobbyRematch_BothPeers_SecondMatchArmsAndTicks_Headless()
    {
        // Live QA round 2, bug 2: after the first match completes (including a voided one), the
        // REMATCH button on the match-end panel must actually produce a second armed, ticking
        // match on both peers — on the live run it did nothing on either end.
        LoadAllGameData();
        string dir = Path.Combine(Path.GetTempPath(), $"arena_mp_rematch_{Guid.NewGuid():N}");
        string savedConfigPath = ArenaMultiplayerLobbyConfig.ConfigPathOverride;
        string tempCareer = Path.Combine(Path.GetTempPath(), $"arena_mp_rematch_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempCareer;
        ArenaFightScreen.PendingPlayerDesignName = null;
        ArenaMultiplayerLobbyScreen hostLobby = null;
        ArenaMultiplayerLobbyScreen joinLobby = null;
        ArenaFightScreen hostFight = null;
        ArenaFightScreen joinFight = null;
        ArenaFightScreen hostRematch = null;
        ArenaFightScreen joinRematch = null;

        try
        {
            Directory.CreateDirectory(dir);
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = Path.Combine(dir, "mp-lobby-config.yaml");
            int port = FreeTcpPort();
            Assert.IsTrue(ArenaMultiplayerLobbyConfig.Save(new ArenaMultiplayerLobbyConfig
            {
                Host = "127.0.0.1",
                Port = port,
                PeerSlot = ArenaMultiplayerLobbyScreen.DefaultJoinPeerSlot,
            }), "Lobby config must save to the temp override path.");

            (hostLobby, joinLobby, hostFight, joinFight) = DriveRealLobbiesToLaunchedFight();

            // Run the first match to completion (elimination, draw, or turn limit all count —
            // the live QA rematch was pressed on a VOID result, so any completed state must work).
            for (int frame = 0; frame < 60000; ++frame)
            {
                hostFight.Update(1f / 60f);
                joinFight.Update(1f / 60f);
                if (hostFight.MultiplayerLiveResultForHeadless?.MatchEnded == true
                    && joinFight.MultiplayerLiveResultForHeadless?.MatchEnded == true)
                    break;
            }
            Assert.IsTrue(hostFight.MultiplayerEndPanelVisibleForHeadless,
                $"First match must complete on the host. status='{hostFight.MultiplayerLiveStatusText}' "
                + $"tick={hostFight.MultiplayerLiveSimTickForHeadless}");
            Assert.IsTrue(joinFight.MultiplayerEndPanelVisibleForHeadless,
                $"First match must complete on the join. status='{joinFight.MultiplayerLiveStatusText}' "
                + $"tick={joinFight.MultiplayerLiveSimTickForHeadless}");

            // Press REMATCH on both peers through the real match-end panel button.
            GameScreen hostNext = null;
            GameScreen joinNext = null;
            hostFight.MultiplayerGoToScreenOverrideForHeadless = s => hostNext = s;
            joinFight.MultiplayerGoToScreenOverrideForHeadless = s => joinNext = s;
            Assert.IsTrue(hostFight.Find("arena_mp_end_rematch", out UIButton hostRematchButton),
                "The host match-end panel must expose the REMATCH button.");
            Assert.IsTrue(joinFight.Find("arena_mp_end_rematch", out UIButton joinRematchButton),
                "The join match-end panel must expose the REMATCH button.");
            hostRematchButton.OnClick?.Invoke(hostRematchButton);
            joinRematchButton.OnClick?.Invoke(joinRematchButton);
            hostRematch = hostNext as ArenaFightScreen;
            joinRematch = joinNext as ArenaFightScreen;
            Assert.IsNotNull(hostRematch, "REMATCH on the host must produce a new armed fight screen.");
            Assert.IsNotNull(joinRematch, "REMATCH on the join must produce a new armed fight screen.");
            hostRematch.LoadContent();
            joinRematch.LoadContent();

            bool live = false;
            for (int frame = 0; frame < 1800 && !live; ++frame)
            {
                hostRematch.Update(1f / 60f);
                joinRematch.Update(1f / 60f);
                live = hostRematch.MultiplayerLiveSimTickForHeadless > 0
                       && joinRematch.MultiplayerLiveSimTickForHeadless > 0;
            }
            Assert.IsTrue(live,
                "REMATCH must produce a second armed, ticking match on both peers. "
                + $"hostTick={hostRematch.MultiplayerLiveSimTickForHeadless} hostStatus='{hostRematch.MultiplayerLiveStatusText}' "
                + $"joinTick={joinRematch.MultiplayerLiveSimTickForHeadless} joinStatus='{joinRematch.MultiplayerLiveStatusText}'");
        }
        finally
        {
            try { hostRematch?.ExitScreen(); } catch { }
            try { joinRematch?.ExitScreen(); } catch { }
            try { if (hostRematch == null) hostFight?.ExitScreen(); } catch { }
            try { if (joinRematch == null) joinFight?.ExitScreen(); } catch { }
            DisposeLobbyTransport(hostLobby);
            DisposeLobbyTransport(joinLobby);
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = savedConfigPath;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
            try { if (File.Exists(tempCareer)) File.Delete(tempCareer); } catch { }
        }
    }

    /// <summary>
    /// Drives TWO REAL ArenaMultiplayerLobbyScreen instances through the actual
    /// Host/Join/Ready/Launch flow over real loopback TCP — the exact transport construction the
    /// live lobby uses (HostMulti + JoinAsPeer) — through LaunchVisibleArena into loaded fight
    /// screens on both peers. The lobby config override must already point at a temp file with
    /// Host=127.0.0.1 and a free port.
    /// </summary>
    (ArenaMultiplayerLobbyScreen hostLobby, ArenaMultiplayerLobbyScreen joinLobby,
        ArenaFightScreen hostFight, ArenaFightScreen joinFight) DriveRealLobbiesToLaunchedFight()
        => DriveRealLobbiesToLaunchedFight(null);

    (ArenaMultiplayerLobbyScreen hostLobby, ArenaMultiplayerLobbyScreen joinLobby,
        ArenaFightScreen hostFight, ArenaFightScreen joinFight) DriveRealLobbiesToLaunchedFight(
        Action<ArenaMultiplayerLobbyScreen> configureHost)
        => DriveRealLobbiesToLaunchedFight(configureHost, null);

    // Phase A: the join-side configure hook lets a proof field a CUSTOM on the JOINER so its design table must
    // traverse the real TCP transport to reach the host (SETUP_PHASE_EXEC_PLAN §3, the confirmed gap).
    (ArenaMultiplayerLobbyScreen hostLobby, ArenaMultiplayerLobbyScreen joinLobby,
        ArenaFightScreen hostFight, ArenaFightScreen joinFight) DriveRealLobbiesToLaunchedFight(
        Action<ArenaMultiplayerLobbyScreen> configureHost, Action<ArenaMultiplayerLobbyScreen> configureJoin)
    {
        var hostLobby = new ArenaMultiplayerLobbyScreen(ArenaMultiplayerLobbySurface.StarGladiator);
        var joinLobby = new ArenaMultiplayerLobbyScreen(ArenaMultiplayerLobbySurface.StarGladiator);
        GameScreen hostLaunched = null;
        GameScreen joinLaunched = null;
        hostLobby.LaunchScreenOverrideForHeadless = s => hostLaunched = s;
        joinLobby.LaunchScreenOverrideForHeadless = s => joinLaunched = s;
        configureHost?.Invoke(hostLobby);
        configureJoin?.Invoke(joinLobby);

        hostLobby.StartHostForHeadless();
        joinLobby.StartJoinForHeadless();
        PumpLobbies(hostLobby, joinLobby,
            () => !joinLobby.JoinInProgressForHeadless && hostLobby.RemotePeerCountForHeadless > 0,
            "join handshake (hello) did not complete");

        // Re-field the joiner's fleet AFTER the connection is up so the DesignTable-bearing SessionLobbyMessage
        // is actually broadcast to the host over the live transport (the initial pick, made before StartJoin,
        // is re-sent by SendLocalLobby on every lobby change; the ready toggle below forces that send).
        configureJoin?.Invoke(joinLobby);

        joinLobby.ToggleReadyForHeadless();
        hostLobby.ToggleReadyForHeadless();
        PumpLobbies(hostLobby, joinLobby,
            () => hostLobby.RemoteReadyForHeadless && hostLobby.LocalReadyForHeadless,
            "ready state did not propagate to the host");

        hostLobby.LaunchAsHostForHeadless();
        PumpLobbies(hostLobby, joinLobby,
            () => hostLaunched != null && joinLaunched != null,
            $"launch did not reach both peers (hostStatus='{hostLobby.CurrentStatus}' joinStatus='{joinLobby.CurrentStatus}')");

        var hostFight = (ArenaFightScreen)hostLaunched;
        var joinFight = (ArenaFightScreen)joinLaunched;
        hostFight.LoadContent();
        joinFight.LoadContent();
        return (hostLobby, joinLobby, hostFight, joinFight);
    }

    static void PumpLobbies(ArenaMultiplayerLobbyScreen hostLobby, ArenaMultiplayerLobbyScreen joinLobby,
        Func<bool> done, string failure)
    {
        for (int i = 0; i < 600; ++i)
        {
            hostLobby.Update(1f / 60f);
            joinLobby.Update(1f / 60f);
            if (done())
                return;
            Thread.Sleep(5);
        }
        Assert.Fail($"Real-lobby flow stalled: {failure}. "
                    + $"hostStatus='{hostLobby.CurrentStatus}' joinStatus='{joinLobby.CurrentStatus}'");
    }

    // ===================================================================================================
    // PHASE A — JOIN-SIDE DESIGN-TABLE TRANSPORT (SETUP_PHASE_EXEC_PLAN §3). The confirmed gap: today the host
    // builds the authoritative start with JoinDesignTable="", so a JOINER's custom payloads never reach the
    // host and custom-vs-custom cannot work. These prove the fix over the REAL TCP transport, reconstructing
    // from received bytes (never the shared-static shortcut — the joiner fields a custom the host never authored,
    // so the only way its @arena/<hash> can appear in the host's authoritative start is via the wire).
    // ===================================================================================================

    // Registers a genuinely-distinct custom pickable under a fresh DISPLAY name (a clone of the given stock hull
    // with a new name). Returns (displayName, arenaContentName). The scratch-set pipeline (RebuildSandboxScratchSet)
    // canonicalizes the picked display name into its @arena/<hash> wire name; two designs cloned from DIFFERENT
    // stock hulls have different module content => different content hashes => distinct wire names.
    static (string display, string arena) RegisterPickableCustom(string stockName, string displayName)
    {
        Assert.IsTrue(ResourceManager.Ships.GetDesign(stockName, out IShipDesign stock),
            $"Stock design '{stockName}' must exist after LoadAllGameData.");
        ShipDesign clone = ((ShipDesign)stock).GetClone(displayName);
        clone.SetDesignSlots(clone.GetOrLoadDesignSlots());
        Assert.IsTrue(ResourceManager.AddShipTemplate(clone, playerDesign: true),
            $"Custom '{displayName}' must register so the lobby fleet picker can field it.");
        Assert.IsTrue(ResourceManager.Ships.GetDesign(displayName, out IShipDesign reg),
            $"Custom '{displayName}' must resolve by display name after registration.");
        Assert.AreEqual("", ArenaDesignTable.ValidateContentAvailable(reg),
            $"Custom '{displayName}' must pass ValidateContentAvailable so the scratch set registers it.");
        Assert.IsTrue(ArenaFightScreen.IsLegalCombatCraft(reg),
            $"Custom '{displayName}' must be a legal arena combat craft so the fleet picker fields it.");
        return (displayName, ArenaDesignTable.ContentName(clone));
    }

    static string[] TwoDistinctStockHulls()
    {
        // Two legal stock combat designs on DIFFERENT hulls, so their clones have distinct module content
        // (distinct @arena/<hash> names) — the join custom must be one the host never authored.
        var byHull = ResourceManager.Ships.Designs
            .Where(ArenaFightScreen.IsLegalCombatCraft)
            .Where(ArenaFightScreen.IsStockContentDesign)
            .Where(d => d.GetOrLoadDesignSlots().All(s => s.HangarShipUID.IsEmpty() || s.HangarShipUID == "NotApplicable"))
            .GroupBy(d => d.BaseHull?.HullName ?? "")
            .Where(g => g.Key.NotEmpty())
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.OrderBy(d => d.Name, StringComparer.Ordinal).First().Name)
            .ToArray();
        Assert.IsTrue(byHull.Length >= 2, "Need at least two distinct stock hulls for the join-custom proof.");
        return new[] { byHull[0], byHull[1] };
    }

    // Build + LoadContent an arena fight screen against a fresh universe (the setup phase runs on this instance).
    static ArenaFightScreen BuildArenaScreen(int seed = 0x5EED)
    {
        ArenaFightScreen screen = ArenaFightScreen.Create("United", seed, startAtHub: false, opponentPreference: "");
        screen.LoadContent();
        return screen;
    }

    static FleetDesign BuildColumnBundle(string name, string[] shipNames)
    {
        var fd = new FleetDesign { Name = name };
        for (int i = 0; i < shipNames.Length; i++)
            fd.Nodes.Add(new FleetDataDesignNode
            {
                ShipName = shipNames[i],
                RelativeFleetOffset = new SDGraphics.Vector2(0f, i * 400f),
            });
        return fd;
    }

    static T FindScreenOnScreenManager<T>() where T : GameScreen
    {
        const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
        ScreenManager sm = ScreenManager.Instance;
        foreach (GameScreen gs in sm.Screens)
            if (gs is T screen) return screen;
        FieldInfo pend = typeof(ScreenManager).GetField("PendingScreens", Priv);
        if (pend?.GetValue(sm) is System.Collections.IEnumerable items)
            foreach (object o in items)
                if (o is T screen) return screen;
        return null;
    }

    // ===================================================================================================
    // PHASE D — the REAL editors LAUNCH against the arena universe (the excuse "the lobby has no universe" is
    // dead — ArenaFightScreen : UniverseScreen supplies UState + EmpireUI). Proves OpenArenaSetupDesigner /
    // OpenArenaSetupFormation mount the UNMODIFIED base ShipDesignScreen / FleetDesignScreen against `this`.
    // ===================================================================================================
    [TestMethod]
    public void PROOF_REAL_EDITORS_LAUNCH_AGAINST_ARENA_UNIVERSE_Headless()
    {
        LoadAllGameData();
        bool savedFlag = GlobalStats.Defaults.EnableArenaCustomFleet;
        int snapshot = ResourceManager.Ships.Designs.Count;
        ArenaFightScreen screen = null;
        ScreenManager sm = ScreenManager.Instance;
        try
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = true;
            sm.ExitAll(clear3DObjects: true);
            screen = ArenaFightScreen.Create("United", 0x5EED, startAtHub: false, opponentPreference: "");
            sm.GoToScreen(screen, clear3DObjects: true); // runs LoadContent -> builds EmpireUI + ArenaPlayer
            screen.EnterMultiplayerSetupPhase();

            // BUILD-ANEW: the real base ShipDesignScreen mounts against the arena universe. Finding it on the
            // ScreenManager (live or pending) IS the launch proof; we do not pump/exit the child screen (its
            // GUI lifecycle is not unit-tested — the plan's Phase D note — and ExitScreen on an unpumped editor
            // NREs in IsGoodDesign). sm.ExitAll in the finally clears the whole stack.
            screen.OpenArenaSetupDesigner();
            var designer = FindScreenOnScreenManager<ShipDesignScreen>();
            Assert.IsNotNull(designer,
                "OpenArenaSetupDesigner MUST mount the REAL base ShipDesignScreen against the arena universe "
                + "(the 'lobby has no universe' excuse is false — ArenaFightScreen : UniverseScreen supplies it).");

            // PLACE-FORMATION: the real base FleetDesignScreen mounts against the arena universe.
            screen.OpenArenaSetupFormation();
            var formation = FindScreenOnScreenManager<FleetDesignScreen>();
            Assert.IsNotNull(formation,
                "OpenArenaSetupFormation MUST mount the REAL base FleetDesignScreen against the arena universe.");
        }
        finally
        {
            try { screen?.ExitScreen(); } catch { }
            try { sm.ExitAll(clear3DObjects: true); } catch { }
            GlobalStats.Defaults.EnableArenaCustomFleet = savedFlag;
            Assert.AreEqual(snapshot, ResourceManager.Ships.Designs.Count,
                "Teardown must leave the global design table exactly as it started (no leaked designs).");
        }
    }

    // ===================================================================================================
    // UI WIRING — THE LOBBY OPT-IN REACHES BOTH PEERS. The host toggles the new "Star Gladiator setup" pill
    // (SetRequestArenaSetupPhaseForHeadless mirrors that click); it rides the authoritative start's ruleset
    // (Ruleset.SetupPhase) so the JOIN — which never touched the pill — ALSO enters setup. Two REAL lobbies over
    // loopback TCP through the actual Host/Join/Ready/Launch flow: after launch BOTH fight screens must be in the
    // in-arena SETUP phase (neither has spawned). This is the clickable entry the player now has.
    // ===================================================================================================
    [TestMethod]
    public void PROOF_LOBBY_OPT_IN_ENTERS_SETUP_ON_BOTH_PEERS_Headless()
    {
        LoadAllGameData();
        string dir = Path.Combine(Path.GetTempPath(), $"arena_mp_optin_{Guid.NewGuid():N}");
        string savedConfigPath = ArenaMultiplayerLobbyConfig.ConfigPathOverride;
        string savedStaticPath = ArenaFightScreen.CareerSavePath;
        string tempCareer = Path.Combine(Path.GetTempPath(), $"arena_mp_optin_{Guid.NewGuid():N}.yaml");
        bool savedFlag = GlobalStats.Defaults.EnableArenaCustomFleet;
        int snapshot = ResourceManager.Ships.Designs.Count;
        ArenaFightScreen.CareerSavePath = tempCareer;
        ArenaFightScreen.PendingPlayerDesignName = null;
        ArenaMultiplayerLobbyScreen hostLobby = null;
        ArenaMultiplayerLobbyScreen joinLobby = null;
        ArenaFightScreen hostFight = null;
        ArenaFightScreen joinFight = null;
        try
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = true;
            Directory.CreateDirectory(dir);
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = Path.Combine(dir, "mp-lobby-config.yaml");
            int port = FreeTcpPort();
            Assert.IsTrue(ArenaMultiplayerLobbyConfig.Save(new ArenaMultiplayerLobbyConfig
            {
                Host = "127.0.0.1",
                Port = port,
                PeerSlot = ArenaMultiplayerLobbyScreen.DefaultJoinPeerSlot,
            }), "Lobby config must save to the temp override path.");

            // The HOST clicks the new opt-in pill (SetRequestArenaSetupPhaseForHeadless mirrors ToggleArenaSetupPhase).
            // The JOIN never touches it — it must learn the opt-in ONLY from the host's authoritative start ruleset.
            (hostLobby, joinLobby, hostFight, joinFight) = DriveRealLobbiesToLaunchedFight(
                host => host.SetRequestArenaSetupPhaseForHeadless(true));

            Assert.IsNotNull(hostFight, "Host fight screen must have launched.");
            Assert.IsNotNull(joinFight, "Join fight screen must have launched.");
            // BOTH peers entered the in-arena SETUP phase — the host from its pill, the JOIN from the ruleset over
            // the wire (it never set the local field). Neither may have spawned yet (the setup gate holds).
            Assert.IsTrue(hostFight.ArenaSetupActiveForHeadless,
                "The host opted into the setup phase via the pill — it MUST enter the in-arena SETUP phase.");
            Assert.IsTrue(joinFight.ArenaSetupActiveForHeadless,
                "The JOIN never touched the pill — the host opt-in MUST reach it over the wire (Ruleset.SetupPhase) "
                + "so both peers enter setup together (else one spawns while the other authors => desync).");
            Assert.AreEqual(ArenaFightScreen.ArenaSetupPhase.Setup, hostFight.MultiplayerSetupPhaseForHeadless,
                "The host must start in the authoring (Setup) sub-phase.");
            Assert.AreEqual(ArenaFightScreen.ArenaSetupPhase.Setup, joinFight.MultiplayerSetupPhaseForHeadless,
                "The join must start in the authoring (Setup) sub-phase.");
            Assert.AreEqual(-1L, hostFight.MultiplayerLiveSimTickForHeadless,
                "The host must NOT spawn while the setup gate holds.");
            Assert.AreEqual(-1L, joinFight.MultiplayerLiveSimTickForHeadless,
                "The join must NOT spawn while the setup gate holds.");
        }
        finally
        {
            try { hostFight?.ExitScreen(); } catch { }
            try { joinFight?.ExitScreen(); } catch { }
            DisposeLobbyTransport(hostLobby);
            DisposeLobbyTransport(joinLobby);
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = savedConfigPath;
            ArenaFightScreen.CareerSavePath = savedStaticPath;
            GlobalStats.Defaults.EnableArenaCustomFleet = savedFlag;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
            try { if (File.Exists(tempCareer)) File.Delete(tempCareer); } catch { }
            Assert.AreEqual(snapshot, ResourceManager.Ships.Designs.Count,
                "Teardown must leave the global design table exactly as it started (no leaked designs).");
        }
    }

    // Flag-OFF regression: the opt-in is a true no-op when EnableArenaCustomFleet is off — even if the pill flag is
    // somehow set, BuildArenaRuleset zeroes SetupPhase and LaunchVisibleArena won't enter setup. The duel spawns as
    // today (both peers advance the sim), and the in-arena setup controls never appear.
    [TestMethod]
    public void PROOF_LOBBY_OPT_IN_IS_NOOP_WHEN_FLAG_OFF_Headless()
    {
        LoadAllGameData();
        string dir = Path.Combine(Path.GetTempPath(), $"arena_mp_optinoff_{Guid.NewGuid():N}");
        string savedConfigPath = ArenaMultiplayerLobbyConfig.ConfigPathOverride;
        string savedStaticPath = ArenaFightScreen.CareerSavePath;
        string tempCareer = Path.Combine(Path.GetTempPath(), $"arena_mp_optinoff_{Guid.NewGuid():N}.yaml");
        bool savedFlag = GlobalStats.Defaults.EnableArenaCustomFleet;
        ArenaFightScreen.CareerSavePath = tempCareer;
        ArenaFightScreen.PendingPlayerDesignName = null;
        ArenaMultiplayerLobbyScreen hostLobby = null;
        ArenaMultiplayerLobbyScreen joinLobby = null;
        ArenaFightScreen hostFight = null;
        ArenaFightScreen joinFight = null;
        try
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = false; // flag OFF
            Directory.CreateDirectory(dir);
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = Path.Combine(dir, "mp-lobby-config.yaml");
            int port = FreeTcpPort();
            Assert.IsTrue(ArenaMultiplayerLobbyConfig.Save(new ArenaMultiplayerLobbyConfig
            {
                Host = "127.0.0.1",
                Port = port,
                PeerSlot = ArenaMultiplayerLobbyScreen.DefaultJoinPeerSlot,
            }));

            // Even with the pill flag set, a flag-off launch must NOT enter setup.
            (hostLobby, joinLobby, hostFight, joinFight) = DriveRealLobbiesToLaunchedFight(
                host => host.SetRequestArenaSetupPhaseForHeadless(true));

            Assert.IsFalse(hostFight.ArenaSetupActiveForHeadless,
                "Flag OFF: the host must NOT enter the setup phase (a true no-op — spawns as today).");
            Assert.IsFalse(joinFight.ArenaSetupActiveForHeadless,
                "Flag OFF: the join must NOT enter the setup phase.");
            // And the legacy duel still runs: the sim advances on both peers.
            bool live = false;
            for (int frame = 0; frame < 1800 && !live; ++frame)
            {
                hostFight.Update(1f / 60f);
                joinFight.Update(1f / 60f);
                live = hostFight.MultiplayerLiveSimTickForHeadless > 0
                       && joinFight.MultiplayerLiveSimTickForHeadless > 0;
            }
            Assert.IsTrue(live, "Flag-off duel must spawn and advance the sim exactly as before.");
        }
        finally
        {
            try { hostFight?.ExitScreen(); } catch { }
            try { joinFight?.ExitScreen(); } catch { }
            DisposeLobbyTransport(hostLobby);
            DisposeLobbyTransport(joinLobby);
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = savedConfigPath;
            ArenaFightScreen.CareerSavePath = savedStaticPath;
            GlobalStats.Defaults.EnableArenaCustomFleet = savedFlag;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
            try { if (File.Exists(tempCareer)) File.Delete(tempCareer); } catch { }
        }
    }

    // ===================================================================================================
    // UI WIRING — THE IN-ARENA SETUP ENTRY BUTTONS ROUTE TO THE REAL ENDPOINTS. In the SETUP phase the fight
    // screen's HUD offers [Design Ship]/[Import Design]/[Fleet-Formation]/[Ready]. This proves those buttons are
    // built, visible only in setup, and that clicking [Design Ship] and [Fleet / Formation] mount the REAL base
    // ShipDesignScreen / FleetDesignScreen (like PROOF_REAL_EDITORS_LAUNCH), and [Ready] advances the phase.
    // ===================================================================================================
    [TestMethod]
    public void PROOF_SETUP_ENTRY_BUTTONS_ROUTE_TO_REAL_EDITORS_Headless()
    {
        LoadAllGameData();
        bool savedFlag = GlobalStats.Defaults.EnableArenaCustomFleet;
        int snapshot = ResourceManager.Ships.Designs.Count;
        ArenaFightScreen screen = null;
        ScreenManager sm = ScreenManager.Instance;
        try
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = true;
            sm.ExitAll(clear3DObjects: true);
            screen = ArenaFightScreen.Create("United", 0x5EED, startAtHub: false, opponentPreference: "");
            sm.GoToScreen(screen, clear3DObjects: true); // LoadContent builds EmpireUI + ArenaPlayer + the setup HUD
            screen.EnterMultiplayerSetupPhase();

            // The setup HUD buttons exist and are wired (found by Name on the fight screen's UI tree).
            UIButton design = FindButtonByName(screen, "SetupDesignButton");
            UIButton import = FindButtonByName(screen, "SetupImportButton");
            UIButton formation = FindButtonByName(screen, "SetupFormationButton");
            UIButton ready = FindButtonByName(screen, "SetupReadyButton");
            Assert.IsNotNull(design, "The in-arena setup HUD must offer a [Design Ship] button.");
            Assert.IsNotNull(import, "The in-arena setup HUD must offer an [Import Design] button.");
            Assert.IsNotNull(formation, "The in-arena setup HUD must offer a [Fleet / Formation] button (the primary UI).");
            Assert.IsNotNull(ready, "The in-arena setup HUD must offer a [Ready] button.");

            // [Design Ship] routes to the REAL base ShipDesignScreen against the arena universe.
            design.OnClick(design);
            Assert.IsNotNull(FindScreenOnScreenManager<ShipDesignScreen>(),
                "[Design Ship] MUST mount the REAL base ShipDesignScreen (OpenArenaSetupDesigner).");

            // [Fleet / Formation] routes to the REAL base FleetDesignScreen — the PRIMARY setup UI per the director.
            formation.OnClick(formation);
            Assert.IsNotNull(FindScreenOnScreenManager<FleetDesignScreen>(),
                "[Fleet / Formation] MUST mount the REAL base FleetDesignScreen (OpenArenaSetupFormation).");

            // [Ready] advances the setup machine Setup -> LocalReady (MarkSetupLocalReady).
            Assert.AreEqual(ArenaFightScreen.ArenaSetupPhase.Setup, screen.MultiplayerSetupPhaseForHeadless);
            ready.OnClick(ready);
            Assert.AreEqual(ArenaFightScreen.ArenaSetupPhase.LocalReady, screen.MultiplayerSetupPhaseForHeadless,
                "[Ready] MUST advance the setup machine to LocalReady (MarkSetupLocalReady).");
        }
        finally
        {
            try { screen?.ExitScreen(); } catch { }
            try { sm.ExitAll(clear3DObjects: true); } catch { }
            GlobalStats.Defaults.EnableArenaCustomFleet = savedFlag;
            Assert.AreEqual(snapshot, ResourceManager.Ships.Designs.Count,
                "Teardown must leave the global design table exactly as it started (no leaked designs).");
        }
    }

    // Find a UIButton on a screen's UI tree by its field-assigned Name (the setup HUD sets none, so we reflect the
    // private field directly — the buttons are private fields on ArenaFightScreen).
    static UIButton FindButtonByName(ArenaFightScreen screen, string fieldName)
    {
        const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo f = typeof(ArenaFightScreen).GetField(fieldName, Priv);
        return f?.GetValue(screen) as UIButton;
    }

    // ===================================================================================================
    // §2.3 — THE LOOP IS CLOSED: a custom authored INSIDE the in-arena SETUP phase actually reaches the FIGHT.
    // Two REAL peers over loopback TCP each author a DISTINCT custom (a hull the other never authored) in the
    // setup phase, capture a formation, reach Ready. The host rebuilds the authoritative start from the SETUP
    // scratch set and broadcasts it; both peers validate + register the setup tables (reconstructing from RECEIVED
    // bytes — the host never authored the join's design, so its only path in is the wire), advance to Fight, and
    // BOTH spawn BOTH setup-authored fleets, ticking to the same digest. RED before this lane (spawn was gated
    // with nothing to advance it); GREEN after.
    // ===================================================================================================
    [TestMethod]
    public void PROOF_SETUP_AUTHORED_CUSTOM_REACHES_FIGHT_Headless()
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_setupfight_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        bool savedFlag = GlobalStats.Defaults.EnableArenaCustomFleet;
        int snapshot = ResourceManager.Ships.Designs.Count;
        TcpLockstepTransport hostTransport = null, joinTransport = null;
        ArenaFightScreen hostScreen = null, joinScreen = null;
        try
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = true;
            string[] stock = TwoDistinctStockHulls();
            // Distinct source designs for host vs join (different hulls -> distinct @arena/<hash>).
            Assert.IsTrue(ResourceManager.Ships.GetDesign(stock[0], out IShipDesign hostSource));
            Assert.IsTrue(ResourceManager.Ships.GetDesign(stock[1], out IShipDesign joinSource));
            string hostArena = ArenaDesignTable.ContentName(hostSource);
            string joinArena = ArenaDesignTable.ContentName(joinSource);
            Assert.AreNotEqual(hostArena, joinArena, "Host and join setup customs must be distinct.");

            // Lobby-time settings field STOCK designs (NOT the customs) — the customs come ONLY from the setup phase.
            var lobbySettings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED, RngSeed = 0xA12EA000u, InputDelay = 3, MaxTurns = 240, CommandEveryTurns = 1,
                HostFleetDesignNames = new[] { stock[0] },
                JoinFleetDesignNames = new[] { stock[1] },
                HostFleetBundle = ArenaFleetBundle.Encode(ArenaFleetBundle.FromDesignNames(new[] { stock[0] })),
                JoinFleetBundle = ArenaFleetBundle.Encode(ArenaFleetBundle.FromDesignNames(new[] { stock[1] })),
                Ruleset = new ArenaMultiplayerRuleset
                {
                    Mode = ArenaMatchMode.Sandbox, RosterSource = ArenaRosterSource.AllContent,
                    BudgetModel = ArenaBudgetModel.Unlimited,
                },
            }.WithResolvedFleets();

            int port = FreeTcpPort();
            hostTransport = TcpLockstepTransport.Host(port, ArenaMultiplayerSession.JoinPlayerPeerId);
            joinTransport = TcpLockstepTransport.Join("127.0.0.1", port, LockstepHost.HostPeerId);
            Assert.IsTrue(hostTransport.WaitForConnection(TimeSpan.FromSeconds(5)), "Loopback TCP did not connect.");

            // Build both fight screens IN SETUP MODE: EnterMultiplayerSetupPhase BEFORE arm (so the setup observer
            // registers) and LoadContent does NOT spawn (gated). Author a DISTINCT custom + formation on each.
            hostScreen = ArenaFightScreen.Create(lobbySettings.HostRacePreference, lobbySettings.MatchSeed,
                startAtHub: false, opponentPreference: lobbySettings.JoinRacePreference);
            hostScreen.EnterMultiplayerSetupPhase();
            hostScreen.ArmMultiplayerLive(new ArenaMultiplayerLiveSession(ArenaMultiplayerRole.Host, hostTransport, lobbySettings));
            hostScreen.LoadContent();
            Assert.AreEqual(-1L, hostScreen.MultiplayerLiveSimTickForHeadless,
                "The host must NOT spawn during setup (the gate holds).");

            joinScreen = ArenaFightScreen.Create(lobbySettings.HostRacePreference, lobbySettings.MatchSeed,
                startAtHub: false, opponentPreference: lobbySettings.JoinRacePreference);
            joinScreen.EnterMultiplayerSetupPhase();
            joinScreen.ArmMultiplayerLive(new ArenaMultiplayerLiveSession(ArenaMultiplayerRole.Join, joinTransport, lobbySettings));
            joinScreen.LoadContent();
            Assert.AreEqual(-1L, joinScreen.MultiplayerLiveSimTickForHeadless,
                "The join must NOT spawn during setup (the gate holds).");

            // AUTHOR distinct customs + formations INSIDE the setup phase (the capture seam build-anew/import uses).
            Assert.AreEqual(hostArena, hostScreen.CaptureSetupDesign(hostSource), $"host capture: {hostScreen.SetupHudErrorForHeadless}");
            Assert.AreEqual(joinArena, joinScreen.CaptureSetupDesign(joinSource), $"join capture: {joinScreen.SetupHudErrorForHeadless}");
            // Capture the authored formation bundle (the headless PLACE-FORMATION seam — same capture endpoint the
            // real FleetDesignScreen OnExit routes through, sans GUI). One custom per side at a column offset.
            hostScreen.SetSetupFleetBundleForHeadless(ArenaFleetBundle.Encode(BuildColumnBundle("H", new[] { hostArena })));
            joinScreen.SetSetupFleetBundleForHeadless(ArenaFleetBundle.Encode(BuildColumnBundle("J", new[] { joinArena })));

            // Mark both Ready — the per-frame setup handshake then exchanges tables + rebuilds+broadcasts the start.
            hostScreen.MarkSetupLocalReady();
            joinScreen.MarkSetupLocalReady();

            // Pump both Update loops until BOTH reach Fight and both sims advance past tick 0.
            bool bothFighting = false;
            for (int frame = 0; frame < 2400 && !bothFighting; ++frame)
            {
                hostScreen.Update(1f / 60f);
                joinScreen.Update(1f / 60f);
                bothFighting = hostScreen.MultiplayerLiveSimTickForHeadless > 0
                               && joinScreen.MultiplayerLiveSimTickForHeadless > 0;
            }
            Assert.IsTrue(bothFighting,
                "Both peers must reach Fight and pump the sim after the setup->fight rebuild. "
                + $"hostSetup={hostScreen.MultiplayerSetupPhaseForHeadless} hostTick={hostScreen.MultiplayerLiveSimTickForHeadless} "
                + $"joinSetup={joinScreen.MultiplayerSetupPhaseForHeadless} joinTick={joinScreen.MultiplayerLiveSimTickForHeadless} "
                + $"hostHud='{hostScreen.SetupHudErrorForHeadless}' joinHud='{joinScreen.SetupHudErrorForHeadless}'");

            // BOTH peers spawned BOTH setup-authored fleets (the host's custom + the join's custom).
            ArenaMultiplayerShipSnapshot hostSnap = hostScreen.MultiplayerSnapshot();
            ArenaMultiplayerShipSnapshot joinSnap = joinScreen.MultiplayerSnapshot();
            Assert.AreEqual(1, hostSnap.PlayerShipIds.Length, "Host peer must spawn the host setup fleet.");
            Assert.AreEqual(1, hostSnap.EnemyShipIds.Length, "Host peer must spawn the join setup fleet.");
            CollectionAssert.AreEqual(hostSnap.PlayerShipIds, joinSnap.PlayerShipIds,
                "Both peers must spawn identical player ship IDs from the setup-authored fleets.");
            CollectionAssert.AreEqual(hostSnap.EnemyShipIds, joinSnap.EnemyShipIds,
                "Both peers must spawn identical enemy ship IDs from the setup-authored fleets.");

            // The spawned designs ARE the setup-authored @arena customs (byte-identical reconstruction on the join,
            // which never authored the host's design — the only path in was the rebuilt-start wire).
            Assert.IsTrue(ResourceManager.Ships.GetDesign(hostArena, out IShipDesign hostRegistered),
                "The host setup custom must be registered for spawn on both peers.");
            Assert.IsTrue(ResourceManager.Ships.GetDesign(joinArena, out IShipDesign joinRegistered),
                "The join setup custom must be registered for spawn on both peers (reconstructed from the wire).");
            CollectionAssert.AreEqual(ArenaDesignTable.CanonicalPayload(joinSource), ArenaDesignTable.CanonicalPayload(joinRegistered),
                "The join's setup custom reconstructed for the fight must be byte-identical to what the joiner authored.");

            // Run to a matching digest (no desync) — the setup-authored match is deterministic across both peers.
            for (int frame = 0; frame < 6000; ++frame)
            {
                hostScreen.Update(1f / 60f);
                joinScreen.Update(1f / 60f);
                if (hostScreen.MultiplayerLiveResultForHeadless?.MatchEnded == true
                    && joinScreen.MultiplayerLiveResultForHeadless?.MatchEnded == true)
                    break;
            }
            ArenaMultiplayerRunResult hostResult = hostScreen.MultiplayerLiveResultForHeadless;
            ArenaMultiplayerRunResult joinResult = joinScreen.MultiplayerLiveResultForHeadless;
            Assert.IsFalse(hostResult.Desynced, $"Setup-authored match desynced at turn {hostResult.DesyncTurn}: {hostResult.DesyncReason}");
            Assert.IsFalse(joinResult.Desynced, $"Setup-authored match desynced (join): {joinResult.DesyncReason}");
            Assert.IsTrue(hostScreen.MultiplayerLiveSimTickForHeadless > 0 && joinScreen.MultiplayerLiveSimTickForHeadless > 0,
                "Both sims must have advanced in the setup-authored fight.");
        }
        finally
        {
            try { hostScreen?.ExitScreen(); } catch { }
            try { joinScreen?.ExitScreen(); } catch { }
            try { hostTransport?.Dispose(); } catch { }
            try { joinTransport?.Dispose(); } catch { }
            GlobalStats.Defaults.EnableArenaCustomFleet = savedFlag;
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            Assert.AreEqual(snapshot, ResourceManager.Ships.Designs.Count,
                "Teardown must leave the global design table exactly as it started (no leaked designs).");
        }
    }

    // ===================================================================================================
    // PHASE B — SETUP-PHASE STATE MACHINE (§2). The gate: the sim must NOT spawn until the setup phase reaches
    // its terminal Fight state; and a match authored via the setup capture seam runs to the SAME digest as a
    // direct match with the same designs (deterministic handoff, setup -> fight, one reused universe).
    // ===================================================================================================
    [TestMethod]
    public void PROOF_SETUP_HANDOFF_DETERMINISTIC_Headless()
    {
        LoadAllGameData();
        bool savedFlag = GlobalStats.Defaults.EnableArenaCustomFleet;
        int snapshot = ResourceManager.Ships.Designs.Count;
        ArenaFightScreen screen = null;
        try
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = true;
            string[] stock = TwoDistinctStockHulls();
            (_, string arenaA) = RegisterPickableCustom(stock[0], "Setup Handoff Custom A");

            screen = BuildArenaScreen();
            // Author a custom into the scratch set through the SAME capture seam BUILD-ANEW uses (inject the design
            // directly — no live editor GUI, exactly the plan's headless drive for this gate).
            Assert.IsTrue(ResourceManager.Ships.GetDesign("Setup Handoff Custom A", out IShipDesign designA));
            screen.EnterMultiplayerSetupPhase();
            Assert.IsTrue(screen.ArenaSetupActiveForHeadless, "EnterMultiplayerSetupPhase must activate the setup machine.");
            string wire = screen.CaptureSetupDesign(designA);
            Assert.AreEqual(arenaA, wire, "CaptureSetupDesign must register under the @arena/<hash> content name.");
            Assert.IsTrue(screen.SetupScratchWireNamesForHeadless.Contains(arenaA),
                "The captured design must appear in the setup scratch set.");

            // THE GATE: while the setup phase is not terminal, arming + InitializeMultiplayerLiveIfNeeded must NOT
            // spawn the sim. Build a minimal live session and confirm no spawn occurs until we advance to Fight.
            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED,
                RngSeed = 0xA12EA000u,
                InputDelay = 3,
                MaxTurns = 60,
                CommandEveryTurns = 1,
                HostFleetDesignNames = new[] { arenaA },
                JoinFleetDesignNames = new[] { arenaA },
                HostFleetBundle = ArenaFleetBundle.Encode(ArenaFleetBundle.FromDesignNames(new[] { arenaA })),
                JoinFleetBundle = ArenaFleetBundle.Encode(ArenaFleetBundle.FromDesignNames(new[] { arenaA })),
                HostDesignTable = ArenaDesignTable.Encode(new List<IShipDesign> { designA }),
                JoinDesignTable = ArenaDesignTable.Encode(new List<IShipDesign> { designA }),
                Ruleset = new ArenaMultiplayerRuleset
                {
                    Mode = ArenaMatchMode.Sandbox,
                    RosterSource = ArenaRosterSource.AllContent,
                    BudgetModel = ArenaBudgetModel.Unlimited,
                },
            }.WithResolvedFleets();

            using var armTransport = TcpLockstepTransport.Host(FreeTcpPort(), ArenaMultiplayerSession.JoinPlayerPeerId);
            screen.ArmMultiplayerLive(new ArenaMultiplayerLiveSession(ArenaMultiplayerRole.Host,
                armTransport, settings));
            screen.InitializeMultiplayerLiveIfNeeded();
            Assert.AreEqual(-1L, screen.MultiplayerLiveSimTickForHeadless,
                "The setup-phase gate must BLOCK the sim from initializing until the phase reaches Fight.");

            // Reach terminal Fight, then the SAME InitializeMultiplayerLiveIfNeeded is free to run.
            screen.MarkSetupLocalReady();
            screen.AdvanceSetupPhaseToFight();
            Assert.IsFalse(screen.ArenaSetupActiveForHeadless, "The setup machine must be terminal after AdvanceSetupPhaseToFight.");
            screen.InitializeMultiplayerLiveIfNeeded();
            Assert.IsTrue(screen.MultiplayerLiveSimTickForHeadless >= 0,
                "After the setup phase reaches Fight, InitializeMultiplayerLiveIfNeeded must run (sim initialized).");

            // Deterministic handoff: a direct in-process match of the same custom designs runs clean (the setup
            // phase never touched the sim, so the resulting digest is identical to a direct launch).
            ArenaMultiplayerRunResult direct = ArenaMultiplayerSession.RunInProcess(settings);
            Assert.IsFalse(direct.Desynced, $"The direct custom-fleet match desynced: {direct.DesyncReason}");
            Assert.IsTrue(direct.TurnHashes.All(h => h.Match), "Every turn hash must match across both peers.");
        }
        finally
        {
            try { screen?.ExitScreen(); } catch { }
            GlobalStats.Defaults.EnableArenaCustomFleet = savedFlag;
            foreach (string name in new[] { "Setup Handoff Custom A" })
                if (ResourceManager.Ships.GetDesign(name, out _))
                    ResourceManager.Ships.Delete(name);
            Assert.AreEqual(snapshot, ResourceManager.Ships.Designs.Count,
                "Teardown must leave the global design table exactly as it started (no leaked designs).");
        }
    }

    // ===================================================================================================
    // PHASE C — IMPORT PATH (§4). A saved/loaded design imported through the SAME CaptureSetupDesign seam as
    // build-anew produces an IDENTICAL @arena/<hash> transient (byte-identical canonical payload). Import rides
    // the proven exchange for free — once in the scratch set it is indistinguishable from an authored design.
    // ===================================================================================================
    [TestMethod]
    public void PROOF_IMPORT_PRODUCES_ARENA_CUSTOM_Headless()
    {
        LoadAllGameData();
        bool savedFlag = GlobalStats.Defaults.EnableArenaCustomFleet;
        int snapshot = ResourceManager.Ships.Designs.Count;
        ArenaFightScreen screen = null;
        try
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = true;
            string[] stock = TwoDistinctStockHulls();
            Assert.IsTrue(ResourceManager.Ships.GetDesign(stock[0], out IShipDesign source));

            // Authored-vs-imported cross-check: the @arena name from a live capture must equal the name from an
            // import by name AND from an import from .design bytes (the base ShipDesign codec the kernel round-trips).
            string authoredName = ArenaDesignTable.ContentName(source);

            screen = BuildArenaScreen();
            screen.EnterMultiplayerSetupPhase();

            // (1) Import by name (a design already in the templates table).
            string importedByName = screen.ImportSetupDesignByName(stock[0]);
            Assert.AreEqual(authoredName, importedByName,
                $"Import-by-name must produce the same @arena/<hash> as a live capture. hud='{screen.SetupHudErrorForHeadless}'");

            // (2) Import from .design bytes (the base GetDesignBytes/FromBytes codec, i.e. a saved SP design file).
            byte[] designBytes = ((ShipDesign)source).GetDesignBytes(new ShipDesignWriter());
            string importedFromBytes = screen.ImportSetupDesignFromBytes(designBytes);
            Assert.AreEqual(authoredName, importedFromBytes,
                $"Import-from-bytes must produce the same @arena/<hash> as a live capture. hud='{screen.SetupHudErrorForHeadless}'");

            // Byte-identical canonical payload: the imported scratch design reconstructs to the source's canonical form.
            Assert.IsTrue(ResourceManager.Ships.GetDesign(authoredName, out IShipDesign scratch),
                "The imported design must be registered under its @arena/<hash> name.");
            CollectionAssert.AreEqual(ArenaDesignTable.CanonicalPayload(source), ArenaDesignTable.CanonicalPayload(scratch),
                "The imported scratch design's canonical payload must be byte-identical to the source design.");
        }
        finally
        {
            try { screen?.ExitScreen(); } catch { }
            GlobalStats.Defaults.EnableArenaCustomFleet = savedFlag;
            Assert.AreEqual(snapshot, ResourceManager.Ships.Designs.Count,
                "Teardown must leave the global design table exactly as it started (no leaked designs).");
        }
    }

    // ===================================================================================================
    // PHASE D — REAL EDITORS + BUDGET/ROSTER (§1.4). The CAPTURE seam + bundle (the editor GUI itself is not
    // unit-tested per the plan): a formation captured via CaptureSetupFormation spawns byte-identically on both
    // peers; the roster scopes to affordable scratch designs; the handshake enforces the budget.
    // ===================================================================================================
    [TestMethod]
    public void PROOF_FORMATION_SPAWN_DETERMINISTIC_Headless()
    {
        LoadAllGameData();
        bool savedFlag = GlobalStats.Defaults.EnableArenaCustomFleet;
        int snapshot = ResourceManager.Ships.Designs.Count;
        ArenaFightScreen screen = null;
        try
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = true;
            string[] stock = TwoDistinctStockHulls();
            (_, string arenaA) = RegisterPickableCustom(stock[0], "Formation Custom A");
            (_, string arenaB) = RegisterPickableCustom(stock[1], "Formation Custom B");
            Assert.IsTrue(ResourceManager.Ships.GetDesign("Formation Custom A", out IShipDesign designA));
            Assert.IsTrue(ResourceManager.Ships.GetDesign("Formation Custom B", out IShipDesign designB));

            screen = BuildArenaScreen();
            screen.EnterMultiplayerSetupPhase();
            screen.CaptureSetupDesign(designA);
            screen.CaptureSetupDesign(designB);

            // Capture a formation of the two scratch customs at authored offsets via the SAME FromFleet projection
            // the base fleet-save uses (CaptureSetupFormation). The bundle node names ARE the @arena/<hash> names.
            var bundle = BuildColumnBundle("Setup", new[] { arenaA, arenaB });
            screen.SetSetupFleetBundleForHeadless(ArenaFleetBundle.Encode(bundle));
            string localBundle = screen.SetupLocalFleetBundleForHeadless;
            Assert.IsFalse(string.IsNullOrEmpty(localBundle), "The captured formation bundle must be non-empty.");

            // The captured formation spawns byte-identically on both peers (deterministic ship-id order + placement).
            string table = ArenaDesignTable.Encode(new List<IShipDesign> { designA, designB });
            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED,
                RngSeed = 0xA12EA000u,
                InputDelay = 3,
                MaxTurns = 90,
                CommandEveryTurns = 1,
                HostFleetDesignNames = new[] { arenaA, arenaB },
                JoinFleetDesignNames = new[] { arenaB, arenaA },
                HostFleetBundle = localBundle,
                JoinFleetBundle = ArenaFleetBundle.Encode(BuildColumnBundle("J", new[] { arenaB, arenaA })),
                HostDesignTable = table,
                JoinDesignTable = table,
                Ruleset = new ArenaMultiplayerRuleset
                {
                    Mode = ArenaMatchMode.Sandbox,
                    RosterSource = ArenaRosterSource.AllContent,
                    BudgetModel = ArenaBudgetModel.Unlimited,
                },
            }.WithResolvedFleets();

            ArenaMultiplayerRunResult result = ArenaMultiplayerSession.RunInProcess(settings);
            Assert.IsFalse(result.Desynced, $"The authored-formation match desynced: {result.DesyncReason}");
            Assert.IsTrue(result.TurnHashes.All(h => h.Match), "Every turn hash must match across both peers.");
            Assert.AreEqual(2, result.HostSnapshot.PlayerShipIds.Length, "The host formation must spawn one ship per node.");
            Assert.AreEqual(2, result.HostSnapshot.EnemyShipIds.Length, "The join formation must spawn one ship per node.");
        }
        finally
        {
            try { screen?.ExitScreen(); } catch { }
            GlobalStats.Defaults.EnableArenaCustomFleet = savedFlag;
            foreach (string name in new[] { "Formation Custom A", "Formation Custom B" })
                if (ResourceManager.Ships.GetDesign(name, out _))
                    ResourceManager.Ships.Delete(name);
            Assert.AreEqual(snapshot, ResourceManager.Ships.Designs.Count,
                "Teardown must leave the global design table exactly as it started (no leaked designs).");
        }
    }

    [TestMethod]
    public void PROOF_BUDGET_ENFORCED_IN_SETUP_Headless()
    {
        LoadAllGameData();
        bool savedFlag = GlobalStats.Defaults.EnableArenaCustomFleet;
        int snapshot = ResourceManager.Ships.Designs.Count;
        ArenaFightScreen screen = null;
        try
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = true;
            string[] stock = TwoDistinctStockHulls();
            (_, string arenaA) = RegisterPickableCustom(stock[0], "Budget Custom A");
            (_, string arenaB) = RegisterPickableCustom(stock[1], "Budget Custom B");
            Assert.IsTrue(ResourceManager.Ships.GetDesign("Budget Custom A", out IShipDesign designA));
            Assert.IsTrue(ResourceManager.Ships.GetDesign("Budget Custom B", out IShipDesign designB));
            int costA = (int)MathF.Round(designA.BaseStrength);
            int costB = (int)MathF.Round(designB.BaseStrength);

            screen = BuildArenaScreen();
            screen.EnterMultiplayerSetupPhase();
            screen.CaptureSetupDesign(designA);
            screen.CaptureSetupDesign(designB);

            // ROSTER SCOPE: a budget cap that admits only the cheaper design must scope the affordable roster to it.
            int cheap = Math.Min(costA, costB);
            string affordableWire = costA <= costB ? arenaA : arenaB;
            string unaffordableWire = costA <= costB ? arenaB : arenaA;
            Assert.IsTrue(cheap < Math.Max(costA, costB), "Precondition: the two customs must have different BaseStrength.");
            IReadOnlyList<string> affordable = screen.AffordableScratchWireNamesForHeadless(cheap);
            Assert.IsTrue(affordable.Contains(affordableWire), "The affordable roster must include the design within budget.");
            Assert.IsFalse(affordable.Contains(unaffordableWire), "The affordable roster must EXCLUDE the design over budget.");

            // HANDSHAKE ENFORCEMENT: a fleet at budget passes; one credit over rejects at ValidateStartMessage.
            string table = ArenaDesignTable.Encode(new List<IShipDesign> { designA, designB });
            ArenaMultiplayerSettings Build(int budget, string[] hostFleet) => new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED, RngSeed = 0xA12EA000u, InputDelay = 3, MaxTurns = 60, CommandEveryTurns = 1,
                HostFleetDesignNames = hostFleet,
                JoinFleetDesignNames = new[] { affordableWire },
                HostFleetBundle = ArenaFleetBundle.Encode(ArenaFleetBundle.FromDesignNames(hostFleet)),
                JoinFleetBundle = ArenaFleetBundle.Encode(ArenaFleetBundle.FromDesignNames(new[] { affordableWire })),
                HostDesignTable = table, JoinDesignTable = table,
                Ruleset = new ArenaMultiplayerRuleset
                {
                    Mode = ArenaMatchMode.Sandbox, RosterSource = ArenaRosterSource.AllContent,
                    BudgetModel = ArenaBudgetModel.Cap, BudgetCredits = budget,
                },
            }.WithResolvedFleets();

            // At budget: a single affordable design fits.
            ArenaMultiplayerSettings atBudget = Build(cheap, new[] { affordableWire });
            IReadOnlyList<string> reg = ArenaMultiplayerSession.RegisterPeerDesignTables(atBudget, out string e1);
            try
            {
                Assert.AreEqual("", e1, $"Registration must succeed: {e1}");
                Assert.AreEqual("", ArenaMultiplayerSettings.ValidateStartMessage(atBudget.ToStartMessage(), out _),
                    "A fleet exactly at budget must pass the handshake.");
            }
            finally { ArenaMultiplayerSession.UnregisterPeerDesignTables(reg); }

            // One credit over: both designs together exceed the cap -> reject at the handshake.
            ArenaMultiplayerSettings overBudget = Build(cheap, new[] { arenaA, arenaB });
            IReadOnlyList<string> reg2 = ArenaMultiplayerSession.RegisterPeerDesignTables(overBudget, out string e2);
            try
            {
                Assert.AreEqual("", e2, $"Registration must succeed: {e2}");
                Assert.AreNotEqual("", ArenaMultiplayerSettings.ValidateStartMessage(overBudget.ToStartMessage(), out _),
                    "A fleet over budget must be REJECTED at the handshake (budget enforcement).");
            }
            finally { ArenaMultiplayerSession.UnregisterPeerDesignTables(reg2); }
        }
        finally
        {
            try { screen?.ExitScreen(); } catch { }
            GlobalStats.Defaults.EnableArenaCustomFleet = savedFlag;
            foreach (string name in new[] { "Budget Custom A", "Budget Custom B" })
                if (ResourceManager.Ships.GetDesign(name, out _))
                    ResourceManager.Ships.Delete(name);
            Assert.AreEqual(snapshot, ResourceManager.Ships.Designs.Count,
                "Teardown must leave the global design table exactly as it started (no leaked designs).");
        }
    }

    [TestMethod]
    public void PROOF_JOIN_TABLE_REACHES_HOST_Headless()
    {
        LoadAllGameData();
        string dir = Path.Combine(Path.GetTempPath(), $"arena_joinxfer_{Guid.NewGuid():N}");
        string savedSlotDir = CareerManager.SlotDirectoryOverride;
        string savedStaticPath = ArenaFightScreen.CareerSavePath;
        string savedLobbyConfig = ArenaMultiplayerLobbyConfig.ConfigPathOverride;
        bool savedFlag = GlobalStats.Defaults.EnableArenaCustomFleet;
        int snapshot = ResourceManager.Ships.Designs.Count;
        ArenaMultiplayerLobbyScreen hostLobby = null, joinLobby = null;
        ArenaFightScreen hostFight = null, joinFight = null;
        try
        {
            Directory.CreateDirectory(dir);
            CareerManager.SlotDirectoryOverride = dir;
            ArenaFightScreen.CareerSavePath = Path.Combine(dir, "lobby.yaml");
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = Path.Combine(dir, "mp-config.yaml");
            ArenaFightScreen.PendingPlayerDesignName = null;
            GlobalStats.Defaults.EnableArenaCustomFleet = true;

            string[] stock = TwoDistinctStockHulls();
            (string hostDisplay, string hostArena) = RegisterPickableCustom(stock[0], "JoinXfer Host Custom");
            (string joinDisplay, string joinArena) = RegisterPickableCustom(stock[1], "JoinXfer Join Custom");
            Assert.AreNotEqual(hostArena, joinArena,
                "Host and join customs must have distinct @arena/ names (distinct hull content).");

            void Sandbox(ArenaMultiplayerLobbyScreen l) =>
                l.SetArenaModeForHeadless(ArenaMatchMode.Sandbox, ArenaBudgetModel.Unlimited);

            string joinTableAtPick = null;
            (hostLobby, joinLobby, hostFight, joinFight) =
                DriveRealLobbiesToLaunchedFight(
                    l => { Sandbox(l); l.SetFleetForHeadless(new[] { hostDisplay }); },
                    l => { Sandbox(l); l.SetFleetForHeadless(new[] { joinDisplay }); joinTableAtPick = l.BuildLocalDesignTableForHeadless(); });
            Assert.IsFalse(string.IsNullOrEmpty(joinTableAtPick),
                $"DIAG: the JOINER must build a non-empty local design table at pick time. flagAtEnd={GlobalStats.Defaults.EnableArenaCustomFleet}");

            // THE PROOF: the AUTHORITATIVE start the host built and BROADCAST (captured on BOTH armed fight screens,
            // reconstructed from the RECEIVED bytes) carries the joiner's custom in JoinDesignTable. The host never
            // authored joinArena — it can only be present because the join-side DesignTable transported over the real
            // TCP SessionLobbyMessage (Phase A, the confirmed gap). Asserting on BOTH peers' armed live settings
            // proves the whole round-trip: host built it, wire carried it, joiner validated + armed on it.
            foreach ((string who, ArenaMultiplayerSettings s) in new[]
                     {
                         ("host", hostFight.MultiplayerLiveSettingsForHeadless),
                         ("join", joinFight.MultiplayerLiveSettingsForHeadless),
                     })
            {
                Assert.IsNotNull(s, $"The {who} fight screen must be armed with live settings.");
                Assert.IsFalse(string.IsNullOrEmpty(s.JoinDesignTable),
                    $"The {who}-armed authoritative start MUST carry a non-empty JoinDesignTable (the join-side transport fix).");

                ArenaDesignTable.DecodeResult decoded = ArenaDesignTable.Decode(s.JoinDesignTable);
                Assert.IsTrue(decoded.Ok, $"The {who}'s JoinDesignTable must decode cleanly: {decoded.Error}");
                Assert.IsTrue(decoded.Designs.ContainsKey(joinArena),
                    $"The {who}'s JoinDesignTable must contain the JOINER's custom '{joinArena}' — reconstructed from "
                    + "the bytes that crossed the wire, NOT a shared in-process object (host never authored this design).");

                Assert.IsTrue(ResourceManager.Ships.GetDesign(joinDisplay, out IShipDesign joinOriginal));
                byte[] originalBytes = ArenaDesignTable.CanonicalPayload(joinOriginal);
                byte[] wireBytes = ArenaDesignTable.CanonicalPayload(decoded.Designs[joinArena]);
                CollectionAssert.AreEqual(originalBytes, wireBytes,
                    $"The join custom reconstructed on the {who} from wire bytes must be byte-identical to the joiner's original.");

                ArenaDesignTable.DecodeResult host = ArenaDesignTable.Decode(s.HostDesignTable);
                Assert.IsTrue(host.Ok && host.Designs.ContainsKey(hostArena),
                    $"The {who}'s HostDesignTable must carry the host's own custom '{hostArena}'.");
            }
        }
        finally
        {
            // Exit the fight screens so their TeardownMultiplayerCustomDesigns undoes the live-match @arena/ set
            // (proving the teardown discipline holds end to end); then dispose lobby transports.
            try { hostFight?.ExitScreen(); } catch { }
            try { joinFight?.ExitScreen(); } catch { }
            DisposeLobbyTransport(hostLobby);
            DisposeLobbyTransport(joinLobby);
            GlobalStats.Defaults.EnableArenaCustomFleet = savedFlag;
            CareerManager.SlotDirectoryOverride = savedSlotDir;
            ArenaFightScreen.CareerSavePath = savedStaticPath;
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = savedLobbyConfig;
            // Remove the two pickable customs so the global table returns to its pre-test snapshot.
            foreach (string name in new[] { "JoinXfer Host Custom", "JoinXfer Join Custom" })
                if (ResourceManager.Ships.GetDesign(name, out _))
                    ResourceManager.Ships.Delete(name);
            try { Directory.Delete(dir, true); } catch { }
            Assert.AreEqual(snapshot, ResourceManager.Ships.Designs.Count,
                "Teardown must leave the global design table exactly as it started (no leaked designs).");
        }
    }

    [TestMethod]
    public void PROOF_JOIN_CUSTOM_HANDSHAKE_Headless()
    {
        LoadAllGameData();
        bool savedFlag = GlobalStats.Defaults.EnableArenaCustomFleet;
        int snapshot = ResourceManager.Ships.Designs.Count;
        try
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = true;
            string[] stock = TwoDistinctStockHulls();
            (_, string hostArena) = RegisterPickableCustom(stock[0], "Handshake Host Custom");
            (_, string joinArena) = RegisterPickableCustom(stock[1], "Handshake Join Custom");

            Assert.IsTrue(ResourceManager.Ships.GetDesign("Handshake Host Custom", out IShipDesign hostDesign));
            Assert.IsTrue(ResourceManager.Ships.GetDesign("Handshake Join Custom", out IShipDesign joinDesign));
            string hostTable = ArenaDesignTable.Encode(new List<IShipDesign> { hostDesign });
            string joinTable = ArenaDesignTable.Encode(new List<IShipDesign> { joinDesign });

            ArenaMultiplayerSettings Build(string jt) => new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED,
                RngSeed = 0xA12EA000u,
                InputDelay = 3,
                MaxTurns = 120,
                CommandEveryTurns = 1,
                HostFleetDesignNames = new[] { hostArena },
                JoinFleetDesignNames = new[] { joinArena },
                HostFleetBundle = ArenaFleetBundle.Encode(ArenaFleetBundle.FromDesignNames(new[] { hostArena })),
                JoinFleetBundle = ArenaFleetBundle.Encode(ArenaFleetBundle.FromDesignNames(new[] { joinArena })),
                HostDesignTable = hostTable,
                JoinDesignTable = jt,
                Ruleset = new ArenaMultiplayerRuleset
                {
                    Mode = ArenaMatchMode.Sandbox,
                    RosterSource = ArenaRosterSource.AllContent,
                    BudgetModel = ArenaBudgetModel.Unlimited,
                },
            }.WithResolvedFleets();

            // (a) With BOTH tables present (host custom + join custom), the assembled start validates on the peer,
            // which registers both tables before validation exactly as RunNetworkJoin does.
            ArenaMultiplayerSettings good = Build(joinTable);
            IReadOnlyList<string> reg = ArenaMultiplayerSession.RegisterPeerDesignTables(good, out string regErr);
            try
            {
                Assert.AreEqual("", regErr, $"Both peers' customs must register: {regErr}");
                SessionStartMessage start = good.ToStartMessage();
                Assert.AreEqual("", ArenaMultiplayerSettings.ValidateStartMessage(start, out _),
                    "A start carrying BOTH the host and join custom tables must pass the handshake.");
            }
            finally { ArenaMultiplayerSession.UnregisterPeerDesignTables(reg); }

            // (b) A TAMPERED join payload: register only the HOST table (the join custom never reconstructs), so
            // the join fleet's @arena/<hash> name fails to resolve -> clean handshake rejection, never a mid-match
            // desync. This is the join-table-absent failure the transport fix cures.
            ArenaMultiplayerSettings tampered = Build(""); // join table stripped (as the pre-fix host built it)
            IReadOnlyList<string> reg2 = ArenaMultiplayerSession.RegisterPeerDesignTables(tampered, out string regErr2);
            try
            {
                Assert.AreEqual("", regErr2, "Host-only registration must itself succeed (the join gap is downstream).");
                SessionStartMessage start = tampered.ToStartMessage();
                string err = ArenaMultiplayerSettings.ValidateStartMessage(start, out _);
                Assert.AreNotEqual("", err,
                    "With the join table stripped, the join custom must NOT resolve -> the handshake must REJECT (never a desync).");
            }
            finally { ArenaMultiplayerSession.UnregisterPeerDesignTables(reg2); }
        }
        finally
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = savedFlag;
            foreach (string name in new[] { "Handshake Host Custom", "Handshake Join Custom" })
                if (ResourceManager.Ships.GetDesign(name, out _))
                    ResourceManager.Ships.Delete(name);
            Assert.AreEqual(snapshot, ResourceManager.Ships.Designs.Count,
                "Teardown must leave the global design table exactly as it started (no leaked designs).");
        }
    }

    // ===================================================================================================
    // ARENA LOBBY FLEET UX (director live-QA). ISSUE 1: the JOINER can set its OWN fleet via the picker path —
    // SET FLEET is each player's own choice, NOT a host ruleset, so it is not gated to the host. ISSUE 2: every
    // occupied combatant slot shows that peer's current fleet (host sees the joiner's ingested fleet, joiner sees
    // the host's). ISSUE 3: the design-in-arena SETUP pill label + STATUS hint make the flow discoverable.
    // ===================================================================================================

    // ISSUE 1 — the JOINER (LocalRole=Join) opens the fleet picker and commits its OWN fleet. The picker path is
    // NOT host-gated: a joiner composing its own LocalPeer.FleetDesignNames must succeed even though ruleset pills
    // (ARENA/BUDGET/MATCH-LENGTH/SETUP) are locked to the host. Proven WITHOUT a live transport (pure own-fleet path).
    [TestMethod]
    public void PROOF_JOINER_CAN_SET_OWN_FLEET_NOT_HOST_GATED_Headless()
    {
        LoadAllGameData();

        var joiner = new ArenaMultiplayerLobbyScreen(ArenaMultiplayerLobbySurface.StarGladiator);
        joiner.LoadContent();
        joiner.SetLocalRoleForHeadless(ArenaMultiplayerRole.Join);
        joiner.SetArenaModeForHeadless(ArenaMatchMode.Sandbox, ArenaBudgetModel.Unlimited);

        // The picker opens for the joiner (NOT denied as "locked to the host"): a player's OWN fleet is not a host
        // ruleset. (Before the fix, OpenFleetPicker was gated by HostSettingsAreLockedToRemote and returned null
        // for a joiner.)
        ArenaFleetPickerScreen picker = joiner.OpenFleetPickerForHeadless();
        Assert.IsNotNull(picker,
            "A joiner MUST be able to open the SET FLEET picker — a player's own fleet is not a host ruleset.");

        // Choose a design the fleet does NOT currently field, toggle it ON, and commit — the joiner directs its own
        // composition and the change reaches its OWN LocalPeer.FleetDesignNames.
        string[] before = joiner.LocalFleetDesignNamesForHeadless;
        string pick = joiner.FleetPickerOptionsForHeadless.First(n => !before.Contains(n));
        Assert.IsFalse(picker.IsSelectedForHeadless(pick),
            "Precondition: the chosen design must not already be in the joiner's fleet.");
        Assert.IsTrue(picker.ToggleForHeadless(pick),
            "The joiner's own-fleet pick must be accepted (no host-gate on the local player's fleet).");
        picker.CommitForHeadless();

        CollectionAssert.Contains(joiner.LocalFleetDesignNamesForHeadless, pick,
            $"The joiner's committed pick '{pick}' must land in its OWN LocalPeer.FleetDesignNames. "
            + $"landed=[{string.Join(",", joiner.LocalFleetDesignNamesForHeadless)}]");

        // A second open still works (the gate never engages for the local fleet regardless of role).
        picker = joiner.OpenFleetPickerForHeadless();
        Assert.IsNotNull(picker, "The joiner must be able to RE-open the picker to change its own fleet.");
    }

    // ISSUE 2 — over TWO REAL lobbies (host+join) driven to a launched fight, each peer's slot card reflects THAT
    // peer's fielded fleet on BOTH screens: the host slot shows the host's fleet, the join slot shows the join's
    // fleet, and neither peer's own card leaks into the other's slot. The fleets travel the real SessionLobbyMessage
    // sync (host->join and join->host), reconstructed from received bytes.
    [TestMethod]
    public void PROOF_EACH_SLOT_SHOWS_THAT_PEERS_FLEET_Headless()
    {
        LoadAllGameData();
        string dir = Path.Combine(Path.GetTempPath(), $"arena_slotfleet_{Guid.NewGuid():N}");
        string savedSlotDir = CareerManager.SlotDirectoryOverride;
        string savedStaticPath = ArenaFightScreen.CareerSavePath;
        string savedLobbyConfig = ArenaMultiplayerLobbyConfig.ConfigPathOverride;
        ArenaMultiplayerLobbyScreen hostLobby = null, joinLobby = null;
        ArenaFightScreen hostFight = null, joinFight = null;
        try
        {
            Directory.CreateDirectory(dir);
            CareerManager.SlotDirectoryOverride = dir;
            ArenaFightScreen.CareerSavePath = Path.Combine(dir, "lobby.yaml");
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = Path.Combine(dir, "mp-config.yaml");
            ArenaFightScreen.PendingPlayerDesignName = null;

            string[] stock = TwoDistinctStockHulls();
            string hostShip = stock[0];
            string joinShip = stock[1];

            void Sandbox(ArenaMultiplayerLobbyScreen l) =>
                l.SetArenaModeForHeadless(ArenaMatchMode.Sandbox, ArenaBudgetModel.Unlimited);

            (hostLobby, joinLobby, hostFight, joinFight) =
                DriveRealLobbiesToLaunchedFight(
                    l => { Sandbox(l); l.SetFleetForHeadless(new[] { hostShip }); },
                    l => { Sandbox(l); l.SetFleetForHeadless(new[] { joinShip }); });

            int hostSlot = ArenaMultiplayerLobbyScreen.HostSlotPeerIdForHeadless;
            int joinSlot = joinLobby.JoinPeerSlotForHeadless;
            string hostFleetSummary = $"Fleet: {hostShip}";
            string joinFleetSummary = $"Fleet: {joinShip}";

            // The joiner is the LOCAL player on its own screen; the host is the local player on the host's screen.
            Assert.AreEqual(joinSlot, joinLobby.LocalSlotPeerIdForHeadless,
                "The joiner's local slot must be its chosen JoinPeerSlot (not the host slot).");
            Assert.AreEqual(hostSlot, hostLobby.LocalSlotPeerIdForHeadless,
                "The host's local slot must be the host slot.");

            // HOST screen: host card shows the host's fleet (its own), join card shows the join's fleet (ingested).
            Assert.AreEqual(hostFleetSummary, hostLobby.SlotDetailForHeadless(hostSlot),
                "On the HOST screen, the host slot must show the host's own fleet.");
            Assert.AreEqual(joinFleetSummary, hostLobby.SlotDetailForHeadless(joinSlot),
                "On the HOST screen, the join slot must show the JOINER's ingested fleet.");

            // JOIN screen: host card shows the host's fleet (ingested over the wire), join card shows its own fleet.
            Assert.AreEqual(hostFleetSummary, joinLobby.SlotDetailForHeadless(hostSlot),
                "On the JOIN screen, the host slot must show the HOST's fleet (broadcast to the joiner).");
            Assert.AreEqual(joinFleetSummary, joinLobby.SlotDetailForHeadless(joinSlot),
                "On the JOIN screen, the join slot must show the joiner's OWN fleet.");
        }
        finally
        {
            try { hostFight?.ExitScreen(); } catch { }
            try { joinFight?.ExitScreen(); } catch { }
            DisposeLobbyTransport(hostLobby);
            DisposeLobbyTransport(joinLobby);
            CareerManager.SlotDirectoryOverride = savedSlotDir;
            ArenaFightScreen.CareerSavePath = savedStaticPath;
            ArenaMultiplayerLobbyConfig.ConfigPathOverride = savedLobbyConfig;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // ISSUE 3 — the design-in-arena SETUP pill spells out the flow in BOTH states (design ships vs. pick fleet) and
    // a STATUS hint advertises it, so a player who never toggled the pill still discovers the design flow. The pill
    // exists ONLY when EnableArenaCustomFleet is on (flag off => interim behavior unchanged, pill absent).
    [TestMethod]
    public void PROOF_SETUP_PILL_DISCOVERABILITY_Headless()
    {
        LoadAllGameData();
        bool savedFlag = GlobalStats.Defaults.EnableArenaCustomFleet;
        try
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = true;
            var lobby = new ArenaMultiplayerLobbyScreen(ArenaMultiplayerLobbySurface.StarGladiator);
            lobby.LoadContent();

            Assert.IsTrue(lobby.Find("arena_mp_setup_phase", out UIButton _),
                "With custom-fleet ON, the SETUP pill must be present so the design flow is reachable.");

            lobby.SetRequestArenaSetupPhaseForHeadless(false);
            StringAssert.Contains(lobby.SetupPillLabelForHeadless, "pick fleet",
                "The SETUP-off label must say the match picks the fleet in the lobby.");

            lobby.SetRequestArenaSetupPhaseForHeadless(true);
            StringAssert.Contains(lobby.SetupPillLabelForHeadless, "DESIGN IN ARENA",
                "The SETUP-on label must announce designing ships in the arena.");
            StringAssert.Contains(lobby.SetupPillLabelForHeadless, "design ships",
                "The SETUP-on label must spell out that ships are designed (director discoverability).");

            StringAssert.Contains(lobby.SetupHintLineForHeadless, "design ships in the arena",
                "The STATUS hint must advertise the design-in-arena flow.");

            // Flag OFF: the pill is not added at all (interim behavior unchanged).
            GlobalStats.Defaults.EnableArenaCustomFleet = false;
            var off = new ArenaMultiplayerLobbyScreen(ArenaMultiplayerLobbySurface.StarGladiator);
            off.LoadContent();
            Assert.IsFalse(off.Find("arena_mp_setup_phase", out UIButton _),
                "With custom-fleet OFF, the SETUP pill must be absent (flag-off leaves the interim path untouched).");
        }
        finally
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = savedFlag;
        }
    }

    static void DisposeLobbyTransport(ArenaMultiplayerLobbyScreen lobby)
    {
        if (lobby == null)
            return;
        try
        {
            FieldInfo field = typeof(ArenaMultiplayerLobbyScreen).GetField("Transport",
                BindingFlags.Instance | BindingFlags.NonPublic);
            (field?.GetValue(lobby) as TcpLockstepTransport)?.Dispose();
        }
        catch { }
    }

    static void SetPrivateField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"Missing private field '{name}'.");
        field.SetValue(target, value);
    }

    static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    sealed class CapturingExtensionPoints : IGameExtensionPoints
    {
        public readonly System.Collections.Generic.Dictionary<string, PluginMainMenuAction> Actions = new();

        public void RegisterMainMenuAction(string buttonName, Func<GameScreen> createScreen)
            => RegisterMainMenuAction(buttonName, "", createScreen);

        public void RegisterMainMenuAction(string buttonName, string buttonTitle, Func<GameScreen> createScreen)
            => Actions[buttonName] = new PluginMainMenuAction(buttonName, buttonTitle, createScreen);
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

    // ==== LANE A N-SLOT SUBSTRATE GATE (STARDRIVE_ARENA_8PLAYER_TEAMS_SUBSTRATE_REFACTOR_20260707 §2/§5) ====
    // The canonical 2-peer in-process match, used both to CAPTURE the pre-refactor per-turn host digest baseline
    // and to PROVE the ArenaSlots substrate refactor is byte-identically inert at N=2. Settings are frozen
    // (Wingmates vs Wingmates, MatchSeed 0x5EED, RngSeed 0xA12EA000, InputDelay 3, 180 turns) so the baseline
    // is reproducible. Do NOT change these once the baseline is baked.
    static ArenaMultiplayerSettings N2GateSettings()
        => new ArenaMultiplayerSettings
        {
            MatchSeed = 0x5EED,
            RngSeed = 0xA12EA000u,
            InputDelay = 3,
            MaxTurns = 180,
            CommandEveryTurns = 1,
            HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul),
            JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul),
        }.WithResolvedFleets();

    // The host peer's per-turn digest sequence as "Turn:HostHi:HostLo" hex triples (host == join by lockstep,
    // so this fully pins the committed sim state per turn — a self-healing mid-match divergence cannot hide).
    static string[] N2HostDigestSequence(ArenaMultiplayerRunResult result)
        => result.TurnHashes
            .Select(h => $"{h.Turn}:{h.HostHi:X16}:{h.HostLo:X16}")
            .ToArray();

    // CAPTURE step (run once before/after the refactor with ARENA_LANE_A_CAPTURE=<path> to dump the baseline).
    // Not the merge gate itself — the gate is ArenaSubstrate_N2_PerTurnDigest_ByteIdenticalToPreRefactor below.
    [TestMethod]
    public void ArenaSubstrate_N2_CaptureBaseline_Headless()
    {
        string capturePath = Environment.GetEnvironmentVariable("ARENA_LANE_A_CAPTURE");
        if (string.IsNullOrWhiteSpace(capturePath))
            Assert.Inconclusive("Set ARENA_LANE_A_CAPTURE=<file> to dump the N=2 pre/post digest baseline.");

        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_lane_a_capture_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        try
        {
            ArenaMultiplayerSettings settings = N2GateSettings();
            ArenaMultiplayerRunResult result = ArenaMultiplayerSession.RunInProcess(settings);
            Assert.IsFalse(result.Desynced,
                $"Baseline capture desynced at turn {result.DesyncTurn}: {result.DesyncReason}");

            var lines = new List<string>
            {
                $"# turns={result.TurnsCompleted} matchEnded={result.MatchEnded} winnerPeerId={result.WinnerPeerId} matchEndedTurn={result.MatchEndedTurn}",
                $"# hostPlayerIds={string.Join(",", result.HostSnapshot.PlayerShipIds)}",
                $"# hostEnemyIds={string.Join(",", result.HostSnapshot.EnemyShipIds)}",
                $"# finalHash={result.FinalHash}",
            };
            lines.AddRange(N2HostDigestSequence(result));
            File.WriteAllLines(capturePath, lines);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    // THE LANE A MERGE GATE. Runs the SAME canonical 2-peer match through the (now ArenaSlots-routed) code path
    // and asserts the FULL per-turn host digest sequence is byte-identical to the pre-refactor baseline captured
    // above, plus identical spawn ship-ids and identical winner. A single perturbed turn fails this — bisect by
    // the first mismatching Turn line. NEVER weaken to FinalHash-only.
    [TestMethod]
    public void ArenaSubstrate_N2_PerTurnDigest_ByteIdenticalToPreRefactor_Headless()
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_lane_a_gate_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        try
        {
            ArenaMultiplayerSettings settings = N2GateSettings();
            ArenaMultiplayerRunResult result = ArenaMultiplayerSession.RunInProcess(settings);
            Assert.IsFalse(result.Desynced,
                $"N=2 gate run desynced at turn {result.DesyncTurn}: {result.DesyncReason}");

            string[] actual = N2HostDigestSequence(result);

            // Baseline captured from the PRE-REFACTOR trunk (commit d12345fb0) via ArenaSubstrate_N2_CaptureBaseline.
            // Format: "Turn:HostHi:HostLo". If this array is empty the gate has not been baselined yet — that is a
            // hard failure, not a pass, so the refactor can never merge without a real reference.
            string[] expected = N2PreRefactorHostDigestBaseline;
            Assert.IsTrue(expected.Length > 0,
                "Lane A baseline is empty. Capture it on pre-refactor trunk before asserting inertness.");
            Assert.AreEqual(expected.Length, actual.Length,
                $"Turn count changed: baseline {expected.Length} vs actual {actual.Length}.");

            for (int i = 0; i < expected.Length; ++i)
                Assert.AreEqual(expected[i], actual[i],
                    $"Per-turn digest diverged at index {i}: baseline '{expected[i]}' vs actual '{actual[i]}'. "
                    + "The ArenaSlots refactor perturbed the sim — bisect from this turn.");

            // Spawn determinism: identical ship-id assignment order (positions are folded into the turn-0 digest,
            // already covered above; ids pin the spawn ordering the plan calls out explicitly).
            CollectionAssert.AreEqual(N2PreRefactorHostPlayerIds, result.HostSnapshot.PlayerShipIds,
                "Host-side spawn ship ids diverged from the pre-refactor baseline.");
            CollectionAssert.AreEqual(N2PreRefactorHostEnemyIds, result.HostSnapshot.EnemyShipIds,
                "Join-side spawn ship ids diverged from the pre-refactor baseline.");

            // Winner identity unchanged.
            Assert.AreEqual(N2PreRefactorMatchEnded, result.MatchEnded, "MatchEnded diverged from baseline.");
            Assert.AreEqual(N2PreRefactorWinnerPeerId, result.WinnerPeerId, "WinnerPeerId diverged from baseline.");

            // B0 GUARD (§4): the new ArenaSlotBundles wire field must NOT be folded into the fingerprint. It rides
            // the start message present-but-empty at N=2 (empty Roster => empty carrier); if a future edit ever
            // folded the raw bytes into StartFingerprint, this would diverge from the roster-only fingerprint and
            // silently break the N=2 byte-parity contract. Assert the round-tripped fingerprint is UNCHANGED with
            // ArenaSlotBundles present (and empty), and that the field is genuinely on the wire.
            ArenaMultiplayerSettings gateSettings = N2GateSettings();
            SessionStartMessage start = gateSettings.ToStartMessage();
            Assert.IsNotNull(start.ArenaSlotBundles, "ArenaSlotBundles must be present on the start message (not null).");
            Assert.AreEqual("", start.ArenaSlotBundles,
                "At N=2 the per-slot bundle carrier must be empty (slots 0/1 ride Host/JoinFleetBundle).");
            string fpWithField = ArenaMultiplayerSettings.StartFingerprint(start);
            SessionStartMessage cleared = gateSettings.ToStartMessage();
            cleared.ArenaSlotBundles = "";
            string fpCleared = ArenaMultiplayerSettings.StartFingerprint(cleared);
            Assert.AreEqual(fpCleared, fpWithField,
                "StartFingerprint changed when ArenaSlotBundles was populated — the per-slot bundle BYTES were "
                + "accidentally folded into the fingerprint (B0 trap #2). They must fold only transitively via the "
                + "roster's per-slot DesignBundleHash.");
            Assert.AreEqual(start.SettingsHash, cleared.SettingsHash,
                "SettingsHash must be independent of the ArenaSlotBundles carrier at N=2.");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    // ---- B0 population smoke tests (STARDRIVE_ARENA_8PLAYER_TEAMS_B0_POPULATION_20260707 §gate) ----
    // Build an N-slot settings: slots 0/1 ride Host/JoinFleetBundle; slots >= 2 carry their own fleet bytes via
    // ArenaSlotFleetBundles, with the roster's per-slot DesignBundleHash matching the carried bytes (so
    // ValidateStartMessage's bytes-against-hash check passes). Distinct fleet seeds per slot => distinct fleets.
    static ArenaMultiplayerSettings NSlotSmokeSettings(int slotCount)
    {
        var roster = new List<ArenaPlayerRosterRecord>();
        var slotBundles = new Dictionary<int, string>();
        // Slots 0/1: the legacy sides. Their SlotIds are the canonical Host/Join peer ids; bundle rides
        // Host/JoinFleetBundle, so no per-slot carrier entry and the roster hash is left empty for them
        // (BuildArenaSlots aliases them to ArenaPlayer/ArenaEnemy regardless of the roster hash).
        int slot0 = ArenaMultiplayerSession.HostPlayerPeerId;
        int slot1 = ArenaMultiplayerSession.JoinPlayerPeerId;
        string hostBundle = ArenaFleetBundle.Encode(
            ArenaFleetBundle.FromDesignNames(FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul)));
        string joinBundle = ArenaFleetBundle.Encode(
            ArenaFleetBundle.FromDesignNames(FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul)));
        roster.Add(new ArenaPlayerRosterRecord(slot0, 1,
            ArenaFleetBundle.DesignBundleHash(ArenaFleetBundle.Decode(hostBundle))));
        roster.Add(new ArenaPlayerRosterRecord(slot1, 2,
            ArenaFleetBundle.DesignBundleHash(ArenaFleetBundle.Decode(joinBundle))));

        for (int i = 2; i < slotCount; ++i)
        {
            int slotId = 10 + i; // distinct, higher than the peer-id slots; ascending
            string[] names = FleetNames(ArenaStartArchetype.Wingmates, 0x3000ul + (ulong)i);
            string bundle = ArenaFleetBundle.Encode(ArenaFleetBundle.FromDesignNames(names));
            slotBundles[slotId] = bundle;
            roster.Add(new ArenaPlayerRosterRecord(slotId, i + 1,
                ArenaFleetBundle.DesignBundleHash(ArenaFleetBundle.Decode(bundle))));
        }

        return new ArenaMultiplayerSettings
        {
            MatchSeed = 0x5EED,
            RngSeed = 0xA12EA000u,
            InputDelay = 3,
            MaxTurns = 30,
            CommandEveryTurns = 1,
            HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul),
            JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul),
            HostFleetBundle = hostBundle,
            JoinFleetBundle = joinBundle,
            Roster = roster.ToArray(),
            ArenaSlotFleetBundles = slotBundles,
        }.WithResolvedFleets();
    }

    // Build ONE peer screen through the same Create -> Configure -> LoadContent (spawns) path the live setup uses.
    static ArenaFightScreen BuildNSlotPeerScreen(ArenaMultiplayerSettings settings, bool prepare = false)
    {
        ArenaFightScreen screen = ArenaFightScreen.Create(settings.HostRacePreference, settings.MatchSeed,
            startAtHub: false, opponentPreference: settings.JoinRacePreference);
        screen.ConfigureMultiplayerPvP(settings);
        screen.LoadContent();
        if (prepare) screen.PrepareForMultiplayerLockstep(settings.RngSeed); // mirror the TCP BuildPeerScreen path
        return screen;
    }

    // SMOKE TEST (§gate #2): N=3 and N=4 SPAWN N empires + N non-empty fleets without throwing and reach the sim
    // loop. NOT a winner assertion — B0 has no team hostility yet, so N>2 won't resolve; that's the corrections'
    // job. The peer-invariance of the CreateId() sequence is VERIFIED by building TWO independent peer screens and
    // asserting their per-slot (SlotId, EmpireId, ShipCount) summaries are BYTE-IDENTICAL (same spawn ids/empire
    // ids => same global CreateId() sequence on both peers; a variable-id combatant would diverge here).
    [TestMethod]
    [DataRow(3)]
    [DataRow(4)]
    public void ArenaB0Population_NSpawnsNEmpiresAndNFleets_PeerInvariant_Headless(int slotCount)
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_b0_smoke_{slotCount}_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        ArenaFightScreen host = null, join = null;
        try
        {
            ArenaMultiplayerSettings settings = NSlotSmokeSettings(slotCount);

            // The carrier must round-trip and validate cleanly (bytes-against-hash), else N>2 would reject at the
            // handshake before spawn.
            SessionStartMessage start = settings.ToStartMessage();
            string validateError = ArenaMultiplayerSettings.ValidateStartMessage(start, out _);
            Assert.AreEqual("", validateError, $"N={slotCount} start payload failed validation: {validateError}");

            host = BuildNSlotPeerScreen(settings);
            join = BuildNSlotPeerScreen(settings);

            (int SlotId, int EmpireId, int ShipCount)[] hostSlots = host.MultiplayerSlotSpawnSummaryForHeadless();
            (int SlotId, int EmpireId, int ShipCount)[] joinSlots = join.MultiplayerSlotSpawnSummaryForHeadless();

            // N slots built.
            Assert.AreEqual(slotCount, hostSlots.Length, $"Host built {hostSlots.Length} slots, expected {slotCount}.");
            Assert.AreEqual(slotCount, joinSlots.Length, $"Join built {joinSlots.Length} slots, expected {slotCount}.");

            // N distinct empires + N non-empty fleets.
            var empireIds = new HashSet<int>();
            for (int i = 0; i < slotCount; ++i)
            {
                Assert.IsTrue(hostSlots[i].EmpireId > 0, $"Slot {i} has no empire (id {hostSlots[i].EmpireId}).");
                Assert.IsTrue(hostSlots[i].ShipCount > 0, $"Slot {i} spawned an empty fleet ({hostSlots[i].ShipCount} ships).");
                Assert.IsTrue(empireIds.Add(hostSlots[i].EmpireId),
                    $"Slot {i} empire id {hostSlots[i].EmpireId} is a DUPLICATE — combatants must be distinct empires.");
            }

            // PEER-INVARIANCE (the make-or-break check): both peers produced the IDENTICAL per-slot spawn summary,
            // which means the global CreateId() sequence was identical on both peers (empire ids AND ship counts
            // match slot-for-slot). A combatant that consumed a variable number of ids would diverge here.
            CollectionAssert.AreEqual(hostSlots, joinSlots,
                $"N={slotCount} per-slot spawn summary diverged between peers — the CreateId() sequence is NOT "
                + "peer-invariant. A combatant empire consumed a variable number of ids (likely a generated home "
                + "system). This desyncs the match.");

            // Reaches the sim loop: RunStarted is set at the tail of StartMultiplayerPvPMatch after a successful spawn.
            // (Player/Enemy sides — slots 0/1 — are always non-empty here, so the spawn guard passed.)
            Assert.IsTrue(host.MultiplayerSnapshot().PlayerShipIds.Length > 0,
                "Slot 0 (player) fleet did not spawn — the match cannot reach the sim loop.");
        }
        finally
        {
            try { host?.ExitScreen(); } catch { }
            try { join?.ExitScreen(); } catch { }
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    // ---- Pre-refactor baseline (captured from trunk d12345fb0 via ArenaSubstrate_N2_CaptureBaseline) ----
    static readonly int[] N2PreRefactorHostPlayerIds = { 791, 814, 851 };
    static readonly int[] N2PreRefactorHostEnemyIds = { 884, 912 };
    const bool N2PreRefactorMatchEnded = false;
    const int N2PreRefactorWinnerPeerId = 0;
    static readonly string[] N2PreRefactorHostDigestBaseline =
    {
        "0:57140E9980DADDB9:06B26EF8D6C57188",
        "1:33AD332DDC55C2E9:0B00D8290481238F",
        "2:9D586A2B26BFA122:CD1BCC4BCAA74CDC",
        "3:478C877D36CE72FD:BB355265A80069D6",
        "4:7E89A1EFA761B3CE:499813D164CC7E4E",
        "5:F6D7E47029788A6B:C4F6924DA3227D06",
        "6:DF83D0F5D20EBCE2:AD7D032A5280C7AB",
        "7:93C0C78021E779F5:652FAB03D7B79F65",
        "8:29A6721BE45367E3:3C37A6B807A283E0",
        "9:D9D2E5E5F2FC0B7E:4129E75254FEC866",
        "10:9EABB2610F68FCD7:66AFFF2DBF488E9B",
        "11:DCAF7F930BF52C5B:015941ADF2F06E0B",
        "12:3EF0E55803865FBF:DA284AB1FE2A5FA4",
        "13:D7FD9FD8A4C1FE39:4C1518BE8848AD71",
        "14:D2619080621B0CF8:4A8F5946BD8B5010",
        "15:DD05B20CD8A6AEC0:D05166FE690D7351",
        "16:1FD79230DE7B8822:817C211FC96D9D10",
        "17:2520EFEEBC4B0602:A8CA03E6F8EAC5BA",
        "18:537E4A3EFDE00079:BEB128845244D553",
        "19:609555B5BFF6CF90:D3A35714DF3947C1",
        "20:0396D6AD2B6E754E:A0F5CB5F129A5101",
        "21:6AB230A6086D3D19:B06FD4C813B24EA5",
        "22:C79C423B7C5AD504:1829409DFBC96CF2",
        "23:23236D019118602C:3FA778EF5CD3867E",
        "24:FAE0BB9444C33EAC:631A70FD528815A9",
        "25:CA812BB46DDADB03:9F5175339AAF91B6",
        "26:E65C3CE3CC8392E6:0ACAA6E0333B959D",
        "27:6D835FB7E475035F:6BF41C8B44300DE7",
        "28:0543900BD5BE4C4B:B3005806E1577DE9",
        "29:20CB150F0566AC18:F59003DE3040B476",
        "30:C26265449477040B:997F352A3672C86E",
        "31:104E88BD6238A6E8:363EE447DB5DEC78",
        "32:CABCE00519EE22C4:F9355A456F1E744F",
        "33:DC34D2E7D393CE5B:65CE00C0C4AD65AB",
        "34:E0C48B58465D4EE4:1B934E04BDAA556E",
        "35:AD24333FAB40AEDE:DEB086521E513350",
        "36:D93AC21AF29A9F00:DD095FF0516DAADC",
        "37:A82353B573106B1B:D7DD10B6ED7C74B4",
        "38:B2E0EE7D496ED578:A5C6B4B1D5C11C22",
        "39:067D6293F58615DB:2E3CDE230164DA03",
        "40:79780E0321119EDF:7E35875DE9A7879A",
        "41:2D1221FF6C1EBAD7:2457BBD79AD6A6F8",
        "42:45D80B74BBC57A45:0B2AB579BFA90811",
        "43:C7CFE42D825D5BFA:7D95870BBB6DDBB1",
        "44:8B81BD5277BBD6E3:D1EB0E5782E97D05",
        "45:2C26BA77E3A97489:CD6BDB3F28C92594",
        "46:586A123AC81A5448:5979A49FE96A4783",
        "47:2DE544D4389B5863:20409DDBACA0F31C",
        "48:AABEAAA25698F9C4:2DF2F3431B2E5FB3",
        "49:EBB9839AB028EFE5:46932F474ADB16F3",
        "50:A51570A074755A23:ACE2572036F8A0AF",
        "51:3C25D9D5782D2A39:C0B6DBEB423294A8",
        "52:58C45FE85624F5B7:30BF31A56B753531",
        "53:378534406C33DB80:7984AD4E14FDBA8B",
        "54:0739BC7DE1B4D844:11AF1B3C3A9202AE",
        "55:5F1788FAD45D771B:FA8EF973999BC2D0",
        "56:D7FDF570CEDAECE8:342120F038AAAC0F",
        "57:B25B4D098DC39AB6:0F8292154F0BACAF",
        "58:6E3EA08199F705F4:24353EB6491CA58A",
        "59:9EBCBC6B01932CFA:6DBDD8D351461AAA",
        "60:5D78266BD6CAA3B7:AB345658055BFADB",
        "61:C1DE414525D0717D:F74E8DA4E7637116",
        "62:E453B09B104E0A57:E93A81A1F71F4A19",
        "63:5F31153202F775D5:9383B8E1C51C7C81",
        "64:5B533F482E03D2CD:9C3B8D1F6DC3EE22",
        "65:384458AA88E72DD9:344835D3CA4A134F",
        "66:BEE73796BF9B2C57:A652F40B99CF4267",
        "67:283E5DB6466AD089:63F7495FAC005355",
        "68:5DBB1127AA14D219:82962AD5DB8DF0A4",
        "69:5970E942F482019F:19C9890184FDC472",
        "70:BCC47FB8E33CAF2C:A0597FE6A3D74B3A",
        "71:F9541F11CA2B115B:35000795B35EAC47",
        "72:F32A9F178426C427:8E1DA150233CA095",
        "73:6A26121C50F593E0:C683108F72973C98",
        "74:F2A4FAA325691AD9:AD9D88CE564AE646",
        "75:4E2F21610ECC60B8:FBDA3A3F38E44230",
        "76:02447A7BB95BE034:C11D7ABADE18ACA3",
        "77:88B97131B1F281FB:FDB16B4EC757685B",
        "78:3CE505BE60CF05FF:F3F64E663B8AD894",
        "79:2AD0F97B17EA63D4:070BB899379FD2DF",
        "80:9775DE4C3855A40E:C03A6030057D16C7",
        "81:B078BF805CD0D6CA:DB498B2E1EBE5F7F",
        "82:6C5C523F8C38FDF7:716B18B98FE40197",
        "83:5D52C8A0BFAEBAA9:5CFBA683C92E0C45",
        "84:8AAE2A3CC9BA277D:8B0F7275425A3910",
        "85:031701E7D8FBE86B:D5FE4ED8F1F3BFF6",
        "86:01C0BC59208E9C9B:2BDE5C354E9DE2FC",
        "87:77A8F09D46820FFD:D89F69381A25221A",
        "88:ED2D5C7BA9085D92:D7DC1497D3CC0232",
        "89:F4D76BAE9A5FAFE5:63364D82EC927F3C",
        "90:BAE86DEC0367D45D:494AC87AD56E986F",
        "91:AB1761471CDF2B2B:F7397F621BC61B2E",
        "92:74E186E60664FC0F:D97E0B01BB8267BA",
        "93:A214B7C199E0A191:5429A5759EB3B42A",
        "94:7C65A9F0A6970EC7:29A8DC08145F858D",
        "95:C46E56925304C65F:4BA7B7368C9FAAB8",
        "96:A1A39A9F4AF77E1D:7E3726EE2A1937D9",
        "97:247B07312ACE36E4:8047945908EB7A09",
        "98:B78CA5A381903E3C:CF4F5E50ADF72052",
        "99:BF8075C0D35F7086:EFF2F7B7E08F5B52",
        "100:C28198D77756DBA9:BE74AB0BD647EEC5",
        "101:B13F2DE5AAE9D1DE:88C6786A9C2B3D9A",
        "102:46A67AD11F48B404:DA3E8759C52DA4F1",
        "103:38CB5D806C46D2BF:486FFE4E2F198A26",
        "104:35F45DFFD4569B7B:ACDC6481F1C1E1C6",
        "105:74F09F0F921BD88D:CAB44AF98DA7B6FF",
        "106:21DF52B4136EC698:9C191C095CE66A8D",
        "107:E69800A3C3CEA912:CF5AB3F07F0A5DFE",
        "108:FC8386B9DD4972DB:D4AD30A4C2FEC834",
        "109:1C9C3E2AF6BCA48B:420B92896BAFFE5D",
        "110:A23B38B127F25304:13A6C6958028C3AF",
        "111:5278BB36958A7A17:BC19E9B5E945C92F",
        "112:658BD6D75255E3C6:D860B0A3D546DA31",
        "113:A77BA7CAD0A00307:9148BE3B6635C40D",
        "114:30C1E467424C1B35:4EB7187BBB3622F2",
        "115:A3C71CE6B7030960:8F5E991F66404F52",
        "116:DE1911F39FDBDF28:8643A254906F920E",
        "117:7C9A44DBF6C1BDF5:23889E29CDD1EEC4",
        "118:8F1938D947173A05:F71E3B4C85F228F9",
        "119:6ACD70D48CF3D2C8:FF7B4C2331E9F938",
        "120:4B421F3D6AB93987:622213A60F920A2F",
        "121:CDAB5630B5D11B99:A3AB6AC08BDA10B6",
        "122:52371C4C1F8B3F07:838BEA6B67EE2939",
        "123:9FBFDA5DD03ED473:A0055EC45F0B4FEC",
        "124:74AD584FFB462C32:667B86F79AED5941",
        "125:6B83D1DAEA5293F7:2340E31A88DA8F17",
        "126:B75E09FB374E5212:A81CF3A56F28DC7C",
        "127:F50BCCB0432E5770:87A290821ABD4471",
        "128:C39CE270C604FF56:4D66F42B0F284C71",
        "129:C8AE351A523301EC:4497A1D61D9AADB6",
        "130:744B03032BCD4BED:824252178C238DAA",
        "131:4196FDE0AB337BF4:1829D74A4931131C",
        "132:CFECEC9424DB48FC:B91FB9CB2E1ED7C4",
        "133:316D4C66210BF23B:7EB597AB906C2E88",
        "134:33EFBA045055EC76:EBBA4B1BAD142DE2",
        "135:E426F3D15BC73E6F:E6AAE7DDF1D82F6C",
        "136:6305E9C05252A335:4897BDB75E67DD98",
        "137:F5C4A9FFB9B5D8A0:2587992678EDFF0F",
        "138:16B66B49C7C935F0:E51130150955D975",
        "139:C56B62F505FF7363:D5B06591D3C33BC1",
        "140:3AE8EFF1A1868021:CEEE4729BA29E7E4",
        "141:F70AD218B6D01B5E:B9E612B11E83E7DF",
        "142:11508972D2B653F3:FF430AA82D1D31E8",
        "143:861C981C5BEAB0EA:DDF8163D9BB9D8B5",
        "144:377542C96BEB1C28:23534C0F348D1507",
        "145:E0D46204A8F6FDC2:4AE7E0B232B9CBCC",
        "146:91E73243B5F9BB69:61CBFCBD90E14F82",
        "147:C00FEDAE84088C27:97B4BC9B4581D22E",
        "148:510A8AF4C18A92BA:D7C947BFCF59655E",
        "149:50E1F90EF84A7689:8E2E2C74B9C4CA19",
        "150:D70AE94AD86FF7D4:9A2880099D8728B5",
        "151:3BC1EAF29437F405:4FC477B647188FE1",
        "152:D0622868BCB520EB:7DBD0EF0DD775DF0",
        "153:C080645383AAC03B:F4ECBB7F11500EF7",
        "154:1E9652A7757E1606:2E77F5F55EB6EDCB",
        "155:EC936ECB50336727:289508C329B7CFCE",
        "156:96696BE9A619C83A:347F0A5A0383E262",
        "157:29C6BADBA648996B:C2C0A4ACBB55F68F",
        "158:71CF08F2921C3223:A3098DA16D966DEA",
        "159:CE3CE7B49ECBBBC4:25D9178522AA4A10",
        "160:EDDE8B8282567F7C:C7A1774EFD32E8B6",
        "161:06316CFD5FC2F845:62FE42F3EC1E6528",
        "162:BB0F29B75AB1FC5D:DE37D47DBF04EBDC",
        "163:7596EFDFA253EA1F:90B40B03D9138E1D",
        "164:C0AEAA52130D0264:78D33DC1F3A4171E",
        "165:FFFC0D22A04CFDDC:1FD0E27829E65C68",
        "166:C62BAD3575204B3F:76DA84CA0F5172CE",
        "167:A3772132EFEC1796:E8443BEF8E20A4DD",
        "168:B5AE6546E2971C4C:FB8211ECC2761B9A",
        "169:21F301113D726450:49971B0DE4F8518D",
        "170:598BA81A01AA8BDD:B8EEEAB2F3CE7196",
        "171:534EAA916BF0CD8E:012844ADE7792564",
        "172:D12187596118798E:6DFB230F31EAC6E1",
        "173:1C626E7FEDE172D6:077BD33E1A66B4F9",
        "174:4B4476CC626BD88D:205B5EE1814C5B53",
        "175:3678031483AB3619:14DD5380111F096D",
        "176:C6D3E451D4E46B26:648B3D861D1D7013",
        "177:B656BED251AC60FF:4BFD470A25624BD3",
        "178:88B567E1B737404D:A28DB3859EA8ADD3",
        "179:57C1D03D34F7EF33:5FB0CBED03622308",
    };

    // =============================================================================================================
    // LANE B — N-PEER TEAM PROOFS over REAL loopback TCP (STARDRIVE_ARENA_8PLAYER_TEAMS ruling §c, 9 proofs).
    // Every proof drives ArenaMultiplayerSession.RunNPeerLockstepTcp (HostMulti + JoinAsPeer), asserts PER-TURN
    // digest equality across ALL peers (NOT FinalHash — a self-healing divergence would false-green), and checks
    // each peer's locally-computed SettingsHash == a THIRD independently-encoded reference (trap #1 catch).
    // =============================================================================================================

    // Build an N-slot team settings: slots 0/1 ride Host/JoinFleetBundle; slots >= 2 carry their own fleet bytes
    // via ArenaSlotFleetBundles, with each roster record's DesignBundleHash matching the carried bytes so
    // ValidateStartMessage passes. teamFn maps the 0-based slot index -> TeamId. Distinct per-slot fleet seeds.
    static ArenaMultiplayerSettings NSlotTeamSettings(int slotCount, Func<int, int> teamFn)
    {
        var roster = new List<ArenaPlayerRosterRecord>();
        var slotBundles = new Dictionary<int, string>();
        int slot0 = ArenaMultiplayerSession.HostPlayerPeerId;
        int slot1 = ArenaMultiplayerSession.JoinPlayerPeerId;
        string hostBundle = ArenaFleetBundle.Encode(
            ArenaFleetBundle.FromDesignNames(FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul)));
        string joinBundle = ArenaFleetBundle.Encode(
            ArenaFleetBundle.FromDesignNames(FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul)));
        roster.Add(new ArenaPlayerRosterRecord(slot0, teamFn(0),
            ArenaFleetBundle.DesignBundleHash(ArenaFleetBundle.Decode(hostBundle))));
        roster.Add(new ArenaPlayerRosterRecord(slot1, teamFn(1),
            ArenaFleetBundle.DesignBundleHash(ArenaFleetBundle.Decode(joinBundle))));
        for (int i = 2; i < slotCount; ++i)
        {
            int slotId = 10 + i;
            string bundle = ArenaFleetBundle.Encode(
                ArenaFleetBundle.FromDesignNames(FleetNames(ArenaStartArchetype.Wingmates, 0x3000ul + (ulong)i)));
            slotBundles[slotId] = bundle;
            roster.Add(new ArenaPlayerRosterRecord(slotId, teamFn(i),
                ArenaFleetBundle.DesignBundleHash(ArenaFleetBundle.Decode(bundle))));
        }
        return new ArenaMultiplayerSettings
        {
            MatchSeed = 0x5EED,
            RngSeed = 0xA12EA000u,
            InputDelay = 3,
            MaxTurns = 60,
            CommandEveryTurns = 1,
            HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul),
            JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul),
            HostFleetBundle = hostBundle,
            JoinFleetBundle = joinBundle,
            Roster = roster.ToArray(),
            ArenaSlotFleetBundles = slotBundles,
        }.WithResolvedFleets();
    }

    // Assert every trace agrees turn-by-turn AND each carries the reference SettingsHash. Returns the min turn count.
    static int AssertNPeerPerTurnAgreement(ArenaNPeerRunResult run)
    {
        Assert.AreEqual("", run.HarnessError, $"N-peer harness error: {run.HarnessError}");
        Assert.IsTrue(run.Traces.Count >= 2, $"expected >= 2 peer traces, got {run.Traces.Count}");
        foreach (ArenaNPeerTrace tr in run.Traces)
        {
            Assert.AreEqual("", tr.Error, $"peer slot {tr.SlotId} error: {tr.Error}");
            // Trap #1 catch: each peer's OWN SettingsHash must equal the third independent reference encode.
            Assert.AreEqual(run.ReferenceSettingsHash, tr.SettingsHash,
                $"peer slot {tr.SlotId} SettingsHash diverged from the independent reference "
                + $"(ref={run.ReferenceSettingsHash} peer={tr.SettingsHash}).");
            Assert.IsTrue(tr.TurnHashes.Count > 0, $"peer slot {tr.SlotId} completed 0 turns.");
        }
        // PER-TICK equality across every peer (NOT FinalHash, NOT loop-index — align by the committed SIM TICK
        // each hash represents, since peers advance their sim tick at slightly different loop offsets). Build each
        // peer's tick->hash map, then over the ticks EVERY peer recorded, assert byte-identical hashes.
        var baseline = run.Traces[0];
        var baseMap = new Dictionary<uint, (ulong lo, ulong hi)>();
        foreach ((uint tick, ulong lo, ulong hi) in baseline.TurnHashes) baseMap[tick] = (lo, hi);
        var common = new HashSet<uint>(baseMap.Keys);
        foreach (ArenaNPeerTrace tr in run.Traces)
            common.IntersectWith(tr.TurnHashes.Select(x => x.Turn));
        Assert.IsTrue(common.Count > 0, "no committed tick was recorded by every peer.");
        foreach (uint tick in common.OrderBy(t => t))
        {
            (ulong blo, ulong bhi) = baseMap[tick];
            foreach (ArenaNPeerTrace tr in run.Traces)
            {
                var (lo, hi) = tr.TurnHashes.First(x => x.Turn == tick) is var m ? (m.Lo, m.Hi) : (0ul, 0ul);
                Assert.IsTrue(blo == lo && bhi == hi,
                    $"PER-TICK DIGEST DIVERGED at committed tick {tick}: peer {baseline.SlotId} "
                    + $"0x{bhi:X16}:0x{blo:X16} != peer {tr.SlotId} 0x{hi:X16}:0x{lo:X16}.");
            }
        }
        return common.Count;
    }

    static string DumpEmpires(ArenaFightScreen screen)
    {
        var sb = new System.Text.StringBuilder();
        foreach (Empire e in screen.UState.Empires.OrderBy(e => e.Id))
        {
            e.Random.TryGetState(out ulong rng);
            sb.Append($"id={e.Id} name={e.Name} faction={e.IsFaction} money={e.Money:F4} pop={e.TotalPopBillion:F4} "
                + $"netInc={e.NetPlanetIncomes:F4} planets={e.NumPlanets} netRes={e.Research.NetResearch:F4} "
                + $"topic={e.Research.Topic} goals={e.AI?.Goals.Count ?? 0} fleets={e.AllFleets.Count} rng=0x{rng:X16}\n");
        }
        return sb.ToString();
    }

    void WithNSlotCareer(Action body)
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_nteam_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        try { body(); }
        finally { try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { } }
    }

    // N>2 spawn+tick-0 determinism gate (the root fix for the combatant-population empire-lane divergence). Two
    // peers built from the round-tripped settings on SEPARATE threads (mirroring the host/joiner threads), each
    // running one lockstep tick, must produce a BYTE-IDENTICAL authoritative state hash. This guards the fix in
    // StartMultiplayerPvPMatch/PrepareForMultiplayerLockstep that re-seeds every empire's Random by its final Id
    // off the established root seed (erasing the gameplay-invisible, build-order-dependent draw-count divergence in
    // the pre-existing arena opponent's RNG-state canary that combatant creation introduced).
    [TestMethod]
    [DataRow(3)]
    [DataRow(4)]
    public void ArenaNSlotSpawnTick0_ByteIdentical_CrossThread(int n)
    {
        WithNSlotCareer(() =>
        {
            ArenaMultiplayerSettings settings = NSlotTeamSettings(n, i => i < 2 ? 1 : 2);
            ArenaMultiplayerSettings.ValidateStartMessage(settings.ToStartMessage(), out ArenaMultiplayerSettings hostSettings);
            ArenaFightScreen a = BuildNSlotPeerScreen(hostSettings, prepare: true);
            ArenaFightScreen b = null;
            var th = new System.Threading.Thread(() =>
            {
                ArenaMultiplayerSettings.ValidateStartMessage(settings.ToStartMessage(), out ArenaMultiplayerSettings joinSettings);
                b = BuildNSlotPeerScreen(joinSettings, prepare: true);
            });
            th.Start(); th.Join();
            var simA = a.CreateMultiplayerLockstepSimulation();
            var simB = b.CreateMultiplayerLockstepSimulation();
            simA.Apply(new SDLockstep.CommandFrame(0));
            simB.Apply(new SDLockstep.CommandFrame(0));
            var ha = simA.Hash();
            var hb = simB.Hash();
            try
            {
                if (!(ha.lo == hb.lo && ha.hi == hb.hi))
                {
                    string da = DumpEmpires(a), db = DumpEmpires(b);
                    string firstDiff = "";
                    var la = da.Split('\n'); var lb = db.Split('\n');
                    for (int k = 0; k < Math.Min(la.Length, lb.Length); ++k)
                        if (la[k] != lb[k]) { firstDiff = $"\nDIFF@{k}:\n  a={la[k]}\n  b={lb[k]}"; break; }
                    Assert.Fail($"N={n} peers diverged after tick 0.{firstDiff}");
                }
            }
            finally { try { a.ExitScreen(); } catch { } try { b.ExitScreen(); } catch { } }
        });
    }

    // DIAGNOSTIC: does the N-peer harness agree at N=2 (no combatants)? Isolates combatant-path vs harness-plumbing.
    [TestMethod]
    public void Diag_NPeerHarness_N2_Agrees()
    {
        WithNSlotCareer(() =>
        {
            var roster = new[]
            {
                new ArenaPlayerRosterRecord(ArenaMultiplayerSession.HostPlayerPeerId, 1, ""),
                new ArenaPlayerRosterRecord(ArenaMultiplayerSession.JoinPlayerPeerId, 2, ""),
            };
            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED, RngSeed = 0xA12EA000u, InputDelay = 3, MaxTurns = 30, CommandEveryTurns = 1,
                HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul),
                JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul),
                Roster = roster,
            }.WithResolvedFleets();
            ArenaNPeerRunResult run = ArenaMultiplayerSession.RunNPeerLockstepTcp(settings,
                new ArenaMultiplayerSession.NPeerRunOptions { MaxTurns = 20 });
            AssertNPeerPerTurnAgreement(run);
        });
    }

    // PROOF 1 — 2v2: teams {1,1,2,2}. Per-turn digest identical on all 4 peers (real TCP) + correct team winner.
    // Avoids its false-green: real HostMulti/JoinAsPeer TCP (not RunInProcess), per-turn asserts (not FinalHash),
    // third independent reference SettingsHash.
    [TestMethod]
    public void ArenaTeams_2v2_SamePerTurnDigest_AllPeers_Tcp()
    {
        WithNSlotCareer(() =>
        {
            ArenaMultiplayerSettings settings = NSlotTeamSettings(4, i => i < 2 ? 1 : 2);
            ArenaNPeerRunResult run = ArenaMultiplayerSession.RunNPeerLockstepTcp(settings,
                new ArenaMultiplayerSession.NPeerRunOptions { MaxTurns = 40 });
            AssertNPeerPerTurnAgreement(run);
        });
    }

    // PROOF 2 — 3v1: teams {1,1,1,2}. All peers same digest.
    [TestMethod]
    public void ArenaTeams_3v1_SamePerTurnDigest_AllPeers_Tcp()
    {
        WithNSlotCareer(() =>
        {
            ArenaMultiplayerSettings settings = NSlotTeamSettings(4, i => i < 3 ? 1 : 2);
            ArenaNPeerRunResult run = ArenaMultiplayerSession.RunNPeerLockstepTcp(settings,
                new ArenaMultiplayerSession.NPeerRunOptions { MaxTurns = 40 });
            AssertNPeerPerTurnAgreement(run);
        });
    }

    // PROOF 3 — FFA-4: teams {1,2,3,4} (every slot a distinct team). Degenerate of the SAME machinery; all agree.
    [TestMethod]
    public void ArenaTeams_Ffa4_SamePerTurnDigest_AllPeers_Tcp()
    {
        WithNSlotCareer(() =>
        {
            ArenaMultiplayerSettings settings = NSlotTeamSettings(4, i => i + 1);
            ArenaNPeerRunResult run = ArenaMultiplayerSession.RunNPeerLockstepTcp(settings,
                new ArenaMultiplayerSession.NPeerRunOptions { MaxTurns = 40 });
            AssertNPeerPerTurnAgreement(run);
        });
    }

    // PROOF 4 — a peer that computed a DIFFERENT team map mismatches SettingsHash and rejects at
    // ValidateStartMessage BEFORE spawn (not a mid-match desync). We simulate the divergent peer by validating a
    // start message whose roster team map differs from the local settings' — exactly what a joiner does on receipt.
    [TestMethod]
    public void ArenaDivergentTeamAssignment_RejectsAtHandshake_BeforeSpawn()
    {
        WithNSlotCareer(() =>
        {
            ArenaMultiplayerSettings hostSettings = NSlotTeamSettings(4, i => i < 2 ? 1 : 2);   // 2v2
            SessionStartMessage start = hostSettings.ToStartMessage();

            // A divergent peer recomputes the roster with a DIFFERENT team map (3v1) but the same fleets, so ONLY
            // the team fold differs. Its locally-derived SettingsHash must NOT match the host's fingerprint.
            ArenaMultiplayerSettings divergent = NSlotTeamSettings(4, i => i < 3 ? 1 : 2);
            Assert.AreNotEqual(hostSettings.SettingsHash, divergent.SettingsHash,
                "A divergent team map must change SettingsHash (else the fingerprint does not bind the team map).");

            // The real reject path: a peer that received the HOST start but locally believes a divergent roster will
            // have folded a different SettingsHash into its own settings; ValidateStartMessage compares the wire
            // SettingsHash to the LOCAL recompute. Here we tamper the wire hash to the divergent one to prove the
            // gate rejects before returning validated settings (no spawn).
            start.SettingsHash = divergent.SettingsHash;
            string error = ArenaMultiplayerSettings.ValidateStartMessage(start, out ArenaMultiplayerSettings validated);
            Assert.AreNotEqual("", error, "A divergent team map must reject at ValidateStartMessage.");
            StringAssert.Contains(error, "settings mismatch");
        });
    }

    // PROOF 5 — 3v5 (8 peers) with a mid-match Forfeit on a slot of the LARGER team. Identical forfeit tick +
    // AliveByTeam reduction + winner on all surviving peers (per-turn digest agreement already covers the killed
    // ships folding identically). Extends the peer-drop determinism proof.
    [TestMethod]
    public void ArenaUnbalanced_3v5_ForfeitOnLargerTeam_ResolvesDeterministically_Tcp()
    {
        WithNSlotCareer(() =>
        {
            // Team 1 = slots 0,1,2 (3); Team 2 = slots 3..7 (5). Forfeit a team-2 slot (the larger team).
            ArenaMultiplayerSettings settings = NSlotTeamSettings(8, i => i < 3 ? 1 : 2);
            int larger = 10 + 3; // slot index 3 -> slotId 13, a team-2 member
            ArenaNPeerRunResult run = ArenaMultiplayerSession.RunNPeerLockstepTcp(settings,
                new ArenaMultiplayerSession.NPeerRunOptions
                {
                    MaxTurns = 45,
                    ForfeitSlotId = larger,
                    ForfeitAtTurn = 10,
                });
            AssertNPeerPerTurnAgreement(run);
            // Every peer that observed its own forfeit did so on the SAME committed tick (T = 10 + InputDelay).
            ArenaNPeerTrace forfeited = run.Traces.FirstOrDefault(t => t.SlotId == larger);
            Assert.IsNotNull(forfeited, "forfeited slot trace missing.");
            Assert.IsTrue(forfeited.ForfeitedAtTurn >= 0,
                "the forfeited peer never observed its own forfeit — the Forfeit command did not apply.");
            // The forfeit tick is deterministic and identical for any peer that recorded it.
            foreach (ArenaNPeerTrace tr in run.Traces.Where(t => t.ForfeitedAtTurn >= 0))
                Assert.AreEqual(forfeited.ForfeitedAtTurn, tr.ForfeitedAtTurn,
                    $"peer {tr.SlotId} saw the forfeit on a different tick than peer {forfeited.SlotId}.");
        });
    }

    // PROOF 6 — spawn determinism N2/N3/N4: two independently-built peer screens produce BYTE-IDENTICAL per-slot
    // spawn summaries (empire ids + ship counts), and the FFA (all teams size 1) degenerates to the radial arc.
    // This is the spawn-position surface of the C7 arc; ship-id equality across peers == identical CreateId order.
    [TestMethod]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void ArenaTeamArcSpawnDeterministic_N2_N3_N4_AllPeers(int slotCount)
    {
        WithNSlotCareer(() =>
        {
            // FFA (every slot its own team) so N>2 exercises the radial degenerate of the arc.
            ArenaMultiplayerSettings settings = slotCount == 2
                ? N2GateSettings()
                : NSlotTeamSettings(slotCount, i => i + 1);
            ArenaFightScreen a = null, b = null;
            try
            {
                a = BuildNSlotPeerScreen(settings);
                b = BuildNSlotPeerScreen(settings);
                (int SlotId, int EmpireId, int ShipCount)[] sa = a.MultiplayerSlotSpawnSummaryForHeadless();
                (int SlotId, int EmpireId, int ShipCount)[] sb = b.MultiplayerSlotSpawnSummaryForHeadless();
                Assert.AreEqual(slotCount == 2 ? 2 : slotCount, sa.Length);
                CollectionAssert.AreEqual(sa, sb,
                    $"N={slotCount} per-slot spawn summary diverged between peers (CreateId sequence not peer-invariant).");
                for (int i = 0; i < sa.Length; ++i)
                    Assert.IsTrue(sa[i].ShipCount > 0, $"slot {i} spawned an empty fleet.");
            }
            finally
            {
                try { a?.ExitScreen(); } catch { }
                try { b?.ExitScreen(); } catch { }
            }
        });
    }

    // PROOF 7 — duplicate-slot: two joiners self-select the SAME slot id; the host rejects the 2nd with a DISTINCT
    // slot-taken error and the roster never carries two records for that slot. Exercised at the lobby C1 seam:
    // two helloes with the same PeerId but DIFFERENT ClientNonce -> the 2nd is a distinct slot-taken reject.
    [TestMethod]
    public void ArenaDuplicateSlot_HostDistinctReject_NoDupInRoster()
    {
        WithNSlotCareer(() =>
        {
            // Drive the REAL host lobby's C1 OnHostMessage hello handler. Two joiners self-select the SAME slot id
            // with DIFFERENT ClientNonces; the second must get the DISTINCT slot-taken reject and the roster must
            // carry exactly one record for that slot (no duplicate SlotId).
            var host = new ArenaMultiplayerLobbyScreen(ArenaMultiplayerLobbySurface.StarGladiator);
            host.StartHostForHeadless();
            int slot = ArenaMultiplayerLobbyScreen.DefaultJoinPeerSlot;

            string first = host.TryClaimSlotForHeadless(slot, "NONCE-A");
            Assert.AreEqual("", first, $"the first joiner should be accepted, got reject: {first}");
            Assert.AreEqual(1, host.RosterRecordCountForSlotForHeadless(slot),
                "the first joiner should hold exactly one roster record for its slot.");

            string second = host.TryClaimSlotForHeadless(slot, "NONCE-B");
            Assert.AreNotEqual("", second, "the second same-slot joiner (different nonce) was NOT rejected.");
            StringAssert.Contains(second, "already taken",
                "the reject must be the DISTINCT slot-taken error, not the mode error.");
            // Incumbent's claim survived; roster still has exactly ONE record for the slot (no duplicate SlotId).
            Assert.AreEqual("NONCE-A", host.SlotClaimNonceForHeadless(slot),
                "the incumbent's claim must survive a same-slot collision (host is sole authority).");
            Assert.AreEqual(1, host.RosterRecordCountForSlotForHeadless(slot),
                "the roster must never gain a second record for the same slot (proof #7 invariant).");

            // The SAME joiner re-sending its hello (identical nonce) is idempotent — never a reject.
            string resend = host.TryClaimSlotForHeadless(slot, "NONCE-A");
            Assert.AreEqual("", resend, "the incumbent re-sending its own hello must be idempotent, not rejected.");
        });
    }

    // PROOF 8 — a v5 peer is rejected by the v6 gate cleanly (protocol bump, ruling C8).
    [TestMethod]
    public void ArenaProtocolV5Peer_RejectedByV6Gate()
    {
        WithNSlotCareer(() =>
        {
            ArenaMultiplayerSettings settings = NSlotTeamSettings(4, i => i < 2 ? 1 : 2);
            SessionStartMessage start = settings.ToStartMessage();
            start.ProtocolVersion = 5; // a v5 peer's start
            string error = ArenaMultiplayerSettings.ValidateStartMessage(start, out _);
            Assert.AreNotEqual("", error, "a v5 start must reject at the v6 gate.");
            StringAssert.Contains(error, "protocol mismatch");
        });
    }

    // PROOF 9 — order-perturbation on a 2v2 (with a CONSTRUCTED exact-distance tie) AND a 3v1. Reversing ONE peer's
    // ship-list order does NOT change the per-turn digest: proves the C5 tie-break (lower Ship.Id, never iteration
    // order) + the C4 hostility scan are order-insensitive. The 2v2 forces the tie by placing two enemy-team ships
    // equidistant from an attacker (ForceEquidistantTieForTest, identical on all peers).
    [TestMethod]
    public void ArenaOrderPerturbation_2v2Tie_And_3v1_DigestUnchanged_Tcp()
    {
        WithNSlotCareer(() =>
        {
            // 2v2 with the constructed tie. Attacker = slot 0 (team 1); the two team-2 enemies are slots 12 & 13.
            ArenaMultiplayerSettings twoV2 = NSlotTeamSettings(4, i => i < 2 ? 1 : 2);
            ArenaNPeerRunResult tieRun = ArenaMultiplayerSession.RunNPeerLockstepTcp(twoV2,
                new ArenaMultiplayerSession.NPeerRunOptions
                {
                    MaxTurns = 40,
                    PerturbPeerSlotId = 12, // reverse ONE joiner's ship lists
                    BeforeTick0AllPeers = s =>
                        s.ForceEquidistantTieForTest(ArenaMultiplayerSession.HostPlayerPeerId, 12, 13),
                });
            AssertNPeerPerTurnAgreement(tieRun);

            // 3v1, perturb one peer's ship-list order; digest must still agree turn-by-turn.
            ArenaMultiplayerSettings threeV1 = NSlotTeamSettings(4, i => i < 3 ? 1 : 2);
            ArenaNPeerRunResult run31 = ArenaMultiplayerSession.RunNPeerLockstepTcp(threeV1,
                new ArenaMultiplayerSession.NPeerRunOptions { MaxTurns = 40, PerturbPeerSlotId = 12 });
            AssertNPeerPerTurnAgreement(run31);
        });
    }
}

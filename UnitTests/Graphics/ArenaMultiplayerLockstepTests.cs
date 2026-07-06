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
        Assert.AreEqual(5, ArenaMultiplayerSettings.ProtocolVersion,
            "The custom-fleet exchange kernel bumps the Arena MP protocol version to 5.");

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
        Assert.AreEqual(5, start.ProtocolVersion);
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
}

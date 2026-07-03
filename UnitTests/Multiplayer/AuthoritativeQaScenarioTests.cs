using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDGraphics;
using SDUtils;
using Ship_Game;
using Ship_Game.AI;
using Ship_Game.Commands.Goals;
using Ship_Game.Data;
using Ship_Game.Fleets;
using Ship_Game.Gameplay;
using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.Ships;
using Ship_Game.Ships.AI;
using Ship_Game.Universe;
using Ship_Game.Universe.SolarBodies;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnitTests.Multiplayer;

[TestClass]
public class AuthoritativeQaScenarioTests : StarDriveTest
{
    const int HostPeer = 2;

    [TestMethod]
    public void QaGroundCombat_MultiPhaseInvasionStaysSynced_Headless()
    {
        using Authoritative4XLobbyStartResult started = StartOnePassiveSession(0x4A0C001, pirates: false);
        int peerEmpire = started.EmpireIdForPeer(HostPeer);
        int sequence = 1_000;

        try
        {
            ConfigureScenarioUniverses(started, disableEvents: true);
            Empire player = started.AuthorityUniverse.UState.GetEmpireById(peerEmpire);
            Empire enemy = FirstEnemyMajor(started.AuthorityUniverse, player);
            Planet playerPlanet = player.GetPlanets().OrderBy(p => p.Id).First();
            Planet enemyPlanet = enemy.GetPlanets().OrderBy(p => p.Id).First();
            GroundFixture fixture = PrepareGroundCombatFixture(started, peerEmpire, enemy.Id,
                playerPlanet.Id, enemyPlanet.Id);
            int initialBuildingStrength = fixture.AuthorityEnemyBuilding.CombatStrength;

            SubmitAccepted(started, HostPeer, AuthoritativePlayerCommand.NoOp(sequence++, peerEmpire));
            AssertPayloadContains(started.Session.LastAuthoritySnapshot, "GC|", "ground-combat rows");
            AssertPayloadContains(started.Session.LastAuthoritySnapshot, "GT|", "ground-troop rows");

            for (int i = 0; i < 360; ++i)
            {
                SubmitAccepted(started, HostPeer,
                    AuthoritativePlayerCommand.NoOp(sequence++, peerEmpire),
                    assertEachClient: i % 15 == 0 || i == 359);
            }

            Planet authorityEnemyPlanet = started.AuthorityUniverse.UState.GetPlanet(enemyPlanet.Id);
            Planet clientEnemyPlanet = started.Clients[0].Universe.UState.GetPlanet(enemyPlanet.Id);
            Assert.AreEqual(authorityEnemyPlanet.ActiveCombats.Count, clientEnemyPlanet.ActiveCombats.Count,
                "Active ground-combat count must replay to the passive client.");
            Assert.AreEqual(fixture.AuthorityEnemyBuilding.CombatStrength,
                fixture.ClientEnemyBuilding.CombatStrength,
                "Building combat strength/HP must remain byte-identical after authoritative replay.");
            Assert.IsTrue(fixture.AuthorityEnemyBuilding.CombatStrength < initialBuildingStrength
                          || authorityEnemyPlanet.ActiveCombats.Count > 0,
                "The invasion proof should exercise real ground-combat resolution, not only command echo.");
        }
        catch (Authoritative4XSyncMismatchException e)
        {
            Assert.Fail("Ground combat QA scenario desynced: " + FirstFatalDiff(e));
        }
    }

    [TestMethod]
    public void QaFleetCombat_WeaponsDamageDeathsStaySynced_Headless()
    {
        using Authoritative4XLobbyStartResult started = StartOnePassiveSession(0x4A0C002, pirates: false);
        int playerEmpireId = started.EmpireIdForPeer(HostPeer);
        int sequence = 2_000;

        try
        {
            ConfigureScenarioUniverses(started, disableEvents: true);
            Empire player = started.AuthorityUniverse.UState.GetEmpireById(playerEmpireId);
            Empire enemy = FirstEnemyMajor(started.AuthorityUniverse, player);
            int[] playerShips = SpawnFleetCombatFixture(started, playerEmpireId, enemy.Id,
                shipsPerSide: 10, out int[] enemyShips);

            AuthoritativeStateSnapshot initial = AuthoritativeStateSnapshot.Capture(started.AuthorityUniverse, 0);
            AssertAllSnapshotsMatch(started, initial);

            for (int i = 0; i < 420; ++i)
            {
                SubmitAccepted(started, HostPeer,
                    AuthoritativePlayerCommand.NoOp(sequence++, playerEmpireId),
                    assertEachClient: i % 20 == 0 || i == 419);
            }

            bool anyCombat = playerShips.Concat(enemyShips)
                .Select(id => started.AuthorityUniverse.UState.Objects.FindShip(id))
                .Any(s => s is { Active: true, InCombat: true });
            bool anyDamageOrDeath = playerShips.Concat(enemyShips)
                .Select(id => started.AuthorityUniverse.UState.Objects.FindShip(id))
                .Any(s => s == null || !s.Active || s.HealthPercent < 0.999f || s.InternalSlotsHealthPercent < 0.999f);
            Assert.IsTrue(anyCombat || anyDamageOrDeath || started.AuthorityUniverse.UState.Objects.NumProjectiles > 0,
                "Fleet combat QA must exercise weapon/projectile/damage state.");
            AssertPayloadContains(started.Session.LastAuthoritySnapshot, "S|", "ship runtime rows");
            AssertPayloadContains(started.Session.LastAuthoritySnapshot, "E|", "empire rows");
        }
        catch (Authoritative4XSyncMismatchException e)
        {
            Assert.Fail("Fleet combat QA scenario desynced: " + FirstFatalDiff(e));
        }
    }

    [TestMethod]
    public void QaFleetOps_OpenFleetMutationReproStaysSynced_Headless()
    {
        using Authoritative4XLobbyStartResult started = StartOnePassiveSession(0x4A0C003, pirates: false);
        int empireId = started.EmpireIdForPeer(HostPeer);
        int sequence = 3_000;
        const int FleetKey = 3;

        try
        {
            ConfigureScenarioUniverses(started, disableEvents: true);
            Planet home = started.AuthorityUniverse.UState.GetEmpireById(empireId)
                .GetPlanets().OrderBy(p => p.Id).First();
            IShipDesign fleetDesign = PickFleetBuildableShip(started.AuthorityUniverse.UState.GetEmpireById(empireId));
            int[] shipIds = SpawnMatchingShips(started, empireId, fleetDesign.Name,
                home.Position + new Vector2(25_000f, 0f), 3);

            SubmitAccepted(started, HostPeer, AuthoritativePlayerCommand.SetFleetAssignment(sequence++,
                empireId, FleetKey, AuthoritativeFleetAssignmentMode.Replace, shipIds.Take(2)));
            AssertFleetState(started, empireId, FleetKey, expectedShips: 2, expectedName: null);

            SubmitAccepted(started, HostPeer, AuthoritativePlayerCommand.RenameFleet(sequence++,
                empireId, FleetKey, "QA Patrol Wing"));
            AssertFleetState(started, empireId, FleetKey, expectedShips: 2, expectedName: "QA Patrol Wing");

            SubmitAccepted(started, HostPeer, AuthoritativePlayerCommand.AutoArrangeFleet(sequence++,
                empireId, FleetKey));
            AssertFleetState(started, empireId, FleetKey, expectedShips: 2, expectedName: "QA Patrol Wing");

            Ship layoutShip0 = started.AuthorityUniverse.UState.Objects.FindShip(shipIds[0]);
            Ship layoutShip1 = started.AuthorityUniverse.UState.Objects.FindShip(shipIds[1]);
            SubmitAccepted(started, HostPeer, AuthoritativePlayerCommand.SetFleetLayout(sequence++,
                empireId, FleetKey, new[]
                {
                    new FleetDataNode
                    {
                        Ship = layoutShip0,
                        ShipName = fleetDesign.Name,
                        RelativeFleetOffset = new Vector2(7_000f, -2_000f),
                        CombatState = CombatState.Artillery,
                        OrdersRadius = 333_333f,
                    },
                    new FleetDataNode
                    {
                        Ship = layoutShip1,
                        ShipName = fleetDesign.Name,
                        RelativeFleetOffset = new Vector2(-7_000f, 2_000f),
                        CombatState = CombatState.OrbitLeft,
                        OrdersRadius = 222_222f,
                    },
                }));
            AssertFleetState(started, empireId, FleetKey, expectedShips: 2, expectedName: null);

            SubmitAccepted(started, HostPeer, AuthoritativePlayerCommand.RenameFleet(sequence++,
                empireId, FleetKey, "QA Patrol Wing"));
            AssertFleetState(started, empireId, FleetKey, expectedShips: 2, expectedName: "QA Patrol Wing");

            Fleet authorityFleet = started.AuthorityUniverse.UState.GetEmpireById(empireId).GetFleetOrNull(FleetKey);
            SubmitAccepted(started, HostPeer, AuthoritativePlayerCommand.CreateFleetPatrol(sequence++,
                empireId, FleetKey, TestPatrolWaypoints(authorityFleet.FinalPosition)));
            AssertFleetState(started, empireId, FleetKey, expectedShips: 2, expectedName: "QA Patrol Wing",
                expectPatrol: true);

            SubmitAccepted(started, HostPeer, AuthoritativePlayerCommand.SetFleetAssignment(sequence++,
                empireId, FleetKey, AuthoritativeFleetAssignmentMode.Add, new[] { shipIds[2] }));
            AssertFleetState(started, empireId, FleetKey, expectedShips: 3, expectedName: "QA Patrol Wing",
                expectPatrol: true);

            SubmitAccepted(started, HostPeer, AuthoritativePlayerCommand.SetFleetAssignment(sequence++,
                empireId, FleetKey, AuthoritativeFleetAssignmentMode.Clear, Array.Empty<int>()));
            Assert.IsNull(started.AuthorityUniverse.UState.GetEmpireById(empireId).GetFleetOrNull(FleetKey));
            Assert.IsNull(started.Clients[0].Universe.UState.GetEmpireById(empireId).GetFleetOrNull(FleetKey));
            AssertAllCanonicallySynced(started.Session, HostPeer);
        }
        catch (Authoritative4XSyncMismatchException e)
        {
            Assert.Fail("Fleet ops QA scenario desynced: " + FirstFatalDiff(e));
        }
    }

    [TestMethod]
    public void QaEconomySoak_TaxesProductionUpkeepTradeMoneyStaysSynced_Headless()
    {
        using Authoritative4XLobbyStartResult started = StartOnePassiveSession(0x4A0C004, pirates: false,
            turnTimer: 1);
        int empireId = started.EmpireIdForPeer(HostPeer);
        int sequence = 4_000;

        try
        {
            ConfigureScenarioUniverses(started, disableEvents: true);
            Planet authorityHome = started.AuthorityUniverse.UState.GetEmpireById(empireId)
                .GetPlanets().OrderBy(p => p.Id).First();
            foreach (UniverseScreen universe in AllUniverses(started))
            {
                Planet home = universe.UState.GetPlanet(authorityHome.Id);
                home.HasSpacePort = true;
                home.Storage.Prod = 200f;
                home.Storage.Food = 200f;
                home.Owner.Money = 1_000f;
                home.Owner.UpdateNetPlanetIncomes();
            }

            Building building = PickBuildableBuilding(authorityHome);
            IShipDesign ship = PickMobileBuildableShip(started.AuthorityUniverse.UState.GetEmpireById(empireId));
            SubmitAccepted(started, HostPeer, AuthoritativePlayerCommand.SetEmpireBudget(sequence++,
                empireId, taxRate: 0.31f, treasuryGoal: 0.44f, autoTaxes: true));
            SubmitAccepted(started, HostPeer, AuthoritativePlayerCommand.QueueBuilding(sequence++,
                empireId, authorityHome.Id, building.Name));
            SubmitAccepted(started, HostPeer, AuthoritativePlayerCommand.QueueBuild(sequence++,
                empireId, authorityHome.Id, ship.Name));

            for (int i = 0; i < 720; ++i)
            {
                SubmitAccepted(started, HostPeer,
                    AuthoritativePlayerCommand.NoOp(sequence++, empireId),
                    assertEachClient: i % 30 == 0 || i == 719);
                if (i % 60 == 0 || i == 719)
                    AssertEmpireEconomyEqual(started, empireId, $"economy tick {i}");
            }

            AssertPayloadContains(started.Session.LastAuthoritySnapshot, $"E|{empireId}|", "empire economy rows");
            AssertEmpireEconomyEqual(started, empireId, "final economy soak");
        }
        catch (Authoritative4XSyncMismatchException e)
        {
            Assert.Fail("Economy QA scenario desynced: " + FirstFatalDiff(e));
        }
    }

    [TestMethod]
    public void QaTechGatedDesign_HammerheadCostAndModuleAvailabilityReplayFromHostRows_Headless()
    {
        const string DesignName = "QA Hammerhead Tech-Gated Repro";
        using Authoritative4XLobbyStartResult started = StartOnePassiveSession(0x4A0C005, pirates: false);
        int empireId = started.EmpireIdForPeer(HostPeer);
        int sequence = 5_000;

        try
        {
            ConfigureScenarioUniverses(started, disableEvents: true);
            ResourceManager.Ships.Delete(DesignName);
            Empire authorityEmpire = started.AuthorityUniverse.UState.GetEmpireById(empireId);
            Empire clientEmpire = started.Clients[0].Universe.UState.GetEmpireById(empireId);
            Planet authorityHome = authorityEmpire.GetPlanets().OrderBy(p => p.Id).First();
            foreach (UniverseScreen universe in AllUniverses(started))
                universe.UState.GetPlanet(authorityHome.Id).HasSpacePort = true;

            (ShipDesign design, string techUid, string gatedModuleUid) =
                BuildTechGatedPlayerDesign(authorityEmpire, DesignName);
            Assert.IsFalse(clientEmpire.HasUnlocked(techUid),
                "The passive client must start without the host-only tech unlock.");
            Assert.IsFalse(clientEmpire.IsModuleUnlocked(gatedModuleUid),
                "The passive client must start with the Hammerhead-like module unavailable.");

            authorityEmpire.UnlockTech(techUid, TechUnlockType.Normal, null);
            Assert.IsTrue(authorityEmpire.HasUnlocked(techUid), $"Authority failed to unlock {techUid}.");
            Assert.IsTrue(authorityEmpire.IsModuleUnlocked(gatedModuleUid),
                $"Authority failed to unlock gated module {gatedModuleUid}.");
            Assert.IsTrue(authorityEmpire.WeCanBuildThis(design, debug: true),
                "The host must be able to build the submitted tech-gated design.");

            SubmitAccepted(started, HostPeer, AuthoritativePlayerCommand.DesignShip(sequence++,
                empireId, design.GetBase64DesignString()));
            AssertPayloadContains(started.Session.LastAuthoritySnapshot, $"U|{empireId}|{techUid}|",
                "host-only tech unlock rows");
            AssertPayloadContains(started.Session.LastAuthoritySnapshot, DesignName,
                "player design rows");

            Assert.IsTrue(clientEmpire.HasUnlocked(techUid),
                "Passive client must take the tech unlock from the host U rows.");
            Assert.IsTrue(clientEmpire.IsModuleUnlocked(gatedModuleUid),
                "Passive client module availability must derive from the replayed tech unlock.");
            AssertDesignCostAndAvailabilityEqual(authorityEmpire, clientEmpire, DesignName, gatedModuleUid);

            SubmitAccepted(started, HostPeer, AuthoritativePlayerCommand.QueueBuild(sequence++,
                empireId, authorityHome.Id, DesignName));
            QueueItem authorityItem = LastQueuedShip(started.AuthorityUniverse.UState.GetPlanet(authorityHome.Id),
                DesignName);
            QueueItem clientItem = LastQueuedShip(started.Clients[0].Universe.UState.GetPlanet(authorityHome.Id),
                DesignName);
            Assert.AreEqual(BitConverter.SingleToInt32Bits(authorityItem.Cost),
                BitConverter.SingleToInt32Bits(clientItem.Cost),
                "Queued design COST must be byte-identical after authoritative replay.");
        }
        catch (Authoritative4XSyncMismatchException e)
        {
            Assert.Fail("Tech-gated design QA scenario desynced: " + FirstFatalDiff(e));
        }
        finally
        {
            ResourceManager.Ships.Delete(DesignName);
        }
    }

    [TestMethod]
    public void QaPirateEventResync_DirectorPaymentDoesNotDuplicateAfterReload_Headless()
    {
        using Authoritative4XLobbyStartResult started = StartOnePassiveSession(0x4A0C006, pirates: true,
            disableResearchStations: true, disableMiningOps: true, turnTimer: 1);
        int empireId = started.EmpireIdForPeer(HostPeer);
        int sequence = 6_000;
        string temp = Path.Combine(Path.GetTempPath(), "stardrive-qaharness-pirates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            ConfigureScenarioUniverses(started, disableEvents: false);
            Empire victim = started.AuthorityUniverse.UState.GetEmpireById(empireId);
            Empire pirate = FindPirateEmpire(started.AuthorityUniverse);
            Assert.IsNotNull(pirate, "Pirate-resync QA requires a generated pirate faction.");

            for (int i = 0; i < 240 && PiratePaymentDirectorCount(pirate, victim) == 0; ++i)
                SubmitAccepted(started, HostPeer,
                    AuthoritativePlayerCommand.NoOp(sequence++, empireId),
                    assertEachClient: i % 30 == 0);
            Assert.AreEqual(1, PiratePaymentDirectorCount(pirate, victim),
                "Initial pirate planning should create one payment director per victim.");

            pirate.Pirates.PaymentTimers[victim.Id] = 0;
            pirate.Pirates.ThreatLevels[victim.Id] = -1;
            SubmitAccepted(started, HostPeer, AuthoritativePlayerCommand.NoOp(sequence++, empireId));
            Assert.AreEqual(1, PiratePaymentDirectorCount(pirate, victim),
                "Driving the payment demand should not duplicate the director before save.");

            FileInfo save = new(Path.Combine(temp, "pirate-resync.sav"));
            Authoritative4XSessionMetadata metadata = Authoritative4XSessionMetadata.FromGenerated(
                started.GeneratedGame, hostPeerId: HostPeer, localPeerId: HostPeer,
                sessionId: "qa-pirate-resync", startFingerprint: "qa-pirates",
                started.Session.LastAuthoritySnapshot.Tick);
            Authoritative4XSessionSave.Save(started.AuthorityUniverse, save, metadata);

            using Authoritative4XLoadedSession loaded = Authoritative4XSessionSave.Load(save);
            Empire loadedVictim = loaded.Universe.UState.GetEmpireById(empireId);
            Empire loadedPirate = FindPirateEmpire(loaded.Universe);
            Assert.IsNotNull(loadedPirate, "Loaded authoritative save lost its pirate faction.");
            Assert.AreEqual(1, PiratePaymentDirectorCount(loadedPirate, loadedVictim),
                "Resync save/load should preserve exactly one pending pirate payment director.");

            loadedPirate.Pirates.AddGoalDirectorPayment(loadedVictim);
            loadedPirate.Pirates.AddGoalDirectorPayment(loadedVictim);
            Assert.AreEqual(1, PiratePaymentDirectorCount(loadedPirate, loadedVictim),
                "Re-fired pirate event setup after resync must be idempotent, not dialogue x10.");

            var authority = new Authoritative4XAuthority(loaded.Universe,
                humanEmpireIds: loaded.HumanEmpireIds);
            for (int i = 0; i < 120; ++i)
            {
                (AuthoritativeCommandResult result, _) = authority.Process(
                    AuthoritativePlayerCommand.NoOp(sequence++, empireId));
                Assert.IsTrue(result.Accepted, result.Reason);
            }
            Assert.AreEqual(1, PiratePaymentDirectorCount(loadedPirate, loadedVictim),
                "Pirate payment director must stay singular while the reloaded authority advances.");
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    Authoritative4XLobbyStartResult StartOnePassiveSession(int seed, bool pirates,
        bool disableResearchStations = true, bool disableMiningOps = true, int turnTimer = 5)
    {
        LoadAllGameData();
        IEmpireData race = ResourceManager.MajorRaces
            .Where(r => !r.IsFactionOrMinorRace)
            .OrderBy(RacePreference, StringComparer.Ordinal)
            .First();

        var settings = new Authoritative4XGameSettings
        {
            GenerationSeed = seed,
            GalaxySize = GalSize.Tiny,
            StarsCount = RaceDesignScreen.StarsAbundance.Rare,
            Mode = RaceDesignScreen.GameMode.Sandbox,
            Difficulty = GameDifficulty.Normal,
            NumOpponents = 1,
            Pace = 1f,
            TurnTimer = turnTimer,
            GameSpeed = 1f,
            StartPaused = false,
            DisablePirates = !pirates,
            DisableResearchStations = disableResearchStations,
            DisableMiningOps = disableMiningOps,
            DisableRemnantStory = true,
        };

        var lobby = new Authoritative4XLobby(HostPeer, "Host");
        Assert.IsTrue(lobby.SetSettings(HostPeer, settings).Valid);
        Assert.IsTrue(lobby.SetPlayerSelection(HostPeer, RacePreference(race), Array.Empty<string>()).Valid);
        Assert.IsTrue(lobby.SetReady(HostPeer, true).Valid);
        Assert.IsTrue(lobby.CanStart().Valid, lobby.CanStart().Reason);

        Authoritative4XLobbyStartResult started = lobby.StartInProcess();
        Assert.AreEqual(1, started.Clients.Length,
            "The QA scenario harness expects one passive in-process client replica.");
        return started;
    }

    static void ConfigureScenarioUniverses(Authoritative4XLobbyStartResult started, bool disableEvents)
    {
        foreach (UniverseScreen universe in AllUniverses(started))
        {
            universe.UState.Events.Disabled = disableEvents;
            universe.UState.NoEliminationVictory = true;
            universe.UState.Objects.EnableParallelUpdate = false;
            universe.UState.Paused = false;
            universe.UState.GameSpeed = 1f;
        }
    }

    static UniverseScreen[] AllUniverses(Authoritative4XLobbyStartResult started)
        => started.Clients.Select(c => c.Universe).Prepend(started.AuthorityUniverse).ToArray();

    static Empire FirstEnemyMajor(UniverseScreen universe, Empire player)
        => universe.UState.MajorEmpires
            .Where(e => e != null && e.Id != player.Id && !e.IsFaction)
            .OrderBy(e => e.Id)
            .First();

    static Empire FindPirateEmpire(UniverseScreen universe)
        => universe.UState.Empires
            .Where(e => e?.WeArePirates == true)
            .OrderBy(e => e.Id)
            .FirstOrDefault();

    static void SubmitAccepted(Authoritative4XLobbyStartResult started, int peer,
        AuthoritativePlayerCommand command, bool assertEachClient = true)
    {
        started.Session.SubmitFromClient(peer, command);
        AssertAccepted(started.Session, peer);
        if (assertEachClient)
            AssertAllCanonicallySynced(started.Session, peer);
    }

    static void AssertAccepted(Authoritative4XInProcessMultiClientSession session, int peer)
    {
        Assert.IsTrue(session.LastResultFor(peer).Accepted, session.LastResultFor(peer).Reason);
    }

    static void AssertAllCanonicallySynced(Authoritative4XInProcessMultiClientSession session,
        params int[] peers)
    {
        foreach (int peer in peers)
        {
            AuthoritativeStateSnapshot authority = session.LastAuthoritySnapshot;
            AuthoritativeStateSnapshot client = session.LastClientSnapshotFor(peer);
            Assert.AreEqual(authority.SyncDigest, client.SyncDigest,
                AuthoritativeStateSnapshot.FirstFatalPayloadDifferenceForLog(authority.Payload, client.Payload));
            Assert.AreEqual(authority.TransformDigest, client.TransformDigest,
                AuthoritativeStateSnapshot.FirstFatalPayloadDifferenceForLog(authority.Payload, client.Payload));
        }
    }

    static void AssertAllSnapshotsMatch(Authoritative4XLobbyStartResult started,
        AuthoritativeStateSnapshot authority)
    {
        foreach (Authoritative4XClientSpec client in started.Clients)
        {
            AuthoritativeStateSnapshot replica = AuthoritativeStateSnapshot.Capture(client.Universe, authority.Tick);
            Assert.AreEqual(authority.SyncDigest, replica.SyncDigest,
                AuthoritativeStateSnapshot.FirstFatalPayloadDifferenceForLog(authority.Payload, replica.Payload));
            Assert.AreEqual(authority.TransformDigest, replica.TransformDigest,
                AuthoritativeStateSnapshot.FirstFatalPayloadDifferenceForLog(authority.Payload, replica.Payload));
        }
    }

    static string FirstFatalDiff(Authoritative4XSyncMismatchException e)
        => AuthoritativeStateSnapshot.FirstFatalPayloadDifferenceForLog(
            e.AuthoritySnapshot?.Payload, e.ClientSnapshot?.Payload);

    static void AssertPayloadContains(AuthoritativeStateSnapshot snapshot, string text, string label)
    {
        StringAssert.Contains(snapshot.Payload, text,
            $"Authoritative snapshot should contain {label} ({text}).");
    }

    sealed class GroundFixture
    {
        public PlanetGridSquare EnemyPlanetInvaderTile;
        public PlanetGridSquare EnemyPlanetBuildingTile;
        public PlanetGridSquare HomeBuildingTile;
        public PlanetGridSquare HomeEnemyTile;
        public Building AuthorityEnemyBuilding;
        public Building ClientEnemyBuilding;
        public string InvaderName = "";
    }

    static GroundFixture PrepareGroundCombatFixture(Authoritative4XLobbyStartResult started,
        int playerEmpireId, int enemyEmpireId, int playerPlanetId, int enemyPlanetId)
    {
        GroundFixture fixture = null;
        foreach (UniverseScreen universe in AllUniverses(started))
        {
            Empire player = universe.UState.GetEmpireById(playerEmpireId);
            Empire enemy = universe.UState.GetEmpireById(enemyEmpireId);
            MakeAtWar(player, enemy);

            Planet enemyPlanet = universe.UState.GetPlanet(enemyPlanetId);
            Planet playerPlanet = universe.UState.GetPlanet(playerPlanetId);
            PlanetGridSquare[] enemyTiles = PrepareGroundTroopTiles(enemyPlanet);
            PlanetGridSquare[] homeTiles = PrepareGroundTroopTiles(playerPlanet);

            Troop invader = PlaceGroundTroop(enemyPlanet, player, enemyTiles[0]);
            Building enemyBuilding = PlaceCombatBuilding(enemyPlanet, enemyTiles[1]);
            Troop homeEnemy = PlaceGroundTroop(playerPlanet, enemy, homeTiles[1]);
            Building homeBuilding = PlaceCombatBuilding(playerPlanet, homeTiles[0]);
            Assert.IsTrue(homeBuilding.CanAttack);
            CombatScreen.StartCombat(invader, enemyBuilding, enemyTiles[1], enemyPlanet);
            enemyPlanet.SetInGroundCombat(player);
            CombatScreen.StartCombat(homeBuilding, homeEnemy, homeTiles[1], playerPlanet);
            playerPlanet.SetInGroundCombat(player);

            if (fixture == null)
            {
                fixture = new GroundFixture
                {
                    EnemyPlanetInvaderTile = enemyTiles[0],
                    EnemyPlanetBuildingTile = enemyTiles[1],
                    HomeBuildingTile = homeTiles[0],
                    HomeEnemyTile = homeTiles[1],
                    AuthorityEnemyBuilding = enemyBuilding,
                    InvaderName = invader.Name,
                };
            }
            else
            {
                fixture.ClientEnemyBuilding = enemyBuilding;
            }
        }

        Assert.IsNotNull(fixture);
        Assert.IsNotNull(fixture.ClientEnemyBuilding);
        return fixture;
    }

    static PlanetGridSquare[] PrepareGroundTroopTiles(Planet planet)
    {
        planet.TilesList.Clear();
        int width = SolarSystemBody.TileMaxX;
        int height = SolarSystemBody.TileMaxY;
        var tiles = new List<PlanetGridSquare>(width * height);
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                var tile = new PlanetGridSquare(planet, x, y, b: null,
                    hab: true, terraformable: false);
                planet.TilesList.Add(tile);
                tiles.Add(tile);
            }
        }
        planet.RefreshBuildingsWeCanBuildHere();
        return tiles.ToArray();
    }

    static Troop PlaceGroundTroop(Planet planet, Empire empire, PlanetGridSquare tile)
    {
        Troop template = ResourceManager.GetTroopTemplatesFor(empire)
            .OrderBy(t => t.ActualCost(empire))
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(template, $"Empire {empire.Id} needs a buildable troop template.");
        Assert.IsTrue(ResourceManager.TryCreateTroop(template.Name, empire, out Troop troop),
            $"Could not create troop {template.Name} for empire {empire.Id}.");
        Assert.IsTrue(tile.IsTileFree(empire),
            $"Planet {planet.Id} tile {tile.X},{tile.Y} should be free for empire {empire.Id}.");
        planet.AddTroop(troop, tile);
        Assert.IsTrue(tile.TroopsHere.ContainsRef(troop),
            $"Could not seed troop {troop.Name} on planet {planet.Id} tile {tile.X},{tile.Y}.");
        troop.AvailableMoveActions = troop.MaxStoredActions;
        troop.AvailableAttackActions = troop.MaxStoredActions;
        troop.MoveTimer = 0f;
        troop.AttackTimer = 0f;
        return troop;
    }

    static Building PlaceCombatBuilding(Planet planet, PlanetGridSquare tile)
    {
        Building template = ResourceManager.BuildingsDict.FilterValues(b => b.CombatStrength > 0)
            .OrderBy(b => b.ActualCost(planet.Owner))
            .ThenBy(b => b.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(template, "Ground combat QA needs an attackable building template.");
        Building building = ResourceManager.CreateBuilding(planet, template.Name);
        building.AvailableAttackActions = 1;
        tile.PlaceBuilding(building, planet);
        Assert.IsTrue(tile.CombatBuildingOnTile);
        return building;
    }

    static void MakeAtWar(Empire a, Empire b)
    {
        if (!a.IsAtWarWith(b))
            a.AI.DeclareWarOn(b, WarType.BorderConflict);
        Assert.IsTrue(a.IsAtWarWith(b), $"Empire {a.Id} should be at war with {b.Id}.");
        Assert.IsTrue(b.IsAtWarWith(a), $"Empire {b.Id} should be at war with {a.Id}.");
    }

    static int[] SpawnFleetCombatFixture(Authoritative4XLobbyStartResult started,
        int playerEmpireId, int enemyEmpireId, int shipsPerSide, out int[] enemyShipIds)
    {
        foreach (UniverseScreen universe in AllUniverses(started))
            MakeAtWar(universe.UState.GetEmpireById(playerEmpireId), universe.UState.GetEmpireById(enemyEmpireId));

        string design = PickCombatDesign();
        var playerIds = new int[shipsPerSide];
        var enemyIds = new int[shipsPerSide];
        for (int i = 0; i < shipsPerSide; ++i)
        {
            Vector2 playerPos = new(-6_000f, (i - shipsPerSide / 2f) * 450f);
            Vector2 enemyPos = new(6_000f, (i - shipsPerSide / 2f) * 450f);
            int expectedPlayerId = 0;
            int expectedEnemyId = 0;

            foreach (UniverseScreen universe in AllUniverses(started))
            {
                Ship player = Ship.CreateShipAtPoint(universe.UState, design,
                    universe.UState.GetEmpireById(playerEmpireId), playerPos);
                Ship enemy = Ship.CreateShipAtPoint(universe.UState, design,
                    universe.UState.GetEmpireById(enemyEmpireId), enemyPos);
                Assert.IsNotNull(player);
                Assert.IsNotNull(enemy);
                player.Rotation = 0f;
                enemy.Rotation = MathF.PI;
                if (expectedPlayerId == 0)
                {
                    expectedPlayerId = player.Id;
                    expectedEnemyId = enemy.Id;
                }
                else
                {
                    Assert.AreEqual(expectedPlayerId, player.Id);
                    Assert.AreEqual(expectedEnemyId, enemy.Id);
                }
            }

            playerIds[i] = expectedPlayerId;
            enemyIds[i] = expectedEnemyId;
        }

        foreach (UniverseScreen universe in AllUniverses(started))
            universe.UState.Objects.UpdateLists(removeInactiveObjects: false);

        for (int i = 0; i < shipsPerSide; ++i)
        {
            foreach (UniverseScreen universe in AllUniverses(started))
            {
                Ship player = universe.UState.Objects.FindShip(playerIds[i]);
                Ship enemy = universe.UState.Objects.FindShip(enemyIds[i]);
                player.AI.OrderAttackSpecificTarget(enemy);
                enemy.AI.OrderAttackSpecificTarget(player);
            }
        }

        enemyShipIds = enemyIds;
        return playerIds;
    }

    static int[] SpawnMatchingShips(Authoritative4XLobbyStartResult started, int empireId,
        string design, Vector2 origin, int count)
    {
        var ids = new int[count];
        for (int i = 0; i < count; ++i)
        {
            int expectedId = 0;
            foreach (UniverseScreen universe in AllUniverses(started))
            {
                Ship ship = Ship.CreateShipAtPoint(universe.UState, design,
                    universe.UState.GetEmpireById(empireId), origin + new Vector2(i * 1_500f, 0f));
                Assert.IsNotNull(ship);
                Assert.IsTrue(ship.CanBeAddedToFleets(), $"Ship {ship.Id} should be fleet-assignable.");
                if (expectedId == 0)
                    expectedId = ship.Id;
                else
                    Assert.AreEqual(expectedId, ship.Id);
            }
            ids[i] = expectedId;
        }

        foreach (UniverseScreen universe in AllUniverses(started))
            universe.UState.Objects.UpdateLists(removeInactiveObjects: false);
        return ids;
    }

    static string PickCombatDesign()
    {
        if (ResourceManager.ShipTemplateExists("Fang Strafer"))
            return "Fang Strafer";
        if (ResourceManager.ShipTemplateExists("Vulcan Scout"))
            return "Vulcan Scout";
        return ResourceManager.Ships.Designs
            .Where(d => d?.Role is RoleName.scout or RoleName.fighter or RoleName.corvette)
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .Select(d => d.Name)
            .First();
    }

    static void AssertFleetState(Authoritative4XLobbyStartResult started, int empireId,
        int fleetKey, int expectedShips, string expectedName, bool expectPatrol = false)
    {
        Fleet authority = started.AuthorityUniverse.UState.GetEmpireById(empireId).GetFleetOrNull(fleetKey);
        Fleet client = started.Clients[0].Universe.UState.GetEmpireById(empireId).GetFleetOrNull(fleetKey);
        Assert.IsNotNull(authority);
        Assert.IsNotNull(client);
        Assert.AreEqual(expectedShips, authority.Ships.Count);
        Assert.AreEqual(expectedShips, client.Ships.Count);
        Assert.AreEqual(authority.DataNodes.Count, client.DataNodes.Count);
        Assert.AreEqual(authority.HasPatrolPlan, client.HasPatrolPlan);
        Assert.AreEqual(expectPatrol, authority.HasPatrolPlan);
        if (expectedName != null)
        {
            Assert.AreEqual(expectedName, authority.Name);
            Assert.AreEqual(expectedName, client.Name);
        }

        Assert.AreEqual(
            started.AuthorityUniverse.UState.GetEmpireById(empireId).AllFleets.Count(f => f.Ships.Count > 0),
            started.Clients[0].Universe.UState.GetEmpireById(empireId).AllFleets.Count(f => f.Ships.Count > 0),
            "Passive client must not invent/drop fleets while opening/editing fleets.");
        AssertPayloadContains(started.Session.LastAuthoritySnapshot, $"F|{empireId}|{fleetKey}|",
            "fleet runtime rows");
        AssertAllCanonicallySynced(started.Session, HostPeer);
    }

    static WayPoint[] TestPatrolWaypoints(Vector2 origin)
        => new[]
        {
            new WayPoint(origin + new Vector2(4_000f, 0f), new Vector2(1f, 0f)),
            new WayPoint(origin + new Vector2(4_000f, 4_000f), new Vector2(0f, 1f)),
        };

    static Building PickBuildableBuilding(Planet planet)
    {
        planet.RefreshBuildingsWeCanBuildHere();
        Building building = planet.GetBuildingsCanBuild()
            .Where(b => !b.IsBiospheres)
            .OrderBy(b => b.ActualCost(planet.Owner))
            .ThenBy(b => b.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(building, $"Planet {planet.Id} needs a buildable building.");
        return building;
    }

    static IShipDesign PickMobileBuildableShip(Empire empire)
    {
        IShipDesign ship = empire.ShipsWeCanBuildSnapshot
            .Where(s => !s.IsPlatformOrStation && !s.IsShipyard)
            .OrderBy(s => s.BaseCost)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(ship, $"Empire {empire.Id} needs a mobile buildable ship.");
        return ship;
    }

    static IShipDesign PickFleetBuildableShip(Empire empire)
    {
        IShipDesign ship = empire.ShipsWeCanBuildSnapshot
            .Where(s => !s.IsPlatformOrStation
                        && !s.IsShipyard
                        && !s.IsColonyShip
                        && !s.IsFreighter
                        && !s.IsConstructor
                        && !s.IsTroopShip
                        && !s.IsSingleTroopShip)
            .OrderBy(s => s.BaseCost)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(ship, $"Empire {empire.Id} needs a fleet-assignable buildable ship.");
        return ship;
    }

    static void AssertEmpireEconomyEqual(Authoritative4XLobbyStartResult started,
        int empireId, string label)
    {
        Empire authority = started.AuthorityUniverse.UState.GetEmpireById(empireId);
        Empire client = started.Clients[0].Universe.UState.GetEmpireById(empireId);
        Assert.AreEqual(BitConverter.SingleToInt32Bits(authority.Money),
            BitConverter.SingleToInt32Bits(client.Money), label + " money bits");
        Assert.AreEqual(BitConverter.SingleToInt32Bits(authority.data.TaxRate),
            BitConverter.SingleToInt32Bits(client.data.TaxRate), label + " tax bits");
        Assert.AreEqual(BitConverter.SingleToInt32Bits(authority.data.treasuryGoal),
            BitConverter.SingleToInt32Bits(client.data.treasuryGoal), label + " treasury goal bits");
        AssertAllCanonicallySynced(started.Session, HostPeer);
    }

    static (ShipDesign Design, string TechUid, string ModuleUid) BuildTechGatedPlayerDesign(
        Empire empire, string name)
    {
        ShipDesign clone = BuildLegalPlayerDesign(empire, name);
        DesignSlot[] slots = clone.GetOrLoadDesignSlots().Select(s => new DesignSlot(s)).ToArray();

        for (int i = 0; i < slots.Length; ++i)
        {
            DesignSlot slot = slots[i];
            if (slot == null || string.IsNullOrEmpty(slot.ModuleUID))
                continue;

            foreach (TechEntry tech in empire.TechEntries
                         .Where(t => t != null && t != TechEntry.None && !t.Unlocked)
                         .OrderBy(t => t.UID, StringComparer.Ordinal))
            {
                foreach (Technology.UnlockedMod unlocked in tech.GetUnlockableModules(empire)
                             .OrderBy(m => m.ModuleUID, StringComparer.Ordinal))
                {
                    if (!ResourceManager.GetModuleTemplate(unlocked.ModuleUID, out ShipModule module)
                        || empire.IsModuleUnlocked(module.UID)
                        || module.GetOrientedSize(slot.ModuleRot) != slot.Size)
                    {
                        continue;
                    }

                    slots[i] = new DesignSlot(slot.Pos, module.UID, slot.Size,
                        slot.TurretAngle, slot.ModuleRot, slot.HangarShipUID);
                    clone.SetDesignSlots(slots);
                    clone.IsPlayerDesign = true;
                    clone.IsReadonlyDesign = false;
                    Assert.IsTrue(clone.UniqueModuleUIDs.Contains(module.UID));
                    return (clone, tech.UID, module.UID);
                }
            }
        }

        throw new AssertFailedException("Could not build a Hammerhead-like tech-gated design from loaded content.");
    }

    static ShipDesign BuildLegalPlayerDesign(Empire empire, string name)
    {
        ShipDesign source = empire.ShipsWeCanBuildSnapshot
            .OfType<ShipDesign>()
            .Where(d => !d.IsPlatformOrStation
                        && d.IsValidDesign
                        && d.NumDesignSlots > 0
                        && d.UniqueModuleUIDs.All(empire.IsModuleUnlocked))
            .OrderBy(d => d.BaseCost)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(source, "The test empire needs a legal mobile design to clone.");
        source.GetOrLoadDesignSlots();

        ShipDesign clone = source.GetClone(name);
        clone.IsPlayerDesign = true;
        clone.IsReadonlyDesign = false;
        return clone;
    }

    static void AssertDesignCostAndAvailabilityEqual(Empire authorityEmpire, Empire clientEmpire,
        string designName, string gatedModuleUid)
    {
        Assert.IsTrue(ResourceManager.Ships.GetDesign(designName, out IShipDesign authorityDesign));
        IShipDesign clientDesign = clientEmpire.ShipsWeCanBuildSnapshot
            .FirstOrDefault(d => string.Equals(d.Name, designName, StringComparison.Ordinal));
        Assert.IsNotNull(clientDesign,
            "Passive client should register the accepted player design from authoritative D rows.");
        Assert.IsTrue(authorityEmpire.CanBuildShip(authorityDesign));
        Assert.IsTrue(clientEmpire.CanBuildShip(clientDesign));
        Assert.IsTrue(clientEmpire.WeCanBuildThis(clientDesign, debug: true));
        Assert.IsTrue(clientEmpire.IsModuleUnlocked(gatedModuleUid));

        float authorityCost = authorityDesign.GetCost(authorityEmpire);
        float clientCost = clientDesign.GetCost(clientEmpire);
        Assert.AreEqual(BitConverter.SingleToInt32Bits(authorityCost),
            BitConverter.SingleToInt32Bits(clientCost),
            $"Design cost drifted for {designName}: authority={authorityCost} client={clientCost}");
        Assert.IsTrue(authorityCost > 3f,
            "The Hammerhead repro guard should catch the live cost=3 style underpricing.");
        CollectionAssert.AreEqual(
            authorityDesign.UniqueModuleUIDs.Select(authorityEmpire.IsModuleUnlocked).ToArray(),
            clientDesign.UniqueModuleUIDs.Select(clientEmpire.IsModuleUnlocked).ToArray(),
            "Module availability flags must match between authority and passive client.");
    }

    static QueueItem LastQueuedShip(Planet planet, string designName)
    {
        QueueItem item = planet.Construction.GetConstructionQueueSnapshot()
            .LastOrDefault(q => q.isShip
                                && string.Equals(q.ShipData?.Name, designName, StringComparison.Ordinal));
        Assert.IsNotNull(item, $"Planet {planet.Id} did not queue ship {designName}.");
        return item;
    }

    static int PiratePaymentDirectorCount(Empire pirate, Empire victim)
    {
        int victimId = victim?.Id ?? 0;
        return pirate?.AI?.Goals?.Count(g => g.Type == GoalType.PirateDirectorPayment
                                             && g.TargetEmpire?.Id == victimId) ?? 0;
    }

    static string RacePreference(IEmpireData race)
        => !string.IsNullOrEmpty(race.ArchetypeName) ? race.ArchetypeName : race.Name;
}

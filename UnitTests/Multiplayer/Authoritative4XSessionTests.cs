using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDGraphics;
using SDLockstep;
using Ship_Game;
using Ship_Game.AI;
using Ship_Game.Data;
using Ship_Game.GameScreens.DiplomacyScreen;
using Ship_Game.Gameplay;
using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.Ships;
using Ship_Game.Universe;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnitTests.Multiplayer;

[TestClass]
public class Authoritative4XSessionTests : StarDriveTest
{
    sealed class BuiltWorld
    {
        public UniverseScreen Screen;
        public UniverseState UState;
        public Empire Player;
        public Empire Enemy;
        public Planet Planet;
        public Planet EnemyPlanet;
        public Ship Ship;
        public string ResearchUid;
    }

    [TestMethod]
    public void Authoritative4XMessages_RoundTripThroughTransportCodec_Headless()
    {
        var request = AuthoritativePlayerCommand.MoveShip(7, 2, 99, new Vector2(1234.5f, -6789.25f))
            .ToMessage(fromPeer: 2);

        DecodedLockstepMessage decoded = LockstepMessageCodec.Decode(
            LockstepMessageCodec.Encode(request, toPeer: 1));

        Assert.AreEqual(1, decoded.ToPeer);
        var copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual(2, copy.FromPeer);
        Assert.AreEqual(request.Sequence, copy.Sequence);
        Assert.AreEqual(request.EmpireId, copy.EmpireId);
        Assert.AreEqual(request.Kind, copy.Kind);
        Assert.AreEqual(request.SubjectId, copy.SubjectId);
        Assert.AreEqual(request.X, copy.X);
        Assert.AreEqual(request.Y, copy.Y);

        var designRequest = AuthoritativePlayerCommand.DesignShip(8, 2, "BASE64-DESIGN")
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(designRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.DesignShip, copy.Kind);
        Assert.AreEqual("BASE64-DESIGN", copy.Text);

        var queueRequest = AuthoritativePlayerCommand.QueueBuild(9, 2, 123, "MP Frigate")
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(queueRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.QueueBuild, copy.Kind);
        Assert.AreEqual(123, copy.SubjectId);
        Assert.AreEqual("MP Frigate", copy.Text);

        var snapshot = new AuthoritativeStateSnapshotMessage
        {
            FromPeer = 1,
            Tick = 42,
            HashLo = 0x1111,
            HashHi = 0x2222,
            SyncDigest = "0x3333",
            Payload = "payload"
        };
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(snapshot, toPeer: 2));
        var snapshotCopy = (AuthoritativeStateSnapshotMessage)decoded.Message;
        Assert.AreEqual(snapshot.Tick, snapshotCopy.Tick);
        Assert.AreEqual(snapshot.HashLo, snapshotCopy.HashLo);
        Assert.AreEqual(snapshot.HashHi, snapshotCopy.HashHi);
        Assert.AreEqual(snapshot.SyncDigest, snapshotCopy.SyncDigest);
        Assert.AreEqual(snapshot.Payload, snapshotCopy.Payload);

        var result = new AuthoritativeCommandResultMessage
        {
            FromPeer = 1,
            Sequence = 88,
            OriginPeer = 3,
            Accepted = true,
            Tick = 99,
            Reason = "ok"
        };
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(result, toPeer: 4));
        var resultCopy = (AuthoritativeCommandResultMessage)decoded.Message;
        Assert.AreEqual(result.Sequence, resultCopy.Sequence);
        Assert.AreEqual(result.OriginPeer, resultCopy.OriginPeer);
        Assert.AreEqual(result.Accepted, resultCopy.Accepted);
        Assert.AreEqual(result.Tick, resultCopy.Tick);
        Assert.AreEqual(result.Reason, resultCopy.Reason);

        var popup = new AuthoritativeDiplomacyPopupMessage
        {
            FromPeer = 1,
            ProposalId = 9,
            ProposerEmpireId = 2,
            TargetEmpireId = 3,
            ProposalType = (byte)AuthoritativeDiplomacyProposalType.Peace,
            Terms = "ceasefire",
            RequiresResponse = true,
            Message = "Diplomacy proposal received."
        };
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(popup, toPeer: 3));
        var popupCopy = (AuthoritativeDiplomacyPopupMessage)decoded.Message;
        Assert.AreEqual(3, decoded.ToPeer);
        Assert.AreEqual(popup.ProposalId, popupCopy.ProposalId);
        Assert.AreEqual(popup.ProposerEmpireId, popupCopy.ProposerEmpireId);
        Assert.AreEqual(popup.TargetEmpireId, popupCopy.TargetEmpireId);
        Assert.AreEqual(popup.ProposalType, popupCopy.ProposalType);
        Assert.AreEqual(popup.Terms, popupCopy.Terms);
        Assert.AreEqual(popup.RequiresResponse, popupCopy.RequiresResponse);
        Assert.AreEqual(popup.Message, popupCopy.Message);
    }

    [TestMethod]
    public void Authoritative4XSession_AppliesCommandRequestsAndSyncsClient_Headless()
    {
        const ulong Seed = 0x5A17D21UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            var move = AuthoritativePlayerCommand.MoveShip(1, authority.Player.Id, authority.Ship.Id,
                authority.Ship.Position + new Vector2(25_000, 10_000));
            session.SubmitFromClient(move);
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(AIState.AwaitingOrders, authority.Ship.AI.State);
            Assert.AreEqual(move.Position, authority.Ship.AI.MovePosition);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            var colony = AuthoritativePlayerCommand.SetColonyType(2, authority.Player.Id, authority.Planet.Id,
                Planet.ColonyType.Research);
            session.SubmitFromClient(colony);
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(Planet.ColonyType.Research, authority.Planet.CType);
            Assert.AreEqual(Planet.ColonyType.Research, client.Planet.CType);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            var research = AuthoritativePlayerCommand.SetResearchTopic(3, authority.Player.Id, authority.ResearchUid);
            session.SubmitFromClient(research);
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(authority.ResearchUid, authority.Player.Research.Topic);
            Assert.AreEqual(client.ResearchUid, client.Player.Research.Topic);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must change after real authoritative commands mutate the game state.");

            var illegal = AuthoritativePlayerCommand.SetColonyType(4, authority.Enemy.Id, authority.Planet.Id,
                Planet.ColonyType.Military);
            session.SubmitFromClient(illegal);
            Assert.IsFalse(session.LastResult.Accepted, "Enemy must not be able to mutate the player's planet.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(Planet.ColonyType.Research, authority.Planet.CType);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XShipyard_DesignAndQueueBuildCommandsSync_Headless()
    {
        const ulong Seed = 0x51F7A11UL;
        const string LegalName = "Authoritative MP Test Scout";
        const string IllegalName = "Authoritative MP Locked Module Test";
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            ResourceManager.Ships.Delete(LegalName);
            ResourceManager.Ships.Delete(IllegalName);
            authority.Planet.HasSpacePort = true;
            client.Planet.HasSpacePort = true;

            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            ShipDesign legal = BuildLegalPlayerDesign(authority.Player, LegalName);

            session.SubmitFromClient(AuthoritativePlayerCommand.DesignShip(10, authority.Player.Id,
                legal.GetBase64DesignString()));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.Player.CanBuildShip(LegalName),
                "The authority empire should register the submitted legal design as buildable.");
            Assert.IsTrue(client.Player.CanBuildShip(LegalName),
                "The client replica should apply the accepted design registration deterministically.");
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, LegalName);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeQueueDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.QueueBuild(11, authority.Player.Id,
                authority.Planet.Id, LegalName));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.Planet.Construction.ContainsShipDesignName(LegalName));
            Assert.IsTrue(client.Planet.Construction.ContainsShipDesignName(LegalName));
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, $"|{LegalName}|");
            Assert.AreNotEqual(beforeQueueDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover real planet queue entries, not just command acks.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            ShipDesign illegal = BuildIllegalLockedModuleDesign(authority.Player, IllegalName);
            session.SubmitFromClient(AuthoritativePlayerCommand.DesignShip(12, authority.Player.Id,
                illegal.GetBase64DesignString()));
            Assert.IsFalse(session.LastResult.Accepted, "A design containing locked modules must be rejected.");
            Assert.IsFalse(authority.Player.CanBuildShip(IllegalName));
            Assert.IsFalse(client.Player.CanBuildShip(IllegalName));
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueBuild(13, authority.Player.Id,
                authority.EnemyPlanet.Id, LegalName));
            Assert.IsFalse(session.LastResult.Accepted, "A player must not queue builds at another empire's planet.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueBuild(14, authority.Player.Id,
                authority.Planet.Id, IllegalName));
            Assert.IsFalse(session.LastResult.Accepted, "An unregistered or unbuildable design must not enter the queue.");
            Assert.IsFalse(authority.Planet.Construction.ContainsShipDesignName(IllegalName));
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            ResourceManager.Ships.Delete(LegalName);
            ResourceManager.Ships.Delete(IllegalName);
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XNetworkTcpLoopback_AppliesCommandsAndSyncsReplica_Headless()
    {
        const ulong Seed = 0x4E7C0DEUL;
        const int Peer = 2;
        const string NetworkDesignName = "Authoritative TCP Test Scout";
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            ResourceManager.Ships.Delete(NetworkDesignName);
            authority.UState.GetPlanet(authority.Planet.Id).HasSpacePort = true;
            client.UState.GetPlanet(authority.Planet.Id).HasSpacePort = true;
            int port = FreeTcpPort();

            TcpLockstepTransport hostTransport = TcpLockstepTransport.Host(port, Peer);
            TcpLockstepTransport clientTransport = TcpLockstepTransport.Join("127.0.0.1", port,
                Authoritative4XNetworkHost.HostPeerId);
            Assert.IsTrue(hostTransport.WaitForConnection(TimeSpan.FromSeconds(3)),
                "Authoritative TCP host did not accept the loopback client.");
            Assert.IsTrue(clientTransport.IsConnected, "Authoritative TCP client did not connect.");

            using var host = new Authoritative4XNetworkHost(authority.Screen, hostTransport,
                new Dictionary<int, int> { [Peer] = authority.Player.Id },
                new[] { authority.Player.Id });
            using var networkClient = new Authoritative4XNetworkClient(client.Screen, clientTransport, Peer,
                new[] { client.Player.Id });

            networkClient.Submit(AuthoritativePlayerCommand.SetColonyType(200, authority.Player.Id,
                authority.Planet.Id, Planet.ColonyType.Research));
            PumpTcpUntil(() => NetworkClientCaughtUp(networkClient, 200), host, networkClient);
            Assert.IsTrue(networkClient.LastResult.Accepted, networkClient.LastResult.Reason);
            Assert.AreEqual(Planet.ColonyType.Research, authority.Planet.CType);
            Assert.AreEqual(Planet.ColonyType.Research, client.Planet.CType);
            Assert.AreEqual(networkClient.LastAuthoritySnapshot.SyncDigest,
                networkClient.LastClientSnapshot.SyncDigest);

            ShipDesign legal = BuildLegalPlayerDesign(authority.Player, NetworkDesignName);
            networkClient.Submit(AuthoritativePlayerCommand.DesignShip(201, authority.Player.Id,
                legal.GetBase64DesignString()));
            PumpTcpUntil(() => NetworkClientCaughtUp(networkClient, 201), host, networkClient);
            Assert.IsTrue(networkClient.LastResult.Accepted, networkClient.LastResult.Reason);
            Assert.IsTrue(authority.Player.CanBuildShip(NetworkDesignName));
            Assert.IsTrue(client.Player.CanBuildShip(NetworkDesignName));
            Assert.AreEqual(networkClient.LastAuthoritySnapshot.SyncDigest,
                networkClient.LastClientSnapshot.SyncDigest);

            networkClient.Submit(AuthoritativePlayerCommand.QueueBuild(202, authority.Player.Id,
                authority.Planet.Id, NetworkDesignName));
            PumpTcpUntil(() => NetworkClientCaughtUp(networkClient, 202), host, networkClient);
            Assert.IsTrue(networkClient.LastResult.Accepted, networkClient.LastResult.Reason);
            Assert.IsTrue(authority.Planet.Construction.ContainsShipDesignName(NetworkDesignName));
            Planet clientQueuePlanet = client.UState.GetPlanet(authority.Planet.Id);
            Assert.IsTrue(clientQueuePlanet.Construction.ContainsShipDesignName(NetworkDesignName));
            Assert.AreEqual(networkClient.LastAuthoritySnapshot.SyncDigest,
                networkClient.LastClientSnapshot.SyncDigest);

            networkClient.Submit(AuthoritativePlayerCommand.SetColonyType(203, authority.Enemy.Id,
                authority.Planet.Id, Planet.ColonyType.Military));
            PumpTcpUntil(() => NetworkClientCaughtUp(networkClient, 203), host, networkClient);
            Assert.IsFalse(networkClient.LastResult.Accepted,
                "The TCP host must reject a peer spoofing another empire.");
            StringAssert.Contains(networkClient.LastResult.Reason, "does not control");
            Assert.AreEqual(Peer, networkClient.LastResult.OriginPeer);
            Assert.AreEqual(networkClient.LastAuthoritySnapshot.SyncDigest,
                networkClient.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            ResourceManager.Ships.Delete(NetworkDesignName);
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XNetworkTcpMultiClient_BroadcastsCommandsToEveryReplica_Headless()
    {
        const ulong Seed = 0x4E7C3EEUL;
        const int PeerA = 2;
        const int PeerB = 3;
        const int SharedLocalSequence = 300;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld clientA = BuildWorld(Seed);
        BuiltWorld clientB = BuildWorld(Seed);

        try
        {
            int empireA = authority.Player.Id;
            int empireB = authority.Enemy.Id;
            int port = FreeTcpPort();

            TcpLockstepTransport hostTransport = TcpLockstepTransport.HostMulti(port);
            TcpLockstepTransport clientATransport = TcpLockstepTransport.JoinAsPeer("127.0.0.1", port,
                PeerA, Authoritative4XNetworkHost.HostPeerId);
            TcpLockstepTransport clientBTransport = TcpLockstepTransport.JoinAsPeer("127.0.0.1", port,
                PeerB, Authoritative4XNetworkHost.HostPeerId);
            Assert.IsTrue(hostTransport.WaitForConnections(2, TimeSpan.FromSeconds(3)),
                "Authoritative TCP host did not accept both loopback clients.");
            Assert.IsTrue(WaitForMappedPeers(hostTransport, PeerA, PeerB),
                "Authoritative TCP host did not map both announced client peer ids.");

            using var host = new Authoritative4XNetworkHost(authority.Screen, hostTransport,
                new Dictionary<int, int> { [PeerA] = empireA, [PeerB] = empireB },
                new[] { empireA, empireB });
            using var networkA = new Authoritative4XNetworkClient(clientA.Screen, clientATransport, PeerA,
                new[] { empireA, empireB });
            using var networkB = new Authoritative4XNetworkClient(clientB.Screen, clientBTransport, PeerB,
                new[] { empireA, empireB });

            networkA.Submit(AuthoritativePlayerCommand.SetColonyType(SharedLocalSequence, empireA,
                authority.Planet.Id, Planet.ColonyType.Research));
            PumpTcpUntil(() => NetworkClientCaughtUp(networkA, PeerA, SharedLocalSequence)
                               && NetworkClientCaughtUp(networkB, PeerA, SharedLocalSequence),
                host, networkA, networkB);
            Assert.IsTrue(networkA.LastResult.Accepted, networkA.LastResult.Reason);
            Assert.IsTrue(networkB.LastResult.Accepted, networkB.LastResult.Reason);
            Assert.AreEqual(PeerA, networkA.LastResult.OriginPeer);
            Assert.AreEqual(PeerA, networkB.LastResult.OriginPeer);
            Assert.AreEqual(Planet.ColonyType.Research, authority.Planet.CType);
            Assert.AreEqual(Planet.ColonyType.Research, clientA.UState.GetPlanet(authority.Planet.Id).CType);
            Assert.AreEqual(Planet.ColonyType.Research, clientB.UState.GetPlanet(authority.Planet.Id).CType);
            AssertNetworkSynced(networkA, networkB);

            networkB.Submit(AuthoritativePlayerCommand.SetColonyType(SharedLocalSequence, empireB,
                authority.EnemyPlanet.Id, Planet.ColonyType.Military));
            PumpTcpUntil(() => NetworkClientCaughtUp(networkA, PeerB, SharedLocalSequence)
                               && NetworkClientCaughtUp(networkB, PeerB, SharedLocalSequence),
                host, networkA, networkB);
            Assert.IsTrue(networkA.LastResult.Accepted, networkA.LastResult.Reason);
            Assert.IsTrue(networkB.LastResult.Accepted, networkB.LastResult.Reason);
            Assert.AreEqual(PeerB, networkA.LastResult.OriginPeer,
                "TCP broadcast must preserve the peer that originated a local sequence number.");
            Assert.AreEqual(PeerB, networkB.LastResult.OriginPeer);
            Assert.AreEqual(Planet.ColonyType.Military, authority.EnemyPlanet.CType);
            Assert.AreEqual(Planet.ColonyType.Military, clientA.UState.GetPlanet(authority.EnemyPlanet.Id).CType);
            Assert.AreEqual(Planet.ColonyType.Military, clientB.UState.GetPlanet(authority.EnemyPlanet.Id).CType);
            AssertNetworkSynced(networkA, networkB);
        }
        finally
        {
            authority.Screen.Dispose();
            clientA.Screen.Dispose();
            clientB.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XMultiClient_BroadcastsAcceptedCommandsToEveryReplica_Headless()
    {
        const ulong Seed = 0xBADC0DEUL;
        const int PeerA = 2;
        const int PeerB = 3;
        const int SharedLocalSequence = 500;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld clientA = BuildWorld(Seed);
        BuiltWorld clientB = BuildWorld(Seed);

        try
        {
            int empireA = authority.Player.Id;
            int empireB = authority.Enemy.Id;
            var session = new Authoritative4XInProcessMultiClientSession(authority.Screen, new[]
            {
                new Authoritative4XClientSpec(PeerA, empireA, clientA.Screen),
                new Authoritative4XClientSpec(PeerB, empireB, clientB.Screen),
            });

            session.SubmitFromClient(PeerA, AuthoritativePlayerCommand.SetColonyType(SharedLocalSequence,
                empireA, authority.Planet.Id, Planet.ColonyType.Research));
            AssertAccepted(session, PeerA);
            Assert.AreEqual(PeerA, session.LastResultFor(PeerB).OriginPeer,
                "Remote replicas must know which peer originated a broadcast command.");
            Assert.AreEqual(Planet.ColonyType.Research, authority.Planet.CType);
            Assert.AreEqual(Planet.ColonyType.Research, clientA.UState.GetPlanet(authority.Planet.Id).CType);
            Assert.AreEqual(Planet.ColonyType.Research, clientB.UState.GetPlanet(authority.Planet.Id).CType);
            AssertAllSynced(session, PeerA, PeerB);

            session.SubmitFromClient(PeerB, AuthoritativePlayerCommand.SetColonyType(SharedLocalSequence,
                empireB, authority.EnemyPlanet.Id, Planet.ColonyType.Military));
            AssertAccepted(session, PeerB);
            Assert.AreEqual(PeerB, session.LastResultFor(PeerA).OriginPeer,
                "Same local sequence numbers from different peers must not collide.");
            Assert.AreEqual(Planet.ColonyType.Military, authority.EnemyPlanet.CType);
            Assert.AreEqual(Planet.ColonyType.Military, clientA.UState.GetPlanet(authority.EnemyPlanet.Id).CType);
            Assert.AreEqual(Planet.ColonyType.Military, clientB.UState.GetPlanet(authority.EnemyPlanet.Id).CType);
            AssertAllSynced(session, PeerA, PeerB);
        }
        finally
        {
            authority.Screen.Dispose();
            clientA.Screen.Dispose();
            clientB.Screen.Dispose();
        }
    }

    [TestMethod]
    public void AuthoritativeHumanDiplomacy_SuppressesAiRelationshipTurnsOnlyForHumanPairs_Headless()
    {
        BuiltWorld world = BuildWorld(0xD170D1UL);
        try
        {
            Relationship rel = world.Player.GetRelations(world.Enemy);
            int turnsKnown = rel.TurnsKnown;

            AuthoritativeHumanPlayers.SetHumanControlledEmpires(world.UState, world.Player.Id, world.Enemy.Id);
            world.Player.UpdateRelationships(takeTurn: true);
            Assert.AreEqual(turnsKnown, rel.TurnsKnown,
                "A human-vs-human pair must not advance through the AI diplomacy relationship turn.");

            AuthoritativeHumanPlayers.SetHumanControlledEmpires(world.UState, world.Player.Id);
            world.Player.UpdateRelationships(takeTurn: true);
            Assert.AreEqual(turnsKnown + 1, rel.TurnsKnown,
                "Human-vs-AI relations must keep the stock relationship turn behavior.");
        }
        finally
        {
            AuthoritativeHumanPlayers.Clear(world.UState);
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void AuthoritativeHumanDiplomacy_ProposalsRouteApplyAndSync_Headless()
    {
        const ulong Seed = 0xA11A2CEUL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld clientA = BuildWorld(Seed);
        BuiltWorld clientB = BuildWorld(Seed);
        const int PeerA = 2;
        const int PeerB = 3;

        try
        {
            int empireA = authority.Player.Id;
            int empireB = authority.Enemy.Id;
            var session = new Authoritative4XInProcessMultiClientSession(authority.Screen, new[]
            {
                new Authoritative4XClientSpec(PeerA, empireA, clientA.Screen),
                new Authoritative4XClientSpec(PeerB, empireB, clientB.Screen),
            });
            authority.UState.CanShowDiplomacyScreen = true;
            clientA.UState.CanShowDiplomacyScreen = true;
            clientB.UState.CanShowDiplomacyScreen = true;

            session.SubmitFromClient(PeerA, AuthoritativePlayerCommand.DiplomacyProposal(1, empireA, empireB,
                AuthoritativeDiplomacyProposalType.TradeDeal, "first offer"));
            AssertAccepted(session, PeerA);
            AuthoritativeDiplomacyPopup tradeOffer = LastPopup(session, PeerB,
                AuthoritativeDiplomacyProposalType.TradeDeal, requiresResponse: true);
            Assert.AreEqual(empireA, tradeOffer.ProposerEmpireId);
            Assert.AreEqual(empireB, tradeOffer.TargetEmpireId);
            Assert.IsFalse(authority.Player.IsTradeTreaty(authority.Enemy));

            session.SubmitFromClient(PeerB, AuthoritativePlayerCommand.DiplomacyResponse(2, empireB,
                tradeOffer.ProposalId, AuthoritativeDiplomacyResponseKind.Reject));
            AssertAccepted(session, PeerB);
            AuthoritativeDiplomacyPopup rejected = LastPopup(session, PeerA,
                AuthoritativeDiplomacyProposalType.TradeDeal, requiresResponse: false);
            StringAssert.Contains(rejected.Message, "rejected");
            Assert.IsFalse(authority.Player.IsTradeTreaty(authority.Enemy));
            AssertAllSynced(session, PeerA, PeerB);

            DiplomacyScreen.DebugResetScreensShown();
            session.SubmitFromClient(PeerB, AuthoritativePlayerCommand.DiplomacyProposal(3, empireB, empireA,
                AuthoritativeDiplomacyProposalType.DeclareWar, "border dispute"));
            AssertAccepted(session, PeerB);
            Assert.IsTrue(authority.Player.IsAtWarWith(authority.Enemy));
            Assert.IsTrue(clientA.Player.IsAtWarWith(clientA.Enemy));
            Assert.IsTrue(clientB.Player.IsAtWarWith(clientB.Enemy));
            Assert.AreEqual(0, DiplomacyScreen.DebugScreensShown,
                "Human-vs-human authoritative declare-war must not route through stock AI DiplomacyScreen.Show.");
            AuthoritativeDiplomacyPopup warNotice = LastPopup(session, PeerA,
                AuthoritativeDiplomacyProposalType.DeclareWar, requiresResponse: false);
            Assert.AreEqual("War declared.", warNotice.Message);
            AssertAllSynced(session, PeerA, PeerB);

            session.SubmitFromClient(PeerA, AuthoritativePlayerCommand.DiplomacyProposal(4, empireA, empireB,
                AuthoritativeDiplomacyProposalType.Peace, "ceasefire"));
            AssertAccepted(session, PeerA);
            AuthoritativeDiplomacyPopup peaceOffer = LastPopup(session, PeerB,
                AuthoritativeDiplomacyProposalType.Peace, requiresResponse: true);
            Assert.IsTrue(authority.Player.IsAtWarWith(authority.Enemy), "Peace must wait for human acceptance.");

            session.SubmitFromClient(PeerB, AuthoritativePlayerCommand.DiplomacyResponse(5, empireB,
                peaceOffer.ProposalId, AuthoritativeDiplomacyResponseKind.Accept));
            AssertAccepted(session, PeerB);
            Assert.IsFalse(authority.Player.IsAtWarWith(authority.Enemy));
            Assert.IsTrue(authority.Player.IsPeaceTreaty(authority.Enemy));
            AssertAllSynced(session, PeerA, PeerB);

            AcceptProposal(session, PeerA, PeerB, empireA, empireB, 6,
                AuthoritativeDiplomacyProposalType.NonAggression);
            Assert.IsTrue(authority.Player.IsNAPactWith(authority.Enemy));
            AssertAllSynced(session, PeerA, PeerB);

            AcceptProposal(session, PeerA, PeerB, empireA, empireB, 8,
                AuthoritativeDiplomacyProposalType.TradeDeal);
            Assert.IsTrue(authority.Player.IsTradeTreaty(authority.Enemy));
            AssertAllSynced(session, PeerA, PeerB);

            AcceptProposal(session, PeerA, PeerB, empireA, empireB, 10,
                AuthoritativeDiplomacyProposalType.Alliance);
            Assert.IsTrue(authority.Player.IsAlliedWith(authority.Enemy));
            Assert.IsTrue(clientA.Player.IsAlliedWith(clientA.Enemy));
            Assert.IsTrue(clientB.Player.IsAlliedWith(clientB.Enemy));
            AssertAllSynced(session, PeerA, PeerB);

            session.SubmitFromClient(PeerA, AuthoritativePlayerCommand.DiplomacyProposal(12, empireB, empireA,
                AuthoritativeDiplomacyProposalType.NonAggression, "spoof"));
            Assert.IsFalse(session.LastResultFor(PeerA).Accepted);
            StringAssert.Contains(session.LastResultFor(PeerA).Reason, "does not control");
            AssertAllSynced(session, PeerA, PeerB);
        }
        finally
        {
            AuthoritativeHumanPlayers.Clear(authority.UState);
            AuthoritativeHumanPlayers.Clear(clientA.UState);
            AuthoritativeHumanPlayers.Clear(clientB.UState);
            authority.Screen.Dispose();
            clientA.Screen.Dispose();
            clientB.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XLobby_StartsGeneratedGameWithHumanRosterAndSettings_Headless()
    {
        LoadAllGameData();

        try
        {
            IEmpireData[] races = ResourceManager.MajorRaces
                .Where(r => !r.IsFactionOrMinorRace)
                .OrderBy(r => RacePreference(r), StringComparer.Ordinal)
                .Take(3)
                .ToArray();
            Assert.IsTrue(races.Length >= 3, "The lobby proof needs at least three playable major races.");

            var settings = new Authoritative4XGameSettings
            {
                GenerationSeed = 0x4B1B4B1,
                GalaxySize = GalSize.Tiny,
                StarsCount = RaceDesignScreen.StarsAbundance.Rare,
                Mode = RaceDesignScreen.GameMode.Sandbox,
                Difficulty = GameDifficulty.Normal,
                NumOpponents = 2,
                Pace = 2f,
                TurnTimer = 3,
                ExtraPlanets = 1,
                StartingPlanetRichnessBonus = 1.5f,
                GameSpeed = 1.5f,
                StartPaused = false,
            };

            var lobby = new Authoritative4XLobby(hostPlayerPeerId: 2, hostName: "Host");
            lobby.Join(3, "Client A");
            lobby.Join(4, "Client B");
            Assert.IsTrue(lobby.SetSettings(2, settings).Valid);

            Authoritative4XLobbyValidation invalid = lobby.SetPlayerSelection(3,
                RacePreference(races[1]), OverBudgetTraitSelection());
            Assert.IsFalse(invalid.Valid, "Trait budget validation must reject an illegal over-budget selection.");
            StringAssert.Contains(invalid.Reason, "budget");
            Assert.IsFalse(lobby.SetReady(3, true).Valid, "A player with invalid traits must not ready up.");

            string[] hostTraits = OneAffordableTrait();
            Assert.IsTrue(lobby.SetPlayerSelection(2, RacePreference(races[0]), hostTraits).Valid);
            Assert.IsTrue(lobby.SetPlayerSelection(3, RacePreference(races[1]), Array.Empty<string>()).Valid);
            Assert.IsTrue(lobby.SetPlayerSelection(4, RacePreference(races[2]), Array.Empty<string>()).Valid);
            Assert.IsTrue(lobby.SetReady(2, true).Valid);
            Assert.IsTrue(lobby.SetReady(3, true).Valid);
            Assert.IsTrue(lobby.SetReady(4, true).Valid);
            Assert.IsTrue(lobby.CanStart().Valid, lobby.CanStart().Reason);

            using Authoritative4XLobbyStartResult started = lobby.StartInProcess();
            UniverseState us = started.AuthorityUniverse.UState;
            Assert.AreEqual(settings.GenerationSeed, us.P.GenerationSeed);
            Assert.AreEqual(settings.GalaxySize, us.P.GalaxySize);
            Assert.AreEqual(settings.StarsCount, us.P.StarsCount);
            Assert.AreEqual(settings.Mode, us.P.Mode);
            Assert.AreEqual(settings.Pace, us.P.Pace);
            Assert.AreEqual(settings.TurnTimer, us.P.TurnTimer);
            Assert.AreEqual(settings.ExtraPlanets, us.P.ExtraPlanets);
            Assert.AreEqual(settings.StartingPlanetRichnessBonus, us.P.StartingPlanetRichnessBonus);
            Assert.AreEqual(settings.GameSpeed, us.GameSpeed);
            Assert.AreEqual(3, started.HumanEmpireIds.Length);

            for (int i = 0; i < races.Length; ++i)
            {
                int peer = 2 + i;
                Empire empire = us.GetEmpireById(started.EmpireIdForPeer(peer));
                Assert.IsNotNull(empire, $"Peer {peer} should map to a generated empire.");
                Assert.IsTrue(SameRace(empire.data, races[i]), $"Peer {peer} race did not match the lobby selection.");
                Assert.IsTrue(AuthoritativeHumanPlayers.IsHumanControlled(empire),
                    $"Peer {peer}'s empire should be registered as human-controlled.");
                Assert.IsTrue(empire.GetPlanets().Count > 0, $"Peer {peer}'s empire should have a homeworld.");
            }

            Empire hostEmpire = us.GetEmpireById(started.EmpireIdForPeer(2));
            Assert.IsTrue(hostTraits.All(t => hostEmpire.data.Traits.PlayerTraitOptions.Contains(t)),
                "The host player's selected trait options should flow into the generated player empire.");

            Planet hostPlanet = hostEmpire.GetPlanets().First();
            started.Session.SubmitFromClient(2, AuthoritativePlayerCommand.SetColonyType(100,
                hostEmpire.Id, hostPlanet.Id, Planet.ColonyType.Research));
            Assert.IsTrue(started.Session.LastResultFor(2).Accepted,
                started.Session.LastResultFor(2).Reason);
            Assert.AreEqual(Planet.ColonyType.Research, hostPlanet.CType);

            foreach (int peer in new[] { 2, 3, 4 })
            {
                Assert.AreEqual(started.Session.LastAuthoritySnapshot.HashLo,
                    started.Session.LastClientSnapshotFor(peer).HashLo);
                Assert.AreEqual(started.Session.LastAuthoritySnapshot.HashHi,
                    started.Session.LastClientSnapshotFor(peer).HashHi);
                Assert.AreEqual(started.Session.LastAuthoritySnapshot.SyncDigest,
                    started.Session.LastClientSnapshotFor(peer).SyncDigest);
            }
        }
        finally
        {
            // StarDriveTest.Cleanup unloads extra data; this keeps the intent explicit for future test edits.
        }
    }

    static void AcceptProposal(Authoritative4XInProcessMultiClientSession session, int proposerPeer, int targetPeer,
        int proposerEmpire, int targetEmpire, int sequence, AuthoritativeDiplomacyProposalType type)
    {
        session.SubmitFromClient(proposerPeer, AuthoritativePlayerCommand.DiplomacyProposal(sequence,
            proposerEmpire, targetEmpire, type, type.ToString()));
        AssertAccepted(session, proposerPeer);
        AuthoritativeDiplomacyPopup offer = LastPopup(session, targetPeer, type, requiresResponse: true);
        session.SubmitFromClient(targetPeer, AuthoritativePlayerCommand.DiplomacyResponse(sequence + 1,
            targetEmpire, offer.ProposalId, AuthoritativeDiplomacyResponseKind.Accept));
        AssertAccepted(session, targetPeer);
    }

    static void AssertAccepted(Authoritative4XInProcessMultiClientSession session, int peer)
    {
        Assert.IsTrue(session.LastResultFor(peer).Accepted, session.LastResultFor(peer).Reason);
    }

    static AuthoritativeDiplomacyPopup LastPopup(Authoritative4XInProcessMultiClientSession session, int peer,
        AuthoritativeDiplomacyProposalType type, bool requiresResponse)
    {
        AuthoritativeDiplomacyPopup popup = session.PopupsFor(peer)
            .LastOrDefault(p => p.ProposalType == type && p.RequiresResponse == requiresResponse);
        Assert.IsNotNull(popup, $"Peer {peer} did not receive {type} popup (requiresResponse={requiresResponse}).");
        return popup;
    }

    static void AssertAllSynced(Authoritative4XInProcessMultiClientSession session, params int[] peers)
    {
        foreach (int peer in peers)
        {
            Assert.AreEqual(session.LastAuthoritySnapshot.HashLo, session.LastClientSnapshotFor(peer).HashLo);
            Assert.AreEqual(session.LastAuthoritySnapshot.HashHi, session.LastClientSnapshotFor(peer).HashHi);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshotFor(peer).SyncDigest);
        }
    }

    static void PumpTcpUntil(Func<bool> done, Authoritative4XNetworkHost host, Authoritative4XNetworkClient client)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!done() && DateTime.UtcNow < deadline)
        {
            host.Poll();
            client.Poll();
            System.Threading.Thread.Sleep(5);
        }
        Assert.IsTrue(done(),
            $"Timed out waiting for authoritative TCP loopback. host='{host.LastError}' client='{client.LastError}'");
    }

    static void PumpTcpUntil(Func<bool> done, Authoritative4XNetworkHost host,
        params Authoritative4XNetworkClient[] clients)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!done() && DateTime.UtcNow < deadline)
        {
            host.Poll();
            foreach (Authoritative4XNetworkClient client in clients)
                client.Poll();
            System.Threading.Thread.Sleep(5);
        }
        string clientErrors = string.Join("; ", clients.Select(c => c.LastError));
        Assert.IsTrue(done(),
            $"Timed out waiting for authoritative TCP multi-client loopback. host='{host.LastError}' clients='{clientErrors}'");
    }

    static bool NetworkClientCaughtUp(Authoritative4XNetworkClient client, int sequence)
    {
        return client.LastResult?.Sequence == sequence
               && client.LastClientSnapshot != null
               && client.LastClientSnapshot.Tick == client.LastResult.Tick;
    }

    static bool NetworkClientCaughtUp(Authoritative4XNetworkClient client, int originPeer, int sequence)
    {
        return client.LastResult?.Sequence == sequence
               && client.LastResult.OriginPeer == originPeer
               && client.LastClientSnapshot != null
               && client.LastClientSnapshot.Tick == client.LastResult.Tick;
    }

    static bool WaitForMappedPeers(TcpLockstepTransport hostTransport, params int[] peers)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            int[] mapped = hostTransport.ConnectedRemotePeerIds;
            if (peers.All(peer => mapped.Contains(peer)))
                return true;
            System.Threading.Thread.Sleep(5);
        }
        return peers.All(peer => hostTransport.ConnectedRemotePeerIds.Contains(peer));
    }

    static void AssertNetworkSynced(params Authoritative4XNetworkClient[] clients)
    {
        foreach (Authoritative4XNetworkClient client in clients)
        {
            Assert.AreEqual(client.LastAuthoritySnapshot.HashLo, client.LastClientSnapshot.HashLo);
            Assert.AreEqual(client.LastAuthoritySnapshot.HashHi, client.LastClientSnapshot.HashHi);
            Assert.AreEqual(client.LastAuthoritySnapshot.SyncDigest, client.LastClientSnapshot.SyncDigest);
        }
    }

    static int FreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
        Assert.IsNotNull(source, "The test empire needs at least one legal mobile design to clone.");
        source.GetOrLoadDesignSlots();

        ShipDesign clone = source.GetClone(name);
        clone.IsPlayerDesign = true;
        clone.IsReadonlyDesign = false;
        return clone;
    }

    static ShipDesign BuildIllegalLockedModuleDesign(Empire empire, string name)
    {
        ShipDesign clone = BuildLegalPlayerDesign(empire, name);
        DesignSlot[] slots = clone.GetOrLoadDesignSlots().Select(s => new DesignSlot(s)).ToArray();
        int slotIndex = -1;
        ShipModule locked = null;
        for (int i = 0; i < slots.Length && locked == null; ++i)
        {
            DesignSlot candidate = slots[i];
            if (candidate == null || candidate.ModuleUID.IsEmpty())
                continue;

            locked = ResourceManager.ShipModuleTemplates
                .Where(m => !empire.IsModuleUnlocked(m.UID)
                            && m.GetOrientedSize(candidate.ModuleRot) == candidate.Size)
                .OrderBy(m => m.UID, StringComparer.Ordinal)
                .FirstOrDefault();
            if (locked != null)
                slotIndex = i;
        }
        Assert.IsNotNull(locked, "Could not find a locked module matching any occupied slot size.");

        DesignSlot original = slots[slotIndex];

        slots[slotIndex] = new DesignSlot(original.Pos, locked.UID, original.Size, original.TurretAngle,
            original.ModuleRot, original.HangarShipUID);
        clone.SetDesignSlots(slots);
        return clone;
    }

    BuiltWorld BuildWorld(ulong seed)
    {
        CreateUniverseAndPlayerEmpire();
        Planet planet = AddDummyPlanetToEmpire(new Vector2(200_000, 200_000), Player, fertility: 1f, minerals: 1f, maxPop: 5f);
        Planet enemyPlanet = AddDummyPlanetToEmpire(new Vector2(-200_000, -200_000), Enemy, fertility: 1f, minerals: 1f, maxPop: 5f);
        Ship ship = SpawnShip("Vulcan Scout", Player, new Vector2(0, 0));

        Player.InitEmpireFromSave(UState);
        Enemy.InitEmpireFromSave(UState);
        UState.Paused = false;
        UState.NoEliminationVictory = true;
        UState.Objects.EnableParallelUpdate = false;
        UState.EnableDeterministicRng(seed);

        string researchUid = Player.TechEntries
            .Where(t => t.Discovered && t.CanBeResearched)
            .OrderBy(t => t.UID, System.StringComparer.Ordinal)
            .First().UID;

        return new BuiltWorld
        {
            Screen = Universe,
            UState = UState,
            Player = Player,
            Enemy = Enemy,
            Planet = planet,
            EnemyPlanet = enemyPlanet,
            Ship = ship,
            ResearchUid = researchUid,
        };
    }

    static string RacePreference(IEmpireData race)
        => race.ArchetypeName.NotEmpty() ? race.ArchetypeName : race.Name;

    static bool SameRace(IEmpireData generated, IEmpireData selected)
        => string.Equals(RacePreference(generated), RacePreference(selected), StringComparison.OrdinalIgnoreCase)
           || string.Equals(generated.Name, selected.Name, StringComparison.OrdinalIgnoreCase);

    static string[] OneAffordableTrait()
    {
        int points = new UniverseParams().RacialTraitPoints;
        RacialTraitOption trait = ResourceManager.RaceTraits.TraitList
            .Where(t => t.Cost > 0 && t.Cost <= points)
            .OrderBy(t => t.Cost)
            .ThenBy(t => t.TraitName, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(trait, "No affordable positive-cost trait exists for lobby validation.");
        return new[] { trait.TraitName };
    }

    static string[] OverBudgetTraitSelection()
    {
        int points = new UniverseParams().RacialTraitPoints;
        var traits = new List<string>();
        int cost = 0;
        foreach (RacialTraitOption trait in ResourceManager.RaceTraits.TraitList
                     .Where(t => t.Cost > 0)
                     .OrderByDescending(t => t.Cost)
                     .ThenBy(t => t.TraitName, StringComparer.Ordinal))
        {
            traits.Add(trait.TraitName);
            cost += trait.Cost;
            if (cost > points)
                return traits.ToArray();
        }

        Assert.Fail("Could not construct an over-budget trait selection from loaded trait data.");
        return Array.Empty<string>();
    }
}

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
        public Ship WingShip;
        public Ship EnemyShip;
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
        Assert.AreEqual((int)MoveOrder.Regular, copy.TargetId);
        Assert.AreEqual(request.X, copy.X);
        Assert.AreEqual(request.Y, copy.Y);

        var aggressiveMove = AuthoritativePlayerCommand.MoveShip(77, 2, 99,
            new Vector2(-22f, 33f), MoveOrder.Aggressive | MoveOrder.AddWayPoint).ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(aggressiveMove, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.MoveShip, copy.Kind);
        Assert.AreEqual((int)(MoveOrder.Aggressive | MoveOrder.AddWayPoint), copy.TargetId);

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

        var buildingRequest = AuthoritativePlayerCommand.QueueBuilding(10, 2, 456, "Factory")
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(buildingRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.QueueBuilding, copy.Kind);
        Assert.AreEqual(456, copy.SubjectId);
        Assert.AreEqual("Factory", copy.Text);

        var troopRequest = AuthoritativePlayerCommand.QueueTroop(11, 2, 789, "Marine")
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(troopRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.QueueTroop, copy.Kind);
        Assert.AreEqual(789, copy.SubjectId);
        Assert.AreEqual("Marine", copy.Text);

        var attackRequest = AuthoritativePlayerCommand.AttackShip(12, 2, 99, 100, queue: true)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(attackRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.AttackShip, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual(100, copy.TargetId);
        Assert.AreEqual("queue", copy.Text);

        var planetOrderRequest = AuthoritativePlayerCommand.ShipPlanetOrder(13, 2, 99, 456,
            AuthoritativeShipPlanetOrderType.Orbit, clearOrders: false, MoveOrder.Aggressive)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(planetOrderRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.ShipPlanetOrder, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual(456, copy.TargetId);
        Assert.AreEqual($"{(int)AuthoritativeShipPlanetOrderType.Orbit}|0|{(int)MoveOrder.Aggressive}",
            copy.Text);

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
                authority.Ship.Position + new Vector2(25_000, 10_000),
                MoveOrder.Aggressive | MoveOrder.AddWayPoint);
            session.SubmitFromClient(move);
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(AIState.AwaitingOrders, authority.Ship.AI.State);
            Assert.AreEqual(move.Position, authority.Ship.AI.MovePosition);
            Assert.IsTrue(authority.Ship.AI.OrderQueue.PeekFirst.MoveOrder.IsSet(MoveOrder.Aggressive));
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

            MakeAtWar(authority.Player, authority.Enemy);
            MakeAtWar(client.Player, client.Enemy);
            string beforeAttackDigest = session.LastAuthoritySnapshot.SyncDigest;
            var attack = AuthoritativePlayerCommand.AttackShip(4, authority.Player.Id, authority.Ship.Id,
                authority.EnemyShip.Id);
            session.SubmitFromClient(attack);
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(AIState.AttackTarget, authority.Ship.AI.State);
            Assert.AreEqual(authority.EnemyShip, authority.Ship.AI.Target);
            Assert.AreEqual(client.EnemyShip, client.Ship.AI.Target);
            Assert.IsTrue(authority.Ship.AI.HasPriorityTarget);
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload,
                $"S|{authority.Ship.Id}|{authority.Player.Id}|{(int)AIState.AttackTarget}|");
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, $"|{authority.EnemyShip.Id}|1|{authority.EnemyShip.Id}");
            Assert.AreNotEqual(beforeAttackDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover accepted ship target orders.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeOrbitDigest = session.LastAuthoritySnapshot.SyncDigest;
            var orbit = AuthoritativePlayerCommand.ShipPlanetOrder(5, authority.Player.Id,
                authority.Ship.Id, authority.Planet.Id, AuthoritativeShipPlanetOrderType.Orbit,
                clearOrders: true, MoveOrder.Aggressive);
            session.SubmitFromClient(orbit);
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.Ship.AI.OrderQueue.ToArray()
                    .Any(g => g.Plan == ShipAI.Plan.Orbit && g.TargetPlanet == authority.Planet),
                "The authority should apply orbit through the real ship AI order queue.");
            Assert.IsTrue(client.Ship.AI.OrderQueue.ToArray()
                    .Any(g => g.Plan == ShipAI.Plan.Orbit && g.TargetPlanet == client.Planet),
                "The client replica should apply the accepted planet order deterministically.");
            Assert.AreNotEqual(beforeOrbitDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover ship planet order queue state.");
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, $"{(int)ShipAI.Plan.Orbit},{authority.Planet.Id}");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            var illegal = AuthoritativePlayerCommand.SetColonyType(6, authority.Enemy.Id, authority.Planet.Id,
                Planet.ColonyType.Military);
            session.SubmitFromClient(illegal);
            Assert.IsFalse(session.LastResult.Accepted, "Enemy must not be able to mutate the player's planet.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(Planet.ColonyType.Research, authority.Planet.CType);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            var illegalAttack = AuthoritativePlayerCommand.AttackShip(7, authority.Enemy.Id,
                authority.Ship.Id, authority.EnemyShip.Id);
            session.SubmitFromClient(illegalAttack);
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not issue attack orders for ships it does not own.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            var illegalOrbit = AuthoritativePlayerCommand.ShipPlanetOrder(8, authority.Enemy.Id,
                authority.Ship.Id, authority.Planet.Id, AuthoritativeShipPlanetOrderType.Orbit);
            session.SubmitFromClient(illegalOrbit);
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not issue planet orders for ships it does not own.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XColonyBuildQueue_QueuesBuildingsAndSyncsPlacement_Headless()
    {
        const ulong Seed = 0xB411D01UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;
            EnsureSingleBuildTile(authority.Planet);
            EnsureSingleBuildTile(client.Planet);
            Building buildable = PickBuildableBuilding(authority.Planet);
            Troop troop = PickBuildableTroop(authority.Player);
            authority.Planet.HasSpacePort = true;
            client.Planet.HasSpacePort = true;

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueBuilding(30, authority.Player.Id,
                authority.Planet.Id, buildable.Name));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);

            QueueItem authorityItem = LastQueuedBuilding(authority.Planet, buildable.Name);
            QueueItem clientItem = LastQueuedBuilding(client.Planet, buildable.Name);
            Assert.IsNotNull(authorityItem.pgs, "Queued buildings must reserve a deterministic planet tile.");
            Assert.IsNotNull(clientItem.pgs, "Client replica should reserve the same deterministic planet tile.");
            Assert.AreEqual(authorityItem.pgs.X, clientItem.pgs.X);
            Assert.AreEqual(authorityItem.pgs.Y, clientItem.pgs.Y);
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The canonical sync digest must cover real building queue mutations.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload,
                $"|{buildable.Name}|");

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueTroop(33, authority.Player.Id,
                authority.Planet.Id, troop.Name));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            QueueItem authorityTroop = LastQueuedTroop(authority.Planet, troop.Name);
            QueueItem clientTroop = LastQueuedTroop(client.Planet, troop.Name);
            Assert.AreEqual(authorityTroop.TroopType, clientTroop.TroopType);
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, $"|{troop.Name}|");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueBuilding(31, authority.Player.Id,
                authority.EnemyPlanet.Id, buildable.Name));
            Assert.IsFalse(session.LastResult.Accepted, "A player must not queue buildings at another empire's planet.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueBuilding(32, authority.Player.Id,
                authority.Planet.Id, "Definitely Missing MP Building"));
            Assert.IsFalse(session.LastResult.Accepted, "Unknown buildings must not be queued.");
            StringAssert.Contains(session.LastResult.Reason, "not found");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueTroop(34, authority.Player.Id,
                authority.EnemyPlanet.Id, troop.Name));
            Assert.IsFalse(session.LastResult.Accepted, "A player must not queue troops at another empire's planet.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueTroop(35, authority.Player.Id,
                authority.Planet.Id, "Definitely Missing MP Troop"));
            Assert.IsFalse(session.LastResult.Accepted, "Unknown troops must not be queued.");
            StringAssert.Contains(session.LastResult.Reason, "not found");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsColonyCommandsWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xC0110C8UL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            EnsureSingleBuildTile(world.Planet);
            Building buildable = PickBuildableBuilding(world.Planet);
            IShipDesign mobileShip = PickMobileBuildableShip(world.Player);
            Troop troop = PickBuildableTroop(world.Player);
            var submitted = new List<AuthoritativePlayerCommand>();
            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 700))
            {
                Assert.IsTrue(Authoritative4XClientContext.TrySubmitQueueBuilding(world.Planet, buildable.Name),
                    "The colony build UI dispatch context should accept an MP command submission.");
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(AuthoritativePlayerCommandKind.QueueBuilding, submitted[0].Kind);
                Assert.AreEqual(700, submitted[0].Sequence);
                Assert.AreEqual(world.Player.Id, submitted[0].EmpireId);
                Assert.AreEqual(world.Planet.Id, submitted[0].SubjectId);
                Assert.AreEqual(buildable.Name, submitted[0].Text);
                Assert.IsFalse(world.Planet.Construction.GetConstructionQueueSnapshot()
                        .Any(q => q.isBuilding && q.Building?.Name == buildable.Name),
                    "Passive MP clients must not locally enqueue the building before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitQueueShip(world.Planet, mobileShip, repeat: 2));
                Assert.AreEqual(3, submitted.Count);
                Assert.IsTrue(submitted.Skip(1).All(c => c.Kind == AuthoritativePlayerCommandKind.QueueBuild));
                Assert.AreEqual(701, submitted[1].Sequence);
                Assert.AreEqual(702, submitted[2].Sequence);
                Assert.IsTrue(submitted.Skip(1).All(c => c.SubjectId == world.Planet.Id && c.Text == mobileShip.Name));
                Assert.IsFalse(world.Planet.Construction.GetConstructionQueueSnapshot()
                        .Any(q => q.isShip && q.ShipData?.Name == mobileShip.Name),
                    "Passive MP clients must not locally enqueue ships before host acceptance.");

                Vector2 originalMovePosition = world.Ship.AI.MovePosition;
                Vector2 destination = world.Ship.Position + new Vector2(8_000f, -3_000f);
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitMoveShip(world.Ship, destination,
                        MoveOrder.StandGround | MoveOrder.AddWayPoint));
                Assert.AreEqual(4, submitted.Count);
                Assert.AreEqual(AuthoritativePlayerCommandKind.MoveShip, submitted[3].Kind);
                Assert.AreEqual(703, submitted[3].Sequence);
                Assert.AreEqual(world.Ship.Id, submitted[3].SubjectId);
                Assert.AreEqual(destination, submitted[3].Position);
                Assert.AreEqual((int)(MoveOrder.StandGround | MoveOrder.AddWayPoint), submitted[3].TargetId);
                Assert.AreEqual(originalMovePosition, world.Ship.AI.MovePosition,
                    "Passive MP clients must not locally issue ship move orders before host acceptance.");

                Ship originalTarget = world.Ship.AI.Target;
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitAttackShip(world.Ship, world.EnemyShip, queue: true));
                Assert.AreEqual(5, submitted.Count);
                Assert.AreEqual(AuthoritativePlayerCommandKind.AttackShip, submitted[4].Kind);
                Assert.AreEqual(704, submitted[4].Sequence);
                Assert.AreEqual(world.Ship.Id, submitted[4].SubjectId);
                Assert.AreEqual(world.EnemyShip.Id, submitted[4].TargetId);
                Assert.AreEqual("queue", submitted[4].Text);
                Assert.AreEqual(originalTarget, world.Ship.AI.Target,
                    "Passive MP clients must not locally issue ship attack orders before host acceptance.");

                int originalOrderCount = world.Ship.AI.OrderQueue.Count;
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitShipPlanetOrder(world.Ship, world.Planet,
                        AuthoritativeShipPlanetOrderType.Orbit, clearOrders: false, MoveOrder.Aggressive));
                Assert.AreEqual(6, submitted.Count);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ShipPlanetOrder, submitted[5].Kind);
                Assert.AreEqual(705, submitted[5].Sequence);
                Assert.AreEqual(world.Ship.Id, submitted[5].SubjectId);
                Assert.AreEqual(world.Planet.Id, submitted[5].TargetId);
                Assert.AreEqual($"{(int)AuthoritativeShipPlanetOrderType.Orbit}|0|{(int)MoveOrder.Aggressive}",
                    submitted[5].Text);
                Assert.AreEqual(originalOrderCount, world.Ship.AI.OrderQueue.Count,
                    "Passive MP clients must not locally issue ship planet orders before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitQueueTroop(world.Planet, troop, repeat: 2));
                Assert.AreEqual(8, submitted.Count);
                Assert.IsTrue(submitted.Skip(6).All(c => c.Kind == AuthoritativePlayerCommandKind.QueueTroop));
                Assert.AreEqual(706, submitted[6].Sequence);
                Assert.AreEqual(707, submitted[7].Sequence);
                Assert.IsTrue(submitted.Skip(6).All(c => c.SubjectId == world.Planet.Id && c.Text == troop.Name));
                Assert.IsFalse(world.Planet.Construction.GetConstructionQueueSnapshot()
                        .Any(q => q.isTroop && q.TroopType == troop.Name),
                    "Passive MP clients must not locally enqueue troops before host acceptance.");

                Planet.ColonyType originalType = world.Planet.CType;
                Assert.IsTrue(Authoritative4XClientContext.TrySubmitSetColonyType(world.Planet,
                    Planet.ColonyType.Research));
                Assert.AreEqual(originalType, world.Planet.CType,
                    "The context should submit a colony-type request without directly mutating the replica.");
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetColonyType, submitted[8].Kind);
                Assert.AreEqual(708, submitted[8].Sequence);

                string originalTopic = world.Player.Research.Topic;
                Assert.IsTrue(Authoritative4XClientContext.TrySubmitSetResearchTopic(world.Player,
                    world.ResearchUid));
                Assert.AreEqual(originalTopic, world.Player.Research.Topic,
                    "The context should submit a research request without directly mutating the replica.");
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetResearchTopic, submitted[9].Kind);
                Assert.AreEqual(709, submitted[9].Sequence);
                Assert.AreEqual(world.ResearchUid, submitted[9].Text);

                int beforeWrongEmpire = submitted.Count;
                Assert.IsFalse(Authoritative4XClientContext.TrySubmitQueueBuilding(world.EnemyPlanet, buildable.Name),
                    "An active client context must only handle its own empire's UI commands.");
                Assert.AreEqual(beforeWrongEmpire, submitted.Count);
                Assert.IsFalse(Authoritative4XClientContext.TrySubmitSetResearchTopic(world.Enemy,
                    world.ResearchUid));
                Assert.AreEqual(beforeWrongEmpire, submitted.Count);
            }

            Assert.IsFalse(Authoritative4XClientContext.IsActive,
                "Disposing the context should restore the single-player/no-context default.");
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsBatchFleetCommandsWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xF1EE7C0UL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            var submitted = new List<AuthoritativePlayerCommand>();
            Vector2 firstMove = world.Ship.AI.MovePosition;
            Vector2 secondMove = world.WingShip.AI.MovePosition;
            Ship firstTarget = world.Ship.AI.Target;
            Ship secondTarget = world.WingShip.AI.Target;
            int firstOrders = world.Ship.AI.OrderQueue.Count;
            int secondOrders = world.WingShip.AI.OrderQueue.Count;

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 900))
            {
                Vector2 destination = world.Ship.Position + new Vector2(12_000f, 6_000f);
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitMoveShips(new[] { world.Ship, world.WingShip },
                        destination, MoveOrder.Aggressive));
                Assert.AreEqual(2, submitted.Count);
                Assert.IsTrue(submitted.All(c => c.Kind == AuthoritativePlayerCommandKind.MoveShip));
                Assert.AreEqual(900, submitted[0].Sequence);
                Assert.AreEqual(901, submitted[1].Sequence);
                Assert.AreEqual(world.Ship.Id, submitted[0].SubjectId);
                Assert.AreEqual(world.WingShip.Id, submitted[1].SubjectId);
                Assert.AreEqual(destination, submitted[0].Position);
                Assert.AreEqual(destination, submitted[1].Position);
                Assert.AreEqual(firstMove, world.Ship.AI.MovePosition);
                Assert.AreEqual(secondMove, world.WingShip.AI.MovePosition);

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitAttackShips(new[] { world.Ship, world.WingShip },
                        world.EnemyShip, queue: false));
                Assert.AreEqual(4, submitted.Count);
                Assert.IsTrue(submitted.Skip(2).All(c => c.Kind == AuthoritativePlayerCommandKind.AttackShip));
                Assert.AreEqual(902, submitted[2].Sequence);
                Assert.AreEqual(903, submitted[3].Sequence);
                Assert.AreEqual(world.EnemyShip.Id, submitted[2].TargetId);
                Assert.AreEqual(world.EnemyShip.Id, submitted[3].TargetId);
                Assert.AreEqual(firstTarget, world.Ship.AI.Target);
                Assert.AreEqual(secondTarget, world.WingShip.AI.Target);

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitShipPlanetOrders(new[] { world.Ship, world.WingShip },
                        world.Planet, clearOrders: true, MoveOrder.StandGround,
                        _ => AuthoritativeShipPlanetOrderType.Orbit));
                Assert.AreEqual(6, submitted.Count);
                Assert.IsTrue(submitted.Skip(4).All(c => c.Kind == AuthoritativePlayerCommandKind.ShipPlanetOrder));
                Assert.AreEqual(904, submitted[4].Sequence);
                Assert.AreEqual(905, submitted[5].Sequence);
                Assert.AreEqual(world.Planet.Id, submitted[4].TargetId);
                Assert.AreEqual(world.Planet.Id, submitted[5].TargetId);
                Assert.AreEqual(firstOrders, world.Ship.AI.OrderQueue.Count);
                Assert.AreEqual(secondOrders, world.WingShip.AI.OrderQueue.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsShipDesignWithoutLocalRegistration_Headless()
    {
        const ulong Seed = 0xDE516EUL;
        const string DesignName = "Authoritative MP UI Dispatch Design";
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            ResourceManager.Ships.Delete(DesignName);
            ShipDesign design = BuildLegalPlayerDesign(world.Player, DesignName);
            Assert.IsFalse(ResourceManager.Ships.GetDesign(DesignName, out _),
                "The dispatch proof starts from an unregistered design name.");

            var submitted = new List<AuthoritativePlayerCommand>();
            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 1000))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitDesignShip(world.Player, design));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(1000, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.DesignShip, submitted[0].Kind);
                Assert.AreEqual(world.Player.Id, submitted[0].EmpireId);

                ShipDesign decoded = ShipDesign.FromBytes(Convert.FromBase64String(submitted[0].Text));
                Assert.AreEqual(DesignName, decoded.Name);
                Assert.AreEqual(design.Hull, decoded.Hull);
                Assert.IsFalse(ResourceManager.Ships.GetDesign(DesignName, out _),
                    "A passive MP client must not locally register the design before host acceptance.");
            }
        }
        finally
        {
            ResourceManager.Ships.Delete(DesignName);
            world.Screen.Dispose();
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
    public void Authoritative4XLiveHost_AttachesPollsAndBroadcastsHeartbeat_Headless()
    {
        const ulong Seed = 0x41E40057UL;
        const int Peer = 2;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            int port = FreeTcpPort();
            TcpLockstepTransport hostTransport = TcpLockstepTransport.Host(port, Peer);
            TcpLockstepTransport clientTransport = TcpLockstepTransport.Join("127.0.0.1", port,
                Authoritative4XNetworkHost.HostPeerId);
            Assert.IsTrue(hostTransport.WaitForConnection(TimeSpan.FromSeconds(3)),
                "Authoritative live host did not accept the loopback client.");

            using var networkClient = new Authoritative4XNetworkClient(client.Screen, clientTransport, Peer,
                new[] { client.Player.Id });
            Authoritative4XLiveSession liveHost = Authoritative4XLiveSession.HostGame(authority.Screen,
                hostTransport, Peer, new Dictionary<int, int> { [Peer] = authority.Player.Id },
                new[] { authority.Player.Id });
            authority.Screen.AttachAuthoritative4XMultiplayer(liveHost);

            PumpLiveTcpUntil(() => NetworkClientCaughtHeartbeat(networkClient, Peer),
                liveHost, networkClient);
            Assert.IsNotNull(authority.Screen.Authoritative4XMultiplayer,
                "The visible universe should own the live authoritative session.");
            Assert.AreEqual(networkClient.LastAuthoritySnapshot.SyncDigest,
                networkClient.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XLiveClient_SubmitsUiCommandThroughTcpHost_Headless()
    {
        const ulong Seed = 0xC11E475UL;
        const int Peer = 2;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            int port = FreeTcpPort();
            TcpLockstepTransport hostTransport = TcpLockstepTransport.Host(port, Peer);
            TcpLockstepTransport clientTransport = TcpLockstepTransport.Join("127.0.0.1", port,
                Authoritative4XNetworkHost.HostPeerId);
            Assert.IsTrue(hostTransport.WaitForConnection(TimeSpan.FromSeconds(3)),
                "Authoritative live client proof did not connect to the loopback host.");

            using var host = new Authoritative4XNetworkHost(authority.Screen, hostTransport,
                new Dictionary<int, int> { [Peer] = authority.Player.Id },
                new[] { authority.Player.Id });
            Authoritative4XLiveSession liveClient = Authoritative4XLiveSession.ClientGame(client.Screen,
                clientTransport, Peer, client.Player.Id, new[] { client.Player.Id });
            client.Screen.AttachAuthoritative4XMultiplayer(liveClient);

            Assert.IsTrue(Authoritative4XClientContext.TrySubmitSetColonyType(client.Planet,
                    Planet.ColonyType.Research),
                "A live passive client should route colony UI actions into the authoritative command stream.");
            PumpLiveTcpUntil(() => NetworkClientCaughtUp(liveClient, Peer, 1), host, liveClient);

            Assert.IsTrue(liveClient.LastResult.Accepted, liveClient.LastResult.Reason);
            Assert.AreEqual(Planet.ColonyType.Research, authority.Planet.CType);
            Assert.AreEqual(Planet.ColonyType.Research, client.Planet.CType);
            Assert.AreEqual(liveClient.LastSnapshot.SyncDigest,
                ((Authoritative4XNetworkHost)host).LastAuthoritySnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XLiveClient_UsesAssignedEmpireForLocalView_Headless()
    {
        const ulong Seed = 0xC11E476UL;
        const int Peer = 2;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            int port = FreeTcpPort();
            TcpLockstepTransport hostTransport = TcpLockstepTransport.Host(port, Peer);
            TcpLockstepTransport clientTransport = TcpLockstepTransport.Join("127.0.0.1", port,
                Authoritative4XNetworkHost.HostPeerId);
            Assert.IsTrue(hostTransport.WaitForConnection(TimeSpan.FromSeconds(3)),
                "Authoritative local-view proof did not connect to the loopback host.");

            using var host = new Authoritative4XNetworkHost(authority.Screen, hostTransport,
                new Dictionary<int, int> { [Peer] = authority.Enemy.Id },
                new[] { authority.Player.Id, authority.Enemy.Id });
            Authoritative4XLiveSession liveClient = Authoritative4XLiveSession.ClientGame(client.Screen,
                clientTransport, Peer, client.Enemy.Id, new[] { client.Player.Id, client.Enemy.Id });
            client.Screen.AttachAuthoritative4XMultiplayer(liveClient);
            Planet clientRemotePlanet = client.UState.GetPlanet(authority.EnemyPlanet.Id);
            Planet clientHostPlanet = client.UState.GetPlanet(authority.Planet.Id);
            Planet authorityRemotePlanet = authority.UState.GetPlanet(clientRemotePlanet?.Id ?? 0);
            Planet authorityHostPlanet = authority.UState.GetPlanet(clientHostPlanet?.Id ?? 0);
            Assert.IsNotNull(clientRemotePlanet, "The client replica should contain the authority enemy planet id.");
            Assert.IsNotNull(clientHostPlanet, "The client replica should contain the authority host planet id.");
            Assert.IsNotNull(authorityRemotePlanet, "The authority should resolve the client remote planet id.");
            Assert.IsNotNull(authorityHostPlanet, "The authority should resolve the client host planet id.");
            Assert.AreSame(authority.Enemy, authorityRemotePlanet.Owner,
                "The chosen authority-side planet should belong to the peer's assigned empire.");
            Assert.AreSame(authority.Player, authorityHostPlanet.Owner,
                "The chosen authority-side host planet should belong to the host empire.");
            Assert.AreSame(client.Enemy, clientRemotePlanet.Owner,
                "The chosen client-side planet should belong to the peer's assigned empire.");
            Assert.AreSame(client.Player, clientHostPlanet.Owner,
                "The chosen client-side host planet should belong to the host empire.");

            Assert.AreSame(client.Enemy, client.Screen.Player,
                "The visible client screen should render and command the empire assigned to this peer.");
            Assert.AreSame(client.Player, client.UState.Player,
                "The deterministic replica state must keep the original generated player empire for sync.");
            Assert.IsTrue(client.Player.isPlayer,
                "The local-view hook must not rewrite simulation isPlayer flags.");
            Assert.IsFalse(client.Enemy.isPlayer,
                "The local-view hook must not rewrite simulation isPlayer flags.");
            Assert.IsTrue(client.Screen.IsLocalShipForUi(client.EnemyShip));
            Assert.IsFalse(client.Screen.IsLocalShipForUi(client.Ship));
            Assert.IsTrue(client.Screen.LocalShipCanTakeFleetOrders(client.EnemyShip, forAttack: false));
            Assert.IsFalse(client.Screen.LocalShipCanTakeFleetOrders(client.Ship, forAttack: false));

            EnsureSingleBuildTile(authorityRemotePlanet);
            EnsureSingleBuildTile(clientRemotePlanet);
            Building buildable = PickBuildableBuilding(clientRemotePlanet);
            Assert.IsTrue(Authoritative4XClientContext.TrySubmitQueueBuilding(clientRemotePlanet,
                    buildable.Name),
                "The visible remote client should submit commands for its assigned empire.");
            PumpLiveTcpUntil(() => NetworkClientCaughtUp(liveClient, Peer, 1), host, liveClient);

            Assert.IsTrue(liveClient.LastResult.Accepted, liveClient.LastResult.Reason);
            Assert.IsNotNull(LastQueuedBuilding(authorityRemotePlanet, buildable.Name));
            Assert.IsNotNull(LastQueuedBuilding(clientRemotePlanet, buildable.Name));
            Assert.AreEqual(liveClient.LastSnapshot.SyncDigest,
                ((Authoritative4XNetworkHost)host).LastAuthoritySnapshot.SyncDigest);

            int beforeWrongEmpire = liveClient.LastResult.Sequence;
            Assert.IsFalse(Authoritative4XClientContext.TrySubmitSetColonyType(clientHostPlanet,
                    Planet.ColonyType.Military),
                "The visible remote client must not submit UI commands for the host empire.");
            Assert.AreEqual(beforeWrongEmpire, liveClient.LastResult.Sequence);
        }
        finally
        {
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

    static void PumpLiveTcpUntil(Func<bool> done, Authoritative4XLiveSession host,
        Authoritative4XNetworkClient client)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!done() && DateTime.UtcNow < deadline)
        {
            host.Poll();
            client.Poll();
            System.Threading.Thread.Sleep(5);
        }
        Assert.IsTrue(done(),
            $"Timed out waiting for live authoritative host. host='{host.LastError}' client='{client.LastError}'");
    }

    static void PumpLiveTcpUntil(Func<bool> done, Authoritative4XNetworkHost host,
        Authoritative4XLiveSession client)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!done() && DateTime.UtcNow < deadline)
        {
            host.Poll();
            client.Poll();
            System.Threading.Thread.Sleep(5);
        }
        Assert.IsTrue(done(),
            $"Timed out waiting for live authoritative client. host='{host.LastError}' client='{client.LastError}'");
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

    static bool NetworkClientCaughtHeartbeat(Authoritative4XNetworkClient client, int originPeer)
    {
        return client.LastResult?.Sequence <= -1
               && client.LastResult.OriginPeer == originPeer
               && client.LastClientSnapshot != null
               && client.LastClientSnapshot.Tick == client.LastResult.Tick;
    }

    static bool NetworkClientCaughtUp(Authoritative4XLiveSession client, int originPeer, int sequence)
    {
        return client.LastResult?.Sequence == sequence
               && client.LastResult.OriginPeer == originPeer
               && client.LastSnapshot != null
               && client.LastSnapshot.Tick == client.LastResult.Tick;
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

    static Building PickBuildableBuilding(Planet planet)
    {
        planet.RefreshBuildingsWeCanBuildHere();
        Building building = planet.GetBuildingsCanBuild()
            .Where(b => planet.TilesList.Any(tile => tile.CanEnqueueBuildingHere(b)))
            .OrderBy(b => b.ActualCost(planet.Owner))
            .ThenBy(b => b.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(building, $"Planet {planet.Id} needs at least one buildable building for the authoritative queue proof.");
        return building;
    }

    static IShipDesign PickMobileBuildableShip(Empire empire)
    {
        IShipDesign ship = empire.ShipsWeCanBuildSnapshot
            .Where(s => !s.IsPlatformOrStation && !s.IsShipyard)
            .OrderBy(s => s.BaseCost)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(ship, $"Empire {empire.Id} needs at least one mobile buildable ship for the UI dispatch proof.");
        return ship;
    }

    static Troop PickBuildableTroop(Empire empire)
    {
        Troop troop = ResourceManager.GetTroopTemplatesFor(empire)
            .OrderBy(t => t.ActualCost(empire))
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(troop, $"Empire {empire.Id} needs at least one buildable troop for the authoritative queue proof.");
        return troop;
    }

    static void EnsureSingleBuildTile(Planet planet)
    {
        planet.TilesList.Clear();
        planet.TilesList.Add(new PlanetGridSquare(planet, 0, 0, b: null, hab: true, terraformable: false));
        planet.RefreshBuildingsWeCanBuildHere();
    }

    static QueueItem LastQueuedBuilding(Planet planet, string buildingName)
    {
        QueueItem item = planet.Construction.GetConstructionQueueSnapshot()
            .LastOrDefault(q => q.isBuilding && q.Building?.Name == buildingName);
        Assert.IsNotNull(item, $"Planet {planet.Id} did not queue building {buildingName}.");
        return item;
    }

    static QueueItem LastQueuedTroop(Planet planet, string troopName)
    {
        QueueItem item = planet.Construction.GetConstructionQueueSnapshot()
            .LastOrDefault(q => q.isTroop && q.TroopType == troopName);
        Assert.IsNotNull(item, $"Planet {planet.Id} did not queue troop {troopName}.");
        return item;
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
        Ship wingShip = SpawnShip("Vulcan Scout", Player, new Vector2(2_000, 0));
        Ship enemyShip = SpawnShip("Vulcan Scout", Enemy, new Vector2(35_000, 0));

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
            WingShip = wingShip,
            EnemyShip = enemyShip,
            ResearchUid = researchUid,
        };
    }

    static void MakeAtWar(Empire a, Empire b)
    {
        if (!a.IsAtWarWith(b))
            a.AI.DeclareWarOn(b, WarType.BorderConflict);
        Assert.IsTrue(a.IsAtWarWith(b), $"Empire {a.Id} should be at war with {b.Id}.");
        Assert.IsTrue(b.IsAtWarWith(a), $"Empire {b.Id} should be at war with {a.Id}.");
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

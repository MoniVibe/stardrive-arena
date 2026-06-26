using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDGraphics;
using SDLockstep;
using Ship_Game;
using Ship_Game.AI;
using Ship_Game.Commands.Goals;
using Ship_Game.Data;
using Ship_Game.GameScreens.DiplomacyScreen;
using Ship_Game.Gameplay;
using Ship_Game.Fleets;
using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.Ships;
using Ship_Game.Ships.AI;
using Ship_Game.Universe;
using System;
using System.Collections.Generic;
using System.IO;
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
        public Planet NeutralPlanet;
        public Ship Ship;
        public Ship WingShip;
        public Ship EnemyShip;
        public Ship PlatformShip;
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

        var colonizationRequest = AuthoritativePlayerCommand.SetColonizationGoal(34, 2, 456, enabled: true)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(colonizationRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetColonizationGoal, copy.Kind);
        Assert.AreEqual(456, copy.SubjectId);
        Assert.AreEqual(1, copy.TargetId);

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

        var tileBuildingRequest = AuthoritativePlayerCommand.QueueBuilding(12, 2, 456, "Factory", 1, 3)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(tileBuildingRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.QueueBuilding, copy.Kind);
        Assert.AreEqual(456, copy.SubjectId);
        Assert.AreEqual(1, copy.TargetId);
        Assert.AreEqual(1f, copy.X);
        Assert.AreEqual(3f, copy.Y);
        Assert.AreEqual("Factory", copy.Text);

        var troopRequest = AuthoritativePlayerCommand.QueueTroop(11, 2, 789, "Marine")
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(troopRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.QueueTroop, copy.Kind);
        Assert.AreEqual(789, copy.SubjectId);
        Assert.AreEqual("Marine", copy.Text);

        var budgetRequest = AuthoritativePlayerCommand.SetEmpireBudget(17, 2,
                taxRate: 0.35f, treasuryGoal: 0.45f, autoTaxes: true)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(budgetRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetEmpireBudget, copy.Kind);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseEmpireBudgetPayload(copy.Text,
            out float taxRate, out float treasuryGoal, out bool autoTaxes));
        Assert.AreEqual(0.35f, taxRate);
        Assert.AreEqual(0.45f, treasuryGoal);
        Assert.IsTrue(autoTaxes);

        var queueResearchRequest = AuthoritativePlayerCommand.QueueResearch(18, 2, "Research UID")
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(queueResearchRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.QueueResearch, copy.Kind);
        Assert.AreEqual("Research UID", copy.Text);

        var removeResearchRequest = AuthoritativePlayerCommand.RemoveResearchQueueItem(19, 2, "Research UID")
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(removeResearchRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.RemoveResearchQueueItem, copy.Kind);
        Assert.AreEqual("Research UID", copy.Text);

        var moveResearchRequest = AuthoritativePlayerCommand.MoveResearchQueueItem(20, 2, "Research UID",
                AuthoritativeResearchQueueMove.ToTopWithPrereqs)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(moveResearchRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.MoveResearchQueueItem, copy.Kind);
        Assert.AreEqual((int)AuthoritativeResearchQueueMove.ToTopWithPrereqs, copy.TargetId);
        Assert.AreEqual("Research UID", copy.Text);

        var cancelQueueRequest = AuthoritativePlayerCommand.CancelConstructionQueueItem(15, 2, 789, queueIndex: 3)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(cancelQueueRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.CancelConstructionQueueItem, copy.Kind);
        Assert.AreEqual(789, copy.SubjectId);
        Assert.AreEqual(3, copy.TargetId);

        var reorderQueueRequest = AuthoritativePlayerCommand.ReorderConstructionQueueItem(16, 2, 789,
                currentIndex: 4, moveToIndex: 1)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(reorderQueueRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.ReorderConstructionQueueItem, copy.Kind);
        Assert.AreEqual(789, copy.SubjectId);
        Assert.AreEqual(4, copy.TargetId);
        Assert.AreEqual(1f, copy.X);

        var rushQueueRequest = AuthoritativePlayerCommand.RushConstructionQueueItem(21, 2, 789,
                queueIndex: 4, maxAmount: 12.5f)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(rushQueueRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.RushConstructionQueueItem, copy.Kind);
        Assert.AreEqual(789, copy.SubjectId);
        Assert.AreEqual(4, copy.TargetId);
        Assert.AreEqual(12.5f, copy.X);

        var toggleRushRequest = AuthoritativePlayerCommand.ToggleConstructionRush(22, 2, 789,
                queueIndex: 4)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(toggleRushRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.ToggleConstructionRush, copy.Kind);
        Assert.AreEqual(789, copy.SubjectId);
        Assert.AreEqual(4, copy.TargetId);

        var goodsRequest = AuthoritativePlayerCommand.SetPlanetGoodsState(23, 2, 789,
                AuthoritativePlanetGoodsKind.Production, Planet.GoodState.EXPORT)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(goodsRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetPlanetGoodsState, copy.Kind);
        Assert.AreEqual(789, copy.SubjectId);
        Assert.AreEqual((int)AuthoritativePlanetGoodsKind.Production, copy.TargetId);
        Assert.AreEqual((int)Planet.GoodState.EXPORT, (int)copy.X);

        var prioritizedRequest = AuthoritativePlayerCommand.SetPlanetPrioritizedPort(24, 2, 789, true)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(prioritizedRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetPlanetPrioritizedPort, copy.Kind);
        Assert.AreEqual(789, copy.SubjectId);
        Assert.AreEqual(1, copy.TargetId);

        var planetBudgetRequest = AuthoritativePlayerCommand.SetPlanetManualBudget(25, 2, 789,
                AuthoritativePlanetBudgetKind.GroundDefense, 17.25f)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(planetBudgetRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetPlanetManualBudget, copy.Kind);
        Assert.AreEqual(789, copy.SubjectId);
        Assert.AreEqual((int)AuthoritativePlanetBudgetKind.GroundDefense, copy.TargetId);
        Assert.AreEqual(17.25f, copy.X);

        var governorOptions = AuthoritativePlanetGovernorOptions.GovOrbitals
                              | AuthoritativePlanetGovernorOptions.Quarantine
                              | AuthoritativePlanetGovernorOptions.SpecializedTradeHub;
        var governorOptionsRequest = AuthoritativePlayerCommand.SetPlanetGovernorOptions(26, 2, 789,
                governorOptions)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(governorOptionsRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetPlanetGovernorOptions, copy.Kind);
        Assert.AreEqual(789, copy.SubjectId);
        Assert.AreEqual((int)governorOptions, copy.TargetId);

        var fleetRequest = AuthoritativePlayerCommand.SetFleetAssignment(27, 2, 7,
                AuthoritativeFleetAssignmentMode.Replace, new[] { 99, 100 })
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(fleetRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetFleetAssignment, copy.Kind);
        Assert.AreEqual(7, copy.SubjectId);
        Assert.AreEqual((int)AuthoritativeFleetAssignmentMode.Replace, copy.TargetId);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseIdList(copy.Text, out int[] fleetShipIds));
        CollectionAssert.AreEqual(new[] { 99, 100 }, fleetShipIds);

        var moveFleetRequest = AuthoritativePlayerCommand.MoveFleet(28, 2, 7,
                new Vector2(12_345f, -67_890f), new Vector2(0f, -1f),
                MoveOrder.StandGround | MoveOrder.ForceReassembly)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(moveFleetRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.MoveFleet, copy.Kind);
        Assert.AreEqual(7, copy.SubjectId);
        Assert.AreEqual((int)(MoveOrder.StandGround | MoveOrder.ForceReassembly), copy.TargetId);
        Assert.AreEqual(12_345f, copy.X);
        Assert.AreEqual(-67_890f, copy.Y);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseVectorPayload(copy.Text, out Vector2 moveFleetDirection));
        Assert.AreEqual(new Vector2(0f, -1f), moveFleetDirection);

        var renameFleetRequest = AuthoritativePlayerCommand.RenameFleet(30, 2, 7, "Alpha Wing")
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(renameFleetRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.RenameFleet, copy.Kind);
        Assert.AreEqual(7, copy.SubjectId);
        Assert.AreEqual("Alpha Wing", copy.Text);

        var autoArrangeFleetRequest = AuthoritativePlayerCommand.AutoArrangeFleet(31, 2, 7)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(autoArrangeFleetRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.AutoArrangeFleet, copy.Kind);
        Assert.AreEqual(7, copy.SubjectId);

        var layoutNodes = new[]
        {
            new FleetDataNode
            {
                ShipName = "Alpha",
                RelativeFleetOffset = new Vector2(1_000f, -2_000f),
                DPSWeight = 0.82f,
                CombatState = CombatState.BroadsideLeft,
                OrdersRadius = 123_456f,
            },
            new FleetDataNode
            {
                ShipName = "Beta",
                RelativeFleetOffset = new Vector2(-3_000f, 4_000f),
                ArmoredWeight = 0.71f,
                CombatState = CombatState.GuardMode,
                OrdersRadius = 654_321f,
            },
        };
        var setLayoutRequest = AuthoritativePlayerCommand.SetFleetLayout(32, 2, 7, layoutNodes)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(setLayoutRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetFleetLayout, copy.Kind);
        Assert.AreEqual(7, copy.SubjectId);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseFleetLayout(copy.Text,
            out AuthoritativeFleetLayoutNode[] decodedLayout));
        Assert.AreEqual(2, decodedLayout.Length);
        Assert.AreEqual("Alpha", decodedLayout[0].ShipName);
        Assert.AreEqual(new Vector2(1_000f, -2_000f), decodedLayout[0].Offset);
        Assert.AreEqual(0.82f, decodedLayout[0].DpsWeight);
        Assert.AreEqual(CombatState.BroadsideLeft, decodedLayout[0].CombatState);
        Assert.AreEqual(123_456f, decodedLayout[0].OrdersRadius);
        Assert.AreEqual("Beta", decodedLayout[1].ShipName);
        Assert.AreEqual(new Vector2(-3_000f, 4_000f), decodedLayout[1].Offset);
        Assert.AreEqual(0.71f, decodedLayout[1].ArmoredWeight);
        Assert.AreEqual(CombatState.GuardMode, decodedLayout[1].CombatState);
        Assert.AreEqual(654_321f, decodedLayout[1].OrdersRadius);

        var deepSpaceBuildRequest = AuthoritativePlayerCommand.QueueDeepSpaceBuild(33, 2,
                "Platform Base mk1-a", new Vector2(10_000f, -20_000f), targetPlanetId: 5,
                targetSystemId: 6, tetherOffset: new Vector2(1_250f, -2_500f))
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(deepSpaceBuildRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.QueueDeepSpaceBuild, copy.Kind);
        Assert.AreEqual(5, copy.SubjectId);
        Assert.AreEqual(6, copy.TargetId);
        Assert.AreEqual(10_000f, copy.X);
        Assert.AreEqual(-20_000f, copy.Y);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseDeepSpaceBuildPayload(copy.Text,
            out string buildDesign, out Vector2 tetherOffset));
        Assert.AreEqual("Platform Base mk1-a", buildDesign);
        Assert.AreEqual(new Vector2(1_250f, -2_500f), tetherOffset);

        var cancelDeepSpaceBuildRequest = AuthoritativePlayerCommand.CancelDeepSpaceBuild(34, 2,
                "Platform Base mk1-a", GoalType.DeepSpaceConstruction, new Vector2(10_000f, -20_000f),
                targetPlanetId: 5, targetSystemId: 6)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(cancelDeepSpaceBuildRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.CancelDeepSpaceBuild, copy.Kind);
        Assert.AreEqual(5, copy.SubjectId);
        Assert.AreEqual(6, copy.TargetId);
        Assert.AreEqual(10_000f, copy.X);
        Assert.AreEqual(-20_000f, copy.Y);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseDeepSpaceCancelPayload(copy.Text,
            out buildDesign, out GoalType cancelGoalType));
        Assert.AreEqual("Platform Base mk1-a", buildDesign);
        Assert.AreEqual(GoalType.DeepSpaceConstruction, cancelGoalType);

        var loadPatrolRequest = AuthoritativePlayerCommand.LoadFleetPatrol(35, 2, 7, "Alpha Patrol")
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(loadPatrolRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.LoadFleetPatrol, copy.Kind);
        Assert.AreEqual(7, copy.SubjectId);
        Assert.AreEqual("Alpha Patrol", copy.Text);

        var specialOrderRequest = AuthoritativePlayerCommand.ShipSpecialOrder(28, 2, 99,
                AuthoritativeShipSpecialOrderType.Explore)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(specialOrderRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.ShipSpecialOrder, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual((int)AuthoritativeShipSpecialOrderType.Explore, copy.TargetId);

        var lifecycleOrderRequest = AuthoritativePlayerCommand.ShipLifecycleOrder(32, 2, 99,
                AuthoritativeShipLifecycleOrderType.Scrap)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(lifecycleOrderRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.ShipLifecycleOrder, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual((int)AuthoritativeShipLifecycleOrderType.Scrap, copy.TargetId);

        var stanceRequest = AuthoritativePlayerCommand.SetShipCombatStance(29, 2, 99,
                CombatState.HoldPosition)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(stanceRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetShipCombatStance, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual((int)CombatState.HoldPosition, copy.TargetId);

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

        var laborRequest = AuthoritativePlayerCommand.SetColonyLabor(14, 2, 456,
            0.25f, 0.5f, 0.25f, foodLocked: true, productionLocked: false, researchLocked: true)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(laborRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetColonyLabor, copy.Kind);
        Assert.AreEqual(456, copy.SubjectId);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseColonyLaborPayload(copy.Text,
            out float foodLabor, out float prodLabor, out float resLabor,
            out bool foodLock, out bool prodLock, out bool resLock));
        Assert.AreEqual(0.25f, foodLabor);
        Assert.AreEqual(0.5f, prodLabor);
        Assert.AreEqual(0.25f, resLabor);
        Assert.IsTrue(foodLock);
        Assert.IsFalse(prodLock);
        Assert.IsTrue(resLock);

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

        var start = new SessionStartMessage
        {
            FromPeer = 1,
            ProtocolVersion = 7,
            MatchSeed = 12345,
            RngSeed = 67890,
            InputDelay = 3,
            MaxTurns = 600,
            CommandEveryTurns = 2,
            GameSpeed = 1.5f,
            StartPaused = true,
            SettingsHash = "0xSTART",
            HostRacePreference = "United",
            JoinRacePreference = "Draylok",
            HostLoadoutTrait = "unused-host",
            JoinLoadoutTrait = "unused-join",
            HostFleet = "arena-host",
            JoinFleet = "arena-join",
            BuildHash = "0xBUILD",
            BuildSummary = "summary",
            IsAuthoritative4X = true,
            AuthoritativeHostPeerId = 2,
            AuthoritativeJoinPeerId = 3,
            GenerationSeed = 0x4B1B4B2,
            GalaxySize = (int)GalSize.Tiny,
            StarsCount = (int)RaceDesignScreen.StarsAbundance.Rare,
            GameMode = (int)RaceDesignScreen.GameMode.Sandbox,
            Difficulty = (int)GameDifficulty.Hard,
            NumOpponents = 1,
            Pace = 2.5f,
            TurnTimer = 4,
            ExtraPlanets = 1,
            StartingPlanetRichnessBonus = 1.25f,
            HostTraitOptions = "Brutal|Cybernetic",
            JoinTraitOptions = "Aquatic",
        };
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(start, toPeer: 3));
        var startCopy = (SessionStartMessage)decoded.Message;
        Assert.AreEqual(3, decoded.ToPeer);
        Assert.IsTrue(startCopy.IsAuthoritative4X);
        Assert.AreEqual(start.ProtocolVersion, startCopy.ProtocolVersion);
        Assert.AreEqual(start.MatchSeed, startCopy.MatchSeed);
        Assert.AreEqual(start.RngSeed, startCopy.RngSeed);
        Assert.AreEqual(start.InputDelay, startCopy.InputDelay);
        Assert.AreEqual(start.MaxTurns, startCopy.MaxTurns);
        Assert.AreEqual(start.CommandEveryTurns, startCopy.CommandEveryTurns);
        Assert.AreEqual(start.GameSpeed, startCopy.GameSpeed);
        Assert.AreEqual(start.StartPaused, startCopy.StartPaused);
        Assert.AreEqual(start.SettingsHash, startCopy.SettingsHash);
        Assert.AreEqual(start.HostRacePreference, startCopy.HostRacePreference);
        Assert.AreEqual(start.JoinRacePreference, startCopy.JoinRacePreference);
        Assert.AreEqual(start.HostFleet, startCopy.HostFleet);
        Assert.AreEqual(start.JoinFleet, startCopy.JoinFleet);
        Assert.AreEqual(start.BuildHash, startCopy.BuildHash);
        Assert.AreEqual(start.BuildSummary, startCopy.BuildSummary);
        Assert.AreEqual(start.AuthoritativeHostPeerId, startCopy.AuthoritativeHostPeerId);
        Assert.AreEqual(start.AuthoritativeJoinPeerId, startCopy.AuthoritativeJoinPeerId);
        Assert.AreEqual(start.GenerationSeed, startCopy.GenerationSeed);
        Assert.AreEqual(start.GalaxySize, startCopy.GalaxySize);
        Assert.AreEqual(start.StarsCount, startCopy.StarsCount);
        Assert.AreEqual(start.GameMode, startCopy.GameMode);
        Assert.AreEqual(start.Difficulty, startCopy.Difficulty);
        Assert.AreEqual(start.NumOpponents, startCopy.NumOpponents);
        Assert.AreEqual(start.Pace, startCopy.Pace);
        Assert.AreEqual(start.TurnTimer, startCopy.TurnTimer);
        Assert.AreEqual(start.ExtraPlanets, startCopy.ExtraPlanets);
        Assert.AreEqual(start.StartingPlanetRichnessBonus, startCopy.StartingPlanetRichnessBonus);
        Assert.AreEqual(start.HostTraitOptions, startCopy.HostTraitOptions);
        Assert.AreEqual(start.JoinTraitOptions, startCopy.JoinTraitOptions);

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
                $"S|{authority.Ship.Id}|{authority.Player.Id}|0|0|{(int)AIState.AttackTarget}|");
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
    public void Authoritative4XColonizationGoal_MarksCancelsAndSyncs_Headless()
    {
        const ulong Seed = 0xC010412EUL;
        BuiltWorld authority = BuildWorld(Seed, includeNeutralPlanet: true);
        BuiltWorld client = BuildWorld(Seed, includeNeutralPlanet: true);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;
            Assert.IsFalse(authority.Player.AI.HasGoal(g => g.IsColonizationGoal(authority.NeutralPlanet)));
            Assert.IsFalse(client.Player.AI.HasGoal(g => g.IsColonizationGoal(client.NeutralPlanet)));

            session.SubmitFromClient(AuthoritativePlayerCommand.SetColonizationGoal(9,
                authority.Player.Id, authority.NeutralPlanet.Id, enabled: true));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.Player.AI.HasGoal(g => g.IsColonizationGoal(authority.NeutralPlanet)));
            Assert.IsTrue(client.Player.AI.HasGoal(g => g.IsColonizationGoal(client.NeutralPlanet)));
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover manual colonization goals.");
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload,
                $"G|{authority.Player.Id}|MarkForColonization|{authority.NeutralPlanet.Id}|1|");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string markedDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetColonizationGoal(10,
                authority.Player.Id, authority.NeutralPlanet.Id, enabled: false));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsFalse(authority.Player.AI.HasGoal(g => g.IsColonizationGoal(authority.NeutralPlanet)));
            Assert.IsFalse(client.Player.AI.HasGoal(g => g.IsColonizationGoal(client.NeutralPlanet)));
            Assert.AreNotEqual(markedDigest, session.LastAuthoritySnapshot.SyncDigest,
                "Canceling a colonization goal must be visible in the sync digest.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string canceledDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetColonizationGoal(11,
                authority.Player.Id, authority.Planet.Id, enabled: true));
            Assert.IsFalse(session.LastResult.Accepted, "Owned planets must not be marked for colonization.");
            StringAssert.Contains(session.LastResult.Reason, "already owned");
            Assert.AreEqual(canceledDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XResearchQueue_AddMoveRemoveSync_Headless()
    {
        const ulong Seed = 0xA47E5EAUL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;
            string[] techs = ResearchCandidates(authority.Player, 4);

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueResearch(21, authority.Player.Id, techs[0]));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.Player.Research.IsQueued(techs[0]));
            Assert.IsTrue(client.Player.Research.IsQueued(techs[0]));
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover research queue additions.");
            AssertResearchQueuesEqual(authority.Player, client.Player);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            for (int i = 1; i < techs.Length; ++i)
            {
                session.SubmitFromClient(AuthoritativePlayerCommand.QueueResearch(21 + i,
                    authority.Player.Id, techs[i]));
                Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            }
            AssertResearchQueuesEqual(authority.Player, client.Player);
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, ResearchQueuePayloadPrefix(authority.Player));

            string beforeMoveDigest = session.LastAuthoritySnapshot.SyncDigest;
            (string movableUp, int upIndex) = FindQueuedResearch(authority.Player, i => authority.Player.Research.CanMoveUp(i));
            session.SubmitFromClient(AuthoritativePlayerCommand.MoveResearchQueueItem(30,
                authority.Player.Id, movableUp, AuthoritativeResearchQueueMove.Up));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(upIndex - 1, authority.Player.Research.IndexInQueue(movableUp));
            AssertResearchQueuesEqual(authority.Player, client.Player);
            Assert.AreNotEqual(beforeMoveDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover research queue order changes.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            (string movableDown, int downIndex) = FindQueuedResearch(authority.Player, i => authority.Player.Research.CanMoveDown(i));
            session.SubmitFromClient(AuthoritativePlayerCommand.MoveResearchQueueItem(31,
                authority.Player.Id, movableDown, AuthoritativeResearchQueueMove.Down));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(downIndex + 1, authority.Player.Research.IndexInQueue(movableDown));
            AssertResearchQueuesEqual(authority.Player, client.Player);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            (string topCandidate, int topIndex) = FindQueuedResearch(authority.Player, i => i > 0 && authority.Player.Research.CanMoveUp(i));
            session.SubmitFromClient(AuthoritativePlayerCommand.MoveResearchQueueItem(32,
                authority.Player.Id, topCandidate, AuthoritativeResearchQueueMove.ToTopWithPrereqs));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.Player.Research.IndexInQueue(topCandidate) < topIndex,
                "Move-to-top should move the tech upward while respecting prerequisite ordering.");
            AssertResearchQueuesEqual(authority.Player, client.Player);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string removeCandidate = authority.Player.data.ResearchQueue.ToArray().Last();
            session.SubmitFromClient(AuthoritativePlayerCommand.RemoveResearchQueueItem(33,
                authority.Player.Id, removeCandidate));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsFalse(authority.Player.Research.IsQueued(removeCandidate));
            Assert.IsFalse(client.Player.Research.IsQueued(removeCandidate));
            AssertResearchQueuesEqual(authority.Player, client.Player);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueResearch(34,
                authority.Player.Id, "Definitely Missing MP Research"));
            Assert.IsFalse(session.LastResult.Accepted, "Unknown tech must not enter the authoritative research queue.");
            StringAssert.Contains(session.LastResult.Reason, "not found");
            AssertResearchQueuesEqual(authority.Player, client.Player);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.RemoveResearchQueueItem(35,
                authority.Player.Id, removeCandidate));
            Assert.IsFalse(session.LastResult.Accepted, "Removing a non-queued tech must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "not queued");
            AssertResearchQueuesEqual(authority.Player, client.Player);
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
    public void Authoritative4XColonyTileBuildQueue_QueuesAtRequestedTile_Headless()
    {
        const ulong Seed = 0xB411D02UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;
            EnsureTwoBuildTiles(authority.Planet);
            EnsureTwoBuildTiles(client.Planet);
            Building buildable = PickBuildableBuilding(authority.Planet);

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueBuilding(39, authority.Player.Id,
                authority.Planet.Id, buildable.Name, tileX: 1, tileY: 0));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);

            QueueItem authorityItem = LastQueuedBuilding(authority.Planet, buildable.Name);
            QueueItem clientItem = LastQueuedBuilding(client.Planet, buildable.Name);
            Assert.IsNotNull(authorityItem.pgs, "Explicit tile placement must reserve the requested authority tile.");
            Assert.IsNotNull(clientItem.pgs, "Explicit tile placement must reserve the requested client tile.");
            Assert.AreEqual(1, authorityItem.pgs.X);
            Assert.AreEqual(0, authorityItem.pgs.Y);
            Assert.AreEqual(authorityItem.pgs.X, clientItem.pgs.X);
            Assert.AreEqual(authorityItem.pgs.Y, clientItem.pgs.Y);
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The canonical sync digest must cover explicit tile building queue mutations.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, "|1|0|");
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XColonyLabor_SyncsAndRejectsIllegalChanges_Headless()
    {
        const ulong Seed = 0x1AB0A11UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            authority.Planet.CType = Planet.ColonyType.Colony;
            client.Planet.CType = Planet.ColonyType.Colony;
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.SetColonyLabor(40, authority.Player.Id,
                authority.Planet.Id, 0.25f, 0.5f, 0.25f,
                foodLocked: true, productionLocked: false, researchLocked: true));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(0.25f, authority.Planet.Food.Percent);
            Assert.AreEqual(0.5f, authority.Planet.Prod.Percent);
            Assert.AreEqual(0.25f, authority.Planet.Res.Percent);
            Assert.IsTrue(authority.Planet.Food.PercentLock);
            Assert.IsFalse(authority.Planet.Prod.PercentLock);
            Assert.IsTrue(authority.Planet.Res.PercentLock);
            Assert.AreEqual(authority.Planet.Food.Percent, client.Planet.Food.Percent);
            Assert.AreEqual(authority.Planet.Prod.Percent, client.Planet.Prod.Percent);
            Assert.AreEqual(authority.Planet.Res.Percent, client.Planet.Res.Percent);
            Assert.AreEqual(authority.Planet.Food.PercentLock, client.Planet.Food.PercentLock);
            Assert.AreEqual(authority.Planet.Res.PercentLock, client.Planet.Res.PercentLock);
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover labor allocation and lock state changes.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetColonyLabor(41, authority.Enemy.Id,
                authority.Planet.Id, 0.25f, 0.5f, 0.25f,
                foodLocked: false, productionLocked: false, researchLocked: false));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not change another empire's colony labor.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(0.25f, authority.Planet.Food.Percent);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetColonyLabor(42, authority.Player.Id,
                authority.Planet.Id, 0.6f, 0.6f, 0.1f,
                foodLocked: false, productionLocked: false, researchLocked: false));
            Assert.IsFalse(session.LastResult.Accepted, "Labor allocations that do not sum to one must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "sum to 1");
            Assert.AreEqual(0.25f, authority.Planet.Food.Percent);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XPlanetGoodsState_SyncsAndRejectsIllegalChanges_Headless()
    {
        const ulong Seed = 0x600D57A7EUL;
        BuiltWorld authority = BuildWorld(Seed, extraPlayerPlanet: true);
        BuiltWorld client = BuildWorld(Seed, extraPlayerPlanet: true);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetGoodsState(80,
                authority.Player.Id, authority.Planet.Id, AuthoritativePlanetGoodsKind.Production,
                Planet.GoodState.IMPORT));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(Planet.GoodState.IMPORT, authority.Planet.PS);
            Assert.AreEqual(Planet.GoodState.IMPORT, client.Planet.PS);
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover planet production import/export state.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeProductionDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetGoodsState(81,
                authority.Player.Id, authority.Planet.Id, AuthoritativePlanetGoodsKind.Production,
                Planet.GoodState.EXPORT));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(Planet.GoodState.EXPORT, authority.Planet.PS);
            Assert.AreEqual(Planet.GoodState.EXPORT, client.Planet.PS);
            Assert.AreNotEqual(beforeProductionDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover planet production import/export state.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetGoodsState(82,
                authority.Enemy.Id, authority.Planet.Id, AuthoritativePlanetGoodsKind.Production,
                Planet.GoodState.STORE));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not change another empire's storage policy.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(Planet.GoodState.EXPORT, authority.Planet.PS);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            var invalidState = AuthoritativePlayerCommand.SetPlanetGoodsState(83,
                authority.Player.Id, authority.Planet.Id, AuthoritativePlanetGoodsKind.Production,
                Planet.GoodState.STORE);
            invalidState.Position = new Vector2(99f, 0f);
            session.SubmitFromClient(invalidState);
            Assert.IsFalse(session.LastResult.Accepted, "Unknown goods states must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "Unsupported planet goods state");
            Assert.AreEqual(Planet.GoodState.EXPORT, authority.Planet.PS);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsColonizationGoalWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xC010C1EUL;
        BuiltWorld world = BuildWorld(Seed, includeNeutralPlanet: true);

        try
        {
            var submitted = new List<AuthoritativePlayerCommand>();
            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 1490))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetColonizationGoal(world.Player,
                        world.NeutralPlanet, enabled: true));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(1490, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetColonizationGoal, submitted[0].Kind);
                Assert.AreEqual(world.Player.Id, submitted[0].EmpireId);
                Assert.AreEqual(world.NeutralPlanet.Id, submitted[0].SubjectId);
                Assert.AreEqual(1, submitted[0].TargetId);
                Assert.IsFalse(world.Player.AI.HasGoal(g => g.IsColonizationGoal(world.NeutralPlanet)),
                    "Passive MP clients must not locally mark colonization goals before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetColonizationGoal(world.Player,
                        world.NeutralPlanet, enabled: false));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(0, submitted[1].TargetId);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetColonizationGoal(world.Player,
                        world.Planet, enabled: true));
                Assert.AreEqual(2, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetColonizationGoal(world.Enemy,
                        world.NeutralPlanet, enabled: true));
                Assert.AreEqual(2, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsColonyLaborWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0x1AB0C11UL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            float originalFood = world.Planet.Food.Percent;
            float originalProd = world.Planet.Prod.Percent;
            float originalRes = world.Planet.Res.Percent;
            bool originalFoodLock = world.Planet.Food.PercentLock;
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 1500))
            {
                Assert.IsTrue(Authoritative4XClientContext.TrySubmitSetColonyLabor(world.Planet,
                    0.25f, 0.5f, 0.25f, foodLocked: true, productionLocked: false, researchLocked: true));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(1500, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetColonyLabor, submitted[0].Kind);
                Assert.AreEqual(world.Player.Id, submitted[0].EmpireId);
                Assert.AreEqual(world.Planet.Id, submitted[0].SubjectId);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParseColonyLaborPayload(submitted[0].Text,
                    out float foodLabor, out float prodLabor, out float resLabor,
                    out bool foodLock, out bool prodLock, out bool resLock));
                Assert.AreEqual(0.25f, foodLabor);
                Assert.AreEqual(0.5f, prodLabor);
                Assert.AreEqual(0.25f, resLabor);
                Assert.IsTrue(foodLock);
                Assert.IsFalse(prodLock);
                Assert.IsTrue(resLock);
                Assert.AreEqual(originalFood, world.Planet.Food.Percent,
                    "Passive MP clients must not locally change food labor before host acceptance.");
                Assert.AreEqual(originalProd, world.Planet.Prod.Percent,
                    "Passive MP clients must not locally change production labor before host acceptance.");
                Assert.AreEqual(originalRes, world.Planet.Res.Percent,
                    "Passive MP clients must not locally change research labor before host acceptance.");
                Assert.AreEqual(originalFoodLock, world.Planet.Food.PercentLock,
                    "Passive MP clients must not locally change labor locks before host acceptance.");

                Assert.IsFalse(Authoritative4XClientContext.TrySubmitSetColonyLabor(world.EnemyPlanet,
                    0.25f, 0.5f, 0.25f, foodLocked: false, productionLocked: false, researchLocked: false));
                Assert.AreEqual(1, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsPlanetGoodsStateWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0x600D57C1UL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            Assert.IsTrue(world.Planet.NonCybernetic,
                "The planet goods context proof needs a non-cybernetic colony to exercise food import/export.");
            Planet.GoodState originalFood = world.Planet.FS;
            Planet.GoodState originalProduction = world.Planet.PS;
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 1950))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetPlanetGoodsState(world.Planet,
                        AuthoritativePlanetGoodsKind.Food, Planet.GoodState.IMPORT));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(1950, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetPlanetGoodsState, submitted[0].Kind);
                Assert.AreEqual(world.Planet.Id, submitted[0].SubjectId);
                Assert.AreEqual((int)AuthoritativePlanetGoodsKind.Food, submitted[0].TargetId);
                Assert.AreEqual((int)Planet.GoodState.IMPORT, (int)submitted[0].Position.X);
                Assert.AreEqual(originalFood, world.Planet.FS,
                    "Passive MP clients must not locally change food storage policy before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetPlanetGoodsState(world.Planet,
                        AuthoritativePlanetGoodsKind.Production, Planet.GoodState.EXPORT));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(1951, submitted[1].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetPlanetGoodsState, submitted[1].Kind);
                Assert.AreEqual((int)AuthoritativePlanetGoodsKind.Production, submitted[1].TargetId);
                Assert.AreEqual((int)Planet.GoodState.EXPORT, (int)submitted[1].Position.X);
                Assert.AreEqual(originalProduction, world.Planet.PS,
                    "Passive MP clients must not locally change production storage policy before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetPlanetGoodsState(world.EnemyPlanet,
                        AuthoritativePlanetGoodsKind.Production, Planet.GoodState.IMPORT));
                Assert.AreEqual(2, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XPlanetGovernorSettings_SyncsAndRejectsIllegalChanges_Headless()
    {
        const ulong Seed = 0xB0D6E701UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            authority.Planet.HasSpacePort = true;
            client.Planet.HasSpacePort = true;
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetManualBudget(90,
                authority.Player.Id, authority.Planet.Id, AuthoritativePlanetBudgetKind.Civilian, 12.5f));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(12.5f, authority.Planet.ManualCivilianBudget);
            Assert.AreEqual(12.5f, client.Planet.ManualCivilianBudget);
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover manual colony budget overrides.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetManualBudget(91,
                authority.Player.Id, authority.Planet.Id, AuthoritativePlanetBudgetKind.GroundDefense, 3.25f));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetManualBudget(92,
                authority.Player.Id, authority.Planet.Id, AuthoritativePlanetBudgetKind.SpaceDefense, 4.5f));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(3.25f, authority.Planet.ManualGrdDefBudget);
            Assert.AreEqual(4.5f, authority.Planet.ManualSpcDefBudget);
            Assert.AreEqual(authority.Planet.ManualGrdDefBudget, client.Planet.ManualGrdDefBudget);
            Assert.AreEqual(authority.Planet.ManualSpcDefBudget, client.Planet.ManualSpcDefBudget);

            string beforePortDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetPrioritizedPort(93,
                authority.Player.Id, authority.Planet.Id, true));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.Planet.PrioritizedPort);
            Assert.IsTrue(client.Planet.PrioritizedPort);
            Assert.IsTrue(authority.Player.PlayerPrioritizedPorts.Contains(authority.Planet));
            Assert.AreNotEqual(beforePortDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover prioritized-port changes.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            var governorOptions = AuthoritativePlanetGovernorOptions.GovOrbitals
                                  | AuthoritativePlanetGovernorOptions.AutoBuildTroops
                                  | AuthoritativePlanetGovernorOptions.Quarantine
                                  | AuthoritativePlanetGovernorOptions.ManualOrbitals
                                  | AuthoritativePlanetGovernorOptions.GovGroundDefense
                                  | AuthoritativePlanetGovernorOptions.SpecializedTradeHub;
            string beforeGovernorOptionsDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetGovernorOptions(94,
                authority.Player.Id, authority.Planet.Id, governorOptions));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            AssertGovernorOptions(authority.Planet, expectedGovOrbitals: true, expectedAutoTroops: true,
                expectedNoScrap: false, expectedQuarantine: true, expectedManualOrbitals: true,
                expectedGovGround: true, expectedSpecializedTradeHub: true);
            AssertGovernorOptions(client.Planet, expectedGovOrbitals: true, expectedAutoTroops: true,
                expectedNoScrap: false, expectedQuarantine: true, expectedManualOrbitals: true,
                expectedGovGround: true, expectedSpecializedTradeHub: true);
            Assert.AreNotEqual(beforeGovernorOptionsDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover governor option flag changes.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetManualBudget(95,
                authority.Enemy.Id, authority.Planet.Id, AuthoritativePlanetBudgetKind.Civilian, 1f));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not change another empire's governor budget.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(12.5f, authority.Planet.ManualCivilianBudget);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetManualBudget(96,
                authority.Player.Id, authority.Planet.Id, AuthoritativePlanetBudgetKind.Civilian, -1f));
            Assert.IsFalse(session.LastResult.Accepted, "Negative manual budgets must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "finite non-negative");
            Assert.AreEqual(12.5f, authority.Planet.ManualCivilianBudget);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            authority.Planet.HasSpacePort = false;
            client.Planet.HasSpacePort = false;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetPrioritizedPort(97,
                authority.Player.Id, authority.Planet.Id, true));
            Assert.IsFalse(session.LastResult.Accepted,
                "A planet without a space port must not be marked as a prioritized port.");
            StringAssert.Contains(session.LastResult.Reason, "space port");
            Assert.IsTrue(authority.Planet.PrioritizedPort,
                "Rejected prioritized-port requests must not mutate the existing port preference.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectedGovernorDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetGovernorOptions(98,
                authority.Enemy.Id, authority.Planet.Id, AuthoritativePlanetGovernorOptions.Quarantine));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not change another empire's governor options.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            AssertGovernorOptions(authority.Planet, expectedGovOrbitals: true, expectedAutoTroops: true,
                expectedNoScrap: false, expectedQuarantine: true, expectedManualOrbitals: true,
                expectedGovGround: true, expectedSpecializedTradeHub: true);
            Assert.AreEqual(beforeRejectedGovernorDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetGovernorOptions(99,
                authority.Player.Id, authority.Planet.Id, (AuthoritativePlanetGovernorOptions)(1 << 12)));
            Assert.IsFalse(session.LastResult.Accepted, "Unsupported governor option bits must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "Unsupported planet governor option flags");
            AssertGovernorOptions(authority.Planet, expectedGovOrbitals: true, expectedAutoTroops: true,
                expectedNoScrap: false, expectedQuarantine: true, expectedManualOrbitals: true,
                expectedGovGround: true, expectedSpecializedTradeHub: true);
            Assert.AreEqual(beforeRejectedGovernorDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }

        static void AssertGovernorOptions(Planet planet, bool expectedGovOrbitals, bool expectedAutoTroops,
            bool expectedNoScrap, bool expectedQuarantine, bool expectedManualOrbitals, bool expectedGovGround,
            bool expectedSpecializedTradeHub)
        {
            Assert.AreEqual(expectedGovOrbitals, planet.GovOrbitals);
            Assert.AreEqual(expectedAutoTroops, planet.AutoBuildTroops);
            Assert.AreEqual(expectedNoScrap, planet.DontScrapBuildings);
            Assert.AreEqual(expectedQuarantine, planet.Quarantine);
            Assert.AreEqual(expectedManualOrbitals, planet.ManualOrbitals);
            Assert.AreEqual(expectedGovGround, planet.GovGroundDefense);
            Assert.AreEqual(expectedSpecializedTradeHub, planet.SpecializedTradeHub);
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsGovernorSettingsWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xB0D6E7C1UL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            world.Planet.HasSpacePort = true;
            float originalCivilian = world.Planet.ManualCivilianBudget;
            bool originalPrioritized = world.Planet.PrioritizedPort;
            world.Planet.GovOrbitals = true;
            world.Planet.AutoBuildTroops = true;
            world.Planet.DontScrapBuildings = false;
            world.Planet.Quarantine = true;
            world.Planet.ManualOrbitals = false;
            world.Planet.GovGroundDefense = true;
            world.Planet.SetSpecializedTradeHub(true);
            var expectedGovernorOptions = AuthoritativePlanetGovernorOptions.GovOrbitals
                                          | AuthoritativePlanetGovernorOptions.AutoBuildTroops
                                          | AuthoritativePlanetGovernorOptions.Quarantine
                                          | AuthoritativePlanetGovernorOptions.GovGroundDefense
                                          | AuthoritativePlanetGovernorOptions.SpecializedTradeHub;
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2050))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetPlanetManualBudget(world.Planet,
                        AuthoritativePlanetBudgetKind.Civilian, 9.75f));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2050, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetPlanetManualBudget, submitted[0].Kind);
                Assert.AreEqual(world.Planet.Id, submitted[0].SubjectId);
                Assert.AreEqual((int)AuthoritativePlanetBudgetKind.Civilian, submitted[0].TargetId);
                Assert.AreEqual(9.75f, submitted[0].Position.X);
                Assert.AreEqual(originalCivilian, world.Planet.ManualCivilianBudget,
                    "Passive MP clients must not locally change manual governor budgets before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetPlanetPrioritizedPort(world.Planet, true));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(2051, submitted[1].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetPlanetPrioritizedPort, submitted[1].Kind);
                Assert.AreEqual(1, submitted[1].TargetId);
                Assert.AreEqual(originalPrioritized, world.Planet.PrioritizedPort,
                    "Passive MP clients must not locally change prioritized-port state before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetPlanetGovernorOptions(world.Planet));
                Assert.AreEqual(3, submitted.Count);
                Assert.AreEqual(2052, submitted[2].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetPlanetGovernorOptions, submitted[2].Kind);
                Assert.AreEqual(world.Planet.Id, submitted[2].SubjectId);
                Assert.AreEqual((int)expectedGovernorOptions, submitted[2].TargetId);
                Assert.IsTrue(world.Planet.Quarantine);
                Assert.IsTrue(world.Planet.SpecializedTradeHub);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetPlanetManualBudget(world.EnemyPlanet,
                        AuthoritativePlanetBudgetKind.Civilian, 1f));
                Assert.AreEqual(3, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetPlanetGovernorOptions(world.EnemyPlanet));
                Assert.AreEqual(3, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XFleetAssignment_ReplacesAddsClearsAndSyncs_Headless()
    {
        const ulong Seed = 0xF1EE7A55UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetAssignment(100,
                authority.Player.Id, fleetKey: 3, AuthoritativeFleetAssignmentMode.Replace,
                new[] { authority.Ship.Id }));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Fleet authorityFleet = authority.Player.GetFleetOrNull(3);
            Fleet clientFleet = client.Player.GetFleetOrNull(3);
            Assert.IsNotNull(authorityFleet);
            Assert.IsNotNull(clientFleet);
            Assert.AreEqual(1, authorityFleet.Ships.Count);
            Assert.AreEqual(1, clientFleet.Ships.Count);
            Assert.AreSame(authorityFleet, authority.Ship.Fleet);
            Assert.AreSame(clientFleet, client.Ship.Fleet);
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover fleet assignment state.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeAddDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetAssignment(101,
                authority.Player.Id, fleetKey: 3, AuthoritativeFleetAssignmentMode.Add,
                new[] { authority.WingShip.Id }));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(2, authorityFleet.Ships.Count);
            Assert.AreEqual(2, clientFleet.Ships.Count);
            Assert.AreSame(authorityFleet, authority.WingShip.Fleet);
            Assert.AreSame(clientFleet, client.WingShip.Fleet);
            Assert.AreNotEqual(beforeAddDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover added fleet membership.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetAssignment(102,
                authority.Enemy.Id, fleetKey: 3, AuthoritativeFleetAssignmentMode.Replace,
                new[] { authority.Ship.Id }));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not assign another empire's ships.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(2, authorityFleet.Ships.Count);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetAssignment(103,
                authority.Player.Id, fleetKey: 3, AuthoritativeFleetAssignmentMode.Replace,
                new[] { authority.Ship.Id, authority.Ship.Id }));
            Assert.IsFalse(session.LastResult.Accepted, "Duplicate ship ids must not be accepted.");
            StringAssert.Contains(session.LastResult.Reason, "unique");
            Assert.AreEqual(2, authorityFleet.Ships.Count);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetAssignment(104,
                authority.Player.Id, fleetKey: 3, AuthoritativeFleetAssignmentMode.Clear,
                Array.Empty<int>()));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsNull(authority.Player.GetFleetOrNull(3));
            Assert.IsNull(client.Player.GetFleetOrNull(3));
            Assert.IsNull(authority.Ship.Fleet);
            Assert.IsNull(client.Ship.Fleet);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XFleetMove_UsesFleetMoveToAndSyncs_Headless()
    {
        const ulong Seed = 0xF1EE700DUL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetAssignment(120,
                authority.Player.Id, fleetKey: 3, AuthoritativeFleetAssignmentMode.Replace,
                new[] { authority.Ship.Id, authority.WingShip.Id }));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);

            Fleet authorityFleet = authority.Player.GetFleetOrNull(3);
            Fleet clientFleet = client.Player.GetFleetOrNull(3);
            Assert.IsNotNull(authorityFleet);
            Assert.IsNotNull(clientFleet);
            authorityFleet.CreatePatrol(TestPatrolWaypoints(authorityFleet.FinalPosition));
            clientFleet.CreatePatrol(TestPatrolWaypoints(clientFleet.FinalPosition));
            Assert.IsTrue(authorityFleet.HasPatrolPlan);
            Assert.IsTrue(clientFleet.HasPatrolPlan);
            string beforeMoveDigest = session.LastAuthoritySnapshot.SyncDigest;
            string patrolDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;
            Assert.AreEqual(patrolDigest, AuthoritativeStateSnapshot.Capture(client.Screen, 0).SyncDigest,
                "The patrol setup must start synchronized before the authoritative move clears it.");

            Vector2 destination = new(90_000f, -24_000f);
            Vector2 direction = new(0f, -1f);
            MoveOrder order = MoveOrder.StandGround | MoveOrder.ForceReassembly;
            session.SubmitFromClient(AuthoritativePlayerCommand.MoveFleet(121,
                authority.Player.Id, fleetKey: 3, destination, direction, order));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);

            Assert.AreEqual(destination, authorityFleet.FinalPosition);
            Assert.AreEqual(direction, authorityFleet.FinalDirection);
            Assert.AreEqual(destination, clientFleet.FinalPosition);
            Assert.AreEqual(direction, clientFleet.FinalDirection);
            Assert.AreEqual(AIState.FormationMoveTo, authority.Ship.AI.State);
            Assert.AreEqual(AIState.FormationMoveTo, authority.WingShip.AI.State);
            Assert.AreEqual(authorityFleet.FinalPosition + authority.Ship.FleetOffset, authority.Ship.AI.MovePosition);
            Assert.AreEqual(authorityFleet.FinalPosition + authority.WingShip.FleetOffset, authority.WingShip.AI.MovePosition);
            Assert.AreEqual(clientFleet.FinalPosition + client.Ship.FleetOffset, client.Ship.AI.MovePosition);
            Assert.AreEqual(clientFleet.FinalPosition + client.WingShip.FleetOffset, client.WingShip.AI.MovePosition);
            Assert.IsFalse(authorityFleet.HasPatrolPlan,
                "Accepted fleet movement should clear an active patrol on the authority.");
            Assert.IsFalse(clientFleet.HasPatrolPlan,
                "Accepted fleet movement should clear the replicated patrol state.");
            Assert.AreNotEqual(beforeMoveDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover fleet destination and formation movement.");
            Assert.AreNotEqual(patrolDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover fleet patrol clearing.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.MoveFleet(122,
                authority.Enemy.Id, fleetKey: 3, destination + new Vector2(1_000f, 0f), direction, order));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not move another empire's fleet.");
            StringAssert.Contains(session.LastResult.Reason, "not found");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.MoveFleet(123,
                authority.Player.Id, fleetKey: 3, destination + new Vector2(2_000f, 0f), direction,
                MoveOrder.Pursue));
            Assert.IsFalse(session.LastResult.Accepted, "Unsupported internal movement flags must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "not supported");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.MoveFleet(124,
                authority.Player.Id, fleetKey: 3, destination + new Vector2(3_000f, 0f), Vector2.Zero, order));
            Assert.IsFalse(session.LastResult.Accepted, "A zero fleet facing vector must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "direction");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
            Assert.AreEqual(destination, authorityFleet.FinalPosition,
                "Rejected fleet movement must not mutate the authoritative fleet target.");
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XFleetRename_ValidatesAndSyncs_Headless()
    {
        const ulong Seed = 0xF1EE4A11UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetAssignment(130,
                authority.Player.Id, fleetKey: 3, AuthoritativeFleetAssignmentMode.Replace,
                new[] { authority.Ship.Id, authority.WingShip.Id }));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);

            Fleet authorityFleet = authority.Player.GetFleetOrNull(3);
            Fleet clientFleet = client.Player.GetFleetOrNull(3);
            Assert.IsNotNull(authorityFleet);
            Assert.IsNotNull(clientFleet);
            string beforeRenameDigest = session.LastAuthoritySnapshot.SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.RenameFleet(131,
                authority.Player.Id, fleetKey: 3, "  Alpha Wing  "));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual("Alpha Wing", authorityFleet.Name);
            Assert.AreEqual("Alpha Wing", clientFleet.Name);
            Assert.AreNotEqual(beforeRenameDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover fleet names.");
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, "|Alpha Wing|");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.RenameFleet(132,
                authority.Player.Id, fleetKey: 3, ""));
            Assert.IsFalse(session.LastResult.Accepted, "Empty fleet names must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "empty");
            Assert.AreEqual("Alpha Wing", authorityFleet.Name);
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.RenameFleet(133,
                authority.Player.Id, fleetKey: 3, new string('A', 41)));
            Assert.IsFalse(session.LastResult.Accepted, "Overlong fleet names must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "too long");
            Assert.AreEqual("Alpha Wing", authorityFleet.Name);
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.RenameFleet(134,
                authority.Player.Id, fleetKey: 3, "Bad\nName"));
            Assert.IsFalse(session.LastResult.Accepted, "Control characters must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "control");
            Assert.AreEqual("Alpha Wing", authorityFleet.Name);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XFleetAutoArrange_UsesFleetLayoutAndSyncs_Headless()
    {
        const ulong Seed = 0xF1EE4A12UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetAssignment(140,
                authority.Player.Id, fleetKey: 3, AuthoritativeFleetAssignmentMode.Replace,
                new[] { authority.Ship.Id, authority.WingShip.Id }));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);

            Fleet authorityFleet = authority.Player.GetFleetOrNull(3);
            Fleet clientFleet = client.Player.GetFleetOrNull(3);
            Assert.IsNotNull(authorityFleet);
            Assert.IsNotNull(clientFleet);
            ScrambleFleetLayout(authorityFleet);
            ScrambleFleetLayout(clientFleet);

            string beforeArrangeDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;
            Assert.AreEqual(beforeArrangeDigest, AuthoritativeStateSnapshot.Capture(client.Screen, 0).SyncDigest,
                "The test setup must scramble both replicas identically before issuing the command.");
            Vector2 scrambledOffset = authorityFleet.DataNodes[0].RelativeFleetOffset;

            session.SubmitFromClient(AuthoritativePlayerCommand.AutoArrangeFleet(141,
                authority.Player.Id, fleetKey: 3));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreNotEqual(scrambledOffset, authorityFleet.DataNodes[0].RelativeFleetOffset,
                "AutoArrangeFleet must invoke the real fleet layout path, not accept as a no-op.");
            Assert.AreEqual(authorityFleet.DataNodes[0].RelativeFleetOffset,
                clientFleet.DataNodes[0].RelativeFleetOffset);
            Assert.AreNotEqual(beforeArrangeDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover fleet node offsets.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.AutoArrangeFleet(142,
                authority.Player.Id, fleetKey: 4));
            Assert.IsFalse(session.LastResult.Accepted, "Missing fleets must not be auto-arranged.");
            StringAssert.Contains(session.LastResult.Reason, "not found or empty");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }

        static void ScrambleFleetLayout(Fleet fleet)
        {
            Assert.IsTrue(fleet.DataNodes.Count >= 2, "Auto-arrange proof needs at least two nodes.");
            fleet.DataNodes[0].RelativeFleetOffset = new Vector2(11_000f, -4_000f);
            fleet.DataNodes[1].RelativeFleetOffset = new Vector2(-8_000f, 7_500f);
            foreach (FleetDataNode node in fleet.DataNodes)
            {
                if (node.Ship == null)
                    continue;
                node.Ship.RelativeFleetOffset = node.RelativeFleetOffset;
                node.Ship.FleetOffset = node.RelativeFleetOffset;
            }
        }
    }

    [TestMethod]
    public void Authoritative4XDeepSpaceBuild_QueuesGoalAndSyncs_Headless()
    {
        const ulong Seed = 0xD3355A4CUL;
        BuiltWorld authority = BuildWorld(Seed, includePlatform: true);
        BuiltWorld client = BuildWorld(Seed, includePlatform: true);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            IShipDesign design = PickBuildableDeepSpacePlatform(authority.Player);
            Vector2 buildPos = authority.Planet.Position + new Vector2(85_000f, 42_000f);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueDeepSpaceBuild(145,
                authority.Player.Id, design.Name, buildPos));

            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Goal authorityGoal = authority.Player.AI.Goals.FirstOrDefault(g =>
                g is BuildConstructionShip && g.ToBuild?.Name == design.Name && g.BuildPosition == buildPos);
            Goal clientGoal = client.Player.AI.Goals.FirstOrDefault(g =>
                g is BuildConstructionShip && g.ToBuild?.Name == design.Name && g.BuildPosition == buildPos);
            Assert.IsNotNull(authorityGoal, "Authority must queue the real deep-space construction goal.");
            Assert.IsNotNull(clientGoal, "Replica must receive the same accepted deep-space construction goal.");
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "Deep-space build goals must be covered by the authoritative sync digest.");
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, "|DeepSpace|");
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, design.Name);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeCancelDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.CancelDeepSpaceBuild(146,
                authority.Player.Id, design.Name, authorityGoal.Type, authorityGoal.BuildPosition,
                authorityGoal.TargetPlanet?.Id ?? 0, 0));

            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsFalse(HasDeepSpaceGoal(authority.Player, design.Name, buildPos),
                "Authority must remove the canceled deep-space build goal.");
            Assert.IsFalse(HasDeepSpaceGoal(client.Player, design.Name, buildPos),
                "Replica must remove the canceled deep-space build goal after host acceptance.");
            Assert.AreNotEqual(beforeCancelDigest, session.LastAuthoritySnapshot.SyncDigest,
                "Deep-space cancel must move the authoritative digest.");
            Assert.IsFalse(session.LastAuthoritySnapshot.Payload.Contains("|DeepSpace|"),
                "Canceled deep-space goals must leave the canonical goal payload.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.QueueDeepSpaceBuild(147,
                authority.Player.Id, "missing-buildable-station", buildPos));
            Assert.IsFalse(session.LastResult.Accepted, "Unknown deep-space designs must be rejected.");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.CancelDeepSpaceBuild(148,
                authority.Player.Id, design.Name, GoalType.DeepSpaceConstruction, buildPos));
            Assert.IsFalse(session.LastResult.Accepted, "Canceling a missing deep-space goal must be rejected.");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }

        static bool HasDeepSpaceGoal(Empire empire, string designName, Vector2 buildPos)
            => empire.AI.Goals.Any(g => g is BuildConstructionShip
                && string.Equals(g.ToBuild?.Name, designName, StringComparison.Ordinal)
                && g.BuildPosition == buildPos);
    }

    [TestMethod]
    public void Authoritative4XFleetLayout_ReplacesNodesAndSyncs_Headless()
    {
        const ulong Seed = 0xF1EE4A14UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;
            IShipDesign designOnly = PickMobileBuildableShip(authority.Player);
            var requested = new[]
            {
                new FleetDataNode
                {
                    Ship = authority.Ship,
                    ShipName = authority.Ship.Name,
                    RelativeFleetOffset = new Vector2(12_500f, -6_000f),
                    VultureWeight = 0.12f,
                    AttackShieldedWeight = 0.23f,
                    AssistWeight = 0.34f,
                    DefenderWeight = 0.45f,
                    DPSWeight = 0.56f,
                    SizeWeight = 0.67f,
                    ArmoredWeight = 0.78f,
                    CombatState = CombatState.OrbitRight,
                    OrdersRadius = 222_222f,
                },
                new FleetDataNode
                {
                    ShipName = designOnly.Name,
                    RelativeFleetOffset = new Vector2(-8_000f, 9_250f),
                    CombatState = CombatState.HoldPosition,
                    OrdersRadius = 333_333f,
                },
            };

            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetLayout(146,
                authority.Player.Id, fleetKey: 3, requested));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);

            Fleet authorityFleet = authority.Player.GetFleetOrNull(3);
            Fleet clientFleet = client.Player.GetFleetOrNull(3);
            Assert.IsNotNull(authorityFleet);
            Assert.IsNotNull(clientFleet);
            Assert.AreEqual(2, authorityFleet.DataNodes.Count);
            Assert.AreEqual(2, clientFleet.DataNodes.Count);
            Assert.AreEqual(1, authorityFleet.Ships.Count);
            Assert.AreEqual(1, clientFleet.Ships.Count);
            Assert.AreSame(authorityFleet, authority.Ship.Fleet);
            Assert.AreSame(clientFleet, client.Ship.Fleet);
            Assert.AreEqual(new Vector2(12_500f, -6_000f), authorityFleet.DataNodes[0].RelativeFleetOffset);
            Assert.AreEqual(authorityFleet.DataNodes[0].RelativeFleetOffset,
                clientFleet.DataNodes[0].RelativeFleetOffset);
            Assert.AreEqual(0.12f, authorityFleet.DataNodes[0].VultureWeight);
            Assert.AreEqual(0.23f, authorityFleet.DataNodes[0].AttackShieldedWeight);
            Assert.AreEqual(0.34f, authorityFleet.DataNodes[0].AssistWeight);
            Assert.AreEqual(0.45f, authorityFleet.DataNodes[0].DefenderWeight);
            Assert.AreEqual(0.56f, authorityFleet.DataNodes[0].DPSWeight);
            Assert.AreEqual(0.67f, authorityFleet.DataNodes[0].SizeWeight);
            Assert.AreEqual(0.78f, authorityFleet.DataNodes[0].ArmoredWeight);
            Assert.AreEqual(CombatState.OrbitRight, authorityFleet.DataNodes[0].CombatState);
            Assert.AreEqual(222_222f, authorityFleet.DataNodes[0].OrdersRadius);
            Assert.AreEqual(authorityFleet.DataNodes[0].DPSWeight, clientFleet.DataNodes[0].DPSWeight);
            Assert.AreEqual(authorityFleet.DataNodes[0].CombatState, clientFleet.DataNodes[0].CombatState);
            Assert.AreEqual(authorityFleet.DataNodes[0].OrdersRadius, clientFleet.DataNodes[0].OrdersRadius);
            Assert.AreEqual(designOnly.Name, authorityFleet.DataNodes[1].ShipName);
            Assert.IsNull(authorityFleet.DataNodes[1].Ship);
            Assert.AreEqual(CombatState.HoldPosition, authorityFleet.DataNodes[1].CombatState);
            Assert.AreEqual(333_333f, authorityFleet.DataNodes[1].OrdersRadius);
            Assert.AreEqual(authorityFleet.DataNodes[1].ShipName, clientFleet.DataNodes[1].ShipName);
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover replaced fleet layout nodes.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
            Assert.AreEqual(0, authority.Player.AllFleets.Count(f => f.Key == 0),
                "SetFleetLayout must not leave stale key-0 fleets after creating a replacement.");
            Assert.AreEqual(1, authority.Player.AllFleets.Count(f => f.Key == 3));
            Assert.AreEqual(0, client.Player.AllFleets.Count(f => f.Key == 0));
            Assert.AreEqual(1, client.Player.AllFleets.Count(f => f.Key == 3));

            string firstLayoutDigest = session.LastAuthoritySnapshot.SyncDigest;
            requested[0].RelativeFleetOffset = new Vector2(15_000f, -7_500f);
            requested[0].DPSWeight = 0.91f;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetLayout(147,
                authority.Player.Id, fleetKey: 3, requested));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreNotEqual(firstLayoutDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover fleet node tactical fields, not just ship names.");
            Assert.AreEqual(0.91f, authority.Player.GetFleetOrNull(3).DataNodes[0].DPSWeight);
            Assert.AreEqual(0, authority.Player.AllFleets.Count(f => f.Key == 0),
                "Repeated layout replacement must remove the old fleet object instead of leaking it.");
            Assert.AreEqual(1, authority.Player.AllFleets.Count(f => f.Key == 3));

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            var enemyLayout = new[]
            {
                new FleetDataNode
                {
                    Ship = authority.EnemyShip,
                    ShipName = authority.EnemyShip.Name,
                    RelativeFleetOffset = Vector2.Zero,
                },
            };
            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetLayout(148,
                authority.Player.Id, fleetKey: 3, enemyLayout));
            Assert.IsFalse(session.LastResult.Accepted, "Fleet layout must reject ships owned by another empire.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetLayout(149,
                authority.Player.Id, fleetKey: 3, Array.Empty<FleetDataNode>()));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsNull(authority.Player.GetFleetOrNull(3));
            Assert.IsNull(client.Player.GetFleetOrNull(3));
            Assert.IsNull(authority.Ship.Fleet);
            Assert.IsNull(client.Ship.Fleet);
            Assert.AreEqual(0, authority.Player.AllFleets.Count(f => f.Key == 0));
            Assert.AreEqual(0, client.Player.AllFleets.Count(f => f.Key == 0));
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XFleetLoadPatrol_LoadsSavedPlanAndSyncs_Headless()
    {
        const ulong Seed = 0xF1EE4A13UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetAssignment(145,
                authority.Player.Id, fleetKey: 3, AuthoritativeFleetAssignmentMode.Replace,
                new[] { authority.Ship.Id, authority.WingShip.Id }));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);

            Fleet authorityFleet = authority.Player.GetFleetOrNull(3);
            Fleet clientFleet = client.Player.GetFleetOrNull(3);
            Assert.IsNotNull(authorityFleet);
            Assert.IsNotNull(clientFleet);
            FleetPatrol authorityPatrol = authority.Player.AddPatrolRoute(authorityFleet,
                TestPatrolWaypoints(authorityFleet.FinalPosition));
            FleetPatrol clientPatrol = client.Player.AddPatrolRoute(clientFleet,
                TestPatrolWaypoints(clientFleet.FinalPosition));
            Assert.AreEqual(authorityPatrol.Name, clientPatrol.Name);
            Assert.IsFalse(authorityFleet.HasPatrolPlan);
            Assert.IsFalse(clientFleet.HasPatrolPlan);

            AuthoritativeStateSnapshot savedPlanSnapshot = AuthoritativeStateSnapshot.Capture(authority.Screen, 0);
            string savedPlanDigest = savedPlanSnapshot.SyncDigest;
            Assert.AreEqual(savedPlanDigest, AuthoritativeStateSnapshot.Capture(client.Screen, 0).SyncDigest,
                "Saved patrol plans must start synchronized before a client loads one.");
            StringAssert.Contains(savedPlanSnapshot.Payload, $"FP|{authority.Player.Id}|{authorityPatrol.Name}");

            session.SubmitFromClient(AuthoritativePlayerCommand.LoadFleetPatrol(146,
                authority.Player.Id, fleetKey: 3, authorityPatrol.Name));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);

            Assert.IsTrue(authorityFleet.HasPatrolPlan);
            Assert.IsTrue(clientFleet.HasPatrolPlan);
            Assert.AreEqual(authorityPatrol.Name, authorityFleet.Patrol.Name);
            Assert.AreEqual(clientPatrol.Name, clientFleet.Patrol.Name);
            Assert.AreEqual(AIState.FormationMoveTo, authority.Ship.AI.State,
                "Loading a saved patrol must run through the real Fleet.LoadPatrol/DoPatrol path.");
            Assert.AreNotEqual(savedPlanDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover active fleet patrol assignment.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.LoadFleetPatrol(147,
                authority.Player.Id, fleetKey: 3, "Missing Patrol"));
            Assert.IsFalse(session.LastResult.Accepted, "Missing saved patrol plans must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "not found");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.LoadFleetPatrol(148,
                authority.Enemy.Id, fleetKey: 3, authorityPatrol.Name));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not load a patrol onto another empire's fleet.");
            StringAssert.Contains(session.LastResult.Reason, "not found or empty");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XShipSpecialOrder_ExploreSyncsAndRejectsInvalidRequests_Headless()
    {
        const ulong Seed = 0xE7010EUL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.ShipSpecialOrder(130,
                authority.Player.Id, authority.Ship.Id, AuthoritativeShipSpecialOrderType.Explore));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(AIState.Explore, authority.Ship.AI.State);
            Assert.AreEqual(AIState.Explore, client.Ship.AI.State);
            Assert.IsTrue(authority.Ship.AI.OrderQueue.ToArray().Any(g => g.Plan == ShipAI.Plan.Explore),
                "The authority should apply Explore through the real ship AI order queue.");
            Assert.IsTrue(client.Ship.AI.OrderQueue.ToArray().Any(g => g.Plan == ShipAI.Plan.Explore),
                "The client replica should replay the accepted Explore order deterministically.");
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover ship special-order state.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.ShipSpecialOrder(131,
                authority.Enemy.Id, authority.Ship.Id, AuthoritativeShipSpecialOrderType.Explore));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not issue explore orders for another empire's ship.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(AIState.Explore, authority.Ship.AI.State);
            Assert.AreEqual(AIState.Explore, client.Ship.AI.State);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(new AuthoritativePlayerCommand
            {
                Sequence = 132,
                EmpireId = authority.Player.Id,
                Kind = AuthoritativePlayerCommandKind.ShipSpecialOrder,
                SubjectId = authority.Ship.Id,
                TargetId = 255,
            });
            Assert.IsFalse(session.LastResult.Accepted, "Unknown special-order types must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "Unsupported");
            Assert.AreEqual(AIState.Explore, authority.Ship.AI.State);
            Assert.AreEqual(AIState.Explore, client.Ship.AI.State);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XShipLifecycleOrder_ScrapAndScuttleSyncAndRejectInvalidRequests_Headless()
    {
        const ulong Seed = 0x5C4A991FUL;
        BuiltWorld authority = BuildWorld(Seed, includePlatform: true);
        BuiltWorld client = BuildWorld(Seed, includePlatform: true);

        try
        {
            Assert.IsNotNull(authority.PlatformShip, "Scuttle proof needs a real platform/station design.");
            Assert.IsTrue(authority.PlatformShip.IsPlatformOrStation);
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.ShipLifecycleOrder(150,
                authority.Player.Id, authority.Ship.Id, AuthoritativeShipLifecycleOrderType.Scrap));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(AIState.Scrap, authority.Ship.AI.State);
            Assert.AreEqual(AIState.Scrap, client.Ship.AI.State);
            Assert.IsTrue(authority.Ship.AI.OrderQueue.ToArray().Any(g => g.Plan == ShipAI.Plan.Scrap),
                "The authority should apply scrap through the real ship AI scrap path.");
            Assert.IsTrue(client.Ship.AI.OrderQueue.ToArray().Any(g => g.Plan == ShipAI.Plan.Scrap));
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover ship lifecycle order state.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.ShipLifecycleOrder(152,
                authority.Enemy.Id, authority.WingShip.Id, AuthoritativeShipLifecycleOrderType.Scrap));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not scrap another empire's ship.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreNotEqual(AIState.Scrap, authority.WingShip.AI.State);
            Assert.AreNotEqual(AIState.Scrap, client.WingShip.AI.State);
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.ShipLifecycleOrder(153,
                authority.Player.Id, authority.PlatformShip.Id, AuthoritativeShipLifecycleOrderType.Scrap));
            Assert.IsFalse(session.LastResult.Accepted, "Platforms should use scuttle, not the planet-scrap path.");
            StringAssert.Contains(session.LastResult.Reason, "scuttled");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.ShipLifecycleOrder(154,
                authority.Player.Id, authority.WingShip.Id, AuthoritativeShipLifecycleOrderType.Scuttle));
            Assert.IsFalse(session.LastResult.Accepted, "Non-platform ships should use scrap, not scuttle.");
            StringAssert.Contains(session.LastResult.Reason, "not a platform");
            Assert.AreEqual(-1f, authority.WingShip.ScuttleTimer);
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeScuttleDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.ShipLifecycleOrder(155,
                authority.Player.Id, authority.PlatformShip.Id, AuthoritativeShipLifecycleOrderType.Scuttle));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.PlatformShip.ScuttleTimer is > 9f and <= 10f,
                $"Scuttle timer should be armed near 10 seconds, got {authority.PlatformShip.ScuttleTimer}.");
            Assert.AreEqual(authority.PlatformShip.ScuttleTimer, client.PlatformShip.ScuttleTimer);
            Assert.AreNotEqual(beforeScuttleDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover scuttle timer state.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XShipCombatStance_SyncsAndRejectsInvalidRequests_Headless()
    {
        const ulong Seed = 0x57AACEUL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;
            CombatState stance = authority.Ship.AI.CombatState == CombatState.HoldPosition
                ? CombatState.Artillery
                : CombatState.HoldPosition;

            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipCombatStance(140,
                authority.Player.Id, authority.Ship.Id, stance));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(stance, authority.Ship.AI.CombatState);
            Assert.AreEqual(stance, client.Ship.AI.CombatState);
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload,
                $"S|{authority.Ship.Id}|{authority.Player.Id}|0|0|{(int)authority.Ship.AI.State}|{(int)stance}|");
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover ship combat stance.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipCombatStance(141,
                authority.Enemy.Id, authority.Ship.Id, CombatState.Evade));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not change another empire's ship stance.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(stance, authority.Ship.AI.CombatState);
            Assert.AreEqual(stance, client.Ship.AI.CombatState);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(new AuthoritativePlayerCommand
            {
                Sequence = 142,
                EmpireId = authority.Player.Id,
                Kind = AuthoritativePlayerCommandKind.SetShipCombatStance,
                SubjectId = authority.Ship.Id,
                TargetId = 255,
            });
            Assert.IsFalse(session.LastResult.Accepted, "Unknown combat stance values must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "Unsupported");
            Assert.AreEqual(stance, authority.Ship.AI.CombatState);
            Assert.AreEqual(stance, client.Ship.AI.CombatState);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsFleetAssignmentWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xF1EE7C17UL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2150))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetFleetAssignment(world.Player, 4,
                        AuthoritativeFleetAssignmentMode.Replace, new[] { world.Ship, world.WingShip }));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2150, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetFleetAssignment, submitted[0].Kind);
                Assert.AreEqual(4, submitted[0].SubjectId);
                Assert.AreEqual((int)AuthoritativeFleetAssignmentMode.Replace, submitted[0].TargetId);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParseIdList(submitted[0].Text, out int[] ids));
                CollectionAssert.AreEqual(new[] { world.Ship.Id, world.WingShip.Id }, ids);
                Assert.IsNull(world.Player.GetFleetOrNull(4),
                    "Passive MP clients must not locally create fleets before host acceptance.");
                Assert.IsNull(world.Ship.Fleet);
                Assert.IsNull(world.WingShip.Fleet);

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetFleetAssignment(world.Player, 4,
                        AuthoritativeFleetAssignmentMode.Replace, Array.Empty<Ship>()));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual((int)AuthoritativeFleetAssignmentMode.Replace, submitted[1].TargetId);
                Assert.AreEqual("", submitted[1].Text);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetFleetAssignment(world.Enemy, 4,
                        AuthoritativeFleetAssignmentMode.Replace, new[] { world.Ship }));
                Assert.AreEqual(2, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsFleetMoveWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xF1EE7C18UL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            Fleet fleet = world.Player.CreateFleet(4, null);
            fleet.AddShips(new[] { world.Ship, world.WingShip });
            fleet.SetCommandShip(null);
            fleet.AutoArrange();
            fleet.Update(FixedSimTime.Zero);
            fleet.CreatePatrol(TestPatrolWaypoints(fleet.FinalPosition));

            Vector2 originalFinalPosition = fleet.FinalPosition;
            Vector2 originalShipMove = world.Ship.AI.MovePosition;
            AIState originalShipState = world.Ship.AI.State;
            FleetPatrol originalPatrol = fleet.Patrol;
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2160))
            {
                Vector2 destination = new(45_000f, 12_000f);
                Vector2 direction = new(1f, 0f);
                MoveOrder order = MoveOrder.Aggressive | MoveOrder.ForceReassembly;
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitMoveFleet(fleet, destination, direction, order));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2160, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.MoveFleet, submitted[0].Kind);
                Assert.AreEqual(4, submitted[0].SubjectId);
                Assert.AreEqual((int)order, submitted[0].TargetId);
                Assert.AreEqual(destination, submitted[0].Position);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParseVectorPayload(submitted[0].Text,
                    out Vector2 submittedDirection));
                Assert.AreEqual(direction, submittedDirection);
                Assert.AreEqual(originalFinalPosition, fleet.FinalPosition,
                    "Passive MP clients must not locally move fleets before host acceptance.");
                Assert.AreEqual(originalShipMove, world.Ship.AI.MovePosition);
                Assert.AreEqual(originalShipState, world.Ship.AI.State);
                Assert.IsTrue(fleet.HasPatrolPlan,
                    "Passive MP clients must not locally clear fleet patrols before host acceptance.");
                Assert.AreSame(originalPatrol, fleet.Patrol);

                Fleet empty = world.Player.CreateFleet(5, null);
                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitMoveFleet(empty, destination, direction, order));
                Assert.AreEqual(1, submitted.Count);

                Fleet enemyFleet = world.Enemy.CreateFleet(4, null);
                enemyFleet.AddShips(new[] { world.EnemyShip });
                enemyFleet.AutoArrange();
                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitMoveFleet(enemyFleet, destination, direction, order));
                Assert.AreEqual(1, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsFleetRenameWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xF1EE7C19UL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            Fleet fleet = world.Player.CreateFleet(4, null);
            fleet.AddShips(new[] { world.Ship });
            fleet.SetCommandShip(null);
            fleet.AutoArrange();
            fleet.Name = "Old Fleet";
            string originalName = fleet.Name;

            var submitted = new List<AuthoritativePlayerCommand>();
            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2165))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitRenameFleet(fleet, "New Fleet"));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2165, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.RenameFleet, submitted[0].Kind);
                Assert.AreEqual(4, submitted[0].SubjectId);
                Assert.AreEqual("New Fleet", submitted[0].Text);
                Assert.AreEqual(originalName, fleet.Name,
                    "Passive MP clients must not locally rename fleets before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitRenameFleet(fleet, ""));
                Assert.AreEqual(1, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitRenameFleet(fleet, new string('A', 41)));
                Assert.AreEqual(1, submitted.Count);

                Fleet enemyFleet = world.Enemy.CreateFleet(4, null);
                enemyFleet.AddShips(new[] { world.EnemyShip });
                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitRenameFleet(enemyFleet, "Enemy Fleet"));
                Assert.AreEqual(1, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsFleetAutoArrangeWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xF1EE7C1AUL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            Fleet fleet = world.Player.CreateFleet(4, null);
            fleet.AddShips(new[] { world.Ship, world.WingShip });
            fleet.SetCommandShip(null);
            fleet.AutoArrange();
            fleet.DataNodes[0].RelativeFleetOffset = new Vector2(10_500f, -6_250f);
            fleet.DataNodes[1].RelativeFleetOffset = new Vector2(-9_250f, 8_000f);
            Vector2 originalOffset = fleet.DataNodes[0].RelativeFleetOffset;

            var submitted = new List<AuthoritativePlayerCommand>();
            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2168))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitAutoArrangeFleet(fleet));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2168, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.AutoArrangeFleet, submitted[0].Kind);
                Assert.AreEqual(4, submitted[0].SubjectId);
                Assert.AreEqual(originalOffset, fleet.DataNodes[0].RelativeFleetOffset,
                    "Passive MP clients must not locally auto-arrange fleets before host acceptance.");

                Fleet empty = world.Player.CreateFleet(5, null);
                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitAutoArrangeFleet(empty));
                Assert.AreEqual(1, submitted.Count);

                Fleet enemyFleet = world.Enemy.CreateFleet(4, null);
                enemyFleet.AddShips(new[] { world.EnemyShip });
                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitAutoArrangeFleet(enemyFleet));
                Assert.AreEqual(1, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsDeepSpaceBuildWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xD3355A51UL;
        BuiltWorld world = BuildWorld(Seed, includePlatform: true);

        try
        {
            IShipDesign design = PickBuildableDeepSpacePlatform(world.Player);
            Vector2 buildPos = world.Planet.Position + new Vector2(60_000f, 35_000f);
            int initialGoals = world.Player.AI.Goals.Count;
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2160))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitQueueDeepSpaceBuild(world.Player, design,
                        buildPos, targetPlanet: null, targetSystem: null, tetherOffset: Vector2.Zero));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2160, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.QueueDeepSpaceBuild, submitted[0].Kind);
                Assert.AreEqual(buildPos, submitted[0].Position);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParseDeepSpaceBuildPayload(submitted[0].Text,
                    out string designName, out Vector2 tetherOffset));
                Assert.AreEqual(design.Name, designName);
                Assert.AreEqual(Vector2.Zero, tetherOffset);
                Assert.AreEqual(initialGoals, world.Player.AI.Goals.Count,
                    "Passive MP clients must not add deep-space build goals before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitQueueDeepSpaceBuild(world.Player, design,
                        new Vector2(float.NaN, 0f), targetPlanet: null, targetSystem: null,
                        tetherOffset: Vector2.Zero));
                Assert.AreEqual(1, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitQueueDeepSpaceBuild(world.Enemy, design,
                        buildPos, targetPlanet: null, targetSystem: null, tetherOffset: Vector2.Zero));
                Assert.AreEqual(1, submitted.Count);

                var localGoal = new BuildConstructionShip(buildPos, design.Name, world.Player, manualPlacement: true);
                world.Player.AI.AddGoalAndEvaluate(localGoal);
                int goalsAfterLocalGoal = world.Player.AI.Goals.Count;
                Assert.IsTrue(world.Player.AI.Goals.Any(g => ReferenceEquals(g, localGoal)));

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitCancelDeepSpaceBuild(world.Player, localGoal));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(2161, submitted[1].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.CancelDeepSpaceBuild, submitted[1].Kind);
                Assert.AreEqual(buildPos, submitted[1].Position);
                Assert.AreEqual(0, submitted[1].SubjectId);
                Assert.AreEqual(0, submitted[1].TargetId);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParseDeepSpaceCancelPayload(submitted[1].Text,
                    out string cancelDesignName, out GoalType cancelGoalType));
                Assert.AreEqual(design.Name, cancelDesignName);
                Assert.AreEqual(localGoal.Type, cancelGoalType);
                Assert.AreEqual(goalsAfterLocalGoal, world.Player.AI.Goals.Count,
                    "Passive MP clients must not remove deep-space build goals before host acceptance.");
                Assert.IsTrue(world.Player.AI.Goals.Any(g => ReferenceEquals(g, localGoal)));

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitCancelDeepSpaceBuild(world.Enemy, localGoal));
                Assert.AreEqual(2, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsFleetLayoutWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xF1EE7C1CUL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            Fleet fleet = world.Player.CreateFleet(4, null);
            fleet.AddShips(new[] { world.Ship, world.WingShip });
            fleet.SetCommandShip(null);
            fleet.AutoArrange();
            Vector2 originalOffset = fleet.DataNodes[0].RelativeFleetOffset;
            var proposed = fleet.DataNodes.Select(n => new FleetDataNode(n)
            {
                Ship = n.Ship,
                Goal = n.Goal,
            }).ToArray();
            proposed[0].RelativeFleetOffset = originalOffset + new Vector2(5_000f, -7_500f);
            proposed = proposed.Take(1).ToArray();

            var submitted = new List<AuthoritativePlayerCommand>();
            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2170))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetFleetLayout(fleet, proposed));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2170, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetFleetLayout, submitted[0].Kind);
                Assert.AreEqual(4, submitted[0].SubjectId);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParseFleetLayout(submitted[0].Text,
                    out AuthoritativeFleetLayoutNode[] layout));
                Assert.AreEqual(1, layout.Length);
                Assert.AreEqual(world.Ship.Id, layout[0].ShipId);
                Assert.AreEqual(proposed[0].RelativeFleetOffset, layout[0].Offset);
                Assert.AreEqual(2, fleet.DataNodes.Count,
                    "Passive MP clients must not locally replace fleet layout before host acceptance.");
                Assert.AreEqual(originalOffset, fleet.DataNodes[0].RelativeFleetOffset);
                Assert.AreSame(fleet, world.WingShip.Fleet,
                    "Passive MP clients must not locally remove ships from a fleet before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetFleetLayout(fleet,
                        new[] { new FleetDataNode { ShipName = "", RelativeFleetOffset = Vector2.Zero } }));
                Assert.AreEqual(1, submitted.Count);

                Fleet enemyFleet = world.Enemy.CreateFleet(4, null);
                enemyFleet.AddShips(new[] { world.EnemyShip });
                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetFleetLayout(enemyFleet, enemyFleet.DataNodes));
                Assert.AreEqual(1, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsFleetPatrolLoadWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xF1EE7C1BUL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            Fleet fleet = world.Player.CreateFleet(4, null);
            fleet.AddShips(new[] { world.Ship, world.WingShip });
            fleet.SetCommandShip(null);
            fleet.AutoArrange();
            fleet.Update(FixedSimTime.Zero);
            FleetPatrol patrol = world.Player.AddPatrolRoute(fleet, TestPatrolWaypoints(fleet.FinalPosition));
            AIState originalShipState = world.Ship.AI.State;

            var submitted = new List<AuthoritativePlayerCommand>();
            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2169))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitLoadFleetPatrol(fleet, patrol));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2169, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.LoadFleetPatrol, submitted[0].Kind);
                Assert.AreEqual(4, submitted[0].SubjectId);
                Assert.AreEqual(patrol.Name, submitted[0].Text);
                Assert.IsFalse(fleet.HasPatrolPlan,
                    "Passive MP clients must not locally load saved patrol plans before host acceptance.");
                Assert.AreEqual(originalShipState, world.Ship.AI.State);

                Fleet empty = world.Player.CreateFleet(5, null);
                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitLoadFleetPatrol(empty, patrol));
                Assert.AreEqual(1, submitted.Count);

                Fleet enemyFleet = world.Enemy.CreateFleet(4, null);
                enemyFleet.AddShips(new[] { world.EnemyShip });
                FleetPatrol enemyPatrol = world.Enemy.AddPatrolRoute(enemyFleet,
                    TestPatrolWaypoints(enemyFleet.FinalPosition));
                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitLoadFleetPatrol(enemyFleet, enemyPatrol));
                Assert.AreEqual(1, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsShipSpecialOrderWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xE7010CUL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            AIState originalState = world.Ship.AI.State;
            int originalOrders = world.Ship.AI.OrderQueue.Count;
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2170))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitShipSpecialOrder(world.Ship,
                        AuthoritativeShipSpecialOrderType.Explore));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2170, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ShipSpecialOrder, submitted[0].Kind);
                Assert.AreEqual(world.Ship.Id, submitted[0].SubjectId);
                Assert.AreEqual((int)AuthoritativeShipSpecialOrderType.Explore, submitted[0].TargetId);
                Assert.AreEqual(originalState, world.Ship.AI.State,
                    "Passive MP clients must not locally start exploration before host acceptance.");
                Assert.AreEqual(originalOrders, world.Ship.AI.OrderQueue.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipSpecialOrder(world.EnemyShip,
                        AuthoritativeShipSpecialOrderType.Explore));
                Assert.AreEqual(1, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipSpecialOrder(world.Ship,
                        (AuthoritativeShipSpecialOrderType)255));
                Assert.AreEqual(1, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsShipLifecycleOrderWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0x5C4A9910UL;
        BuiltWorld world = BuildWorld(Seed, includePlatform: true);

        try
        {
            AIState originalShipState = world.Ship.AI.State;
            int originalOrders = world.Ship.AI.OrderQueue.Count;
            float originalPlatformTimer = world.PlatformShip.ScuttleTimer;
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2190))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitShipLifecycleOrder(world.Ship,
                        AuthoritativeShipLifecycleOrderType.Scrap));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2190, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ShipLifecycleOrder, submitted[0].Kind);
                Assert.AreEqual(world.Ship.Id, submitted[0].SubjectId);
                Assert.AreEqual((int)AuthoritativeShipLifecycleOrderType.Scrap, submitted[0].TargetId);
                Assert.AreEqual(originalShipState, world.Ship.AI.State,
                    "Passive MP clients must not locally start scrap before host acceptance.");
                Assert.AreEqual(originalOrders, world.Ship.AI.OrderQueue.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitShipLifecycleOrder(world.PlatformShip,
                        AuthoritativeShipLifecycleOrderType.Scuttle));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(2191, submitted[1].Sequence);
                Assert.AreEqual(world.PlatformShip.Id, submitted[1].SubjectId);
                Assert.AreEqual((int)AuthoritativeShipLifecycleOrderType.Scuttle, submitted[1].TargetId);
                Assert.AreEqual(originalPlatformTimer, world.PlatformShip.ScuttleTimer,
                    "Passive MP clients must not locally set scuttle timers before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipLifecycleOrder(world.EnemyShip,
                        AuthoritativeShipLifecycleOrderType.Scrap));
                Assert.AreEqual(2, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipLifecycleOrder(world.Ship,
                        (AuthoritativeShipLifecycleOrderType)255));
                Assert.AreEqual(2, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsShipCombatStanceWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0x57AACCUL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            CombatState originalShipStance = world.Ship.AI.CombatState;
            CombatState originalWingStance = world.WingShip.AI.CombatState;
            CombatState requested = originalShipStance == CombatState.Evade
                ? CombatState.Artillery
                : CombatState.Evade;
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2180))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetShipCombatStance(
                        new[] { world.Ship, world.WingShip }, requested));
                Assert.AreEqual(2, submitted.Count);
                Assert.IsTrue(submitted.All(c => c.Kind == AuthoritativePlayerCommandKind.SetShipCombatStance));
                Assert.AreEqual(2180, submitted[0].Sequence);
                Assert.AreEqual(2181, submitted[1].Sequence);
                Assert.AreEqual(world.Ship.Id, submitted[0].SubjectId);
                Assert.AreEqual(world.WingShip.Id, submitted[1].SubjectId);
                Assert.AreEqual((int)requested, submitted[0].TargetId);
                Assert.AreEqual((int)requested, submitted[1].TargetId);
                Assert.AreEqual(originalShipStance, world.Ship.AI.CombatState,
                    "Passive MP clients must not locally change ship stance before host acceptance.");
                Assert.AreEqual(originalWingStance, world.WingShip.AI.CombatState);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetShipCombatStance(world.EnemyShip, CombatState.HoldPosition));
                Assert.AreEqual(2, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetShipCombatStance(world.Ship, (CombatState)255));
                Assert.AreEqual(2, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XConstructionQueue_CancelAndReorderSync_Headless()
    {
        const ulong Seed = 0xC0DE011UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            EnsureSingleBuildTile(authority.Planet);
            EnsureSingleBuildTile(client.Planet);
            Building buildable = PickBuildableBuilding(authority.Planet);
            Troop troop = PickBuildableTroop(authority.Player);
            authority.Planet.HasSpacePort = true;
            client.Planet.HasSpacePort = true;

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueBuilding(50, authority.Player.Id,
                authority.Planet.Id, buildable.Name));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            session.SubmitFromClient(AuthoritativePlayerCommand.QueueTroop(51, authority.Player.Id,
                authority.Planet.Id, troop.Name));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(2, authority.Planet.ConstructionQueue.Count);
            Assert.IsTrue(authority.Planet.ConstructionQueue[0].isBuilding);
            Assert.IsTrue(authority.Planet.ConstructionQueue[1].isTroop);

            string beforeReorderDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.ReorderConstructionQueueItem(52,
                authority.Player.Id, authority.Planet.Id, currentIndex: 1, moveToIndex: 0));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.Planet.ConstructionQueue[0].isTroop);
            Assert.IsTrue(authority.Planet.ConstructionQueue[1].isBuilding);
            Assert.IsTrue(client.Planet.ConstructionQueue[0].isTroop);
            Assert.IsTrue(client.Planet.ConstructionQueue[1].isBuilding);
            Assert.AreNotEqual(beforeReorderDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover construction queue order.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.CancelConstructionQueueItem(53,
                authority.Player.Id, authority.Planet.Id, queueIndex: 0));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(1, authority.Planet.ConstructionQueue.Count);
            Assert.AreEqual(1, client.Planet.ConstructionQueue.Count);
            Assert.IsTrue(authority.Planet.ConstructionQueue[0].isBuilding);
            Assert.IsTrue(client.Planet.ConstructionQueue[0].isBuilding);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.CancelConstructionQueueItem(54,
                authority.Enemy.Id, authority.Planet.Id, queueIndex: 0));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not cancel another empire's construction queue.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(1, authority.Planet.ConstructionQueue.Count);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.ReorderConstructionQueueItem(55,
                authority.Player.Id, authority.Planet.Id, currentIndex: 2, moveToIndex: 0));
            Assert.IsFalse(session.LastResult.Accepted, "A stale construction queue index must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "outside");
            Assert.AreEqual(1, authority.Planet.ConstructionQueue.Count);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XConstructionRush_ToggleAndRushSync_Headless()
    {
        const ulong Seed = 0xC0DE705UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            EnsureSingleBuildTile(authority.Planet);
            EnsureSingleBuildTile(client.Planet);
            Building buildable = PickBuildableBuilding(authority.Planet);
            authority.Player.Money = client.Player.Money = 100_000f;
            authority.Planet.ProdHere = client.Planet.ProdHere = 100f;

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueBuilding(70, authority.Player.Id,
                authority.Planet.Id, buildable.Name));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);

            QueueItem authorityItem = authority.Planet.ConstructionQueue[0];
            QueueItem clientItem = client.Planet.ConstructionQueue[0];
            Assert.IsFalse(authorityItem.Rush);
            string beforeToggleDigest = session.LastAuthoritySnapshot.SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.ToggleConstructionRush(71,
                authority.Player.Id, authority.Planet.Id, queueIndex: 0));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.Planet.ConstructionQueue[0].Rush);
            Assert.IsTrue(client.Planet.ConstructionQueue[0].Rush);
            Assert.AreNotEqual(beforeToggleDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The canonical sync digest must cover construction continuous-rush flags.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.RushConstructionQueueItem(74,
                authority.Player.Id, authority.Planet.Id, queueIndex: 0, maxAmount: float.NaN));
            Assert.IsFalse(session.LastResult.Accepted, "Non-finite rush amounts must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "not valid");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            float beforeMoney = authority.Player.Money;
            float beforeSpent = authorityItem.ProductionSpent;
            session.SubmitFromClient(AuthoritativePlayerCommand.RushConstructionQueueItem(72,
                authority.Player.Id, authority.Planet.Id, queueIndex: 0, maxAmount: 1f));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.Player.Money < beforeMoney,
                "Rush production should charge the authoritative empire.");
            Assert.AreEqual(authority.Player.Money, client.Player.Money,
                "Client replica money must match the authority after rush production.");
            bool productionApplied = authority.Planet.ConstructionQueue.Count == 0
                                     || authority.Planet.ConstructionQueue[0].ProductionSpent > beforeSpent;
            Assert.IsTrue(productionApplied,
                "Rush production must either add production to the queued item or complete it.");
            Assert.AreEqual(authority.Planet.ConstructionQueue.Count, client.Planet.ConstructionQueue.Count);
            if (authority.Planet.ConstructionQueue.Count > 0)
                Assert.AreEqual(authority.Planet.ConstructionQueue[0].ProductionSpent,
                    client.Planet.ConstructionQueue[0].ProductionSpent);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.RushConstructionQueueItem(73,
                authority.Player.Id, authority.Planet.Id, queueIndex: 99, maxAmount: 1f));
            Assert.IsFalse(session.LastResult.Accepted, "A stale rush queue index must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "outside");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsConstructionQueueManagementWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xC0DE0C1UL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            EnsureSingleBuildTile(world.Planet);
            Building buildable = PickBuildableBuilding(world.Planet);
            Troop troop = PickBuildableTroop(world.Player);
            world.Planet.HasSpacePort = true;
            Assert.IsTrue(world.Planet.Construction.Enqueue(buildable, where: null, playerAdded: true));
            world.Planet.Construction.Enqueue(troop, QueueItemType.Troop);
            QueueItem buildingItem = world.Planet.ConstructionQueue[0];
            QueueItem troopItem = world.Planet.ConstructionQueue[1];
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 1700))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitReorderConstructionQueueItem(world.Planet,
                        troopItem, moveToIndex: 0));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(1700, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ReorderConstructionQueueItem, submitted[0].Kind);
                Assert.AreEqual(world.Planet.Id, submitted[0].SubjectId);
                Assert.AreEqual(1, submitted[0].TargetId);
                Assert.AreEqual(0f, submitted[0].Position.X);
                Assert.AreSame(buildingItem, world.Planet.ConstructionQueue[0],
                    "Passive MP clients must not locally reorder construction before host acceptance.");
                Assert.AreSame(troopItem, world.Planet.ConstructionQueue[1]);

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitCancelConstructionQueueItem(world.Planet, buildingItem));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(1701, submitted[1].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.CancelConstructionQueueItem, submitted[1].Kind);
                Assert.AreEqual(0, submitted[1].TargetId);
                Assert.AreEqual(2, world.Planet.ConstructionQueue.Count,
                    "Passive MP clients must not locally cancel construction before host acceptance.");

                bool originalRush = buildingItem.Rush;
                float originalProductionSpent = buildingItem.ProductionSpent;
                float originalMoney = world.Player.Money;
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitToggleConstructionRush(world.Planet, buildingItem));
                Assert.AreEqual(3, submitted.Count);
                Assert.AreEqual(1702, submitted[2].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ToggleConstructionRush, submitted[2].Kind);
                Assert.AreEqual(originalRush, buildingItem.Rush,
                    "Passive MP clients must not locally toggle continuous rush before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitRushConstructionQueueItem(world.Planet,
                        buildingItem, maxAmount: 5f));
                Assert.AreEqual(4, submitted.Count);
                Assert.AreEqual(1703, submitted[3].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.RushConstructionQueueItem, submitted[3].Kind);
                Assert.AreEqual(originalProductionSpent, buildingItem.ProductionSpent,
                    "Passive MP clients must not locally apply rushed production before host acceptance.");
                Assert.AreEqual(originalMoney, world.Player.Money,
                    "Passive MP clients must not locally spend rush credits before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitCancelConstructionQueueItem(world.EnemyPlanet, buildingItem));
                Assert.AreEqual(4, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XEmpireBudget_SyncsAndRejectsInvalidValues_Headless()
    {
        const ulong Seed = 0xB0D6E7UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            authority.Player.AutoTaxes = false;
            client.Player.AutoTaxes = false;
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.SetEmpireBudget(60, authority.Player.Id,
                taxRate: 0.35f, treasuryGoal: 0.45f, autoTaxes: false));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(0.35f, authority.Player.data.TaxRate);
            Assert.AreEqual(0.45f, authority.Player.data.treasuryGoal);
            Assert.IsFalse(authority.Player.AutoTaxes);
            Assert.AreEqual(authority.Player.data.TaxRate, client.Player.data.TaxRate);
            Assert.AreEqual(authority.Player.data.treasuryGoal, client.Player.data.treasuryGoal);
            Assert.AreEqual(authority.Player.AutoTaxes, client.Player.AutoTaxes);
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover empire budget settings.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetEmpireBudget(61, authority.Player.Id,
                taxRate: 0.25f, treasuryGoal: 0.4f, autoTaxes: true));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.Player.AutoTaxes);
            Assert.IsTrue(client.Player.AutoTaxes);
            Assert.AreEqual(authority.Player.data.TaxRate, client.Player.data.TaxRate,
                "Auto-tax planner output must replicate deterministically.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            float acceptedTax = authority.Player.data.TaxRate;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetEmpireBudget(62, authority.Player.Id,
                taxRate: 1.25f, treasuryGoal: 0.5f, autoTaxes: false));
            Assert.IsFalse(session.LastResult.Accepted, "Out-of-range tax values must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "between 0 and 1");
            Assert.AreEqual(acceptedTax, authority.Player.data.TaxRate);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsEmpireBudgetWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xB0D6ECUL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            float originalTax = world.Player.data.TaxRate;
            float originalTreasury = world.Player.data.treasuryGoal;
            bool originalAutoTaxes = world.Player.AutoTaxes;
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 1800))
            {
                Assert.IsTrue(Authoritative4XClientContext.TrySubmitSetEmpireBudget(world.Player,
                    taxRate: 0.36f, treasuryGoal: 0.44f, autoTaxes: false));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(1800, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetEmpireBudget, submitted[0].Kind);
                Assert.AreEqual(world.Player.Id, submitted[0].EmpireId);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParseEmpireBudgetPayload(submitted[0].Text,
                    out float taxRate, out float treasuryGoal, out bool autoTaxes));
                Assert.AreEqual(0.36f, taxRate);
                Assert.AreEqual(0.44f, treasuryGoal);
                Assert.IsFalse(autoTaxes);
                Assert.AreEqual(originalTax, world.Player.data.TaxRate,
                    "Passive MP clients must not locally change tax before host acceptance.");
                Assert.AreEqual(originalTreasury, world.Player.data.treasuryGoal,
                    "Passive MP clients must not locally change treasury goal before host acceptance.");
                Assert.AreEqual(originalAutoTaxes, world.Player.AutoTaxes,
                    "Passive MP clients must not locally change auto-tax before host acceptance.");

                Assert.IsFalse(Authoritative4XClientContext.TrySubmitSetEmpireBudget(world.Enemy,
                    taxRate: 0.2f, treasuryGoal: 0.3f, autoTaxes: true));
                Assert.AreEqual(1, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsResearchQueueWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0x4E5EACCUL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            string[] techs = ResearchCandidates(world.Player, 3);
            world.Player.Research.AddTechToQueue(techs[0]);
            string[] originalQueue = world.Player.data.ResearchQueue.ToArray();
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 1900))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitQueueResearch(world.Player, techs[1]));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(1900, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.QueueResearch, submitted[0].Kind);
                Assert.AreEqual(techs[1], submitted[0].Text);
                CollectionAssert.AreEqual(originalQueue, world.Player.data.ResearchQueue.ToArray(),
                    "Passive MP clients must not locally add research before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitMoveResearchQueueItem(world.Player, techs[0],
                        AuthoritativeResearchQueueMove.ToTopWithPrereqs));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(1901, submitted[1].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.MoveResearchQueueItem, submitted[1].Kind);
                Assert.AreEqual((int)AuthoritativeResearchQueueMove.ToTopWithPrereqs, submitted[1].TargetId);
                CollectionAssert.AreEqual(originalQueue, world.Player.data.ResearchQueue.ToArray(),
                    "Passive MP clients must not locally reorder research before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitRemoveResearchQueueItem(world.Player, techs[0]));
                Assert.AreEqual(3, submitted.Count);
                Assert.AreEqual(1902, submitted[2].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.RemoveResearchQueueItem, submitted[2].Kind);
                CollectionAssert.AreEqual(originalQueue, world.Player.data.ResearchQueue.ToArray(),
                    "Passive MP clients must not locally remove research before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitQueueResearch(world.Enemy, techs[2]));
                Assert.AreEqual(3, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
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

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitDiplomacyProposal(world.Enemy,
                        AuthoritativeDiplomacyProposalType.TradeDeal, "trade terms"));
                Assert.AreEqual(11, submitted.Count);
                Assert.AreEqual(AuthoritativePlayerCommandKind.DiplomacyProposal, submitted[10].Kind);
                Assert.AreEqual(710, submitted[10].Sequence);
                Assert.AreEqual(world.Player.Id, submitted[10].EmpireId);
                Assert.AreEqual(world.Enemy.Id, submitted[10].SubjectId);
                Assert.AreEqual((int)AuthoritativeDiplomacyProposalType.TradeDeal, submitted[10].TargetId);
                Assert.AreEqual("trade terms", submitted[10].Text);

                PlanetGridSquare explicitTile = world.Planet.TilesList.First();
                Assert.IsTrue(Authoritative4XClientContext.TrySubmitQueueBuilding(world.Planet,
                    buildable.Name, explicitTile), "Dragging a building to a colony tile should submit tile coordinates.");
                Assert.AreEqual(12, submitted.Count);
                Assert.AreEqual(AuthoritativePlayerCommandKind.QueueBuilding, submitted[11].Kind);
                Assert.AreEqual(711, submitted[11].Sequence);
                Assert.AreEqual(world.Planet.Id, submitted[11].SubjectId);
                Assert.AreEqual(1, submitted[11].TargetId);
                Assert.AreEqual(explicitTile.X, (int)submitted[11].Position.X);
                Assert.AreEqual(explicitTile.Y, (int)submitted[11].Position.Y);
                Assert.AreEqual(buildable.Name, submitted[11].Text);
                Assert.IsFalse(world.Planet.Construction.GetConstructionQueueSnapshot()
                        .Any(q => q.isBuilding && q.Building?.Name == buildable.Name),
                    "Passive MP clients must not locally enqueue dragged tile buildings before host acceptance.");

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

                var looseGroupShips = new SDUtils.Array<Ship> { world.Ship, world.WingShip };
                Vector2 groupDestination = world.Ship.Position + new Vector2(18_000f, -2_500f);
                Vector2 groupFacing = new(1f, 0f);
                var looseGroup = new ShipGroup(looseGroupShips, groupDestination, groupDestination,
                    groupFacing, world.Player);
                new ShipMoveCommands(world.Screen).MoveFleetToLocation(System.Array.Empty<Ship>(),
                    null, null, groupDestination, groupFacing, looseGroup);
                Assert.AreEqual(8, submitted.Count);
                Assert.IsTrue(submitted.Skip(6).All(c => c.Kind == AuthoritativePlayerCommandKind.MoveShip),
                    "Loose selected ship groups should route through per-ship authoritative movement.");
                Assert.AreEqual(906, submitted[6].Sequence);
                Assert.AreEqual(907, submitted[7].Sequence);
                Assert.AreEqual(world.Ship.Id, submitted[6].SubjectId);
                Assert.AreEqual(world.WingShip.Id, submitted[7].SubjectId);
                Assert.AreEqual(submitted[6].Position, submitted[7].Position);
                Assert.AreEqual(firstMove, world.Ship.AI.MovePosition,
                    "Passive MP clients must not locally move loose ship groups before host acceptance.");
                Assert.AreEqual(secondMove, world.WingShip.AI.MovePosition);
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
        const int HostPeer = 2;
        const int RemotePeer = 3;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            int port = FreeTcpPort();
            TcpLockstepTransport hostTransport = TcpLockstepTransport.Host(port, RemotePeer);
            TcpLockstepTransport clientTransport = TcpLockstepTransport.Join("127.0.0.1", port,
                Authoritative4XNetworkHost.HostPeerId);
            Assert.IsTrue(hostTransport.WaitForConnection(TimeSpan.FromSeconds(3)),
                "Authoritative live host did not accept the loopback client.");

            using var networkClient = new Authoritative4XNetworkClient(client.Screen, clientTransport, RemotePeer,
                new[] { client.Player.Id, client.Enemy.Id });
            Authoritative4XLiveSession liveHost = Authoritative4XLiveSession.HostGame(authority.Screen,
                hostTransport, HostPeer, new Dictionary<int, int>
                {
                    [HostPeer] = authority.Player.Id,
                    [RemotePeer] = authority.Enemy.Id,
                },
                new[] { authority.Player.Id, authority.Enemy.Id });
            authority.Screen.AttachAuthoritative4XMultiplayer(liveHost);

            PumpLiveTcpUntil(() => NetworkClientCaughtHeartbeat(networkClient, HostPeer),
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
    public void Authoritative4XLiveHost_BroadcastsPauseAndSpeedControl_Headless()
    {
        const ulong Seed = 0x41E40058UL;
        const int HostPeer = 2;
        const int RemotePeer = 3;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            int port = FreeTcpPort();
            TcpLockstepTransport hostTransport = TcpLockstepTransport.Host(port, RemotePeer);
            TcpLockstepTransport clientTransport = TcpLockstepTransport.Join("127.0.0.1", port,
                Authoritative4XNetworkHost.HostPeerId);
            Assert.IsTrue(hostTransport.WaitForConnection(TimeSpan.FromSeconds(3)),
                "Authoritative live control proof did not connect to the loopback host.");

            using var networkClient = new Authoritative4XNetworkClient(client.Screen, clientTransport, RemotePeer,
                new[] { client.Player.Id, client.Enemy.Id });
            Authoritative4XLiveSession liveHost = Authoritative4XLiveSession.HostGame(authority.Screen,
                hostTransport, HostPeer, new Dictionary<int, int>
                {
                    [HostPeer] = authority.Player.Id,
                    [RemotePeer] = authority.Enemy.Id,
                },
                new[] { authority.Player.Id, authority.Enemy.Id });
            authority.Screen.AttachAuthoritative4XMultiplayer(liveHost);
            authority.UState.Paused = false;
            client.UState.Paused = false;
            authority.UState.GameSpeed = 1f;
            client.UState.GameSpeed = 1f;

            Assert.IsTrue(liveHost.TryTogglePause(), "The host should own live pause control.");
            PumpLiveTcpUntil(() => client.UState.Paused && Math.Abs(client.UState.GameSpeed - 1f) < 0.001f,
                liveHost, networkClient);
            Assert.IsTrue(authority.UState.Paused);
            Assert.IsTrue(client.UState.Paused);

            Assert.IsTrue(liveHost.TrySetGameSpeed(2f), "The host should own live speed control.");
            PumpLiveTcpUntil(() => client.UState.Paused && Math.Abs(client.UState.GameSpeed - 2f) < 0.001f,
                liveHost, networkClient);
            Assert.AreEqual(2f, authority.UState.GameSpeed);
            Assert.AreEqual(2f, client.UState.GameSpeed);

            using var passiveTransport = TcpLockstepTransport.Host(FreeTcpPort(), RemotePeer);
            using var passiveClient = Authoritative4XLiveSession.ClientGame(client.Screen,
                passiveTransport, RemotePeer, client.Enemy.Id, new[] { client.Player.Id, client.Enemy.Id });
            Assert.IsFalse(passiveClient.TryTogglePause(),
                "Passive clients must not own live pause control.");
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XLiveTelemetry_WritesSessionCommandAndControlEvidence_Headless()
    {
        const ulong Seed = 0x41E40059UL;
        const int HostPeer = 2;
        const int RemotePeer = 3;
        string dir = Path.Combine(Path.GetTempPath(), $"auth4x_live_telemetry_{Guid.NewGuid():N}");
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);
        string oldOutput = Authoritative4XLiveTelemetry.OutputDirectoryOverride;
        bool? oldEnabled = Authoritative4XLiveTelemetry.EnabledOverride;

        try
        {
            Authoritative4XLiveTelemetry.OutputDirectoryOverride = dir;
            Authoritative4XLiveTelemetry.EnabledOverride = true;
            int port = FreeTcpPort();
            TcpLockstepTransport hostTransport = TcpLockstepTransport.Host(port, RemotePeer);
            TcpLockstepTransport clientTransport = TcpLockstepTransport.Join("127.0.0.1", port,
                Authoritative4XNetworkHost.HostPeerId);
            Assert.IsTrue(hostTransport.WaitForConnection(TimeSpan.FromSeconds(3)),
                "Authoritative live telemetry proof did not connect to the loopback client.");

            using var networkClient = new Authoritative4XNetworkClient(client.Screen, clientTransport, RemotePeer,
                new[] { client.Player.Id, client.Enemy.Id });
            Authoritative4XLiveSession liveHost = Authoritative4XLiveSession.HostGame(authority.Screen,
                hostTransport, HostPeer, new Dictionary<int, int>
                {
                    [HostPeer] = authority.Player.Id,
                    [RemotePeer] = authority.Enemy.Id,
                },
                new[] { authority.Player.Id, authority.Enemy.Id });
            authority.Screen.AttachAuthoritative4XMultiplayer(liveHost);
            Assert.IsFalse(string.IsNullOrWhiteSpace(liveHost.TelemetrySessionPath),
                "The live session should expose the telemetry session path when telemetry is enabled.");

            Assert.IsTrue(Authoritative4XClientContext.TrySubmitSetColonyType(authority.Planet,
                    Planet.ColonyType.Military),
                "The visible live host should route UI commands through the authoritative context.");
            PumpLiveTcpUntil(() => networkClient.LastAuthoritySnapshot != null
                                    && networkClient.LastClientSnapshot != null,
                liveHost, networkClient);
            Assert.IsTrue(liveHost.TrySetGameSpeed(2f));
            PumpLiveTcpUntil(() => Math.Abs(client.UState.GameSpeed - 2f) < 0.001f,
                liveHost, networkClient);
            Planet.ColonyType remoteType = authority.EnemyPlanet.CType == Planet.ColonyType.Research
                ? Planet.ColonyType.Military
                : Planet.ColonyType.Research;
            networkClient.Submit(AuthoritativePlayerCommand.SetColonyType(600, client.Enemy.Id,
                client.EnemyPlanet.Id, remoteType));
            PumpLiveTcpUntil(() => authority.EnemyPlanet.CType == remoteType
                                    && client.EnemyPlanet.CType == remoteType,
                liveHost, networkClient);
            Assert.AreEqual(remoteType, authority.EnemyPlanet.CType);

            string path = liveHost.TelemetrySessionPath;
            liveHost.Dispose();
            string text = File.ReadAllText(path);
            StringAssert.Contains(text, "BEGIN role=Host");
            StringAssert.Contains(text, "ENV game=");
            StringAssert.Contains(text, "PEERS empireByPeer=");
            StringAssert.Contains(text, "COMMAND source=ui");
            StringAssert.Contains(text, $"peer={HostPeer}");
            StringAssert.Contains(text, "kind=SetColonyType");
            StringAssert.Contains(text, $"COMMAND source=network peer={RemotePeer}");
            StringAssert.Contains(text, "seq=600");
            StringAssert.Contains(text, "RESULT origin=");
            StringAssert.Contains(text, "accepted=True");
            StringAssert.Contains(text, "SNAPSHOT tick=");
            StringAssert.Contains(text, "payloadHash=0x");
            StringAssert.Contains(text, "rows='");
            StringAssert.Contains(text, "P:");
            StringAssert.Contains(text, "S:");
            StringAssert.Contains(text, "CONTROL source=host");
            StringAssert.Contains(text, "END utc=");
        }
        finally
        {
            Authoritative4XLiveTelemetry.OutputDirectoryOverride = oldOutput;
            Authoritative4XLiveTelemetry.EnabledOverride = oldEnabled;
            authority.Screen.Dispose();
            client.Screen.Dispose();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void Authoritative4XLiveTelemetry_RecordsSyncMismatchEvidence_Headless()
    {
        const ulong Seed = 0x41E4005AUL;
        const int Peer = 2;
        string dir = Path.Combine(Path.GetTempPath(), $"auth4x_live_mismatch_{Guid.NewGuid():N}");
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);
        string oldOutput = Authoritative4XLiveTelemetry.OutputDirectoryOverride;
        bool? oldEnabled = Authoritative4XLiveTelemetry.EnabledOverride;

        try
        {
            Authoritative4XLiveTelemetry.OutputDirectoryOverride = dir;
            Authoritative4XLiveTelemetry.EnabledOverride = true;
            int port = FreeTcpPort();
            TcpLockstepTransport hostTransport = TcpLockstepTransport.Host(port, Peer);
            TcpLockstepTransport clientTransport = TcpLockstepTransport.Join("127.0.0.1", port,
                Authoritative4XNetworkHost.HostPeerId);
            Assert.IsTrue(hostTransport.WaitForConnection(TimeSpan.FromSeconds(3)),
                "Authoritative live mismatch proof did not connect to the loopback host.");

            using var host = new Authoritative4XNetworkHost(authority.Screen, hostTransport,
                new Dictionary<int, int> { [Peer] = authority.Player.Id },
                new[] { authority.Player.Id });
            Authoritative4XLiveSession liveClient = Authoritative4XLiveSession.ClientGame(client.Screen,
                clientTransport, Peer, client.Player.Id, new[] { client.Player.Id });
            client.Screen.AttachAuthoritative4XMultiplayer(liveClient);
            string path = liveClient.TelemetrySessionPath;

            Planet.ColonyType nextType = client.Planet.CType == Planet.ColonyType.Research
                ? Planet.ColonyType.Military
                : Planet.ColonyType.Research;
            Assert.IsTrue(Authoritative4XClientContext.TrySubmitSetColonyType(client.Planet, nextType),
                "The live client should submit a real command before the forced mismatch is detected.");

            DateTime hostDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (host.LastResult?.Sequence != 1 && DateTime.UtcNow < hostDeadline)
            {
                host.Poll();
                System.Threading.Thread.Sleep(5);
            }
            Assert.IsNotNull(host.LastResult, "The host should process the submitted command before the client polls.");
            Assert.AreEqual(1, host.LastResult.Sequence);

            client.Planet.SetPrioritizedPort(!authority.Planet.PrioritizedPort);
            Authoritative4XSyncMismatchException mismatch = null;
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (mismatch == null && DateTime.UtcNow < deadline)
            {
                try
                {
                    liveClient.Poll();
                }
                catch (Authoritative4XSyncMismatchException e)
                {
                    mismatch = e;
                }
                System.Threading.Thread.Sleep(5);
            }

            Assert.IsNotNull(mismatch, "The deliberately perturbed client replica should report a sync mismatch.");
            Assert.AreEqual(1, mismatch.Result.Sequence);
            Assert.AreEqual(AuthoritativePlayerCommandKind.SetColonyType, mismatch.Command.Kind);

            liveClient.Dispose();
            string text = File.ReadAllText(path);
            StringAssert.Contains(text, "SYNC_MISMATCH");
            StringAssert.Contains(text, "seq=1");
            StringAssert.Contains(text, "kind=SetColonyType");
            StringAssert.Contains(text, "firstDiff='");
            StringAssert.Contains(text, "authorityPayload='");
            StringAssert.Contains(text, "clientPayload='");
            string[] authorityPayloads = Directory.GetFiles(dir, "*sync-mismatch-authority.payload");
            string[] clientPayloads = Directory.GetFiles(dir, "*sync-mismatch-client.payload");
            Assert.AreEqual(1, authorityPayloads.Length, "Mismatch telemetry should persist the authority payload.");
            Assert.AreEqual(1, clientPayloads.Length, "Mismatch telemetry should persist the client payload.");
            StringAssert.Contains(File.ReadAllText(authorityPayloads[0]), "P|");
            StringAssert.Contains(File.ReadAllText(clientPayloads[0]), "P|");
        }
        finally
        {
            Authoritative4XLiveTelemetry.OutputDirectoryOverride = oldOutput;
            Authoritative4XLiveTelemetry.EnabledOverride = oldEnabled;
            authority.Screen.Dispose();
            client.Screen.Dispose();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void AuthoritativeDiplomacyPopupScreen_DoesNotPauseUniverseAndExposesResponses_Headless()
    {
        BuiltWorld world = BuildWorld(0xD1A1060UL);
        try
        {
            world.UState.Paused = false;
            var offer = new AuthoritativeDiplomacyPopup
            {
                ProposalId = 7,
                ProposerEmpireId = world.Enemy.Id,
                TargetEmpireId = world.Player.Id,
                ProposalType = AuthoritativeDiplomacyProposalType.TradeDeal,
                Terms = "minerals for trade access",
                RequiresResponse = true,
                Message = "Diplomacy proposal received.",
            };

            var offerScreen = new AuthoritativeDiplomacyPopupScreen(world.Screen, offer);
            offerScreen.LoadContent();
            Assert.IsFalse(world.UState.Paused,
                "Authoritative diplomacy popups must not pause only the local peer.");
            Assert.IsTrue(offerScreen.Find(AuthoritativeDiplomacyPopupScreen.AcceptButtonName, out UIButton _),
                "A response-required popup should expose ACCEPT.");
            Assert.IsTrue(offerScreen.Find(AuthoritativeDiplomacyPopupScreen.RejectButtonName, out UIButton _),
                "A response-required popup should expose REJECT.");
            offerScreen.Dispose();

            var notice = new AuthoritativeDiplomacyPopup
            {
                ProposalId = 0,
                ProposerEmpireId = world.Enemy.Id,
                TargetEmpireId = world.Player.Id,
                ProposalType = AuthoritativeDiplomacyProposalType.DeclareWar,
                RequiresResponse = false,
                Message = "War declared.",
            };

            var noticeScreen = new AuthoritativeDiplomacyPopupScreen(world.Screen, notice);
            noticeScreen.LoadContent();
            Assert.IsFalse(world.UState.Paused);
            Assert.IsTrue(noticeScreen.Find(AuthoritativeDiplomacyPopupScreen.OkButtonName, out UIButton _),
                "A notification popup should expose a single acknowledgement.");
            noticeScreen.Dispose();
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void AuthoritativeDiplomacyProposalScreen_DoesNotPauseUniverseAndExposesProposalButtons_Headless()
    {
        BuiltWorld world = BuildWorld(0xD1A1062UL);
        try
        {
            world.UState.Paused = false;
            var screen = new AuthoritativeDiplomacyProposalScreen(world.Screen, world.Screen, world.Enemy);
            screen.LoadContent();

            Assert.IsFalse(world.UState.Paused,
                "The human proposal picker must not pause only the local authoritative peer.");
            Assert.IsTrue(screen.Find(AuthoritativeDiplomacyProposalScreen.NonAggressionButtonName, out UIButton _));
            Assert.IsTrue(screen.Find(AuthoritativeDiplomacyProposalScreen.TradeButtonName, out UIButton _));
            Assert.IsTrue(screen.Find(AuthoritativeDiplomacyProposalScreen.AllianceButtonName, out UIButton _));
            Assert.IsTrue(screen.Find(AuthoritativeDiplomacyProposalScreen.PeaceButtonName, out UIButton _));
            Assert.IsTrue(screen.Find(AuthoritativeDiplomacyProposalScreen.DeclareWarButtonName, out UIButton _));
            Assert.IsTrue(screen.Find(AuthoritativeDiplomacyProposalScreen.BackButtonName, out UIButton _));
            screen.Dispose();
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XLiveHost_RoutesLocalHumanDiplomacyPopupAndResponse_Headless()
    {
        const ulong Seed = 0xD1A1061UL;
        const int HostPeer = 2;
        const int RemotePeer = 3;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            int port = FreeTcpPort();
            TcpLockstepTransport hostTransport = TcpLockstepTransport.Host(port, RemotePeer);
            TcpLockstepTransport clientTransport = TcpLockstepTransport.Join("127.0.0.1", port,
                Authoritative4XNetworkHost.HostPeerId);
            Assert.IsTrue(hostTransport.WaitForConnection(TimeSpan.FromSeconds(3)),
                "Authoritative diplomacy live proof did not connect to the loopback host.");

            using var networkClient = new Authoritative4XNetworkClient(client.Screen, clientTransport, RemotePeer,
                new[] { client.Player.Id, client.Enemy.Id });
            Authoritative4XLiveSession liveHost = Authoritative4XLiveSession.HostGame(authority.Screen,
                hostTransport, HostPeer, new Dictionary<int, int>
                {
                    [HostPeer] = authority.Player.Id,
                    [RemotePeer] = authority.Enemy.Id,
                },
                new[] { authority.Player.Id, authority.Enemy.Id });
            authority.Screen.AttachAuthoritative4XMultiplayer(liveHost);
            authority.UState.Paused = true; // keep the proof focused on diplomacy, not live heartbeat churn
            client.UState.Paused = true;

            networkClient.Submit(AuthoritativePlayerCommand.DiplomacyProposal(400,
                authority.Enemy.Id, authority.Player.Id, AuthoritativeDiplomacyProposalType.TradeDeal,
                "live trade"));

            AuthoritativeDiplomacyPopup popup = null;
            bool CapturePopup()
            {
                if (popup != null)
                    return true;
                return liveHost.TryDequeueDiplomacyPopup(out popup);
            }
            PumpLiveTcpUntil(CapturePopup,
                liveHost, networkClient);
            Assert.IsNotNull(popup);
            Assert.AreEqual(authority.Enemy.Id, popup.ProposerEmpireId);
            Assert.AreEqual(authority.Player.Id, popup.TargetEmpireId);
            Assert.AreEqual(AuthoritativeDiplomacyProposalType.TradeDeal, popup.ProposalType);
            Assert.IsTrue(popup.RequiresResponse);
            Assert.IsFalse(authority.Player.IsTradeTreaty(authority.Enemy),
                "The target human must explicitly accept before a proposal applies.");

            Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                Authoritative4XClientContext.TrySubmitDiplomacyResponse(popup.ProposalId,
                    AuthoritativeDiplomacyResponseKind.Accept),
                "The visible host popup should respond through the authoritative UI command context.");

            PumpLiveTcpUntil(() => NetworkClientCaughtUp(networkClient, HostPeer, 1),
                liveHost, networkClient);
            Assert.IsTrue(liveHost.LastResult.Accepted, liveHost.LastResult.Reason);
            Assert.IsTrue(authority.Player.IsTradeTreaty(authority.Enemy));
            Assert.IsTrue(client.Player.IsTradeTreaty(client.Enemy));
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
            Assert.IsFalse(client.EnemyShip.KnownByEmpires.KnownBy(client.Player),
                "The local-view visibility hook must not rewrite UState.Player sensor knowledge.");
            Assert.IsTrue(client.Screen.IsKnownToLocalPlayerForUi(client.EnemyShip),
                "A client should treat the assigned empire's own ships as known for UI visibility.");
            client.EnemyShip.InFrustum = true;
            Assert.IsTrue(client.Screen.IsVisibleToLocalPlayerInMapForUi(client.EnemyShip));
            Assert.IsTrue(client.EnemyShip.IsVisibleToPlayerInMap,
                "Ship-level visibility should also use the assigned local empire so icons and scene objects can render.");
            Assert.AreNotEqual(client.EnemyShip.InPlayerSensorRange,
                client.Screen.IsKnownToLocalPlayerForUi(client.EnemyShip),
                "The local-view visibility proof must not be just a wrapper around UState.Player visibility.");

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
    public void Authoritative4XLobby_StartGeneratedGameHandoff_Headless()
    {
        LoadAllGameData();

        try
        {
            IEmpireData[] races = ResourceManager.MajorRaces
                .Where(r => !r.IsFactionOrMinorRace)
                .OrderBy(r => RacePreference(r), StringComparer.Ordinal)
                .Take(2)
                .ToArray();
            Assert.IsTrue(races.Length >= 2, "The generated-game handoff proof needs two playable major races.");

            var settings = new Authoritative4XGameSettings
            {
                GenerationSeed = 0x4B1B4B2,
                GalaxySize = GalSize.Tiny,
                StarsCount = RaceDesignScreen.StarsAbundance.Rare,
                Mode = RaceDesignScreen.GameMode.Sandbox,
                Difficulty = GameDifficulty.Normal,
                NumOpponents = 1,
                Pace = 1.5f,
                TurnTimer = 4,
                ExtraPlanets = 1,
                StartingPlanetRichnessBonus = 1f,
                GameSpeed = 2f,
                StartPaused = true,
            };

            var lobby = new Authoritative4XLobby(hostPlayerPeerId: 2, hostName: "Host");
            lobby.Join(3, "Client");
            Assert.IsTrue(lobby.SetSettings(2, settings).Valid);
            Assert.IsTrue(lobby.SetPlayerSelection(2, RacePreference(races[0]), OneAffordableTrait()).Valid);
            Assert.IsTrue(lobby.SetPlayerSelection(3, RacePreference(races[1]), Array.Empty<string>()).Valid);
            Assert.IsTrue(lobby.SetReady(2, true).Valid);
            Assert.IsTrue(lobby.SetReady(3, true).Valid);

            using Authoritative4XGeneratedGameStart generated = lobby.StartGeneratedGame();
            UniverseState us = generated.AuthorityUniverse.UState;
            Assert.AreEqual(settings.GenerationSeed, generated.Settings.GenerationSeed);
            Assert.AreEqual(settings.GenerationSeed, us.P.GenerationSeed);
            Assert.AreEqual(settings.GameSpeed, us.GameSpeed);
            Assert.IsTrue(us.Paused, "The generated live handoff should preserve the host pause setting.");
            Assert.AreEqual(2, generated.HumanEmpireIds.Length);
            Assert.AreEqual(2, generated.EmpireIdByPeer.Count);

            Empire hostEmpire = us.GetEmpireById(generated.EmpireIdForPeer(2));
            Empire clientEmpire = us.GetEmpireById(generated.EmpireIdForPeer(3));
            Assert.IsNotNull(hostEmpire);
            Assert.IsNotNull(clientEmpire);
            Assert.AreNotEqual(hostEmpire.Id, clientEmpire.Id);
            Assert.IsTrue(SameRace(hostEmpire.data, races[0]));
            Assert.IsTrue(SameRace(clientEmpire.data, races[1]));
            Assert.IsTrue(AuthoritativeHumanPlayers.IsHumanControlled(hostEmpire));
            Assert.IsTrue(AuthoritativeHumanPlayers.IsHumanControlled(clientEmpire));
            Assert.IsTrue(hostEmpire.GetPlanets().Count > 0);
            Assert.IsTrue(clientEmpire.GetPlanets().Count > 0);
        }
        finally
        {
            // StarDriveTest.Cleanup unloads extra data; this keeps the intent explicit for future test edits.
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

    static WayPoints TestPatrolWaypoints(Vector2 origin)
    {
        var waypoints = new WayPoints();
        waypoints.Set(new[]
        {
            new WayPoint(origin + new Vector2(4_000f, 0f), new Vector2(1f, 0f)),
            new WayPoint(origin + new Vector2(4_000f, 4_000f), new Vector2(0f, 1f)),
        });
        return waypoints;
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

    static IShipDesign PickBuildableDeepSpacePlatform(Empire empire)
    {
        IShipDesign design = empire.SpaceStationsWeCanBuildSnapshot
            .Where(s => !s.IsResearchStation && !s.IsMiningStation && !s.IsShipyard)
            .OrderBy(s => s.BaseCost)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(design,
            $"Empire {empire.Id} needs at least one buildable non-research/mining/shipyard station for deep-space build proofs.");
        return design;
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

    static void EnsureTwoBuildTiles(Planet planet)
    {
        planet.TilesList.Clear();
        planet.TilesList.Add(new PlanetGridSquare(planet, 0, 0, b: null, hab: true, terraformable: false));
        planet.TilesList.Add(new PlanetGridSquare(planet, 1, 0, b: null, hab: true, terraformable: false));
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

    BuiltWorld BuildWorld(ulong seed, bool extraPlayerPlanet = false, bool includePlatform = false,
        bool includeNeutralPlanet = false)
    {
        if (includePlatform)
            LoadStarterShips("Platform Base mk1-a");

        CreateUniverseAndPlayerEmpire();
        Planet planet = AddDummyPlanetToEmpire(new Vector2(200_000, 200_000), Player, fertility: 1f, minerals: 1f, maxPop: 5f);
        if (extraPlayerPlanet)
            AddDummyPlanetToEmpire(new Vector2(240_000, 200_000), Player, fertility: 1f, minerals: 1f, maxPop: 5f);
        Planet enemyPlanet = AddDummyPlanetToEmpire(new Vector2(-200_000, -200_000), Enemy, fertility: 1f, minerals: 1f, maxPop: 5f);
        Planet neutralPlanet = includeNeutralPlanet
            ? AddDummyPlanet(new Vector2(0, 240_000), fertility: 1f, minerals: 1f, pop: 5f,
                pos: new Vector2(5_000, 245_000), explored: true)
            : null;
        Ship ship = SpawnShip("Vulcan Scout", Player, new Vector2(0, 0));
        Ship wingShip = SpawnShip("Vulcan Scout", Player, new Vector2(2_000, 0));
        Ship enemyShip = SpawnShip("Vulcan Scout", Enemy, new Vector2(35_000, 0));
        Ship platformShip = includePlatform
            ? SpawnShip("Platform Base mk1-a", Player, new Vector2(4_000, 4_000))
            : null;

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
            NeutralPlanet = neutralPlanet,
            Ship = ship,
            WingShip = wingShip,
            EnemyShip = enemyShip,
            PlatformShip = platformShip,
            ResearchUid = researchUid,
        };
    }

    static string[] ResearchCandidates(Empire empire, int count)
    {
        string[] uids = empire.TechEntries
            .Where(t => t.Discovered && t.CanBeResearched)
            .OrderBy(t => t.UID, StringComparer.Ordinal)
            .Select(t => t.UID)
            .Distinct(StringComparer.Ordinal)
            .Take(count)
            .ToArray();
        Assert.IsTrue(uids.Length >= count,
            $"Expected at least {count} discovered, researchable techs for authoritative MP research tests.");
        return uids;
    }

    static (string uid, int index) FindQueuedResearch(Empire empire, Func<int, bool> indexPredicate)
    {
        for (int i = 0; i < empire.data.ResearchQueue.Count; ++i)
        {
            if (indexPredicate(i))
                return (empire.data.ResearchQueue[i], i);
        }

        Assert.Fail("Could not find a queued research item matching the requested movement predicate.");
        return ("", -1);
    }

    static void AssertResearchQueuesEqual(Empire expected, Empire actual)
    {
        CollectionAssert.AreEqual(expected.data.ResearchQueue.ToArray(), actual.data.ResearchQueue.ToArray(),
            "Research queue order must match between authority and replica.");
    }

    static string ResearchQueuePayloadPrefix(Empire empire)
        => $"E|{empire.Id}|{empire.Research.Topic}|{string.Join(",", empire.data.ResearchQueue)}|";

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

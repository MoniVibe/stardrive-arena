using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDGraphics;
using SDLockstep;
using SDUtils;
using Ship_Game;
using Ship_Game.AI;
using Ship_Game.Commands.Goals;
using Ship_Game.Data;
using Ship_Game.GameScreens.DiplomacyScreen;
using Ship_Game.GameScreens.Arena;
using Ship_Game.GameScreens.ShipDesign;
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
        public Ship ColonyShip;
        public Ship FreighterShip;
        public Ship CarrierShip;
        public Ship TroopCarrierShip;
        public Ship TroopShip;
        public Ship FriendlyTroopTargetShip;
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

        var automationFlags = AuthoritativeEmpireAutomationFlags.AutoExplore
                              | AuthoritativeEmpireAutomationFlags.AutoColonize
                              | AuthoritativeEmpireAutomationFlags.AutoFreighters
                              | AuthoritativeEmpireAutomationFlags.RushAllConstruction;
        var automationRequest = AuthoritativePlayerCommand.SetEmpireAutomation(117, 2,
                automationFlags, "Freighter", "Colony", "Scout", "Constructor",
                "Research Station", "Mining Station")
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(automationRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetEmpireAutomation, copy.Kind);
        Assert.AreEqual((int)automationFlags, copy.TargetId);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseEmpireAutomationPayload(copy.Text,
            out string freighter, out string colony, out string scout, out string constructor,
            out string researchStation, out string miningStation));
        Assert.AreEqual("Freighter", freighter);
        Assert.AreEqual("Colony", colony);
        Assert.AreEqual("Scout", scout);
        Assert.AreEqual("Constructor", constructor);
        Assert.AreEqual("Research Station", researchStation);
        Assert.AreEqual("Mining Station", miningStation);

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

        var manualTradeSlotsRequest = AuthoritativePlayerCommand.SetPlanetManualTradeSlots(27, 2, 789,
                foodImport: 2, prodImport: 3, coloImport: 4, foodExport: 5, prodExport: 6, coloExport: 7)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(manualTradeSlotsRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetPlanetManualTradeSlots, copy.Kind);
        Assert.AreEqual(789, copy.SubjectId);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseManualTradeSlotsPayload(copy.Text,
            out int foodImport, out int prodImport, out int coloImport,
            out int foodExport, out int prodExport, out int coloExport));
        AssertManualTradeSlots(foodImport, prodImport, coloImport, foodExport, prodExport, coloExport,
            expectedFoodImport: 2, expectedProdImport: 3, expectedColoImport: 4,
            expectedFoodExport: 5, expectedProdExport: 6, expectedColoExport: 7);

        var defenseTargetsRequest = AuthoritativePlayerCommand.SetPlanetDefenseTargets(28, 2, 789,
                garrisonSize: 9, wantedPlatforms: 10, wantedShipyards: 2, wantedStations: 6)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(defenseTargetsRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetPlanetDefenseTargets, copy.Kind);
        Assert.AreEqual(789, copy.SubjectId);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParsePlanetDefenseTargetsPayload(copy.Text,
            out int garrisonSize, out int wantedPlatforms, out int wantedShipyards, out int wantedStations));
        AssertDefenseTargets(garrisonSize, wantedPlatforms, wantedShipyards, wantedStations,
            expectedGarrisonSize: 9, expectedWantedPlatforms: 10,
            expectedWantedShipyards: 2, expectedWantedStations: 6);

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

        var setFleetIconRequest = AuthoritativePlayerCommand.SetFleetIcon(31, 2, 7, 17)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(setFleetIconRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetFleetIcon, copy.Kind);
        Assert.AreEqual(7, copy.SubjectId);
        Assert.AreEqual(17, copy.TargetId);

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

        var fleetRequisitionRequest = AuthoritativePlayerCommand.QueueFleetRequisition(44, 2,
                fleetKey: 7, rush: true, new[] { 0, 2 })
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(fleetRequisitionRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.QueueFleetRequisition, copy.Kind);
        Assert.AreEqual(7, copy.SubjectId);
        Assert.AreEqual(1, copy.TargetId);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseIdList(copy.Text, out int[] requisitionNodeIds));
        CollectionAssert.AreEqual(new[] { 0, 2 }, requisitionNodeIds);

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

        var planetOrbitalBuildRequest = AuthoritativePlayerCommand.QueuePlanetOrbitalBuild(40, 2,
                planetId: 5, "Platform Base mk1-a")
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(planetOrbitalBuildRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.QueuePlanetOrbitalBuild, copy.Kind);
        Assert.AreEqual(5, copy.SubjectId);
        Assert.AreEqual("Platform Base mk1-a", copy.Text);

        var buildCapitalRequest = AuthoritativePlayerCommand.BuildCapitalHere(41, 2, planetId: 5)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(buildCapitalRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.BuildCapitalHere, copy.Kind);
        Assert.AreEqual(5, copy.SubjectId);

        var blueprints = new BlueprintsTemplate("MP Forge", exclusive: true, linkTo: "",
            new HashSet<string>(StringComparer.Ordinal) { "Factory", "Laboratory" },
            Planet.ColonyType.Industrial);
        var applyBlueprintsRequest = AuthoritativePlayerCommand.ApplyColonyBlueprints(42, 2,
                planetId: 5, blueprints)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(applyBlueprintsRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.ApplyColonyBlueprints, copy.Kind);
        Assert.AreEqual(5, copy.SubjectId);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseBlueprintsTemplate(copy.Text,
            out BlueprintsTemplate decodedBlueprints));
        Assert.AreEqual("MP Forge", decodedBlueprints.Name);
        Assert.IsTrue(decodedBlueprints.Exclusive);
        Assert.AreEqual(Planet.ColonyType.Industrial, decodedBlueprints.ColonyType);
        CollectionAssert.AreEqual(new[] { "Factory", "Laboratory" },
            decodedBlueprints.PlannedBuildings.OrderBy(name => name, StringComparer.Ordinal).ToArray());

        var clearBlueprintsRequest = AuthoritativePlayerCommand.ClearColonyBlueprints(43, 2, planetId: 5)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(clearBlueprintsRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.ClearColonyBlueprints, copy.Kind);
        Assert.AreEqual(5, copy.SubjectId);

        var scrapBuildingRequest = AuthoritativePlayerCommand.ScrapColonyTile(44, 2, planetId: 5,
                tileX: 2, tileY: 3, AuthoritativeColonyTileScrapKind.Building, "Factory")
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(scrapBuildingRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.ScrapColonyTile, copy.Kind);
        Assert.AreEqual(5, copy.SubjectId);
        Assert.AreEqual((int)AuthoritativeColonyTileScrapKind.Building, copy.TargetId);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseTileCoordinates(
            new Vector2(copy.X, copy.Y), out int tileX, out int tileY));
        Assert.AreEqual(2, tileX);
        Assert.AreEqual(3, tileY);
        Assert.AreEqual("Factory", copy.Text);

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

        var renamePatrolRequest = AuthoritativePlayerCommand.RenameFleetPatrol(36, 2,
                "Alpha Patrol", "Beta Patrol")
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(renamePatrolRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.RenameFleetPatrol, copy.Kind);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParsePatrolRenamePayload(copy.Text,
            out string oldPatrolName, out string newPatrolName));
        Assert.AreEqual("Alpha Patrol", oldPatrolName);
        Assert.AreEqual("Beta Patrol", newPatrolName);

        var deletePatrolRequest = AuthoritativePlayerCommand.DeleteFleetPatrol(37, 2, "Beta Patrol")
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(deletePatrolRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.DeleteFleetPatrol, copy.Kind);
        Assert.AreEqual("Beta Patrol", copy.Text);

        var clearPatrolRequest = AuthoritativePlayerCommand.ClearFleetPatrol(38, 2, 7)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(clearPatrolRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.ClearFleetPatrol, copy.Kind);
        Assert.AreEqual(7, copy.SubjectId);

        var patrolWaypoints = TestPatrolWaypoints(new Vector2(100f, 200f)).ToArray();
        var createPatrolRequest = AuthoritativePlayerCommand.CreateFleetPatrol(39, 2, 7, patrolWaypoints)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(createPatrolRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.CreateFleetPatrol, copy.Kind);
        Assert.AreEqual(7, copy.SubjectId);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParsePatrolWaypoints(copy.Text, out WayPoint[] decodedWaypoints));
        Assert.AreEqual(patrolWaypoints.Length, decodedWaypoints.Length);
        Assert.AreEqual(patrolWaypoints[0].Position, decodedWaypoints[0].Position);
        Assert.AreEqual(patrolWaypoints[1].Direction, decodedWaypoints[1].Direction);

        var specialOrderRequest = AuthoritativePlayerCommand.ShipSpecialOrder(28, 2, 99,
                AuthoritativeShipSpecialOrderType.Explore)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(specialOrderRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.ShipSpecialOrder, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual((int)AuthoritativeShipSpecialOrderType.Explore, copy.TargetId);

        var clearOrderRequest = AuthoritativePlayerCommand.ShipSpecialOrder(29, 2, 99,
                AuthoritativeShipSpecialOrderType.ClearOrders)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(clearOrderRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.ShipSpecialOrder, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual((int)AuthoritativeShipSpecialOrderType.ClearOrders, copy.TargetId);

        var lifecycleOrderRequest = AuthoritativePlayerCommand.ShipLifecycleOrder(32, 2, 99,
                AuthoritativeShipLifecycleOrderType.Scrap)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(lifecycleOrderRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.ShipLifecycleOrder, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual((int)AuthoritativeShipLifecycleOrderType.Scrap, copy.TargetId);

        var cancelScuttleRequest = AuthoritativePlayerCommand.ShipLifecycleOrder(33, 2, 99,
                AuthoritativeShipLifecycleOrderType.CancelScuttle)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(cancelScuttleRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.ShipLifecycleOrder, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual((int)AuthoritativeShipLifecycleOrderType.CancelScuttle, copy.TargetId);

        var stanceRequest = AuthoritativePlayerCommand.SetShipCombatStance(34, 2, 99,
                CombatState.HoldPosition)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(stanceRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetShipCombatStance, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual((int)CombatState.HoldPosition, copy.TargetId);

        var tradePolicyRequest = AuthoritativePlayerCommand.SetShipTradePolicy(35, 2, 99,
                AuthoritativeShipTradePolicyKind.Food, enabled: false)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(tradePolicyRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetShipTradePolicy, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual((int)AuthoritativeShipTradePolicyKind.Food, copy.TargetId);
        Assert.AreEqual("0", copy.Text);

        var carrierPolicyRequest = AuthoritativePlayerCommand.SetShipCarrierPolicy(36, 2, 99,
                AuthoritativeShipCarrierPolicyKind.FightersOut, enabled: true)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(carrierPolicyRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetShipCarrierPolicy, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual((int)AuthoritativeShipCarrierPolicyKind.FightersOut, copy.TargetId);
        Assert.AreEqual("1", copy.Text);

        var tradeRouteRequest = AuthoritativePlayerCommand.SetShipTradeRoute(37, 2, 99,
                planetId: 456, enabled: true)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(tradeRouteRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetShipTradeRoute, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual(456, copy.TargetId);
        Assert.AreEqual("1", copy.Text);

        var area = new Rectangle(-1000, 2000, 6000, 7000);
        var areaRequest = AuthoritativePlayerCommand.SetShipAreaOfOperation(38, 2, 99,
                AuthoritativeShipAreaOfOperationAction.AddRectangle, area)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(areaRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.SetShipAreaOfOperation, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual((int)AuthoritativeShipAreaOfOperationAction.AddRectangle, copy.TargetId);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseRectanglePayload(copy.Text, out Rectangle parsedArea));
        Assert.AreEqual(area.X, parsedArea.X);
        Assert.AreEqual(area.Y, parsedArea.Y);
        Assert.AreEqual(area.Width, parsedArea.Width);
        Assert.AreEqual(area.Height, parsedArea.Height);

        var refitRequest = AuthoritativePlayerCommand.RefitShip(39, 2, 99,
                "MP Refit Corvette", AuthoritativeShipRefitMode.Fleet, rush: true)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(refitRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.RefitShip, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual((int)AuthoritativeShipRefitMode.Fleet, copy.TargetId);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseShipRefitPayload(copy.Text,
            out string refitDesign, out bool refitRush));
        Assert.AreEqual("MP Refit Corvette", refitDesign);
        Assert.IsTrue(refitRush);

        var attackRequest = AuthoritativePlayerCommand.AttackShip(12, 2, 99, 100, queue: true)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(attackRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.AttackShip, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual(100, copy.TargetId);
        Assert.AreEqual("queue", copy.Text);

        var shipTargetRequest = AuthoritativePlayerCommand.ShipTargetOrder(40, 2, 99, 100,
                AuthoritativeShipTargetOrderType.TransferTroops)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(shipTargetRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.ShipTargetOrder, copy.Kind);
        Assert.AreEqual(99, copy.SubjectId);
        Assert.AreEqual(100, copy.TargetId);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseShipTargetOrderPayload(copy.Text,
            out AuthoritativeShipTargetOrderType shipTargetOrder, out bool shipTargetQueued));
        Assert.AreEqual(AuthoritativeShipTargetOrderType.TransferTroops, shipTargetOrder);
        Assert.IsFalse(shipTargetQueued);

        var queuedShipTargetRequest = AuthoritativePlayerCommand.ShipTargetOrder(41, 2, 99, 100,
                AuthoritativeShipTargetOrderType.Attack, queue: true)
            .ToMessage(fromPeer: 2);
        decoded = LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(queuedShipTargetRequest, toPeer: 1));
        copy = (AuthoritativeCommandRequestMessage)decoded.Message;
        Assert.AreEqual((byte)AuthoritativePlayerCommandKind.ShipTargetOrder, copy.Kind);
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseShipTargetOrderPayload(copy.Text,
            out shipTargetOrder, out shipTargetQueued));
        Assert.AreEqual(AuthoritativeShipTargetOrderType.Attack, shipTargetOrder);
        Assert.IsTrue(shipTargetQueued);

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
    public void Authoritative4XShipTargetOrder_AppliesVariantsAndSyncs_Headless()
    {
        const ulong Seed = 0x5147E70UL;
        BuiltWorld authority = BuildWorld(Seed, includeTroopShips: true);
        BuiltWorld client = BuildWorld(Seed, includeTroopShips: true);

        try
        {
            EnsureTroopLoaded(authority.TroopShip);
            EnsureTroopLoaded(client.TroopShip);
            MakeAtWar(authority.Player, authority.Enemy);
            MakeAtWar(client.Player, client.Enemy);

            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);

            var escort = AuthoritativePlayerCommand.ShipTargetOrder(20, authority.Player.Id,
                authority.Ship.Id, authority.WingShip.Id, AuthoritativeShipTargetOrderType.Escort);
            session.SubmitFromClient(escort);
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(AIState.Escort, authority.Ship.AI.State);
            AssertShipOrder(authority.Ship, ShipAI.Plan.Escort, authority.WingShip,
                "The authority should apply friendly ship target orders as escort goals.");
            AssertShipOrder(client.Ship, ShipAI.Plan.Escort, client.WingShip,
                "The client replica should apply accepted escort goals deterministically.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeTransferDigest = session.LastAuthoritySnapshot.SyncDigest;
            var transfer = AuthoritativePlayerCommand.ShipTargetOrder(21, authority.Player.Id,
                authority.TroopShip.Id, authority.FriendlyTroopTargetShip.Id,
                AuthoritativeShipTargetOrderType.TransferTroops);
            session.SubmitFromClient(transfer);
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(authority.FriendlyTroopTargetShip, authority.TroopShip.AI.EscortTarget);
            AssertShipPlan(authority.TroopShip, ShipAI.Plan.TroopToShip,
                "The authority should apply troop transfer through OrderTroopToShip.");
            Assert.AreEqual(client.FriendlyTroopTargetShip, client.TroopShip.AI.EscortTarget);
            AssertShipPlan(client.TroopShip, ShipAI.Plan.TroopToShip,
                "The client replica should apply accepted troop transfers deterministically.");
            Assert.AreNotEqual(beforeTransferDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover troop transfer target orders.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeBoardDigest = session.LastAuthoritySnapshot.SyncDigest;
            var board = AuthoritativePlayerCommand.ShipTargetOrder(22, authority.Player.Id,
                authority.TroopShip.Id, authority.EnemyShip.Id, AuthoritativeShipTargetOrderType.Board);
            session.SubmitFromClient(board);
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(authority.EnemyShip, authority.TroopShip.AI.EscortTarget);
            AssertShipPlan(authority.TroopShip, ShipAI.Plan.BoardShip,
                "The authority should apply boarding through OrderTroopToBoardShip.");
            Assert.AreEqual(client.EnemyShip, client.TroopShip.AI.EscortTarget);
            AssertShipPlan(client.TroopShip, ShipAI.Plan.BoardShip,
                "The client replica should apply accepted boarding orders deterministically.");
            Assert.AreNotEqual(beforeBoardDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover boarding target orders.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeQueuedAttackDigest = session.LastAuthoritySnapshot.SyncDigest;
            var queuedAttack = AuthoritativePlayerCommand.ShipTargetOrder(23, authority.Player.Id,
                authority.WingShip.Id, authority.EnemyShip.Id, AuthoritativeShipTargetOrderType.Attack,
                queue: true);
            session.SubmitFromClient(queuedAttack);
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(AIState.AttackTarget, authority.WingShip.AI.State);
            Assert.AreEqual(authority.EnemyShip, authority.WingShip.AI.Target);
            Assert.AreEqual(client.EnemyShip, client.WingShip.AI.Target);
            Assert.IsTrue(authority.WingShip.AI.HasPriorityTarget);
            Assert.AreNotEqual(beforeQueuedAttackDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover queued ship target attack orders.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            var illegalFriendlyAttack = AuthoritativePlayerCommand.ShipTargetOrder(24, authority.Player.Id,
                authority.Ship.Id, authority.WingShip.Id, AuthoritativeShipTargetOrderType.Attack);
            session.SubmitFromClient(illegalFriendlyAttack);
            Assert.IsFalse(session.LastResult.Accepted, "Friendly ships must not be legal attack targets.");
            StringAssert.Contains(session.LastResult.Reason, "cannot attack");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            var illegalEnemyEscort = AuthoritativePlayerCommand.ShipTargetOrder(25, authority.Player.Id,
                authority.Ship.Id, authority.EnemyShip.Id, AuthoritativeShipTargetOrderType.Escort);
            session.SubmitFromClient(illegalEnemyEscort);
            Assert.IsFalse(session.LastResult.Accepted, "Enemy ships must not be legal escort targets.");
            StringAssert.Contains(session.LastResult.Reason, "friendly escort");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            var illegalNonTroopBoard = AuthoritativePlayerCommand.ShipTargetOrder(26, authority.Player.Id,
                authority.Ship.Id, authority.EnemyShip.Id, AuthoritativeShipTargetOrderType.Board);
            session.SubmitFromClient(illegalNonTroopBoard);
            Assert.IsFalse(session.LastResult.Accepted, "Non-troop ships must not receive boarding orders.");
            StringAssert.Contains(session.LastResult.Reason, "boarding troop");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            var illegalTransferCapacity = AuthoritativePlayerCommand.ShipTargetOrder(27, authority.Player.Id,
                authority.TroopShip.Id, authority.WingShip.Id, AuthoritativeShipTargetOrderType.TransferTroops);
            session.SubmitFromClient(illegalTransferCapacity);
            Assert.IsFalse(session.LastResult.Accepted,
                "Troop transfer must require spare troop capacity on the friendly target ship.");
            StringAssert.Contains(session.LastResult.Reason, "troop capacity");
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
    public void Authoritative4XShipPlanetOrder_LandsFriendlyTroopsAndSyncs_Headless()
    {
        const ulong Seed = 0x5147E73UL;
        BuiltWorld authority = BuildWorld(Seed, includeTroopShips: true);
        BuiltWorld client = BuildWorld(Seed, includeTroopShips: true);

        try
        {
            EnsureTroopLoaded(authority.TroopShip);
            EnsureTroopLoaded(client.TroopShip);
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string beforeLandingDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            var land = AuthoritativePlayerCommand.ShipPlanetOrder(30, authority.Player.Id,
                authority.TroopShip.Id, authority.Planet.Id, AuthoritativeShipPlanetOrderType.LandTroops,
                clearOrders: true, MoveOrder.Regular);
            session.SubmitFromClient(land);

            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            AssertShipPlan(authority.TroopShip, ShipAI.Plan.LandTroop,
                "The authority should apply friendly troop landings through the real ship AI order queue.");
            AssertShipPlan(client.TroopShip, ShipAI.Plan.LandTroop,
                "The client replica should apply accepted friendly troop landings deterministically.");
            Assert.AreNotEqual(beforeLandingDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover accepted troop landing orders.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XShipPlanetOrder_ColonizeAssignsShipAndSyncs_Headless()
    {
        const ulong Seed = 0xC010C01UL;
        BuiltWorld authority = BuildWorld(Seed, includeNeutralPlanet: true, includeColonyShip: true);
        BuiltWorld client = BuildWorld(Seed, includeNeutralPlanet: true, includeColonyShip: true);

        try
        {
            Ship authorityColony = authority.ColonyShip;
            Ship clientColony = client.ColonyShip;
            Assert.AreEqual(authorityColony.Id, clientColony.Id,
                "Authority and replica need matching spawned colony ship ids for command replay.");

            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            var colonize = AuthoritativePlayerCommand.ShipPlanetOrder(12, authority.Player.Id,
                authorityColony.Id, authority.NeutralPlanet.Id, AuthoritativeShipPlanetOrderType.Colonize,
                clearOrders: true, MoveOrder.Regular);
            session.SubmitFromClient(colonize);
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);

            MarkForColonization authorityGoal = authority.Player.AI.FindGoals<MarkForColonization>()
                .FirstOrDefault(g => g.TargetPlanet == authority.NeutralPlanet);
            MarkForColonization clientGoal = client.Player.AI.FindGoals<MarkForColonization>()
                .FirstOrDefault(g => g.TargetPlanet == client.NeutralPlanet);
            Assert.IsNotNull(authorityGoal, "The host should create the real ship-bound colonization goal.");
            Assert.IsNotNull(clientGoal, "The replica should receive the accepted colonization goal.");
            Assert.AreSame(authorityColony, authorityGoal.FinishedShip);
            Assert.AreSame(clientColony, clientGoal.FinishedShip);
            Assert.IsTrue(authorityGoal.IsManualColonizationOrder);
            Assert.IsTrue(clientGoal.IsManualColonizationOrder);
            Assert.AreEqual(AIState.Colonize, authorityColony.AI.State);
            Assert.AreEqual(AIState.Colonize, clientColony.AI.State);
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload,
                $"G|{authority.Player.Id}|MarkForColonization|{authority.NeutralPlanet.Id}|1|{authorityColony.Id}");
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover ship-bound colonization orders.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string acceptedDigest = session.LastAuthoritySnapshot.SyncDigest;
            var nonColony = AuthoritativePlayerCommand.ShipPlanetOrder(13, authority.Player.Id,
                authority.Ship.Id, authority.NeutralPlanet.Id, AuthoritativeShipPlanetOrderType.Colonize);
            session.SubmitFromClient(nonColony);
            Assert.IsFalse(session.LastResult.Accepted, "Non-colony ships must not receive colonize orders.");
            StringAssert.Contains(session.LastResult.Reason, "not a colony ship");
            Assert.AreEqual(acceptedDigest, session.LastAuthoritySnapshot.SyncDigest);

            var ownedTarget = AuthoritativePlayerCommand.ShipPlanetOrder(14, authority.Player.Id,
                authorityColony.Id, authority.Planet.Id, AuthoritativeShipPlanetOrderType.Colonize);
            session.SubmitFromClient(ownedTarget);
            Assert.IsFalse(session.LastResult.Accepted, "Owned planets must not receive colonize orders.");
            StringAssert.Contains(session.LastResult.Reason, "not a legal colonization target");
            Assert.AreEqual(acceptedDigest, session.LastAuthoritySnapshot.SyncDigest);
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
    public void Authoritative4XBuildCapitalHere_SyncsAndRejectsUnownedPlanets_Headless()
    {
        const ulong Seed = 0xCA917A1UL;
        BuiltWorld authority = BuildWorld(Seed, includeNeutralPlanet: true);
        BuiltWorld client = BuildWorld(Seed, includeNeutralPlanet: true);

        try
        {
            EnsureSingleBuildTile(authority.Planet);
            EnsureSingleBuildTile(client.Planet);
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;
            Assert.IsFalse(authority.Planet.IsHomeworld);
            Assert.IsFalse(authority.Planet.TestIsCapitalInQueue());
            Assert.IsFalse(client.Planet.IsHomeworld);
            Assert.IsFalse(client.Planet.TestIsCapitalInQueue());

            session.SubmitFromClient(AuthoritativePlayerCommand.BuildCapitalHere(84,
                authority.Player.Id, authority.Planet.Id));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.Planet.IsHomeworld);
            Assert.IsTrue(client.Planet.IsHomeworld);
            Assert.IsTrue(authority.Planet.TestIsCapitalInQueue());
            Assert.IsTrue(client.Planet.TestIsCapitalInQueue());
            Assert.AreEqual(1, authority.Planet.ConstructionQueue.Count(q => q.isBuilding && q.Building.IsCapital));
            Assert.AreEqual(1, client.Planet.ConstructionQueue.Count(q => q.isBuilding && q.Building.IsCapital));
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover the capital rebuild queue item.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string acceptedDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.BuildCapitalHere(85,
                authority.Enemy.Id, authority.Planet.Id));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not rebuild capital on another empire's planet.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(1, authority.Planet.ConstructionQueue.Count(q => q.isBuilding && q.Building.IsCapital));
            Assert.AreEqual(acceptedDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.BuildCapitalHere(86,
                authority.Player.Id, authority.NeutralPlanet.Id));
            Assert.IsFalse(session.LastResult.Accepted, "Unowned planets must not accept capital rebuild commands.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.IsFalse(authority.NeutralPlanet.IsHomeworld);
            Assert.IsFalse(authority.NeutralPlanet.TestIsCapitalInQueue());
            Assert.AreEqual(acceptedDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsBuildCapitalHereWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xCA917C1UL;
        BuiltWorld world = BuildWorld(Seed, includeNeutralPlanet: true);

        try
        {
            bool originalHomeworld = world.Planet.IsHomeworld;
            int originalQueueCount = world.Planet.ConstructionQueue.Count;
            bool originalCapitalQueued = world.Planet.TestIsCapitalInQueue();
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2600))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitBuildCapitalHere(world.Planet));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2600, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.BuildCapitalHere, submitted[0].Kind);
                Assert.AreEqual(world.Player.Id, submitted[0].EmpireId);
                Assert.AreEqual(world.Planet.Id, submitted[0].SubjectId);
                Assert.AreEqual(originalHomeworld, world.Planet.IsHomeworld,
                    "Passive MP clients must not locally mark the colony as a homeworld before host acceptance.");
                Assert.AreEqual(originalQueueCount, world.Planet.ConstructionQueue.Count,
                    "Passive MP clients must not locally enqueue capital rebuild before host acceptance.");
                Assert.AreEqual(originalCapitalQueued, world.Planet.TestIsCapitalInQueue());

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitBuildCapitalHere(world.EnemyPlanet));
                Assert.AreEqual(1, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitBuildCapitalHere(world.NeutralPlanet));
                Assert.AreEqual(1, submitted.Count);
                Assert.IsFalse(world.NeutralPlanet.IsHomeworld);
                Assert.IsFalse(world.NeutralPlanet.TestIsCapitalInQueue());
            }
        }
        finally
        {
            world.Screen.Dispose();
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

            string beforeManualTradeSlotsDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetManualTradeSlots(95,
                authority.Player.Id, authority.Planet.Id,
                foodImport: 2, prodImport: 3, coloImport: 4,
                foodExport: 5, prodExport: 6, coloExport: 7));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            AssertPlanetManualTradeSlots(authority.Planet,
                expectedFoodImport: 2, expectedProdImport: 3, expectedColoImport: 4,
                expectedFoodExport: 5, expectedProdExport: 6, expectedColoExport: 7);
            AssertPlanetManualTradeSlots(client.Planet,
                expectedFoodImport: 2, expectedProdImport: 3, expectedColoImport: 4,
                expectedFoodExport: 5, expectedProdExport: 6, expectedColoExport: 7);
            Assert.AreNotEqual(beforeManualTradeSlotsDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover manual trade slot changes.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeDefenseTargetsDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetDefenseTargets(104,
                authority.Player.Id, authority.Planet.Id,
                garrisonSize: 8, wantedPlatforms: 9, wantedShipyards: 2, wantedStations: 6));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            AssertPlanetDefenseTargets(authority.Planet, expectedGarrisonSize: 8,
                expectedWantedPlatforms: 9, expectedWantedShipyards: 2, expectedWantedStations: 6);
            AssertPlanetDefenseTargets(client.Planet, expectedGarrisonSize: 8,
                expectedWantedPlatforms: 9, expectedWantedShipyards: 2, expectedWantedStations: 6);
            Assert.AreNotEqual(beforeDefenseTargetsDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover defense target changes.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetManualBudget(96,
                authority.Enemy.Id, authority.Planet.Id, AuthoritativePlanetBudgetKind.Civilian, 1f));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not change another empire's governor budget.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(12.5f, authority.Planet.ManualCivilianBudget);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetManualBudget(97,
                authority.Player.Id, authority.Planet.Id, AuthoritativePlanetBudgetKind.Civilian, -1f));
            Assert.IsFalse(session.LastResult.Accepted, "Negative manual budgets must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "finite non-negative");
            Assert.AreEqual(12.5f, authority.Planet.ManualCivilianBudget);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            authority.Planet.HasSpacePort = false;
            client.Planet.HasSpacePort = false;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetPrioritizedPort(98,
                authority.Player.Id, authority.Planet.Id, true));
            Assert.IsFalse(session.LastResult.Accepted,
                "A planet without a space port must not be marked as a prioritized port.");
            StringAssert.Contains(session.LastResult.Reason, "space port");
            Assert.IsTrue(authority.Planet.PrioritizedPort,
                "Rejected prioritized-port requests must not mutate the existing port preference.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectedGovernorDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetGovernorOptions(99,
                authority.Enemy.Id, authority.Planet.Id, AuthoritativePlanetGovernorOptions.Quarantine));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not change another empire's governor options.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            AssertGovernorOptions(authority.Planet, expectedGovOrbitals: true, expectedAutoTroops: true,
                expectedNoScrap: false, expectedQuarantine: true, expectedManualOrbitals: true,
                expectedGovGround: true, expectedSpecializedTradeHub: true);
            Assert.AreEqual(beforeRejectedGovernorDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetGovernorOptions(100,
                authority.Player.Id, authority.Planet.Id, (AuthoritativePlanetGovernorOptions)(1 << 12)));
            Assert.IsFalse(session.LastResult.Accepted, "Unsupported governor option bits must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "Unsupported planet governor option flags");
            AssertGovernorOptions(authority.Planet, expectedGovOrbitals: true, expectedAutoTroops: true,
                expectedNoScrap: false, expectedQuarantine: true, expectedManualOrbitals: true,
                expectedGovGround: true, expectedSpecializedTradeHub: true);
            Assert.AreEqual(beforeRejectedGovernorDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetManualTradeSlots(101,
                authority.Enemy.Id, authority.Planet.Id,
                foodImport: 1, prodImport: 1, coloImport: 1,
                foodExport: 1, prodExport: 1, coloExport: 1));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not change another empire's manual trade slots.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            AssertPlanetManualTradeSlots(authority.Planet,
                expectedFoodImport: 2, expectedProdImport: 3, expectedColoImport: 4,
                expectedFoodExport: 5, expectedProdExport: 6, expectedColoExport: 7);
            Assert.AreEqual(beforeRejectedGovernorDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetDefenseTargets(105,
                authority.Enemy.Id, authority.Planet.Id,
                garrisonSize: 1, wantedPlatforms: 1, wantedShipyards: 1, wantedStations: 1));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not change another empire's defense targets.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            AssertPlanetDefenseTargets(authority.Planet, expectedGarrisonSize: 8,
                expectedWantedPlatforms: 9, expectedWantedShipyards: 2, expectedWantedStations: 6);
            Assert.AreEqual(beforeRejectedGovernorDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetDefenseTargets(106,
                authority.Player.Id, authority.Planet.Id,
                garrisonSize: 26, wantedPlatforms: 0, wantedShipyards: 0, wantedStations: 0));
            Assert.IsFalse(session.LastResult.Accepted, "Garrison size must stay within the slider range.");
            StringAssert.Contains(session.LastResult.Reason, "Invalid planet defense target payload");
            AssertPlanetDefenseTargets(authority.Planet, expectedGarrisonSize: 8,
                expectedWantedPlatforms: 9, expectedWantedShipyards: 2, expectedWantedStations: 6);
            Assert.AreEqual(beforeRejectedGovernorDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetDefenseTargets(107,
                authority.Player.Id, authority.Planet.Id,
                garrisonSize: 0, wantedPlatforms: 0, wantedShipyards: 4, wantedStations: 0));
            Assert.IsFalse(session.LastResult.Accepted, "Wanted shipyards must stay within the slider range.");
            StringAssert.Contains(session.LastResult.Reason, "Invalid planet defense target payload");
            AssertPlanetDefenseTargets(authority.Planet, expectedGarrisonSize: 8,
                expectedWantedPlatforms: 9, expectedWantedShipyards: 2, expectedWantedStations: 6);
            Assert.AreEqual(beforeRejectedGovernorDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetManualTradeSlots(102,
                authority.Player.Id, authority.Planet.Id,
                foodImport: 21, prodImport: 0, coloImport: 0,
                foodExport: 0, prodExport: 0, coloExport: 0));
            Assert.IsFalse(session.LastResult.Accepted, "Import slot overrides must stay within the slider range.");
            StringAssert.Contains(session.LastResult.Reason, "Invalid planet manual trade slot payload");
            AssertPlanetManualTradeSlots(authority.Planet,
                expectedFoodImport: 2, expectedProdImport: 3, expectedColoImport: 4,
                expectedFoodExport: 5, expectedProdExport: 6, expectedColoExport: 7);
            Assert.AreEqual(beforeRejectedGovernorDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetPlanetManualTradeSlots(103,
                authority.Player.Id, authority.Planet.Id,
                foodImport: 0, prodImport: 0, coloImport: 0,
                foodExport: 0, prodExport: 26, coloExport: 0));
            Assert.IsFalse(session.LastResult.Accepted, "Export slot overrides must stay within the slider range.");
            StringAssert.Contains(session.LastResult.Reason, "Invalid planet manual trade slot payload");
            AssertPlanetManualTradeSlots(authority.Planet,
                expectedFoodImport: 2, expectedProdImport: 3, expectedColoImport: 4,
                expectedFoodExport: 5, expectedProdExport: 6, expectedColoExport: 7);
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
    public void Authoritative4XColonyBlueprints_AppliesClearsAndSyncs_Headless()
    {
        const ulong Seed = 0xB10E9A17UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            Building blueprintBuilding = PickBlueprintBuilding(authority.Planet);
            BlueprintsTemplate template = TestBlueprintTemplate("MP Industrial Plan", blueprintBuilding,
                Planet.ColonyType.Industrial);
            authority.Planet.DontScrapBuildings = true;
            client.Planet.DontScrapBuildings = true;
            authority.Planet.SetSpecializedTradeHub(true);
            client.Planet.SetSpecializedTradeHub(true);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.ApplyColonyBlueprints(108,
                authority.Player.Id, authority.Planet.Id, template));

            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            AssertColonyBlueprints(authority.Planet, "MP Industrial Plan", blueprintBuilding.Name,
                Planet.ColonyType.Industrial);
            AssertColonyBlueprints(client.Planet, "MP Industrial Plan", blueprintBuilding.Name,
                Planet.ColonyType.Industrial);
            Assert.IsFalse(authority.Planet.DontScrapBuildings,
                "Loading blueprints should mirror the live UI and clear the no-scrap governor flag.");
            Assert.IsFalse(authority.Planet.SpecializedTradeHub,
                "Loading blueprints should mirror the live UI and clear specialized trade hub.");
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover colony blueprint apply state.");
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, $"BP|{authority.Planet.Id}|MP Industrial Plan");
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, blueprintBuilding.Name);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeClearDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.ClearColonyBlueprints(109,
                authority.Player.Id, authority.Planet.Id));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsFalse(authority.Planet.HasBlueprints);
            Assert.IsFalse(client.Planet.HasBlueprints);
            Assert.AreNotEqual(beforeClearDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover colony blueprint clear state.");
            Assert.IsFalse(session.LastAuthoritySnapshot.Payload.Contains($"BP|{authority.Planet.Id}|"));
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            var linkedTemplate = TestBlueprintTemplate("MP Linked Plan", blueprintBuilding,
                Planet.ColonyType.Industrial, linkTo: "Local AppData Template");
            session.SubmitFromClient(AuthoritativePlayerCommand.ApplyColonyBlueprints(110,
                authority.Player.Id, authority.Planet.Id, linkedTemplate));
            Assert.IsFalse(session.LastResult.Accepted,
                "Linked blueprint chains depend on local saved-template catalogs and must be rejected in MP.");
            StringAssert.Contains(session.LastResult.Reason, "Linked colony blueprints");
            Assert.IsFalse(authority.Planet.HasBlueprints);
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.ApplyColonyBlueprints(111,
                authority.Enemy.Id, authority.Planet.Id, template));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not load blueprints onto another empire's planet.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
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
    public void Authoritative4XClientContext_SubmitsColonyBlueprintsWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xB10E9A18UL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            Building blueprintBuilding = PickBlueprintBuilding(world.Planet);
            BlueprintsTemplate template = TestBlueprintTemplate("Passive MP Plan", blueprintBuilding,
                Planet.ColonyType.Research);
            Planet.ColonyType originalType = world.Planet.CType;
            world.Planet.DontScrapBuildings = true;
            world.Planet.SetSpecializedTradeHub(true);
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2075))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitApplyColonyBlueprints(world.Planet, template));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2075, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ApplyColonyBlueprints, submitted[0].Kind);
                Assert.AreEqual(world.Planet.Id, submitted[0].SubjectId);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParseBlueprintsTemplate(submitted[0].Text,
                    out BlueprintsTemplate decoded));
                Assert.AreEqual("Passive MP Plan", decoded.Name);
                Assert.IsFalse(world.Planet.HasBlueprints,
                    "Passive MP clients must not apply blueprints locally before host acceptance.");
                Assert.AreEqual(originalType, world.Planet.CType);
                Assert.IsTrue(world.Planet.DontScrapBuildings);
                Assert.IsTrue(world.Planet.SpecializedTradeHub);

                world.Planet.AddBlueprints(template, world.Player);
                Assert.IsTrue(world.Planet.HasBlueprints);
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitClearColonyBlueprints(world.Planet));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(2076, submitted[1].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ClearColonyBlueprints, submitted[1].Kind);
                Assert.IsTrue(world.Planet.HasBlueprints,
                    "Passive MP clients must not clear blueprints locally before host acceptance.");

                var linkedTemplate = TestBlueprintTemplate("Rejected Link", blueprintBuilding,
                    Planet.ColonyType.Research, linkTo: "Local AppData Template");
                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitApplyColonyBlueprints(world.Planet, linkedTemplate));
                Assert.AreEqual(2, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitApplyColonyBlueprints(world.EnemyPlanet, template));
                Assert.AreEqual(2, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XColonyTileScrap_ScrapsBuildingBiosphereAndSyncs_Headless()
    {
        const ulong Seed = 0x5C2A9A7EUL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            (PlanetGridSquare authorityBuildingTile, PlanetGridSquare authorityBioTile, string buildingName)
                = PrepareScrapTiles(authority.Planet);
            (PlanetGridSquare clientBuildingTile, PlanetGridSquare clientBioTile, _)
                = PrepareScrapTiles(client.Planet, buildingName);
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.ScrapColonyTile(112,
                authority.Player.Id, authority.Planet.Id, authorityBuildingTile.X, authorityBuildingTile.Y,
                AuthoritativeColonyTileScrapKind.Building, buildingName));

            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsNull(authorityBuildingTile.Building,
                "The authoritative host should scrap the requested building through the real planet API.");
            Assert.IsNull(clientBuildingTile.Building,
                "The replica should apply the accepted building scrap deterministically.");
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover colony tile building removal.");
            Assert.IsFalse(session.LastAuthoritySnapshot.Payload.Contains(
                $"T|{authority.Planet.Id}|{authorityBuildingTile.X}|{authorityBuildingTile.Y}|{buildingName}|"));
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string buildingScrappedDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.ScrapColonyTile(113,
                authority.Player.Id, authority.Planet.Id, authorityBioTile.X, authorityBioTile.Y,
                AuthoritativeColonyTileScrapKind.Biosphere));

            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsFalse(authorityBioTile.Biosphere);
            Assert.IsFalse(clientBioTile.Biosphere);
            Assert.IsFalse(authorityBioTile.Habitable);
            Assert.IsFalse(clientBioTile.Habitable);
            Assert.AreNotEqual(buildingScrappedDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover colony tile biosphere removal.");
            Assert.IsFalse(session.LastAuthoritySnapshot.Payload.Contains(
                $"T|{authority.Planet.Id}|{authorityBioTile.X}|{authorityBioTile.Y}||1|"));
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectedDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.ScrapColonyTile(114,
                authority.Player.Id, authority.Planet.Id, authorityBuildingTile.X, authorityBuildingTile.Y,
                AuthoritativeColonyTileScrapKind.Building, buildingName));
            Assert.IsFalse(session.LastResult.Accepted,
                "A stale building-scrap request must not mutate a tile that no longer contains the expected building.");
            StringAssert.Contains(session.LastResult.Reason, "no building");
            Assert.AreEqual(beforeRejectedDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.ScrapColonyTile(115,
                authority.Enemy.Id, authority.Planet.Id, authorityBioTile.X, authorityBioTile.Y,
                AuthoritativeColonyTileScrapKind.Biosphere));
            Assert.IsFalse(session.LastResult.Accepted,
                "An empire must not scrap another empire's colony tile.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(beforeRejectedDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsColonyTileScrapWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0x5C2A9A7FUL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            (PlanetGridSquare buildingTile, PlanetGridSquare bioTile, string buildingName)
                = PrepareScrapTiles(world.Planet);
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2085))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitScrapColonyBuilding(world.Planet,
                        buildingTile, buildingName));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2085, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ScrapColonyTile, submitted[0].Kind);
                Assert.AreEqual(world.Planet.Id, submitted[0].SubjectId);
                Assert.AreEqual((int)AuthoritativeColonyTileScrapKind.Building, submitted[0].TargetId);
                Assert.AreEqual(buildingName, submitted[0].Text);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParseTileCoordinates(submitted[0].Position,
                    out int x, out int y));
                Assert.AreEqual(buildingTile.X, x);
                Assert.AreEqual(buildingTile.Y, y);
                Assert.IsNotNull(buildingTile.Building,
                    "Passive MP clients must not locally scrap buildings before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitScrapColonyBiosphere(world.Planet, bioTile));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(2086, submitted[1].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ScrapColonyTile, submitted[1].Kind);
                Assert.AreEqual((int)AuthoritativeColonyTileScrapKind.Biosphere, submitted[1].TargetId);
                Assert.IsTrue(bioTile.Biosphere,
                    "Passive MP clients must not locally destroy biospheres before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitScrapColonyBuilding(world.Planet,
                        buildingTile, "Stale Building Name"));
                Assert.AreEqual(2, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitScrapColonyBuilding(world.EnemyPlanet,
                        buildingTile, buildingName));
                Assert.AreEqual(2, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
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
            AssertPlanetManualTradeSlots(world.Planet,
                expectedFoodImport: 0, expectedProdImport: 0, expectedColoImport: 0,
                expectedFoodExport: 0, expectedProdExport: 0, expectedColoExport: 0);
            AssertPlanetDefenseTargets(world.Planet, expectedGarrisonSize: 0,
                expectedWantedPlatforms: 0, expectedWantedShipyards: 0, expectedWantedStations: 0);
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

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetPlanetManualTradeSlots(world.Planet,
                        foodImport: 2, prodImport: 3, coloImport: 4,
                        foodExport: 5, prodExport: 6, coloExport: 7));
                Assert.AreEqual(4, submitted.Count);
                Assert.AreEqual(2053, submitted[3].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetPlanetManualTradeSlots, submitted[3].Kind);
                Assert.AreEqual(world.Planet.Id, submitted[3].SubjectId);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParseManualTradeSlotsPayload(submitted[3].Text,
                    out int foodImport, out int prodImport, out int coloImport,
                    out int foodExport, out int prodExport, out int coloExport));
                AssertManualTradeSlots(foodImport, prodImport, coloImport, foodExport, prodExport, coloExport,
                    expectedFoodImport: 2, expectedProdImport: 3, expectedColoImport: 4,
                    expectedFoodExport: 5, expectedProdExport: 6, expectedColoExport: 7);
                AssertPlanetManualTradeSlots(world.Planet,
                    expectedFoodImport: 0, expectedProdImport: 0, expectedColoImport: 0,
                    expectedFoodExport: 0, expectedProdExport: 0, expectedColoExport: 0);

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetPlanetDefenseTargets(world.Planet,
                        garrisonSize: 8, wantedPlatforms: 9, wantedShipyards: 2, wantedStations: 6));
                Assert.AreEqual(5, submitted.Count);
                Assert.AreEqual(2054, submitted[4].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetPlanetDefenseTargets, submitted[4].Kind);
                Assert.AreEqual(world.Planet.Id, submitted[4].SubjectId);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParsePlanetDefenseTargetsPayload(submitted[4].Text,
                    out int garrisonSize, out int wantedPlatforms, out int wantedShipyards, out int wantedStations));
                AssertDefenseTargets(garrisonSize, wantedPlatforms, wantedShipyards, wantedStations,
                    expectedGarrisonSize: 8, expectedWantedPlatforms: 9,
                    expectedWantedShipyards: 2, expectedWantedStations: 6);
                AssertPlanetDefenseTargets(world.Planet, expectedGarrisonSize: 0,
                    expectedWantedPlatforms: 0, expectedWantedShipyards: 0, expectedWantedStations: 0);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetPlanetManualBudget(world.EnemyPlanet,
                        AuthoritativePlanetBudgetKind.Civilian, 1f));
                Assert.AreEqual(5, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetPlanetGovernorOptions(world.EnemyPlanet));
                Assert.AreEqual(5, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetPlanetManualTradeSlots(world.EnemyPlanet,
                        foodImport: 1, prodImport: 1, coloImport: 1,
                        foodExport: 1, prodExport: 1, coloExport: 1));
                Assert.AreEqual(5, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetPlanetManualTradeSlots(world.Planet,
                        foodImport: 21, prodImport: 0, coloImport: 0,
                        foodExport: 0, prodExport: 0, coloExport: 0));
                Assert.AreEqual(5, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetPlanetDefenseTargets(world.EnemyPlanet,
                        garrisonSize: 1, wantedPlatforms: 1, wantedShipyards: 1, wantedStations: 1));
                Assert.AreEqual(5, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetPlanetDefenseTargets(world.Planet,
                        garrisonSize: 0, wantedPlatforms: 16, wantedShipyards: 0, wantedStations: 0));
                Assert.AreEqual(5, submitted.Count);
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
    public void Authoritative4XShipPlanetRename_ValidatesAndSyncs_Headless()
    {
        const ulong Seed = 0xF1EE4A12UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);

            session.SubmitFromClient(AuthoritativePlayerCommand.RenameShip(140,
                authority.Player.Id, authority.Ship.Id, "  Spear One  "));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual("Spear One", authority.Ship.VanityName);
            Assert.AreEqual("Spear One", client.Ship.VanityName);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.RenamePlanet(141,
                authority.Player.Id, authority.Planet.Id, "  Anchor  "));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual("Anchor", authority.Planet.Name);
            Assert.AreEqual("Anchor", client.Planet.Name);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.RenameShip(142,
                authority.Player.Id, authority.EnemyShip.Id, "Enemy Rename"));
            Assert.IsFalse(session.LastResult.Accepted, "A player must not rename enemy ships.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.RenamePlanet(143,
                authority.Player.Id, authority.EnemyPlanet.Id, "Enemy World"));
            Assert.IsFalse(session.LastResult.Accepted, "A player must not rename enemy planets.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.RenameShip(144,
                authority.Player.Id, authority.Ship.Id, new string('A', AuthoritativePlayerCommand.MaxShipRenameLength + 1)));
            Assert.IsFalse(session.LastResult.Accepted, "Overlong ship names must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "too long");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.RenamePlanet(145,
                authority.Player.Id, authority.Planet.Id, "Bad\nName"));
            Assert.IsFalse(session.LastResult.Accepted, "Control characters must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "control");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XFleetDesignLoadCommands_ReplaceRenameIconAndSync_Headless()
    {
        const ulong Seed = 0xF1EE4A13UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;
            IShipDesign designOnly = PickMobileBuildableShip(authority.Player);
            var savedLayout = new[]
            {
                new FleetDataNode
                {
                    ShipName = designOnly.Name,
                    RelativeFleetOffset = new Vector2(18_000f, -9_000f),
                    DPSWeight = 0.77f,
                    CombatState = CombatState.OrbitLeft,
                    OrdersRadius = 444_444f,
                },
            };

            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetLayout(135,
                authority.Player.Id, fleetKey: 3, savedLayout));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);

            session.SubmitFromClient(AuthoritativePlayerCommand.RenameFleet(136,
                authority.Player.Id, fleetKey: 3, "Saved Wing"));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetIcon(137,
                authority.Player.Id, fleetKey: 3, iconIndex: 22));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);

            Fleet authorityFleet = authority.Player.GetFleetOrNull(3);
            Fleet clientFleet = client.Player.GetFleetOrNull(3);
            Assert.IsNotNull(authorityFleet);
            Assert.IsNotNull(clientFleet);
            Assert.AreEqual("Saved Wing", authorityFleet.Name);
            Assert.AreEqual("Saved Wing", clientFleet.Name);
            Assert.AreEqual(22, authorityFleet.FleetIconIndex);
            Assert.AreEqual(22, clientFleet.FleetIconIndex);
            Assert.AreEqual(1, authorityFleet.DataNodes.Count);
            Assert.AreEqual(1, clientFleet.DataNodes.Count);
            Assert.AreEqual(designOnly.Name, authorityFleet.DataNodes[0].ShipName);
            Assert.AreEqual(new Vector2(18_000f, -9_000f), authorityFleet.DataNodes[0].RelativeFleetOffset);
            Assert.AreEqual(0.77f, authorityFleet.DataNodes[0].DPSWeight);
            Assert.AreEqual(CombatState.OrbitLeft, authorityFleet.DataNodes[0].CombatState);
            Assert.AreEqual(444_444f, authorityFleet.DataNodes[0].OrdersRadius);
            Assert.AreEqual(authorityFleet.DataNodes[0].ShipName, clientFleet.DataNodes[0].ShipName);
            Assert.AreEqual(authorityFleet.DataNodes[0].RelativeFleetOffset,
                clientFleet.DataNodes[0].RelativeFleetOffset);
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "Saved fleet load commands must move the canonical digest.");
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, "|Saved Wing|22|");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetIcon(138,
                authority.Player.Id, fleetKey: 3, iconIndex: 0));
            Assert.IsFalse(session.LastResult.Accepted, "Fleet icon zero must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "outside");
            Assert.AreEqual(22, authorityFleet.FleetIconIndex);
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetIcon(139,
                authority.Player.Id, fleetKey: 4, iconIndex: 22));
            Assert.IsFalse(session.LastResult.Accepted, "Missing fleets must not accept icon changes.");
            StringAssert.Contains(session.LastResult.Reason, "not found");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
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
    public void Authoritative4XPlanetOrbitalBuild_QueuesGoalAndSyncs_Headless()
    {
        const ulong Seed = 0x0B17A14UL;
        BuiltWorld authority = BuildWorld(Seed, includePlatform: true);
        BuiltWorld client = BuildWorld(Seed, includePlatform: true);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            IShipDesign design = PickBuildablePlanetOrbital(authority.Player);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.QueuePlanetOrbitalBuild(144,
                authority.Player.Id, authority.Planet.Id, design.Name));

            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(HasPlanetOrbitalGoal(authority.Player, authority.Planet, design.Name),
                "Authority must queue the real planet orbital construction goal.");
            Assert.IsTrue(HasPlanetOrbitalGoal(client.Player, client.Planet, design.Name),
                "Replica must receive the same accepted planet orbital construction goal.");
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "Planet orbital build goals must be covered by the authoritative sync digest.");
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, "|DeepSpace|");
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, design.Name);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.QueuePlanetOrbitalBuild(145,
                authority.Player.Id, authority.Planet.Id, "missing-orbital-design"));
            Assert.IsFalse(session.LastResult.Accepted, "Unknown orbital designs must be rejected.");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.QueuePlanetOrbitalBuild(146,
                authority.Enemy.Id, authority.Planet.Id, design.Name));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not build orbitals at another empire's planet.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
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
    public void Authoritative4XFleetRequisition_QueuesGoalsAndSyncs_Headless()
    {
        const ulong Seed = 0xF1EE4A15UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            EnableFleetRequisitionYard(authority);
            EnableFleetRequisitionYard(client);

            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            IShipDesign design = PickMobileBuildableShip(authority.Player);
            Fleet authorityFleet = CreateFleetRequisitionFixture(authority.Player, fleetKey: 4, design.Name);
            Fleet clientFleet = CreateFleetRequisitionFixture(client.Player, fleetKey: 4, design.Name);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueFleetRequisition(150,
                authority.Player.Id, fleetKey: 4, rush: true));

            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            FleetRequisition authorityGoal = FindFleetRequisitionGoal(authority.Player, authorityFleet, nodeIndex: 0);
            FleetRequisition clientGoal = FindFleetRequisitionGoal(client.Player, clientFleet, nodeIndex: 0);
            Assert.IsNotNull(authorityGoal, "Authority must create the real FleetRequisition goal.");
            Assert.IsNotNull(clientGoal, "Replica must create the same accepted FleetRequisition goal.");
            Assert.AreSame(authorityGoal, authorityFleet.DataNodes[0].Goal);
            Assert.AreSame(clientGoal, clientFleet.DataNodes[0].Goal);
            Assert.AreEqual(design.Name, authorityGoal.Build.Template.Name);
            Assert.IsTrue(authorityGoal.Build.Rush);
            Assert.IsTrue(HasQueuedFleetRequisitionShip(authority.Planet, authorityGoal, design.Name),
                "The real FleetRequisition goal should enqueue its ship at the selected spaceport.");
            Assert.IsTrue(HasQueuedFleetRequisitionShip(client.Planet, clientGoal, design.Name),
                "The client replica should enqueue the same requisition after host acceptance.");
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "Fleet requisition goals must be covered by the authoritative sync digest.");
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, "|FleetRequisition|");
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, design.Name);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.QueueFleetRequisition(151,
                authority.Player.Id, fleetKey: 4, rush: false, new[] { 8 }));
            Assert.IsFalse(session.LastResult.Accepted, "Missing fleet requisition nodes must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "was not found");
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
    public void Authoritative4XFleetRequisition_RejectsWrongEmpireAndMissingFleet_Headless()
    {
        const ulong Seed = 0xF1EE4A16UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            EnableFleetRequisitionYard(authority);
            EnableFleetRequisitionYard(client);

            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            IShipDesign design = PickMobileBuildableShip(authority.Player);
            CreateFleetRequisitionFixture(authority.Enemy, fleetKey: 4, design.Name);
            CreateFleetRequisitionFixture(client.Enemy, fleetKey: 4, design.Name);

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueFleetRequisition(152,
                authority.Player.Id, fleetKey: 4, rush: false));
            Assert.IsFalse(session.LastResult.Accepted,
                "A player command must not requisition against another empire's fleet slot.");
            StringAssert.Contains(session.LastResult.Reason, "not found");
            Assert.IsFalse(authority.Player.AI.Goals.Any(g => g is FleetRequisition));
            Assert.IsFalse(authority.Enemy.AI.Goals.Any(g => g is FleetRequisition));
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.QueueFleetRequisition(153,
                authority.Player.Id, fleetKey: 5, rush: false));
            Assert.IsFalse(session.LastResult.Accepted, "Missing fleets must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "not found");
            Assert.IsFalse(authority.Player.AI.Goals.Any(g => g is FleetRequisition));
            Assert.IsFalse(authority.Enemy.AI.Goals.Any(g => g is FleetRequisition));
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XFleetCreatePatrol_CreatesSavedAndActivePlanAndSyncs_Headless()
    {
        const ulong Seed = 0xF1EE4A12UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetAssignment(144,
                authority.Player.Id, fleetKey: 3, AuthoritativeFleetAssignmentMode.Replace,
                new[] { authority.Ship.Id, authority.WingShip.Id }));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);

            Fleet authorityFleet = authority.Player.GetFleetOrNull(3);
            Fleet clientFleet = client.Player.GetFleetOrNull(3);
            Assert.IsNotNull(authorityFleet);
            Assert.IsNotNull(clientFleet);
            Assert.IsFalse(authorityFleet.HasPatrolPlan);
            Assert.IsFalse(clientFleet.HasPatrolPlan);
            Assert.AreEqual(0, authority.Player.FleetPatrols.Count);
            Assert.AreEqual(0, client.Player.FleetPatrols.Count);

            AuthoritativeStateSnapshot before = AuthoritativeStateSnapshot.Capture(authority.Screen, 0);
            Assert.AreEqual(before.SyncDigest, AuthoritativeStateSnapshot.Capture(client.Screen, 0).SyncDigest);

            WayPoint[] points = TestPatrolWaypoints(authorityFleet.FinalPosition).ToArray();
            session.SubmitFromClient(AuthoritativePlayerCommand.CreateFleetPatrol(145,
                authority.Player.Id, fleetKey: 3, points));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(1, authority.Player.FleetPatrols.Count);
            Assert.AreEqual(1, client.Player.FleetPatrols.Count);
            Assert.IsTrue(authorityFleet.HasPatrolPlan);
            Assert.IsTrue(clientFleet.HasPatrolPlan);
            Assert.AreEqual(authorityFleet.Patrol.Name, clientFleet.Patrol.Name);
            Assert.AreEqual(points.Length, authorityFleet.Patrol.WayPoints.Count);
            Assert.AreEqual(points[0].Position, authorityFleet.Patrol.WayPoints.ElementAt(0).Position);
            Assert.AreEqual(points[1].Direction, authorityFleet.Patrol.WayPoints.ElementAt(1).Direction);
            Assert.AreEqual(AIState.FormationMoveTo, authority.Ship.AI.State,
                "Creating a patrol must run through the real Fleet.CreatePatrol/DoPatrol path.");
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload,
                $"FP|{authority.Player.Id}|{authorityFleet.Patrol.Name}");
            Assert.AreNotEqual(before.SyncDigest, session.LastAuthoritySnapshot.SyncDigest,
                "Creating a patrol must change the saved-plan/active-patrol sync digest.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.CreateFleetPatrol(146,
                authority.Player.Id, fleetKey: 3, points));
            Assert.IsFalse(session.LastResult.Accepted, "Creating a patrol over an active patrol must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "already has an active patrol");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.CreateFleetPatrol(147,
                authority.Enemy.Id, fleetKey: 3, points));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not create a patrol on another empire's fleet.");
            StringAssert.Contains(session.LastResult.Reason, "not found or empty");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.ClearFleetPatrol(148,
                authority.Player.Id, fleetKey: 3));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            string beforeInvalidWaypointDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(new AuthoritativePlayerCommand
            {
                Sequence = 149,
                EmpireId = authority.Player.Id,
                Kind = AuthoritativePlayerCommandKind.CreateFleetPatrol,
                SubjectId = 3,
                Text = AuthoritativePlayerCommand.EncodePatrolWaypoints(new[] { points[0] }),
            });
            Assert.IsFalse(session.LastResult.Accepted, "Patrols require at least two waypoints.");
            StringAssert.Contains(session.LastResult.Reason, "Invalid fleet patrol waypoint payload");
            Assert.AreEqual(beforeInvalidWaypointDigest, session.LastAuthoritySnapshot.SyncDigest);
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
    public void Authoritative4XFleetPatrolManagement_RenamesDeletesAndSyncs_Headless()
    {
        const ulong Seed = 0xF1EE4A14UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            session.SubmitFromClient(AuthoritativePlayerCommand.SetFleetAssignment(149,
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
            FleetPatrol authorityOtherPatrol = authority.Player.AddPatrolRoute(authorityFleet,
                TestPatrolWaypoints(authorityFleet.FinalPosition + new Vector2(3_000f, 0f)));
            FleetPatrol clientOtherPatrol = client.Player.AddPatrolRoute(clientFleet,
                TestPatrolWaypoints(clientFleet.FinalPosition + new Vector2(3_000f, 0f)));
            Assert.AreEqual(authorityPatrol.Name, clientPatrol.Name);
            Assert.AreEqual(authorityOtherPatrol.Name, clientOtherPatrol.Name);
            string originalName = authorityPatrol.Name;
            string otherName = authorityOtherPatrol.Name;

            string savedPlanDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;
            Assert.AreEqual(savedPlanDigest, AuthoritativeStateSnapshot.Capture(client.Screen, 0).SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.RenameFleetPatrol(150,
                authority.Player.Id, originalName, "Renamed Patrol"));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual("Renamed Patrol", authorityPatrol.Name);
            Assert.AreEqual("Renamed Patrol", clientPatrol.Name);
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, $"FP|{authority.Player.Id}|Renamed Patrol");
            Assert.AreNotEqual(savedPlanDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover saved patrol plan renames.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.RenameFleetPatrol(151,
                authority.Player.Id, "Renamed Patrol", otherName));
            Assert.IsFalse(session.LastResult.Accepted, "Duplicate patrol names must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "already exists");
            Assert.AreEqual("Renamed Patrol", authorityPatrol.Name);
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.RenameFleetPatrol(152,
                authority.Enemy.Id, "Renamed Patrol", "Enemy Rename"));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not rename another empire's patrol plan.");
            StringAssert.Contains(session.LastResult.Reason, "not found");
            Assert.AreEqual("Renamed Patrol", authorityPatrol.Name);
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.LoadFleetPatrol(153,
                authority.Player.Id, fleetKey: 3, "Renamed Patrol"));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authorityFleet.HasPatrolPlan);
            Assert.IsTrue(clientFleet.HasPatrolPlan);
            Assert.AreEqual("Renamed Patrol", authorityFleet.Patrol.Name);
            Assert.AreEqual("Renamed Patrol", clientFleet.Patrol.Name);

            string beforeClearDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.ClearFleetPatrol(154,
                authority.Player.Id, fleetKey: 3));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsFalse(authorityFleet.HasPatrolPlan,
                "Clearing an active patrol must remove only the fleet assignment on the authority.");
            Assert.IsFalse(clientFleet.HasPatrolPlan,
                "Clearing an active patrol must remove only the fleet assignment on replicas.");
            Assert.IsTrue(authority.Player.FleetPatrols.Any(p => p.Name == "Renamed Patrol"),
                "Clearing an active patrol must not delete the saved patrol plan.");
            Assert.IsTrue(client.Player.FleetPatrols.Any(p => p.Name == "Renamed Patrol"));
            Assert.AreNotEqual(beforeClearDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover active patrol clearing.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectedClearDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.ClearFleetPatrol(155,
                authority.Player.Id, fleetKey: 3));
            Assert.IsFalse(session.LastResult.Accepted, "Clearing a fleet without an active patrol must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "no active patrol");
            Assert.AreEqual(beforeRejectedClearDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.LoadFleetPatrol(156,
                authority.Player.Id, fleetKey: 3, "Renamed Patrol"));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);

            string beforeDeleteDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.DeleteFleetPatrol(157,
                authority.Player.Id, "Renamed Patrol"));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsFalse(authority.Player.FleetPatrols.Any(p => p.Name == "Renamed Patrol"));
            Assert.IsFalse(client.Player.FleetPatrols.Any(p => p.Name == "Renamed Patrol"));
            Assert.IsFalse(authorityFleet.HasPatrolPlan,
                "Deleting a saved patrol must clear any fleet currently assigned to it on the authority.");
            Assert.IsFalse(clientFleet.HasPatrolPlan,
                "Deleting a saved patrol must clear any fleet currently assigned to it on replicas.");
            Assert.AreNotEqual(beforeDeleteDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover saved patrol deletion and active fleet patrol clearing.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeMissingDeleteDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.DeleteFleetPatrol(158,
                authority.Player.Id, "Renamed Patrol"));
            Assert.IsFalse(session.LastResult.Accepted, "Missing patrol deletes must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "not found");
            Assert.AreEqual(beforeMissingDeleteDigest, session.LastAuthoritySnapshot.SyncDigest);
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

            string beforeClearDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.ShipSpecialOrder(133,
                authority.Player.Id, authority.Ship.Id, AuthoritativeShipSpecialOrderType.ClearOrders));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreNotEqual(AIState.Explore, authority.Ship.AI.State);
            Assert.AreNotEqual(AIState.Explore, client.Ship.AI.State);
            Assert.IsFalse(authority.Ship.AI.OrderQueue.ToArray().Any(g => g.Plan == ShipAI.Plan.Explore));
            Assert.IsFalse(client.Ship.AI.OrderQueue.ToArray().Any(g => g.Plan == ShipAI.Plan.Explore));
            Assert.AreNotEqual(beforeClearDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover order-clearing state.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeResupplyDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.ShipSpecialOrder(134,
                authority.Player.Id, authority.Ship.Id, AuthoritativeShipSpecialOrderType.Resupply));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.Ship.Resupplying,
                "The authority should apply Resupply through the real ship supply order path.");
            Assert.IsTrue(client.Ship.Resupplying,
                "The client replica should replay the accepted Resupply order deterministically.");
            Assert.AreNotEqual(beforeResupplyDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover ship resupply-order state.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(new AuthoritativePlayerCommand
            {
                Sequence = 135,
                EmpireId = authority.Player.Id,
                Kind = AuthoritativePlayerCommandKind.ShipSpecialOrder,
                SubjectId = authority.Ship.Id,
                TargetId = 255,
            });
            Assert.IsFalse(session.LastResult.Accepted, "Unknown special-order types must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "Unsupported");
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

            string beforeCancelScuttleDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.ShipLifecycleOrder(156,
                authority.Player.Id, authority.PlatformShip.Id, AuthoritativeShipLifecycleOrderType.CancelScuttle));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(-1f, authority.PlatformShip.ScuttleTimer);
            Assert.AreEqual(-1f, client.PlatformShip.ScuttleTimer);
            Assert.AreNotEqual(beforeCancelScuttleDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover scuttle cancellation.");
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
            world.Ship.VanityName = "Old Ship";
            world.Planet.Name = "Old Planet";
            string originalShipName = world.Ship.VanityName;
            string originalPlanetName = world.Planet.Name;

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

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitRenameShip(world.Ship, "New Ship"));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(2166, submitted[1].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.RenameShip, submitted[1].Kind);
                Assert.AreEqual(world.Ship.Id, submitted[1].SubjectId);
                Assert.AreEqual("New Ship", submitted[1].Text);
                Assert.AreEqual(originalShipName, world.Ship.VanityName,
                    "Passive MP clients must not locally rename ships before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitRenamePlanet(world.Planet, "New Planet"));
                Assert.AreEqual(3, submitted.Count);
                Assert.AreEqual(2167, submitted[2].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.RenamePlanet, submitted[2].Kind);
                Assert.AreEqual(world.Planet.Id, submitted[2].SubjectId);
                Assert.AreEqual("New Planet", submitted[2].Text);
                Assert.AreEqual(originalPlanetName, world.Planet.Name,
                    "Passive MP clients must not locally rename planets before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitRenameFleet(fleet, ""));
                Assert.AreEqual(3, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitRenameFleet(fleet, new string('A', 41)));
                Assert.AreEqual(3, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitRenameShip(world.Ship,
                        new string('A', AuthoritativePlayerCommand.MaxShipRenameLength + 1)));
                Assert.AreEqual(3, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitRenamePlanet(world.Planet, "Bad\nName"));
                Assert.AreEqual(3, submitted.Count);

                Fleet enemyFleet = world.Enemy.CreateFleet(4, null);
                enemyFleet.AddShips(new[] { world.EnemyShip });
                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitRenameFleet(enemyFleet, "Enemy Fleet"));
                Assert.AreEqual(3, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitRenameShip(world.EnemyShip, "Enemy Ship"));
                Assert.AreEqual(3, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitRenamePlanet(world.EnemyPlanet, "Enemy Planet"));
                Assert.AreEqual(3, submitted.Count);
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
    public void Authoritative4XClientContext_SubmitsPlanetOrbitalBuildWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0x0B17A15UL;
        BuiltWorld world = BuildWorld(Seed, includePlatform: true);

        try
        {
            IShipDesign design = PickBuildablePlanetOrbital(world.Player);
            int initialGoals = world.Player.AI.Goals.Count;
            int initialQueue = world.Planet.ConstructionQueue.Count;
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2162))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitQueuePlanetOrbitalBuild(world.Planet, design));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2162, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.QueuePlanetOrbitalBuild, submitted[0].Kind);
                Assert.AreEqual(world.Planet.Id, submitted[0].SubjectId);
                Assert.AreEqual(design.Name, submitted[0].Text);
                Assert.AreEqual(initialGoals, world.Player.AI.Goals.Count,
                    "Passive MP clients must not add planet orbital build goals before host acceptance.");
                Assert.AreEqual(initialQueue, world.Planet.ConstructionQueue.Count,
                    "Passive MP clients must not enqueue planet orbitals before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitQueuePlanetOrbitalBuild(world.EnemyPlanet, design));
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
    public void Authoritative4XClientContext_SubmitsFleetRequisitionWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xF1EE7C1DUL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            Assert.IsTrue(world.Player.CanBuildShip(world.Ship.Name),
                $"The passive requisition proof expects {world.Ship.Name} to be buildable.");
            Fleet fleet = CreateFleetRequisitionFixture(world.Player, fleetKey: 4, world.Ship.Name);
            int initialRequisitionGoals = world.Player.AI.Goals.Count(g => g is FleetRequisition);
            var proposed = new[]
            {
                new FleetDataNode(fleet.DataNodes[0])
                {
                    Ship = world.Ship,
                    Goal = fleet.DataNodes[0].Goal,
                },
            };

            var submitted = new List<AuthoritativePlayerCommand>();
            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2180))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitQueueFleetRequisition(fleet, rush: true));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2180, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.QueueFleetRequisition, submitted[0].Kind);
                Assert.AreEqual(4, submitted[0].SubjectId);
                Assert.AreEqual(1, submitted[0].TargetId);
                Assert.AreEqual("", submitted[0].Text,
                    "Empty requisition payload means all currently empty fleet nodes.");
                Assert.IsNull(fleet.DataNodes[0].Goal,
                    "Passive MP clients must not locally create FleetRequisition goals before host acceptance.");
                Assert.AreEqual(initialRequisitionGoals, world.Player.AI.Goals.Count(g => g is FleetRequisition));

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetFleetLayout(fleet, proposed));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetFleetLayout, submitted[1].Kind);
                Assert.IsNull(fleet.DataNodes[0].Ship,
                    "Passive MP clients must not locally assign ships to requisition nodes before host acceptance.");
                Assert.IsNull(world.Ship.Fleet,
                    "Passive MP clients must not locally mutate fleet membership before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitQueueFleetRequisition(fleet, rush: false,
                        new[] { 8 }));
                Assert.AreEqual(2, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsFleetDesignLoadWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xF1EE7C1EUL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            Fleet fleet = world.Player.CreateFleet(4, null);
            fleet.AddShips(new[] { world.Ship, world.WingShip });
            fleet.SetCommandShip(null);
            fleet.AutoArrange();
            fleet.Name = "Original Fleet";
            fleet.FleetIconIndex = 3;
            int originalNodeCount = fleet.DataNodes.Count;
            Vector2 originalOffset = fleet.DataNodes[0].RelativeFleetOffset;
            IShipDesign buildable = PickMobileBuildableShip(world.Player);
            var design = new FleetDesign
            {
                Name = "Saved Wing",
                FleetIconIndex = 21,
            };
            design.Nodes.Add(new FleetDataDesignNode
            {
                ShipName = buildable.Name,
                RelativeFleetOffset = originalOffset + new Vector2(3_000f, -4_000f),
                DPSWeight = 0.73f,
                CombatState = CombatState.BroadsideRight,
                OrdersRadius = 111_111f,
            });

            var submitted = new List<AuthoritativePlayerCommand>();
            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2180))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitLoadFleetDesign(fleet, design));
                Assert.AreEqual(3, submitted.Count);

                Assert.AreEqual(2180, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetFleetLayout, submitted[0].Kind);
                Assert.AreEqual(4, submitted[0].SubjectId);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParseFleetLayout(submitted[0].Text,
                    out AuthoritativeFleetLayoutNode[] layout));
                Assert.AreEqual(1, layout.Length);
                Assert.AreEqual(buildable.Name, layout[0].ShipName);
                Assert.AreEqual(originalOffset + new Vector2(3_000f, -4_000f), layout[0].Offset);
                Assert.AreEqual(0.73f, layout[0].DpsWeight);
                Assert.AreEqual(CombatState.BroadsideRight, layout[0].CombatState);
                Assert.AreEqual(111_111f, layout[0].OrdersRadius);

                Assert.AreEqual(2181, submitted[1].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.RenameFleet, submitted[1].Kind);
                Assert.AreEqual(4, submitted[1].SubjectId);
                Assert.AreEqual("Saved Wing", submitted[1].Text);

                Assert.AreEqual(2182, submitted[2].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetFleetIcon, submitted[2].Kind);
                Assert.AreEqual(4, submitted[2].SubjectId);
                Assert.AreEqual(21, submitted[2].TargetId);

                Assert.AreEqual("Original Fleet", fleet.Name,
                    "Passive MP clients must not locally rename fleets when loading designs before host acceptance.");
                Assert.AreEqual(3, fleet.FleetIconIndex,
                    "Passive MP clients must not locally change fleet icons when loading designs before host acceptance.");
                Assert.AreEqual(originalNodeCount, fleet.DataNodes.Count,
                    "Passive MP clients must not locally replace fleet layout when loading designs before host acceptance.");
                Assert.AreEqual(originalOffset, fleet.DataNodes[0].RelativeFleetOffset);
                Assert.AreSame(fleet, world.WingShip.Fleet,
                    "Passive MP clients must not locally remove current fleet ships before host acceptance.");

                var invalidIconDesign = new FleetDesign
                {
                    Name = "Invalid Icon",
                    FleetIconIndex = 0,
                };
                invalidIconDesign.Nodes.Add(new FleetDataDesignNode
                {
                    ShipName = buildable.Name,
                    RelativeFleetOffset = Vector2.Zero,
                });
                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitLoadFleetDesign(fleet, invalidIconDesign));
                Assert.AreEqual(3, submitted.Count);

                var missingShipDesign = new FleetDesign
                {
                    Name = "Missing Ship",
                    FleetIconIndex = 7,
                };
                missingShipDesign.Nodes.Add(new FleetDataDesignNode
                {
                    ShipName = "missing-saved-fleet-ship",
                    RelativeFleetOffset = Vector2.Zero,
                });
                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitLoadFleetDesign(fleet, missingShipDesign));
                Assert.AreEqual(3, submitted.Count);

                Fleet enemyFleet = world.Enemy.CreateFleet(4, null);
                enemyFleet.AddShips(new[] { world.EnemyShip });
                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitLoadFleetDesign(enemyFleet, design));
                Assert.AreEqual(3, submitted.Count);
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
    public void Authoritative4XClientContext_SubmitsFleetPatrolManagementWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xF1EE7C1DUL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            Fleet fleet = world.Player.CreateFleet(4, null);
            fleet.AddShips(new[] { world.Ship, world.WingShip });
            fleet.SetCommandShip(null);
            fleet.AutoArrange();
            fleet.Update(FixedSimTime.Zero);
            FleetPatrol patrol = world.Player.AddPatrolRoute(fleet, TestPatrolWaypoints(fleet.FinalPosition));
            FleetPatrol otherPatrol = world.Player.AddPatrolRoute(fleet,
                TestPatrolWaypoints(fleet.FinalPosition + new Vector2(2_500f, 0f)));
            string originalName = patrol.Name;
            int originalPatrolCount = world.Player.FleetPatrols.Count;

            var submitted = new List<AuthoritativePlayerCommand>();
            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2175))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitRenameFleetPatrol(world.Player, patrol, "Renamed Patrol"));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2175, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.RenameFleetPatrol, submitted[0].Kind);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParsePatrolRenamePayload(submitted[0].Text,
                    out string oldName, out string newName));
                Assert.AreEqual(originalName, oldName);
                Assert.AreEqual("Renamed Patrol", newName);
                Assert.AreEqual(originalName, patrol.Name,
                    "Passive MP clients must not locally rename saved patrol plans before host acceptance.");
                Assert.AreEqual(originalPatrolCount, world.Player.FleetPatrols.Count);

                WayPoint[] createPoints = TestPatrolWaypoints(fleet.FinalPosition + new Vector2(5_000f, 0f)).ToArray();
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitCreateFleetPatrol(fleet, createPoints));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(2176, submitted[1].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.CreateFleetPatrol, submitted[1].Kind);
                Assert.AreEqual(4, submitted[1].SubjectId);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParsePatrolWaypoints(submitted[1].Text,
                    out WayPoint[] submittedPoints));
                Assert.AreEqual(createPoints.Length, submittedPoints.Length);
                Assert.AreEqual(createPoints[0].Position, submittedPoints[0].Position);
                Assert.AreEqual(originalPatrolCount, world.Player.FleetPatrols.Count,
                    "Passive MP clients must not locally create saved patrol plans before host acceptance.");
                Assert.IsFalse(fleet.HasPatrolPlan,
                    "Passive MP clients must not locally activate a newly-created patrol before host acceptance.");

                fleet.LoadPatrol(patrol);
                Assert.IsTrue(fleet.HasPatrolPlan);
                Assert.AreSame(patrol, fleet.Patrol);
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitClearFleetPatrol(fleet));
                Assert.AreEqual(3, submitted.Count);
                Assert.AreEqual(2177, submitted[2].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ClearFleetPatrol, submitted[2].Kind);
                Assert.AreEqual(4, submitted[2].SubjectId);
                Assert.AreEqual(originalPatrolCount, world.Player.FleetPatrols.Count,
                    "Passive MP clients must not locally mutate saved patrol plans when clearing active patrols.");
                Assert.IsTrue(fleet.HasPatrolPlan,
                    "Passive MP clients must not locally clear the fleet patrol before host acceptance.");
                Assert.AreSame(patrol, fleet.Patrol);

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitDeleteFleetPatrol(world.Player, patrol));
                Assert.AreEqual(4, submitted.Count);
                Assert.AreEqual(2178, submitted[3].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.DeleteFleetPatrol, submitted[3].Kind);
                Assert.AreEqual(originalName, submitted[3].Text);
                Assert.AreEqual(originalPatrolCount, world.Player.FleetPatrols.Count,
                    "Passive MP clients must not locally delete saved patrol plans before host acceptance.");
                Assert.IsTrue(fleet.HasPatrolPlan,
                    "Passive MP clients must not locally clear fleets assigned to a deleted patrol before host acceptance.");
                Assert.AreSame(patrol, fleet.Patrol);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitRenameFleetPatrol(world.Player, patrol, otherPatrol.Name));
                Assert.AreEqual(4, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitRenameFleetPatrol(world.Enemy, patrol, "Enemy Rename"));
                Assert.AreEqual(4, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitDeleteFleetPatrol(world.Enemy, patrol));
                Assert.AreEqual(4, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XShipTradePolicy_SyncsAndRejectsInvalidRequests_Headless()
    {
        const ulong Seed = 0x71ADE001UL;
        BuiltWorld authority = BuildWorld(Seed, includeFreighter: true);
        BuiltWorld client = BuildWorld(Seed, includeFreighter: true);

        try
        {
            Assert.IsTrue(authority.FreighterShip.IsFreighter,
                "The trade-policy proof must operate on a real freighter.");
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;
            bool nextFoodPolicy = !authority.FreighterShip.TransportingFood;

            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipTradePolicy(160,
                authority.Player.Id, authority.FreighterShip.Id, AuthoritativeShipTradePolicyKind.Food,
                enabled: nextFoodPolicy));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(nextFoodPolicy, authority.FreighterShip.TransportingFood);
            Assert.AreEqual(nextFoodPolicy, client.FreighterShip.TransportingFood);
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover freighter trade-policy flags.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeInterTradeDigest = session.LastAuthoritySnapshot.SyncDigest;
            bool nextInterTradePolicy = !authority.FreighterShip.AllowInterEmpireTrade;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipTradePolicy(161,
                authority.Player.Id, authority.FreighterShip.Id,
                AuthoritativeShipTradePolicyKind.InterEmpire, enabled: nextInterTradePolicy));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(nextInterTradePolicy, authority.FreighterShip.AllowInterEmpireTrade);
            Assert.AreEqual(nextInterTradePolicy, client.FreighterShip.AllowInterEmpireTrade);
            Assert.AreNotEqual(beforeInterTradeDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipTradePolicy(162,
                authority.Player.Id, authority.Ship.Id, AuthoritativeShipTradePolicyKind.Production, enabled: false));
            Assert.IsFalse(session.LastResult.Accepted, "Non-freighter trade-policy changes must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "not a freighter");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipTradePolicy(163,
                authority.Enemy.Id, authority.FreighterShip.Id, AuthoritativeShipTradePolicyKind.Colonists, enabled: false));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not change another empire's freighter policy.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(new AuthoritativePlayerCommand
            {
                Sequence = 164,
                EmpireId = authority.Player.Id,
                Kind = AuthoritativePlayerCommandKind.SetShipTradePolicy,
                SubjectId = authority.FreighterShip.Id,
                TargetId = 255,
                Text = "1",
            });
            Assert.IsFalse(session.LastResult.Accepted, "Unknown trade-policy kinds must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "Unsupported");
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
    public void Authoritative4XShipTradeRouteAndAreaOfOperation_SyncsAndRejectsInvalidRequests_Headless()
    {
        const ulong Seed = 0x71ADE101UL;
        BuiltWorld authority = BuildWorld(Seed, includeFreighter: true);
        BuiltWorld client = BuildWorld(Seed, includeFreighter: true);

        try
        {
            Assert.IsTrue(authority.FreighterShip.IsFreighter,
                "The route/AO proof must operate on a real freighter.");
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipTradeRoute(172,
                authority.Player.Id, authority.FreighterShip.Id, authority.Planet.Id, enabled: true));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.FreighterShip.TradeRoutes.Contains(authority.Planet.Id));
            Assert.IsTrue(client.FreighterShip.TradeRoutes.Contains(client.Planet.Id));
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover freighter route lists.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRemoveRouteDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipTradeRoute(173,
                authority.Player.Id, authority.FreighterShip.Id, authority.Planet.Id, enabled: false));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsFalse(authority.FreighterShip.TradeRoutes.Contains(authority.Planet.Id));
            Assert.IsFalse(client.FreighterShip.TradeRoutes.Contains(client.Planet.Id));
            Assert.AreNotEqual(beforeRemoveRouteDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeAreaDigest = session.LastAuthoritySnapshot.SyncDigest;
            var area = new Rectangle(-12_000, -8_000, 6_000, 7_000);
            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipAreaOfOperation(174,
                authority.Player.Id, authority.FreighterShip.Id,
                AuthoritativeShipAreaOfOperationAction.AddRectangle, area));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(HasArea(authority.FreighterShip, area));
            Assert.IsTrue(HasArea(client.FreighterShip, area));
            Assert.AreNotEqual(beforeAreaDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover freighter AO rectangles.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRemoveAreaDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipAreaOfOperation(175,
                authority.Player.Id, authority.FreighterShip.Id,
                AuthoritativeShipAreaOfOperationAction.RemoveAtPoint,
                new Rectangle(-11_000, -7_000, 0, 0)));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsFalse(HasArea(authority.FreighterShip, area));
            Assert.IsFalse(HasArea(client.FreighterShip, area));
            Assert.AreNotEqual(beforeRemoveAreaDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipTradeRoute(176,
                authority.Player.Id, authority.Ship.Id, authority.Planet.Id, enabled: true));
            Assert.IsFalse(session.LastResult.Accepted, "Non-freighters must reject trade-route changes.");
            StringAssert.Contains(session.LastResult.Reason, "not a freighter");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipTradeRoute(177,
                authority.Player.Id, authority.FreighterShip.Id, planetId: 999_999, enabled: true));
            Assert.IsFalse(session.LastResult.Accepted, "Missing planets must reject trade-route changes.");
            StringAssert.Contains(session.LastResult.Reason, "not found");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipTradeRoute(178,
                authority.Enemy.Id, authority.FreighterShip.Id, authority.Planet.Id, enabled: true));
            Assert.IsFalse(session.LastResult.Accepted,
                "An empire must not change another empire's freighter route list.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipAreaOfOperation(179,
                authority.Player.Id, authority.FreighterShip.Id,
                AuthoritativeShipAreaOfOperationAction.AddRectangle,
                new Rectangle(0, 0, 4_999, 6_000)));
            Assert.IsFalse(session.LastResult.Accepted, "Small AO rectangles must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "5000x5000");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(new AuthoritativePlayerCommand
            {
                Sequence = 180,
                EmpireId = authority.Player.Id,
                Kind = AuthoritativePlayerCommandKind.SetShipAreaOfOperation,
                SubjectId = authority.FreighterShip.Id,
                TargetId = 255,
                Text = AuthoritativePlayerCommand.EncodeRectanglePayload(new Rectangle(0, 0, 6_000, 6_000)),
            });
            Assert.IsFalse(session.LastResult.Accepted, "Unknown AO actions must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "Unsupported");
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
    public void Authoritative4XShipRefit_SyncsAndRejectsInvalidRequests_Headless()
    {
        const ulong Seed = 0x4EF17001UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            IShipDesign refitDesign = PickRefitTarget(authority.Ship);
            EnableRefitYard(authority);
            EnableRefitYard(client);
            Assert.IsFalse(authority.Player.AI.HasGoal(g => g is RefitShip && g.OldShip == authority.Ship));
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.RefitShip(181,
                authority.Player.Id, authority.Ship.Id, refitDesign.Name,
                AuthoritativeShipRefitMode.One, rush: true));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            RefitShip authorityGoal = FindRefitGoal(authority.Player, authority.Ship);
            RefitShip clientGoal = FindRefitGoal(client.Player, client.Ship);
            Assert.IsNotNull(authorityGoal, "Accepted refit must add the real mobile refit goal.");
            Assert.IsNotNull(clientGoal, "Accepted refit must replay on the client replica.");
            Assert.AreEqual(refitDesign.Name, authorityGoal.ToBuild.Name);
            Assert.IsTrue(authorityGoal.Rush);
            Assert.AreEqual(AIState.Refit, authority.Ship.AI.State);
            Assert.AreEqual(AIState.Refit, client.Ship.AI.State);
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover early mobile refit goals.");
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, "|Refit|",
                "The canonical payload must expose mobile refit goals before queue items exist.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRejectDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.RefitShip(182,
                authority.Enemy.Id, authority.Ship.Id, refitDesign.Name,
                AuthoritativeShipRefitMode.One, rush: false));
            Assert.IsFalse(session.LastResult.Accepted,
                "An empire must not refit another empire's ship.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(beforeRejectDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.RefitShip(183,
                authority.Player.Id, authority.Ship.Id, authority.Ship.ShipData.Name,
                AuthoritativeShipRefitMode.One, rush: false));
            Assert.IsFalse(session.LastResult.Accepted, "Refitting to the same design must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "not a valid refit target");
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
    public void Authoritative4XShipRefit_AllAndFleetModesRespectScope_Headless()
    {
        const ulong AllSeed = 0x4EF17003UL;
        BuiltWorld allAuthority = BuildWorld(AllSeed);
        BuiltWorld allClient = BuildWorld(AllSeed);

        try
        {
            IShipDesign refitDesign = PickRefitTarget(allAuthority.Ship);
            EnableRefitYard(allAuthority);
            EnableRefitYard(allClient);
            var allSession = new Authoritative4XInProcessSession(allAuthority.Screen, allClient.Screen);

            allSession.SubmitFromClient(AuthoritativePlayerCommand.RefitShip(191,
                allAuthority.Player.Id, allAuthority.Ship.Id, refitDesign.Name,
                AuthoritativeShipRefitMode.All, rush: false));

            Assert.IsTrue(allSession.LastResult.Accepted, allSession.LastResult.Reason);
            Assert.IsNotNull(FindRefitGoal(allAuthority.Player, allAuthority.Ship));
            Assert.IsNotNull(FindRefitGoal(allClient.Player, allClient.Ship));
            Assert.AreEqual(allSession.LastAuthoritySnapshot.SyncDigest, allSession.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            allAuthority.Screen.Dispose();
            allClient.Screen.Dispose();
        }

        const ulong FleetSeed = 0x4EF17004UL;
        BuiltWorld fleetAuthority = BuildWorld(FleetSeed);
        BuiltWorld fleetClient = BuildWorld(FleetSeed);

        try
        {
            IShipDesign refitDesign = PickRefitTarget(fleetAuthority.Ship);
            EnableRefitYard(fleetAuthority);
            EnableRefitYard(fleetClient);
            Fleet authorityFleet = fleetAuthority.Player.CreateFleet(4, null);
            Fleet clientFleet = fleetClient.Player.CreateFleet(4, null);
            authorityFleet.AddShips(new[] { fleetAuthority.Ship });
            clientFleet.AddShips(new[] { fleetClient.Ship });
            authorityFleet.AutoArrange();
            clientFleet.AutoArrange();
            var fleetSession = new Authoritative4XInProcessSession(fleetAuthority.Screen, fleetClient.Screen);

            fleetSession.SubmitFromClient(AuthoritativePlayerCommand.RefitShip(192,
                fleetAuthority.Player.Id, fleetAuthority.Ship.Id, refitDesign.Name,
                AuthoritativeShipRefitMode.Fleet, rush: true));

            Assert.IsTrue(fleetSession.LastResult.Accepted, fleetSession.LastResult.Reason);
            Assert.IsNotNull(FindRefitGoal(fleetAuthority.Player, fleetAuthority.Ship));
            Assert.IsNull(FindRefitGoal(fleetAuthority.Player, fleetAuthority.WingShip),
                "Fleet-mode refit must not touch same-design ships outside the selected fleet.");
            Assert.IsNotNull(FindRefitGoal(fleetClient.Player, fleetClient.Ship));
            Assert.IsNull(FindRefitGoal(fleetClient.Player, fleetClient.WingShip));
            Assert.AreEqual(fleetSession.LastAuthoritySnapshot.SyncDigest, fleetSession.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            fleetAuthority.Screen.Dispose();
            fleetClient.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XShipCarrierPolicy_SyncsAndRejectsInvalidRequests_Headless()
    {
        const ulong Seed = 0xCA441001UL;
        BuiltWorld authority = BuildWorld(Seed, includeCarrierPolicyShips: true);
        BuiltWorld client = BuildWorld(Seed, includeCarrierPolicyShips: true);

        try
        {
            Assert.IsTrue(authority.CarrierShip.Carrier.HasFighterBays,
                "The carrier-policy proof must operate on a real fighter carrier.");
            Assert.IsTrue(authority.TroopCarrierShip.Carrier.HasTroopBays,
                "The carrier-policy proof must operate on a real troop/assault carrier.");

            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            bool originalCarrierFightersOut = authority.CarrierShip.Carrier.FightersOut;

            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipCarrierPolicy(169,
                authority.Player.Id, authority.Ship.Id,
                AuthoritativeShipCarrierPolicyKind.FightersOut, enabled: true));
            Assert.IsFalse(session.LastResult.Accepted, "Non-carriers must reject fighter launch policy changes.");
            StringAssert.Contains(session.LastResult.Reason, "cannot receive carrier policy");
            Assert.AreEqual(originalCarrierFightersOut, authority.CarrierShip.Carrier.FightersOut);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipCarrierPolicy(170,
                authority.Enemy.Id, authority.CarrierShip.Id,
                AuthoritativeShipCarrierPolicyKind.FightersOut, enabled: false));
            Assert.IsFalse(session.LastResult.Accepted, "An empire must not change another empire's carrier policy.");
            StringAssert.Contains(session.LastResult.Reason, "not owned");
            Assert.AreEqual(originalCarrierFightersOut, authority.CarrierShip.Carrier.FightersOut);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(new AuthoritativePlayerCommand
            {
                Sequence = 171,
                EmpireId = authority.Player.Id,
                Kind = AuthoritativePlayerCommandKind.SetShipCarrierPolicy,
                SubjectId = authority.CarrierShip.Id,
                TargetId = 255,
                Text = "1",
            });
            Assert.IsFalse(session.LastResult.Accepted, "Unknown carrier-policy kinds must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "Unsupported");
            Assert.AreEqual(originalCarrierFightersOut, authority.CarrierShip.Carrier.FightersOut);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeFighterDigest = session.LastAuthoritySnapshot.SyncDigest;
            bool nextFightersOut = !authority.CarrierShip.Carrier.FightersOut;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipCarrierPolicy(165,
                authority.Player.Id, authority.CarrierShip.Id,
                AuthoritativeShipCarrierPolicyKind.FightersOut, enabled: nextFightersOut));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(nextFightersOut, authority.CarrierShip.Carrier.FightersOut);
            Assert.AreEqual(nextFightersOut, client.CarrierShip.Carrier.FightersOut);
            Assert.AreNotEqual(beforeFighterDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover fighter-carrier launch flags.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeTroopsDigest = session.LastAuthoritySnapshot.SyncDigest;
            bool nextTroopsOut = !authority.TroopCarrierShip.Carrier.TroopsOut;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipCarrierPolicy(166,
                authority.Player.Id, authority.TroopCarrierShip.Id,
                AuthoritativeShipCarrierPolicyKind.TroopsOut, enabled: nextTroopsOut));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(nextTroopsOut, authority.TroopCarrierShip.Carrier.TroopsOut);
            Assert.AreEqual(nextTroopsOut, client.TroopCarrierShip.Carrier.TroopsOut);
            Assert.AreNotEqual(beforeTroopsDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover troop-carrier launch flags.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeRecallDigest = session.LastAuthoritySnapshot.SyncDigest;
            bool nextRecall = !authority.CarrierShip.Carrier.RecallFightersBeforeFTL;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipCarrierPolicy(167,
                authority.Player.Id, authority.CarrierShip.Id,
                AuthoritativeShipCarrierPolicyKind.RecallFightersBeforeFTL, enabled: nextRecall));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(nextRecall, authority.CarrierShip.Carrier.RecallFightersBeforeFTL);
            Assert.AreEqual(!nextRecall, authority.CarrierShip.ManualHangarOverride);
            Assert.AreEqual(nextRecall, client.CarrierShip.Carrier.RecallFightersBeforeFTL);
            Assert.AreEqual(!nextRecall, client.CarrierShip.ManualHangarOverride);
            Assert.AreNotEqual(beforeRecallDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover recall/manual-hangar flags.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string beforeBoardDigest = session.LastAuthoritySnapshot.SyncDigest;
            bool nextBoard = !authority.TroopCarrierShip.Carrier.AllowBoardShip;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetShipCarrierPolicy(168,
                authority.Player.Id, authority.TroopCarrierShip.Id,
                AuthoritativeShipCarrierPolicyKind.AllowBoardShip, enabled: nextBoard));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.AreEqual(nextBoard, authority.TroopCarrierShip.Carrier.AllowBoardShip);
            Assert.AreEqual(nextBoard, client.TroopCarrierShip.Carrier.AllowBoardShip);
            Assert.AreNotEqual(beforeBoardDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The authoritative sync digest must cover ship-board policy flags.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
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

                world.Ship.AI.OrderExplore();
                AIState exploringState = world.Ship.AI.State;
                int exploringOrders = world.Ship.AI.OrderQueue.Count;
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitShipSpecialOrder(world.Ship,
                        AuthoritativeShipSpecialOrderType.ClearOrders));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(2171, submitted[1].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ShipSpecialOrder, submitted[1].Kind);
                Assert.AreEqual(world.Ship.Id, submitted[1].SubjectId);
                Assert.AreEqual((int)AuthoritativeShipSpecialOrderType.ClearOrders, submitted[1].TargetId);
                Assert.AreEqual(exploringState, world.Ship.AI.State,
                    "Passive MP clients must not locally clear ship orders before host acceptance.");
                Assert.AreEqual(exploringOrders, world.Ship.AI.OrderQueue.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitShipSpecialOrder(world.Ship,
                        AuthoritativeShipSpecialOrderType.Resupply));
                Assert.AreEqual(3, submitted.Count);
                Assert.AreEqual(2172, submitted[2].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ShipSpecialOrder, submitted[2].Kind);
                Assert.AreEqual(world.Ship.Id, submitted[2].SubjectId);
                Assert.AreEqual((int)AuthoritativeShipSpecialOrderType.Resupply, submitted[2].TargetId);
                Assert.AreEqual(exploringState, world.Ship.AI.State,
                    "Passive MP clients must not locally enqueue resupply before host acceptance.");
                Assert.AreEqual(exploringOrders, world.Ship.AI.OrderQueue.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipSpecialOrder(world.EnemyShip,
                        AuthoritativeShipSpecialOrderType.Explore));
                Assert.AreEqual(3, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipSpecialOrder(world.Ship,
                        (AuthoritativeShipSpecialOrderType)255));
                Assert.AreEqual(3, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void ShipInfoExploreButton_SubmitsAuthoritativeCommandWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xE7010DUL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            var submitted = new List<AuthoritativePlayerCommand>();
            var method = typeof(ShipInfoUIElement).GetMethod("SubmitOrApplyExplore",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(method,
                "The ship info Explore button should route through a small authoritative helper.");
            AIState originalState = world.Ship.AI.State;
            int originalOrders = world.Ship.AI.OrderQueue.Count;

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2180))
            {
                method.Invoke(null, new object[] { world.Ship, true });
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ShipSpecialOrder, submitted[0].Kind);
                Assert.AreEqual(world.Ship.Id, submitted[0].SubjectId);
                Assert.AreEqual((int)AuthoritativeShipSpecialOrderType.Explore, submitted[0].TargetId);
                Assert.AreEqual(originalState, world.Ship.AI.State,
                    "The ship info Explore button must not locally start exploration before host acceptance.");
                Assert.AreEqual(originalOrders, world.Ship.AI.OrderQueue.Count);

                world.Ship.AI.OrderExplore();
                AIState exploringState = world.Ship.AI.State;
                int exploringOrders = world.Ship.AI.OrderQueue.Count;
                method.Invoke(null, new object[] { world.Ship, false });
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ShipSpecialOrder, submitted[1].Kind);
                Assert.AreEqual(world.Ship.Id, submitted[1].SubjectId);
                Assert.AreEqual((int)AuthoritativeShipSpecialOrderType.ClearOrders, submitted[1].TargetId);
                Assert.AreEqual(exploringState, world.Ship.AI.State,
                    "The ship info Explore off-click must not locally clear orders before host acceptance.");
                Assert.AreEqual(exploringOrders, world.Ship.AI.OrderQueue.Count);

                AIState enemyOriginalState = world.EnemyShip.AI.State;
                method.Invoke(null, new object[] { world.EnemyShip, true });
                Assert.AreEqual(2, submitted.Count,
                    "A passive client must not submit or locally apply ship-info orders for another empire.");
                Assert.AreEqual(enemyOriginalState, world.EnemyShip.AI.State);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsShipTradePolicyWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0x71ADE002UL;
        BuiltWorld world = BuildWorld(Seed, includeFreighter: true);

        try
        {
            Assert.IsTrue(world.FreighterShip.IsFreighter,
                "The passive trade-policy proof must operate on a real freighter.");
            bool originalFood = world.FreighterShip.TransportingFood;
            bool originalProduction = world.FreighterShip.TransportingProduction;
            bool originalColonists = world.FreighterShip.TransportingColonists;
            bool originalInterTrade = world.FreighterShip.AllowInterEmpireTrade;
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2270))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetShipTradePolicy(world.FreighterShip,
                        AuthoritativeShipTradePolicyKind.Food, enabled: !originalFood));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2270, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetShipTradePolicy, submitted[0].Kind);
                Assert.AreEqual(world.FreighterShip.Id, submitted[0].SubjectId);
                Assert.AreEqual((int)AuthoritativeShipTradePolicyKind.Food, submitted[0].TargetId);
                Assert.AreEqual(!originalFood ? "1" : "0", submitted[0].Text);
                Assert.AreEqual(originalFood, world.FreighterShip.TransportingFood,
                    "Passive MP clients must not locally change freighter food policy before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetShipTradePolicy(world.FreighterShip,
                        AuthoritativeShipTradePolicyKind.InterEmpire, enabled: !originalInterTrade));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(2271, submitted[1].Sequence);
                Assert.AreEqual((int)AuthoritativeShipTradePolicyKind.InterEmpire, submitted[1].TargetId);
                Assert.AreEqual(originalInterTrade, world.FreighterShip.AllowInterEmpireTrade,
                    "Passive MP clients must not locally change inter-empire trade policy before host acceptance.");

                Assert.AreEqual(originalProduction, world.FreighterShip.TransportingProduction);
                Assert.AreEqual(originalColonists, world.FreighterShip.TransportingColonists);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetShipTradePolicy(world.Ship,
                        AuthoritativeShipTradePolicyKind.Production, enabled: false));
                Assert.AreEqual(2, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetShipTradePolicy(world.FreighterShip,
                        (AuthoritativeShipTradePolicyKind)255, enabled: true));
                Assert.AreEqual(2, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsShipTradeRouteAndAreaOfOperationWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0x71ADE102UL;
        BuiltWorld world = BuildWorld(Seed, includeFreighter: true);

        try
        {
            Assert.IsTrue(world.FreighterShip.IsFreighter,
                "The passive route/AO proof must operate on a real freighter.");
            Assert.IsFalse(world.FreighterShip.TradeRoutes.Contains(world.Planet.Id));
            int originalRouteCount = world.FreighterShip.TradeRoutes.Count;
            int originalAreaCount = world.FreighterShip.AreaOfOperation.Count;
            var area = new Rectangle(10_000, 20_000, 6_000, 7_000);
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2290))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetShipTradeRoute(world.FreighterShip,
                        world.Planet, enabled: true));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2290, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetShipTradeRoute, submitted[0].Kind);
                Assert.AreEqual(world.FreighterShip.Id, submitted[0].SubjectId);
                Assert.AreEqual(world.Planet.Id, submitted[0].TargetId);
                Assert.AreEqual("1", submitted[0].Text);
                Assert.AreEqual(originalRouteCount, world.FreighterShip.TradeRoutes.Count,
                    "Passive MP clients must not locally add freighter routes before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetShipTradeRoute(world.FreighterShip,
                        world.Planet, enabled: false));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(2291, submitted[1].Sequence);
                Assert.AreEqual("0", submitted[1].Text);
                Assert.AreEqual(originalRouteCount, world.FreighterShip.TradeRoutes.Count,
                    "Passive MP clients must not locally remove freighter routes before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetShipAreaOfOperation(world.FreighterShip,
                        AuthoritativeShipAreaOfOperationAction.AddRectangle, area));
                Assert.AreEqual(3, submitted.Count);
                Assert.AreEqual(2292, submitted[2].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetShipAreaOfOperation, submitted[2].Kind);
                Assert.AreEqual(world.FreighterShip.Id, submitted[2].SubjectId);
                Assert.AreEqual((int)AuthoritativeShipAreaOfOperationAction.AddRectangle, submitted[2].TargetId);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParseRectanglePayload(submitted[2].Text,
                    out Rectangle submittedArea));
                Assert.AreEqual(area.Width, submittedArea.Width);
                Assert.AreEqual(originalAreaCount, world.FreighterShip.AreaOfOperation.Count,
                    "Passive MP clients must not locally add AO rectangles before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetShipAreaOfOperation(world.FreighterShip,
                        AuthoritativeShipAreaOfOperationAction.RemoveAtPoint,
                        new Rectangle(11_000, 21_000, 0, 0)));
                Assert.AreEqual(4, submitted.Count);
                Assert.AreEqual(2293, submitted[3].Sequence);
                Assert.AreEqual((int)AuthoritativeShipAreaOfOperationAction.RemoveAtPoint, submitted[3].TargetId);
                Assert.AreEqual(originalAreaCount, world.FreighterShip.AreaOfOperation.Count,
                    "Passive MP clients must not locally remove AO rectangles before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetShipTradeRoute(world.Ship, world.Planet,
                        enabled: true));
                Assert.AreEqual(4, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetShipAreaOfOperation(world.FreighterShip,
                        AuthoritativeShipAreaOfOperationAction.AddRectangle,
                        new Rectangle(0, 0, 4_000, 6_000)));
                Assert.AreEqual(4, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsShipRefitWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0x4EF17002UL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            IShipDesign refitDesign = PickRefitTarget(world.Ship);
            AIState originalState = world.Ship.AI.State;
            int originalOrders = world.Ship.AI.OrderQueue.Count;
            int originalGoals = world.Player.AI.Goals.Count;
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2300))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitShipRefit(world.Ship, refitDesign,
                        AuthoritativeShipRefitMode.One, rush: true));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2300, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.RefitShip, submitted[0].Kind);
                Assert.AreEqual(world.Ship.Id, submitted[0].SubjectId);
                Assert.AreEqual((int)AuthoritativeShipRefitMode.One, submitted[0].TargetId);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParseShipRefitPayload(submitted[0].Text,
                    out string submittedDesign, out bool submittedRush));
                Assert.AreEqual(refitDesign.Name, submittedDesign);
                Assert.IsTrue(submittedRush);
                Assert.AreEqual(originalState, world.Ship.AI.State,
                    "Passive MP clients must not locally start refit before host acceptance.");
                Assert.AreEqual(originalOrders, world.Ship.AI.OrderQueue.Count);
                Assert.AreEqual(originalGoals, world.Player.AI.Goals.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipRefit(world.EnemyShip, refitDesign,
                        AuthoritativeShipRefitMode.One, rush: false));
                Assert.AreEqual(1, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipRefit(world.Ship, world.Ship.ShipData,
                        AuthoritativeShipRefitMode.One, rush: false));
                Assert.AreEqual(1, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsShipCarrierPolicyWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xCA441002UL;
        BuiltWorld world = BuildWorld(Seed, includeCarrierPolicyShips: true);

        try
        {
            Assert.IsTrue(world.CarrierShip.Carrier.HasFighterBays,
                "The passive carrier-policy proof must operate on a real fighter carrier.");
            Assert.IsTrue(world.TroopCarrierShip.Carrier.HasTroopBays,
                "The passive carrier-policy proof must operate on a real troop/assault carrier.");
            bool originalFightersOut = world.CarrierShip.Carrier.FightersOut;
            bool originalRecall = world.CarrierShip.Carrier.RecallFightersBeforeFTL;
            bool originalManualOverride = world.CarrierShip.ManualHangarOverride;
            bool originalTroopsOut = world.TroopCarrierShip.Carrier.TroopsOut;
            bool originalSendTroops = world.TroopCarrierShip.Carrier.SendTroopsToShip;
            bool originalAllowBoard = world.TroopCarrierShip.Carrier.AllowBoardShip;
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2280))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetShipCarrierPolicy(world.CarrierShip,
                        AuthoritativeShipCarrierPolicyKind.FightersOut, enabled: !originalFightersOut));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2280, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetShipCarrierPolicy, submitted[0].Kind);
                Assert.AreEqual(world.CarrierShip.Id, submitted[0].SubjectId);
                Assert.AreEqual((int)AuthoritativeShipCarrierPolicyKind.FightersOut, submitted[0].TargetId);
                Assert.AreEqual(!originalFightersOut ? "1" : "0", submitted[0].Text);
                Assert.AreEqual(originalFightersOut, world.CarrierShip.Carrier.FightersOut,
                    "Passive MP clients must not locally launch/recover fighters before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetShipCarrierPolicy(world.CarrierShip,
                        AuthoritativeShipCarrierPolicyKind.RecallFightersBeforeFTL, enabled: !originalRecall));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(2281, submitted[1].Sequence);
                Assert.AreEqual((int)AuthoritativeShipCarrierPolicyKind.RecallFightersBeforeFTL, submitted[1].TargetId);
                Assert.AreEqual(originalRecall, world.CarrierShip.Carrier.RecallFightersBeforeFTL,
                    "Passive MP clients must not locally change recall policy before host acceptance.");
                Assert.AreEqual(originalManualOverride, world.CarrierShip.ManualHangarOverride);

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetShipCarrierPolicy(world.TroopCarrierShip,
                        AuthoritativeShipCarrierPolicyKind.TroopsOut, enabled: !originalTroopsOut));
                Assert.AreEqual(3, submitted.Count);
                Assert.AreEqual(2282, submitted[2].Sequence);
                Assert.AreEqual((int)AuthoritativeShipCarrierPolicyKind.TroopsOut, submitted[2].TargetId);
                Assert.AreEqual(originalTroopsOut, world.TroopCarrierShip.Carrier.TroopsOut,
                    "Passive MP clients must not locally launch/recover assault shuttles before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetShipCarrierPolicy(world.TroopCarrierShip,
                        AuthoritativeShipCarrierPolicyKind.SendTroopsToShip, enabled: !originalSendTroops));
                Assert.AreEqual(4, submitted.Count);
                Assert.AreEqual(2283, submitted[3].Sequence);
                Assert.AreEqual((int)AuthoritativeShipCarrierPolicyKind.SendTroopsToShip, submitted[3].TargetId);
                Assert.AreEqual(originalSendTroops, world.TroopCarrierShip.Carrier.SendTroopsToShip);

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitSetShipCarrierPolicy(world.TroopCarrierShip,
                        AuthoritativeShipCarrierPolicyKind.AllowBoardShip, enabled: !originalAllowBoard));
                Assert.AreEqual(5, submitted.Count);
                Assert.AreEqual(2284, submitted[4].Sequence);
                Assert.AreEqual((int)AuthoritativeShipCarrierPolicyKind.AllowBoardShip, submitted[4].TargetId);
                Assert.AreEqual(originalAllowBoard, world.TroopCarrierShip.Carrier.AllowBoardShip);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetShipCarrierPolicy(world.Ship,
                        AuthoritativeShipCarrierPolicyKind.FightersOut, enabled: true));
                Assert.AreEqual(5, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitSetShipCarrierPolicy(world.CarrierShip,
                        (AuthoritativeShipCarrierPolicyKind)255, enabled: true));
                Assert.AreEqual(5, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void ShipCarrierPolicyUiHelpers_SubmitAuthoritativeCommandWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xCA441003UL;
        BuiltWorld world = BuildWorld(Seed, includeCarrierPolicyShips: true);

        try
        {
            var ordersMethod = typeof(OrdersButton).GetMethod("SetCarrierPolicy",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var shipInfoMethod = typeof(ShipInfoUIElement).GetMethod("SetCarrierPolicy",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(ordersMethod,
                "The multi-select OrdersButton carrier buttons should route through a small authoritative helper.");
            Assert.IsNotNull(shipInfoMethod,
                "The single-ship info carrier buttons should route through a small authoritative helper.");

            bool originalFightersOut = world.CarrierShip.Carrier.FightersOut;
            bool originalAllowBoard = world.TroopCarrierShip.Carrier.AllowBoardShip;
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2290))
            {
                ordersMethod.Invoke(null, new object[]
                {
                    world.CarrierShip,
                    AuthoritativeShipCarrierPolicyKind.FightersOut,
                    !originalFightersOut,
                    new Action<bool>(v => world.CarrierShip.Carrier.FightersOut = v)
                });
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(2290, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetShipCarrierPolicy, submitted[0].Kind);
                Assert.AreEqual(world.CarrierShip.Id, submitted[0].SubjectId);
                Assert.AreEqual((int)AuthoritativeShipCarrierPolicyKind.FightersOut, submitted[0].TargetId);
                Assert.AreEqual(originalFightersOut, world.CarrierShip.Carrier.FightersOut,
                    "The multi-select carrier button must not locally launch/recover fighters before host acceptance.");

                shipInfoMethod.Invoke(null, new object[]
                {
                    world.TroopCarrierShip,
                    AuthoritativeShipCarrierPolicyKind.AllowBoardShip,
                    !originalAllowBoard,
                    new Action<bool>(v => world.TroopCarrierShip.Carrier.AllowBoardShip = v)
                });
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(2291, submitted[1].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetShipCarrierPolicy, submitted[1].Kind);
                Assert.AreEqual(world.TroopCarrierShip.Id, submitted[1].SubjectId);
                Assert.AreEqual((int)AuthoritativeShipCarrierPolicyKind.AllowBoardShip, submitted[1].TargetId);
                Assert.AreEqual(originalAllowBoard, world.TroopCarrierShip.Carrier.AllowBoardShip,
                    "The single-ship carrier button must not locally change boarding policy before host acceptance.");
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

                world.PlatformShip.ScuttleTimer = 10f;
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitShipLifecycleOrder(world.PlatformShip,
                        AuthoritativeShipLifecycleOrderType.CancelScuttle));
                Assert.AreEqual(3, submitted.Count);
                Assert.AreEqual(2192, submitted[2].Sequence);
                Assert.AreEqual(world.PlatformShip.Id, submitted[2].SubjectId);
                Assert.AreEqual((int)AuthoritativeShipLifecycleOrderType.CancelScuttle, submitted[2].TargetId);
                Assert.AreEqual(10f, world.PlatformShip.ScuttleTimer,
                    "Passive MP clients must not locally cancel scuttle timers before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipLifecycleOrder(world.EnemyShip,
                        AuthoritativeShipLifecycleOrderType.Scrap));
                Assert.AreEqual(3, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipLifecycleOrder(world.Ship,
                        (AuthoritativeShipLifecycleOrderType)255));
                Assert.AreEqual(3, submitted.Count);
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
    public void Authoritative4XGroundTroopOrders_LaunchMoveAndSync_Headless()
    {
        const ulong Seed = 0x6700D101UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            PlanetGridSquare[] authorityTiles = PrepareGroundTroopTiles(authority.Planet, columns: 2, rows: 1);
            PlanetGridSquare[] clientTiles = PrepareGroundTroopTiles(client.Planet, columns: 2, rows: 1);
            Troop authorityMover = PlaceGroundTroop(authority.Planet, authority.Player, authorityTiles[0]);
            Troop clientMover = PlaceGroundTroop(client.Planet, client.Player, clientTiles[0]);
            Troop authorityLauncher = PlaceGroundTroop(authority.Planet, authority.Player, authorityTiles[1]);
            Troop clientLauncher = PlaceGroundTroop(client.Planet, client.Player, clientTiles[1]);
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.GroundTroopOrder(900, authority.Player.Id,
                authority.Planet.Id, AuthoritativeGroundTroopOrderType.Move, authorityTiles[0].X,
                authorityTiles[0].Y, troopIndex: 0, targetTileX: authorityTiles[1].X,
                targetTileY: authorityTiles[1].Y, expectedTroopName: authorityMover.Name));
            Assert.IsFalse(session.LastResult.Accepted,
                "Moving onto a tile that already contains a friendly troop must be rejected.");
            StringAssert.Contains(session.LastResult.Reason, "not free");
            Assert.IsTrue(authorityTiles[0].TroopsHere.ContainsRef(authorityMover));
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.GroundTroopOrder(901, authority.Player.Id,
                authority.Planet.Id, AuthoritativeGroundTroopOrderType.LaunchOne, authorityTiles[1].X,
                authorityTiles[1].Y, troopIndex: 0, expectedTroopName: authorityLauncher.Name));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsFalse(authorityTiles[1].TroopsHere.ContainsRef(authorityLauncher));
            Assert.IsFalse(clientTiles[1].TroopsHere.ContainsRef(clientLauncher));
            Assert.IsTrue(authority.UState.Ships.Any(s => s.Loyalty == authority.Player && s.GetOurTroops().Count > 0),
                "Launching one ground troop should create a troop ship on the authority.");
            Assert.IsTrue(client.UState.Ships.Any(s => s.Loyalty == client.Player && s.GetOurTroops().Count > 0),
                "The client replica should launch the same troop ship after host acceptance.");
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The canonical sync digest must cover launched ship-carried troops.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, "ST|");

            session.SubmitFromClient(AuthoritativePlayerCommand.GroundTroopOrder(902, authority.Player.Id,
                authority.Planet.Id, AuthoritativeGroundTroopOrderType.Move, authorityTiles[0].X,
                authorityTiles[0].Y, troopIndex: 0, targetTileX: authorityTiles[1].X,
                targetTileY: authorityTiles[1].Y, expectedTroopName: authorityMover.Name));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsFalse(authorityTiles[0].TroopsHere.ContainsRef(authorityMover));
            Assert.IsTrue(authorityTiles[1].TroopsHere.ContainsRef(authorityMover));
            Assert.IsFalse(clientTiles[0].TroopsHere.ContainsRef(clientMover));
            Assert.IsTrue(clientTiles[1].TroopsHere.ContainsRef(clientMover));
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, "GT|");
        }
        finally
        {
            authority.Screen.Dispose();
            client.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XGroundTroopOrders_AttacksAndSync_Headless()
    {
        const ulong Seed = 0x6700D102UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            MakeAtWar(authority.Player, authority.Enemy);
            MakeAtWar(client.Player, client.Enemy);

            PlanetGridSquare[] authorityHomeTiles = PrepareGroundTroopTiles(authority.Planet, columns: 2, rows: 2);
            PlanetGridSquare[] clientHomeTiles = PrepareGroundTroopTiles(client.Planet, columns: 2, rows: 2);
            PlanetGridSquare[] authorityEnemyTiles = PrepareGroundTroopTiles(authority.EnemyPlanet, columns: 2, rows: 1);
            PlanetGridSquare[] clientEnemyTiles = PrepareGroundTroopTiles(client.EnemyPlanet, columns: 2, rows: 1);

            Troop authorityAttacker = PlaceGroundTroop(authority.Planet, authority.Player, authorityHomeTiles[0]);
            Troop clientAttacker = PlaceGroundTroop(client.Planet, client.Player, clientHomeTiles[0]);
            PlaceGroundTroop(authority.Planet, authority.Enemy, authorityHomeTiles[1]);
            PlaceGroundTroop(client.Planet, client.Enemy, clientHomeTiles[1]);
            PlaceCombatBuilding(authority.Planet, authorityHomeTiles[2]);
            PlaceCombatBuilding(client.Planet, clientHomeTiles[2]);
            PlaceGroundTroop(authority.Planet, authority.Enemy, authorityHomeTiles[3]);
            PlaceGroundTroop(client.Planet, client.Enemy, clientHomeTiles[3]);

            Troop authorityInvader = PlaceGroundTroop(authority.EnemyPlanet, authority.Player, authorityEnemyTiles[0]);
            Troop clientInvader = PlaceGroundTroop(client.EnemyPlanet, client.Player, clientEnemyTiles[0]);
            PlaceCombatBuilding(authority.EnemyPlanet, authorityEnemyTiles[1]);
            PlaceCombatBuilding(client.EnemyPlanet, clientEnemyTiles[1]);

            string beforeAttackDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;
            int beforeHomeCombats = authority.Planet.ActiveCombats.Count;
            session.SubmitFromClient(AuthoritativePlayerCommand.GroundTroopOrder(910, authority.Player.Id,
                authority.Planet.Id, AuthoritativeGroundTroopOrderType.AttackTroop, authorityHomeTiles[0].X,
                authorityHomeTiles[0].Y, troopIndex: 0, targetTileX: authorityHomeTiles[1].X,
                targetTileY: authorityHomeTiles[1].Y, expectedTroopName: authorityAttacker.Name));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.Planet.ActiveCombats.Count > beforeHomeCombats,
                "The accepted troop attack should start ground combat on the authority.");
            Assert.AreEqual(authority.Planet.ActiveCombats.Count, client.Planet.ActiveCombats.Count);
            Assert.AreEqual(0, authorityAttacker.AvailableAttackActions);
            Assert.AreEqual(0, clientAttacker.AvailableAttackActions);
            Assert.AreNotEqual(beforeAttackDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The canonical sync digest must cover active troop-vs-troop ground combat.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);
            StringAssert.Contains(session.LastAuthoritySnapshot.Payload, "GC|");

            beforeHomeCombats = authority.Planet.ActiveCombats.Count;
            session.SubmitFromClient(AuthoritativePlayerCommand.GroundTroopOrder(911, authority.Player.Id,
                authority.Planet.Id, AuthoritativeGroundTroopOrderType.BuildingAttackTroop,
                authorityHomeTiles[2].X, authorityHomeTiles[2].Y, troopIndex: 0,
                targetTileX: authorityHomeTiles[3].X, targetTileY: authorityHomeTiles[3].Y));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.Planet.ActiveCombats.Count > beforeHomeCombats,
                "The accepted building attack should start another ground combat on the authority.");
            Assert.AreEqual(authority.Planet.ActiveCombats.Count, client.Planet.ActiveCombats.Count);
            Assert.AreEqual(0, authorityHomeTiles[2].Building.AvailableAttackActions);
            Assert.AreEqual(0, clientHomeTiles[2].Building.AvailableAttackActions);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            int beforeEnemyPlanetCombats = authority.EnemyPlanet.ActiveCombats.Count;
            session.SubmitFromClient(AuthoritativePlayerCommand.GroundTroopOrder(912, authority.Player.Id,
                authority.EnemyPlanet.Id, AuthoritativeGroundTroopOrderType.AttackBuilding,
                authorityEnemyTiles[0].X, authorityEnemyTiles[0].Y, troopIndex: 0,
                targetTileX: authorityEnemyTiles[1].X, targetTileY: authorityEnemyTiles[1].Y,
                expectedTroopName: authorityInvader.Name));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            Assert.IsTrue(authority.EnemyPlanet.ActiveCombats.Count > beforeEnemyPlanetCombats,
                "The accepted troop-vs-building attack should start ground combat on the target planet.");
            Assert.AreEqual(authority.EnemyPlanet.ActiveCombats.Count, client.EnemyPlanet.ActiveCombats.Count);
            Assert.AreEqual(authorityInvader.Name, clientInvader.Name);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            session.SubmitFromClient(AuthoritativePlayerCommand.GroundTroopOrder(913, authority.Enemy.Id,
                authority.EnemyPlanet.Id, AuthoritativeGroundTroopOrderType.AttackBuilding,
                authorityEnemyTiles[0].X, authorityEnemyTiles[0].Y, troopIndex: 0,
                targetTileX: authorityEnemyTiles[1].X, targetTileY: authorityEnemyTiles[1].Y,
                expectedTroopName: authorityInvader.Name));
            Assert.IsFalse(session.LastResult.Accepted,
                "A remote empire must not order another empire's invading ground troop.");
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
    public void Authoritative4XEmpireAutomation_SyncsAndRejectsInvalidDesigns_Headless()
    {
        const ulong Seed = 0xA470A11UL;
        BuiltWorld authority = BuildWorld(Seed);
        BuiltWorld client = BuildWorld(Seed);

        try
        {
            ClearEmpireAutomation(authority.Player);
            ClearEmpireAutomation(client.Player);

            var session = new Authoritative4XInProcessSession(authority.Screen, client.Screen);
            string freighter = PickBuildableAutomationDesign(authority.Player, s => s.IsFreighter, "freighter").Name;
            string colony = PickBuildableColonyShip(authority.Player).Name;
            string scout = PickBuildableAutomationDesign(authority.Player,
                s => s.Role == RoleName.scout || s.Role == RoleName.fighter || s.ShipCategory == ShipCategory.Recon,
                "scout").Name;
            string constructor = PickOptionalAutomationDesign(authority.Player, s => s.IsConstructor);
            string researchStation = PickOptionalAutomationDesign(authority.Player, s => s.IsResearchStation);
            string miningStation = PickOptionalAutomationDesign(authority.Player, s => s.IsMiningStation);
            var flags = AuthoritativeEmpireAutomationFlags.AutoExplore
                        | AuthoritativeEmpireAutomationFlags.AutoColonize
                        | AuthoritativeEmpireAutomationFlags.AutoFreighters
                        | AuthoritativeEmpireAutomationFlags.AutoPickBestFreighter
                        | AuthoritativeEmpireAutomationFlags.AutoResearch
                        | AuthoritativeEmpireAutomationFlags.AutoTaxes
                        | AuthoritativeEmpireAutomationFlags.RushAllConstruction;
            string initialDigest = AuthoritativeStateSnapshot.Capture(authority.Screen, 0).SyncDigest;

            session.SubmitFromClient(AuthoritativePlayerCommand.SetEmpireAutomation(63, authority.Player.Id,
                flags, freighter, colony, scout, constructor, researchStation, miningStation));
            Assert.IsTrue(session.LastResult.Accepted, session.LastResult.Reason);
            AssertEmpireAutomation(authority.Player, flags, freighter, colony, scout, constructor,
                researchStation, miningStation);
            AssertEmpireAutomation(client.Player, flags, freighter, colony, scout, constructor,
                researchStation, miningStation);
            Assert.AreNotEqual(initialDigest, session.LastAuthoritySnapshot.SyncDigest,
                "The sync digest must cover empire automation flags and selected automation designs.");
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            string acceptedDigest = session.LastAuthoritySnapshot.SyncDigest;
            session.SubmitFromClient(AuthoritativePlayerCommand.SetEmpireAutomation(64, authority.Player.Id,
                flags, freighter, colony, "Missing Authoritative Scout", constructor, researchStation, miningStation));
            Assert.IsFalse(session.LastResult.Accepted, "Missing automation designs must be rejected by the host.");
            StringAssert.Contains(session.LastResult.Reason, "scout");
            Assert.AreEqual(acceptedDigest, session.LastAuthoritySnapshot.SyncDigest);
            Assert.AreEqual(session.LastAuthoritySnapshot.SyncDigest, session.LastClientSnapshot.SyncDigest);

            var illegalFlags = flags | (AuthoritativeEmpireAutomationFlags)(1 << 20);
            session.SubmitFromClient(AuthoritativePlayerCommand.SetEmpireAutomation(65, authority.Player.Id,
                illegalFlags, freighter, colony, scout, constructor, researchStation, miningStation));
            Assert.IsFalse(session.LastResult.Accepted, "Unsupported automation flag bits must be rejected.");
            Assert.AreEqual(acceptedDigest, session.LastAuthoritySnapshot.SyncDigest);
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
    public void Authoritative4XClientContext_SubmitsEmpireAutomationWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xA470A12UL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            ClearEmpireAutomation(world.Player);
            var submitted = new List<AuthoritativePlayerCommand>();
            string freighter = PickBuildableAutomationDesign(world.Player, s => s.IsFreighter, "freighter").Name;
            string colony = PickBuildableColonyShip(world.Player).Name;
            string scout = PickBuildableAutomationDesign(world.Player,
                s => s.Role == RoleName.scout || s.Role == RoleName.fighter || s.ShipCategory == ShipCategory.Recon,
                "scout").Name;
            string constructor = PickOptionalAutomationDesign(world.Player, s => s.IsConstructor);
            var flags = AuthoritativeEmpireAutomationFlags.AutoExplore
                        | AuthoritativeEmpireAutomationFlags.AutoColonize
                        | AuthoritativeEmpireAutomationFlags.AutoFreighters
                        | AuthoritativeEmpireAutomationFlags.RushAllConstruction;

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 1850))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitEmpireAutomation(world.Player, flags,
                        freighter, colony, scout, constructor, "", ""));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(1850, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.SetEmpireAutomation, submitted[0].Kind);
                Assert.AreEqual(world.Player.Id, submitted[0].EmpireId);
                Assert.AreEqual((int)flags, submitted[0].TargetId);
                Assert.IsTrue(AuthoritativePlayerCommand.TryParseEmpireAutomationPayload(submitted[0].Text,
                    out string parsedFreighter, out string parsedColony, out string parsedScout,
                    out string parsedConstructor, out string parsedResearchStation, out string parsedMiningStation));
                Assert.AreEqual(freighter, parsedFreighter);
                Assert.AreEqual(colony, parsedColony);
                Assert.AreEqual(scout, parsedScout);
                Assert.AreEqual(constructor, parsedConstructor);
                Assert.AreEqual("", parsedResearchStation);
                Assert.AreEqual("", parsedMiningStation);
                AssertEmpireAutomation(world.Player, AuthoritativeEmpireAutomationFlags.None,
                    "", "", "", "", "", "",
                    "Passive MP clients must not locally change automation before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitEmpireAutomation(world.Enemy, flags,
                        freighter, colony, scout, constructor, "", ""));
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
    public void Authoritative4XShipDesignResearch_SubmitsMissingTechsWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0x4E5EACDUL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            TechEntry[] entries = ResearchCandidates(world.Player, 6)
                .Select(world.Player.GetTechEntry)
                .OrderBy(t => t.TechCost)
                .ToArray();
            world.Player.Research.AddTechToQueue(entries[0].UID);
            TechEntry unqueued = entries.FirstOrDefault(t => !world.Player.Research.IsQueued(t.UID));
            Assert.IsNotNull(unqueued, "The fixture needs at least one missing ship-design tech not already queued.");
            string[] originalQueue = world.Player.data.ResearchQueue.ToArray();
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2400))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    ShipDesignLoadScreen.QueueShipDesignMissingResearch(world.Player,
                        new[] { entries[0], unqueued }));
                Assert.AreEqual(1, submitted.Count,
                    "Already-queued ship-design techs must not emit duplicate queue commands.");
                Assert.AreEqual(2400, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.QueueResearch, submitted[0].Kind);
                Assert.AreEqual(unqueued.UID, submitted[0].Text);
                CollectionAssert.AreEqual(originalQueue, world.Player.data.ResearchQueue.ToArray(),
                    "Passive MP clients must not locally queue ship-design research before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    ShipDesignLoadScreen.QueueShipDesignMissingResearch(world.Enemy, new[] { unqueued }));
                Assert.AreEqual(1, submitted.Count);
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
    public void Authoritative4XClientContext_SubmitsGroundTroopOrdersWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0x6700D103UL;
        BuiltWorld world = BuildWorld(Seed);

        try
        {
            MakeAtWar(world.Player, world.Enemy);
            PlanetGridSquare[] homeTiles = PrepareGroundTroopTiles(world.Planet, columns: 3, rows: 2);
            PlanetGridSquare[] enemyTiles = PrepareGroundTroopTiles(world.EnemyPlanet, columns: 2, rows: 1);
            Troop mover = PlaceGroundTroop(world.Planet, world.Player, homeTiles[0]);
            Troop launcher = PlaceGroundTroop(world.Planet, world.Player, homeTiles[1]);
            PlaceCombatBuilding(world.Planet, homeTiles[2]);
            PlaceGroundTroop(world.Planet, world.Enemy, homeTiles[3]);
            Troop invader = PlaceGroundTroop(world.EnemyPlanet, world.Player, enemyTiles[0]);
            PlaceCombatBuilding(world.EnemyPlanet, enemyTiles[1]);

            int originalHomeTroops = world.Planet.TilesList.Sum(t => t.TroopsHere.Count);
            int originalEnemyPlanetCombats = world.EnemyPlanet.ActiveCombats.Count;
            var submitted = new List<AuthoritativePlayerCommand>();
            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2600))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitLaunchGroundTroop(world.Player, world.Planet,
                        homeTiles[1], launcher));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(AuthoritativePlayerCommandKind.GroundTroopOrder, submitted[0].Kind);
                Assert.AreEqual((int)AuthoritativeGroundTroopOrderType.LaunchOne, submitted[0].TargetId);
                Assert.IsTrue(homeTiles[1].TroopsHere.ContainsRef(launcher),
                    "Passive MP clients must not locally launch ground troops before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitMoveGroundTroop(world.Player, world.Planet,
                        homeTiles[0], mover, homeTiles[7]));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual((int)AuthoritativeGroundTroopOrderType.Move, submitted[1].TargetId);
                Assert.IsTrue(homeTiles[0].TroopsHere.ContainsRef(mover));
                Assert.IsFalse(homeTiles[7].TroopsHere.ContainsRef(mover));

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitBuildingAttackGroundTroop(world.Player, world.Planet,
                        homeTiles[2], homeTiles[3]));
                Assert.AreEqual(3, submitted.Count);
                Assert.AreEqual((int)AuthoritativeGroundTroopOrderType.BuildingAttackTroop, submitted[2].TargetId);
                Assert.AreEqual(0, world.Planet.ActiveCombats.Count,
                    "Passive MP clients must not locally start building-vs-troop combat before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitAttackGroundBuilding(world.Player, world.EnemyPlanet,
                        enemyTiles[0], invader, enemyTiles[1]));
                Assert.AreEqual(4, submitted.Count);
                Assert.AreEqual((int)AuthoritativeGroundTroopOrderType.AttackBuilding, submitted[3].TargetId);
                Assert.AreEqual(originalEnemyPlanetCombats, world.EnemyPlanet.ActiveCombats.Count,
                    "Passive MP clients must not locally start troop-vs-building combat before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitLaunchGroundTroop(world.Enemy, world.Planet,
                        homeTiles[3], homeTiles[3].TroopsHere[0]));
                Assert.AreEqual(4, submitted.Count);
            }

            Assert.AreEqual(originalHomeTroops, world.Planet.TilesList.Sum(t => t.TroopsHere.Count),
                "Submitting ground troop commands must leave the passive replica's tile troops untouched.");
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsShipTargetOrdersWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0x5147E71UL;
        BuiltWorld world = BuildWorld(Seed, includeTroopShips: true);

        try
        {
            EnsureTroopLoaded(world.TroopShip);
            MakeAtWar(world.Player, world.Enemy);
            var submitted = new List<AuthoritativePlayerCommand>();
            AIState originalShipState = world.Ship.AI.State;
            Ship originalShipTarget = world.Ship.AI.Target;
            int originalShipOrders = world.Ship.AI.OrderQueue.Count;
            Ship originalTroopEscortTarget = world.TroopShip.AI.EscortTarget;
            int originalTroopOrders = world.TroopShip.AI.OrderQueue.Count;

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 2500))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitShipTargetOrder(world.Ship, world.WingShip,
                        AuthoritativeShipTargetOrderType.Escort, queue: false));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ShipTargetOrder, submitted[0].Kind);
                Assert.AreEqual(2500, submitted[0].Sequence);
                Assert.AreEqual(world.Ship.Id, submitted[0].SubjectId);
                Assert.AreEqual(world.WingShip.Id, submitted[0].TargetId);
                Assert.AreEqual(AuthoritativePlayerCommand.EncodeShipTargetOrderPayload(
                    AuthoritativeShipTargetOrderType.Escort, queue: false), submitted[0].Text);
                Assert.AreEqual(originalShipState, world.Ship.AI.State,
                    "Passive MP clients must not locally issue ship escort orders before host acceptance.");
                Assert.AreEqual(originalShipOrders, world.Ship.AI.OrderQueue.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitShipTargetOrder(world.TroopShip,
                        world.FriendlyTroopTargetShip, AuthoritativeShipTargetOrderType.TransferTroops,
                        queue: false));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ShipTargetOrder, submitted[1].Kind);
                Assert.AreEqual(2501, submitted[1].Sequence);
                Assert.AreEqual(world.TroopShip.Id, submitted[1].SubjectId);
                Assert.AreEqual(world.FriendlyTroopTargetShip.Id, submitted[1].TargetId);
                Assert.AreEqual(AuthoritativePlayerCommand.EncodeShipTargetOrderPayload(
                    AuthoritativeShipTargetOrderType.TransferTroops, queue: false), submitted[1].Text);
                Assert.AreEqual(originalTroopEscortTarget, world.TroopShip.AI.EscortTarget,
                    "Passive MP clients must not locally issue troop transfer before host acceptance.");
                Assert.AreEqual(originalTroopOrders, world.TroopShip.AI.OrderQueue.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitShipTargetOrder(world.TroopShip, world.EnemyShip,
                        AuthoritativeShipTargetOrderType.Board, queue: false));
                Assert.AreEqual(3, submitted.Count);
                Assert.AreEqual(2502, submitted[2].Sequence);
                Assert.AreEqual(AuthoritativeShipTargetOrderType.Board,
                    ParseShipTargetOrder(submitted[2]).Order);
                Assert.AreEqual(originalTroopEscortTarget, world.TroopShip.AI.EscortTarget,
                    "Passive MP clients must not locally issue boarding orders before host acceptance.");
                Assert.AreEqual(originalTroopOrders, world.TroopShip.AI.OrderQueue.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitShipTargetOrder(world.Ship, world.EnemyShip,
                        AuthoritativeShipTargetOrderType.Attack, queue: true));
                Assert.AreEqual(4, submitted.Count);
                Assert.AreEqual(2503, submitted[3].Sequence);
                (AuthoritativeShipTargetOrderType order, bool queued) = ParseShipTargetOrder(submitted[3]);
                Assert.AreEqual(AuthoritativeShipTargetOrderType.Attack, order);
                Assert.IsTrue(queued);
                Assert.AreEqual(originalShipTarget, world.Ship.AI.Target,
                    "Passive MP clients must not locally issue target attack orders before host acceptance.");
                Assert.AreEqual(originalShipOrders, world.Ship.AI.OrderQueue.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipTargetOrder(world.Ship, world.WingShip,
                        AuthoritativeShipTargetOrderType.Attack, queue: false));
                Assert.AreEqual(4, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipTargetOrder(world.Ship, world.EnemyShip,
                        AuthoritativeShipTargetOrderType.Escort, queue: false));
                Assert.AreEqual(4, submitted.Count);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipTargetOrder(world.TroopShip, world.WingShip,
                        AuthoritativeShipTargetOrderType.TransferTroops, queue: false));
                Assert.AreEqual(4, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsTroopLandingAndBombardmentWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0x5147E72UL;
        BuiltWorld world = BuildWorld(Seed, includeTroopShips: true);

        try
        {
            EnsureTroopLoaded(world.TroopShip);
            MakeAtWar(world.Player, world.Enemy);
            world.Ship.BombBays.Add(null);
            int originalTroopOrders = world.TroopShip.AI.OrderQueue.Count;
            int originalBomberOrders = world.Ship.AI.OrderQueue.Count;
            AIState originalTroopState = world.TroopShip.AI.State;
            AIState originalBomberState = world.Ship.AI.State;

            var submitted = new List<AuthoritativePlayerCommand>();
            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 940))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitShipPlanetOrder(world.TroopShip, world.Planet,
                        AuthoritativeShipPlanetOrderType.LandTroops, clearOrders: true, MoveOrder.Regular));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ShipPlanetOrder, submitted[0].Kind);
                Assert.AreEqual(world.TroopShip.Id, submitted[0].SubjectId);
                Assert.AreEqual(world.Planet.Id, submitted[0].TargetId);
                Assert.AreEqual($"{(int)AuthoritativeShipPlanetOrderType.LandTroops}|1|{(int)MoveOrder.Regular}",
                    submitted[0].Text);
                Assert.AreEqual(originalTroopOrders, world.TroopShip.AI.OrderQueue.Count,
                    "Passive MP clients must not locally enqueue troop landing before host acceptance.");
                Assert.AreEqual(originalTroopState, world.TroopShip.AI.State);

                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitShipPlanetOrders(new[] { world.Ship },
                        world.EnemyPlanet, clearOrders: true, MoveOrder.Aggressive,
                        _ => AuthoritativeShipPlanetOrderType.Bombard));
                Assert.AreEqual(2, submitted.Count);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ShipPlanetOrder, submitted[1].Kind);
                Assert.AreEqual(world.Ship.Id, submitted[1].SubjectId);
                Assert.AreEqual(world.EnemyPlanet.Id, submitted[1].TargetId);
                Assert.AreEqual($"{(int)AuthoritativeShipPlanetOrderType.Bombard}|1|{(int)MoveOrder.Aggressive}",
                    submitted[1].Text);
                Assert.AreEqual(originalBomberOrders, world.Ship.AI.OrderQueue.Count,
                    "Passive MP clients must not locally enqueue bombardment before host acceptance.");
                Assert.AreEqual(originalBomberState, world.Ship.AI.State);

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipPlanetOrders(new[] { world.Ship },
                        world.Planet, clearOrders: true, MoveOrder.Aggressive,
                        _ => AuthoritativeShipPlanetOrderType.Bombard));
                Assert.AreEqual(2, submitted.Count);
            }
        }
        finally
        {
            world.Screen.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XClientContext_SubmitsShipColonizeWithoutLocalMutation_Headless()
    {
        const ulong Seed = 0xC0110C9UL;
        BuiltWorld world = BuildWorld(Seed, includeNeutralPlanet: true, includeColonyShip: true);

        try
        {
            Ship colony = world.ColonyShip;
            AIState originalState = colony.AI.State;
            int originalOrderCount = colony.AI.OrderQueue.Count;
            bool originalGoalPresent = world.Player.AI.HasGoal(g => g.Type == GoalType.MarkForColonization
                                                                 && g.FinishedShip == colony);
            var submitted = new List<AuthoritativePlayerCommand>();

            using (Authoritative4XClientContext.Begin(peerId: 2, empireId: world.Player.Id,
                       submitted.Add, firstSequence: 730))
            {
                Assert.AreEqual(Authoritative4XUiCommandResult.Submitted,
                    Authoritative4XClientContext.TrySubmitShipPlanetOrder(colony, world.NeutralPlanet,
                        AuthoritativeShipPlanetOrderType.Colonize, clearOrders: true, MoveOrder.Regular));
                Assert.AreEqual(1, submitted.Count);
                Assert.AreEqual(730, submitted[0].Sequence);
                Assert.AreEqual(AuthoritativePlayerCommandKind.ShipPlanetOrder, submitted[0].Kind);
                Assert.AreEqual(colony.Id, submitted[0].SubjectId);
                Assert.AreEqual(world.NeutralPlanet.Id, submitted[0].TargetId);
                Assert.AreEqual($"{(int)AuthoritativeShipPlanetOrderType.Colonize}|1|{(int)MoveOrder.Regular}",
                    submitted[0].Text);
                Assert.AreEqual(originalState, colony.AI.State,
                    "Passive MP clients must not locally order colonization before host acceptance.");
                Assert.AreEqual(originalOrderCount, colony.AI.OrderQueue.Count,
                    "Passive MP clients must not locally queue colony ship goals before host acceptance.");
                Assert.AreEqual(originalGoalPresent, world.Player.AI.HasGoal(g =>
                        g.Type == GoalType.MarkForColonization && g.FinishedShip == colony),
                    "Passive MP clients must not locally create colonization goals before host acceptance.");

                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipPlanetOrder(world.Ship, world.NeutralPlanet,
                        AuthoritativeShipPlanetOrderType.Colonize, clearOrders: true, MoveOrder.Regular));
                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipPlanetOrder(colony, world.Planet,
                        AuthoritativeShipPlanetOrderType.Colonize, clearOrders: true, MoveOrder.Regular));
                Assert.AreEqual(Authoritative4XUiCommandResult.Blocked,
                    Authoritative4XClientContext.TrySubmitShipPlanetOrder(colony, world.NeutralPlanet,
                        AuthoritativeShipPlanetOrderType.Colonize, clearOrders: true,
                        MoveOrder.Regular | MoveOrder.AddWayPoint));
                Assert.AreEqual(1, submitted.Count,
                    "Invalid passive-client colonize attempts should not send commands for host rejection.");
            }
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
        const string SessionId = "auth4x-test-session";
        const string StartFingerprint = "0x0123456789ABCDEF";
        const string SessionIdField = "sessionId=" + SessionId;
        const string StartFingerprintField = "startFingerprint=" + StartFingerprint;
        const string StartSummary = SessionIdField + " " + StartFingerprintField
            + " protocol=77 buildHash='0xTESTBUILD' buildSummary='unit-test' settingsHash=0xTESTSETTINGS "
            + "seed=424242 hostPeer=2 joinPeer=3";
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
                new[] { authority.Player.Id, authority.Enemy.Id },
                SessionId, StartFingerprint, StartSummary);
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
            StringAssert.Contains(text, SessionIdField);
            StringAssert.Contains(text, StartFingerprintField);
            StringAssert.Contains(text, "SESSION sessionId=");
            StringAssert.Contains(text, "protocol=77");
            StringAssert.Contains(text, "buildHash='0xTESTBUILD'");
            StringAssert.Contains(text, "settingsHash=0xTESTSETTINGS");
            StringAssert.Contains(text, "seed=424242");
            StringAssert.Contains(text, "hostPeer=2 joinPeer=3");
            StringAssert.Contains(text, "ENV game=");
            StringAssert.Contains(text, "PEERS empireByPeer=");
            StringAssert.Contains(text, "COMMAND source=ui");
            StringAssert.Contains(text, $"peer={HostPeer}");
            StringAssert.Contains(text, "kind=SetColonyType");
            StringAssert.Contains(text, "textHash=0x");
            StringAssert.Contains(text, "summary='payload=ColonyType type=Military'");
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
    public void Authoritative4XLiveTelemetry_DecodesCommandPayloadEvidence_Headless()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"auth4x_live_decode_{Guid.NewGuid():N}");
        string oldOutput = Authoritative4XLiveTelemetry.OutputDirectoryOverride;
        bool? oldEnabled = Authoritative4XLiveTelemetry.EnabledOverride;

        try
        {
            Authoritative4XLiveTelemetry.OutputDirectoryOverride = dir;
            Authoritative4XLiveTelemetry.EnabledOverride = true;
            var telemetry = Authoritative4XLiveTelemetry.Start(Authoritative4XLiveRole.Host,
                localPeerId: 9, localEmpireId: 1, new Dictionary<int, int> { [9] = 1 }, new[] { 1 });
            string path = telemetry.SessionPath;

            telemetry.Command("unit", 9,
                AuthoritativePlayerCommand.SetEmpireBudget(1, 1, taxRate: 0.25f,
                    treasuryGoal: 0.5f, autoTaxes: true));
            telemetry.Command("unit", 9,
                AuthoritativePlayerCommand.SetEmpireAutomation(6, 1,
                    AuthoritativeEmpireAutomationFlags.AutoExplore
                    | AuthoritativeEmpireAutomationFlags.AutoFreighters,
                    "Freighter", "Colony", "Scout", "Constructor", "", ""));
            telemetry.Command("unit", 9,
                AuthoritativePlayerCommand.ShipPlanetOrder(2, 1, shipId: 77, planetId: 88,
                    AuthoritativeShipPlanetOrderType.Colonize, clearOrders: false, MoveOrder.Aggressive));
            telemetry.Command("unit", 9,
                AuthoritativePlayerCommand.ShipTargetOrder(12, 1, shipId: 77, targetShipId: 99,
                    AuthoritativeShipTargetOrderType.Board));
            telemetry.Command("unit", 9,
                AuthoritativePlayerCommand.SetShipTradePolicy(7, 1, shipId: 77,
                    AuthoritativeShipTradePolicyKind.InterEmpire, enabled: true));
            telemetry.Command("unit", 9,
                AuthoritativePlayerCommand.SetShipCarrierPolicy(8, 1, shipId: 77,
                    AuthoritativeShipCarrierPolicyKind.FightersOut, enabled: false));
            telemetry.Command("unit", 9,
                AuthoritativePlayerCommand.SetShipTradeRoute(9, 1, shipId: 77,
                    planetId: 88, enabled: true));
            telemetry.Command("unit", 9,
                AuthoritativePlayerCommand.SetShipAreaOfOperation(10, 1, shipId: 77,
                    AuthoritativeShipAreaOfOperationAction.AddRectangle,
                    new Rectangle(1000, 2000, 6000, 7000)));
            telemetry.Command("unit", 9,
                AuthoritativePlayerCommand.RefitShip(11, 1, shipId: 77,
                    "MP Refit Corvette", AuthoritativeShipRefitMode.All, rush: true));
            telemetry.Command("unit", 9,
                AuthoritativePlayerCommand.SetPlanetManualTradeSlots(3, 1, planetId: 88,
                    foodImport: 1, prodImport: 2, coloImport: 3, foodExport: 4, prodExport: 5, coloExport: 6));
            telemetry.Command("unit", 9,
                AuthoritativePlayerCommand.ApplyColonyBlueprints(4, 1, planetId: 88,
                    new BlueprintsTemplate("MP Core", exclusive: true, linkTo: "",
                        plannedBuildings: new HashSet<string>(StringComparer.Ordinal) { "Outpost" },
                        Planet.ColonyType.Core)));
            telemetry.Command("unit", 9,
                AuthoritativePlayerCommand.DesignShip(5, 1, new string('A', 320)));
            telemetry.Dispose();

            string text = File.ReadAllText(path);
            StringAssert.Contains(text, "textHash=0x");
            StringAssert.Contains(text, "summary='payload=EmpireBudget tax=0.25 treasury=0.5 auto=True'");
            StringAssert.Contains(text, "summary='payload=EmpireAutomation flags=AutoExplore, AutoFreighters");
            StringAssert.Contains(text, "freighter=\\'Freighter\\'");
            StringAssert.Contains(text, "summary='payload=ShipPlanetOrder order=Colonize clear=False move=Aggressive'");
            StringAssert.Contains(text, "summary='payload=ShipTargetOrder order=Board queued=False'");
            StringAssert.Contains(text, "summary='payload=ShipTradePolicy kind=InterEmpire enabled=True'");
            StringAssert.Contains(text, "summary='payload=ShipCarrierPolicy kind=FightersOut enabled=False'");
            StringAssert.Contains(text, "summary='payload=ShipTradeRoute planet=88 enabled=True'");
            StringAssert.Contains(text, "summary='payload=ShipAreaOfOperation action=AddRectangle rect=1000,2000,6000,7000'");
            StringAssert.Contains(text, "summary='payload=ShipRefit mode=All design=\\'MP Refit Corvette\\' rush=True'");
            StringAssert.Contains(text, "summary='payload=ManualTradeSlots import=1,2,3 export=4,5,6'");
            StringAssert.Contains(text, "summary='payload=Blueprints name=\\'MP Core\\' type=Core buildings=1'");
            StringAssert.Contains(text, "summary='payload=DesignShip encodedChars=320'");
            StringAssert.Contains(text, "textChars=320");
            StringAssert.Contains(text, "name='AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        }
        finally
        {
            Authoritative4XLiveTelemetry.OutputDirectoryOverride = oldOutput;
            Authoritative4XLiveTelemetry.EnabledOverride = oldEnabled;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void Authoritative4XLiveTelemetry_RecordsSyncMismatchEvidence_Headless()
    {
        const ulong Seed = 0x41E4005AUL;
        const int Peer = 2;
        const string SessionId = "auth4x-mismatch-test";
        const string StartFingerprint = "0x0BADF00D0BADF00D";
        const string StartSummary = "sessionId=" + SessionId + " startFingerprint=" + StartFingerprint
            + " protocol=77 buildHash='0xMISMATCH' settingsHash=0xMISMATCHSETTINGS seed=424243 hostPeer=2 joinPeer=3";
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
                clientTransport, Peer, client.Player.Id, new[] { client.Player.Id },
                SessionId, StartFingerprint, StartSummary,
                new Dictionary<int, int> { [Peer] = client.Player.Id });
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
            StringAssert.Contains(text, $"sessionId={SessionId}");
            StringAssert.Contains(text, $"startFingerprint={StartFingerprint}");
            StringAssert.Contains(text, "seq=1");
            StringAssert.Contains(text, "kind=SetColonyType");
            StringAssert.Contains(text, "firstDiff='");
            StringAssert.Contains(text, "authorityPayload='");
            StringAssert.Contains(text, "clientPayload='");
            StringAssert.Contains(text, "recentEvents='");
            StringAssert.Contains(text, $"recentEventCapacity={Authoritative4XLiveTelemetry.RecentEventCapacity}");
            string[] authorityPayloads = Directory.GetFiles(dir, "*sync-mismatch-authority.payload");
            string[] clientPayloads = Directory.GetFiles(dir, "*sync-mismatch-client.payload");
            string[] recentEvents = Directory.GetFiles(dir, "*sync-mismatch-recent-events.log");
            Assert.AreEqual(1, authorityPayloads.Length, "Mismatch telemetry should persist the authority payload.");
            Assert.AreEqual(1, clientPayloads.Length, "Mismatch telemetry should persist the client payload.");
            Assert.AreEqual(1, recentEvents.Length, "Mismatch telemetry should persist the recent event ring.");
            StringAssert.Contains(File.ReadAllText(authorityPayloads[0]), "P|");
            StringAssert.Contains(File.ReadAllText(clientPayloads[0]), "P|");
            string recentText = File.ReadAllText(recentEvents[0]);
            StringAssert.Contains(recentText, $"sessionId={SessionId}");
            StringAssert.Contains(recentText, $"startFingerprint={StartFingerprint}");
            StringAssert.Contains(recentText, $"eventCapacity={Authoritative4XLiveTelemetry.RecentEventCapacity}");
            StringAssert.Contains(recentText, "mismatch origin=");
            StringAssert.Contains(recentText, "COMMAND source=ui");
            StringAssert.Contains(recentText, "RESULT origin=");
            StringAssert.Contains(recentText, "seq=1");
            StringAssert.Contains(recentText, "kind=SetColonyType");
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
    public void Authoritative4XLobbyNetworkFlow_ExchangesStartOverTcpAndAttachesLiveSession_Headless()
    {
        LoadAllGameData();

        const int Protocol = 77;
        const int HostPeer = 2;
        const int JoinPeer = 3;
        const string BuildHash = "0xLOBBYFLOW";
        IEmpireData[] races = ResourceManager.MajorRaces
            .Where(r => !r.IsFactionOrMinorRace)
            .OrderBy(r => RacePreference(r), StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        Assert.IsTrue(races.Length >= 2, "The network-flow proof needs two playable major races.");

        var settings = new Authoritative4XGameSettings
        {
            GenerationSeed = 0x4B1B4B3,
            GalaxySize = GalSize.Tiny,
            StarsCount = RaceDesignScreen.StarsAbundance.Rare,
            Mode = RaceDesignScreen.GameMode.Sandbox,
            Difficulty = GameDifficulty.Hard,
            NumOpponents = 1,
            Pace = 2.25f,
            TurnTimer = 4,
            ExtraPlanets = 1,
            StartingPlanetRichnessBonus = 1.25f,
            GameSpeed = 2f,
            StartPaused = true,
        };

        TcpLockstepTransport hostTransport = null;
        TcpLockstepTransport joinTransport = null;
        Authoritative4XGeneratedGameStart hostGenerated = null;
        Authoritative4XGeneratedGameStart joinGenerated = null;
        Authoritative4XLiveSession liveHost = null;
        Authoritative4XLiveSession liveJoin = null;

        try
        {
            var flow = new Authoritative4XLobbyNetworkFlow(HostPeer, JoinPeer);
            var hostLobby = new Authoritative4XLobby(HostPeer, "Host");
            hostLobby.Join(JoinPeer, "Join");
            Assert.IsTrue(hostLobby.SetSettings(HostPeer, settings).Valid);
            string[] hostTraits = OneAffordableTrait();
            Assert.IsTrue(hostLobby.SetPlayerSelection(HostPeer, RacePreference(races[0]), hostTraits).Valid);
            Assert.IsTrue(hostLobby.SetReady(HostPeer, true).Valid);

            var joinLobby = new Authoritative4XLobby(JoinPeer, "Join");
            Assert.IsTrue(joinLobby.SetPlayerSelection(JoinPeer, RacePreference(races[1]), Array.Empty<string>()).Valid);
            Assert.IsTrue(joinLobby.SetReady(JoinPeer, true).Valid);
            SessionLobbyMessage receivedJoinLobby = null;
            SessionStartMessage receivedStart = null;

            int port = FreeTcpPort();
            hostTransport = TcpLockstepTransport.HostMulti(port);
            joinTransport = TcpLockstepTransport.JoinAsPeer("127.0.0.1", port, JoinPeer,
                Authoritative4XLobby.AuthorityPeerId);
            Assert.IsTrue(hostTransport.WaitForConnections(1, TimeSpan.FromSeconds(3)),
                "Lobby TCP host did not accept the joiner.");
            hostTransport.Register(Authoritative4XLobby.AuthorityPeerId, message =>
            {
                if (message is SessionLobbyMessage lobby)
                    receivedJoinLobby = lobby;
            });
            joinTransport.Register(JoinPeer, message =>
            {
                if (message is SessionStartMessage start)
                    receivedStart = start;
            });

            SessionLobbyMessage joinMessage = flow.BuildLobbyMessage(joinLobby, JoinPeer,
                BuildHash, "loopback join");
            joinTransport.Send(Authoritative4XLobby.AuthorityPeerId, joinMessage);
            PumpTransportUntil(() => receivedJoinLobby != null, hostTransport, joinTransport);
            Assert.AreEqual(JoinPeer, receivedJoinLobby.PeerId);
            Assert.AreEqual(RacePreference(races[1]), receivedJoinLobby.RacePreference);

            Authoritative4XLobbyValidation applied = flow.ApplyLobbyMessage(hostLobby, receivedJoinLobby);
            Assert.IsTrue(applied.Valid, applied.Reason);
            Assert.IsTrue(hostLobby.CanStart().Valid, hostLobby.CanStart().Reason);
            SessionStartMessage start = flow.BuildStartMessage(hostLobby, Protocol,
                BuildHash, "loopback host", maxTurns: 600);
            string sessionId = Authoritative4XLobbyNetworkFlow.SessionId(start);
            string startFingerprint = Authoritative4XLobbyNetworkFlow.StartFingerprint(start);
            Assert.IsTrue(sessionId.StartsWith("auth4x-", StringComparison.Ordinal));
            Assert.IsTrue(startFingerprint.StartsWith("0x", StringComparison.Ordinal));
            StringAssert.Contains(Authoritative4XLobbyNetworkFlow.StartTelemetrySummary(start),
                $"sessionId={sessionId}");
            StringAssert.Contains(Authoritative4XLobbyNetworkFlow.StartTelemetrySummary(start),
                $"startFingerprint={startFingerprint}");
            hostTransport.Send(JoinPeer, start);
            PumpTransportUntil(() => receivedStart != null, hostTransport, joinTransport);
            Assert.AreEqual("", flow.ValidateStartMessage(receivedStart, Protocol, BuildHash));
            Assert.AreEqual(settings.Normalized(2).SettingsHash, receivedStart.SettingsHash);
            Assert.AreEqual(sessionId, Authoritative4XLobbyNetworkFlow.SessionId(receivedStart));
            Assert.AreEqual(startFingerprint, Authoritative4XLobbyNetworkFlow.StartFingerprint(receivedStart));

            hostGenerated = flow.CreateGeneratedGame(start);
            joinGenerated = flow.CreateGeneratedGame(receivedStart);
            Assert.AreEqual(settings.GenerationSeed, hostGenerated.Settings.GenerationSeed);
            Assert.AreEqual(hostGenerated.Settings.SettingsHash, joinGenerated.Settings.SettingsHash);
            Assert.AreEqual(hostGenerated.EmpireIdForPeer(HostPeer), joinGenerated.EmpireIdForPeer(HostPeer));
            Assert.AreEqual(hostGenerated.EmpireIdForPeer(JoinPeer), joinGenerated.EmpireIdForPeer(JoinPeer));

            liveHost = flow.AttachLiveSession(hostGenerated, hostTransport, HostPeer,
                Authoritative4XLiveRole.Host, start);
            liveJoin = flow.AttachLiveSession(joinGenerated, joinTransport, JoinPeer,
                Authoritative4XLiveRole.Client, receivedStart);
            Assert.IsTrue(hostGenerated.AuthorityUniverse.IsAuthoritative4XMultiplayer);
            Assert.IsTrue(joinGenerated.AuthorityUniverse.IsAuthoritative4XMultiplayer);
            Assert.AreEqual(sessionId, liveHost.TelemetrySessionId);
            Assert.AreEqual(sessionId, liveJoin.TelemetrySessionId);
            Assert.AreEqual(startFingerprint, liveHost.TelemetryStartFingerprint);
            Assert.AreEqual(startFingerprint, liveJoin.TelemetryStartFingerprint);
            Assert.AreEqual(hostGenerated.EmpireIdForPeer(HostPeer), liveHost.LocalEmpireId);
            Assert.AreEqual(joinGenerated.EmpireIdForPeer(JoinPeer), liveJoin.LocalEmpireId);
            Assert.IsTrue(hostGenerated.AuthorityUniverse.UState.Paused);
            Assert.AreEqual(settings.GameSpeed, hostGenerated.AuthorityUniverse.UState.GameSpeed);

            Planet clientJoinPlanet = joinGenerated.AuthorityUniverse.UState
                .GetEmpireById(joinGenerated.EmpireIdForPeer(JoinPeer))
                .GetPlanets()
                .OrderBy(p => p.Id)
                .FirstOrDefault();
            Assert.IsNotNull(clientJoinPlanet, "The generated client empire needs a planet for the UI command proof.");
            Planet authorityJoinPlanet = hostGenerated.AuthorityUniverse.UState.GetPlanet(clientJoinPlanet.Id);
            Assert.IsNotNull(authorityJoinPlanet, "The authority game must contain the client's planet id.");
            Planet.ColonyType targetType = clientJoinPlanet.CType == Planet.ColonyType.Research
                ? Planet.ColonyType.Military
                : Planet.ColonyType.Research;
            Assert.IsTrue(Authoritative4XClientContext.TrySubmitSetColonyType(clientJoinPlanet, targetType),
                "The visible client session should route UI commands through the authoritative context.");
            PumpLiveTcpUntil(() => NetworkClientCaughtUp(liveJoin, JoinPeer, 1)
                                  && liveHost.LastSnapshot != null,
                liveHost, liveJoin);
            Assert.IsTrue(liveJoin.LastResult.Accepted, liveJoin.LastResult.Reason);
            Assert.AreEqual(targetType, authorityJoinPlanet.CType);
            Assert.AreEqual(targetType, clientJoinPlanet.CType);
            Assert.AreEqual(liveHost.LastSnapshot.HashLo, liveJoin.LastSnapshot.HashLo);
            Assert.AreEqual(liveHost.LastSnapshot.HashHi, liveJoin.LastSnapshot.HashHi);
            Assert.AreEqual(liveHost.LastSnapshot.SyncDigest, liveJoin.LastSnapshot.SyncDigest);

            liveHost.TryTogglePause();
            PumpLiveTcpUntil(() => liveJoin.LastSnapshot != null
                                  && !joinGenerated.AuthorityUniverse.UState.Paused,
                liveHost, liveJoin);
            Assert.IsFalse(hostGenerated.AuthorityUniverse.UState.Paused);
            Assert.IsFalse(joinGenerated.AuthorityUniverse.UState.Paused);
        }
        finally
        {
            liveHost?.Dispose();
            liveJoin?.Dispose();
            hostGenerated?.Dispose();
            joinGenerated?.Dispose();
            hostTransport?.Dispose();
            joinTransport?.Dispose();
        }
    }

    [TestMethod]
    public void Authoritative4XLobbySelfTest_RunsRealLoopbackUiCommandProof_Headless()
    {
        LoadAllGameData();

        Authoritative4XLobbySelfTestResult result =
            ArenaMultiplayerLobbyScreen.RunAuthoritative4XSelfTestForHeadless(60);

        Assert.IsTrue(result.Passed, result.FailureReason);
        Assert.IsTrue(result.CommandSubmitted, "The 4X lobby self-test must submit a UI command.");
        Assert.IsTrue(result.CommandAccepted, "The authoritative host must accept the self-test UI command.");
        Assert.IsTrue(result.SnapshotsSynced,
            $"Authority/client snapshots diverged. authority={result.AuthorityDigest} client={result.ClientDigest}");
        Assert.AreEqual(60, result.MaxTurns);
        Assert.AreEqual(2, result.HostPeerId);
        Assert.AreEqual(3, result.JoinPeerId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.FinalHash));
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

    static void AssertPlanetManualTradeSlots(Planet planet, int expectedFoodImport, int expectedProdImport,
        int expectedColoImport, int expectedFoodExport, int expectedProdExport, int expectedColoExport)
        => AssertManualTradeSlots(planet.ManualFoodImportSlots, planet.ManualProdImportSlots,
            planet.ManualColoImportSlots, planet.ManualFoodExportSlots, planet.ManualProdExportSlots,
            planet.ManualColoExportSlots, expectedFoodImport, expectedProdImport, expectedColoImport,
            expectedFoodExport, expectedProdExport, expectedColoExport);

    static void AssertManualTradeSlots(int foodImport, int prodImport, int coloImport,
        int foodExport, int prodExport, int coloExport,
        int expectedFoodImport, int expectedProdImport, int expectedColoImport,
        int expectedFoodExport, int expectedProdExport, int expectedColoExport)
    {
        Assert.AreEqual(expectedFoodImport, foodImport, "Food import slots did not match.");
        Assert.AreEqual(expectedProdImport, prodImport, "Production import slots did not match.");
        Assert.AreEqual(expectedColoImport, coloImport, "Colonist import slots did not match.");
        Assert.AreEqual(expectedFoodExport, foodExport, "Food export slots did not match.");
        Assert.AreEqual(expectedProdExport, prodExport, "Production export slots did not match.");
        Assert.AreEqual(expectedColoExport, coloExport, "Colonist export slots did not match.");
    }

    static void AssertPlanetDefenseTargets(Planet planet, int expectedGarrisonSize,
        int expectedWantedPlatforms, int expectedWantedShipyards, int expectedWantedStations)
        => AssertDefenseTargets(planet.GarrisonSize, planet.WantedPlatforms,
            planet.WantedShipyards, planet.WantedStations, expectedGarrisonSize,
            expectedWantedPlatforms, expectedWantedShipyards, expectedWantedStations);

    static void AssertDefenseTargets(int garrisonSize, int wantedPlatforms,
        int wantedShipyards, int wantedStations, int expectedGarrisonSize,
        int expectedWantedPlatforms, int expectedWantedShipyards, int expectedWantedStations)
    {
        Assert.AreEqual(expectedGarrisonSize, garrisonSize, "Garrison target did not match.");
        Assert.AreEqual(expectedWantedPlatforms, wantedPlatforms, "Wanted platform target did not match.");
        Assert.AreEqual(expectedWantedShipyards, wantedShipyards, "Wanted shipyard target did not match.");
        Assert.AreEqual(expectedWantedStations, wantedStations, "Wanted station target did not match.");
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

    static void PumpTransportUntil(Func<bool> done, params TcpLockstepTransport[] transports)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!done() && DateTime.UtcNow < deadline)
        {
            foreach (TcpLockstepTransport transport in transports)
                transport.Poll();
            System.Threading.Thread.Sleep(5);
        }
        string errors = string.Join("; ", transports.Select(t => t.LastError));
        Assert.IsTrue(done(), $"Timed out waiting for lobby transport messages. errors='{errors}'");
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

    static void PumpLiveTcpUntil(Func<bool> done, Authoritative4XLiveSession host,
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
            $"Timed out waiting for live authoritative sessions. host='{host.LastError}' client='{client.LastError}'");
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

    static (PlanetGridSquare BuildingTile, PlanetGridSquare BioTile, string BuildingName) PrepareScrapTiles(
        Planet planet, string buildingName = null)
    {
        planet.TilesList.Clear();
        var buildingTile = new PlanetGridSquare(planet, 0, 0, b: null, hab: true, terraformable: false);
        var bioTile = new PlanetGridSquare(planet, 1, 0, b: null, hab: false, terraformable: true);
        planet.TilesList.Add(buildingTile);
        planet.TilesList.Add(bioTile);
        planet.RefreshBuildingsWeCanBuildHere();

        buildingName ??= PickScrappableBuildingName(planet, buildingTile);
        Building building = ResourceManager.CreateBuilding(planet, buildingName);
        Assert.IsTrue(building.Scrappable, $"Building {building.Name} should be scrappable for the MP tile scrap proof.");
        buildingTile.PlaceBuilding(building, planet);

        Building biosphere = ResourceManager.CreateBuilding(planet, Building.BiospheresId);
        bioTile.PlaceBuilding(biosphere, planet);
        Assert.IsTrue(bioTile.Biosphere,
            $"Planet {planet.Id} needs a placed biosphere for the authoritative tile scrap proof.");
        planet.RefreshBuildingsWeCanBuildHere();
        return (buildingTile, bioTile, buildingName);
    }

    static string PickScrappableBuildingName(Planet planet, PlanetGridSquare tile)
    {
        planet.RefreshBuildingsWeCanBuildHere();
        Building building = planet.GetBuildingsCanBuild()
            .Where(b => !b.IsBiospheres && b.Scrappable && tile.CanPlaceBuildingHere(b))
            .OrderBy(b => b.ActualCost(planet.Owner))
            .ThenBy(b => b.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(building,
            $"Planet {planet.Id} needs at least one scrappable building for the authoritative tile scrap proof.");
        return building.Name;
    }

    static Building PickBlueprintBuilding(Planet planet)
    {
        planet.RefreshBuildingsWeCanBuildHere();
        Building building = planet.GetBuildingsCanBuild()
            .Where(b => b.IsSuitableForBlueprints)
            .OrderBy(b => b.ActualCost(planet.Owner))
            .ThenBy(b => b.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(building,
            $"Planet {planet.Id} needs at least one blueprint-suitable building for the authoritative blueprint proof.");
        return building;
    }

    static BlueprintsTemplate TestBlueprintTemplate(string name, Building building, Planet.ColonyType type,
        bool exclusive = true, string linkTo = "")
        => new(name, exclusive, linkTo, new HashSet<string>(StringComparer.Ordinal) { building.Name }, type);

    static void AssertColonyBlueprints(Planet planet, string expectedName, string expectedBuilding,
        Planet.ColonyType expectedType)
    {
        Assert.IsTrue(planet.HasBlueprints, $"Planet {planet.Id} should have authoritative blueprints.");
        Assert.AreEqual(expectedName, planet.Blueprints.Name);
        Assert.AreEqual(expectedType, planet.Blueprints.ColonyType);
        Assert.IsTrue(planet.Blueprints.PlannedBuildingNames.Contains(expectedBuilding),
            $"Blueprints for planet {planet.Id} should include {expectedBuilding}.");
        Assert.AreEqual(expectedType, planet.CType,
            "Applying blueprints through the host should preserve the template-driven colony type change.");
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

    static void EnableFleetRequisitionYard(BuiltWorld world)
    {
        world.Planet.HasSpacePort = true;
        world.Player.UpdateRallyPoints();
    }

    static Fleet CreateFleetRequisitionFixture(Empire empire, int fleetKey, string designName)
    {
        Fleet fleet = empire.CreateFleet(fleetKey, null);
        fleet.DataNodes.Add(new FleetDataNode
        {
            ShipName = designName,
            RelativeFleetOffset = new Vector2(6_000f, -3_000f),
            CombatState = CombatState.Artillery,
            OrdersRadius = 500_000f,
        });
        return fleet;
    }

    static FleetRequisition FindFleetRequisitionGoal(Empire empire, Fleet fleet, int nodeIndex)
        => empire.AI.Goals.OfType<FleetRequisition>()
            .FirstOrDefault(g => g.Fleet == fleet && fleet.DataNodes[nodeIndex].Goal == g);

    static bool HasQueuedFleetRequisitionShip(Planet planet, FleetRequisition goal, string designName)
        => planet.Construction.GetConstructionQueueSnapshot()
            .Any(q => q.isShip
                      && q.Goal == goal
                      && string.Equals(q.ShipData?.Name, designName, StringComparison.Ordinal));

    static IShipDesign PickBuildableColonyShip(Empire empire)
    {
        IShipDesign ship = empire.ShipsWeCanBuildSnapshot
            .Where(s => s.IsColonyShip && !s.IsPlatformOrStation && !s.IsShipyard)
            .OrderBy(s => s.BaseCost)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(ship, $"Empire {empire.Id} needs at least one buildable colony ship for the authoritative colonize proof.");
        return ship;
    }

    static IShipDesign PickBuildableAutomationDesign(Empire empire, Func<IShipDesign, bool> predicate, string label)
    {
        IShipDesign ship = empire.ShipsWeCanBuildSnapshot
            .Where(s => s.IsShipGoodToBuild(empire) && predicate(s))
            .OrderBy(s => s.BaseCost)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(ship, $"Empire {empire.Id} needs at least one buildable {label} design for the authoritative automation proof.");
        return ship;
    }

    static IShipDesign PickLoadedDesign(Func<IShipDesign, bool> predicate, string label)
    {
        IShipDesign ship = ResourceManager.Ships.Designs
            .Where(d => d.IsValidDesign && predicate(d))
            .OrderBy(d => d.BaseCost)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(ship, $"Loaded content needs at least one {label} design for the authoritative MP proof.");
        return ship;
    }

    static string PickOptionalAutomationDesign(Empire empire, Func<IShipDesign, bool> predicate)
    {
        return empire.ShipsWeCanBuildSnapshot
            .Where(s => s.IsShipGoodToBuild(empire) && predicate(s))
            .OrderBy(s => s.BaseCost)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => s.Name)
            .FirstOrDefault() ?? "";
    }

    static void ClearEmpireAutomation(Empire empire)
    {
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
        empire.RushAllConstruction = false;
        empire.SwitchRushAllConstruction(false);
        empire.data.CurrentAutoFreighter = "";
        empire.data.CurrentAutoColony = "";
        empire.data.CurrentAutoScout = "";
        empire.data.CurrentConstructor = "";
        empire.data.CurrentResearchStation = "";
        empire.data.CurrentMiningStation = "";
    }

    static void AssertEmpireAutomation(Empire empire, AuthoritativeEmpireAutomationFlags flags,
        string freighter, string colony, string scout, string constructor,
        string researchStation, string miningStation, string message = "")
    {
        Assert.AreEqual(flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickConstructors),
            empire.AutoPickConstructors, message);
        Assert.AreEqual(flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestColonizer),
            empire.AutoPickBestColonizer, message);
        Assert.AreEqual(flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestFreighter),
            empire.AutoPickBestFreighter, message);
        Assert.AreEqual(flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoResearch),
            empire.AutoResearch, message);
        Assert.AreEqual(flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildTerraformers),
            empire.AutoBuildTerraformers, message);
        Assert.AreEqual(flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoTaxes),
            empire.AutoTaxes, message);
        Assert.AreEqual(flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestResearchStation),
            empire.AutoPickBestResearchStation, message);
        Assert.AreEqual(flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestMiningStation),
            empire.AutoPickBestMiningStation, message);
        Assert.AreEqual(flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoExplore),
            empire.AutoExplore, message);
        Assert.AreEqual(flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoColonize),
            empire.AutoColonize, message);
        Assert.AreEqual(flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildSpaceRoads),
            empire.AutoBuildSpaceRoads, message);
        Assert.AreEqual(flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoFreighters),
            empire.AutoFreighters, message);
        Assert.AreEqual(flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildResearchStations),
            empire.AutoBuildResearchStations, message);
        Assert.AreEqual(flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildMiningStations),
            empire.AutoBuildMiningStations, message);
        Assert.AreEqual(flags.HasFlag(AuthoritativeEmpireAutomationFlags.RushAllConstruction),
            empire.RushAllConstruction, message);
        Assert.AreEqual(freighter, empire.data.CurrentAutoFreighter ?? "", message);
        Assert.AreEqual(colony, empire.data.CurrentAutoColony ?? "", message);
        Assert.AreEqual(scout, empire.data.CurrentAutoScout ?? "", message);
        Assert.AreEqual(constructor, empire.data.CurrentConstructor ?? "", message);
        Assert.AreEqual(researchStation, empire.data.CurrentResearchStation ?? "", message);
        Assert.AreEqual(miningStation, empire.data.CurrentMiningStation ?? "", message);
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

    static IShipDesign PickBuildablePlanetOrbital(Empire empire)
    {
        IShipDesign design = empire.SpaceStationsWeCanBuildSnapshot
            .Where(s => s.IsPlatformOrStation && !s.IsResearchStation && !s.IsMiningStation && !s.IsShipyard)
            .OrderBy(s => s.BaseCost)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(design,
            $"Empire {empire.Id} needs at least one buildable planet orbital for authoritative build proofs.");
        return design;
    }

    static bool HasPlanetOrbitalGoal(Empire empire, Planet planet, string designName)
        => empire.AI.Goals.Any(g => g.Type == GoalType.BuildOrbital
            && g.PlanetBuildingAt == planet
            && string.Equals(g.ToBuild?.Name, designName, StringComparison.Ordinal));

    static bool HasArea(Ship ship, Rectangle expected)
        => ship?.AreaOfOperation != null
           && ship.AreaOfOperation.Any(area => area.X == expected.X
                                               && area.Y == expected.Y
                                               && area.Width == expected.Width
                                               && area.Height == expected.Height);

    static void AssertShipOrder(Ship ship, ShipAI.Plan plan, Ship target, string message)
    {
        Assert.IsTrue(ship.AI.OrderQueue.ToArray()
                .Any(g => g.Plan == plan && g.TargetShip == target),
            message);
    }

    static void AssertShipPlan(Ship ship, ShipAI.Plan plan, string message)
    {
        Assert.IsTrue(ship.AI.OrderQueue.ToArray().Any(g => g.Plan == plan), message);
    }

    static (AuthoritativeShipTargetOrderType Order, bool Queue) ParseShipTargetOrder(
        AuthoritativePlayerCommand command)
    {
        Assert.IsTrue(AuthoritativePlayerCommand.TryParseShipTargetOrderPayload(command.Text,
            out AuthoritativeShipTargetOrderType order, out bool queue));
        return (order, queue);
    }

    static void EnableRefitYard(BuiltWorld world)
    {
        world.Planet.HasSpacePort = true;
        world.Player.UpdateRallyPoints();
    }

    static IShipDesign PickRefitTarget(Ship ship)
    {
        IShipDesign design = ship.Loyalty.ShipsWeCanBuildSnapshot
            .Where(d => ship.Loyalty.CanBuildShip(d)
                        && (d.Hull == ship.ShipData.Hull || ship.IsResearchStation || ship.IsMiningStation)
                        && !string.Equals(d.Name, ship.ShipData.Name, StringComparison.Ordinal)
                        && !d.ShipRole.Protected
                        && ship.IsResearchStation == d.IsResearchStation
                        && ship.IsMiningStation == d.IsMiningStation)
            .OrderBy(d => d.BaseCost)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(design,
            $"Ship '{ship.Name}' needs at least one buildable same-hull refit target for the authoritative refit proof.");
        return design;
    }

    static RefitShip FindRefitGoal(Empire empire, Ship ship)
        => empire.AI.Goals.OfType<RefitShip>().FirstOrDefault(g => g.OldShip == ship);

    static Troop PickBuildableTroop(Empire empire)
    {
        Troop troop = ResourceManager.GetTroopTemplatesFor(empire)
            .OrderBy(t => t.ActualCost(empire))
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(troop, $"Empire {empire.Id} needs at least one buildable troop for the authoritative queue proof.");
        return troop;
    }

    static void EnsureTroopLoaded(Ship ship)
    {
        Assert.IsNotNull(ship, "Expected a troop ship fixture.");
        Assert.AreEqual(RoleName.troop, ship.DesignRole, $"Ship '{ship.Name}' should be a single troop ship fixture.");
        if (ship.TroopCount > 0)
            return;

        Troop template = PickBuildableTroop(ship.Loyalty);
        Assert.IsTrue(ResourceManager.TryCreateTroop(template.Name, ship.Loyalty, out Troop troop),
            $"Could not create troop '{template.Name}' for ship '{ship.Name}'.");
        ship.AddTroop(troop);
        Assert.IsTrue(ship.TroopCount > 0, $"Ship '{ship.Name}' should have a loaded troop.");
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

    static PlanetGridSquare[] PrepareGroundTroopTiles(Planet planet, int columns, int rows)
    {
        planet.TilesList.Clear();
        int width = SolarSystemBody.TileMaxX;
        int height = SolarSystemBody.TileMaxY;
        Assert.IsTrue(columns <= width && rows <= height,
            $"Requested {columns}x{rows} ground-test tiles exceeds the engine {width}x{height} tile grid.");
        var tiles = new List<PlanetGridSquare>(width * height);
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                var tile = new PlanetGridSquare(planet, x, y, b: null, hab: true, terraformable: false);
                planet.TilesList.Add(tile);
                tiles.Add(tile);
            }
        }
        planet.RefreshBuildingsWeCanBuildHere();
        return tiles.ToArray();
    }

    static Troop PlaceGroundTroop(Planet planet, Empire empire, PlanetGridSquare tile)
    {
        Troop template = PickBuildableTroop(empire);
        Assert.IsTrue(ResourceManager.TryCreateTroop(template.Name, empire, out Troop troop),
            $"Could not create troop '{template.Name}' for empire {empire.Id}.");
        Assert.IsTrue(troop.TryLandTroop(planet, tile),
            $"Could not place troop '{troop.Name}' at planet {planet.Id} tile {tile.X},{tile.Y}.");
        troop.AvailableMoveActions = troop.MaxStoredActions;
        troop.AvailableAttackActions = troop.MaxStoredActions;
        troop.MoveTimer = 0f;
        troop.AttackTimer = 0f;
        Assert.IsTrue(tile.TroopsHere.ContainsRef(troop),
            $"Troop '{troop.Name}' should occupy requested planet {planet.Id} tile {tile.X},{tile.Y}.");
        return troop;
    }

    static Building PlaceCombatBuilding(Planet planet, PlanetGridSquare tile)
    {
        Building template = ResourceManager.BuildingsDict.FilterValues(b => b.CombatStrength > 0)
            .OrderBy(b => b.ActualCost(planet.Owner))
            .ThenBy(b => b.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.IsNotNull(template, "Ground combat proofs need at least one attackable building template.");
        Building building = ResourceManager.CreateBuilding(planet, template.Name);
        building.AvailableAttackActions = 1;
        tile.PlaceBuilding(building, planet);
        Assert.IsTrue(tile.CombatBuildingOnTile, $"Tile {tile.X},{tile.Y} should contain an attackable building.");
        Assert.IsTrue(tile.Building.CanAttack, $"Building '{tile.Building.Name}' should be ready to attack.");
        return building;
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
        bool includeNeutralPlanet = false, bool includeColonyShip = false, bool includeFreighter = false,
        bool includeCarrierPolicyShips = false, bool includeTroopShips = false)
    {
        if (includePlatform)
            LoadStarterShips("Platform Base mk1-a");
        if (includeCarrierPolicyShips || includeTroopShips)
        {
            LoadStarterShips("Heavy Carrier mk5-b",
                             "Alliance-Class Mk Ia Hvy Assault",
                             "Assault Shuttle",
                             "Terran Assault Shuttle");
        }

        CreateUniverseAndPlayerEmpire();
        UState.Events.Disabled = true;
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
        Ship colonyShip = includeColonyShip
            ? SpawnShip(PickBuildableColonyShip(Player).Name, Player, new Vector2(12_000, 4_000))
            : null;
        Ship freighterShip = includeFreighter
            ? SpawnShip(PickBuildableAutomationDesign(Player, s => s.IsFreighter, "freighter").Name,
                Player, new Vector2(14_000, 4_000))
            : null;
        Ship carrierShip = includeCarrierPolicyShips
            ? SpawnShip(PickLoadedDesign(d => d.AllFighterHangars != null && d.AllFighterHangars.Length > 0,
                    "fighter-carrier").Name,
                Player, new Vector2(16_000, 4_000))
            : null;
        Ship troopCarrierShip = includeCarrierPolicyShips
            ? SpawnShip(PickLoadedDesign(d => d.Hangars != null && d.Hangars.Any(h => h.IsTroopBay),
                    "troop-carrier").Name,
                Player, new Vector2(18_000, 4_000))
            : null;
        Ship troopShip = includeTroopShips
            ? SpawnShip("Assault Shuttle", Player, new Vector2(20_000, 4_000))
            : null;
        Ship friendlyTroopTargetShip = includeTroopShips
            ? SpawnShip("Assault Shuttle", Player, new Vector2(22_000, 4_000))
            : null;
        if (includeCarrierPolicyShips)
        {
            Assert.IsTrue(carrierShip.Carrier.HasFighterBays,
                $"Spawned carrier fixture '{carrierShip.Name}' must have fighter hangars.");
            Assert.IsTrue(troopCarrierShip.Carrier.HasTroopBays,
                $"Spawned troop-carrier fixture '{troopCarrierShip.Name}' must have troop/assault bays.");
        }
        if (includeTroopShips)
        {
            Assert.AreEqual(RoleName.troop, troopShip.DesignRole,
                $"Spawned troop fixture '{troopShip.Name}' must be a single troop ship.");
            Assert.AreEqual(RoleName.troop, friendlyTroopTargetShip.DesignRole,
                $"Spawned troop target fixture '{friendlyTroopTargetShip.Name}' must have troop capacity.");
            Assert.IsTrue(friendlyTroopTargetShip.TroopCapacity > friendlyTroopTargetShip.TroopCount,
                $"Spawned troop target fixture '{friendlyTroopTargetShip.Name}' must have spare troop capacity.");
        }
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
            ColonyShip = colonyShip,
            FreighterShip = freighterShip,
            CarrierShip = carrierShip,
            TroopCarrierShip = troopCarrierShip,
            TroopShip = troopShip,
            FriendlyTroopTargetShip = friendlyTroopTargetShip,
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

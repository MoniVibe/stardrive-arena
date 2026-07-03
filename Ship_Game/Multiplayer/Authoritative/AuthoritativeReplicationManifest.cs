using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ship_Game.Gameplay;
using Ship_Game.Universe;

namespace Ship_Game.Multiplayer.Authoritative;

public enum AuthoritativeReplicationApplyMode
{
    DirectReplay,
    BatchReplay,
    DigestOnly,
}

public enum AuthoritativeReplicationDigestPolicy
{
    Fatal,
    Transform,
    HostOnlyDiagnostic,
}

public enum AuthoritativeReplicationEmitStage
{
    None,
    PreScoped,
    PerEmpire,
    PerPlanet,
    PostScoped,
}

public enum AuthoritativeReplicationApplyStage
{
    None,
    UnlockedTech,
    PlayerDesign,
    ShipPresence,
    ShipRuntime,
    ShipTroop,
    ColonyTile,
    GroundTroop,
    GroundCombat,
    FleetRuntime,
    ConstructionQueue,
    ColonizationGoal,
    DeepSpaceGoal,
}

internal delegate void AuthoritativeReplicationEmit(UniverseState universe, uint tick, StringBuilder payload);

internal delegate void AuthoritativeReplicationEmpireEmit(Empire empire, uint tick, StringBuilder payload);

internal delegate void AuthoritativeReplicationPlanetEmit(Planet planet, uint tick, StringBuilder payload);

internal delegate void AuthoritativeReplicationLineApply(UniverseState universe, string line);

internal delegate void AuthoritativeReplicationApply(UniverseState universe, string[] lines);

public sealed class ReplicatedRowDescriptor
{
    public readonly string Prefix;
    public readonly string Id;
    public readonly string Owner;
    public readonly string FieldGroup;
    public readonly AuthoritativeReplicationApplyMode ApplyMode;
    public readonly AuthoritativeReplicationDigestPolicy DigestPolicy;
    public readonly AuthoritativeReplicationEmitStage EmitStage;
    public readonly AuthoritativeReplicationApplyStage ApplyStage;
    public readonly bool KnownGap;
    public readonly string P0Mapping;
    public readonly string Notes;
    public readonly string[] PayloadFields;
    public readonly string[] AppliedFields;
    public readonly string Variant;
    internal readonly AuthoritativeReplicationEmit Emit;
    internal readonly AuthoritativeReplicationEmpireEmit EmitEmpire;
    internal readonly AuthoritativeReplicationPlanetEmit EmitPlanet;
    internal readonly AuthoritativeReplicationLineApply InitialApplyLine;
    internal readonly AuthoritativeReplicationApply Apply;

    internal ReplicatedRowDescriptor(string id, string prefix, string owner, string fieldGroup,
        AuthoritativeReplicationApplyMode applyMode, AuthoritativeReplicationDigestPolicy digestPolicy,
        AuthoritativeReplicationEmit emit, AuthoritativeReplicationApply apply,
        string[] payloadFields, string[] appliedFields, string notes,
        bool knownGap = false, string p0Mapping = "", string variant = "",
        AuthoritativeReplicationEmitStage emitStage = AuthoritativeReplicationEmitStage.None,
        AuthoritativeReplicationEmpireEmit emitEmpire = null,
        AuthoritativeReplicationPlanetEmit emitPlanet = null,
        AuthoritativeReplicationLineApply initialApplyLine = null,
        AuthoritativeReplicationApplyStage applyStage = AuthoritativeReplicationApplyStage.None)
    {
        Id = id;
        Prefix = prefix;
        Owner = owner;
        FieldGroup = fieldGroup;
        ApplyMode = applyMode;
        DigestPolicy = digestPolicy;
        EmitStage = emitStage;
        ApplyStage = applyStage;
        Emit = emit;
        EmitEmpire = emitEmpire;
        EmitPlanet = emitPlanet;
        InitialApplyLine = initialApplyLine;
        Apply = apply;
        PayloadFields = payloadFields ?? Array.Empty<string>();
        AppliedFields = appliedFields ?? Array.Empty<string>();
        KnownGap = knownGap;
        P0Mapping = p0Mapping ?? "";
        Variant = variant ?? "";
        Notes = notes;
    }

    public bool EmitsPayload => Emit != null || EmitEmpire != null || EmitPlanet != null;

    public bool HasApply => InitialApplyLine != null || Apply != null;

    public bool MatchesLine(string line)
    {
        if (string.IsNullOrEmpty(line) || !line.StartsWith(Prefix + "|", StringComparison.Ordinal))
            return false;
        if (string.IsNullOrEmpty(Variant))
            return true;

        string[] p = line.Split('|');
        return p.Length > 2 && string.Equals(p[2], Variant, StringComparison.Ordinal);
    }
}

public static class AuthoritativeReplicationManifest
{
    static readonly ReplicatedRowDescriptor[] Rows =
    {
        new("V.UniversePreferences", "V", "UniversePreferences", "PreferenceFlags",
            AuthoritativeReplicationApplyMode.DirectReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: AuthoritativeStateSnapshot.EmitUniversePreferenceRows, apply: null,
            Fields("PreferenceFlags", "AllowPlayerInterTrade", "PrioritizeProjectors"),
            Fields("PreferenceFlags", "AllowPlayerInterTrade", "PrioritizeProjectors"),
            "Host-owned global multiplayer preference flags.",
            emitStage: AuthoritativeReplicationEmitStage.PreScoped,
            initialApplyLine: AuthoritativeStateSnapshot.ApplyUniversePreferenceLine),
        new("SD.StarDate", "SD", "StarDate", "StarDate",
            AuthoritativeReplicationApplyMode.DirectReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: AuthoritativeStateSnapshot.EmitStarDateRows, apply: null, Fields("StarDate"), Fields("StarDate"),
            "Host-owned stardate for passive clients, which do not advance simulation time locally.",
            emitStage: AuthoritativeReplicationEmitStage.PreScoped,
            initialApplyLine: AuthoritativeStateSnapshot.ApplyStarDateLine),
        new("E.EmpireRuntime", "E", "EmpireRuntime", "EconomyResearchAutomation",
            AuthoritativeReplicationApplyMode.DirectReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: AuthoritativeStateSnapshot.EmitEmpireRuntimeRows, apply: null,
            Fields("EmpireId", "Research.Topic", "ResearchQueueSignature", "Money", "TaxRate",
                "TreasuryGoal", "AutoTaxes", "AutomationFlags", "CurrentAutoFreighter",
                "CurrentAutoColony", "CurrentAutoScout", "CurrentConstructor",
                "CurrentResearchStation", "CurrentMiningStation", "ResearchProgress"),
            Fields("EmpireId", "Research.Topic", "ResearchQueueSignature", "Money", "TaxRate",
                "TreasuryGoal", "AutomationFlags", "CurrentAutoFreighter", "CurrentAutoColony",
                "CurrentAutoScout", "CurrentConstructor", "CurrentResearchStation",
                "CurrentMiningStation", "ResearchProgress"),
            "Cash, research queue/progress, taxes, automation flags, and auto-design selections.",
            emitStage: AuthoritativeReplicationEmitStage.PreScoped,
            initialApplyLine: AuthoritativeStateSnapshot.ApplyEmpireRuntimeLine),
        new("U.UnlockedTech", "U", "UnlockedTech", "ExactUnlockedSet",
            AuthoritativeReplicationApplyMode.BatchReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: AuthoritativeStateSnapshot.EmitUnlockedTechRows,
            apply: AuthoritativeStateSnapshot.ReplayUnlockedTechPayload,
            Fields("EmpireId", "TechUid", "TechLevel"),
            Fields("EmpireId", "TechUid", "TechLevel", "ExactUnlockedSet"),
            "Unlocked empire technology rows are replayed as an exact host-owned set.",
            emitStage: AuthoritativeReplicationEmitStage.PreScoped,
            applyStage: AuthoritativeReplicationApplyStage.UnlockedTech),
        new("D.PlayerDesignRegistration", "D", "PlayerDesign", "Registration",
            AuthoritativeReplicationApplyMode.DirectReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: AuthoritativeStateSnapshot.EmitPlayerDesignRows,
            apply: AuthoritativeStateSnapshot.ReplayPlayerDesignPayload,
            Fields("EmpireId", "DesignName", "DesignBase64"),
            Fields("EmpireId", "DesignName", "DesignBase64"),
            "Player ship designs registered for an empire.",
            emitStage: AuthoritativeReplicationEmitStage.PreScoped,
            applyStage: AuthoritativeReplicationApplyStage.PlayerDesign),
        new("D.DescriptiveFields", "D", "PlayerDesign", "DescriptiveFields",
            AuthoritativeReplicationApplyMode.DigestOnly, AuthoritativeReplicationDigestPolicy.HostOnlyDiagnostic,
            emit: null, apply: null,
            Fields("Hull", "Role", "BaseCost", "DesignSlotSignature"),
            Array.Empty<string>(),
            "Descriptive design fields are host-only diagnostics; fatal design compare narrows to registered name/base64."),
        new("R.Relationship", "R", "Relationship", "DiplomacyTreaties",
            AuthoritativeReplicationApplyMode.DirectReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: AuthoritativeStateSnapshot.EmitRelationshipRows, apply: null,
            Fields("EmpireId", "TargetEmpireId", "Known", "AtWar", "Treaty_NAPact",
                "Treaty_Trade", "Treaty_OpenBorders", "Treaty_Alliance", "Treaty_Peace"),
            Fields("EmpireId", "TargetEmpireId", "Known", "AtWar", "Treaty_NAPact",
                "Treaty_Trade", "Treaty_OpenBorders", "Treaty_Alliance", "Treaty_Peace",
                "CanAttack", "IsHostile", "ActiveWar"),
            "Diplomacy and treaty state.",
            emitStage: AuthoritativeReplicationEmitStage.PreScoped,
            initialApplyLine: AuthoritativeStateSnapshot.ApplyRelationshipLine),
        new("G.MarkForColonization", "G", "EmpireGoal", "MarkForColonization",
            AuthoritativeReplicationApplyMode.BatchReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: null, apply: AuthoritativeStateSnapshot.ReplayColonizationGoalPayload,
            Fields("EmpireId", "GoalKind", "TargetPlanetId", "IsManual", "FinishedShipId"),
            Fields("EmpireId", "GoalKind", "TargetPlanetId", "IsManual", "FinishedShipId", "ExactGoalSet"),
            "MarkForColonization goals are replayed as an exact host-owned set.",
            variant: "MarkForColonization", emitStage: AuthoritativeReplicationEmitStage.PerEmpire,
            emitEmpire: AuthoritativeStateSnapshot.EmitMarkForColonizationRows,
            applyStage: AuthoritativeReplicationApplyStage.ColonizationGoal),
        new("G.Refit", "G", "EmpireGoal", "Refit",
            AuthoritativeReplicationApplyMode.DigestOnly, AuthoritativeReplicationDigestPolicy.HostOnlyDiagnostic,
            emit: null, apply: null,
            Fields("EmpireId", "GoalKind", "Step", "OldShipId", "ToBuildName",
                "PlanetBuildingAtId", "Rush", "FleetId", "FleetKey"),
            Array.Empty<string>(),
            "Refit goals are host-only diagnostics and do not participate in client fatal digest repair.",
            variant: "Refit",
            emitStage: AuthoritativeReplicationEmitStage.PerEmpire,
            emitEmpire: AuthoritativeStateSnapshot.EmitRefitGoalRows),
        new("G.FleetRequisition", "G", "EmpireGoal", "FleetRequisition",
            AuthoritativeReplicationApplyMode.DigestOnly, AuthoritativeReplicationDigestPolicy.HostOnlyDiagnostic,
            emit: null, apply: null,
            Fields("EmpireId", "GoalKind", "Step", "FleetId", "FleetKey", "NodeIndex",
                "TemplateName", "PlanetBuildingAtId", "Rush"),
            Array.Empty<string>(),
            "Fleet requisition goals are host-only diagnostics and do not participate in client fatal digest repair.",
            variant: "FleetRequisition", emitStage: AuthoritativeReplicationEmitStage.PerEmpire,
            emitEmpire: AuthoritativeStateSnapshot.EmitFleetRequisitionRows),
        new("G.DeepSpace", "G", "EmpireGoal", "DeepSpace",
            AuthoritativeReplicationApplyMode.BatchReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: null, apply: AuthoritativeStateSnapshot.ReplayDeepSpaceGoalPayload,
            Fields("EmpireId", "GoalKind", "GoalType", "Step", "ToBuildName",
                "TargetPlanetId", "TargetSystemId", "TargetShipId", "PlanetBuildingAtId",
                "BuildPosition.X", "BuildPosition.Y", "MovePosition.X", "MovePosition.Y"),
            Fields("EmpireId", "GoalKind", "GoalType", "Step", "ToBuildName",
                "TargetPlanetId", "TargetSystemId", "TargetShipId", "PlanetBuildingAtId",
                "BuildPosition.X", "BuildPosition.Y", "MovePosition.X", "MovePosition.Y",
                "ExactGoalSet"),
            "Deep-space build state, including passive MovePosition, is replayed as an exact host-owned set.",
            variant: "DeepSpace", emitStage: AuthoritativeReplicationEmitStage.PerEmpire,
            emitEmpire: AuthoritativeStateSnapshot.EmitDeepSpaceGoalRows,
            applyStage: AuthoritativeReplicationApplyStage.DeepSpaceGoal),
        new("G.DeepSpaceMovePosition", "G", "EmpireGoal", "DeepSpace.MovePosition",
            AuthoritativeReplicationApplyMode.BatchReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: null, apply: AuthoritativeStateSnapshot.ReplayDeepSpaceGoalPayload,
            Fields("MovePosition.X", "MovePosition.Y"),
            Fields("MovePosition.X", "MovePosition.Y"),
            "DeepSpace MovePosition is carried through DeepSpaceGoalRuntime and applied to passive replay goals.",
            variant: "DeepSpace"),
        new("FP.FleetPatrol", "FP", "FleetPatrol", "PlanSignature",
            AuthoritativeReplicationApplyMode.DigestOnly, AuthoritativeReplicationDigestPolicy.HostOnlyDiagnostic,
            emit: null, apply: null,
            Fields("EmpireId", "FleetPatrolPlanSignature"), Array.Empty<string>(),
            "Fleet patrol definitions are host-only diagnostics and do not participate in client fatal digest repair.",
            emitStage: AuthoritativeReplicationEmitStage.PerEmpire,
            emitEmpire: AuthoritativeStateSnapshot.EmitFleetPatrolRows),
        new("F.Runtime", "F", "FleetRuntime", "RuntimeReplay",
            AuthoritativeReplicationApplyMode.DirectReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: null, apply: AuthoritativeStateSnapshot.ReplayFleetRuntimePayload,
            Fields("EmpireId", "FleetId", "FleetKey", "Name", "FleetIconIndex",
                "CommandShipId", "FinalPosition.X", "FinalPosition.Y", "FinalDirection.X",
                "FinalDirection.Y"),
            Fields("EmpireId", "FleetId", "FleetKey", "Name", "FleetIconIndex",
                "CommandShipId", "CommandShipMembership", "FinalPosition.X",
                "FinalPosition.Y", "FinalDirection.X", "FinalDirection.Y"),
            "Fleet command ship, final vector, icon, and name are replayed.",
            emitStage: AuthoritativeReplicationEmitStage.PerEmpire,
            emitEmpire: AuthoritativeStateSnapshot.EmitFleetRuntimeRows,
            applyStage: AuthoritativeReplicationApplyStage.FleetRuntime),
        new("F.Signatures", "F", "FleetRuntime", "MembershipLayoutPatrolSignatures",
            AuthoritativeReplicationApplyMode.DigestOnly, AuthoritativeReplicationDigestPolicy.HostOnlyDiagnostic,
            emit: null, apply: null,
            Fields("FleetShipSignature", "FleetNodeSignature", "FleetPatrolSignature"),
            Array.Empty<string>(),
            "Fleet membership/layout/patrol signatures are host-only diagnostics; fatal fleet compare narrows to replayed runtime columns."),
        new("P.PlanetRuntime", "P", "PlanetRuntime", "ColonyRuntime",
            AuthoritativeReplicationApplyMode.DirectReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: null, apply: null,
            Fields("PlanetId", "OwnerId", "ColonyType", "GarrisonSize", "WantedPlatforms",
                "WantedShipyards", "WantedStations", "Food.Percent", "Prod.Percent",
                "Res.Percent", "Food.PercentLock", "Prod.PercentLock", "Res.PercentLock",
                "FoodState", "ProdState", "ManualFoodImportSlots", "ManualProdImportSlots",
                "ManualColoImportSlots", "ManualFoodExportSlots", "ManualProdExportSlots",
                "ManualColoExportSlots", "PrioritizedPort", "GovOrbitals", "AutoBuildTroops",
                "DontScrapBuildings", "Quarantine", "ManualOrbitals", "GovGroundDefense",
                "SpecializedTradeHub", "ManualCivilianBudget", "ManualGroundDefBudget",
                "ManualSpaceDefBudget", "ConstructionQueue.Count"),
            Fields("PlanetId", "OwnerId", "ColonyType", "GarrisonSize", "WantedPlatforms",
                "WantedShipyards", "WantedStations", "Food.Percent", "Prod.Percent",
                "Res.Percent", "Food.PercentLock", "Prod.PercentLock", "Res.PercentLock",
                "FoodState", "ProdState", "ManualFoodImportSlots", "ManualProdImportSlots",
                "ManualColoImportSlots", "ManualFoodExportSlots", "ManualProdExportSlots",
                "ManualColoExportSlots", "PrioritizedPort", "GovOrbitals", "AutoBuildTroops",
                "DontScrapBuildings", "Quarantine", "ManualOrbitals", "GovGroundDefense",
                "SpecializedTradeHub", "ManualCivilianBudget", "ManualGroundDefBudget",
                "ManualSpaceDefBudget", "ConstructionQueue.Count"),
            "Colony type, labor, import/export, governor, budgets, and queue count.",
            emitStage: AuthoritativeReplicationEmitStage.PerPlanet,
            emitPlanet: AuthoritativeStateSnapshot.EmitPlanetRuntimeRows,
            initialApplyLine: AuthoritativeStateSnapshot.ApplyPlanetRuntimeLine),
        new("PX.PlanetTransform", "PX", "PlanetTransform", "Transform",
            AuthoritativeReplicationApplyMode.DirectReplay, AuthoritativeReplicationDigestPolicy.Transform,
            emit: null, apply: null,
            Fields("PlanetId", "Tick", "OrbitalAngle", "Position.X", "Position.Y"),
            Fields("PlanetId", "OrbitalAngle", "Position.X", "Position.Y"),
            "Host-authored planet orbital presentation for passive clients; excluded from the fatal SyncDigest and covered by TransformDigest.",
            emitStage: AuthoritativeReplicationEmitStage.PerPlanet,
            emitPlanet: AuthoritativeStateSnapshot.EmitPlanetTransformRows,
            initialApplyLine: AuthoritativeStateSnapshot.ApplyPlanetTransformLine),
        new("BP.Blueprint", "BP", "Blueprint", "BlueprintSignature",
            AuthoritativeReplicationApplyMode.DigestOnly, AuthoritativeReplicationDigestPolicy.HostOnlyDiagnostic,
            emit: null, apply: null,
            Fields("PlanetId", "BlueprintSignature"), Array.Empty<string>(),
            "Colony blueprint signatures are host-only diagnostics and do not participate in client fatal digest repair.",
            emitStage: AuthoritativeReplicationEmitStage.PerPlanet,
            emitPlanet: AuthoritativeStateSnapshot.EmitBlueprintRows),
        new("T.ColonyTile", "T", "ColonyTile", "ExactTileSet",
            AuthoritativeReplicationApplyMode.BatchReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: null, apply: AuthoritativeStateSnapshot.ReplayColonyTilePayload,
            Fields("PlanetId", "TileX", "TileY", "BuildingName", "Biosphere",
                "Habitable", "Terraformable", "BuildingStrength", "BuildingCombatStrength"),
            Fields("PlanetId", "TileX", "TileY", "BuildingName", "Biosphere",
                "Habitable", "Terraformable", "BuildingStrength", "BuildingCombatStrength", "ExactTileSet"),
            "Tile buildings/biospheres and building HP are replayed as an exact host-owned set before digest comparison.",
            emitStage: AuthoritativeReplicationEmitStage.PerPlanet,
            emitPlanet: AuthoritativeStateSnapshot.EmitColonyTileRows,
            applyStage: AuthoritativeReplicationApplyStage.ColonyTile),
        new("GT.GroundTroop", "GT", "GroundTroop", "ExactGroundTroopSet",
            AuthoritativeReplicationApplyMode.BatchReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: null, apply: AuthoritativeStateSnapshot.ReplayGroundTroopPayload,
            Fields("PlanetId", "TileX", "TileY", "TroopIndex", "LoyaltyId", "TroopName",
                "Strength", "AvailableMoveActions", "AvailableAttackActions", "MoveTimer", "AttackTimer"),
            Fields("PlanetId", "TileX", "TileY", "TroopIndex", "LoyaltyId", "TroopName",
                "Strength", "AvailableMoveActions", "AvailableAttackActions", "MoveTimer",
                "AttackTimer", "ExactGroundTroopSet"),
            "Planet troop tile membership, owner, action counters, strength, and timers are replayed from the host.",
            emitStage: AuthoritativeReplicationEmitStage.PerPlanet,
            emitPlanet: AuthoritativeStateSnapshot.EmitGroundTroopRows,
            initialApplyLine: AuthoritativeStateSnapshot.ApplyGroundTroopLine,
            applyStage: AuthoritativeReplicationApplyStage.GroundTroop),
        new("GC.GroundCombat", "GC", "GroundCombat", "Runtime",
            AuthoritativeReplicationApplyMode.DirectReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: null, apply: AuthoritativeStateSnapshot.ReplayGroundCombatPayload,
            Fields("PlanetId", "CombatIndex", "Phase", "Timer", "AttackerLoyaltyId",
                "DefenseTile.X", "DefenseTile.Y", "AttackingTroopRef", "DefendingTroopRef",
                "AttackingBuildingRef", "DefendingBuildingRef"),
            Fields("PlanetId", "CombatIndex", "Phase", "Timer", "AttackerLoyaltyId",
                "DefenseTile.X", "DefenseTile.Y", "AttackingTroopRef", "DefendingTroopRef",
                "AttackingBuildingRef", "DefendingBuildingRef"),
            "Ground-combat phase, timer, participants, and defense tile are replayed from the host.",
            emitStage: AuthoritativeReplicationEmitStage.PerPlanet,
            emitPlanet: AuthoritativeStateSnapshot.EmitGroundCombatRows,
            applyStage: AuthoritativeReplicationApplyStage.GroundCombat),
        new("Q.ConstructionQueue", "Q", "ConstructionQueue", "ExactQueueSet",
            AuthoritativeReplicationApplyMode.BatchReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: null, apply: AuthoritativeStateSnapshot.ReplayConstructionQueuePayload,
            Fields("PlanetId", "QueueIndex", "IsShip", "IsBuilding", "IsTroop",
                "QueueType", "ShipName", "BuildingName", "TroopType", "TileX", "TileY",
                "Cost", "ProductionSpent", "Rush", "Canceled"),
            Fields("PlanetId", "QueueIndex", "IsShip", "IsBuilding", "IsTroop",
                "QueueType", "ShipName", "BuildingName", "TroopType", "TileX", "TileY",
                "Cost", "ProductionSpent", "Rush", "Canceled", "ExactQueueSet"),
            "Planet construction queue shape and runtime progress.",
            emitStage: AuthoritativeReplicationEmitStage.PerPlanet,
            emitPlanet: AuthoritativeStateSnapshot.EmitConstructionQueueRows,
            applyStage: AuthoritativeReplicationApplyStage.ConstructionQueue),
        new("SC.ShipPresence", "SC", "ShipPresence", "ExactActiveShipSet",
            AuthoritativeReplicationApplyMode.DirectReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: AuthoritativeStateSnapshot.EmitShipPresenceRows,
            apply: AuthoritativeStateSnapshot.ReplayShipPresencePayload,
            Fields("ShipId", "LoyaltyId", "DesignName"),
            Fields("ShipId", "LoyaltyId", "DesignName", "ExactActiveShipSet"),
            "Host-authored ship identity, owner, design, and initial materialization position.",
            emitStage: AuthoritativeReplicationEmitStage.PostScoped,
            applyStage: AuthoritativeReplicationApplyStage.ShipPresence),
        new("S.RuntimeReplay", "S", "ShipRuntime", "RuntimeReplay",
            AuthoritativeReplicationApplyMode.DirectReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: AuthoritativeStateSnapshot.EmitShipRuntimeRows,
            apply: AuthoritativeStateSnapshot.ReplayShipRuntimePayload,
            Fields("ShipId", "FleetId", "FleetKey", "AIState", "CombatState",
                "ScuttleTimer", "TargetShipId", "HasPriorityTarget", "TargetQueueSignature",
                "EscortTargetId", "ShipOrderQueueSignature"),
            Fields("ShipId", "FleetId", "FleetKey", "AIState", "CombatState",
                "ScuttleTimer", "TargetShipId", "HasPriorityTarget", "TargetQueueSignature",
                "EscortTargetId", "ShipOrderQueueSignature"),
            "Ship AI state, targets, and durable orders are replayed.",
            emitStage: AuthoritativeReplicationEmitStage.PostScoped,
            initialApplyLine: AuthoritativeStateSnapshot.ApplyShipRuntimeLine,
            applyStage: AuthoritativeReplicationApplyStage.ShipRuntime),
        new("S.PolicyFields", "S", "ShipRuntime", "OwnerTradeCarrierPolicy",
            AuthoritativeReplicationApplyMode.DirectReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: null, apply: AuthoritativeStateSnapshot.ReplayShipRuntimePayload,
            Fields("LoyaltyId", "TransportingFood", "TransportingProduction",
                "TransportingColonists", "AllowInterEmpireTrade", "FightersOut", "TroopsOut",
                "RecallFightersBeforeFTL", "SendTroopsToShip", "AllowBoardShip",
                "ManualHangarOverride", "TradeRouteSignature", "AreaOfOperationSignature"),
            Fields("LoyaltyId", "TransportingFood", "TransportingProduction",
                "TransportingColonists", "AllowInterEmpireTrade", "FightersOut", "TroopsOut",
                "RecallFightersBeforeFTL", "SendTroopsToShip", "AllowBoardShip",
                "ManualHangarOverride", "TradeRouteSignature", "AreaOfOperationSignature"),
            "Ship owner, freighter trade policy, carrier policy, manual hangar override, trade routes, and area-of-operation are replayed from S rows."),
        new("S.PolicyDiagnostics", "S", "ShipRuntime", "PolicyCapabilities",
            AuthoritativeReplicationApplyMode.DigestOnly, AuthoritativeReplicationDigestPolicy.HostOnlyDiagnostic,
            emit: null, apply: null,
            Fields("IsFreighter", "HasFighterBays", "HasTroopBays"),
            Array.Empty<string>(),
            "Ship policy capability columns are design-derived diagnostics; fatal S compare narrows to replayed runtime and policy columns."),
        new("SX.ShipTransform", "SX", "ShipTransform", "Transform",
            AuthoritativeReplicationApplyMode.DirectReplay, AuthoritativeReplicationDigestPolicy.Transform,
            emit: AuthoritativeStateSnapshot.EmitShipTransformRows, apply: null,
            Fields("ShipId", "Tick", "Position.X", "Position.Y", "Velocity.X",
                "Velocity.Y", "Rotation", "SystemId", "Active", "Dying", "YRotation", "XRotation"),
            Fields("ShipId", "Position.X", "Position.Y", "Velocity.X", "Velocity.Y",
                "Rotation", "SystemId", "Active", "Dying", "YRotation", "XRotation"),
            "Host-authored ship position, velocity, rotation, active/dying flags, and containing system for passive client presentation.",
            emitStage: AuthoritativeReplicationEmitStage.PostScoped,
            initialApplyLine: AuthoritativeStateSnapshot.ApplyShipTransformLine),
        new("SV.ShipVisibility", "SV", "ShipVisibility", "KnownByMask",
            AuthoritativeReplicationApplyMode.DirectReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: AuthoritativeStateSnapshot.EmitShipVisibilityRows, apply: null,
            Fields("ShipId", "KnownByMask"), Fields("ShipId", "KnownByMask"),
            "Host-authored per-empire known-by bitmask for passive clients, which do not run local sensor/contact simulation.",
            emitStage: AuthoritativeReplicationEmitStage.PostScoped,
            initialApplyLine: AuthoritativeStateSnapshot.ApplyShipVisibilityLine),
        new("ST.ShipTroop", "ST", "ShipTroop", "ExactShipTroopSet",
            AuthoritativeReplicationApplyMode.BatchReplay, AuthoritativeReplicationDigestPolicy.Fatal,
            emit: AuthoritativeStateSnapshot.EmitShipTroopRows,
            apply: AuthoritativeStateSnapshot.ReplayShipTroopPayload,
            Fields("ShipId", "TroopIndex", "LoyaltyId", "TroopName", "Strength",
                "AvailableMoveActions", "AvailableAttackActions", "MoveTimer", "AttackTimer"),
            Fields("ShipId", "TroopIndex", "LoyaltyId", "TroopName", "Strength",
                "AvailableMoveActions", "AvailableAttackActions", "MoveTimer", "AttackTimer",
                "ExactShipTroopSet"),
            "Ship-carried troop membership, owner, action counters, strength, and timers are replayed as an exact host-owned set.",
            emitStage: AuthoritativeReplicationEmitStage.PostScoped,
            applyStage: AuthoritativeReplicationApplyStage.ShipTroop),
    };

    static readonly Dictionary<string, ReplicatedRowDescriptor[]> ByPrefix =
        Rows.GroupBy(row => row.Prefix, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

    public static IReadOnlyList<ReplicatedRowDescriptor> AllRows => Rows;

    public static bool TryGetRow(string prefix, out ReplicatedRowDescriptor row)
    {
        row = null;
        if (!ByPrefix.TryGetValue(prefix ?? "", out ReplicatedRowDescriptor[] rows) || rows.Length == 0)
            return false;
        row = rows[0];
        return true;
    }

    public static ReplicatedRowDescriptor DescriptorForLine(string line)
    {
        string prefix = PrefixForLine(line);
        if (string.IsNullOrEmpty(prefix) || !ByPrefix.TryGetValue(prefix, out ReplicatedRowDescriptor[] rows))
            return null;
        return rows.FirstOrDefault(row => row.MatchesLine(line)) ?? rows[0];
    }

    public static AuthoritativeReplicationDigestPolicy DigestPolicyForLine(string line)
    {
        ReplicatedRowDescriptor descriptor = DescriptorForLine(line);
        return descriptor?.DigestPolicy ?? AuthoritativeReplicationDigestPolicy.Fatal;
    }

    public static string PrefixForLine(string line)
    {
        if (string.IsNullOrEmpty(line) || string.Equals(line, "<missing>", StringComparison.Ordinal))
            return "";
        int pipe = line.IndexOf('|');
        return pipe > 0 ? line.Substring(0, pipe) : line;
    }

    public static string[] UnknownPrefixesForPayload(string payload)
        => (payload ?? "").Split('\n')
            .Select(line => PrefixForLine(line.TrimEnd('\r')))
            .Where(prefix => !string.IsNullOrEmpty(prefix) && !ByPrefix.ContainsKey(prefix))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(prefix => prefix, StringComparer.Ordinal)
            .ToArray();

    public static string DescribeLine(string line)
    {
        string prefix = PrefixForLine(line);
        if (string.IsNullOrEmpty(prefix))
            return "prefix=<none> owner=<none> apply=<none>";
        ReplicatedRowDescriptor row = DescriptorForLine(line);
        return row != null
            ? $"prefix={row.Prefix} owner={row.Owner} apply={row.ApplyMode}"
            : $"prefix={prefix} owner=<unknown> apply=<unknown>";
    }

    public static string DescribeDiff(string authorityLine, string clientLine)
    {
        string line = !string.Equals(authorityLine, "<missing>", StringComparison.Ordinal)
            ? authorityLine
            : clientLine;
        return DescribeLine(line);
    }

    static string[] Fields(params string[] fields) => fields ?? Array.Empty<string>();
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;
using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.Universe;

namespace UnitTests.Multiplayer;

[TestClass]
public sealed class ReplicationCoverageDiagnosticTests
{
    public TestContext TestContext { get; set; }

    [TestMethod]
    public void ReplicationManifest_ExecutableDescriptors_EnforceCoverageSymmetry_Headless()
    {
        ReplicatedRowDescriptor[] descriptors = AuthoritativeReplicationManifest.AllRows.ToArray();

        string[] declaredPrefixes = Sorted(descriptors.Select(row => row.Prefix)).ToArray();
        string[] emittedPrefixes = Sorted(descriptors.Where(row => row.EmitsPayload).Select(row => row.Prefix)).ToArray();
        string[] coveredPrefixes = Sorted(descriptors
            .Where(row => row.HasApply
                          || row.DigestPolicy == AuthoritativeReplicationDigestPolicy.HostOnlyDiagnostic)
            .Select(row => row.Prefix)).ToArray();

        AssertSetsEqual(declaredPrefixes, emittedPrefixes,
            "Every declared row prefix must be emitted by at least one executable descriptor.");
        AssertSetsEqual(declaredPrefixes, coveredPrefixes,
            "Every declared row prefix must either replay or be explicitly host-only diagnostic.");

        ReplicatedRowDescriptor[] uncoveredFatal = descriptors
            .Where(row => row.DigestPolicy == AuthoritativeReplicationDigestPolicy.Fatal)
            .Where(row => !row.HasApply)
            .ToArray();
        Assert.AreEqual(0, uncoveredFatal.Length,
            "Fatal digest descriptors require an apply delegate: "
            + string.Join(",", uncoveredFatal.Select(row => row.Id)));

        ReplicatedRowDescriptor[] uncoveredTransform = descriptors
            .Where(row => row.DigestPolicy == AuthoritativeReplicationDigestPolicy.Transform)
            .Where(row => !row.HasApply)
            .ToArray();
        Assert.AreEqual(0, uncoveredTransform.Length,
            "Transform digest descriptors require replay coverage: "
            + string.Join(",", uncoveredTransform.Select(row => row.Id)));

        string[] knownGapIds = descriptors
            .Where(row => row.KnownGap)
            .Select(row => row.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        AssertSetsEqual(Array.Empty<string>(), knownGapIds,
            "KnownGap descriptors must be closed or explicitly moved out of the fatal digest.");

        string[] hostOnlyIds = descriptors
            .Where(row => row.DigestPolicy == AuthoritativeReplicationDigestPolicy.HostOnlyDiagnostic)
            .Select(row => row.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        AssertSetsEqual(new[]
        {
            "BP.Blueprint",
            "D.DescriptiveFields",
            "F.Signatures",
            "FP.FleetPatrol",
            "G.FleetRequisition",
            "G.Refit",
            "S.PolicyDiagnostics",
        }, hostOnlyIds, "P2 host-only diagnostic descriptors must stay explicit.");
    }

    [TestMethod]
    public void ReplicationManifest_TopLevelDispatch_WalksDescriptorList_Headless()
    {
        string root = FindRepoRoot();
        string authoritativeSessionPath = Path.Combine(root, "Ship_Game", "Multiplayer", "Authoritative",
            "Authoritative4XSession.cs");
        string source = File.ReadAllText(authoritativeSessionPath);

        string buildPayloadBody = Slice(source, "static string BuildPayload", "static Ship[] SnapshotShips");
        Assert.IsFalse(Regex.IsMatch(buildPayloadBody, @"sb\.Append\(""[A-Z][A-Z0-9]*\|""\)"),
            "BuildPayload must emit through descriptor delegates, not raw row append blocks.");
        StringAssert.Contains(buildPayloadBody, "AuthoritativeReplicationManifest.AllRows");

        string replayBody = Slice(source, "public void ApplyEmpireRuntimePayload", "public void ApplyShipPresencePayload");
        Assert.IsFalse(Regex.IsMatch(replayBody, @"StartsWith\(""[A-Z][A-Z0-9]*\|"",\s*StringComparison\.Ordinal\)"),
            "Top-level replay dispatch must walk descriptors, not raw prefix branches.");
        StringAssert.Contains(replayBody, "AuthoritativeReplicationManifest.DescriptorForLine");
        StringAssert.Contains(replayBody, "AuthoritativeReplicationManifest.AllRows");

        string filterBody = Slice(source, "static string FilterPayload", "static string BlueprintSignature");
        Assert.IsFalse(filterBody.Contains("StartsWith(\"SX|", StringComparison.Ordinal),
            "Digest filtering must derive from descriptor digest policy, not hard-coded SX checks.");
        StringAssert.Contains(filterBody, "AuthoritativeReplicationManifest.DigestPolicyForLine");
    }

    [TestMethod]
    public void ReplicationCoverageDiagnostic_DumpsTruthGapReport_Headless()
    {
        DiagnosticSnapshot snapshot = BuildDiagnosticSnapshot();
        string report = RenderReport(snapshot);
        Console.WriteLine(report);
        TestContext?.WriteLine(report);
    }

    static DiagnosticSnapshot BuildDiagnosticSnapshot()
    {
        string root = FindRepoRoot();
        string authoritativeSessionPath = Path.Combine(root, "Ship_Game", "Multiplayer", "Authoritative",
            "Authoritative4XSession.cs");
        string authoritativeSessionSource = File.ReadAllText(authoritativeSessionPath);

        string payload = CaptureRepresentativePayload();
        string[] observedPayloadPrefixes = PrefixesForPayload(payload).ToArray();
        string[] builderSourcePrefixes = AuthoritativeReplicationManifest.AllRows
            .Where(row => row.EmitsPayload)
            .Select(row => row.Prefix)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(prefix => prefix, StringComparer.Ordinal)
            .ToArray();
        string[] replaySourcePrefixes = ExtractReplayDispatchPrefixes(authoritativeSessionSource).ToArray();

        var manifestRows = AuthoritativeReplicationManifest.AllRows
            .OrderBy(row => row.Prefix, StringComparer.Ordinal)
            .ThenBy(row => row.Id, StringComparer.Ordinal)
            .Select(row => new ManifestRow(row.Prefix, row.Owner, row.ApplyMode.ToString(), row.Notes))
            .ToArray();

        return new DiagnosticSnapshot(
            builderSourcePrefixes,
            observedPayloadPrefixes,
            replaySourcePrefixes,
            manifestRows,
            FieldCoverage);
    }

    static string CaptureRepresentativePayload()
    {
        var harness = new Authoritative4XSessionTests();
        try
        {
            MethodInfo buildWorld = typeof(Authoritative4XSessionTests).GetMethod("BuildWorld",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (buildWorld == null)
                return "";

            object builtWorld = buildWorld.Invoke(harness, new object[]
            {
                0xD351C0DEUL,
                true,  // extraPlayerPlanet
                true,  // includePlatform
                true,  // includeNeutralPlanet
                true,  // includeColonyShip
                true,  // includeFreighter
                true,  // includeCarrierPolicyShips
                true,  // includeTroopShips
            });

            FieldInfo screenField = builtWorld?.GetType().GetField("Screen",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (screenField?.GetValue(builtWorld) is not UniverseScreen screen)
                return "";

            return AuthoritativeStateSnapshot.Capture(screen, tick: 17).Payload;
        }
        finally
        {
            harness.Cleanup();
            harness.Dispose();
        }
    }

    static string RenderReport(DiagnosticSnapshot snapshot)
    {
        var sb = new StringBuilder(16 * 1024);
        SortedSet<string> allPrefixes = new(StringComparer.Ordinal);
        allPrefixes.UnionWith(snapshot.PayloadBuilderPrefixes);
        allPrefixes.UnionWith(snapshot.ObservedPayloadPrefixes);
        allPrefixes.UnionWith(snapshot.ReplayDispatchPrefixes);
        allPrefixes.UnionWith(snapshot.ManifestRows.Select(row => row.Prefix));
        allPrefixes.UnionWith(snapshot.FieldCoverage.Select(row => row.Prefix));

        Dictionary<string, ManifestRow> manifestByPrefix = snapshot.ManifestRows
            .GroupBy(row => row.Prefix, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        Dictionary<string, RowFieldCoverage> fieldsByPrefix = snapshot.FieldCoverage
            .ToDictionary(row => row.Prefix, StringComparer.Ordinal);

        sb.AppendLine("# Replication Coverage Diagnostic");
        sb.AppendLine();
        sb.AppendLine("This is a print-only diagnostic. It does not assert coverage symmetry.");
        sb.AppendLine();
        sb.AppendLine("## Row Prefix Coverage");
        sb.AppendLine();
        sb.AppendLine("| Prefix | Payload builder source | Payload observed | Replay dispatch | Manifest mode | Raw hash fields |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (string prefix in allPrefixes)
        {
            fieldsByPrefix.TryGetValue(prefix, out RowFieldCoverage fields);
            manifestByPrefix.TryGetValue(prefix, out ManifestRow manifest);
            sb.Append("| ").Append(prefix)
              .Append(" | ").Append(YesNo(snapshot.PayloadBuilderPrefixes.Contains(prefix)))
              .Append(" | ").Append(YesNo(snapshot.ObservedPayloadPrefixes.Contains(prefix)))
              .Append(" | ").Append(YesNo(snapshot.ReplayDispatchPrefixes.Contains(prefix)))
              .Append(" | ").Append(manifest?.Mode ?? "")
              .Append(" | ").Append(InlineFields(fields?.RawHashFields))
              .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## Field Coverage");
        sb.AppendLine();
        sb.AppendLine("| Prefix | Payload/fatal digest fields | Client-applied fields | Raw hash fields | Gap summary |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (RowFieldCoverage row in snapshot.FieldCoverage.OrderBy(row => row.Prefix, StringComparer.Ordinal))
        {
            string[] rawOnly = Except(row.RawHashFields, row.ApplyFields);
            string[] applyOnly = Except(row.ApplyFields, row.RawHashFields);
            string[] payloadOnly = Except(row.PayloadFields, row.ApplyFields);
            string gap = $"raw-not-applied: {InlineFields(rawOnly)}; applied-not-raw: {InlineFields(applyOnly)}";
            if (payloadOnly.Length > 0)
                gap += $"; payload-not-applied: {InlineFields(payloadOnly)}";
            if (!string.IsNullOrEmpty(row.Notes))
                gap += $"; notes: {row.Notes}";

            sb.Append("| ").Append(row.Prefix)
              .Append(" | ").Append(InlineFields(row.PayloadFields))
              .Append(" | ").Append(InlineFields(row.ApplyFields))
              .Append(" | ").Append(InlineFields(row.RawHashFields))
              .Append(" | ").Append(gap)
              .AppendLine(" |");
        }

        return sb.ToString();
    }

    static SortedSet<string> ExtractPayloadBuilderPrefixes(string source)
    {
        string body = Slice(source, "static string BuildPayload", "return sb.ToString()");
        return Sorted(Regex.Matches(body, @"sb\.Append\(""([A-Z][A-Z0-9]*)\|""\)")
            .Select(match => match.Groups[1].Value)
            .ToArray());
    }

    static SortedSet<string> ExtractReplayDispatchPrefixes(string source)
        => Sorted(Regex.Matches(source, @"StartsWith\(""([A-Z][A-Z0-9]*)\|""")
            .Select(match => match.Groups[1].Value)
            .Where(prefix => prefix != "off")
            .ToArray());

    static SortedSet<string> PrefixesForPayload(string payload)
        => Sorted((payload ?? "").Split('\n')
            .Select(line => AuthoritativeReplicationManifest.PrefixForLine(line.TrimEnd('\r')))
            .Where(prefix => !string.IsNullOrEmpty(prefix))
            .ToArray());

    static SortedSet<string> Sorted(IEnumerable<string> values)
        => new(values, StringComparer.Ordinal);

    static string Slice(string source, string startNeedle, string endNeedle)
    {
        int start = source.IndexOf(startNeedle, StringComparison.Ordinal);
        if (start < 0)
            return "";
        int end = source.IndexOf(endNeedle, start, StringComparison.Ordinal);
        return end > start ? source.Substring(start, end - start) : source.Substring(start);
    }

    static string FindRepoRoot()
    {
        string dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "StarDrive.csproj"))
                && Directory.Exists(Path.Combine(dir, "Ship_Game")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".."));
    }

    static string[] Except(string[] left, string[] right)
        => left.Except(right, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();

    static void AssertSetsEqual(string[] expected, string[] actual, string message)
    {
        string[] expectedSorted = expected.Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] actualSorted = actual.Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(expectedSorted, actualSorted, message);
    }

    static string InlineFields(string[] fields)
        => fields == null || fields.Length == 0 ? "" : string.Join("<br>", fields);

    static string YesNo(bool value) => value ? "yes" : "";

    static string[] Fields(string fields)
        => string.IsNullOrWhiteSpace(fields)
            ? Array.Empty<string>()
            : fields.Split('|').Select(field => field.Trim()).Where(field => field.Length > 0).ToArray();

    static RowFieldCoverage Row(string prefix, string owner, string payload, string apply, string rawHash,
        string notes = "")
        => new(prefix, owner, Fields(payload), Fields(apply), Fields(rawHash), notes);

    // Universe raw hash coverage is hand-derived from UniverseStateHash.cs:29-101.
    // Payload coverage is hand-derived from Authoritative4XSession.cs:1043-1415.
    // Client apply coverage is hand-derived from Authoritative4XSession.cs:69-117 and :140-2405.
    // These constants are intentionally explicit: reflection would hide field-set drift instead of documenting it.
    static readonly RowFieldCoverage[] FieldCoverage =
    {
        Row("V", "UniversePreferences",
            "PreferenceFlags|AllowPlayerInterTrade|PrioritizeProjectors",
            "PreferenceFlags|AllowPlayerInterTrade|PrioritizeProjectors",
            "",
            "Global preference row is payload/fatal-digest covered, not raw-hash covered."),

        Row("SD", "StarDate",
            "StarDate",
            "StarDate",
            "StarDate"),

        Row("E", "EmpireRuntime",
            "EmpireId|Research.Topic|ResearchQueueSignature|Money|TaxRate|TreasuryGoal|AutoTaxes|AutomationFlags|CurrentAutoFreighter|CurrentAutoColony|CurrentAutoScout|CurrentConstructor|CurrentResearchStation|CurrentMiningStation|ResearchProgress",
            "EmpireId|Research.Topic|ResearchQueueSignature|Money|TaxRate|TreasuryGoal|AutomationFlags|CurrentAutoFreighter|CurrentAutoColony|CurrentAutoScout|CurrentConstructor|CurrentResearchStation|CurrentMiningStation|ResearchProgress",
            "EmpireId|Money|TotalPopBillion|NetPlanetIncomes|NumPlanets|UnlockedTechs.Length|Research.NetResearch|Research.Topic|AI.Goals.Count|AllFleets.Count|EmpireRandomState",
            "Builder emits AutoTaxes as a separate field, but apply reads automation flags for AutoTaxes."),

        Row("U", "UnlockedTech",
            "EmpireId|TechUid|TechLevel",
            "EmpireId|TechUid|TechLevel|ExactUnlockedSet",
            "UnlockedTechs.Length",
            "Raw hash only counts unlocked techs; payload/apply carry the exact set and level."),

        Row("D", "PlayerDesign",
            "EmpireId|DesignName|Hull|Role|BaseCost|DesignSlotSignature|DesignBase64",
            "EmpireId|DesignName|DesignBase64",
            "",
            "Descriptive hull/role/base cost/slot signature columns are host-only diagnostic; fatal compare keeps name/base64."),

        Row("R", "Relationship",
            "EmpireId|TargetEmpireId|Known|AtWar|Treaty_NAPact|Treaty_Trade|Treaty_OpenBorders|Treaty_Alliance|Treaty_Peace",
            "EmpireId|TargetEmpireId|Known|AtWar|Treaty_NAPact|Treaty_Trade|Treaty_OpenBorders|Treaty_Alliance|Treaty_Peace|CanAttack|IsHostile|ActiveWar",
            "",
            "Relationship state is fatal payload-digest covered and replayed, but absent from the raw hash."),

        Row("G", "EmpireGoal",
            "EmpireId|GoalKind|MarkForColonization.TargetPlanetId|MarkForColonization.IsManual|MarkForColonization.FinishedShipId|Refit.Step|Refit.OldShipId|Refit.ToBuildName|Refit.PlanetBuildingAtId|Refit.Rush|Refit.FleetId|Refit.FleetKey|FleetRequisition.Step|FleetRequisition.FleetId|FleetRequisition.FleetKey|FleetRequisition.NodeIndex|FleetRequisition.TemplateName|FleetRequisition.PlanetBuildingAtId|FleetRequisition.Rush|DeepSpace.GoalType|DeepSpace.Step|DeepSpace.ToBuildName|DeepSpace.TargetPlanetId|DeepSpace.TargetSystemId|DeepSpace.TargetShipId|DeepSpace.PlanetBuildingAtId|DeepSpace.BuildPosition.X|DeepSpace.BuildPosition.Y|DeepSpace.MovePosition.X|DeepSpace.MovePosition.Y",
            "EmpireId|GoalKind|MarkForColonization.TargetPlanetId|MarkForColonization.IsManual|MarkForColonization.FinishedShipId|DeepSpace.GoalType|DeepSpace.Step|DeepSpace.ToBuildName|DeepSpace.TargetPlanetId|DeepSpace.TargetSystemId|DeepSpace.TargetShipId|DeepSpace.PlanetBuildingAtId|DeepSpace.BuildPosition.X|DeepSpace.BuildPosition.Y|DeepSpace.MovePosition.X|DeepSpace.MovePosition.Y",
            "AI.Goals.Count",
            "G is mode-mixed: MarkForColonization and DeepSpace replay, while Refit and FleetRequisition are host-only diagnostics."),

        Row("FP", "FleetPatrol",
            "EmpireId|FleetPatrolPlanSignature",
            "",
            "",
            "Fleet patrol definitions are host-only diagnostic and excluded from the fatal client digest."),

        Row("F", "FleetRuntime",
            "EmpireId|FleetId|FleetKey|Name|FleetIconIndex|CommandShipId|FinalPosition.X|FinalPosition.Y|FinalDirection.X|FinalDirection.Y|FleetShipSignature|FleetNodeSignature|FleetPatrolSignature",
            "EmpireId|FleetId|FleetKey|Name|FleetIconIndex|CommandShipId|FinalPosition.X|FinalPosition.Y|FinalDirection.X|FinalDirection.Y|CommandShipMembership",
            "AllFleets.Count",
            "Membership/layout/patrol signatures are host-only diagnostic; fatal fleet compare keeps replayed runtime columns."),

        Row("P", "PlanetRuntime",
            "PlanetId|OwnerId|ColonyType|GarrisonSize|WantedPlatforms|WantedShipyards|WantedStations|Food.Percent|Prod.Percent|Res.Percent|Food.PercentLock|Prod.PercentLock|Res.PercentLock|FoodState|ProdState|ManualFoodImportSlots|ManualProdImportSlots|ManualColoImportSlots|ManualFoodExportSlots|ManualProdExportSlots|ManualColoExportSlots|PrioritizedPort|GovOrbitals|AutoBuildTroops|DontScrapBuildings|Quarantine|ManualOrbitals|GovGroundDefense|SpecializedTradeHub|ManualCivilianBudget|ManualGroundDefBudget|ManualSpaceDefBudget|ConstructionQueue.Count",
            "PlanetId|OwnerId|ColonyType|GarrisonSize|WantedPlatforms|WantedShipyards|WantedStations|Food.Percent|Prod.Percent|Res.Percent|Food.PercentLock|Prod.PercentLock|Res.PercentLock|FoodState|ProdState|ManualFoodImportSlots|ManualProdImportSlots|ManualColoImportSlots|ManualFoodExportSlots|ManualProdExportSlots|ManualColoExportSlots|PrioritizedPort|GovOrbitals|AutoBuildTroops|DontScrapBuildings|Quarantine|ManualOrbitals|GovGroundDefense|SpecializedTradeHub|ManualCivilianBudget|ManualGroundDefBudget|ManualSpaceDefBudget|ConstructionQueue.Count",
            "PlanetId|OwnerId|PopulationBillion|ConstructionQueue.Count",
            "PopulationBillion is raw-hashed but not payload/replay covered."),

        Row("PX", "PlanetTransform",
            "PlanetId|Tick|OrbitalAngle|Position.X|Position.Y",
            "PlanetId|OrbitalAngle|Position.X|Position.Y",
            "",
            "Planet orbital presentation is excluded from SyncDigest and covered by TransformDigest instead."),

        Row("BP", "Blueprint",
            "PlanetId|BlueprintSignature",
            "",
            "",
            "Colony blueprint signatures are host-only diagnostic and excluded from the fatal client digest."),

        Row("T", "ColonyTile",
            "PlanetId|TileX|TileY|BuildingName|Biosphere|Habitable|Terraformable",
            "PlanetId|TileX|TileY|BuildingName|Biosphere|Habitable|Terraformable|ExactTileSet",
            "",
            "Tile state is replayed by exact-set replacement, but absent from the raw hash."),

        Row("GT", "GroundTroop",
            "PlanetId|TileX|TileY|TroopIndex|LoyaltyId|TroopName|Strength|AvailableMoveActions|AvailableAttackActions|MoveTimer|AttackTimer",
            "PlanetId|TileX|TileY|TroopIndex|LoyaltyId|TroopName|Strength|AvailableMoveActions|AvailableAttackActions|MoveTimer|AttackTimer|ExactGroundTroopSet",
            "",
            "Ground troops are fatal payload-digest covered and replayed, but absent from the raw hash."),

        Row("GC", "GroundCombat",
            "PlanetId|CombatIndex|Phase|Timer|AttackerLoyaltyId|DefenseTile.X|DefenseTile.Y|AttackingTroopRef|DefendingTroopRef|AttackingBuildingRef|DefendingBuildingRef",
            "PlanetId|CombatIndex|Phase|Timer|AttackerLoyaltyId|DefenseTile.X|DefenseTile.Y|AttackingTroopRef|DefendingTroopRef|AttackingBuildingRef|DefendingBuildingRef",
            "",
            "Ground combat is fatal payload-digest covered and replayed, but absent from the raw hash."),

        Row("Q", "ConstructionQueue",
            "PlanetId|QueueIndex|IsShip|IsBuilding|IsTroop|QueueType|ShipName|BuildingName|TroopType|TileX|TileY|Cost|ProductionSpent|Rush|Canceled",
            "PlanetId|QueueIndex|IsShip|IsBuilding|IsTroop|QueueType|ShipName|BuildingName|TroopType|TileX|TileY|Cost|ProductionSpent|Rush|Canceled|ExactQueueSet",
            "",
            "Raw hash only sees queue count through P.ConstructionQueue.Count, not queue item identity/progress."),

        Row("SC", "ShipPresence",
            "ShipId|LoyaltyId|DesignName",
            "ShipId|LoyaltyId|DesignName|ExactActiveShipSet",
            "ShipId",
            "Raw hash sees ship existence/id but not owner/design identity."),

        Row("S", "ShipRuntime",
            "ShipId|LoyaltyId|FleetId|FleetKey|AIState|CombatState|ScuttleTimer|TargetShipId|HasPriorityTarget|TargetQueueSignature|EscortTargetId|ShipOrderQueueSignature|TransportingFood|TransportingProduction|TransportingColonists|AllowInterEmpireTrade|FightersOut|TroopsOut|RecallFightersBeforeFTL|SendTroopsToShip|AllowBoardShip|ManualHangarOverride|TradeRouteSignature|AreaOfOperationSignature",
            "ShipId|LoyaltyId|FleetId|FleetKey|AIState|CombatState|ScuttleTimer|TargetShipId|HasPriorityTarget|TargetQueueSignature|EscortTargetId|ShipOrderQueueSignature|TransportingFood|TransportingProduction|TransportingColonists|AllowInterEmpireTrade|FightersOut|TroopsOut|RecallFightersBeforeFTL|SendTroopsToShip|AllowBoardShip|ManualHangarOverride|TradeRouteSignature|AreaOfOperationSignature",
            "ShipId|Health",
            "Fatal digest projection drops volatile movement placeholders and design-derived capability diagnostics while keeping replayed runtime and policy fields."),

        Row("SX", "ShipTransform",
            "ShipId|Tick|Position.X|Position.Y|Velocity.X|Velocity.Y|Rotation|SystemId|Active|Dying|YRotation|XRotation",
            "ShipId|Position.X|Position.Y|Velocity.X|Velocity.Y|Rotation|SystemId|Active|Dying|YRotation|XRotation",
            "ShipId|Position.X|Position.Y|Velocity.X|Velocity.Y|Rotation",
            "SX is excluded from SyncDigest and covered by TransformDigest instead."),

        Row("SV", "ShipVisibility",
            "ShipId|KnownByMask",
            "ShipId|KnownByMask",
            "",
            "Visibility is fatal payload-digest covered and replayed, but absent from the raw hash."),

        Row("ST", "ShipTroop",
            "ShipId|TroopIndex|LoyaltyId|TroopName|Strength|AvailableMoveActions|AvailableAttackActions|MoveTimer|AttackTimer",
            "ShipId|TroopIndex|LoyaltyId|TroopName|Strength|AvailableMoveActions|AvailableAttackActions|MoveTimer|AttackTimer|ExactShipTroopSet",
            "",
            "Ship-carried troops are fatal payload-digest covered and replayed, but absent from the raw hash."),

        Row("RNG", "RawHashOnly",
            "",
            "",
            "EmpireRandomState|UniverseRandomState",
            "Raw hash can drift on RNG with no payload/replay repair path."),
    };

    sealed class DiagnosticSnapshot
    {
        public readonly string[] PayloadBuilderPrefixes;
        public readonly string[] ObservedPayloadPrefixes;
        public readonly string[] ReplayDispatchPrefixes;
        public readonly ManifestRow[] ManifestRows;
        public readonly RowFieldCoverage[] FieldCoverage;

        public DiagnosticSnapshot(string[] payloadBuilderPrefixes, string[] observedPayloadPrefixes,
            string[] replayDispatchPrefixes, ManifestRow[] manifestRows, RowFieldCoverage[] fieldCoverage)
        {
            PayloadBuilderPrefixes = payloadBuilderPrefixes;
            ObservedPayloadPrefixes = observedPayloadPrefixes;
            ReplayDispatchPrefixes = replayDispatchPrefixes;
            ManifestRows = manifestRows;
            FieldCoverage = fieldCoverage;
        }
    }

    sealed class ManifestRow
    {
        public readonly string Prefix;
        public readonly string Owner;
        public readonly string Mode;
        public readonly string Notes;

        public ManifestRow(string prefix, string owner, string mode, string notes)
        {
            Prefix = prefix;
            Owner = owner;
            Mode = mode;
            Notes = notes;
        }
    }

    sealed class RowFieldCoverage
    {
        public readonly string Prefix;
        public readonly string Owner;
        public readonly string[] PayloadFields;
        public readonly string[] ApplyFields;
        public readonly string[] RawHashFields;
        public readonly string Notes;

        public RowFieldCoverage(string prefix, string owner, string[] payloadFields, string[] applyFields,
            string[] rawHashFields, string notes)
        {
            Prefix = prefix;
            Owner = owner;
            PayloadFields = payloadFields;
            ApplyFields = applyFields;
            RawHashFields = rawHashFields;
            Notes = notes;
        }
    }
}

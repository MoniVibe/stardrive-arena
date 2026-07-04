using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using SDLockstep;
using SDUtils;
using SDUtils.Deterministic;
using Ship_Game.AI;
using Ship_Game.AI.StrategyAI.WarGoals;
using Ship_Game.Commands.Goals;
using Ship_Game.Determinism;
using Ship_Game.Empires.Components;
using Ship_Game.Fleets;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
using Ship_Game.Ships.AI;
using Ship_Game.Universe;
using Rectangle = SDGraphics.Rectangle;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.Multiplayer.Authoritative;

public sealed partial class AuthoritativeStateSnapshot
{
    const int VolatileShipPositionDigest = 0;
    static readonly ConstructorInfo ShipWithIdCtor = typeof(Ship).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        new[] { typeof(UniverseState), typeof(int), typeof(Ship), typeof(Empire), typeof(Vector2) },
        modifiers: null);
    static readonly MethodInfo ShipTemplateLookup = typeof(Ship)
        .GetMethod("GetShipTemplate", BindingFlags.Static | BindingFlags.NonPublic);
    static readonly FieldInfo UniverseUniqueObjectIdsField = typeof(UniverseState)
        .GetField("UniqueObjectIds", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo GoalStepField = typeof(Goal)
        .GetField("<Step>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly Dictionary<string, int[]> FatalDigestColumnsByPrefix = new(StringComparer.Ordinal)
    {
        ["D"] = new[] { 0, 1, 2, 7 },
        ["F"] = Enumerable.Range(0, 11).ToArray(),
        ["S"] = new[]
        {
            0, 1, 2, 3, 4, 5, 6,
            11, 12, 13, 14, 15, 16,
            18, 19, 20, 21,
            23, 25, 26, 27, 28, 29, 30, 31,
        },
    };

    public uint Tick;
    public ulong HashLo;
    public ulong HashHi;
    public string SyncDigest = "";
    public string TransformDigest = "";
    public string Payload = "";

    public AuthoritativeStateSnapshotMessage ToMessage(int fromPeer)
        => new()
        {
            FromPeer = fromPeer,
            Tick = Tick,
            HashLo = HashLo,
            HashHi = HashHi,
            SyncDigest = SyncDigest,
            TransformDigest = TransformDigest,
            Payload = Payload,
        };

    public static AuthoritativeStateSnapshot FromMessage(AuthoritativeStateSnapshotMessage message)
        => new()
        {
            Tick = message.Tick,
            HashLo = message.HashLo,
            HashHi = message.HashHi,
            SyncDigest = message.SyncDigest ?? "",
            TransformDigest = message.TransformDigest ?? "",
            Payload = message.Payload ?? "",
        };

    public void ApplyEmpireRuntimePayload(UniverseState universe)
    {
#if DEBUG
        using (AuthoritativeMutationGuard.EnterReplayApply())
#endif
        {
        if (universe == null || string.IsNullOrEmpty(Payload))
            return;

        universe.Objects.UpdateLists(removeInactiveObjects: false);
        string[] lines = Payload.Split('\n');
        ApplyInitialReplayLines(universe, lines);
        ApplyReplayStage(universe, lines, AuthoritativeReplicationApplyStage.UnlockedTech);
        ApplyReplayStage(universe, lines, AuthoritativeReplicationApplyStage.PlayerDesign);
        ApplyReplayStage(universe, lines, AuthoritativeReplicationApplyStage.ShipPresence);
        ApplyReplayStage(universe, lines, AuthoritativeReplicationApplyStage.FleetRuntime);
        ApplyReplayStage(universe, lines, AuthoritativeReplicationApplyStage.ShipRuntime);
        ApplyReplayStage(universe, lines, AuthoritativeReplicationApplyStage.ShipTroop);
        ApplyReplayStage(universe, lines, AuthoritativeReplicationApplyStage.ColonyTile);
        ApplyReplayStage(universe, lines, AuthoritativeReplicationApplyStage.GroundTroop);
        ApplyReplayStage(universe, lines, AuthoritativeReplicationApplyStage.GroundCombat);
        ApplyReplayStage(universe, lines, AuthoritativeReplicationApplyStage.ConstructionQueue);
        ApplyReplayStage(universe, lines, AuthoritativeReplicationApplyStage.ColonizationGoal);
        ApplyReplayStage(universe, lines, AuthoritativeReplicationApplyStage.DeepSpaceGoal);
        }
    }

    static void ApplyInitialReplayLines(UniverseState universe, string[] lines)
    {
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            ReplicatedRowDescriptor descriptor = AuthoritativeReplicationManifest.DescriptorForLine(line);
            descriptor?.InitialApplyLine?.Invoke(universe, line);
        }
    }

    static void ApplyReplayStage(UniverseState universe, string[] lines,
        AuthoritativeReplicationApplyStage stage)
    {
        foreach (ReplicatedRowDescriptor descriptor in AuthoritativeReplicationManifest.AllRows)
        {
            if (descriptor.ApplyStage == stage)
                descriptor.Apply?.Invoke(universe, lines);
        }
    }

    public void ApplyShipPresencePayload(UniverseState universe)
    {
#if DEBUG
        using (AuthoritativeMutationGuard.EnterReplayApply())
#endif
        {
        if (universe == null || string.IsNullOrEmpty(Payload))
            return;

        ApplyShipPresencePayload(universe, Payload.Split('\n'));
        }
    }

    public void ApplyRelationshipPayload(UniverseState universe)
    {
#if DEBUG
        using (AuthoritativeMutationGuard.EnterReplayApply())
#endif
        {
        if (universe == null || string.IsNullOrEmpty(Payload))
            return;

        foreach (string rawLine in Payload.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith("R|", StringComparison.Ordinal))
                ApplyRelationshipLine(universe, line);
        }
        }
    }

    internal static void ApplyUniversePreferenceLine(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        if (p.Length < 2
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int flags))
        {
            return;
        }

        var preferences = (AuthoritativeUniversePreferenceFlags)flags;
        universe.P.AllowPlayerInterTrade =
            preferences.HasFlag(AuthoritativeUniversePreferenceFlags.AllowPlayerInterTrade);
        universe.P.PrioitizeProjectors =
            preferences.HasFlag(AuthoritativeUniversePreferenceFlags.PrioritizeProjectors);
    }

    internal static void ApplyStarDateLine(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        if (p.Length < 2 || !TryParseFloatBits(p[1], out float starDate))
            return;

        universe.StarDate = starDate;
    }

    internal static void ApplyEmpireRuntimeLine(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        if (p.Length < 15
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int empireId)
            || empireId <= 0
            || empireId > universe.Empires.Count)
        {
            return;
        }

        Empire empire = universe.GetEmpireById(empireId);
        if (empire == null)
            return;

        ApplyResearchRuntime(empire, p[2] ?? "", p[3] ?? "");
        if (TryParseFloatBits(p[4], out float money))
            empire.Money = money;
        if (TryParseFloatBits(p[5], out float taxRate))
            empire.data.TaxRate = taxRate;
        if (TryParseFloatBits(p[6], out float treasuryGoal))
            empire.data.treasuryGoal = treasuryGoal;
        AuthoritativeEmpireAutomationFlags automationFlags = int.TryParse(p[8],
            NumberStyles.Integer, CultureInfo.InvariantCulture, out int flags)
                ? (AuthoritativeEmpireAutomationFlags)flags
                : AutomationFlags(empire);
        empire.SetAuthoritativeAutomationState(automationFlags, p[9] ?? "", p[10] ?? "",
            p[11] ?? "", p[12] ?? "", p[13] ?? "", p[14] ?? "");
        if (p.Length > 15 && TryParseFloatBits(p[15], out float researchProgress))
            ApplyResearchProgress(empire, p[2] ?? "", researchProgress);
    }

    static void ApplyResearchRuntime(Empire empire, string topic, string queueSignature)
    {
        empire.Research.SetQueueExact(queueSignature.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
    }

    static void ApplyResearchProgress(Empire empire, string topic, float progress)
    {
        if (topic.IsEmpty()
            || !empire.TryGetTechEntry(topic, out TechEntry tech)
            || tech == TechEntry.None
            || tech.Unlocked)
        {
            return;
        }

        tech.Progress = Math.Clamp(progress, 0f, tech.TechCost);
    }

    static void ApplyUnlockedTechLine(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        if (p.Length < 4
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int empireId)
            || empireId <= 0
            || empireId > universe.Empires.Count)
        {
            return;
        }

        string techUid = p[2] ?? "";
        if (techUid.IsEmpty())
            return;

        Empire empire = universe.GetEmpireById(empireId);
        if (empire == null || !empire.TryGetTechEntry(techUid, out TechEntry tech) || tech == TechEntry.None)
            return;

        int level = int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLevel)
            ? parsedLevel
            : 0;
        Authoritative4XSessionSave.ApplyUnlockedTech(empire, tech, level);
    }

    static void ApplyUnlockedTechPayload(UniverseState universe, string[] lines)
    {
        var desiredByEmpire = new Dictionary<int, Dictionary<string, int>>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (!line.StartsWith("U|", StringComparison.Ordinal))
                continue;

            string[] p = line.Split('|');
            if (p.Length < 4
                || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int empireId)
                || empireId <= 0
                || empireId > universe.Empires.Count)
            {
                continue;
            }

            string techUid = p[2] ?? "";
            if (techUid.IsEmpty())
                continue;

            int level = int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLevel)
                ? parsedLevel
                : 0;
            if (!desiredByEmpire.TryGetValue(empireId, out Dictionary<string, int> desired))
            {
                desired = new Dictionary<string, int>(StringComparer.Ordinal);
                desiredByEmpire[empireId] = desired;
            }
            desired[techUid] = level;
        }

        foreach (Empire empire in universe.Empires)
        {
            if (empire == null)
                continue;

            desiredByEmpire.TryGetValue(empire.Id, out Dictionary<string, int> desired);
            desired ??= new Dictionary<string, int>(StringComparer.Ordinal);

            bool changed = false;
            foreach (TechEntry tech in empire.TechEntries)
            {
                if (tech == null || tech == TechEntry.None || !tech.Unlocked)
                    continue;

                if (!desired.ContainsKey(tech.UID))
                {
                    tech.ResetUnlockedTech();
                    changed = true;
                }
            }

            foreach ((string techUid, int level) in desired.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (!empire.TryGetTechEntry(techUid, out TechEntry tech) || tech == TechEntry.None)
                    continue;

                if (tech.Unlocked && tech.Level > level)
                {
                    tech.ResetUnlockedTech();
                    changed = true;
                }

                bool wasUnlocked = tech.Unlocked;
                int wasLevel = tech.Level;
                Authoritative4XSessionSave.ApplyUnlockedTech(empire, tech, level);
                changed |= wasUnlocked != tech.Unlocked || wasLevel != tech.Level;
            }

            if (changed || desired.Count > 0)
                empire.RebuildUnlockCachesForAuthoritativeSync();
        }
    }

    static void ApplyPlayerDesignPayload(UniverseState universe, string[] lines)
    {
        var desiredByEmpire = new Dictionary<int, HashSet<string>>();
        var designLines = new List<string>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (!line.StartsWith("D|", StringComparison.Ordinal))
                continue;

            string[] p = line.Split('|');
            if (p.Length < 3
                || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int empireId)
                || empireId <= 0
                || empireId > universe.Empires.Count)
            {
                continue;
            }

            string designName = p[2] ?? "";
            if (designName.IsEmpty())
                continue;

            if (!desiredByEmpire.TryGetValue(empireId, out HashSet<string> desired))
            {
                desired = new HashSet<string>(StringComparer.Ordinal);
                desiredByEmpire[empireId] = desired;
            }
            desired.Add(designName);
            designLines.Add(line);
        }

        foreach (Empire empire in universe.Empires)
        {
            if (empire == null)
                continue;

            desiredByEmpire.TryGetValue(empire.Id, out HashSet<string> desired);
            foreach (IShipDesign design in empire.ShipsWeCanBuildSnapshot.Where(d => d.IsPlayerDesign).ToArray())
            {
                if (desired == null || !desired.Contains(design.Name))
                    empire.RemoveBuildableShip(design);
            }
        }

        foreach (string line in designLines)
            ApplyPlayerDesignLine(universe, line);
    }

    internal static void ApplyPlayerDesignLine(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        if (p.Length < 3
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int empireId)
            || empireId <= 0
            || empireId > universe.Empires.Count)
        {
            return;
        }

        string designName = p[2] ?? "";
        if (designName.IsEmpty())
            return;

        Empire empire = universe.GetEmpireById(empireId);
        if (empire == null)
            return;

        string design64 = p.Length >= 8 ? p[7] ?? "" : "";
        if (ResourceManager.Ships.GetDesign(designName, out IShipDesign existing)
            && existing.IsPlayerDesign
            && string.Equals(DesignBase64(existing), design64, StringComparison.Ordinal))
        {
            ReplaceBuildableShipForAuthoritativeReplay(empire, existing);
            return;
        }

        if (design64.IsEmpty())
            return;

        ShipDesign design;
        try
        {
            design = ShipDesign.FromBytes(Convert.FromBase64String(design64));
        }
        catch
        {
            return;
        }

        if (design == null || !string.Equals(design.Name, designName, StringComparison.Ordinal))
            return;

        if (!ResourceManager.AddShipTemplate(design, playerDesign: true, readOnly: false))
            return;

        if (ResourceManager.Ships.GetDesign(design.Name, out IShipDesign registered) && registered.IsPlayerDesign)
            ReplaceBuildableShipForAuthoritativeReplay(empire, registered);
    }

    static void ReplaceBuildableShipForAuthoritativeReplay(Empire empire, IShipDesign registered)
    {
        if (empire == null || registered == null)
            return;

        foreach (IShipDesign current in empire.ShipsWeCanBuildSnapshot)
        {
            if (!ReferenceEquals(current, registered)
                && string.Equals(current?.Name ?? "", registered.Name, StringComparison.Ordinal))
            {
                empire.RemoveBuildableShip(current);
            }
        }
        empire.AddBuildableShip(registered);
    }

    static void ApplyConstructionQueuePayload(UniverseState universe, string[] lines)
    {
        var expectedCounts = new Dictionary<int, int>();
        var desired = new Dictionary<int, List<ConstructionQueueRuntime>>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith("P|", StringComparison.Ordinal))
            {
                string[] p = line.Split('|');
                if (p.Length >= 34
                    && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int planetId)
                    && int.TryParse(p[33], NumberStyles.Integer, CultureInfo.InvariantCulture, out int queueCount))
                {
                    expectedCounts[planetId] = queueCount;
                }
            }
            else if (line.StartsWith("Q|", StringComparison.Ordinal)
                     && TryParseConstructionQueueRuntime(line, out ConstructionQueueRuntime runtime))
            {
                if (!desired.TryGetValue(runtime.PlanetId, out List<ConstructionQueueRuntime> queue))
                {
                    queue = new List<ConstructionQueueRuntime>();
                    desired[runtime.PlanetId] = queue;
                }
                queue.Add(runtime);
            }
        }

        foreach ((int planetId, int queueCount) in expectedCounts.OrderBy(kv => kv.Key))
        {
            Planet planet = universe.GetPlanet(planetId);
            if (planet == null)
                continue;

            if (queueCount == 0 || !desired.TryGetValue(planetId, out List<ConstructionQueueRuntime> queueRows))
            {
                if (planet.ConstructionQueue.Count != 0)
                    planet.Construction.ReplaceQueueForAuthoritativeSync(Array.Empty<QueueItem>());
                continue;
            }

            ConstructionQueueRuntime[] orderedRows = queueRows
                .OrderBy(q => q.QueueIndex)
                .ThenBy(q => q.ShipName, StringComparer.Ordinal)
                .ThenBy(q => q.BuildingName, StringComparer.Ordinal)
                .ThenBy(q => q.TroopType, StringComparer.Ordinal)
                .ToArray();

            if (QueueShapeMatches(planet, orderedRows))
            {
                for (int i = 0; i < orderedRows.Length; ++i)
                    ApplyQueueItemRuntime(planet.ConstructionQueue[i], orderedRows[i]);
                continue;
            }

            var items = new List<QueueItem>(orderedRows.Length);
            foreach (ConstructionQueueRuntime row in orderedRows)
            {
                if (TryCreateQueueItem(planet, row, out QueueItem item))
                    items.Add(item);
            }

            planet.Construction.ReplaceQueueForAuthoritativeSync(items);
        }
    }

    static bool QueueShapeMatches(Planet planet, ConstructionQueueRuntime[] rows)
    {
        if (planet.ConstructionQueue.Count != rows.Length)
            return false;

        for (int i = 0; i < rows.Length; ++i)
        {
            if (!QueueItemMatches(planet.ConstructionQueue[i], rows[i]))
                return false;
        }
        return true;
    }

    static bool QueueItemMatches(QueueItem item, ConstructionQueueRuntime row)
    {
        return item != null
               && item.isShip == row.IsShip
               && item.isBuilding == row.IsBuilding
               && item.isTroop == row.IsTroop
               && (int)item.QType == row.QueueType
               && string.Equals(item.ShipData?.Name ?? "", row.ShipName, StringComparison.Ordinal)
               && string.Equals(item.Building?.Name ?? "", row.BuildingName, StringComparison.Ordinal)
               && string.Equals(item.TroopType ?? "", row.TroopType, StringComparison.Ordinal);
    }

    static void ApplyQueueItemRuntime(QueueItem item, ConstructionQueueRuntime row)
    {
        item.Cost = row.Cost;
        item.ProductionSpent = row.ProductionSpent;
        item.Rush = row.Rush;
        item.SetCanceled(row.Canceled);
        if (row.IsBuilding)
            item.pgs = FindQueueTile(item.Planet, row.TileX, row.TileY);
    }

    static bool TryParseConstructionQueueRuntime(string line, out ConstructionQueueRuntime runtime)
    {
        runtime = default;
        string[] p = line.Split('|');
        if (p.Length < 16
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int planetId)
            || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int queueIndex)
            || queueIndex < 0
            || !int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int isShip)
            || !int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int isBuilding)
            || !int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int isTroop)
            || !int.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int queueType)
            || !int.TryParse(p[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tileX)
            || !int.TryParse(p[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tileY)
            || !TryParseFloatBits(p[12], out float cost)
            || !TryParseFloatBits(p[13], out float productionSpent))
        {
            return false;
        }

        int.TryParse(p[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rush);
        int.TryParse(p[15], NumberStyles.Integer, CultureInfo.InvariantCulture, out int canceled);
        runtime = new ConstructionQueueRuntime(planetId, queueIndex, isShip != 0, isBuilding != 0,
            isTroop != 0, queueType, p[7] ?? "", p[8] ?? "", p[9] ?? "", tileX, tileY,
            cost, productionSpent, rush != 0, canceled != 0);
        return true;
    }

    static bool TryCreateQueueItem(Planet planet, ConstructionQueueRuntime row, out QueueItem item)
    {
        item = new QueueItem(planet)
        {
            isShip = row.IsShip,
            isBuilding = row.IsBuilding,
            isTroop = row.IsTroop,
            QType = (QueueItemType)row.QueueType,
            Cost = row.Cost,
            ProductionSpent = row.ProductionSpent,
            Rush = row.Rush,
            NotifyOnEmpty = false,
        };
        item.SetCanceled(row.Canceled);

        if (row.IsShip)
        {
            if (!ResourceManager.Ships.GetDesign(row.ShipName, out IShipDesign design))
                return false;
            item.ShipData = design;
            item.isOrbital = design.IsPlatformOrStation;
        }

        if (row.IsBuilding)
        {
            Building building = ResourceManager.CreateBuilding(planet, row.BuildingName);
            if (building == null)
                return false;
            item.Building = building;
            item.IsMilitary = building.IsMilitary;
            item.IsTerraformer = building.IsTerraformer;
            item.pgs = FindQueueTile(planet, row.TileX, row.TileY);
        }

        if (row.IsTroop)
        {
            if (!ResourceManager.GetTroopTemplate(row.TroopType, out _))
                return false;
            item.TroopType = row.TroopType;
        }

        return row.IsShip || row.IsBuilding || row.IsTroop;
    }

    static PlanetGridSquare FindQueueTile(Planet planet, int x, int y)
        => x >= 0 && y >= 0
            ? planet.TilesList.FirstOrDefault(t => t.X == x && t.Y == y)
            : null;

    internal static void ApplyPlanetRuntimeLine(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        if (p.Length < 34
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int planetId))
        {
            return;
        }

        Planet planet = universe.GetPlanet(planetId);
        if (planet == null)
            return;

        if (int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ownerId))
        {
            Empire owner = ownerId > 0 ? universe.GetEmpireById(ownerId) : null;
            if (planet.Owner != owner)
                planet.SetOwner(owner);
        }

        if (int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int colonyType)
            && Enum.IsDefined(typeof(Planet.ColonyType), colonyType))
        {
            planet.SetColonyType((Planet.ColonyType)colonyType);
        }

        int garrisonSize = planet.GarrisonSize;
        byte parsedWantedPlatforms = planet.WantedPlatforms;
        byte parsedWantedShipyards = planet.WantedShipyards;
        byte parsedWantedStations = planet.WantedStations;
        if (int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int garrison))
            garrisonSize = garrison;
        if (TryParseByte(p[5], out byte wantedPlatforms))
            parsedWantedPlatforms = wantedPlatforms;
        if (TryParseByte(p[6], out byte wantedShipyards))
            parsedWantedShipyards = wantedShipyards;
        if (TryParseByte(p[7], out byte wantedStations))
            parsedWantedStations = wantedStations;
        planet.SetDefenseTargets(garrisonSize, parsedWantedPlatforms, parsedWantedShipyards,
            parsedWantedStations);

        if (TryParseFloatBits(p[8], out float foodPercent))
            planet.Food.Percent = foodPercent;
        if (TryParseFloatBits(p[9], out float prodPercent))
            planet.Prod.Percent = prodPercent;
        if (TryParseFloatBits(p[10], out float resPercent))
            planet.Res.Percent = resPercent;

        planet.Food.PercentLock = ParseFlag(p[11]);
        planet.Prod.PercentLock = ParseFlag(p[12]);
        planet.Res.PercentLock = ParseFlag(p[13]);

        if (int.TryParse(p[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out int foodState)
            && Enum.IsDefined(typeof(Planet.GoodState), foodState))
        {
            planet.FS = (Planet.GoodState)foodState;
        }
        if (int.TryParse(p[15], NumberStyles.Integer, CultureInfo.InvariantCulture, out int prodState)
            && Enum.IsDefined(typeof(Planet.GoodState), prodState))
        {
            planet.PS = (Planet.GoodState)prodState;
        }

        int foodImport = planet.ManualFoodImportSlots;
        int prodImport = planet.ManualProdImportSlots;
        int coloImport = planet.ManualColoImportSlots;
        int foodExport = planet.ManualFoodExportSlots;
        int prodExport = planet.ManualProdExportSlots;
        int coloExport = planet.ManualColoExportSlots;
        if (int.TryParse(p[16], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedFoodImport))
            foodImport = parsedFoodImport;
        if (int.TryParse(p[17], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedProdImport))
            prodImport = parsedProdImport;
        if (int.TryParse(p[18], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedColoImport))
            coloImport = parsedColoImport;
        if (int.TryParse(p[19], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedFoodExport))
            foodExport = parsedFoodExport;
        if (int.TryParse(p[20], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedProdExport))
            prodExport = parsedProdExport;
        if (int.TryParse(p[21], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedColoExport))
            coloExport = parsedColoExport;
        planet.SetManualTradeSlots(foodImport, prodImport, coloImport, foodExport, prodExport, coloExport);

        planet.SetPrioritizedPort(ParseFlag(p[22]));
        planet.SetGovernorOptions(ParseFlag(p[23]), ParseFlag(p[24]), ParseFlag(p[25]),
            ParseFlag(p[26]), ParseFlag(p[27]), ParseFlag(p[28]), ParseFlag(p[29]));

        if (TryParseFloatBits(p[30], out float civilianBudget))
            planet.SetManualCivBudget(civilianBudget);
        if (TryParseFloatBits(p[31], out float groundBudget))
            planet.SetManualGroundDefBudget(groundBudget);
        if (TryParseFloatBits(p[32], out float spaceBudget))
            planet.SetManualSpaceDefBudget(spaceBudget);
    }

    internal static void ApplyPlanetTransformLine(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        if (p.Length < 6
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int planetId)
            || !TryParseFloatBits(p[3], out float orbitalAngle)
            || !TryParseFloatBits(p[4], out float x)
            || !TryParseFloatBits(p[5], out float y))
        {
            return;
        }

        Planet planet = universe.GetPlanet(planetId);
        if (planet == null)
            return;

        planet.OrbitalAngle = orbitalAngle;
        planet.Position = new Vector2(x, y);
    }

    static void ApplyShipPresencePayload(UniverseState universe, string[] lines)
    {
        universe.Objects.UpdateLists(removeInactiveObjects: false);
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith("SC|", StringComparison.Ordinal))
                MaterializeMissingShip(universe, line);
        }
        universe.Objects.UpdateLists(removeInactiveObjects: false);

        var expectedShipIds = new HashSet<int>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (!line.StartsWith("S|", StringComparison.Ordinal))
                continue;

            string[] p = line.Split('|');
            if (p.Length >= 2
                && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int shipId))
            {
                expectedShipIds.Add(shipId);
            }
        }

        bool removedAny = false;
        Ship[] ships = universe.Objects.GetShips();
        for (int i = 0; i < ships.Length; ++i)
        {
            Ship ship = ships[i];
            if (ship?.Active == true && !expectedShipIds.Contains(ship.Id))
            {
                ship.QueueTotalRemoval();
                removedAny = true;
            }
        }

        if (removedAny)
            universe.Objects.UpdateLists(removeInactiveObjects: true);
    }

    static void MaterializeMissingShip(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        if (p.Length < 4
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int shipId)
            || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int empireId))
        {
            return;
        }

        if (universe.Objects.FindShip(shipId) != null)
            return;

        Empire empire = universe.GetEmpireById(empireId);
        string designName = p[3] ?? "";
        if (empire == null || designName.IsEmpty()
            || ShipWithIdCtor == null)
        {
            return;
        }

        Ship template = ShipTemplateLookup?.Invoke(null, new object[] { designName }) as Ship;
        if (template == null)
            return;

        if (ShipWithIdCtor.Invoke(new object[] { universe, shipId, template, empire, Vector2.Zero }) is not Ship ship
            || !ship.HasModules)
        {
            return;
        }

        BumpUniqueObjectIds(universe, shipId);
        universe.AddShip(ship);
        AddShipToEmpireListsForAuthoritativeReplay(ship);
    }

    static void AddShipToEmpireListsForAuthoritativeReplay(Ship ship)
    {
        Empire empire = ship?.Loyalty;
        if (empire == null)
            return;

        empire.EmpireShips.UpdatePublicLists();
        bool alreadyListed = ship.IsSubspaceProjector
            ? empire.OwnedProjectors.Contains(ship)
            : empire.OwnedShips.Contains(ship);
        if (alreadyListed)
            return;

        (empire as IEmpireShipLists)?.AddNewShipAtEndOfTurn(ship);
        empire.EmpireShips.UpdatePublicLists();
    }

    static void BumpUniqueObjectIds(UniverseState universe, int objectId)
    {
        if (UniverseUniqueObjectIdsField?.GetValue(universe) is int current && current < objectId)
            UniverseUniqueObjectIdsField.SetValue(universe, objectId);
    }

    static void ApplyFleetRuntimePayload(UniverseState universe, string[] lines)
    {
        var desired = new Dictionary<int, List<string>>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (!line.StartsWith("F|", StringComparison.Ordinal))
                continue;

            string[] p = line.Split('|');
            if (p.Length < 14
                || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int empireId)
                || empireId <= 0
                || empireId > universe.Empires.Count
                || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fleetId)
                || !int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fleetKey))
            {
                continue;
            }

            if (!desired.TryGetValue(empireId, out List<string> rows))
            {
                rows = new List<string>();
                desired[empireId] = rows;
            }
            rows.Add(line);
        }

        foreach (Empire empire in universe.Empires.OrderBy(e => e.Id))
        {
            desired.TryGetValue(empire.Id, out List<string> rows);
            rows ??= new List<string>();
            RemoveStaleAuthoritativeReplayFleets(empire, rows);

            foreach (string line in rows
                         .OrderBy(r => FleetReplaySortKey(r).Key)
                         .ThenBy(r => FleetReplaySortKey(r).Id))
            {
                string[] p = line.Split('|');
                if (p.Length < 14
                    || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fleetId)
                    || !int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fleetKey))
                {
                    continue;
                }

                ApplyFleetRuntimeLine(universe, lines, empire, p, fleetId, fleetKey);
            }
        }
    }

    static void RemoveStaleAuthoritativeReplayFleets(Empire empire, List<string> rows)
    {
        var desiredKeys = new HashSet<int>();
        var desiredIds = new HashSet<int>();
        foreach (string row in rows)
        {
            string[] p = row.Split('|');
            if (p.Length < 4)
                continue;
            if (int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fleetId))
                desiredIds.Add(fleetId);
            if (int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fleetKey))
                desiredKeys.Add(fleetKey);
        }

        Fleet[] stale = empire.AllFleets
            .Where(f => f != null
                        && (f.Key > 0
                            ? !desiredKeys.Contains(f.Key)
                            : !desiredIds.Contains(SnapshotFleetId(f))))
            .ToArray();
        for (int i = 0; i < stale.Length; ++i)
        {
            stale[i].RemoveAllShips(clearOrders: false, fleeIfInCombat: false);
            empire.RemoveFleet(stale[i]);
        }
    }

    static (int Key, int Id) FleetReplaySortKey(string line)
    {
        string[] p = line.Split('|');
        int.TryParse(p.ElementAtOrDefault(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id);
        int.TryParse(p.ElementAtOrDefault(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out int key);
        return (key, id);
    }

    static void ApplyFleetRuntimeLine(UniverseState universe, string[] lines, Empire empire, string[] p,
        int fleetId, int fleetKey)
    {
        int commandShipId = int.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCommandShipId)
            ? parsedCommandShipId
            : 0;
        Fleet fleet = fleetKey > 0
            ? empire.GetFleetOrNull(fleetKey)
            : empire.AllFleets.FirstOrDefault(f => f != null && f.Id == fleetId);
        Ship commandShip = commandShipId > 0
            ? FindOrMaterializeShipForReplay(universe, lines, commandShipId)
            : null;
        fleet ??= commandShip?.Fleet;
        if (fleet == null && commandShip != null)
            fleet = empire.AllFleets.FirstOrDefault(f => f?.Ships.ContainsRef(commandShip) == true);
        string authoritativeName = p[4] ?? "";
        if (fleet == null && authoritativeName.NotEmpty())
            fleet = empire.AllFleets.FirstOrDefault(f => f != null
                                                         && string.Equals(f.Name ?? "", authoritativeName,
                                                             StringComparison.Ordinal));
        if (fleet == null)
        {
            int createKey = fleetKey > 0 ? fleetKey : fleetId;
            if (createKey <= 0)
                return;
            fleet = empire.CreateFleet(createKey, authoritativeName.NotEmpty() ? authoritativeName : null);
        }
        fleet.Key = fleetKey;

        if (int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int icon))
            fleet.FleetIconIndex = icon;
        if (commandShipId >= 0)
        {
            if (commandShip != null && commandShip.Fleet != fleet && !fleet.Ships.ContainsRef(commandShip))
            {
                commandShip.Fleet?.RemoveShip(commandShip, clearOrders: false);
                fleet.AddShip(commandShip);
            }
            fleet.SetCommandShip(commandShip);
        }
        if (TryParseFloatBits(p[7], out float finalX)
            && TryParseFloatBits(p[8], out float finalY))
        {
            fleet.FinalPosition = new Vector2(finalX, finalY);
        }
        if (TryParseFloatBits(p[9], out float dirX)
            && TryParseFloatBits(p[10], out float dirY))
        {
            fleet.FinalDirection = new Vector2(dirX, dirY);
        }

        if (authoritativeName.NotEmpty() && !string.Equals(fleet.Name, authoritativeName, StringComparison.Ordinal))
            fleet.Name = authoritativeName;
    }

    static Ship FindOrMaterializeShipForReplay(UniverseState universe, string[] lines, int shipId)
    {
        Ship ship = universe.Objects.FindShip(shipId);
        if (ship != null)
            return ship;

        string prefix = $"SC|{shipId}|";
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                MaterializeMissingShip(universe, line);
                universe.Objects.UpdateLists(removeInactiveObjects: false);
                return universe.Objects.FindShip(shipId);
            }
        }
        return null;
    }

    static void ApplyColonizationGoalPayload(UniverseState universe, string[] lines)
    {
        var desired = new Dictionary<int, List<ColonizationGoalRuntime>>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (!line.StartsWith("G|", StringComparison.Ordinal))
                continue;

            string[] p = line.Split('|');
            if (p.Length < 5
                || !string.Equals(p[2], "MarkForColonization", StringComparison.Ordinal)
                || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int empireId)
                || !int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetPlanetId))
            {
                continue;
            }

            int finishedShipId = 0;
            if (p.Length >= 6)
                int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out finishedShipId);

            if (!desired.TryGetValue(empireId, out List<ColonizationGoalRuntime> goals))
            {
                goals = new List<ColonizationGoalRuntime>();
                desired[empireId] = goals;
            }

            goals.Add(new ColonizationGoalRuntime(targetPlanetId, ParseFlag(p[4]), finishedShipId));
        }

        foreach (Empire empire in universe.Empires)
        {
            MarkForColonization[] existing = empire.AI.FindGoals<MarkForColonization>();
            for (int i = 0; i < existing.Length; ++i)
                empire.AI.RemoveGoal(existing[i]);

            if (!desired.TryGetValue(empire.Id, out List<ColonizationGoalRuntime> goals))
                continue;

            foreach (ColonizationGoalRuntime runtime in goals
                         .OrderBy(g => g.TargetPlanetId)
                         .ThenBy(g => g.FinishedShipId))
            {
                Planet target = universe.GetPlanet(runtime.TargetPlanetId);
                if (target == null)
                    continue;

                var goal = new MarkForColonization(empire)
                {
                    TargetPlanet = target,
                    IsManualColonizationOrder = runtime.IsManual,
                    FinishedShip = runtime.FinishedShipId != 0
                        ? universe.Objects.FindShip(runtime.FinishedShipId)
                        : null,
                };
                empire.AI.AddGoal(goal);
            }
        }
    }

    static void ApplyDeepSpaceGoalPayload(UniverseState universe, string[] lines)
    {
        var desired = new Dictionary<int, List<DeepSpaceGoalRuntime>>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (!line.StartsWith("G|", StringComparison.Ordinal))
                continue;

            string[] p = line.Split('|');
            if (p.Length < 14
                || !string.Equals(p[2], "DeepSpace", StringComparison.Ordinal)
                || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int empireId)
                || !int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int goalType)
                || !int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int step)
                || !int.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetPlanetId)
                || !int.TryParse(p[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetSystemId)
                || !int.TryParse(p[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetShipId)
                || !int.TryParse(p[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out int planetBuildingAtId)
                || !TryParseFloatBits(p[10], out float buildX)
                || !TryParseFloatBits(p[11], out float buildY)
                || !TryParseFloatBits(p[12], out float moveX)
                || !TryParseFloatBits(p[13], out float moveY))
            {
                continue;
            }

            if (!desired.TryGetValue(empireId, out List<DeepSpaceGoalRuntime> goals))
            {
                goals = new List<DeepSpaceGoalRuntime>();
                desired[empireId] = goals;
            }
            goals.Add(new DeepSpaceGoalRuntime((GoalType)goalType, step, p[5] ?? "", targetPlanetId,
                targetSystemId, targetShipId, planetBuildingAtId, new Vector2(buildX, buildY),
                new Vector2(moveX, moveY)));
        }

        foreach (Empire empire in universe.Empires)
        {
            Goal[] existing = empire.AI.Goals.Where(IsDeepSpaceBuildStateGoal).ToArray();
            for (int i = 0; i < existing.Length; ++i)
                empire.AI.RemoveGoal(existing[i]);

            if (!desired.TryGetValue(empire.Id, out List<DeepSpaceGoalRuntime> goals))
                continue;

            foreach (DeepSpaceGoalRuntime runtime in goals
                         .OrderBy(g => (int)g.Type)
                         .ThenBy(g => g.ToBuildName, StringComparer.Ordinal)
                         .ThenBy(g => g.TargetPlanetId)
                         .ThenBy(g => g.TargetSystemId)
                         .ThenBy(g => g.BuildPosition.X)
                         .ThenBy(g => g.BuildPosition.Y))
            {
                Goal goal = CreatePassiveDeepSpaceGoal(universe, empire, runtime);
                if (goal != null)
                    empire.AI.AddGoal(goal);
            }
        }
    }

    static Goal CreatePassiveDeepSpaceGoal(UniverseState universe, Empire empire, DeepSpaceGoalRuntime runtime)
    {
        Goal goal = runtime.Type switch
        {
            GoalType.DeepSpaceConstruction => new BuildConstructionShip(empire),
            GoalType.BuildOrbital => new BuildOrbital(empire),
            GoalType.ProcessResearchStation => new ProcessResearchStation(empire),
            GoalType.MiningOps => new MiningOps(empire),
            _ => new DeepSpaceBuildGoal(runtime.Type, empire),
        };

        if (goal is DeepSpaceBuildGoal deepSpace)
        {
            deepSpace.Build = runtime.ToBuildName.NotEmpty() ? new BuildableShip(runtime.ToBuildName) : null;
            deepSpace.StaticBuildPos = runtime.BuildPosition;
            deepSpace.SetAuthoritativeReplayMovePosition(runtime.MovePosition);
            deepSpace.TargetSystem = runtime.TargetSystemId != 0
                ? universe.Systems.FirstOrDefault(s => s.Id == runtime.TargetSystemId)
                : null;
            deepSpace.FinishedShip = runtime.TargetShipId != 0 ? universe.Objects.FindShip(runtime.TargetShipId) : null;
        }

        if (runtime.TargetPlanetId != 0)
            TrySetGoalPlanet(() => goal.TargetPlanet = universe.GetPlanet(runtime.TargetPlanetId));
        if (runtime.PlanetBuildingAtId != 0)
            TrySetGoalPlanet(() => goal.PlanetBuildingAt = universe.GetPlanet(runtime.PlanetBuildingAtId));
        if (runtime.TargetShipId != 0)
            TrySetGoalPlanet(() => goal.TargetShip = universe.Objects.FindShip(runtime.TargetShipId));
        GoalStepField?.SetValue(goal, runtime.Step);
        return goal;
    }

    static void TrySetGoalPlanet(Action setGoalReference)
    {
        try
        {
            setGoalReference();
        }
        catch (InvalidOperationException)
        {
        }
    }

    internal static void ApplyRelationshipLine(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        if (p.Length < 10
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int empireId)
            || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetEmpireId)
            || empireId <= 0
            || targetEmpireId <= 0
            || empireId > universe.Empires.Count
            || targetEmpireId > universe.Empires.Count)
        {
            return;
        }

        Empire empire = universe.GetEmpireById(empireId);
        Empire target = universe.GetEmpireById(targetEmpireId);
        if (empire == null || target == null || empire == target)
            return;

        empire.GetRelations(target).SetAuthoritativeDiplomacyState(empire,
            ParseFlag(p[3]), ParseFlag(p[4]), ParseFlag(p[5]), ParseFlag(p[6]),
            ParseFlag(p[7]), ParseFlag(p[8]), ParseFlag(p[9]));
    }

    internal static void ApplySystemExplorationLine(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        if (p.Length < 4
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int systemId)
            || !uint.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint exploredMask)
            || !uint.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint fullyExploredMask))
        {
            return;
        }

        SolarSystem system = universe.Systems.FirstOrDefault(s => s.Id == systemId);
        if (system == null)
            return;

        system.SetExploredByMask(exploredMask);
        system.SetFullyExploredByMask(fullyExploredMask);
    }

    internal static void ApplyPlanetExplorationLine(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        if (p.Length < 3
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int planetId)
            || !uint.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint exploredMask))
        {
            return;
        }

        universe.GetPlanet(planetId)?.SetExploredByMask(exploredMask);
    }

    static bool ParseFlag(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int flag) && flag != 0;

    public static AuthoritativeStateSnapshot Capture(UniverseScreen universe, uint tick,
        DeterminismProfile profile = DeterminismProfile.ReplayWinX64Float)
    {
        (ulong lo, ulong hi, _) = universe.UState.ComputeAuthoritativeStateHash(profile);
        string payload = BuildPayload(universe.UState, tick);
        return new AuthoritativeStateSnapshot
        {
            Tick = tick,
            HashLo = lo,
            HashHi = hi,
            Payload = payload,
            SyncDigest = Digest(DurablePayload(payload)),
            TransformDigest = Digest(TransformPayload(payload)),
        };
    }

    static string BuildPayload(UniverseState us, uint tick)
    {
        var sb = new StringBuilder(4096);
        EmitPayloadStage(us, tick, sb, AuthoritativeReplicationEmitStage.PreScoped);

        foreach (Empire empire in us.Empires.OrderBy(e => e.Id))
        {
            foreach (ReplicatedRowDescriptor descriptor in AuthoritativeReplicationManifest.AllRows)
            {
                if (descriptor.EmitStage == AuthoritativeReplicationEmitStage.PerEmpire)
                    descriptor.EmitEmpire?.Invoke(empire, tick, sb);
            }
        }

        foreach (Planet planet in us.Planets.OrderBy(p => p.Id))
        {
            foreach (ReplicatedRowDescriptor descriptor in AuthoritativeReplicationManifest.AllRows)
            {
                if (descriptor.EmitStage == AuthoritativeReplicationEmitStage.PerPlanet)
                    descriptor.EmitPlanet?.Invoke(planet, tick, sb);
            }
        }

        EmitPayloadStage(us, tick, sb, AuthoritativeReplicationEmitStage.PostScoped);
        return sb.ToString();
    }

    static void EmitPayloadStage(UniverseState us, uint tick, StringBuilder sb,
        AuthoritativeReplicationEmitStage stage)
    {
        foreach (ReplicatedRowDescriptor descriptor in AuthoritativeReplicationManifest.AllRows)
        {
            if (descriptor.EmitStage == stage)
                descriptor.Emit?.Invoke(us, tick, sb);
        }
    }
    static Ship[] SnapshotShips(UniverseState us)
        => us.Ships
            .Concat(us.Empires.SelectMany(e => e.AllFleets
                .Where(f => f != null)
                .SelectMany(f => f.Ships)))
            .Where(s => s?.Active == true && !IsTransientEnvironmentShipForReplication(us, s))
            .Distinct()
            .OrderBy(s => s.Id)
            .ToArray();

    internal static bool IsTransientEnvironmentShipForReplication(UniverseState us, Ship ship)
        => ship != null
           && (ship.IsTransientEnvironment
               || (ship.IsMeteor && us?.Unknown != null && ReferenceEquals(ship.Loyalty, us.Unknown)));

    static int ShipKnownByMask(UniverseState us, Ship ship)
    {
        int mask = 0;
        foreach (Empire empire in us.Empires)
        {
            int bit = empire.Id - 1;
            if (bit is < 0 or >= 30)
                continue;
            if (ship.Loyalty == empire || ship.KnownByEmpires?.KnownBy(empire) == true)
                mask |= 1 << bit;
        }
        return mask;
    }

    static int SnapshotFleetId(Fleet fleet)
        => fleet == null
            ? 0
            : fleet.Key > 0
                ? fleet.Key
                : SnapshotFleetCommandShipId(fleet) != 0
                    ? SnapshotFleetCommandShipId(fleet)
                    : fleet.Ships.FirstOrDefault(s => s?.Fleet == fleet)?.Id ?? 0;

    static int SnapshotFleetCommandShipId(Fleet fleet)
        => fleet?.CommandShip?.Fleet == fleet ? fleet.CommandShip.Id : 0;

    static string DesignBase64(IShipDesign design)
        => design is ShipDesign shipDesign ? shipDesign.GetBase64DesignString() : "";

    static string GroundTroopRef(Planet planet, Troop troop)
    {
        if (troop == null)
            return "-";
        foreach (PlanetGridSquare tile in planet.TilesList.OrderBy(t => t.X).ThenBy(t => t.Y))
        {
            for (int i = 0; i < tile.TroopsHere.Count; ++i)
            {
                if (ReferenceEquals(tile.TroopsHere[i], troop))
                    return string.Create(CultureInfo.InvariantCulture,
                        $"{tile.X},{tile.Y},{i},{troop.Loyalty?.Id ?? 0},{FloatBits(troop.Strength)}");
            }
        }
        return string.Create(CultureInfo.InvariantCulture,
            $"off,{troop.Loyalty?.Id ?? 0},{FloatBits(troop.Strength)}");
    }

    static string GroundBuildingRef(Planet planet, Building building)
    {
        if (building == null)
            return "-";
        foreach (PlanetGridSquare tile in planet.TilesList.OrderBy(t => t.X).ThenBy(t => t.Y))
        {
            if (ReferenceEquals(tile.Building, building))
                return string.Create(CultureInfo.InvariantCulture,
                    $"{tile.X},{tile.Y},{building.Name},{FloatBits(building.Strength)},{building.CombatStrength}");
        }
        return string.Create(CultureInfo.InvariantCulture,
            $"off,{building.Name},{FloatBits(building.Strength)},{building.CombatStrength}");
    }

    static string Digest(string payload)
    {
        var h = DetHash.New();
        h.AddString(payload);
        return "0x" + h.Value.ToString("X16", CultureInfo.InvariantCulture);
    }

    static string DurablePayload(string payload)
        => FilterPayload(payload, AuthoritativeReplicationDigestPolicy.Fatal);

    static string TransformPayload(string payload)
        => FilterPayload(payload, AuthoritativeReplicationDigestPolicy.Transform);

    internal static string FirstFatalPayloadDifferenceForLog(string authorityPayload, string clientPayload)
    {
        List<ProjectedPayloadLine> authority = ProjectedFatalPayloadLines(authorityPayload);
        List<ProjectedPayloadLine> client = ProjectedFatalPayloadLines(clientPayload);
        int count = Math.Max(authority.Count, client.Count);
        for (int i = 0; i < count; ++i)
        {
            bool hasAuthority = i < authority.Count;
            bool hasClient = i < client.Count;
            string authorityProjection = hasAuthority ? authority[i].DigestLine : "<missing>";
            string clientProjection = hasClient ? client[i].DigestLine : "<missing>";
            if (string.Equals(authorityProjection, clientProjection, StringComparison.Ordinal))
                continue;

            string authorityRaw = hasAuthority ? authority[i].RawLine : "<missing>";
            string clientRaw = hasClient ? client[i].RawLine : "<missing>";
            string authorityRawLine = hasAuthority
                ? authority[i].RawLineNumber.ToString(CultureInfo.InvariantCulture)
                : "<missing>";
            string clientRawLine = hasClient
                ? client[i].RawLineNumber.ToString(CultureInfo.InvariantCulture)
                : "<missing>";
            return $"firstDiff line={i + 1} authorityRawLine={authorityRawLine} " +
                   $"clientRawLine={clientRawLine} " +
                   $"{AuthoritativeReplicationManifest.DescribeDiff(authorityRaw, clientRaw)} " +
                   $"authorityProjected='{authorityProjection}' clientProjected='{clientProjection}' " +
                   $"authority='{authorityRaw}' client='{clientRaw}'";
        }
        return "payloads matched under fatal projection";
    }

    static List<ProjectedPayloadLine> ProjectedFatalPayloadLines(string payload)
    {
        string[] lines = (payload ?? "").Split('\n');
        var projected = new List<ProjectedPayloadLine>(lines.Length);
        for (int i = 0; i < lines.Length; ++i)
        {
            string raw = lines[i].TrimEnd('\r');
            string digestLine = FatalDigestLine(raw);
            if (string.IsNullOrEmpty(digestLine))
                continue;

            projected.Add(new ProjectedPayloadLine(i + 1, raw, digestLine));
        }
        return projected;
    }

    static string FilterPayload(string payload, AuthoritativeReplicationDigestPolicy digestPolicy)
    {
        if (string.IsNullOrEmpty(payload))
            return "";

        var sb = new StringBuilder(payload.Length);
        foreach (string rawLine in payload.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            string digestLine = DigestLineForPolicy(line, digestPolicy);
            if (string.IsNullOrEmpty(digestLine))
            {
                continue;
            }
            sb.Append(digestLine).AppendLine();
        }
        return sb.ToString();
    }

    static string DigestLineForPolicy(string line, AuthoritativeReplicationDigestPolicy digestPolicy)
    {
        if (string.IsNullOrEmpty(line))
            return "";

        if (digestPolicy == AuthoritativeReplicationDigestPolicy.Fatal)
            return FatalDigestLine(line);

        return AuthoritativeReplicationManifest.DigestPolicyForLine(line) == digestPolicy ? line : "";
    }

    static string FatalDigestLine(string line)
    {
        ReplicatedRowDescriptor descriptor = AuthoritativeReplicationManifest.DescriptorForLine(line);
        if (descriptor?.DigestPolicy == AuthoritativeReplicationDigestPolicy.HostOnlyDiagnostic)
            return "";

        string[] p = line.Split('|');
        if (p.Length == 0)
            return "";

        if (FatalDigestColumnsByPrefix.TryGetValue(p[0], out int[] columns))
            return ProjectDigestColumns(line, p, columns);

        return AuthoritativeReplicationManifest.DigestPolicyForLine(line)
               == AuthoritativeReplicationDigestPolicy.Fatal
            ? line
            : "";
    }

    static string ProjectDigestColumns(string line, string[] parts, int[] columns)
    {
        if (columns.Length == 0 || columns.Any(column => column < 0 || column >= parts.Length))
            return line;

        var sb = new StringBuilder(line.Length);
        for (int i = 0; i < columns.Length; ++i)
        {
            if (i != 0)
                sb.Append('|');
            sb.Append(parts[columns[i]]);
        }
        return sb.ToString();
    }

    static string BlueprintSignature(Planet planet)
    {
        if (!planet.HasBlueprints)
            return "";

        var blueprints = planet.Blueprints;
        return string.Join("|",
            blueprints.Name ?? "",
            blueprints.ModName ?? "",
            blueprints.Exclusive ? "1" : "0",
            blueprints.LinkedBlueprintsName ?? "",
            ((int)blueprints.ColonyType).ToString(CultureInfo.InvariantCulture),
            blueprints.PercentCompleted.ToString(CultureInfo.InvariantCulture),
            blueprints.PercentAchievable.ToString(CultureInfo.InvariantCulture),
            string.Join(",", blueprints.PlannedBuildingNames
                .OrderBy(name => name, StringComparer.Ordinal)),
            string.Join(",", blueprints.PlannedBuildingsWeCanBuild
                .Select(building => building.Name)
                .OrderBy(name => name, StringComparer.Ordinal)));
    }

    static string TradeRouteSignature(Ship ship)
    {
        if (ship?.TradeRoutes == null || ship.TradeRoutes.Count == 0)
            return "";

        return string.Join(",", ship.TradeRoutes.OrderBy(id => id));
    }

    static string AreaOfOperationSignature(Ship ship)
    {
        if (ship?.AreaOfOperation == null || ship.AreaOfOperation.Count == 0)
            return "";

        return string.Join(";", ship.AreaOfOperation
            .OrderBy(r => r.X)
            .ThenBy(r => r.Y)
            .ThenBy(r => r.Width)
            .ThenBy(r => r.Height)
            .Select(r => string.Create(CultureInfo.InvariantCulture,
                $"{r.X},{r.Y},{r.Width},{r.Height}")));
    }

    static string DesignSlotSignature(IShipDesign design)
    {
        if (design is not ShipDesign shipDesign)
            return string.Join(",", design.UniqueModuleUIDs.OrderBy(uid => uid, StringComparer.Ordinal));

        DesignSlot[] slots = shipDesign.GetOrLoadDesignSlots() ?? Empty<DesignSlot>.Array;
        var sb = new StringBuilder(slots.Length * 32);
        for (int i = 0; i < slots.Length; ++i)
        {
            DesignSlot s = slots[i];
            if (i != 0) sb.Append(';');
            sb.Append(s.Pos.X).Append(',').Append(s.Pos.Y)
              .Append(',').Append(s.Size.X).Append('x').Append(s.Size.Y)
              .Append(',').Append((int)s.ModuleRot)
              .Append(',').Append(s.TurretAngle)
              .Append(',').Append(s.ModuleUID ?? "")
              .Append(',').Append(s.HangarShipUID ?? "");
        }
        return sb.ToString();
    }

    static int SnapshotShipId(Ship ship, HashSet<int> activeShipIds)
        => ship != null && (activeShipIds == null || activeShipIds.Contains(ship.Id)) ? ship.Id : 0;

    static string TargetQueueSignature(Ship ship)
        => TargetQueueSignature(ship, activeShipIds: null);

    static string TargetQueueSignature(Ship ship, HashSet<int> activeShipIds)
        => string.Join(",", ship.AI.TargetQueue
            .Select(s => SnapshotShipId(s, activeShipIds))
            .Where(id => id > 0));

    static string ResearchQueueSignature(Empire empire)
        => string.Join(",", empire.data.ResearchQueue);

    static uint ResearchProgress(Empire empire)
    {
        string topic = empire.Research.Topic ?? "";
        return topic.NotEmpty()
               && empire.TryGetTechEntry(topic, out TechEntry tech)
               && tech != TechEntry.None
            ? FloatBits(tech.Progress)
            : FloatBits(0f);
    }

    static AuthoritativeEmpireAutomationFlags AutomationFlags(Empire e)
    {
        var flags = AuthoritativeEmpireAutomationFlags.None;
        if (e.AutoPickConstructors) flags |= AuthoritativeEmpireAutomationFlags.AutoPickConstructors;
        if (e.AutoPickBestColonizer) flags |= AuthoritativeEmpireAutomationFlags.AutoPickBestColonizer;
        if (e.AutoPickBestFreighter) flags |= AuthoritativeEmpireAutomationFlags.AutoPickBestFreighter;
        if (e.AutoResearch) flags |= AuthoritativeEmpireAutomationFlags.AutoResearch;
        if (e.AutoBuildTerraformers) flags |= AuthoritativeEmpireAutomationFlags.AutoBuildTerraformers;
        if (e.AutoTaxes) flags |= AuthoritativeEmpireAutomationFlags.AutoTaxes;
        if (e.AutoPickBestResearchStation) flags |= AuthoritativeEmpireAutomationFlags.AutoPickBestResearchStation;
        if (e.AutoPickBestMiningStation) flags |= AuthoritativeEmpireAutomationFlags.AutoPickBestMiningStation;
        if (e.AutoExplore) flags |= AuthoritativeEmpireAutomationFlags.AutoExplore;
        if (e.AutoColonize) flags |= AuthoritativeEmpireAutomationFlags.AutoColonize;
        if (e.AutoBuildSpaceRoads) flags |= AuthoritativeEmpireAutomationFlags.AutoBuildSpaceRoads;
        if (e.AutoFreighters) flags |= AuthoritativeEmpireAutomationFlags.AutoFreighters;
        if (e.AutoBuildResearchStations) flags |= AuthoritativeEmpireAutomationFlags.AutoBuildResearchStations;
        if (e.AutoBuildMiningStations) flags |= AuthoritativeEmpireAutomationFlags.AutoBuildMiningStations;
        if (e.AutoMilitary) flags |= AuthoritativeEmpireAutomationFlags.AutoMilitary;
        if (e.RushAllConstruction) flags |= AuthoritativeEmpireAutomationFlags.RushAllConstruction;
        return flags;
    }

    static AuthoritativeUniversePreferenceFlags UniversePreferenceFlags(UniverseState us)
    {
        var flags = AuthoritativeUniversePreferenceFlags.None;
        if (us.P.AllowPlayerInterTrade) flags |= AuthoritativeUniversePreferenceFlags.AllowPlayerInterTrade;
        if (us.P.PrioitizeProjectors) flags |= AuthoritativeUniversePreferenceFlags.PrioritizeProjectors;
        return flags;
    }

    static bool IsDeepSpaceBuildStateGoal(Goal goal)
        => goal is DeepSpaceBuildGoal or ProcessResearchStation or MiningOps;

    static SolarSystem DeepSpaceGoalSystem(Goal goal)
        => goal switch
        {
            DeepSpaceBuildGoal deepSpace => deepSpace.TargetSystem,
            ProcessResearchStation research => research.TargetSystem,
            MiningOps mining => mining.TargetSystem,
            _ => null,
        };

    static int FleetGoalNodeIndex(FleetRequisition goal)
    {
        if (goal?.Fleet == null)
            return -1;
        for (int i = 0; i < goal.Fleet.DataNodes.Count; ++i)
            if (goal.Fleet.DataNodes[i].Goal == goal)
                return i;
        return -1;
    }

    static string FleetShipSignature(Fleet fleet)
        => "";

    static string FleetNodeSignature(Fleet fleet)
        => "";

    static string FleetPatrolSignature(Fleet fleet)
    {
        if (fleet?.HasPatrolPlan != true)
            return "none";

        FleetPatrol patrol = fleet.Patrol;
        return FleetPatrolPlanSignature(patrol);
    }

    static string FleetPatrolPlanSignature(FleetPatrol patrol)
    {
        WayPoint[] waypoints = patrol.WayPoints.ToArray();
        var sb = new StringBuilder(64 + waypoints.Length * 32);
        sb.Append(patrol.Name ?? "")
          .Append(',').Append(patrol.CurrentWaypointIndexForSync)
          .Append(',').Append(waypoints.Length);
        for (int i = 0; i < waypoints.Length; ++i)
        {
            WayPoint wp = waypoints[i];
            sb.Append(';')
              .Append(FloatBits(wp.Position.X).ToString("X8", CultureInfo.InvariantCulture)).Append(',')
              .Append(FloatBits(wp.Position.Y).ToString("X8", CultureInfo.InvariantCulture)).Append(',')
              .Append(FloatBits(wp.Direction.X).ToString("X8", CultureInfo.InvariantCulture)).Append(',')
              .Append(FloatBits(wp.Direction.Y).ToString("X8", CultureInfo.InvariantCulture)).Append(',')
              .Append(wp.IsDetour ? 1 : 0);
        }
        return sb.ToString();
    }

    static string ShipOrderQueueSignature(Ship ship)
        => ShipOrderQueueSignature(ship, activeShipIds: null);

    static string ShipOrderQueueSignature(Ship ship, HashSet<int> activeShipIds)
    {
        ShipAI.ShipGoal[] goals = ship.AI.OrderQueue.ToArray();
        return string.Join(";", goals
            .Where(g => g != null && !IsVolatileReplayOnlyPlan(g.Plan))
            .Select(g =>
            {
                Ship signatureTarget = g.TargetShip;
                if (signatureTarget == null && g.Plan == ShipAI.Plan.DoCombat)
                    signatureTarget = ship.AI.Target;

                int targetShipId = SnapshotShipId(signatureTarget, activeShipIds);
                if (signatureTarget != null && targetShipId == 0)
                    return null;

                // Targeted goals are durable by target ID; their MovePosition is derived from
                // local planet/ship state and can drift during replay/resync.
                Vector2 movePosition = (g.TargetPlanet != null || signatureTarget != null)
                    ? Vector2.Zero
                    : g.MovePosition;
                return $"{(int)g.Plan},{g.TargetPlanet?.Id ?? 0},{targetShipId}," +
                       $"{FloatBits(movePosition.X):X8},{FloatBits(movePosition.Y):X8}," +
                       $"{(int)g.MoveOrder},{(int)g.WantedState}";
            })
            .Where(signature => signature != null));
    }

    internal static string ShipOrderQueueSignatureForTest(Ship ship) => ShipOrderQueueSignature(ship);

    static bool IsVolatileReplayOnlyPlan(ShipAI.Plan plan)
        => IsVolatileMovementSolverPlan(plan) || IsUnreplayableFreightPlan(plan);

    static bool IsVolatileMovementSolverPlan(ShipAI.Plan plan)
        => plan is ShipAI.Plan.RotateToFaceMovePosition
            or ShipAI.Plan.RotateToDesiredFacing
            or ShipAI.Plan.MoveToWithin1000
            or ShipAI.Plan.MakeFinalApproach
            or ShipAI.Plan.RotateInlineWithVelocity;

    static bool IsUnreplayableFreightPlan(ShipAI.Plan plan)
        => plan is ShipAI.Plan.PickupGoods
            or ShipAI.Plan.DropOffGoods
            or ShipAI.Plan.PickupGoodsForStation
            or ShipAI.Plan.DropOffGoodsForStation;

    internal static void ApplyShipRuntimeLine(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        if (p.Length < 32
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int shipId))
        {
            return;
        }

        Ship ship = universe.Objects.FindShip(shipId);
        if (ship?.AI == null)
            return;

        if (int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int loyaltyId))
            ApplyShipLoyalty(universe, ship, loyaltyId);

        if (int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fleetId)
            && int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fleetKey))
        {
            ApplyShipFleetMembership(ship, fleetId, fleetKey);
        }

        if (int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int aiState)
            && Enum.IsDefined(typeof(AIState), aiState))
        {
            ship.AI.State = (AIState)aiState;
        }

        if (int.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int combatState)
            && Enum.IsDefined(typeof(CombatState), combatState))
        {
            ship.AI.CombatState = (CombatState)combatState;
        }

        if (TryParseFloatBits(p[11], out float scuttleTimer))
            ship.ScuttleTimer = scuttleTimer;

        ship.AI.Target = int.TryParse(p[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetId)
            && targetId > 0
                ? universe.Objects.FindShip(targetId)
                : null;
        ship.AI.HasPriorityTarget = ParseFlag(p[13]);
        ApplyTargetQueueSignature(universe, ship, p[14]);
        ship.AI.EscortTarget = int.TryParse(p[15], NumberStyles.Integer, CultureInfo.InvariantCulture, out int escortId)
            && escortId > 0
                ? universe.Objects.FindShip(escortId)
                : null;
        ApplyShipOrderQueueSignature(universe, ship, p[16]);
        ApplyShipPolicyFields(ship, p);
    }

    static void ApplyShipLoyalty(UniverseState universe, Ship ship, int loyaltyId)
    {
        Empire newLoyalty = universe.Empires.FirstOrDefault(e => e.Id == loyaltyId);
        Empire oldLoyalty = ship.Loyalty;
        if (newLoyalty == null || newLoyalty == oldLoyalty)
            return;

        RemoveShipFromAuthoritativeFleetCaches(ship);
        ship.Loyalty = newLoyalty;
        if (oldLoyalty != null)
            universe.UpdateShipInfluence(ship, oldLoyalty, newLoyalty);

        (oldLoyalty as IEmpireShipLists)?.RemoveShipAtEndOfTurn(ship);
        (newLoyalty as IEmpireShipLists)?.AddNewShipAtEndOfTurn(ship);
        ship.ShipStatusChanged = true;
        ship.ReinsertSpatial = true;
    }

    static void ApplyShipPolicyFields(Ship ship, string[] p)
    {
        ship.TransportingFood = ParseFlag(p[18]);
        ship.TransportingProduction = ParseFlag(p[19]);
        ship.TransportingColonists = ParseFlag(p[20]);
        ship.AllowInterEmpireTrade = ParseFlag(p[21]);

        if (ship.Carrier != null)
        {
            bool fightersOut = ParseFlag(p[23]);
            bool troopsOut = ParseFlag(p[25]);
            if (ship.Carrier.FightersOut != fightersOut)
                ship.Carrier.FightersOut = fightersOut;
            if (ship.Carrier.TroopsOut != troopsOut)
                ship.Carrier.TroopsOut = troopsOut;
            ship.Carrier.SetRecallFightersBeforeFTL(ParseFlag(p[26]));
            ship.Carrier.SetSendTroopsToShip(ParseFlag(p[27]));
            ship.Carrier.AllowBoardShip = ParseFlag(p[28]);
        }
        ship.ManualHangarOverride = ParseFlag(p[29]);
        ApplyShipTradeRouteSignature(ship, p[30]);
        ApplyShipAreaOfOperationSignature(ship, p[31]);
    }

    static void ApplyShipTradeRouteSignature(Ship ship, string signature)
    {
        var tradeRoutes = new Array<int>();
        if (!string.IsNullOrEmpty(signature))
        {
            foreach (string token in signature.Split(','))
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int planetId))
                    tradeRoutes.Add(planetId);
        }
        ship.DownloadTradeRoutes(tradeRoutes);
    }

    static void ApplyShipAreaOfOperationSignature(Ship ship, string signature)
    {
        ship.AreaOfOperation ??= new Array<Rectangle>();
        ship.AreaOfOperation.Clear();
        if (string.IsNullOrEmpty(signature))
            return;

        foreach (string rectText in signature.Split(';'))
        {
            string[] values = rectText.Split(',');
            if (values.Length != 4
                || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                || !int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
                || !int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
                || !int.TryParse(values[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
            {
                continue;
            }
            ship.AreaOfOperation.Add(new Rectangle(x, y, width, height));
        }
    }

    static void ApplyShipFleetMembership(Ship ship, int fleetId, int fleetKey)
    {
        if (fleetId <= 0 && fleetKey <= 0)
        {
            RemoveShipFromAuthoritativeFleetCaches(ship);
            return;
        }

        Empire empire = ship.Loyalty;
        Fleet fleet = empire?.AllFleets.FirstOrDefault(f => f != null && f.Id == fleetId)
                      ?? empire?.GetFleetOrNull(fleetKey);
        if (fleet == null)
            return;

        RemoveShipFromAuthoritativeFleetCaches(ship, except: fleet);
        if (fleet.Ships.ContainsRef(ship))
        {
            ship.Fleet = fleet;
            return;
        }

        ship.Fleet = null;
        fleet.AddShip(ship);
    }

    static void RemoveShipFromAuthoritativeFleetCaches(Ship ship, Fleet except = null)
    {
        Empire empire = ship?.Loyalty;
        if (empire == null)
            return;

        foreach (Fleet fleet in empire.AllFleets.Where(f => f != null && f != except))
        {
            fleet.Ships.RemoveRef(ship);
            for (int i = 0; i < fleet.DataNodes.Count; ++i)
                if (fleet.DataNodes[i].Ship == ship)
                    fleet.DataNodes[i].Ship = null;

            foreach (Array<Fleet.Squad> flank in fleet.AllFlanks)
            {
                foreach (Fleet.Squad squad in flank)
                {
                    squad.Ships.RemoveRef(ship);
                    for (int i = 0; i < squad.DataNodes.Count; ++i)
                        if (squad.DataNodes[i].Ship == ship)
                            squad.DataNodes[i].Ship = null;
                }
            }
        }

        if (ship.Fleet != except)
        {
            ship.Fleet = null;
            ship.CatchingUpToFleet = false;
        }
    }

    static void ApplyShipRuntimePayload(UniverseState universe, string[] lines)
    {
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith("S|", StringComparison.Ordinal))
                ApplyShipRuntimeLine(universe, line);
            else if (line.StartsWith("SX|", StringComparison.Ordinal))
                ApplyShipTransformLine(universe, line);
            else if (line.StartsWith("SV|", StringComparison.Ordinal))
                ApplyShipVisibilityLine(universe, line);
        }
    }

    static void ApplyShipTroopPayload(UniverseState universe, string[] lines)
    {
        var desired = new Dictionary<int, List<string>>();
        var shipIds = new HashSet<int>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if ((line.StartsWith("S|", StringComparison.Ordinal)
                 || line.StartsWith("SX|", StringComparison.Ordinal))
                && int.TryParse(line.Split('|')[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int snapshotShipId))
            {
                shipIds.Add(snapshotShipId);
                continue;
            }

            if (!line.StartsWith("ST|", StringComparison.Ordinal))
                continue;

            string[] troopParts = line.Split('|');
            if (troopParts.Length < 10
                || !int.TryParse(troopParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int shipId))
            {
                continue;
            }

            if (!desired.TryGetValue(shipId, out List<string> rows))
            {
                rows = new List<string>();
                desired[shipId] = rows;
            }
            shipIds.Add(shipId);
            rows.Add(line);
        }

        foreach (int shipId in shipIds.OrderBy(id => id))
        {
            Ship ship = universe.Objects.FindShip(shipId);
            if (ship == null)
                continue;

            desired.TryGetValue(shipId, out List<string> rows);
            rows ??= new List<string>();
            var existing = ship.GetOurTroops().ToList();
            foreach (Troop troop in existing)
            {
                ship.RemoveAnyTroop(troop);
                troop.SetShip(null);
            }

            var used = new HashSet<Troop>();
            foreach (string row in rows)
            {
                string[] p = row.Split('|');
                if (p.Length < 10)
                    continue;

                Troop troop = FindMatchingShipTroop(existing, used, p)
                              ?? CreateShipTroopForAuthoritativeReplay(universe, p);
                if (troop == null)
                    continue;

                used.Add(troop);
                ApplyTroopRuntime(universe, troop, p, loyaltyIndex: 3, nameIndex: 4,
                    strengthIndex: 5, moveActionsIndex: 6, attackActionsIndex: 7, moveTimerIndex: 8,
                    attackTimerIndex: 9);
                if (troop.Loyalty != null)
                    ship.AddTroop(troop);
            }
        }
    }

    static void ApplyColonyTilePayload(UniverseState universe, string[] lines)
    {
        var desired = new Dictionary<int, Dictionary<(int X, int Y), string[]>>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (!line.StartsWith("T|", StringComparison.Ordinal))
                continue;

            string[] p = line.Split('|');
            if (p.Length < 8
                || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int planetId)
                || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                || !int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
            {
                continue;
            }

            if (!desired.TryGetValue(planetId, out Dictionary<(int X, int Y), string[]> planetTiles))
            {
                planetTiles = new Dictionary<(int X, int Y), string[]>();
                desired[planetId] = planetTiles;
            }
            planetTiles[(x, y)] = p;
        }

        foreach (Planet planet in universe.Planets.OrderBy(p => p.Id))
        {
            desired.TryGetValue(planet.Id, out Dictionary<(int X, int Y), string[]> planetTiles);
            foreach (PlanetGridSquare tile in planet.TilesList
                         .Where(t => t.BuildingOnTile || t.Biosphere)
                         .OrderBy(t => t.X)
                         .ThenBy(t => t.Y))
            {
                if (planetTiles == null || !planetTiles.ContainsKey((tile.X, tile.Y)))
                    ApplyColonyTileState(planet, tile, buildingName: "", biosphere: false,
                        habitable: tile.Habitable, terraformable: tile.Terraformable);
            }

            if (planetTiles == null)
                continue;

            foreach (string[] row in planetTiles.Values
                         .OrderBy(p => int.Parse(p[2], CultureInfo.InvariantCulture))
                         .ThenBy(p => int.Parse(p[3], CultureInfo.InvariantCulture)))
            {
                ApplyColonyTileRow(universe, row);
            }
        }
    }

    static void ApplyColonyTileLine(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        ApplyColonyTileRow(universe, p);
    }

    static void ApplyColonyTileRow(UniverseState universe, string[] p)
    {
        if (p.Length < 8
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int planetId)
            || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
        {
            return;
        }

        Planet planet = universe.GetPlanet(planetId);
        PlanetGridSquare tile = planet?.GetTileByCoordinates(x, y);
        if (tile == null)
            return;

        int strength = p.Length >= 9
                       && int.TryParse(p[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedStrength)
            ? parsedStrength
            : int.MinValue;
        int combatStrength = p.Length >= 10
                             && int.TryParse(p[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCombatStrength)
            ? parsedCombatStrength
            : int.MinValue;

        ApplyColonyTileState(planet, tile, p[4] ?? "", ParseFlag(p[5]),
            ParseFlag(p[6]), ParseFlag(p[7]), strength, combatStrength);
    }

    static void ApplyColonyTileState(Planet planet, PlanetGridSquare tile, string buildingName,
        bool biosphere, bool habitable, bool terraformable, int strength = int.MinValue,
        int combatStrength = int.MinValue)
    {
        buildingName ??= "";
        if (tile.Building != null && !string.Equals(tile.Building.Name ?? "", buildingName, StringComparison.Ordinal))
            planet.DestroyBuildingOn(tile);

        if (!string.IsNullOrEmpty(buildingName)
            && (tile.Building == null || !string.Equals(tile.Building.Name ?? "", buildingName, StringComparison.Ordinal))
            && ResourceManager.GetBuilding(buildingName, out Building _))
        {
            tile.PlaceBuilding(ResourceManager.CreateBuilding(planet, buildingName), planet);
        }

        if (biosphere && !tile.Biosphere)
        {
            if (tile.Habitable)
                tile.SetHabitable(false);
            if (ResourceManager.GetBuilding(Building.BiospheresId, out Building _))
                tile.PlaceBuilding(ResourceManager.CreateBuilding(planet, Building.BiospheresId), planet);
            tile.Biosphere = true;
        }
        else if (!biosphere && tile.Biosphere)
        {
            if (planet.FindBuilding(b => b.IsBiospheres) != null)
                planet.DestroyBioSpheres(tile, destroyBuilding: false);
            else
                tile.Biosphere = false;
        }

        tile.SetHabitable(habitable);
        tile.Terraformable = terraformable;
        if (tile.Building != null)
        {
            if (strength != int.MinValue)
                tile.Building.Strength = strength;
            if (combatStrength != int.MinValue)
                tile.Building.CombatStrength = combatStrength;
        }
        planet.UpdatePlanetStatsByRecalculation();
    }

    internal static void ApplyGroundTroopLine(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        if (p.Length < 12
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int planetId)
            || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
            || !int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int troopIndex))
        {
            return;
        }

        Planet planet = universe.GetPlanet(planetId);
        PlanetGridSquare tile = planet?.GetTileByCoordinates(x, y);
        if (tile == null || troopIndex < 0 || troopIndex >= tile.TroopsHere.Count)
            return;

        ApplyTroopRuntime(universe, tile.TroopsHere[troopIndex], p, loyaltyIndex: 5, nameIndex: 6,
            strengthIndex: 7, moveActionsIndex: 8, attackActionsIndex: 9, moveTimerIndex: 10,
            attackTimerIndex: 11);
    }

    static void ApplyGroundTroopPayload(UniverseState universe, string[] lines)
    {
        var desired = new Dictionary<int, List<string>>();
        var planetIds = new HashSet<int>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith("P|", StringComparison.Ordinal))
            {
                string[] planetParts = line.Split('|');
                if (planetParts.Length >= 2
                    && int.TryParse(planetParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out int planetRowId))
                {
                    planetIds.Add(planetRowId);
                }
                continue;
            }
            if (!line.StartsWith("GT|", StringComparison.Ordinal))
                continue;

            string[] troopParts = line.Split('|');
            if (troopParts.Length < 12
                || !int.TryParse(troopParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int troopPlanetId))
            {
                continue;
            }

            if (!desired.TryGetValue(troopPlanetId, out List<string> planetRows))
            {
                planetRows = new List<string>();
                desired[troopPlanetId] = planetRows;
            }
            planetIds.Add(troopPlanetId);
            planetRows.Add(line);
        }

        foreach (int planetId in planetIds.OrderBy(id => id))
        {
            Planet planet = universe.GetPlanet(planetId);
            if (planet == null)
                continue;

            desired.TryGetValue(planetId, out List<string> rows);
            rows ??= new List<string>();
            var existing = planet.TilesList
                .SelectMany(tile => tile.TroopsHere)
                .Distinct()
                .ToList();
            foreach (PlanetGridSquare tile in planet.TilesList)
            {
                for (int i = tile.TroopsHere.Count - 1; i >= 0; --i)
                    planet.Troops.TryRemoveTroop(tile, tile.TroopsHere[i], quiet: true);
            }

            var used = new HashSet<Troop>();
            foreach (string row in rows)
            {
                string[] p = row.Split('|');
                if (p.Length < 12
                    || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                    || !int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
                {
                    continue;
                }

                PlanetGridSquare tile = planet.GetTileByCoordinates(x, y);
                Troop troop = tile != null
                    ? FindMatchingGroundTroop(existing, used, p)
                      ?? CreateGroundTroopForAuthoritativeReplay(universe, p)
                    : null;
                if (troop == null)
                    continue;

                used.Add(troop);
                ApplyTroopRuntime(universe, troop, p, loyaltyIndex: 5, nameIndex: 6,
                    strengthIndex: 7, moveActionsIndex: 8, attackActionsIndex: 9, moveTimerIndex: 10,
                    attackTimerIndex: 11);
                troop.SetPlanet(planet);
                troop.SetShip(null);
                if (troop.Loyalty != null)
                    planet.Troops.AddTroop(tile, troop);
                else
                    tile.AddTroop(troop);
            }
        }
    }

    static Troop CreateGroundTroopForAuthoritativeReplay(UniverseState universe, string[] p)
    {
        int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int loyaltyId);
        string name = p[6] ?? "";
        if (!ResourceManager.GetTroopTemplate(name, out Troop template))
            return null;

        Troop troop = template.Clone();
        if (troop.StrengthMax <= 0)
            troop.StrengthMax = troop.Strength;
        Empire loyalty = universe.Empires.FirstOrDefault(e => e.Id == loyaltyId);
        if (loyalty != null)
            troop.SetOwner(loyalty);
        return troop;
    }

    static Troop CreateShipTroopForAuthoritativeReplay(UniverseState universe, string[] p)
    {
        int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int loyaltyId);
        string name = p[4] ?? "";
        if (!ResourceManager.GetTroopTemplate(name, out Troop template))
            return null;

        Troop troop = template.Clone();
        if (troop.StrengthMax <= 0)
            troop.StrengthMax = troop.Strength;
        Empire loyalty = universe.Empires.FirstOrDefault(e => e.Id == loyaltyId);
        if (loyalty != null)
            troop.SetOwner(loyalty);
        return troop;
    }

    static Troop FindMatchingGroundTroop(List<Troop> existing, HashSet<Troop> used, string[] p)
    {
        int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int loyaltyId);
        string name = p[6] ?? "";
        Troop match = existing.FirstOrDefault(t => !used.Contains(t)
                                                   && (t.Loyalty?.Id ?? 0) == loyaltyId
                                                   && string.Equals(t.Name ?? "", name, StringComparison.Ordinal));
        match ??= existing.FirstOrDefault(t => !used.Contains(t)
                                               && string.Equals(t.Name ?? "", name, StringComparison.Ordinal));
        match ??= existing.FirstOrDefault(t => !used.Contains(t));
        return match;
    }

    static Troop FindMatchingShipTroop(List<Troop> existing, HashSet<Troop> used, string[] p)
    {
        int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int loyaltyId);
        string name = p[4] ?? "";
        Troop match = existing.FirstOrDefault(t => !used.Contains(t)
                                                   && (t.Loyalty?.Id ?? 0) == loyaltyId
                                                   && string.Equals(t.Name ?? "", name, StringComparison.Ordinal));
        match ??= existing.FirstOrDefault(t => !used.Contains(t)
                                               && string.Equals(t.Name ?? "", name, StringComparison.Ordinal));
        match ??= existing.FirstOrDefault(t => !used.Contains(t));
        return match;
    }

    static void ApplyGroundCombatPayload(UniverseState universe, string[] lines)
    {
        var desired = new Dictionary<int, List<string>>();
        var planetIds = new HashSet<int>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith("P|", StringComparison.Ordinal))
            {
                string[] planetParts = line.Split('|');
                if (planetParts.Length >= 2
                    && int.TryParse(planetParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out int planetRowId))
                {
                    planetIds.Add(planetRowId);
                }
                continue;
            }

            if (!line.StartsWith("GC|", StringComparison.Ordinal))
                continue;

            string[] p = line.Split('|');
            if (p.Length < 12
                || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int planetId)
                || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                || index < 0)
            {
                continue;
            }

            if (!desired.TryGetValue(planetId, out List<string> rows))
            {
                rows = new List<string>();
                desired[planetId] = rows;
            }
            planetIds.Add(planetId);
            rows.Add(line);
        }

        foreach (int planetId in planetIds.OrderBy(id => id))
        {
            Planet planet = universe.GetPlanet(planetId);
            if (planet == null)
                continue;

            planet.ActiveCombats.Clear();
            if (!desired.TryGetValue(planetId, out List<string> rows))
                continue;

            foreach (string row in rows
                         .OrderBy(r => int.Parse(r.Split('|')[2], CultureInfo.InvariantCulture)))
            {
                string[] p = row.Split('|');
                Combat created = CreateGroundCombatFromRow(universe, planet, p);
                if (created == null)
                    continue;

                ApplyGroundCombatRuntime(universe, planet, created, p);
                planet.ActiveCombats.Add(created);
            }
        }
    }

    static void ApplyGroundCombatRuntime(UniverseState universe, Planet planet, Combat combat, string[] p)
    {
        if (int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int phase))
            combat.Phase = phase;
        if (TryParseFloatBits(p[4], out float timer))
            combat.Timer = timer;
        if (int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int attackerLoyaltyId))
            combat.AttackerLoyalty = attackerLoyaltyId > 0 ? universe.GetEmpireById(attackerLoyaltyId) : null;
        if (int.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tileX)
            && int.TryParse(p[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tileY))
        {
            combat.DefenseTile = planet.GetTileByCoordinates(tileX, tileY);
        }
        combat.AttackingTroop = ResolveGroundTroopRef(planet, p[8]);
        combat.DefendingTroop = ResolveGroundTroopRef(planet, p[9]);
        combat.AttackingBuilding = ResolveAndApplyGroundBuildingRef(planet, p[10]);
        combat.DefendingBuilding = ResolveAndApplyGroundBuildingRef(planet, p[11]);
        combat.Planet = planet;
    }

    static Combat CreateGroundCombatFromRow(UniverseState universe, Planet planet, string[] p)
    {
        PlanetGridSquare defenseTile = null;
        if (int.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tileX)
            && int.TryParse(p[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tileY))
        {
            defenseTile = planet.GetTileByCoordinates(tileX, tileY);
        }
        Troop attackingTroop = ResolveGroundTroopRef(planet, p[8]);
        Troop defendingTroop = ResolveGroundTroopRef(planet, p[9]);
        Building attackingBuilding = ResolveAndApplyGroundBuildingRef(planet, p[10]);
        Building defendingBuilding = ResolveAndApplyGroundBuildingRef(planet, p[11]);

        if (attackingTroop != null && defendingTroop != null)
            return new Combat(attackingTroop, defendingTroop, defenseTile, planet);
        if (attackingBuilding != null && defendingTroop != null)
            return new Combat(attackingBuilding, defendingTroop, defenseTile, planet);
        if (attackingTroop != null && defendingBuilding != null)
            return new Combat(attackingTroop, defendingBuilding, defenseTile, planet);
        return null;
    }

    static Troop ResolveGroundTroopRef(Planet planet, string reference)
    {
        if (string.IsNullOrEmpty(reference) || reference == "-" || reference.StartsWith("off,", StringComparison.Ordinal))
            return null;

        string[] p = reference.Split(',');
        if (p.Length < 3
            || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
            || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            return null;
        }

        PlanetGridSquare tile = planet.GetTileByCoordinates(x, y);
        return tile != null && index >= 0 && index < tile.TroopsHere.Count ? tile.TroopsHere[index] : null;
    }

    static Building ResolveGroundBuildingRef(Planet planet, string reference)
    {
        if (string.IsNullOrEmpty(reference) || reference == "-" || reference.StartsWith("off,", StringComparison.Ordinal))
            return null;

        string[] p = reference.Split(',');
        if (p.Length < 3
            || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
        {
            return null;
        }

        return planet.GetTileByCoordinates(x, y)?.Building;
    }

    static Building ResolveAndApplyGroundBuildingRef(Planet planet, string reference)
    {
        Building building = ResolveGroundBuildingRef(planet, reference);
        if (building == null)
            return null;

        string[] p = reference.Split(',');
        if (p.Length >= 4 && TryParseFloatBits(p[3], out float strength))
            building.Strength = (int)Math.Round(strength, MidpointRounding.AwayFromZero);
        if (p.Length >= 5
            && int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int combatStrength))
        {
            building.CombatStrength = combatStrength;
        }
        return building;
    }

    static void ApplyShipTroopLine(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        if (p.Length < 10
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int shipId)
            || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int troopIndex))
        {
            return;
        }

        Ship ship = universe.Objects.FindShip(shipId);
        IReadOnlyList<Troop> troops = ship?.GetOurTroops();
        if (troops == null || troopIndex < 0 || troopIndex >= troops.Count)
            return;

        ApplyTroopRuntime(universe, troops[troopIndex], p, loyaltyIndex: 3, nameIndex: 4,
            strengthIndex: 5, moveActionsIndex: 6, attackActionsIndex: 7, moveTimerIndex: 8,
            attackTimerIndex: 9);
    }

    static void ApplyTroopRuntime(UniverseState universe, Troop troop, string[] p, int loyaltyIndex,
        int nameIndex, int strengthIndex, int moveActionsIndex, int attackActionsIndex, int moveTimerIndex,
        int attackTimerIndex)
    {
        if (troop == null)
            return;

        if (int.TryParse(p[loyaltyIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out int loyaltyId))
            troop.Loyalty = loyaltyId > 0 ? universe.GetEmpireById(loyaltyId) : null;
        troop.Name = p[nameIndex];
        if (TryParseFloatBits(p[strengthIndex], out float strength))
            troop.Strength = strength;
        if (int.TryParse(p[moveActionsIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out int moveActions))
            troop.AvailableMoveActions = moveActions;
        if (int.TryParse(p[attackActionsIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out int attackActions))
            troop.AvailableAttackActions = attackActions;
        if (TryParseFloatBits(p[moveTimerIndex], out float moveTimer))
            troop.MoveTimer = moveTimer;
        if (TryParseFloatBits(p[attackTimerIndex], out float attackTimer))
            troop.AttackTimer = attackTimer;
    }

    internal static void ApplyShipTransformLine(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        if (p.Length < 11
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int shipId)
            || !TryParseFloatBits(p[3], out float x)
            || !TryParseFloatBits(p[4], out float y)
            || !TryParseFloatBits(p[5], out float vx)
            || !TryParseFloatBits(p[6], out float vy)
            || !TryParseFloatBits(p[7], out float rotation))
        {
            return;
        }

        Ship ship = universe.Objects.FindShip(shipId);
        if (ship == null)
            return;

        SolarSystem system = ship.System;
        if (int.TryParse(p[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out int systemId))
            system = systemId > 0 ? universe.Systems.FirstOrDefault(s => s.Id == systemId) : null;
        bool active = ParseFlag(p[9]);
        bool dying = ParseFlag(p[10]);
        float yRotation = ship.YRotation;
        if (p.Length > 11 && TryParseFloatBits(p[11], out float parsedYRotation))
            yRotation = parsedYRotation;
        float xRotation = ship.XRotation;
        if (p.Length > 12 && TryParseFloatBits(p[12], out float parsedXRotation))
            xRotation = parsedXRotation;
        if (uint.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint tick))
            ship.ObservePassiveAuthoritativeTransform(tick, rotation, active, dying);

        bool refreshProjectorInfluence = ship.IsSubspaceProjector
            && ship.Loyalty != null
            && (BitConverter.SingleToInt32Bits(ship.Position.X) != BitConverter.SingleToInt32Bits(x)
                || BitConverter.SingleToInt32Bits(ship.Position.Y) != BitConverter.SingleToInt32Bits(y)
                || ship.Active != active);
        if (refreshProjectorInfluence)
            universe.Influence.Remove(ship.Loyalty, ship);

        ship.SetAuthoritativeTransform(new Vector2(x, y), new Vector2(vx, vy), rotation,
            system, active, dying, yRotation, xRotation);

        if (refreshProjectorInfluence && ship.Active)
            universe.Influence.Insert(ship.Loyalty, ship);
    }

    internal static void ApplyShipVisibilityLine(UniverseState universe, string line)
    {
        string[] p = line.Split('|');
        if (p.Length < 3
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int shipId)
            || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mask))
        {
            return;
        }

        Ship ship = universe.Objects.FindShip(shipId);
        ship?.KnownByEmpires?.SetKnownMask(universe, mask);
    }

    static void ApplyTargetQueueSignature(UniverseState universe, Ship ship, string signature)
    {
        ship.AI.TargetQueue.Clear();
        if (string.IsNullOrWhiteSpace(signature))
            return;

        foreach (string token in signature.Split(','))
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                && id > 0)
            {
                Ship target = universe.Objects.FindShip(id);
                if (target != null)
                    ship.AI.TargetQueue.Add(target);
            }
        }
    }

    static void ApplyShipOrderQueueSignature(UniverseState universe, Ship ship, string signature)
    {
        if (ShouldPreserveLocalMovementSolverQueue(ship, signature))
            return;

        if (string.IsNullOrWhiteSpace(signature))
        {
            ship.AI.OrderQueue.Clear();
            return;
        }

        var goals = new List<ShipAI.ShipGoal>();
        foreach (string part in signature.Split(';'))
        {
            if (TryParseShipGoalSignature(universe, ship, part, out ShipAI.ShipGoal goal))
                goals.Add(goal);
        }
        ship.AI.OrderQueue.SetRange(goals);
    }

    static bool ShouldPreserveLocalMovementSolverQueue(Ship ship, string authoritativeSignature)
    {
        if (ship?.AI == null)
            return false;

        ShipAI.ShipGoal[] localGoals = ship.AI.OrderQueue.ToArray();
        if (!localGoals.Any(g => g != null && IsVolatileMovementSolverPlan(g.Plan)))
            return false;

        return string.Equals(ShipOrderQueueSignature(ship), authoritativeSignature ?? "",
            StringComparison.Ordinal);
    }

    static bool TryParseShipGoalSignature(UniverseState universe, Ship ship, string signature,
        out ShipAI.ShipGoal goal)
    {
        goal = null;
        string[] p = (signature ?? "").Split(',');
        if (p.Length < 6
            || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int planValue)
            || !Enum.IsDefined(typeof(ShipAI.Plan), planValue)
            || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int planetId)
            || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetShipId)
            || !TryParseFloatHexBits(p[3], out float x)
            || !TryParseFloatHexBits(p[4], out float y)
            || !int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int moveOrderValue))
        {
            return false;
        }

        var plan = (ShipAI.Plan)planValue;
        if (IsUnreplayableFreightPlan(plan))
            return false;

        var moveOrder = (MoveOrder)moveOrderValue;
        Planet targetPlanet = planetId > 0 ? universe.GetPlanet(planetId) : null;
        Ship targetShip = targetShipId > 0 ? universe.Objects.FindShip(targetShipId) : null;
        var movePosition = new Vector2(x, y);
        AIState wantedState = ship.AI.State;
        if (p.Length >= 7
            && int.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int wantedStateValue)
            && Enum.IsDefined(typeof(AIState), wantedStateValue))
        {
            wantedState = (AIState)wantedStateValue;
        }

        if (targetPlanet != null || targetShip != null || moveOrder == MoveOrder.Regular)
        {
            goal = new ShipAI.ShipGoal(plan, movePosition, Vectors.Up, targetPlanet, null,
                0f, "", 0f, wantedState, targetShip);
        }
        else
        {
            goal = new ShipAI.ShipGoal(plan, movePosition, Vectors.Up, wantedState, moveOrder, 0f, null);
        }
        return true;
    }

    static bool TryParseFloatHexBits(string text, out float value)
    {
        value = 0f;
        if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint bits))
            return false;
        value = BitConverter.UInt32BitsToSingle(bits);
        return true;
    }

    static uint FloatBits(float value) => System.BitConverter.SingleToUInt32Bits(value);
    static bool TryParseByte(string text, out byte value)
        => byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    static bool TryParseFloatBits(string text, out float value)
    {
        value = 0f;
        if (!uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint bits))
            return false;
        value = BitConverter.UInt32BitsToSingle(bits);
        return true;
    }

    static void ApplyAutomationFlags(Empire empire, AuthoritativeEmpireAutomationFlags flags)
    {
        empire.SetAuthoritativeAutomationState(flags);
    }

    readonly struct ConstructionQueueRuntime
    {
        public readonly int PlanetId;
        public readonly int QueueIndex;
        public readonly bool IsShip;
        public readonly bool IsBuilding;
        public readonly bool IsTroop;
        public readonly int QueueType;
        public readonly string ShipName;
        public readonly string BuildingName;
        public readonly string TroopType;
        public readonly int TileX;
        public readonly int TileY;
        public readonly float Cost;
        public readonly float ProductionSpent;
        public readonly bool Rush;
        public readonly bool Canceled;

        public ConstructionQueueRuntime(int planetId, int queueIndex, bool isShip, bool isBuilding,
            bool isTroop, int queueType, string shipName, string buildingName, string troopType,
            int tileX, int tileY, float cost, float productionSpent, bool rush, bool canceled)
        {
            PlanetId = planetId;
            QueueIndex = queueIndex;
            IsShip = isShip;
            IsBuilding = isBuilding;
            IsTroop = isTroop;
            QueueType = queueType;
            ShipName = shipName ?? "";
            BuildingName = buildingName ?? "";
            TroopType = troopType ?? "";
            TileX = tileX;
            TileY = tileY;
            Cost = cost;
            ProductionSpent = productionSpent;
            Rush = rush;
            Canceled = canceled;
        }
    }

    readonly struct ColonizationGoalRuntime
    {
        public readonly int TargetPlanetId;
        public readonly bool IsManual;
        public readonly int FinishedShipId;

        public ColonizationGoalRuntime(int targetPlanetId, bool isManual, int finishedShipId)
        {
            TargetPlanetId = targetPlanetId;
            IsManual = isManual;
            FinishedShipId = finishedShipId;
        }
    }

    readonly struct DeepSpaceGoalRuntime
    {
        public readonly GoalType Type;
        public readonly int Step;
        public readonly string ToBuildName;
        public readonly int TargetPlanetId;
        public readonly int TargetSystemId;
        public readonly int TargetShipId;
        public readonly int PlanetBuildingAtId;
        public readonly Vector2 BuildPosition;
        public readonly Vector2 MovePosition;

        public DeepSpaceGoalRuntime(GoalType type, int step, string toBuildName, int targetPlanetId,
            int targetSystemId, int targetShipId, int planetBuildingAtId, Vector2 buildPosition,
            Vector2 movePosition)
        {
            Type = type;
            Step = step;
            ToBuildName = toBuildName ?? "";
            TargetPlanetId = targetPlanetId;
            TargetSystemId = targetSystemId;
            TargetShipId = targetShipId;
            PlanetBuildingAtId = planetBuildingAtId;
            BuildPosition = buildPosition;
            MovePosition = movePosition;
        }
    }

    readonly struct ProjectedPayloadLine
    {
        public readonly int RawLineNumber;
        public readonly string RawLine;
        public readonly string DigestLine;

        public ProjectedPayloadLine(int rawLineNumber, string rawLine, string digestLine)
        {
            RawLineNumber = rawLineNumber;
            RawLine = rawLine;
            DigestLine = digestLine;
        }
    }
}

public sealed class Authoritative4XAuthority
{
    readonly UniverseScreen Universe;
    readonly FixedSimTime Step;
    readonly Authoritative4XCommandApplicator Applicator;
    readonly AuthoritativeDiplomacyManager Diplomacy;

    public uint Tick { get; private set; }

    public Authoritative4XAuthority(UniverseScreen universe, float dt = 1f / 60f, int[] humanEmpireIds = null)
    {
        Universe = universe;
        Step = new FixedSimTime(dt);
        if (humanEmpireIds != null)
            AuthoritativeHumanPlayers.SetHumanControlledEmpires(universe.UState, humanEmpireIds);
        Diplomacy = new AuthoritativeDiplomacyManager(universe.UState);
        Applicator = new Authoritative4XCommandApplicator(universe.UState, Diplomacy);
    }

    public (AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot)
        Process(AuthoritativePlayerCommand command, bool captureSnapshot = true)
    {
        AuthoritativeCommandResult result = Applicator.Apply(command, Tick + 1);
        return Advance(result, captureSnapshot);
    }

    public (AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot)
        RejectAndAdvance(int sequence, string reason, bool captureSnapshot = true)
    {
        return Advance(new AuthoritativeCommandResult
        {
            Sequence = sequence,
            Accepted = false,
            Tick = Tick + 1,
            Reason = reason ?? "",
        }, captureSnapshot);
    }

    (AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot) Advance(
        AuthoritativeCommandResult result, bool captureSnapshot)
    {
        using (AuthoritativeMutationGuard.EnterAcceptedCommandApply())
            Universe.SingleSimulationStep(Step);
        Tick++;
        return (result, captureSnapshot ? AuthoritativeStateSnapshot.Capture(Universe, Tick) : null);
    }

    public AuthoritativeDiplomacyPopup[] DrainDiplomacyPopups() => Diplomacy.DrainPopups();
}

public sealed class Authoritative4XClientReplica
{
    readonly UniverseScreen Universe;
    readonly FixedSimTime Step;
    readonly Authoritative4XCommandApplicator Applicator;
    readonly AuthoritativeDiplomacyManager Diplomacy;
    readonly bool DetectLocalMutation;
    bool AuthoritativePaused;
    float AuthoritativeGameSpeed = 1f;

    public uint Tick { get; private set; }
    public AuthoritativeStateSnapshot LastSnapshot { get; private set; }
    public Authoritative4XRawHashDrift LastRawHashDrift { get; private set; }

    public Authoritative4XClientReplica(UniverseScreen universe, float dt = 1f / 60f,
        int[] humanEmpireIds = null, bool detectLocalMutation = true)
    {
        Universe = universe;
        Step = new FixedSimTime(dt);
        DetectLocalMutation = detectLocalMutation;
        AuthoritativePaused = universe?.UState?.Paused ?? false;
        AuthoritativeGameSpeed = ClampGameSpeed(universe?.UState?.GameSpeed ?? 1f);
        if (humanEmpireIds != null)
            AuthoritativeHumanPlayers.SetHumanControlledEmpires(universe.UState, humanEmpireIds);
        Diplomacy = new AuthoritativeDiplomacyManager(universe.UState);
        Applicator = new Authoritative4XCommandApplicator(universe.UState, Diplomacy);
    }

    public void ApplyAuthoritativeResult(AuthoritativePlayerCommand command, AuthoritativeCommandResult result,
        AuthoritativeStateSnapshot authoritySnapshot)
    {
        AssertReplicaUnchangedSinceLastSnapshot(command, result);
        authoritySnapshot.ApplyRelationshipPayload(Universe.UState);
        Universe.UState.Paused = AuthoritativePaused;
        Universe.UState.GameSpeed = AuthoritativeGameSpeed;
        if (result.Accepted)
        {
            if (!IsSnapshotOnlyReplay(command.Kind))
            {
                AuthoritativeCommandResult local = Applicator.ApplyTrustedHostAccepted(command, result.Tick);
                if (!local.Accepted)
                {
                    Log.Warning("Client replica could not locally apply host-accepted command "
                                + $"{command.Sequence} ({command.Kind}): {local.Reason}");
                }
            }
        }
        Empire localEmpireBefore = Universe.Player;
        int localEmpireId = localEmpireBefore?.Id ?? 0;
        HashSet<int> ownedPlanetsBefore = OwnedPlanetIds(localEmpireBefore);
        HashSet<string> unlockedTechsBefore = UnlockedTechIds(localEmpireBefore);
        authoritySnapshot.ApplyShipPresencePayload(Universe.UState);
        authoritySnapshot.ApplyEmpireRuntimePayload(Universe.UState);
        NotifyLocalAuthoritativeDeltas(localEmpireId, ownedPlanetsBefore, unlockedTechsBefore);
        Tick = authoritySnapshot.Tick;
        LastSnapshot = AuthoritativeStateSnapshot.Capture(Universe, Tick);
        bool rawHashMismatch = LastSnapshot.HashLo != authoritySnapshot.HashLo
                               || LastSnapshot.HashHi != authoritySnapshot.HashHi;
        if (!string.Equals(LastSnapshot.SyncDigest, authoritySnapshot.SyncDigest, StringComparison.Ordinal))
        {
            throw new Authoritative4XSyncMismatchException(command, result, authoritySnapshot, LastSnapshot);
        }
        if (!string.IsNullOrEmpty(authoritySnapshot.TransformDigest)
            && !string.Equals(LastSnapshot.TransformDigest, authoritySnapshot.TransformDigest, StringComparison.Ordinal))
        {
            throw new Authoritative4XSyncMismatchException(command, result, authoritySnapshot, LastSnapshot,
                "Authoritative transform mismatch");
        }
        LastRawHashDrift = rawHashMismatch
            ? new Authoritative4XRawHashDrift(command, result, authoritySnapshot, LastSnapshot)
            : null;
    }

    void NotifyLocalAuthoritativeDeltas(int localEmpireId, HashSet<int> ownedPlanetsBefore,
        HashSet<string> unlockedTechsBefore)
    {
        if (localEmpireId <= 0 || Universe.NotificationManager == null)
            return;

        Empire local = Universe.UState.GetEmpire(localEmpireId);
        if (local == null)
            return;

        foreach (Planet planet in local.GetPlanets().OrderBy(p => p.Id))
            if (!ownedPlanetsBefore.Contains(planet.Id))
                Universe.NotificationManager.AddColonizedNotification(planet, local);

        foreach (TechEntry tech in local.TechEntries
                     .Where(t => t?.Unlocked == true && !unlockedTechsBefore.Contains(t.UID))
                     .OrderBy(t => t.UID, StringComparer.Ordinal))
        {
            Universe.NotificationManager.AddResearchComplete(tech.UID, local);
        }
    }

    static HashSet<int> OwnedPlanetIds(Empire empire)
        => empire?.GetPlanets().Select(p => p.Id).ToHashSet()
           ?? new HashSet<int>();

    static HashSet<string> UnlockedTechIds(Empire empire)
        => empire?.TechEntries
               .Where(t => t?.Unlocked == true)
               .Select(t => t.UID)
               .ToHashSet(StringComparer.Ordinal)
           ?? new HashSet<string>(StringComparer.Ordinal);

    static bool IsSnapshotOnlyReplay(AuthoritativePlayerCommandKind kind)
        => kind is AuthoritativePlayerCommandKind.DiplomacyProposal
                   or AuthoritativePlayerCommandKind.DiplomacyResponse
                   or AuthoritativePlayerCommandKind.DesignShip;

    void AssertReplicaUnchangedSinceLastSnapshot(AuthoritativePlayerCommand command,
        AuthoritativeCommandResult result)
    {
        if (!DetectLocalMutation || LastSnapshot == null)
            return;

        AuthoritativeStateSnapshot current = AuthoritativeStateSnapshot.Capture(Universe, Tick);
        if (string.Equals(current.SyncDigest, LastSnapshot.SyncDigest, StringComparison.Ordinal))
            return;

        throw new Authoritative4XSyncMismatchException(command, result, LastSnapshot, current,
            "Client replica mutated before authoritative apply");
    }

    public void ApplySessionControl(bool paused, float gameSpeed)
    {
        AuthoritativePaused = paused;
        AuthoritativeGameSpeed = ClampGameSpeed(gameSpeed);
        Universe.UState.Paused = AuthoritativePaused;
        Universe.UState.GameSpeed = AuthoritativeGameSpeed;
    }

    static float ClampGameSpeed(float speed)
        => float.IsFinite(speed) ? Math.Clamp(speed, 0.25f, 8f) : 1f;
}

public sealed class Authoritative4XRawHashDrift
{
    public readonly AuthoritativePlayerCommand Command;
    public readonly AuthoritativeCommandResult Result;
    public readonly AuthoritativeStateSnapshot AuthoritySnapshot;
    public readonly AuthoritativeStateSnapshot ClientSnapshot;

    public Authoritative4XRawHashDrift(AuthoritativePlayerCommand command,
        AuthoritativeCommandResult result, AuthoritativeStateSnapshot authoritySnapshot,
        AuthoritativeStateSnapshot clientSnapshot)
    {
        Command = command;
        Result = result;
        AuthoritySnapshot = authoritySnapshot;
        ClientSnapshot = clientSnapshot;
    }
}

public sealed class Authoritative4XSyncMismatchException : InvalidOperationException
{
    public readonly AuthoritativePlayerCommand Command;
    public readonly AuthoritativeCommandResult Result;
    public readonly AuthoritativeStateSnapshot AuthoritySnapshot;
    public readonly AuthoritativeStateSnapshot ClientSnapshot;

    public Authoritative4XSyncMismatchException(AuthoritativePlayerCommand command,
        AuthoritativeCommandResult result, AuthoritativeStateSnapshot authoritySnapshot,
        AuthoritativeStateSnapshot clientSnapshot, string label = "Authoritative sync mismatch")
        : base(BuildMessage(label, result, authoritySnapshot, clientSnapshot))
    {
        Command = command;
        Result = result;
        AuthoritySnapshot = authoritySnapshot;
        ClientSnapshot = clientSnapshot;
    }

    static string BuildMessage(string label, AuthoritativeCommandResult result,
        AuthoritativeStateSnapshot authoritySnapshot, AuthoritativeStateSnapshot clientSnapshot)
        => $"{label} at tick {clientSnapshot?.Tick ?? 0}: " +
           $"origin={result?.OriginPeer ?? 0} seq={result?.Sequence ?? 0} " +
           $"authority 0x{authoritySnapshot?.HashLo ?? 0UL:X16}:0x{authoritySnapshot?.HashHi ?? 0UL:X16}/" +
           $"{authoritySnapshot?.SyncDigest ?? ""}/{authoritySnapshot?.TransformDigest ?? ""}, client " +
           $"0x{clientSnapshot?.HashLo ?? 0UL:X16}:0x{clientSnapshot?.HashHi ?? 0UL:X16}/" +
           $"{clientSnapshot?.SyncDigest ?? ""}/{clientSnapshot?.TransformDigest ?? ""}; " +
           $"{FirstPayloadDifferenceForLog(authoritySnapshot?.Payload, clientSnapshot?.Payload)}";

    internal static string FirstPayloadDifferenceForLog(string authorityPayload, string clientPayload)
        => AuthoritativeStateSnapshot.FirstFatalPayloadDifferenceForLog(authorityPayload, clientPayload);
}

public readonly struct Authoritative4XClientSpec
{
    public readonly int PeerId;
    public readonly int EmpireId;
    public readonly UniverseScreen Universe;

    public Authoritative4XClientSpec(int peerId, int empireId, UniverseScreen universe)
    {
        PeerId = peerId;
        EmpireId = empireId;
        Universe = universe;
    }
}

readonly struct AuthoritativeCommandKey : IEquatable<AuthoritativeCommandKey>
{
    public readonly int OriginPeer;
    public readonly int Sequence;

    public AuthoritativeCommandKey(int originPeer, int sequence)
    {
        OriginPeer = originPeer;
        Sequence = sequence;
    }

    public bool Equals(AuthoritativeCommandKey other)
        => OriginPeer == other.OriginPeer && Sequence == other.Sequence;
    public override bool Equals(object obj)
        => obj is AuthoritativeCommandKey other && Equals(other);
    public override int GetHashCode()
        => HashCode.Combine(OriginPeer, Sequence);
}

/// <summary>
/// Headless Phase-A harness: client submits primitive requests over the SDLockstep transport, host
/// validates/applies/advances, then sends ack + canonical sync snapshot back.
/// </summary>
public sealed class Authoritative4XInProcessSession
{
    const int HostPeer = 1;
    const int ClientPeer = 2;

    readonly ILockstepTransport Transport;
    readonly Authoritative4XAuthority Authority;
    readonly Authoritative4XClientReplica Client;
    readonly Map<int, AuthoritativePlayerCommand> Pending = new();

    public AuthoritativeCommandResult LastResult { get; private set; }
    public AuthoritativeStateSnapshot LastAuthoritySnapshot { get; private set; }
    public AuthoritativeStateSnapshot LastClientSnapshot => Client.LastSnapshot;

    public Authoritative4XInProcessSession(UniverseScreen authorityUniverse, UniverseScreen clientUniverse,
        ILockstepTransport transport = null)
    {
        Transport = transport ?? new FakeTransport();
        Authority = new Authoritative4XAuthority(authorityUniverse);
        Client = new Authoritative4XClientReplica(clientUniverse);
        Transport.Register(HostPeer, OnHostMessage);
        Transport.Register(ClientPeer, OnClientMessage);
    }

    public void SubmitFromClient(AuthoritativePlayerCommand command)
    {
        Pending[command.Sequence] = command;
        Transport.Send(HostPeer, command.ToMessage(ClientPeer));
        Pump();
    }

    void Pump()
    {
        Transport.Poll();
        Transport.Poll();
    }

    void OnHostMessage(LockstepMessage message)
    {
        if (message is not AuthoritativeCommandRequestMessage request)
            return;

        AuthoritativePlayerCommand command = AuthoritativePlayerCommand.FromMessage(request);
        (AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot) = Authority.Process(command);
        result.OriginPeer = request.FromPeer;
        Transport.Send(ClientPeer, result.ToMessage(HostPeer));
        Transport.Send(ClientPeer, snapshot.ToMessage(HostPeer));
    }

    void OnClientMessage(LockstepMessage message)
    {
        switch (message)
        {
            case AuthoritativeCommandResultMessage resultMessage:
                LastResult = new AuthoritativeCommandResult
                {
                    Sequence = resultMessage.Sequence,
                    OriginPeer = resultMessage.OriginPeer,
                    Accepted = resultMessage.Accepted,
                    Tick = resultMessage.Tick,
                    Reason = resultMessage.Reason ?? "",
                };
                break;
            case AuthoritativeStateSnapshotMessage snapshotMessage:
                LastAuthoritySnapshot = AuthoritativeStateSnapshot.FromMessage(snapshotMessage);
                if (LastResult == null || !Pending.TryGetValue(LastResult.Sequence, out AuthoritativePlayerCommand command))
                    throw new System.InvalidOperationException("Received authoritative snapshot without a matching command result.");
                Pending.Remove(LastResult.Sequence);
                Client.ApplyAuthoritativeResult(command, LastResult, LastAuthoritySnapshot);
                break;
        }
    }
}

/// <summary>
/// Multi-client in-process harness for Phase-A authoritative MP features that need targeted routing,
/// such as human-to-human diplomacy popups.
/// </summary>
public sealed class Authoritative4XInProcessMultiClientSession
{
    const int HostPeer = 1;

    readonly ILockstepTransport Transport;
    readonly Authoritative4XAuthority Authority;
    readonly Dictionary<int, Authoritative4XClientReplica> Clients = new();
    readonly Dictionary<int, int> EmpireByPeer = new();
    readonly Dictionary<int, int> PeerByEmpire = new();
    readonly Dictionary<AuthoritativeCommandKey, AuthoritativePlayerCommand> Pending = new();
    readonly Dictionary<int, AuthoritativeCommandResult> LastResults = new();
    readonly Dictionary<int, AuthoritativeStateSnapshot> LastSnapshots = new();
    readonly Dictionary<int, List<AuthoritativeDiplomacyPopup>> Popups = new();

    public AuthoritativeStateSnapshot LastAuthoritySnapshot { get; private set; }

    public Authoritative4XInProcessMultiClientSession(UniverseScreen authorityUniverse,
        Authoritative4XClientSpec[] clients, ILockstepTransport transport = null)
    {
        Transport = transport ?? new FakeTransport();
        int[] humanEmpireIds = clients.Select(c => c.EmpireId).ToArray();
        AuthoritativeHumanPlayers.SetHumanControlledEmpires(authorityUniverse.UState, humanEmpireIds);
        Authority = new Authoritative4XAuthority(authorityUniverse, humanEmpireIds: humanEmpireIds);
        Transport.Register(HostPeer, OnHostMessage);

        foreach (Authoritative4XClientSpec spec in clients)
        {
            AuthoritativeHumanPlayers.SetHumanControlledEmpires(spec.Universe.UState, humanEmpireIds);
            Clients[spec.PeerId] = new Authoritative4XClientReplica(spec.Universe, humanEmpireIds: humanEmpireIds);
            EmpireByPeer[spec.PeerId] = spec.EmpireId;
            PeerByEmpire[spec.EmpireId] = spec.PeerId;
            Popups[spec.PeerId] = new List<AuthoritativeDiplomacyPopup>();
            int peer = spec.PeerId;
            Transport.Register(peer, message => OnClientMessage(peer, message));
        }
    }

    public void SubmitFromClient(int peerId, AuthoritativePlayerCommand command)
    {
        Transport.Send(HostPeer, command.ToMessage(peerId));
        Pump();
    }

    public AuthoritativeCommandResult LastResultFor(int peerId) => LastResults[peerId];
    public AuthoritativeStateSnapshot LastClientSnapshotFor(int peerId) => LastSnapshots[peerId];
    public AuthoritativeDiplomacyPopup[] PopupsFor(int peerId) => Popups[peerId].ToArray();

#if DEBUG
    public void ApplySessionControlForTest(bool paused, float gameSpeed)
    {
        foreach (Authoritative4XClientReplica client in Clients.Values)
            client.ApplySessionControl(paused, gameSpeed);
    }
#endif

    void Pump()
    {
        for (int i = 0; i < 4; ++i)
            Transport.Poll();
    }

    void OnHostMessage(LockstepMessage message)
    {
        if (message is not AuthoritativeCommandRequestMessage request)
            return;

        AuthoritativePlayerCommand command = AuthoritativePlayerCommand.FromMessage(request);
        (AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot) =
            !EmpireByPeer.TryGetValue(request.FromPeer, out int allowedEmpire) || allowedEmpire != command.EmpireId
                ? Authority.RejectAndAdvance(command.Sequence,
                    $"Peer {request.FromPeer} does not control empire {command.EmpireId}.")
                : Authority.Process(command);
        result.OriginPeer = request.FromPeer;
        LastAuthoritySnapshot = snapshot;

        foreach (int peer in Clients.Keys)
        {
            Transport.Send(peer, command.ToMessage(request.FromPeer));
            Transport.Send(peer, result.ToMessage(HostPeer));
            Transport.Send(peer, snapshot.ToMessage(HostPeer));
        }

        foreach (AuthoritativeDiplomacyPopup popup in Authority.DrainDiplomacyPopups())
        {
            if (PeerByEmpire.TryGetValue(popup.TargetEmpireId, out int targetPeer))
                Transport.Send(targetPeer, popup.ToMessage(HostPeer));
        }
    }

    void OnClientMessage(int peerId, LockstepMessage message)
    {
        switch (message)
        {
            case AuthoritativeCommandRequestMessage requestMessage:
                Pending[new AuthoritativeCommandKey(requestMessage.FromPeer, requestMessage.Sequence)] =
                    AuthoritativePlayerCommand.FromMessage(requestMessage);
                break;
            case AuthoritativeCommandResultMessage resultMessage:
                LastResults[peerId] = new AuthoritativeCommandResult
                {
                    Sequence = resultMessage.Sequence,
                    OriginPeer = resultMessage.OriginPeer,
                    Accepted = resultMessage.Accepted,
                    Tick = resultMessage.Tick,
                    Reason = resultMessage.Reason ?? "",
                };
                break;
            case AuthoritativeStateSnapshotMessage snapshotMessage:
                AuthoritativeStateSnapshot authoritySnapshot = AuthoritativeStateSnapshot.FromMessage(snapshotMessage);
                if (!LastResults.TryGetValue(peerId, out AuthoritativeCommandResult result)
                    || !Pending.TryGetValue(new AuthoritativeCommandKey(result.OriginPeer, result.Sequence),
                        out AuthoritativePlayerCommand command))
                    throw new System.InvalidOperationException("Received authoritative snapshot without a matching command result.");
                Clients[peerId].ApplyAuthoritativeResult(command, result, authoritySnapshot);
                LastSnapshots[peerId] = Clients[peerId].LastSnapshot;
                break;
            case AuthoritativeDiplomacyPopupMessage popupMessage:
                Popups[peerId].Add(AuthoritativeDiplomacyPopup.FromMessage(popupMessage));
                break;
        }
    }
}

/// <summary>
/// TCP-backed authoritative 4X host spine. This is deliberately thin: the host owns the real
/// UniverseState, validates incoming PlayerCommand requests, broadcasts the accepted snapshot,
/// and routes human diplomacy popups to the peer that owns the target empire.
/// </summary>
public sealed class Authoritative4XNetworkHost : IDisposable
{
    public const int HostPeerId = 1;

    readonly TcpLockstepTransport Transport;
    readonly Authoritative4XAuthority Authority;
    readonly Dictionary<int, int> EmpireByPeer;
    readonly Dictionary<int, int> PeerByEmpire;
    readonly int[] PeerIds;
    readonly int LocalPeerId;
    readonly List<AuthoritativeDiplomacyPopup> LocalPopups = new();
    readonly List<Authoritative4XProcessedCommand> ProcessedCommands = new();
    readonly List<AuthoritativeResyncRequestMessage> ResyncRequests = new();
    readonly List<AuthoritativeResyncAckMessage> ResyncAcks = new();
    readonly HashSet<int> PendingResyncAcks = new();
    int NextSaveTransferId = 1;

    public AuthoritativeCommandResult LastResult { get; private set; }
    public AuthoritativeStateSnapshot LastAuthoritySnapshot { get; private set; }
    public string LastError => Transport.LastError;
    public TcpLockstepTransport SharedTransport => Transport;
    public int CurrentResyncEpoch { get; private set; }
    public bool IsResyncInProgress => PendingResyncAcks.Count > 0;
    public int[] RemotePeerIds => PeerIds.Where(peer => peer != LocalPeerId).OrderBy(peer => peer).ToArray();
    public int[] PendingResyncPeerIds => PendingResyncAcks.OrderBy(peer => peer).ToArray();
    public AuthoritativeDiplomacyPopup[] DrainLocalPopups()
    {
        AuthoritativeDiplomacyPopup[] popups = LocalPopups.ToArray();
        LocalPopups.Clear();
        return popups;
    }

    public Authoritative4XProcessedCommand[] DrainProcessedCommands()
    {
        Authoritative4XProcessedCommand[] processed = ProcessedCommands.ToArray();
        ProcessedCommands.Clear();
        return processed;
    }

    public AuthoritativeResyncRequestMessage[] DrainResyncRequests()
    {
        AuthoritativeResyncRequestMessage[] requests = ResyncRequests.ToArray();
        ResyncRequests.Clear();
        return requests;
    }

    public AuthoritativeResyncAckMessage[] DrainResyncAcks()
    {
        AuthoritativeResyncAckMessage[] acks = ResyncAcks.ToArray();
        ResyncAcks.Clear();
        return acks;
    }

    public Authoritative4XNetworkHost(UniverseScreen authorityUniverse, TcpLockstepTransport transport,
        IReadOnlyDictionary<int, int> empireByPeer, int[] humanEmpireIds = null, int localPeerId = 0)
    {
        Transport = transport;
        EmpireByPeer = new Dictionary<int, int>(empireByPeer);
        PeerByEmpire = empireByPeer.ToDictionary(kv => kv.Value, kv => kv.Key);
        PeerIds = empireByPeer.Keys.OrderBy(peer => peer).ToArray();
        LocalPeerId = localPeerId;
        if (humanEmpireIds != null)
            AuthoritativeHumanPlayers.SetHumanControlledEmpires(authorityUniverse.UState, humanEmpireIds);
        Authority = new Authoritative4XAuthority(authorityUniverse, humanEmpireIds: humanEmpireIds);
        Transport.Register(HostPeerId, OnHostMessage);
    }

    public void Poll() => Transport.Poll();
    public void Dispose() => Transport.Dispose();

    public void SubmitLocal(int peerId, AuthoritativePlayerCommand command, bool broadcast = true)
    {
        if (IsResyncInProgress)
            return;
        ProcessCommand(peerId, command, broadcast);
    }

    public int BeginResyncEpoch(AuthoritativeResyncRequestMessage request)
    {
        if (IsResyncInProgress)
            return CurrentResyncEpoch;

        int[] remotePeers = RemotePeerIds;
        if (remotePeers.Length == 0)
            return 0;

        CurrentResyncEpoch++;
        PendingResyncAcks.Clear();
        foreach (int peer in remotePeers)
            PendingResyncAcks.Add(peer);

        var begin = new AuthoritativeResyncBeginMessage
        {
            FromPeer = HostPeerId,
            Epoch = CurrentResyncEpoch,
            RequestingPeer = request?.FromPeer ?? 0,
            Tick = request?.Tick ?? 0,
            ClientDigest = request?.ClientDigest ?? "",
            Reason = request?.Reason ?? "",
        };
        foreach (int peer in remotePeers)
            Transport.Send(peer, begin);

        return CurrentResyncEpoch;
    }

    public void BroadcastControl(bool paused, float gameSpeed)
    {
        foreach (int peer in PeerIds)
        {
            if (peer == LocalPeerId)
                continue;
            Transport.Send(peer, new SessionControlMessage
            {
                FromPeer = HostPeerId,
                Paused = paused,
                GameSpeed = gameSpeed,
            });
        }
    }

    public void SendSaveTransfer(int peerId, FileInfo saveFile,
        Authoritative4XSessionMetadata metadata, string reason = "")
    {
        int transferId = NextSaveTransferId++;
        foreach (LockstepMessage message in Authoritative4XSaveTransfer.CreateMessages(saveFile,
                     metadata, HostPeerId, transferId, reason))
        {
            Transport.Send(peerId, message);
        }
    }

    void OnHostMessage(LockstepMessage message)
    {
        switch (message)
        {
            case AuthoritativeResyncRequestMessage request:
                ResyncRequests.Add(request);
                return;
            case AuthoritativeResyncAckMessage ack:
                if (ack.Epoch == CurrentResyncEpoch && PendingResyncAcks.Remove(ack.FromPeer))
                    ResyncAcks.Add(ack);
                return;
            case AuthoritativeCommandRequestMessage commandRequest:
                if (IsResyncInProgress)
                    return;
                ProcessCommand(commandRequest.FromPeer, AuthoritativePlayerCommand.FromMessage(commandRequest));
                return;
        }
    }

    void ProcessCommand(int fromPeer, AuthoritativePlayerCommand command, bool broadcast = true)
    {
        (AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot) =
            !EmpireByPeer.TryGetValue(fromPeer, out int allowedEmpire) || allowedEmpire != command.EmpireId
                ? Authority.RejectAndAdvance(command.Sequence,
                    $"Peer {fromPeer} does not control empire {command.EmpireId}.", broadcast)
                : Authority.Process(command, broadcast);
        result.OriginPeer = fromPeer;

        LastResult = result;
        if (snapshot != null)
            LastAuthoritySnapshot = snapshot;
        if (broadcast)
            ProcessedCommands.Add(new Authoritative4XProcessedCommand(fromPeer, command, result, snapshot));
        if (!broadcast)
            return;

        foreach (int peer in PeerIds)
        {
            if (peer == LocalPeerId)
                continue;
            Transport.Send(peer, command.ToMessage(fromPeer));
            Transport.Send(peer, result.ToMessage(HostPeerId));
            Transport.Send(peer, snapshot.ToMessage(HostPeerId));
        }

        foreach (AuthoritativeDiplomacyPopup popup in Authority.DrainDiplomacyPopups())
        {
            if (!PeerByEmpire.TryGetValue(popup.TargetEmpireId, out int targetPeer))
                continue;
            if (targetPeer == LocalPeerId)
                LocalPopups.Add(popup);
            else
                Transport.Send(targetPeer, popup.ToMessage(HostPeerId));
        }
    }
}

public sealed class Authoritative4XProcessedCommand
{
    public readonly int PeerId;
    public readonly AuthoritativePlayerCommand Command;
    public readonly AuthoritativeCommandResult Result;
    public readonly AuthoritativeStateSnapshot Snapshot;

    public Authoritative4XProcessedCommand(int peerId, AuthoritativePlayerCommand command,
        AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot)
    {
        PeerId = peerId;
        Command = command;
        Result = result;
        Snapshot = snapshot;
    }
}

/// <summary>
/// TCP-backed passive authoritative 4X client replica. UI code submits commands through this
/// object; it never mutates locally except when a host-accepted result and snapshot arrive.
/// </summary>
public sealed class Authoritative4XNetworkClient : IDisposable
{
    readonly TcpLockstepTransport Transport;
    Authoritative4XClientReplica Replica;
    readonly Dictionary<AuthoritativeCommandKey, AuthoritativePlayerCommand> Pending = new();
    readonly Queue<AuthoritativeCommandResult> PendingResults = new();
    readonly List<AuthoritativeDiplomacyPopup> Popups = new();
    readonly List<Authoritative4XProcessedCommand> ProcessedCommands = new();
    readonly List<Authoritative4XReceivedSave> ReceivedSaves = new();
    readonly Authoritative4XSaveTransferReceiver SaveTransfers = new();
    bool WaitingForResync;
    int CurrentResyncEpoch;

    public int PeerId { get; }
    public AuthoritativeCommandResult LastResult { get; private set; }
    public AuthoritativeStateSnapshot LastAuthoritySnapshot { get; private set; }
    public AuthoritativeStateSnapshot LastClientSnapshot => Replica.LastSnapshot;
    public Authoritative4XRawHashDrift LastRawHashDrift => Replica.LastRawHashDrift;
    public Authoritative4XSyncMismatchException LastSyncMismatch { get; private set; }
    public bool IsWaitingForResync => WaitingForResync;
    public int ResyncEpoch => CurrentResyncEpoch;
    public int ReceivedSaveCount => ReceivedSaves.Count;
    public string LastError => Transport.LastError;
    public TcpLockstepTransport SharedTransport => Transport;

    public Authoritative4XProcessedCommand[] DrainProcessedCommands()
    {
        Authoritative4XProcessedCommand[] processed = ProcessedCommands.ToArray();
        ProcessedCommands.Clear();
        return processed;
    }

    public Authoritative4XReceivedSave[] DrainReceivedSaves()
    {
        Authoritative4XReceivedSave[] saves = ReceivedSaves.ToArray();
        ReceivedSaves.Clear();
        return saves;
    }

    public Authoritative4XNetworkClient(UniverseScreen clientUniverse, TcpLockstepTransport transport,
        int peerId, int[] humanEmpireIds = null)
    {
        PeerId = peerId;
        Transport = transport;
        if (humanEmpireIds != null)
            AuthoritativeHumanPlayers.SetHumanControlledEmpires(clientUniverse.UState, humanEmpireIds);
        Replica = new Authoritative4XClientReplica(clientUniverse, humanEmpireIds: humanEmpireIds,
            detectLocalMutation: true);
        Transport.Register(peerId, OnClientMessage);
    }

    public void Submit(AuthoritativePlayerCommand command)
    {
        if (WaitingForResync)
            return;
        Transport.Send(Authoritative4XNetworkHost.HostPeerId, command.ToMessage(PeerId));
    }

    public void SendResyncAck(uint tick, string loadedDigest, string saveSha256, string error = "")
    {
        if (CurrentResyncEpoch <= 0)
            return;

        Transport.Send(Authoritative4XNetworkHost.HostPeerId, new AuthoritativeResyncAckMessage
        {
            FromPeer = PeerId,
            Epoch = CurrentResyncEpoch,
            Tick = tick,
            LoadedDigest = loadedDigest ?? "",
            SaveSha256 = saveSha256 ?? "",
            Error = error ?? "",
        });
    }

    public void Poll() => Transport.Poll();
    public AuthoritativeDiplomacyPopup[] PopupsForClient() => Popups.ToArray();
    public AuthoritativeDiplomacyPopup[] DrainPopupsForClient()
    {
        AuthoritativeDiplomacyPopup[] popups = Popups.ToArray();
        Popups.Clear();
        return popups;
    }
    public void Dispose() => Transport.Dispose();

    void OnClientMessage(LockstepMessage message)
    {
        switch (message)
        {
            case AuthoritativeResyncBeginMessage begin:
                CurrentResyncEpoch = begin.Epoch;
                WaitingForResync = true;
                Pending.Clear();
                PendingResults.Clear();
                break;
            case AuthoritativeSaveTransferBeginMessage:
            case AuthoritativeSaveTransferChunkMessage:
            case AuthoritativeSaveTransferEndMessage:
                if (SaveTransfers.TryAccept(message, out Authoritative4XReceivedSave received, out string error)
                    && received != null)
                {
                    ReceivedSaves.Add(received);
                }
                else if (error.NotEmpty())
                {
                    throw new InvalidDataException(error);
                }
                break;
            case AuthoritativeCommandRequestMessage requestMessage:
                if (WaitingForResync)
                    return;
                Pending[new AuthoritativeCommandKey(requestMessage.FromPeer, requestMessage.Sequence)] =
                    AuthoritativePlayerCommand.FromMessage(requestMessage);
                break;
            case AuthoritativeCommandResultMessage resultMessage:
                if (WaitingForResync)
                    return;
                LastResult = new AuthoritativeCommandResult
                {
                    Sequence = resultMessage.Sequence,
                    OriginPeer = resultMessage.OriginPeer,
                    Accepted = resultMessage.Accepted,
                    Tick = resultMessage.Tick,
                    Reason = resultMessage.Reason ?? "",
                };
                PendingResults.Enqueue(LastResult);
                break;
            case AuthoritativeStateSnapshotMessage snapshotMessage:
                if (WaitingForResync)
                    return;
                LastAuthoritySnapshot = AuthoritativeStateSnapshot.FromMessage(snapshotMessage);
                if (PendingResults.Count == 0)
                    throw new InvalidOperationException("Received authoritative snapshot without a matching command result.");

                AuthoritativeCommandResult result = PendingResults.Dequeue();
                LastResult = result;
                if (!Pending.TryGetValue(new AuthoritativeCommandKey(result.OriginPeer, result.Sequence),
                        out AuthoritativePlayerCommand command))
                    throw new InvalidOperationException("Received authoritative snapshot without a matching command result.");
                Pending.Remove(new AuthoritativeCommandKey(result.OriginPeer, result.Sequence));
                try
                {
                    Replica.ApplyAuthoritativeResult(command, result, LastAuthoritySnapshot);
                }
                catch (Authoritative4XSyncMismatchException e)
                {
                    LastSyncMismatch = e;
                    WaitingForResync = true;
                    Pending.Clear();
                    PendingResults.Clear();
                    Transport.Send(Authoritative4XNetworkHost.HostPeerId, new AuthoritativeResyncRequestMessage
                    {
                        FromPeer = PeerId,
                        Tick = e.ClientSnapshot?.Tick ?? 0,
                        ClientDigest = e.ClientSnapshot?.SyncDigest ?? "",
                        Reason = e.Message,
                    });
                    break;
                }
                ProcessedCommands.Add(new Authoritative4XProcessedCommand(result.OriginPeer,
                    command, result, LastClientSnapshot));
                break;
            case AuthoritativeDiplomacyPopupMessage popupMessage:
                if (WaitingForResync)
                    return;
                Popups.Add(AuthoritativeDiplomacyPopup.FromMessage(popupMessage));
                break;
            case SessionControlMessage controlMessage:
                if (WaitingForResync)
                    return;
                Replica.ApplySessionControl(controlMessage.Paused, controlMessage.GameSpeed);
                break;
        }
    }
}

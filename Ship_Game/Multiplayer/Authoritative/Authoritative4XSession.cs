using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using SDLockstep;
using SDUtils;
using SDUtils.Deterministic;
using Ship_Game.AI;
using Ship_Game.AI.StrategyAI.WarGoals;
using Ship_Game.Commands.Goals;
using Ship_Game.Determinism;
using Ship_Game.Fleets;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
using Ship_Game.Ships.AI;
using Ship_Game.Universe;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.Multiplayer.Authoritative;

public sealed class AuthoritativeStateSnapshot
{
    const int VolatileShipPositionDigest = 0;

    public uint Tick;
    public ulong HashLo;
    public ulong HashHi;
    public string SyncDigest = "";
    public string Payload = "";

    public AuthoritativeStateSnapshotMessage ToMessage(int fromPeer)
        => new()
        {
            FromPeer = fromPeer,
            Tick = Tick,
            HashLo = HashLo,
            HashHi = HashHi,
            SyncDigest = SyncDigest,
            Payload = Payload,
        };

    public static AuthoritativeStateSnapshot FromMessage(AuthoritativeStateSnapshotMessage message)
        => new()
        {
            Tick = message.Tick,
            HashLo = message.HashLo,
            HashHi = message.HashHi,
            SyncDigest = message.SyncDigest ?? "",
            Payload = message.Payload ?? "",
        };

    public void ApplyEmpireRuntimePayload(UniverseState universe)
    {
        if (universe == null || string.IsNullOrEmpty(Payload))
            return;

        string[] lines = Payload.Split('\n');
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith("E|", StringComparison.Ordinal))
                ApplyEmpireRuntimeLine(universe, line);
            else if (line.StartsWith("U|", StringComparison.Ordinal))
                ApplyUnlockedTechLine(universe, line);
            else if (line.StartsWith("D|", StringComparison.Ordinal))
                ApplyPlayerDesignLine(universe, line);
            else if (line.StartsWith("P|", StringComparison.Ordinal))
                ApplyPlanetRuntimeLine(universe, line);
            else if (line.StartsWith("R|", StringComparison.Ordinal))
                ApplyRelationshipLine(universe, line);
            else if (line.StartsWith("S|", StringComparison.Ordinal))
                ApplyShipRuntimeLine(universe, line);
        }

        ApplyConstructionQueuePayload(universe, lines);
        ApplyColonizationGoalPayload(universe, lines);
    }

    public void ApplyRelationshipPayload(UniverseState universe)
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

    static void ApplyEmpireRuntimeLine(UniverseState universe, string line)
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

        if (TryParseFloatBits(p[4], out float money))
            empire.Money = money;
        if (TryParseFloatBits(p[5], out float taxRate))
            empire.data.TaxRate = taxRate;
        if (TryParseFloatBits(p[6], out float treasuryGoal))
            empire.data.treasuryGoal = treasuryGoal;
        if (int.TryParse(p[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out int flags))
            ApplyAutomationFlags(empire, (AuthoritativeEmpireAutomationFlags)flags);

        empire.data.CurrentAutoFreighter = p[9] ?? "";
        empire.data.CurrentAutoColony = p[10] ?? "";
        empire.data.CurrentAutoScout = p[11] ?? "";
        empire.data.CurrentConstructor = p[12] ?? "";
        empire.data.CurrentResearchStation = p[13] ?? "";
        empire.data.CurrentMiningStation = p[14] ?? "";
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

    static void ApplyPlayerDesignLine(UniverseState universe, string line)
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

        if (ResourceManager.Ships.GetDesign(designName, out IShipDesign existing) && existing.IsPlayerDesign)
        {
            empire.AddBuildableShip(existing);
            return;
        }

        string design64 = p.Length >= 8 ? p[7] ?? "" : "";
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

    static void ApplyPlanetRuntimeLine(UniverseState universe, string line)
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

        if (int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int colonyType)
            && Enum.IsDefined(typeof(Planet.ColonyType), colonyType))
        {
            planet.CType = (Planet.ColonyType)colonyType;
        }

        if (int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int garrison))
            planet.GarrisonSize = garrison;
        if (TryParseByte(p[5], out byte wantedPlatforms))
            planet.SetWantedPlatforms(wantedPlatforms);
        if (TryParseByte(p[6], out byte wantedShipyards))
            planet.SetWantedShipyards(wantedShipyards);
        if (TryParseByte(p[7], out byte wantedStations))
            planet.SetWantedStations(wantedStations);

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

        if (int.TryParse(p[16], NumberStyles.Integer, CultureInfo.InvariantCulture, out int foodImport))
            planet.ManualFoodImportSlots = foodImport;
        if (int.TryParse(p[17], NumberStyles.Integer, CultureInfo.InvariantCulture, out int prodImport))
            planet.ManualProdImportSlots = prodImport;
        if (int.TryParse(p[18], NumberStyles.Integer, CultureInfo.InvariantCulture, out int coloImport))
            planet.ManualColoImportSlots = coloImport;
        if (int.TryParse(p[19], NumberStyles.Integer, CultureInfo.InvariantCulture, out int foodExport))
            planet.ManualFoodExportSlots = foodExport;
        if (int.TryParse(p[20], NumberStyles.Integer, CultureInfo.InvariantCulture, out int prodExport))
            planet.ManualProdExportSlots = prodExport;
        if (int.TryParse(p[21], NumberStyles.Integer, CultureInfo.InvariantCulture, out int coloExport))
            planet.ManualColoExportSlots = coloExport;

        planet.SetPrioritizedPort(ParseFlag(p[22]));
        planet.GovOrbitals = ParseFlag(p[23]);
        planet.AutoBuildTroops = ParseFlag(p[24]);
        planet.DontScrapBuildings = ParseFlag(p[25]);
        planet.Quarantine = ParseFlag(p[26]);
        planet.ManualOrbitals = ParseFlag(p[27]);
        planet.GovGroundDefense = ParseFlag(p[28]);
        planet.SetSpecializedTradeHub(ParseFlag(p[29]));

        if (TryParseFloatBits(p[30], out float civilianBudget))
            planet.SetManualCivBudget(civilianBudget);
        if (TryParseFloatBits(p[31], out float groundBudget))
            planet.SetManualGroundDefBudget(groundBudget);
        if (TryParseFloatBits(p[32], out float spaceBudget))
            planet.SetManualSpaceDefBudget(spaceBudget);
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

    static void ApplyRelationshipLine(UniverseState universe, string line)
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

        Relationship rel = empire.GetRelations(target);
        rel.Known = ParseFlag(p[3]);
        rel.AtWar = ParseFlag(p[4]);
        rel.Treaty_NAPact = ParseFlag(p[5]);
        rel.Treaty_Trade = ParseFlag(p[6]);
        rel.Treaty_OpenBorders = ParseFlag(p[7]);
        rel.Treaty_Alliance = ParseFlag(p[8]);
        rel.Treaty_Peace = ParseFlag(p[9]);

        if (rel.AtWar)
        {
            rel.CanAttack = true;
            rel.IsHostile = true;
            if (rel.ActiveWar == null)
                rel.ActiveWar = War.CreateInstance(empire, target, WarType.ImperialistWar);
        }
        else
        {
            rel.CanAttack = false;
            rel.IsHostile = false;
            rel.CancelPrepareForWar();
            if (rel.ActiveWar != null)
            {
                rel.ActiveWar.EndStarDate = empire.Universe.StarDate;
                rel.WarHistory.Add(rel.ActiveWar);
                rel.ActiveWar = null;
            }
        }
    }

    static bool ParseFlag(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int flag) && flag != 0;

    public static AuthoritativeStateSnapshot Capture(UniverseScreen universe, uint tick,
        DeterminismProfile profile = DeterminismProfile.ReplayWinX64Float)
    {
        (ulong lo, ulong hi, _) = universe.UState.ComputeAuthoritativeStateHash(profile);
        string payload = BuildPayload(universe.UState);
        return new AuthoritativeStateSnapshot
        {
            Tick = tick,
            HashLo = lo,
            HashHi = hi,
            Payload = payload,
            SyncDigest = Digest(payload),
        };
    }

    static string BuildPayload(UniverseState us)
    {
        var sb = new StringBuilder(4096);
        foreach (Empire e in us.Empires.OrderBy(e => e.Id))
            sb.Append("E|").Append(e.Id)
              .Append('|').Append(e.Research.Topic ?? "")
              .Append('|').Append(ResearchQueueSignature(e))
              .Append('|').Append(FloatBits(e.Money))
              .Append('|').Append(FloatBits(e.data.TaxRate))
              .Append('|').Append(FloatBits(e.data.treasuryGoal))
              .Append('|').Append(e.AutoTaxes ? 1 : 0)
              .Append('|').Append((int)AutomationFlags(e))
              .Append('|').Append(e.data.CurrentAutoFreighter ?? "")
              .Append('|').Append(e.data.CurrentAutoColony ?? "")
              .Append('|').Append(e.data.CurrentAutoScout ?? "")
              .Append('|').Append(e.data.CurrentConstructor ?? "")
              .Append('|').Append(e.data.CurrentResearchStation ?? "")
              .Append('|').Append(e.data.CurrentMiningStation ?? "")
              .AppendLine();

        foreach (Empire e in us.Empires.OrderBy(e => e.Id))
        {
            foreach (TechEntry tech in e.TechEntries
                         .Where(t => t.Unlocked)
                         .OrderBy(t => t.UID, StringComparer.Ordinal))
            {
                sb.Append("U|").Append(e.Id)
                  .Append('|').Append(tech.UID)
                  .Append('|').Append(tech.Level)
                  .AppendLine();
            }
        }

        foreach (Empire e in us.Empires.OrderBy(e => e.Id))
        {
            foreach (IShipDesign design in e.ShipsWeCanBuildSnapshot
                         .Where(d => d.IsPlayerDesign)
                         .OrderBy(d => d.Name, StringComparer.Ordinal))
            {
                sb.Append("D|").Append(e.Id)
                  .Append('|').Append(design.Name)
                  .Append('|').Append(design.Hull)
                  .Append('|').Append((int)design.Role)
                  .Append('|').Append(FloatBits(design.BaseCost))
                  .Append('|').Append(DesignSlotSignature(design))
                  .Append('|').Append(DesignBase64(design))
                  .AppendLine();
            }
        }

        foreach (Empire e in us.Empires.OrderBy(e => e.Id))
        {
            foreach (Relationship rel in e.AllRelations.OrderBy(r => r.Them.Id))
                sb.Append("R|").Append(e.Id)
                  .Append('|').Append(rel.Them.Id)
                  .Append('|').Append(rel.Known ? 1 : 0)
                  .Append('|').Append(rel.AtWar ? 1 : 0)
                  .Append('|').Append(rel.Treaty_NAPact ? 1 : 0)
                  .Append('|').Append(rel.Treaty_Trade ? 1 : 0)
                  .Append('|').Append(rel.Treaty_OpenBorders ? 1 : 0)
                  .Append('|').Append(rel.Treaty_Alliance ? 1 : 0)
                  .Append('|').Append(rel.Treaty_Peace ? 1 : 0)
                  .AppendLine();
        }

        foreach (Empire e in us.Empires.OrderBy(e => e.Id))
        {
            foreach (MarkForColonization goal in e.AI.FindGoals<MarkForColonization>()
                         .OrderBy(g => g.TargetPlanet?.Id ?? 0)
                         .ThenBy(g => g.FinishedShip?.Id ?? 0))
            {
                sb.Append("G|").Append(e.Id)
                  .Append("|MarkForColonization")
                  .Append('|').Append(goal.TargetPlanet?.Id ?? 0)
                  .Append('|').Append(goal.IsManualColonizationOrder ? 1 : 0)
                  .Append('|').Append(goal.FinishedShip?.Id ?? 0)
                  .AppendLine();
            }

            foreach (Goal goal in e.AI.Goals
                         .Where(g => g is RefitShip)
                         .OrderBy(g => g.OldShip?.Id ?? 0)
                         .ThenBy(g => g.ToBuild?.Name ?? "", StringComparer.Ordinal))
            {
                var refitGoal = (RefitShip)goal;
                sb.Append("G|").Append(e.Id)
                  .Append("|Refit")
                  .Append('|').Append(goal.Step)
                  .Append('|').Append(goal.OldShip?.Id ?? 0)
                  .Append('|').Append(goal.ToBuild?.Name ?? "")
                  .Append('|').Append(goal.PlanetBuildingAt?.Id ?? 0)
                  .Append('|').Append(refitGoal.Rush ? 1 : 0)
                  .Append('|').Append(refitGoal.Fleet?.Id ?? 0)
                  .Append('|').Append(refitGoal.Fleet?.Key ?? 0)
                  .AppendLine();
            }

            foreach (FleetRequisition goal in e.AI.Goals
                         .OfType<FleetRequisition>()
                         .OrderBy(g => g.Fleet?.Key ?? 0)
                         .ThenBy(FleetGoalNodeIndex)
                         .ThenBy(g => g.Build?.Template?.Name ?? "", StringComparer.Ordinal))
            {
                sb.Append("G|").Append(e.Id)
                  .Append("|FleetRequisition")
                  .Append('|').Append(goal.Step)
                  .Append('|').Append(goal.Fleet?.Id ?? 0)
                  .Append('|').Append(goal.Fleet?.Key ?? 0)
                  .Append('|').Append(FleetGoalNodeIndex(goal))
                  .Append('|').Append(goal.Build?.Template?.Name ?? "")
                  .Append('|').Append(goal.PlanetBuildingAt?.Id ?? 0)
                  .Append('|').Append(goal.Build?.Rush == true ? 1 : 0)
                  .AppendLine();
            }

            foreach (Goal goal in e.AI.Goals
                         .Where(IsDeepSpaceBuildStateGoal)
                         .OrderBy(g => (int)g.Type)
                         .ThenBy(g => g.ToBuild?.Name ?? "", StringComparer.Ordinal)
                         .ThenBy(g => g.TargetPlanet?.Id ?? 0)
                         .ThenBy(g => DeepSpaceGoalSystem(g)?.Id ?? 0)
                         .ThenBy(g => g.BuildPosition.X)
                         .ThenBy(g => g.BuildPosition.Y))
            {
                Vector2 buildPosition = goal.BuildPosition;
                Vector2 movePosition = goal.MovePosition;
                sb.Append("G|").Append(e.Id)
                  .Append("|DeepSpace")
                  .Append('|').Append((int)goal.Type)
                  .Append('|').Append(goal.Step)
                  .Append('|').Append(goal.ToBuild?.Name ?? "")
                  .Append('|').Append(goal.TargetPlanet?.Id ?? 0)
                  .Append('|').Append(DeepSpaceGoalSystem(goal)?.Id ?? 0)
                  .Append('|').Append(goal.TargetShip?.Id ?? 0)
                  .Append('|').Append(goal.PlanetBuildingAt?.Id ?? 0)
                  .Append('|').Append(FloatBits(buildPosition.X))
                  .Append('|').Append(FloatBits(buildPosition.Y))
                  .Append('|').Append(FloatBits(movePosition.X))
                  .Append('|').Append(FloatBits(movePosition.Y))
                  .AppendLine();
            }

            foreach (FleetPatrol patrol in e.FleetPatrols.OrderBy(p => p.Name ?? "", StringComparer.Ordinal))
                sb.Append("FP|").Append(e.Id)
                  .Append('|').Append(FleetPatrolPlanSignature(patrol))
                  .AppendLine();

            foreach (Fleet f in e.AllFleets.Where(f => f != null).OrderBy(f => f.Key).ThenBy(f => f.Id))
                sb.Append("F|").Append(e.Id)
                  .Append('|').Append(f.Id)
                  .Append('|').Append(f.Key)
                  .Append('|').Append(f.Name ?? "")
                  .Append('|').Append(f.FleetIconIndex)
                  .Append('|').Append(f.CommandShip?.Id ?? 0)
                  .Append('|').Append(FloatBits(f.FinalPosition.X))
                  .Append('|').Append(FloatBits(f.FinalPosition.Y))
                  .Append('|').Append(FloatBits(f.FinalDirection.X))
                  .Append('|').Append(FloatBits(f.FinalDirection.Y))
                  .Append('|').Append(FleetShipSignature(f))
                  .Append('|').Append(FleetNodeSignature(f))
                  .Append('|').Append(FleetPatrolSignature(f))
                  .AppendLine();
        }

        foreach (Planet p in us.Planets.OrderBy(p => p.Id))
        {
            sb.Append("P|").Append(p.Id)
              .Append('|').Append(p.Owner?.Id ?? 0)
              .Append('|').Append((int)p.CType)
              .Append('|').Append(p.GarrisonSize)
              .Append('|').Append(p.WantedPlatforms)
              .Append('|').Append(p.WantedShipyards)
              .Append('|').Append(p.WantedStations)
              .Append('|').Append(FloatBits(p.Food.Percent))
              .Append('|').Append(FloatBits(p.Prod.Percent))
              .Append('|').Append(FloatBits(p.Res.Percent))
              .Append('|').Append(p.Food.PercentLock ? 1 : 0)
              .Append('|').Append(p.Prod.PercentLock ? 1 : 0)
              .Append('|').Append(p.Res.PercentLock ? 1 : 0)
              .Append('|').Append((int)p.FS)
              .Append('|').Append((int)p.PS)
              .Append('|').Append(p.ManualFoodImportSlots)
              .Append('|').Append(p.ManualProdImportSlots)
              .Append('|').Append(p.ManualColoImportSlots)
              .Append('|').Append(p.ManualFoodExportSlots)
              .Append('|').Append(p.ManualProdExportSlots)
              .Append('|').Append(p.ManualColoExportSlots)
              .Append('|').Append(p.PrioritizedPort ? 1 : 0)
              .Append('|').Append(p.GovOrbitals ? 1 : 0)
              .Append('|').Append(p.AutoBuildTroops ? 1 : 0)
              .Append('|').Append(p.DontScrapBuildings ? 1 : 0)
              .Append('|').Append(p.Quarantine ? 1 : 0)
              .Append('|').Append(p.ManualOrbitals ? 1 : 0)
              .Append('|').Append(p.GovGroundDefense ? 1 : 0)
              .Append('|').Append(p.SpecializedTradeHub ? 1 : 0)
              .Append('|').Append(FloatBits(p.ManualCivilianBudget))
              .Append('|').Append(FloatBits(p.ManualGrdDefBudget))
              .Append('|').Append(FloatBits(p.ManualSpcDefBudget))
              .Append('|').Append(p.ConstructionQueue.Count)
              .AppendLine();

            if (p.HasBlueprints)
                sb.Append("BP|").Append(p.Id)
                  .Append('|').Append(BlueprintSignature(p))
                  .AppendLine();

            foreach (PlanetGridSquare tile in p.TilesList
                         .Where(t => t.BuildingOnTile || t.Biosphere)
                         .OrderBy(t => t.X)
                         .ThenBy(t => t.Y))
            {
                sb.Append("T|").Append(p.Id)
                  .Append('|').Append(tile.X)
                  .Append('|').Append(tile.Y)
                  .Append('|').Append(tile.Building?.Name ?? "")
                  .Append('|').Append(tile.Biosphere ? 1 : 0)
                  .Append('|').Append(tile.Habitable ? 1 : 0)
                  .Append('|').Append(tile.Terraformable ? 1 : 0)
                  .AppendLine();
            }

            foreach (PlanetGridSquare tile in p.TilesList
                         .Where(t => t.TroopsHere.Count > 0)
                         .OrderBy(t => t.X)
                         .ThenBy(t => t.Y))
            {
                for (int i = 0; i < tile.TroopsHere.Count; ++i)
                {
                    Troop troop = tile.TroopsHere[i];
                    sb.Append("GT|").Append(p.Id)
                      .Append('|').Append(tile.X)
                      .Append('|').Append(tile.Y)
                      .Append('|').Append(i)
                      .Append('|').Append(troop.Loyalty?.Id ?? 0)
                      .Append('|').Append(troop.Name ?? "")
                      .Append('|').Append(FloatBits(troop.Strength))
                      .Append('|').Append(troop.AvailableMoveActions)
                      .Append('|').Append(troop.AvailableAttackActions)
                      .Append('|').Append(FloatBits(troop.MoveTimer))
                      .Append('|').Append(FloatBits(troop.AttackTimer))
                      .AppendLine();
                }
            }

            for (int i = 0; i < p.ActiveCombats.Count; ++i)
            {
                Combat combat = p.ActiveCombats[i];
                sb.Append("GC|").Append(p.Id)
                  .Append('|').Append(i)
                  .Append('|').Append(combat.Phase)
                  .Append('|').Append(FloatBits(combat.Timer))
                  .Append('|').Append(combat.AttackerLoyalty?.Id ?? 0)
                  .Append('|').Append(combat.DefenseTile?.X ?? -1)
                  .Append('|').Append(combat.DefenseTile?.Y ?? -1)
                  .Append('|').Append(GroundTroopRef(p, combat.AttackingTroop))
                  .Append('|').Append(GroundTroopRef(p, combat.DefendingTroop))
                  .Append('|').Append(GroundBuildingRef(p, combat.AttackingBuilding))
                  .Append('|').Append(GroundBuildingRef(p, combat.DefendingBuilding))
                  .AppendLine();
            }

            QueueItem[] queue = p.Construction.GetConstructionQueueSnapshot();
            for (int i = 0; i < queue.Length; ++i)
            {
                QueueItem q = queue[i];
                sb.Append("Q|").Append(p.Id)
                  .Append('|').Append(i)
                  .Append('|').Append(q.isShip ? 1 : 0)
                  .Append('|').Append(q.isBuilding ? 1 : 0)
                  .Append('|').Append(q.isTroop ? 1 : 0)
                  .Append('|').Append((int)q.QType)
                  .Append('|').Append(q.ShipData?.Name ?? "")
                  .Append('|').Append(q.Building?.Name ?? "")
                  .Append('|').Append(q.TroopType ?? "")
                  .Append('|').Append(q.pgs?.X ?? -1)
                  .Append('|').Append(q.pgs?.Y ?? -1)
                  .Append('|').Append(FloatBits(q.Cost))
                  .Append('|').Append(FloatBits(q.ProductionSpent))
                  .Append('|').Append(q.Rush ? 1 : 0)
                  .Append('|').Append(q.IsCancelled ? 1 : 0)
                  .AppendLine();
            }
        }

        foreach (Ship s in us.Ships.OrderBy(s => s.Id))
            sb.Append("S|").Append(s.Id)
              .Append('|').Append(s.Loyalty?.Id ?? 0)
              .Append('|').Append(s.Fleet?.Id ?? 0)
              .Append('|').Append(s.Fleet?.Key ?? 0)
              .Append('|').Append((int)s.AI.State)
              .Append('|').Append((int)s.AI.CombatState)
              .Append('|').Append(VolatileShipPositionDigest)
              .Append('|').Append(VolatileShipPositionDigest)
              .Append('|').Append(VolatileShipPositionDigest)
              .Append('|').Append(VolatileShipPositionDigest)
              .Append('|').Append(FloatBits(s.ScuttleTimer))
              .Append('|').Append(s.AI.Target?.Id ?? 0)
              .Append('|').Append(s.AI.HasPriorityTarget ? 1 : 0)
              .Append('|').Append(TargetQueueSignature(s))
              .Append('|').Append(s.AI.EscortTarget?.Id ?? 0)
              .Append('|').Append(ShipOrderQueueSignature(s))
              .Append('|').Append(s.IsFreighter ? 1 : 0)
              .Append('|').Append(s.TransportingFood ? 1 : 0)
              .Append('|').Append(s.TransportingProduction ? 1 : 0)
              .Append('|').Append(s.TransportingColonists ? 1 : 0)
              .Append('|').Append(s.AllowInterEmpireTrade ? 1 : 0)
              .Append('|').Append(s.Carrier?.HasFighterBays == true ? 1 : 0)
              .Append('|').Append(s.Carrier?.FightersOut == true ? 1 : 0)
              .Append('|').Append(s.Carrier?.HasTroopBays == true ? 1 : 0)
              .Append('|').Append(s.Carrier?.TroopsOut == true ? 1 : 0)
              .Append('|').Append(s.Carrier?.RecallFightersBeforeFTL == true ? 1 : 0)
              .Append('|').Append(s.Carrier?.SendTroopsToShip == true ? 1 : 0)
              .Append('|').Append(s.Carrier?.AllowBoardShip == true ? 1 : 0)
              .Append('|').Append(s.ManualHangarOverride ? 1 : 0)
              .Append('|').Append(TradeRouteSignature(s))
              .Append('|').Append(AreaOfOperationSignature(s))
              .AppendLine();

        foreach (Ship s in us.Ships.OrderBy(s => s.Id))
        {
            IReadOnlyList<Troop> troops = s.GetOurTroops();
            for (int i = 0; i < troops.Count; ++i)
            {
                Troop troop = troops[i];
                sb.Append("ST|").Append(s.Id)
                  .Append('|').Append(i)
                  .Append('|').Append(troop.Loyalty?.Id ?? 0)
                  .Append('|').Append(troop.Name ?? "")
                  .Append('|').Append(FloatBits(troop.Strength))
                  .Append('|').Append(troop.AvailableMoveActions)
                  .Append('|').Append(troop.AvailableAttackActions)
                  .Append('|').Append(FloatBits(troop.MoveTimer))
                  .Append('|').Append(FloatBits(troop.AttackTimer))
                  .AppendLine();
            }
        }

        return sb.ToString();
    }

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
                    $"{tile.X},{tile.Y},{building.Name},{FloatBits(building.Strength)}");
        }
        return string.Create(CultureInfo.InvariantCulture,
            $"off,{building.Name},{FloatBits(building.Strength)}");
    }

    static string Digest(string payload)
    {
        var h = DetHash.New();
        h.AddString(payload);
        return "0x" + h.Value.ToString("X16", CultureInfo.InvariantCulture);
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

    static string TargetQueueSignature(Ship ship)
        => string.Join(",", ship.AI.TargetQueue.Select(s => s?.Id ?? 0));

    static string ResearchQueueSignature(Empire empire)
        => string.Join(",", empire.data.ResearchQueue);

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
        => string.Join(",", fleet.Ships.OrderBy(s => s.Id).Select(s => s.Id.ToString(CultureInfo.InvariantCulture)));

    static string FleetNodeSignature(Fleet fleet)
        => string.Join(";", fleet.DataNodes
            .OrderBy(n => n.Ship?.Id ?? int.MaxValue)
            .ThenBy(n => n.ShipName ?? "", StringComparer.Ordinal)
            .ThenBy(n => n.RelativeFleetOffset.X)
            .ThenBy(n => n.RelativeFleetOffset.Y)
            .Select(n => string.Join(",",
                (n.Ship?.Id ?? 0).ToString(CultureInfo.InvariantCulture),
                n.ShipName ?? "",
                FloatBits(n.RelativeFleetOffset.X).ToString("X8", CultureInfo.InvariantCulture),
                FloatBits(n.RelativeFleetOffset.Y).ToString("X8", CultureInfo.InvariantCulture),
                FloatBits(n.VultureWeight).ToString("X8", CultureInfo.InvariantCulture),
                FloatBits(n.AttackShieldedWeight).ToString("X8", CultureInfo.InvariantCulture),
                FloatBits(n.AssistWeight).ToString("X8", CultureInfo.InvariantCulture),
                FloatBits(n.DefenderWeight).ToString("X8", CultureInfo.InvariantCulture),
                FloatBits(n.DPSWeight).ToString("X8", CultureInfo.InvariantCulture),
                FloatBits(n.SizeWeight).ToString("X8", CultureInfo.InvariantCulture),
                FloatBits(n.ArmoredWeight).ToString("X8", CultureInfo.InvariantCulture),
                ((int)n.CombatState).ToString(CultureInfo.InvariantCulture),
                FloatBits(n.OrdersRadius).ToString("X8", CultureInfo.InvariantCulture))));

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
    {
        ShipAI.ShipGoal[] goals = ship.AI.OrderQueue.ToArray();
        return string.Join(";", goals
            .Where(g => g != null && !IsVolatileMovementSolverPlan(g.Plan))
            .Select(g =>
            {
                // Targeted goals are durable by target ID; their MovePosition is derived from
                // local planet/ship state and can drift during replay/resync.
                Vector2 movePosition = (g.TargetPlanet != null || g.TargetShip != null)
                    ? Vector2.Zero
                    : g.MovePosition;
                return $"{(int)g.Plan},{g.TargetPlanet?.Id ?? 0},{g.TargetShip?.Id ?? 0}," +
                       $"{FloatBits(movePosition.X):X8},{FloatBits(movePosition.Y):X8},{(int)g.MoveOrder}";
            }));
    }

    internal static string ShipOrderQueueSignatureForTest(Ship ship) => ShipOrderQueueSignature(ship);

    static bool IsVolatileMovementSolverPlan(ShipAI.Plan plan)
        => plan is ShipAI.Plan.RotateToFaceMovePosition
            or ShipAI.Plan.RotateToDesiredFacing
            or ShipAI.Plan.MoveToWithin1000
            or ShipAI.Plan.MakeFinalApproach
            or ShipAI.Plan.RotateInlineWithVelocity;

    static void ApplyShipRuntimeLine(UniverseState universe, string line)
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
        var moveOrder = (MoveOrder)moveOrderValue;
        Planet targetPlanet = planetId > 0 ? universe.GetPlanet(planetId) : null;
        Ship targetShip = targetShipId > 0 ? universe.Objects.FindShip(targetShipId) : null;
        var movePosition = new Vector2(x, y);
        AIState wantedState = ship.AI.State;

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
        flags &= AuthoritativeEmpireAutomationFlags.All;
        empire.AutoPickConstructors = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickConstructors);
        empire.AutoPickBestColonizer = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestColonizer);
        empire.AutoPickBestFreighter = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestFreighter);
        empire.AutoResearch = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoResearch);
        empire.AutoBuildTerraformers = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildTerraformers);
        empire.AutoTaxes = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoTaxes);
        empire.AutoPickBestResearchStation = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestResearchStation);
        empire.AutoPickBestMiningStation = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestMiningStation);
        empire.AutoExplore = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoExplore);
        empire.AutoColonize = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoColonize);
        empire.AutoBuildSpaceRoads = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildSpaceRoads);
        empire.AutoFreighters = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoFreighters);
        empire.AutoBuildResearchStations = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildResearchStations);
        empire.AutoBuildMiningStations = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildMiningStations);
        empire.AutoMilitary = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoMilitary);

        bool rushAll = flags.HasFlag(AuthoritativeEmpireAutomationFlags.RushAllConstruction);
        empire.RushAllConstruction = rushAll;
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
        Process(AuthoritativePlayerCommand command)
    {
        AuthoritativeCommandResult result = Applicator.Apply(command, Tick + 1);
        return Advance(result);
    }

    public (AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot)
        RejectAndAdvance(int sequence, string reason)
    {
        return Advance(new AuthoritativeCommandResult
        {
            Sequence = sequence,
            Accepted = false,
            Tick = Tick + 1,
            Reason = reason ?? "",
        });
    }

    (AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot) Advance(AuthoritativeCommandResult result)
    {
        Universe.SingleSimulationStep(Step);
        Tick++;
        return (result, AuthoritativeStateSnapshot.Capture(Universe, Tick));
    }

    public AuthoritativeDiplomacyPopup[] DrainDiplomacyPopups() => Diplomacy.DrainPopups();
}

public sealed class Authoritative4XClientReplica
{
    readonly UniverseScreen Universe;
    readonly FixedSimTime Step;
    readonly Authoritative4XCommandApplicator Applicator;
    readonly AuthoritativeDiplomacyManager Diplomacy;
    bool AuthoritativePaused;
    float AuthoritativeGameSpeed = 1f;

    public uint Tick { get; private set; }
    public AuthoritativeStateSnapshot LastSnapshot { get; private set; }
    public Authoritative4XRawHashDrift LastRawHashDrift { get; private set; }

    public Authoritative4XClientReplica(UniverseScreen universe, float dt = 1f / 60f, int[] humanEmpireIds = null)
    {
        Universe = universe;
        Step = new FixedSimTime(dt);
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
        authoritySnapshot.ApplyRelationshipPayload(Universe.UState);
        Universe.UState.Paused = AuthoritativePaused;
        Universe.UState.GameSpeed = AuthoritativeGameSpeed;
        if (result.Accepted)
        {
            AuthoritativeCommandResult local = Applicator.Apply(command, result.Tick);
            if (!local.Accepted)
                throw new System.InvalidOperationException($"Client replica rejected accepted command {command.Sequence}: {local.Reason}");
        }

        Universe.SingleSimulationStep(Step);
        Tick++;
        authoritySnapshot.ApplyEmpireRuntimePayload(Universe.UState);
        LastSnapshot = AuthoritativeStateSnapshot.Capture(Universe, Tick);
        bool rawHashMismatch = LastSnapshot.HashLo != authoritySnapshot.HashLo
                               || LastSnapshot.HashHi != authoritySnapshot.HashHi;
        if (!string.Equals(LastSnapshot.SyncDigest, authoritySnapshot.SyncDigest, StringComparison.Ordinal))
        {
            throw new Authoritative4XSyncMismatchException(command, result, authoritySnapshot, LastSnapshot);
        }
        LastRawHashDrift = rawHashMismatch
            ? new Authoritative4XRawHashDrift(command, result, authoritySnapshot, LastSnapshot)
            : null;
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
        AuthoritativeStateSnapshot clientSnapshot)
        : base(BuildMessage(result, authoritySnapshot, clientSnapshot))
    {
        Command = command;
        Result = result;
        AuthoritySnapshot = authoritySnapshot;
        ClientSnapshot = clientSnapshot;
    }

    static string BuildMessage(AuthoritativeCommandResult result,
        AuthoritativeStateSnapshot authoritySnapshot, AuthoritativeStateSnapshot clientSnapshot)
        => $"Authoritative sync mismatch at tick {clientSnapshot?.Tick ?? 0}: " +
           $"origin={result?.OriginPeer ?? 0} seq={result?.Sequence ?? 0} " +
           $"authority 0x{authoritySnapshot?.HashLo ?? 0UL:X16}:0x{authoritySnapshot?.HashHi ?? 0UL:X16}/" +
           $"{authoritySnapshot?.SyncDigest ?? ""}, client " +
           $"0x{clientSnapshot?.HashLo ?? 0UL:X16}:0x{clientSnapshot?.HashHi ?? 0UL:X16}/" +
           $"{clientSnapshot?.SyncDigest ?? ""}; {FirstPayloadDifference(authoritySnapshot?.Payload, clientSnapshot?.Payload)}";

    static string FirstPayloadDifference(string authorityPayload, string clientPayload)
    {
        string[] authority = (authorityPayload ?? "").Split('\n');
        string[] client = (clientPayload ?? "").Split('\n');
        int count = Math.Max(authority.Length, client.Length);
        for (int i = 0; i < count; ++i)
        {
            string a = i < authority.Length ? authority[i].TrimEnd('\r') : "<missing>";
            string c = i < client.Length ? client[i].TrimEnd('\r') : "<missing>";
            if (!string.Equals(a, c, StringComparison.Ordinal))
                return $"firstDiff line={i + 1} authority='{a}' client='{c}'";
        }
        return "payloads matched";
    }
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

    public void SubmitLocal(int peerId, AuthoritativePlayerCommand command)
    {
        if (IsResyncInProgress)
            return;
        ProcessCommand(peerId, command);
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

    void ProcessCommand(int fromPeer, AuthoritativePlayerCommand command)
    {
        (AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot) =
            !EmpireByPeer.TryGetValue(fromPeer, out int allowedEmpire) || allowedEmpire != command.EmpireId
                ? Authority.RejectAndAdvance(command.Sequence,
                    $"Peer {fromPeer} does not control empire {command.EmpireId}.")
                : Authority.Process(command);
        result.OriginPeer = fromPeer;

        LastResult = result;
        LastAuthoritySnapshot = snapshot;
        ProcessedCommands.Add(new Authoritative4XProcessedCommand(fromPeer, command, result, snapshot));
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
        Replica = new Authoritative4XClientReplica(clientUniverse, humanEmpireIds: humanEmpireIds);
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

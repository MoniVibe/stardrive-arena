using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SDUtils;
using Ship_Game.AI;
using Ship_Game.AI.StrategyAI.WarGoals;
using Ship_Game.Commands.Goals;
using Ship_Game.Fleets;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
using Ship_Game.Universe;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.Multiplayer.Authoritative;

public sealed partial class AuthoritativeStateSnapshot
{
    internal static void EmitUniversePreferenceRows(UniverseState us, uint tick, StringBuilder sb)
    {
        sb.Append("V|").Append((int)UniversePreferenceFlags(us)).AppendLine();
    }

    internal static void EmitStarDateRows(UniverseState us, uint tick, StringBuilder sb)
    {
        sb.Append("SD|").Append(FloatBits(us.StarDate)).AppendLine();
    }

    internal static void EmitEmpireRuntimeRows(UniverseState us, uint tick, StringBuilder sb)
    {
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
              .Append('|').Append(ResearchProgress(e))
              .AppendLine();
    }

    internal static void EmitUnlockedTechRows(UniverseState us, uint tick, StringBuilder sb)
    {
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
    }

    internal static void EmitPlayerDesignRows(UniverseState us, uint tick, StringBuilder sb)
    {
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
    }

    internal static void EmitRelationshipRows(UniverseState us, uint tick, StringBuilder sb)
    {
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
    }

    internal static void EmitSystemExplorationRows(UniverseState us, uint tick, StringBuilder sb)
    {
        foreach (SolarSystem system in us.Systems.OrderBy(s => s.Id))
            sb.Append("XS|").Append(system.Id)
              .Append('|').Append(system.ExploredByMask)
              .Append('|').Append(system.FullyExploredByMask)
              .AppendLine();
    }

    internal static void EmitPlanetExplorationRows(Planet p, uint tick, StringBuilder sb)
    {
        sb.Append("XP|").Append(p.Id)
          .Append('|').Append(p.ExploredByMask)
          .AppendLine();
    }

    internal static void EmitMarkForColonizationRows(Empire e, uint tick, StringBuilder sb)
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
    }

    internal static void EmitRefitGoalRows(Empire e, uint tick, StringBuilder sb)
    {
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
    }

    internal static void EmitFleetRequisitionRows(Empire e, uint tick, StringBuilder sb)
    {
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
    }

    internal static void EmitDeepSpaceGoalRows(Empire e, uint tick, StringBuilder sb)
    {
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
    }

    internal static void EmitFleetPatrolRows(Empire e, uint tick, StringBuilder sb)
    {
        foreach (FleetPatrol patrol in e.FleetPatrols.OrderBy(p => p.Name ?? "", StringComparer.Ordinal))
            sb.Append("FP|").Append(e.Id)
              .Append('|').Append(FleetPatrolPlanSignature(patrol))
              .AppendLine();
    }

    internal static void EmitFleetRuntimeRows(Empire e, uint tick, StringBuilder sb)
    {
        foreach (Fleet f in e.AllFleets.Where(IsReplayableFleetRuntimeRow).OrderBy(f => f.Key).ThenBy(f => f.Id))
            sb.Append("F|").Append(e.Id)
              .Append('|').Append(SnapshotFleetId(f))
              .Append('|').Append(f.Key)
              .Append('|').Append(f.Name ?? "")
              .Append('|').Append(f.FleetIconIndex)
              .Append('|').Append(SnapshotFleetCommandShipId(f))
              .Append('|').Append(FloatBits(f.FinalPosition.X))
              .Append('|').Append(FloatBits(f.FinalPosition.Y))
              .Append('|').Append(FloatBits(f.FinalDirection.X))
              .Append('|').Append(FloatBits(f.FinalDirection.Y))
              .Append('|').Append(FleetShipSignature(f))
              .Append('|').Append(FleetNodeSignature(f))
              .Append('|').Append(FleetPatrolSignature(f))
              .AppendLine();
    }

    static bool IsReplayableFleetRuntimeRow(Fleet fleet)
        => fleet != null
           && (fleet.Key > 0 || SnapshotFleetCommandShipId(fleet) > 0);

    internal static void EmitPlanetRuntimeRows(Planet p, uint tick, StringBuilder sb)
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
    }

    internal static void EmitPlanetTransformRows(Planet p, uint tick, StringBuilder sb)
    {
        sb.Append("PX|").Append(p.Id)
          .Append('|').Append(tick)
          .Append('|').Append(FloatBits(p.OrbitalAngle))
          .Append('|').Append(FloatBits(p.Position.X))
          .Append('|').Append(FloatBits(p.Position.Y))
          .AppendLine();
    }

    internal static void EmitBlueprintRows(Planet p, uint tick, StringBuilder sb)
    {
        if (p.HasBlueprints)
            sb.Append("BP|").Append(p.Id)
              .Append('|').Append(BlueprintSignature(p))
              .AppendLine();
    }

    internal static void EmitColonyTileRows(Planet p, uint tick, StringBuilder sb)
    {
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
              .Append('|').Append(tile.Building?.Strength ?? 0)
              .Append('|').Append(tile.Building?.CombatStrength ?? 0)
              .AppendLine();
        }
    }

    internal static void EmitGroundTroopRows(Planet p, uint tick, StringBuilder sb)
    {
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
    }

    internal static void EmitGroundCombatRows(Planet p, uint tick, StringBuilder sb)
    {
        for (int i = 0; i < p.ActiveCombats.Count; ++i)
        {
            Combat combat = p.ActiveCombats[i];
            string attackingTroop = GroundTroopRef(p, combat.AttackingTroop);
            string defendingTroop = GroundTroopRef(p, combat.DefendingTroop);
            string attackingBuilding = GroundBuildingRef(p, combat.AttackingBuilding);
            string defendingBuilding = GroundBuildingRef(p, combat.DefendingBuilding);
            if (!GroundCombatRefsReplayable(attackingTroop, defendingTroop,
                    attackingBuilding, defendingBuilding))
            {
                continue;
            }

            sb.Append("GC|").Append(p.Id)
              .Append('|').Append(i)
              .Append('|').Append(combat.Phase)
              .Append('|').Append(FloatBits(combat.Timer))
              .Append('|').Append(combat.AttackerLoyalty?.Id ?? 0)
              .Append('|').Append(combat.DefenseTile?.X ?? -1)
              .Append('|').Append(combat.DefenseTile?.Y ?? -1)
              .Append('|').Append(attackingTroop)
              .Append('|').Append(defendingTroop)
              .Append('|').Append(attackingBuilding)
              .Append('|').Append(defendingBuilding)
              .AppendLine();
        }
    }

    static bool GroundCombatRefsReplayable(string attackingTroop, string defendingTroop,
        string attackingBuilding, string defendingBuilding)
    {
        if (IsOffGroundRef(attackingTroop) || IsOffGroundRef(defendingTroop)
            || IsOffGroundRef(attackingBuilding) || IsOffGroundRef(defendingBuilding))
        {
            return false;
        }

        bool hasAttackingTroop = attackingTroop != "-";
        bool hasDefendingTroop = defendingTroop != "-";
        bool hasAttackingBuilding = attackingBuilding != "-";
        bool hasDefendingBuilding = defendingBuilding != "-";
        return hasAttackingTroop && hasDefendingTroop
               || hasAttackingBuilding && hasDefendingTroop
               || hasAttackingTroop && hasDefendingBuilding;
    }

    static bool IsOffGroundRef(string reference)
        => reference?.StartsWith("off,", StringComparison.Ordinal) == true;

    internal static void EmitConstructionQueueRows(Planet p, uint tick, StringBuilder sb)
    {
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

    internal static void EmitShipPresenceRows(UniverseState us, uint tick, StringBuilder sb)
    {
        foreach (Ship s in SnapshotShips(us))
            sb.Append("SC|").Append(s.Id)
              .Append('|').Append(s.Loyalty?.Id ?? 0)
              .Append('|').Append(s.ShipData?.Name ?? s.Name ?? "")
              .AppendLine();
    }

    internal static void EmitShipRuntimeRows(UniverseState us, uint tick, StringBuilder sb)
    {
        Ship[] snapshotShips = SnapshotShips(us);
        var activeShipIds = new HashSet<int>(snapshotShips.Select(s => s.Id));
        foreach (Ship s in snapshotShips)
        {
            int snapshotFleetId = SnapshotFleetId(s.Fleet);
            sb.Append("S|").Append(s.Id)
              .Append('|').Append(s.Loyalty?.Id ?? 0)
              .Append('|').Append(snapshotFleetId)
              .Append('|').Append(snapshotFleetId)
              .Append('|').Append((int)s.AI.State)
              .Append('|').Append((int)s.AI.CombatState)
              .Append('|').Append(VolatileShipPositionDigest)
              .Append('|').Append(VolatileShipPositionDigest)
              .Append('|').Append(VolatileShipPositionDigest)
              .Append('|').Append(VolatileShipPositionDigest)
              .Append('|').Append(FloatBits(s.ScuttleTimer))
              .Append('|').Append(SnapshotShipId(s.AI.Target, activeShipIds))
              .Append('|').Append(s.AI.HasPriorityTarget ? 1 : 0)
              .Append('|').Append(TargetQueueSignature(s, activeShipIds))
              .Append('|').Append(SnapshotShipId(s.AI.EscortTarget, activeShipIds))
              .Append('|').Append(ShipOrderQueueSignature(s, activeShipIds))
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
        }
    }

    internal static void EmitShipTransformRows(UniverseState us, uint tick, StringBuilder sb)
    {
        foreach (Ship s in SnapshotShips(us))
            sb.Append("SX|").Append(s.Id)
              .Append('|').Append(tick)
              .Append('|').Append(FloatBits(s.Position.X))
              .Append('|').Append(FloatBits(s.Position.Y))
              .Append('|').Append(FloatBits(s.Velocity.X))
              .Append('|').Append(FloatBits(s.Velocity.Y))
              .Append('|').Append(FloatBits(s.Rotation))
              .Append('|').Append(s.System?.Id ?? 0)
              .Append('|').Append(s.Active ? 1 : 0)
              .Append('|').Append(s.Dying ? 1 : 0)
              .Append('|').Append(FloatBits(s.YRotation))
              .Append('|').Append(FloatBits(s.XRotation))
              .AppendLine();
    }

    internal static void EmitWeaponFireRows(UniverseState us, uint tick, StringBuilder sb)
        => us.AuthoritativeWeaponFire.EmitRows(tick, sb);

    internal static void EmitShipVisibilityRows(UniverseState us, uint tick, StringBuilder sb)
    {
        foreach (Ship s in SnapshotShips(us))
            sb.Append("SV|").Append(s.Id)
              .Append('|').Append(ShipKnownByMask(us, s))
              .AppendLine();
    }

    internal static void EmitShipTroopRows(UniverseState us, uint tick, StringBuilder sb)
    {
        foreach (Ship s in SnapshotShips(us))
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
    }
}

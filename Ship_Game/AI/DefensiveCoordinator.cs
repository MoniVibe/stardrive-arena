using Ship_Game.Debug;
using Ship_Game.Ships;
using System;
using System.Collections.Generic;
using System.Linq;
using SDGraphics;
using SDUtils;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.AI
{
    public sealed class DefensiveCoordinator : IDisposable
    {
        readonly Empire Us;
        readonly Empire Player;
        public float DefenseDeficit;
        public Map<SolarSystem, SystemCommander> DefenseDict = new();
        int TotalValue;
        public float TroopsToTroopsWantedRatio;

        public int Id { get; }
        public string Name { get; }
        public Empire OwnerEmpire => Us;
        public DefensiveCoordinator(int id, Empire e, string name)
        {
            Id = id;
            Us = e;
            Player = e.Universe.Player;
            Name = name;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~DefensiveCoordinator()
        {
            Dispose(false);
        }

        void Dispose(bool disposing)
        {
            if (DefenseDict != null)
                foreach (var kv in DefenseDict)
                    kv.Value?.Dispose();
            DefenseDict = null;
        }

        void ClearEmptyPlanetsOfTroops()
        {
            //@TODO move this to planet.
            // FB - This code is crappy. And it launches troops into space combat zones as well
            // and it doesnt only clear empty planets but also adds the planet to defense dict. very misleading
            // also why are we running this for the player at all, why do we need to add to defense dict for players?
            for (int i = 0; i < Us.Universe.Planets.Count; i++)
            {
                Planet p = Us.Universe.Planets[i];
                if (p.Habitable
                    && Us != Player 
                    && p.Owner != Us 
                    && !p.EventsOnTiles() 
                    && !p.RecentCombat
                    && !p.TroopsHereAreEnemies(Us))
                {
                    p.ForceLaunchAllTroops(Us);
                }
                else if (p.Owner == Us && p.System != null && !DefenseDict.ContainsKey(p.System)) // This should stay here.
                {
                    DefenseDict.Add(p.System, new SystemCommander(this, p.System, Us));
                }
            }
        }

        void CalculateSystemImportance()
        {
            TotalValue = 0;

            KeyValuePair<SolarSystem, SystemCommander>[] kvs = DefenseDict.ToArray();
            for (int i = 0; i < kvs.Length; i++)
            {
                var kv = kvs[i];
                if (kv.Key.OwnerList.Contains(Us))
                {
                    kv.Value.UpdatePlanetTracker();
                    continue;
                }

                kv.Value.Dispose();
                DefenseDict.Remove(kv.Key);
            }

            foreach (var kv in DefenseDict)
                TotalValue += (int)kv.Value.UpdateSystemValue();

            foreach (var kv in DefenseDict)
                kv.Value.PercentageOfValue = kv.Value.TotalValueToUs / TotalValue.LowerBound(1);

            int ranker = 0;
            int split = DefenseDict.Count / 10;
            int splitStore = split;
            SystemCommander[] commanders = DefenseDict.Select(kv => kv.Value)
                .OrderBy(com => com.PercentageOfValue).ToArr();
            foreach (SystemCommander com in commanders)
            {
                split--;
                if (split <= 0)
                {
                    ranker++;
                    split = splitStore;
                    if (ranker > 10)
                        ranker = 10;
                }
                com.RankImportance = ranker;
            }
            foreach (SystemCommander com in commanders)
            {
                com.RankImportance = (int) (10 * (com.RankImportance / ranker));
                com.CalculateTroopNeeds();
            }
        }

        public SolarSystem GetNearestSystemNeedingTroops(Vector2 fromPos, Empire empire)
        {
            float maxDist = empire.Universe.Size * 2f;
            return DefenseDict.FindMaxKeyByValuesFiltered(
                com => com.TroopStrengthNeeded > 0
                       && com.System.PlanetList.Any(p => p.Owner == empire
                                                        && !p.MightBeAWarZone(empire)
                                                        && p.FreeTilesWithRebaseOnTheWay(empire) > 0),
                com => (1f - ((float)com.TroopCount / com.IdealTroopCount))
                       * com.TotalValueToUs
                       * ((maxDist - com.System.Position.Distance(fromPos)).LowerBound(0f) / maxDist)
            );
        }

        void ManageTroops()
        {
            if (Us.isPlayer) 
                return;

            TroopsInSystems troops    = new TroopsInSystems(Us, DefenseDict);
            int rebasedTroops         = RebaseIdleTroops(troops.TroopShips);
            TroopsToTroopsWantedRatio = (troops.TotalCurrentTroops + rebasedTroops) / (float)troops.TotalTroopWanted;

            if (TroopsToTroopsWantedRatio > 1.25f)
                ScrapExcessTroop(troops.TroopShips);
            else
                LaunchExcessTroops();
        }

        void ScrapExcessTroop(Array<Ship> troopShips)
        {
            foreach (Ship troopShip in troopShips)
            {
                if (troopShip.DesignRole == RoleName.troop 
                    && troopShip.AI.State == AIState.AwaitingOrders
                    && troopShip.GetOurFirstTroop(out Troop troop)
                    && troop.Level == Us.data.MinimumTroopLevel) // only scrap rookies
                {
                    troopShip.AI.OrderScrapShip();
                    return;
                }
            }
        }

        void LaunchExcessTroops()
        {
            foreach (var kv in DefenseDict)
                kv.Value.LaunchExcessTroops();
        }

        private struct TroopsInSystems
        {
            public readonly int TotalTroopWanted;
            public readonly int TotalCurrentTroops;
            public readonly Array<Ship> TroopShips;

            public TroopsInSystems(Empire empire, Map<SolarSystem, SystemCommander> DefenseDict)
            {
                TotalCurrentTroops = 0;
                TotalTroopWanted = 0;
                TroopShips = empire.GetAvailableTroopShips(out int troopsInFleets);
                foreach (var kv in DefenseDict)
                {
                    kv.Value.CreditIncomingRebases(TroopShips);
                    TotalCurrentTroops += kv.Value.TroopCount;
                    TotalTroopWanted += kv.Value.IdealTroopCount;
                }

                TotalCurrentTroops += troopsInFleets;
            }
        }

        private int RebaseIdleTroops(Array<Ship> troopShips)
        {
            int totalRebasedTroops = 0;
            for (int i = troopShips.Count - 1; i >= 0; i--)
            {
                Ship troopShip = troopShips[i];

                // ship is already in flight to a rebase target; respect those orders
                if (troopShip.AI.State == AIState.Rebase)
                    continue;

                SolarSystem solarSystem = GetNearestSystemNeedingTroops(troopShip.Position, troopShip.Loyalty);

                if (solarSystem == null)
                    break;

                if (!DefenseDict[solarSystem].AbsorbIdleTroop(troopShip))
                    continue;

                troopShips.RemoveAtSwapLast(i);
                totalRebasedTroops++;
            }
            return totalRebasedTroops;
        }

        public void ManageForcePool()
        {
            ClearEmptyPlanetsOfTroops();
            CalculateSystemImportance();
            ManageTroops();
        }

        public SystemCommander GetSystemCommander(SolarSystem system)
        {
            DefenseDict.TryGetValue(system, out SystemCommander systemCommander);
            return systemCommander;
        }
    }
}
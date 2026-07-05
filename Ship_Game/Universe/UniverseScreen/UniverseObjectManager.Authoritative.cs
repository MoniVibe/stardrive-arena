using SDUtils;
using Ship_Game.Ships;

namespace Ship_Game
{
    public partial class UniverseObjectManager
    {
        /// <summary>
        /// Passive authoritative clients consume host-authored ship transforms but
        /// must not advance gameplay simulation locally. This refreshes only the
        /// render/spatial side needed for picking and drawing those host transforms.
        /// </summary>
        public void UpdatePassiveAuthoritativeView()
        {
            if (UState.GameOver || Universe.IsExiting)
                return;

            TotalTime.Start();

            UpdateLists(removeInactiveObjects: false);
            UpdatePassiveSystemPresentation();
            Spatial.Update(Objects.GetItems());
            UpdateVisibleObjects();
            SyncPassiveVisibleShipSceneObjects(PassiveAuthoritativeFrameDeltaSeconds());

            TotalTime.Stop();
        }

        void UpdatePassiveSystemPresentation()
        {
            UpdatePassiveSolarSystemShipLists();

            for (int i = 0; i < UState.Systems.Count; ++i)
                UState.Systems[i].UpdatePassiveAuthoritativeView(Universe);

            UState.PlanetsTree.UpdateAll(UState.Planets.ToArr());
        }

        void UpdatePassiveSolarSystemShipLists()
        {
            for (int i = 0; i < UState.Systems.Count; ++i)
                UState.Systems[i].ShipList.Clear();

            Ship[] allShips = Ships.GetItems();
            for (int i = 0; i < allShips.Length; ++i)
            {
                Ship ship = allShips[i];
                if (ship?.Active == true && ship.System != null)
                    ship.System.ShipList.AddUniqueRef(ship);
            }
        }

        static float PassiveAuthoritativeFrameDeltaSeconds()
            => GameBase.Base?.Elapsed?.RealTime.Seconds ?? -1f;

        void SyncPassiveVisibleShipSceneObjects(float elapsedSeconds)
        {
            Ship[] visibleShips = VisibleShips;
            for (int i = 0; i < visibleShips.Length; ++i)
                visibleShips[i]?.SyncSceneObjectForPassiveAuthoritativeView(elapsedSeconds: elapsedSeconds);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using Ship_Game.Multiplayer.Authoritative;

namespace Ship_Game.Universe.SolarBodies
{
    public partial class SBProduction
    {
        public void ReplaceQueueForAuthoritativeSync(IEnumerable<QueueItem> items)
        {
            AssertCanMutateQueue("ReplaceQueueForAuthoritativeSync");
            foreach (PlanetGridSquare tile in P.TilesList)
                tile.RemoveQueueItem();

            lock (ConstructionQueue)
            {
                ConstructionQueue.Clear();
                foreach (QueueItem item in items ?? Enumerable.Empty<QueueItem>())
                {
                    if (item == null)
                        continue;
                    item.Planet = P;
                    ConstructionQueue.Add(item);
                    item.pgs?.SetQueueItem(item);
                }
                QueueSnapshotDirty = true;
            }
        }

        void AssertCanMutateQueue(string field)
        {
            AuthoritativeMutationGuard.AssertCanMutate(P, AuthoritativeMutationFamily.ConstructionQueue, field);
        }
    }
}

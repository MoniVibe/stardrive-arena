using System.Collections.Generic;
using Ship_Game.Data.Serialization;
using Ship_Game.Utils;

namespace Ship_Game.Universe
{
    [StarDataType]
    public class ExplorableGameObject : GameObject
    {
        // this is a tiny bitset where Empire.Id is the bit index for Explored:true|false
        [StarData] SmallBitSet ExploredBy;

        [StarData] public bool IsResearchable { get; private set; }
        public ExplorableGameObject(int id, GameObjectType type) : base(id, type)
        {
        }

        public void SetExploredBy(Empire empire) => ExploredBy.Set(empire.Id);
        public bool IsExploredBy(Empire empire)  => ExploredBy.IsSet(empire.Id);

        public void SetResearchable(bool value, UniverseState universe)
        {
            IsResearchable = value;
            if (value)
                universe.AddResearchableSolarBody(this);
            else
                universe.RemoveResearchableSolarBody(this);
        }

        public bool CanBeResearchedBy(Empire empire)
        {
            if (!IsResearchable || IsResearchStationDeployedBy(empire))
                return false;

            if (!empire.AI.HasGoal(g => g.IsResearchStationGoal(this)))
                return true;

            return false;
        }

        public bool IsResearchStationDeployedBy(Empire empire)
        {
            // TryGetValue, not the throwing indexer: a save crossing a patch can restore
            // IsResearchable=true on a body whose ResearchableSolarBodies entry is absent.
            // Not in the map means no station is tracked there, so none is deployed.
            return IsResearchable
                && empire.Universe.ResearchableSolarBodies.TryGetValue(this, out HashSet<int> deployedBy)
                && deployedBy.Contains(empire.Id);
        }
    }
}

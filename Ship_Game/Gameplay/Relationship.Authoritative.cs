using Ship_Game.AI.StrategyAI.WarGoals;
using Ship_Game.Multiplayer.Authoritative;

namespace Ship_Game.Gameplay
{
    public partial class Relationship
    {
        public void SetAuthoritativeDiplomacyState(Empire us, bool known, bool atWar, bool nap, bool trade,
            bool openBorders, bool alliance, bool peace)
        {
            AuthoritativeMutationGuard.AssertCanMutate(this, us, AuthoritativeMutationFamily.Diplomacy,
                "RelationshipState");

            Known = known;
            us.SetKnownEmpireForAuthoritativeSync(Them, known);
            AtWar = atWar;
            Treaty_NAPact = nap;
            Treaty_Trade = trade;
            Treaty_OpenBorders = openBorders;
            Treaty_Alliance = alliance;
            Treaty_Peace = peace;

            if (AtWar)
            {
                CanAttack = true;
                IsHostile = true;
                if (ActiveWar == null)
                    ActiveWar = War.CreateInstance(us, Them, WarType.ImperialistWar);
            }
            else
            {
                CanAttack = false;
                IsHostile = false;
                PreparingForWar = false;
                if (ActiveWar != null)
                {
                    ActiveWar.EndStarDate = us.Universe.StarDate;
                    WarHistory.Add(ActiveWar);
                    ActiveWar = null;
                }
            }
        }
    }
}

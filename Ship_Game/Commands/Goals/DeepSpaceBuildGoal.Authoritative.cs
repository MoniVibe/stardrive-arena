using SDGraphics;

namespace Ship_Game.Commands.Goals
{
    public partial class DeepSpaceBuildGoal
    {
        public void SetAuthoritativeReplayMovePosition(Vector2 movePosition)
        {
            HasAuthoritativeReplayMovePosition = true;
            AuthoritativeReplayMovePosition = movePosition;
        }
    }
}

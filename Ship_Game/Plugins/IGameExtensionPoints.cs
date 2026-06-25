using System;
using Ship_Game.GameScreens;

namespace Ship_Game.Plugins;

public interface IGameExtensionPoints
{
    void RegisterMainMenuAction(string buttonName, Func<GameScreen> createScreen);
}

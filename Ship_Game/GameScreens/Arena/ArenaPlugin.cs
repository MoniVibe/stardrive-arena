using Ship_Game.Plugins;

namespace Ship_Game.GameScreens.Arena;

public sealed class ArenaPlugin : IGamePlugin
{
    public string Name => "StarDrive Arena";
    public string Version => "1.0";

    public void Register(IGameExtensionPoints ext)
        => ext.RegisterMainMenuAction("arena", () => new ArenaCareerMenuScreen());
}

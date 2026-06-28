using Ship_Game.Plugins;

namespace Ship_Game.GameScreens.Arena;

public sealed class ArenaPlugin : IGamePlugin
{
    public const string ArenaButtonName = "arena";
    public const string Authoritative4XMultiplayerButtonName = "auth_4x_multiplayer";

    public string Name => "StarDrive Arena";
    public string Version => "1.0";

    public void Register(IGameExtensionPoints ext)
    {
        ext.RegisterMainMenuAction(ArenaButtonName, "Star Gladiator", () => new ArenaCareerMenuScreen());
        ext.RegisterMainMenuAction(Authoritative4XMultiplayerButtonName, "4X Multiplayer",
            () => new ArenaMultiplayerLobbyScreen(ArenaMultiplayerLobbySurface.Authoritative4X));
    }
}

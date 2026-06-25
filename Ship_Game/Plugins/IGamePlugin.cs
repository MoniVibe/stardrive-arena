namespace Ship_Game.Plugins;

public interface IGamePlugin
{
    string Name { get; }
    string Version { get; }
    void Register(IGameExtensionPoints ext);
}

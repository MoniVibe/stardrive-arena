using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ship_Game.GameScreens;

namespace Ship_Game.Plugins;

public readonly struct PluginMainMenuAction
{
    public readonly string ButtonName;
    public readonly string ButtonTitle;
    public readonly Func<GameScreen> CreateScreen;

    public PluginMainMenuAction(string buttonName, Func<GameScreen> createScreen)
        : this(buttonName, "", createScreen)
    {
    }

    public PluginMainMenuAction(string buttonName, string buttonTitle, Func<GameScreen> createScreen)
    {
        ButtonName = buttonName ?? "";
        ButtonTitle = buttonTitle ?? "";
        CreateScreen = createScreen;
    }
}

public static class PluginManager
{
    static readonly List<PluginMainMenuAction> MainMenuActions = new();

    public static PluginMainMenuAction[] RegisteredMainMenuActions => MainMenuActions.ToArray();

    public static void LoadAndRegister(string pluginsDir)
    {
        Clear();
        if (pluginsDir.IsEmpty() || !Directory.Exists(pluginsDir))
        {
            Log.Info($"PluginManager: plugin directory absent; skipping ({pluginsDir}).");
            return;
        }

        string[] dlls;
        try
        {
            dlls = Directory.GetFiles(pluginsDir, "*.dll", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception e)
        {
            Log.Warning($"PluginManager: could not scan {pluginsDir}: {e.Message}");
            return;
        }

        foreach (string dll in dlls)
            LoadPluginAssembly(dll);
    }

    public static void Clear()
    {
        MainMenuActions.Clear();
    }

    public static void RegisterMainMenuAction(string buttonName, Func<GameScreen> createScreen)
        => RegisterMainMenuAction(buttonName, "", createScreen);

    public static void RegisterMainMenuAction(string buttonName, string buttonTitle, Func<GameScreen> createScreen)
    {
        if (buttonName.IsEmpty() || createScreen == null)
            return;

        MainMenuActions.RemoveAll(a => string.Equals(a.ButtonName, buttonName, StringComparison.Ordinal));
        MainMenuActions.Add(new PluginMainMenuAction(buttonName, buttonTitle, createScreen));
        Log.Info($"PluginManager: registered main-menu action '{buttonName}'.");
    }

    static void LoadPluginAssembly(string dll)
    {
        try
        {
            Assembly assembly = Assembly.LoadFrom(dll);
            foreach (Type type in PluginTypes(assembly))
                RegisterPluginType(type);
        }
        catch (Exception e)
        {
            Log.Warning($"PluginManager: skipping plugin assembly {dll}: {e.Message}");
        }
    }

    static IEnumerable<Type> PluginTypes(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            Log.Warning($"PluginManager: partial type load from {assembly.FullName}: {e.Message}");
            types = e.Types?.Where(t => t != null).ToArray() ?? Array.Empty<Type>();
        }
        catch (Exception e)
        {
            Log.Warning($"PluginManager: could not enumerate {assembly.FullName}: {e.Message}");
            return Array.Empty<Type>();
        }

        return types.Where(t => t != null
                             && !t.IsAbstract
                             && typeof(IGamePlugin).IsAssignableFrom(t)
                             && t.GetConstructor(Type.EmptyTypes) != null);
    }

    static void RegisterPluginType(Type type)
    {
        try
        {
            var plugin = (IGamePlugin)Activator.CreateInstance(type);
            var ext = new ExtensionPoints();
            plugin.Register(ext);
            Log.Info($"PluginManager: registered plugin {plugin.Name} {plugin.Version} " +
                     $"({type.Assembly.GetName().Name}).");
        }
        catch (Exception e)
        {
            Log.Warning($"PluginManager: plugin type {type.FullName} failed registration: {e.Message}");
        }
    }

    sealed class ExtensionPoints : IGameExtensionPoints
    {
        public void RegisterMainMenuAction(string buttonName, Func<GameScreen> createScreen)
            => PluginManager.RegisterMainMenuAction(buttonName, createScreen);

        public void RegisterMainMenuAction(string buttonName, string buttonTitle, Func<GameScreen> createScreen)
            => PluginManager.RegisterMainMenuAction(buttonName, buttonTitle, createScreen);
    }
}

# General plugin loader for compiled engine extensions (inert with no plugins)

This is an upstream-ready PR spec for the minimal BlackBox plugin-loader infrastructure already proven in this fork. It is intentionally general: the engine loads compiled C# plugins and exposes narrow extension points, but it does not name or depend on any specific plugin.

This document is the source of truth for a squash/import PR. Do not treat this fork's full git diff as the upstream patch: this branch also contains the Star Gladiator arena plugin and unrelated arena work. The manifest below is the minimal self-contained loader subset.

## Rationale

BlackBox's mod system is data-only. It can load YAML/content/layout changes, but it does not `Assembly.Load` compiled mod code. That means a feature such as Star Gladiator Arena either has to be compiled into the engine or carried as a fork.

This PR adds a small, general mechanism for compiled C# plugins to register against the engine through extension points. It enables drop-in submods and experimental compiled extensions without coupling the engine to any one plugin and without requiring an engine rebuild for every plugin change.

The loader is inert unless plugin DLLs are present.

## Current Design

### `Ship_Game/Plugins/IGamePlugin.cs`

Plugin entry interface:

```csharp
namespace Ship_Game.Plugins;

public interface IGamePlugin
{
    string Name { get; }
    string Version { get; }
    void Register(IGameExtensionPoints ext);
}
```

A plugin DLL can contain one or more non-abstract `IGamePlugin` implementors with parameterless constructors.

### `Ship_Game/Plugins/IGameExtensionPoints.cs`

Single extension point today:

```csharp
using System;
using Ship_Game.GameScreens;

namespace Ship_Game.Plugins;

public interface IGameExtensionPoints
{
    void RegisterMainMenuAction(string buttonName, Func<GameScreen> createScreen);
}
```

This is deliberately narrow. Add new hooks only when a real plugin needs them.

### `Ship_Game/Plugins/PluginManager.cs`

Static plugin manager. Current implementation:

- `LoadAndRegister(string pluginsDir)` clears prior menu-action registrations.
- If the plugin directory path is empty or absent, it logs and returns.
- It scans only the top directory for `*.dll`.
- DLL paths are ordered with `StringComparer.OrdinalIgnoreCase` for stable load order.
- Each assembly is loaded with `Assembly.LoadFrom`.
- It discovers non-abstract `IGamePlugin` implementors with parameterless constructors.
- It instantiates each plugin and calls `Register(ext)`.
- It exposes `RegisteredMainMenuActions` as a snapshot array.
- `RegisterMainMenuAction` replaces an existing action with the same exact button name and ignores empty names/null factories.

Error isolation exists at every level:

- Directory scan exceptions are caught and logged.
- Assembly load exceptions are caught and logged.
- `ReflectionTypeLoadException` is caught; successfully loaded plugin types are still considered.
- Other type-enumeration failures are caught and logged.
- Per-plugin instantiation/registration failures are caught and logged.

One stale, malformed, or incompatible plugin must never crash engine startup.

Current source:

```csharp
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
    public readonly Func<GameScreen> CreateScreen;

    public PluginMainMenuAction(string buttonName, Func<GameScreen> createScreen)
    {
        ButtonName = buttonName ?? "";
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
    {
        if (buttonName.IsEmpty() || createScreen == null)
            return;

        MainMenuActions.RemoveAll(a => string.Equals(a.ButtonName, buttonName, StringComparison.Ordinal));
        MainMenuActions.Add(new PluginMainMenuAction(buttonName, createScreen));
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
    }
}
```

## Engine Wiring

### Startup scan

`Ship_Game/GameScreens/StarDriveGame.cs:117`:

```csharp
PluginManager.LoadAndRegister(Path.Combine(Directory.GetCurrentDirectory(), "Plugins"));
```

Runtime current working directory is the game/install directory, so plugins live under:

```text
<install>\Plugins\*.dll
```

### Main menu consumption

`Ship_Game/GameScreens/MainMenu/MainMenuScreen.cs:86-88`:

```csharp
foreach (PluginMainMenuAction action in PluginManager.RegisteredMainMenuActions)
    if (list.Find(action.ButtonName, out UIButton pluginButton))
        pluginButton.OnClick = _ => PluginAction_Clicked(action);
```

The click handler isolates plugin screen-factory failures:

```csharp
void PluginAction_Clicked(PluginMainMenuAction action)
{
    try
    {
        GameScreen screen = action.CreateScreen?.Invoke();
        if (screen != null)
            ScreenManager.GoToScreen(screen, clear3DObjects: true);
    }
    catch (Exception e)
    {
        Log.Warning($"MainMenuScreen: plugin menu action '{action.ButtonName}' failed: {e.Message}");
    }
}
```

## Inert-By-Default Safety Contract

- No `Plugins/` directory: `LoadAndRegister` logs and returns.
- Empty `Plugins/` directory: no DLLs are loaded and no menu actions are registered.
- `RegisteredMainMenuActions` is empty in both cases.
- Main menu loops over an empty action array and does nothing.
- A malformed/incompatible plugin DLL is caught and skipped; startup continues.
- A plugin type that fails construction or `Register` is caught and skipped; startup continues.
- A plugin menu action whose screen factory throws is caught and logged at click time; the main menu remains alive.
- The engine names no plugin type. The only coupling is the data-defined button name a plugin claims.

This means the loader has zero behavior change in stock installs with no plugins.

## File-By-File Manifest

### `Ship_Game/Plugins/IGamePlugin.cs` - new file

Add the plugin entry interface:

```csharp
namespace Ship_Game.Plugins;

public interface IGamePlugin
{
    string Name { get; }
    string Version { get; }
    void Register(IGameExtensionPoints ext);
}
```

### `Ship_Game/Plugins/IGameExtensionPoints.cs` - new file

Add the initial extension-point interface:

```csharp
using System;
using Ship_Game.GameScreens;

namespace Ship_Game.Plugins;

public interface IGameExtensionPoints
{
    void RegisterMainMenuAction(string buttonName, Func<GameScreen> createScreen);
}
```

### `Ship_Game/Plugins/PluginManager.cs` - new file

Add the static manager and `PluginMainMenuAction` struct. Use the full source from the Design section above.

### `StarDrive.csproj` - in-place add

Add compile includes for the three new files:

```xml
<Compile Include="Ship_Game\Plugins\IGameExtensionPoints.cs" />
<Compile Include="Ship_Game\Plugins\IGamePlugin.cs" />
<Compile Include="Ship_Game\Plugins\PluginManager.cs" />
```

### `Ship_Game/GameScreens/StarDriveGame.cs` - in-place add

Add:

```csharp
using Ship_Game.Plugins;
```

Then, in `Initialize()` after video/backend setup and before game initialization continues, add:

```csharp
PluginManager.LoadAndRegister(Path.Combine(Directory.GetCurrentDirectory(), "Plugins"));
```

Current fork line context: `StarDriveGame.cs:117`.

### `Ship_Game/GameScreens/MainMenu/MainMenuScreen.cs` - in-place add

Add:

```csharp
using Ship_Game.Plugins;
```

After existing built-in button lookups, loop registered plugin actions and wire matching data-defined buttons:

```csharp
foreach (PluginMainMenuAction action in PluginManager.RegisteredMainMenuActions)
    if (list.Find(action.ButtonName, out UIButton pluginButton))
        pluginButton.OnClick = _ => PluginAction_Clicked(action);
```

Add the isolated click handler:

```csharp
void PluginAction_Clicked(PluginMainMenuAction action)
{
    try
    {
        GameScreen screen = action.CreateScreen?.Invoke();
        if (screen != null)
            ScreenManager.GoToScreen(screen, clear3DObjects: true);
    }
    catch (Exception e)
    {
        Log.Warning($"MainMenuScreen: plugin menu action '{action.ButtonName}' failed: {e.Message}");
    }
}
```

Current fork line context:

- Main-menu loop: `MainMenuScreen.cs:86-88`.
- Handler: `MainMenuScreen.cs:175-187`.

## Design Consideration For Maintainers

The current minimal `RegisterMainMenuAction` design wires an existing data-defined button by name. It does not create or layout a button.

That is acceptable for this fork because the menu data can include an `arena` button and the plugin claims that button name. It also keeps the first upstream PR very small: no layout ownership, no plugin UI chrome model, no new menu item schema.

For true DLL-only drop-in plugins, upstream has two choices:

1. **Current minimal design: wire-existing button**
   - Plugin calls `RegisterMainMenuAction("my_button", factory)`.
   - The install or mod data must already provide a main-menu button named `my_button`.
   - Lowest risk and no layout policy decisions.

2. **Recommended enhancement: plugin contributes button metadata**
   - Extend the extension point later with label/slot/order/tooltip metadata.
   - MainMenuScreen can create a button when no data-defined button exists.
   - Better for friendlier DLL-only plugins, but it needs layout policy and should be a follow-up once upstream accepts the basic loader.

Do not hide this tradeoff in the upstream PR. The current PR is a minimal safe loader, not a full plugin UI contribution system.

## Tests

Existing tests in this fork:

- `UnitTests/Graphics/ArenaRenderSmokeTests.cs:2620` - `ArenaPluginManagerEmptyDirectory_Headless`
  - Missing plugin directory registers no actions.
  - Empty plugin directory registers no actions.

- `UnitTests/Graphics/ArenaRenderSmokeTests.cs:2644` - `ArenaPluginManagerRegistersMainMenuAction_Headless`
  - Direct registration exposes one action.
  - Action button name is preserved.
  - Registered factory constructs the expected screen.

- `UnitTests/Graphics/ArenaRenderSmokeTests.cs:2665` - `ArenaPluginManagerLoadsDropInArenaDll_Headless`
  - Empty temp plugin dir starts with no action.
  - Copies built `StarDriveArena.dll` into the temp plugin dir.
  - `PluginManager.LoadAndRegister` discovers the DLL.
  - The `arena` action constructs `ArenaCareerMenuScreen`.

Upstream test adaptation:

- The empty-dir test transfers directly.
- The manual-registration test transfers directly if it uses a tiny test `GameScreen` or mock `GameScreen` factory instead of Arena.
- The drop-in DLL test should not depend on `StarDriveArena.dll`, because the arena plugin will not exist in a clean BlackBox checkout. Replace it with a minimal stub plugin assembly in the test fixture:
  - `public sealed class StubPlugin : IGamePlugin`
  - `Name => "Stub Plugin"`
  - `Version => "1.0"`
  - `Register(ext) => ext.RegisterMainMenuAction("stub_plugin", () => new MockGameScreen())`

Recommended upstream tests:

1. `PluginManagerEmptyDirectory_Headless`
2. `PluginManagerRegistersMainMenuAction_Headless`
3. `PluginManagerLoadsStubPluginDll_Headless`
4. Optional hardening test: include a malformed/non-plugin DLL or a plugin whose constructor/register method throws; assert scan completes and valid plugins still register.

## Import Checklist

1. Add `Ship_Game/Plugins/IGamePlugin.cs`.
2. Add `Ship_Game/Plugins/IGameExtensionPoints.cs`.
3. Add `Ship_Game/Plugins/PluginManager.cs`.
4. Add the three `<Compile Include>` entries to `StarDrive.csproj`.
5. Add `using Ship_Game.Plugins;` to `StarDriveGame.cs`.
6. Add `PluginManager.LoadAndRegister(Path.Combine(Directory.GetCurrentDirectory(), "Plugins"));` in `StarDriveGame.Initialize()`.
7. Add `using Ship_Game.Plugins;` to `MainMenuScreen.cs`.
8. Add the `RegisteredMainMenuActions` loop after built-in button wiring.
9. Add the isolated `PluginAction_Clicked` handler.
10. Add arena-free loader tests using a stub plugin DLL/test fixture.
11. Build `StarDrive.csproj`.
12. Build `UnitTests/SDUnitTests.csproj`.
13. Run the loader tests.
14. Launch a stock install with no `Plugins/` directory and confirm no behavior change.

## Provenance

This spec was produced from the working StarDrive arena fork after the loader was implemented and tested. The upstream import should be a squash/manual application of this manifest, not a cherry-pick of the fork branch.

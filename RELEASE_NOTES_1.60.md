# BlackBox Jupiter 1.60 — Release Notes

This is the first release in the **Jupiter** line: the post-migration, 64-bit + MonoGame revival of StarDrive BlackBox. It supersedes the Mars 1.51 line, but does **not** replace it on disk — see "Coexistence with Mars 1.51" below.

## Headline changes

- **Codename: Jupiter** (was Mars through 1.51). Jupiter is the largest planet — fitting for the scale of the migration.
- **64-bit engine.** The 4 GB process-memory ceiling is gone. Combined Arms and other heavy-content modes that hit OOM on Mars run cleanly on Jupiter.
- **MonoGame 3.8 renderer** replaces the original XNA + SunBurn stack. SunBurn middleware was discontinued in 2014; the migration ports the rendering pipeline to a maintained, open-source equivalent that runs on current Windows.
- **All six broken-on-migration effects restored**: BeamFX, scale, Thrust, desaturate, BasicFogOfWar, PlanetHalo.
- **Skinned/animated meshes** — the Ralyeh `ship17` family articulates again (limbs and turret animation playback).
- **Material maps** (normal, specular, emissive) on all hulls.
- **Post-process passes**: bloom, screen-space distortion, fog-of-war.
- **Shadow-map infrastructure restored** but currently dormant — no screen attaches it in 1.60 (the universe view's coplanar layout produces no visible benefit and triggered planet self-shadow artifacts during testing, so the pre-pass is deliberately skipped there). The Shadow Quality graphics option has no visible effect in 1.60; planned for future scenes (hangar floor, 3D fleet view, planet-surface combat) that genuinely benefit from cast shadows.
- **Steam SDK x64** via Steamworks.NET — achievements, stats, and cloud saves work in the 64-bit binary.
- **Combined Arms compatible** — the major 1.51 mod runs unmodified on Jupiter.

## Default install path

Jupiter installs to **`C:\Games\StarDrivePlus64`** by default (was `C:\Games\StarDrivePlus` through 1.51). The `64` suffix signals the bitness change so users with both versions on disk can tell them apart.

The installer **prompts** you for the install location with `C:\Games\StarDrivePlus64` pre-filled. You can override via the directory page if you want to install elsewhere (for example, to upgrade in place over an existing Mars 1.51 directory). The installer does **not** auto-target the Steam app folder or the Mars 1.51 path — you have to browse to those explicitly.

Subsequent Jupiter patches (1.60.x → 1.60.y) read `HKLM\Software\StarDrivePlus64\InstallPath` and re-use the directory that the first Jupiter install wrote there, so the standard upgrade-in-place ergonomics still work within the Jupiter line.

## Install and update flow

**First-time install (Jupiter 1.60):**

1. **Download** the installer from [https://stardriveteam.itch.io/jupiter-160](https://stardriveteam.itch.io/jupiter-160) (~690 MB).
2. **Run the .exe.** Windows SmartScreen warns that the installer is unsigned — click **More info** → **Run anyway** (see "SmartScreen warning" under Known limitations).
3. **Accept the UAC prompt.** The installer needs admin rights to write into `C:\Games\` and update the registry.
4. **Choose an install location.** The directory page pre-fills `C:\Games\StarDrivePlus64`. You can browse to a different location, including a Steam folder (see "Steam folder install" below).
5. **.NET 8 prerequisite.** If the installer detects that the .NET 8 Desktop Runtime (x64) isn't present, it launches Microsoft's official runtime installer as a prerequisite step (~56 MB extra). Accept the second UAC prompt; this only happens once per machine.
6. **Finish.** Desktop and Start Menu shortcuts named **StarDrive BlackBox 64** are created. Launch the game from either.
7. **First launch — in-game auto-updater fires.** On the main menu, a top-left popup checks for the latest 1.60 patch. Click to download and apply; the game restarts into the patched build automatically. Right-click to dismiss for the session.

**Subsequent patch updates (1.60.x → 1.60.y):**

1. The in-game auto-updater checks for new patches on every launch and shows a top-left popup with the new version number. Hover for the changelog.
2. Click the popup. The game downloads the cumulative patch zip, applies it on top of the install, requests UAC if the install path requires elevated writes, and restarts into the patched build.
3. If you decline once, the popup reappears on the next launch. No automatic background updates.

**Manual patch install** (fallback if the in-game updater can't run, or for offline machines): download the patch installer .exe from the [GitHub Releases page](https://github.com/TeamStarDrive/StarDrive/releases), run it, and point it at your existing install directory. Patches are cumulative — you only need the latest one.

## Steam folder install (new in 1.60)

The Jupiter installer can install **over** a vanilla 15b Steam install of StarDrive so Steam keeps tracking your playtime under the title you already own. **This is a new feature in 1.60 — supported, but take it with a grain of salt.** It works in straightforward cases, but the Steam-side ergonomics are awkward and the failure mode (Steam silently overwriting Jupiter with vanilla 15b) is unforgiving.

**How to use it:**

1. Run the Jupiter installer normally.
2. On the directory page, the installer auto-detects an existing Steam install of StarDrive via the Uninstall registry entry and offers to point at it. Accept that offer, or browse manually to your Steam `steamapps\common\StarDrive` folder.
3. Complete the install. Jupiter files land on top of the Steam install's vanilla 15b content; Steam's existing shortcuts continue to launch the game (now Jupiter instead of 15b).

**Before you launch the game through Steam, do these two things:**

1. **Disable Steam auto-updates for StarDrive.** In your Steam library: right-click StarDrive → **Properties** → **Updates** → set to **Only update this game when I launch it** (or, better, launch via the desktop/Start Menu shortcut and never click the Steam launcher entry). Steam's StarDrive depot is **vanilla 15b** — the original 2013 publisher build, not any BlackBox version. The next Steam auto-sync will overwrite Jupiter back to stock 15b without asking.
2. **Never run "Verify Integrity of Game Files"** on StarDrive in Steam. Same depot, same wipe.

**Known caveats:**

- Steam reports the title as plain **"StarDrive"** in your profile and playtime. Steam has no knowledge of BlackBox, Mars, or Jupiter — the DisplayName for AppID 220660 is just "StarDrive".
- **Steam achievements and cloud-save sync are not wired in 1.60**. The Steam SDK x64 binding is in place; the integration just isn't done yet.
- The maintainer has no SteamPipe push access, so this overlay install is the closest we can get to a "Steam release" without a publisher relationship.

If any of this feels too fiddly, install to `C:\Games\StarDrivePlus64` (the default) and skip the Steam path entirely. The game runs identically — you just don't get Steam playtime tracking.

## Coexistence with Mars 1.51

A user with Mars 1.51 already installed at `C:\Games\StarDrivePlus` (or in their Steam library) **keeps it**. Jupiter installs side by side at `StarDrivePlus64`. The two majors share a save folder at `%APPDATA%\StarDrive\` but partition the load list by **save-game version**:

- Mars 1.51 saves: `SaveGameVersion = 20`
- Jupiter 1.60 saves: `SaveGameVersion = 21`

Each version's load screen filters out the other version's saves, so you'll see only your own saves in either menu. **No saves are deleted, modified, or corrupted** — they're just invisible to the wrong version. If you uninstall Jupiter and go back to Mars 1.51, your Mars saves are still there exactly as you left them.

**Practical implications**:

- A save started on Mars 1.51 cannot be loaded in Jupiter 1.60, and vice versa. There's no migration path — finish your in-progress Mars campaigns on Mars, or start fresh on Jupiter.
- The desktop shortcut behavior is install-path-dependent. If you have shortcuts pointing at `C:\Games\StarDrivePlus\StarDrive.exe`, they keep launching Mars. Jupiter installs its own shortcuts pointing at `StarDrivePlus64`.
- **User-content folders are *not* partitioned** the way save games are. `Saved Designs\`, `Fleet Designs\`, `Saved Setups\`, `Saved Races\`, and `Colony Blueprints\` under `%APPDATA%\StarDrive\` are shared by both versions. A ship/fleet/setup file authored on Jupiter may reference modules, hulls, or tech IDs that Mars 1.51 doesn't recognise (and vice versa) — Mars will either reject it or load it with the unknown bits silently dropped, which can produce a half-working design. Until the format diverges enough to break loading outright, **treat these folders as one-way: prefer the newer (Jupiter) version, or back up the folders before switching back to Mars.**

## Cross-major discovery

Mars 1.51 users who have applied patch `mars-patch-1.51.15118` or later see a top-left popup on the main menu when Jupiter 1.60 is detected as available. Clicking it opens the itch.io page for Jupiter and exits the game so the new installer can run cleanly. Right-clicking dismisses it for that session.

If you're on a Mars 1.51 build *before* `15118`, the popup does not exist — your game's auto-updater silently suppresses cross-major notifications. Apply the latest Mars 1.51 patch through the standard in-game flow (the prompt fires on the next launch), and the cross-major popup appears on the launch after that.

## Mods

- **Combined Arms** is compatible with Jupiter 1.60.
- **Star Trek: Shattered Alliance** — verify against Jupiter before assuming compatibility; some mods that depend on specific XNA/SunBurn quirks may need maintainer updates.

## System requirements

- **Windows 10 or later, 64-bit**.
- **.NET 8 Desktop Runtime (x64)** — **bundled with the installer**. The Mars 1.51 line ran on .NET Framework 4.8 (built into Windows); Jupiter migrated to .NET 8 (`net8.0-windows`), which is a separate runtime. The Jupiter 1.60 installer detects whether you already have it and runs Microsoft's official runtime installer as a prerequisite step if you don't (~56 MB extra at install time, accept the UAC prompt when it appears). Install once; future Jupiter patches reuse the same runtime — patches do NOT bundle it. If you ever need the runtime separately, grab it from <https://dotnet.microsoft.com/download/dotnet/8.0> (pick **Windows → Desktop Runtime → x64**).

## Known limitations

- **SmartScreen warning on first install**: the Jupiter 1.60 installer is **unsigned**, same as Mars 1.51 and earlier. When you download and run it, Windows will show a "Windows protected your PC" dialog. Click **More info** → **Run anyway** to proceed. Code signing is being evaluated for a later 1.60.x patch; for now the install path matches the Mars-line precedent. (The Mars 1.51 → Jupiter 1.60 cross-major popup, when clicked, exits the running game and opens the itch.io page — you download the new installer from there and dismiss the SmartScreen warning the same way.)
- **Steam folder install is supported but new** — see the "Steam folder install (new in 1.60)" section above for the gotchas (disabling Steam auto-update to prevent overwrite, "Verify Integrity of Game Files" wipes Jupiter, achievements/cloud-save not yet wired).
- The legacy `mars-1.51` development branch on GitHub is preserved for back-port hotfixes to the 32-bit Mars line. New feature work targets the Jupiter line.
- **Don't install Jupiter into a Mars 1.51 directory.** The 1.60 installer permits browsing to an existing Mars install (e.g. `C:\Games\StarDrivePlus`) and will write Jupiter files on top, but it does **not** remove pre-existing Mars files first — you end up with a Frankenstein directory: Jupiter binaries plus orphan Mars/SunBurn/XNA files, plus two registry entries (`HKLM\Software\StarDrive` from Mars and `HKLM\Software\StarDrivePlus64` from Jupiter) that both point at the same directory. Running Mars's uninstaller after this would delete a mix of Mars-era and Jupiter-era files and break the Jupiter install. The supported flow is: install Jupiter at the default `C:\Games\StarDrivePlus64`, leave Mars where it is (or uninstall Mars separately first via Add/Remove Programs).

## For developers

- Branch: Jupiter line lives on `main` (post §5.1 release).
- Build counter: stamped at CI build time from `github.run_number` (zero-padded to 5 digits), so the public version stamp is `1.60.<run_number>`. The static `AssemblyVersion` value in the repo (`1.60.00000`) is a placeholder for local dev builds — CI overwrites it.

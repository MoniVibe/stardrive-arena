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
- **Basic shadow maps**.
- **Steam SDK x64** via Steamworks.NET — achievements, stats, and cloud saves work in the 64-bit binary.
- **Combined Arms compatible** — the major 1.51 mod runs unmodified on Jupiter.

## Default install path

Jupiter installs to **`C:\Games\StarDrivePlus64`** by default (was `C:\Games\StarDrivePlus` through 1.51). The `64` suffix signals the bitness change so users with both versions on disk can tell them apart.

The installer **prompts** you for the install location with `C:\Games\StarDrivePlus64` pre-filled. You can override via the directory page if you want to install elsewhere (for example, to upgrade in place over an existing Mars 1.51 directory). The installer does **not** auto-target the Steam app folder or the Mars 1.51 path — you have to browse to those explicitly.

Subsequent Jupiter patches (1.60.x → 1.60.y) read `HKLM\Software\StarDrivePlus64\InstallPath` and re-use the directory that the first Jupiter install wrote there, so the standard upgrade-in-place ergonomics still work within the Jupiter line.

## Coexistence with Mars 1.51

A user with Mars 1.51 already installed at `C:\Games\StarDrivePlus` (or in their Steam library) **keeps it**. Jupiter installs side by side at `StarDrivePlus64`. The two majors share a save folder at `%APPDATA%\StarDrive\` but partition the load list by **save-game version**:

- Mars 1.51 saves: `SaveGameVersion = 20`
- Jupiter 1.60 saves: `SaveGameVersion = 21`

Each version's load screen filters out the other version's saves, so you'll see only your own saves in either menu. **No saves are deleted, modified, or corrupted** — they're just invisible to the wrong version. If you uninstall Jupiter and go back to Mars 1.51, your Mars saves are still there exactly as you left them.

**Practical implications**:

- A save started on Mars 1.51 cannot be loaded in Jupiter 1.60, and vice versa. There's no migration path — finish your in-progress Mars campaigns on Mars, or start fresh on Jupiter.
- The desktop shortcut behavior is install-path-dependent. If you have shortcuts pointing at `C:\Games\StarDrivePlus\StarDrive.exe`, they keep launching Mars. Jupiter installs its own shortcuts pointing at `StarDrivePlus64`.

## Cross-major discovery

Mars 1.51 users who have applied patch `mars-patch-1.51.15118` or later see a top-left popup on the main menu when Jupiter 1.60 is detected as available. Clicking it opens the itch.io page for Jupiter and exits the game so the new installer can run cleanly. Right-clicking dismisses it for that session.

If you're on a Mars 1.51 build *before* `15118`, the popup does not exist — your game's auto-updater silently suppresses cross-major notifications. Apply the latest Mars 1.51 patch through the standard in-game flow (the prompt fires on the next launch), and the cross-major popup appears on the launch after that.

## Mods

- **Combined Arms** is compatible with Jupiter 1.60.
- **Star Trek: Shattered Alliance** — verify against Jupiter before assuming compatibility; some mods that depend on specific XNA/SunBurn quirks may need maintainer updates.

## Known limitations

- **Steam release**: the maintainer has no SteamPipe push access for the StarDrive Steam app (AppID 220680). Jupiter installs into the Steam app folder are technically possible (browse there manually) but Steam's manifest will still report "StarDrive 1.0" — Steam-side achievement / cloud-save sync is not guaranteed.
- The legacy `mars-1.51` development branch on GitHub is preserved for back-port hotfixes to the 32-bit Mars line. New feature work targets the Jupiter line.

## For developers

- Branch: Jupiter line lives on `main` (post §5.1 release).
- Build counter: stamped at CI build time from `github.run_number` (zero-padded to 5 digits), so the public version stamp is `1.60.<run_number>`. The static `AssemblyVersion` value in the repo (`1.60.00000`) is a placeholder for local dev builds — CI overwrites it.

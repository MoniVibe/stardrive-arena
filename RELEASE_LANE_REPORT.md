# Release Packaging Lane Report

## Deliverables

- Added `Tools/Package-Release.ps1`.
  - Builds `StarDrive.csproj` and `StarDriveArena.csproj` with `-p:Platform=x64`.
  - Reads `ArenaMultiplayerSettings.ProtocolVersion` from `Ship_Game\GameScreens\Arena\ArenaMultiplayerSession.cs`.
  - Collects `StarDrive.dll`, `SDUtils.dll`, `SDGraphics.dll`, `SDLockstep.dll`, and `Plugins\StarDriveArena.dll`.
  - Writes `manifest.json`, includes `INSTALL.md`, creates `stardrive-arena-<version>.zip`, and validates the manifest by reopening the zip and parsing JSON.
- Added `INSTALL.md`.
  - Install/uninstall guide for the free overlay model.
  - Code-verified 4X lobby guide: main menu `4X Multiplayer`, default TCP port `47377`, `HOST`, `JOIN`, `READY`, `LAUNCH`, slots `P3` through `P9`, protocol/build mismatch rejection.
- Added `RELEASE.md`.
  - Maintainer checklist for tests, Release packaging, 2-machine smoke, tagging, and GitHub release upload.

## Code Verification Notes

- `Ship_Game\GameScreens\Arena\ArenaPlugin.cs` registers the main-menu action titled `4X Multiplayer`.
- `Ship_Game\GameScreens\MainMenu\MainMenuScreen.cs` adds registered plugin main-menu actions.
- `Ship_Game\GameScreens\Arena\ArenaMultiplayerLobbyScreen.cs` defines `DefaultPort = 47377`, `DefaultJoinPeerSlot = 3`, `LastJoinPeerSlot = 9`, and the `HOST`, `JOIN`, `READY`, `LAUNCH` lobby actions.
- `Ship_Game\GameScreens\Arena\ArenaMultiplayerSession.cs` defines `ArenaMultiplayerSettings.ProtocolVersion = 2`.
- `Ship_Game\Multiplayer\Authoritative\Authoritative4XLobbyNetworkFlow.cs` rejects mismatched authoritative 4X protocol/start payloads.

## Verified Debug Run

Command:

```powershell
pwsh -NoLogo -NoProfile -File Tools\Package-Release.ps1 -Configuration Debug
```

Result:

- `StarDrive.csproj` Debug|x64 build succeeded.
- `StarDriveArena.csproj` Debug|x64 build succeeded.
- Package created: `dist\release\stardrive-arena-g8e28fe80c-p2.zip`.
- Manifest validation passed by reopening the zip and JSON round-tripping `manifest.json`.
- Manual archive inspection confirmed entries:
  - `INSTALL.md`
  - `manifest.json`
  - `StarDrive.dll`
  - `SDUtils.dll`
  - `SDGraphics.dll`
  - `SDLockstep.dll`
  - `Plugins/StarDriveArena.dll`

Manifest summary:

- Version: `g8e28fe80c-p2`
- Git SHA: `8e28fe80caa30990ee63e6326626c24da6ce9438`
- Protocol version: `2`
- Configuration: `Debug`
- File count: `5`

## Caveats

- The Debug build emitted repeated `NU1900` warnings because the restricted environment could not reach `https://api.nuget.org/v3/index.json` for vulnerability metadata. Builds completed with `0 Error(s)`.
- The local `game\` build-output layout was sufficient for the Debug package run; no commercial game content was required or packaged.
- Release configuration packaging and the 2-machine live smoke remain maintainer release-gate steps in `RELEASE.md`.

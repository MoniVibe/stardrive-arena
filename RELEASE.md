# StarDrive Arena Release Checklist

This release model is a free overlay. Ship only the StarDrive Arena DLL bundle and release metadata. Do not redistribute BlackBox/Jupiter content.

## Build Package

1. Verify the release branch and worktree are clean.
2. Run the full current test/scenario suites for the release branch.
3. Build the release overlay:

```powershell
pwsh -File Tools/Package-Release.ps1 -Configuration Release -Version X.Y
```

If `-Version` is omitted, the package version defaults to `g<git-short-sha>-p<protocol-version>`.

The script builds `StarDrive.csproj` and `StarDriveArena.csproj` for `x64`, collects the five overlay DLLs from `game\`, writes `manifest.json`, includes `INSTALL.md`, creates `dist\release\stardrive-arena-<version>.zip`, and reopens the zip to validate the manifest JSON.

## Smoke Release

1. Install clean BlackBox Jupiter 1.60 on two Windows x64 machines.
2. Extract the generated overlay zip over each game folder.
3. Start both games and verify the main menu shows `4X Multiplayer`.
4. Host on one machine with the default TCP port `47377`.
5. Join from the second machine with the host IP and port.
6. Ready both players, launch, and confirm the live authoritative 4X game starts.
7. Keep the zip, manifest output, release notes, and smoke notes with the release evidence.

## Publish

1. Tag the release as `arena-vX.Y`.
2. Create a GitHub release.
3. Attach `stardrive-arena-<version>.zip`.
4. Paste the manifest hashes or attach the manifest output.
5. Update `INSTALL.md` if the lobby flow, default port, prerequisites, or known limits changed.

## Caveats

- The package must contain only `StarDrive.dll`, `SDUtils.dll`, `SDGraphics.dll`, `SDLockstep.dll`, `Plugins\StarDriveArena.dll`, `INSTALL.md`, and `manifest.json`.
- If a build machine is missing SDKs or the local `game\` build-output layout, fix provisioning before packaging. Do not work around that by copying commercial content into the release artifact.

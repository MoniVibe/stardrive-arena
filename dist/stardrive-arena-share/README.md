# Star Gladiator Arena + 4X Multiplayer for StarDrive BlackBox

Star Gladiator adds a career-mode arena to StarDrive BlackBox: build a combat roster, pick risk/reward bouts, earn fame, fight ladder contenders, survive bosses, and watch a living rival ecosystem churn in the background.

This package also includes the current experimental authoritative 4X multiplayer support used by the arena test branch: TCP lobby, race/trait/setup selection, host-authoritative command requests, client state sync, save-transfer resync, human diplomacy, and QA probes.

This repository package intentionally contains only original contribution code plus patch files. It does not contain StarDrive, BlackBox, Combined Arms, game assets, binaries, or decompiled engine source files.

## Dependencies

You bring your own licensed copies:

- StarDrive on Steam.
- TeamStarDrive/StarDrive BlackBox `jupiter-patch-1.60.00045`.
- Combined Arms 9.0A content/mod install.
- .NET 8 SDK or newer. This package has also been deploy-tested with .NET SDK 10.0.201.

## Package Contents

- `StarDriveArena/` - the arena plugin source and portable `StarDriveArena.csproj`.
- `engine-patches/stardrive-arena-mp-full.patch` - required all-in-one patch against stock BlackBox `jupiter-patch-1.60.00045`. This is the release install patch.
- `engine-patches/plugin-loader.patch` - narrow upstream-ready plugin-loader slice, kept for maintainers.
- `engine-patches/determinism.patch` - narrow optional determinism slice, kept for maintainers.
- `docs/PLUGIN_LOADER_UPSTREAM_PR.md` - upstream-ready plugin-loader PR spec.
- `docs/DETERMINISM_UPSTREAM_PR.md` - upstream-ready optional determinism PR spec.

The separate private `arena-pc-dlls.zip` test artifact is not part of the public release package. It contains patched binaries for syncing local test machines only and should not be redistributed.

## Install / Build

1. Clone your own BlackBox checkout and pin it to the supported tag.

   ```powershell
   git clone https://github.com/TeamStarDrive/StarDrive.git BlackBox
   cd BlackBox
   git checkout jupiter-patch-1.60.00045
   ```

   `--recurse-submodules` is only needed if you intend to rebuild `SDNative.dll` yourself. Normal plugin builds can use the native DLL from your existing BlackBox + Combined Arms install.

2. Apply the all-in-one release patch from this package.

   ```powershell
   git apply path\to\stardrive-arena-share\engine-patches\stardrive-arena-mp-full.patch
   ```

   The narrow `plugin-loader.patch` and `determinism.patch` files are maintainer slices. They are not enough for the current arena + multiplayer release by themselves.

3. Build the patched engine.

   ```powershell
   dotnet build StarDrive.csproj -c Release -p:Platform=x64
   ```

4. Build the arena plugin against that patched checkout.

   ```powershell
   dotnet build path\to\stardrive-arena-share\StarDriveArena\StarDriveArena.csproj -c Release -p:Platform=x64 -p:BlackBoxCheckout=C:\path\to\BlackBox
   ```

   The arena project writes `StarDriveArena.dll` to `$(BlackBoxCheckout)\game\Plugins\` by default.

5. Deploy the build outputs into a real StarDrive BlackBox + Combined Arms install. Do not run from a bare source checkout. Copy the patched engine DLLs from the BlackBox build output and the plugin DLL into the matching runtime folders of your actual install:

   ```text
   StarDrive.dll
   SDUtils.dll
   SDGraphics.dll
   SDLockstep.dll
   Plugins\StarDriveArena.dll
   ```

   `SDNative.dll` and game content should come from the real install unless you deliberately rebuilt the native submodule.

6. Launch the real BlackBox + Combined Arms install. The main menu exposes the Star Gladiator / multiplayer entry points through the patched menu and plugin loader.

## Runtime Notes

The runtime install needs the normal BlackBox + Combined Arms files, including native libraries and game content. A Windows dialog or log message saying `SDNative.dll` could not be loaded usually means the game is being launched from an incomplete source/build directory or the runtime files are missing. It is usually not a security block.

4X multiplayer is currently experimental. For best results, all machines should use the exact same patched DLL set and the same Combined Arms content. The host is authoritative; clients should join through the multiplayer lobby. If a client diverges, the resync path can request the host's authoritative save package.

## Determinism

The release patch includes the deterministic RNG, stable collision ordering, and serial-update controls used by the arena and lockstep QA tools. These controls are opt-in for deterministic simulations and inert for normal stock play paths.

## Credits

- TeamStarDrive / BlackBox for the StarDrive BlackBox engine and community maintenance.
- deveks / Combined Arms for the Combined Arms mod ecosystem this arena is designed to complement.
- Star Gladiator arena and multiplayer package: original contribution intended as a drop-in plugin/patch layer.

## License / Distribution Note

This package is for non-commercial mod/plugin review and integration. It does not grant rights to StarDrive, BlackBox, Combined Arms, or any proprietary game assets. Users must own StarDrive on Steam and bring their own legally obtained BlackBox and Combined Arms files. Do not redistribute StarDrive/BlackBox engine source, binaries, game data, or decompiled Steam content as part of this package.

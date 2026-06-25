# Star Gladiator Arena for StarDrive BlackBox

Star Gladiator is a career-mode arena plugin for StarDrive BlackBox: build a small combat roster, pick risk/reward bouts, earn salvage and fame, fight ladder contenders, survive bosses, and watch a living rival ecosystem churn in the background.

This repository package intentionally contains only original arena plugin code plus patch files. It does not contain StarDrive, BlackBox, Combined Arms, game assets, binaries, or decompiled engine source files.

## Dependencies

You bring your own licensed copies:

- StarDrive on Steam.
- TeamStarDrive/StarDrive BlackBox `jupiter-patch-1.60.00045`.
- Combined Arms 9.0A content/mod install.
- .NET 8 SDK or newer. This package has also been deploy-tested with .NET SDK 10.0.201.

## Package Contents

- `StarDriveArena/` - the arena plugin source and `StarDriveArena.csproj`.
- `engine-patches/plugin-loader.patch` - required BlackBox engine patch for compiled plugin loading.
- `engine-patches/determinism.patch` - optional deterministic simulation patch. The arena runs without it, but ladder/ecosystem/background sims are not reproducible on stock RNG.
- `docs/PLUGIN_LOADER_UPSTREAM_PR.md` - upstream-ready plugin-loader PR spec.
- `docs/DETERMINISM_UPSTREAM_PR.md` - upstream-ready optional determinism PR spec.

## Install / Build

1. Clone your own BlackBox checkout and pin it to the supported tag.

   ```powershell
   git clone https://github.com/TeamStarDrive/StarDrive.git BlackBox
   cd BlackBox
   git checkout jupiter-patch-1.60.00045
   ```

   `--recurse-submodules` is only needed if you intend to rebuild `SDNative.dll` yourself. Normal arena plugin builds can use the native DLL from your existing BlackBox + Combined Arms install.

2. Apply the required plugin-loader patch from this package.

   ```powershell
   git apply path\to\stardrive-arena-share\engine-patches\plugin-loader.patch
   ```

3. Optional: apply deterministic simulation support.

   ```powershell
   git apply path\to\stardrive-arena-share\engine-patches\determinism.patch
   ```

4. Build BlackBox normally, then build the arena plugin against that checkout.

   ```powershell
   dotnet build StarDrive.csproj -c Release
   dotnet build path\to\stardrive-arena-share\StarDriveArena\StarDriveArena.csproj -c Release -p:BlackBoxCheckout=C:\path\to\BlackBox
   ```

   The arena project writes `StarDriveArena.dll` to `$(BlackBoxCheckout)\game\Plugins\` by default.

5. Deploy the build outputs into a real StarDrive BlackBox + Combined Arms install. Do not run the arena from a bare source checkout. Copy the patched `StarDrive.dll` from the BlackBox build and `StarDriveArena.dll` from `game\Plugins\` into the matching runtime folders of your actual install. `SDNative.dll` should come from that install unless you deliberately rebuilt the native submodule.

6. Launch the real BlackBox + Combined Arms install. The loader wires the existing data-defined `arena` main-menu button to the plugin.

## Runtime Notes

The runtime install needs the normal BlackBox + Combined Arms files, including native libraries and game content. A Windows dialog or log message saying the native library is blocked, missing, or `SDNative.dll` could not be loaded usually means the game is being launched from an incomplete source/build directory or the runtime files are missing. It is usually not a security block; deploy the patched `StarDrive.dll`, `StarDriveArena.dll`, and use the install's existing `SDNative.dll` and content files.

## Determinism Is Optional

The required plugin-loader patch is enough to run the arena. If `determinism.patch` is absent, the arena's capability probe no-ops and uses stock BlackBox RNG/ordering. The game remains functional, but headless ladder, ecosystem, and betting outcomes are not guaranteed to reproduce bit-for-bit from the same seed.

With `determinism.patch`, patched engines expose the deterministic RNG, stable collision ordering, and serial-update controls used by the arena's reproducibility tests.

## Credits

- TeamStarDrive / BlackBox for the StarDrive BlackBox engine and community maintenance.
- deveks / Combined Arms for the Combined Arms mod ecosystem this arena is designed to complement.
- Star Gladiator arena code and packaging: original contribution intended as a drop-in plugin layer.

## License / Distribution Note

This package is for non-commercial mod/plugin review and integration. It does not grant rights to StarDrive, BlackBox, Combined Arms, or any proprietary game assets. Users must own StarDrive on Steam and bring their own legally obtained BlackBox and Combined Arms files. Do not redistribute StarDrive/BlackBox engine source, binaries, game data, or decompiled Steam content as part of this arena package.

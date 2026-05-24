using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("UnitTests")]
[assembly: AssemblyCompany("Zero Sum Games")]
[assembly: AssemblyCopyright("Copyright � Zero Sum Games 2022")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyProduct("StarDrive BlackBox")]

[assembly: AssemblyTitle("StarDrive BlackBox")]


[assembly: AssemblyTrademark("")]
[assembly: CompilationRelaxations(8)]
[assembly: ComVisible(false)]
#if !DEBUG // only enable these settings for Release builds, because we need breakpoint support
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.Default | DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints)]
#endif
[assembly: Guid("b38aad3b-18b8-41a8-b758-0e5614dafc49")]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows=true)]

// CROSS-MAJOR RELEASE PROCESS — read this before bumping MAJOR.MINOR (e.g. 1.51 → 1.60).
//
// The in-game cross-major upgrade popup (AutoUpdateChecker.MajorUpgradeAvailablePopup,
// added in §5.0) is what notifies existing users on the previous major that a new
// major release exists. The popup reads `game/upgrade-url.txt` for the destination URL.
//
// REQUIRED ORDER when shipping a new major release:
//   1. On the previous major's maintenance branch (e.g. `mars-1.51`), update
//      `game/upgrade-url.txt` to point at the new release page.
//   2. Ship a patch from that branch through the normal intra-major flow
//      (e.g. tag `mars-patch-1.51.<next>`). Existing users auto-update onto
//      this patched build and inherit the new URL.
//   3. ONLY THEN tag and ship the new major (e.g. `jupiter-release-1.60`).
//
// Skipping step 2 means existing users on the previous major have no in-game
// discovery channel for the new release — the popup stays silent because the
// old `upgrade-url.txt` still points at the now-stale page (or is missing).
[assembly: AssemblyVersion("1.60.00000")]
// The "jupiter-1.60" suffix is load-bearing: ModEntry.CheckSupport does a
// substring match against this string for each comma-separated entry in a
// mod's Globals.yaml SupportedBlackBoxVersions. Mars-line convention was
// "1.51.15100 mars-1.51" — the trailing hyphenated codename token is what
// mods declare against ("mars-1.51,mars-1.50", "jupiter-1.60", ...).
// Drop the suffix and every mod silently flips to "Not supported".
[assembly: AssemblyInformationalVersion("1.60.00000 jupiter-1.60")]

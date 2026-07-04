# DESYNC JOINTECH Report

## Root Cause

Both requested hypotheses were real.

1. Host-side joiner empire setup was not equivalent to a human empire setup.
   - Lobby-created joiner empires use the `player:false` path until the authoritative human registry is applied after universe creation.
   - Buildability and tech usability used `isPlayer`, so the host's remote human joiner was filtered like an AI empire during unlock/build-cache initialization.
   - Empty lobby trait selections also meant the selected race's default `PlayerTraitOptions` were not applied, so racial/default starting tech effects could be absent from the authoritative host state and therefore absent from emitted U rows.

2. Client authoritative tech replay did not always repair derived build caches.
   - U-row replay rebuilt unlock/build caches only when raw `TechEntry.Unlocked` or `Level` changed.
   - If the client already had matching tech flags but stale `ShipsWeCanBuild` or building caches, exact-set replay left the client in the "has tech, cannot build" state.
   - Sidecar tech-state apply also restored tech rows without a final cache rebuild for touched empires.

## Fix

- `Authoritative4XLobby` now resolves empty trait selections to the race default `PlayerTraitOptions`.
- Joiner trait summaries are recorded without pre-applying trait effects on the lobby path, so normal empire initialization applies the effective trait set once.
- After `AuthoritativeHumanPlayers.SetHumanControlledEmpires()`, each configured human empire rebuilds authoritative unlock/build caches.
- Human buildability and ship-only tech usability now use `Empire.IsHumanControlled` instead of local-only `isPlayer`.
- Authoritative U-row replay rebuilds derived caches whenever an empire receives a non-empty exact unlocked-tech set, even when raw tech flags already matched.
- `Empire.RebuildUnlockCachesForAuthoritativeSync()` now refreshes per-planet building build caches as well as tech unlock and ship build caches.
- Sidecar tech-state restore rebuilds caches once per touched empire after applying tech rows.

## Regression

Added `QaJoinerTechState_StartingTechsAndSubspaceBuildabilityReplay_Headless` in `UnitTests/Multiplayer/AuthoritativeQaScenarioTests.cs`.

The repro:
- Starts an authoritative host with a passive joiner human empire using omitted lobby trait options to exercise default race traits.
- Asserts the authoritative joiner has non-root starting techs, including the Militaristic/default starting tech path.
- Replicates to the passive client and asserts unlocked-tech exact-set equality for the joiner empire.
- Unlocks the stock `Subspace Projector` tech requirements on the authoritative joiner.
- Forces the passive client into the stale-cache shape: matching raw tech flags but the projector removed from `ShipsWeCanBuild`.
- Replays authoritative rows and asserts the passive client now has exact tech equality and can build the Subspace Projector via both `CanBuildShip` and `WeCanBuildThis`.

## Verification

Passed:

```powershell
dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter QaJoinerTechState_StartingTechsAndSubspaceBuildabilityReplay_Headless
dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter QaTechGatedDesign_HammerheadCostAndModuleAvailabilityReplayFromHostRows_Headless
dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter Authoritative4XSnapshot_AppliesUnlockedTechRowsBeforeDigestCompare_Headless
dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter Authoritative4XSnapshot_RemovesStaleClientOnlyTechRowsBeforeDigestCompare_Headless
dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false
```

The requested build completed with 0 warnings and 0 errors.

Note: a parallel attempt to run multiple StarDrive test processes concurrently hit shared `TestResults/AppData/StarDrive/*.config` and `blackbox.log` file locks. The same relevant tests passed when rerun sequentially.

## 2026-07-04 Regression Follow-up

### Exact Research Queue Regression

Cause:
- `RebuildUnlockCachesForAuthoritativeSync()` reused the normal empire initialization path (`InitEmpireUnlocks()`).
- That path reapplies `UnlockedAtGameStart` rules to every discovered tech.
- In `Authoritative4XClientReplica_ReplaysResearchQueueExactWithoutPrereqExpansion_Headless`, the passive fixture intentionally has discovered but unresearched prerequisite techs; cache rebuild turned `Corvettes` back into an unlocked U row and shifted the payload before digest comparison.

Fix:
- Authoritative cache rebuild is now cache-only: it applies static data unlocks, replays content for the current exact unlocked tech set, rebuilds ship/building caches, and restores the exact research queue and tech progress/level flags.
- It no longer grants start techs or routes through research queue prerequisite expansion.
- `SetQueueExact` remains the passive replay path for E-row research queues.

### Player Design Cache Regression Found By Soak

Cause:
- Once player designs were allowed for human-controlled empires, `UpdateShipsWeCanBuild()` could re-add machine-local/global `IsPlayerDesign` templates during authoritative cache rebuild.
- `D` player-design replay was additive, so a client-only buildable player design could survive when the authority emitted no matching D row. `Soak_Smoke` exposed this as a tick-1 extra `D|...P4D2_210055...` row on the passive client.

Fix:
- Authoritative cache rebuild skips global player-design scanning and preserves only player designs that were already buildable before the rebuild.
- `D` replay is now exact per empire: client-only buildable player designs absent from authority D rows are removed before applying the authority D payload.
- Added `Authoritative4XClientReplica_RemovesClientOnlyPlayerDesignRows_Headless` and extended the machine-local scrub proof so cache rebuild cannot resurrect scrubbed designs.

### Ground Combat QA Regression

Cause:
- The jointech trait fix correctly applies default player trait options. For the QA seed, the host is Cordrazine with `GroundCombatModifier=-0.3`.
- The seeded invader dropped to strength 7; with the deterministic planet RNG, all seven 25% hard-attack rolls missed. The scenario stayed byte-synced but no longer exercised real damage/active combat, so the proof failed its guard.

Fix:
- The ground-combat QA fixture now sets seeded troop strength explicitly through the replicated GT strength field, making the proof independent of racial ground-combat penalties while still exercising real ground-combat replay.

### Final Verification

Passed on the final tree:

```powershell
dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-restore --filter FullyQualifiedName~Authoritative4X
# Passed 188/188

dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-restore --filter FullyQualifiedName~QaScenario
# Passed 10/10

dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-restore --filter FullyQualifiedName~Arena
# Passed 106/106

dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-restore --filter FullyQualifiedName~Soak_Smoke
# Passed 1/1

dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-restore --filter FullyQualifiedName~QaJoinerTechState_StartingTechsAndSubspaceBuildabilityReplay_Headless
# Passed 1/1
```

Notes:
- The first attempt to run two `dotnet test` commands in parallel collided during NuGet restore with `Cannot create a file when that file already exists`; all verification above was rerun serially.
- The NuGet vulnerability feed warnings are from restricted network access and did not affect the compiled/tested outputs.

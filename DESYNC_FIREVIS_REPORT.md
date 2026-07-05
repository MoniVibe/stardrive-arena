# DESYNC FIREVIS REPORT

## Event Format

Added transient `WF` rows:

`WF|Tick|Sequence|ShooterShipId|WeaponUid|ModuleGridX|ModuleGridY|TargetShipId|Source.X|Source.Y|Direction.X|Direction.Y|IsBeam|Destination.X|Destination.Y`

Float fields are encoded as raw IEEE-754 bit integers, matching existing authoritative snapshot float rows. `Sequence` disambiguates same-tick/same-muzzle salvo rows. Host events are buffered on `UniverseState.AuthoritativeWeaponFire` only while `Authoritative4XAuthority.Advance` is inside the authoritative simulation tick.

Weapon hooks:
- `Weapon.FireDrone`
- `Weapon.SpawnSalvo`
- `Weapon.FireBeam`

The hook runs after the real gameplay projectile/beam is spawned on the host, so rejected/non-firing attempts do not emit `WF`.

## Digest Policy

`WF.WeaponFire` is registered in `AuthoritativeReplicationManifest` as `DigestPolicy.Transform`, not `Fatal`.

This keeps transient fire events out of `SyncDigest`. On apply, the passive client stores the exact raw `WF` line and emits it once during its immediate recapture, so `TransformDigest` matches the authority snapshot that carried the event. Both host pending rows and client echo rows are drained after capture.

## Client Visual Isolation

The passive client spawns `RenderOnlyWeaponFireVisual` instances in `UniverseObjectManager.WeaponFireVisuals`.

These visuals are not `Projectile`, not `Beam`, not `GameObject`, not inserted into `Objects`, `Projectiles`, spatial, save data, `SnapshotShips`, or `UniverseStateHash`. They only update/draw from a private render-only list. Projectile visuals use the replicated muzzle/direction plus the real weapon texture/speed where available; beams draw a short-lived projected line between shooter muzzle and target/destination.

No client-side damage path exists in this visual class.

## Overflow Cap

Host `WF` buffering caps at `384` events per snapshot. Excess events are dropped and counted (`LastOverflowDropCount`, `TotalOverflowDropCount`). This bounds large-battle packet growth; dropped rows only lose cosmetics.

## Protocol

`ArenaMultiplayerSettings.ProtocolVersion` is bumped from `2` to `3`.

## Regression

Added `Authoritative4XSnapshot_WeaponFireRowsSpawnRenderOnlyVisualsAndStayDigestNeutral_Headless`:
- fires through the real `Weapon.cs` path on the host
- asserts `WF` rows are emitted
- applies to passive client
- asserts no gameplay projectile/beam is created client-side
- asserts render-only visual count increases
- asserts `SyncDigest` and `TransformDigest` match with visuals active
- asserts host/client `WF` rows drain and do not replay
- asserts reapplying the same snapshot does not duplicate stale visuals

## Verification

Passed:
- `dotnet build StarDrive.csproj --no-restore`
- `dotnet build UnitTests\SDUnitTests.csproj --no-restore`
- `dotnet test UnitTests\SDUnitTests.csproj --no-build --filter "FullyQualifiedName~Authoritative4XSnapshot_WeaponFireRowsSpawnRenderOnlyVisualsAndStayDigestNeutral_Headless|FullyQualifiedName~ReplicationManifest_ExecutableDescriptors_EnforceCoverageSymmetry_Headless"`
- `dotnet test UnitTests\SDUnitTests.csproj --no-build --filter "FullyQualifiedName~ReplicationCoverageDiagnosticTests"`
- `dotnet test UnitTests\SDUnitTests.csproj --no-build --filter "FullyQualifiedName~Authoritative4XSnapshot"`: 17/17
- `dotnet test UnitTests\SDUnitTests.csproj --no-build --filter "FullyQualifiedName~Authoritative4XSessionTests"`: 189/189
- `dotnet test UnitTests\SDUnitTests.csproj --no-build --filter "Name=Soak_Smoke"`: 1/1
- `dotnet test UnitTests\SDUnitTests.csproj --no-build --filter "FullyQualifiedName~AuthoritativeQaScenarioTests"`: 12/12
- `dotnet test UnitTests\SDUnitTests.csproj --no-build --filter "FullyQualifiedName~Arena"`: 106/106

After the final render-order-only adjustment, `dotnet build UnitTests\SDUnitTests.csproj --no-restore` passed again. A final targeted rerun was blocked before test execution by a runner config-path permission error:

`Access to the path 'C:\dev\stardrive\StarDrive-firevis\TestResults\AppData\StarDrive\*.tmp' is denied`

Restore/build produced only `NU1900` warnings because vulnerability lookup could not reach `https://api.nuget.org/v3/index.json` under sandbox networking.

Shared-core verifier note: `scripts/invoke_shared_core_review.ps1` was not present in this worktree, so the shared-core skill verifier could not be run.

# Passive Ship Interpolation Report

## Design

Passive authoritative clients now keep render-only previous/current SX transform samples on `Ship`.
The samples store position, 2D rotation, and SX tick in non-`[StarData]` fields in
`Ship.Authoritative.cs`.

`ApplyShipTransformLine` still applies the host-authored `Ship.Position`, `Velocity`,
`Rotation`, `YRotation`, and `XRotation` through `SetAuthoritativeTransform`. Before that replay,
it observes the SX position/rotation/tick for passive presentation. The observation path only
updates render-only fields and the existing visual-bank render fields.

`SyncSceneObjectForPassiveAuthoritativeView` advances a real-time interpolation clock and renders
from previous SX to current SX over `(currentTick - previousTick) / 60`. The interpolation amount is
clamped to `[0, 1]`, so it never extrapolates past the latest authoritative transform. Large
position gaps above 25,000 world units or tick gaps above 60 ticks snap directly to the latest SX
sample, covering resync/teleport/FTL-style jumps without sliding across the map.

2D rotation uses shortest-arc angle interpolation. The existing passive visual-bank layer remains
stacked on top of the interpolated render rotation/Y-bank and still derives its target from the
authoritative SX rotation delta. Tactical icon projection now reads the cached render position and
render rotation/bank, while picking/spatial/gameplay state remains at `Ship.Position`.

## Files

- `Ship_Game/Ships/Ship.Authoritative.cs`
  - Added render-only previous/current/interpolated SX state.
  - Added clamped snapshot interpolation and shortest-arc rotation lerp.
  - Scene object matrix now uses render-only position/rotation plus the existing visual bank.
- `Ship_Game/Multiplayer/Authoritative/Authoritative4XSession.cs`
  - `ApplyShipTransformLine` passes parsed SX position into `ObservePassiveAuthoritativeTransform`.
- `Ship_Game/Universe/UniverseScreen/UniverseObjectManager.Authoritative.cs`
  - Passive view refresh passes frame elapsed seconds into ship render sync.
- `Ship_Game/Ships/Ship_Rendering.cs`
  - Tactical icon projection uses the render-only passive position when present.
- `UnitTests/Multiplayer/Authoritative4XSessionTests.cs`
  - Updated the existing passive visual-bank matrix assertion for interpolated render transforms.
  - Added mid-interval interpolation and snap-on-large-jump coverage.
  - Added digest-neutrality coverage through `Authoritative4XClientReplica`.
  - Updated `Authoritative4XPassiveClientViewRefresh_ReindexesHostTransforms_Headless` to assert
    authoritative gameplay/spatial position separately from render-only scene-object interpolation.

## Digest Neutrality

The interpolation path does not write replicated/[StarData] ship fields. It writes only the new
passive render fields and `ShipSO.World` during presentation refresh.

The new regression `Authoritative4XPassiveSnapshotInterpolation_IsRenderOnlyAndDigestNeutral_Headless`
drives two host snapshots through a passive replica, refreshes interpolation at mid-interval, then
asserts:

- passive `Ship.Position` equals the latest host SX position,
- passive `Ship.Rotation` equals the latest host SX rotation,
- `SyncDigest` equals the authority snapshot digest,
- `TransformDigest` equals the authority snapshot transform digest.

## Reindex Test Disposition

`Authoritative4XPassiveClientViewRefresh_ReindexesHostTransforms_Headless` was a contract update,
not a gameplay leak. The reindex path still updates spatial from `SpatialObjectBase.Position`;
`AABoundingBox2D`, `AABoundingBox2Di`, `SpatialObj`, and `NativeSpatialObject` all read the
authoritative position, not the passive render interpolation fields.

The test now asserts the split explicitly:

- `Ship.Position` and `Ship.Rotation` remain bit-exact to the latest host SX transform.
- `Spatial.Update`/`FindNearby` locates the ship at that authoritative gameplay position.
- `ShipSO.World` is built from the render-only interpolated position/rotation and visual-bank
  render Y rotation, so it may sit between the previous and current SX samples mid-interval.

## Verification

Static checks completed:

- `rg` confirmed the only `ObservePassiveAuthoritativeTransform` call site was updated.
- `rg` confirmed existing `SyncSceneObjectForPassiveAuthoritativeView` call sites remain valid with
  the optional elapsed-time parameter.

Targeted test execution completed with sandbox-safe paths:

- Direct unredirected `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64`
  attempts hit sandbox ACLs on existing `obj`/`bin` files and on the default test AppData path.
- The verification rerun used `STARDRIVE_TEST_APPDATA=.codex-msbuild\AppData`, a temporary guarded
  MSBuild props hook for redirected intermediates, and redirected `OutDir`/`OutputPath` to
  `UnitTests\.codex-bin\Debug\` so the test harness still resolved `../../../game`. The temporary
  props hook was removed after verification.
- `FullyQualifiedName~Authoritative4XPassiveClientViewRefresh`: passed, 1/1.
- `FullyQualifiedName~Authoritative4XPassiveVisualBank|FullyQualifiedName~Authoritative4XPassiveSnapshotInterpolation`:
  passed, 3/3.
- `FullyQualifiedName~Authoritative4X`: attempted with the same sandbox-safe paths; build completed
  and test execution started, but the run exceeded the 10-minute local timeout before producing a
  TRX summary.

No commit or push was performed.

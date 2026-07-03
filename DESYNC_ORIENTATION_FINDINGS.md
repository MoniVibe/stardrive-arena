# Passive authoritative orientation findings

Branch: `arena-045-port`

Scope: investigate why a passive authoritative 4X joiner shows smooth ship orbit motion but frozen 3D bank/pitch, while the host visibly banks the same ships.

## Summary

I do not find a passive-client path that locally advances mobile orbiting ship position. The passive joiner is still a transform replica for mobile ships:

- `UniverseScreen.UpdateGame.cs:116-122` exits the passive sim before host-only simulation.
- `UniverseObjectManager.UpdatePassiveAuthoritativeView()` calls `UpdateAllShips(FixedSimTime.Zero)` and does not call `UpdateAllShipAI`.
- `Ship.UpdateAlive()` skips `UpdateEnginesAndVelocity()` when `timeStep.FixedTime == 0`, so it does not call `UpdateShipRotation()` or `UpdateVelocityAndPosition()` on passive view refresh.
- `SyncSceneObjectForPassiveAuthoritativeView()` only rebuilds the scene-object matrix from current `Position`, `YRotation`, `XRotation`, and `Rotation`.

The smooth orbit report is therefore most likely host-transform cadence, not passive orbit simulation. Live host polling submits heartbeat `NoOp`s and broadcasts snapshots every fourth heartbeat (`Authoritative4XLiveSession.cs:22-23, 563-570`). Each authoritative command advances one `SingleSimulationStep()` before capture (`Authoritative4XSession.cs:2521-2527`). The client applies those `SX` rows and refreshes passive scene objects every UI update (`UniverseScreen.AuthoritativeMultiplayer.cs:335-356`). If the host heartbeat cadence is high enough, `SX` position replay can look smooth even though the passive client never runs orbit AI locally.

## Host banking path

Normal-flight bank is computed in `Ship_Movement.cs`:

- `ShipAI.RotateTowardsPosition()` / `ThrustOrWarpToPos()` call `Owner.RotateToFacing(...)`.
- `Ship.RotateToFacing()` stores signed turn intent in `RotationThisFrame`.
- On the next `Ship.Update()`, `UpdateEnginesAndVelocity()` calls `UpdateShipRotation()`.
- `UpdateShipRotation()` consumes `RotationThisFrame`, clamps actual 2D `Rotation`, and increments/decrements `YRotation` toward `MaxBank`.

Important constraints:

- `UpdateShipRotation()` only runs when `timeStep.FixedTime > 0`, so passive zero-time ship updates cannot compute local bank.
- It only banks when `IsVisibleToPlayer && rotAmount != 0f && !AI.IsInOrbit`.
- `XRotation` is not part of normal movement banking. It is assigned by launch/death paths, plus authoritative replay.

For planet orbit specifically, `OrbitPlan.Orbit(Planet, timeStep)` sets `InOrbit = true` when inside the orbit band, then calls `AI.RotateTowardsPosition(OrbitPos, ...)`. Because `Ship.Update()` consumes the previous frame's `AI.IsInOrbit`, stable planet orbit tends to suppress `YRotation` bank and ease it back toward zero. Combat orbit tactics are different: `CombatTactics.OrbitTarget` and `BroadSides` use `UpdateOrbitPos()` plus `ThrustOrWarpToPos()` / `RotateTowardsPosition()` without necessarily setting the main `ShipAI.Orbit.InOrbit` flag, so they can produce visible host bank while orbiting a target.

## Passive/client asymmetry

The exact asymmetry is:

1. Host runs full ship update and ship AI. AI produces turn intent; the next ship update turns that into `YRotation`.
2. Host captures `SX` rows with `Position`, `Velocity`, `Rotation`, `YRotation`, and `XRotation`.
3. Passive client applies `SX` rows via `ApplyShipTransformLine()` -> `Ship.SetAuthoritativeTransform(...)`.
4. Passive view refresh rebuilds scene objects from those fields, but does not locally produce new turn intent or bank between snapshots.

So passive-side orientation can only change when an `SX` snapshot changes `YRotation/XRotation`. Position can look smooth from frequent host-authored `SX` updates; bank can look frozen if the authoritative `YRotation/XRotation` values in those packets are zero/stale, or if the visible host "bank" is being perceived from host-only per-frame presentation that is not represented in the replicated fields.

I did not find evidence that the 2026-07-02 "MAGIC STOP" change causes passive mobile ships to advance orbit position locally. The relevant code is in `OrbitPlan.Orbit`: it treats authoritative 4X multiplayer as visible for the off-screen orbit optimization, but that branch only matters when `OrbitPlan.Orbit()` is executed. The passive gate prevents the normal passive loop from reaching `UpdateAllShipAI()`.

## Digest and guard risk

`YRotation` and `XRotation` are part of the `SX.ShipTransform` transform digest. They are not in the durable `SyncDigest`.

Relevant behavior:

- `Authoritative4XClientReplica.AssertReplicaUnchangedSinceLastSnapshot()` checks only `SyncDigest` before applying the next host snapshot.
- After replay, `ApplyAuthoritativeResult()` checks `TransformDigest` against the authority snapshot.
- Directly mutating `Ship.YRotation/XRotation` between snapshots would not currently trip the pre-apply durable mutation check, but it would still mutate replicated transform state outside replay. It would also bypass `AuthoritativeMutationGuard` because those fields are public raw fields.

For that reason, I do not recommend fixing this by locally editing `Ship.YRotation/XRotation` on the passive client. It is easy to make today's digest checks pass while still blurring the contract between host-authored transform state and client-only presentation.

## Recommended minimal fix

Add a passive-authoritative visual-only bank layer that never feeds StarData, `SX`, `SyncDigest`, or `TransformDigest`.

Minimal shape:

1. Add non-`[StarData]` fields to `Ship` for passive visual orientation, for example:
   - last authoritative transform tick/rotation observed on this client
   - current/target passive visual bank angle
   - optionally current/target passive visual pitch angle if pitch is desired
2. In `ApplyShipTransformLine()`, parse the `SX` tick and compute a signed `Rotation` delta from the previous applied `SX` rotation for that ship. Convert delta-per-tick into a clamped visual bank target. Do not write `YRotation/XRotation`.
3. In `SyncSceneObjectForPassiveAuthoritativeView()`, use:
   - host-authored `YRotation/XRotation` when those values are non-zero/non-stale, plus
   - passive visual-only bank as a fallback or additive presentation-only value.
4. Reset/decay the passive visual-only bank when deltas stop or the ship becomes inactive/dying.

This keeps the authoritative transform tuple untouched while allowing passive rendering to show banking between snapshots from host-authored `Rotation` deltas. It also avoids running ship AI or gameplay movement on the passive client.

## Implementation update

Implemented on branch `fix/bank` as a passive-authoritative render-only layer:

- `Ship` now keeps private, non-`[StarData]` passive visual fields for last observed SX `Rotation`/tick plus current/target visual bank. These fields are not emitted by `SX`, durable payload rows, `SyncDigest`, `TransformDigest`, or the authoritative raw hash.
- `ApplyShipTransformLine()` observes the SX tick and signed `Rotation` delta before authoritative transform replay. It converts delta-per-elapsed-tick into a clamped bank target with host-compatible sign, but still only writes `YRotation`/`XRotation` through the existing host-authored `SetAuthoritativeTransform(...)` path.
- `SyncSceneObjectForPassiveAuthoritativeView()` composes the scene-object matrix with `YRotation + passiveVisualBank`, clamped to `MaxBank`, while leaving replicated `YRotation`/`XRotation` untouched.
- `DrawTacticalIcon()`/`DrawTactical()` feeds the same passive visual bank into the tactical icon presentation via a small render-only icon lean.
- The bank eases toward target during passive render refresh and targets zero when SX rotation deltas stop or the ship becomes inactive/dying, so the visual tilt decays instead of sticking.

Digest-unaffected proof added:

- `Authoritative4XPassiveVisualBank_IsRenderOnlyAndDigestNeutral_Headless` forces a host SX heading delta while host `YRotation`/`XRotation` stay zero, applies it through the passive client replica, refreshes the passive scene object, verifies the mesh/icon render helpers see a nonzero visual bank, and then captures the client snapshot to assert both `SyncDigest` and `TransformDigest` still match the host. It also verifies decay after a stopped heading delta.

Verification in this sandbox:

- `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false` passed.
- Focused `dotnet test` could not complete in this sandbox because normal runtime/deps generation is denied for `game\*.deps.json` / `game\*.runtimeconfig.json`; a no-build testhost launch also lacks generated runtime dependencies. Orchestrator/full environment should run Authoritative4X, Arena, and Soak_Smoke suites.

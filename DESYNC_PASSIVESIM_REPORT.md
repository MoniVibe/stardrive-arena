# Passive authoritative client desync report

Branch: `fix/passivesim`
Worktree: `C:\dev\stardrive\StarDrive-passivesim`

## Mechanism found

The passive joiner correctly returns before `SingleSimulationStep` in
`UniverseScreen.UpdateGame.ProcessSimulationTurns`, but the passive branch still
called `UState.Objects.UpdatePassiveAuthoritativeView()`.

Before this fix, `UpdatePassiveAuthoritativeView()` was not presentation-only. It
called:

- `UpdateAllSystems(FixedSimTime.Zero)`
- `UpdateAllShips(FixedSimTime.Zero)`
- `UpdateAllProjectiles(FixedSimTime.Zero)`
- spatial/visibility refresh

The concrete GC mutation path was:

`ProcessSimulationTurns` passive branch -> `UniverseObjectManager.UpdatePassiveAuthoritativeView`
-> `UpdateAllSystems(Zero)` -> `SolarSystem.Update(Zero)` -> `Planet.Update(Zero)`
-> `UpdateHabitable(Zero)` -> `TroopManager.Update(Zero)`.

`TroopManager.Update(Zero)` is not time-step pure. With `Init`/combat timers it
still runs `MakeCombatDecisions`, `DoBuildingTimers`, and `DoTroopTimers`.
Those paths call `CombatScreen.StartCombat(...)` and `Combat.ResolveDamage(...)`,
which create GC rows, consume troop/building actions, and damage building/troop
HP. The P3 passive gate existed but was compiled under `#if DEBUG`, so Release
QA passive clients still ran the ground combat branch.

The economy/construction/freighter/fleet rows are turn/empire lifecycle state,
not presentation state. The host-side mutation paths are:

- `Empire.Update` -> `AI.Update` / `GovernPlanets` / `DoMoney` / `TakeTurn`.
- `Empire.UpdateEmpirePlanets` -> `Planet.UpdateOwnedPlanet` -> `ApplyResources`
  -> `Construction.AutoApplyProduction` -> `SBProduction.ProcessCompleteQueueItem`.
- `SBProduction.OnShipComplete` -> `Ship.CreateShipAtShipyard` or
  `Ship.CreateShipNearPlanet`, including freighter initialization.
- `Empire.DispatchBuildAndScrapFreighters` -> `IncreaseFreighters` goal ->
  `planet.Construction.Enqueue(... QueueItemType.Freighter ...)`.
- fleet lifecycle through `Empire.CreateFleet/SetFleet/RemoveFleet` and
  `Fleet.AddShip/RemoveShip/Reset`.

Those must remain host-only. The passive client may only take the resulting
`E/Q/SC/S/SX/F/GC/GT` state from authoritative snapshots.

## Fixes applied

`UniverseObjectManager.UpdatePassiveAuthoritativeView()` is now presentation-only:

- refreshes object lists without removing inactive objects
- updates system/planet scene visibility through passive-only presentation methods
- updates spatial and visible-object lists
- syncs visible ship scene objects from host-authored transforms
- no `UpdateAllSystems`, no `UpdateAllShips`, no projectile update, no ship AI,
  no planet/troop/economy/fleet/freighter simulation

Added passive presentation methods:

- `SolarSystem.UpdatePassiveAuthoritativeView`
- `Planet.UpdatePassiveAuthoritativeView`
- `SolarSystemBody.UpdatePresentationVisibilityOnly`

The Release build now honors the existing passive client gate in
`TroopManager.Update`; the `#if DEBUG` wrapper was removed.

Snapshot replay was tightened for host-authored dynamic state:

- `GC` replay is exact per planet: stale client-only combats are cleared, host
  combats are rebuilt in snapshot order, and building HP encoded in
  `GroundBuildingRef` is applied to the local building.
- `F` replay runs before `S` runtime rows, creates missing host fleets, removes
  stale client-only fleets, and lets subsequent ship runtime rows attach all
  members.
- Host-authored freighters and other ships continue to materialize through
  `SC/S` replay; focused coverage was added for the freighter case.

## Guard coverage added

`AuthoritativeMutationGuard` now covers the newly implicated families:

- `ShipPresence`
- `GroundCombat`
- `EmpireRuntime`
- `ConstructionQueue`
- `FleetRuntime`

New choke points:

- GC: `CombatScreen.StartCombat`, `Combat.ResolveDamage`
- empire runtime: `Empire.AddMoney`, `Empire.DoMoney`, production credit
  charge/refund, absorption money transfer
- construction queue: queue membership, production spent, rush, finish/cancel,
  reorder/move/swap, rush flags, clear/replace authoritative sync
- fleet runtime: `Empire.CreateFleet/SetFleet/RemoveFleet`,
  `Fleet.AddShip/AddExistingShip/RemoveShip/Reset`
- ship presence: `UniverseState.AddShip`, `Ship.QueueTotalRemoval`

Replay and accepted-command scopes remain sanctioned so host snapshots and
accepted authoritative commands can apply these mutations without false positives.

## Regression coverage added

Added focused tests in `UnitTests/Multiplayer/Authoritative4XSessionTests.cs`:

- `Authoritative4XPassiveView_DoesNotTickGroundCombat_Headless`
- `Authoritative4XGroundCombatReplay_RepairsBuildingHpAndClearsStaleCombats_Headless`
- `Authoritative4XClientReplica_MaterializesHostAuthoredFreighterBeforeDigest_Headless`
- `Authoritative4XPassiveReplica_NoLocalSimAcrossGcEconomyQueueFleetFreighterSoak_Headless`
- `Authoritative4XSnapshot_MaterializesMissingFleetRuntimeRows_Headless`

Existing mutation-guard tests were extended to assert passive trips and sanctioned
accepted-command scope for GC, empire runtime, construction queue, fleet runtime,
and ship presence.

## Verification

Passed compile-only lane verification:

`dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false`

Result: build succeeded, 0 errors. The only warnings were `NU1900` vulnerability
metadata warnings because the sandbox cannot reach `https://api.nuget.org/v3/index.json`.

Focused `dotnet test` was attempted. It was blocked by the sandbox/testhost
runtime path:

- `--no-build` test run failed because the restricted build intentionally did not
  generate `testhost.runtimeconfig.json`.
- normal `dotnet test` failed writing `game\SDUtils.deps.json` under the sandbox
  copy with `UnauthorizedAccessException`.

No git commit or push was made.

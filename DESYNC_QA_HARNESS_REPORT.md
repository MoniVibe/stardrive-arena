# Desync QA Harness Report

Worktree: `C:\dev\stardrive\StarDrive-qaharness`  
Branch: `fix/qaharness`

## Implemented Scenarios

1. Ground combat: `QaGroundCombat_MultiPhaseInvasionStaysSynced_Headless`
   - Reproduced: yes. A host ground-combat snapshot could contain building HP changes that the passive client did not keep byte-identical once an unreplayable off-tile combat row was cleaned up.
   - Fix: `T|` colony tile rows now carry `BuildingStrength` and `BuildingCombatStrength`; ground building refs also replay `CombatStrength`. Off-planet troop combats are treated as done and unreplayable `GC` rows are not emitted.
   - Result: green in the new QA scenario suite.

2. Fleet combat: `QaFleetCombat_WeaponsDamageDeathsStaySynced_Headless`
   - Reproduced: no digest divergence in the seeded headless fight.
   - Coverage: host/player fleet vs enemy fleet with attack orders, projectiles/combat/damage observed, `S/SX/E` digest checked through 420 ticks.
   - Result: green.

3. Fleet ops: `QaFleetOps_OpenFleetMutationReproStaysSynced_Headless`
   - Reproduced: no digest divergence in the focused open/edit fleet flow.
   - Coverage: create/replace fleet, rename, auto-arrange, layout with live ship ids, patrol creation, add ship, clear fleet; asserts `F` rows and no passive invent/drop.
   - Result: green.

4. Economy soak: `QaEconomySoak_TaxesProductionUpkeepTradeMoneyStaysSynced_Headless`
   - Reproduced: no money drift in the focused soak.
   - Coverage: taxes/auto-taxes, production queue, ship queue, many 1-second turns, exact money/tax/treasury float-bit checks.
   - Result: green.

5. Tech-gated design / Hammerhead repro: `QaTechGatedDesign_HammerheadCostAndModuleAvailabilityReplayFromHostRows_Headless`
   - Reproduced: yes. An accepted host design can rely on a host-only tech unlock; passive local command validation can reject/recompute from incomplete tech state.
   - Fix: `DesignShip` is now snapshot-only on passive replay. The passive client takes `U|` and `D|` rows from the host before semantic cost/buildability assertions.
   - Result: green; host/client unlocked tech, gated module availability, buildability, computed cost, and queued cost all match.

6. Pirate event + resync: `QaPirateEventResync_DirectorPaymentDoesNotDuplicateAfterReload_Headless`
   - Reproduced: yes as an idempotency repro for re-fired payment-director setup after save/resync.
   - Fix: pirate payment/raid director addition is idempotent by target empire; `Pirates.Init` no longer duplicate-adds payment/threat map entries after reload.
   - Result: green.

## Deferred Scenarios

7. Subspace projector: not implemented in this pass.
8. Fog/exploration expected-behavior proof: not implemented in this pass.
9. Full-game combined soak: not implemented in this pass.

## Authoritative4X Red-Test Disposition

- `Authoritative4XClientContext_SubmitsFleetMoveWithoutLocalMutation_Headless`,
  `...FleetRename...`, `...FleetAutoArrange...`, `...FleetLayout...`,
  `...FleetDesignLoad...`, `...FleetPatrolLoad...`: **GUARD MISFIRE**.
  The failing `FleetRuntime/CreateFleet` trips were negative-case fixture/setup fleets
  constructed while the passive client context was intentionally active. Those setup
  mutations now run under `Authoritative4XClientContext.EnterStateApplication()`.
  The `FleetRuntime` guard remains intact and unsanctioned passive mutation coverage
  still asserts that `CreateFleet` throws.
- `Authoritative4XPassiveView_DoesNotTickGroundCombat_Headless`: **FIXTURE BUG**.
  Trace showed no `GroundCombat` guard/replay trip. The digest delta was a pending
  `SC|...|Vulcan Scout` row flushed by `UpdatePassiveAuthoritativeView`; the fixture now
  applies pending object lists before the baseline snapshot so the test measures only
  passive ground-combat no-tick behavior.
- `Authoritative4XLobby_FourHumansFourAiClientInteractionGauntletStaysSynced_Headless`:
  **REAL LEAK in the fatal sync surface**. The authority emitted an unreplayable
  key-0/id-0 empty `F.Runtime` reset remnant (`Second Fleet`) that clients need not
  possess. `F.Runtime` emission now includes only replayable fleets: keyed fleets or
  keyless fleets resolvable by command ship. Keyed fleet replay/materialization coverage
  remains active.

## Verification

- `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false`: passed. Later builds also passed; restore emitted `NU1900` warnings because the sandbox cannot reach NuGet vulnerability data.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~AuthoritativeQaScenarioTests"`: passed, 6/6.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ArenaMultiplayerLockstepTests"`: passed, 13/13.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName=UnitTests.Multiplayer.AuthoritativeSoakTests.Soak_Smoke"`: passed, 1/1.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter FullyQualifiedName~Authoritative4X`: passed, 180/180.

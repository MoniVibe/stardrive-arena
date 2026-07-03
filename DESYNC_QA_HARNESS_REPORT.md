# Desync QA Harness Report

Worktree: `C:\dev\stardrive\StarDrive-qa2`
Branch: `fix/qa2`

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

7. Subspace projector: `QaSubspaceProjector_GameplayStateReplicatesBubbleIsRenderOnly_Headless`
   - Reproduced: yes. `SC` rows materialized the passive-client projector ship, but replay did not rehydrate the owning empire's `OwnedProjectors` list, and the initial influence entry was inserted at `Vector2.Zero` before the `SX` row moved the ship to the host position.
   - Fix: missing-ship replay now adds materialized ships to the owner ship/projector list and publishes it; projector `SX` replay removes/reinserts influence when authoritative position or active state changes.
   - Classification: the original joiner issue was a gameplay replay gap. After the fix, the underlying projector ship, owner list, projection radius, and influence tree state replicate. The remaining bubble draw is render-only/overlay-gated (`ShowingFTLOverlay` and border-node rendering), not a digest desync.
   - Result: green.

8. Fog/exploration expected-behavior proof: `QaFogExploration_UnknownEnemyPlanetsStayHiddenUntilContactWar_Headless`
   - Reproduced: by-design hidden state plus one replay/UI gap. With no contact, enemy planet `P` rows exist but owner visibility stays hidden because the relationship row is `R|...|0|0|...`; this matches the live report that planets stay hidden until contact/war. When the host relationship becomes known/at-war, replay previously updated `Relationship.Known` but not the empire `KnownEmpires` bitset used by UI visibility.
   - Fix: authoritative diplomacy replay now updates `KnownEmpires` through a guarded helper when applying `R` rows.
   - Result: green; no-contact hidden is expected, contact/war visible is asserted.

9. Full-game combined soak: `QaFullGameCombinedSoak_AllFamiliesStaySyncedThroughForcedResync_Headless`
   - Reproduced: no new divergence after the projector and fog replay fixes.
   - Coverage: seeded in-process host plus passive client, host-side colonization via `P` rows, building queue, tech-gated player design via `U/D` rows, ship queue, fleet formation/move, trade/freighter automation, fleet combat, ground invasion, pirate payment event, and forced authoritative replay/resync.
   - Result: green end-to-end across fatal sync and transform digests.

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

- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~AuthoritativeQaScenarioTests"`: passed, 9/9.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Authoritative4X"`: passed, 180/180.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Arena"`: passed, 106/106.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName=UnitTests.Multiplayer.AuthoritativeSoakTests.Soak_Smoke"`: passed, 1/1.
- Restore emitted `NU1900` warnings because the sandbox cannot reach NuGet vulnerability data; all requested test gates still passed.

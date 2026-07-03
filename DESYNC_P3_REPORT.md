# DESYNC P3 Report

## Summary

Implemented the passive-client replicated-state mutation tripwire for Phase P3.

- Added `AuthoritativeMutationGuard` with debug-only passive-client checks and sanctioned replay/accepted-command scopes.
- Routed replay and accepted-command mutation funnels through guarded setters for planet runtime controls, troop runtime membership/state, ship transform/orientation, diplomacy/first-contact state, and empire automation flags.
- Made `Authoritative4XClientReplica` local-mutation digest checking default-on for the headless/in-process test substrate.
- Added planted-leak regressions for all five requested families and sanctioned-path coverage for replay apply plus accepted-command scope.

The guard compiles out of Release through `[Conditional("DEBUG")]` guard calls and `#if DEBUG` scope usage. `Release` build is green.

## Follow-Up Disposition: 10 Guard-Active Failures

P2 made `firstDiff` truthful by diffing the projected fatal payload. With the P3 local-mutation sentinel active, most follow-up failures fell into guard-scope misfires in same-process live loopback tests and test fixtures that were mutating replicas after an accepted baseline snapshot. The final round found one genuine passive-client simulation leak in troop combat-timer refresh during passive live-session view updates.

| Test | Bucket | Evidence | Fix applied |
| --- | --- | --- | --- |
| `Authoritative4XColonyBuildQueue_QueuesBuildingsAndSyncsPlacement_Headless` | TEST/FIXTURE BUG | The test directly wrote `clientItem.ProductionSpent` after an accepted queue snapshot, so the local-mutation pre-apply check would truthfully report a `Q|` production-spent firstDiff before replay. | Removed the planted passive-client queue drift from this integrated command test; the queue command still proves deterministic placement and synchronized queue runtime. |
| `Authoritative4XFleetMove_UsesFleetMoveToAndSyncs_Headless` | TEST/FIXTURE BUG | The fixture called `clientFleet.CreatePatrol(...)` after `SetFleetAssignment` had produced the client's last accepted snapshot. That is a passive-client fleet/patrol mutation outside replay or accepted-command apply. | Replaced the direct patrol setup with accepted `CreateFleetPatrol` command application, then kept `MoveFleet` as the command under test. |
| `AuthoritativeMutationGuard_AllowsReplayApplyAndAcceptedCommandScopes_Headless` | TEST/FIXTURE BUG | Final round evidence: the troop helper created troops, but the fixture captured before applying object-list changes. `SnapshotShips()` reads the front `UState.Ships` list, so the authority payload could omit `SX`/`ST` rows and the client stayed at the seed transform/troop state (`[0,0]` instead of `[32,8]`). | After `FirstLoadedTroop` on both worlds, the fixture now calls `UpdateLists(removeInactiveObjects: false)` before mutating/capturing the authority snapshot, so the ship is present and carries the expected troop rows before replay-scope assertions run. |
| `Authoritative4XLiveClient_RecoversFromHostSaveAfterSyncMismatch_Headless` | TEST-CONFIRMED HOST SAVE-ADOPTION GUARD MISFIRE | Follow-up test evidence failed at `UnitTests/Multiplayer/Authoritative4XSessionTests.cs:9327`: the host returned no recovered save image and an empty error. In the same-process host/client recovery flow, the passive client's UI context can remain active while the host's saved image is loaded before it is reattached as an `IsHost:true` universe. The broad `ShouldTripMutationGuard` predicate was correct for direct local-mutation detection but too broad for troop simulation gating and authoritative save state installation. | Split the concerns without weakening the mutation tripwire: `TroopManager.Update` now gates only universes already attached as passive authoritative clients, while `Authoritative4XSessionSave.Load` runs under an authoritative state-application scope so save image installation is not misclassified as passive-client activity. Host adoption of its own save is ungated; the passive client's attached view refresh still skips local troop SIM writes and still consumes host-authored troop state through replay/save load. |
| `AuthoritativeHumanDiplomacy_ProposalsRouteApplyAndSync_Headless` | TEST/FIXTURE BUG | `PrepareTechnologyTrade` unlocked the proposer tech on both client replicas after several accepted diplomacy snapshots. The next command could truthfully fail pre-apply with unlocked-tech payload drift. | Moved the tech-trade fixture setup before the multi-client session starts, so the later proposal/acceptance remains the authoritative path under test. |
| `Authoritative4XLobbyNetworkFlow_ExchangesStartOverTcpAndAttachesLiveSession_Headless` | GUARD MISFIRE | Same-process host/client live sessions leave the passive join UI context active while host polling applies the accepted command. Host planet mutations were misidentified as passive-client writes. | Same guard-scope fix: passive active context no longer trips objects attached to a host live universe. |
| `Authoritative4XLobbySelfTest_RunsRealLoopbackUiCommandProof_Headless` | REAL LATENT LEAK | Final round trace: `AttachLiveSession(... Client)` activates a passive UI context, then `AttachAuthoritative4XMultiplayer` calls `RefreshAuthoritative4XPassiveClientView()` on the join client's generated universe. That calls `UpdatePassiveAuthoritativeView()` -> `UpdateAllSystems(FixedSimTime.Zero)` -> `Planet.Update` -> `TroopManager.Update`; the troop object belongs to the passive client universe (`IsHost:false`), so `SetInCombat()` was a real local replicated-state mutation, not host misattribution. | Added a narrow Debug passive-client gate at the start of `TroopManager.Update`. Passive clients consume host-authored `GT`/`ST` troop timer rows through replay, so local troop combat-timer updates are skipped under the tripwire without widening any mutation scope. |
| `Authoritative4XLiveGeneratedStart_UnpausedHeartbeatsStayCanonicalSynced_Headless` | REAL LATENT LEAK | Final round trace: this generated-start test begins unpaused. The join client is an attached passive universe and the live view refresh path can run troop update code locally; host heartbeat snapshots already replay canonical troop rows to the client. The object ownership is passive client, while host heartbeat processing remains protected by the existing host-universe attribution check. | Same `TroopManager.Update` passive-client gate prevents local troop timer/combat `Init` writes on the client while preserving host-authored replay. |
| `Authoritative4XLobby_TwoPlayerFourHundredShipTargetedCombatStaysSynced_Headless` | TEST/FIXTURE BUG | The stress fixture installed bulk `OrderAttackSpecificTarget` orders on every replica after explicit accepted attack commands had established client snapshots. The next no-op would truthfully report `S|` target/order drift before apply. | Moved the synthetic background target orders into pre-snapshot setup; explicit `AttackShip` commands still exercise the authoritative command path. |
| `Authoritative4XLobby_FourHumansFourAiClientInteractionGauntletStaysSynced_Headless` | TEST/FIXTURE BUG | The fixture spawned matching move ships and unlocked tech-trade setup on replicas after accepted snapshots. Those were setup writes, not host-authored or command-applied mutations. | Moved synthetic ship spawning and tech-trade unlock setup before the first submitted command; subsequent fleet, move, queue, diplomacy, and heartbeat checks still run through authoritative commands. |

The final four-red round changed the live-session disposition: the lobby self-test and unpaused generated heartbeat failures are a genuine passive-client troop simulation leak. The recovery-path failure is now test-confirmed as a host save-adoption guard misfire in the same-process resync path, fixed by separating passive simulation gating from authoritative save state application. Release behavior remains unchanged because the new troop-update gate is under `#if DEBUG`.

## Guarded Choke Points

- Planet runtime:
  - `Planet.SetOwner`, `SetColonyType`, `SetPrioritizedPort`, `ResetGarrisonSize`
  - defense/garrison/gov/manual-trade setters
  - authoritative replay and command applicator paths now use those setters.
- Troop runtime:
  - troop loyalty/host/action/timer/strength setters
  - ship troop membership and ground troop manager add/remove/combat state.
- Ship runtime:
  - `Ship.SetAuthoritativeTransform` covers SX replay/planted transform leaks.
- Diplomacy:
  - first-contact known-state via `Empire.SetRelationsAsKnown`
  - treaty/prepare/initial-strength changes
  - `Relationship.SetAuthoritativeDiplomacyState` for replay.
- Empire automation:
  - `Empire.SetAuthoritativeAutomationState`
  - `Empire.SwitchRushAllConstruction`
  - command/replay automation paths now use the guarded setter.

## Verification

Passed:

- `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false`
- `dotnet restore StarDrive.csproj --source "$env:USERPROFILE\.nuget\packages" --source "C:\Program Files (x86)\Microsoft SDKs\NuGetPackages" --ignore-failed-sources`
- `dotnet build StarDrive.csproj -c Debug --no-restore`
- `dotnet restore UnitTests\SDUnitTests.csproj --source "$env:USERPROFILE\.nuget\packages" --source "C:\Program Files (x86)\Microsoft SDKs\NuGetPackages" --ignore-failed-sources`
- `dotnet build UnitTests\SDUnitTests.csproj -c Debug --no-restore`
- `dotnet restore StarDrive.csproj --source "$env:USERPROFILE\.nuget\packages" --source "C:\Program Files (x86)\Microsoft SDKs\NuGetPackages" --ignore-failed-sources`
- `dotnet build StarDrive.csproj -c Release --no-restore`

Blocked by sandbox:

- `dotnet test UnitTests\SDUnitTests.csproj -c Debug --no-build --filter "FullyQualifiedName~AuthoritativeMutationGuard"`
- `dotnet test UnitTests\SDUnitTests.csproj -c Debug --no-build --filter "FullyQualifiedName~Authoritative4X"`

Both test runs failed before executing test bodies because assembly initialization attempts to write:

`C:\Users\shonh\AppData\Roaming\StarDrive\StarDrive.user.config`

The sandbox denies that path, and `APPDATA`/`LOCALAPPDATA` redirection did not change the configuration target.

Git commit was also blocked because this worktree's git index is stored under
`C:\dev\stardrive\StarDrive-main\.git\worktrees\StarDrive-desync-p3`, outside the writable roots.
`P3_COMMIT_PLAN.md` contains the exact commands to create the required local commit.

## Unguarded Residue Audit

Remaining direct writes in the same leaked families, intentionally left as P4/public-field debt:

- Planet runtime: colony/gov/trade UI fallbacks (`GovernorDetailsComponent`, `ColonyScreen_Update`), setup/lobby paths (`Authoritative4XLobby`, generated/new-colony setup), blueprint/test fixture direct `CType` and governor flag writes, and AI/simulation defense recalculation direct `Wanted*` writes.
- Troop runtime: replay internals still assign troop payload fields directly inside the sanctioned replay scope; some launch/landing internals still assign `HostShip`/`HostPlanet` directly; test setup still writes troop fields directly.
- Ship runtime: host simulation and visuals still write transform/orientation fields directly (`Ship_Movement`, `Ship_Update`, `LaunchShip`, `PlanetCrash`, carrier/hangar paths). SX replay uses the guarded transform setter.
- Diplomacy: AI war planner/offers, empire cleanup, and `AuthoritativeDiplomacy` still write relationship fields directly in host/accepted-command code. Replay and first-contact choke points are guarded.
- Empire automation: `AutomationWindow`, save/load transfer apply, lobby setup, and tests still write automation fields directly. The authoritative command/replay path uses `SetAuthoritativeAutomationState`.

These residues are mostly public-field legacy surfaces or host simulation/setup paths. P3 covers the passive-client leak tripwire at the requested choke points without broad field/property refactors.

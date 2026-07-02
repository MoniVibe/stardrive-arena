# StarDrive Desync P2 Report

Worktree: `C:\dev\stardrive\StarDrive-desync-p2`
Branch: `desync/p2-lane`

## KnownGap Disposition

| Row | P2 disposition | Code path |
| --- | --- | --- |
| `S.PolicyFields` | Repaired by real replay. | `ApplyShipRuntimeLine` now applies `LoyaltyId`, freighter trade flags, carrier policy flags, manual hangar override, trade route signature, and area-of-operation signature before digest comparison. Fatal S projection hashes only replayed runtime/policy columns. |
| `S.PolicyDiagnostics` | Moved out of fatal digest. | `IsFreighter`, `HasFighterBays`, and `HasTroopBays` stay on the wire as design-derived diagnostics but are excluded from the fatal S projection. |
| `G.DeepSpaceMovePosition` | Repaired by real replay. | `ApplyDeepSpaceGoalPayload` now parses `MovePosition.X/Y`; `DeepSpaceGoalRuntime` carries it; passive `DeepSpaceBuildGoal` replay stores an authoritative move-position override. |
| `D.DescriptiveFields` | Moved out of fatal digest. | Fatal `D|` projection keeps replayed registration identity only: `EmpireId`, `DesignName`, `DesignBase64`; descriptive fields remain emitted for diagnostics. |
| `G.Refit` | Moved to `HostOnlyDiagnostic`. | Manifest marks the Refit variant host-only; fatal digest filtering excludes it. |
| `G.FleetRequisition` | Moved to `HostOnlyDiagnostic`. | Manifest marks the FleetRequisition variant host-only; fatal digest filtering excludes it. |
| `FP.FleetPatrol` | Moved to `HostOnlyDiagnostic`. | Manifest marks fleet patrol plan signatures host-only; fatal digest filtering excludes them. |
| `F.Signatures` | Moved out of fatal digest. | Fatal `F|` projection keeps replayed runtime columns only and drops ship/node/patrol signatures; the full row remains emitted. |
| `BP.Blueprint` | Moved to `HostOnlyDiagnostic`. | Manifest marks blueprint signatures host-only; fatal digest filtering excludes them. |

## Mixed-Row Projection Audit

The wire payload format is unchanged. `SyncDigest` now projects mixed physical rows before hashing:

- `D|`: hashes columns `Prefix`, `EmpireId`, `DesignName`, and `DesignBase64`; drops `Hull`, `Role`, `BaseCost`, and `DesignSlotSignature`.
- `F|`: hashes replayed fleet runtime columns through `FinalDirection.Y`; drops `FleetShipSignature`, `FleetNodeSignature`, and `FleetPatrolSignature`.
- `S|`: hashes replayed runtime and policy columns; drops volatile movement placeholders plus design-derived capability diagnostics (`IsFreighter`, `HasFighterBays`, `HasTroopBays`).

## Final P2 Lane Follow-Up

- `Authoritative4XSyncMismatchException.FirstPayloadDifferenceForLog` and live `SYNC_MISMATCH firstDiff` now diff the same fatal projection that `SyncDigest` hashes. The diagnostic compares filtered/projected fatal rows, skips host-only rows, and reports both projected values plus raw authority/client lines for context.
- The corrected diagnostic removes the D `BaseCost` red herring: raw descriptive field drift is no longer allowed to choose the first reported line when its projected `D|EmpireId|DesignName|DesignBase64` content is equal.
- Root cause fix: `D|` replay now treats player-design registration as idempotent by same-name authoritative identity. If the matching global player design has the same digest-covered base64, it is reused; otherwise the authoritative payload is registered. In both paths, stale same-name buildable design references are removed from the empire before the authoritative registered instance is added. This prevents a host-only client design drift from leaving duplicate or stale fatal registration state after replay.

## Fingerprint

`ArenaMultiplayerSettings.ProtocolVersion` was bumped from `1` to `2`.

## Guard Change

`ReplicationCoverageDiagnostic_FatalRowsHaveApplyOrExplicitKnownGap_Headless` now rejects any `Fatal` descriptor without an apply delegate. `KnownGap` is no longer an allowed fatal waiver, and the expected KnownGap set is empty. Host-only diagnostics are explicitly enumerated as `BP.Blueprint`, `D.DescriptiveFields`, `F.Signatures`, `FP.FleetPatrol`, `G.FleetRequisition`, `G.Refit`, and `S.PolicyDiagnostics`.

## Regressions

- Added `Authoritative4XSnapshot_AppliesShipPolicyRowsBeforeDigestCompare_Headless` for `S.PolicyFields` drift repair.
- Added `Authoritative4XSnapshot_AppliesDeepSpaceMovePositionBeforeDigestCompare_Headless` for `G.DeepSpaceMovePosition` drift repair.
- Added `Authoritative4XSyncMismatch_FirstDiffUsesFatalProjection_Headless` for projected `firstDiff` diagnostics.
- Added `Authoritative4XSnapshot_HostOnlyDiagnosticRowsDoNotTriggerFatalResync_Headless` covering `D.DescriptiveFields`, `G.Refit`, `G.FleetRequisition`, `FP.FleetPatrol`, `F.Signatures`, and `BP.Blueprint`.
- Fixed the D replay side effect covered by `Authoritative4XSnapshot_HostOnlyDiagnosticRowsDoNotTriggerFatalResync_Headless`: client-only descriptive design drift must not leave stale same-name player-design buildables after authoritative replay.
- Fixed the `S.PolicyFields` fixture so the freighter is present in the canonical `S|` payload row before policy drift is asserted.
- Updated legacy BP/FP command-routing tests to the host-only contract: commands still mutate host/client state, while client-only BP/FP diagnostic drift does not change the fatal digest.
- Tightened the clean live heartbeat test so host/client multi-tick sync also asserts no resync wait state and zero drained resync requests.
- Updated older forced-mismatch tests so they inject fatal planet runtime drift instead of host-only blueprint drift.

## Verification

Passed:

- `git diff --check`
- `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false`

Not runnable to completion in this sandbox:

- Runtime tests were not attempted in this follow-up per orchestrator instruction; the orchestrator will run the remaining red test.
- Historical sandbox note: the build without `-p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false` can fail at `GenerateDepsFile` when writing `C:\dev\stardrive\StarDrive-desync-p2\game\SDUtils.deps.json`.

## Commit State

Local git commit is blocked in this sandbox because the worktree git metadata lives outside the writable roots:

`fatal: Unable to create 'C:/dev/stardrive/StarDrive-main/.git/worktrees/StarDrive-desync-p2/index.lock': Permission denied`

No commit or push was attempted in this follow-up. See `P2_COMMIT_PLAN.md` for the exact commits to make outside the sandbox.

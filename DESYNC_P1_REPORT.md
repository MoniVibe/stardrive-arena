# DESYNC P1 Report - Executable Replication Manifest

Date: 2026-07-02
Branch/worktree: `desync/p1-executable-manifest` / `C:\dev\stardrive\StarDrive-desync-p1`

## 1. Summary

Phase P1 converted the authoritative 4X replication manifest from a descriptive prefix list into executable descriptors.

Implemented:
- `ReplicatedRowDescriptor` with prefix, owner, field group, emit delegate, replay delegate, digest policy, apply mode, P0 field mapping, variant matching, and explicit `KnownGap`.
- Manifest-driven payload emission in `AuthoritativeStateSnapshot.BuildPayload`.
- Manifest-driven top-level replay dispatch in `ApplyEmpireRuntimePayload`.
- Manifest-driven `SyncDigest`/`TransformDigest` filtering through descriptor `DigestPolicy`.
- Enforcing descriptor symmetry tests in `ReplicationCoverageDiagnosticTests`.

No P0 gaps were repaired in this lane. Existing missing/partial replay surfaces are declared as `KnownGap` descriptors for P2.

## 2. Descriptor Map

| Descriptor | P0 row/fields | Digest | Replay status |
| --- | --- | --- | --- |
| `V.UniversePreferences` | `V`: preference flags / inter-trade / projectors | Fatal | Initial line replay |
| `SD.StarDate` | `SD`: `StarDate` | Fatal | Initial line replay |
| `E.EmpireRuntime` | `E`: research, money, tax, treasury, automation, auto-designs, research progress | Fatal | Initial line replay |
| `U.UnlockedTech` | `U`: `EmpireId`, `TechUid`, `TechLevel` exact set | Fatal | Batch replay |
| `D.PlayerDesignRegistration` | `D`: `EmpireId`, `DesignName`, `DesignBase64` | Fatal | Batch line replay |
| `D.DescriptiveFields` | `D`: `Hull`, `Role`, `BaseCost`, `DesignSlotSignature` | Fatal | `KnownGap` |
| `R.Relationship` | `R`: diplomacy/treaty fields | Fatal | Initial line replay |
| `G.MarkForColonization` | `G`: MarkForColonization fields | Fatal | Batch replay |
| `G.Refit` | `G`: all Refit fields | Fatal | `KnownGap` |
| `G.FleetRequisition` | `G`: all FleetRequisition fields | Fatal | `KnownGap` |
| `G.DeepSpace` | `G`: DeepSpace replayed fields | Fatal | Batch replay |
| `G.DeepSpaceMovePosition` | `G`: `DeepSpace.MovePosition.X/Y` | Fatal | `KnownGap` |
| `FP.FleetPatrol` | `FP`: `FleetPatrolPlanSignature` | Fatal | `KnownGap` |
| `F.Runtime` | `F`: fleet key/name/icon/command/final vector | Fatal | Batch replay |
| `F.Signatures` | `F`: `FleetShipSignature`, `FleetNodeSignature`, `FleetPatrolSignature` | Fatal | `KnownGap` |
| `P.PlanetRuntime` | `P`: owner, colony controls, labor/import/export/governor/budget/queue count | Fatal | Initial line replay |
| `BP.Blueprint` | `BP`: `BlueprintSignature` | Fatal | `KnownGap` |
| `T.ColonyTile` | `T`: tile building/biosphere/habitable/terraformable exact set | Fatal | Batch replay |
| `GT.GroundTroop` | `GT`: ground troop membership/runtime exact set | Fatal | Initial line + batch replay |
| `GC.GroundCombat` | `GC`: ground combat runtime/participants | Fatal | Batch replay |
| `Q.ConstructionQueue` | `Q`: queue shape/progress exact set | Fatal | Batch replay |
| `SC.ShipPresence` | `SC`: ship id/owner/design active set | Fatal | Batch replay |
| `S.RuntimeReplay` | `S`: fleet ids, AI/combat state, scuttle, targets, order signatures | Fatal | Initial line + batch replay |
| `S.PolicyFields` | `S`: loyalty, freighter/trade flags, carrier flags, hangar override, trade route, area of operation | Fatal | `KnownGap` |
| `SX.ShipTransform` | `SX`: transform/system/active/dying/orientation | Transform | Initial line replay |
| `SV.ShipVisibility` | `SV`: known-by mask | Fatal | Initial line replay |
| `ST.ShipTroop` | `ST`: ship troop membership/runtime exact set | Fatal | Batch replay |

## 3. Byte-Identical Wire/Digest Evidence

The legacy append blocks were moved into explicit emit delegates without changing row text, field order, field formatting, or sort keys.

Emission order is preserved by descriptor emit stages:
- Pre-scoped: `V, SD, E, U, D, R`
- Per-empire: `G.MarkForColonization, G.Refit, G.FleetRequisition, G.DeepSpace, FP, F`
- Per-planet: `P, BP, T, GT, GC, Q`
- Post-scoped: `SC, S, SX, SV, ST`

Digest policy is behavior-preserving:
- `SX.ShipTransform` is `Transform`.
- Every other emitted payload prefix remains `Fatal`.
- `FP` and `BP` intentionally remain Fatal in P1 to preserve existing digest behavior.

Runtime digest comparison could not execute in this sandbox because unit-test assembly initialization fails before test methods on the AppData config write. Build verification and source-level descriptor guards passed/compiled.

## 4. KnownGap Descriptors For P2

Explicit P2 debt, not fixed here:
- `FP.FleetPatrol`: fatal `FP` rows have no replay path.
- `BP.Blueprint`: fatal `BP` rows have no replay path.
- `G.Refit`: fatal Refit goal variant has no replay path.
- `G.FleetRequisition`: fatal FleetRequisition goal variant has no replay path.
- `G.DeepSpaceMovePosition`: emitted DeepSpace `MovePosition.X/Y` are not replayed.
- `F.Signatures`: fleet membership/layout/patrol signatures are fatal-digest-only.
- `S.PolicyFields`: ship loyalty, freighter/trade, carrier, hangar, trade-route, and operation-area fields are fatal-digest-only.
- `D.DescriptiveFields`: design hull/role/base-cost/slot-signature fields are fatal-digest-only.

## 5. Test Status

Builds:
- `dotnet build StarDrive.csproj -c Debug -p:Platform=x64 --no-restore` passed.
- `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-restore` passed.
- Restore/build required `NUGET_PACKAGES=$env:USERPROFILE\.nuget\packages`; the default sandbox package cache was incomplete and NuGet network access is restricted.

Runtime suites:
- `FullyQualifiedName~Authoritative4X`: 163 selected, 0 passed, 163 failed before test methods at assembly initialization.
- `FullyQualifiedName~Arena`: 106 selected, 0 passed, 106 failed before test methods at assembly initialization.
- Failure cause: `GlobalStats.LoadConfig()` cannot write `C:\Users\shonh\AppData\Roaming\StarDrive\StarDrive.user.config` from this sandbox.

## 6. Commit Log / Fallback

Real commits were blocked because git could not create:
`C:/dev/stardrive/StarDrive-main/.git/worktrees/StarDrive-desync-p1/index.lock`

Fallback file maintained:
- `P1_COMMIT_PLAN.md`

Logical commits recorded there:
1. Add executable replication descriptor metadata.
2. Drive payload emission from replication descriptors.
3. Drive replay dispatch and digest filtering from descriptors.
4. Add descriptor coverage symmetry guard.
5. Resolve first-diff labels through line descriptors.

## 7. Open Risks / P2 Follow-Ups

- P2 must decide whether to add replay or change digest policy for `FP` and `BP`.
- P2 must add real replay for `G.Refit` and `G.FleetRequisition`, or explicitly move them out of Fatal digest.
- P2 must resolve fatal-only field groups in `F`, `S`, and `D`.
- Raw-hash-only fields from P0 (`Ship.Health`, `Planet.PopulationBillion`, empire aggregates, RNG state) remain outside the executable payload/replay contract.
- Runtime byte/digest proof still needs to be rerun outside the AppData-restricted sandbox.

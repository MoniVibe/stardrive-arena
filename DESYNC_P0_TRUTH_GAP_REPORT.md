# StarDrive MP P0 Truth-Gap Report

Date: 2026-07-02
Branch/worktree: `desync/p0-truth-gap` / `C:\dev\stardrive\StarDrive-desync-p0`

## Scope and Definitions

This is Phase P0 of the MP desync convergence plan: diagnose the gap between the state the host records, the state the payload replicates, and the state the passive client can actually apply.

The current code has two distinct digest contracts:

- Raw authoritative hash: `AuthoritativeStateSnapshot.Capture` computes `HashLo/HashHi` via `UniverseStateHash.WriteAuthoritative` (`Authoritative4XSession.cs:1027-1040`, `UniverseStateHash.cs:29-101`).
- Fatal payload digest: `SyncDigest` hashes every payload row except `SX`; `TransformDigest` hashes only `SX` (`Authoritative4XSession.cs:1495-1513`). Client apply throws on `SyncDigest` or `TransformDigest` mismatch (`:2730-2738`). Raw hash mismatch is recorded as `LastRawHashDrift` and does not throw when payload digests match (`:2728-2742`).

This means the practical fatal desync surface today is the payload builder plus client replay, not the raw `UniverseStateHash` walker. The raw walker is still important as a host/client drift diagnostic and is included below as the "raw hash" column.

## Source-Extracted Row Prefixes

Extraction method implemented in `UnitTests/Multiplayer/ReplicationCoverageDiagnosticTests.cs`:

- Payload builder prefixes: regex over `BuildPayload` in `Authoritative4XSession.cs:1043-1415`.
- Replay dispatch prefixes: regex over `StartsWith("<prefix>|")` replay/dispatch scans in `Authoritative4XSession.cs`.
- Manifest prefixes and modes: `AuthoritativeReplicationManifest.AllRows`.
- Raw hash fields: explicit hand-derived constants from `UniverseStateHash.cs:29-101`, with field-level constants in the diagnostic test.

Representative built-world capture is wired in the diagnostic test through the existing `Authoritative4XSessionTests.BuildWorld` plus `AuthoritativeStateSnapshot.Capture`. In this sandbox, test execution is blocked before the test method by `System.Configuration` writing to `C:\Users\shonh\AppData\Roaming\StarDrive\StarDrive.user.config`, so the report below uses the source-derived complete builder set.

| Set | Prefixes |
| --- | --- |
| Payload builder source | `BP,D,E,F,FP,G,GC,GT,P,Q,R,S,SC,SD,ST,SV,SX,T,U,V` |
| Replay dispatch/source consumes | `D,E,F,G,GC,GT,P,Q,R,S,SC,SD,ST,SV,SX,T,U,V` |
| Manifest declared | `BP,D,E,F,FP,G,GC,GT,P,Q,R,S,SC,SD,ST,SV,SX,T,U,V` |
| Builder but no replay prefix | `BP,FP` |
| Replay but no builder prefix | none |
| Builder but no manifest prefix | none |
| Manifest but no builder prefix | none |

## Row-Level Coverage Diff

Legend: Fatal digest = `SyncDigest` for every row except `SX`; `SX` is `TransformDigest`.

| Prefix | Payload/fatal digest | Replay apply path | Manifest mode | Raw hash coverage | Row-level finding |
| --- | --- | --- | --- | --- | --- |
| `V` | yes | yes | DirectReplay | no | Payload/replay only. |
| `SD` | yes | yes | DirectReplay | `StarDate` | Aligned at row level. |
| `E` | yes | yes | DirectReplay | partial | Raw hash covers economy/research aggregates/RNG fields not present in payload replay. |
| `U` | yes | yes | BatchReplay | aggregate only | Raw hash only counts unlocked techs; payload/replay carry exact tech id/level set. |
| `D` | yes | yes | DirectReplay | no | Replay applies name/base64 only; descriptive design fields are payload-only. |
| `R` | yes | yes | DirectReplay | no | Fatal payload/replay only; absent from raw hash. |
| `G` | yes | yes | BatchReplay | aggregate only | Prefix exists, but replay is variant-partial. |
| `FP` | yes | no | DigestOnly | no | Fatal payload-digest row with no replay path. |
| `F` | yes | yes | DirectReplay | aggregate only | Prefix exists, but membership/layout/patrol fields are payload-only. |
| `P` | yes | yes | DirectReplay | partial | Owner and queue count align; population is raw-hash-only. |
| `BP` | yes | no | DigestOnly | no | Fatal payload-digest row with no replay path. |
| `T` | yes | yes | BatchReplay | no | Fatal payload/replay only; absent from raw hash. |
| `GT` | yes | yes | BatchReplay | no | Fatal payload/replay only; absent from raw hash. |
| `GC` | yes | yes | DirectReplay | no | Fatal payload/replay only; absent from raw hash. |
| `Q` | yes | yes | BatchReplay | no direct row | Raw hash only sees `P.ConstructionQueue.Count`, not queue item identity/progress. |
| `SC` | yes | yes | DirectReplay | `ShipId` only | Raw hash sees ship existence/id, not owner/design. |
| `S` | yes | yes | DirectReplay | `ShipId`, `Health` | Prefix exists, but many durable ship fields are payload-only. |
| `SX` | TransformDigest | yes | DirectReplay | partial | Transform digest/replay align on position/velocity/rotation; active/system/orientation are transform-only, not raw hash. |
| `SV` | yes | yes | DirectReplay | no | Fatal payload/replay only; absent from raw hash. |
| `ST` | yes | yes | BatchReplay | no | Fatal payload/replay only; absent from raw hash. |
| `RNG` | no | no | none | yes | Raw-hash-only drift can never be snapshot-repaired. |

## Field-Level Diff

### `V` UniversePreferences

- Payload fields: `PreferenceFlags`, `AllowPlayerInterTrade`, `PrioritizeProjectors`.
- Replay fields: same fields via `ApplyUniversePreferenceLine` (`Authoritative4XSession.cs:140-153`).
- Raw hash fields: none.
- Gap: fatal payload/replay state is not represented in `UniverseStateHash`.

### `SD` StarDate

- Payload fields: `StarDate`.
- Replay fields: `StarDate` via `ApplyStarDateLine` (`:155-162`).
- Raw hash fields: `StarDate` (`UniverseStateHash.cs:31-32`).
- Gap: none at field level.

### `E` EmpireRuntime

- Payload fields: `EmpireId`, `Research.Topic`, `ResearchQueueSignature`, `Money`, `TaxRate`, `TreasuryGoal`, `AutoTaxes`, `AutomationFlags`, auto-design selections, `ResearchProgress` (`Authoritative4XSession.cs:1049-1065`).
- Replay fields: `Research.Topic`, `ResearchQueueSignature`, `Money`, `TaxRate`, `TreasuryGoal`, `AutomationFlags`, auto-design selections, `ResearchProgress` (`:164-221`, `:2526-2547`).
- Raw hash fields: `EmpireId`, `Money`, `TotalPopBillion`, `NetPlanetIncomes`, `NumPlanets`, `UnlockedTechs.Length`, `Research.NetResearch`, `Research.Topic`, `AI.Goals.Count`, `AllFleets.Count`, `EmpireRandomState` (`UniverseStateHash.cs:35-65`).
- Raw-hashed but not replayed: `TotalPopBillion`, `NetPlanetIncomes`, `NumPlanets`, `UnlockedTechs.Length`, `Research.NetResearch`, `AI.Goals.Count`, `AllFleets.Count`, `EmpireRandomState`.
- Replayed/fatal-digest but not raw-hashed: research queue shape, taxes, treasury goal, automation flags and auto-design selections, research progress.
- Payload-only nuance: builder emits `AutoTaxes` separately, but replay applies automation flags; if those diverge field-by-field, the separate payload column is not independently consumed.

### `U` UnlockedTech

- Payload fields: `EmpireId`, `TechUid`, `TechLevel` (`Authoritative4XSession.cs:1067-1078`).
- Replay fields: exact desired unlocked set by empire, including `TechUid` and `TechLevel` (`:249-322`).
- Raw hash fields: `UnlockedTechs.Length` only (`UniverseStateHash.cs:50-52`).
- Raw-hashed but not replayed as same field: aggregate count only.
- Replayed/fatal-digest but not raw-hashed: exact tech identity and level.

### `D` PlayerDesign

- Payload fields: `EmpireId`, `DesignName`, `Hull`, `Role`, `BaseCost`, `DesignSlotSignature`, `DesignBase64` (`Authoritative4XSession.cs:1080-1094`).
- Replay fields: `EmpireId`, `DesignName`, `DesignBase64`; if design already exists by name, replay registers that existing design (`:325-372`).
- Raw hash fields: none.
- Payload-not-applied: `Hull`, `Role`, `BaseCost`, `DesignSlotSignature`.
- Risk: descriptive design fields affect fatal `SyncDigest` but do not independently repair client state.

### `R` Relationship

- Payload fields: `EmpireId`, `TargetEmpireId`, `Known`, `AtWar`, treaty flags (`Authoritative4XSession.cs:1097-1110`).
- Replay fields: same fields plus derived `CanAttack`, `IsHostile`, `ActiveWar` maintenance (`:975-1022`).
- Raw hash fields: none.
- Gap: fatal payload/replay state is absent from raw hash.

### `G` EmpireGoal

- Payload variants: `MarkForColonization`, `Refit`, `FleetRequisition`, `DeepSpace` (`Authoritative4XSession.cs:1112-1187`).
- Replay variants: `MarkForColonization` (`:814-873`) and `DeepSpace` (`:875-962`).
- Raw hash fields: `AI.Goals.Count` only (`UniverseStateHash.cs:56-57`).
- Payload-not-applied:
  - All `Refit` fields: `Step`, `OldShipId`, `ToBuildName`, `PlanetBuildingAtId`, `Rush`, `FleetId`, `FleetKey`.
  - All `FleetRequisition` fields: `Step`, `FleetId`, `FleetKey`, `NodeIndex`, `TemplateName`, `PlanetBuildingAtId`, `Rush`.
  - `DeepSpace.MovePosition.X/Y` are emitted but not carried into `DeepSpaceGoalRuntime`.
- Manifest mismatch: prefix-level `BatchReplay` overstates field coverage. `G` needs variant-level modes.

### `FP` FleetPatrol

- Payload fields: `EmpireId`, `FleetPatrolPlanSignature` (`Authoritative4XSession.cs:1189-1192`).
- Replay fields: none.
- Raw hash fields: none in `UniverseStateHash`.
- Manifest mode: `DigestOnly`.
- Critical gap: `FP` is included in fatal `SyncDigest` because only `SX` rows are filtered out. A client mismatch in patrol plan signature cannot be patched by replay and will cause resync.

### `F` FleetRuntime

- Payload fields: `EmpireId`, `FleetId`, `FleetKey`, `Name`, `FleetIconIndex`, `CommandShipId`, `FinalPosition`, `FinalDirection`, `FleetShipSignature`, `FleetNodeSignature`, `FleetPatrolSignature` (`Authoritative4XSession.cs:1194-1208`).
- Replay fields: `FleetKey`, `FleetIconIndex`, `CommandShipId`, command ship membership, `FinalPosition`, `FinalDirection`, `Name` (`:723-792`).
- Raw hash fields: `AllFleets.Count` aggregate only (`UniverseStateHash.cs:58-59`).
- Payload-not-applied: `FleetShipSignature`, `FleetNodeSignature`, `FleetPatrolSignature`; most full membership/layout state remains digest-observed only.
- Manifest mismatch: row mode `DirectReplay` hides field-level digest-only fields.

### `P` PlanetRuntime

- Payload fields: `PlanetId`, `OwnerId`, colony type, garrison/platform/station wants, labor percents/locks, import/export states, governor flags, manual budgets, `ConstructionQueue.Count` (`Authoritative4XSession.cs:1211-1246`).
- Replay fields: same fields via `ApplyPlanetRuntimeLine`; `OwnerId` calls `planet.SetOwner(owner)` (`:553-638`).
- Raw hash fields: `PlanetId`, `OwnerId`, `PopulationBillion`, `ConstructionQueue.Count` (`UniverseStateHash.cs:84-93`).
- Raw-hashed but not replayed: `PopulationBillion`.
- Replayed/fatal-digest but not raw-hashed: most colony controls and manual budgets.
- Historical mapping: the old "planet owner hashed, replay did not apply it" class would be visible here. Current code now applies `OwnerId`, so this report marks that field as aligned.

### `BP` Blueprint

- Payload fields: `PlanetId`, `BlueprintSignature` (`Authoritative4XSession.cs:1248-1251`, `:1516+`).
- Replay fields: none.
- Raw hash fields: none.
- Manifest mode: `DigestOnly`.
- Critical gap: `BP` is included in fatal `SyncDigest` and has no repair path.

### `T` ColonyTile

- Payload fields: `PlanetId`, `TileX`, `TileY`, `BuildingName`, `Biosphere`, `Habitable`, `Terraformable` (`Authoritative4XSession.cs:1253-1266`).
- Replay fields: exact tile set replacement and row apply (`:1930-2039`).
- Raw hash fields: none.
- Gap: fatal payload/replay state is absent from raw hash.

### `GT` GroundTroop

- Payload fields: planet/tile/index, `LoyaltyId`, `TroopName`, `Strength`, action counters, timers (`Authoritative4XSession.cs:1268-1288`).
- Replay fields: exact ground troop set plus runtime fields (`:2041-2166`, `:2336-2356`).
- Raw hash fields: none.
- Gap: fatal payload/replay state is absent from raw hash.

### `GC` GroundCombat

- Payload fields: `PlanetId`, `CombatIndex`, `Phase`, `Timer`, `AttackerLoyaltyId`, defense tile, attacking/defending troop refs, attacking/defending building refs (`Authoritative4XSession.cs:1291-1306`).
- Replay fields: same fields (`:2210-2280`).
- Raw hash fields: none.
- Gap: fatal payload/replay state is absent from raw hash.

### `Q` ConstructionQueue

- Payload fields: `PlanetId`, `QueueIndex`, shape (`IsShip`, `IsBuilding`, `IsTroop`, `QueueType`, names, tile), `Cost`, `ProductionSpent`, `Rush`, `Canceled` (`Authoritative4XSession.cs:1308-1328`).
- Replay fields: exact queue set replacement plus item runtime fields (`:374-551`).
- Raw hash fields: no direct `Q` row; only `P.ConstructionQueue.Count`.
- Gap: raw hash can detect count only, not queue identity/order/progress.

### `SC` ShipPresence

- Payload fields: `ShipId`, `LoyaltyId`, `DesignName` (`Authoritative4XSession.cs:1331-1338`).
- Replay fields: materializes missing ships from `ShipId`, `LoyaltyId`, `DesignName`; removes active ships missing from expected `S` rows (`:640-721`).
- Raw hash fields: ship count and `ShipId` only (`UniverseStateHash.cs:70-80`).
- Raw-hashed but not replayed as same field: count is implicit through exact active set.
- Replayed/fatal-digest but not raw-hashed: ship owner/design identity.
- Risk: `SC` does not update existing ship owner/design, only materializes missing ships.

### `S` ShipRuntime

- Payload fields: `ShipId`, `LoyaltyId`, fleet ids, `AIState`, `CombatState`, volatile movement placeholders, `ScuttleTimer`, target fields, order queue signature, freighter/trade flags, carrier flags, hangar override, trade route, area of operation (`Authoritative4XSession.cs:1339-1374`).
- Replay fields from snapshot: fleet ids, `AIState`, `CombatState`, `ScuttleTimer`, target fields, target queue, escort target, order queue signature (`:1739-1784`).
- Raw hash fields: `ShipId`, `Position`, `Velocity`, `Rotation`, `Health` (`UniverseStateHash.cs:70-80`). Position/velocity/rotation map more naturally to `SX`; `Health` has no payload/replay row.
- Raw-hashed but not replayed: `Health`.
- Payload-not-applied: `LoyaltyId`, freighter/trade flags, carrier flags, `ManualHangarOverride`, `TradeRouteSignature`, `AreaOfOperationSignature`.
- Risk nuance: many of these fields are command-applied on the passive client for accepted commands, so normal command flow can pass. They are not snapshot-repaired from `S`, so stale clients, join/recovery paths, or missed local command application remain exposed.

### `SX` ShipTransform

- Payload fields: `ShipId`, `Tick`, `Position.X/Y`, `Velocity.X/Y`, `Rotation`, `SystemId`, `Active`, `Dying`, `YRotation`, `XRotation` (`Authoritative4XSession.cs:1376-1390`).
- Replay fields: all except `Tick` (`:2358-2392`).
- Raw hash fields: `ShipId`, `Position.X/Y`, `Velocity.X/Y`, `Rotation` (`UniverseStateHash.cs:70-80`).
- Transform-digest/replay but not raw-hashed: `SystemId`, `Active`, `Dying`, `YRotation`, `XRotation`.
- Raw/payload nuance: `SX` is intentionally excluded from `SyncDigest` and validated through `TransformDigest`.

### `SV` ShipVisibility

- Payload fields: `ShipId`, `KnownByMask` (`Authoritative4XSession.cs:1391-1395`).
- Replay fields: same fields via `KnownByEmpires.SetKnownMask` (`:2394-2405`).
- Raw hash fields: none.
- Gap: fatal payload/replay state is absent from raw hash.

### `ST` ShipTroop

- Payload fields: `ShipId`, `TroopIndex`, `LoyaltyId`, `TroopName`, `Strength`, action counters, timers (`Authoritative4XSession.cs:1396-1412`).
- Replay fields: exact ship troop set plus runtime fields (`:1857-1928`, `:2316-2356`).
- Raw hash fields: none.
- Gap: fatal payload/replay state is absent from raw hash.

### `RNG` Raw Hash Only

- Payload fields: none.
- Replay fields: none.
- Raw hash fields: `EmpireRandomState`, `UniverseRandomState` (`UniverseStateHash.cs:62-65`, `:96-99`).
- Gap: raw hash drift can identify RNG divergence, but the snapshot replay contract cannot repair it.

## Priority Backlog for P1/P2

1. Split `G` by variant. `MarkForColonization` and `DeepSpace` have replay paths; `Refit` and `FleetRequisition` are fatal-digest-only today.
2. Split `F` by field. Fleet command/position/name/icon replay; membership/layout/patrol signatures do not.
3. Remove fatal `SyncDigest` exposure for `FP` and `BP`, or add real replay paths. Current `DigestOnly` rows are fatal because only `SX` is filtered out.
4. Split `S` by field. Snapshot replay does not apply owner, freighter/trade flags, carrier flags, hangar override, trade route, or area of operation.
5. Decide whether raw-hash-only fields (`Ship.Health`, `Planet.PopulationBillion`, empire economy aggregates, RNG state) are intended diagnostic-only lanes or need payload/replay coverage.
6. Recast manifest modes from row-level only to field/variant-level descriptors. Prefix-level `DirectReplay`/`BatchReplay` is not precise enough for `G`, `F`, `S`, or `D`.

## Retro Mapping

- Planet owner desync class: now covered. `P.OwnerId` is emitted, replayed, and raw-hashed.
- Missing replay row class: still exists as `FP` and `BP`. Both are in fatal payload digest and manifest `DigestOnly`, with no replay path.
- Fleet layout/patrol desync class: likely explained by `F.FleetShipSignature`, `F.FleetNodeSignature`, `F.FleetPatrolSignature`, and `FP` being fatal-digest fields without replay repair.
- Goal desync class: likely explained by `G.Refit` and `G.FleetRequisition` being emitted into the fatal payload digest but not replayed.
- Carrier/freighter/trade-route policy desyncs: normal accepted-command tests can pass because the client applies the command locally, but stale/rejoin/resync repair depends on snapshot fields that `ApplyShipRuntimeLine` currently ignores.

## Verification Note

Compile of the new diagnostic test passed during `dotnet test` build. Runtime test execution in this sandbox is blocked before test methods run by StarDrive assembly initialization attempting to save `StarDrive.user.config` under `C:\Users\shonh\AppData\Roaming\StarDrive`, which is outside the writable sandbox.

Attempted verification commands selected the expected suite counts but failed at assembly initialization:

- `FullyQualifiedName~Authoritative4X`: 163 selected, 0 passed, 163 failed at `GlobalStats.LoadConfig()` user-config save.
- `FullyQualifiedName~Arena`: 106 selected, 0 passed, 106 failed at `GlobalStats.LoadConfig()` user-config save.

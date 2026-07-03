# Trusted Host-Accepted Command Replay Report

## Summary

`Authoritative4XCommandApplicator.Apply(..., trustHostAccepted)` now threads `trustHostAccepted`
into every authoritative 4X command handler. Host-side `Apply(command, tick)` still passes
`false`, so host authorization remains unchanged.

Trusted passive-client replay now:

- bypasses state-dependent authorization/eligibility gates such as ownership, attackability,
  buildability, tech unlocks, affordability, queue availability, fleet assignment capability,
  colonization legality, troop action availability, and treaty/friendly-hostile gates;
- keeps malformed command guards such as invalid enum values, malformed payloads, non-finite
  coordinates/amounts, illegal names, unsupported command modes, and impossible command shapes;
- converts missing/not-yet-materialized entity references and stale index/lookups into accepted
  no-ops on the trusted path, leaving the authoritative snapshot rows to repair local state;
- keeps trusted replay inside the existing accepted-command mutation scope entered around the
  applicator switch.

## Command-Kind Classification

| Command kind | Trusted authorization / eligibility bypassed | Structural / malformed kept |
|---|---|---|
| `NoOp` | None. | Unsupported outer command kind still rejects. |
| `MoveShip` | Ship ownership. | Missing/inactive ship becomes trusted no-op; unsupported move flags reject. |
| `SetColonyType` | Planet ownership. | Missing planet trusted no-op; invalid colony type rejects. |
| `SetColonizationGoal` | Existing owner and habitability. | Missing planet trusted no-op; invalid enabled flag rejects. |
| `SetColonyLabor` | Planet ownership, colony/trade-hub type, cybernetic food restriction. | Missing planet trusted no-op; malformed labor payload, non-finite/out-of-range/sum-invalid percentages reject. |
| `SetResearchTopic` | Already-unlocked tech. | Empty UID rejects; missing tech/tech-entry trusted no-op. |
| `QueueResearch` | Discovered and can-be-researched gates. | Empty UID rejects; missing tech/tech-entry trusted no-op. |
| `RemoveResearchQueueItem` | Queued-state gate. | Empty UID rejects; missing tech/tech-entry/not-queued trusted no-op. |
| `MoveResearchQueueItem` | Queued-state and can-move up/down/result-count gates. | Empty UID and invalid move enum reject; missing tech/tech-entry/not-queued trusted no-op. |
| `SetEmpireBudget` | None; it is direct state setting. | Malformed budget payload and invalid percentages reject. |
| `SetEmpireAutomation` | Design requirement/buildability normalization gates. | Invalid automation flags/payload reject. |
| `SetUniversePreferences` | None; it is direct state setting. | Unsupported preference flags reject. |
| `DiplomacyProposal` | Trusted replay accepts without local diplomacy re-authorization; host diplomacy validation unchanged. | Host path still delegates to `AuthoritativeDiplomacyManager`; trusted path is snapshot-only. |
| `DiplomacyResponse` | Trusted replay accepts without local diplomacy re-authorization; host diplomacy validation unchanged. | Host path still delegates to `AuthoritativeDiplomacyManager`; trusted path is snapshot-only. |
| `DesignShip` | Hull unlock, `IsValidDesign`, legal player buildable, module/tech availability, post-register `CanBuildShip`. | Empty/invalid base64 payload, unnamed design, unknown hull, platform/station command shape, registration name conflict/failure reject. |
| `QueueBuild` | Planet ownership, spaceport/shipyard, `CanBuildShip`. | Missing planet/design trusted no-op; empty design name and platform/station shape reject. |
| `QueueBuilding` | Planet ownership, building-can-build-here, tile-can-enqueue, enqueue failure. | Missing planet/building/tile trusted no-op; empty name and unsupported placement mode reject. |
| `QueueTroop` | Planet ownership, spaceport, `WeCanBuildTroop`. | Missing planet/troop trusted no-op; empty troop name rejects. |
| `CancelConstructionQueueItem` | Planet ownership and completed-item gate. | Missing planet or stale queue index trusted no-op. |
| `ReorderConstructionQueueItem` | Planet ownership. | Missing planet/current index/target index trusted no-op. |
| `RushConstructionQueueItem` | Planet ownership, completed-item, and lower-level affordability/rush failure. | Missing planet/stale queue index trusted no-op; invalid rush amount rejects. |
| `ToggleConstructionRush` | Planet ownership and completed-item gate. | Missing planet/stale queue index trusted no-op. |
| `SetPlanetGoodsState` | Planet ownership and non-cybernetic food policy gate. | Missing planet trusted no-op; invalid goods kind/state rejects. |
| `SetPlanetPrioritizedPort` | Planet ownership and spaceport-required gate. | Missing planet trusted no-op. |
| `SetPlanetManualBudget` | Planet ownership. | Missing planet trusted no-op; invalid budget kind/value rejects. |
| `SetPlanetGovernorOptions` | Planet ownership. | Missing planet trusted no-op; unsupported option flags reject. |
| `SetPlanetManualTradeSlots` | Planet ownership. | Missing planet trusted no-op; invalid trade-slot payload rejects. |
| `SetPlanetDefenseTargets` | Planet ownership. | Missing planet trusted no-op; invalid defense-target payload rejects. |
| `SetFleetAssignment` | Ship ownership and can-be-added-to-fleets; no-new-ships gate. | Invalid fleet key/mode/id list/duplicates reject; missing/inactive ships trusted no-op. |
| `MoveFleet` | Any fleet ship can move. | Invalid fleet key, destination/vector payload/order reject; missing/empty fleet trusted no-op. |
| `RenameFleet` | None beyond lookup. | Invalid fleet key/name reject; missing fleet trusted no-op. |
| `RenameShip` | Ship ownership. | Missing/inactive ship trusted no-op; invalid name rejects. |
| `RenamePlanet` | Planet ownership. | Missing planet trusted no-op; invalid name rejects. |
| `GroundTroopOrder` | Planet habitability, troop ownership, launch/move/attack availability, tile-free, range, target attackability. | Invalid order/payload/tile coordinates reject; missing planet/tile/troop/target trusted no-op. |
| `SetFleetIcon` | None beyond lookup. | Invalid fleet key/icon reject; missing fleet trusted no-op. |
| `AutoArrangeFleet` | None beyond lookup. | Invalid fleet key rejects; missing/empty fleet trusted no-op. |
| `LoadFleetPatrol` | None beyond lookup. | Invalid fleet key/name reject; missing/empty fleet or missing patrol trusted no-op. |
| `RenameFleetPatrol` | Duplicate-new-name gate. | Invalid rename payload and no-op rename reject; missing patrol trusted no-op. |
| `DeleteFleetPatrol` | None beyond lookup. | Invalid patrol name rejects; missing patrol trusted no-op. |
| `ClearFleetPatrol` | Active-patrol presence gate. | Invalid fleet key rejects; missing/empty fleet or no active patrol trusted no-op. |
| `CreateFleetPatrol` | Already-has-patrol gate. | Invalid fleet key/waypoint payload reject; missing/empty fleet trusted no-op. |
| `SetFleetLayout` | Design buildability, ship ownership, can-be-added-to-fleets. | Invalid fleet key/layout rejects; missing design/ship/inactive ship trusted no-op. |
| `QueueFleetRequisition` | Fleet ownership, empty-node availability, already-filled/queued node, design buildability. | Invalid fleet key/rush flag/index payload/duplicates reject; missing fleet/node/design trusted no-op; platform/station node design rejects. |
| `QueueDeepSpaceBuild` | `CanBuildStation` and deep-space placement legality. | Invalid payload/position reject; missing design/planet/system trusted no-op; planet/system mismatch rejects. |
| `CancelDeepSpaceBuild` | Goal-present gate. | Invalid payload/position reject; missing goal trusted no-op. |
| `QueuePlanetOrbitalBuild` | Planet ownership and orbital buildability/placement. | Missing planet/design trusted no-op. |
| `BuildCapitalHere` | Planet ownership. | Missing planet trusted no-op. |
| `ApplyColonyBlueprints` | Planet ownership and blueprint content eligibility. | Missing planet trusted no-op; invalid blueprint payload rejects. |
| `ClearColonyBlueprints` | Planet ownership. | Missing planet trusted no-op. |
| `ScrapColonyTile` | Planet ownership and building scrappability. | Invalid coordinates/scrap kind reject; missing planet/tile/building/biosphere or expected-building mismatch trusted no-op. |
| `ShipSpecialOrder` | Ship ownership and explore eligibility. | Invalid order rejects; missing/inactive ship trusted no-op. |
| `ShipLifecycleOrder` | Ship ownership, can-scrap/scuttle, platform/station mode gates. | Invalid order rejects; missing/inactive ship trusted no-op. |
| `SetShipCombatStance` | Ship ownership and role/capability gate. | Invalid stance rejects; missing/inactive ship trusted no-op. |
| `SetShipTradePolicy` | Ship ownership and freighter gate. | Invalid policy/enabled flag reject; missing/inactive ship trusted no-op. |
| `SetShipCarrierPolicy` | Ship ownership and bay/capability gate. | Invalid policy/enabled flag reject; missing/inactive/no-carrier ship trusted no-op. |
| `SetShipTradeRoute` | Ship ownership, freighter gate, treaty/route validity. | Invalid enabled flag rejects; missing/inactive ship or missing planet trusted no-op. |
| `SetShipAreaOfOperation` | Ship ownership and freighter gate. | Invalid action/payload and too-small rectangle reject; missing/inactive ship trusted no-op. |
| `RefitShip` | Ship ownership, refit eligibility, design buildability, refit-yard availability. | Invalid mode/payload reject; missing/inactive ship, missing design, or missing fleet for fleet refit trusted no-op. |
| `AttackShip` | Ship ownership and stale attackability/war relationship. | Missing/inactive source/target trusted no-op; self-target and troop-boarding command shape reject. |
| `ShipTargetOrder` | Ship ownership, stale attackability, friendly target gates, troop transfer/boarding availability, troop capacity. | Invalid payload/queue mode reject; missing/inactive source/target/no-owner target trusted no-op; self-target rejects. |
| `ShipPlanetOrder` | Ship ownership, planet-order eligibility, colony ship/habitable/unowned target, bombard attackability, assault availability, landing legality. | Invalid payload/move order reject; missing/inactive ship or missing planet trusted no-op. |

## Regressions Added

Added to `UnitTests/Multiplayer/Authoritative4XSessionTests.cs`:

- `Authoritative4XTrustedQueueBuild_StaleClientBuildabilityDoesNotRejectAcceptedCommand_Headless`
- `Authoritative4XTrustedQueueBuildingAndTroop_StalePlanetStateDoesNotRejectAcceptedCommands_Headless`
- `Authoritative4XTrustedColonizationGoal_StalePlanetOwnershipDoesNotRejectAcceptedCommand_Headless`
- `Authoritative4XTrustedRushConstruction_StaleClientMoneyDoesNotRejectAcceptedCommand_Headless`
- `Authoritative4XTrustedApply_MissingEntitySafelyNoOpsWithoutCorruption_Headless`

Each stale-state regression first proves normal local apply still rejects the stale passive state,
then applies the host-accepted result through `Authoritative4XClientReplica` and asserts snapshot
digest convergence.

## Verification

- `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false`
  - Passed, with NuGet vulnerability-feed warnings because network access to `https://api.nuget.org/v3/index.json` is unavailable.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug --filter "FullyQualifiedName~Authoritative4XTrusted" -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=true`
  - Passed: 5/5.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug --filter "FullyQualifiedName~UnitTests.Multiplayer.Authoritative4XSessionTests" -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=true`
  - Passed: 187/187.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug --filter "FullyQualifiedName~UnitTests.Multiplayer.AuthoritativeQaScenarioTests" -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=true`
  - Passed: 9/9.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug --filter "FullyQualifiedName~UnitTests.Multiplayer.AuthoritativeSoakTests.Soak_Smoke" -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=true`
  - Passed: 1/1.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug --filter "FullyQualifiedName~Arena" -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=true`
  - Passed: 106/106.

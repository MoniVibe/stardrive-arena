# Joinmisc Desync Report

## Bug A: passive joiner exploration reveal

Root cause:
- System and planet exploration is per-empire state stored on `ExplorableGameObject.ExploredBy` and `SolarSystem.FullyExplored`.
- The host updates those masks from gameplay simulation such as ship presence and exploration behavior.
- Passive clients correctly do not run that gameplay simulation, and the authoritative snapshot did not include those masks, so a joiner replica could have ships in a system while the system stayed hidden locally.

Fix:
- Exposed mask accessors on `ExplorableGameObject` and `SolarSystem`.
- Added authoritative snapshot rows:
  - `XS|SystemId|ExploredByMask|FullyExploredByMask`
  - `XP|PlanetId|ExploredByMask`
- Replayed those rows during initial/passive snapshot apply.
- Registered `XS` and `XP` in the replication manifest and coverage diagnostics as fatal payload-digest state, since passive replicas now replay the host-authored exploration masks before digest comparison.

Repro:
- `QaFogExploration_HostExploredSystemMasksReplayToPassiveJoiner_Headless` creates a host-only joiner scout in an initially hidden system, marks the host system explored for the joiner empire, advances the authoritative session, and asserts the passive client sees that system as explored while a second hidden system remains fogged.

## Bug B: pirate protection renewal popup spam

Root cause:
- `PirateDirectorPayment.RequestPayment()` could raise a renewal encounter every time the expired timer was evaluated.
- There was no per-victim "demand pending" state, so unanswered human renewals were not distinguished from "ready to ask again".
- The follow-up payment activity step also treated the still-peaceful relationship as paid and restarted the goal without a pending guard, allowing the expired renewal condition to be revisited.

Fix:
- Added host-authored, serialized `Pirates.PaymentDemandPending` keyed by victim empire id.
- Renewal requests mark the victim pending before showing the local encounter popup.
- The payment director suppresses new requests while pending and keeps the activity step waiting while the answer is outstanding.
- Answering a pirate encounter clears the pending flag when the dialog reaches an end-transmission outcome.
- Resetting the payment timer also clears pending state.
- Human checks now use `Empire.IsHumanControlled` for authoritative MP state, while the legacy local `EncounterPopup` path remains gated to the local `isPlayer` screen path.

Repro:
- `QaPirateRenewalDemand_RaisesOnceWhilePending_Headless` forces an expired renewal demand, waits until the first renewal popup is raised, advances many commands without answering, and asserts the popup count stays at one and the host pending flag remains set. It also forces authoritative replay and verifies no duplicate renewal appears afterward.

## Verification

- `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false`
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~QaFogExploration_HostExploredSystemMasksReplayToPassiveJoiner_Headless|FullyQualifiedName~QaPirateRenewalDemand_RaisesOnceWhilePending_Headless|FullyQualifiedName~ReplicationManifest_ExecutableDescriptors_EnforceCoverageSymmetry_Headless" -v minimal`
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~Authoritative4XSessionTests|FullyQualifiedName~AuthoritativeQaScenarioTests|FullyQualifiedName~Arena|FullyQualifiedName~Soak_Smoke" -v minimal`

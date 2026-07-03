# DESYNC HOSTSIM REPORT

## Summary

Fixed the authoritative host stall when a full-screen in-game menu covers the universe screen. The fix uses the decoupled scheduling option: authoritative hosts can advance sim time and process sim turns while the universe screen is hidden, and the hidden universe still polls the live authoritative session so host snapshots continue reaching joiners.

## Part A: Pause-Flag Audit

Changed gameplay screens opened from an active universe so they do not set `UState.Paused` in authoritative 4X MP:

- `ShipListScreen`: changed unconditional `toPause: parent` to `PauseTargetFor(parent)`.
- `ChoosePatrolPlan`: changed unconditional `toPause: parent` to `PauseTargetFor(parent)`.
- `BlueprintsScreen`: changed unconditional `toPause: parent` to `PauseTargetFor(parent)`.
- `EmpirePatrolsScreen`: changed unconditional `toPause: parent` to `PauseTargetFor(parent)`.
- `BudgetScreen`: changed unconditional `toPause: screen` to `PauseTargetFor(screen)`.
- `DiplomacyScreen`: base constructor now guards the passed universe pause target.
- `MainDiplomacyScreen`: replaced direct `screen.IsAuthoritative4XMultiplayer ? null : screen` with the standard helper including `Authoritative4XClientContext.IsActive`.
- `RefitToWindow(UniverseScreen, Ship)`: changed unconditional `toPause: parent` to `PauseTargetFor(parent)`.
- `PlanetListScreen`: changed unconditional `toPause: parent` to `PauseTargetFor(parent)`.
- `ExoticSystemsListScreen`: changed unconditional `toPause: parent` to `PauseTargetFor(parent)`.

Already guarded or intentionally pause-free:

- `ShipDesignScreen`, `FleetDesignScreen`, `ResearchScreenNew`, `EmpireManagementScreen`, `GamePlayMenuScreen`, `GenericLoadSaveScreen`, `PopupWindow`, `EspionageScreen`, `InfiltrationScreen`.
- `AutomationWindow`, `DebugInfoScreen`, `ExoticBonusesWindow`, `FreighterUtilizationWindow`, `RelationshipsDiagramScreen`, `RequisitionScreen`, `SearchTechScreen`, ship-design load/save/issues popups, authoritative diplomacy screens, new-game/main-menu/load screens, developer sandbox, mod manager.

Left unchanged as intentional modal/app-level or non-4X surfaces:

- `MessageBoxScreen`, `YouWinScreen`, `YouLoseScreen`.
- Arena screens using `toPause: arena`.

## Part B: Host Sim Decoupling

Chosen mechanism: Option 1, decouple the authoritative host from universe rendering.

- Added `UniverseScreen.IsAuthoritative4XHost`.
- `UniverseScreen.Draw` now routes sim-target advancement through `AdvanceSimulationTargetTimeFromDraw`; authoritative hosts can advance target time even when `IsActive` is false under a popup.
- `UniverseSimulationLoop` still waits on `DrawCompletedEvt` while the universe is visible, preserving the existing draw/sim race boundary.
- When an authoritative host universe is hidden by a full-screen menu, the sim loop uses a 16 ms timed wake and advances target time from a `Stopwatch`-based host clock.
- `ProcessSimulationTurns` now allows stepping when `IsActive || IsAuthoritative4XHost`.
- `ScreenManager` now has a no-op hidden update hook. `UniverseScreen.UpdateHidden` polls authoritative 4X multiplayer while hidden, so host heartbeats/snapshots continue even when normal `UniverseScreen.Update` is skipped.
- The host clock resets while paused/saving, preventing pause time from being consumed as catch-up sim time.

This avoids making full-screen design/research/diplomacy screens popup-like, so there is no input fall-through risk and no forced universe rendering/GPU cost under opaque screens.

## Single-Player Preservation

Single-player pause behavior is preserved:

- All new `PauseTargetFor` helpers return the universe when authoritative MP/client context is inactive.
- The new sim-loop timeout is host-authoritative only.
- A hidden single-player universe still fails the `IsActive || IsAuthoritative4XHost` sim-step gate.
- The expanded `Authoritative4XManagementScreens_DoNotPauseLocalSimulation_Headless` test asserts the SP pause target is still returned for the guarded screens.

## Regression

Added `Authoritative4XHostSimAndSnapshotsAdvanceWhileUniverseCovered_Headless`.

The regression:

- Builds an in-process host/client authoritative fixture over a real local TCP pair.
- Attaches live host/client sessions.
- Forces the host universe to `ScreenState.Hidden`.
- Pumps the same `ProcessSimulationTurns` entry point used by the live sim thread.
- Asserts host `SimTurnId` and `CurrentSimTime` advance while hidden.
- Polls the hidden host via `UpdateHidden` and asserts the passive client receives a newer authoritative snapshot with matching digest.

## Verification

Passed:

- `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false`
- Targeted tests: 2 passed.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false --filter "FullyQualifiedName~Authoritative4X"`: 173 passed.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false --filter "FullyQualifiedName~Arena"`: 106 passed.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false --filter "Name=Soak_Smoke"`: 1 passed.

Notes:

- NuGet vulnerability lookups emitted `NU1900` warnings because the environment cannot reach `https://api.nuget.org/v3/index.json`.
- The shared-core review skill was invoked, but this worktree does not contain `scripts/invoke_shared_core_review.ps1` or `.agents/skills/_shared/scripts/write_skill_receipt.ps1`; no shared-core receipt could be generated locally.

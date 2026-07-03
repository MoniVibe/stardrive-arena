# Host authoritative sim covered-screen fix v2

## Root cause

The previous fix keyed the host sim wake fallback on `!UniverseScreen.Visible`.
That missed the live failure shape:

- Popup/full-screen management screens can take focus from the universe while the
  universe remains `Visible == true`.
- `ScreenManager` makes `GameScreen.IsActive` false when another screen has
  focus, even if the covered universe is still visible under a popup.
- The old `WaitForSimulationWake()` therefore used the blocking
  `DrawCompletedEvt.WaitOne()` path whenever `Visible` stayed true. If no
  universe draw followed, the sim thread could block indefinitely.
- A second gate also survived the first fix: `ProcessTurnEmpires()` still
  required `IsActive`, so a visible popup could let `SimTurnId` and target time
  move while empire turns and `StarDate` stayed frozen.

## Fix

`UniverseScreen.UpdateGame.cs` now treats authoritative host coverage as an
inactive-universe condition:

- `WaitForSimulationWake()` uses `IsActive` instead of `Visible` for the host
  draw wait. Active hosts still block on draw as before. Inactive hosts use the
  16 ms timeout path; if a draw arrives, the event wins, otherwise the host
  advances target sim time from the wall clock and wakes the sim loop.
- Gameplay simulation gates now use `CanRunGameplaySimulation`
  (`IsActive || IsAuthoritative4XHost`) for the outer sim loop, empire turns,
  and post-empire updates. This is what keeps `StarDate`, AI, economy, and
  authoritative snapshots moving when the host has a menu/dialogue on top.
- Passive clients keep the existing early return path and do not start running
  local gameplay simulation.
- Single-player still requires `IsActive`, so menus/dialogue still pause local
  single-player simulation.

## Dialogue and notification pause

Added `UniverseScreen.CanLocalUiPauseSimulation` and
`UniverseScreen.PauseTargetForLocalUi()` as the central pause guard:

- `PopupWindow` and `MessageBoxScreen` now route their pause target through the
  guard.
- `NotificationManager` no longer sets `UState.Paused = true` for
  pause-on-notification when the universe is authoritative MP.
- `Notification` right-click dismissal no longer forces `UState.Paused = false`
  in authoritative MP, so a deliberate host session pause is not accidentally
  cleared by notification UI.

## Live diagnostic

When the host fallback fires without a draw, telemetry emits a throttled
`VIEW_PERF` detail containing:

`HOST_COVERED_SIM fallback=true drawCompleted=false visible=... isActive=... screenState=... topScreen=... paused=... starDate=... simTurn=... currentSim=... targetSim=... simFps=...`

For live QA, open ship design, fleet design, ShipList/ships-in-empire, and
pirate/story dialogue on the host. The expected result is:

- `StarDate` continues to advance.
- Joiner snapshots continue to arrive without planet lag/jump on resume.
- If the universe is not drawing, telemetry should show
  `HOST_COVERED_SIM ... drawCompleted=false`.
- If the universe is visible under a popup, `visible=true isActive=false` is
  expected; the sim should still advance.

## Verification

Build:

- `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false` passed.

Focused regressions:

- `Authoritative4XHostSimAndSnapshotsAdvanceWhileUniverseCovered_Headless` now
  reproduces the live visible-but-inactive popup state (`Visible == true`,
  `IsActive == false`), exercises the no-draw timeout path, and asserts
  `SimTurnId`, `CurrentSimTime`, `StarDate`, and passive-client snapshots
  advance. It also keeps the hidden full-screen coverage check.
- `Authoritative4XNotificationsAndMessageBoxes_DoNotPauseHostSimulation_Headless`
  proves single-player pause-on-notification still pauses, while authoritative
  host notifications and message boxes do not locally pause the sim.

Required suites:

- `Authoritative4X`: 175/175 passed.
- `Arena`: 106/106 passed.
- `Soak_Smoke`: 1/1 passed.


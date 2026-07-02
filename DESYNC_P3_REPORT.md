# DESYNC P3 Report

## Summary

Implemented the passive-client replicated-state mutation tripwire for Phase P3.

- Added `AuthoritativeMutationGuard` with debug-only passive-client checks and sanctioned replay/accepted-command scopes.
- Routed replay and accepted-command mutation funnels through guarded setters for planet runtime controls, troop runtime membership/state, ship transform/orientation, diplomacy/first-contact state, and empire automation flags.
- Made `Authoritative4XClientReplica` local-mutation digest checking default-on for the headless/in-process test substrate.
- Added planted-leak regressions for all five requested families and sanctioned-path coverage for replay apply plus accepted-command scope.

The guard compiles out of Release through `[Conditional("DEBUG")]` guard calls and `#if DEBUG` scope usage. `Release` build is green.

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

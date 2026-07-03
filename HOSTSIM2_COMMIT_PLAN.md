# HOSTSIM2 commit plan

Branch: `fix/hostsim2`

Scope:

- Host-only authoritative sim wake fix for covered/inactive universe screens.
- Host-only empire-turn gate fix so `StarDate` advances while menus/dialogue own focus.
- Local UI pause guard for notifications, popups, and message boxes in authoritative MP.
- Regression coverage for visible-but-inactive covered screens, hidden full-screen screens, notification pause, and passive snapshot delivery.

Do not include:

- No push.
- No unrelated refactors.
- No generated `sim-output` artifacts.

Verification already run:

- `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false`
- Focused covered-screen and notification regressions: 2/2 passed.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~Authoritative4X"`: 175/175 passed.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~Arena"`: 106/106 passed.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~Soak_Smoke"`: 1/1 passed.


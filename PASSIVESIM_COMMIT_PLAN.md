# PASSIVESIM commit plan

Branch: `fix/passivesim`

## Scope

- Make passive authoritative view presentation-only.
- Keep passive clients out of GC/economy/construction/fleet/freighter gameplay simulation.
- Add exact GC and fleet replay repairs for stale/missing dynamic host state.
- Extend debug mutation guards for the new replicated-state families.
- Add focused Authoritative4X regression/soak coverage.
- Add `DESYNC_PASSIVESIM_REPORT.md`.

## Verification completed

Command:

`dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false`

Result: passed, 0 errors.

Warnings: `NU1900` package vulnerability metadata fetch failed because the
sandbox cannot reach NuGet.

## Tests attempted but blocked

Focused `dotnet test` filter for the new/changed Authoritative4X tests was
attempted.

- `--no-build` could not run because the restricted build omits
  `testhost.runtimeconfig.json`.
- normal test build was blocked by sandbox output permissions while generating
  `game\SDUtils.deps.json`.

## Suggested commit message

`Fix passive authoritative client gameplay sim leaks`

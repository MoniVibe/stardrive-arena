# Passive Planet Orbit Presentation Fix

## Approach

Implemented host-authored, non-fatal planet transform replay with a new `PX` row:

- `PX|PlanetId|Tick|OrbitalAngle|Position.X|Position.Y`
- Emitted per planet by `AuthoritativeStateSnapshot.EmitPlanetTransformRows`.
- Applied by `AuthoritativeStateSnapshot.ApplyPlanetTransformLine`.
- Registered in `AuthoritativeReplicationManifest` as `AuthoritativeReplicationDigestPolicy.Transform`.

I chose the transform/presentation row instead of deterministic local passive advance because current host planet orbit advancement is not a pure tick function: `SolarSystemBody.UpdatePosition` is gated by pause state plus `PosUpdateTimer` or host `System.InFrustum`. Replaying the host's exact angle and position keeps the passive joiner visually locked to the host without depending on host-only camera/frustum state.

## Fatal Digest Safety

`PX` is excluded from the fatal `SyncDigest` by manifest policy. It participates only in `TransformDigest`, matching the existing ship `SX` presentation model.

The replay runs inside the existing authoritative snapshot apply scope, so passive clients do not run gameplay simulation to move planets. The row applies exact float bits for `OrbitalAngle` and `Position.X/Y`, avoiding recomputation drift.

`UniverseStateHash` and the fatal payload projection still exclude planet orbital angle and position. A stale passive planet presentation can therefore be detected as a transform mismatch, but cannot become a fatal sync/resync row.

## Regression

Added `Authoritative4XClientReplica_ReplaysHostPlanetTransformWithoutFatalDigest_Headless`:

- Advances the host for 420 sim ticks so the planet orbit moves while the passive client stays stale.
- Captures a host snapshot containing `PX`.
- Applies the snapshot to the passive client.
- Asserts passive `OrbitalAngle` and `Position.X/Y` match the host bit-for-bit.
- Asserts `SyncDigest` matches after replay.
- Re-stales the passive planet presentation and verifies `SyncDigest` remains equal while `TransformDigest` diverges.

Updated the existing entity-order orbit test to assert host/client ship and planet presentation consistency after `PX` replay instead of measuring against the passive client's former stale planet position.

## Verification

Commands were run with `STARDRIVE_TEST_APPDATA` redirected to `.build\AppData` and `OutDir` redirected to `UnitTests\bin\Debug` because this sandbox blocks the default junctioned `game\runtimes` output and the default external `TestResults` app-data path.

- `dotnet test UnitTests\SDUnitTests.csproj -c Debug -p:Platform=x64 --no-restore -p:OutDir=C:\dev\stardrive\StarDrive-planetorbit\UnitTests\bin\Debug\ --filter "FullyQualifiedName~Authoritative4XClientReplica_ReplaysHostPlanetTransformWithoutFatalDigest_Headless|FullyQualifiedName~ReplicationCoverageDiagnosticTests" -v minimal` passed 4/4.
- `dotnet test UnitTests\SDUnitTests.csproj -c Debug -p:Platform=x64 --no-build -p:OutDir=C:\dev\stardrive\StarDrive-planetorbit\UnitTests\bin\Debug\ --filter "FullyQualifiedName~Authoritative4X" -v minimal` passed 181/181.
- `dotnet test UnitTests\SDUnitTests.csproj -c Debug -p:Platform=x64 --no-build -p:OutDir=C:\dev\stardrive\StarDrive-planetorbit\UnitTests\bin\Debug\ --filter "FullyQualifiedName~AuthoritativeQaScenarioTests" -v minimal` passed 9/9.
- `dotnet test UnitTests\SDUnitTests.csproj -c Debug -p:Platform=x64 --no-build -p:OutDir=C:\dev\stardrive\StarDrive-planetorbit\UnitTests\bin\Debug\ --filter "FullyQualifiedName~Arena" -v minimal` passed 106/106.
- `dotnet test UnitTests\SDUnitTests.csproj -c Debug -p:Platform=x64 --no-build -p:OutDir=C:\dev\stardrive\StarDrive-planetorbit\UnitTests\bin\Debug\ --filter "FullyQualifiedName~AuthoritativeSoakTests.Soak_Smoke" -v minimal` passed 1/1.
- `dotnet build StarDrive.csproj -c Debug -p:Platform=x64 --no-restore -p:OutDir=C:\dev\stardrive\StarDrive-planetorbit\UnitTests\bin\Debug\ -v minimal` passed.
- `dotnet build UnitTests\SDUnitTests.csproj -c Debug -p:Platform=x64 --no-restore -p:OutDir=C:\dev\stardrive\StarDrive-planetorbit\UnitTests\bin\Debug\ -v minimal` passed.

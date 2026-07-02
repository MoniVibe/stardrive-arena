# P4 Commit Plan

Git staging/commit was blocked by sandbox permissions:

`Unable to create 'C:/dev/stardrive/StarDrive-main/.git/worktrees/StarDrive-desync-p4/index.lock': Permission denied`

## Commit 1

Message: `Add P4 seeded authoritative soak harness`

Files:

- `SDUtils/Dir.cs`
- `Ship_Game/Multiplayer/Authoritative/Authoritative4XSession.cs`
- `Ship_Game/Utils/Log.cs`
- `UnitTests/SDUnitTests.csproj`
- `UnitTests/StarDriveTestContext.cs`
- `UnitTests/Multiplayer/AuthoritativeSoakHarness.cs`
- `UnitTests/Multiplayer/AuthoritativeSoakTests.cs`
- `DESYNC_P4_REPORT.md`

Summary:

- Adds the seeded in-process host/client command fuzzer and replay harness.
- Adds `Soak_Smoke`, `Soak_Nightly`, and `Soak_Replay` entry points.
- Adds deterministic command-log persistence, 14-lane localization, hazard injection, and KnownGap allowlisting.
- Adds DEBUG/test-only seams for session-control and sandbox-safe unit-test AppData/log isolation.
- Documents the discovered non-allowlisted `E.EmpireRuntime` research queue divergence.

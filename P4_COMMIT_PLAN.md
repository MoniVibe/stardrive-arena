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

## Commit 2

Message: `Fix P4 research queue replay exact-set`

Files:

- `Ship_Game/EmpireResearch.cs`
- `Ship_Game/Multiplayer/Authoritative/Authoritative4XSession.cs`
- `UnitTests/Multiplayer/Authoritative4XSessionTests.cs`
- `DESYNC_P4_REPORT.md`
- `P4_COMMIT_PLAN.md`

Summary:

- Adds `Empire.Research.SetQueueExact(...)` to rebuild the queue and queued-module index from host-authored tech ids without prerequisite expansion.
- Changes `E.EmpireRuntime` replay to exact-set the research queue from the payload instead of calling `AddTechToQueue`.
- Leaves host-side `QueueResearch` command gameplay unchanged, so authoritative prerequisite expansion still happens on real host commands.
- Adds `Authoritative4XClientReplica_ReplaysResearchQueueExactWithoutPrereqExpansion_Headless` for P4-NG-001.
- Verified with `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false`.

## Commit 3

Message: `Fix P4 diplomacy replay treaty-break crash`

Files:

- `Ship_Game/Empire_Relationship.cs`
- `Ship_Game/Multiplayer/Authoritative/Authoritative4XSession.cs`
- `DESYNC_P4_REPORT.md`
- `P4_COMMIT_PLAN.md`

Summary:

- Classifies P4-NG-002 as passive-client diplomacy replay side effects, with a direct headless-authority notification hardening.
- Changes passive authoritative clients to skip local `DiplomacyProposal`/`DiplomacyResponse` gameplay application after an accepted result; relationship/treaty/war state is applied from the authoritative snapshot rows instead.
- Keeps host command application unchanged, so real diplomacy commands still validate and mutate on the authority.
- Makes treaty-break stock notifications null-safe for generated/headless universes where `UniverseScreen.LoadContent` has not created a `NotificationManager`.
- Verified with `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false --no-restore` (`0` warnings, `0` errors).
- Orchestrator should verify `Soak_Smoke` green, focused `Authoritative4X` green, and full `Arena` green before committing.

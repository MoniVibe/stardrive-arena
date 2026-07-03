# QA Harness Commit Plan

Do not commit or push from this lane. Suggested commit scope for the orchestrator:

- `UnitTests/Multiplayer/AuthoritativeQaScenarioTests.cs`
- `UnitTests/SDUnitTests.csproj`
- `Ship_Game/Multiplayer/Authoritative/Authoritative4XSession.cs`
- `Ship_Game/Multiplayer/Authoritative/AuthoritativeStateSnapshot.Emit.cs`
- `Ship_Game/Multiplayer/Authoritative/AuthoritativeReplicationManifest.cs`
- `Ship_Game/Universe/SolarBodies/Planet/Planet.cs`
- `Ship_Game/Combat.cs`
- `Ship_Game/Pirates.cs`
- `DESYNC_QA_HARNESS_REPORT.md`
- `QAHARNESS_COMMIT_PLAN.md`

Verification already run:

- Build: passed with sandbox NuGet `NU1900` warnings.
- New QA scenario suite: passed 6/6.
- ArenaMultiplayerLockstep: passed 13/13.
- AuthoritativeSoakTests.Soak_Smoke: passed 1/1.
- Authoritative4XSessionTests: not green; 8 failures remain and are documented in `DESYNC_QA_HARNESS_REPORT.md`.

Recommended next action before commit:

- Decide whether to include this partial lane with the new QA suite green, or first address the remaining Authoritative4XSessionTests failures so the requested Authoritative4X gate is fully green.

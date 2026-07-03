# P3 Commit Plan

Git staging/commit is blocked by sandbox permissions because this worktree's git index is stored under:

`C:\dev\stardrive\StarDrive-main\.git\worktrees\StarDrive-desync-p3`

Run these commands from `C:\dev\stardrive\StarDrive-desync-p3`:

```powershell
git add DESYNC_P3_REPORT.md P3_COMMIT_PLAN.md `
  Ship_Game\GameScreens\Universe\ResearchScreenNew.cs `
  Ship_Game\Multiplayer\Authoritative\Authoritative4XClientContext.cs `
  Ship_Game\Multiplayer\Authoritative\Authoritative4XLiveSession.cs `
  Ship_Game\Multiplayer\Authoritative\Authoritative4XSessionSave.cs `
  Ship_Game\Multiplayer\Authoritative\AuthoritativeMutationGuard.cs `
  Ship_Game\Universe\SolarBodies\TroopManager.cs `
  Ship_Game\Universe\UniverseScreen\UniverseScreen.AuthoritativeMultiplayer.cs `
  UnitTests\StarDriveTest.cs `
  UnitTests\StarDriveTestContext.cs `
  UnitTests\Multiplayer\Authoritative4XSessionTests.cs

git commit -m "Fix passive mutation guard P3 follow-up"
```

Do not push from this lane unless explicitly requested.

## Follow-Up Scope

- Fixed the same-process live loopback guard misfire: a passive client UI context no longer makes host-attached live universes look like passive clients to `ShouldTripMutationGuard`.
- Split passive troop simulation gating from broad mutation-guard detection so host save adoption and authoritative save state application are not blocked by an active passive UI context.
- Gated passive-client troop combat timer updates in Debug only for universes attached as passive authoritative clients, so generated live attach/heartbeat view refresh cannot mutate replicated troop runtime locally.
- Fixed cross-test authoritative context contamination without relaxing stale-replica blocking: disposed UI contexts are unlinked from the active stack instead of being resurrected through `Previous`, direct live-session disposal drops only the UI context so attached authoritative universes remain mutation-blocked, and DEBUG test cleanup resets authoritative context/guard statics.
- Made the research-screen queue refresh null-safe so context-missing/headless guard invocations block cleanly before a UI queue component exists.
- Made authoritative save adoption's state-application bypass an explicit `try/finally` scope so the bypass depth is disposed on exception paths.
- Reworked guard-active fixture setup in the listed P3 failures so tests no longer mutate passive replicas after an accepted baseline snapshot.
- Recorded the required ten-test disposition table in `DESYNC_P3_REPORT.md`.

## Verification

Passed:

- `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false`

Not run in this sandbox:

- Authoritative4X runtime tests; the orchestrator will run them.

# P2 Commit Plan

Git staging/commit is blocked in this sandbox because the worktree git metadata lives under `C:\dev\stardrive\StarDrive-main\.git\worktrees\StarDrive-desync-p2`, outside the writable roots.

This follow-up closes the four orchestrator failures after the main P2 implementation and addresses the remaining P2 lane red in `Authoritative4XSnapshot_HostOnlyDiagnosticRowsDoNotTriggerFatalResync_Headless`:

- `D|`, `F|`, and `S|` mixed physical rows keep their wire format but now project only replayed/fatal columns into `SyncDigest`.
- `S.PolicyDiagnostics` documents design-derived S capability columns as host-only diagnostics.
- The `S.PolicyFields` regression fixture now updates object lists and verifies the freighter `S|` row exists before asserting policy drift.
- Legacy BP/FP command tests now preserve command-routing/state assertions while treating client-only BP/FP row drift as non-fatal.
- `firstDiff` diagnostics now compare the projected fatal payload rows that `SyncDigest` hashes, while still reporting raw authority/client lines for context.
- `D|` player-design replay now removes stale same-name buildable design references before adding the authoritative registered design, preventing host-only design-field drift from becoming duplicate/stale fatal registration state after replay.

Make these commits locally from `C:\dev\stardrive\StarDrive-desync-p2` on branch `desync/p2-lane`:

## Commit 1

Message:

```text
Implement P2 replay digest dispositions
```

Files:

```text
Ship_Game/Commands/Goals/DeepSpaceBuildGoal.cs
Ship_Game/GameScreens/Arena/ArenaMultiplayerSession.cs
Ship_Game/Multiplayer/Authoritative/Authoritative4XSession.cs
Ship_Game/Multiplayer/Authoritative/Authoritative4XLiveTelemetry.cs
Ship_Game/Multiplayer/Authoritative/AuthoritativeReplicationManifest.cs
```

## Commit 2

Message:

```text
Add P2 desync regression coverage
```

Files:

```text
UnitTests/Multiplayer/Authoritative4XSessionTests.cs
UnitTests/Multiplayer/ReplicationCoverageDiagnosticTests.cs
DESYNC_P2_REPORT.md
P2_COMMIT_PLAN.md
```

Do not stage `codex-build-out/`; it is a sandbox scratch output directory.

## Verification Notes

- `git diff --check` passed.
- Compile verification passed:

```text
dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false
```

- Runtime tests were not attempted in this follow-up per orchestrator instruction; the orchestrator should rerun `Authoritative4XSnapshot_HostOnlyDiagnosticRowsDoNotTriggerFatalResync_Headless`.
- Historical sandbox note: the build without `-p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false` can fail at `GenerateDepsFile` because `game\SDUtils.deps.json` is not writable.

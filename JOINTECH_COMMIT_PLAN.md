# JOINTECH Commit Plan

## Scope

- Fix authoritative joiner human empire initialization so default racial trait techs and human buildability are represented in the host state.
- Fix passive-client authoritative tech replay so exact unlocked-tech rows also rebuild derived ship/building build caches.
- Add a headless regression proving joiner starting tech exact-set replication and Subspace Projector buildability after stale-cache replay.

## Files

- `Ship_Game/Multiplayer/Authoritative/Authoritative4XLobby.cs`
- `Ship_Game/Multiplayer/Authoritative/Authoritative4XSession.cs`
- `Ship_Game/Multiplayer/Authoritative/Authoritative4XSessionSave.cs`
- `Ship_Game/Empire.cs`
- `Ship_Game/Empire_ShipsWeCanBuild.cs`
- `Ship_Game/Ships/ShipDesign_Stats.cs`
- `UnitTests/Multiplayer/AuthoritativeQaScenarioTests.cs`
- `DESYNC_JOINTECH_REPORT.md`
- `JOINTECH_COMMIT_PLAN.md`

## Validation Before Commit

```powershell
dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter QaJoinerTechState_StartingTechsAndSubspaceBuildabilityReplay_Headless
dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter QaTechGatedDesign_HammerheadCostAndModuleAvailabilityReplayFromHostRows_Headless
dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter Authoritative4XSnapshot_AppliesUnlockedTechRowsBeforeDigestCompare_Headless
dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter Authoritative4XSnapshot_RemovesStaleClientOnlyTechRowsBeforeDigestCompare_Headless
dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false
```

All listed commands have passed sequentially in this worktree.

## Suggested Commit Message

```text
Fix joiner tech replay and human build caches
```

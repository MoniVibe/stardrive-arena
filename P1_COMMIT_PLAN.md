# P1 Commit Plan Fallback

Git commits were requested, but local commit creation is blocked in this sandbox because git cannot create
`C:/dev/stardrive/StarDrive-main/.git/worktrees/StarDrive-desync-p1/index.lock` (`Permission denied`).
The logical commit boundaries below are the commits that should be made by the orchestrator.

## Commit 1: Add executable replication descriptor metadata

Files:
- `Ship_Game/Multiplayer/Authoritative/AuthoritativeReplicationManifest.cs`
- `UnitTests/Multiplayer/Authoritative4XSessionTests.cs`

Summary:
- Replaced the descriptive manifest row shape with `ReplicatedRowDescriptor`.
- Added digest policy, field group, P0 mapping, payload/applied field lists, variant matching, and KnownGap metadata.
- Split G/F/S/D descriptor metadata at the variant/field-group level called out by P0 while preserving row-prefix lookups for existing first-diff labels.

Verification:
- `dotnet build StarDrive.csproj -c Debug -p:Platform=x64 --no-restore` passed after `NUGET_PACKAGES` was pointed at the local user package cache.
- `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-restore` passed with the same cache setting.

## Commit 2: Drive payload emission from replication descriptors

Files:
- `Ship_Game/Multiplayer/Authoritative/Authoritative4XSession.cs`
- `Ship_Game/Multiplayer/Authoritative/AuthoritativeReplicationManifest.cs`
- `Ship_Game/Multiplayer/Authoritative/AuthoritativeStateSnapshot.Emit.cs`
- `StarDrive.csproj`

Summary:
- Made `AuthoritativeStateSnapshot` partial and moved the existing payload append blocks into explicit descriptor emit delegates.
- Added descriptor emit stages for pre-scoped rows, per-empire rows, per-planet rows, and post-scoped ship rows.
- Replaced `BuildPayload` with a manifest-stage walk while preserving the legacy wire row order:
  `V,SD,E,U,D,R`, then per-empire `G/FP/F`, then per-planet `P/BP/T/GT/GC/Q`, then `SC/S/SX/SV/ST`.

Verification:
- `dotnet build StarDrive.csproj -c Debug -p:Platform=x64 --no-restore` passed with `NUGET_PACKAGES=$env:USERPROFILE\.nuget\packages`.
- `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-restore` passed with the same cache setting.

## Commit 3: Drive replay dispatch and digest filtering from descriptors

Files:
- `Ship_Game/Multiplayer/Authoritative/Authoritative4XSession.cs`
- `Ship_Game/Multiplayer/Authoritative/AuthoritativeReplicationManifest.cs`
- `Ship_Game/Multiplayer/Authoritative/AuthoritativeStateSnapshot.Apply.cs`
- `StarDrive.csproj`

Summary:
- Replaced the top-level replay prefix dispatcher with descriptor-driven initial line replay plus ordered batch replay stages.
- Bound replay descriptors to the existing appliers; KnownGap descriptors remain explicitly unapplied.
- Changed `SyncDigest`/`TransformDigest` payload filtering to use descriptor `DigestPolicy` instead of hard-coded `SX` prefix filtering.

Verification:
- `dotnet build StarDrive.csproj -c Debug -p:Platform=x64 --no-restore` passed with `NUGET_PACKAGES=$env:USERPROFILE\.nuget\packages`.
- `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-restore` passed with the same cache setting.

## Commit 4: Add descriptor coverage symmetry guard

Files:
- `UnitTests/Multiplayer/ReplicationCoverageDiagnosticTests.cs`

Summary:
- Added a metadata/source enforcing test for descriptor symmetry:
  emitted prefixes == declared prefixes, covered-or-known-gap prefixes == declared prefixes.
- Asserted every Fatal descriptor has an apply delegate unless it is an explicit KnownGap.
- Asserted Transform descriptors have replay coverage.
- Locked the expected KnownGap descriptor IDs for P2 debt.
- Added source checks that `BuildPayload`, top-level replay dispatch, and digest filtering walk descriptor metadata instead of raw prefix branches.
- Updated the old diagnostic to use descriptor-emitted prefixes now that raw append blocks moved out of `BuildPayload`.

Verification:
- `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-restore` passed with `NUGET_PACKAGES=$env:USERPROFILE\.nuget\packages`.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~ReplicationManifest"` selected 3 tests but failed before test methods at assembly initialization because `GlobalStats.LoadConfig()` cannot write `C:\Users\shonh\AppData\Roaming\StarDrive\StarDrive.user.config` in this sandbox.

## Commit 5: Resolve first-diff labels through line descriptors

Files:
- `Ship_Game/Multiplayer/Authoritative/AuthoritativeReplicationManifest.cs`

Summary:
- Changed `DescribeLine` to resolve the descriptor from the concrete payload line, so variant descriptors such as `G.Refit` and `G.FleetRequisition` report their descriptor apply mode instead of the first row for the prefix.

Verification:
- `dotnet build StarDrive.csproj -c Debug -p:Platform=x64 --no-restore` passed with `NUGET_PACKAGES=$env:USERPROFILE\.nuget\packages`.
- `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-restore` passed with the same cache setting.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~Authoritative4X"` selected 163 tests; all 163 failed before test methods at assembly initialization because `GlobalStats.LoadConfig()` cannot write `C:\Users\shonh\AppData\Roaming\StarDrive\StarDrive.user.config`.
- `dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~Arena"` selected 106 tests; all 106 failed before test methods for the same AppData write limitation.

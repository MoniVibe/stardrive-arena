# Planet Orbit Commit Plan

## Scope

Commit the passive-client planet orbit presentation fix only.

## Files

- `Ship_Game/Multiplayer/Authoritative/AuthoritativeStateSnapshot.Emit.cs`
- `Ship_Game/Multiplayer/Authoritative/AuthoritativeReplicationManifest.cs`
- `Ship_Game/Multiplayer/Authoritative/Authoritative4XSession.cs`
- `UnitTests/Multiplayer/Authoritative4XSessionTests.cs`
- `UnitTests/Multiplayer/ReplicationCoverageDiagnosticTests.cs`
- `DESYNC_PLANETORBIT_REPORT.md`
- `PLANETORBIT_COMMIT_PLAN.md`

## Commit Message

```
Replicate passive planet orbit presentation
```

## Notes

- Do not push from this lane.
- `PX` is transform/presentation-only and excluded from fatal `SyncDigest`.
- Verification is recorded in `DESYNC_PLANETORBIT_REPORT.md`.

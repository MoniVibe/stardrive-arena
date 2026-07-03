# Desync Meteor Report

## Predicate

Transient environment ships are excluded from authoritative 4X replication when:

- `Ship.IsTransientEnvironment` is set, or
- the ship is an existing semantic meteor (`Ship.IsMeteor`) owned by `UniverseState.Unknown`.

`IsTransientEnvironment` is a persisted ship flag set by `RandomEventManager.CreateMeteors` immediately after meteor creation and before the meteor AI goal is assigned. The `IsMeteor && Unknown` fallback keeps already-materialized or saved meteor ships out of the contract even if they predate the new flag.

Unknown loyalty alone is not used. Other Unknown-loyalty content exists, including dimensional-prison platforms and event-spawned pirate fallback ships, and those must remain replicated unless they are meteor/transient ships.

## Filter Points

The shared filter is `AuthoritativeStateSnapshot.IsTransientEnvironmentShipForReplication`.

`AuthoritativeStateSnapshot.SnapshotShips` now drops matching ships before any ship row emitter runs. All ship rows are emitted from that shared snapshot, so the exclusion applies symmetrically to:

- `SC` ship presence
- `S` ship runtime
- `SX` ship transform
- `SV` ship visibility
- `ST` ship troops, if a transient ship ever had any

`Capture` builds `SyncDigest` and `TransformDigest` from the filtered payload, so host emit, client capture, pre-apply mutation checks, fatal digest, and transform digest all use the same predicate.

## Regression

Added `Authoritative4XClientReplica_ExcludesTransientEnvironmentMeteorsFromSyncContract_Headless`.

The test starts from an accepted clean snapshot, then creates:

- a host-only transient Unknown meteor,
- a stale client-only transient Unknown meteor, and
- a normal Unknown-loyalty `Vulcan Scout`.

It asserts the host meteor has no `SC`, `S`, `SX`, or `SV` rows; the stale client meteor does not trip the pre-apply mutation guard; both digests still match after apply; and the normal Unknown ship still appears in `SC` and materializes on the client.

## Gameplay Impact

Meteor planet effects are unchanged. The host still creates and simulates meteor ships, and planet damage/crater/building effects remain host-owned gameplay state. Those results continue to replicate through planet rows such as `P` and tile/building rows. Only the short-lived flying meteor ship visuals are removed from the fatal replication contract.

## Verification

- `dotnet test UnitTests\SDUnitTests.csproj -c Debug -p:Platform=x64 --no-restore --filter "FullyQualifiedName~Authoritative4X"` passed 172/172.
- `dotnet test UnitTests\SDUnitTests.csproj -c Debug -p:Platform=x64 --no-restore --filter "FullyQualifiedName~Arena"` passed 106/106.
- `dotnet test UnitTests\SDUnitTests.csproj -c Debug -p:Platform=x64 --no-restore --filter "FullyQualifiedName~Soak_Smoke"` passed 1/1.

The runs emitted `NU1900` warnings because NuGet vulnerability data could not be fetched from `https://api.nuget.org/v3/index.json` under restricted network access.

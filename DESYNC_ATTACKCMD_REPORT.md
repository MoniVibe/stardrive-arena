# DESYNC_ATTACKCMD Report

## Reject Cause

The passive client could still run the authority accept/reject gate for a command the host had already accepted.

The concrete stale state is:

- host accepts `AttackShip` / ship-target attack from human empire B against human empire A;
- host auto-declares war through `AuthoritativeDiplomacyManager.TryDeclareWarForHostileHumanAction`;
- passive client can already have `Relationship.AtWar=true` but stale `Relationship.CanAttack=false`;
- on local re-apply, `TryDeclareWarForHostileHumanAction` sees `IsAtWarWith=true` and does not run `ApplyDeclareWar`;
- the final `empire.IsEmpireAttackable(targetEmpire, target)` still returns false because `Ship.IsAttackable` depends on `rel.CanAttack`;
- `Authoritative4XClientReplica.ApplyAuthoritativeResult` threw `Client replica rejected accepted command ...`.

The new regression asserts the client-side human registry contains both empires before the repro:
`AuthoritativeHumanPlayers.IsHumanVsHuman(client.Enemy, client.Player) == true`.

## Fix Layer

Fix is in the passive accepted-command replay path, not the host validator.

- `Authoritative4XClientReplica.ApplyAuthoritativeResult` now uses `Authoritative4XCommandApplicator.ApplyTrustedHostAccepted` for host-accepted commands.
- If a trusted local effect apply still cannot run because local objects are missing, it logs a warning and continues to authoritative snapshot repair instead of throwing.
- The trusted applicator path preserves the existing object/ownership sanity checks, but skips the hostile ship-target authorization gate for host-accepted `AttackShip` and `ShipTargetOrder` attack/board effects.
- When trusted replay sees an already-at-war relationship with stale attackability, it immediately sets `CanAttack=true` and `IsHostile=true` on both relationship directions.
- The existing applicator debug mutation scope still wraps accepted-command effect application.

## Regression

Added:

- `Authoritative4XHumanAttackShip_HostAcceptedReplayCannotBeClientRejected_Headless`

The test:

- builds an in-process authority and passive client;
- has joiner/human empire B attack host/human empire A's ship with no prior war;
- asserts the host accepts and auto-declares war;
- proves the old local validation gate rejects the stale client state (`AtWar=true`, `CanAttack=false`);
- suppresses passive `R|` relationship replay while preserving the canonical host digest to recreate the crash timing window;
- applies the host-accepted result through the passive replica without throwing;
- asserts host/client final sync digest and transform digest match;
- asserts both client relationship directions are at war and attackable;
- asserts the passive joiner's ship has the accepted attack target.

## Verification

Requested build command was attempted:

`dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false`

Result: source compilation passed after the namespace fix, but the command is blocked in this sandbox by `game\runtimes`, which is a junction to `C:\dev\stardrive\StarDrive-main\game\runtimes` outside the writable roots. MSBuild fails copying `libuv` runtime files with `MSB3021 Access to the path ... is denied`.

Validated with repo-local output instead:

`dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:OutputPath=C:\dev\stardrive\StarDrive-attackcmd\.build-output\ -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false`

Result: passed. Only `NU1900` warnings from blocked NuGet vulnerability lookups.

Targeted tests:

`dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:OutputPath=C:\dev\stardrive\StarDrive-attackcmd\UnitTests\bin\Debug\ --filter "FullyQualifiedName~Authoritative4XHumanShipAttack_AutoDeclaresWarAndSyncs_Headless|FullyQualifiedName~Authoritative4XHumanAttackShip_HostAcceptedReplayCannotBeClientRejected_Headless"`

Result: passed, 2/2.

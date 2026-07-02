# StarDrive MP Desync P4 Report

## NEW Non-Allowlisted Divergences

### P4-NG-002: Diplomacy treaty-break side effects during passive replay crash on null notifications

- Status: **FIXED**.
- Trigger: `Soak_Smoke` after the P4-NG-001 exact research-queue fix, when the diplomacy fuzzer generated treaty/declare-war commands after tick 10.
- Crash: `System.NullReferenceException` in `Empire.AddTreatyBreakNotification` (`Empire_Relationship.cs`) via `Empire.BreakTreatyWith`, reached from authoritative human diplomacy declare-war treaty breaking.
- Bucket: **(a) passive-client replay side effect**, with a direct headless-authority null hardening. Evidence:
  - Fuzzer submission enters `Authoritative4XInProcessMultiClientSession.SubmitFromClient`.
  - The authority legitimately applies the command through `Authority.Process` -> `Authoritative4XCommandApplicator.Apply` -> `AuthoritativeDiplomacyManager.ApplyProposal/ApplyResponse` -> `ApplyDeclareWar` -> `proposer.BreakAllTreatiesWith(...)`.
  - The same accepted command was then broadcast to every passive client and replayed again by `Authoritative4XClientReplica.ApplyAuthoritativeResult` through the local `Authoritative4XCommandApplicator`, even though the authoritative snapshot had already supplied the `R|` relationship rows.
  - Generated headless universes do not call `UniverseScreen.LoadContent`, so `Universe.NotificationManager` can be null; real loaded clients create it in `UniverseScreen.LoadContent`, but headless authority/replay paths must not crash when it is absent.
- Fix mechanism: passive replicas now treat `DiplomacyProposal` and `DiplomacyResponse` as snapshot-owned commands. They pre-apply relationship rows, skip the local gameplay applicator for diplomacy, and then apply the full authoritative payload, so treaty/war fields come from `R|` rows verbatim and diplomacy popups remain routed by the existing authoritative popup messages.
- Direct hardening: `AddTreatyBreakNotification` now null-checks `Universe.Notifications` before adding the stock treaty-break notification. This protects the legitimate host-side command application in headless generated universes without re-enabling passive replay side effects.

### P4-NG-001: Empire runtime research queue replay expands prerequisites on client

- Status: **FIXED**.
- Original trigger: `Soak_Nightly` default matrix, seed `0x50440002`, 2 clients, hazards `All`.
- Current smoke reproduction before fix: `Soak_Smoke`, seed `0x50440001`, 2 clients, tick `10`.
- Divergence: tick `8`, peer `2`, command `SetResearchTopic(ConventionalTorpedo)` for empire `1`.
- First payload diff:
  - Authority: `E|1|ConventionalTorpedo|ConventionalTorpedo|...`
  - Client: `E|1|ConventionalTorpedo|ConventionalTorpedo,MissileTheory,MicroTorpedo,LightTorpedo|...`
- Current smoke payload diff:
  - Authority: `Titans,Spaceport`
  - Client: `Titans,Corvettes,FrigateConstruction,Cruisers,Battleships,Spaceport`
- 14-DetLane localization: `Rng`, `Economy`, `Research`.
- Artifact: `TestResults/DesyncP4/p4_new-divergence_seed0x50440002_clients2_tick8.txt`
- Replay log: `TestResults/DesyncP4/p4_new-divergence_seed0x50440002_clients2_tick8.p4cmdlog`
- Fix mechanism: `E.EmpireRuntime` replay now calls `Empire.Research.SetQueueExact(...)`, which clears and rebuilds the research queue and queued-module index from the host payload verbatim. Replay no longer routes through `Empire.Research.AddTechToQueue` or `SetTopic`, so it cannot add discovered prerequisites or reorder the host-authored queue. Host-side command application still uses the gameplay helper `AddTechToQueue`, preserving authoritative prerequisite expansion for real queue commands.
- Regression: `Authoritative4XClientReplica_ReplaysResearchQueueExactWithoutPrereqExpansion_Headless` forces a discovered, unresearched `Titans` prerequisite chain, proves host gameplay queueing still expands prerequisites, then replays a host `Titans,Spaceport` E row and asserts the passive client queue remains exactly `Titans,Spaceport`.

## Built

- Added `UnitTests/Multiplayer/AuthoritativeSoakHarness.cs`.
- Added `UnitTests/Multiplayer/AuthoritativeSoakTests.cs`.
- Wired both files into `UnitTests/SDUnitTests.csproj`.
- Added a DEBUG-only in-process session-control seam: `Authoritative4XInProcessMultiClientSession.ApplySessionControlForTest`.
- Added `Empire.Research.SetQueueExact(...)` for passive authoritative replay.
- Changed `AuthoritativeStateSnapshot.ApplyResearchRuntime` to exact-set the research queue from the E-row payload.
- Added focused regression `Authoritative4XClientReplica_ReplaysResearchQueueExactWithoutPrereqExpansion_Headless`.
- Added DEBUG/test-only environment isolation so unit tests can run under sandboxed AppData/log constraints:
  - `STARDRIVE_TEST_APPDATA` override in `SDUtils.Dir`.
  - Unit-test bootstrap seeds isolated `StarDrive.user.config`.
  - Unit-test logging routes to isolated AppData in DEBUG.

## Harness Architecture

- In-process host plus N passive clients uses the existing `Authoritative4XLobby.StartInProcess()` and `Authoritative4XInProcessMultiClientSession`.
- The fuzzer is seeded with a deterministic SplitMix-style RNG; no unseeded randomness or wall-clock decisions are used.
- Commands are submitted through `Authoritative4XClientContext.Begin(...)` and the real `TrySubmit*` surface where available.
- Weighted command families cover no-op, colony/build queue, research, diplomacy, ship, fleet, automation/design, and ground troop commands.
- Per submitted tick, the harness compares authoritative/client contract digests through the existing replica mismatch path.
- On divergence, the harness persists the seed and command log and computes debug lane hashes across the 14 `DetLane` lanes.

## Entry Points

- `Soak_Smoke`: 1 seed, 500 ticks, 2 clients, all hazards by default. This remains in the normal test surface.
- `Soak_Nightly`: env-configurable seed matrix via `SD_P4_SOAK_SEEDS`, `SD_P4_SOAK_TICKS`, `SD_P4_SOAK_CLIENTS`, `SD_P4_SOAK_HAZARDS`. Marked `TestCategory("Performance")` for explicit opt-in, matching existing long-running test policy.
- `Soak_Replay`: reads `SD_P4_REPLAY_PATH` when present, otherwise captures and replays a short deterministic log. Also `Performance`-gated for explicit use.

## Hazard Injection

- Focus/unfocus: deterministic pause/speed toggles through the DEBUG-only test seam plus client universe state.
- Forced resync: applies the latest authoritative runtime payload to one deterministic client mid-run and asserts convergence.
- Camera-independence A/B: reruns the exact command log with alternate authority camera state and compares host digest streams.

## KnownGap Allowlist

KnownGap matching is narrow and field/variant-based. It treats the following as successful detections, not harness bugs:

- `FP.FleetPatrol`
- `BP.Blueprint`
- `G.Refit`
- `G.FleetRequisition`
- `G.DeepSpaceMovePosition`
- `F.Signatures`
- `S.PolicyFields`
- `D.DescriptiveFields` from the P1 manifest/report

Any other divergence remains fatal and is persisted as a replayable artifact.

## Verification Results

- Current P4-NG-002 fix build: passed with `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false --no-restore` (`0` warnings, `0` errors).
- Current P4-NG-001 fix build: passed with `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false` (`0` warnings, `0` errors).
- Not run in this sandbox: `Soak_Smoke`, `Authoritative4X`, or `Arena`; the orchestrator must run those acceptance suites.
- Earlier P4 harness build: passed with `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:OutDir=...UnitTests/bin/Debug --no-restore`.
- Earlier `Soak_Nightly`: exercised and failed fast on **P4-NG-001**, as intended for a new non-allowlisted divergence.
- Earlier `Soak_Replay`: exercised against the captured `P4-NG-001` command log and reproduced the same tick-8 divergence.
- Earlier normal non-performance suite: ran with `unittests.runsettings`; result `1014 total, 975 passed, 34 failed, 5 skipped`. The failures are pre-existing/non-P4 surfaces:
  - `26` sandbox write denials, mostly `game/battle-replays/...` junction output and `c:/tmp/fbx-specular-survey.csv`.
  - `8` existing determinism/AI diagnostic assertions in strategy, snowball, AI war, oracle sidekick, colony fix, and generated-game probes.
  - TRX: `UnitTests/TestResults/p4_full_nonperf.trx`

## Assumptions

- The P4-NG-001 runtime fix is intentionally narrow: only E-row research queue replay uses exact-set behavior; host command-side research queue gameplay remains unchanged.
- `Soak_Nightly` and `Soak_Replay` are explicit opt-in entry points because they are designed to fail on new divergences and can be longer-running. `Soak_Smoke` is the normal quick gate.

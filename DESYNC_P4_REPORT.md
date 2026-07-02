# StarDrive MP Desync P4 Report

## NEW Non-Allowlisted Divergences

### P4-NG-001: Empire runtime research queue replay expands prerequisites on client

- Status: **real non-allowlisted divergence, reproduced by deterministic replay**.
- Trigger: `Soak_Nightly` default matrix, seed `0x50440002`, 2 clients, hazards `All`.
- Divergence: tick `8`, peer `2`, command `SetResearchTopic(ConventionalTorpedo)` for empire `1`.
- First payload diff:
  - Authority: `E|1|ConventionalTorpedo|ConventionalTorpedo|...`
  - Client: `E|1|ConventionalTorpedo|ConventionalTorpedo,MissileTheory,MicroTorpedo,LightTorpedo|...`
- 14-DetLane localization: `Rng`, `Economy`, `Research`.
- Artifact: `TestResults/DesyncP4/p4_new-divergence_seed0x50440002_clients2_tick8.txt`
- Replay log: `TestResults/DesyncP4/p4_new-divergence_seed0x50440002_clients2_tick8.p4cmdlog`
- Notes: this is **not** in the P4 KnownGap allowlist. It appears to come from the replicated `E.EmpireRuntime` apply path reconstructing an exact queue signature through `Empire.Research.AddTechToQueue`, which expands discovered prerequisites on the client. I did not fix this in P4 because the lane brief forbids shipping/runtime behavior changes outside test-gated seams.

## Built

- Added `UnitTests/Multiplayer/AuthoritativeSoakHarness.cs`.
- Added `UnitTests/Multiplayer/AuthoritativeSoakTests.cs`.
- Wired both files into `UnitTests/SDUnitTests.csproj`.
- Added a DEBUG-only in-process session-control seam: `Authoritative4XInProcessMultiClientSession.ApplySessionControlForTest`.
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

- Build: passed with `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:OutDir=...UnitTests/bin/Debug --no-restore`.
- `Soak_Smoke`: passed green.
- `Soak_Nightly`: exercised and failed fast on **P4-NG-001**, as intended for a new non-allowlisted divergence.
- `Soak_Replay`: exercised against the captured `P4-NG-001` command log and reproduced the same tick-8 divergence.
- Normal non-performance suite: ran with `unittests.runsettings`; result `1014 total, 975 passed, 34 failed, 5 skipped`. The failures are pre-existing/non-P4 surfaces:
  - `26` sandbox write denials, mostly `game/battle-replays/...` junction output and `c:/tmp/fbx-specular-survey.csv`.
  - `8` existing determinism/AI diagnostic assertions in strategy, snowball, AI war, oracle sidekick, colony fix, and generated-game probes.
  - TRX: `UnitTests/TestResults/p4_full_nonperf.trx`

## Assumptions

- P4 is a harness lane, so I did not modify shipping desync behavior such as `ApplyResearchRuntime`.
- `Soak_Nightly` and `Soak_Replay` are explicit opt-in entry points because they are designed to fail on new divergences and can be longer-running. `Soak_Smoke` is the normal quick gate.

# Arena Duel Live-Driver Fix — P0 "The duel fights"

Date: 2026-07-05. Branch: `arena-045-port` (working tree, uncommitted — orchestrator verifies/commits).
Plan: `C:\dev\plans\STARDRIVE_ARENA_MP_MODES_PLAN_20260705.md`, Phase P0 (A.3 match-flow contract, A.4 proof).
Scope amended per ruling 2 synthesis (`C:\dev\plans\STARDRIVE_ARENA_MP_MODES_RULING2_20260705.md`):
P0 = liveness fix + watchdog + rendered live-driver proof only; the watchdog additionally implements
the ruling-2 engagement-liveness predicate with an `ARENA_LIVENESS_FAIL` telemetry halt.

## Root cause — CONFIRMED (reproduced headlessly, not just suspected)

The new proof test `ArenaMultiplayerLiveDriver_TwoScreensReachElimination_Headless` drives TWO real
`ArenaFightScreen`s through the real `Update → UpdateMultiplayerLive → AdvanceMultiplayerLiveTurn`
path over real loopback TCP, including the live handoff hazard: the join peer's transport is polled
(the live lobby's `Update` does this during `GoToScreen`) before the fight screen registers its
lockstep receiver.

RED run against the unmodified driver froze in **exactly the live QA state**:

```
hostTick=4 hostStatus='waiting for turn 4 input'
joinTick=0 joinStatus='waiting for host turn 0'
```

Which freeze modes actually fired:

1. **Init-ordering frame loss (suspected mode 2) — CONFIRMED, primary.**
   `TcpLockstepTransport.Deliver` invokes observers/receiver and otherwise *drops* the message;
   `LockstepHost.CommitTick` broadcasts each frame exactly once with no retransmit. The host
   commits ticks 0..InputDelay-1 unconditionally (no input barrier below the delay), so any frame
   committed while the join transport is polled without a registered `LockstepClient` is lost
   forever. The join then can never advance `Sim.Tick` past 0.
2. **Lockstep-barrier starvation (suspected mode 1) — CONFIRMED as the deadlock's second half.**
   The join's per-turn submit pointer had already moved past the lost exec-ticks (it submits
   exec tick `turn+delay` only for its *current* turn and never resubmits older ones), so the host
   starved at `waiting for turn 4 input` while the join starved at `waiting for host turn 0` —
   a mutual barrier deadlock with no recovery path and no visible failure.
3. **`UState.Paused` bracket (suspected mode 3) — DISPROVEN.**
   The proof drives the real screen `Update`, which brackets the live step in `UState.Paused=true`.
   With the handshake fixed, ships fight and die to elimination under that bracket:
   `UState.Objects.Update(Step)` and the combat branches it reaches do not consult `Paused`.
   No change was needed here beyond proving it.

Additionally confirmed by the second proof: **no liveness watchdog existed** — a never-arming peer
left the host silently "waiting for turn 3 input" forever (A.3 rule 4 violation).

## What changed

All changes in `Ship_Game/GameScreens/Arena/ArenaFightScreen.Multiplayer.cs` (driver only; no
combat/balance/gameplay changes, no wire-format changes):

1. **Arm/ack gate before the first `CommitTick` (A.3 rule 1).**
   - The join sends a `SessionReadyMessage` armed-ack immediately after registering its
     `LockstepClient` + observer in `InitializeMultiplayerLiveIfNeeded`.
   - The host refuses to advance any turn (`waiting for peer to arm`) until it observes that ack
     (`MultiplayerRemoteArmed`), so no command frame can ever be committed into an unregistered
     receiver.
   - The join refuses to submit/advance (`waiting for host to arm`) until it has observed any host
     message (`MultiplayerHostSeen`), so its submits can't be dropped by a not-yet-initialized host.
   - **Retransmit:** `MaintainMultiplayerLiveHandshake` re-sends the join's armed-ack / the host's
     `SessionControlMessage` every 0.5s until the far side is seen, and the host answers every
     observed ack with a control resend. Both message types already existed → **ProtocolVersion
     stays 3** (no wire change; the plan's "bump to 4 only if the message set changes" clause).
2. **Drain-transport-before-starve (A.3 rule 3).** Both wait branches in
   `AdvanceMultiplayerLiveTurn` poll (and, on the join, pump) once more before returning false to
   the next rendered frame.
3. **Submit exactly once per turn.** A barrier-blocked turn re-enters the driver every rendered
   frame; previously it re-submitted the identical command each frame, piling duplicates into the
   committed frame. Guarded by `MultiplayerLastSubmitTurn`.
4. **No-progress watchdog (A.3 rule 4).** If neither the turn counter nor `Sim.Tick` moves:
   after 5s the HUD status becomes `STALLED Ns — <barrier status>`; after 30s the match halts with
   a **void result** (`Disconnected=true`, no winner) and the visible match-complete panel —
   never a silent freeze.
5. **Engagement-liveness predicate (ruling 2).** Once the sim is ticking, at least one of
   {target acquired (`AI.Target`), ship `InCombat`, weapon fire attempted (active projectile)}
   must occur within `MultiplayerEngagementWindowTicks` (300 sim ticks = 5 sim-seconds) of spawn.
   Otherwise the match halts visibly with a void result and telemetry records
   **`ARENA_LIVENESS_FAIL`** with a deterministic cannot-engage reason (alive counts included).
   This catches missing-engagement causes as well as frame-pump causes, whichever fires.
6. **Accumulator clamp** (0.25s) so a long stall can't turn into an 8-steps-per-frame catch-up
   spiral afterwards.
7. Headless accessors `MultiplayerLiveSimTickForHeadless` / `MultiplayerEngagementSeenForHeadless`
   for the proofs.

## Proof results

New tests in `UnitTests/Graphics/ArenaMultiplayerLockstepTests.cs`:

- `ArenaMultiplayerLiveDriver_TwoScreensReachElimination_Headless` — two real screens over real
  loopback TCP through the real Update loop, with the lobby-era pre-registration polling window.
  Asserts (a) both sims strictly advance (liveness), (b) at least one ship dies, (c) asymmetric
  matchup completes with the correct winner on BOTH peers + visible end panels, no desync.
  **RED before fix** (mutual barrier deadlock above) → **GREEN after**.
- `ArenaMultiplayerLiveDriver_NoProgressWatchdogHaltsInsteadOfSilentFreeze_Headless` — connected
  but never-arming peer; host must surface the halt panel with a void, winnerless result.
  **RED before fix** (silent eternal wait) → **GREEN after**.
- `ArenaMultiplayer_RenderedFightScreen_PumpsLockstepFrames` (ruling-2 layer a) — rendered fight
  screens pump lockstep frames: `Sim.Tick` strictly advances on both peers under the real Update
  path, including the handoff hazard window.
- `ArenaMultiplayer_Duel_LiveLoopbackTcp_DoesNotIdleAfterSpawn` (ruling-2 layer b) — engagement
  actually starts within the bounded post-spawn window on both peers (target acquired / in combat /
  weapon fired) and the `ARENA_LIVENESS_FAIL` halt does not trip on a healthy duel.

Full Arena suite (`dotnet test --filter FullyQualifiedName~Arena`): **112/112 passed** (4m57s, TRX
`UnitTests/TestResults/shonh_DESKTOP-9VVJV75_2026-07-05_16_37_18.trx`; one earlier run invalidated
by a zombie testhost holding blackbox.log — killed, clean re-run green).

## Deferred (per P0 scope guard)

- **RulesetV0 / ProtocolVersion 3→4 / career-roster manifest validation** — that is P1 (the plan
  ships it with P0 as Milestone 1, but the task scope for this lane was duel-driver fix + proof
  only). No protocol bump was needed for P0 since the arm/ack handshake reuses existing
  `SessionReadyMessage`/`SessionControlMessage` types.
- Everything in plan Part E (matchmaking, reconnect/resync, anti-cheat, etc.).

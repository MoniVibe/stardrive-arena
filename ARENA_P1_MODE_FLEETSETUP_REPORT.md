# StarDrive Arena MP — P1 "Mode-First Lobby + Fleet Setup + Deterministic Match Flow" — Implementation Report

Date: 2026-07-06. Branch: `arena-045-port` (trunk `903cddbb5`). Changes left in the working tree (NOT committed) per instructions.

Plan: `C:\dev\plans\STARDRIVE_ARENA_P1_FLEETSETUP_EXEC_PLAN_20260705.md` (worked in order, test-first).
Context: `STARDRIVE_ARENA_MP_MODES_PLAN_20260705.md`, `STARDRIVE_ARENA_MP_MODES_RULING2_20260705.md`.

## Summary

All 7 plan steps landed. Protocol bumped **3 → 4**. Every new headless proof is GREEN and all pre-existing
Arena lockstep / determinism tests stay green (39/39 in the focused suite). The determinism foundation
(steps 1–5) plus mode validation + roster scoping (step 6) plus the end-to-end lobby→duel flow (step 7)
are playtestable. **FleetDesignScreen integration used the plan's documented fallback** (zero-offset
name-picker bundle) — see "Fleet-setup approach" below; the determinism machinery supports authored
formations today and the real editor slots in later without any wire/hash change.

Fleet-setup approach: **fallback (zero-offset name-picker), FleetDesignScreen deferred.**
Protocol version: **4.**

## Per-step: what landed + proof result

| Step | What | Proof | Result |
|---|---|---|---|
| 1 | `ArenaFleetBundle` — shared `StableNodeOrder`, `FromFleet`/`FromDesignNames`, `CanonicalBytes`/`DesignBundleHash`, `Encode`/`Decode` (size-capped) | `ArenaFleetBundle_CanonicalHash_StableAcrossNodeOrder_Headless` | PASS |
| 2 | `ArenaMultiplayerRuleset` (RulesetV0) + fold into SettingsHash/StartFingerprint (fixed order) + protocol 3→4 + SessionStartMessage/codec wire fields | `RulesetV0_SettingsHash_RoundTripsAndOrderFixed_Headless` | PASS |
| 3 | Mode validation in `ValidateStartMessage` (Career/Sandbox; Coop rejected; wager rejected) + bundle-hash inclusion | `RulesetV0_MismatchRejectsStart_Headless` | PASS |
| 4 | Formation-aware spawn from the bundle's per-ship offsets, STABLE order, join-side mirror | `FormationSpawn_Deterministic_Headless` | PASS |
| 5 | `ArenaMatchPhase` (Spawn→Countdown→Engage→Fight→Resolve) + tick-based countdown + engagement-liveness rebased to engage-tick + end-reason on panel | `Countdown_EngageAtDeterministicTick_Headless` | PASS |
| 6 | Career/Sandbox roster scoping (`ArenaFleetSetupScope`) + lobby mode/ruleset authoring + budget validation | `MatchingRuleset_CareerAndSandbox_RunsToDigest_Headless`, `Sandbox_BudgetCapRejectsOverspend_Headless` | PASS |
| 7 | End-to-end lobby→setup→duel two-screen integration with visible end-reason | `ArenaMultiplayer_ModeSetupToResolve_TwoScreens_Headless` | PASS |

## Protocol bump note

`ArenaMultiplayerSettings.ProtocolVersion` = **3 → 4** (`ArenaMultiplayerSession.cs`). This is the single
bump for the whole Arena-MP-modes program (RulesetV0 + design bundles enter the start payload). The lobby
hello check and `ValidateStartMessage` already gate on ProtocolVersion equality, so a v3 peer is cleanly
rejected against a v4 peer. `ArenaBattleCodes` format bumped **1 → 2** (carries the new ruleset/bundle
fields; format-1 codes still parse via a version guard).

## Hard-determinism decisions (as built)

- **Single shared `StableNodeOrder`** (`ArenaFleetBundle.StableNodeOrder`) drives BOTH the bundle hash AND
  the formation spawn order — sort by (ShipName ordinal, offset.X IEEE754 bits, offset.Y bits). Never trusts
  insertion order (drag sequence). This is the one anti-desync invariant the plan called out (risk 4).
- **RulesetV0 + both DesignBundleHashes fold into SettingsHash in a FIXED order** (existing settings →
  RulesetV0 → host bundle hash → join bundle hash), so any ruleset/bundle divergence changes SettingsHash
  and rejects at `ValidateStartMessage` (handshake), never mid-match.
- **Countdown is sim-ticks, never wall-clock.** `MultiplayerEngageAtTick` is an ABSOLUTE tick
  (`= CountdownTicks`, default 180 = 3s × 60), not `firstSeenTick + countdown` — the host reaches its first
  committed tick at simTick 1 while the join lags by InputDelay, so a relative baseline would give the peers
  different engage ticks. Both peers evaluate `simTick >= CountdownTicks` against the SAME lockstepped tick.
- **Engagement-liveness window rebased to the engage tick** (`MultiplayerEngageAtTick + 300`), so the
  180-tick countdown no longer eats the liveness budget and a slow matchup doesn't false-fail.

### Countdown "hold-fire" — non-trivial finding (design by deep-reasoner)

The plan assumed "no attack orders → fleets hold." **That assumption is false in this engine:** `ShipAI`
autonomously acquires sensor targets and `UpdateCombatStateAI` fires weapons whenever enemies are in range,
independent of `CombatState`/`IgnoreCombat`/`Target`. A naive freeze (CombatState.None + IgnoreCombat) did
NOT stop fire; re-applying the freeze every tick in the screen driver DESYNCED (repeated side-effectful
mutation between sim steps).

Resolution (deterministic, minimal core touch): a one-way `ShipAI.ArenaHoldFire` bool —
- set once at spawn (`FreezeMultiplayerFleet`), cleared once at the engage tick (`UnfreezeMultiplayerFleet`
  from the phase machine's Engage case, immediately before `EngageAll`);
- read at the top of `FireOnTarget` (the sole fire path, incl. point-defense) and `SelectCombatTarget`
  (so `Target`/`InCombat` stay clear and the countdown never trips the liveness predicate);
- transient (never `[StarData]`), default false, so it can never leak to non-arena ships or saves.

Determinism argument: the flag is READ inside the deterministic sim step and WRITTEN only at spawn (identical
on both peers) and at a monotonic sim-tick threshold OUTSIDE the sim step. `AdvanceMultiplayerPhase(N)` runs
AFTER tick N's sim, so the clear-at-engage takes effect for tick N+1 on BOTH peers symmetrically. Also, the
live driver submits a **NoOp** focus command during Spawn/Countdown (instead of the AttackTarget heartbeat),
so no `Target` is set during the countdown — the input stream still flows so the lockstep barrier advances.

## Fleet-setup approach: fallback used, FleetDesignScreen integration DEFERRED

Per the plan's step-6 risk 1 and its documented FALLBACK, this pass uses a **zero-offset name-picker bundle**
rather than standing up the UniverseScreen-coupled `FleetDesignScreen` as a modal setup step:

- `ArenaFleetSetupScope` resolves the roster (Career = `ArenaCareer.FieldedFleetVessels()` designs; Sandbox =
  all legal stock combat designs), clamps to `MaxFleetShipsPerSide`, builds a canonical bundle via
  `ArenaFleetBundle.FromDesignNames`, and validates the chosen fleet (Career roster membership; Sandbox
  budget cap).
- The lobby authors the RulesetV0 (`BuildArenaRuleset`) from the host's mode selection and attaches both the
  ruleset and the (fallback) bundles in `BuildArenaSettings`.

**Why deferred:** `FleetDesignScreen` is deeply `UniverseScreen`-coupled (ctor takes a live `UniverseScreen`
+ `EmpireUIOverlay`, edits a live `Fleet` on a live `Empire`, reads `Player.ShipsWeCanBuildSnapshot`). Standing
up a scoped setup universe cleanly is the biggest execution risk in the lane and the plan explicitly permits
the fallback so the determinism foundation (steps 1–5) lands playtestable without blocking on the setup UI.

**What's already in place for the real screen** (so it slots in with no wire/hash change): the bundle format
carries per-ship `RelativeFleetOffset` + weights + CombatState; `ArenaFleetBundle.FromFleet` is the shared
"live Fleet → FleetDesign" projection (mirrors `SaveFleetDesignScreen.DoSave`); formation-aware spawn already
reads offsets from the bundle. The remaining work is purely UI: launch `FleetDesignScreen` against the arena's
own universe scoped to `ArenaFleetSetupScope.ResolveRoster`, read back the saved `FleetDesign`, and
`ArenaFleetBundle.Encode` it into `HostFleetBundle`/`JoinFleetBundle` in place of the name-list fallback. A
thin budget-readout/LOCK overlay is the only genuinely new UI.

## Deferred within P1 (hooks kept, not built)

- Wagers (P2): `WagerCredits` reserved on RulesetV0, forced 0, non-zero rejected at validation.
- Coop (P4): `ArenaMatchMode.Coop` reserved, rejected at validation.
- FleetDesignScreen modal setup + budget/LOCK overlay (fallback used, see above).
- Per-ship targeting-weight / CombatState tuning UI (rides in the bundle + hash, not exposed).
- `RosterCommitmentHash` stays "" (honor-system, Q3); `ContentFingerprint` = active-mod name/version.
- Lobby mode/budget *pills* (UI): the ruleset authoring + headless accessors are wired
  (`SetArenaModeForHeadless`, `ArenaRulesetForHeadless`); the on-screen pill controls are a thin follow-up.

## Files changed

New:
- `Ship_Game/GameScreens/Arena/ArenaFleetBundle.cs` — canonical bundle projection/hash/encode/decode + `StableNodeOrder`.
- `Ship_Game/GameScreens/Arena/ArenaMultiplayerRuleset.cs` — RulesetV0 record + fixed-order `AppendTo`.
- `Ship_Game/GameScreens/Arena/ArenaFleetSetupScope.cs` — Career/Sandbox roster scoping + fleet validation.

Modified:
- `Ship_Game/GameScreens/Arena/ArenaMultiplayerSession.cs` — protocol 3→4; Ruleset+bundle fields; SettingsHash/StartFingerprint fold; ToStartMessage/FromStartMessage/WithResolvedFleets/WithRematchSeed carry-through; `ValidateRuleset` + `SumBundleCost` mode validation + bundle-hash checks.
- `Ship_Game/GameScreens/Arena/ArenaFightScreen.Multiplayer.cs` — ArenaMatchPhase machine + tick countdown; formation-aware `SpawnMultiplayerFormation` (replaces hardcoded column) + `ResolveMultiplayerBundle`/`BundleShipNames`; `FreezeMultiplayerFleet`/`UnfreezeMultiplayerFleet`; NoOp submit during countdown; liveness rebased to engage-tick; end-reason on result panel + `arena_mp_end_reason` label; headless accessors.
- `Ship_Game/GameScreens/Arena/ArenaMultiplayerLobbyScreen.cs` — `ArenaMode`/`ArenaBudgetModel`/`ArenaBudgetCredits` state; `BuildArenaRuleset`; ruleset + fallback bundles attached in `BuildArenaSettings`; headless accessors (`ArenaModeForHeadless`, `ArenaRulesetForHeadless`, `SetArenaModeForHeadless`).
- `Ship_Game/GameScreens/Arena/ArenaBattleCodes.cs` — format 1→2, carries ruleset+bundle fields (back-compat guard).
- `Ship_Game/AI/ShipAI/ShipAI.Combat.cs` — transient `ArenaHoldFire` gate at `FireOnTarget` + `SelectCombatTarget`.
- `SDLockstep/SessionMessages.cs`, `SDLockstep/LockstepMessageCodec.cs` — SessionStartMessage ruleset+bundle wire fields (appended optional, back-compat).
- `StarDriveArena.csproj` — compile includes for the 3 new files.
- `UnitTests/Graphics/ArenaMultiplayerLockstepTests.cs` — 8 new P1 proofs + a `DriveRealLobbiesToLaunchedFight(configureHost)` overload.

## Verification

- `dotnet build StarDrive.csproj` / `UnitTests/SDUnitTests.csproj` — clean (0 warnings, 0 errors).
- Focused suite `ArenaMultiplayerLockstepTests` + `ArenaDeterminismPatchContractTests` + `ArenaPilotTraitsTests`
  — **39/39 PASS** (26 pre-existing lockstep + 8 new P1 proofs + determinism/pilot-trait classes).
- `LockstepNetworkTransportTests` — 4/4 PASS (SessionStartMessage codec round-trip intact).
- Per the lane instructions, the full combined `FullyQualifiedName~Arena` suite was NOT run (known intermittent
  native crash); focused filters only, stray `testhost` killed before each run.

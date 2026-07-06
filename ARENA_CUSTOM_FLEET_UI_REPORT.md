# StarDrive Arena — Custom-Fleet PLAYABLE UI Slice (Phase 3/4) — Implementation Report

Date 2026-07-06. Repo `C:\dev\stardrive\StarDrive-main`, branch `arena-045-port` (trunk `17cccde47`, ProtocolVersion 5).
Spec: `C:\dev\plans\STARDRIVE_ARENA_CUSTOM_FLEET_PROGRAM_PLAN_20260706.md` (KERNEL REVIEW — BINDING AMENDMENTS honored).
Kernel already built + proven (see `ARENA_CUSTOM_FLEET_KERNEL_REPORT.md`); this pass is the UI + wiring that FEEDS designs into it.

**Left uncommitted in the working tree** for the orchestrator to verify and commit. No kernel determinism internals re-touched; protocol NOT re-bumped (stays 5).

---

## 1. What was built (the N=2 playable loop)

A player picks designs → they enter a **sandbox scratch set** (transiently registered, never Saved Designs / the 4X) →
the fleet is exchanged through the **already-proven `ArenaDesignTable` kernel** → duels the opponent under the host's
**budget** with a real host-set **match length**. Everything gated behind `GlobalStats.Defaults.EnableArenaCustomFleet`
(default FALSE = today's name-picker duel, unchanged and proven no-op).

Deliverables:

1. **MATCH-LENGTH FIX (§5.2) — corrected to actually LENGTHEN.** `RulesetV0.MaxMatchSeconds` was HASHED but never
   ENFORCED (the real cap was the separate `MaxTurns`). Fixed: new
   `ArenaMultiplayerSettings.EffectiveMaxTurns = min(MaxTurns, MaxMatchSeconds*60)`, with `MaxTurns` kept as an
   absolute SAFETY CEILING. Both inputs are already folded into `SettingsHash` (MaxTurns directly, MaxMatchSeconds via
   `Ruleset.AppendTo`), so **no new fingerprint surface** — the derived cap is identical on both peers and a timeout is
   deterministic.
   **The `min()` alone was INERT** (coordinator catch): `MaxTurns` defaulted to 600 and was clamped to ≤2000, so
   `min()` was always bound by MaxTurns and every host MATCH-LENGTH choice still ended at ≤33s (default 10s) — the
   director's "fights too short" complaint was NOT fixed. The correction: for REAL lobby matches `MaxTurns` is now a
   HIGH ceiling (`DefaultTurns` 600 → **36000** = 10 min; the `Clamped(30, 2000)` ceiling → new `MaxTurnsCeiling`
   **216000** = 60 min, all 3 sites), so `MaxMatchSeconds` actually binds. Headless self-tests keep passing an
   explicit small `MaxTurns` (min keeps their low ceiling authoritative → runs stay fast). Host-settable via a new
   lobby **MATCH LENGTH** pill (30/60/120/300/600s).

2. **HOST BUDGET UI (§5.1).** The lobby already had budget mode/cap pills (`CycleBudget`, `BudgetLabel`). Fixed the
   client-side friendly guard currency: `ArenaFleetPickerScreen.CostOf` now sums **`BaseStrength`** (rounded), matching
   the authoritative `ArenaMultiplayerSession.SumBundleCost` gate — previously it used `BaseCost`, so the picker could
   deem a fleet affordable that the handshake then rejected. The kernel's `SumBundleCost > BudgetCredits` branch is
   the authoritative gate; this is the matching client guard.

3. **CUSTOM-DESIGN → KERNEL SCAFFOLDING (fallback composer).** Custom designs flow into the exchange kernel
   end-to-end from the lobby via an in-memory sandbox scratch set + transient `@arena/` registration + the proven
   zero-offset column composer (see §2). **NOTE: the real ship-designer and formation-editor UI are NOT delivered
   here** — they are held for a SEPARATE architecture decision (see §2). This pass provides the plumbing that feeds
   designs into the kernel; it does not launch `ShipDesignScreen`/`FleetDesignScreen`.

4. Everything **flag-gated**; the current working duel / interim `ArenaFleetPickerScreen` path is **not regressed**.

---

## 2. Designer + formation — NOT delivered this pass (held for a separate architecture decision)

**The real ship-designer and formation-editor UI are NOT wired in this pass.** They are held for a separate
architecture decision. What this pass delivers is the KERNEL-FEEDING scaffolding (sandbox scratch set + transient
registration + zero-offset column composer) so custom designs can be exercised end-to-end through the exchange kernel;
it does not launch `ShipDesignScreen` or `FleetDesignScreen`.

A deep-reasoner ruling (2026-07-06) evaluated real-editors-from-lobby vs the fallback composer and recommended the
fallback, on lowest-risk grounds — recorded here as context for the pending decision, NOT as a delivered feature:

- **Real `ShipDesignScreen`/`FleetDesignScreen` require a live `UniverseScreen`** (they deref `universe.Player`/`UState`).
  The lobby has NO live universe; `ArenaFightScreen.Create` builds a FULL `UniverseState` (empires + systems) — too
  heavy to stand up, tear down, and rebuild at match start, for ZERO added playability at N=2.
- **The real designer IS already proven against the arena universe** — `ArenaFightScreen.OpenCustomizerForActiveVessel`
  (ArenaFightScreen.cs:4591) does `new ShipDesignScreen(this, EmpireUI)` — but that only exists AFTER the match starts,
  so it can't feed `HostDesignTable` at lobby lock. Wiring "author in the fight-screen customizer → capture into a
  sandbox that survives back to a fresh lobby → next match fields it" is the **next-phase** feature.

**This pass fields EXISTING / previously-imported designs through the composer**, which exercises the ENTIRE exchange
kernel end-to-end (canonicalize → encode → `HostDesignTable` → far-peer register+validate → spawn):

- **Formation** = `ArenaFleetBundle.FromDesignNames` (zero-offset column) — the proven deterministic layout already on
  the P1 bundle path. Real drag-place `FleetDesignScreen` deferred with the designer.
- **Sandbox scratch set** = an in-memory map on the lobby (`SandboxDisplayToWire`: display name → `@arena/<hash>` wire
  name). Picked designs are **transiently registered** under their content-derived `@arena/` names via
  `ArenaDesignTable.RegisterTransient` (`AddShipTemplate(playerDesign:false, readOnly:true)` — never Saved Designs,
  never a dir, never the 4X, collision-free namespace), and **torn down on every lobby-exit path**.

**DEFERRED to the after-action-report lane (clearly flagged):**
- Real `ShipDesignScreen`/`FleetDesignScreen` launched from the lobby (needs the setup-universe seam, next phase).
- **Join-side custom payloads over the lobby sync** (advisor risk 4): the fleet NAME list transports (as `@arena/`
  names), but the join's design PAYLOAD table does not yet — so at N=2 the HOST authors the customs this pass. The
  kernel + settings already carry both `HostDesignTable`/`JoinDesignTable` (proven symmetric in the headless digest
  test); only the lobby transport of the joiner's payload is the bounded extension.
- **After-action panel (ADDENDUM 3)** — survivors / top damage-dealer / per-ship kills / per-weapon breakdown on the
  match-end panel. Additive and well-specified (transient PURE-OBSERVATION counters at the existing deterministic
  damage sites, NEVER hashed), but it touches the sim damage hooks (`ShipModule` per-hit path) — a determinism-sensitive
  change best landed with its own proof budget in the after-action lane rather than rushed into this slice.

---

## 3. Budget + match-length wiring (exact seams)

### Match length (the real cap) — corrected EffectiveMaxTurns numbers
- **NEW** `ArenaMultiplayerSettings.EffectiveMaxTurns` (ArenaMultiplayerSession.cs) — `min(MaxTurns, MaxMatchSeconds*60)`;
  `MaxMatchSeconds<=0` falls back to the `MaxTurns` ceiling only; overflow-guarded.
- **Four comparison sites swapped** from `settings.MaxTurns` to `settings.EffectiveMaxTurns`: the live loop
  (`AdvanceMultiplayerLiveTurn`, ArenaFightScreen.Multiplayer.cs, "time limit") + the three headless loops
  (`RunTwoPeerLockstep`, `RunHostNetworkLoop`, `RunJoinNetworkLoop` in ArenaMultiplayerSession.cs).
- **Raised ceiling so MaxMatchSeconds actually BINDS (the correction):** `DefaultTurns` 600 → **36000**; new
  `MaxTurnsCeiling` **216000** replaces the `Clamped(30, 2000)` cap at all 3 sites (`CreateDefaultSettings`,
  `RunAuthoritative4XSelfTestForHeadless`, and the 4X self-test `maxTurns:`). `BuildArenaSettings` sets
  `MaxTurns = Math.Max(ParseTurns()=36000, MaxMatchSeconds*60 + 60)`, so for ANY offered length the ceiling is
  comfortably above the host's choice and `MaxMatchSeconds` is what binds.
- **Corrected EffectiveMaxTurns (high ceiling, real match):** 30s→**1800**, 60s→**3600**, 120s→**7200**,
  300s→**18000**, 600s→**36000** ticks. (Pre-fix: ALL of these collapsed to ≤2000 / default 600 — the inert bug.)
- **Headless self-tests stay fast:** every match-run test sets an explicit small `MaxTurns` (60/90/180/300/600/900),
  and `min()` keeps that low value authoritative regardless of the 600s ruleset default — audited all callers of
  `CreateDefaultSettings`/`RunLoopbackTcpSelfTest`/`RunInProcess`; none rely on the `DefaultTurns` default for a match
  run, so raising it does not lengthen or hang any test (confirmed: 49 focused tests, 2m4s, no hang).
- **Lobby control:** `ArenaMaxMatchSeconds` field + `CycleMatchLength()` (steps 30/60/120/300/600s) + `MatchLengthLabel()`
  + a **MATCH LENGTH** pill in `BuildStarGladiatorSetup`; `BuildArenaRuleset` now sets `MaxMatchSeconds = ArenaMaxMatchSeconds`.

### Budget
- **Currency fix:** `ArenaFleetPickerScreen.CostOf` → `MathF.Round(design.BaseStrength)` (was `BaseCost`), matching the
  authoritative `SumBundleCost`. The host budget cap/model UI (`CycleBudget`/`BudgetLabel`, `ArenaBudgetCredits`) already
  existed; the picker receives the cap and now guards with the correct currency. The authoritative overspend rejection
  at the handshake (`ValidateRuleset` Cap branch) was already wired and is proven for CUSTOM fleets by the kernel test.

---

## 4. Custom-design → kernel wiring (the fallback composer plumbing)

All in `ArenaMultiplayerLobbyScreen.cs`, all no-op when the flag is off:
- `RebuildSandboxScratchSet(displayNames)` — called from `ApplyPickedFleet`; tears down the prior set (the "open the
  picker twice" leak, advisor risk 3), then for each picked design that passes `ArenaDesignTable.ValidateContentAvailable`
  (non-carrier, ids resolvable) registers it transiently under `@arena/<hash>` and records display→wire.
- `ToWireFleetNames(displayNames)` — maps display names → `@arena/` wire names at the two wire boundaries only
  (`BroadcastLobbyState` `Fleet=` sync + `BuildArenaSettings`), so `LocalPeer.FleetDesignNames` stays display names and
  the picker/labels are unaffected. Persistent config does NOT store fleet names, so no disk pollution.
- `BuildLocalDesignTable()` — `ArenaDesignTable.Encode` of the registered scratch set → `HostDesignTable`
  (host role) / `JoinDesignTable` (join role) on the settings.
- **Teardown** on `LaunchVisibleArena` (clean handoff — the fight's `ArmMultiplayerLive` re-registers the SAME
  content-hash designs from the authoritative start-message tables, idempotently) and on `ExitScreen` (back-out/abandon),
  targeting the EXACT tracked set (never a blanket `@arena/*` delete).

---

## 5. Files changed

| File | Change |
|---|---|
| `Ship_Game/GameScreens/Arena/ArenaMultiplayerSession.cs` | NEW `EffectiveMaxTurns`; 3 headless-loop cap sites use it |
| `Ship_Game/GameScreens/Arena/ArenaFightScreen.Multiplayer.cs` | live-loop cap uses `EffectiveMaxTurns` ("time limit") |
| `Ship_Game/GameScreens/Arena/ArenaMultiplayerLobbyScreen.cs` | `DefaultTurns` 600→36000 + NEW `MaxTurnsCeiling`=216000 (3 clamp sites) so MaxMatchSeconds binds; match-length pill + host control; sandbox scratch set + transient registration + `@arena/` wire rewrite + `HostDesignTable`/`JoinDesignTable` population + teardown |
| `Ship_Game/GameScreens/Arena/ArenaFleetPickerScreen.cs` | budget guard currency `BaseCost` → `BaseStrength` |
| `UnitTests/Determinism/ArenaCustomFleetUiTests.cs` | NEW — 5 UI-phase proof gates |
| `UnitTests/SDUnitTests.csproj` | compile the new test file |

No change to the kernel (`ArenaDesignTable.cs`), the codec, the protocol version, or the settings hash structure.

---

## 6. Proof results (headless, focused filters; stray testhost killed before each run)

| Suite | Result |
|---|---|
| `ArenaCustomFleetUiTests` (NEW, 5 gates) | **5 / 5 passed** |
| `ArenaCustomFleetUi` + `ArenaCustomFleetKernel` + `ArenaMultiplayerLockstep` + `ArenaDeterminism` (all four focused filters, one run) | **49 / 49 passed, 2m4s, no hang** |
| `dotnet build StarDrive.csproj` / `StarDriveArena.csproj` / `UnitTests/SDUnitTests.csproj` | **0 Warning(s) 0 Error(s)** each |

The 5 new gates:
- `MatchCap_EffectiveMaxTurns_DerivesFromMaxMatchSeconds_Headless` — **the LENGTHENING proof**: under a high MaxTurns
  ceiling (DefaultTurns=36000) a 60s host match → EffectiveMaxTurns **3600** (30s→1800, 120s→7200, 300s→18000,
  600s→36000); a low-MaxTurns self-test (90) stays 90 (min keeps headless fast); a 5s match → 300; `MaxMatchSeconds<=0`
  → the ceiling. RED against the pre-fix code (which collapsed all of these to ≤600).
- `MatchCap_LobbyCeiling_IsHighEnoughForMaxMatchSeconds_Headless` — guards the constants (`DefaultTurns >= 600*60`,
  `MaxTurnsCeiling >= DefaultTurns`) so a future edit that re-lowers them (reintroducing the inert bug) fails loudly.
- `MatchCap_MatchEndsAtMaxMatchSeconds_NotMaxTurns_Headless` — a 12s (720-tick) match under the high ceiling runs a
  balanced mirror match to a turn count **> 600** (impossible under the old MaxTurns hard cap), with every turn hash
  matching on both peers — the runtime proof that MaxMatchSeconds now LENGTHENS the match.
- `CustomFleet_ScratchDesigns_RoundTripAndDigestMatch_Headless` — two scratch-authored custom designs at authored
  column offsets round-trip through `ArenaDesignTable` + the bundle, validate at the handshake, and both in-process
  peers reach the SAME final digest with the custom ships spawned (one per node).
- `Budget_ClientPickerGuard_UsesBaseStrength_Headless` — the client picker guard denies the pick that overspends by
  `BaseStrength`, mirroring the authoritative gate's currency.

(Per the kernel review, the two-peer digest match is a SECONDARY gate — the primary per-peer reconstruction proof is
the kernel's pure byte round-trip, already green. The custom-fleet digest match here proves the UI-phase spawn/formation
wiring on top of that proven reconstruction.)

---

## 7. Flag-off no-op verification

`EnableArenaCustomFleet` default FALSE. With it off: `RebuildSandboxScratchSet` early-returns, `SandboxDisplayToWire`
stays empty, `ToWireFleetNames` returns names unchanged, `BuildLocalDesignTable` returns `""`, and no design table is
emitted or consumed. For headless/self-test settings that set a low explicit `MaxTurns`, `EffectiveMaxTurns` equals
that `MaxTurns` (min keeps the low ceiling authoritative), so those runs are unchanged and fast. The 49-test focused
suite (which runs with the default flag) confirms today's behavior is intact and nothing hangs under the raised ceiling.

---

## 8. Deferred to the after-action-report lane
- Real `ShipDesignScreen`/`FleetDesignScreen` launched from the lobby (setup-universe seam).
- Join-side custom design PAYLOAD transport over the lobby sync (symmetric custom-vs-custom at N=2 from the live lobby;
  the kernel + settings already support it, only the lobby payload transport is missing).
- The after-action panel (ADDENDUM 3): survivors / top damage-dealer / per-ship kills / per-weapon breakdown, as
  transient PURE-OBSERVATION counters at the existing deterministic damage sites (`ShipModule.OnDamageInflicted` /
  `DamageShield`), read at the Resolve phase, NEVER hashed — folded onto `ShowMultiplayerEndPanel`.
- N>2 match flow (Phase 5), persistent ammo / pay-to-rearm (Phase 7), in-match `UnlimitedAmmo` toggle (Phase 3 remainder).

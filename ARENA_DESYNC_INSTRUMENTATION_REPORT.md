# Arena Lockstep Desync — Self-Diagnosing Instrumentation + Reproduction Harness

Branch: `arena-045-port` · Trunk `b67039db5` · ProtocolVersion 5
Scope: make the live 2-machine arena lockstep desync (held ~1231 turns, flipped at turn 1232) SELF-DIAGNOSING, and add an order-perturbation reproduction/bisection harness. Everything is flag/telemetry-gated and protocol-neutral — the canonical payload and the wire checksum are untouched.

Read `ARENA_DESYNC_FIX_REPORT.md` first — it disproved the design-identity split as the cause and named the surviving suspects (order-sensitive combat tie-breaks vs cross-machine float divergence). This instrumentation is the tool that will localize the real cause from the NEXT live reproduction.

---

## 1. What the per-turn checksum folds, and in what order

The value the lockstep protocol compares each turn is `UniverseState.ComputeAuthoritativeStateHash(profile)` (`Ship_Game/Determinism/UniverseStateHash.cs:106`), a 128-bit `Hash128Checksum` produced by the single canonical traversal `WriteAuthoritative` (`UniverseStateHash.cs:29`). The lockstep adapter `UniverseStateLockstepSimulation.Hash()` (`Ship_Game/Determinism/Lockstep/UniverseStateLockstepSimulation.cs:54`) returns exactly this `(lo, hi)`; `DesyncDetector` (`SDLockstep/Lockstep.cs:75`) records the FIRST tick whose two peers' `(lo, hi)` differ.

**Iteration order is SAFE (this rules out one whole hypothesis).** Every entity collection is folded in a stable sorted order:

- Empires: `us.Empires.OrderBy(e => e.Id)`
- Ships: `us.Ships.OrderBy(s => s.Id)`
- Planets: `us.Planets.OrderBy(p => p.Id)`

`Ship.Id` is an immutable `readonly int` assigned at spawn (`GameObject.Id`), and both peers spawn identical stable ids (asserted by `ValidateSnapshots`, `ArenaMultiplayerSession.cs`). So the checksum does NOT iterate an unordered collection — the order-of-iteration-of-the-hash is not itself the bug.

**Field set folded, in fold order:**

| Lane | Fields (per entity, in order) |
|---|---|
| Universe | `StarDate` |
| Empires (per, sorted by Id) | `Id`, `Money`, `TotalPopBillion`, `NetPlanetIncomes`, `NumPlanets`, `UnlockedTechs.Length`, `Research.NetResearch`, `Research.Topic`, `AI.Goals.Count`, `AllFleets.Count`, **empire RNG stream state** (canary) |
| **Ships (per, sorted by Id)** | **`Id`, `Position.X`, `Position.Y`, `Velocity.X`, `Velocity.Y`, `Rotation`, `Health`** |
| Planets (per, sorted by Id) | `Id`, `Owner.Id`, `PopulationBillion`, `ConstructionQueue.Count` |
| Universe RNG | universe RNG stream state |

Floats are hashed as raw IEEE-754 bits (`FloatRaw`). The per-ship set is **6 floats + Id** — it deliberately does NOT fold shield, ordnance, per-module health, or AI target id. The arena skirmish slice moves ships (position/velocity/rotation) and damages them (health); the empire economy turn is excluded from the arena tick. So a mid-match arena divergence almost certainly first surfaces in the **Ships lane** (a position/velocity/rotation/health float) or in an **empire RNG-state** canary.

---

## 2. Field-level desync dump — what it emits and where to read it

New file `Ship_Game/Determinism/UniverseStateFieldDump.cs`. It re-folds **exactly the checksum's per-ship field set** (same primitive calls, same order) into a *separate* `Hash128Checksum`, so a per-ship digest here equals that ship's contribution to the wire checksum. It is pure observation — it never touches the sim or the wire checksum, so a flag-on run is bit-identical to a flag-off run (proven by `UniverseStateFieldDump_MirrorsWireChecksumShipFold_Headless`).

**Gate.** New flag `GamePlayGlobals.EnableArenaDesyncFieldDump` (default false). The dump is armed when that flag is on OR when `EnableArenaCustomFleet` is on (the custom-fleet path is the one that produced the live turn-1232 desync, so a live custom-fleet reproduction self-diagnoses with no extra toggle). Flag-off ⇒ nothing is captured (true no-op).

**Where it fires + what it writes.** When the live driver detects a desync (`ArenaFightScreen.Multiplayer.cs`, `RecordMultiplayerLiveTurn` → `desync.HasDesync`), each peer calls `DumpMultiplayerDesyncFields(turn)` which writes to the arena telemetry log via `ArenaMultiplayerTelemetry.FieldDump(...)`. On the live path each peer holds only ITS OWN sim state (the remote is a 128-bit checksum), so **each machine dumps its own breakdown; you diff the two machines' logs.** Two lines are emitted:

```
<utc> DESYNC_FIELDS which=PRIOR     turn=1231 ships=N roster=[id:HI:LO;id:HI:LO;...] fieldsOf=<id> (...) PosX=..(0x..) PosY=.. VelX=.. VelY=.. Rotation=.. Health=..
<utc> DESYNC_FIELDS which=DIVERGING turn=1232 ships=N roster=[id:HI:LO;id:HI:LO;...] fieldsOf=<id> (first-ship-changed-since-prior-turn) PosX=..(0x..) ...
```

- `PRIOR` is rendered from digests **cached before the sim advanced into the diverging turn** (the last-clean state — current `UState` has already moved on), so you have the turn-1231 truth alongside turn-1232.
- `roster=[...]` is the per-ship digest list `id:HI:LO`. **Diff the two machines' `roster` on the same turn: the first `id` whose `HI:LO` differs is the ship that diverged first.**
- `fieldsOf=<id>` expands one ship field-by-field (raw decimal + `0x` IEEE-754 bits). On the DIVERGING line the anchor is the first ship whose digest changed since PRIOR; per-field, compare the two machines' `0x` bits to see WHICH field(s) moved.

**Where the file is.** `sim-output/arena-multiplayer-<stamp>-<surface>-<role>-<pid>-<guid>.log` on each machine (plus the shared `sim-output/arena-multiplayer-last-session.log`), written by `ArenaMultiplayerTelemetry` (`Start` at `ArenaMultiplayerTelemetry.cs:52`). `grep DESYNC_FIELDS` in each machine's log after the run.

**In-process, both peers in scope — the direct diff.** When a desync fires inside `RunTwoPeerLockstep` (the headless harness has BOTH `UState`s), `DiagnoseFieldDivergence` compares the two peers' digests directly and populates `ArenaMultiplayerRunResult.DesyncFieldBreakdown` (also `Log.Warning`'d). `UniverseStateFieldDump.DiagnoseHostVsJoin` finds the first ship whose host-vs-join digest differs, emits a per-field host-vs-join diff with a ULP distance, and classifies it (see §4).

---

## 3. Order-perturbation harness — result: the sim is ModuleSlotList-order-INSENSITIVE

Per the prior report's §3 suggestion, the harness forces one peer to iterate `Ship.ModuleSlotList` in a PERTURBED (reversed) order and asserts the per-turn digest STILL matches — proving order-insensitivity, or failing loudly at the first order-sensitive site (repair-target tie `Ship_Repair.cs` `GetModuleToRepair`→`FindMax`; AI-target tie `ShipAI.Combat.cs:422` `PotentialTargets.FindMax`; module-explosion tie `Ship.cs:420` `ModuleSlotList.Filter(...).FindMax`).

Seams added:
- `Ship.PerturbModuleOrderForTest()` (`Ship_Game/Ships/Ship_ModuleGrid.cs`) — reverses `ModuleSlotList` in place without touching module identity, grid coordinates, or health. Grid/spatial code reads module positions from the flyweight, not the array index, so reversal is behavior-neutral **except at first-wins tie-breaks** — which is exactly what we probe.
- `ArenaFightScreen.PerturbMultiplayerModuleOrderForTest()` — reverses every spawned ship's module order on one peer (both fleets, attacker + defender).
- `ArenaMultiplayerSession.RunInProcess(settings, forceDesyncAfterTurn, perturbJoinPeer)` — perturbs the JOIN peer AFTER spawn but BEFORE the first tick, so both peers start from identical state and any divergence is purely a consequence of the order difference feeding an order-sensitive site.

**Test:** `OrderPerturbation_ModuleSlotList_SimIsOrderInsensitive_Headless` — an asymmetric 3v3 brawl (strong+weak mix to force real damage/module-death/repair/retarget) bounded to **300 turns**, JOIN peer's module order reversed on every ship, asserting every per-turn digest still matches.

**Result: GREEN.** The combat sim is ModuleSlotList-order-INSENSITIVE for these fleets — reversing module iteration order on one peer did NOT diverge the digest. So the repair/target/explosion `FindMax` tie-breaks, **as exercised in a same-process (identical-hardware) run, do not resolve differently under a reversed module list**. This is consistent with the prior report's note: with two bit-identical peers those ties resolve the same way, so a same-process soak stays green. It confirms an order-sensitive `ModuleSlotList` tie is NOT a same-process reproduction vector; if the live desync IS one of those ties, the trigger is a genuine cross-peer state asymmetry (or an FP difference feeding the tie), which only a real 2-machine run exposes. The harness now stands as a permanent regression guard AND the bisection instrument: if a future change makes any of those sites order-sensitive, this test flips RED and prints the bisected site via `DesyncFieldBreakdown`.

---

## 4. Reading the next live desync — FP drift vs logic/order bug

After the next live 2-machine reproduction, on EACH machine `grep DESYNC_FIELDS sim-output/arena-multiplayer-*.log`, then:

1. **Which ship diverged first** — diff the two machines' `roster=[...]` on the DIVERGING turn. The first `id` whose `HI:LO` differs is the culprit ship. (If NO ship digest differs but the wire checksum still mismatched, the divergence is in the EMPIRE/PLANET/RNG lanes, not the ship lane — inspect the empire RNG-state canary and economy fields; the ship dump will say `ship-lane-identical` in the in-process diagnosis.)
2. **Which field(s)** — for that ship, compare the two machines' `fieldsOf=` per-field `0x` bits (or run the in-process `DiagnoseHostVsJoin`, which prints the host-vs-join diff and a ULP distance directly).
3. **FP vs logic** — the classifier's rule, and how to read it yourself:
   - **`FP-DRIFT`** (cross-machine float inexactness — same code, different CPU FMA/rounding): several fields off by a **tiny** amount; on the FIRST diverging turn the ULP gap is `<= 8` (`UniverseStateFieldDump.FpDriftUlpThreshold`). This is the LOCKSTEP-across-two-different-CPUs hazard the prior report predicted (arena runs the sim independently on each machine; the match-length fix let it run long enough to accumulate). Fix direction: pin the float path (the `DeterminismProfile.MPSamePlatformPinnedFloat` build fingerprint already exists) / move the divergent math to fixed-point / restrict to same-CPU-arch pairings.
   - **`DISCRETE-FLIP`** (order/logic bug — a tie/branch resolved differently, or ship membership differs): one field jumps by **many** ULPs at once, or a ship is present on one peer and absent on the other. Fix direction: find the tie-break / branch that took a different path — start with the §3 `FindMax` sites and any iteration-order-tied RNG consumption on that ship.

**Caveat on the classifier (important):** the ULP threshold is a *hint on the first diverging turn*, where genuine FP drift is a 1-few-ULP tail. It is scale-independent (ULPs, not absolute units), so it holds whether the ship sits at the origin or at a 1e6 arena coordinate — note that a "small" absolute nudge like +3.0 near a 1e6 coordinate is ~48 ULPs and is (correctly) classified DISCRETE. The **raw ULP count and both `0x` bit patterns are always printed**, so a human can always override the label. If drift accumulates across many turns before the DesyncDetector fires, the first-divergence ULP gap can be larger than 8 — read the raw numbers, don't trust the label blindly.

---

## 5. Verification

- `dotnet build UnitTests/SDUnitTests.csproj --no-restore` — **green** (0 warnings, 0 errors).
- New tests, all **PASS**:
  - `DesyncFieldDump_LocalizesForcedDivergence_Headless` — a forced +X nudge is localized to the exact ship + `PosX`, classified `DISCRETE-FLIP`.
  - `OrderPerturbation_ModuleSlotList_SimIsOrderInsensitive_Headless` — reversed-module-order peer stays digest-matched for 300 turns (order-insensitive).
  - `UniverseStateFieldDump_MirrorsWireChecksumShipFold_Headless` — the field-dump per-ship fold equals the wire checksum's ship contribution byte-for-byte (guard).
- Focused suites `ArenaMultiplayerLockstep` + `ArenaCustomFleetKernel` + `ArenaDeterminism`: **52 passed, 6 failed**. The 6 failures (`PROOF_SETUP_ENTRY_BUTTONS_ROUTE_TO_REAL_EDITORS`, `PROOF_SETUP_AUTHORED_CUSTOM_REACHES_FIGHT`, `PROOF_SETUP_HANDOFF_DETERMINISTIC`, `PROOF_IMPORT_PRODUCES_ARENA_CUSTOM`, `PROOF_FORMATION_SPAWN_DETERMINISTIC`, `PROOF_BUDGET_ENFORCED_IN_SETUP`) are **PRE-EXISTING** — verified RED on a clean stash of the same trunk (6 failed / 0 passed) with my changes removed. My changes add 3 passing tests and introduce zero new failures.

## Files changed (working tree; not committed)

- `Ship_Game/Determinism/UniverseStateFieldDump.cs` (new) — the field-dump + host-vs-join diagnosis + FP/discrete classifier.
- `Ship_Game/GamePlayGlobals.cs` — `EnableArenaDesyncFieldDump` flag.
- `Ship_Game/GameScreens/Arena/ArenaMultiplayerTelemetry.cs` — `FieldDump(label, dump)` telemetry line.
- `Ship_Game/GameScreens/Arena/ArenaFightScreen.Multiplayer.cs` — live desync dump hook + prior-turn digest cache + perturbation seam + field-digest accessors.
- `Ship_Game/GameScreens/Arena/ArenaMultiplayerSession.cs` — in-process `DiagnoseFieldDivergence`, `RunInProcess` perturbation overload, `DesyncFieldBreakdown` on the result.
- `Ship_Game/Ships/Ship_ModuleGrid.cs` — `PerturbModuleOrderForTest()`.
- `StarDrive.csproj` — compile-include the new file.
- `UnitTests/Graphics/ArenaMultiplayerLockstepTests.cs` — the 3 new tests.

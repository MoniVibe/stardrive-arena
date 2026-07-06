# Arena After-Action Report — implementation report

Spec: `C:\dev\plans\STARDRIVE_ARENA_CUSTOM_FLEET_PROGRAM_PLAN_20260706.md` — **ADDENDUM 3 — AFTER-ACTION REPORT**.
Repo `C:\dev\stardrive\StarDrive-main`, branch `arena-045-port`, trunk `dbabb1495`, ProtocolVersion 5.
Status: implemented + test-verified. **Not committed** (left in the working tree for the orchestrator to verify/commit).

---

## What the director asked for

At fight end, alongside the existing Rematch/Lobby result panel: per-ship damage dealt, damage absorbed by
defenses, ships surviving, kills per ship — the feedback loop that makes fleet design worth iterating.

---

## The counter fields (pure observation) and where they accumulate

Three transient per-`Ship` fields, mirroring the `BonusEMPProtection` / `Pilot*Bonus` idiom
(`Ship_Game/Ships/Ship.cs`, right after the pilot-traits block). **NOT `[StarData]`, never hashed, never fed
back into the sim.** Default 0 → a pure no-op when nothing fires.

| Field | Meaning | Accumulation site |
|---|---|---|
| `Ship.ArenaDamageDealt` | damage this ship LANDED on enemy modules/shields (attacker credit) | `Ship.OnDamageInflicted(victim, damage)` override (`Ship.cs`) — the base-game damage callback, previously a no-op for Ship sources |
| `Ship.ArenaDamageTaken` | damage LANDED on this ship (defender debit) | `ShipModule.EvtDamageInflicted` (`ShipModule.cs` ~783) — mirrors the dealt credit with the SAME amount at the SAME site, so `sum(dealt) == sum(taken)` reconciles exactly |
| `Ship.ArenaDamageAbsorbed` | incoming damage this ship's shields/resistances NEGATED | `ShipModule.DamageShield` (~758, shield soak = incoming − remainder) + `ShipModule.Damage` (~800, resistance reduction when `damageModifier < 1`) |

`Kills` is the pre-existing `Ship.Kills` (read only, free). Survivors = alive-and-not-dying count at match end.

Because every increment happens at an already-deterministic damage-application site inside the shared lockstep
sim, **both peers compute byte-identical totals for free** — verified by the digest/report-identity proofs below.

## How they stay determinism-neutral

- The fields are plain public floats, **not `[StarData]`** → never serialized.
- `UniverseStateHash.WriteAuthoritative` (`Ship_Game/Determinism/UniverseStateHash.cs`) writes an explicit
  whitelist per ship (Id, Position, Velocity, Rotation, Health) — it was **not touched**, so the counters are
  outside the per-turn checksum by construction.
- Nothing in the sim reads the counters back — they are a pure read-out at the Resolve phase.
- **Proof result (digest-unchanged):** `AfterActionCountersAreOutsideTheChecksum_Headless` hashes authoritative
  state, bumps all three counters on every ship, re-hashes → the 128-bit checksum is byte-identical (low and
  high words). This is "add-counter run digest == baseline digest" at the field level. Additionally the
  match-level `AfterActionReportIsIdenticalOnBothPeersAndReconciles_Headless` re-runs an identical seed and gets
  the identical `FinalHash` with the counters present.

## Gather at Resolve

- New file `Ship_Game/GameScreens/Arena/ArenaAfterActionReport.cs`: `ArenaShipStatRow` (per-ship),
  `ArenaAfterActionSide` (survivors / kills / dealt / taken / absorbed + top-damage / best-absorber / top-killer
  highlights, deterministic tie-break by lowest ship id), and `ArenaAfterActionReport.Gather(hostShips, joinShips)`
  which reads the ships **in stable Id order** (identical on both peers). Includes dead ships so damage from ships
  that later died still counts. `Signature()` is a canonical one-line encoding used by the proofs.
- Live path: `ArenaFightScreen.CompleteMultiplayerLive` (Resolve) gathers the report once from
  `PlayerShips`/`EnemyShips` before showing the panel.
- Headless proof path: `RunTwoPeerLockstep` gathers each peer's report into
  `ArenaMultiplayerRunResult.HostAfterAction` / `.JoinAfterAction` (both peers in scope in-process), so the proof
  can assert byte-identical reports.

## What the panel shows (UI)

Reuses the existing `MultiplayerEndPanel` (`ArenaFightScreen.Multiplayer.cs` `ShowMultiplayerEndPanel`) — no new
screen. The panel is widened (760×340) to carry a second column:

- **Left column (unchanged):** winner, losses, turns/final-hash, flags, end reason, REMATCH / LOBBY.
- **Right column (AFTER-ACTION, new):** compact `DynamicText` labels — survivors per side, top damage-dealer
  (ship + total), best damage-absorber, top killer, total damage per side. Reuses `ArenaTheme.SectionHeader` /
  `BodySmallFont` / `TextSecondary`.
- **FULL REPORT** pill button toggles a `ScrollList<ArenaPopupListItem>` expansion below the panel: a per-ship
  breakdown (SHIP / SURV / KILLS / DEALT / TAKEN / ABSORBED), grouped by side, dead ships dimmed. Built lazily on
  first open. Reuses the existing `ArenaPopupListItem` row type (mono font).

Works for any arena MP match end (flag-independent results screen); with nothing to show, the report is all-zero
and the labels read "—".

## Per-weapon damage: DEFERRED

Per-SHIP is the deliverable and is complete. Per-WEAPON is deferred: the damage callback
`ShipModule.EvtDamageInflicted → source.OnDamageInflicted(victim, amount)` carries the attacker Ship and the
victim module, but **not the source weapon/mount context** — the projectile/beam is consumed before the Ship-level
callback. Threading weapon identity through `OnDamageInflicted` (a moderate signature change across the
projectile/beam paths) is the follow-up, exactly as the spec's survey flagged ("per-WEAPON needs threading weapon
context through the hook (moderate)").

## Files changed

- `Ship_Game/Ships/Ship.cs` — 3 transient counter fields + `OnDamageInflicted` override (damage-dealt credit).
- `Ship_Game/Ships/ShipModule.cs` — damage-absorbed accumulation (`DamageShield` shield soak + `Damage`
  resistance reduction) and damage-taken debit in `EvtDamageInflicted`.
- `Ship_Game/GameScreens/Arena/ArenaAfterActionReport.cs` — NEW; the report data model + `Gather`.
- `Ship_Game/GameScreens/Arena/ArenaFightScreen.Multiplayer.cs` — gather at Resolve; AAR summary column +
  FULL REPORT expansion on the end panel; headless seams.
- `Ship_Game/GameScreens/Arena/ArenaMultiplayerSession.cs` — `HostAfterAction`/`JoinAfterAction` on the run
  result; gather both peers in `RunTwoPeerLockstep`.
- `StarDriveArena.csproj` — compile `ArenaAfterActionReport.cs`.
- `UnitTests/Determinism/ArenaAfterActionReportTests.cs` — NEW; 4 proofs (see below).
- `UnitTests/SDUnitTests.csproj` — compile the new test.

## Proof results

Focused filters only; no full ~Arena suite; no stray testhost; nothing hung.

- **New `ArenaAfterActionReportTests` — 4/4 PASS:**
  - `AfterActionCountersAreOutsideTheChecksum_Headless` — mutating the counters does NOT change the authoritative
    128-bit checksum (digest-unchanged / zero determinism surface).
  - `AfterActionCountersAccumulateAndReconcileInDirectCombat_Headless` — real weapons fire accumulates dealt +
    absorbed; `sum(dealt) == sum(taken)` exactly, and cross-fire reconciles (A-dealt == B-taken and vice-versa);
    the gathered report reflects the same numbers.
  - `AfterActionReportIsIdenticalOnBothPeersAndReconciles_Headless` — a fixed-seed two-peer in-process match gathers
    BYTE-IDENTICAL reports on both peers; side totals equal the per-ship row sums; report-wide dealt == taken;
    survivors/start-counts match the sim snapshot; an identical-seed re-run reproduces the identical `FinalHash`
    AND the identical report (counters present do not perturb the sim).
  - `AfterActionEndPanelBuildsWithSummaryAndFullReport_Headless` — the end panel builds against a live
    `ArenaFightScreen`, exposes the Rematch/Lobby/FULL-REPORT controls and all five AAR summary labels, and the
    FULL REPORT ScrollList expansion materializes and collapses without throwing.
- **Regression (focused):** `ArenaMultiplayerLockstep` + `ArenaDeterminism` = 47/47 PASS; `ShipAICombat` = 16/16
  PASS. Pre-existing tests stay green.

## Notes for review

- The reconciliation pair is dealt↔taken (credited at the same site with the same amount → exact equality).
  "Absorbed" is a distinct defender-side quantity (damage the defenses NEGATED) and intentionally does not equal
  dealt/taken.
- Minor accounting nuance: the resistance-absorb line in `ShipModule.Damage` runs before the deflection check, so a
  fully-deflected sub-threshold shot can add a small resistance-reduction to "absorbed" with no corresponding
  dealt. This is deterministic (identical on both peers) and cosmetic; tighten later if the readout should exclude
  deflected shots.
- The two-peer pure-lockstep harness (`RunInProcess`) does not reliably drive AI weapon fire at every seed, so the
  "damage actually flows + reconciles" proof is the direct-combat unit test; the lockstep test proves report
  identity + determinism.

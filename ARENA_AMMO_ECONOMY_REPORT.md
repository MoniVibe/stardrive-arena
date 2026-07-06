# StarDrive Arena — Persistent Ammo + Repair/Rearm Economy — Implementation Report

Date: 2026-07-07. Branch: `arena-045-port` (trunk `3c9c34ac1`, ProtocolVersion **5**, unchanged).
Spec: `C:\dev\plans\STARDRIVE_ARENA_AMMO_ECONOMY_EXEC_PLAN_20260706.md`. Left in the working tree (not committed).

All three phases landed test-first with their named headless proofs. **113 focused tests green, 0 failures**;
both game projects (`StarDrive.csproj` + `StarDriveArena.csproj`) build clean. No protocol bump (append-only).
Default `UnlimitedAmmo=true` keeps a default build byte-identical to trunk.

New test file: `UnitTests/Determinism/ArenaAmmoEconomyTests.cs` (10 proofs G1–G7). Registered in `SDUnitTests.csproj`
(the project uses explicit `<Compile Include>`, not globbing — a new file must be added there).

---

## The determinism decision: INSTANCE flag, not a static (resolved as the spec flagged)

The spec suggested `ArenaFiniteAmmoActive` could be a **static** frozen at match start, but flagged the hazard that a
process-global static could make a same-process two-peer proof lie. **I used a per-ship INSTANCE flag instead** —
`Ship.ArenaFiniteAmmo` — threaded onto each ship at the `CreateArenaShipAtPoint` spawn choke, mirroring the existing
`Ship.ArenaCombatant` marker exactly.

Why this is strictly safer, confirmed against the harness's peer-isolation model:
- `ArenaMultiplayerSession.RunInProcess` builds **both peers in the SAME process** (`BuildPeerScreen` twice). A static
  `ArenaFiniteAmmoActive` would be shared mutable state: a UnlimitedAmmo=ON match running before/after a finite match
  (as G1/G3 do, back-to-back in one process), or the loopback host+join `Task.Run` pair, could cross-contaminate it.
- The instance flag eliminates the hazard entirely. The sim's regen gate reads a **per-ship** bool that was stamped at
  spawn from the fingerprinted ruleset — the same lockstep-safety argument that already holds for `ArenaCombatant`
  (set symmetrically at spawn, never mutated per-tick, no-op for 4X ships).
- A screen-level `ArenaFiniteAmmoActive` bool is resolved ONCE per spawn (MP: from the fingerprinted ruleset in
  `ConfigureMultiplayerPvP`; SP career: from the `CareerUnlimitedAmmo` config default) and stamped onto ships. It is
  **never read inside a sim tick** — only the per-ship instance flag is. So no process-global or per-peer state ever
  enters a lockstep digest.

A live bug this surfaced: `BuildPeerScreen`/`ConfigureMultiplayerPvP` spawn ships **before** `MultiplayerLiveSession`
is armed, so the original resolve site (`MultiplayerLiveSession?.Settings?.Ruleset`) fell back to the default and the
toggle never reached the ships. Fix: resolve finite-ammo in `ConfigureMultiplayerPvP` (which receives the full settings
on every MP path), proven by the stamp test `G1_FiniteAmmoFlag_StampsEveryArenaShipInstance`.

---

## Phase 1 — In-match finite magazine + host `UnlimitedAmmo` toggle

**What changed**
- `Ship.cs`: new transient instance bool `ArenaFiniteAmmo` (beside `ArenaCombatant`). Regen gate at
  `UpdateModulesAndStatus`: `if (OrdAddedPerSecond > 0f && !(ArenaCombatant && ArenaFiniteAmmo)) ChangeOrdnance(...)`.
- `ArenaMultiplayerRuleset.cs`: new `bool UnlimitedAmmo = true` (DEFAULT TRUE = today's regen). Added to `Clone()` and
  folded into `AppendTo` **appended LAST after `SetupPhase`** (append-only, no protocol bump). A divergent toggle
  changes `SettingsHash` → rejects at the handshake.
- Wire plumbing (mirrors `RulesetSetupPhase` exactly, one hop further, **default true on absence**):
  `SessionMessages.RulesetUnlimitedAmmo`; `LockstepMessageCodec` write/read (read defaults to true at end-of-stream);
  `ArenaMultiplayerSession.ToStartMessage`/`RulesetFromStartMessage`; `ArenaBattleCodes` FormatVersion 3→4 (a format-3
  code replays with UnlimitedAmmo defaulting true).
- Spawn choke `CreateArenaShipAtPoint` (both overloads): new `bool finiteAmmo=false` param stamps `ship.ArenaFiniteAmmo`.
  SP `SpawnPlayerShips` + the 4 SP enemy spawns pass `ArenaFiniteAmmoActive` (resolved from `CareerUnlimitedAmmo`);
  MP `SpawnMultiplayerFormation` passes the value resolved in `ConfigureMultiplayerPvP` from the ruleset.
- Gate site 2 (SP screen-side leak): `RearmArenaShip` early-returns when `ship.ArenaFiniteAmmo` (career ships run dry).
- Host lobby pill `arena_mp_ammo` (`AMMO UNLIMITED`/`AMMO FINITE`), host-gated via `HostSettingsAreLockedToRemote`,
  folded into the built ruleset (`BuildArenaRuleset.UnlimitedAmmo = RequestUnlimitedAmmo`, default true).

**Determinism-safe because** finite ammo runs in the sim, but the gate keys off the symmetric per-ship `ArenaFiniteAmmo`
(instance, spawn-stamped, never per-tick) AND the fingerprinted `UnlimitedAmmo` toggle. Default true = no-op = trunk.

**Proofs (green)**
- **G1** `FiniteAmmoShip_RunsDry_UnlimitedShipRegens`: a finite arena combatant's drained magazine does NOT regen under
  repeated `UpdateShipStatus(1s)` ticks (directly exercises the gated line); an UnlimitedAmmo ship of the same hull DOES.
- **G1** `FiniteAmmoMatch_TwoPeersStayInSync`: a real 2-peer in-process finite match — no desync, all turn hashes match.
- **G1** `FiniteAmmoFlag_StampsEveryArenaShipInstance`: finite → every arena ship carries `ArenaFiniteAmmo`; unlimited → none.
- **G2** `DivergentUnlimitedAmmoToggle_RejectsAtHandshake`: flipping only the toggle mismatches `SettingsHash` →
  `ValidateStartMessage` rejects; identical toggles validate clean; the two toggle values yield different hashes.
- **G3** `UnlimitedAmmoDefault_IsAByteIdenticalNoOp`: explicit `UnlimitedAmmo=true` reproduces the default-ruleset
  lockstep digest exactly.

---

## Phase 2 — Persistent ammo state (SP career; career-vs-career deferred)

**What changed**
- `ArenaCareer.OwnedVessel`: new `[StarData] float CurrentOrdnance, MaxOrdnance` (exact twin of `CurrentHullHealth`/
  `MaxHullHealth`; `0` == "spawn full"; additive → old saves default 0 → spawn full).
- `NormalizeForPersistence`: clamps both ≥ 0 and folds `Current >= Max` → 0 (full), beside the hull-scar clamp.
- Bank: `RecordSurvivingVesselScars` records `MaxOrdnance = ship.OrdinanceMax` and `CurrentOrdnance = ship.Ordinance`
  (or 0 when full), **gated on `ArenaFiniteAmmoActive`** — under UnlimitedAmmo it always banks 0 so ammo never persists.
- Re-apply: new `ApplyCarriedVesselOrdnance(ship, vessel)` (twin of `ApplyCarriedVesselHullState`) using `SetOrdnance`
  (direct, clamped, deterministic), called at the SP spawn choke right after the hull re-apply.

**Determinism-safe because** the re-apply sets ordnance from a persisted career value at spawn, before the sim ticks —
identical to the proven scar/veterancy re-apply. Never touches a live sim tick.

**Proofs (green)**
- **G4** `PersistedOrdnance_ReAppliesDeterministicallyAtSpawn`: a fresh vessel spawns full; a vessel with a persisted
  60%-magazine re-spawns at exactly that value (not full); OrdinanceMax stable across spawns.
- **G5** `OldSaveWithoutAmmoFields_SpawnsFull`: a vessel with the ammo fields at 0 (how a pre-field save deserializes)
  spawns at full OrdinanceMax — no regression.
- **G5b** `NormalizeForPersistence_ClampsAndFoldsAmmoToFull`: negatives clamp to 0, full folds to 0, partial preserved.

---

## Phase 3 — Rearm cost (between-match SP economy, mirrors repair 1:1)

**What changed** (all in `ArenaFightScreen.cs`, mirroring the repair economy exactly)
- `ArenaRearmResult` struct (twin of `ArenaRepairResult`); `const int RearmCost = 40` (beside `RepairCost=50`, tunable).
- `CurrentAmmoSpentFraction()` (twin of `CurrentRepairDamageFraction()`) = `sum(spent)/sum(OrdinanceMax)` over fielded +
  surviving owned vessels; `CurrentRearmAllCost()` = `ArenaPerks.RepairCost(RearmCost, Perks) * spentFraction`;
  `CountRearmableOwnedVessels()`; `CurrentRearmCost` property.
- `RearmAllFromHub()` (twin of `RepairAllFromHub()`): gates on `Phase == Shopping|Idle` + `Cash >= cost`, spends
  `Cash`, tops live ships to `OrdinanceMax` via `SetOrdnance`, zeroes each vessel's `CurrentOrdnance`/`MaxOrdnance`
  (full), persists `Career.Cash`, and saves.
- **Perk reuse:** v0 routes through `ArenaPerks.RepairCost` so the `repair_crews` discount extends to rearm.
- UI: `RearmButton` ("Rearm All ($cost)") beside `RepairButton` (stack shifted down 52px), `OnClick = BuyRearm`.
  `RefreshShop` labels/enables it and **hides it when cost is 0** (nothing to pay for under UnlimitedAmmo).

**Determinism boundary (critical, honored)** — the rearm spend is BETWEEN-MATCH client economy: it runs only in
`Phase == Shopping|Idle`, mutates `Career.Cash` + `OwnedVessel` records + saves, and NEVER touches a sim tick. Cash /
persisted-ammo differences between peers can never desync because the sim reads neither — it reads only the per-ship
`ArenaFiniteAmmo` (fingerprinted). Mirrors the existing repair spend exactly.

**Proofs (green)**
- **G6** `RearmAllFromHub_RestoresAmmoForCash_HonorsRepairCrewsDiscount`: rearm restores fielded vessels to full ammo,
  charges exactly the displayed cost, decrements cash, saves the career (banked `CurrentOrdnance` = 0), rejects when
  cash < cost, and the `repair_crews` discount lowers the base cost.
- **G7** `RearmSpend_IsBetweenMatchOnly_RefusesDuringFight`: rearm refuses during `Fighting` (cites "between fights",
  spends no cash, restores no ammo); the same call succeeds in `Shopping` — the guard is phase, not content.

---

## Verification summary

- New ammo proofs: **10/10 green** (G1×3, G2, G3, G4, G5, G5b, G6, G7).
- Regression (focused filters, all green): `ArenaMultiplayerLockstep`+`ArenaCustomFleetKernel` **59**;
  `ArenaDeterminism`+`ShipAICombat`+`ArenaCareer` **27**; `BattleCode`+`ArenaPortableFingerprint`+`ArenaCustomFleetUi`+
  `ArenaAfterActionReport` **12**; `ArenaMultiplayerLobby` **5**. **113 total, 0 failures.**
- **Lockstep digest UNCHANGED with UnlimitedAmmo=default**: the 59 lockstep/custom-fleet tests (default rulesets) pass,
  and G3 proves explicit-on == default digest — no sync regression.
- `dotnet build UnitTests/SDUnitTests.csproj`, `StarDrive.csproj`, `StarDriveArena.csproj` all build clean.

## Deferred / out of scope (per spec)
- Career-vs-career persisted-ammo EXCHANGE between two peers (rides with career-vs-career later). Phase 2 is scoped to
  the SP career loop + deterministic re-apply, exactly as the spec directed.
- Per-weapon/per-magazine ammo; ammo as a fleet-lock build cost; a distinct `RearmDiscount` perk (v0 shares
  `repair_crews`); a reduced-regen middle-ground knob (v0 is the clean binary regen ON/OFF).

## Files changed
- `Ship_Game/Ships/Ship.cs` — `ArenaFiniteAmmo` field + regen gate.
- `Ship_Game/GameScreens/Arena/ArenaMultiplayerRuleset.cs` — `UnlimitedAmmo` field, `AppendTo`, `Clone`.
- `SDLockstep/SessionMessages.cs`, `SDLockstep/LockstepMessageCodec.cs` — wire field (append-only, default true).
- `Ship_Game/GameScreens/Arena/ArenaMultiplayerSession.cs` — `ToStartMessage`/`RulesetFromStartMessage`.
- `Ship_Game/GameScreens/Arena/ArenaBattleCodes.cs` — FormatVersion 3→4, write/read.
- `Ship_Game/GameScreens/Arena/ArenaFightScreen.Multiplayer.cs` — resolve finite-ammo in `ConfigureMultiplayerPvP`;
  thread through `SpawnMultiplayerFormation`.
- `Ship_Game/GameScreens/Arena/ArenaFightScreen.cs` — `ArenaFiniteAmmoActive`/`CareerUnlimitedAmmo`; spawn-choke
  threading; SP rearm-leak gate; ammo bank/re-apply; rearm economy (cost/action/struct/UI); headless ordnance accessor.
- `Ship_Game/GameScreens/Arena/ArenaCareer.cs` — `CurrentOrdnance`/`MaxOrdnance` + normalization.
- `Ship_Game/GameScreens/Arena/ArenaMultiplayerLobbyScreen.cs` — AMMO pill + toggle + ruleset fold.
- `UnitTests/Determinism/ArenaAmmoEconomyTests.cs` (new), `UnitTests/SDUnitTests.csproj` (include).

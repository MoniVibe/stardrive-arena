# Arena Pilot/Crew Leveling — Layer 1 Implementation Report

Date: 2026-07-05. Repo: `C:\dev\stardrive\StarDrive-main`, branch `arena-045-port`.
Plan followed: `C:\dev\plans\STARDRIVE_ARENA_PILOT_SYSTEM_PLAN_20260705.md` (advisor ruling).
Scope: **Layer 1 only** — SP-only, flag-gated, default-OFF. No MP path touched. No balance change
to any existing system (default-off is a proven no-op). Changes left in the working tree (uncommitted)
for the orchestrator to verify and commit.

---

## The flag

`GamePlayGlobals.EnablePilotTraits` — a `[StarData] public bool` (default **false**) added to the
feature-flags block of `Ship_Game/GamePlayGlobals.cs` (the type of `GlobalStats.Defaults`,
deserialized from `Content/Globals.yaml`, tunable). ALL new mechanical behavior is gated on it. With
it off, every new field stays 0 and the sim is byte-for-byte identical to today (proven by the
flag-off no-op proof below).

---

## Trait catalog (`PilotTraitV0`)

New file `Ship_Game/GameScreens/Arena/PilotTraitV0.cs`, mirroring the `ArenaPerks.Catalog`
static-array idiom (the established sibling pattern for arena content — the project has YAML via
`[StarData]`/`YamlParser`, but perks and similar catalogs are hardcoded static arrays, so this
matches surrounding style). Four traits, one per already-live mechanical channel, Ordinal-sorted by id:

| id                    | name                | levelReq | kind      | value | effect |
|-----------------------|---------------------|----------|-----------|-------|--------|
| `eagle_eye`           | Eagle Eye           | 2        | Accuracy  | 0.15  | -15% weapon target error |
| `gunnery_drill`       | Gunnery Drill       | 3        | Damage    | 0.08  | +8% weapon damage (separate channel) |
| `predictive_tracking` | Predictive Tracking | 4        | Tracking  | 1     | +1 tracked target |
| `evasive_ace`         | Evasive Ace         | 5        | Evade     | 10    | +10 explosion-evade points |

- `enum PilotTraitKind { Accuracy, Damage, Tracking, Evade }` — each maps 1:1 to one additive `Ship`
  field. **Family-blind:** no kind reads target family/type (enforced structurally + by catalog lint).
- Schema carries the DEFERRED Layer-3 fields (`Branch`/`Excludes`/`PointCost`) but the v0 path ignores
  them: traits are **auto-granted at level thresholds**, no point-buy, no respec.
- Pure helpers: `GrantedTraitsForLevel(level)`, `ComposeShipEffects(traitIds)`, `ComposeForLevel(level)`,
  `ApplyToShip(ship, effect)`, `Normalize` (drops unknowns, de-dupes, Ordinal-sorts — canonical for
  future MP hashing). All are pure functions of (level/ids, static catalog) — no empire, RNG, or clock.

Values are placeholders per the director's no-locked-balance mandate.

---

## XP -> level (no parallel counter)

Reuses the engine's existing per-`OwnedVessel` `Experience`/`Level`/`Kills` veterancy and the engine's
`ExpPerLevel * (1 + Level)` curve, clamped 0..10 (`Ship.AddToShipLevel`). Pilot level == ship crew
level — there is **no second curve and no parallel counter**. At bank time,
`ArenaFightScreen.MirrorPilotVeterancyToCaptain` mirrors the fielded vessel's `Level` into the
captain record (`ArenaCaptain.Level`, monotonic) and records the auto-granted ids in a new
`ArenaCaptain.GrantedTraits` field (display/hash record; ejection retains traits because they live
on the captain). Runs regardless of the flag so the derived record stays consistent — the flag only
gates whether traits have any mechanical effect.

---

## Choke-point wiring (determinism-critical)

**Effect application:** `ArenaFightScreen.ReapplyVeterancy()` — in the SAME loop that writes
`s.Experience/Level/Kills = v.*`, and in the legacy single-gladiator fallback branch, a one-line call
to the new private helper `ApplyPilotTraits(s, v)`:

```csharp
if (s == null || v == null || !GlobalStats.Defaults.EnablePilotTraits) return;
ArenaCaptain captain = Career?.CaptainForVessel(v);
int pilotLevel = captain?.Level > 0 ? captain.Level : v.Level;
ShipTraitEffect effect = PilotTraitV0.ComposeForLevel(pilotLevel);
PilotTraitV0.ApplyToShip(s, effect);   // overwrites the additive Pilot*Bonus fields (idempotent)
```

`ComposeForLevel` is a pure function of (pilot level, static catalog) — identical inputs give an
identical `ShipTraitEffect` on any peer. No `Empire.data` mutation, no RNG, no wall-clock. This is a
SEPARATE additive channel; it never re-multiplies `s.Level`.

**Additive per-`Ship` fields** (new, non-serialized, default 0 — mirror the `BonusEMPProtection`
pattern in `Ship.cs`):
`PilotAccuracyBonus` (float), `PilotDamageBonus` (float), `PilotTrackingBonus` (int),
`PilotEvadeBonus` (float).

**Read-site folds** (each an additive term, pure no-op at 0):
- Damage: `WeaponTemplate.GetDamageWithBonuses` — `damageAmount += damageAmount * PilotDamageBonus`
  applied AFTER the veterancy `+5%/level` term, i.e. `veterancy_damage * (1 + bonus)`. Does NOT bump
  Level, so it never compounds recursively through targeting/tracking/evade/turn-rate.
- Accuracy: `Weapon.BaseTargetError` — final return folds `* (1 - PilotAccuracyBonus)`, exactly like
  the existing empire `Traits.TargetingModifier` fold (lower target error = more accurate).
- Tracking: `Weapon` PD + normal target selection — `maxTracking = 1 + TrackingPower + Level + PilotTrackingBonus`.
- Evade: `Ship.ExplosionEvadeBaseChance` — `explosionEvadeBaseChance += PilotEvadeBonus`, alongside `+Level`.

---

## Headless proofs (RED -> GREEN)

New test class `UnitTests/Determinism/ArenaPilotTraitsTests.cs` (MSTest, inherits `StarDriveTest`,
class name contains "Arena" so `Run-ArenaSuiteChunked.ps1` picks it up). `[TestCleanup]` restores
`EnablePilotTraits = false` so the flag never leaks.

1. **`PilotTraits_XpToLevelGrant_IsDeterministicAndLevelDerived_Headless`** — same kills derive the
   same level reproducibly via the engine curve (clamped 0..10, monotonic); the auto-granted trait set
   is a pure, reproducible, monotonic function of level (0 traits at L0/L1, `eagle_eye` at L2, all four
   Ordinal-sorted at L6).
2. **`PilotTraits_FlagOffIsNoOp_FlagOnChangesOutcomeReproducibly_Headless`** — a fixed-seed serial duel:
   flag OFF is deterministic AND a true no-op (requesting a pilot edge does not change the digest);
   flag ON with the full v0 trait set on the player yields a DIFFERENT but per-run-STABLE digest.
3. **`PilotTraits_GunneryDrill_IsSeparateChannel_NoLevelDoubleApply_Headless`** — a Level-6 veteran's
   `gunnery_drill` damage equals exactly `veterancy_damage * (1 + 0.08)` (asserted to 1e-5), the Level
   number is unchanged, and the delta is a clean multiple of veterancy damage — guarding against any
   double-apply through the Level multiplier.
4. **`PilotTraits_Catalog_IsFamilyBlindAndOffByDefault_Headless`** — flag defaults false; empty grant
   composes to zero; exactly 4 traits on 4 distinct channels; `Normalize` is canonical.

**RED verification:** with the four mechanical read-site folds temporarily neutralized, proofs 2 and 3
FAIL (proof 3 saw 53.3 vs the expected 57.564 = 53.3*1.08; proof 2 saw the ON digest equal the OFF
digest), while the pure-catalog proofs 1 and 4 stayed green. Restoring the folds returns all four to
GREEN — confirming the folds (not the test scaffolding) produce the additive channel.

### Results
- New proofs: **4 passed / 0 failed**.
- Build: `dotnet build UnitTests/SDUnitTests.csproj --no-restore` — **0 warnings, 0 errors**.
- Pre-existing focused regression (per-class filters, not the combined single-process suite):
  `TestWeaponArcs` 4P/1 pre-skip, `ArenaDeterminismPatchContractTests` 2P,
  `StarDriveFleetVsFleetTests` 6P, `StarDriveAutobattleTests` 2P,
  `ArenaFleetVeterancyBank_Headless` 1P, `ArenaGarageRefreshDoesNotReduceBankedVeterancy_Headless` 1P,
  `ArenaGarageFleetDisplayLiveKillsAndLevel_Headless` 1P, `~Captain` 1P, `~ArenaCareer` 9P.
  **All green.** (The combined `~Arena` single-process suite was intentionally NOT run — known
  intermittent native crash; per-class filters used instead.)

---

## Files changed

New:
- `Ship_Game/GameScreens/Arena/PilotTraitV0.cs` — catalog, enum, `ShipTraitEffect`, pure compose/apply.
- `UnitTests/Determinism/ArenaPilotTraitsTests.cs` — the 4 headless proofs.

Modified:
- `Ship_Game/GamePlayGlobals.cs` — `EnablePilotTraits` flag (default false).
- `Ship_Game/Ships/Ship.cs` — 4 additive `Pilot*Bonus` fields; evade fold.
- `Ship_Game/Gameplay/WeaponTemplate.cs` — damage fold.
- `Ship_Game/Gameplay/Weapon.cs` — accuracy fold + 2 tracking folds.
- `Ship_Game/GameScreens/Arena/ArenaCareer.cs` — `ArenaCaptain.GrantedTraits` field.
- `Ship_Game/GameScreens/Arena/ArenaFightScreen.cs` — `ApplyPilotTraits` at the choke point (both
  branches) + `MirrorPilotVeterancyToCaptain` at bank.
- `StarDriveArena.csproj`, `UnitTests/SDUnitTests.csproj` — Compile includes for the two new files
  (both projects use explicit includes, not globbing).

---

## Deferred to Layer 2 / 3 (unchanged from the plan's scope guard)

- **Layer 2 (batch with P1 RulesetV0):** serialize per-slot pilot loadout into the MP fleet manifest;
  add `AddPilotLoadout` to the `DetHash` fingerprint; apply `ComposeShipEffects` in
  `SpawnMultiplayerFleet` (resolving the pre-existing MP Level-0 asymmetry EXPLICITLY, now hashed).
  `PilotTraitV0.Normalize` already produces the canonical sorted id list Layer 2 will hash.
- **Layer 3:** point-buy respec, trait branching / mutual-exclusion (schema fields already present),
  multi-crew stations/roles, injuries/fatigue, damage/survival/win XP sources (v0 = kills only), trait
  inheritance to a protégé, memorial trait display, creative-MP point-capped presets.

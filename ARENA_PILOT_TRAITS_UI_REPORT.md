# Arena Pilot-Trait System — In-Game Playtest UI Report

Date: 2026-07-06. Repo: `C:\dev\stardrive\StarDrive-main`, branch `arena-045-port`.
Scope: make the flag-gated Layer-1 pilot-trait system reachable from inside the game (previously only
by hand-editing `Content/Globals.yaml`). SP-only, no balance change, no MP path touched. Changes left
uncommitted in the working tree for the orchestrator to verify and commit.

---

## 1. In-game toggle — where it landed

**Screen: `ArenaConfigScreen`** (`Ship_Game/GameScreens/Arena/ArenaConfigScreen.cs`) — the arena
CONFIG popup that already exposes the run's gameplay toggles (player-ship permadeath, hard-loss-ends-run,
contender permadeath chance). This is the correct home: it is the one arena screen whose whole job is
gameplay run-settings, and it already owns the persist-and-refresh idiom.

Added, mirroring the screen's existing `AddToggleRow` / `AddChanceRow` widget idiom:
- **"Pilot Traits (veteran perks)"** — an ON/OFF pill row bound to `GlobalStats.Defaults.EnablePilotTraits`
  (the master flag the fight reads). Always shown.
- **"Trait level source"** — a PILOT/SHIP pill row bound to `GlobalStats.Defaults.PilotTraitScopeVessel`
  (`false`=Pilot/Captain-carried, `true`=Ship/ship-bound). Only rendered when the master is ON, since it
  is only meaningful then. The panel grows one row (510→580px) when the scope row appears so the BACK
  footer stays clear.

**Persistence** mirrors the sibling toggles exactly. The siblings persist on the `ArenaCareer` save via
`Arena.Set*(...)` → `ManualSaveCareer()`. Because the two pilot flags live on `GamePlayGlobals`
(`GlobalStats.Defaults`, loaded from yaml at boot and NOT written back per-run), a write-through pair was
added so the choice both takes effect live AND survives save/load like the other settings:
- New `[StarData] bool EnablePilotTraits` / `[StarData] bool PilotTraitScopeVessel` on `ArenaCareer`
  (default false = today's behavior).
- New `ArenaFightScreen.SetEnablePilotTraits` / `SetPilotTraitScopeVessel` write BOTH `Career.*` (persist)
  and `GlobalStats.Defaults.*` (live, what the fight reads), then `ManualSaveCareer()` — same return-bool
  success contract as `SetPlayerShipsPermadeath`.
- `ArenaFightScreen.ApplyCareer()` re-applies the persisted `Career.*` flags onto `GlobalStats.Defaults`
  on career load, so a re-opened career restores its playtest setting. A fresh career early-outs of
  `ApplyCareer`, so the default stays OFF (no-op).
- Read-side accessors `CurrentEnablePilotTraits` / `CurrentPilotTraitScopeVessel` on `ArenaFightScreen`
  for the screen to render current state (mirroring `CurrentPlayerShipsPermadeath`).

## 2. In-fight readout — where it shows

**`ArenaFightScreen.Draw`** now calls a new flag-gated `DrawPilotTraitReadouts(batch)`. When
`GlobalStats.Defaults.EnablePilotTraits` is ON, it draws a small centered line just under each managed
arena ship (player + enemy) showing the crew **Level** and its **active pilot-trait NAMES**, e.g.
`Lv 6 — Eagle Eye, Evasive Ace, Gunnery Drill, Predictive Tracking`, or `Lv 1 — no traits yet` below the
first threshold.

- Reuses the existing per-ship draw idiom: iterates `PlayerShips`/`EnemyShips` gated by the same
  `ShouldDrawArenaModuleOverlayFallback` visibility filter the overlay path uses, projects
  `ship.Position` via the base `ProjectToScreenPosition`, and draws with `Fonts.Arial12Bold` +
  `batch.DrawString` + `ArenaTheme.TextPrimary` — the same font/color/DrawString pattern already used for
  HUD text in `UniverseScreen.Draw`. No new panel invented.
- The displayed level comes from `ResolvePilotDisplayLevel(ship)`, which mirrors `ApplyPilotTraits`'
  scope selection exactly (player ships resolve their `OwnedVessel` → captain-vs-vessel per the scope
  flag; enemy/wingman ships fall back to `Ship.Level`), so the shown level matches the level the effect
  was composed from.
- **Flag OFF = true no-op:** the method returns immediately with zero extra draw calls and zero visual
  change. Read-only display only — it never mutates the sim.

Trait names come from a new pure helper on `PilotTraitV0`:
- `NamesForLevel(int level)` — the Catalog display names for the traits granted at that level, in the
  same canonical Ordinal-by-id order as `GrantedTraitsForLevel`.
- `DescribeForLevel(int level)` — the exact one-line readout string (`"Lv N — A, B"` /
  `"Lv N — no traits yet"`). Pure function of (level, static catalog); no I/O, no RNG.

---

## Files changed

Modified:
- `Ship_Game/GameScreens/Arena/ArenaConfigScreen.cs` — pilot-traits master toggle row + conditional
  Pilot/Ship scope row + handlers, panel-height grow, mirroring existing toggle idiom.
- `Ship_Game/GameScreens/Arena/ArenaFightScreen.cs` — `Set/CurrentEnablePilotTraits`,
  `Set/CurrentPilotTraitScopeVessel`, `ApplyCareer` re-apply of persisted flags,
  `DrawPilotTraitReadouts` + `ResolvePilotDisplayLevel` in the fight draw path.
- `Ship_Game/GameScreens/Arena/ArenaCareer.cs` — `[StarData] EnablePilotTraits` /
  `PilotTraitScopeVessel` persistence fields (default false).
- `Ship_Game/GameScreens/Arena/PilotTraitV0.cs` — pure `NamesForLevel` + `DescribeForLevel` readout
  helpers.
- `UnitTests/Determinism/ArenaPilotTraitsTests.cs` — new headless proof
  `PilotTraits_Readout_ListsGrantedTraitNamesForLevel_Headless`.

No new files. No project include changes needed (edits are to already-included files).

---

## Verification

- Build `dotnet build UnitTests/SDUnitTests.csproj --no-restore` — **0 warnings, 0 errors** (builds
  StarDrive.dll + StarDriveArena.dll + UnitTests.dll).
- Build `dotnet build StarDriveArena.csproj --no-restore` — **0 warnings, 0 errors** (game builds).
- Focused tests (per-class filters; the combined `~Arena` single-process suite intentionally NOT run):
  - `~PilotTraits_Readout_ListsGrantedTraitNamesForLevel` — **1 passed** (new readout proof).
  - `~ArenaPilotTraitsTests` (whole class) — **9 passed / 0 failed** (8 pre-existing + new; no regression).
  - `~ArenaCareer` — **9 passed / 0 failed** (confirms the new `[StarData]` fields serialize cleanly).
  - Stray `testhost` was killed before each run per the harness note.

## Constraints honored
- Flag-gated: OFF = true no-op (no new UI drawn in-fight; toggle merely reflects the default-false flag).
- Reuses existing widgets (`AddToggleRow`/pill idiom) and the existing per-ship draw + HUD-text idiom —
  no new panel invented.
- SP-only: only `ArenaCareer`/`ArenaFightScreen` SP career path touched; the MP spawn path
  (`EnableArenaPilotTraitsInMultiplayer`, `SpawnMultiplayerFleet`) is untouched.
- No balance change: placeholder trait values unchanged; display + toggle only.

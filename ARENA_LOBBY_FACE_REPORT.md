# Star Gladiator Multiplayer Lobby — Arena Face + Fleet Picker

Branch `arena-045-port` (trunk 55d19705a). Working-tree changes only — NOT committed/pushed.

## Problem

`ArenaMultiplayerLobbyScreen` serves two surfaces (`StarGladiator` | `Authoritative4X`) via
the `Surface` field, but built the SETUP grid and the 8-slot player loop UNCONDITIONALLY. The
Star Gladiator 1v1 fleet-duel surface therefore showed the whole 4X galaxy-generation shell
(SIZE/STARS/DIFF/EMP/RICH/EXTRA/REM/PACE/TURN/DECAY/VOLC/MAINT/FTL/GW/PIRATES/STORY/OPS/AI RULES
+ the 4X "MODE" selector) and 8 player slots. The arena Career/Sandbox mode had no UI (only
`SetArenaModeForHeadless`), and the fleet was auto-derived with the "Fleet: X" text a read-only
label — no picker.

## What each surface shows now

### StarGladiator (arena duel) surface
- **Header**: STAR GLADIATOR / MULTIPLAYER LOBBY (unchanged).
- **Slots**: exactly 2 — host P2 + one joiner P3 (section header "COMBATANTS").
- **Arena controls (new)**:
  - `arena_mp_arena_mode` — `ARENA <CAREER|SANDBOX>`, wired to new `CycleArenaMode()`. Flips the
    `ArenaMode` field and mirrors `SetArenaModeForHeadless`'s effect on `ArenaBudgetModel`
    (Career forces Unlimited; Sandbox retains the cap).
  - `arena_mp_budget` — `BUDGET <UNLIMITED|CAP N>`, wired to new `CycleBudget()`. Visible/enabled
    only in Sandbox. Cap steps 5000→10000→15000→20000→Unlimited.
  - `arena_mp_set_fleet` — `SET FLEET (n)`, opens the new fleet picker (below).
- **Kept loadout/flow controls**: `arena_mp_race`, `arena_mp_trait`, `arena_mp_trait_toggle`
  (loadout flavor), `arena_mp_start_paused` (START LIVE/PAUSED), `arena_mp_peer_slot` (SLOT).
- **4X galaxy chrome**: NONE built (all 19 RegularSettings.* pills + the 4X `arena_mp_mode` are gone).
- **Bottom bar**: HOST / JOIN / READY / LAUNCH / SELF TEST / BACK (unchanged).
- Host/port/seed/speed entries kept; no turns field.

### Authoritative4X surface — UNCHANGED
- Header STARDRIVE MULTIPLAYER / AUTHORITATIVE 4X LOBBY.
- Full 4X galaxy-generation pill grid + 4X MODE selector, up-to-8 slots (P2–P9) with
  per-slot mode/kick. Arena-mode/budget/fleet controls are NOT built here.

## Fleet-picker approach

New popup `ArenaFleetPickerScreen` (`Ship_Game/GameScreens/Arena/ArenaFleetPickerScreen.cs`),
modeled on the existing `ArenaFleetScreen` idiom (`GameScreen(parent, toPause:null)`, `IsPopup`,
`ScrollList<ArenaPopupListItem>` with toggle-and-rebuild, DONE/BACK actions). It is the interim
name/vessel picker — the drag-drop `FleetDesignScreen` formation editor stays DEFERRED.

- **Options source** (`FleetPickerOptions`): Career → the career's OWNED vessels' designs
  (`ArenaCareer.OwnedVessels`, filtered to legal combat craft). Sandbox → every legal arena
  combat-craft design (`IsLegalArenaFleetDesignName` over `ResourceManager.Ships.Designs`).
- **Budget** (Sandbox + Cap only): running credits line using each design's `BaseCost`; a pick
  that would exceed the cap is denied.
- **Commit** (`ApplyPickedFleet`): normalizes to legal names, pins them in a new
  `ManualFleetDesignNames` field, writes `LocalPeer.FleetDesignNames`, saves config, and refreshes
  the SET FLEET count + the slot "Fleet: ..." label. `ApplyLocalSelection` now honors the manual
  pin instead of always auto-deriving, so the choice sticks.
- **Deterministic-safe**: only legal fleet DESIGN NAMES that already ride the P1 bundle path are
  set — no wire/hash/protocol change.

## Files changed

- `Ship_Game/GameScreens/Arena/ArenaMultiplayerLobbyScreen.cs`
  - `StarGladiatorLastJoinPeerSlot` const; `HighestVisibleSlot` surface-scoped bound (slot loop +
    `CycleJoinSlot` now respect it).
  - `ManualFleetDesignNames` field; `ApplyLocalSelection` honors it.
  - `LoadContent` slot header + loop gated by surface; SETUP split into `BuildAuthoritative4XSetup`
    (the extracted original 4X pill grid) and `BuildStarGladiatorSetup` (new arena controls).
  - New: `CycleArenaMode`, `CycleBudget`, `BudgetLabel`, `RefreshSetup`, `OpenFleetPicker`,
    `FleetPickerOptions`, `CareerOwnedFleetDesignNames`, `ApplyPickedFleet`.
  - Headless seams: `FleetPickerOptionsForHeadless`, `LocalFleetDesignNamesForHeadless`,
    `ArenaBudgetModelForHeadless`, `ArenaBudgetCreditsForHeadless`, `CycleArenaModeForHeadless`,
    `CycleBudgetForHeadless`, `SetFleetForHeadless`, `OpenFleetPickerForHeadless`.
- `Ship_Game/GameScreens/Arena/ArenaFleetPickerScreen.cs` — new picker popup.
- `StarDriveArena.csproj` — added the new file to the explicit compile set.
- `UnitTests/Graphics/ArenaMultiplayerLockstepTests.cs` — rewrote
  `ArenaMultiplayerLobbyEntryAndSelfTest_Headless`: on the StarGladiator surface asserts the 19 4X
  pills + `arena_mp_mode` are ABSENT, the arena-mode/set-fleet controls present, `CycleArenaMode`
  flips Career↔Sandbox (and toggles the BUDGET pill), a Sandbox fleet pick updates
  `LocalPeer.FleetDesignNames`, and only slots P2–P3 exist; on a fresh Authoritative4X-surface
  lobby asserts the 4X pills + P2–P9 slots STILL build and the arena controls do NOT.

## Test results

- `dotnet build UnitTests/SDUnitTests.csproj` (transitively StarDriveArena) — succeeded, 0 warnings/errors.
- `dotnet test --no-build --filter FullyQualifiedName~ArenaMultiplayerLockstep` — **28/28 passed**
  (~64 s), including the rewritten entry test and the real lobby→launched-fight drive tests.
- Pre-existing tests stay green; no other test file asserts arena lobby pill/slot layout.

# StarDrive Arena — CUSTOM-FLEET USER-FACING ENTRY — Implementation Report

Date 2026-07-06. Repo `C:\dev\stardrive\StarDrive-main`, branch `arena-045-port`, trunk `082f926f1`, **ProtocolVersion 5 (unchanged)**.
Left **uncommitted in the working tree** for the orchestrator to verify + commit. Everything gated behind
`GlobalStats.Defaults.EnableArenaCustomFleet` (default OFF → off is a true no-op).

This lane wires UI onto the ALREADY-PROVEN setup endpoints (`ARENA_SETUP_PHASE_REPORT.md`). No new systems, no
endpoint re-implementation, no forks of `ShipDesignScreen`/`FleetDesignScreen`. Director directive followed: the
existing `FleetDesignScreen` is the primary setup UI; facing stays fixed per-side (unchanged).

---

## Can a player now click the whole loop? YES.

**Lobby toggle → setup → design/import + fleet page → ready → fight**, host-controlled, synced to the join, deterministic.

1. **Lobby opt-in.** In the Star Gladiator lobby the host clicks a new pill **`SETUP: DESIGN IN ARENA` / `SETUP: OFF`**
   (only shown when the custom-fleet flag is on). It sets `RequestArenaSetupPhase`, host-controlled like the other
   ruleset pills (join sees it read-only). The opt-in rides the authoritative start's ruleset, so the **join enters
   setup too** — proven over real loopback TCP.
2. **In-arena setup HUD.** On entering `ArenaSetupPhase.Setup` the fight screen shows a lean control cluster:
   **[Design Ship]**, **[Import Design]**, **[Fleet / Formation]** (the star — the real fleet page), **[Ready]**,
   plus a live **budget readout** (fleet cost vs cap) and a status line. Hidden outside setup.
3. **Ready → fight.** [Ready] calls `MarkSetupLocalReady`; the already-built per-frame `UpdateMultiplayerSetup`
   exchange rebuilds+broadcasts the authoritative start from the setup scratch set and both peers spawn.

---

## 1. Lobby opt-in control

`ArenaMultiplayerLobbyScreen.cs`:
- New pill in `BuildStarGladiatorSetup` (row 3, next to the SLOT pill), **added only when `EnableArenaCustomFleet`
  is on** — flag-off it is not built at all (true no-op). Mirrors the ARENA/BUDGET/MATCH LENGTH pill idiom exactly
  (`ArenaTheme.AddPillButton`, `Name`, `DynamicText`).
- New `ToggleArenaSetupPhase()` handler mirrors `CycleBudget`: gated by `HostSettingsAreLockedToRemote()` (host-only;
  the join gets the read-only "Host controls game settings" feedback), toggles `RequestArenaSetupPhase`, `SetStatus`,
  `GameAudio.AffirmativeClick()`.
- **Host→join sync (the key wiring):** `BuildArenaRuleset` now sets `SetupPhase = RequestArenaSetupPhase && flag`, so
  the opt-in is folded into the authoritative `SessionStartMessage` the host broadcasts. `LaunchVisibleArena` now
  enters setup when `(RequestArenaSetupPhase || settings.Ruleset?.SetupPhase == true) && flag` — the host from its own
  pill, the **join from the received ruleset** (it never touched the pill). Folded into `SettingsHash` (see §4) so a
  divergent value rejects cleanly at the handshake rather than one-sided setup→spawn desync.

## 2. In-arena setup entry UI (the fleet page is the star)

`ArenaFightScreen.cs` / `ArenaFightScreen.Multiplayer.cs`:
- `BuildArenaSetupHud()` (called from `LoadContent` after `BuildHudAndShop`) builds four `ArenaTheme`/`UIButton`
  HUD buttons + three labels, using the existing fight-screen `Add(new UIButton(ButtonStyle.Medium, …))` idiom.
- `RefreshArenaSetupHud()` (called per frame from `UpdateMultiplayerSetup`) toggles `Visible`/`Enabled` so the cluster
  shows only while `MultiplayerSetupPhase != Fight` and the authoring buttons are enabled only in `Setup`. Flag-off /
  legacy launch never enters setup → the controls stay hidden.
- Button routing — **all to proven endpoints, no new logic:**
  - **[Design Ship]** → `OpenArenaSetupDesigner()` (real base `ShipDesignScreen`, capture → `CaptureSetupDesign`).
  - **[Import Design]** → `OpenArenaSetupImportPicker()`, which reuses the **existing `ArenaFleetPickerScreen`** modal
    (the same design-load list the lobby fleet picker uses) over the legal non-`@arena/` designs; each pick routes
    through the proven `ImportSetupDesignByName`.
  - **[Fleet / Formation]** → `OpenArenaSetupFormation()` (**the real `FleetDesignScreen`** — the primary UI).
  - **[Ready]** → `OnArenaSetupReadyClicked()` → `MarkSetupLocalReady()`.
- **Budget readout:** `SetupBudgetReadout()` shows the authored fleet cost via the SAME `ArenaMultiplayerSettings.SumBundleCost`
  (BaseStrength currency) the handshake enforces, vs the host `Ruleset.BudgetCredits` cap. Status line surfaces
  `SetupHudError` (carrier/mod-gap/over-budget rejections) or the ready progress.

## 3. Roster scoping (the fleet page shows the affordable custom set)

Already built and now live-wired: `CaptureSetupDesign` → `UnlockScratchDesignForArenaDesigner` unlocks each captured
scratch design on `ArenaPlayer` (`UnlockEmpireHull` + `UnlockDesignModulesForArenaDesigner` + `UpdateShipsWeCanBuild`
under the `@arena/<hash>` wire name), and `OpenArenaSetupFormation` refreshes `Player.ShipsWeCanBuild` before
constructing the real `FleetDesignScreen`, so the fleet page's roster offers the custom/affordable designs. Import
feeds the same scratch set. Not re-implemented — wired.

## 4. Wire/determinism (protocol stays 5)

One append-only host-authored ruleset field carries the opt-in:
- `ArenaMultiplayerRuleset.SetupPhase` (+ `Clone` + **folded LAST into `AppendTo`/`SettingsHash`** so two peers that
  disagree reject up front — advisor-ruled FOLD, matching the `MaxMatchSeconds`/`CountdownSeconds` convention).
- `SessionStartMessage.RulesetSetupPhase`, round-tripped **append-only** in `LockstepMessageCodec` (live wire, after
  `JoinDesignTable`, via the established `Position < Length` optional-read idiom) and in `ArenaBattleCodes` (offline
  exporter, `FormatVersion` 2→3, reader guarded by `format >= 3`). A pre-field reader stops before it → default false
  = legacy launch. **No `ProtocolVersion` bump; no sim-tick determinism surface touched** (setup is pre-lockstep;
  the actual custom designs already fold via the design-bundle hashes, unchanged).
- `ArenaMultiplayerSettings.ToStartMessage` / `RulesetFromStartMessage` map the field both ways.

## Proof results (headless, focused filters)

| Proof | Result |
|---|---|
| `PROOF_LOBBY_OPT_IN_ENTERS_SETUP_ON_BOTH_PEERS` — 2 real lobbies over loopback TCP; host clicks the pill; BOTH fight screens enter `ArenaSetupPhase.Setup`, neither spawns (join learned the opt-in ONLY from the wire ruleset) | **PASS** |
| `PROOF_LOBBY_OPT_IN_IS_NOOP_WHEN_FLAG_OFF` — flag off: even with the pill flag set, neither peer enters setup; the legacy duel spawns + advances the sim on both peers | **PASS** |
| `PROOF_SETUP_ENTRY_BUTTONS_ROUTE_TO_REAL_EDITORS` — the setup HUD buttons exist; [Design Ship]/[Fleet-Formation] mount the REAL base `ShipDesignScreen`/`FleetDesignScreen`; [Ready] advances Setup→LocalReady | **PASS** |
| Focused regression: `ArenaMultiplayerLockstep` + `ArenaCustomFleetKernel` + `ArenaCustomFleetUi` + `ArenaDeterminism` (incl. all prior `PROOF_*` + the 3 new) | **60 / 60 PASS** |
| Builds: `StarDrive.csproj`, `StarDriveArena.csproj`, `UnitTests/SDUnitTests.csproj` | **0 error, 0 warning** |

Stray `testhost`/`dotnet` killed before each run; only the focused filters used (never the full `~Arena` suite).
No run hung; none detached.

## Files changed (working tree, uncommitted)

- `SDLockstep/SessionMessages.cs` — `SessionStartMessage.RulesetSetupPhase` (append-only).
- `SDLockstep/LockstepMessageCodec.cs` — write/read the field, append-only after `JoinDesignTable`.
- `Ship_Game/GameScreens/Arena/ArenaMultiplayerRuleset.cs` — `SetupPhase` field + `AppendTo` fold + `Clone`.
- `Ship_Game/GameScreens/Arena/ArenaMultiplayerSession.cs` — map `SetupPhase` in `ToStartMessage`/`RulesetFromStartMessage`.
- `Ship_Game/GameScreens/Arena/ArenaBattleCodes.cs` — round-trip `RulesetSetupPhase`, `FormatVersion` 2→3.
- `Ship_Game/GameScreens/Arena/ArenaMultiplayerLobbyScreen.cs` — the opt-in pill + `ToggleArenaSetupPhase`;
  `BuildArenaRuleset.SetupPhase`; `LaunchVisibleArena` enters setup from the received ruleset.
- `Ship_Game/GameScreens/Arena/ArenaFightScreen.cs` — `BuildArenaSetupHud()` call in `LoadContent`.
- `Ship_Game/GameScreens/Arena/ArenaFightScreen.Multiplayer.cs` — the in-arena setup HUD (buttons/labels, refresh,
  budget readout, import picker, ready click).
- `UnitTests/Graphics/ArenaMultiplayerLockstepTests.cs` — the 3 new proofs + `FindButtonByName` helper.

**No change** to the setup-phase kernel, the exchange machinery, `ArenaDesignTable`, `ArenaFleetBundle`, or the base
editors — this lane only wires UI onto the proven endpoints.

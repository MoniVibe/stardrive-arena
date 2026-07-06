# StarDrive Arena — PRE-MATCH SETUP PHASE — Implementation Report

Date 2026-07-06. Repo `C:\dev\stardrive\StarDrive-main`, branch `arena-045-port` (trunk `1d3089430`).
Spec: `C:\dev\plans\STARDRIVE_ARENA_SETUP_PHASE_EXEC_PLAN_20260706.md` (+ program plan ADDENDUM 4 + KERNEL-REVIEW amendments).

**Left uncommitted in the working tree** for the orchestrator to verify FUNCTION and commit. No protocol re-bump (stays 5; append-only wire). Everything gated behind `GlobalStats.Defaults.EnableArenaCustomFleet` (default OFF).

---

## Headline result

- **Real editors LAUNCH against the arena universe: YES.** `PROOF_REAL_EDITORS_LAUNCH_AGAINST_ARENA_UNIVERSE` mounts the UNMODIFIED base `ShipDesignScreen(this, EmpireUI)` AND `FleetDesignScreen(this, EmpireUI)` on the live arena `ArenaFightScreen : UniverseScreen` and asserts both appear on the ScreenManager. The "lobby has no universe" excuse is dead — no name-picker stub.
- **Join-transport: WORKS over the real wire.** `PROOF_JOIN_TABLE_REACHES_HOST` drives two real lobbies over actual loopback TCP; the joiner fields a custom the host NEVER authored; the host's authoritative start (captured on BOTH armed fight screens) carries that custom in `JoinDesignTable`, reconstructed byte-identically from the received bytes. This also exposed and fixed a real lane-1 bug (see below).
- **Import: WORKS.** `PROOF_IMPORT_PRODUCES_ARENA_CUSTOM` — import-by-name and import-from-`.design`-bytes both converge on the SAME `@arena/<hash>` and byte-identical canonical payload as a live capture.
- **All 7 new proofs GREEN; 57/57 focused tests pass; both game projects build clean.**

---

## Per-step: what was built + proof result

### A. JOIN-SIDE DESIGN-TABLE TRANSPORT (the hard blocker — done first)
- `SDLockstep/SessionMessages.cs`: added trailing `string DesignTable = ""` to `SessionLobbyMessage` (append-only).
- `SDLockstep/LockstepMessageCodec.cs`: write `lobby.DesignTable` after `lobby.Fleet`; read via append-tolerant `ReadOptionalString` after `Fleet`. **No field reorder; no protocol bump.**
- `ArenaMultiplayerLobbyScreen.cs`:
  - `SendLocalLobby` now publishes `DesignTable = BuildLocalDesignTable()` so each peer broadcasts its OWN full canonical payloads.
  - `LobbyPeer` carries `string DesignTable`; `LobbyPeer.From` populates it from the decoded message.
  - New `UnionRemoteDesignTables()` — decode every remote peer's table, union the reconstructed designs (dedup by content name, sorted by peer id → N-peer-ready), re-`Encode`.
  - `BuildArenaSettings` now sets `HostDesignTable`/`JoinDesignTable` **symmetrically**: host peer's own customs ride `HostDesignTable`, the union of remote (joiner) tables rides `JoinDesignTable` (and vice-versa on the join peer). Previously `JoinDesignTable=""` when local was host — the confirmed gap.
- **Proofs:**
  - `PROOF_JOIN_TABLE_REACHES_HOST` — **GREEN.** Real 2-lobby loopback-TCP; joiner's custom (distinct hull, never authored by host) reaches the host's authoritative start; both peers' armed settings decode it byte-identically; teardown snapshot clean.
  - `PROOF_JOIN_CUSTOM_HANDSHAKE` — **GREEN.** A start carrying BOTH host+join tables validates on the peer (registers both before `ValidateStartMessage`); a **stripped** join table (the pre-fix host behaviour) makes the join fleet's `@arena/<hash>` fail to resolve → clean handshake REJECT, never a desync.

### B. SETUP-PHASE STATE MACHINE
- `ArenaFightScreen.Multiplayer.cs`: new `enum ArenaSetupPhase { Setup, LocalReady, AwaitingPeers, Exchange, Countdown, Fight }` + `MultiplayerSetupPhase` field, **SEPARATE** from the sim-tick `ArenaMatchPhase` (kept deterministic). Default = `Fight` (so a flag-off/legacy launch is unchanged).
- `InitializeMultiplayerLiveIfNeeded` (:234) gated: early-returns while `MultiplayerSetupPhase != Fight`, so the sim cannot spawn until authoring + exchange complete. One reused `ArenaFightScreen` from setup → fight.
- Drivers: `EnterMultiplayerSetupPhase` (flag-gated), `MarkSetupLocalReady`, `AdvanceSetupPhaseToFight`.
- **Proof `PROOF_SETUP_HANDOFF_DETERMINISTIC` — GREEN.** Author a custom via the capture seam; assert the gate BLOCKS `InitializeMultiplayerLiveIfNeeded` (sim tick stays -1) until `AdvanceSetupPhaseToFight`; then the sim initializes; a direct in-process match of the same designs runs to a matching digest.

### C. IMPORT PATH
- `ArenaFightScreen.Multiplayer.cs`: `ImportSetupDesignByName` (a design already in the templates table) and `ImportSetupDesignFromBytes` (a saved `.design`, via the base `ShipDesign.FromBytes` codec). Both feed the SAME `CaptureSetupDesign` seam as build-anew.
- **Proof `PROOF_IMPORT_PRODUCES_ARENA_CUSTOM` — GREEN.** Import-by-name and import-from-bytes both yield the SAME `@arena/<hash>` and a byte-identical canonical payload vs. a live capture. Teardown snapshot clean.

### D. REAL EDITORS + BUDGET/ROSTER
- `ArenaFightScreen.cs`: `OpenArenaSetupDesigner(seed)` — launches the REAL `new ShipDesignScreen(this, EmpireUI)` (identical to `OpenCustomizerForActiveVessel`:4599), OnExit rerouted to `CaptureSetupDesign` (**NOT** `AdoptDesignerChoice`/`CareerManager.Save`). `OpenArenaSetupFormation()` — launches the REAL `new FleetDesignScreen(this, EmpireUI)`, OnExit → `CaptureSetupFormation(editor.SelectedFleet)`.
- `CaptureSetupDesign` canonicalizes the in-memory design (`ArenaDesignTable.ContentName`/`RegisterTransient`, `playerDesign:false`, `readOnly:true`), rejects carrier/mod-gap via `ValidateContentAvailable`, and unlocks it on `ArenaPlayer` (`UnlockEmpireHull` + `UnlockDesignModulesForArenaDesigner` + `UpdateShipsWeCanBuild` — the trio at :1923-1927) so the formation roster can offer it.
- `CaptureSetupFormation` projects the authored `Fleet` via `ArenaFleetBundle.FromFleet` — the SAME projection `SaveFleetDesignScreen.DoSave` uses, so setup and base fleet-save produce byte-identical bundles. Captured IN-MEMORY (no `.fleet` disk write).
- `AffordableScratchWireNames(budget)` scopes the roster to affordable scratch designs (BaseStrength currency, mirroring `SumBundleCost`).
- **Proofs:**
  - `PROOF_REAL_EDITORS_LAUNCH_AGAINST_ARENA_UNIVERSE` — **GREEN.** Both real editors mount against the arena universe.
  - `PROOF_FORMATION_SPAWN_DETERMINISTIC` — **GREEN.** A formation captured via `CaptureSetupFormation` spawns byte-identically on both peers (one ship per node, matching digest).
  - `PROOF_BUDGET_ENFORCED_IN_SETUP` — **GREEN.** The affordable roster includes the within-budget design and EXCLUDES the over-budget one; a fleet at budget passes the handshake; one credit over is REJECTED at `ValidateStartMessage`.

---

## How each ANTI-STUB flag is satisfied

1. **Join-transport (non-negotiable).** Satisfied — proven over REAL loopback TCP with the joiner fielding a custom the host never authored, reconstructed byte-identically from received bytes on both peers. Not the shared-static false-green: the design's only path into the host's `JoinDesignTable` is the wire `SessionLobbyMessage`. See `PROOF_JOIN_TABLE_REACHES_HOST` / `PROOF_JOIN_CUSTOM_HANDSHAKE`.
2. **Base-save Saved-Designs pollution — resolution chosen: (A) QUARANTINE-IN-PLACE.** `CaptureSetupDesign` captures the **in-memory** design object and registers a content-hashed `@arena/<hash>` transient (`playerDesign:false`, `readOnly:true`) — never in the player's Saved Designs enumeration. If the base `ShipDesignScreen` save button also writes a player-typed `.design` to SP Saved Designs, that template is a harmless legal stock-namespace design; the arena sim only ever references the `@arena/` name. Zero base-screen changes, zero risk (determinism is content-hashed regardless). No sandbox save root (option B) was needed.
3. **Post-setup authoritative-start rebuild — HONESTLY DEFERRED (see "Deferred" below).** NOT silently stubbed: the state machine, gate, capture seams and real-editor launches are all built and proven; the one piece not wired is the host re-broadcasting a NEW authoritative start over the live fight-screen transport after in-arena authoring. Customs authored in the LOBBY (Phase A) fully reach the host and fight today. Customs authored in the in-arena SETUP phase are captured into a per-screen scratch set + local bundle but are not yet re-broadcast as a fresh start. Stated loudly, not pretended.
4. **Roster scoping via `ShipsWeCanBuild`.** Satisfied — `CaptureSetupDesign` unlocks each scratch design on `ArenaPlayer` and `AffordableScratchWireNames` scopes to affordable designs; `PROOF_BUDGET_ENFORCED_IN_SETUP` asserts the affordable/unaffordable split.
5. **Carrier rejection.** Preserved — `CaptureSetupDesign` runs `ArenaDesignTable.ValidateContentAvailable`, which rejects any non-empty `HangarShipUID` (kernel amendment 7), surfacing the error to `SetupHudError`.
6. **Teardown on every exit.** Preserved + extended — `TeardownMultiplayerCustomDesigns` now also calls `TeardownSetupScratchDesigns` (one method, both lists), so the setup scratch set is undone on `BackToMultiplayerLobby` / `StartMultiplayerRematch` / `ExitScreen` (all already routed through `TeardownMultiplayerCustomDesigns`). EVERY new proof asserts `ResourceManager.Ships.Designs.Count` returns to its pre-test snapshot.

**Flag-off is a true no-op:** default `MultiplayerSetupPhase = Fight` (gate transparent), `EnterMultiplayerSetupPhase`/`CaptureSetupDesign` early-return when the flag is off, `BuildLocalDesignTable`/`UnionRemoteDesignTables` return `""`. The existing flag-off lockstep + determinism self-tests all stay green.

---

## Bug fixed in passing (real product bug, exposed by the join-transport proof)

`ArenaMultiplayerLobbyScreen.RebuildSandboxScratchSet` (lane 1) passed **display-named** designs to `ArenaDesignTable.RegisterTransient`, whose defensive guard SKIPS any design whose `Name` doesn't start with `@arena/`. Result: the sandbox scratch set was ALWAYS empty, so `BuildLocalDesignTable` always returned `""` — no custom ever transported, even host-side. **Fix:** clone + rename each picked design to its `@arena/<hash>` content name before registering (the same canonicalization the join side does when reconstructing from bytes). `CaptureSetupDesign` applies the identical clone-rename. Without this, custom-fleet transport silently did nothing.

---

## Proof results (all headless, focused filters)

| Proof | Result |
|---|---|
| `PROOF_JOIN_TABLE_REACHES_HOST` (real TCP, byte-identical, both peers) | **PASS** |
| `PROOF_JOIN_CUSTOM_HANDSHAKE` (both-tables validate; stripped-join rejects) | **PASS** |
| `PROOF_SETUP_HANDOFF_DETERMINISTIC` (gate blocks sim until Fight) | **PASS** |
| `PROOF_IMPORT_PRODUCES_ARENA_CUSTOM` (by-name + from-bytes == live capture) | **PASS** |
| `PROOF_FORMATION_SPAWN_DETERMINISTIC` (formation spawns identically both peers) | **PASS** |
| `PROOF_BUDGET_ENFORCED_IN_SETUP` (roster scope + handshake budget) | **PASS** |
| `PROOF_REAL_EDITORS_LAUNCH_AGAINST_ARENA_UNIVERSE` (real ShipDesign/FleetDesign mount) | **PASS** |
| Focused regression: `ArenaCustomFleetKernel` + `ArenaCustomFleetUi` + `ArenaMultiplayerLockstep` + `ArenaDeterminism` + all `PROOF_*` | **57 / 57 PASS** |
| Game builds: `StarDrive.csproj`, `StarDriveArena.csproj`, `SDUnitTests.csproj` | **0 error, 0 warning** |

Stray `testhost` was killed before each run; only focused filters used (never the full `~Arena` suite). Match-run proofs pass explicit small `MaxTurns` (60–90).

---

## Files changed (working tree, uncommitted)

- `SDLockstep/SessionMessages.cs` — `SessionLobbyMessage.DesignTable` (append-only).
- `SDLockstep/LockstepMessageCodec.cs` — write/read the new field, append-only.
- `Ship_Game/GameScreens/Arena/ArenaMultiplayerLobbyScreen.cs` — `SendLocalLobby` publishes `DesignTable`; `LobbyPeer`+`From` carry it; `UnionRemoteDesignTables`; `BuildArenaSettings` symmetric host/join tables; `RebuildSandboxScratchSet` clone-rename bugfix; headless seams.
- `Ship_Game/GameScreens/Arena/ArenaFightScreen.Multiplayer.cs` — `ArenaSetupPhase` machine + gate; `EnterMultiplayerSetupPhase`/`CaptureSetupDesign`/`ImportSetupDesign*`/`CaptureSetupFormation`/roster scoping/`BuildSetupLocalDesignTable`/`TeardownSetupScratchDesigns`; `using Ship_Game.Fleets`.
- `Ship_Game/GameScreens/Arena/ArenaFightScreen.cs` — `OpenArenaSetupDesigner`/`OpenArenaSetupFormation` (real editors against arena universe, capture rerouted).
- `UnitTests/Graphics/ArenaMultiplayerLockstepTests.cs` — the 7 new proofs + the `DriveRealLobbiesToLaunchedFight` join-configure extension + `MultiplayerLiveSettingsForHeadless` accessor.

**No change** to `ArenaDesignTable.cs`, `ArenaFleetBundle`, `SessionStartMessage` fields, or `RegisterPeerDesignTables` (kernel is done; this lane feeds it correctly).

---

## Genuinely deferred (with reason)

- **Post-setup authoritative-start REBUILD + re-broadcast (§2.3, anti-stub #3).** The lobby still builds the authoritative start at LOCK. Customs authored in the in-arena SETUP phase are captured (scratch set + `SetupLocalFleetBundle` + `BuildSetupLocalDesignTable`) but not yet re-broadcast as a fresh `SessionStartMessage` over the live fight-screen transport with a re-run ack loop. This is a substantial cross-screen protocol change (relocating start-build + ack from the lobby to the setup terminal state). **Consequence today:** BUILD-ANEW/IMPORT authored *inside* the arena setup phase does not yet drive a live match; customs authored in the LOBBY (Phase A) fully reach the host and fight. Everything needed to complete this is in place (the scratch set, the local table/bundle builders, the phase machine, `AdvanceSetupPhaseToFight`); what remains is wiring the setup terminal state to rebuild+send the start and re-run `HandleStartAck`. Documented loudly per the plan rather than silently stubbed.
- **Live per-peer SETUP-READY exchange over the fight-screen transport.** The phase transitions and local-ready are wired and proven; the peer-to-peer READY broadcast reuses the same transport as Phase A but is driven headlessly via `AdvanceSetupPhaseToFight` rather than a live 2-peer setup handshake (which depends on the rebuild above).
- **Editor GUI interaction** (dragging modules, pressing Save) is not unit-tested — per the plan's Phase D note, the CAPTURE seam and the bundle are proven; the editors are proven to LAUNCH.

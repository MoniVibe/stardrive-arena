# Arena Lobby Fleet UX — Director Live-QA Fixes

Branch: `arena-045-port` (trunk `4f26675c5`, ProtocolVersion 5)
Scope: StarGladiator multiplayer lobby UI + fleet-name selection only. No protocol / sim / determinism change. Authoritative4X surface untouched. Left in the working tree — not committed.

---

## ISSUE 1 — The joiner can now change their own fleet

**Root cause.** `OpenFleetPicker()` opened with a host-gate:
```
if (HostSettingsAreLockedToRemote()) { ... return; }
```
`HostSettingsAreLockedToRemote()` returns `true` for any `LocalRole == Join`, so a joiner clicking SET FLEET was bounced with "Host controls game settings." SET FLEET is NOT a ruleset — it is each player's OWN fleet choice — so it must be available to both roles.

**Fix.** Removed the host-gate from `OpenFleetPicker()` (kept it on the actual ruleset pills: ARENA / BUDGET / MATCH-LENGTH / SETUP). Each player now edits their own `LocalPeer.FleetDesignNames`.

**How the joiner sets fleet now.** SET FLEET → `OpenFleetPicker()` → `ArenaFleetPickerScreen` → `ApplyPickedFleet()` writes `LocalPeer.FleetDesignNames` (pinned via `ManualFleetDesignNames`). `ApplyPickedFleet` now also calls `SendLocalLobby()` (mirroring `CycleRace`/`ToggleTrait`) so the pick is **broadcast immediately** over the live transport, not only at the next ready-toggle. End to end: joiner pick → `SendLocalLobby` → `SessionLobbyMessage.Fleet` → host `OnHostMessage` → `RemotePeers[peerId] = LobbyPeer.From(lobby)` → used at launch in `BuildArenaSettings`. Verified over the real TCP transport by the existing `PROOF_JOIN_TABLE_REACHES_HOST` test (still green).

## ISSUE 2 — Each player's current fleet is shown per slot, on both screens

**Root cause (join side).** Slot cards read `RemotePeers[peerId]`, and `SlotStatus`/`SlotDetail` always treated slot `HostPlayerPeerId4X` (2) as the *local* player. That is correct on the host's screen, but on the **joiner's** screen the local player sits at `JoinPeerSlot` (3), while the host sits at slot 2 as a *remote* peer. Two bugs followed: (a) the host card showed the joiner's own fleet, and (b) the joiner ingested the host lobby only into the singular `RemotePeer`, never into `RemotePeers[2]` that the cards read.

**Fix.**
- Added `LocalSlotPeerId` (= `JoinPeerSlot` for a joiner, `HostPlayerPeerId4X` for the host) and made `SlotStatus`/`SlotDetail` render `LocalPeer` at that id and `RemotePeers[...]` elsewhere.
- `OnJoinMessage` now mirrors the ingested host lobby into `RemotePeers[HostPlayerPeerId4X]` (not just `RemotePeer`), so the host slot card renders the host's fielded fleet on the joiner's screen. The host already re-broadcasts its lobby to the joiner after every change (`SendLocalLobby` host→join path), so this stays live.

**How each player's fleet is displayed.** `SlotDetail(peerId)` returns `FleetSummary(fleetDesignNames)` on the StarGladiator surface — e.g. `Fleet: Ving Defender +2` (first design name, `+N` for the rest). The local slot shows `LocalPeer.FleetDesignNames`; every other occupied combatant slot shows that peer's ingested `FleetDesignNames`. Updates live as either player re-picks (host sees the joiner's via the `SessionLobbyMessage` it ingests; the joiner sees the host's via the host's broadcast).

## ISSUE 3 — Design-in-arena flow is discoverable

**Root cause.** The custom-fleet design flow was reachable only via a pill labelled `SETUP: OFF` / `SETUP: DESIGN IN ARENA` — opaque, so the director missed it.

**Fix (lean, no restructure).** When `EnableArenaCustomFleet` is on:
- The SETUP pill spells out both states: `SETUP: DESIGN IN ARENA (design ships)` vs `SETUP: pick fleet in lobby` (via `SetupPillLabel()`), and the pill is widened to fit.
- A one-line STATUS hint is surfaced: `Custom Fleet on: toggle SETUP to design ships in the arena before the fight.` (via `SetupHintLine()`).
- Flag OFF → the pill is not added at all (interim behavior unchanged), the hint never appears.

Still host-controlled like the other ruleset pills (`ToggleArenaSetupPhase` keeps its `HostSettingsAreLockedToRemote` guard).

---

## Files changed

- `Ship_Game/GameScreens/Arena/ArenaMultiplayerLobbyScreen.cs`
  - `OpenFleetPicker()` — removed the host-gate (ISSUE 1).
  - `ApplyPickedFleet()` — added `SendLocalLobby()` so own-fleet changes broadcast live (ISSUE 1/2).
  - `OnJoinMessage()` — mirror the host lobby into `RemotePeers[HostPlayerPeerId4X]` (ISSUE 2).
  - Added `LocalSlotPeerId` / `IsLocalSlot`; made `SlotTitle`/`SlotStatus`/`SlotDetail` role-aware (ISSUE 2).
  - Added `SetupPillLabel()` / `SetupHintLine()`; wired the SETUP pill label + STATUS hint, widened the pill (ISSUE 3).
  - New headless seams: `SlotTitleForHeadless`, `SlotStatusForHeadless`, `SlotDetailForHeadless`, `LocalSlotPeerIdForHeadless`, `HostSlotPeerIdForHeadless`, `SetupPillLabelForHeadless`, `SetupHintLineForHeadless`.
- `UnitTests/Graphics/ArenaMultiplayerLockstepTests.cs`
  - `PROOF_JOINER_CAN_SET_OWN_FLEET_NOT_HOST_GATED_Headless` (ISSUE 1)
  - `PROOF_EACH_SLOT_SHOWS_THAT_PEERS_FLEET_Headless` (ISSUE 2, two real lobbies over TCP)
  - `PROOF_SETUP_PILL_DISCOVERABILITY_Headless` (ISSUE 3, flag on/off)

## Proof results

- `dotnet build UnitTests/SDUnitTests.csproj` — succeeded, 0 warnings/errors.
- `dotnet build StarDrive.csproj` — succeeded.
- Focused run `PROOF_JOINER_... | PROOF_EACH_SLOT_... | PROOF_SETUP_PILL_...` — **3/3 passed**.
- Regression gate `FullyQualifiedName~ArenaMultiplayerLockstep` (includes ArenaCustomFleetUi + ArenaCustomFleetKernel + all lockstep tests) — **45/45 passed** (42 pre-existing + 3 new), 1m52s. No regressions.

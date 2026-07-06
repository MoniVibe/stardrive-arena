# StarDrive Arena — Custom-Fleet EXCHANGE KERNEL (Phase 0) — Implementation Report

Date 2026-07-06. Repo `C:\dev\stardrive\StarDrive-main`, branch `arena-045-port` (trunk `3fdf5a5d2`).
Spec: `C:\dev\plans\STARDRIVE_ARENA_CUSTOM_FLEET_PROGRAM_PLAN_20260706.md` (+ the 7 BINDING AMENDMENTS).
Review evidence: `C:\dev\plans\STARDRIVE_ARENA_CUSTOM_FLEET_KERNEL_REVIEW_20260706.md`.

**HEADLESS ONLY.** No UI (no ship designer, no lobby changes). This is the determinism kernel that lets a peer
reconstruct a custom ship design it has never seen, byte-identically, and reject any mismatch at the handshake.
Left uncommitted in the working tree for the orchestrator to verify and commit.

---

## 1. What was built

### New files
- **`Ship_Game/GameScreens/Arena/ArenaDesignTable.cs`** — the exchange kernel. A static owning:
  - `CanonicalPayload(IShipDesign)` — our OWN byte-exact serializer (NOT the base `CreateShipDataText`/`GetDesignBytes`).
  - `DesignContentHash(...)` / `ContentName(...)` — content-hash-as-name (`@arena/<hash16>`).
  - `ValidateContentAvailable(IShipDesign)` — hull/module id resolution + carrier rejection.
  - `Encode(designs)` / `Decode(text) -> DecodeResult` — size-capped, append-tolerant, exception-safe multi-design container.
  - `RegisterTransient(designs) -> names` / `UnregisterTransient(names)` — transient registration + precise teardown.
- **`UnitTests/Determinism/ArenaCustomFleetKernelTests.cs`** — the CORRECTED proof gates (14 tests).

### Modified files
- `SDLockstep/SessionMessages.cs` — added trailing `HostDesignTable` / `JoinDesignTable` strings to `SessionStartMessage`.
- `SDLockstep/LockstepMessageCodec.cs` — append-only `WriteString`/`ReadOptionalString` for the two new fields (encode + decode + initializer).
- `Ship_Game/GamePlayGlobals.cs` — added `[StarData] bool EnableArenaCustomFleet` (default FALSE).
- `Ship_Game/GameScreens/Arena/ArenaMultiplayerSession.cs` —
  - `ProtocolVersion` **4 → 5**;
  - `ArenaMultiplayerSettings.HostDesignTable`/`JoinDesignTable` carried through `ToStartMessage`/`FromStartMessage`/`WithResolvedFleets`/`WithRematchSeed`;
  - `RegisterPeerDesignTables(settings, out error)` / `UnregisterPeerDesignTables(names)` (bidirectional, flag-gated);
  - `RunInProcess` / `RunNetworkHost` / `RunNetworkJoin` wrapped in `try { register → validate → run } finally { unregister }`.
- `Ship_Game/GameScreens/Arena/ArenaFightScreen.Multiplayer.cs` — live-path teardown: register on `ArmMultiplayerLive`, tear down on `BackToMultiplayerLobby`, `StartMultiplayerRematch` (with re-registration), and `ExitScreen` (catch-all).
- `StarDriveArena.csproj` — compile `ArenaDesignTable.cs` (arena code lives in the plugin, explicit `<Compile Include>` list).
- `UnitTests/SDUnitTests.csproj` — compile the new test file (explicit list; `EnableDefaultCompileItems=false`).
- `UnitTests/Graphics/ArenaMultiplayerLockstepTests.cs` — updated the two hardcoded `ProtocolVersion == 4` assertions to 5.

---

## 2. How each of the 7 BINDING AMENDMENTS is satisfied

1. **FALSE-GREEN proof replaced.** The reconstruction gate OF RECORD is `Kernel_ByteRoundTrip_NoRegistration_Headless`:
   author → `CanonicalPayload` → `FromBytes` → `CanonicalPayload`, assert **byte-equal**, with **NO registration**
   (asserts the global `ResourceManager.Ships.Designs.Count` is unchanged). Plus `Kernel_AuthoredVsParsed_ByteIdentical`
   (same ship authored-live vs parsed-from-base-codec emits identical canonical bytes). The two-peer digest match
   (`RunInProcess`) is treated as a secondary regression gate only — it shares the process-global `ResourceManager.Ships`
   and cannot prove per-peer reconstruction.

2. **Total ORDINAL UID order in the emitter.** `OrdinalModuleUIDs` builds the UID table with a
   `SortedSet<string>(StringComparer.Ordinal)` (matching `Authoritative4XSession.cs:1531`), and each slot's index is
   computed against THAT ordinal list. Slots are emitted in `DesignSlot.Sorter()` scanline order, tie-broken by ordinal
   UID. The emitter NEVER trusts `ShipDesign.UniqueModuleUIDs` (HashSet order in the authoring path) or
   `SlotModuleUIDMapping` (file order in the parse path). Proven by `Kernel_AuthoredVsParsed_ByteIdentical`.

3. **InvariantCulture on all floats.** We emit our OWN UTF-8 text and never route through the base
   `ShipDesignWriter.Write<T>` culture hole. `FixedUpkeep` (the only culture-sensitive float in the base format, and
   inert for the arena sim since cost = BaseStrength) is DROPPED entirely, deleting the hazard rather than guarding it.
   All remaining numbers (Size/GridCenter ints, slot coords) are `ToString(CultureInfo.InvariantCulture)`.

4. **try/finally teardown on EVERY exit path.** Headless: `RunInProcess`/`RunNetworkHost`/`RunNetworkJoin` wrap
   register→validate→run in `try { } finally { UnregisterPeerDesignTables(registered) }`, so the A2 leak (RunNetworkJoin
   throws after registration) is closed. Live UI: `ArmMultiplayerLive` registers (tearing down any prior set first for
   clean rematch re-registration); `BackToMultiplayerLobby`, `StartMultiplayerRematch`, and `ExitScreen` (catch-all) all
   tear down. Teardown targets the EXACT tracked name set — never a blanket `@arena/*` delete (a concurrent match may
   share the process-global namespace). Proven by `Kernel_RegisterTransient_NoLeakAfterUnregister` and the teardown
   assert in `Kernel_CustomFleet_ValidatesAndOverspendRejects`.

5. **Parse exception-SAFE.** `Decode` guards the `Modules=`/`ModuleUIDs=` header count against `MaxModulesPerDesign`
   (4096) BEFORE `FromBytes` (so a crafted ushort UID index can't `IndexOutOfRange`), wraps base64 decode and `FromBytes`
   in try/catch, and enforces per-design (64 KB) and per-table (512 KB) base64 caps. ANY hostile input returns a clean
   `DecodeResult.Error` (handshake rejection) — never an uncaught throw. Proven by `Kernel_Decode_MalformedPayload_RejectsCleanly`.

6. **Bidirectional exchange.** `RegisterPeerDesignTables` decodes+validates+registers BOTH `HostDesignTable` and
   `JoinDesignTable` before validation/spawn; authored as a loop over peer tables so it generalizes to N peers (tested
   at N=2). The host loop now registers the join's customs too.

7. **Carrier deferral enforced at VALIDATION.** `ValidateContentAvailable` rejects ANY slot with a non-empty
   `HangarShipUID` (stricter than the plan text, which allowed stock hangar refs) — this closes the `BaseStrength`
   impurity (`ShipModule.CalculateModuleOffense` pulls the hangar ship's live strength / +100 fallback) completely for
   v0. The error explicitly says "carrier … not supported in this build" so it isn't read as a content-gap bug. Proven
   by `Kernel_CarrierDesign_RejectsAtValidation`.

---

## 3. Canonical payload — exact contents

Emitted as our own UTF-8 `key=value\n` text, FIXED order, **no `Name=` line** (identity IS the content hash, so there is
no sender-supplied name to trust — amendment 1 is structural):
`Version, Hull, ModName, Role, ShipCategory, HangarDesignation, Size, GridCenter, IsShipyard, IsOrbitalDefense,
IsCarrierOnly, EventOnDeath, ModuleUIDs (ordinal-sorted), Modules (count), then each slot line in `DesignSlot.Sorter()`
order` (`gridX,gridY;moduleUIDIndex[;size[;turret[;rot[;hangar]]]]`, indices into the ordinal UID list).

- **Size/GridCenter are KEPT** (mandatory): `ModuleGridFlyweight` sizes its index grid from `Size`; omitting it
  `IndexOutOfRange`s `FromBytes` for any ship with modules. They are inert for `BaseStrength` (SurfaceArea comes from the
  hull) but load-bearing for reconstruction.
- Cosmetic/editor metadata stripped: `IconPath, SelectIcon, Description, Style, Source`, and `Name`.
- The registration name `@arena/<hash16>` is RE-DERIVED from the received bytes at decode time and written onto the
  reconstructed `ShipDesign` — never round-tripped through the payload.

`DesignContentHash` = `DetHash` (FNV-1a) over the canonical UTF-8 bytes → `"0x"+X16`; `@arena/` + the 16 hex chars.
The content-hash-as-name folds module content into the existing `ArenaFleetBundle.DesignBundleHash` (which hashes
`node.ShipName`) transitively, so the design table is NOT folded into `SettingsHash` directly (that would false-reject
benign base64/dedup/ordering variance between peers).

---

## 4. How the 3 top-risk amendments are handled (summary)

- **False-green proof (amendment 1):** the gate of record is the pure `CanonicalPayload → FromBytes → CanonicalPayload`
  byte-equality with the global-table-count-unchanged assertion, plus authored-vs-parsed. Reconstruction is proven
  independent of the shared process-global registry.
- **UID order (amendment 2):** self-contained ordinal UID table + Sorter-ordered slots; proven by authoring the same
  ship two ways (HashSet path vs base-codec file-order path) and asserting byte-identical canonical output.
- **Teardown (amendment 4):** caller-owned `try/finally` on all headless run paths + all four live-UI exit paths,
  tearing down exactly the tracked set; proven by the register→unregister snapshot equality and the flag-gated
  no-leak assertion after a validated custom match.

---

## 5. Protocol note

`ArenaMultiplayerSettings.ProtocolVersion` bumped **4 → 5**. The codec is append-tolerant (position-guarded
`ReadOptionalString`), so a v4 peer would decode the new design table as `""` — but that is exactly why the bump is
needed: a v4 peer reading `""` would fail to resolve the custom `@arena/<hash>` name with a confusing "design not
available" error instead of an honest protocol mismatch. `ProtocolVersion` is compared for exact equality at both the
Hello and Start gates, so a v4↔v5 pairing now fails cleanly at the version gate. One bump covers the whole custom-fleet
+ N-player program.

---

## 6. Proof results (all headless, RED→GREEN)

Focused filters (stray testhost killed before each run; no full `~Arena` suite run):

| Suite | Result |
|---|---|
| `ArenaCustomFleetKernelTests` (new) | **14 / 14 passed** |
| `LockstepNetworkTransportTests` (codec round-trip regression) | **4 / 4 passed** |
| `ArenaMultiplayerLockstepTests` (RunInProcess digests, SettingsHash, protocol) | **28 / 28 passed** |
| `ArenaDeterminism*` + `ArenaPortableFingerprint` + `ArenaPortableLockstepProbe` | **6 / 6 passed** |

The 5 named kernel gates, mapped to tests:
- **ByteRoundTrip** → `Kernel_ByteRoundTrip_NoRegistration_Headless`
- **AuthoredVsParsed** → `Kernel_AuthoredVsParsed_ByteIdentical_Headless`
- **BundleHashCoversModuleContent** → `Kernel_BundleHashCoversModuleContent_Headless`
- **RejectAtHandshake** → `Kernel_Decode_MalformedPayload_RejectsCleanly` (malformed/oversized/mod-gap), `Kernel_CarrierDesign_RejectsAtValidation` (carrier), `Kernel_CustomFleet_ValidatesAndOverspendRejects_AtHandshake` (overspend), `Kernel_TamperedBytes_RederiveDifferentName` (tamper), `Kernel_RegisterPeerDesignTables_MalformedRejectsCleanly`
- **NoLeakedRegistrations** → `Kernel_RegisterTransient_NoLeakAfterUnregister`, plus the teardown snapshot assert in `Kernel_CustomFleet_ValidatesAndOverspendRejects`

Game builds verified: `dotnet build StarDrive.csproj` and `dotnet build StarDriveArena.csproj` both `0 Warning(s) 0 Error(s)`.

---

## 7. Deferred to later phases (not in this kernel)

- Ship-designer / formation-editor UI integration (Phases 2/4 of the plan) — this phase is headless-only.
- N>2 match flow: N-empire radial spawn, N-peer commit barrier + deterministic drop, last-fleet-standing win
  (Phase 5). `RegisterPeerDesignTables` is already authored N-generally (loops over peer tables).
- Budget/`MaxMatchSeconds` fix + in-match ammo model + `UnlimitedAmmo` toggle (Phase 3). Budget enforcement for
  custom fleets is proven working here via the existing `SumBundleCost`/`ValidateRuleset` path once designs are
  registered, but the `MaxMatchSeconds` derive-the-cap fix is NOT in this kernel.
- Persistent ammo bank + pay-to-rearm economy (Phase 7).
- Nested/custom carriers (`HangarShipUID` → any design) — rejected at validation for v0.

---

## 8. Notes for the orchestrator / reviewer

- Arena code lives in the **`StarDriveArena.csproj` plugin** (built to `game/Plugins/StarDriveArena.dll`), NOT in
  `StarDrive.csproj`. Both csprojs use `EnableDefaultCompileItems=false` — every new file needs an explicit
  `<Compile Include>` (done for both the kernel and the tests).
- `ArenaDesignTable.cs` uses `using ShipDesignT = global::Ship_Game.Ships.ShipDesign;` to disambiguate the
  `ShipDesign` TYPE from the shadowing `Ship_Game.GameScreens.ShipDesign` NAMESPACE (same idiom as `ArenaFleetBundle.cs`).
- One latent bug fixed in passing: `WithResolvedFleets` was constructing a fresh settings copy that DROPPED the new
  design-table fields; they are now carried through (they were also added to `ToStartMessage`/`FromStartMessage`/
  `WithRematchSeed`).
- With `EnableArenaCustomFleet = false` (the default), `RegisterPeerDesignTables` is a true no-op (registers nothing)
  and no design table is ever emitted or consumed — today's name-only behavior is unchanged.

# Arena Custom-Fleet Lockstep Desync — Root-Cause Investigation Report

Branch: `arena-045-port` · Trunk `b67039db5` · ProtocolVersion 5
Scope: verify the "host spawns GetClone, join spawns FromBytes(CanonicalPayload)" lead for the live turn-1232 desync (host 4 ships vs join 7), fix the determinism law, add a long-run soak proof.

Status: **Lead VERIFIED as a real host/join object split, but empirically DISPROVEN as the cause of the sim divergence.** The specific field that differs between the two spawn paths is provably NOT read by the deterministic combat sim. Read this before implementing the "round-trip the host through CanonicalPayload" fix — the fix is correct as determinism hygiene but, on its own, will NOT turn a reproduction RED→GREEN, because there is no reproduction from this vector. The real turn-1232 divergence lives elsewhere (candidate list below), and the soak proof is the instrument that will actually catch it.

---

## 1. The lead, verified in code

- **HOST setup path** — `ArenaFightScreen.Multiplayer.cs:307` (`CaptureSetupDesign`): registers `((ShipDesign)design).GetClone(wire)` — the full-fidelity authored/picked design, renamed to `@arena/<hash>`.
- **HOST lobby path** — `ArenaMultiplayerLobbyScreen.cs:842` (`RebuildSandboxScratchSet`): same `((ShipDesign)design).GetClone(wire)`.
- **JOIN path** — `ArenaDesignTable.cs:321` (`Decode`) → `ShipDesign.FromBytes(CanonicalPayload)` via `ArenaMultiplayerSession.RegisterPeerDesignTables` (`ArenaMultiplayerSession.cs:617`).

So yes: the two peers register objects produced by two different code paths for the same source ship. That part of the brief is correct.

Important nuance about which object survives to spawn: `ShipsManager.Add` (`ShipsManager.cs:51`) **overwrites** (delete-then-add) on a name collision — it is last-writer-wins, not skip-if-present. In the live flow both the setup rebuild (`TryRebuildAndBroadcastSetupStart` → `RegisterMultiplayerCustomDesigns` at `ArenaFightScreen.Multiplayer.cs:679`) and the lobby→fight handoff (`ArmMultiplayerLive` → `RegisterMultiplayerCustomDesigns` at line 799) tear the GetClone scratch down and re-register the `FromBytes` reconstruction before spawn. So in several paths **both peers actually converge on the reconstruction**. But even in the paths where the host keeps its GetClone, the analysis in §3 shows it does not matter.

## 2. What actually differs between GetClone(design) and FromBytes(CanonicalPayload(design))

Empirically measured (temporary probe over 30 real stock combat designs, live-authoring UID rebuild forced on the clone exactly as the ship designer does, then reconstructed via `CanonicalPayload`→`FromBytes`):

| Field | Diverges host↔join? |
|---|---|
| `DesignSlots` positional order (grid X,Y + ModuleUID per slot) | **0 / 30 — identical** |
| `BaseStrength` | **0 / 30 — identical** |
| Canonical payload bytes / content hash | **0 / 30 — identical** |
| `UniqueModuleUIDs` array order **and** `SlotModuleUIDMapping` | **30 / 30 — DIFFERENT** |

The *only* divergent field is the design's `UniqueModuleUIDs` order (HashSet iteration order in the live authoring path, `ShipDesign.cs:177-183`/`SetModuleUIDs`) vs the self-contained ordinal order that `CanonicalPayload` emits and `FromBytes` rebuilds. This is exactly the hazard the kernel amendment 2 was written to defeat.

## 3. Why that divergent field is provably inert for the sim (the lead does NOT explain turn-1232)

The spawned `Ship.ModuleSlotList` — the thing the per-frame combat sim iterates — is built **only** from `GetOrLoadDesignSlots()` in `DesignSlots` order (`Ship_Initialize.cs:38`, `CreateModuleSlotsFromData` at line 114-135, `ModuleSlotList[i] = ShipModule.Create(slot_i)`). Ship creation never consumes `UniqueModuleUIDs` or `SlotModuleUIDMapping` order. Since `DesignSlots` order is identical on both peers (measured, §2), **both peers build byte-identical ships** whether the design came from GetClone or FromBytes.

Every runtime read of `UniqueModuleUIDs` is out of the combat sim (ShipsWeCanBuild, tooltips, the design screen, serialization). The one place it feeds a hash — the arena design digest at `Authoritative4XSession.cs:1531` — **sorts the UIDs first** (`OrderBy(uid, Ordinal)`), so it is order-insensitive. `ModuleGridFlyweight` is a fully `readonly` immutable flyweight built from the (order-identical) `DesignSlots`, so sharing it via `MemberwiseClone` is harmless.

Corroborating evidence from the existing test suite: `ArenaMultiplayerSession.RunInProcess` already runs an **asymmetric PvP brawl to elimination at MaxTurns=900** with per-turn `hostSim.Hash()` vs `joinSim.Hash()` comparison (`ArenaMultiplayerLockstepTests.cs:831`, `RunTwoPeerLockstep` at `ArenaMultiplayerSession.cs:892`) and it is GREEN. In that harness both peers share the one reconstructed design object — but §3 shows a GetClone-sourced peer and a FromBytes-sourced peer spawn identical ships anyway, so sharing vs not sharing the design object makes no difference to ship state. That is why no existing test is red and why round-tripping the host design will not, by itself, create a red-then-green reproduction.

## 4. The determinism-hygiene fix (still worth applying) vs the real hunt

Recommended fix (defense in depth, does not change protocol, does not change the canonical payload):
- In `CaptureSetupDesign` (`ArenaFightScreen.Multiplayer.cs:307`) and `RebuildSandboxScratchSet` (`ArenaMultiplayerLobbyScreen.cs:842`), replace `((ShipDesign)design).GetClone(wire)` with a canonical round-trip: `ShipDesign.FromBytes(ArenaDesignTable.CanonicalPayload(design))`, then set `.Name = wire`. This makes the host register byte-for-byte what the join reconstructs, so both peers are provably identical in every field including `UniqueModuleUIDs`. It closes the class even though the class is currently inert.
- `FixedUpkeep`: confirmed **provably inert** for the arena sim. Its only reader is `ShipMaintenance.cs:40-42` (economy upkeep); the arena uses `BaseStrength` as cost and never runs empire maintenance. Dropping it from the canonical payload is safe. Do **not** re-add it or bump the payload version.

Because this fix touches only the flag-gated `EnableArenaCustomFleet` capture paths and produces the same `@arena/<hash>` names, it is protocol-neutral.

## 5. Where the real turn-1232 desync most likely lives (for the soak proof to catch)

A late (identical for ~1231 turns, single flip at 1232), asymmetric-fleet divergence is the signature of an order-sensitive combat resolution that only trips once a rare event (module death / ship death / pool depletion / a value tie) occurs. RNG is well-architected here — per-empire deterministic streams keyed by stable empire Id (`Empire.Authoritative.cs:37`, `UniverseState.cs:191-199`), and `Weapon.Random => Owner.Loyalty.Random` (`Weapon.cs:43`) — and parallel update is force-disabled in the MP path, so those are not the vector. The surviving suspects are first-wins-on-ties selectors and iteration-order-tied RNG consumption:

1. Repair target tie: `Ship_Repair.cs` `GetModuleToRepair` → `ModuleSlotList.FindMax(GetRepairPriority)` (`CollectionFindMax.cs:74`, strict `>`, first-wins). Two equally-damaged identical modules ⇒ order decides the winner.
2. AI target tie: `ShipAI.Combat.cs:422` `PotentialTargets.FindMax(GetTargetPriority)` — tie on priority ⇒ order decides target.
3. Module explosion selection: `Ship.cs:420` `ModuleSlotList.Filter(...).FindMax(ExplosionDamage)` — tie ⇒ order decides.
4. Weapon-fire / imprecision RNG consumed in weapon-iteration order (`Weapon.cs` fire jitter, `ShipAI.AttackRun` maneuver rolls, `MissileAI` phase): if any peer ever iterates weapons/modules in a genuinely different order for the same logical turn, the per-empire RNG stream desyncs from that point.

Note: with two truly-identical peers processing identical inputs, ModuleSlotList order is identical on both, so these ties resolve the SAME way on both — a pure identical-peer sim should not diverge from them. The live 2-machine desync therefore implies a genuine cross-peer state asymmetry feeding one of these. The design-identity split was the obvious candidate for that asymmetry and has been ruled out (§3). The soak proof must run **real per-peer reconstruction** and, to actually reproduce a 2-machine-style divergence, should be the instrument used to bisect which of the above sites (or a floating-point platform difference) is responsible — a same-process soak may well stay green precisely because both peers are bit-identical.

## 6. Soak proof — design guidance (not yet landed)

- Drive `RunTwoPeerLockstep` (or a thin variant) with two CUSTOM `@arena/<hash>` fleets, asymmetric (4 vs 7), to a few hundred turns (bound it — e.g. 300-400 — never 36000), asserting `hostSim.Hash() == joinSim.Hash()` on EVERY turn, not just the final digest.
- Do NOT let both peers share one design object. Register the host fleet from `GetClone` (current live behavior) into a first table and the join fleet from `FromBytes` reconstruction into a second, OR build two independent `ResourceManager.Ships` registrations, so the harness models real per-peer reconstruction rather than the shared-static shortcut (`ArenaCustomFleetKernelTests.cs:18-22` documents that shortcut as a known false-green).
- Add the direct kernel assertion: after the §4 fix, the host-registered and join-registered design for the same source are byte-identical — assert `CanonicalPayload(hostRegistered) == CanonicalPayload(joinRegistered)` AND that their `UniqueModuleUIDs`/`SlotModuleUIDMapping` match (the existing tests only assert canonical-byte equality, which was already true and is the blind spot that hid this).
- Expectation to be honest about: per §3, a same-process soak with the current code may **stay green** even with GetClone vs FromBytes on the two sides, because the spawned ships are identical. If so, the soak's value is as a permanent regression guard, and the true reproduction requires either a forced order perturbation on one peer or a cross-machine run. Flag this in the test so a green result is not misread as "the live desync is fixed."

---

## Bottom line for the orchestrator

- Confirmed root cause of the *object split*: host `GetClone(original)` vs join `FromBytes(CanonicalPayload(original))` — real, in both setup and lobby paths.
- Exact divergent field: `ShipDesign.UniqueModuleUIDs` order + `SlotModuleUIDMapping` (30/30 designs). Slot order, BaseStrength, module grid, canonical bytes are all identical.
- Does it explain turn-1232? **No.** That field is not read by the deterministic combat sim (ship spawn uses `DesignSlots` order only; the design digest sorts UIDs). The design-identity split is inert.
- Fix (both paths): round-trip the host design through `CanonicalPayload → FromBytes` before registering, so both peers are byte-identical. Correct as determinism hygiene; will not produce a RED→GREEN reproduction on its own. FixedUpkeep confirmed inert — keep it dropped.
- Other nondeterminism: the genuine turn-1232 vector is an order-sensitive combat selector or iteration-order-tied RNG consumption (§5 list, repair/target `FindMax` ties the strongest), triggered by a real cross-peer asymmetry that this investigation could not source from the design layer. The soak proof (real per-peer reconstruction, per-turn hash assert, bounded turns) is the right instrument to bisect it — with the caveat that a same-process soak may stay green because the peers are bit-identical.

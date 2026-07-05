# Upstreaming guide — for the BlackBox team

This document exists to make one thing easy: **if TeamStarDrive ever wants any part of this fork,
here is exactly what it touches and how we'd hand it over.** No expectations — the fork works as a
standalone overlay either way.

## What the fork is

Authoritative host–client multiplayer for the 4X campaign, plus an arena battle mode, on top of
`jupiter-patch-1.60.00045` (every change is a clean descendant of that tag — no history rewrites).

- The host runs the only real simulation; clients submit commands and replay host-authored
  snapshots. A deterministic 128-bit digest is compared every tick; a divergence is localized to
  the exact state row and recovered by resync.
- ~190 headless multiplayer tests, including an in-process host+clients fuzz/soak harness and
  full-game scenario tests (combat, invasion, economy, tech, pirates, exploration, mid-game resync).

## Where the code lives (isolation map)

| Layer | Location | Upstream footprint |
|---|---|---|
| MP core (snapshots, replay, digest, lobby, net, saves, telemetry) | `Ship_Game/Multiplayer/Authoritative/` (~24k lines) | **None** — new module |
| Arena mode | `Ship_Game/GameScreens/Arena/` + `StarDriveArena.csproj` plugin | **None** — new files, loads via the existing `PluginManager` |
| Per-type additive members (replication setters, passive-presentation state) | `*.Authoritative.cs` partial-class files next to core types (`Ship.Authoritative.cs`, `Empire.Authoritative.cs`, …) | **None** — new files; core types gained only a `partial` keyword |
| Inline engine hooks | Small edits inside existing engine files | **The real diff** — see below |

## The actual engine diff

Measured against `jupiter-patch-1.60.00045`, ignoring whitespace, excluding all new files:
**~124 existing files touched, roughly +4,100 / −550 lines**, most files with only one-to-four-line
touches. The main hook families:

1. **UI command routing** — player actions on screens route through `Authoritative4XClientContext.TrySubmit*`
   (so a client's click becomes a command to the host instead of a local mutation). Largest family;
   we plan to shrink it to one-line guards per handler with helper bodies in per-screen partials.
2. **Mutation guard** — one-line `[Conditional("DEBUG")]` asserts on replicated-state setters; zero
   cost in Release. These caught several real desyncs and we'd keep them, but they're trivially removable.
3. **`isPlayer` → `IsHumanControlled`** — a genuine "the player is not a singleton" correctness fix
   in tech/build/trade/diplomacy logic. **We'd offer this as a small standalone PR regardless of MP**
   (it degrades to exactly `isPlayer` in single-player).
4. **Sim gates** — a handful of branch points (host keeps simulating under menus; passive clients
   run presentation-only). Real semantic forks; each is one guarded call whose body lives in a partial.
5. **Replication apply calls** — the snapshot replay invoking the per-type setters from (the partials).

## Standalone pieces we'd PR independently (no MP required)

- **`isPlayer → IsHumanControlled`** correctness fix (small, self-contained).
- **`RandomBase` determinism seam** — SplitMix64 backend, explicit-seed diagnostics, save/restore of
  RNG state. Useful for any reproducibility work, incidental to MP.
- **`LocalPlayerForUi`** — UI resolves "the local player" through one accessor instead of assuming
  `UState.Player` (harmless in SP, prerequisite for any multi-human future).

## How we'd hand any of it over

Whatever shape you prefer: a PR against your `main`, a patch series per hook family, or you cherry-pick
and we adapt the fork. We maintain the fork against your releases either way, and we're happy to
rename/reframe anything about how the fork presents itself. Contact: GitHub issues here, or MoniVibe
on the BlackBox Discord.

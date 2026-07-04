# StarDrive Arena

A fork of **StarDrive BlackBox** — the community 64‑bit / MonoGame / .NET 8 rewrite of *StarDrive* — that adds **authoritative host–client multiplayer** to the 4X campaign and a dedicated **arena battle** mode, on top of the BlackBox Jupiter (1.60) engine.

> **Lineage & license.** This project builds on [StarDrive BlackBox](https://github.com/TeamStarDrive/StarDrive) by the BlackBox team, itself a decompiled‑and‑rewritten *StarDrive* (© Zero Sum Games). *StarDrive* remains under its Steam license. This fork exists for **educational and modding purposes only** and inherits the same restrictions: no personal financial gain, no DRM circumvention, and be reasonably respectful of the original developer, the software, and Steam.

---

## What this fork adds

### Authoritative 4X multiplayer

The campaign runs **host‑authoritative**: the host owns the *only* real simulation. Clients submit player commands and **render host‑authored snapshot state** rather than simulating independently, so every client stays in lockstep with the host without trusting client‑side computation.

- **Command model** — clients issue orders (move, colonize, build, research, diplomacy, fleet ops, invade, refit, …). The host validates and applies each authoritatively, then broadcasts the resulting state. A passive client never re‑authorizes a host‑accepted command; it applies the effect.
- **Snapshot replication** — world state is serialized each tick into a compact, per‑row payload (empires, planets, ships, fleets, troops, construction queues, tech, diplomacy, exploration, transforms, …) that passive clients apply.
- **Deterministic desync detection** — a portable 128‑bit state digest is compared between host and client every tick. Any divergence is localized to the exact diverging row (`firstDiff`) and, when needed, recovered by a full‑state resync.
- **Passive clients don't simulate gameplay** — they apply host snapshots and refresh presentation only. A debug‑time mutation guard catches any code path that tries to mutate replicated state locally, so desync‑inducing bugs surface in tests rather than in a live session.

### Arena battle mode

A standalone fleet‑combat arena (`StarDriveArena`) with its own multiplayer lobby, for head‑to‑head battles outside the campaign.

### Determinism & QA engineering

Multiplayer correctness is backed by a substantial, headless test surface:

- an **executable replication manifest** — one source of truth that drives payload emit, client replay, and the digest, so *"if it's hashed, it's replayed (or explicitly excluded)"* holds by construction;
- a **seeded soak/fuzz harness** — an in‑process host plus N passive clients run through thousands of fuzzed legal commands with per‑tick, per‑lane digest comparison and fully replayable failures;
- **headless QA scenarios** that reproduce real playtest situations — ground combat, fleet battles, economy, tech‑gated builds, pirate events, exploration/fog, and a full‑game combined soak — so this class of bug is caught without a live session.

---

## System requirements

- **OS**: Windows 10 (build 1803 / April 2018 Update) or later, including Windows 11. Older Windows 10 builds lack the per‑thread DPI APIs MonoGame 3.8 requires.
- **Architecture**: 64‑bit (x64)
- **Runtime**: .NET 8

## Building

- Install **Visual Studio 2022** with the `.NET desktop development` (.NET 8 SDK), `Desktop development with C++` (MSVC v143 + Windows 10 SDK) workloads.
- Clone **with submodules** (`--recurse-submodules`).

```
dotnet build StarDrive.sln -c Debug -p:Platform=x64
```

Run the test suite (multiplayer/arena coverage lives under `UnitTests/Multiplayer`):

```
dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64
```

Load with a content mod (the game ships with **Combined Arms**):

```
StarDrive.exe --mod="Combined Arms"
```

`StarDrive.exe --help` lists the developer CLI (texture/mesh export, hull/ship generation, localization, debug console, …).

---

## Credits

- ***StarDrive*** — Zero Sum Games (Daniel DiCicco).
- **StarDrive BlackBox** — the BlackBox team, whose 64‑bit / MonoGame 3.8 / .NET 8 rewrite this fork is based on. See the [upstream repository](https://github.com/TeamStarDrive/StarDrive) for the full engine changelog and the [Combined Arms](https://github.com/TeamStarDrive/CombinedArms) content mod, and their [Discord](https://discord.gg/dfvnfH4) for the engine and modding community.
- **Authoritative multiplayer & arena work** — MoniVibe.

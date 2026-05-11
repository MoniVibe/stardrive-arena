# Phase 5 Migration Plan — Release

## Context

[Phase 4](migration-plan-phase4.md) closes the development side of the x64 + MonoGame migration: Combined Arms regression sweep, zero-warnings gate, performance budget, visual polish, mesh-export toolchain decision (§4.7 — DONE 2026-05-08), NanoMesh upstream PR, and Steam SDK x64. Phase 5 is purely about turning that work into a downloadable artefact.

Three sub-phases:

1. **§5.0 — Mars-line forward-compat patch (cross-major upgrade discovery).** Ships *before* §5.1 work begins. A final 1.51.x Mars patch adds a new `MajorUpgradeAvailablePopup` that fires when GitHub reports a higher-major release (e.g., `1.60`) — the existing `IsSameMajorVer` silently suppresses the standard new-version popup on major-mismatch, so without §5.0 a 1.51 user has no in-game signal that Jupiter exists. Same code is forward-ported to the migration branch so 1.60 (and any future major bump) inherits the behavior. Authored on the Mars maintenance line, testable in dev by bumping the local AssemblyVersion to simulate a major mismatch.
2. **§5.1 — Cut 1.60 release.** Installer + ZIP. The major release is built **locally** on a clean dev machine (`Deploy/MakeInstaller.py --major`) and **manually uploaded** to itch.io — once-per-multi-year-cycle cadence doesn't justify a dedicated CI workflow. Patches ride the existing [.github/workflows/patch-build.yml](../.github/workflows/patch-build.yml) (push to `main` / `workflow_dispatch` → chunked ZIP + NSIS attached as draft GitHub Release assets), which is also the in-game `AutoUpdateChecker` channel (`api.github.com/repos/<owner>/<repo>/releases/latest`). Promoted from §4.11 in the prior plan so the release work has its own document and isn't gated on Phase 4 sign-off. **§5.1.B (code signing) is deferred** past 1.60 launch (forward-prep scripts already authored). **§5.1.C (Steam-folder install + UAC) is dropped** — Jupiter installs to `C:\Games\StarDrivePlus64`, a user-writable directory. **§5.1.D (release.yml authoring) is dropped** — patch-build.yml + manual itch.io covers the actual flow. **§5.1.F (publish-patch automation) is dropped** — patch-build.yml already attaches chunks to GitHub Releases, which IS the in-game updater channel.
3. **§5.2 — Migration close (optional, post-release).** PHASE4_RESULTS.md + ARCHITECTURE.md update + memory cleanup. Promoted from §4.10 in the prior plan and reordered after the release because the release is what ships value to users; the wrap-up doc captures what already lives in commits/memory and is not a release blocker.

**Related memory**:
- [project_phase2_backlog_runtime.md](c:/Users/gkapu/.claude/projects/c--Development-stardrive-BlackBoxPlus/memory/project_phase2_backlog_runtime.md) — Steam SDK execution recipe (relevant when verifying §4.9 in §5.1.E smoke)
- [reference_migration_plan.md](c:/Users/gkapu/.claude/projects/c--Development-stardrive-BlackBoxPlus/memory/reference_migration_plan.md) — Plan + log file locations

---

## Phase 5 Goals (Success Gate)

1. **BlackBox Jupiter 1.60 published** to itch.io (alongside — not replacing — the 1.51 Mars listing) with installer, ZIP, and release notes. Maintainer **builds locally** on a clean dev machine via `Deploy/MakeInstaller.py --major --type=nsis` (+ `--type=zip`) and **manually uploads** to itch.io's Edit Game → Uploads page. Tag `jupiter-release-1.60` pushed to git as a version marker (no automation triggers off it; patch-build.yml handles patches separately on push-to-main). **Codename**: BlackBox Jupiter (was Mars through 1.51).
2. *(DEFERRED — §5.1.B not on critical path for 1.60)* No SmartScreen "Windows protected your PC" warning. Mars-line behavior — users see and dismiss the warning — is the 1.60 baseline. Documented as a known-issue in `RELEASE_NOTES_1.60.md`. Revisit signing in a 1.60.x patch once we have install-rate data.
3. *(DEFERRED — §5.1.B)* `signtool verify /pa /v` reports valid Authenticode signatures on `StarDrive.exe`, `SDNative.dll`, and the installer EXE. Activated when §5.1.B activates.
4. **All three install scenarios pass** (see §5.1.E):
   - Clean machine, standalone install at the new default `C:\Games\StarDrivePlus64`.
   - Coexistence — 1.51 already installed at `C:\Games\StarDrivePlus`, 1.60's directory page defaults to `StarDrivePlus64` (Option A clean break: installer reads `HKLM\Software\StarDrivePlus64\InstallPath` only — empty on this machine — and never falls back to Mars's `HKLM\Software\StarDrive\InstallPath`); both versions launch independently, each sees only its own SaveGameVersion.
   - Manual upgrade-in-place — user explicitly browses to `C:\Games\StarDrivePlus`; 1.51 binaries replaced; saves preserved (1.51 v20 saves stay on disk but invisible to 1.60).
5. **README + Sentry release record updated** to point at 1.60.
6. **Cross-major upgrade discovery channel live**: §5.0's Mars-line patch shipping `MajorUpgradeAvailablePopup` already **published** as `mars-patch-1.51.15118` (✅ done 2026-05-09) before the Jupiter 1.60 itch.io upload. No deliberate user-soak needed — the patch *is* the discovery channel whenever a user lands on it via the standard intra-major auto-update. Same code forward-ported to the migration branch so 1.60 inherits the behavior for any future major bump.
7. *(Optional, §5.2)* **PHASE4_RESULTS.md committed; ARCHITECTURE.md updated** to mark the migration roadmap §9 items DONE; memory entries marked RESOLVED with commit refs.

**Anti-goals for Phase 5** (out of scope):
- Auto-update logic redesign. The existing in-game patch-check works for cumulative patches on top of a major release; a re-architecture is post-release work.
- Multi-platform builds. 1.60 is Windows-only (matches 1.51 distribution).
- Localization beyond what's already shipped.
- Marketing / store-page updates beyond the GitHub release notes.
- **Save-game format migration from v20 (Mars) to v21 (Jupiter).** SaveGameVersion bumps for save partitioning, not conversion — 1.51 saves are not loadable in 1.60 (and vice versa). Documented in `RELEASE_NOTES_1.60.md`. A future save-importer is its own workstream if ever needed.

---

## Confirmed Strategic Decisions

| Decision | Choice | Rationale |
|---|---|---|
| **Release codename** | **BlackBox Jupiter** (replaces Mars). 1.60 is the first Jupiter release; the Mars line ended at 1.51. | Jupiter (largest planet) fits the scale of this release — the x64 + MonoGame migration is the project's biggest single change set. Matches the project's planet-codenamed major-version pattern. |
| **Default install path** | `C:\Games\StarDrivePlus64` (was `StarDrivePlus` for Mars). Option A clean break: 1.60 installer ignores `HKLM\Software\StarDrive\InstallPath` (the Mars-line registry key) and writes its own `Software\StarDrivePlus64\InstallPath`. | Allows 1.51 and 1.60 to coexist on disk with no surprise overwrite. Users who want upgrade-in-place can manually browse to `C:\Games\StarDrivePlus`; the radio-button default just doesn't pre-select that. The `64` suffix is also a visual signal that this is the bitness-changed line. |
| **Save-game coexistence** | Bump `SavedGame.SaveGameVersion` from `20` (Mars) to `21` (Jupiter). Save folder stays single (`%APPDATA%\StarDrive\`) — no `Dir.StarDriveAppData` change. Each version's load-list filter at `LoadSaveScreen.cs:56` is exact-match on `SaveGameVersion`, so v20 and v21 saves are mutually invisible. | Cleaner than partitioning the appdata folder — no save-folder migration step, no orphaned saves if a user uninstalls one version. The trade is that 1.51 saves can't be loaded in 1.60; a future save-importer would be a separate workstream. |
| **Code-signing approach** | Microsoft Trusted Signing (default). Fall back to OV cert if Trusted Signing identity validation stalls; ship unsigned-with-followup-patch as last resort. | ~$10/month, inherits Microsoft reputation immediately. EV cert ($300–$500/yr + hardware token shipping) is the next step up but heavyweight for a community project. |
| ~~**Steam-folder install**~~ | DROPPED (§5.1.C). Default install path lives at `C:\Games\StarDrivePlus64` per §5.1.A — no Program Files writes, no UAC elevation needed. | The Steam-folder scenario was a Mars-line carryover; with Jupiter installing into a user-writable directory by default, none of the radio-button / `CheckSteam` machinery applies. Maintainer also has no SteamPipe push access, so co-locating with Steam's manifest is actively discouraged anyway. |
| **CI pipeline (patches)** | **Existing [.github/workflows/patch-build.yml](../.github/workflows/patch-build.yml)** — already replaced AppVeyor on the migration branch. Triggers on push to `main` + `workflow_dispatch`. Builds `Release\|x64`, runs unit tests, packages chunked patch ZIP + NSIS installer via `MakeInstaller.py --patch`, attaches assets to a draft GitHub Release tagged `jupiter-patch-<version>`, posts Discord notification. | Already working; nothing to author for §5.1. The legacy AppVeyor at `ci.appveyor.com/project/RedFox20/stardrive` is owned by an account we don't control and stays read-only — we never had write access there. |
| **CI pipeline (major releases)** | **None — built locally**. Maintainer runs `Deploy/MakeInstaller.py --major --type=nsis` (+ `--type=zip`) on a clean dev machine, then manually uploads to itch.io. | Major releases ship ~once per multi-year cycle; no return on automating a workflow that fires once every few years. (§5.1.D originally proposed authoring `release.yml` for this — DROPPED.) |
| **Distribution channel (full release)** | **itch.io** (primary), alongside — not replacing — the 1.51 Mars listing. Maintainer uploads manually through the project's Edit Game → Uploads page at `https://stardriveteam.itch.io/mars-160`. | Major releases happen rarely; the manual upload step is acceptable at that cadence and avoids carrying a butler-upload integration we'd touch only every few years. The `mars-160` slug is hardcoded in [game/upgrade-url.txt](../game/upgrade-url.txt) shipped to Mars 1.51 users via the §5.0 cross-major popup. |
| **Distribution channel (patches)** | **GitHub Releases** (draft attached automatically by patch-build.yml; maintainer publishes). The in-game [AutoUpdateChecker](../Ship_Game/GameScreens/MainMenu/AutoUpdateChecker.cs#L261-L265) polls `api.github.com/repos/<owner>/<repo>/releases/latest` directly and reads `assets[].browser_download_url` — GitHub Releases IS the in-game updater channel; no separate manifest CDN. | Single source of truth; no host-credentials to manage. (§5.1.F originally proposed a `publish-patch` job uploading to a separate CDN with manifest refresh — DROPPED, redundant.) |
| **Release artefact set** | NSIS installer (primary), full ZIP (single file), patch ZIP (cumulative, **split into 25 MB chunks** for the in-game updater's resumable downloads), Wix MSI (optional, kept around for parity). | NSIS is what most users download; full ZIP is for users who don't trust installers; patch ZIP chunking is the only place the 25 MB split survives — full release ZIPs go up as one file (itch.io has no 25 MB cap). |
| **Migration close shape (§5.2)** | Optional — PHASE4_RESULTS.md captures the dev-phase narrative if/when authored, but the release is what users see. | The Phase 1/2/3 RESULTS docs were authored before each release, but Phase 5's audience is the release page; the wrap-up doc is for future maintainers and can be written when bandwidth allows. |

---

## Sub-phase Index

| # | Title | Risk |
|---|---|---|
| 5.0 | Mars-line forward-compat patch: cross-major upgrade discovery popup (ships *before* §5.1) | Low |
| 5.1 | Cut 1.60 release: local build + manual itch.io upload; patches via existing patch-build.yml (signing DEFERRED §5.1.B; §5.1.C/D/F DROPPED) | Low |
| 5.2 | Migration close (optional, post-release): PHASE4_RESULTS.md + ARCHITECTURE.md update + memory cleanup | Low |

Each sub-phase ends with a commit and is rollback-able. §5.0 puts the discovery channel into 1.51 users' hands first; §5.1 then ships the Jupiter artefact; §5.2 documents the closed migration.

---

## 5.0 — Mars-line Forward-compat Patch: Cross-major Upgrade Discovery Popup

**Goal**: ship a final 1.51.x Mars patch *before* §5.1 release work begins that gives existing 1.51 users an in-game discovery channel for Jupiter. Same code is forward-ported into the migration branch so 1.60 inherits the behavior for any future major bump.

**Why this is §5.0 and not part of §5.1**: §5.0 ships through the existing Mars-line release pipeline against the **current** AutoUpdateChecker codebase; it has no dependency on Jupiter codename rename, install-path change, or SaveGameVersion bump. Promoting it ahead of §5.1 means the discovery channel is already live in users' hands before the §5.1.A version bump even starts — there's no risk of forgetting the §5.0 prerequisite mid-way through cutting Jupiter.

**Background — why a separate popup is needed**: [AutoUpdateChecker.IsSameMajorVer](../Ship_Game/GameScreens/MainMenu/AutoUpdateChecker.cs#L220-L235) compares `version.Split('.').Take(2)` between current and latest GitHub release. On mismatch (e.g., `1.51` vs `1.60`), [IsLatestVerNewer](../Ship_Game/GameScreens/MainMenu/AutoUpdateChecker.cs#L207-L218) returns `false` and the standard new-version popup is silently suppressed — log line `AutoUpdater: Major version mismatch ... Will not update` is the only trace. This is intentional: the in-game patch flow at [AutoPatcher.cs](../Ship_Game/GameScreens/MainMenu/AutoPatcher.cs) drops files into `Directory.GetCurrentDirectory()` and can't safely cross install-dir / SaveGameVersion / dependency-graph boundaries that a major bump introduces. So a 1.51 user has zero in-game signal that 1.60 exists. §5.0 adds a *second* popup that fires on major-mismatch instead of just logging — distinct from the standard NewVersionPopup, positioned top-left so it doesn't collide with intra-major patch popups in the bottom-right `Popups` UIList stack.

**Mod path** (verified): mods bypass the major-version check entirely — [line 212](../Ship_Game/GameScreens/MainMenu/AutoUpdateChecker.cs#L212) gates `IsSameMajorVer` on `!isMod`. Mod patches are files-into-folder operations with no install-dir / save-format invariants, so the in-game patcher can already cross "major" mod versions without the suppression. **No mod-path change in §5.0** — the new popup is vanilla-only, matching the existing asymmetry.

**Sub-steps**:

1. **Author `MajorUpgradeAvailablePopup`** alongside the existing [NewVersionPopup](../Ship_Game/GameScreens/MainMenu/AutoUpdateChecker.cs#L56-L146). Different visual: positioned at the **top-left of the screen** (not via the bottom-right `Popups` UIList at `Screen.Height * 0.6f`), so it doesn't fight for stack slots with intra-major patch notifications. Click → opens URL via `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })` and `Application.Exit()` (game closes so the user can run the new installer cleanly).

2. **URL config via on-disk txt file**. New file `game/upgrade-url.txt` containing the destination URL on a single line. Read at popup-creation time; first nonblank line wins. Fallback to a hardcoded constant in code (`https://stardriveteam.itch.io/mars-160`) if the file is missing/empty/IO-error. **Why on-disk**: lets us redirect users (itch.io URL changes, or we move the project to another store) without shipping another C# binary patch — push a txt-file-only patch through the existing AutoPatcher flow. Ship `upgrade-url.txt` (default content `https://stardriveteam.itch.io/mars-160`) in the Mars patch's `Release.txt` manifest *and* in the Jupiter `game/` baseline.

3. **Wire into AutoUpdateChecker**. Refactor [IsLatestVerNewer / IsSameMajorVer](../Ship_Game/GameScreens/MainMenu/AutoUpdateChecker.cs#L207-L235) so that on major-mismatch, the parallel-task path can `RunOnNextFrame` a `MajorUpgradeAvailablePopup` (the existing pattern at [line 152](../Ship_Game/GameScreens/MainMenu/AutoUpdateChecker.cs#L152) for cross-thread popup creation). Existing log line stays. The popup-creation site replaces the current bare `return false` in `IsSameMajorVer`.

4. **Dev-side test recipe** (before shipping). Verify the wiring against a real GitHub release without needing a live 1.60 to exist:
   - Locally bump `AssemblyVersion` *down* (e.g., to `1.50.99999`) so the running build sees the live 1.51 GitHub release as a "higher major" — `IsSameMajorVer` will return false, the new popup should fire.
   - Or bump *up* (e.g., to a fake `1.51.99999`) and create a draft GitHub release tagged `jupiter-release-1.60.16000` to validate the real major-mismatch path.
   - Either path validates the wiring + URL config + thread-marshaling without shipping anything to users.

5. **Ship the Mars patch**.
   - Branch from the last 1.51 commit on the Mars maintenance line (likely the predecessor of `migration/monogame_migration`; verify before starting against tag `mars-patch-1.51.15112`).
   - Bump `AssemblyVersion` `1.51.15112` → `1.51.<next-build>`; tag `mars-patch-1.51.<next-build>`.
   - Use whichever pipeline the Mars line ran on (legacy AppVeyor, if still operational; or mirror through the new GitHub Action).
   - Verify on a clean 1.51 install: in-game AutoUpdateChecker prompts (existing intra-major flow) → patch downloads + applies → restart → new code is live → next launch's GitHub poll triggers `MajorUpgradeAvailablePopup` against the dev-side staging release from step 4.

6. **Forward-port to `migration/monogame_migration`**. Cherry-pick the `MajorUpgradeAvailablePopup` class + AutoUpdateChecker wiring + `upgrade-url.txt` + manifest entry. Verify compile + smoke. The forward-port means Jupiter ships with the same major-mismatch popup from day one, and any 1.7+ release in the future automatically gives 1.60 users the same in-game discovery — the popup behavior is now permanent, not a Mars-line patch-only feature.

7. **Sequencing**. §5.0 must be **published** before §5.1.D's itch.io upload of the Jupiter 1.60 major. **No deliberate soak is required**: the patch itself is the discovery channel — whenever a 1.51 user lands on the patched build via standard intra-major auto-update (immediately after publish, or weeks later when they next launch), they'll see the cross-major popup pointing at the itch.io page for Jupiter. Without §5.0 shipped, 1.51 users have no in-game discovery for Jupiter — they'd have to find out through external channels alone (itch.io listing, README link). **Status**: §5.0 shipped 2026-05-09 as `mars-patch-1.51.15118`. ✅

**Tests added**:
- Dev-side (per step 4): bump local AssemblyVersion to `1.50.99999`, run game → on launch, GitHub poll fires `MajorUpgradeAvailablePopup` against the live 1.51 GitHub release; click opens browser to `upgrade-url.txt` URL; game exits.
- Smoke (manual, post-patch-ship): on 1.51.<patched>, point AutoUpdateChecker at a staging GitHub release tagged `jupiter-release-1.60.16000`, verify `MajorUpgradeAvailablePopup` fires top-left, click opens browser at `upgrade-url.txt` URL, game exits.
- Edge: missing `upgrade-url.txt` → popup fires, click opens hardcoded fallback URL (`https://stardriveteam.itch.io/mars-160`).
- Edge: malformed `upgrade-url.txt` (CRLF, BOM, multiple lines, blank lines) → first nonblank line wins; if no valid URL, fall back to hardcoded.
- Forward-port smoke: on 1.60.<build>, point GitHub at a hypothetical `saturn-release-1.70` (or whatever the next major codename becomes), verify same popup fires.
- Negative: 1.51.<patched> with GitHub returning a 1.51.<later-patch> → standard NewVersionPopup fires (existing intra-major flow), no MajorUpgradeAvailablePopup. Both code paths coexist.

**Verification**:
- 1.51.<patched> install + 1.60 release on GitHub → MajorUpgradeAvailablePopup fires top-left; click opens correct URL; game exits.
- 1.51.<patched> install + 1.51.<later-patch> release on GitHub → standard NewVersionPopup fires (no Major popup).
- 1.60 install (post-Jupiter) + 1.60.<later-patch> on GitHub → standard NewVersionPopup; no Major popup.
- Forward-port verified: 1.60 install + simulated 1.70 release on GitHub → MajorUpgradeAvailablePopup fires (proves the migration-branch port works).

**Rollback**:
- Mars patch can be rolled back via standard patch rollback. Existing 1.51.<patched> users keep the new behavior harmlessly (popup keeps firing, click leads to whatever `upgrade-url.txt` says — push a txt-only patch to redirect or neutralize if needed).
- Migration-branch forward-port can be reverted via `git revert` of the cherry-picked commit if a regression surfaces during §5.1.E smoke before the major release ships.

**Risk**: Low. The change is additive: failure mode is the popup doesn't fire (= existing silent log-only behavior, no regression). One concern worth flagging: AutoUpdateChecker runs the version-check on `Parallel.Run`; popup creation must use `RunOnNextFrame` for thread-safe UI marshaling (the pattern is already used at line 152). Audit the wiring in step 3 to confirm marshaling discipline before shipping the Mars patch.

---

## 5.1 — Cut 1.60 Release: Installer + ZIP

**Goal**: Ship the first post-migration public release as **BlackBox 1.60**. The release pipeline largely inherits Mars 1.51's machinery (NSIS + Wix MSI + ZIP via `Deploy/MakeInstaller.py`); the 1.60-specific changes are scoped tightly: §5.1.A bumps codename + version + install-path + save-format; §5.1.D wires the build pipeline into a GitHub Action (replacing AppVeyor); §5.1.E smoke-tests; §5.1.F adds patch-pipeline automation. **§5.1.B (code signing) is deferred** — see that section for rationale; users will see the same SmartScreen warning Mars 1.51 users saw, documented as a known-issue in `RELEASE_NOTES_1.60.md`. **§5.1.C (Steam-folder install + UAC) is dropped** — Jupiter's default install path is `C:\Games\StarDrivePlus64`, a user-writable directory, so none of the Program-Files / Steam-folder machinery is needed.

**Context — what the 1.51 release looked like** (from `Deploy/`, `README.md`, GitHub releases page) and what changes for 1.60:
- Version string lives in `Properties/AssemblyInfo.cs::AssemblyVersion`. Current value: `1.51.15100`. Pattern: `MAJOR.MINOR.BUILD` (mod version + monotonic build counter from AppVeyor's `APPVEYOR_BUILD_VERSION`).
- Three installer artefacts produced by `Deploy/MakeInstaller.py`:
  - **NSIS** (`BlackBox-Jupiter.nsi` full / `BlackBox-Jupiter-Patch.nsi` cumulative patch) → `Deploy/upload/BlackBox_Jupiter_<version>.exe`. The Mars-line `.nsi` filenames are renamed at §5.1.A (codename change for the post-migration release line — see [project_jupiter_codename.md](../../../Users/gkapu/.claude/projects/c--Development-stardrive-BlackBoxPlus/memory/project_jupiter_codename.md)).
  - **ZIP** — for 1.60, the **full release ZIP is a single file** (itch.io has no 25 MB upload cap). The 25 MB chunking survives only on the **cumulative patch ZIP** (`BlackBox-Jupiter-Patch.nsi` flow), where the in-game patch updater benefits from chunked, resumable downloads on slow connections.
  - **MSI** (Wix, `Deploy/SDInstaller.wixproj` + `Deploy/Product.wxs`) — kept around but not the primary distribution channel
- Default install path: `C:\Games\StarDrivePlus` for the Mars line (NSIS line 76 in `Deploy/BBInstaller.nsi`). **For the Jupiter line, default changes to `C:\Games\StarDrivePlus64`** — the `64` suffix signals the bitness change so users with both versions on disk can tell them apart, and Mars-line registry-driven upgrade-in-place behavior at `BBInstaller.nsi:66-68` is removed (clean-break Option A; see §5.1.A and [project_jupiter_install_path.md](../../../Users/gkapu/.claude/projects/c--Development-stardrive-BlackBoxPlus/memory/project_jupiter_install_path.md)). Steam-detection code is commented out at lines 70–74 — the previous team had it in mind but disabled it, almost certainly because the installer doesn't request UAC elevation today.
- Distribution: 1.51 lives on GitHub Releases at `https://github.com/TeamStarDrive/StarDrive/releases/tag/mars-release-1.51`. **For 1.60 we move primary distribution to itch.io**. The full-release upload is **manual** (Edit Game → Uploads page) — major releases happen rarely (~once per multi-year cycle), so the manual step is fine. `notify-sentry-of-release.bash` continues to post a Sentry release record. The git tag `jupiter-release-1.60` stays as the version marker.
- Auto-update: in-game logic checks for newer patch versions on launch and prompts to install; works for cumulative patches on top of a major release. The patch ZIP feeding this flow is the chunked artefact.
- CI (patches): the existing [.github/workflows/patch-build.yml](../.github/workflows/patch-build.yml) handles patches end-to-end — push to `main` (or `workflow_dispatch`) → build `Release|x64` → unit tests → `Deploy/MakeInstaller.py --patch` (auto-chunks at 25 MB) → draft GitHub Release with chunks + NSIS attached → Discord notification. The legacy AppVeyor at `ci.appveyor.com/project/RedFox20/stardrive` is owned by an account we don't control and stays read-only.
- CI (major releases): **none — built locally**. Maintainer runs `Deploy/MakeInstaller.py --major` on a clean dev machine and uploads to itch.io manually. ~Once per multi-year cycle; not worth automating.
- In-game updater channel: [AutoUpdateChecker](../Ship_Game/GameScreens/MainMenu/AutoUpdateChecker.cs#L261-L265) polls `api.github.com/repos/<owner>/<repo>/releases/latest` directly and reads `assets[].browser_download_url` for each chunk. **GitHub Releases is the channel** — no separate manifest CDN to maintain.

### Sub-steps

**§5.1.A — Version bump + codename + install-path + save-version + release notes**

The post-migration release line is **BlackBox Jupiter** (was Mars through 1.51) — Jupiter is the largest planet, fitting the scale of the x64 + MonoGame migration. The default install dir changes to `C:\Games\StarDrivePlus64`, and `SavedGame.SaveGameVersion` bumps so 1.51 and 1.60 saves coexist by mutual invisibility (no corruption — each version filters the other's saves out of the load list).

1. Bump `Properties/AssemblyInfo.cs::AssemblyVersion` from `1.51.15100` to `1.60.00000`. The build counter is now provided by `github.run_number` (zero-padded to 5 digits) at CI build time via patch-build.yml's "Patch AssemblyInfo files" step — the static value in the repo is just a local-dev marker.

2. **Codename rename Mars → Jupiter.** Rename and edit:
   - [Deploy/BlackBox-Mars.nsi](../Deploy/BlackBox-Mars.nsi) → `Deploy/BlackBox-Jupiter.nsi` (full installer NSIS script)
   - [Deploy/BlackBox-Mars-Patch.nsi](../Deploy/BlackBox-Mars-Patch.nsi) → `Deploy/BlackBox-Jupiter-Patch.nsi` (cumulative patch NSIS)
   - [Deploy/MakeInstaller.py:41-42](../Deploy/MakeInstaller.py#L41-L42) — `BlackBox-Mars.nsi` / `BlackBox-Mars-Patch.nsi` paths
   - [Deploy/MakeInstaller.py:54](../Deploy/MakeInstaller.py#L54) — `BlackBox_Mars_{BUILD_VERSION}.zip` archive filename → `BlackBox_Jupiter_{BUILD_VERSION}.zip`
   - [Deploy/Product.wxs](../Deploy/Product.wxs) + [Deploy/SDInstaller.wixproj](../Deploy/SDInstaller.wixproj) — Wix MSI metadata: product/upgrade names, output filename
   - [Ship_Game/GlobalStats.cs:313-314](../Ship_Game/GlobalStats.cs#L313-L314) — `ExtendedVersion = $"Mars : {Version}"` and `ExtendedVersionNoHash = $"Mars : {...}"` → `Jupiter`. This string surfaces in game logs ([Log.cs:139](../Ship_Game/Utils/Log.cs#L139)), Sentry/breadcrumb context, and `ResourceManager.LoadCommon` log lines ([ResourceManager.cs:246-248](../Ship_Game/Data/ResourceManager.cs#L246-L248)) — the codename is what shows up in every diagnostic.
   - [Ship_Game/GlobalStats.cs:44-45](../Ship_Game/GlobalStats.cs#L44-L45) — refresh the example doc-comments (`"Mars : 1.20.12000 develop/f83ab4a"`) to use a Jupiter example.
   - `README.md` — release-name + badge label references

3. **Default install path → `C:\Games\StarDrivePlus64`** (clean-break from Mars, but registry-driven upgrade-detection within the Jupiter line — Option A refined). Edit [Deploy/BBInstaller.nsi:64-78](../Deploy/BBInstaller.nsi#L64-L78) `.onInit`:
   - Update `${REGPATH}` (defined earlier in the file) to `Software\StarDrivePlus64` so the installer never reads or writes Mars's `Software\StarDrive` key.
   - Keep the `ReadRegStr $PREVDIR HKLM ${REGPATH} InstallPath` mechanic, but point it at the new `Software\StarDrivePlus64` key. This makes subsequent Jupiter patches (1.60.x → 1.60.y) detect the existing Jupiter install and seed `$INSTDIR` from `$PREVDIR` as usual — same registry-driven upgrade ergonomics the Mars line had, scoped to the Jupiter key.
   - **Cross-major fresh install (no Jupiter key yet)**: when `$PREVDIR` is empty after the `ReadRegStr`, default `$INSTDIR` to `C:\Games\StarDrivePlus64` and let the user override via the standard NSIS directory page. **Do not** fall back to reading `Software\StarDrive` (Mars) or any Steam path — the user must explicitly browse there if they want it. This is what prevents the installer from silently landing inside the Steam app dir for Steam-Mars users (the maintainer has no SteamPipe push access — see project_steam_partner_access.md — so co-locating Jupiter under Steam's manifest creates a silent mismatch).
   - Mirror the Wix HKCU registry path in [Deploy/Product.wxs:68,78](../Deploy/Product.wxs#L68): `Software\StarDrivePlus` → `Software\StarDrivePlus64`.
   - Mirror the Wix `INSTALLFOLDER` Name in [Deploy/Product.wxs:43](../Deploy/Product.wxs#L43): `StarDrivePlus` → `StarDrivePlus64`.

   Users who explicitly want to upgrade-in-place over their existing Mars 1.51 can manually browse to `C:\Games\StarDrivePlus` from the directory page; the default just never pre-selects that.

4. **Bump `SaveGameVersion`** to partition saves cleanly. Edit [Ship_Game/SavedGame.cs:27](../Ship_Game/SavedGame.cs#L27): `public const int SaveGameVersion = 20` → `21`. Effect: the exact-match filter at [LoadSaveScreen.cs:56](../Ship_Game/GameScreens/LoadSaveItems/LoadSaveScreen.cs#L56) means 1.51 sees only Version-20 saves, 1.60 sees only Version-21 saves. Save folder stays single (`%APPDATA%\StarDrive\`) — no `Dir.StarDriveAppData` change. No save corruption; both versions silently filter the other's saves out of the load list.

5. **README.md is deferred to §5.1.D** (after the Jupiter itch.io page is live with downloadable artefacts). Updating it now would publish a dead `https://stardriveteam.itch.io/mars-160` link on the public repo and rebrand "current release" to a release that hasn't shipped yet. §5.1.D will: point "Current Major Release Link" at the itch.io page, replace the "BlackBox - Hyperion" future-goals list with a "BlackBox Jupiter 1.60 — 64-bit + MonoGame" achievements list, refresh the dev-setup branch hint (`mars-1.51` → `main`), and refresh the CLI example log line (`Mars : 1.51.15100` → `Jupiter : 1.60.<build>`).

6. Author `RELEASE_NOTES_1.60.md` summarizing user-visible changes since 1.51:
   - **Codename: Jupiter** (replaces Mars). 1.60 is the first Jupiter release.
   - **Default install path is now `C:\Games\StarDrivePlus64`** (was `C:\Games\StarDrivePlus`). Both versions can coexist on disk.
   - **1.51 saves are not loadable in 1.60** (and vice versa) — the SaveGameVersion bump partitions the load list. No saves are deleted or corrupted; each version just filters the other's saves out of the menu.
   - 64-bit engine (no more 4 GB limit; Combined Arms + huge galaxies stable).
   - MonoGame 3.8 renderer (XNA + SunBurn replaced).
   - All 6 broken effects restored (BeamFX, scale, Thrust, desaturate, BasicFogOfWar, PlanetHalo).
   - Skinned/animated mesh playback (Ralyeh ship17 family articulates).
   - Material maps (normal/specular/emissive) on all hulls.
   - Bloom + screen-space distortion + fog-of-war post-process passes.
   - Basic shadow maps.
   - Steam SDK x64 via Steamworks.NET (achievements/stats/cloud saves work in 64-bit).
   - Combined Arms compatible.

**§5.1.B — Code signing — DEFERRED**

> **Status (2026-05-09): DEFERRED past the Jupiter 1.60 launch.** Mars 1.51 and earlier shipped unsigned and the project survived. Cost/benefit for the §5.1 launch tilted toward "ship faster, defer signing until we have data on whether SmartScreen friction hurts install rates." The signing scripts ([Deploy/sign-binaries.ps1](../Deploy/sign-binaries.ps1) — dual-mode Trusted Signing + local thumbprint) and verify gate ([Deploy/SignedBinaryCheck.ps1](../Deploy/SignedBinaryCheck.ps1)) are already authored, smoke-tested with a self-signed cert, and committed as forward-prep — flipping signing on in a later patch is one PR plus 1–3 business days of Microsoft identity validation. See [Deploy/SIGNING.md](../Deploy/SIGNING.md) for the procurement checklist + GitHub Actions secret names. **Activation procedure** when ready: (1) maintainer completes Trusted Signing procurement per SIGNING.md §1, (2) sets the seven repo secrets per SIGNING.md §2, (3) opens a PR adding the signing + verify steps to release.yml per SIGNING.md §3 Option A, (4) ships as a 1.60.x patch.

The blocker today (deferred): an unsigned EXE downloaded from the internet triggers SmartScreen "Windows protected your PC" dialog, which most users dismiss as malware. We need an authenticode signature on `StarDrive.exe`, `SDNative.dll`, and the installer EXE itself to defeat that dialog. Documented here for the activation sub-phase, not for §5.1.

**Signing options** (pick one in §5.1 entry):

| Option | Cost | Reputation | Notes |
|---|---|---|---|
| **Microsoft Trusted Signing** | ~$10/month | Inherits Microsoft's reputation immediately | New service (formerly Azure Code Signing). Requires Azure account + identity verification. **Recommended.** |
| **EV code-signing certificate** (DigiCert / Sectigo) | ~$300–$500/year | Skips SmartScreen warning from day 1 | Hardware token shipping required; less convenient for community projects. |
| **OV code-signing certificate** | ~$80–$200/year | Builds reputation over weeks/months of downloads | Cheapest paid option but doesn't immediately defeat SmartScreen — early users still see warnings until reputation builds. |
| **Self-signed** | Free | None — SmartScreen always flags | Only useful for internal testing. Not for public release. |

Steps:
1. Pick the signing approach. **Default recommendation: Microsoft Trusted Signing** for the cost/reputation balance.
2. Acquire the certificate / set up Trusted Signing identity validation.
3. Sign the binaries via `signtool.exe` after the build, before the installer is packaged. Three things get signed:
   - `game/StarDrive.exe`
   - `game/SDNative.dll`
   - The installer EXE itself (sign as the last step, after MakeInstaller produces it)
4. Add a signing step to the build pipeline:
   ```powershell
   signtool.exe sign /tr http://timestamp.digicert.com /td sha256 /fd sha256 /a "$file"
   ```
   The `/tr` timestamp ensures the signature stays valid after the cert expires.
5. Verify on a clean Windows install: download the installer through a browser, run it, confirm SmartScreen does NOT show "Windows protected your PC".

**§5.1.C — Steam-folder install path — DROPPED**

> **Status (2026-05-09): DROPPED. Not needed for Jupiter 1.60 or any planned successor.** §5.1.A locked the Jupiter default install path at `C:\Games\StarDrivePlus64`, which is a user-writable location that doesn't require UAC elevation. The Steam-folder install scenario was a Mars-line carryover that targeted `C:\Program Files (x86)\Steam\steamapps\common\StarDrive\`; with Jupiter installing into its own non-Program-Files directory, none of this sub-phase's machinery applies — no `RequestExecutionLevel admin`, no `CheckSteam` registry probe, no replace-vs-standalone radio-button page, no backup-the-original-StarDrive-1 dance. The `CheckSteam` block in `Deploy/BBInstaller.nsi` was already deleted in §5.1.A commit 2 (06b199b12).
>
> A user who *explicitly* wants Jupiter under their Steam folder can still browse the directory page there during install — but they'd need to "Run as Administrator" the installer themselves to write into Program Files, and the maintainer has no SteamPipe push access (see [project_steam_partner_access.md](../../../Users/gkapu/.claude/projects/c--Development-stardrive-BlackBoxPlus/memory/project_steam_partner_access.md)) so co-locating with Steam's manifest creates a silent mismatch — actively discouraged, not supported.
>
> Original §5.1.C body preserved below in case the Steam-folder scenario ever needs to come back (e.g. if SteamPipe access is acquired and a real Steam-line ships):
>
> ~~Steam typically installs StarDrive 1 to `C:\Program Files (x86)\Steam\steamapps\common\StarDrive\`. Writing there requires UAC elevation. Steps: (1) add `RequestExecutionLevel admin` to the NSIS installer; (2) uncomment `CheckSteam` in `Deploy/BBInstaller.nsi` to probe `HKLM\SOFTWARE\WOW6432Node\Valve\Steam\InstallPath`; (3) present a radio-button page offering `Replace original StarDrive 1 in Steam folder` vs `Install to standalone folder` (standalone pre-selected); (4) when Steam path is chosen and an existing StarDrive 1 is present, back up `StarDrive.exe` + `Content/` to `<INSTDIR>\Original_StarDrive_Backup\` and verify Steam isn't running; (5) leave the Steam manifest alone (Steam's manifest still reads "StarDrive 1.0").~~

**§5.1.D — Major release: local build + manual itch.io upload**

> **Status (2026-05-09): the original §5.1.D scope (author `release.yml` to mirror AppVeyor's full-release pipeline; tag-triggered build artefacts; download from workflow run; conditional `publish-patch` job) is DROPPED.** The patch pipeline already exists at [.github/workflows/patch-build.yml](../.github/workflows/patch-build.yml) — push to `main` (or `workflow_dispatch`) builds a chunked patch ZIP + NSIS installer and attaches them as a draft GitHub Release, which the in-game `AutoUpdateChecker` polls via `api.github.com/repos/<owner>/<repo>/releases/latest` directly (no separate manifest CDN). Major releases ship rarely (~once per multi-year cycle) and go to itch.io, **not** GitHub Releases — there's no return on automating a workflow that fires once every few years.

What §5.1.D actually requires for the Jupiter 1.60 major release:

1. **Build locally on a clean dev machine** at the `jupiter-release-1.60` tag (or current `main` after the migration merge):
   ```powershell
   # from the repo root, after a fresh build of Release|x64:
   py -3 Deploy/MakeInstaller.py --root_dir=. --major --type=nsis  # NSIS full installer
   py -3 Deploy/MakeInstaller.py --root_dir=. --major --type=zip   # full ZIP (single file — itch.io has no 25 MB cap)
   ```
   Output lands in `Deploy/upload/`. Double-check both artefacts open cleanly + the NSIS installer's directory page pre-fills `C:\Games\StarDrivePlus64`.

2. **Upload manually** to the new itch.io project at `https://stardriveteam.itch.io/mars-160` via Edit Game → Uploads page. Set the user-version on each upload to `1.60.<build>` so itch.io's "latest" tracking works. (The slug name is `mars-160` for legacy reasons — the §5.0 cross-major popup hardcodes that URL in [game/upgrade-url.txt](../game/upgrade-url.txt) shipped to Mars 1.51 users.)

3. **Run `Deploy/notify-sentry-of-release.bash`** with `APPVEYOR_BUILD_VERSION=1.60.<build>` (manual after the upload; env var name preserved for compat with the existing script).

4. **Tag `jupiter-release-1.60`** on the merged commit and push to GitHub. The tag is a version marker — nothing automated triggers off it. Required so future patches' "since previous release" commit summaries have a baseline.

5. **README.md refresh** (deferred from §5.1.A — must happen *after* the itch.io upload in step 2 so the link resolves):
   - Update the "Current Major Release Link" to point at `https://stardriveteam.itch.io/mars-160` (replacing the 1.51 GitHub Releases link).
   - Replace the AppVeyor build badge with the patch-build.yml one (or remove if the legacy badge is misleading).
   - Replace the "BlackBox - Hyperion" future-goals list with a "BlackBox Jupiter 1.60 — 64-bit + MonoGame" achievements list (the migration is now done).
   - Update the intro paragraph: "current release is BlackBox - Mars" → "current release is BlackBox - Jupiter (the post-migration release line)".
   - Refresh the dev-setup branch hint: `Switch to mars-1.51 branch` → `Switch to main branch`.
   - Refresh the CLI example log line: `Mars : 1.51.15100 develop-latest` → `Jupiter : 1.60.<build>`.

6. **Prerequisite** — §5.0 must already have shipped (`mars-patch-1.51.15118` published, ✅ as of 2026-05-09). No deliberate user-soak is needed — the patch *is* the discovery channel whenever a user lands on it via standard intra-major auto-update — but §5.0 must have happened, otherwise existing 1.51 users get no in-game discovery channel for Jupiter.

**§5.1.E — Smoke test on three install scenarios**
1. **Clean machine, standalone install (default path)**: download installer via Edge or Chrome, run it (SmartScreen will warn — click "More info" → "Run anyway"; this is expected behavior for 1.60 with §5.1.B deferred), accept the default `C:\Games\StarDrivePlus64`, complete install, launch game. Then **re-run the same installer**: confirm the directory page is pre-filled from `HKLM\Software\StarDrivePlus64\InstallPath` (the value the first install just wrote) — this validates the Jupiter-line registry-driven upgrade path that future full-installer reinstalls (e.g. 1.60.0 → 1.60.5 catch-up) rely on.
2. **Coexistence — 1.51 already installed at `C:\Games\StarDrivePlus`**: run the 1.60 installer, accept the default `C:\Games\StarDrivePlus64`. Verify (a) installer does NOT default to the 1.51 path (Option A clean break — no `HKLM\Software\StarDrive\InstallPath` detection); (b) installer writes `HKLM\Software\StarDrivePlus64\InstallPath`, leaving `Software\StarDrive` untouched; (c) post-install, both `StarDrive.exe` (1.51) and `StarDrivePlus64\StarDrive.exe` (1.60) launch independently; (d) saves at `%APPDATA%\StarDrive\` are visible to both, but each version's load list shows only its own SaveGameVersion (1.51 sees v20, 1.60 sees v21).
3. **Manual upgrade-in-place** (user explicitly chooses old path): on a 1.51 machine, run the 1.60 installer, browse the path-picker to `C:\Games\StarDrivePlus`, complete install. Confirm 1.51 binaries are replaced in place; 1.51 desktop shortcut now launches 1.60. Saves preserved (load list shows only v21 saves; v20 saves still on disk but invisible until user reinstalls 1.51).

**§5.1.F — Post-release patch automation — DROPPED (already covered by patch-build.yml)**

> **Status (2026-05-09): DROPPED.** The original §5.1.F scope (a conditional `publish-patch` job uploading chunks to a separate in-game updater CDN + refreshing a patch manifest JSON) is redundant. The in-game [AutoUpdateChecker](../Ship_Game/GameScreens/MainMenu/AutoUpdateChecker.cs#L261-L265) polls `https://api.github.com/repos/<owner>/<repo>/releases/latest` directly and reads `assets[].browser_download_url` for each chunk — **GitHub Releases is the in-game updater channel**, no separate manifest CDN involved. The existing [.github/workflows/patch-build.yml](../.github/workflows/patch-build.yml) already handles the entire patch flow:
>
> - Triggers on push to `main` + `workflow_dispatch`.
> - Stamps `1.60.<github.run_number>` into all four `AssemblyInfo.cs` files.
> - Builds `Release|x64`, runs unit tests via vstest.console.
> - Calls `Deploy/MakeInstaller.py --patch --type=zip` (auto-chunks at 25 MB via [MakeInstaller.py:60-80](../Deploy/MakeInstaller.py#L60-L80)) and `--type=nsis`.
> - Generates a commit summary since the previous patch tag.
> - Creates a **draft** GitHub Release tagged `jupiter-patch-<version>` with all chunks + the NSIS installer attached as assets.
> - Posts a Discord notification on success/failure via `DISCORD_WEBHOOK_URL`.
>
> Maintainer publishes the draft Release → in-game `AutoUpdateChecker` finds it on the next launch poll → `NewVersionPopup` fires → user clicks → `AutoPatcher` downloads each chunk via `browser_download_url`, reassembles via [PostProcessMultipleZipChunks](../Ship_Game/GameScreens/MainMenu/AutoPatcher.cs#L136-L169), applies. End-to-end loop works without any §5.1.F-style automation.
>
> Sentry release notify is *not* currently wired into patch-build.yml. If we want it, it'd be a small addition (a step that runs `Deploy/notify-sentry-of-release.bash` on `success()`); but it's not on the critical path for shipping patches and can be added when there's a Sentry signal we want correlated with a release tag.

**Verification per patch (note to self when shipping a patch)**: after the patch-build.yml workflow runs (push to main or workflow_dispatch), check that the resulting GitHub Release draft has all expected assets attached, then publish the draft. The in-game `AutoUpdateChecker` will pick it up on the next user-launch poll. If the workflow run failed, do **not** publish the draft — diagnose the failure and re-run.

### Tests added
- *(DEFERRED — §5.1.B activation only)* [Deploy/SignedBinaryCheck.ps1](../Deploy/SignedBinaryCheck.ps1) — runs `signtool.exe verify /pa /v` against `StarDrive.exe`, `SDNative.dll`, and the installer EXE. Authored + smoke-tested as forward-prep; not wired into the §5.1 release pipeline.

### Verification
- All three smoke scenarios pass. Scenario 1 will trigger SmartScreen ("More info" → "Run anyway") since 1.60 ships unsigned per §5.1.B deferral; the rest are signing-independent.
- *(DEFERRED — §5.1.B activation only)* `signtool verify` reports valid Authenticode signatures on the three target binaries.
- Local build of `Deploy/MakeInstaller.py --major` produced an NSIS installer + full ZIP under `Deploy/upload/`; both are tested by the maintainer (open NSIS installer's directory page; extract ZIP and run `StarDrive.exe`) before upload.
- itch.io project at `https://stardriveteam.itch.io/mars-160` shows BlackBox Jupiter 1.60 as a published upload at version `1.60.<build>`. Cross-major popup on a Mars 1.51.15118+ install successfully launches that URL when clicked.
- README updated to point at itch.io; Sentry release record posted.
- 1.51 → 1.60 in-place upgrade preserves saves (manual upgrade scenario in §5.1.E).
- *(Per-patch verification — separate from initial 1.60)* `patch-build.yml` workflow ran green; draft GitHub Release `jupiter-patch-<version>` has all expected chunks + NSIS installer attached as assets; maintainer publishes the draft.

### Rollback
- **Major (1.60 itself)**: archive (or hide) the 1.60 build on itch.io via Edit Game → Uploads page; users see the prior listing until a fixed version replaces it. Revert version-bump + README + plan commits via `git revert`. Delete the `jupiter-release-1.60` tag locally + on origin (`git push origin :refs/tags/jupiter-release-1.60`) if the tag needs to be re-cut on a fixed commit. Mars 1.51 binaries are unaffected — users on 1.51 stay on 1.51 until they choose to upgrade.
- **Patch (1.60.x)**: unpublish the GitHub Release (set back to draft) so the in-game `AutoUpdateChecker` stops finding it on `releases/latest` polls. Optionally publish a follow-up patch with the fix. Users who already auto-applied the bad patch get the new patch on their next launch.

### Risk
**Low.** With §5.1.B/C/D/F all dropped or deferred, §5.1's remaining work is mechanical: §5.1.A (DONE), §5.1.E (smoke test), local build + manual itch.io upload. The only residual unknown is:
1. *(Signing — §5.1.B activation only, not §5.1)* Trusted Signing setup involves Microsoft identity verification with unpredictable timing (1–14 days). When the maintainer activates §5.1.B in a future patch, the activation PR can wait on validation without blocking unrelated work — the scripts already exist; only the CI wiring + secrets setup is the activation effort.

*(Steam-folder install + UAC elevation paragraph removed — §5.1.C was dropped. Jupiter installs to `C:\Games\StarDrivePlus64`, so no UAC change is introduced.)*

---

## 5.2 — Migration Close (Optional, Post-Release): PHASE4_RESULTS.md + ARCHITECTURE.md Update + Memory Cleanup

**Status**: Optional. The release in §5.1 is what ships value to users; the wrap-up doc captures the dev-phase narrative for future maintainers and is not a release blocker. Ship §5.1 first; do §5.2 when bandwidth allows.

**Goal**: Sign off the migration as a development effort. Produce `PHASE4_RESULTS.md`. Update ARCHITECTURE.md to reflect the post-migration state. Mark all migration-related memory entries RESOLVED.

**Steps**:
1. **Final runtime smoke** (post-release confirmation): launch the released `game/StarDrive.exe`. Walk MainMenu → New Game → Universe → engage in combat → reach mid-game → save → reload → exit. Capture `phase4-runtime-smoke.log`. Repeat with Combined Arms loaded.
2. **Final build matrix**: 5 configs × x64 against the released source. Capture all 5 logs under `phase4-logs/wrap/`. Confirm 0 warnings, 0 errors on Release|x64 (warnings-as-errors gate from §4.3).
3. Author **`PHASE4_RESULTS.md`** in `x64Migration/`. Sections (mirroring PHASE3_RESULTS.md):
   - Sub-phase completion table with commit refs (§4.1 through §4.9).
   - Build matrix outcomes.
   - Success-gate verification (each item from Phase 4 Goals, ✅ / ❌).
   - Combined Arms + vanilla regression summary.
   - Performance summary table (vs Phase 2 baseline).
   - Migration retrospective: total commits across Phase 1+2+3+4, total LOC delta, what went well / what would have been done differently across the entire migration.
   - 1.60 release outcome (cross-reference §5.1 commit shas + itch.io page URL; for any post-release patches shipped, the per-patch GitHub Release URLs from patch-build.yml).
4. **ARCHITECTURE.md update**:
   - §8 "32-Bit Assumptions" — strike through (now resolved).
   - §9 "Migration Roadmap" — mark all sub-phases (1–4) DONE with commit refs.
   - §9 "Suggested Migration Order" — replace with a "Migration completed (2026-XX-XX)" marker pointing at PHASE4_RESULTS.md and the 1.60 itch.io page.
   - Update §6 "Native C++ Integration (SDNative)" if NanoMesh upstream PR landed (§4.8).
   - Update §7 "Third-Party Libraries" with Steamworks.NET (§4.9), FBX SDK 2020 (Phase 3 outcome).
5. **NanoMesh §4.8 follow-up** — close out whichever path the upstream PR took. This was deliberately deferred from Phase 4 because it depends on RedFox20's review cadence rather than our work cadence:
   - **Merge path** (PR accepted): in `SDNative/NanoMesh`, fast-forward `master` to upstream tip, check out `master`, run `git submodule update`-friendly verification (`git -C SDNative/NanoMesh log --oneline origin/master -3` should include the squashed commit). On the parent repo, `git add SDNative/NanoMesh` + commit the new submodule pointer ("submodule: bump NanoMesh to upstream tip post §4.8 merge"). Then drop the `gkapulis` remote (`git remote remove gkapulis`) and delete the `upstream-pr/fbx-skin-anim` branch (kept for review-iteration; no longer needed). Mark `project_nanomesh_local_branch.md` RESOLVED with the PR URL.
   - **Stall / reject path** (no merge by Phase 5 sign-off): tag `blackbox-migration` head as `blackboxplus-2026-05-07` on the fork (`gkapulis/NanoMesh`). Update `.gitmodules` URL in the parent repo to point at the fork (`https://github.com/gkapulis/NanoMesh.git`) so fresh clones succeed. Update `project_nanomesh_local_branch.md` to record the fork-pin decision and link the (still-open or rejected) PR for context.
6. **Memory file updates**:
   - `project_phase4_legacy_mesh_export_sync.md` → already RESOLVED (§4.7 close); confirm.
   - `project_nanomesh_local_branch.md` → mark RESOLVED with PR link or fork-tag note.
   - `project_phase3_3_youlose_desaturate_unresolved.md` → already RESOLVED (Phase 4.5.A); confirm.
   - `MEMORY.md` → update one-line hooks. Audit for any other entries that became stale during Phase 4/5.
   - Author new `project_phase4_zero_warnings_gate.md` capturing the warning-suppression patterns chosen (vendored vs first-party) so future contributors don't blanket-disable warnings.
   - Author new `project_phase5_release_signing.md` capturing the signing approach + cert provider + renewal cadence.
7. **Open Phase 5 close PR** and tag `phase5-end`. The Phase 5 PR closes the migration as a development effort.

**Verification**:
- All Phase 4 + Phase 5 success-gate items verified.
- PHASE4_RESULTS.md committed; ARCHITECTURE.md updated; memory files updated.
- Build matrix green; runtime smoke clean for both vanilla and Combined Arms (post-1.60 source).
- `phase5-end` tag exists; PR open or merged.

**Rollback**: N/A (sign-off step). If a regression is found post-merge, revert specific sub-phase commits — each is independently revertible by design.

**Risk**: Low. Documentation-only.

---

## Cross-cutting Concerns

### Branch hygiene
§5.1 commits to `migration/release-1.60` (or directly to `main` if the release flow is "merge Phase 4 → main first, then tag"). §5.2, when done, opens a follow-up PR against `main` carrying the wrap-up doc + ARCHITECTURE updates.

### What's NOT in Phase 5
- Auto-update mechanism redesign.
- Multi-platform builds (Linux, macOS).
- New gameplay features. Post-release feature work belongs in a separate plan series.
- Save-game compatibility with pre-migration XNA 3.1 saves (separate workstream if ever needed).

---

## Risk Summary

| Sub-phase | Risk | Mitigation |
|---|---|---|
| 5.0 Mars-line forward-compat patch | Low | Additive change to AutoUpdateChecker on the Mars maintenance line. Failure mode is the popup doesn't fire (= existing silent log-only behavior, no regression). One concern worth flagging: popup creation from a `Parallel.Run` task must go through `RunOnNextFrame` for thread-safe UI marshaling — pattern already used at AutoUpdateChecker line 152. Dev-side test (local AssemblyVersion bump + live 1.51 GitHub release) validates the wiring before the Mars patch ships. |
| 5.1 1.60 Release | Medium | Signing infra (Microsoft Trusted Signing identity verification has unpredictable lead time) is the largest unknown. Steam-folder install + UAC elevation are mechanical. §5.0 must be **published** before `jupiter-release-1.60` tags (cross-major discovery channel). No deliberate user-soak required — the §5.0 patch is the discovery channel whenever a 1.51 user lands on it via standard intra-major auto-update. Fallback for signing: ship unsigned 1.60 to the existing 1.51 audience, follow up with a signed 1.60.<build+1> patch when signing infra is ready. |
| 5.2 Migration close (optional) | Low | Documentation only. The release in §5.1 is what users see; this step is for future maintainers. |

**Migration close**: §5.1 ships `jupiter-release-1.60`. After that, ARCHITECTURE.md §9's "Suggested Migration Order" gets a "Migration completed" marker (in §5.2 if done, or directly when convenient otherwise), and all migration-related memory entries are settled. Future work falls under "post-migration" — gameplay features, mod support extensions, engine upgrades — and is out of scope for this plan series.

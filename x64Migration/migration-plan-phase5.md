# Phase 5 Migration Plan — Release

## Context

[Phase 4](migration-plan-phase4.md) closes the development side of the x64 + MonoGame migration: Combined Arms regression sweep, zero-warnings gate, performance budget, visual polish, mesh-export toolchain decision (§4.7 — DONE 2026-05-08), NanoMesh upstream PR, and Steam SDK x64. Phase 5 is purely about turning that work into a downloadable artefact.

Two sub-phases:

1. **§5.1 — Cut 1.60 release.** Signed installer + ZIP + Steam-folder install path. A GitHub Action under `.github/workflows/release.yml` (replicating the legacy AppVeyor steps — we don't have access to modify RedFox20's AppVeyor) builds the artefacts. Maintainer manually downloads the signed artefacts and uploads to itch.io for full releases. Patch tags trigger an additional `publish-patch` job that chunks the patch ZIP and uploads to the in-game updater path. Promoted from §4.11 in the prior plan so the release work has its own document and isn't gated on Phase 4 sign-off; UAC elevation, code signing, and the Steam-folder install path are user-facing and deserve their own PR cadence.
2. **§5.2 — Migration close (optional, post-release).** PHASE4_RESULTS.md + ARCHITECTURE.md update + memory cleanup. Promoted from §4.10 in the prior plan and reordered after the release because the release is what ships value to users; the wrap-up doc captures what already lives in commits/memory and is not a release blocker.

**Related memory**:
- [project_phase2_backlog_runtime.md](c:/Users/gkapu/.claude/projects/c--Development-stardrive-BlackBoxPlus/memory/project_phase2_backlog_runtime.md) — Steam SDK execution recipe (relevant when verifying §4.9 in §5.1.E smoke)
- [reference_migration_plan.md](c:/Users/gkapu/.claude/projects/c--Development-stardrive-BlackBoxPlus/memory/reference_migration_plan.md) — Plan + log file locations

---

## Phase 5 Goals (Success Gate)

1. **BlackBox Jupiter 1.60 published** to itch.io (replacing the 1.51 Mars listing) with signed installer, ZIP, and release notes. Tag `jupiter-release-1.60` pushed to git as the version marker that triggers the `release.yml` GitHub Action; the maintainer downloads the signed artefacts from the workflow run and manually uploads to itch.io. **Codename**: BlackBox Jupiter (was Mars through 1.51).
2. **No SmartScreen "Windows protected your PC" warning** when the installer is downloaded via a clean browser on a clean Windows install.
3. **`signtool verify /pa /v` reports valid Authenticode signatures** on `StarDrive.exe`, `SDNative.dll`, and the installer EXE — including a valid timestamp so signatures survive cert expiry.
4. **All four install scenarios pass** (see §5.1.E):
   - Clean machine, standalone install at the new default `C:\Games\StarDrivePlus64`.
   - Clean machine, Steam-folder install (replaces original StarDrive 1 in Steam library; original backed up).
   - Coexistence — 1.51 already installed at `C:\Games\StarDrivePlus`, 1.60 lands at `StarDrivePlus64` (Option A clean break: no upgrade-detection from `HKLM\Software\StarDrive\InstallPath`); both versions launch independently, each sees only its own SaveGameVersion.
   - Manual upgrade-in-place — user explicitly browses to `C:\Games\StarDrivePlus`; 1.51 binaries replaced; saves preserved (1.51 v20 saves stay on disk but invisible to 1.60).
5. **README + Sentry release record updated** to point at 1.60.
6. **Cross-major upgrade discovery channel live**: Mars-line patch shipping the new `MajorUpgradeAvailablePopup` deployed to existing 1.51 users at least 1–2 weeks before the `jupiter-release-1.60` tag. Same code forward-ported to the migration branch so 1.60 inherits the behavior for any future major bump (§5.1.G).
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
| **Steam-folder install** | Opt-in via radio-button page, NOT default. UAC elevation requested via `RequestExecutionLevel admin` in the NSIS script. | Surprise-replacing the user's Steam install of StarDrive 1 is hostile; the user must consciously pick that install target. UAC elevation is required for any write into `Program Files (x86)`. |
| **Steam-folder backup** | Original `StarDrive.exe` + `Content/` backed up to `<INSTDIR>\Original_StarDrive_Backup\` before BlackBox 1.60 files land. | "Verify Integrity" in Steam may re-fetch original files; the backup gives the user a clean rollback path either way. |
| **CI pipeline (build)** | **GitHub Actions** under `.github/workflows/release.yml`, **replicating the legacy AppVeyor build steps**. The legacy AppVeyor at `ci.appveyor.com/project/RedFox20/stardrive` is alive (we can read its public build log to see what steps it runs) but **not modifiable** — owned by RedFox20, no maintainer access — so we can't add signing or change targets there. We mirror its observable steps in our own workflow. | The fork can't drive its own builds through someone else's AppVeyor project; replicating in GitHub Actions gives us write access to our own pipeline (signing keys, secrets, configurable targets) while preserving build-step continuity with what the legacy pipeline does. |
| **Distribution channel (full release)** | **itch.io** (primary), replacing the 1.51 GitHub Releases listing. **Uploaded manually** by the maintainer through the project's Edit Game → Uploads page after downloading the GitHub Action's signed artefacts. | Major releases happen rarely (~once per multi-year cycle); the manual upload step is acceptable at that cadence and avoids carrying a butler-upload integration we'd touch only every few years. |
| **Post-release patch automation** | **GitHub Actions** — patch tags trigger an additional `publish-patch` job in the same `release.yml` workflow that builds the patch artefact, splits the cumulative patch ZIP into 25 MB chunks, and uploads to the in-game updater's distribution path. | Patches ship more often than majors, so the post-build steps (chunking + upload) are worth automating. Same workflow, conditional job — keeps the build steps shared between full-release and patch flows. **Verification per patch: confirm the Action ran green before announcing the patch.** |
| **Release artefact set** | NSIS installer (primary), full ZIP (single file), patch ZIP (cumulative, **split into 25 MB chunks** for the in-game updater's resumable downloads), Wix MSI (optional, kept around for parity). | NSIS is what most users download; full ZIP is for users who don't trust installers; patch ZIP chunking is the only place the 25 MB split survives — full release ZIPs go up as one file (itch.io has no 25 MB cap). |
| **Migration close shape (§5.2)** | Optional — PHASE4_RESULTS.md captures the dev-phase narrative if/when authored, but the release is what users see. | The Phase 1/2/3 RESULTS docs were authored before each release, but Phase 5's audience is the release page; the wrap-up doc is for future maintainers and can be written when bandwidth allows. |

---

## Sub-phase Index

| # | Title | Risk |
|---|---|---|
| 5.1 | Cut 1.60 release: signed installer + ZIP + Steam-folder install path | Medium |
| 5.2 | Migration close (optional, post-release): PHASE4_RESULTS.md + ARCHITECTURE.md update + memory cleanup | Low |

Each sub-phase ends with a commit and is rollback-able. §5.1 ships the artefact; §5.2 documents the closed migration.

---

## 5.1 — Cut 1.60 Release: Signed Installer + ZIP + Steam-folder Install Path

**Goal**: Ship the first post-migration public release as **BlackBox 1.60**. Three new capabilities relative to the 1.51 release machinery: (a) signed binaries and installer so Windows Defender SmartScreen doesn't flag the download as a potential virus, (b) a Steam-folder install option that replaces the original StarDrive1 install when the user has it on Steam, (c) UAC elevation handling so writes into `Program Files (x86)\Steam\steamapps\...` actually succeed.

**Context — what the 1.51 release looked like** (from `Deploy/`, `README.md`, GitHub releases page) and what changes for 1.60:
- Version string lives in `Properties/AssemblyInfo.cs::AssemblyVersion`. Current value: `1.51.15100`. Pattern: `MAJOR.MINOR.BUILD` (mod version + monotonic build counter from AppVeyor's `APPVEYOR_BUILD_VERSION`).
- Three installer artefacts produced by `Deploy/MakeInstaller.py`:
  - **NSIS** (`BlackBox-Jupiter.nsi` full / `BlackBox-Jupiter-Patch.nsi` cumulative patch) → `Deploy/upload/BlackBox_Jupiter_<version>.exe`. The Mars-line `.nsi` filenames are renamed at §5.1.A (codename change for the post-migration release line — see [project_jupiter_codename.md](../../../Users/gkapu/.claude/projects/c--Development-stardrive-BlackBoxPlus/memory/project_jupiter_codename.md)).
  - **ZIP** — for 1.60, the **full release ZIP is a single file** (itch.io has no 25 MB upload cap). The 25 MB chunking survives only on the **cumulative patch ZIP** (`BlackBox-Jupiter-Patch.nsi` flow), where the in-game patch updater benefits from chunked, resumable downloads on slow connections.
  - **MSI** (Wix, `Deploy/SDInstaller.wixproj` + `Deploy/Product.wxs`) — kept around but not the primary distribution channel
- Default install path: `C:\Games\StarDrivePlus` for the Mars line (NSIS line 76 in `Deploy/BBInstaller.nsi`). **For the Jupiter line, default changes to `C:\Games\StarDrivePlus64`** — the `64` suffix signals the bitness change so users with both versions on disk can tell them apart, and Mars-line registry-driven upgrade-in-place behavior at `BBInstaller.nsi:66-68` is removed (clean-break Option A; see §5.1.A and [project_jupiter_install_path.md](../../../Users/gkapu/.claude/projects/c--Development-stardrive-BlackBoxPlus/memory/project_jupiter_install_path.md)). Steam-detection code is commented out at lines 70–74 — the previous team had it in mind but disabled it, almost certainly because the installer doesn't request UAC elevation today.
- Distribution: 1.51 lives on GitHub Releases at `https://github.com/TeamStarDrive/StarDrive/releases/tag/mars-release-1.51`. **For 1.60 we move primary distribution to itch.io**. The full-release upload is **manual** (Edit Game → Uploads page) — major releases happen rarely (~once per multi-year cycle), so the manual step is fine. `notify-sentry-of-release.bash` continues to post a Sentry release record. The git tag `jupiter-release-1.60` stays as the version marker.
- Auto-update: in-game logic checks for newer patch versions on launch and prompts to install; works for cumulative patches on top of a major release. The patch ZIP feeding this flow is the chunked artefact.
- CI (build): for 1.60 we move to **GitHub Actions** under `.github/workflows/release.yml`, replicating the legacy AppVeyor build steps. The legacy AppVeyor at `ci.appveyor.com/project/RedFox20/stardrive` is *alive* (`README.md` shows the badge; the project still runs builds and we can read its public log to see what it does) but **not modifiable** — owned by RedFox20, no maintainer write access. We can't add Trusted Signing keys, change targets, or wire post-build hooks there, so we replicate observable steps in our own workflow. The current repo has no `appveyor.yml` checked in to mirror, so the replication source is the AppVeyor build log itself.
- CI (post-release patches): the same `release.yml` workflow has a conditional `publish-patch` job that runs *after* the build job when the tag matches `jupiter-release-*-patch`. The job chunks the patch ZIP into 25 MB parts, uploads to the in-game updater's distribution path, and posts Sentry. The full-release path stops at the build job (manual itch.io upload follows).

### Sub-steps

**§5.1.A — Version bump + codename + install-path + save-version + release notes**

The post-migration release line is **BlackBox Jupiter** (was Mars through 1.51) — Jupiter is the largest planet, fitting the scale of the x64 + MonoGame migration. The default install dir changes to `C:\Games\StarDrivePlus64`, and `SavedGame.SaveGameVersion` bumps so 1.51 and 1.60 saves coexist by mutual invisibility (no corruption — each version filters the other's saves out of the load list).

1. Bump `Properties/AssemblyInfo.cs::AssemblyVersion` from `1.51.15100` to `1.60.<build>`. The build counter convention (`15100`-style) is set by AppVeyor; pick the first build number for the post-migration cycle (e.g., `1.60.16000` to leave a clear gap from the 1.51 line).

2. **Codename rename Mars → Jupiter.** Rename and edit:
   - [Deploy/BlackBox-Mars.nsi](../Deploy/BlackBox-Mars.nsi) → `Deploy/BlackBox-Jupiter.nsi` (full installer NSIS script)
   - [Deploy/BlackBox-Mars-Patch.nsi](../Deploy/BlackBox-Mars-Patch.nsi) → `Deploy/BlackBox-Jupiter-Patch.nsi` (cumulative patch NSIS)
   - [Deploy/MakeInstaller.py:41-42](../Deploy/MakeInstaller.py#L41-L42) — `BlackBox-Mars.nsi` / `BlackBox-Mars-Patch.nsi` paths
   - [Deploy/MakeInstaller.py:54](../Deploy/MakeInstaller.py#L54) — `BlackBox_Mars_{BUILD_VERSION}.zip` archive filename → `BlackBox_Jupiter_{BUILD_VERSION}.zip`
   - [Deploy/Product.wxs](../Deploy/Product.wxs) + [Deploy/SDInstaller.wixproj](../Deploy/SDInstaller.wixproj) — Wix MSI metadata: product/upgrade names, output filename
   - [Ship_Game/GlobalStats.cs:313-314](../Ship_Game/GlobalStats.cs#L313-L314) — `ExtendedVersion = $"Mars : {Version}"` and `ExtendedVersionNoHash = $"Mars : {...}"` → `Jupiter`. This string surfaces in game logs ([Log.cs:139](../Ship_Game/Utils/Log.cs#L139)), Sentry/breadcrumb context, and `ResourceManager.LoadCommon` log lines ([ResourceManager.cs:246-248](../Ship_Game/Data/ResourceManager.cs#L246-L248)) — the codename is what shows up in every diagnostic.
   - [Ship_Game/GlobalStats.cs:44-45](../Ship_Game/GlobalStats.cs#L44-L45) — refresh the example doc-comments (`"Mars : 1.20.12000 develop/f83ab4a"`) to use a Jupiter example.
   - `README.md` — release-name + badge label references

3. **Default install path → `C:\Games\StarDrivePlus64`** (clean-break, Option A). Edit [Deploy/BBInstaller.nsi:64-78](../Deploy/BBInstaller.nsi#L64-L78) `.onInit`:
   - Remove the `ReadRegStr $PREVDIR HKLM ${REGPATH} InstallPath` upgrade-detection path (Mars-line ergonomics; ignored for Jupiter).
   - Set `$INSTDIR` directly to `C:\Games\StarDrivePlus64`.
   - Update `${REGPATH}` to `Software\StarDrivePlus64` (or equivalent new key) so writes don't clobber Mars's `Software\StarDrive` keys.
   - Mirror the Wix HKCU registry path in [Deploy/Product.wxs:68,78](../Deploy/Product.wxs#L68): `Software\StarDrivePlus` → `Software\StarDrivePlus64`.
   - Mirror the Wix `INSTALLFOLDER` Name in [Deploy/Product.wxs:43](../Deploy/Product.wxs#L43): `StarDrivePlus` → `StarDrivePlus64`.

   Users who explicitly want to upgrade-in-place can manually browse to `C:\Games\StarDrivePlus` from the installer's path-picker page; the radio-button default just doesn't pre-select that.

4. **Bump `SaveGameVersion`** to partition saves cleanly. Edit [Ship_Game/SavedGame.cs:27](../Ship_Game/SavedGame.cs#L27): `public const int SaveGameVersion = 20` → `21`. Effect: the exact-match filter at [LoadSaveScreen.cs:56](../Ship_Game/GameScreens/LoadSaveItems/LoadSaveScreen.cs#L56) means 1.51 sees only Version-20 saves, 1.60 sees only Version-21 saves. Save folder stays single (`%APPDATA%\StarDrive\`) — no `Dir.StarDriveAppData` change. No save corruption; both versions silently filter the other's saves out of the load list.

5. Update README.md "Current Major Release Link" to point at the **itch.io page** for 1.60 (replacing the 1.51 GitHub Releases link). Replace the "BlackBox - Hyperion" future-goals list (the migration is now done) with a "BlackBox Jupiter 1.60 — 64-bit + MonoGame" achievements list.

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

**§5.1.B — Code signing**

The blocker today: an unsigned EXE downloaded from the internet triggers SmartScreen "Windows protected your PC" dialog, which 9 out of 10 users dismiss as malware. We need an authenticode signature on `StarDrive.exe`, `SDNative.dll`, and the installer EXE itself.

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

**§5.1.C — Steam-folder install path**

Steam typically installs StarDrive 1 to `C:\Program Files (x86)\Steam\steamapps\common\StarDrive\`. Writing there requires UAC elevation (the current installer doesn't request it, which is why the Steam-detection code in `Deploy/BBInstaller.nsi` lines 70–74 is commented out).

Steps:
1. Add UAC manifest to the NSIS installer:
   ```nsis
   RequestExecutionLevel admin
   ```
   This makes the installer prompt for elevation on launch. Without it, writes to `Program Files (x86)` silently fail or get redirected to `%LOCALAPPDATA%\VirtualStore`.
2. Uncomment and finalize the `CheckSteam` block in `Deploy/BBInstaller.nsi`:
   ```nsis
   ReadRegStr $STEAMDIR HKLM "SOFTWARE\WOW6432Node\Valve\Steam" InstallPath
   StrCmp $STEAMDIR "" SetDefaultPath 0
   StrCpy $INSTDIR "$STEAMDIR\SteamApps\common\StarDrive"
   ```
3. **Make Steam install opt-in**, not default — present a radio-button page with two choices:
   - **Replace original StarDrive 1 in Steam folder** (only available if Steam install detected — but NOT pre-selected; Jupiter clean-break Option A means standalone is the default).
   - **Install to standalone folder** (default `C:\Games\StarDrivePlus64`, pre-selected). The path-picker still lets the user browse to `C:\Games\StarDrivePlus` if they explicitly want to upgrade an existing 1.51 install in-place.
4. When the Steam path is chosen and an existing StarDrive 1 install is present:
   - Back up the original `StarDrive.exe` + `Content/` to `<INSTDIR>\Original_StarDrive_Backup\` so the user can restore later.
   - Show a confirmation dialog: "This will replace your original StarDrive 1 with BlackBox 1.60. The original files will be backed up to Original_StarDrive_Backup/. Continue?"
   - Verify Steam isn't running; abort with a clear message if it is (Steam files lock under steamapps/common).
5. After install completes, leave the Steam manifest alone — Steam's manifest still says "StarDrive 1.0", but the launcher binary is now BlackBox 1.60. Document this in the release notes (Steam will not auto-update over our install; user can right-click → Properties → Verify Integrity to roll back).

**§5.1.D — Build pipeline (GitHub Action, replicating AppVeyor) + tag + manual itch.io upload**

We can't use the existing AppVeyor (`ci.appveyor.com/project/RedFox20/stardrive`) because it's owned by an account we don't control. So we **read** its public build log to see what steps it runs, then **replicate** those steps in our own GitHub Action.

1. **Audit the legacy AppVeyor build.** Open `ci.appveyor.com/project/RedFox20/stardrive`, pick a recent green build, and capture from the log:
   - Pre-build environment setup (toolchain installs, NuGet restore commands, submodule init).
   - Build commands (msbuild target, configuration, platform).
   - Test invocation (if any tests run in CI).
   - Packaging command (the `Deploy/MakeInstaller.py` invocation + flags).
   - Artefact upload (where AppVeyor sends the resulting `.exe` / `.zip` — this tells us the in-game updater's distribution URL we'll need for §5.1.F).

   Save the captured step list under `phase5-logs/appveyor-mirror.md` for the workflow author to translate into YAML.

2. **Author `.github/workflows/release.yml`.** Triggers on tag push matching `jupiter-release-*`. Two jobs:
   - `build` (always runs):
     - Checkout with submodules.
     - Set up .NET 8 SDK + VS2022 build tools (the GitHub-hosted `windows-latest` runner has both).
     - Run the build commands captured in step 1, but on `Release|x64` (legacy AppVeyor likely builds x86; this is the 1.60 difference).
     - Run §5.1.B signing on `game/StarDrive.exe` + `game/SDNative.dll` (Trusted Signing credentials from GitHub secrets).
     - Run `Deploy/MakeInstaller.py` to produce NSIS full installer + full ZIP. **Full ZIP is a single file** — no 25 MB split.
     - Sign the installer EXE last.
     - Run `Deploy/SignedBinaryCheck.ps1` (see Tests added) — fail the workflow if any binary is unsigned or has an expired timestamp.
     - Emit the signed artefacts as **workflow artefacts** (downloadable from the run's summary page) so the maintainer can grab them for the manual itch.io upload.
   - `publish-patch` (conditional — see §5.1.F).

3. Tag `jupiter-release-1.60` on the merged Phase 4 branch and push it. The workflow's `build` job runs. **Prerequisite**: §5.1.G's Mars-line forward-compat patch must be shipped and live in users' hands before this tag pushes (allow a 1–2 week soak so existing 1.51 users auto-update onto the patched build); otherwise existing 1.51 users get no in-game discovery channel for Jupiter.

4. **Verify the GitHub Action ran successfully**: open the Actions tab, confirm the `release.yml` run for tag `jupiter-release-1.60` reports `build` job green. Specifically check:
   - All build + sign + `SignedBinaryCheck.ps1` steps green.
   - Workflow artefacts uploaded (signed installer, full ZIP, optional MSI). Download size matches expectation.
   - If `build` failed, do not proceed — diagnose the workflow first.

5. **Download the signed artefacts** from the workflow run's Artifacts section.

6. **Manually upload** to itch.io via the project's Edit Game → Uploads page. Set the user-version on each upload to `1.60.<build>`. Major releases happen rarely — once per multi-year cycle — so the manual step is acceptable here and avoids carrying a butler integration we'd touch only every few years.

7. Run `Deploy/notify-sentry-of-release.bash` with `APPVEYOR_BUILD_VERSION=1.60.<build>` (manual after the upload; env var name preserved for compat with the existing script).

8. Update README.md "Current Major Release Link" to point at the itch.io page (replacing the 1.51 GitHub Releases link). Replace the AppVeyor build badge with the GitHub Actions one (or remove if the legacy badge is misleading now).

9. *(Optional, secondary mirror)* If we keep a GitHub Release page in addition to itch.io: publish under tag `jupiter-release-1.60`, body = `RELEASE_NOTES_1.60.md` content. Decide once based on whether external downloaders (e.g., third-party mod listings) link at GitHub.

**§5.1.E — Smoke test on four install scenarios**
1. **Clean machine, standalone install (default path)**: download installer via Edge or Chrome, run it, confirm no SmartScreen warning, accept the default `C:\Games\StarDrivePlus64`, complete install, launch game.
2. **Clean machine, Steam install**: same as scenario 1 but pick the Steam-folder option. Confirm Steam still launches StarDrive (now showing BlackBox Jupiter 1.60). Confirm achievements/stats round-trip via §4.9.
3. **Coexistence — 1.51 already installed at `C:\Games\StarDrivePlus`**: run the 1.60 installer, accept the default `C:\Games\StarDrivePlus64`. Verify (a) installer does NOT default to the 1.51 path (Option A clean break — no `HKLM\Software\StarDrive\InstallPath` detection); (b) installer writes `HKLM\Software\StarDrivePlus64\InstallPath`, leaving `Software\StarDrive` untouched; (c) post-install, both `StarDrive.exe` (1.51) and `StarDrivePlus64\StarDrive.exe` (1.60) launch independently; (d) saves at `%APPDATA%\StarDrive\` are visible to both, but each version's load list shows only its own SaveGameVersion (1.51 sees v20, 1.60 sees v21).
4. **Manual upgrade-in-place** (user explicitly chooses old path): on a 1.51 machine, run the 1.60 installer, browse the path-picker to `C:\Games\StarDrivePlus`, complete install. Confirm 1.51 binaries are replaced in place; 1.51 desktop shortcut now launches 1.60. Saves preserved (load list shows only v21 saves; v20 saves still on disk but invisible until user reinstalls 1.51).

**§5.1.F — Post-release patch automation (`publish-patch` job in the same workflow)**

Patches (1.60.x bumps) ship more often than majors, so the post-build steps that are tedious to do by hand — uploading the chunks to wherever the in-game updater fetches them, refreshing the patch manifest, posting Sentry — are automated. This is a conditional job in the **same `release.yml` workflow** as §5.1.D (not a separate file): the build steps are shared between full-release and patch flows, and only the post-build packaging differs.

**Closed-loop note**: chunking is **already implemented end-to-end**. [Deploy/MakeInstaller.py:60-80](../Deploy/MakeInstaller.py#L60-L80) splits the patch ZIP into raw 25 MB byte slices (`001-BlackBox_Mars_<ver>.zip`, `002-...`, ...) when the produced archive is over 25 MB; the in-game updater's [AutoPatcher.PostProcessMultipleZipChunks](../Ship_Game/GameScreens/MainMenu/AutoPatcher.cs#L136-L169) downloads each chunk per the manifest's `ZipUrls`, concatenates them client-side into `combined.zip`, and unzips. The workflow does **not** chunk in YAML — it just uploads what `MakeInstaller.py` already produced and refreshes the manifest.

1. Patch tag convention: `jupiter-release-1.60.<build>-patch`. The trailing `-patch` suffix is what the workflow uses to decide whether to run the patch path.

2. Add the `publish-patch` job to `.github/workflows/release.yml`:
   ```yaml
   publish-patch:
     needs: build
     if: endsWith(github.ref_name, '-patch')
     runs-on: windows-latest
     steps:
       - uses: actions/download-artifact@v4
         with: { name: patch-chunks, path: Deploy/upload }
       # MakeInstaller.py:60-80 already wrote 001-...zip / 002-...zip into Deploy/upload
       # when the patch ZIP exceeded 25 MB. AutoPatcher.cs reassembles client-side.
       - name: Upload chunks to in-game updater path
         run: # curl/azcopy/whatever the legacy host requires (URL+creds from the AppVeyor log audit in §5.1.D step 1).
              # Upload every Deploy/upload/*-BlackBox_Jupiter_*.zip in lexical order.
       - name: Update patch manifest
         run: # Refresh the JSON manifest the in-game updater reads on launch:
              # - bump version to ${{ github.ref_name }} (strip the trailing -patch).
              # - set ZipUrls = ordered list of the chunk URLs just uploaded (or single URL if MakeInstaller.py produced one file).
              # - publish manifest to the same host as the chunks.
       - name: Sentry release notify
         run: bash Deploy/notify-sentry-of-release.bash
         env: { APPVEYOR_BUILD_VERSION: ${{ github.ref_name }} }
   ```

3. The `build` job needs to **conditionally produce the cumulative patch ZIP** when the tag ends in `-patch` — invoke `py -3 Deploy/MakeInstaller.py --root_dir=. --patch --type=zip`. Output lands in `Deploy/upload/`: either a single `BlackBox_Mars_<ver>.zip` (if ≤25 MB) or `001-...zip`, `002-...zip`, ... (if larger; the original is deleted by [MakeInstaller.py:79](../Deploy/MakeInstaller.py#L79)). Capture the entire `Deploy/upload/` directory as a workflow artefact named `patch-chunks` so `publish-patch` can pick it up.

4. Credentials for the in-game updater upload (whatever the legacy AppVeyor used — likely an FTP/S3/HTTP-PUT endpoint) live as GitHub secrets. Recover the URL + auth pattern from the legacy AppVeyor build log in §5.1.D step 1. The patch manifest lives at the same host (the URL the in-game `AutoUpdateChecker` polls on launch); recover that endpoint from the same audit.

5. **Verification per patch (note to self when shipping a patch)**: after pushing a `jupiter-release-*-patch` tag, **check that the `release.yml` workflow ran green end-to-end**, specifically the `publish-patch` job. Open the Actions tab, find the run for the patch tag, confirm:
   - `build` job green (signing + `SignedBinaryCheck.ps1` + `patch-chunks` artefact uploaded; chunk count = `ceil(zip_size / 25 MB)` if chunked, else 1).
   - `publish-patch` job green:
     - Download step pulled all chunks from the artefact into `Deploy/upload/`.
     - Upload step landed every file at the in-game updater path (HTTP 200 for each).
     - Manifest update step published the refreshed JSON with `ZipUrls` listing chunks in order.
     - Sentry release record posted.
   - **End-to-end smoke** (one-off, not every patch): on a 1.60 install, force the updater to re-check (or wait for the launch poll), confirm AutoPatcher downloads all chunks, `PostProcessMultipleZipChunks` reassembles, and the patch applies without error.
   - If any step failed, the patch chunks aren't reachable to in-game updaters; do **not** announce the patch until the workflow is fixed and re-run.

**§5.1.G — Mars-line forward-compat patch (cross-major upgrade discovery)**

**Goal**: ship a final 1.51.x Mars patch *before* `jupiter-release-1.60` goes live that gives existing 1.51 users an in-game discovery channel for Jupiter. Same code is forward-ported into the migration branch so 1.60 inherits the behavior for any future major bump.

**Background — why a separate popup is needed**: [AutoUpdateChecker.IsSameMajorVer](../Ship_Game/GameScreens/MainMenu/AutoUpdateChecker.cs#L220-L235) compares `version.Split('.').Take(2)` between current and latest GitHub release. On mismatch (e.g., `1.51` vs `1.60`), [IsLatestVerNewer](../Ship_Game/GameScreens/MainMenu/AutoUpdateChecker.cs#L207-L218) returns `false` and the standard new-version popup is silently suppressed — log line `AutoUpdater: Major version mismatch ... Will not update` is the only trace. This is intentional: the in-game patch flow at [AutoPatcher.cs](../Ship_Game/GameScreens/MainMenu/AutoPatcher.cs) drops files into `Directory.GetCurrentDirectory()` and can't safely cross install-dir / SaveGameVersion / dependency-graph boundaries that a major bump introduces. So a 1.51 user has zero in-game signal that 1.60 exists. §5.1.G adds a *second* popup that fires on major-mismatch instead of just logging — distinct from the standard NewVersionPopup, positioned top-left so it doesn't collide with intra-major patch popups in the bottom-right `Popups` UIList stack.

**Mod path** (verified): mods bypass the major-version check entirely — [line 212](../Ship_Game/GameScreens/MainMenu/AutoUpdateChecker.cs#L212) gates `IsSameMajorVer` on `!isMod`. Mod patches are files-into-folder operations with no install-dir / save-format invariants, so the in-game patcher can already cross "major" mod versions without the suppression. **No mod-path change in §5.1.G** — the new popup is vanilla-only, matching the existing asymmetry.

**Sub-steps**:

1. **Author `MajorUpgradeAvailablePopup`** alongside the existing [NewVersionPopup](../Ship_Game/GameScreens/MainMenu/AutoUpdateChecker.cs#L56-L146). Different visual: positioned at the **top-left of the screen** (not via the bottom-right `Popups` UIList at `Screen.Height * 0.6f`), so it doesn't fight for stack slots with intra-major patch notifications. Click → opens URL via `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })` and `Application.Exit()` (game closes so the user can run the new installer cleanly).

2. **URL config via on-disk txt file**. New file `game/upgrade-url.txt` containing the destination URL on a single line (default content: the Jupiter itch.io page URL). Read at popup-creation time; first nonblank line wins. Fallback to a hardcoded constant in code if the file is missing/empty/IO-error. **Why on-disk**: lets us redirect users (itch.io URL changes, or we move the project to another store) without shipping another C# binary patch — push a txt-file-only patch through the existing AutoPatcher flow. Ship `upgrade-url.txt` in the Mars patch's `Release.txt` manifest *and* in the Jupiter `game/` baseline.

3. **Wire into AutoUpdateChecker**. Refactor [IsLatestVerNewer / IsSameMajorVer](../Ship_Game/GameScreens/MainMenu/AutoUpdateChecker.cs#L207-L235) so that on major-mismatch, the parallel-task path can `RunOnNextFrame` a `MajorUpgradeAvailablePopup` (the existing pattern at [line 152](../Ship_Game/GameScreens/MainMenu/AutoUpdateChecker.cs#L152) for cross-thread popup creation). Existing log line stays. The popup-creation site replaces the current bare `return false` in `IsSameMajorVer`.

4. **Ship the Mars patch**.
   - Branch from the last 1.51 commit on whichever branch the Mars line was last released from (verify before starting; likely a tag like `mars-release-1.51` or the predecessor of `migration/monogame_migration`).
   - Bump `AssemblyVersion` `1.51.15100` → `1.51.<next-build>`; tag `mars-release-1.51.<next-build>-patch`.
   - Use whichever pipeline the Mars line ran on (legacy AppVeyor, if still operational; or mirror through the new GitHub Action).
   - Verify on a clean 1.51 install: in-game AutoUpdateChecker prompts (existing intra-major flow) → patch downloads + applies → restart → new code is live → next launch's GitHub poll triggers `MajorUpgradeAvailablePopup` against a staging "1.60" release.

5. **Forward-port to `migration/monogame_migration`**. Cherry-pick the `MajorUpgradeAvailablePopup` class + AutoUpdateChecker wiring + `upgrade-url.txt` + manifest entry. Verify compile + smoke. The forward-port means Jupiter ships with the same major-mismatch popup from day one, and any 1.7+ release in the future automatically gives 1.60 users the same in-game discovery — the popup behavior is now permanent, not a Mars-line patch-only feature.

6. **Sequencing**. §5.1.G's 1.51 patch must be **shipped and live in users' hands** before the `jupiter-release-1.60` tag pushes (§5.1.D step 3). Otherwise existing 1.51 users get no in-game discovery channel for Jupiter — they'd have to find out through external channels alone (itch.io listing, README link). Allow time for users to auto-update onto the patched 1.51 build before tagging Jupiter; suggest a 1–2 week soak.

**Tests added**:
- Smoke (manual): on 1.51.<patched>, point AutoUpdateChecker at a staging GitHub release tagged `jupiter-release-1.60.16000`, verify `MajorUpgradeAvailablePopup` fires top-left, click opens browser at `upgrade-url.txt` URL, game exits.
- Edge: missing `upgrade-url.txt` → popup fires, click opens hardcoded fallback URL.
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
- Migration-branch forward-port can be reverted via `git revert` of the cherry-picked commit if a regression surfaces during §5.1.D pre-tag smoke.

**Risk**: Low. The change is additive: failure mode is the popup doesn't fire (= existing silent log-only behavior, no regression). One concern worth flagging: AutoUpdateChecker runs the version-check on `Parallel.Run`; popup creation must use `RunOnNextFrame` for thread-safe UI marshaling (the pattern is already used at line 152). Audit the wiring in step 3 to confirm marshaling discipline before shipping the Mars patch.

### Tests added
- `Deploy/SignedBinaryCheck.ps1` *(`release.yml` build-job step)* — runs `signtool.exe verify /pa /v` against `StarDrive.exe`, `SDNative.dll`, and the installer EXE. Fails the workflow if any binary is unsigned or has an expired timestamp.

### Verification
- All three smoke scenarios pass with no SmartScreen warning.
- `signtool verify` reports valid Authenticode signatures on the three target binaries (verified in `release.yml` via `SignedBinaryCheck.ps1` and re-verified locally on the artefacts pulled from itch.io).
- The `release.yml` workflow run for tag `jupiter-release-1.60` is green (build job); signed artefacts manually downloaded from the workflow run and uploaded to itch.io; itch.io project page shows BlackBox 1.60 as the latest build.
- README updated to point at itch.io; Sentry release record posted.
- 1.51 → 1.60 in-place upgrade preserves saves.
- *(Patch path — verified per patch, not for the initial 1.60)* `release.yml` workflow's `publish-patch` job ran green after the `build` job; chunks reachable at the in-game updater URL.

### Rollback
- On itch.io: archive (or hide) the 1.60 build via the project's Edit Game → Uploads page; users see the previous build until a fixed version replaces it.
- Revert version bump, README change, and `release.yml` workflow commits via `git revert`. Delete the `jupiter-release-1.60` tag locally + on origin if the workflow needs to be re-run for a fixed build (`git push origin :refs/tags/jupiter-release-1.60`). The unsigned 1.51 binaries are unaffected — users on 1.51 stay on 1.51 until they choose to upgrade.
- For a bad patch: roll back the in-game updater's distribution path to the previous patch's chunks (the `publish-patch` job overwrites by version, so keep the prior version's chunks live during rollout).

### Risk
**Medium.** Two unknowns:
1. **Signing infrastructure** — Trusted Signing setup involves Microsoft identity verification with unpredictable timing (1–14 days). If signing isn't ready by §5.1 entry, ship 1.60 unsigned (acceptable for the existing 1.51 audience who already trust the source) and follow up with a 1.60.<build+1> signed patch.
2. **Replicating AppVeyor's build steps** — we read the legacy log but never owned the config; subtle steps (env vars, NuGet restore order, post-build hooks) may need iteration before the GitHub Action produces an artefact byte-equivalent to AppVeyor's. Mitigate by running the new workflow on a **non-tag pre-release branch first** to shake out failures before the `jupiter-release-1.60` tag pushes.

Steam-folder install is straightforward but the UAC elevation change introduces a UX shift — old users running the installer without admin rights now hit an elevation prompt; document this in release notes.

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
   - 1.60 release outcome (cross-reference §5.1 commit + itch.io page URL + `release.yml` workflow run URL; for any post-release patches shipped, the per-patch `publish-patch` job run URLs).
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
| 5.1 1.60 Release | Medium | Signing infra (Microsoft Trusted Signing identity verification has unpredictable lead time) is the largest unknown. Steam-folder install + UAC elevation are mechanical. §5.1.G adds a Mars-line forward-compat patch dependency: the 1.51 patch must be live before `jupiter-release-1.60` tags, plus a 1–2 week soak window so 1.51 users auto-update onto the patched build. Fallback: ship unsigned 1.60 to the existing 1.51 audience, follow up with a signed 1.60.<build+1> patch when signing infra is ready. |
| 5.2 Migration close (optional) | Low | Documentation only. The release in §5.1 is what users see; this step is for future maintainers. |

**Migration close**: §5.1 ships `jupiter-release-1.60`. After that, ARCHITECTURE.md §9's "Suggested Migration Order" gets a "Migration completed" marker (in §5.2 if done, or directly when convenient otherwise), and all migration-related memory entries are settled. Future work falls under "post-migration" — gameplay features, mod support extensions, engine upgrades — and is out of scope for this plan series.

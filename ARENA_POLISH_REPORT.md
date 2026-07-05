# Arena Polish Report

## Screens Finished

- Betting payoff: `ArenaBetting` now writes a capped `[StarData]` `SettledBets` history on fight-option resolution and immediate contender-duel bets. `ArenaBettingScreen` shows the latest payoff, open slip, and settled history rows with stake, odds, payout/loss, matchup, winner, and cash delta. `ArenaHubScreen` also shows a compact last-bet banner.
- Memorial wall: `ArenaMemorialScreen` now lists `career.Memorials` newest-first with kind, ship/captain name, design, level, kills, killer/cause tooltip, and round/fame context. `ArenaMemorialRecord` was extended additively with `[StarData] RoundAtDeath` and `FameAtDeath`; the arena permadeath path writes current round/fame.
- Pilot dossier: `ArenaPilotSoulScreen` now shows career level, battles, W/L, fleet size, captains, current fielded vessels, and perks. Battles/W/L are derived from chronicle win/loss rows with `CareerLevel` as the old-save win fallback.
- League season modal: hub `CLIMB` now opens `ArenaLeagueSeasonScreen`, runs the existing `RunLeagueSeasonAndApplyAsync` path with a small deterministic 3v3 sampled season, saves the applied career, and renders standings, recent match results, permadeath note, and next matchup. The existing `LADDER` utility still opens the contender challenge list.
- Garage labels: `ArenaGarageScreen` rows now tag owned vessels as `[VETERAN]` or `[LEGENDARY]` from level/kill thresholds, plus `[HULL n%]` and `[SCAR n]` damage indicators.

## Data Sources

- Betting: `ArenaCareer.PendingBet`, new additive `ArenaCareer.SettledBets`, `ArenaBetSlip`, `ArenaBetQuote`.
- Memorials: `ArenaCareer.Memorials`, `OwnedVessel` combat state, permadeath write path in `RemoveDestroyedOwnedVessels`.
- Dossier: `ArenaCareer.Captains`, `OwnedVessels`, `FieldedFleetVessels`, `Perks`, `Chronicle`.
- League: `ArenaBigLeagueReport`, `ArenaBigLeagueStanding`, `ArenaBigLeagueMatchResult`, `career.Teams`.
- Garage: `OwnedVessel.Level`, `Kills`, `CurrentHullHealth`, `MaxHullHealth`, `DestroyedModules`.

## Coverage / Verification

- Added/extended headless tests in `ArenaRenderSmokeTests`:
  - `ArenaBettingWagers_Headless` now asserts settled history for fight-option wins/losses and contender-duel bets, including history capping.
  - `ArenaPolishPayoffScreensPopulate_Headless` constructs/populates BET, memorial, pilot dossier, league season, and garage screens from a persisted career fixture.
  - `ArenaChronicleMemorialLedger_Headless` now asserts memorial round/fame persistence.
- Compile checks passed with `-p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false`:
  - `dotnet build StarDrive.csproj --no-restore -v q -nologo ...`
  - `dotnet build StarDriveArena.csproj --no-restore -v q -nologo ...`
  - `dotnet build UnitTests\SDUnitTests.csproj --no-restore -v q -nologo ...`
- Full normal build/test execution is blocked in this sandbox by output/runtime environment issues: `game/*.deps.json` / `game/*.runtimeconfig.json` writes are denied, NuGet vulnerability metadata is offline, and VSTest aborts before test execution because `System.Configuration.ConfigurationManager` is missing from the compile-only runtime output.

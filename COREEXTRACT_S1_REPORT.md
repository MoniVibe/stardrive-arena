# COREEXTRACT S1 Report — additive partial-class extraction

Branch `fix/coreextract-s1`, worktree `C:\dev\stardrive\StarDrive-coreextract`.
Baseline for all diff numbers: upstream `9ac48f1cb` (`git diff --ignore-all-space --numstat 9ac48f1cb -- <file>`, added/deleted).
Build gate after every type: `dotnet build StarDrive.csproj -c Debug -p:Platform=x64` — green (0 warnings, 0 errors) after each step.
Final: `dotnet build UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 -p:GenerateDependencyFile=false -p:GenerateRuntimeConfigurationFiles=false` — green.
NOT committed (per brief; orchestrator verifies + commits). Full test suites NOT run here (orchestrator's gate).

## Summary table (diff vs upstream, added/deleted, before → after)

| Original file | Before | After | Lines relocated |
|---|---|---|---|
| Ship_Game/Ships/Ship.cs | 142/0 | 7/0 | ~135 |
| Ship_Game/Ships/Ship_Update.cs | 32/4 | 9/4 | ~23 |
| Ship_Game/Empire.cs | 175/15 | 64/15 | ~111 |
| Ship_Game/Empire_Relationship.cs | 17/2 | 7/2 | 10 |
| Ship_Game/Gameplay/Relationship.cs | 43/0 | 7/0 | 36 |
| Ship_Game/Universe/SolarBodies/SBProduction.cs | 52/3 | 27/4 | ~26 |
| Ship_Game/Commands/Goals/DeepSpaceBuildGoal.cs | 11/0 | 6/1 | 6 |
| Ship_Game/Universe/SolarBodies/SolarSystemBody.cs | 46/3 | 23/4 | ~24 |
| Ship_Game/Universe/SolarBodies/Planet/Planet.cs | 32/1 | 9/1 | ~23 |
| Ship_Game/Universe/UniverseScreen/UniverseObjectManager.cs | 66/4 | 15/5 | ~52 |

Net: ~446 fork-added lines moved out of upstream-owned files into 8 new `*.Authoritative.cs` partials. All new files registered in `StarDrive.csproj` (explicit `<Compile Include>` list; Arena + UnitTests projects consume via ProjectReference, no extra registration needed).

## New partial files

1. **Ship_Game/Ships/Ship.Authoritative.cs** (`public partial class Ship`)
   - From Ship.cs: PassiveAuthoritative* const/field block (10 members), `MarkAsTransientEnvironment`, `SetAuthoritativeTransform`, `ObservePassiveAuthoritativeTransform`, `SignedRotationDelta`, `ResetPassiveAuthoritativeVisualBank`, `AdvancePassiveAuthoritativeVisualBank`, `PassiveAuthoritativeRenderYRotation/TacticalIconRotation/TacticalIconWidthScale` properties, 5 `*ForTest` internal properties.
   - From Ship_Update.cs: `SyncSceneObjectForPassiveAuthoritativeView`.
2. **Ship_Game/Empire.Authoritative.cs** (`public sealed partial class Empire`)
   - From Empire.cs: `IsHumanControlled`, `IsAIControlled`, `EnableAISidekick`, `UseDeterministicRandom`, `SeedPersonalityRandom`, `RebuildUnlockCachesForAuthoritativeSync`, `SetAuthoritativeAutomationState` (all verbatim incl. comments).
   - From Empire_Relationship.cs: `SetKnownEmpireForAuthoritativeSync`.
3. **Ship_Game/Gameplay/Relationship.Authoritative.cs** (`public partial class Relationship`, already partial upstream)
   - `SetAuthoritativeDiplomacyState`. Note: brief mentioned `SetRelationshipStateForAuthoritativeSync` — no such member exists in the tree (grep-verified); `SetAuthoritativeDiplomacyState` is the only added Relationship member.
4. **Ship_Game/Universe/SolarBodies/SBProduction.Authoritative.cs** (`public partial class SBProduction`; original marked `partial`)
   - `ReplaceQueueForAuthoritativeSync`, `AssertCanMutateQueue` (private helper body; its ~17 inline call sites stay in SBProduction.cs — irreducible per brief).
5. **Ship_Game/Commands/Goals/DeepSpaceBuildGoal.Authoritative.cs** (`public partial class DeepSpaceBuildGoal`; original marked `partial`)
   - `SetAuthoritativeReplayMovePosition`.
6. **Ship_Game/Universe/SolarBodies/SolarSystemBody.Authoritative.cs** (`public partial class SolarSystemBody`; original marked `partial`)
   - `OwnerIsHumanControlled`, `UseDeterministicRandom`, `UpdatePresentationVisibilityOnly`.
7. **Ship_Game/Universe/SolarBodies/Planet/Planet.Authoritative.cs** (`public sealed partial class Planet`)
   - `UpdatePassiveAuthoritativeView`, `SeedDeterministicBodyRandom` (helper body; two ctor call sites stay in Planet.cs).
8. **Ship_Game/Universe/UniverseScreen/UniverseObjectManager.Authoritative.cs** (`public partial class UniverseObjectManager`; original marked `partial`)
   - `UpdatePassiveAuthoritativeView`, `UpdatePassiveSystemPresentation`, `UpdatePassiveSolarSystemShipLists`, `SyncPassiveVisibleShipSceneObjects` (method bodies only; the gate/call sites in UpdateGame stay — Stage 4).

## Deliberately NOT moved (and why)

- **All `AuthoritativeMutationGuard.AssertCanMutate(...)` inline call sites** — irreducible inline hooks per brief (Ship.QueueTotalRemoval, Empire money/automation methods, Empire_Fleets, Ship_Troop, Relationship.SetTreaty/CancelPrepareForWar/SetInitialStrength, SBProduction call sites, Planet.SetPrioritizedPort/ResetGarrisonSize, Fleet.cs).
- **`[StarData]` field declarations** (`Ship.IsTransientEnvironment`; Empire `AutoMilitary`/`AutoSpy`/`AISidekickEnabled`/`OracleSidekickEnabled`; DeepSpaceBuildGoal `HasAuthoritativeReplayMovePosition`/`AuthoritativeReplayMovePosition`) — left in the original files as a serialization-order caution (partial-class member ordering across files could perturb order-sensitive StarData layout; only the accessor/setter methods moved). If the orchestrator's save/replay suite proves order-insensitivity, these are a trivial follow-up move (~15 more lines/file).
- **Modified upstream members** (interleaved edits, stay by rule): `Ship_Update.IsVisibleToPlayer*`/`InLocalPlayerSensorRange`, `Ship_Rendering.DrawTactical*`, `Empire.Random` declaration, `Empire.ResetTechsAndUnlocks`/`InitEmpireUnlocks` refactor, `Empire.AssignSniffingTasks`/`AssignExplorationTasks`, `SolarSystemBody.OwnerIsPlayer`/`Random`/colonize logic, `SBProduction` upstream methods, `UniverseObjectManager.UpdateSystems` parallel-gate + `UpdateAllShips` viewer logic, all `Notifications?.` null-guards.
- **`Empire.ApplyDataUnlocks`** — added helper, but it is the extraction half of the upstream `InitEmpireUnlocks` refactor; moving it would strand the refactored upstream method's body. Left in Empire.cs.
- **Non-authoritative additive members** (out of S1 scope, candidates for a later sweep): Ship arena fields `ArenaEngagementBias`/`ArenaStandoffDecay`/`ArenaCombatTicks` (Ship.cs, referenced from modified `Ship_Update.Update`), `Ship_Repair.RepairFully`/`RechargeShieldsFully` (arena shop), `SolarSystemBody.SetArenaGravityWellRadius`, `KnownByEmpire.SetKnownMask` (different type, not in target list).
- **Fleet.cs** — diff vs upstream is guard call sites + a using only; zero movable members, no partial created.

## Notes

- Line-ending: several touched files had mixed/inconsistent EOL from earlier fork edits; edits normalized EOL on some adjacent lines. Content-wise the working-tree diff vs the pre-extraction HEAD is pure deletion (verified with `git diff --ignore-cr-at-eol`), and all reported numbers use `--ignore-all-space`.
- `partial` keyword added to: SBProduction, DeepSpaceBuildGoal, SolarSystemBody, UniverseObjectManager (Ship, Empire, Relationship, Planet were already partial).

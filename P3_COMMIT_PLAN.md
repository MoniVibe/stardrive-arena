# P3 Commit Plan

Git staging/commit was blocked by sandbox permissions:

`fatal: Unable to create 'C:/dev/stardrive/StarDrive-main/.git/worktrees/StarDrive-desync-p3/index.lock': Permission denied`

Run these commands from `C:\dev\stardrive\StarDrive-desync-p3`:

```powershell
git add DESYNC_P3_REPORT.md P3_COMMIT_PLAN.md `
  Ship_Game\Multiplayer\Authoritative\AuthoritativeMutationGuard.cs `
  Ship_Game\Empire.cs `
  Ship_Game\Empire_Relationship.cs `
  Ship_Game\Gameplay\Relationship.cs `
  Ship_Game\Multiplayer\Authoritative\Authoritative4XClientContext.cs `
  Ship_Game\Multiplayer\Authoritative\Authoritative4XCommandApplicator.cs `
  Ship_Game\Multiplayer\Authoritative\Authoritative4XLiveSession.cs `
  Ship_Game\Multiplayer\Authoritative\Authoritative4XSession.cs `
  Ship_Game\Ships\Ship.cs `
  Ship_Game\Ships\Ship_Troop.cs `
  Ship_Game\Troops\Troop.cs `
  Ship_Game\Universe\SolarBodies\Planet\Planet.cs `
  Ship_Game\Universe\SolarBodies\Planet\Planet_BuildDefenses.cs `
  Ship_Game\Universe\SolarBodies\Planet\Planet_Colonize.cs `
  Ship_Game\Universe\SolarBodies\Planet\Planet_Govern.cs `
  Ship_Game\Universe\SolarBodies\Planet\Planet_Trade.cs `
  Ship_Game\Universe\SolarBodies\TroopManager.cs `
  StarDrive.csproj `
  UnitTests\Multiplayer\Authoritative4XSessionTests.cs

git commit -m "Add passive client mutation tripwire"
```

Do not push from this lane unless explicitly requested.

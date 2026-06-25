using System;
using System.Collections.Generic;
using System.Linq;
using SDLockstep;
using SDUtils.Deterministic;
using Ship_Game.Determinism;
using Ship_Game.Determinism.Lockstep;
using Ship_Game.Ships;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.GameScreens.Arena;

public readonly struct ArenaMultiplayerShipSnapshot
{
    public readonly int PlayerEmpireId;
    public readonly int EnemyEmpireId;
    public readonly int[] PlayerShipIds;
    public readonly int[] EnemyShipIds;
    public readonly string[] PlayerFleetDesigns;
    public readonly string[] EnemyFleetDesigns;
    public readonly string PlayerDesign;
    public readonly string EnemyDesign;

    public ArenaMultiplayerShipSnapshot(int playerEmpireId, int enemyEmpireId,
        int[] playerShipIds, int[] enemyShipIds, string[] playerFleetDesigns,
        string[] enemyFleetDesigns, string playerDesign, string enemyDesign)
    {
        PlayerEmpireId = playerEmpireId;
        EnemyEmpireId = enemyEmpireId;
        PlayerShipIds = playerShipIds ?? Array.Empty<int>();
        EnemyShipIds = enemyShipIds ?? Array.Empty<int>();
        PlayerFleetDesigns = playerFleetDesigns ?? Array.Empty<string>();
        EnemyFleetDesigns = enemyFleetDesigns ?? Array.Empty<string>();
        PlayerDesign = playerDesign ?? "";
        EnemyDesign = enemyDesign ?? "";
    }
}

public readonly struct ArenaMultiplayerMatchStatus
{
    public readonly int PlayerAlive;
    public readonly int EnemyAlive;
    public readonly bool Ended;
    public readonly int WinnerPeerId;

    public ArenaMultiplayerMatchStatus(int playerAlive, int enemyAlive)
    {
        PlayerAlive = playerAlive;
        EnemyAlive = enemyAlive;
        Ended = playerAlive == 0 || enemyAlive == 0;
        WinnerPeerId = !Ended ? 0
            : playerAlive > 0 && enemyAlive == 0 ? ArenaMultiplayerSession.HostPlayerPeerId
            : enemyAlive > 0 && playerAlive == 0 ? ArenaMultiplayerSession.JoinPlayerPeerId
            : 0;
    }
}

public sealed partial class ArenaFightScreen
{
    string[] MultiplayerHostFleetDesigns = Array.Empty<string>();
    string[] MultiplayerJoinFleetDesigns = Array.Empty<string>();
    bool MultiplayerPvPMode;

    public bool HasPendingMultiplayerPvPSetup => MultiplayerPvPMode;

    public ArenaMultiplayerShipSnapshot MultiplayerSnapshot()
        => new(
            ArenaPlayer?.Id ?? 1,
            ArenaEnemy?.Id ?? 2,
            PlayerShips.Where(s => s?.Active == true).Select(s => s.Id).OrderBy(id => id).ToArray(),
            EnemyShips.Where(s => s?.Active == true).Select(s => s.Id).OrderBy(id => id).ToArray(),
            MultiplayerHostFleetDesigns,
            MultiplayerJoinFleetDesigns,
            PlayerDesign?.Name ?? "",
            EnemyDesign?.Name ?? "");

    public void ConfigureMultiplayerPvP(ArenaMultiplayerSettings settings)
    {
        ArenaMultiplayerSettings resolved = (settings ?? new ArenaMultiplayerSettings()).WithResolvedFleets();
        MultiplayerHostFleetDesigns = ArenaMultiplayerSettings.NormalizeFleet(resolved.HostFleetDesignNames);
        MultiplayerJoinFleetDesigns = ArenaMultiplayerSettings.NormalizeFleet(resolved.JoinFleetDesignNames);
        MultiplayerPvPMode = MultiplayerHostFleetDesigns.Length > 0 && MultiplayerJoinFleetDesigns.Length > 0;
    }

    public void StartMultiplayerPvPMatch()
    {
        if (!MultiplayerPvPMode)
            return;

        Round = 1;
        AdvanceRoundOnNextFight = false;
        Phase = RunPhase.Fighting;
        UState.Paused = false;
        CurrentFightModifier = ArenaFightModifier.None;
        CurrentBossEncounter = ArenaBossEncounter.None;
        ActiveFightOption = null;
        PendingFightOption = null;
        FieldedFleet.Clear();
        FleetShipVessel.Clear();
        FleetShipBaseSlotIndices.Clear();
        RemoveMultiplayerShips(PlayerShips);
        RemoveMultiplayerShips(EnemyShips);

        IShipDesign[] hostDesigns = ResolveMultiplayerFleet(MultiplayerHostFleetDesigns);
        IShipDesign[] joinDesigns = ResolveMultiplayerFleet(MultiplayerJoinFleetDesigns);
        if (hostDesigns.Length == 0 || joinDesigns.Length == 0)
            throw new InvalidOperationException("Arena PvP lockstep requires at least one legal design on each side.");

        PlayerDesign = hostDesigns[0];
        EnemyDesign = joinDesigns[0];
        SpawnMultiplayerFleet(PlayerShips, ArenaPlayer, hostDesigns, -Gap, PlayerSpawnFacing);
        SpawnMultiplayerFleet(EnemyShips, ArenaEnemy, joinDesigns, +Gap, EnemySpawnFacing);
        RetargetTimer = 0f;
        EngageAll();
        RunStarted = PlayerShips.Count > 0 && EnemyShips.Count > 0;
        if (!RunStarted)
            throw new InvalidOperationException("Arena PvP lockstep failed to spawn both fleets.");
    }

    public void PrepareForMultiplayerLockstep(uint rngSeed)
    {
        CreateSimThread = false;
        UState.Paused = false;
        UState.P.GravityWellRange = 0;
        UState.Objects.EnableParallelUpdate = false;
        ArenaEngineCapabilities.TryEnableSeededRng(UState, rngSeed);
    }

    public UniverseStateLockstepSimulation CreateMultiplayerLockstepSimulation(
        DeterminismProfile profile = DeterminismProfile.ReplayWinX64Float,
        float dt = 1f / 60f)
        => new(this, profile, dt);

    public SimCommand BuildMultiplayerFocusCommand(int peerId, uint tick, uint localSequence)
    {
        bool peerIsPlayer = ArenaPlayer != null && peerId == ArenaPlayer.Id;
        bool peerIsEnemy = ArenaEnemy != null && peerId == ArenaEnemy.Id;
        if (!peerIsPlayer && !peerIsEnemy)
            return new SimCommand(tick, peerId, localSequence, SimCommandKind.NoOp);

        Ship subject = FirstAlive(peerIsPlayer ? PlayerShips : EnemyShips);
        Ship target = FirstAlive(peerIsPlayer ? EnemyShips : PlayerShips);
        if (subject == null || target == null)
            return new SimCommand(tick, peerId, localSequence, SimCommandKind.NoOp);

        return new SimCommand(tick, peerId, localSequence, SimCommandKind.AttackTarget, subject.Id, target.Id);
    }

    public (ulong lo, ulong hi, string algorithm) MultiplayerStateHash(
        DeterminismProfile profile = DeterminismProfile.ReplayWinX64Float)
        => UState.ComputeAuthoritativeStateHash(profile);

    public void ForceMultiplayerDesyncForTest()
    {
        Ship ship = FirstAlive(PlayerShips);
        if (ship != null)
            ship.Position = ship.Position + new Vector2(3f, 0f);
    }

    public ArenaMultiplayerMatchStatus MultiplayerMatchStatus()
        => new(AliveCount(PlayerShips), AliveCount(EnemyShips));

    static void SpawnMultiplayerFleet(List<Ship> ships, Empire owner, IShipDesign[] designs, float x, Vector2 facing)
    {
        Vector2 center = ArenaCenter;
        for (int i = 0; i < designs.Length; ++i)
        {
            float y = (i - (designs.Length - 1) / 2f) * RowSpan;
            Ship ship = CreateArenaShipAtPoint(owner.Universe, designs[i].Name, owner, center + new Vector2(x, y), facing);
            if (ship == null)
                throw new InvalidOperationException($"Failed to spawn Arena PvP ship '{designs[i].Name}'.");
            ship.SensorRange = 400000f;
            ships.Add(ship);
        }
    }

    static IShipDesign[] ResolveMultiplayerFleet(string[] designNames)
    {
        var designs = new List<IShipDesign>();
        foreach (string name in ArenaMultiplayerSettings.NormalizeFleet(designNames))
        {
            if (!ResourceManager.Ships.GetDesign(name, out IShipDesign design)
                || !IsLegalCombatCraft(design))
                continue;
            designs.Add(design);
        }
        return designs.ToArray();
    }

    static void RemoveMultiplayerShips(List<Ship> ships)
    {
        foreach (Ship ship in ships)
            if (ship != null && ship.Active)
                ship.Die(null, cleanupOnly: true);
        ships.Clear();
    }

    static int AliveCount(List<Ship> ships)
        => ships?.Count(s => s != null && s.IsAlive) ?? 0;

    static Ship FirstAlive(System.Collections.Generic.List<Ship> ships)
    {
        if (ships == null)
            return null;
        return ships
            .Where(s => s != null && s.Active && s.IsAlive)
            .OrderBy(s => s.Id)
            .FirstOrDefault();
    }
}

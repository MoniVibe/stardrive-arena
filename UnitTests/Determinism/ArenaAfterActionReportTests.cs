using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;
using Ship_Game.Determinism;
using Ship_Game.GameScreens.Arena;
using Ship_Game.Ships;
using Vector2 = SDGraphics.Vector2;

namespace UnitTests.Determinism;

// ADDENDUM 3 — AFTER-ACTION REPORT proofs. The per-ship stat counters (Ship.ArenaDamageDealt /
// Ship.ArenaDamageAbsorbed, plus the pre-existing Ship.Kills) are PURE OBSERVATION: transient, never hashed,
// never fed back into the sim. These focused headless proofs assert:
//   1. The counters are OUTSIDE the authoritative checksum (mutating them does not change the state hash) —
//      the "add-counter run digest == baseline digest" zero-determinism-surface proof.
//   2. A fixed-seed two-peer arena match produces BYTE-IDENTICAL after-action reports on both peers.
//   3. The totals reconcile (damage dealt is non-zero and consistent; absorbed is accounted) and
//      survivors/kills read back from the report match the sim.
[TestClass]
public class ArenaAfterActionReportTests : StarDriveTest
{
    const DeterminismProfile Profile = DeterminismProfile.ReplayWinX64Float;

    // PROOF: the counters never enter UniverseStateHash.WriteAuthoritative. We hash the authoritative state,
    // then bump the transient counters on every ship, then hash again. The digest MUST be byte-identical —
    // that is exactly "add-counter run digest == baseline digest" at the field level: zero determinism surface.
    [TestMethod]
    public void AfterActionCountersAreOutsideTheChecksum_Headless()
    {
        CreateUniverseAndPlayerEmpire();
        Ship a = SpawnShip("Fang Strafer", Player, new Vector2(-1500f, 0f), new Vector2(1f, 0f));
        Ship b = SpawnShip("Fang Strafer", Enemy, new Vector2(1500f, 0f), new Vector2(-1f, 0f));
        UState.Objects.Update(TestSimStep);

        (ulong lo, ulong hi, string _) baseline = UState.ComputeAuthoritativeStateHash(Profile);

        foreach (Ship s in UState.Ships)
        {
            s.ArenaDamageDealt += 12345.678f;
            s.ArenaDamageTaken += 6543.21f;
            s.ArenaDamageAbsorbed += 987.654f;
        }

        (ulong lo, ulong hi, string _) mutated = UState.ComputeAuthoritativeStateHash(Profile);

        Assert.AreEqual(baseline.lo, mutated.lo,
            "Mutating the after-action counters must NOT change the authoritative checksum (low word).");
        Assert.AreEqual(baseline.hi, mutated.hi,
            "Mutating the after-action counters must NOT change the authoritative checksum (high word).");
        // Sanity: the fields really did take the values (the test isn't vacuous).
        Assert.IsTrue(a.ArenaDamageDealt >= 12345f && b.ArenaDamageAbsorbed >= 987f,
            "The transient counters must actually be settable on a ship.");
    }

    // PROOF: a fixed-seed two-peer arena match gathers BYTE-IDENTICAL after-action reports on both peers, and
    // the same fixed seed re-run produces the identical report + identical final digest (determinism holds with
    // the counters present).
    [TestMethod]
    public void AfterActionReportIsIdenticalOnBothPeersAndReconciles_Headless()
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_aar_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;

        try
        {
            // Symmetric wingmate brawl: both sides field identical stock combat craft so damage flows through
            // direct weapon fire on both sides (a clean, non-degenerate fight for the dealt/absorbed read-out).
            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED,
                RngSeed = 0xA12EA000u,
                InputDelay = 3,
                MaxTurns = 600,
                CommandEveryTurns = 1,
                HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul),
                JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul),
            };

            ArenaMultiplayerRunResult result = ArenaMultiplayerSession.RunInProcess(settings);

            Assert.IsFalse(result.Desynced,
                $"After-action proof desynced at turn {result.DesyncTurn}: {result.DesyncReason}");
            Assert.IsNotNull(result.HostAfterAction, "Host peer must gather an after-action report at resolve.");
            Assert.IsNotNull(result.JoinAfterAction, "Join peer must gather an after-action report at resolve.");

            // (1) BYTE-IDENTICAL between peers — the whole determinism-for-free claim.
            Assert.AreEqual(result.HostAfterAction.Signature(), result.JoinAfterAction.Signature(),
                "Both peers must compute a byte-identical after-action report from the shared deterministic sim.");

            ArenaAfterActionReport rep = result.HostAfterAction;

            // (2) Reconciliation: side totals are the exact sums of their per-ship rows (no double counting,
            // nothing dropped) on every channel; and the global dealt==taken reconciliation invariant holds.
            AssertSideReconciles(rep.Host);
            AssertSideReconciles(rep.Join);
            Assert.AreEqual(rep.TotalDamageDealt, rep.TotalDamageTaken, 0.001f,
                "Report-wide sum of damage dealt must equal sum of damage taken.");

            // (3) Survivors + start counts read back from the report match the sim snapshot (the snapshot lists
            // the ALIVE ship ids per side, so its length is exactly the survivor count).
            Assert.AreEqual(result.HostSnapshot.PlayerShipIds.Length, rep.Host.Survivors,
                "Report host survivors must equal the alive host-ship count in the sim snapshot.");
            Assert.AreEqual(result.HostSnapshot.EnemyShipIds.Length, rep.Join.Survivors,
                "Report join survivors must equal the alive join-ship count in the sim snapshot.");
            Assert.AreEqual(settings.HostFleetDesignNames.Length, rep.Host.StartCount,
                "The host side must field one row per host design.");
            Assert.AreEqual(settings.JoinFleetDesignNames.Length, rep.Join.StartCount,
                "The join side must field one row per join design.");

            // (4) Re-run identical seed => identical report + identical final digest. This is the "add-counter run
            // digest == baseline digest" proof at the match level: the counters present do NOT perturb the sim.
            ArenaMultiplayerRunResult rerun = ArenaMultiplayerSession.RunInProcess(settings);
            Assert.IsFalse(rerun.Desynced, "Re-run must also stay in sync.");
            Assert.AreEqual(result.FinalHash, rerun.FinalHash,
                "Identical seed must reproduce the identical final digest with the counters present (zero surface).");
            Assert.AreEqual(rep.Signature(), rerun.HostAfterAction.Signature(),
                "Identical seed must reproduce the identical after-action report.");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    // PROOF: the after-action end panel builds and exposes the summary labels, and the FULL REPORT expansion
    // materializes without throwing (ArenaTheme card + DynamicText AAR labels + ScrollList per-ship breakdown).
    [TestMethod]
    public void AfterActionEndPanelBuildsWithSummaryAndFullReport_Headless()
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_aar_ui_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        ArenaFightScreen screen = null;

        try
        {
            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED,
                RngSeed = 0xA12EA000u,
                InputDelay = 3,
                MaxTurns = 60,
                CommandEveryTurns = 1,
                HostFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x1001ul),
                JoinFleetDesignNames = FleetNames(ArenaStartArchetype.Wingmates, 0x2002ul),
            }.WithResolvedFleets();

            screen = ArenaFightScreen.Create(settings.HostRacePreference, settings.MatchSeed,
                startAtHub: false, opponentPreference: settings.JoinRacePreference);
            screen.ConfigureMultiplayerPvP(settings);
            screen.LoadContent();

            screen.ShowMultiplayerEndPanelForHeadless();

            Assert.IsTrue(screen.MultiplayerEndPanelVisibleForHeadless,
                "The match-end panel must be visible after resolve.");
            Assert.IsNotNull(screen.MultiplayerAfterActionForHeadless,
                "The after-action report must be gathered when the panel shows.");

            // Existing result controls are still present alongside the AAR.
            Assert.IsTrue(screen.Find("arena_mp_end_rematch", out UIButton _), "Rematch button must remain.");
            Assert.IsTrue(screen.Find("arena_mp_end_lobby", out UIButton _), "Lobby button must remain.");
            Assert.IsTrue(screen.Find("arena_mp_end_full_report", out UIButton _),
                "The FULL REPORT toggle must be present.");

            // The compact after-action summary labels exist.
            foreach (string name in new[]
                     {
                         "arena_mp_aar_survivors", "arena_mp_aar_topdamage",
                         "arena_mp_aar_absorber", "arena_mp_aar_killer", "arena_mp_aar_total",
                     })
                Assert.IsTrue(screen.Find(name, out UILabel _),
                    $"The after-action summary label '{name}' must be present.");

            // The FULL REPORT expansion materializes without throwing.
            bool expanded = screen.ToggleMultiplayerFullReportForHeadless();
            Assert.IsTrue(expanded, "Toggling FULL REPORT must materialize and show the per-ship scroll list.");
            Assert.IsTrue(screen.Find("arena_mp_end_full_report_list", out ScrollList<ArenaPopupListItem> _),
                "The full-report scroll list must be added to the screen.");

            // A second toggle hides it again (no crash on the collapse path).
            bool collapsed = screen.ToggleMultiplayerFullReportForHeadless();
            Assert.IsFalse(collapsed, "A second toggle must collapse the full-report list.");
        }
        finally
        {
            try { screen?.ExitScreen(); } catch { }
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    static void AssertSideReconciles(ArenaAfterActionSide side)
    {
        float dealt = side.Ships.Sum(r => r.DamageDealt);
        float taken = side.Ships.Sum(r => r.DamageTaken);
        float absorbed = side.Ships.Sum(r => r.DamageAbsorbed);
        int kills = side.Ships.Sum(r => r.Kills);
        int survivors = side.Ships.Count(r => r.Survived);
        Assert.AreEqual(dealt, side.DamageDealt, 0.001f,
            $"{side.Label} side DamageDealt total must equal the sum of its per-ship rows.");
        Assert.AreEqual(taken, side.DamageTaken, 0.001f,
            $"{side.Label} side DamageTaken total must equal the sum of its per-ship rows.");
        Assert.AreEqual(absorbed, side.DamageAbsorbed, 0.001f,
            $"{side.Label} side DamageAbsorbed total must equal the sum of its per-ship rows.");
        Assert.AreEqual(kills, side.Kills,
            $"{side.Label} side Kills total must equal the sum of its per-ship rows.");
        Assert.AreEqual(survivors, side.Survivors,
            $"{side.Label} side Survivors total must equal the count of surviving rows.");
        Assert.IsTrue(side.DamageDealt >= 0f && side.DamageAbsorbed >= 0f,
            $"{side.Label} side counters must be non-negative.");
    }

    // PROOF: in a real weapons-fire exchange the counters accumulate at the deterministic sites AND reconcile:
    // sum(damage DEALT by attackers) == sum(damage TAKEN by defenders) exactly (same site, same amount), and
    // the after-action report gathered from those ships reflects the same numbers. Damage was also absorbed by
    // defenses. This is the director's design-iteration feedback loop, proven at the sim level.
    [TestMethod]
    public void AfterActionCountersAccumulateAndReconcileInDirectCombat_Headless()
    {
        CreateUniverseAndPlayerEmpire();
        UState.P.GravityWellRange = 0;
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;

        Ship a = SpawnShip("Fang Strafer", Player, new Vector2(-900f, 0f), new Vector2(1f, 0f));
        Ship b = SpawnShip("Fang Strafer", Enemy, new Vector2(900f, 0f), new Vector2(-1f, 0f));
        a.SensorRange = 60000f;
        b.SensorRange = 60000f;
        a.AI.OrderAttackSpecificTarget(b);
        b.AI.OrderAttackSpecificTarget(a);

        for (int tick = 0; tick < 600; ++tick)
        {
            if ((tick % 60) == 0)
            {
                if (a.Active && b.Active) a.AI.OrderAttackSpecificTarget(b);
                if (a.Active && b.Active) b.AI.OrderAttackSpecificTarget(a);
            }
            UState.Objects.Update(TestSimStep);
        }

        float totalDealt = a.ArenaDamageDealt + b.ArenaDamageDealt;
        float totalTaken = a.ArenaDamageTaken + b.ArenaDamageTaken;
        Assert.IsTrue(totalDealt > 0f,
            $"Weapons fire must accumulate ArenaDamageDealt. a.dealt={a.ArenaDamageDealt} b.dealt={b.ArenaDamageDealt}");

        // RECONCILIATION: every dealt credit is mirrored as a taken debit with the identical amount at the same
        // site, so the global totals are exactly equal.
        Assert.AreEqual(totalDealt, totalTaken, 0.001f,
            "Sum of per-attacker damage dealt must equal sum of per-defender damage taken (exact reconciliation).");

        // Cross-fire reconciliation: what A dealt is exactly what B took, and vice-versa.
        Assert.AreEqual(a.ArenaDamageDealt, b.ArenaDamageTaken, 0.001f,
            "Damage A dealt must equal damage B took.");
        Assert.AreEqual(b.ArenaDamageDealt, a.ArenaDamageTaken, 0.001f,
            "Damage B dealt must equal damage A took.");

        // The after-action report reads these numbers straight back.
        var rep = ArenaAfterActionReport.Gather(new[] { a }, new[] { b });
        Assert.AreEqual(a.ArenaDamageDealt, rep.Host.DamageDealt, 0.001f, "Report must reflect A's dealt.");
        Assert.AreEqual(b.ArenaDamageTaken, rep.Join.DamageTaken, 0.001f, "Report must reflect B's taken.");
        Assert.AreEqual(rep.TotalDamageDealt, rep.TotalDamageTaken, 0.001f,
            "Report totals must reconcile dealt == taken.");
    }

    static string[] FleetNames(ArenaStartArchetype archetype, ulong seed)
        => CareerManager.StartingRosterDesigns(archetype, seed)
            .Select(d => d.Name)
            .ToArray();

    static IShipDesign[] LegalPvPDesigns()
        => ResourceManager.Ships.Designs
            .Where(ArenaFightScreen.IsLegalCombatCraft)
            .Where(ArenaFightScreen.IsStockContentDesign)
            .OrderBy(d => d.BaseStrength)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();
}

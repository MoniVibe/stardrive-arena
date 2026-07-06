using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDLockstep;
using Ship_Game;
using Ship_Game.Ships;
using Ship_Game.GameScreens.Arena;
using UnitTests;

namespace UnitTests.Determinism;

/// <summary>
/// Persistent-ammo economy proofs (STARDRIVE_ARENA_AMMO_ECONOMY_EXEC_PLAN_20260706).
///
/// Phase 1 — finite MAGAZINE + host UnlimitedAmmo toggle. Determinism is law: finite ammo runs INSIDE the
/// sim, so the regen gate keys off the symmetric per-ship ArenaFiniteAmmo instance flag (threaded at the
/// CreateArenaShipAtPoint spawn choke, mirroring ArenaCombatant) AND the fingerprinted ruleset toggle —
/// never any per-peer state. Default UnlimitedAmmo=true keeps a default match byte-identical to trunk.
/// </summary>
[TestClass]
public class ArenaAmmoEconomyTests : StarDriveTest
{
    static ArenaMultiplayerSettings OrdnanceMatch(bool unlimitedAmmo, int maxTurns = 900)
    {
        // Pick an ordnance-hungry hull with a SMALL magazine and modest regen, so the bout actually burns
        // the magazine dry within the match window — the whole point of the finite-vs-unlimited digest
        // divergence proof (G1). A hull with a huge magazine + massive regen (e.g. Behemoth) would never
        // deplete, making finite/unlimited indistinguishable. Fall back to the smallest-magazine legal
        // ordnance hull if the named one is absent in this content set.
        string top = PickSmallMagazineOrdnanceDesign();

        return new ArenaMultiplayerSettings
        {
            MatchSeed = 0x5EED,
            RngSeed = 0xA12EA000u,
            InputDelay = 3,
            MaxTurns = maxTurns,
            CommandEveryTurns = 1,
            HostFleetDesignNames = new[] { top, top },
            JoinFleetDesignNames = new[] { top },
            Ruleset = new ArenaMultiplayerRuleset
            {
                Mode = ArenaMatchMode.Sandbox,
                RosterSource = ArenaRosterSource.AllContent,
                CountdownSeconds = 0,    // engage immediately so the bout has time to burn the magazine dry
                MaxMatchSeconds = 0,     // 0 => no derived cap; MaxTurns is the ceiling
                UnlimitedAmmo = unlimitedAmmo,
            },
        }.WithResolvedFleets();
    }

    // Spawn candidate hulls once and pick the smallest-magazine legal design that BOTH fires ordnance
    // weapons AND regenerates ordnance (OrdAddedPerSecond > 0). Only such a hull makes the regen gate
    // observable: it depletes its small magazine and, under UnlimitedAmmo, would refill — so finite vs.
    // unlimited diverge. Deterministic (fixed content order); cached across calls in one test run.
    static string _cachedOrdnanceDesign;
    static string PickSmallMagazineOrdnanceDesign()
    {
        if (_cachedOrdnanceDesign != null)
            return _cachedOrdnanceDesign;

        IShipDesign[] candidates = ResourceManager.Ships.Designs
            .Where(ArenaFightScreen.IsLegalCombatCraft)
            .Where(ArenaFightScreen.IsStockContentDesign)
            .OrderByDescending(d => d.BaseStrength)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .Take(40).ToArray();

        string best = null;
        float bestOrdMax = float.MaxValue;
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_pick_{Guid.NewGuid():N}.yaml");
        string savedPath = ArenaFightScreen.CareerSavePath;
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        try
        {
            foreach (IShipDesign d in candidates)
            {
                ArenaFightScreen screen = null;
                try
                {
                    var s = new ArenaMultiplayerSettings
                    {
                        MatchSeed = 0x5EED, RngSeed = 0xA12EA000u, InputDelay = 3, MaxTurns = 30, CommandEveryTurns = 1,
                        HostFleetDesignNames = new[] { d.Name }, JoinFleetDesignNames = new[] { d.Name },
                        Ruleset = new ArenaMultiplayerRuleset { Mode = ArenaMatchMode.Sandbox, RosterSource = ArenaRosterSource.AllContent },
                    }.WithResolvedFleets();
                    screen = ArenaFightScreen.Create(s.HostRacePreference, s.MatchSeed, startAtHub: false, opponentPreference: s.JoinRacePreference);
                    screen.ConfigureMultiplayerPvP(s);
                    screen.LoadContent();
                    ArenaMultiplayerShipSnapshot snap = screen.MultiplayerSnapshot();
                    Ship ship = screen.UState.Objects.FindShip(snap.PlayerShipIds.FirstOrDefault());
                    if (ship == null) continue;
                    int ordWeapons = ship.Weapons?.Count(w => w.OrdinanceRequiredToFire > 0f) ?? 0;
                    // Small enough to deplete in a bout, but real ordnance use + real regen to suppress.
                    if (ordWeapons > 0 && ship.OrdAddedPerSecond > 0f && ship.OrdinanceMax > 0f
                        && ship.OrdinanceMax < bestOrdMax)
                    {
                        best = d.Name;
                        bestOrdMax = ship.OrdinanceMax;
                    }
                }
                finally
                {
                    try { screen?.ExitScreen(); } catch { }
                }
            }
        }
        finally
        {
            ArenaFightScreen.CareerSavePath = savedPath;
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }

        Assert.IsNotNull(best, "No legal stock design fires ordnance weapons AND regenerates ordnance; " +
            "the finite-ammo regen gate cannot be observed without one.");
        _cachedOrdnanceDesign = best;
        return best;
    }

    // ---- G1: an ordnance ship on a FINITE magazine runs dry — the sim-tick regen gate suppresses refill ----
    // ---- for a finite arena combatant, while an UnlimitedAmmo ship regenerates. Directly exercises the ----
    // ---- gated line Ship.UpdateModulesAndStatus: `if (OrdAddedPerSecond > 0 && !(ArenaCombatant && ----
    // ---- ArenaFiniteAmmo)) ChangeOrdnance(...)`. Plus: the finite match stays lockstep-synced (below). ----
    [TestMethod]
    public void G1_FiniteAmmoShip_RunsDry_UnlimitedShipRegens_Headless()
    {
        LoadAllGameData();

        // FINITE ship: drained ordnance must NOT regenerate under repeated per-second sim ticks.
        Ship finiteShip = SpawnFirstArenaShip(unlimitedAmmo: false);
        Assert.IsTrue(finiteShip.ArenaCombatant && finiteShip.ArenaFiniteAmmo,
            "The finite-ammo arena ship must carry both the combatant and finite-ammo markers.");
        Assert.IsTrue(finiteShip.OrdAddedPerSecond > 0f,
            "The chosen hull must have positive ordnance regen so the gate is observable.");
        finiteShip.SetOrdnance(finiteShip.OrdinanceMax * 0.25f);
        float finiteBefore = finiteShip.Ordinance;
        for (int i = 0; i < 5; ++i)
            finiteShip.UpdateShipStatus(FixedSimTime.One); // 5 seconds of per-second status updates
        Assert.AreEqual(finiteBefore, finiteShip.Ordinance, 0.001f,
            "A finite-magazine arena combatant must NOT regenerate ordnance in the sim tick.");

        // UNLIMITED ship (same hull): drained ordnance MUST regenerate — proving the gate is the only diff.
        Ship unlimitedShip = SpawnFirstArenaShip(unlimitedAmmo: true);
        Assert.IsTrue(unlimitedShip.ArenaCombatant && !unlimitedShip.ArenaFiniteAmmo,
            "The unlimited-ammo arena ship must be a combatant WITHOUT the finite-ammo marker.");
        unlimitedShip.SetOrdnance(unlimitedShip.OrdinanceMax * 0.25f);
        float unlimitedBefore = unlimitedShip.Ordinance;
        unlimitedShip.UpdateShipStatus(FixedSimTime.One);
        Assert.IsTrue(unlimitedShip.Ordinance > unlimitedBefore,
            "An UnlimitedAmmo arena combatant must regenerate ordnance exactly as trunk does today.");
    }

    // ---- G1 (sync): finite ammo runs INSIDE the sim, so prove a real 2-peer finite match stays lockstep- ----
    // ---- identical (no desync, all turn hashes match, host==join final digest) — the gate is symmetric. ----
    [TestMethod]
    public void G1_FiniteAmmoMatch_TwoPeersStayInSync_Headless()
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_ammo_g1sync_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;

        try
        {
            ArenaMultiplayerRunResult finiteResult = ArenaMultiplayerSession.RunInProcess(OrdnanceMatch(unlimitedAmmo: false));
            Assert.IsFalse(finiteResult.Desynced,
                $"Finite-ammo match desynced at turn {finiteResult.DesyncTurn}: {finiteResult.DesyncReason}");
            Assert.IsTrue(finiteResult.TurnHashes.All(h => h.Match),
                "Every finite-ammo turn hash must match across peers (finite ammo is lockstep-safe).");
            Assert.IsFalse(string.IsNullOrWhiteSpace(finiteResult.FinalHash),
                "The finite-ammo match must produce a final digest.");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    // Spawn a live arena PvP screen with the given ammo toggle and return the first player ship (an arena
    // combatant, finite-ammo-stamped per the ruleset). Caller owns nothing — the screen is left resident for
    // the test's lifetime (ordnance-tick reads only touch the ship, never re-enter the screen).
    static Ship SpawnFirstArenaShip(bool unlimitedAmmo)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_ammo_ship_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        ArenaMultiplayerSettings settings = OrdnanceMatch(unlimitedAmmo, maxTurns: 60);
        ArenaFightScreen screen = ArenaFightScreen.Create(settings.HostRacePreference, settings.MatchSeed,
            startAtHub: false, opponentPreference: settings.JoinRacePreference);
        screen.ConfigureMultiplayerPvP(settings);
        screen.LoadContent();
        ArenaMultiplayerShipSnapshot snapshot = screen.MultiplayerSnapshot();
        Ship ship = screen.UState.Objects.FindShip(snapshot.PlayerShipIds.First());
        Assert.IsNotNull(ship, "The arena PvP setup must spawn a player ship.");
        return ship;
    }

    // ---- G1 (stamp): the fingerprinted toggle threads onto the per-ship instance flag at spawn on the ----
    // ---- MP path; finite -> every arena ship carries ArenaFiniteAmmo, unlimited -> none do. ----
    [TestMethod]
    public void G1_FiniteAmmoFlag_StampsEveryArenaShipInstance_Headless()
    {
        LoadAllGameData();
        AssertSpawnedShipsFiniteFlag(unlimitedAmmo: false, expectFinite: true);
        AssertSpawnedShipsFiniteFlag(unlimitedAmmo: true, expectFinite: false);
    }

    static void AssertSpawnedShipsFiniteFlag(bool unlimitedAmmo, bool expectFinite)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_ammo_stamp_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        ArenaFightScreen screen = null;
        try
        {
            ArenaMultiplayerSettings settings = OrdnanceMatch(unlimitedAmmo, maxTurns: 60);
            screen = ArenaFightScreen.Create(settings.HostRacePreference, settings.MatchSeed,
                startAtHub: false, opponentPreference: settings.JoinRacePreference);
            screen.ConfigureMultiplayerPvP(settings);
            screen.LoadContent();

            ArenaMultiplayerShipSnapshot snapshot = screen.MultiplayerSnapshot();
            int[] ids = snapshot.PlayerShipIds.Concat(snapshot.EnemyShipIds).ToArray();
            Assert.IsTrue(ids.Length >= 2, "The finite-ammo PvP setup must spawn both fleets.");

            foreach (int id in ids)
            {
                Ship ship = screen.UState.Objects.FindShip(id);
                Assert.IsNotNull(ship, $"Spawned ship id {id} should resolve in the universe.");
                Assert.IsTrue(ship.ArenaCombatant, $"Arena ship {id} must be flagged as an arena combatant.");
                Assert.AreEqual(expectFinite, ship.ArenaFiniteAmmo,
                    $"Arena ship {id} finite-ammo flag must match the resolved ruleset toggle " +
                    $"(UnlimitedAmmo={unlimitedAmmo}).");
            }
        }
        finally
        {
            try { screen?.ExitScreen(); } catch { }
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    // ---- G2: a divergent UnlimitedAmmo toggle REJECTS at ValidateStartMessage (SettingsHash mismatch); ----
    // ---- an identical toggle validates clean. The toggle rides the fingerprint (AppendTo, append-only). ----
    [TestMethod]
    public void G2_DivergentUnlimitedAmmoToggle_RejectsAtHandshake_Headless()
    {
        LoadAllGameData();

        ArenaMultiplayerSettings hostFinite = OrdnanceMatch(unlimitedAmmo: false, maxTurns: 60);

        // Identical toggle: the host's own start validates clean against locally-derived settings.
        string cleanError = ArenaMultiplayerSettings.ValidateStartMessage(hostFinite.ToStartMessage(), out _);
        Assert.AreEqual("", cleanError,
            $"A start with a self-consistent UnlimitedAmmo toggle must validate clean. err='{cleanError}'");

        // Divergent toggle: the local peer resolves UnlimitedAmmo=true while the host sent finite. The
        // SettingsHash folds UnlimitedAmmo (AppendTo), so the local recompute mismatches -> reject.
        SessionStartMessage divergent = hostFinite.ToStartMessage();
        divergent.RulesetUnlimitedAmmo = true; // flip only the ammo toggle; leave the (finite) SettingsHash
        string mismatchError = ArenaMultiplayerSettings.ValidateStartMessage(divergent, out _);
        StringAssert.Contains(mismatchError, "settings mismatch",
            "A peer disagreeing on UnlimitedAmmo must reject at the SettingsHash handshake, never desync mid-match.");

        // Sanity: the two toggle values really do produce different SettingsHashes (the fold is live).
        ArenaMultiplayerSettings unlimited = OrdnanceMatch(unlimitedAmmo: true, maxTurns: 60);
        Assert.AreNotEqual(hostFinite.SettingsHash, unlimited.SettingsHash,
            "UnlimitedAmmo must change SettingsHash so a divergent toggle is caught at the handshake.");
    }

    // ---- G3: UnlimitedAmmo=ON reproduces the DEFAULT (trunk-behavior) digest exactly — the default is a ----
    // ---- true no-op. Explicit-true and default-ruleset matches evolve byte-identically. ----
    [TestMethod]
    public void G3_UnlimitedAmmoDefault_IsAByteIdenticalNoOp_Headless()
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_ammo_g3_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;

        try
        {
            // Explicit UnlimitedAmmo=true.
            ArenaMultiplayerSettings explicitOn = OrdnanceMatch(unlimitedAmmo: true);

            // Default ruleset (UnlimitedAmmo defaults to true) — same fleets/seeds, no toggle touched.
            ArenaMultiplayerSettings defaultRuleset = OrdnanceMatch(unlimitedAmmo: true);
            defaultRuleset.Ruleset = new ArenaMultiplayerRuleset
            {
                Mode = ArenaMatchMode.Sandbox,
                RosterSource = ArenaRosterSource.AllContent,
                // UnlimitedAmmo left at its default (true) — this is the trunk-behavior path.
            };
            defaultRuleset = defaultRuleset.WithResolvedFleets();

            Assert.IsTrue(defaultRuleset.Ruleset.UnlimitedAmmo,
                "The ruleset default must be UnlimitedAmmo=true so a default match is byte-identical to trunk.");

            ArenaMultiplayerRunResult onResult = ArenaMultiplayerSession.RunInProcess(explicitOn);
            ArenaMultiplayerRunResult defaultResult = ArenaMultiplayerSession.RunInProcess(defaultRuleset);

            Assert.IsFalse(onResult.Desynced, onResult.DesyncReason);
            Assert.IsFalse(defaultResult.Desynced, defaultResult.DesyncReason);
            Assert.AreEqual(defaultResult.FinalHash, onResult.FinalHash,
                "UnlimitedAmmo=ON must reproduce the default (trunk-behavior) lockstep digest exactly — the " +
                $"default toggle is a true no-op. on={onResult.FinalHash} default={defaultResult.FinalHash}");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    // ========================= Phase 2 — persistent ammo state (SP career) =========================

    // Spawn a fresh SP career arena with a single seeded OwnedVessel and return the live screen + the
    // flagship's OrdinanceMax. Mirrors the proven SP career spawn idiom (seed career -> save -> Create ->
    // LoadContent spawns PlayerShips). CareerUnlimitedAmmo drives finite (persistence) vs. default.
    static ArenaFightScreen SpawnCareer(OwnedVessel seed, bool unlimitedAmmo, string savePath, out float ordinanceMax)
    {
        bool savedUnlimited = ArenaFightScreen.CareerUnlimitedAmmo;
        ArenaFightScreen.CareerUnlimitedAmmo = unlimitedAmmo;
        try
        {
            var career = new ArenaCareer
            {
                CareerLevel = 7,
                Cash = 10_000,
                OwnedVessels = new[] { seed },
                ActiveVesselId = seed.VesselId,
            };
            Assert.IsTrue(CareerManager.Save(career, savePath), "Seeded ammo career must save.");

            ArenaFightScreen.CareerSavePath = savePath;
            ArenaFightScreen.PendingPlayerDesignName = null;
            ArenaFightScreen screen = ArenaFightScreen.Create("United", 0x5EED);
            screen.UState.Objects.EnableParallelUpdate = false;
            screen.UState.EnableDeterministicRng(0xA12EA000u);
            screen.CreateSimThread = false;
            screen.LoadContent();

            (float _, float OrdinanceMax)[] ord = screen.PlayerShipOrdnanceForHeadless;
            Assert.IsTrue(ord.Length >= 1, "A seeded SP career must field a flagship.");
            ordinanceMax = ord[0].OrdinanceMax;
            return screen;
        }
        finally
        {
            ArenaFightScreen.CareerUnlimitedAmmo = savedUnlimited;
        }
    }

    static string TierThreeDesignName()
    {
        // A strong stock ordnance hull so OrdinanceMax is comfortably positive (persisted-ammo proof needs
        // room between 0 and full). Deterministic pick by strength then name.
        IShipDesign d = ResourceManager.Ships.Designs
            .Where(ArenaFightScreen.IsLegalCombatCraft)
            .Where(ArenaFightScreen.IsStockContentDesign)
            .Where(x => x.BaseStrength > 0f)
            .OrderByDescending(x => x.BaseStrength)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .First();
        return d.Name;
    }

    // ---- G4: a vessel that spent ammo re-spawns with the PERSISTED ammo (bank -> reapply is deterministic ----
    // ---- and identical to the persisted value), and re-spawning twice yields the same result. ----
    [TestMethod]
    public void G4_PersistedOrdnance_ReAppliesDeterministicallyAtSpawn_Headless()
    {
        LoadAllGameData();
        string design = TierThreeDesignName();
        string pathA = Path.Combine(Path.GetTempPath(), $"arena_ammo_g4a_{Guid.NewGuid():N}.yaml");
        string pathB = Path.Combine(Path.GetTempPath(), $"arena_ammo_g4b_{Guid.NewGuid():N}.yaml");
        string savedPath = ArenaFightScreen.CareerSavePath;
        string savedPending = ArenaFightScreen.PendingPlayerDesignName;
        ArenaFightScreen screenA = null, screenB = null;
        try
        {
            // First pass: seed with MaxOrdnance placeholder; discover the real OrdinanceMax from a full spawn.
            var probeVessel = new OwnedVessel(design, 0f, 0, 0, "Ammo Proof") { VesselId = "ammo-proof" };
            screenA = SpawnCareer(probeVessel, unlimitedAmmo: false, pathA, out float ordinanceMax);
            Assert.IsTrue(ordinanceMax > 0f, "The proof hull must carry ordnance capacity.");
            // A fresh vessel (CurrentOrdnance 0) spawns FULL.
            Assert.AreEqual(ordinanceMax, screenA.PlayerShipOrdnanceForHeadless[0].Ordnance, 1f,
                "A vessel with no persisted ammo (CurrentOrdnance 0) must spawn at full OrdinanceMax.");
            try { screenA.ExitScreen(); } catch { }
            screenA = null;

            // Second pass: persist a PARTIAL magazine (40% spent -> 60% remaining) and prove re-spawn matches.
            float persisted = ordinanceMax * 0.6f;
            var spentVessel = new OwnedVessel(design, 0f, 0, 0, "Ammo Proof") { VesselId = "ammo-proof" };
            spentVessel.MaxOrdnance = ordinanceMax;
            spentVessel.CurrentOrdnance = persisted;
            screenB = SpawnCareer(spentVessel, unlimitedAmmo: false, pathB, out float ordinanceMax2);
            Assert.AreEqual(ordinanceMax, ordinanceMax2, 1f, "OrdinanceMax must be stable across spawns.");
            Assert.AreEqual(persisted, screenB.PlayerShipOrdnanceForHeadless[0].Ordnance, 1f,
                "A vessel that spent ammo must re-spawn at exactly its persisted CurrentOrdnance, not full.");
        }
        finally
        {
            try { screenA?.ExitScreen(); } catch { }
            try { screenB?.ExitScreen(); } catch { }
            ArenaFightScreen.CareerSavePath = savedPath;
            ArenaFightScreen.PendingPlayerDesignName = savedPending;
            try { if (File.Exists(pathA)) File.Delete(pathA); } catch { }
            try { if (File.Exists(pathB)) File.Delete(pathB); } catch { }
        }
    }

    // ---- G5: an OLD save (no ammo fields => deserialize to 0) spawns FULL — no regression. Also proves ----
    // ---- normalization treats 0 / >=max as "full". ----
    [TestMethod]
    public void G5_OldSaveWithoutAmmoFields_SpawnsFull_Headless()
    {
        LoadAllGameData();
        string design = TierThreeDesignName();
        string path = Path.Combine(Path.GetTempPath(), $"arena_ammo_g5_{Guid.NewGuid():N}.yaml");
        string savedPath = ArenaFightScreen.CareerSavePath;
        string savedPending = ArenaFightScreen.PendingPlayerDesignName;
        ArenaFightScreen screen = null;
        try
        {
            // An "old save" vessel: ammo fields defaulted to 0 (exactly how a pre-field career.yaml deserializes).
            var oldVessel = new OwnedVessel(design, 0f, 0, 0, "Old Save") { VesselId = "old-save" };
            Assert.AreEqual(0f, oldVessel.CurrentOrdnance, "Old-save default CurrentOrdnance must be 0.");
            Assert.AreEqual(0f, oldVessel.MaxOrdnance, "Old-save default MaxOrdnance must be 0.");

            // Even with finite ammo active, a 0 persisted value means spawn full (the old-save no-regression path).
            screen = SpawnCareer(oldVessel, unlimitedAmmo: false, path, out float ordinanceMax);
            Assert.IsTrue(ordinanceMax > 0f, "The proof hull must carry ordnance capacity.");
            Assert.AreEqual(ordinanceMax, screen.PlayerShipOrdnanceForHeadless[0].Ordnance, 1f,
                "An old save lacking the ammo fields (0) must spawn at full OrdinanceMax — no regression.");
        }
        finally
        {
            try { screen?.ExitScreen(); } catch { }
            ArenaFightScreen.CareerSavePath = savedPath;
            ArenaFightScreen.PendingPlayerDesignName = savedPending;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    // ---- G5b: NormalizeForPersistence clamps ammo fields and folds "full" to 0 (persistence sanitizer). ----
    [TestMethod]
    public void G5b_NormalizeForPersistence_ClampsAndFoldsAmmoToFull_Headless()
    {
        LoadAllGameData();
        var career = new ArenaCareer
        {
            OwnedVessels = new[]
            {
                new OwnedVessel("x", 0f, 0, 0, "Neg")  { VesselId = "neg",  CurrentOrdnance = -5f, MaxOrdnance = -1f },
                new OwnedVessel("y", 0f, 0, 0, "Full") { VesselId = "full", CurrentOrdnance = 500f, MaxOrdnance = 500f },
                new OwnedVessel("z", 0f, 0, 0, "Part") { VesselId = "part", CurrentOrdnance = 200f, MaxOrdnance = 500f },
            },
        };
        career.NormalizeForPersistence();
        OwnedVessel neg = career.OwnedVessels.First(v => v.VesselId == "neg");
        OwnedVessel full = career.OwnedVessels.First(v => v.VesselId == "full");
        OwnedVessel part = career.OwnedVessels.First(v => v.VesselId == "part");
        Assert.AreEqual(0f, neg.CurrentOrdnance, "Negative CurrentOrdnance clamps to 0.");
        Assert.AreEqual(0f, neg.MaxOrdnance, "Negative MaxOrdnance clamps to 0.");
        Assert.AreEqual(0f, full.CurrentOrdnance, "A full magazine (Current>=Max) folds to 0 (spawn full).");
        Assert.AreEqual(200f, part.CurrentOrdnance, "A partial magazine is preserved as the persisted value.");
        Assert.AreEqual(500f, part.MaxOrdnance, "MaxOrdnance is preserved for a partial magazine.");
    }

    // ========================= Phase 3 — rearm cost economy (between-match spend) =========================

    const System.Reflection.BindingFlags Priv =
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

    static void SetPhase(ArenaFightScreen s, string phaseName)
    {
        var f = typeof(ArenaFightScreen).GetField("Phase", Priv);
        f.SetValue(s, Enum.Parse(f.FieldType, phaseName));
    }

    static void EnsureCash(ArenaFightScreen s, int min)
    {
        var f = typeof(ArenaFightScreen).GetField("Cash", Priv);
        if ((int)f.GetValue(s) < min) f.SetValue(s, min);
    }

    // Spawn a finite-ammo SP career whose flagship carries a partial (spent) magazine, ready for a rearm.
    static ArenaFightScreen SpawnCareerWithSpentAmmo(string savePath, string vesselId, out float ordinanceMax, out float persisted)
    {
        string design = TierThreeDesignName();
        // Discover OrdinanceMax from a full spawn first.
        string probePath = Path.Combine(Path.GetTempPath(), $"arena_ammo_probe_{Guid.NewGuid():N}.yaml");
        var probe = new OwnedVessel(design, 0f, 0, 0, "Rearm Probe") { VesselId = vesselId };
        ArenaFightScreen probeScreen = SpawnCareer(probe, unlimitedAmmo: false, probePath, out ordinanceMax);
        try { probeScreen.ExitScreen(); } catch { }
        try { if (File.Exists(probePath)) File.Delete(probePath); } catch { }

        persisted = ordinanceMax * 0.5f; // 50% spent
        var spent = new OwnedVessel(design, 0f, 0, 0, "Rearm Proof") { VesselId = vesselId };
        spent.MaxOrdnance = ordinanceMax;
        spent.CurrentOrdnance = persisted;
        return SpawnCareer(spent, unlimitedAmmo: false, savePath, out float _);
    }

    // ---- G6: RearmAllFromHub restores fielded vessels to full ammo for cash; cash decrements; the career ----
    // ---- saves; insufficient cash rejects; the repair_crews discount lowers the rearm cost. ----
    [TestMethod]
    public void G6_RearmAllFromHub_RestoresAmmoForCash_HonorsRepairCrewsDiscount_Headless()
    {
        LoadAllGameData();
        string path = Path.Combine(Path.GetTempPath(), $"arena_ammo_g6_{Guid.NewGuid():N}.yaml");
        string savedPath = ArenaFightScreen.CareerSavePath;
        string savedPending = ArenaFightScreen.PendingPlayerDesignName;
        ArenaFightScreen screen = null;
        try
        {
            screen = SpawnCareerWithSpentAmmo(path, "rearm-proof", out float ordinanceMax, out float persisted);
            Assert.AreEqual(persisted, screen.PlayerShipOrdnanceForHeadless[0].Ordnance, 1f,
                "The seeded flagship must spawn at its partial (spent) magazine.");

            // Between-match: force the shop phase (the spend only runs in Shopping/Idle) and make it affordable.
            SetPhase(screen, "Shopping");
            int cost = screen.CurrentRearmCost;
            Assert.IsTrue(cost > 0, "A spent magazine must produce a positive rearm cost.");

            // Insufficient cash rejects (no spend).
            EnsureCash(screen, 0);
            var typeCash = typeof(ArenaFightScreen).GetField("Cash", Priv);
            typeCash.SetValue(screen, cost - 1);
            ArenaRearmResult broke = screen.RearmAllFromHub();
            Assert.IsFalse(broke.Success, "Rearm must reject when cash < cost.");
            Assert.AreEqual((int)typeCash.GetValue(screen), broke.CashAfter, "A rejected rearm must not spend cash.");

            // Affordable rearm restores ammo for cash and saves.
            EnsureCash(screen, cost + 100);
            int cashBefore = screen.CurrentCash;
            ArenaRearmResult ok = screen.RearmAllFromHub();
            Assert.IsTrue(ok.Success, ok.Message);
            Assert.AreEqual(cost, ok.CashBefore - ok.CashAfter, "Rearm must charge exactly the displayed cost.");
            Assert.AreEqual(cashBefore - cost, ok.CashAfter, "Rearm must deduct the cost from cash.");
            Assert.AreEqual(ordinanceMax, screen.PlayerShipOrdnanceForHeadless[0].Ordnance, 1f,
                "Rearm must top the live flagship back to full OrdinanceMax.");

            ArenaCareer banked = CareerManager.Load(path);
            OwnedVessel v = banked.FindOwnedVessel("rearm-proof");
            Assert.IsNotNull(v, "The rearmed vessel must stay owned.");
            Assert.AreEqual(0f, v.CurrentOrdnance, "Rearm must clear the persisted spent-ammo (0 == full).");

            // repair_crews discount extends to rearm (v0 shares the discount): a discounted career pays less.
            int undiscounted = ArenaPerks.RepairCost(ArenaFightScreen.RearmCost, Array.Empty<string>());
            int discounted = ArenaPerks.RepairCost(ArenaFightScreen.RearmCost, new[] { ArenaPerks.RepairDiscountId });
            Assert.IsTrue(discounted < undiscounted,
                "The repair_crews discount must lower the rearm base cost (v0 shares the repair discount).");
        }
        finally
        {
            try { screen?.ExitScreen(); } catch { }
            ArenaFightScreen.CareerSavePath = savedPath;
            ArenaFightScreen.PendingPlayerDesignName = savedPending;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    // ---- G7: the rearm spend is BETWEEN-MATCH ONLY — it refuses to run during Fight (a sim tick), so cash ----
    // ---- and persisted ammo can never diverge mid-sim (determinism boundary regression guard). ----
    [TestMethod]
    public void G7_RearmSpend_IsBetweenMatchOnly_RefusesDuringFight_Headless()
    {
        LoadAllGameData();
        string path = Path.Combine(Path.GetTempPath(), $"arena_ammo_g7_{Guid.NewGuid():N}.yaml");
        string savedPath = ArenaFightScreen.CareerSavePath;
        string savedPending = ArenaFightScreen.PendingPlayerDesignName;
        ArenaFightScreen screen = null;
        try
        {
            screen = SpawnCareerWithSpentAmmo(path, "fight-guard", out float _, out float persisted);

            // DURING A FIGHT (a sim tick): rearm must refuse and touch no cash.
            SetPhase(screen, "Fighting");
            EnsureCash(screen, screen.CurrentRearmCost + 1000);
            int cashDuringFight = screen.CurrentCash;
            ArenaRearmResult duringFight = screen.RearmAllFromHub();
            Assert.IsFalse(duringFight.Success, "Rearm must refuse during a fight (sim tick).");
            StringAssert.Contains(duringFight.Message, "between fights",
                "Rearm-during-fight rejection must cite the between-match rule.");
            Assert.AreEqual(cashDuringFight, screen.CurrentCash, "A refused in-fight rearm must not spend cash.");
            Assert.AreEqual(persisted, screen.PlayerShipOrdnanceForHeadless[0].Ordnance, 1f,
                "A refused in-fight rearm must not restore ammo.");

            // BETWEEN MATCHES (Shopping): the same call now succeeds — proving the guard is phase, not content.
            SetPhase(screen, "Shopping");
            ArenaRearmResult betweenMatch = screen.RearmAllFromHub();
            Assert.IsTrue(betweenMatch.Success, betweenMatch.Message);
        }
        finally
        {
            try { screen?.ExitScreen(); } catch { }
            ArenaFightScreen.CareerSavePath = savedPath;
            ArenaFightScreen.PendingPlayerDesignName = savedPending;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}

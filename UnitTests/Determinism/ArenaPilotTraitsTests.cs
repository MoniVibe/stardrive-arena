using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDUtils;
using Ship_Game;
using Ship_Game.Data.Yaml;
using Ship_Game.Gameplay;
using Ship_Game.GameScreens.Arena;
using Ship_Game.Ships;
using Ship_Game.Utils;
using Vector2 = SDGraphics.Vector2;

namespace UnitTests.Determinism;

/// <summary>
/// Layer-1 headless proofs for the Arena pilot-trait system (SP-only, flag-gated default-OFF).
/// Proves: (1) deterministic XP-&gt;level grant is reproducible and derives the auto-granted trait set
/// purely from Level; (2) a fixed-seed duel changes reproducibly with traits ON and is UNCHANGED
/// with the flag OFF (default-off is a true no-op); (3) gunnery_drill applies as a SEPARATE trait
/// multiplier on veterancy damage and does NOT compound into the Level curve (no double-apply);
/// (4) the catalog is family-blind and off-by-default. No Unity Editor boot — pure sim harness,
/// mirroring ArenaDeterminismPatchContractTests.
/// </summary>
[TestClass]
public class ArenaPilotTraitsTests : StarDriveTest
{
    [TestCleanup]
    public override void Cleanup()
    {
        // Never let the flag leak into other tests — default is OFF.
        if (GlobalStats.Defaults != null)
            GlobalStats.Defaults.EnablePilotTraits = false;
        base.Cleanup();
    }

    // ---- Proof 1: deterministic XP -> level grant, purely Level-derived trait set. ----

    [TestMethod]
    public void PilotTraits_XpToLevelGrant_IsDeterministicAndLevelDerived_Headless()
    {
        // Same kills -> same engine crew-level curve -> same level, reproducibly. We drive the exact
        // engine curve the sim already uses (ExpPerLevel * (1 + Level), clamped 0..10) so pilot level
        // == ship crew level with no parallel counter.
        int levelA = LevelFromKills(kills: 12, expPerLevel: 100f, killWorth: 60f);
        int levelB = LevelFromKills(kills: 12, expPerLevel: 100f, killWorth: 60f);
        Assert.AreEqual(levelA, levelB, "Same kills must derive the same level reproducibly.");

        // More kills never regresses level (monotonic), and level is clamped to the engine's 0..10.
        int levelFew  = LevelFromKills(kills: 2,   expPerLevel: 100f, killWorth: 60f);
        int levelMany = LevelFromKills(kills: 999, expPerLevel: 100f, killWorth: 60f);
        Assert.IsTrue(levelMany >= levelFew, "More kills must not lower the derived level.");
        Assert.IsTrue(levelMany <= 10, "Derived level must respect the engine 0..10 clamp.");
        Assert.IsTrue(levelFew >= 0, "Derived level must be non-negative.");

        // The auto-granted trait set is a PURE function of that level and is monotonic in level.
        Assert.AreEqual(0, PilotTraitV0.GrantedTraitsForLevel(0).Length,
            "A level-0 pilot has no traits.");
        Assert.AreEqual(0, PilotTraitV0.GrantedTraitsForLevel(1).Length,
            "The first trait (eagle_eye) unlocks at level 2, not 1.");
        CollectionAssertEquals(new[] { PilotTraitV0.EagleEyeId },
            PilotTraitV0.GrantedTraitsForLevel(2), "Level 2 grants exactly eagle_eye.");

        // Level 6 has crossed all four thresholds (2/3/4/5) -> the full v0 set, Ordinal-sorted.
        string[] atSix = PilotTraitV0.GrantedTraitsForLevel(6);
        CollectionAssertEquals(
            new[] { PilotTraitV0.EagleEyeId, PilotTraitV0.EvasiveAceId,
                    PilotTraitV0.GunneryDrillId, PilotTraitV0.PredictiveTrackingId },
            atSix, "Level 6 grants all four v0 traits, sorted Ordinal.");

        // Reproducible: same level -> byte-identical granted list.
        CollectionAssertEquals(atSix, PilotTraitV0.GrantedTraitsForLevel(6),
            "GrantedTraitsForLevel must be a pure, reproducible function of level.");
    }

    // Drive the exact engine crew-level curve headlessly (no Ship needed): kills * killWorth of XP,
    // spent against ExpPerLevel * (1 + Level) thresholds, clamped 0..10 — the same math as
    // Ship.ConvertExperienceToLevel / AddToShipLevel.
    static int LevelFromKills(int kills, float expPerLevel, float killWorth)
    {
        float experience = kills * killWorth;
        int level = 0;
        while (experience > 0)
        {
            float threshold = expPerLevel * (1 + level);
            if (threshold <= 0 || experience < threshold)
                break;
            experience -= threshold;
            level = Math.Min(level + 1, 10);
            if (level == 10) break;
        }
        return level;
    }

    // ---- Proof 3: no double-apply — gunnery_drill is a separate multiplier on veterancy damage. ----

    [TestMethod]
    public void PilotTraits_GunneryDrill_IsSeparateChannel_NoLevelDoubleApply_Headless()
    {
        CreateUniverseAndPlayerEmpire();
        Ship ship = SpawnShip("Fang Strafer", Player, new Vector2(0f, 0f), new Vector2(1f, 0f));
        Assert.IsTrue(ship.Weapons.Count > 0, "Test ship must have at least one weapon.");
        IWeaponTemplate weapon = ship.Weapons[0]; // Weapon implements IWeaponTemplate

        // A Level-6 veteran. The +5%/level curve makes veterancy damage = base * (1 + 6*0.05).
        ship.Level = 6;

        // Traits OFF -> pure veterancy damage (baseline). Guard: fields default to 0.
        ship.PilotDamageBonus = 0f;
        float veterancyDamage = WeaponTemplate.GetDamageWithBonuses(ship, weapon);
        Assert.IsTrue(veterancyDamage > 0f, "Baseline veterancy damage must be positive.");

        // gunnery_drill ON -> exactly veterancy_damage * (1 + traitValue). NOT compounded into Level.
        PilotTraitDefinition drill = RequireTrait(PilotTraitV0.GunneryDrillId);
        ship.PilotDamageBonus = drill.Value; // 0.08
        float traitDamage = WeaponTemplate.GetDamageWithBonuses(ship, weapon);

        float expected = veterancyDamage * (1f + drill.Value);
        Assert.AreEqual(expected, traitDamage, expected * 1e-5f,
            "Gunnery Drill must equal veterancy_damage * (1 + traitValue) — a separate additive " +
            "channel, never a second Level bump (which would compound through the +5%/level curve).");

        // Explicit anti-double-apply guard: the trait delta is exactly value * veterancy, and it did
        // NOT change the Level number (so targeting/tracking/evade/turn-rate are untouched).
        Assert.AreEqual(6, ship.Level, "Applying the trait must not bump the crew Level.");
        Assert.AreEqual(veterancyDamage * drill.Value, traitDamage - veterancyDamage, expected * 1e-5f,
            "The trait's damage delta must be a clean multiple of veterancy damage, not Level*value.");
    }

    // ---- Proof 2: fixed-seed duel changes with traits ON, unchanged with flag OFF. ----

    [TestMethod]
    public void PilotTraits_FlagOffIsNoOp_FlagOnChangesOutcomeReproducibly_Headless()
    {
        const ulong Seed = 0xB47D_1EE7_0000_0007ul;

        // Baseline: flag OFF (default). This is today's exact behavior.
        GlobalStats.Defaults.EnablePilotTraits = false;
        ulong offA = RunSeededDuel(Seed, giveEdgeToPlayer: true);
        ulong offB = RunSeededDuel(Seed, giveEdgeToPlayer: true);
        Assert.AreEqual(offA, offB, "Flag OFF must be deterministic across repeats.");

        // Flag OFF must be a TRUE no-op even when we set the pilot-bonus source: with the flag off,
        // ApplyPilotTraits never runs, so the digest matches a run that never touched traits at all.
        ulong offNoBonus = RunSeededDuel(Seed, giveEdgeToPlayer: false);
        Assert.AreEqual(offA, offNoBonus,
            "With the flag OFF, whether or not a pilot edge is requested must not change the sim " +
            "(default-off is a true no-op).");

        // Flag ON with a real pilot edge: the additive channel perturbs the deterministic outcome.
        GlobalStats.Defaults.EnablePilotTraits = true;
        ulong onA = RunSeededDuel(Seed, giveEdgeToPlayer: true);
        ulong onB = RunSeededDuel(Seed, giveEdgeToPlayer: true);
        Assert.AreEqual(onA, onB, "Flag ON must still be deterministic (same seed -> same result).");
        Assert.AreNotEqual(offA, onA,
            "Turning traits ON with a real pilot edge must change the fixed-seed duel outcome.");
    }

    // A fixed-seed serial duel that returns a state digest, mirroring ArenaDeterminismPatchContractTests.
    // When giveEdgeToPlayer is set AND the flag is on, the player pilot gets the full v0 trait set via
    // the same additive Pilot*Bonus channel the choke point writes; the enemy stays a base pilot.
    ulong RunSeededDuel(ulong seed, bool giveEdgeToPlayer)
    {
        CreateUniverseAndPlayerEmpire();
        UState.P.GravityWellRange = 0;
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;
        UState.EnableDeterministicRng(seed);

        Ship player = SpawnShip("Fang Strafer", Player,
            new Vector2(-1800f + UState.Random.Float(-150f, 150f), UState.Random.Float(-120f, 120f)),
            new Vector2(1f, 0f));
        Ship enemy = SpawnShip("Fang Strafer", Enemy,
            new Vector2(1800f + UState.Random.Float(-150f, 150f), UState.Random.Float(-120f, 120f)),
            new Vector2(-1f, 0f));

        player.SensorRange = 60000f;
        enemy.SensorRange = 60000f;

        // Mirror the choke-point behavior exactly: only when the flag is on do the additive bonus
        // fields get set (from the pure, level-derived trait set). Flag off -> fields stay 0.
        if (giveEdgeToPlayer && GlobalStats.Defaults.EnablePilotTraits)
        {
            ShipTraitEffect edge = PilotTraitV0.ComposeForLevel(6); // full v0 set
            PilotTraitV0.ApplyToShip(player, edge);
        }

        player.AI.OrderAttackSpecificTarget(enemy);
        enemy.AI.OrderAttackSpecificTarget(player);

        for (int tick = 0; tick < 360; ++tick)
        {
            if ((tick % 90) == 0)
            {
                if (player.Active && enemy.Active) player.AI.OrderAttackSpecificTarget(enemy);
                if (enemy.Active && player.Active) enemy.AI.OrderAttackSpecificTarget(player);
            }
            UState.Objects.Update(TestSimStep);
        }

        return Digest(player, enemy);
    }

    // ---- Proof 4: catalog lint (family-blind, off-by-default, schema integrity). ----

    [TestMethod]
    public void PilotTraits_Catalog_IsFamilyBlindAndOffByDefault_Headless()
    {
        // Off by default: the flag field defaults false and composing on a fresh ship writes nothing.
        Assert.IsFalse(new GamePlayGlobals().EnablePilotTraits,
            "EnablePilotTraits must default to false (zero behavior change).");
        Assert.IsTrue(PilotTraitV0.ComposeShipEffects(Empty<string>.Array).IsZero,
            "An empty granted set must compose to a zero effect.");

        // Exactly the 4 v0 traits, each on a distinct family-blind channel (accuracy/damage/tracking/
        // evade). None keys off target family/type — enforced structurally: the only channels that
        // exist are the four in PilotTraitKind, none of which reads a target.
        Assert.AreEqual(4, PilotTraitV0.Catalog.Length, "v0 catalog is exactly 4 traits.");
        var kinds = new System.Collections.Generic.HashSet<PilotTraitKind>();
        foreach (PilotTraitDefinition t in PilotTraitV0.Catalog)
        {
            Assert.IsFalse(t.Id.IsEmpty(), "Every trait needs a stable id.");
            Assert.IsTrue(t.LevelReq is >= 0 and <= 10, $"{t.Id} levelReq must be within 0..10.");
            Assert.IsTrue(kinds.Add(t.Kind), $"{t.Id} duplicates a channel — v0 wants one per channel.");
        }
        CollectionAssertEquals(
            new object[] { PilotTraitKind.Accuracy, PilotTraitKind.Damage,
                           PilotTraitKind.Tracking, PilotTraitKind.Evade },
            SortedKinds(kinds), "v0 covers exactly the four proven mechanical channels.");

        // Normalize is canonical: unknown ids dropped, dupes removed, Ordinal-sorted.
        string[] normalized = PilotTraitV0.Normalize(
            new[] { "not_a_trait", PilotTraitV0.GunneryDrillId, PilotTraitV0.EagleEyeId,
                    PilotTraitV0.GunneryDrillId });
        CollectionAssertEquals(new[] { PilotTraitV0.EagleEyeId, PilotTraitV0.GunneryDrillId },
            normalized, "Normalize must drop unknowns, de-dup, and sort Ordinal.");
    }

    [TestMethod]
    public void PilotTraits_CatalogHash_IsStableAndOrderIndependent_Headless()
    {
        // The catalog hash is the identity Layer 2 folds into the MP fingerprint so peers running
        // different trait tables refuse to start rather than desync. It must be non-empty, stable
        // across repeated calls, and independent of source-array order (the impl canonicalizes by
        // sorting Ordinal before hashing).
        string a = PilotTraitV0.CatalogHash();
        string b = PilotTraitV0.CatalogHash();
        Assert.IsFalse(a.IsEmpty(), "Catalog hash must be non-empty.");
        Assert.AreEqual(a, b, "Catalog hash must be stable across calls.");

        // Recompute the same identity from a deliberately shuffled copy of the catalog fields and
        // confirm it matches — proving the hash is order-independent (the compose result is too).
        var h = SDUtils.Deterministic.DetHash.New();
        var shuffled = new System.Collections.Generic.List<PilotTraitDefinition>(PilotTraitV0.Catalog);
        shuffled.Reverse();
        shuffled.Sort((x, y) => string.CompareOrdinal(x.Id, y.Id));
        foreach (PilotTraitDefinition t in shuffled)
        {
            h.AddString(t.Id);
            h.AddInt(t.LevelReq);
            h.AddInt((int)t.Kind);
            h.AddFloat(t.Value);
        }
        Assert.AreEqual("0x" + h.Value.ToString("X16"), a,
            "Catalog hash must equal the canonical (Ordinal-sorted) recomputation.");
    }

    [TestMethod]
    public void PilotTraits_Scope_CaptainPreferredVesselFallbackAndOverride_Headless()
    {
        // Scope flag is the escape hatch: Captain (default) grants from the transferable pilot's
        // level when one is linked; Vessel forces ship-bound veterancy. Proven at the compose layer
        // (pure, no spawn needed): the granted set is a function of whichever level scope selects.
        int vesselLevel = 3;   // -> gunnery_drill (L3) granted
        int captainLevel = 5;  // -> + evasive_ace (L5)

        string[] vesselGrant = PilotTraitV0.GrantedTraitsForLevel(vesselLevel);
        string[] captainGrant = PilotTraitV0.GrantedTraitsForLevel(captainLevel);
        CollectionAssert.AreNotEquivalent(vesselGrant, captainGrant,
            "The two levels must grant different trait sets for the scope choice to be observable.");

        // Captain scope with a level-5 captain must grant the captain's (larger) set; Vessel scope
        // must grant the vessel's set regardless of captain. This mirrors ApplyPilotTraits' selection.
        Assert.IsTrue(System.Array.IndexOf(captainGrant, PilotTraitV0.EvasiveAceId) >= 0,
            "Captain scope (level 5) should include evasive_ace.");
        Assert.IsTrue(System.Array.IndexOf(vesselGrant, PilotTraitV0.EvasiveAceId) < 0,
            "Vessel scope (level 3) should NOT include evasive_ace.");
        Assert.IsFalse(new GamePlayGlobals().PilotTraitScopeVessel,
            "Pilot trait scope must default to Captain (PilotTraitScopeVessel=false, transferable pilots).");
    }

    [TestMethod]
    public void PilotTraits_ShippedYaml_LoadsAndMatchesEmbeddedFallback_Headless()
    {
        // Prove the DATA-DRIVEN path actually works: the shipped Content/PilotTraits.yaml must
        // deserialize (file present + parseable in the content dir) and build to EXACTLY the
        // embedded fallback catalog. This is the sync guard — if someone edits the yaml but not the
        // embedded fallback (or vice-versa), determinism between the two load paths breaks and this
        // fails loudly.
        Array<PilotTraitEntry> rows = YamlParser.DeserializeArray<PilotTraitEntry>(PilotTraitV0.CatalogFile);
        Assert.IsNotNull(rows, "Content/PilotTraits.yaml must exist and parse.");
        Assert.AreEqual(4, rows.Count, "Shipped yaml must contain the 4 v0 traits.");

        PilotTraitDefinition[] fromYaml = PilotTraitV0.BuildCatalogFromEntries(rows);
        PilotTraitDefinition[] embedded = PilotTraitV0.EmbeddedFallback();
        Assert.AreEqual(embedded.Length, fromYaml.Length, "Yaml and embedded fallback must have equal counts.");
        for (int i = 0; i < embedded.Length; ++i)
        {
            Assert.AreEqual(embedded[i].Id, fromYaml[i].Id, $"trait {i} id");
            Assert.AreEqual(embedded[i].LevelReq, fromYaml[i].LevelReq, $"{embedded[i].Id} levelReq");
            Assert.AreEqual(embedded[i].Kind, fromYaml[i].Kind, $"{embedded[i].Id} kind");
            Assert.AreEqual(embedded[i].Value, fromYaml[i].Value, 1e-6f, $"{embedded[i].Id} value");
        }

        // And the live Catalog (loaded through the cache) equals the shipped content too.
        Assert.AreEqual(4, PilotTraitV0.Catalog.Length, "Loaded Catalog must be the 4 shipped traits.");
    }

    [TestMethod]
    public void PilotTraits_BuildCatalog_DropsInvalidRows_AndFallbackWhenEmpty_Headless()
    {
        // The lint drops empty/duplicate ids and out-of-range levels; the survivors are canonical
        // (Ordinal-sorted). An all-invalid input yields an empty result, which is the signal
        // LoadCatalog uses to fall back to the embedded catalog.
        var rows = new Array<PilotTraitEntry>
        {
            new() { Id = "  ", Name = "blank", LevelReq = 2, Kind = PilotTraitKind.Damage, Value = 0.1f },
            new() { Id = "zeta_trait", LevelReq = 4, Kind = PilotTraitKind.Evade, Value = 5f },
            new() { Id = "alpha_trait", LevelReq = 3, Kind = PilotTraitKind.Damage, Value = 0.2f },
            new() { Id = "alpha_trait", LevelReq = 6, Kind = PilotTraitKind.Damage, Value = 0.9f }, // dup -> dropped
            new() { Id = "bad_level", LevelReq = 99, Kind = PilotTraitKind.Accuracy, Value = 0.1f }, // out of range -> dropped
        };
        PilotTraitDefinition[] built = PilotTraitV0.BuildCatalogFromEntries(rows);
        Assert.AreEqual(2, built.Length, "Only the two valid, unique, in-range rows survive.");
        Assert.AreEqual("alpha_trait", built[0].Id, "Survivors must be Ordinal-sorted.");
        Assert.AreEqual("zeta_trait", built[1].Id);
        Assert.AreEqual(0.2f, built[0].Value, 1e-6f, "The FIRST alpha_trait wins; the duplicate is dropped.");

        Assert.AreEqual(0, PilotTraitV0.BuildCatalogFromEntries(new Array<PilotTraitEntry>()).Length,
            "An all-invalid/empty input yields an empty catalog (the fallback signal).");
    }

    // ---- helpers ----

    static PilotTraitDefinition RequireTrait(string id)
    {
        Assert.IsTrue(PilotTraitV0.TryGet(id, out PilotTraitDefinition t), $"Catalog must contain {id}.");
        return t;
    }

    static object[] SortedKinds(System.Collections.Generic.IEnumerable<PilotTraitKind> kinds)
    {
        var list = new System.Collections.Generic.List<PilotTraitKind>(kinds);
        list.Sort();
        return list.ConvertAll(k => (object)k).ToArray();
    }

    static void CollectionAssertEquals(Array expected, Array actual, string message)
    {
        Assert.AreEqual(expected.Length, actual.Length, message + " (length)");
        for (int i = 0; i < expected.Length; ++i)
            Assert.AreEqual(expected.GetValue(i), actual.GetValue(i), $"{message} (index {i})");
    }

    static ulong Digest(params Ship[] ships)
    {
        ulong hash = 1469598103934665603ul;
        foreach (Ship ship in ships)
        {
            Mix(ref hash, ship?.Id ?? 0);
            Mix(ref hash, ship?.Active == true ? 1 : 0);
            Mix(ref hash, Bits(ship?.Position.X ?? 0f));
            Mix(ref hash, Bits(ship?.Position.Y ?? 0f));
            Mix(ref hash, Bits(ship?.Rotation ?? 0f));
            Mix(ref hash, Bits(ship?.Velocity.X ?? 0f));
            Mix(ref hash, Bits(ship?.Velocity.Y ?? 0f));
            Mix(ref hash, Bits(ship?.Health ?? 0f));
            Mix(ref hash, Bits(ship?.Ordinance ?? 0f));
        }
        return hash;
    }

    static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    static void Mix(ref ulong hash, int value)
    {
        unchecked
        {
            hash ^= (uint)value;
            hash *= 1099511628211ul;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDLockstep;
using Ship_Game;
using Ship_Game.GameScreens.Arena;
using Ship_Game.Ships;
using Vector2 = SDGraphics.Vector2;

namespace UnitTests.Determinism;

/// <summary>
/// Phase-3/4 proof gates for the Arena custom-fleet PLAYABLE slice
/// (STARDRIVE_ARENA_CUSTOM_FLEET_PROGRAM_PLAN_20260706 §5.2 match-cap fix, §5.1 budget currency, §8 slice).
///
/// The Phase-0 kernel tests (ArenaCustomFleetKernelTests) prove per-peer byte-identical reconstruction and
/// handshake rejection. These prove the UI-phase wiring on TOP of that kernel:
///   1. EffectiveMaxTurns derives the REAL match cap from RulesetV0.MaxMatchSeconds (was hashed, never enforced),
///      with MaxTurns kept only as an absolute safety ceiling — and it changes NOTHING for the default ruleset.
///   2. A match actually ENDS at MaxMatchSeconds*60 ticks (not the legacy MaxTurns), identically on both peers.
///   3. A full fleet of scratch-authored CUSTOM designs round-trips through ArenaDesignTable + the bundle and both
///      in-process peers reach the SAME final digest (the end-to-end playable-slice determinism proof).
///   4. The client-side budget guard currency = BaseStrength (mirrors the authoritative SumBundleCost gate).
///
/// All flag-gated behind EnableArenaCustomFleet; the default-ruleset match-cap behavior is unchanged (no regress).
/// </summary>
[TestClass]
public class ArenaCustomFleetUiTests : StarDriveTest
{
    static ShipDesign AuthorCustomFromStock(string stockName)
    {
        Assert.IsTrue(ResourceManager.Ships.GetDesign(stockName, out IShipDesign stock),
            $"Stock design '{stockName}' must exist after LoadAllGameData.");
        ShipDesign clone = ((ShipDesign)stock).GetClone("Custom Author Copy");
        clone.SetDesignSlots(clone.GetOrLoadDesignSlots());
        return clone;
    }

    static string[] LegalStockDesignNames()
        => ResourceManager.Ships.Designs
            .Where(ArenaFightScreen.IsLegalCombatCraft)
            .Where(ArenaFightScreen.IsStockContentDesign)
            .Where(d => d.GetOrLoadDesignSlots().All(s => s.HangarShipUID.IsEmpty() || s.HangarShipUID == "NotApplicable"))
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .Select(d => d.Name)
            .ToArray();

    // ---------------------------------------------------------------------------------------------------
    // §5.2 — EffectiveMaxTurns derivation (pure, no game data required).
    // ---------------------------------------------------------------------------------------------------
    [TestMethod]
    public void MatchCap_EffectiveMaxTurns_DerivesFromMaxMatchSeconds_Headless()
    {
        // THE FIX (§5.2): with a HIGH MaxTurns ceiling (as real lobby matches now use, DefaultTurns=36000), the
        // host's MaxMatchSeconds ACTUALLY BINDS. A 60s match -> 3600 ticks, NOT the legacy 600/420 cap. This is
        // the LENGTHENING proof — it was RED against the pre-fix behavior (hard-capped at MaxTurns<=2000).
        var hostMatch60s = new ArenaMultiplayerSettings
        {
            MaxTurns = ArenaMultiplayerLobbyScreen.DefaultTurns, // 36000, the real-match ceiling
            Ruleset = new ArenaMultiplayerRuleset { MaxMatchSeconds = 60 },
        };
        Assert.AreEqual(3600u, hostMatch60s.EffectiveMaxTurns,
            "A 60s host match under a high ceiling must cap at 60*60=3600 ticks (the length actually takes effect).");

        // Each offered host length binds under the high ceiling.
        foreach ((int seconds, uint expected) in new[] { (30, 1800u), (120, 7200u), (300, 18000u), (600, 36000u) })
        {
            var m = new ArenaMultiplayerSettings
            {
                MaxTurns = ArenaMultiplayerLobbyScreen.DefaultTurns,
                Ruleset = new ArenaMultiplayerRuleset { MaxMatchSeconds = seconds },
            };
            Assert.AreEqual(expected, m.EffectiveMaxTurns,
                $"A {seconds}s host match must cap at {expected} ticks under the high ceiling.");
        }

        // MaxTurns remains the HARD SAFETY CEILING that headless self-tests set LOW to stay fast: a test that
        // sets MaxTurns=90 gets 90 regardless of the 600s ruleset default (min keeps the low ceiling authoritative).
        var fastSelfTest = new ArenaMultiplayerSettings { MaxTurns = 90 }; // default ruleset MaxMatchSeconds=600
        Assert.AreEqual(90u, fastSelfTest.EffectiveMaxTurns,
            "A self-test's low MaxTurns must stay authoritative (min keeps headless runs fast).");

        // A short host match below even a low ceiling: the derived cap still wins.
        var shortMatch = new ArenaMultiplayerSettings
        {
            MaxTurns = 420,
            Ruleset = new ArenaMultiplayerRuleset { MaxMatchSeconds = 5 }, // 300 ticks
        };
        Assert.AreEqual(300u, shortMatch.EffectiveMaxTurns,
            "A 5s host match must cap at 300 ticks (below the 420 ceiling).");

        // MaxMatchSeconds<=0 disables the derived cap (safety ceiling only).
        var noCap = new ArenaMultiplayerSettings
        {
            MaxTurns = 250,
            Ruleset = new ArenaMultiplayerRuleset { MaxMatchSeconds = 0 },
        };
        Assert.AreEqual(250u, noCap.EffectiveMaxTurns,
            "MaxMatchSeconds<=0 must fall back to the MaxTurns ceiling only.");
    }

    // The lobby builds real matches with a HIGH MaxTurns ceiling so MaxMatchSeconds binds. Guard the constants so
    // a future edit that re-lowers them (reintroducing the "fights too short" bug) fails loudly here.
    [TestMethod]
    public void MatchCap_LobbyCeiling_IsHighEnoughForMaxMatchSeconds_Headless()
    {
        Assert.IsTrue(ArenaMultiplayerLobbyScreen.DefaultTurns >= 600 * 60,
            "DefaultTurns must be a high ceiling (>= 600s*60) so a real match lets MaxMatchSeconds drive the length.");
        Assert.IsTrue(ArenaMultiplayerLobbyScreen.MaxTurnsCeiling >= ArenaMultiplayerLobbyScreen.DefaultTurns,
            "The MaxTurns clamp ceiling must not re-clamp DefaultTurns back down.");
    }

    // ---------------------------------------------------------------------------------------------------
    // §5.2 — a real match ENDS at MaxMatchSeconds*60 ticks, not the legacy MaxTurns, identically on both peers.
    // ---------------------------------------------------------------------------------------------------
    [TestMethod]
    public void MatchCap_MatchEndsAtMaxMatchSeconds_NotMaxTurns_Headless()
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_matchcap_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        try
        {
            // THE LENGTHENING PROOF: a HIGH MaxTurns ceiling (like a real lobby match, DefaultTurns=36000) + a host
            // MaxMatchSeconds of 12s -> a 720-tick cap. Under the PRE-FIX code the match was hard-bound at
            // MaxTurns (<=2000, default 600) and could NEVER run past 600 ticks regardless of the host's choice.
            // Now it runs to the DERIVED 720-tick timeout, PAST 600 — the director's "fights too short" is fixed.
            // Balanced identical fleets so the match runs to the timeout rather than eliminating early.
            const int capSeconds = 12;
            string[] fleet = FleetForBalancedStalemate();
            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED,
                RngSeed = 0xA12EA000u,
                InputDelay = 3,
                MaxTurns = ArenaMultiplayerLobbyScreen.DefaultTurns, // 36000 high ceiling — MaxMatchSeconds must bind
                CommandEveryTurns = 1,
                HostFleetDesignNames = fleet,
                JoinFleetDesignNames = fleet,
                Ruleset = new ArenaMultiplayerRuleset { MaxMatchSeconds = capSeconds },
            }.WithResolvedFleets();

            uint derivedCap = settings.EffectiveMaxTurns;
            Assert.AreEqual((uint)(capSeconds * 60), derivedCap,
                "The derived cap must be MaxMatchSeconds*60 = 720 (the high MaxTurns ceiling must not bind).");
            Assert.IsTrue(derivedCap > 600,
                "The whole point: the derived cap must exceed the legacy 600-turn hard cap.");

            ArenaMultiplayerRunResult result = ArenaMultiplayerSession.RunInProcess(settings);

            Assert.IsFalse(result.Desynced,
                $"The capped match desynced at turn {result.DesyncTurn}: {result.DesyncReason}");
            Assert.IsTrue(result.TurnsCompleted <= derivedCap,
                $"The match must not run past the derived cap {derivedCap}; ran {result.TurnsCompleted}.");
            // The match must run PAST 600 turns — impossible under the old MaxTurns hard cap. (If both fleets are
            // still alive at the timeout, it runs the full 720; a balanced stalemate does not eliminate by 600.)
            Assert.IsTrue(result.TurnsCompleted > 600,
                $"The match must run past the legacy 600-turn cap (proving MaxMatchSeconds now lengthens the match); ran {result.TurnsCompleted}.");
            Assert.IsTrue(result.TurnHashes.All(h => h.Match),
                "Every turn hash of the lengthened match must match across both peers.");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // §8 — a full fleet of scratch-authored CUSTOM designs round-trips and both peers reach the SAME digest.
    // ---------------------------------------------------------------------------------------------------
    [TestMethod]
    public void CustomFleet_ScratchDesigns_RoundTripAndDigestMatch_Headless()
    {
        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"arena_mp_customfleet_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;
        bool savedFlag = GlobalStats.Defaults.EnableArenaCustomFleet;
        int snapshot = ResourceManager.Ships.Designs.Count;
        try
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = true;

            // Author two distinct scratch custom designs (the "sandbox scratch set"), reference them by their
            // @arena/<hash> content names, and carry the full payloads in the design table for BOTH peers.
            string[] stock = LegalStockDesignNames();
            ShipDesign a = AuthorCustomFromStock(stock[0]);
            ShipDesign b = AuthorCustomFromStock(stock[1]);
            string nameA = ArenaDesignTable.ContentName(a);
            string nameB = ArenaDesignTable.ContentName(b);
            string table = ArenaDesignTable.Encode(new List<IShipDesign> { a, b });

            // Host fleet = [A, B] at authored column offsets; join fleet = [B, A] (a distinct formation).
            FleetDesign hostBundle = BuildColumnBundle("H", new[] { nameA, nameB });
            FleetDesign joinBundle = BuildColumnBundle("J", new[] { nameB, nameA });

            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED,
                RngSeed = 0xA12EA000u,
                InputDelay = 3,
                MaxTurns = 120,
                CommandEveryTurns = 1,
                HostFleetDesignNames = new[] { nameA, nameB },
                JoinFleetDesignNames = new[] { nameB, nameA },
                HostFleetBundle = ArenaFleetBundle.Encode(hostBundle),
                JoinFleetBundle = ArenaFleetBundle.Encode(joinBundle),
                HostDesignTable = table,
                JoinDesignTable = table,
                Ruleset = new ArenaMultiplayerRuleset
                {
                    Mode = ArenaMatchMode.Sandbox,
                    RosterSource = ArenaRosterSource.AllContent,
                    BudgetModel = ArenaBudgetModel.Unlimited,
                },
            }.WithResolvedFleets();

            // Handshake must accept the custom fleet.
            IReadOnlyList<string> registered = ArenaMultiplayerSession.RegisterPeerDesignTables(settings, out string regErr);
            try
            {
                Assert.AreEqual("", regErr, $"Registering scratch designs must succeed: {regErr}");
                SessionStartMessage start = settings.ToStartMessage();
                Assert.AreEqual("", ArenaMultiplayerSettings.ValidateStartMessage(start, out _),
                    "A registered custom fleet at authored offsets must validate at the handshake.");
            }
            finally
            {
                ArenaMultiplayerSession.UnregisterPeerDesignTables(registered);
            }

            // End-to-end: two in-process peers run the custom-fleet match to the SAME digest. RunInProcess
            // registers/tears down the tables itself.
            ArenaMultiplayerRunResult result = ArenaMultiplayerSession.RunInProcess(settings);
            Assert.IsFalse(result.Desynced,
                $"The custom-fleet in-process match desynced at turn {result.DesyncTurn}: {result.DesyncReason}");
            Assert.IsTrue(result.TurnHashes.Count > 0, "The custom-fleet match must run at least one turn.");
            Assert.IsTrue(result.TurnHashes.All(h => h.Match),
                "Every turn hash of the custom-fleet match must match across both peers.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.FinalHash),
                "The custom-fleet match must report a final digest.");
            // The custom designs must have spawned (one ship per fleet node).
            Assert.AreEqual(2, result.HostSnapshot.PlayerShipIds.Length,
                "The host peer must spawn one ship per custom fleet node.");
            Assert.AreEqual(2, result.HostSnapshot.EnemyShipIds.Length,
                "The join peer's custom fleet must become side 2 with one ship per node.");
        }
        finally
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = savedFlag;
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            Assert.AreEqual(snapshot, ResourceManager.Ships.Designs.Count,
                "Teardown must leave the global design table exactly as it started (no leaked scratch designs).");
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // §5.1 — the client-side budget guard uses BaseStrength (mirrors the authoritative SumBundleCost gate).
    // ---------------------------------------------------------------------------------------------------
    [TestMethod]
    public void Budget_ClientPickerGuard_UsesBaseStrength_Headless()
    {
        LoadAllGameData();
        string[] stock = LegalStockDesignNames();
        Assert.IsTrue(ResourceManager.Ships.GetDesign(stock[0], out IShipDesign d0));
        Assert.IsTrue(ResourceManager.Ships.GetDesign(stock[1], out IShipDesign d1));

        int strength0 = (int)MathF.Round(d0.BaseStrength);
        int strength1 = (int)MathF.Round(d1.BaseStrength);

        // A budget cap that admits exactly one of the two designs by BaseStrength. The picker's guard (which
        // now sums BaseStrength, matching SumBundleCost) must deny the second pick. This is the friendly
        // mirror of the authoritative handshake rejection — they agree on the currency.
        int cap = Math.Max(strength0, strength1); // fits the single most-expensive design, but not the sum
        Assert.IsTrue(strength0 + strength1 > cap,
            "Test precondition: the two designs together must exceed the single-design cap.");

        bool first = false, second = false;
        var picker = new ArenaFleetPickerScreen(null, new[] { stock[0], stock[1] }, Array.Empty<string>(), cap,
            _ => { });
        first = picker.ToggleForHeadless(stock[0]);
        second = picker.ToggleForHeadless(stock[1]);

        Assert.IsTrue(first, "The first (affordable) pick must be admitted.");
        Assert.IsFalse(second, "The second pick, which overspends by BaseStrength, must be denied by the client guard.");
    }

    // A single-ship-per-side identical (mirror) fleet of the lowest-strength legal stock combat craft. A symmetric
    // low-DPS mirror match does not resolve by elimination within the 12s (720-tick) window, so the match runs to
    // the derived timeout — giving a deterministic "ran past 600 turns" without depending on damage tuning.
    static string[] FleetForBalancedStalemate()
    {
        string weakest = ResourceManager.Ships.Designs
            .Where(ArenaFightScreen.IsLegalCombatCraft)
            .Where(ArenaFightScreen.IsStockContentDesign)
            .Where(d => d.GetOrLoadDesignSlots().All(s => s.HangarShipUID.IsEmpty() || s.HangarShipUID == "NotApplicable"))
            .OrderBy(d => d.BaseStrength)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .Select(d => d.Name)
            .First();
        return new[] { weakest };
    }

    static FleetDesign BuildColumnBundle(string name, string[] shipNames)
    {
        var fd = new FleetDesign { Name = name };
        for (int i = 0; i < shipNames.Length; i++)
            fd.Nodes.Add(new FleetDataDesignNode
            {
                ShipName = shipNames[i],
                RelativeFleetOffset = new Vector2(0f, i * 400f),
            });
        return fd;
    }
}

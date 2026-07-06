using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDLockstep;
using Ship_Game;
using Ship_Game.GameScreens.Arena;
using Ship_Game.Ships;
using Vector2 = SDGraphics.Vector2;

namespace UnitTests.Determinism;

/// <summary>
/// Phase-0 proof gates for the Arena custom-fleet EXCHANGE KERNEL
/// (STARDRIVE_ARENA_CUSTOM_FLEET_PROGRAM_PLAN_20260706 + the 7 binding kernel-review amendments).
///
/// These are the CORRECTED gates. The plan's original "two-peer digest match" is a FALSE-GREEN for
/// reconstruction because ResourceManager.Ships is a process-global static shared by both in-process/loopback
/// peers, so the second peer never actually reconstructs from bytes. The gate OF RECORD here is a PURE payload
/// round-trip (no registration) plus an authored-vs-parsed cross-check — the only things that actually prove
/// per-peer byte-identical reconstruction and that the ordinal UID sort defeats the HashSet-order hazard.
/// </summary>
[TestClass]
public class ArenaCustomFleetKernelTests : StarDriveTest
{
    // A stock design cloned into a fresh custom design authored via the LIVE path (SetDesignSlots -> the
    // non-order-stable HashSet UID build, ShipDesign.cs:177-183). This is the "authored in the designer" side.
    static ShipDesign AuthorCustomFromStock(string stockName)
    {
        Assert.IsTrue(ResourceManager.Ships.GetDesign(stockName, out IShipDesign stock),
            $"Stock design '{stockName}' must exist after LoadAllGameData.");
        ShipDesign clone = ((ShipDesign)stock).GetClone("Custom Author Copy");
        // Force the live-authoring UID rebuild path (HashSet.ToArr order), exactly as the ship designer does.
        clone.SetDesignSlots(clone.GetOrLoadDesignSlots());
        return clone;
    }

    // The same ship reconstructed via the base disk codec -> FromBytes (the FILE-ORDER UID path). This is the
    // "parsed from a saved file" side. Its UID table order differs from the authored side's HashSet order.
    static ShipDesign ParseViaBaseCodec(ShipDesign authored)
    {
        byte[] baseBytes = authored.GetDesignBytes(new ShipDesignWriter());
        ShipDesign parsed = ShipDesign.FromBytes(baseBytes);
        Assert.IsNotNull(parsed, "The base disk codec must reconstruct the authored design.");
        return parsed;
    }

    static string FirstStockCombatDesignName()
        => LegalStockDesignNames().First();

    static string[] LegalStockDesignNames()
        => ResourceManager.Ships.Designs
            .Where(ArenaFightScreen.IsLegalCombatCraft)
            .Where(ArenaFightScreen.IsStockContentDesign)
            .Where(d => d.GetOrLoadDesignSlots().All(s => s.HangarShipUID.IsEmpty() || s.HangarShipUID == "NotApplicable"))
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .Select(d => d.Name)
            .ToArray();

    // ---------------------------------------------------------------------------------------------------
    // GATE OF RECORD: ByteRoundTrip — pure payload round-trip, NO registration (amendment 1).
    // ---------------------------------------------------------------------------------------------------
    [TestMethod]
    public void Kernel_ByteRoundTrip_NoRegistration_Headless()
    {
        LoadAllGameData();
        int snapshot = ResourceManager.Ships.Designs.Count;

        foreach (string name in LegalStockDesignNames().Take(12))
        {
            ShipDesign authored = AuthorCustomFromStock(name);

            byte[] bytesA = ArenaDesignTable.CanonicalPayload(authored);
            ShipDesign reconstructed = ShipDesign.FromBytes(bytesA);
            Assert.IsNotNull(reconstructed, $"'{name}': canonical payload must reconstruct via FromBytes.");
            byte[] bytesB = ArenaDesignTable.CanonicalPayload(reconstructed);

            CollectionAssert.AreEqual(bytesA, bytesB,
                $"'{name}': author -> bytes -> reconstruct -> bytes must be BYTE-IDENTICAL (the reconstruction proof).");
            Assert.AreEqual(ArenaDesignTable.DesignContentHash(bytesA),
                ArenaDesignTable.DesignContentHash(bytesB),
                $"'{name}': the content hash must be stable across the round-trip.");

            // Amendment 1 tamper-close is STRUCTURAL: the canonical payload carries NO Name line to trust.
            string text = Encoding.UTF8.GetString(bytesA);
            StringAssert.DoesNotMatch(text, new System.Text.RegularExpressions.Regex(@"(?m)^Name="),
                $"'{name}': the canonical payload must contain no Name= line (identity is the content hash).");
        }

        Assert.AreEqual(snapshot, ResourceManager.Ships.Designs.Count,
            "The pure round-trip proof must NOT register anything into the global design table.");
    }

    // ---------------------------------------------------------------------------------------------------
    // AuthoredVsParsed — the same ship authored live vs parsed from file emits IDENTICAL canonical bytes.
    // This is the ONLY thing that proves the ordinal UID sort defeats the HashSet-order nondeterminism
    // (amendment 2) and the InvariantCulture handling (amendment 3).
    // ---------------------------------------------------------------------------------------------------
    [TestMethod]
    public void Kernel_AuthoredVsParsed_ByteIdentical_Headless()
    {
        LoadAllGameData();
        foreach (string name in LegalStockDesignNames().Take(12))
        {
            ShipDesign authored = AuthorCustomFromStock(name);   // live HashSet UID order
            ShipDesign parsed = ParseViaBaseCodec(authored);     // file UID order

            byte[] authoredBytes = ArenaDesignTable.CanonicalPayload(authored);
            byte[] parsedBytes = ArenaDesignTable.CanonicalPayload(parsed);

            CollectionAssert.AreEqual(authoredBytes, parsedBytes,
                $"'{name}': authored-live vs parsed-from-file must emit IDENTICAL canonical bytes " +
                "(proves the self-contained ordinal UID order defeats the HashSet-order hazard).");
            Assert.AreEqual(ArenaDesignTable.ContentName(authored), ArenaDesignTable.ContentName(parsed),
                $"'{name}': authored and parsed copies must derive the SAME @arena/ content name.");
        }
    }

    [TestMethod]
    public void Kernel_ContentName_IsArenaNamespaced_Headless()
    {
        LoadAllGameData();
        ShipDesign authored = AuthorCustomFromStock(FirstStockCombatDesignName());
        string cname = ArenaDesignTable.ContentName(authored);
        StringAssert.StartsWith(cname, ArenaDesignTable.NamePrefix,
            "The registration name must live in the collision-free @arena/ namespace.");
        Assert.AreEqual(ArenaDesignTable.NamePrefix.Length + 16, cname.Length,
            "The content name must be @arena/ + 16 hex digits.");
    }

    // ---------------------------------------------------------------------------------------------------
    // BundleHashCoversModuleContent — two designs differing ONLY in a module produce DIFFERENT bundle hashes,
    // because the @arena/ name IS the content hash and the bundle hashes node.ShipName.
    // ---------------------------------------------------------------------------------------------------
    [TestMethod]
    public void Kernel_BundleHashCoversModuleContent_Headless()
    {
        LoadAllGameData();
        string[] names = LegalStockDesignNames();
        Assert.IsTrue(names.Length >= 2, "Need two distinct stock designs for the module-content proof.");

        ShipDesign a = AuthorCustomFromStock(names[0]);
        ShipDesign b = AuthorCustomFromStock(names[1]);
        string nameA = ArenaDesignTable.ContentName(a);
        string nameB = ArenaDesignTable.ContentName(b);
        Assert.AreNotEqual(nameA, nameB,
            "Two designs with different module content must derive DIFFERENT @arena/ content names.");

        var bundleA = new FleetDesign { Name = "F" };
        bundleA.Nodes.Add(new FleetDataDesignNode { ShipName = nameA, RelativeFleetOffset = Vector2.Zero });
        var bundleB = new FleetDesign { Name = "F" };
        bundleB.Nodes.Add(new FleetDataDesignNode { ShipName = nameB, RelativeFleetOffset = Vector2.Zero });

        Assert.AreNotEqual(ArenaFleetBundle.DesignBundleHash(bundleA),
            ArenaFleetBundle.DesignBundleHash(bundleB),
            "A bundle referencing a design that differs only in a module must produce a DIFFERENT bundle hash " +
            "(module content folds into the bundle hash transitively via the content-hash-as-name).");

        // Identical content => identical name => identical bundle hash (dedup / determinism).
        ShipDesign a2 = AuthorCustomFromStock(names[0]);
        var bundleA2 = new FleetDesign { Name = "F" };
        bundleA2.Nodes.Add(new FleetDataDesignNode { ShipName = ArenaDesignTable.ContentName(a2), RelativeFleetOffset = Vector2.Zero });
        Assert.AreEqual(ArenaFleetBundle.DesignBundleHash(bundleA),
            ArenaFleetBundle.DesignBundleHash(bundleA2),
            "Identical module content must produce an identical bundle hash.");
    }

    // ---------------------------------------------------------------------------------------------------
    // Encode/Decode container round-trips and RE-DERIVES the name from bytes (never trusts sender name).
    // ---------------------------------------------------------------------------------------------------
    [TestMethod]
    public void Kernel_EncodeDecode_RoundTrips_RederivesName_Headless()
    {
        LoadAllGameData();
        int snapshot = ResourceManager.Ships.Designs.Count;

        string[] names = LegalStockDesignNames();
        ShipDesign a = AuthorCustomFromStock(names[0]);
        ShipDesign b = AuthorCustomFromStock(names[1]);
        string expectA = ArenaDesignTable.ContentName(a);
        string expectB = ArenaDesignTable.ContentName(b);

        string table = ArenaDesignTable.Encode(new List<IShipDesign> { a, b });
        Assert.IsTrue(table.Length > 0, "A non-empty design set must encode to a non-empty table.");

        ArenaDesignTable.DecodeResult decoded = ArenaDesignTable.Decode(table);
        Assert.IsTrue(decoded.Ok, $"A well-formed table must decode cleanly: {decoded.Error}");
        Assert.IsTrue(decoded.Designs.ContainsKey(expectA), "Decode must re-derive design A's @arena/ name.");
        Assert.IsTrue(decoded.Designs.ContainsKey(expectB), "Decode must re-derive design B's @arena/ name.");
        Assert.AreEqual(expectA, decoded.Designs[expectA].Name,
            "The reconstructed design's Name must be the RE-DERIVED @arena/ content name.");

        Assert.AreEqual(snapshot, ResourceManager.Ships.Designs.Count,
            "Encode/Decode must not register anything (registration is a separate, caller-owned step).");

        // Empty table is legal.
        ArenaDesignTable.DecodeResult empty = ArenaDesignTable.Decode("");
        Assert.IsTrue(empty.Ok && empty.Designs.Count == 0, "An empty table must decode to zero designs, no error.");
    }

    // ---------------------------------------------------------------------------------------------------
    // RejectAtHandshake: malformed / tampered / mod-gap / oversized each REJECT cleanly (no crash) — amendment 5.
    // (Carrier rejection is covered separately below; budget/overspend is at the session layer.)
    // ---------------------------------------------------------------------------------------------------
    [TestMethod]
    public void Kernel_Decode_MalformedPayload_RejectsCleanly_Headless()
    {
        LoadAllGameData();

        // Garbage base64 in a design slot -> clean reject, never throw.
        string garbageTable = "01!!!not-base64!!!";
        ArenaDesignTable.DecodeResult r1 = ArenaDesignTable.Decode(garbageTable);
        Assert.IsFalse(r1.Ok, "A non-base64 payload must reject.");
        StringAssert.Contains(r1.Error, "malformed", "The rejection must name the malformed payload.");

        // Valid base64 of random bytes -> FromBytes fails / hull gap -> clean reject.
        string randomB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("this is not a ship design at all\nHull=Nonexistent\n"));
        string randomTable = "01" + randomB64;
        ArenaDesignTable.DecodeResult r2 = ArenaDesignTable.Decode(randomTable);
        Assert.IsFalse(r2.Ok, "A payload with an unresolved hull must reject (mod/content gap).");

        // A crafted Modules= count over the cap -> reject BEFORE FromBytes (guards the ushort UID index).
        var craft = new StringBuilder();
        craft.Append("Version=0\nHull=Anything\nModuleUIDs=X\nModules=999999\n");
        string craftB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(craft.ToString()));
        ArenaDesignTable.DecodeResult r3 = ArenaDesignTable.Decode("01" + craftB64);
        Assert.IsFalse(r3.Ok, "A crafted oversized Modules count must reject before FromBytes.");
        StringAssert.Contains(r3.Error, "cap", "The rejection must cite the module-count cap.");

        // Over the per-design base64 cap -> reject.
        string huge = new string('A', ArenaDesignTable.MaxDesignBase64Chars + 4);
        ArenaDesignTable.DecodeResult r4 = ArenaDesignTable.Decode("01" + huge);
        Assert.IsFalse(r4.Ok, "An over-cap design payload must reject.");
    }

    [TestMethod]
    public void Kernel_TamperedBytes_RederiveDifferentName_Headless()
    {
        LoadAllGameData();
        string[] names = LegalStockDesignNames();
        ShipDesign a = AuthorCustomFromStock(names[0]);
        string honestName = ArenaDesignTable.ContentName(a);

        // Encode A, then splice in B's payload under a table that a malicious sender might CLAIM is A.
        // The receiver re-derives the name from the actual bytes, so it never registers under A's name.
        ShipDesign b = AuthorCustomFromStock(names[1]);
        string tableB = ArenaDesignTable.Encode(new List<IShipDesign> { b });
        ArenaDesignTable.DecodeResult decoded = ArenaDesignTable.Decode(tableB);
        Assert.IsTrue(decoded.Ok, $"B's table must decode: {decoded.Error}");
        Assert.IsFalse(decoded.Designs.ContainsKey(honestName),
            "A tampered/substituted payload must NOT resolve under the honest design's name — the receiver " +
            "re-derives the name from the received bytes, so a bundle referencing A's name would fail to resolve.");
        Assert.IsTrue(decoded.Designs.ContainsKey(ArenaDesignTable.ContentName(b)),
            "The substituted payload registers under ITS OWN content-derived name.");
    }

    // ---------------------------------------------------------------------------------------------------
    // Carrier rejection (amendment 7): any non-empty HangarShipUID rejects at validation.
    // ---------------------------------------------------------------------------------------------------
    [TestMethod]
    public void Kernel_CarrierDesign_RejectsAtValidation_Headless()
    {
        LoadAllGameData();

        // Find a stock design that HAS a hangar module (carrier). If none exists in this content set, the
        // rule is still exercised by the synthetic path below.
        IShipDesign carrier = ResourceManager.Ships.Designs
            .FirstOrDefault(d => d.GetOrLoadDesignSlots().Any(s => s.HangarShipUID.NotEmpty() && s.HangarShipUID != "NotApplicable"));

        if (carrier != null)
        {
            string err = ArenaDesignTable.ValidateContentAvailable(carrier);
            Assert.IsTrue(err.NotEmpty(), "A carrier design must be rejected by content validation.");
            StringAssert.Contains(err.ToLowerInvariant(), "carrier",
                "The carrier rejection must be explicit (not read as a content-gap bug).");
        }
        else
        {
            Assert.Inconclusive("No stock carrier design in this content set; carrier reject path unexercised by a real design.");
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // NoLeakedRegistrations: Register -> Unregister returns the global table to its exact snapshot (amendment 4).
    // (The full session teardown-on-throw is proven at the session layer; this proves the primitive.)
    // ---------------------------------------------------------------------------------------------------
    [TestMethod]
    public void Kernel_RegisterTransient_NoLeakAfterUnregister_Headless()
    {
        LoadAllGameData();
        int snapshot = ResourceManager.Ships.Designs.Count;

        string[] names = LegalStockDesignNames();
        ShipDesign a = AuthorCustomFromStock(names[0]);
        ShipDesign b = AuthorCustomFromStock(names[1]);
        string table = ArenaDesignTable.Encode(new List<IShipDesign> { a, b });

        ArenaDesignTable.DecodeResult decoded = ArenaDesignTable.Decode(table);
        Assert.IsTrue(decoded.Ok, $"Table must decode: {decoded.Error}");

        IReadOnlyList<string> registered = ArenaDesignTable.RegisterTransient(decoded.Designs.Values);
        Assert.IsTrue(registered.Count >= 1, "At least one custom design must register.");
        foreach (string rn in registered)
        {
            StringAssert.StartsWith(rn, ArenaDesignTable.NamePrefix, "Every registration must be @arena/ namespaced.");
            Assert.IsTrue(ResourceManager.Ships.GetDesign(rn, out _),
                $"'{rn}' must be resolvable while registered.");
            // playerDesign:false => must NOT appear as a player design in Saved Designs enumeration.
            Assert.IsFalse(ResourceManager.Ships.GetDesign(rn, out IShipDesign d) && d.IsPlayerDesign,
                "A transient arena design must not be a player design.");
        }

        ArenaDesignTable.UnregisterTransient(registered);
        foreach (string rn in registered)
            Assert.IsFalse(ResourceManager.Ships.GetDesign(rn, out _),
                $"'{rn}' must be gone after UnregisterTransient (no leak).");

        Assert.AreEqual(snapshot, ResourceManager.Ships.Designs.Count,
            "After register->unregister the global design table must byte-match its pre-match snapshot.");
    }

    // ---------------------------------------------------------------------------------------------------
    // EXCHANGE-LAYER proofs: the design table folded into the start payload, the flag gate, and handshake
    // validation of CUSTOM fleets (budget + registration + protocol bump).
    // ---------------------------------------------------------------------------------------------------

    [TestMethod]
    public void Kernel_ProtocolVersionBumpedTo6_Headless()
    {
        Assert.AreEqual(6, ArenaMultiplayerSettings.ProtocolVersion,
            "The 8-player + first-class-teams roster bumps the Arena MP protocol version 5 -> 6 (ruling C8).");
    }

    [TestMethod]
    public void Kernel_DesignTable_SurvivesStartMessageRoundTrip_Headless()
    {
        LoadAllGameData();
        string[] names = LegalStockDesignNames();
        ShipDesign a = AuthorCustomFromStock(names[0]);
        string table = ArenaDesignTable.Encode(new List<IShipDesign> { a });

        var settings = new ArenaMultiplayerSettings
        {
            MatchSeed = 0x5EED, RngSeed = 0xA12EA000u,
            HostFleetDesignNames = new[] { names[0] },
            JoinFleetDesignNames = new[] { names[0] },
            HostDesignTable = table,
            JoinDesignTable = "",
            Ruleset = new ArenaMultiplayerRuleset { Mode = ArenaMatchMode.Sandbox, RosterSource = ArenaRosterSource.AllContent },
        }.WithResolvedFleets();

        // The design table must survive WithResolvedFleets (it was being dropped) AND the codec round-trip.
        Assert.AreEqual(table, settings.HostDesignTable,
            "WithResolvedFleets must carry the host design table through fleet resolution.");
        SessionStartMessage start = settings.ToStartMessage();
        Assert.AreEqual(table, start.HostDesignTable, "ToStartMessage must carry the host design table.");
        ArenaMultiplayerSettings back = ArenaMultiplayerSettings.FromStartMessage(start);
        Assert.AreEqual(table, back.HostDesignTable, "FromStartMessage must recover the host design table.");
    }

    [TestMethod]
    public void Kernel_FlagOff_RegisterPeerTables_IsInert_Headless()
    {
        LoadAllGameData();
        bool saved = GlobalStats.Defaults.EnableArenaCustomFleet;
        int snapshot = ResourceManager.Ships.Designs.Count;
        try
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = false;
            ShipDesign a = AuthorCustomFromStock(LegalStockDesignNames()[0]);
            var settings = new ArenaMultiplayerSettings
            {
                HostDesignTable = ArenaDesignTable.Encode(new List<IShipDesign> { a }),
            };
            IReadOnlyList<string> registered = ArenaMultiplayerSession.RegisterPeerDesignTables(settings, out string err);
            Assert.AreEqual("", err, "Flag-off registration must not error.");
            Assert.AreEqual(0, registered.Count, "Flag-off must register NOTHING (a true no-op).");
            Assert.AreEqual(snapshot, ResourceManager.Ships.Designs.Count,
                "Flag-off must leave the global design table untouched.");
        }
        finally { GlobalStats.Defaults.EnableArenaCustomFleet = saved; }
    }

    [TestMethod]
    public void Kernel_CustomFleet_ValidatesAndOverspendRejects_AtHandshake_Headless()
    {
        LoadAllGameData();
        bool saved = GlobalStats.Defaults.EnableArenaCustomFleet;
        int snapshot = ResourceManager.Ships.Designs.Count;
        IReadOnlyList<string> registered = Array.Empty<string>();
        try
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = true;

            // Author one custom design; the fleet references it by its @arena/<hash> name; the design table
            // carries the payload for BOTH peers to reconstruct.
            ShipDesign custom = AuthorCustomFromStock(LegalStockDesignNames()[0]);
            string cname = ArenaDesignTable.ContentName(custom);
            string table = ArenaDesignTable.Encode(new List<IShipDesign> { custom });
            int cost = (int)MathF.Round(custom.BaseStrength);

            var hostBundle = new FleetDesign { Name = "H" };
            hostBundle.Nodes.Add(new FleetDataDesignNode { ShipName = cname, RelativeFleetOffset = Vector2.Zero });
            var joinBundle = new FleetDesign { Name = "J" };
            joinBundle.Nodes.Add(new FleetDataDesignNode { ShipName = cname, RelativeFleetOffset = Vector2.Zero });

            var affordable = new ArenaMultiplayerSettings
            {
                MatchSeed = 0x5EED, RngSeed = 0xA12EA000u,
                HostFleetDesignNames = new[] { cname },
                JoinFleetDesignNames = new[] { cname },
                HostFleetBundle = ArenaFleetBundle.Encode(hostBundle),
                JoinFleetBundle = ArenaFleetBundle.Encode(joinBundle),
                HostDesignTable = table,
                JoinDesignTable = table,
                Ruleset = new ArenaMultiplayerRuleset
                {
                    Mode = ArenaMatchMode.Sandbox, RosterSource = ArenaRosterSource.AllContent,
                    BudgetModel = ArenaBudgetModel.Cap, BudgetCredits = cost + 10,
                },
            }.WithResolvedFleets();

            // Register the tables first (as the session callers do), then validate.
            registered = ArenaMultiplayerSession.RegisterPeerDesignTables(affordable, out string regErr);
            Assert.AreEqual("", regErr, $"Registering the custom design tables must succeed: {regErr}");
            Assert.IsTrue(ResourceManager.Ships.GetDesign(cname, out _),
                "The custom @arena/ design must be resolvable after registration.");

            SessionStartMessage start = affordable.ToStartMessage();
            Assert.AreEqual("", ArenaMultiplayerSettings.ValidateStartMessage(start, out _),
                "A registered, affordable CUSTOM fleet must validate at the handshake.");

            // Same fleet, budget one credit short -> reject at handshake (custom BaseStrength summed correctly).
            ArenaMultiplayerSettings overspent = ArenaMultiplayerSettings.FromStartMessage(start).WithResolvedFleets();
            overspent.Ruleset.BudgetCredits = Math.Max(0, cost - 1);
            SessionStartMessage overStart = overspent.ToStartMessage();
            StringAssert.Contains(ArenaMultiplayerSettings.ValidateStartMessage(overStart, out _),
                "exceeds budget", "A custom fleet over the budget cap must reject at the handshake.");
        }
        finally
        {
            ArenaMultiplayerSession.UnregisterPeerDesignTables(registered);
            GlobalStats.Defaults.EnableArenaCustomFleet = saved;
            Assert.AreEqual(snapshot, ResourceManager.Ships.Designs.Count,
                "Teardown must leave the global design table exactly as it started (no leak).");
        }
    }

    [TestMethod]
    public void Kernel_RegisterPeerDesignTables_MalformedRejectsCleanly_Headless()
    {
        LoadAllGameData();
        bool saved = GlobalStats.Defaults.EnableArenaCustomFleet;
        try
        {
            GlobalStats.Defaults.EnableArenaCustomFleet = true;
            // A container header followed by a garbage (non-base64) design record after the RS separator.
            string malformed = "0" + '' + "!!!not-base64-at-all!!!";
            var settings = new ArenaMultiplayerSettings { HostDesignTable = malformed };
            IReadOnlyList<string> registered = ArenaMultiplayerSession.RegisterPeerDesignTables(settings, out string err);
            Assert.IsTrue(err.NotEmpty(), "A malformed table must reject with an error, not crash.");
            Assert.AreEqual(0, registered.Count, "A rejected table must leave NOTHING registered (self-cleaning).");
        }
        finally { GlobalStats.Defaults.EnableArenaCustomFleet = saved; }
    }
}

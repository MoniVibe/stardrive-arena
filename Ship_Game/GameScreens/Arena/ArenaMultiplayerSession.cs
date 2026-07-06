using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SDLockstep;
using SDUtils.Deterministic;
using Ship_Game.Determinism;
using Ship_Game.Determinism.Lockstep;
using Ship_Game.Ships;
using FleetDesignT = global::Ship_Game.FleetDesign;

namespace Ship_Game.GameScreens.Arena;

public enum ArenaMultiplayerRole
{
    Host = 0,
    Join = 1,
}

public sealed class ArenaMultiplayerSettings
{
    // 3 -> 4: RulesetV0 + canonical design bundles enter the start payload.
    // 4 -> 5: the parallel custom-design TABLE enters the start payload (Arena custom-fleet exchange kernel,
    // STARDRIVE_ARENA_CUSTOM_FLEET_PROGRAM_PLAN_20260706). The codec is append-tolerant, but the design table
    // is a NEW SEMANTIC: a v4 peer would decode it as "" and then fail to resolve the custom @arena/<hash>
    // name with a confusing "design not available" error. The bump makes a v4<->v5 pairing fail cleanly at the
    // version gate instead. One bump covers the whole custom-fleet + N-player program.
    public const int ProtocolVersion = 5;
    const char FleetSeparator = '\u001f';

    public int MatchSeed = 0x5EED;
    public uint RngSeed = 0xA12EA000u;
    public int InputDelay = 3;
    public int MaxTurns = 420;
    public int CommandEveryTurns = 1;
    public string PlayerPreference = "United";
    public string HostRacePreference = "United";
    public string JoinRacePreference = "";
    public string HostLoadoutTrait = ArenaStartArchetype.Wingmates.ToString();
    public string JoinLoadoutTrait = ArenaStartArchetype.Wingmates.ToString();
    public float GameSpeed = 1f;
    public bool StartPaused;
    public string[] HostFleetDesignNames = Array.Empty<string>();
    public string[] JoinFleetDesignNames = Array.Empty<string>();

    // Arena P1: RulesetV0 + canonical design bundles. The ruleset + both design-bundle hashes fold
    // into SettingsHash/StartFingerprint in a FIXED order, so a divergent ruleset or bundle rejects
    // at ValidateStartMessage rather than desyncing mid-match. The bundles default to zero-offset
    // column bundles derived from the fleet name lists (see WithResolvedFleets), so the legacy
    // name-list path keeps working unchanged.
    public ArenaMultiplayerRuleset Ruleset = new();
    public string HostFleetBundle = "";
    public string JoinFleetBundle = "";

    // Arena custom-fleet program (STARDRIVE_ARENA_CUSTOM_FLEET_PROGRAM_PLAN_20260706 §5.2): the REAL match
    // cap. RulesetV0.MaxMatchSeconds was hashed into the fingerprint but never enforced — the actual cap was
    // MaxTurns (a separate field), so a host-set "10 minute" match ended at MaxTurns ticks (~7s at the old
    // 420 default) regardless. Fix: the effective cap is a pure function of MaxMatchSeconds (60 Hz sim), with
    // MaxTurns kept only as an absolute safety ceiling. Both MaxTurns AND MaxMatchSeconds (via Ruleset.AppendTo)
    // are already folded into SettingsHash, so both peers agree on this derived cap and a timeout is
    // deterministic — no new fingerprint surface. A MaxMatchSeconds<=0 disables the derived cap (safety ceiling
    // only), matching the ruleset's "0 = no explicit cap" intent.
    public uint EffectiveMaxTurns
    {
        get
        {
            int maxTurns = Math.Max(1, MaxTurns);
            int seconds = (Ruleset ?? new ArenaMultiplayerRuleset()).MaxMatchSeconds;
            if (seconds <= 0)
                return (uint)maxTurns;
            // 1 turn == 1 sim tick == 1/60 s. Guard the multiply against int overflow for absurd host input.
            long derived = (long)seconds * 60L;
            long capped = Math.Min((long)maxTurns, derived);
            return (uint)Math.Max(1L, capped);
        }
    }

    // Arena custom-fleet exchange kernel (STARDRIVE_ARENA_CUSTOM_FLEET_PROGRAM_PLAN_20260706).
    // The parallel custom-design TABLE for each side: full canonical payloads keyed by content-derived
    // @arena/<hash> names, encoded via ArenaDesignTable.Encode. Empty when EnableArenaCustomFleet is off.
    // The table content is NOT folded into SettingsHash directly — it folds TRANSITIVELY via the bundle
    // hashes (the bundle references @arena/<hash> names, and those names ARE the design content hash), so a
    // divergent module list changes the name -> changes the bundle hash -> changes SettingsHash. Folding the
    // raw table string too would false-reject benign base64/ordering variance between peers.
    public string HostDesignTable = "";
    public string JoinDesignTable = "";

    public string HostDesignBundleHash =>
        ArenaFleetBundle.DesignBundleHash(ResolveBundleOrNames(HostFleetBundle, HostFleetDesignNames));
    public string JoinDesignBundleHash =>
        ArenaFleetBundle.DesignBundleHash(ResolveBundleOrNames(JoinFleetBundle, JoinFleetDesignNames));

    static FleetDesignT ResolveBundleOrNames(string bundle, string[] names)
    {
        if (bundle.NotEmpty())
        {
            FleetDesignT decoded = ArenaFleetBundle.Decode(bundle);
            if (decoded.Nodes.Count > 0)
                return decoded;
        }
        return ArenaFleetBundle.FromDesignNames(names);
    }

    public string SettingsHash
    {
        get
        {
            var h = DetHash.New();
            h.AddInt(ProtocolVersion);
            h.AddInt(MatchSeed);
            h.AddUInt(RngSeed);
            h.AddInt(InputDelay);
            h.AddInt(MaxTurns);
            h.AddInt(CommandEveryTurns);
            h.AddString(PlayerPreference);
            h.AddString(HostRacePreference);
            h.AddString(JoinRacePreference);
            h.AddString(HostLoadoutTrait);
            h.AddString(JoinLoadoutTrait);
            h.AddInt((int)(ClampGameSpeed(GameSpeed) * 1000f));
            h.AddBool(StartPaused);
            AddFleet(ref h, HostFleetDesignNames);
            AddFleet(ref h, JoinFleetDesignNames);
            // FIXED order (plan Part 4b): existing settings -> RulesetV0 -> host bundle hash -> join
            // bundle hash. Any ruleset or bundle divergence changes SettingsHash, which
            // ValidateStartMessage already compares for exact equality and rejects.
            (Ruleset ?? new ArenaMultiplayerRuleset()).AppendTo(ref h);
            h.AddString(HostDesignBundleHash);
            h.AddString(JoinDesignBundleHash);
            return "0x" + h.Value.ToString("X16", CultureInfo.InvariantCulture);
        }
    }

    public SessionStartMessage ToStartMessage(int fromPeer = LockstepHost.HostPeerId)
    {
        ArenaMultiplayerRuleset ruleset = Ruleset ?? new ArenaMultiplayerRuleset();
        return new SessionStartMessage
        {
            FromPeer = fromPeer,
            ProtocolVersion = ProtocolVersion,
            MatchSeed = MatchSeed,
            RngSeed = RngSeed,
            InputDelay = InputDelay,
            MaxTurns = MaxTurns,
            CommandEveryTurns = CommandEveryTurns,
            GameSpeed = ClampGameSpeed(GameSpeed),
            StartPaused = StartPaused,
            SettingsHash = SettingsHash,
            BuildHash = ArenaMultiplayerPeerSignature.Hash(this),
            BuildSummary = ArenaMultiplayerPeerSignature.Summary(this),
            HostRacePreference = HostRacePreference,
            JoinRacePreference = JoinRacePreference,
            HostLoadoutTrait = HostLoadoutTrait,
            JoinLoadoutTrait = JoinLoadoutTrait,
            HostFleet = EncodeFleet(HostFleetDesignNames),
            JoinFleet = EncodeFleet(JoinFleetDesignNames),
            RulesetVersion = ruleset.Version,
            RulesetMode = (int)ruleset.Mode,
            RulesetBudgetModel = (int)ruleset.BudgetModel,
            RulesetBudgetCredits = ruleset.BudgetCredits,
            RulesetRosterSource = (int)ruleset.RosterSource,
            RulesetCountdownSeconds = ruleset.CountdownSeconds,
            RulesetMaxMatchSeconds = ruleset.MaxMatchSeconds,
            RulesetMaxFleetShipsPerSide = ruleset.MaxFleetShipsPerSide,
            RulesetWagerCredits = ruleset.WagerCredits,
            RulesetCommitmentHash = ruleset.RosterCommitmentHash ?? "",
            RulesetContentFingerprint = ruleset.ContentFingerprint ?? "",
            HostFleetBundle = HostFleetBundle ?? "",
            JoinFleetBundle = JoinFleetBundle ?? "",
            HostDesignBundleHash = HostDesignBundleHash,
            JoinDesignBundleHash = JoinDesignBundleHash,
            HostDesignTable = HostDesignTable ?? "",
            JoinDesignTable = JoinDesignTable ?? "",
        };
    }

    public static ArenaMultiplayerSettings FromStartMessage(SessionStartMessage message)
        => new()
        {
            MatchSeed = message.MatchSeed,
            RngSeed = message.RngSeed,
            InputDelay = Math.Max(0, message.InputDelay),
            MaxTurns = Math.Max(1, message.MaxTurns),
            CommandEveryTurns = Math.Max(1, message.CommandEveryTurns),
            PlayerPreference = message.HostRacePreference.NotEmpty() ? message.HostRacePreference : "United",
            HostRacePreference = message.HostRacePreference.NotEmpty() ? message.HostRacePreference : "United",
            JoinRacePreference = message.JoinRacePreference ?? "",
            HostLoadoutTrait = NormalizeLoadoutTrait(message.HostLoadoutTrait),
            JoinLoadoutTrait = NormalizeLoadoutTrait(message.JoinLoadoutTrait),
            GameSpeed = ClampGameSpeed(message.GameSpeed),
            StartPaused = message.StartPaused,
            HostFleetDesignNames = DecodeFleet(message.HostFleet),
            JoinFleetDesignNames = DecodeFleet(message.JoinFleet),
            Ruleset = RulesetFromStartMessage(message),
            HostFleetBundle = message.HostFleetBundle ?? "",
            JoinFleetBundle = message.JoinFleetBundle ?? "",
            HostDesignTable = message.HostDesignTable ?? "",
            JoinDesignTable = message.JoinDesignTable ?? "",
        };

    public static ArenaMultiplayerRuleset RulesetFromStartMessage(SessionStartMessage message)
        => new()
        {
            Version = message.RulesetVersion,
            Mode = (ArenaMatchMode)message.RulesetMode,
            BudgetModel = (ArenaBudgetModel)message.RulesetBudgetModel,
            BudgetCredits = message.RulesetBudgetCredits,
            RosterSource = (ArenaRosterSource)message.RulesetRosterSource,
            CountdownSeconds = message.RulesetCountdownSeconds,
            MaxMatchSeconds = message.RulesetMaxMatchSeconds,
            MaxFleetShipsPerSide = message.RulesetMaxFleetShipsPerSide,
            WagerCredits = message.RulesetWagerCredits,
            RosterCommitmentHash = message.RulesetCommitmentHash ?? "",
            ContentFingerprint = message.RulesetContentFingerprint ?? "",
        };

    public static string StartFingerprint(SessionStartMessage start)
    {
        if (start == null)
            return "";

        var h = DetHash.New();
        h.AddInt(start.ProtocolVersion);
        h.AddInt(start.MatchSeed);
        h.AddUInt(start.RngSeed);
        h.AddInt(start.InputDelay);
        h.AddInt(start.MaxTurns);
        h.AddInt(start.CommandEveryTurns);
        h.AddFloat(start.GameSpeed);
        h.AddBool(start.StartPaused);
        h.AddString(start.SettingsHash ?? "");
        h.AddString(start.BuildHash ?? "");
        h.AddString(start.BuildSummary ?? "");
        h.AddString(start.HostRacePreference ?? "");
        h.AddString(start.JoinRacePreference ?? "");
        h.AddString(start.HostLoadoutTrait ?? "");
        h.AddString(start.JoinLoadoutTrait ?? "");
        h.AddString(start.HostFleet ?? "");
        h.AddString(start.JoinFleet ?? "");
        // Arena P1: RulesetV0 + design bundles, FIXED order (matches ToStartMessage field order).
        h.AddInt(start.RulesetVersion);
        h.AddInt(start.RulesetMode);
        h.AddInt(start.RulesetBudgetModel);
        h.AddInt(start.RulesetBudgetCredits);
        h.AddInt(start.RulesetRosterSource);
        h.AddInt(start.RulesetCountdownSeconds);
        h.AddInt(start.RulesetMaxMatchSeconds);
        h.AddInt(start.RulesetMaxFleetShipsPerSide);
        h.AddInt(start.RulesetWagerCredits);
        h.AddString(start.RulesetCommitmentHash ?? "");
        h.AddString(start.RulesetContentFingerprint ?? "");
        h.AddString(start.HostDesignBundleHash ?? "");
        h.AddString(start.JoinDesignBundleHash ?? "");
        return "0x" + h.Value.ToString("X16", CultureInfo.InvariantCulture);
    }

    public static string ValidateStartMessage(SessionStartMessage start,
        out ArenaMultiplayerSettings settings)
    {
        settings = null;
        if (start == null)
            return "Arena multiplayer start payload was missing.";
        if (start.IsAuthoritative4X)
            return "Received an authoritative 4X start while waiting for an Arena duel.";
        if (start.ProtocolVersion != ProtocolVersion)
            return $"Arena multiplayer protocol mismatch. Local {ProtocolVersion}, host {start.ProtocolVersion}.";

        settings = FromStartMessage(start).WithResolvedFleets();
        if (!string.Equals(start.SettingsHash, settings.SettingsHash, StringComparison.Ordinal))
            return $"Arena multiplayer settings mismatch. Host {start.SettingsHash}, local {settings.SettingsHash}.";
        if (settings.HostFleetDesignNames.Length == 0 || settings.JoinFleetDesignNames.Length == 0)
            return "Arena multiplayer start did not include legal fleet design names for both sides.";
        string unavailable = FirstUnavailableFleetDesign(settings.HostFleetDesignNames
            .Concat(settings.JoinFleetDesignNames)
            .ToArray());
        if (unavailable.NotEmpty())
            return $"Arena multiplayer fleet design '{unavailable}' is not available or legal on this machine.";

        // Explicit design-bundle-hash inclusion: the SettingsHash equality above already folds both
        // bundle hashes, but re-check them directly so a tampered bundle produces a precise error.
        if (!string.Equals(start.HostDesignBundleHash ?? "", settings.HostDesignBundleHash, StringComparison.Ordinal))
            return $"Arena multiplayer host design bundle mismatch. Host {start.HostDesignBundleHash}, local {settings.HostDesignBundleHash}.";
        if (!string.Equals(start.JoinDesignBundleHash ?? "", settings.JoinDesignBundleHash, StringComparison.Ordinal))
            return $"Arena multiplayer join design bundle mismatch. Host {start.JoinDesignBundleHash}, local {settings.JoinDesignBundleHash}.";

        string modeError = ValidateRuleset(settings);
        if (modeError.NotEmpty())
            return modeError;

        string buildError = ArenaMultiplayerPeerSignature.ValidateSession(
            start.BuildHash, start.BuildSummary, settings, "host");
        return buildError;
    }

    /// <summary>
    /// Mode-specific validation (plan Part 4e). Both peers run this locally; a divergent ruleset has
    /// already been caught by the SettingsHash equality, so this is the "is this mode legal in this
    /// build, with a legal roster/budget" gate. Returns "" when the ruleset is acceptable.
    /// </summary>
    public static string ValidateRuleset(ArenaMultiplayerSettings settings)
    {
        ArenaMultiplayerRuleset r = settings.Ruleset ?? new ArenaMultiplayerRuleset();
        if (r.WagerCredits != 0)
            return "Wagers are not available in this build.";

        switch (r.Mode)
        {
            case ArenaMatchMode.Coop:
                return "Coop mode is not available in this build.";
            case ArenaMatchMode.Career:
                if (r.RosterSource != ArenaRosterSource.CareerLocked)
                    return "Career mode requires a career-locked roster source.";
                break;
            case ArenaMatchMode.Sandbox:
                if (r.RosterSource != ArenaRosterSource.AllContent)
                    return "Sandbox mode requires an all-content roster source.";
                if (r.BudgetModel == ArenaBudgetModel.Cap)
                {
                    int hostCost = SumBundleCost(settings.HostFleetBundle, settings.HostFleetDesignNames);
                    int joinCost = SumBundleCost(settings.JoinFleetBundle, settings.JoinFleetDesignNames);
                    if (hostCost > r.BudgetCredits)
                        return $"Sandbox host fleet cost {hostCost} exceeds budget {r.BudgetCredits}.";
                    if (joinCost > r.BudgetCredits)
                        return $"Sandbox join fleet cost {joinCost} exceeds budget {r.BudgetCredits}.";
                }
                break;
            default:
                return $"Unknown Arena match mode {(int)r.Mode}.";
        }
        return "";
    }

    /// <summary>
    /// Deterministic total build cost of a fleet, summing each design's BaseStrength (rounded) — the
    /// scalar the arena already uses as design cost/value everywhere (ArenaBetting, ArenaFightOptions).
    /// Empire-independent so both peers compute the same total.
    /// </summary>
    public static int SumBundleCost(string bundle, string[] fallbackNames)
    {
        FleetDesignT design = ResolveBundleOrNames(bundle, fallbackNames);
        int total = 0;
        foreach (FleetDataDesignNode node in ArenaFleetBundle.StableNodeOrder(design))
            if (ResourceManager.Ships.GetDesign(node.ShipName, out IShipDesign d))
                total += (int)MathF.Round(d.BaseStrength);
        return total;
    }

    public ArenaMultiplayerSettings WithRematchSeed()
    {
        int nextSeed = MatchSeed == int.MaxValue ? 1 : MatchSeed + 1;
        return new ArenaMultiplayerSettings
        {
            MatchSeed = nextSeed,
            RngSeed = (uint)nextSeed ^ 0xA12EA000u,
            InputDelay = InputDelay,
            MaxTurns = MaxTurns,
            CommandEveryTurns = CommandEveryTurns,
            PlayerPreference = PlayerPreference,
            HostRacePreference = HostRacePreference,
            JoinRacePreference = JoinRacePreference,
            HostLoadoutTrait = HostLoadoutTrait,
            JoinLoadoutTrait = JoinLoadoutTrait,
            GameSpeed = GameSpeed,
            StartPaused = StartPaused,
            HostFleetDesignNames = NormalizeFleet(HostFleetDesignNames),
            JoinFleetDesignNames = NormalizeFleet(JoinFleetDesignNames),
            Ruleset = (Ruleset ?? new ArenaMultiplayerRuleset()).Clone(),
            HostFleetBundle = HostFleetBundle ?? "",
            JoinFleetBundle = JoinFleetBundle ?? "",
            // Rematch reuses the SAME custom designs (WithRematchSeed keeps the bundle/design names), so carry
            // the design tables forward; the caller re-registers them at each match start (idempotent dedup).
            HostDesignTable = HostDesignTable ?? "",
            JoinDesignTable = JoinDesignTable ?? "",
        }.WithResolvedFleets();
    }

    public ArenaMultiplayerSettings WithResolvedFleets()
    {
        var copy = new ArenaMultiplayerSettings
        {
            Ruleset = (Ruleset ?? new ArenaMultiplayerRuleset()).Clone(),
            HostFleetBundle = HostFleetBundle ?? "",
            JoinFleetBundle = JoinFleetBundle ?? "",
            // Custom-fleet exchange kernel: carry the design tables through fleet resolution (they were being
            // dropped, silently disabling custom designs on every path that re-resolves).
            HostDesignTable = HostDesignTable ?? "",
            JoinDesignTable = JoinDesignTable ?? "",
            MatchSeed = MatchSeed,
            RngSeed = RngSeed,
            InputDelay = Math.Max(0, InputDelay),
            MaxTurns = Math.Max(1, MaxTurns),
            CommandEveryTurns = Math.Max(1, CommandEveryTurns),
            PlayerPreference = HostRacePreference.NotEmpty()
                ? HostRacePreference
                : PlayerPreference.NotEmpty() ? PlayerPreference : "United",
            HostRacePreference = HostRacePreference.NotEmpty()
                ? HostRacePreference
                : PlayerPreference.NotEmpty() ? PlayerPreference : "United",
            JoinRacePreference = JoinRacePreference ?? "",
            HostLoadoutTrait = NormalizeLoadoutTrait(HostLoadoutTrait),
            JoinLoadoutTrait = NormalizeLoadoutTrait(JoinLoadoutTrait),
            GameSpeed = ClampGameSpeed(GameSpeed),
            StartPaused = StartPaused,
            HostFleetDesignNames = NormalizeFleet(HostFleetDesignNames),
            JoinFleetDesignNames = NormalizeFleet(JoinFleetDesignNames),
        };

        if (copy.HostFleetDesignNames.Length == 0)
            copy.HostFleetDesignNames = DefaultFleetForSeed((ulong)(uint)copy.MatchSeed ^ 0xA12E_0001ul,
                ParseLoadoutTrait(copy.HostLoadoutTrait));
        if (copy.JoinFleetDesignNames.Length == 0)
            copy.JoinFleetDesignNames = DefaultFleetForSeed((ulong)(uint)copy.MatchSeed ^ 0xA12E_0002ul,
                ParseLoadoutTrait(copy.JoinLoadoutTrait));
        return copy;
    }

    public static string EncodeFleet(string[] names)
        => string.Join(FleetSeparator, NormalizeFleet(names));

    public static string[] DecodeFleet(string text)
        => string.IsNullOrEmpty(text)
            ? Array.Empty<string>()
            : NormalizeFleet(text.Split(FleetSeparator));

    public static string[] NormalizeFleet(string[] names)
        => (names ?? Array.Empty<string>())
            .Where(n => n.NotEmpty())
            .Select(n => n.Trim())
            .Where(n => n.NotEmpty())
            .Take(32)
            .ToArray();

    public static string NormalizeLoadoutTrait(string trait)
    {
        ArenaStartArchetype parsed = ParseLoadoutTrait(trait);
        return parsed.ToString();
    }

    public static ArenaStartArchetype ParseLoadoutTrait(string trait)
    {
        if (Enum.TryParse(trait ?? "", ignoreCase: true, out ArenaStartArchetype parsed))
            return parsed;
        return ArenaStartArchetype.Wingmates;
    }

    public static float ClampGameSpeed(float speed)
        => Math.Max(0.25f, Math.Min(4f, speed));

    static string[] DefaultFleetForSeed(ulong seed, ArenaStartArchetype archetype)
    {
        IShipDesign[] designs = CareerManager.StartingRosterDesigns(archetype, seed);
        if (designs == null || designs.Length == 0)
        {
            IShipDesign fallback = ArenaFightScreen.AutoPickPlayerWarship(null, careerLevel: 0);
            return fallback != null ? new[] { fallback.Name } : Array.Empty<string>();
        }
        return designs.Select(d => d.Name).ToArray();
    }

    static void AddFleet(ref DetHash hash, string[] names)
    {
        string[] normalized = NormalizeFleet(names);
        hash.AddInt(normalized.Length);
        for (int i = 0; i < normalized.Length; ++i)
            hash.AddString(normalized[i]);
    }

    static string FirstUnavailableFleetDesign(string[] names)
    {
        foreach (string name in NormalizeFleet(names))
            if (!ResourceManager.Ships.GetDesign(name, out IShipDesign design)
                || !ArenaFightScreen.IsLegalCombatCraft(design))
                return name;
        return "";
    }
}

public static class ArenaMultiplayerPeerSignature
{
    public static string EnvironmentHash()
    {
        var h = DetHash.New();
        h.AddString(GlobalStats.ExtendedVersionNoHash);
        h.AddString(GlobalStats.ExtendedVersion);
        h.AddString(GlobalStats.ModName);
        h.AddString(GlobalStats.ModVersion);
        h.AddString(typeof(ArenaPlugin).Assembly.GetName().Version?.ToString() ?? "");
        h.AddString(typeof(LockstepHost).Assembly.GetName().Version?.ToString() ?? "");
        h.AddULong(BuildFingerprint.Compute(DeterminismProfile.MPSamePlatformPinnedFloat));
        return "0x" + h.Value.ToString("X16", CultureInfo.InvariantCulture);
    }

    public static string Hash(ArenaMultiplayerSettings settings)
    {
        settings ??= new ArenaMultiplayerSettings();
        var h = DetHash.New();
        h.AddString(EnvironmentHash());
        h.AddString(settings.SettingsHash);
        return "0x" + h.Value.ToString("X16", CultureInfo.InvariantCulture);
    }

    public static string EnvironmentSummary()
    {
        string game = GlobalStats.ExtendedVersionNoHash.NotEmpty()
            ? GlobalStats.ExtendedVersionNoHash
            : GlobalStats.ExtendedVersion.NotEmpty() ? GlobalStats.ExtendedVersion : "unknown-game";
        string mod = GlobalStats.HasMod ? $"{GlobalStats.ModName} {GlobalStats.ModVersion}" : "Vanilla";
        return $"{game}; {mod}; env {EnvironmentHash()}";
    }

    public static string Summary(ArenaMultiplayerSettings settings)
    {
        settings ??= new ArenaMultiplayerSettings();
        return $"{EnvironmentSummary()}; settings {settings.SettingsHash}; build {Hash(settings)}";
    }

    public static string ValidateEnvironment(string remoteHash, string remoteSummary, string remoteLabel)
    {
        string localHash = EnvironmentHash();
        if (remoteHash.IsEmpty())
            return $"{remoteLabel} did not send an Arena multiplayer environment fingerprint.";
        if (string.Equals(remoteHash, localHash, StringComparison.Ordinal))
            return "";

        string remote = remoteSummary.NotEmpty() ? remoteSummary : remoteHash;
        return "Arena multiplayer environment mismatch.\n"
               + $"Local {EnvironmentSummary()}\n"
               + $"{remoteLabel} {remote}";
    }

    public static string ValidateSession(string remoteHash, string remoteSummary,
        ArenaMultiplayerSettings settings, string remoteLabel)
    {
        string localHash = Hash(settings);
        if (remoteHash.IsEmpty())
            return $"{remoteLabel} did not send an Arena multiplayer session fingerprint.";
        if (string.Equals(remoteHash, localHash, StringComparison.Ordinal))
            return "";

        string remote = remoteSummary.NotEmpty() ? remoteSummary : remoteHash;
        return "Arena multiplayer session mismatch.\n"
               + $"Local {Summary(settings)}\n"
               + $"{remoteLabel} {remote}";
    }
}

public readonly struct ArenaMultiplayerTurnHash
{
    public readonly uint Turn;
    public readonly ulong HostLo;
    public readonly ulong HostHi;
    public readonly ulong JoinLo;
    public readonly ulong JoinHi;

    public ArenaMultiplayerTurnHash(uint turn, (ulong lo, ulong hi) host, (ulong lo, ulong hi) join)
    {
        Turn = turn;
        HostLo = host.lo;
        HostHi = host.hi;
        JoinLo = join.lo;
        JoinHi = join.hi;
    }

    public bool Match => HostLo == JoinLo && HostHi == JoinHi;
}

public sealed class ArenaMultiplayerRunResult
{
    public readonly List<ArenaMultiplayerTurnHash> TurnHashes = new();
    public bool Desynced;
    public long DesyncTurn = -1;
    public string DesyncReason = "";
    public bool MatchEnded;
    public int WinnerPeerId;
    public long MatchEndedTurn = -1;
    public bool Disconnected;
    public string DisconnectReason = "";
    public string FinalHash = "";
    public ArenaMultiplayerShipSnapshot HostSnapshot;
    public ArenaMultiplayerShipSnapshot JoinSnapshot;
    public int CommandsSubmitted;
    public int TurnsCompleted => TurnHashes.Count;
}

/// <summary>
/// Phase-1, 2-player Arena lockstep session harness. Single-player Arena never calls this; it is
/// a separate multiplayer path that creates two deterministic Arena peers, exchanges canonical
/// command frames, and halts on checksum divergence.
/// </summary>
public static class ArenaMultiplayerSession
{
    public const int HostPlayerPeerId = 1;
    public const int JoinPlayerPeerId = 2;
    public const int DefaultPort = 47377;
    static readonly object PeerScreenBuildGate = new();

    /// <summary>
    /// Arena custom-fleet exchange kernel: BIDIRECTIONALLY decode + validate + transiently register EVERY
    /// peer's custom-design table BEFORE ValidateStartMessage / spawn (amendment 6). The returned name set is
    /// the EXACT set to tear down in the caller's finally on every exit path — throw/reject/disconnect/rematch
    /// (amendment 4). Authored to generalize to N peers even though this phase tests N=2.
    ///
    /// Registration RE-DERIVES each name from the received bytes (never a sender-supplied name), so a tampered
    /// payload registers under its own @arena/<hash> and the bundle's referenced name fails to resolve at the
    /// existing FirstUnavailableFleetDesign gate (amendment 1 tamper-close). A malformed/oversized/carrier/
    /// mod-gap payload rejects cleanly here via the out error, never crashing the peer (amendment 5, 7).
    ///
    /// No-op (returns an empty set, "" error) when GlobalStats.Defaults.EnableArenaCustomFleet is off — the
    /// legacy name-only behavior is unchanged and no @arena/ design is ever registered.
    /// </summary>
    public static IReadOnlyList<string> RegisterPeerDesignTables(ArenaMultiplayerSettings settings, out string error)
    {
        error = "";
        var registered = new List<string>();
        if (settings == null || !(GlobalStats.Defaults?.EnableArenaCustomFleet ?? false))
            return registered;

        foreach (string table in new[] { settings.HostDesignTable, settings.JoinDesignTable })
        {
            if ((table ?? "").IsEmpty())
                continue;
            ArenaDesignTable.DecodeResult decoded = ArenaDesignTable.Decode(table);
            if (!decoded.Ok)
            {
                error = $"Arena custom-fleet design table rejected: {decoded.Error}";
                UnregisterPeerDesignTables(registered); // undo anything already registered this call
                registered.Clear();
                return registered;
            }
            registered.AddRange(ArenaDesignTable.RegisterTransient(decoded.Designs.Values));
        }
        return registered;
    }

    /// <summary>Tears down exactly the set returned by <see cref="RegisterPeerDesignTables"/>. Safe on empty/partial.</summary>
    public static void UnregisterPeerDesignTables(IReadOnlyList<string> registeredNames)
        => ArenaDesignTable.UnregisterTransient(registeredNames);

    public static ArenaMultiplayerRunResult RunInProcess(ArenaMultiplayerSettings settings,
        int forceDesyncAfterTurn = -1)
    {
        settings = (settings ?? new ArenaMultiplayerSettings()).WithResolvedFleets();
        // Register both peers' custom-design tables before building the peer screens (so the @arena/<hash>
        // names resolve during spawn), and tear them down on EVERY exit path (amendment 4).
        IReadOnlyList<string> registered = RegisterPeerDesignTables(settings, out string tableError);
        if (tableError.NotEmpty())
        {
            UnregisterPeerDesignTables(registered);
            throw new InvalidOperationException(tableError);
        }
        try
        {
            ArenaFightScreen hostScreen = BuildPeerScreen(settings);
            ArenaFightScreen joinScreen = BuildPeerScreen(settings);
            return RunTwoPeerLockstep(settings, hostScreen, joinScreen, new FakeTransport(), forceDesyncAfterTurn);
        }
        finally
        {
            UnregisterPeerDesignTables(registered);
        }
    }

    public static ArenaMultiplayerRunResult RunLoopbackTcpSelfTest(ArenaMultiplayerSettings settings,
        Action<string> log = null)
    {
        settings = (settings ?? new ArenaMultiplayerSettings()).WithResolvedFleets();
        int port = FreeTcpPort();
        using var listening = new ManualResetEventSlim(false);
        ArenaMultiplayerRunResult hostResult = null;
        ArenaMultiplayerRunResult joinResult = null;

        Task hostTask = Task.Run(() =>
        {
            hostResult = RunNetworkHost(settings, port, line =>
            {
                if (line.StartsWith("HOST listening", StringComparison.Ordinal))
                    listening.Set();
                log?.Invoke(line);
            });
        });

        if (!listening.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Arena loopback host did not start listening.");

        Task joinTask = Task.Run(() =>
        {
            joinResult = RunNetworkJoin("127.0.0.1", port, log);
        });

        if (!Task.WaitAll(new[] { hostTask, joinTask }, TimeSpan.FromSeconds(120)))
            throw new TimeoutException("Arena loopback TCP self-test timed out.");

        if (hostTask.Exception != null)
            throw hostTask.Exception.GetBaseException();
        if (joinTask.Exception != null)
            throw joinTask.Exception.GetBaseException();
        if (hostResult == null || joinResult == null)
            throw new InvalidOperationException("Arena loopback TCP self-test did not produce both peer results.");

        if (!string.Equals(hostResult.FinalHash, joinResult.FinalHash, StringComparison.Ordinal))
        {
            hostResult.Desynced = true;
            hostResult.DesyncTurn = Math.Max(hostResult.TurnsCompleted, joinResult.TurnsCompleted) - 1;
            hostResult.DesyncReason =
                $"loopback final hash mismatch host={hostResult.FinalHash} join={joinResult.FinalHash}";
        }
        if (hostResult.MatchEnded != joinResult.MatchEnded || hostResult.WinnerPeerId != joinResult.WinnerPeerId)
        {
            hostResult.Desynced = true;
            hostResult.DesyncReason =
                $"loopback match outcome mismatch hostEnded={hostResult.MatchEnded} joinEnded={joinResult.MatchEnded} "
                + $"hostWinner={hostResult.WinnerPeerId} joinWinner={joinResult.WinnerPeerId}";
        }

        hostResult.JoinSnapshot = joinResult.JoinSnapshot;
        return hostResult;
    }

    public static string DesyncSummary(DesyncDetector desync)
    {
        if (desync == null || !desync.HasDesync)
            return "";

        string reference = desync.ReferencePeer >= 0
            ? $"peer {desync.ReferencePeer} 0x{desync.ReferenceHi:X16}:0x{desync.ReferenceLo:X16}"
            : "no reference";
        string divergent = $"peer {desync.DivergentPeer} 0x{desync.DivergentHi:X16}:0x{desync.DivergentLo:X16}";
        return $"turn {desync.FirstDivergentTick} {reference} != {divergent}";
    }

    public static ArenaMultiplayerRunResult RunNetworkHost(ArenaMultiplayerSettings settings, int port,
        Action<string> log = null)
    {
        settings = (settings ?? new ArenaMultiplayerSettings()).WithResolvedFleets();
        using TcpLockstepTransport transport = TcpLockstepTransport.Host(port, JoinPlayerPeerId);
        log?.Invoke($"HOST listening on port {port}");
        if (!transport.WaitForConnection(TimeSpan.FromSeconds(60)))
            throw new TimeoutException("Timed out waiting for Arena multiplayer client.");

        int remoteReadyCount = 0;
        string handshakeError = "";
        transport.AddObserver(LockstepHost.HostPeerId, m =>
        {
            if (m is SessionHelloMessage h && h.PeerId == JoinPlayerPeerId)
            {
                if (h.ProtocolVersion != ArenaMultiplayerSettings.ProtocolVersion)
                    handshakeError = $"Arena multiplayer protocol mismatch. Local {ArenaMultiplayerSettings.ProtocolVersion}, remote {h.ProtocolVersion}.";
                else
                    handshakeError = ArenaMultiplayerPeerSignature.ValidateEnvironment(
                        h.BuildHash, h.BuildSummary, "remote");
            }
            if (m is SessionReadyMessage r && r.PeerId == JoinPlayerPeerId && r.Ready)
            {
                string readyError = ArenaMultiplayerPeerSignature.ValidateEnvironment(
                    r.BuildHash, r.BuildSummary, "remote");
                if (readyError.NotEmpty())
                    handshakeError = readyError;
                remoteReadyCount++;
            }
        });

        DateTime readyDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (remoteReadyCount < 1 && handshakeError.IsEmpty() && DateTime.UtcNow < readyDeadline)
        {
            transport.Poll();
            Thread.Sleep(5);
        }
        if (handshakeError.NotEmpty())
        {
            transport.Send(JoinPlayerPeerId,
                new SessionErrorMessage { FromPeer = LockstepHost.HostPeerId, Error = handshakeError });
            throw new InvalidOperationException(handshakeError);
        }
        if (remoteReadyCount < 1)
            throw new TimeoutException("Client connected but did not ready-up.");

        transport.Send(JoinPlayerPeerId, settings.ToStartMessage());
        // Arena custom-fleet exchange kernel: the host registers BOTH peers' custom-design tables so its own
        // spawn resolves the @arena/<hash> names, then tears them down on every exit path (amendment 4, 6).
        IReadOnlyList<string> registered = RegisterPeerDesignTables(settings, out string tableError);
        if (tableError.NotEmpty())
        {
            UnregisterPeerDesignTables(registered);
            transport.Send(JoinPlayerPeerId,
                new SessionErrorMessage { FromPeer = LockstepHost.HostPeerId, Error = tableError });
            throw new InvalidOperationException(tableError);
        }
        try
        {
            ArenaFightScreen screen = BuildPeerScreen(settings);
            return RunHostNetworkLoop(settings, screen, transport, () => remoteReadyCount >= 2, log);
        }
        finally
        {
            UnregisterPeerDesignTables(registered);
        }
    }

    public static ArenaMultiplayerRunResult RunNetworkJoin(string host, int port,
        Action<string> log = null)
    {
        using TcpLockstepTransport transport = TcpLockstepTransport.Join(host, port, LockstepHost.HostPeerId);
        log?.Invoke($"JOIN connected to {host}:{port}");

        SessionStartMessage start = null;
        string sessionError = "";
        transport.AddObserver(JoinPlayerPeerId, m =>
        {
            if (m is SessionStartMessage s)
                start = s;
            if (m is SessionErrorMessage e)
                sessionError = e.Error;
        });
        transport.Send(LockstepHost.HostPeerId,
            new SessionHelloMessage
            {
                FromPeer = JoinPlayerPeerId,
                PeerId = JoinPlayerPeerId,
                ProtocolVersion = ArenaMultiplayerSettings.ProtocolVersion,
                PlayerName = "Arena Join",
                BuildHash = ArenaMultiplayerPeerSignature.EnvironmentHash(),
                BuildSummary = ArenaMultiplayerPeerSignature.EnvironmentSummary(),
            });
        transport.Send(LockstepHost.HostPeerId,
            new SessionReadyMessage
            {
                FromPeer = JoinPlayerPeerId,
                PeerId = JoinPlayerPeerId,
                Ready = true,
                BuildHash = ArenaMultiplayerPeerSignature.EnvironmentHash(),
                BuildSummary = ArenaMultiplayerPeerSignature.EnvironmentSummary(),
            });

        DateTime startDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (start == null && sessionError.IsEmpty() && DateTime.UtcNow < startDeadline)
        {
            transport.Poll();
            Thread.Sleep(5);
        }
        if (sessionError.NotEmpty())
            throw new InvalidOperationException(sessionError);
        if (start == null)
            throw new TimeoutException("Host did not send Arena multiplayer start settings.");

        // Arena custom-fleet exchange kernel: register BOTH peers' custom-design tables BEFORE ValidateStartMessage
        // (which throws on any error — the A2 leak path) and tear them down on EVERY exit path (amendment 4).
        ArenaMultiplayerSettings preSettings = ArenaMultiplayerSettings.FromStartMessage(start);
        IReadOnlyList<string> registered = RegisterPeerDesignTables(preSettings, out string tableError);
        if (tableError.NotEmpty())
        {
            UnregisterPeerDesignTables(registered);
            throw new InvalidOperationException(tableError);
        }
        try
        {
            string startError = ArenaMultiplayerSettings.ValidateStartMessage(start, out ArenaMultiplayerSettings settings);
            if (startError.NotEmpty())
                throw new InvalidOperationException(startError);

            ArenaFightScreen screen = BuildPeerScreen(settings);
            return RunJoinNetworkLoop(settings, screen, transport, log);
        }
        finally
        {
            UnregisterPeerDesignTables(registered);
        }
    }

    static ArenaFightScreen BuildPeerScreen(ArenaMultiplayerSettings settings)
    {
        lock (PeerScreenBuildGate)
        {
            settings = (settings ?? new ArenaMultiplayerSettings()).WithResolvedFleets();
            ArenaFightScreen screen = ArenaFightScreen.Create(settings.HostRacePreference, settings.MatchSeed,
                startAtHub: false, opponentPreference: settings.JoinRacePreference);
            screen.ConfigureMultiplayerPvP(settings);
            screen.CreateSimThread = false;
            screen.UState.Objects.EnableParallelUpdate = false;
            ArenaEngineCapabilities.TryEnableSeededRng(screen.UState, settings.RngSeed);
            screen.LoadContent();
            screen.PrepareForMultiplayerLockstep(settings.RngSeed);
            return screen;
        }
    }

    static ArenaMultiplayerRunResult RunTwoPeerLockstep(ArenaMultiplayerSettings settings,
        ArenaFightScreen hostScreen, ArenaFightScreen joinScreen, FakeTransport transport,
        int forceDesyncAfterTurn)
    {
        var host = new LockstepHost(transport);
        var hostSim = hostScreen.CreateMultiplayerLockstepSimulation();
        var joinSim = joinScreen.CreateMultiplayerLockstepSimulation();
        var hostClient = new LockstepClient(transport, HostPlayerPeerId, hostSim);
        var joinClient = new LockstepClient(transport, JoinPlayerPeerId, joinSim);
        host.AddClient(HostPlayerPeerId);
        host.AddClient(JoinPlayerPeerId);

        var result = NewResult(hostScreen, joinScreen);
        ValidateSnapshots(result);

        uint maxTurns = settings.EffectiveMaxTurns;
        for (uint turn = 0; turn < maxTurns; ++turn)
        {
            SubmitTurnCommands(settings, turn, hostClient, joinClient, hostScreen, ref result.CommandsSubmitted);
            transport.Poll();
            host.CommitTick(turn);
            transport.Poll();
            hostClient.Pump();
            joinClient.Pump();
            if (forceDesyncAfterTurn >= 0 && turn == forceDesyncAfterTurn)
                joinScreen.ForceMultiplayerDesyncForTest();
            transport.Poll();

            RecordTurn(result, turn, hostSim.Hash(), joinSim.Hash(), host.Desync);
            UpdateMatchOutcome(result, turn, hostScreen.MultiplayerMatchStatus(), hostSim.Hash());
            if (result.Desynced)
                break;
            if (result.MatchEnded)
                break;
        }

        return result;
    }

    static ArenaMultiplayerRunResult RunHostNetworkLoop(ArenaMultiplayerSettings settings,
        ArenaFightScreen screen, TcpLockstepTransport transport, Func<bool> remoteArmed, Action<string> log)
    {
        long remoteChecksumTick = -1;
        var submittedInputs = new Dictionary<uint, HashSet<int>>();
        transport.AddObserver(LockstepHost.HostPeerId, m =>
        {
            if (m is ChecksumMessage c && c.FromPeer == JoinPlayerPeerId)
                remoteChecksumTick = Math.Max(remoteChecksumTick, c.Tick);
            if (m is SubmitCommandMessage s)
            {
                if (!submittedInputs.TryGetValue(s.Command.Tick, out HashSet<int> peers))
                {
                    peers = new HashSet<int>();
                    submittedInputs[s.Command.Tick] = peers;
                }
                peers.Add(s.Command.PlayerId);
            }
        });

        var host = new LockstepHost(transport);
        var sim = screen.CreateMultiplayerLockstepSimulation();
        var client = new LockstepClient(transport, HostPlayerPeerId, sim);
        host.AddClient(HostPlayerPeerId);
        host.AddClient(JoinPlayerPeerId);
        WaitFor(remoteArmed, transport, TimeSpan.FromSeconds(30),
            "client received Arena settings but did not arm the simulation");

        var result = NewResult(screen, screen);
        uint maxTurns = settings.EffectiveMaxTurns;
        for (uint turn = 0; turn < maxTurns; ++turn)
        {
            if (ShouldSubmit(settings, turn))
            {
                client.Submit(screen.BuildMultiplayerFocusCommand(HostPlayerPeerId, turn + (uint)settings.InputDelay, turn));
                result.CommandsSubmitted++;
            }
            transport.Poll();
            if (ShouldHaveSubmittedForExecTick(settings, turn))
            {
                WaitFor(() => HasBothInputs(submittedInputs, turn), transport, TimeSpan.FromSeconds(10),
                    $"both peers did not submit input for turn {turn}");
            }
            host.CommitTick(turn);
            transport.Poll();
            client.Pump();
            transport.Poll();
            WaitFor(() => sim.Tick > turn, transport, TimeSpan.FromSeconds(5),
                $"host local sim did not apply turn {turn}");
            WaitFor(() => remoteChecksumTick >= turn || host.Desync.HasDesync, transport, TimeSpan.FromSeconds(10),
                $"remote peer did not report checksum for turn {turn}");
            RecordSinglePeerTurn(result, turn, sim.Hash(), host.Desync);
            UpdateMatchOutcome(result, turn, screen.MultiplayerMatchStatus(), sim.Hash());
            if (result.Desynced)
            {
                Log.Warning($"Arena MP DESYNC network-host turn={result.DesyncTurn}: {result.DesyncReason}");
                log?.Invoke($"DESYNC at turn {result.DesyncTurn}: {result.DesyncReason}");
                break;
            }
            if (result.MatchEnded)
                break;
        }
        log?.Invoke($"HOST completed turns={result.TurnsCompleted} desynced={result.Desynced}");
        return result;
    }

    static ArenaMultiplayerRunResult RunJoinNetworkLoop(ArenaMultiplayerSettings settings,
        ArenaFightScreen screen, TcpLockstepTransport transport, Action<string> log)
    {
        var sim = screen.CreateMultiplayerLockstepSimulation();
        var client = new LockstepClient(transport, JoinPlayerPeerId, sim);
        transport.Send(LockstepHost.HostPeerId,
            new SessionReadyMessage
            {
                FromPeer = JoinPlayerPeerId,
                PeerId = JoinPlayerPeerId,
                Ready = true,
                BuildHash = ArenaMultiplayerPeerSignature.EnvironmentHash(),
                BuildSummary = ArenaMultiplayerPeerSignature.EnvironmentSummary(),
            });
        var result = NewResult(screen, screen);

        uint maxTurns = settings.EffectiveMaxTurns;
        for (uint turn = 0; turn < maxTurns; ++turn)
        {
            if (ShouldSubmit(settings, turn))
            {
                client.Submit(screen.BuildMultiplayerFocusCommand(JoinPlayerPeerId, turn + (uint)settings.InputDelay, turn));
                result.CommandsSubmitted++;
            }
            transport.Poll();
            client.Pump();
            transport.Poll();
            WaitForClientTick(client, sim, transport, turn, TimeSpan.FromSeconds(10),
                $"join sim did not receive/apply turn {turn}");
            RecordSinglePeerTurn(result, turn, sim.Hash(), null);
            UpdateMatchOutcome(result, turn, screen.MultiplayerMatchStatus(), sim.Hash());
            if (result.MatchEnded)
                break;
            Thread.Sleep(1);
        }
        log?.Invoke($"JOIN completed turns={result.TurnsCompleted}");
        return result;
    }

    static ArenaMultiplayerRunResult NewResult(ArenaFightScreen hostScreen, ArenaFightScreen joinScreen)
        => new()
        {
            HostSnapshot = hostScreen.MultiplayerSnapshot(),
            JoinSnapshot = joinScreen.MultiplayerSnapshot(),
        };

    static void ValidateSnapshots(ArenaMultiplayerRunResult result)
    {
        if (!SameIds(result.HostSnapshot.PlayerShipIds, result.JoinSnapshot.PlayerShipIds)
            || !SameIds(result.HostSnapshot.EnemyShipIds, result.JoinSnapshot.EnemyShipIds))
            throw new InvalidOperationException("Arena multiplayer peers did not spawn identical stable ship IDs.");
        if (!SameStrings(result.HostSnapshot.PlayerFleetDesigns, result.JoinSnapshot.PlayerFleetDesigns)
            || !SameStrings(result.HostSnapshot.EnemyFleetDesigns, result.JoinSnapshot.EnemyFleetDesigns))
            throw new InvalidOperationException("Arena multiplayer peers did not spawn identical fleet manifests.");
    }

    static bool SameIds(int[] a, int[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; ++i)
            if (a[i] != b[i])
                return false;
        return true;
    }

    static void SubmitTurnCommands(ArenaMultiplayerSettings settings, uint turn,
        LockstepClient hostClient, LockstepClient joinClient, ArenaFightScreen commandSource,
        ref int commandsSubmitted)
    {
        if (!ShouldSubmit(settings, turn))
            return;

        uint execTick = turn + (uint)Math.Max(0, settings.InputDelay);
        hostClient.Submit(commandSource.BuildMultiplayerFocusCommand(HostPlayerPeerId, execTick, turn));
        joinClient.Submit(commandSource.BuildMultiplayerFocusCommand(JoinPlayerPeerId, execTick, turn));
        commandsSubmitted += 2;
    }

    static bool ShouldSubmit(ArenaMultiplayerSettings settings, uint turn)
        => settings.CommandEveryTurns <= 1 || turn % (uint)settings.CommandEveryTurns == 0;

    static bool ShouldHaveSubmittedForExecTick(ArenaMultiplayerSettings settings, uint turn)
    {
        uint delay = (uint)Math.Max(0, settings.InputDelay);
        if (turn < delay)
            return false;
        return ShouldSubmit(settings, turn - delay);
    }

    static bool HasBothInputs(Dictionary<uint, HashSet<int>> submittedInputs, uint turn)
        => submittedInputs.TryGetValue(turn, out HashSet<int> peers)
           && peers.Contains(HostPlayerPeerId)
           && peers.Contains(JoinPlayerPeerId);

    static void RecordTurn(ArenaMultiplayerRunResult result, uint turn,
        (ulong lo, ulong hi) hostHash, (ulong lo, ulong hi) joinHash, DesyncDetector desync)
    {
        result.TurnHashes.Add(new ArenaMultiplayerTurnHash(turn, hostHash, joinHash));
        result.FinalHash = HashText(hostHash);
        if (desync.HasDesync || hostHash != joinHash)
        {
            result.Desynced = true;
            result.DesyncTurn = desync.HasDesync ? desync.FirstDivergentTick : turn;
            result.DesyncReason = desync.HasDesync
                ? DesyncSummary(desync)
                : "local hash comparison diverged";
            Log.Warning($"Arena MP DESYNC in-process turn={result.DesyncTurn}: {result.DesyncReason}");
        }
    }

    static void RecordSinglePeerTurn(ArenaMultiplayerRunResult result, uint turn,
        (ulong lo, ulong hi) hash, DesyncDetector desync)
    {
        result.TurnHashes.Add(new ArenaMultiplayerTurnHash(turn, hash, hash));
        result.FinalHash = HashText(hash);
        if (desync != null && desync.HasDesync)
        {
            result.Desynced = true;
            result.DesyncTurn = desync.FirstDivergentTick;
            result.DesyncReason = DesyncSummary(desync);
        }
    }

    static void UpdateMatchOutcome(ArenaMultiplayerRunResult result, uint turn,
        ArenaMultiplayerMatchStatus status, (ulong lo, ulong hi) hash)
    {
        result.FinalHash = HashText(hash);
        if (!status.Ended || result.MatchEnded)
            return;
        result.MatchEnded = true;
        result.MatchEndedTurn = turn;
        result.WinnerPeerId = status.WinnerPeerId;
    }

    static string HashText((ulong lo, ulong hi) hash)
        => $"0x{hash.hi:X16}:0x{hash.lo:X16}";

    static bool SameStrings(string[] a, string[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; ++i)
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                return false;
        return true;
    }

    static void WaitFor(Func<bool> done, ILockstepTransport transport, TimeSpan timeout, string error)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!done() && DateTime.UtcNow < deadline)
        {
            transport.Poll();
            Thread.Sleep(1);
        }
        if (!done())
            throw new TimeoutException(error);
    }

    static void WaitForClientTick(LockstepClient client, UniverseStateLockstepSimulation sim,
        ILockstepTransport transport, uint turn, TimeSpan timeout, string error)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (sim.Tick <= turn && DateTime.UtcNow < deadline)
        {
            transport.Poll();
            client.Pump();
            transport.Poll();
            Thread.Sleep(1);
        }
        if (sim.Tick <= turn)
            throw new TimeoutException(error);
    }

    static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

namespace SDLockstep;

/// <summary>
/// Minimal lockstep session/lobby control messages. They ride over the same reliable ordered
/// transport as command/checksum frames, but the simulation clients ignore them unless a session
/// coordinator observes the transport. Phase 1 keeps this intentionally small: hello, ready, start,
/// and error are enough for a 2-player host/join flow.
/// </summary>
public sealed class SessionHelloMessage : LockstepMessage
{
    public int ProtocolVersion;
    public int PeerId;
    public string PlayerName = "";
    public string BuildHash = "";
    public string BuildSummary = "";
}

public sealed class SessionReadyMessage : LockstepMessage
{
    public int PeerId;
    public bool Ready;
    public string BuildHash = "";
    public string BuildSummary = "";
}

public sealed class SessionLobbyMessage : LockstepMessage
{
    public int PeerId;
    public bool Ready;
    public string PlayerName = "";
    public string RacePreference = "";
    public string LoadoutTrait = "";
    public string TraitOptions = "";
    public string Fleet = "";
    public string BuildHash = "";
    public string BuildSummary = "";

    // Arena custom-fleet exchange (STARDRIVE_ARENA_SETUP_PHASE_EXEC_PLAN_20260706, Phase A).
    // Each peer publishes its OWN full canonical design-table payloads (the @arena/<hash> customs
    // referenced by Fleet) so the joiner's customs reach the host over the lobby sync. Optional
    // trailing string; empty when EnableArenaCustomFleet is off (today's name-only behaviour).
    // Append-only wire (no protocol re-bump; stays 5).
    public string DesignTable = "";
}

public sealed class SessionStartMessage : LockstepMessage
{
    public int ProtocolVersion;
    public int MatchSeed;
    public uint RngSeed;
    public int InputDelay;
    public int MaxTurns;
    public int CommandEveryTurns;
    public float GameSpeed;
    public bool StartPaused;
    public string SettingsHash = "";
    public string BuildHash = "";
    public string BuildSummary = "";
    public string HostRacePreference = "";
    public string JoinRacePreference = "";
    public string HostLoadoutTrait = "";
    public string JoinLoadoutTrait = "";
    public string HostFleet = "";
    public string JoinFleet = "";

    // Arena P1 RulesetV0 + canonical design bundles (STARDRIVE_ARENA_P1_FLEETSETUP_EXEC_PLAN_20260705).
    // Optional trailing fields — a v3 peer that never wrote them decodes them as the defaults below.
    // Arena mode/roster/budget author the RulesetV0; Host/JoinFleetBundle are the canonical
    // FleetDesign bundles (names + per-ship offsets); the bundle hashes fold into SettingsHash.
    public int RulesetVersion;
    public int RulesetMode;
    public int RulesetBudgetModel;
    public int RulesetBudgetCredits;
    public int RulesetRosterSource;
    public int RulesetCountdownSeconds = 3;
    public int RulesetMaxMatchSeconds = 600;
    public int RulesetMaxFleetShipsPerSide = 32;
    public int RulesetWagerCredits;
    public string RulesetCommitmentHash = "";
    public string RulesetContentFingerprint = "";
    // Host-authored custom-fleet in-arena SETUP-phase opt-in (append-only optional trailing field; no
    // ProtocolVersion bump — a pre-field reader stops before it and gets the default false = legacy launch).
    public bool RulesetSetupPhase;
    public string HostFleetBundle = "";
    public string JoinFleetBundle = "";
    public string HostDesignBundleHash = "";
    public string JoinDesignBundleHash = "";

    // Arena custom-fleet exchange kernel (STARDRIVE_ARENA_CUSTOM_FLEET_PROGRAM_PLAN_20260706).
    // The PARALLEL design TABLE: full custom-design payloads keyed by content-derived @arena/<hash> names,
    // exchanged alongside the lean FleetBundle so a peer can reconstruct a design it has never seen. Optional
    // trailing strings; empty when EnableArenaCustomFleet is off (today's name-only behavior). Semantically
    // new payload => ProtocolVersion bumped 4->5 so a v4 peer fails cleanly at the version gate.
    public string HostDesignTable = "";
    public string JoinDesignTable = "";

    // Optional authoritative 4X launch payload. Arena/skirmish sessions leave this false
    // and ignore the fields; 4X lobby handoff uses it to generate the same real galaxy on
    // host and clients before attaching the authoritative session.
    public bool IsAuthoritative4X;
    public int AuthoritativeHostPeerId;
    public int AuthoritativeJoinPeerId;
    public int GenerationSeed;
    public int GalaxySize;
    public int StarsCount;
    public int ExtraRemnant;
    public int GameMode;
    public int Difficulty;
    public int NumOpponents;
    public float Pace;
    public int TurnTimer;
    public int ExtraPlanets;
    public float CustomMineralDecay = 1f;
    public float VolcanicActivity = 1f;
    public float StartingPlanetRichnessBonus;
    public float ShipMaintenanceMultiplier = 1f;
    public float FTLModifier = 1f;
    public float EnemyFTLModifier = 0.5f;
    public float GravityWellRange = 8000f;
    public bool AIUsesPlayerDesigns = true;
    public bool UseUpkeepByHullSize;
    public bool DisableRemnantStory;
    public bool EnableRandomizedAIFleetSizes;
    public bool DisableAlternateAITraits;
    public bool DisablePirates;
    public bool DisableResearchStations;
    public bool DisableMiningOps;
    public string HostTraitOptions = "";
    public string JoinTraitOptions = "";
    public string AuthoritativePlayerRoster = "";
}

public sealed class SessionStartAckMessage : LockstepMessage
{
    public int PeerId;
    public bool Accepted;
    public string StartFingerprint = "";
    public string Error = "";
}

public sealed class SessionControlMessage : LockstepMessage
{
    public bool Paused;
    public float GameSpeed;
}

public sealed class SessionErrorMessage : LockstepMessage
{
    public string Error = "";
}

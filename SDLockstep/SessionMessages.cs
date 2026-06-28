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
    public string BuildHash = "";
    public string BuildSummary = "";
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

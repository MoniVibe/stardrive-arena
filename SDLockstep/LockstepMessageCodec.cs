using System;
using System.IO;
using System.Text;

namespace SDLockstep;

/// <summary>
/// Length-prefixed TCP payload codec for <see cref="LockstepMessage"/>. The transport writes one
/// encoded message per frame; TCP supplies ordering/reliability, this supplies message boundaries.
/// Numeric fields are fixed-width little-endian through BinaryWriter/BinaryReader, matching the
/// existing command serializer contract.
/// </summary>
public static class LockstepMessageCodec
{
    const byte SubmitCommand = 1;
    const byte CommandFrame = 2;
    const byte Checksum = 3;
    const byte SessionHello = 10;
    const byte SessionReady = 11;
    const byte SessionStart = 12;
    const byte SessionError = 13;
    const byte SessionLobby = 14;
    const byte SessionControl = 15;
    const byte AuthoritativeCommandRequest = 20;
    const byte AuthoritativeCommandResult = 21;
    const byte AuthoritativeStateSnapshot = 22;
    const byte AuthoritativeDiplomacyPopup = 23;
    const byte AuthoritativeSaveTransferBegin = 24;
    const byte AuthoritativeSaveTransferChunk = 25;
    const byte AuthoritativeSaveTransferEnd = 26;
    const byte AuthoritativeResyncRequest = 27;
    const byte AuthoritativeResyncBegin = 28;
    const byte AuthoritativeResyncAck = 29;

    public static byte[] Encode(LockstepMessage message, int toPeer)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8))
        {
            w.Write(toPeer);
            w.Write(message.FromPeer);
            switch (message)
            {
                case SubmitCommandMessage submit:
                    w.Write(SubmitCommand);
                    CommandSerializer.WriteCommand(w, submit.Command);
                    break;
                case CommandFrameMessage frame:
                    w.Write(CommandFrame);
                    CommandSerializer.WriteFrame(w, frame.Frame);
                    break;
                case ChecksumMessage checksum:
                    w.Write(Checksum);
                    w.Write(checksum.Tick);
                    w.Write(checksum.Lo);
                    w.Write(checksum.Hi);
                    break;
                case SessionHelloMessage hello:
                    w.Write(SessionHello);
                    w.Write(hello.ProtocolVersion);
                    w.Write(hello.PeerId);
                    WriteString(w, hello.PlayerName);
                    WriteString(w, hello.BuildHash);
                    WriteString(w, hello.BuildSummary);
                    break;
                case SessionReadyMessage ready:
                    w.Write(SessionReady);
                    w.Write(ready.PeerId);
                    w.Write(ready.Ready);
                    WriteString(w, ready.BuildHash);
                    WriteString(w, ready.BuildSummary);
                    break;
                case SessionLobbyMessage lobby:
                    w.Write(SessionLobby);
                    w.Write(lobby.PeerId);
                    w.Write(lobby.Ready);
                    WriteString(w, lobby.PlayerName);
                    WriteString(w, lobby.RacePreference);
                    WriteString(w, lobby.LoadoutTrait);
                    WriteString(w, lobby.BuildHash);
                    WriteString(w, lobby.BuildSummary);
                    WriteString(w, lobby.TraitOptions);
                    break;
                case SessionStartMessage start:
                    w.Write(SessionStart);
                    w.Write(start.ProtocolVersion);
                    w.Write(start.MatchSeed);
                    w.Write(start.RngSeed);
                    w.Write(start.InputDelay);
                    w.Write(start.MaxTurns);
                    w.Write(start.CommandEveryTurns);
                    w.Write(start.GameSpeed);
                    w.Write(start.StartPaused);
                    WriteString(w, start.SettingsHash);
                    WriteString(w, start.HostRacePreference);
                    WriteString(w, start.JoinRacePreference);
                    WriteString(w, start.HostLoadoutTrait);
                    WriteString(w, start.JoinLoadoutTrait);
                    WriteString(w, start.HostFleet);
                    WriteString(w, start.JoinFleet);
                    WriteString(w, start.BuildHash);
                    WriteString(w, start.BuildSummary);
                    w.Write(start.IsAuthoritative4X);
                    w.Write(start.AuthoritativeHostPeerId);
                    w.Write(start.AuthoritativeJoinPeerId);
                    w.Write(start.GenerationSeed);
                    w.Write(start.GalaxySize);
                    w.Write(start.StarsCount);
                    w.Write(start.GameMode);
                    w.Write(start.Difficulty);
                    w.Write(start.NumOpponents);
                    w.Write(start.Pace);
                    w.Write(start.TurnTimer);
                    w.Write(start.ExtraPlanets);
                    w.Write(start.StartingPlanetRichnessBonus);
                    WriteString(w, start.HostTraitOptions);
                    WriteString(w, start.JoinTraitOptions);
                    WriteString(w, start.AuthoritativePlayerRoster);
                    w.Write(start.ExtraRemnant);
                    w.Write(start.CustomMineralDecay);
                    w.Write(start.VolcanicActivity);
                    w.Write(start.ShipMaintenanceMultiplier);
                    w.Write(start.FTLModifier);
                    w.Write(start.EnemyFTLModifier);
                    w.Write(start.GravityWellRange);
                    w.Write(start.AIUsesPlayerDesigns);
                    w.Write(start.UseUpkeepByHullSize);
                    w.Write(start.DisableRemnantStory);
                    w.Write(start.EnableRandomizedAIFleetSizes);
                    w.Write(start.DisableAlternateAITraits);
                    w.Write(start.DisablePirates);
                    w.Write(start.DisableResearchStations);
                    w.Write(start.DisableMiningOps);
                    break;
                case SessionControlMessage control:
                    w.Write(SessionControl);
                    w.Write(control.Paused);
                    w.Write(control.GameSpeed);
                    break;
                case SessionErrorMessage error:
                    w.Write(SessionError);
                    WriteString(w, error.Error);
                    break;
                case AuthoritativeCommandRequestMessage request:
                    w.Write(AuthoritativeCommandRequest);
                    w.Write(request.Sequence);
                    w.Write(request.EmpireId);
                    w.Write(request.Kind);
                    w.Write(request.SubjectId);
                    w.Write(request.TargetId);
                    w.Write(request.X);
                    w.Write(request.Y);
                    WriteString(w, request.Text);
                    break;
                case AuthoritativeCommandResultMessage result:
                    w.Write(AuthoritativeCommandResult);
                    w.Write(result.Sequence);
                    w.Write(result.Accepted);
                    w.Write(result.Tick);
                    WriteString(w, result.Reason);
                    w.Write(result.OriginPeer);
                    break;
                case AuthoritativeStateSnapshotMessage snapshot:
                    w.Write(AuthoritativeStateSnapshot);
                    w.Write(snapshot.Tick);
                    w.Write(snapshot.HashLo);
                    w.Write(snapshot.HashHi);
                    WriteString(w, snapshot.SyncDigest);
                    WriteString(w, snapshot.Payload);
                    break;
                case AuthoritativeDiplomacyPopupMessage popup:
                    w.Write(AuthoritativeDiplomacyPopup);
                    w.Write(popup.ProposalId);
                    w.Write(popup.ProposerEmpireId);
                    w.Write(popup.TargetEmpireId);
                    w.Write(popup.ProposalType);
                    WriteString(w, popup.Terms);
                    w.Write(popup.RequiresResponse);
                    WriteString(w, popup.Message);
                    break;
                case AuthoritativeSaveTransferBeginMessage begin:
                    w.Write(AuthoritativeSaveTransferBegin);
                    w.Write(begin.TransferId);
                    w.Write(begin.TotalBytes);
                    w.Write(begin.TotalChunks);
                    w.Write(begin.ChunkSize);
                    WriteString(w, begin.SaveFileName);
                    WriteString(w, begin.MetadataYaml);
                    WriteString(w, begin.Sha256);
                    WriteString(w, begin.Reason);
                    break;
                case AuthoritativeSaveTransferChunkMessage chunk:
                    w.Write(AuthoritativeSaveTransferChunk);
                    w.Write(chunk.TransferId);
                    w.Write(chunk.ChunkIndex);
                    w.Write(chunk.Offset);
                    WriteBytes(w, chunk.Data);
                    break;
                case AuthoritativeSaveTransferEndMessage end:
                    w.Write(AuthoritativeSaveTransferEnd);
                    w.Write(end.TransferId);
                    WriteString(w, end.Sha256);
                    break;
                case AuthoritativeResyncRequestMessage resync:
                    w.Write(AuthoritativeResyncRequest);
                    w.Write(resync.Tick);
                    WriteString(w, resync.ClientDigest);
                    WriteString(w, resync.Reason);
                    break;
                case AuthoritativeResyncBeginMessage begin:
                    w.Write(AuthoritativeResyncBegin);
                    w.Write(begin.Epoch);
                    w.Write(begin.RequestingPeer);
                    w.Write(begin.Tick);
                    WriteString(w, begin.ClientDigest);
                    WriteString(w, begin.Reason);
                    break;
                case AuthoritativeResyncAckMessage ack:
                    w.Write(AuthoritativeResyncAck);
                    w.Write(ack.Epoch);
                    w.Write(ack.Tick);
                    WriteString(w, ack.LoadedDigest);
                    WriteString(w, ack.SaveSha256);
                    WriteString(w, ack.Error);
                    break;
                default:
                    throw new InvalidDataException($"Unsupported lockstep message type {message.GetType().FullName}");
            }
        }
        return ms.ToArray();
    }

    public static DecodedLockstepMessage Decode(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms, Encoding.UTF8);
        int toPeer = r.ReadInt32();
        int fromPeer = r.ReadInt32();
        byte type = r.ReadByte();
        LockstepMessage message;
        switch (type)
        {
            case SubmitCommand:
                message = new SubmitCommandMessage { Command = CommandSerializer.ReadCommand(r) };
                break;
            case CommandFrame:
                message = new CommandFrameMessage { Frame = CommandSerializer.ReadFrame(r) };
                break;
            case Checksum:
                message = new ChecksumMessage { Tick = r.ReadUInt32(), Lo = r.ReadUInt64(), Hi = r.ReadUInt64() };
                break;
            case SessionHello:
                message = new SessionHelloMessage
                {
                    ProtocolVersion = r.ReadInt32(),
                    PeerId = r.ReadInt32(),
                    PlayerName = ReadString(r),
                    BuildHash = ReadOptionalString(r),
                    BuildSummary = ReadOptionalString(r),
                };
                break;
            case SessionReady:
                message = new SessionReadyMessage
                {
                    PeerId = r.ReadInt32(),
                    Ready = r.ReadBoolean(),
                    BuildHash = ReadOptionalString(r),
                    BuildSummary = ReadOptionalString(r),
                };
                break;
            case SessionLobby:
                message = new SessionLobbyMessage
                {
                    PeerId = r.ReadInt32(),
                    Ready = r.ReadBoolean(),
                    PlayerName = ReadString(r),
                    RacePreference = ReadString(r),
                    LoadoutTrait = ReadString(r),
                    BuildHash = ReadOptionalString(r),
                    BuildSummary = ReadOptionalString(r),
                    TraitOptions = ReadOptionalString(r),
                };
                break;
            case SessionStart:
                int protocolVersion = r.ReadInt32();
                int matchSeed = r.ReadInt32();
                uint rngSeed = r.ReadUInt32();
                int inputDelay = r.ReadInt32();
                int maxTurns = r.ReadInt32();
                int commandEveryTurns = r.BaseStream.Position < r.BaseStream.Length ? r.ReadInt32() : 1;
                float gameSpeed = r.BaseStream.Position < r.BaseStream.Length ? r.ReadSingle() : 1f;
                bool startPaused = r.BaseStream.Position < r.BaseStream.Length && r.ReadBoolean();
                string settingsHash = ReadString(r);
                string hostRace = ReadOptionalString(r);
                string joinRace = ReadOptionalString(r);
                string hostTrait = ReadOptionalString(r);
                string joinTrait = ReadOptionalString(r);
                string hostFleet = ReadOptionalString(r);
                string joinFleet = ReadOptionalString(r);
                string buildHash = ReadOptionalString(r);
                string buildSummary = ReadOptionalString(r);
                bool isAuthoritative4X = r.BaseStream.Position < r.BaseStream.Length && r.ReadBoolean();
                int authoritativeHostPeerId = r.BaseStream.Position < r.BaseStream.Length ? r.ReadInt32() : 0;
                int authoritativeJoinPeerId = r.BaseStream.Position < r.BaseStream.Length ? r.ReadInt32() : 0;
                int generationSeed = r.BaseStream.Position < r.BaseStream.Length ? r.ReadInt32() : 0;
                int galaxySize = r.BaseStream.Position < r.BaseStream.Length ? r.ReadInt32() : 0;
                int starsCount = r.BaseStream.Position < r.BaseStream.Length ? r.ReadInt32() : 0;
                int gameMode = r.BaseStream.Position < r.BaseStream.Length ? r.ReadInt32() : 0;
                int difficulty = r.BaseStream.Position < r.BaseStream.Length ? r.ReadInt32() : 0;
                int numOpponents = r.BaseStream.Position < r.BaseStream.Length ? r.ReadInt32() : 0;
                float pace = r.BaseStream.Position < r.BaseStream.Length ? r.ReadSingle() : 0f;
                int turnTimer = r.BaseStream.Position < r.BaseStream.Length ? r.ReadInt32() : 0;
                int extraPlanets = r.BaseStream.Position < r.BaseStream.Length ? r.ReadInt32() : 0;
                float richness = r.BaseStream.Position < r.BaseStream.Length ? r.ReadSingle() : 0f;
                string hostTraitOptions = ReadOptionalString(r);
                string joinTraitOptions = ReadOptionalString(r);
                string authoritativePlayerRoster = ReadOptionalString(r);
                int extraRemnant = r.BaseStream.Position < r.BaseStream.Length ? r.ReadInt32() : 0;
                float mineralDecay = r.BaseStream.Position < r.BaseStream.Length ? r.ReadSingle() : 1f;
                float volcanicActivity = r.BaseStream.Position < r.BaseStream.Length ? r.ReadSingle() : 1f;
                float maintenance = r.BaseStream.Position < r.BaseStream.Length ? r.ReadSingle() : 1f;
                float ftl = r.BaseStream.Position < r.BaseStream.Length ? r.ReadSingle() : 1f;
                float enemyFtl = r.BaseStream.Position < r.BaseStream.Length ? r.ReadSingle() : 0.5f;
                float gravityWell = r.BaseStream.Position < r.BaseStream.Length ? r.ReadSingle() : 8000f;
                bool aiPlayerDesigns = r.BaseStream.Position >= r.BaseStream.Length || r.ReadBoolean();
                bool hullUpkeep = r.BaseStream.Position < r.BaseStream.Length && r.ReadBoolean();
                bool disableRemnants = r.BaseStream.Position < r.BaseStream.Length && r.ReadBoolean();
                bool randomizedFleets = r.BaseStream.Position < r.BaseStream.Length && r.ReadBoolean();
                bool disableAltTraits = r.BaseStream.Position < r.BaseStream.Length && r.ReadBoolean();
                bool disablePirates = r.BaseStream.Position < r.BaseStream.Length && r.ReadBoolean();
                bool disableResearchStations = r.BaseStream.Position < r.BaseStream.Length && r.ReadBoolean();
                bool disableMiningOps = r.BaseStream.Position < r.BaseStream.Length && r.ReadBoolean();
                message = new SessionStartMessage
                {
                    ProtocolVersion = protocolVersion,
                    MatchSeed = matchSeed,
                    RngSeed = rngSeed,
                    InputDelay = inputDelay,
                    MaxTurns = maxTurns,
                    CommandEveryTurns = commandEveryTurns,
                    GameSpeed = gameSpeed,
                    StartPaused = startPaused,
                    SettingsHash = settingsHash,
                    HostRacePreference = hostRace,
                    JoinRacePreference = joinRace,
                    HostLoadoutTrait = hostTrait,
                    JoinLoadoutTrait = joinTrait,
                    HostFleet = hostFleet,
                    JoinFleet = joinFleet,
                    BuildHash = buildHash,
                    BuildSummary = buildSummary,
                    IsAuthoritative4X = isAuthoritative4X,
                    AuthoritativeHostPeerId = authoritativeHostPeerId,
                    AuthoritativeJoinPeerId = authoritativeJoinPeerId,
                    GenerationSeed = generationSeed,
                    GalaxySize = galaxySize,
                    StarsCount = starsCount,
                    GameMode = gameMode,
                    Difficulty = difficulty,
                    NumOpponents = numOpponents,
                    Pace = pace,
                    TurnTimer = turnTimer,
                    ExtraPlanets = extraPlanets,
                    ExtraRemnant = extraRemnant,
                    CustomMineralDecay = mineralDecay,
                    VolcanicActivity = volcanicActivity,
                    StartingPlanetRichnessBonus = richness,
                    ShipMaintenanceMultiplier = maintenance,
                    FTLModifier = ftl,
                    EnemyFTLModifier = enemyFtl,
                    GravityWellRange = gravityWell,
                    AIUsesPlayerDesigns = aiPlayerDesigns,
                    UseUpkeepByHullSize = hullUpkeep,
                    DisableRemnantStory = disableRemnants,
                    EnableRandomizedAIFleetSizes = randomizedFleets,
                    DisableAlternateAITraits = disableAltTraits,
                    DisablePirates = disablePirates,
                    DisableResearchStations = disableResearchStations,
                    DisableMiningOps = disableMiningOps,
                    HostTraitOptions = hostTraitOptions,
                    JoinTraitOptions = joinTraitOptions,
                    AuthoritativePlayerRoster = authoritativePlayerRoster,
                };
                break;
            case SessionControl:
                message = new SessionControlMessage
                {
                    Paused = r.ReadBoolean(),
                    GameSpeed = r.ReadSingle(),
                };
                break;
            case SessionError:
                message = new SessionErrorMessage { Error = ReadString(r) };
                break;
            case AuthoritativeCommandRequest:
                message = new AuthoritativeCommandRequestMessage
                {
                    Sequence = r.ReadInt32(),
                    EmpireId = r.ReadInt32(),
                    Kind = r.ReadByte(),
                    SubjectId = r.ReadInt32(),
                    TargetId = r.ReadInt32(),
                    X = r.ReadSingle(),
                    Y = r.ReadSingle(),
                    Text = ReadString(r),
                };
                break;
            case AuthoritativeCommandResult:
                message = new AuthoritativeCommandResultMessage
                {
                    Sequence = r.ReadInt32(),
                    Accepted = r.ReadBoolean(),
                    Tick = r.ReadUInt32(),
                    Reason = ReadString(r),
                };
                ((AuthoritativeCommandResultMessage)message).OriginPeer =
                    r.BaseStream.Position < r.BaseStream.Length ? r.ReadInt32() : 0;
                break;
            case AuthoritativeStateSnapshot:
                message = new AuthoritativeStateSnapshotMessage
                {
                    Tick = r.ReadUInt32(),
                    HashLo = r.ReadUInt64(),
                    HashHi = r.ReadUInt64(),
                    SyncDigest = ReadString(r),
                    Payload = ReadString(r),
                };
                break;
            case AuthoritativeDiplomacyPopup:
                message = new AuthoritativeDiplomacyPopupMessage
                {
                    ProposalId = r.ReadInt32(),
                    ProposerEmpireId = r.ReadInt32(),
                    TargetEmpireId = r.ReadInt32(),
                    ProposalType = r.ReadByte(),
                    Terms = ReadString(r),
                    RequiresResponse = r.ReadBoolean(),
                    Message = ReadString(r),
                };
                break;
            case AuthoritativeSaveTransferBegin:
                message = new AuthoritativeSaveTransferBeginMessage
                {
                    TransferId = r.ReadInt32(),
                    TotalBytes = r.ReadInt32(),
                    TotalChunks = r.ReadInt32(),
                    ChunkSize = r.ReadInt32(),
                    SaveFileName = ReadString(r),
                    MetadataYaml = ReadString(r),
                    Sha256 = ReadString(r),
                    Reason = ReadString(r),
                };
                break;
            case AuthoritativeSaveTransferChunk:
                message = new AuthoritativeSaveTransferChunkMessage
                {
                    TransferId = r.ReadInt32(),
                    ChunkIndex = r.ReadInt32(),
                    Offset = r.ReadInt32(),
                    Data = ReadBytes(r),
                };
                break;
            case AuthoritativeSaveTransferEnd:
                message = new AuthoritativeSaveTransferEndMessage
                {
                    TransferId = r.ReadInt32(),
                    Sha256 = ReadString(r),
                };
                break;
            case AuthoritativeResyncRequest:
                message = new AuthoritativeResyncRequestMessage
                {
                    Tick = r.ReadUInt32(),
                    ClientDigest = ReadString(r),
                    Reason = ReadString(r),
                };
                break;
            case AuthoritativeResyncBegin:
                message = new AuthoritativeResyncBeginMessage
                {
                    Epoch = r.ReadInt32(),
                    RequestingPeer = r.ReadInt32(),
                    Tick = r.ReadUInt32(),
                    ClientDigest = ReadString(r),
                    Reason = ReadString(r),
                };
                break;
            case AuthoritativeResyncAck:
                message = new AuthoritativeResyncAckMessage
                {
                    Epoch = r.ReadInt32(),
                    Tick = r.ReadUInt32(),
                    LoadedDigest = ReadString(r),
                    SaveSha256 = ReadString(r),
                    Error = ReadString(r),
                };
                break;
            default:
                throw new InvalidDataException($"Unsupported lockstep wire message type {type}");
        }
        message.FromPeer = fromPeer;
        return new DecodedLockstepMessage(toPeer, message);
    }

    static void WriteString(BinaryWriter w, string value) => w.Write(value ?? "");
    static string ReadString(BinaryReader r) => r.ReadString() ?? "";
    static string ReadOptionalString(BinaryReader r)
        => r.BaseStream.Position < r.BaseStream.Length ? ReadString(r) : "";
    static void WriteBytes(BinaryWriter w, byte[] value)
    {
        value ??= Array.Empty<byte>();
        w.Write(value.Length);
        w.Write(value);
    }
    static byte[] ReadBytes(BinaryReader r)
    {
        int length = r.ReadInt32();
        if (length < 0 || length > 1_048_576)
            throw new InvalidDataException($"Invalid lockstep byte payload length {length}");
        return r.ReadBytes(length);
    }
}

public readonly struct DecodedLockstepMessage
{
    public readonly int ToPeer;
    public readonly LockstepMessage Message;

    public DecodedLockstepMessage(int toPeer, LockstepMessage message)
    {
        ToPeer = toPeer;
        Message = message;
    }
}

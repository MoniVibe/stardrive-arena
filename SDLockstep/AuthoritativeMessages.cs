namespace SDLockstep;

/// <summary>
/// Primitive, engine-agnostic request envelope for authoritative 4X multiplayer.
/// The game layer maps Kind/ids/args onto real StarDrive actions.
/// </summary>
public sealed class AuthoritativeCommandRequestMessage : LockstepMessage
{
    public int Sequence;
    public int EmpireId;
    public byte Kind;
    public int SubjectId;
    public int TargetId;
    public float X;
    public float Y;
    public string Text = "";
}

public sealed class AuthoritativeCommandResultMessage : LockstepMessage
{
    public int Sequence;
    public int OriginPeer;
    public bool Accepted;
    public uint Tick;
    public string Reason = "";
}

public sealed class AuthoritativeStateSnapshotMessage : LockstepMessage
{
    public uint Tick;
    public ulong HashLo;
    public ulong HashHi;
    public string SyncDigest = "";
    public string TransformDigest = "";
    public string Payload = "";
}

public sealed class AuthoritativeDiplomacyPopupMessage : LockstepMessage
{
    public int ProposalId;
    public int ProposerEmpireId;
    public int TargetEmpireId;
    public byte ProposalType;
    public string Terms = "";
    public bool RequiresResponse;
    public string Message = "";
}

public sealed class AuthoritativeSaveTransferBeginMessage : LockstepMessage
{
    public int TransferId;
    public int TotalBytes;
    public int TotalChunks;
    public int ChunkSize;
    public string SaveFileName = "";
    public string MetadataYaml = "";
    public string Sha256 = "";
    public string Reason = "";
}

public sealed class AuthoritativeSaveTransferChunkMessage : LockstepMessage
{
    public int TransferId;
    public int ChunkIndex;
    public int Offset;
    public byte[] Data = System.Array.Empty<byte>();
}

public sealed class AuthoritativeSaveTransferEndMessage : LockstepMessage
{
    public int TransferId;
    public string Sha256 = "";
}

public sealed class AuthoritativeResyncRequestMessage : LockstepMessage
{
    public uint Tick;
    public string ClientDigest = "";
    public string Reason = "";
}

public sealed class AuthoritativeResyncBeginMessage : LockstepMessage
{
    public int Epoch;
    public int RequestingPeer;
    public uint Tick;
    public string ClientDigest = "";
    public string Reason = "";
}

public sealed class AuthoritativeResyncAckMessage : LockstepMessage
{
    public int Epoch;
    public uint Tick;
    public string LoadedDigest = "";
    public string SaveSha256 = "";
    public string Error = "";
}

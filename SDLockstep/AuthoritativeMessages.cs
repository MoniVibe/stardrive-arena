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
    public string Payload = "";
}

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
                case SessionStartMessage start:
                    w.Write(SessionStart);
                    w.Write(start.ProtocolVersion);
                    w.Write(start.MatchSeed);
                    w.Write(start.RngSeed);
                    w.Write(start.InputDelay);
                    w.Write(start.MaxTurns);
                    WriteString(w, start.SettingsHash);
                    WriteString(w, start.HostFleet);
                    WriteString(w, start.JoinFleet);
                    WriteString(w, start.BuildHash);
                    WriteString(w, start.BuildSummary);
                    break;
                case SessionErrorMessage error:
                    w.Write(SessionError);
                    WriteString(w, error.Error);
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
            case SessionStart:
                message = new SessionStartMessage
                {
                    ProtocolVersion = r.ReadInt32(),
                    MatchSeed = r.ReadInt32(),
                    RngSeed = r.ReadUInt32(),
                    InputDelay = r.ReadInt32(),
                    MaxTurns = r.ReadInt32(),
                    SettingsHash = ReadString(r),
                    HostFleet = ReadString(r),
                    JoinFleet = ReadString(r),
                    BuildHash = ReadOptionalString(r),
                    BuildSummary = ReadOptionalString(r),
                };
                break;
            case SessionError:
                message = new SessionErrorMessage { Error = ReadString(r) };
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

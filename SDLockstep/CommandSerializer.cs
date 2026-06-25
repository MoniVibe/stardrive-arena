using System.Collections.Generic;
using System.IO;

namespace SDLockstep;

/// <summary>
/// Byte-stable serialization of commands / frames / logs (advisor plan VS7). BinaryWriter is
/// little-endian on every platform, and every field is fixed-width, so the same logical content
/// always produces identical bytes — required for replay equality and a stable wire format.
/// </summary>
public static class CommandSerializer
{
    public static void WriteCommand(BinaryWriter w, in SimCommand c)
    {
        w.Write(SimCommand.Schema);
        w.Write(c.Tick);
        w.Write(c.PlayerId);
        w.Write(c.PlayerLocalSequence);
        w.Write((byte)c.Kind);
        w.Write(c.SubjectId);
        w.Write(c.TargetId);
        w.Write(c.PosXRaw);
        w.Write(c.PosYRaw);
    }

    public static SimCommand ReadCommand(BinaryReader r)
    {
        ushort schema = r.ReadUInt16();
        _ = schema; // reserved for versioned upgrades
        uint tick = r.ReadUInt32();
        int playerId = r.ReadInt32();
        uint seq = r.ReadUInt32();
        var kind = (SimCommandKind)r.ReadByte();
        int subject = r.ReadInt32();
        int target = r.ReadInt32();
        long px = r.ReadInt64();
        long py = r.ReadInt64();
        return new SimCommand(tick, playerId, seq, kind, subject, target, px, py);
    }

    public static void WriteFrame(BinaryWriter w, CommandFrame frame)
    {
        w.Write(frame.Tick);
        w.Write(frame.Commands.Count);
        for (int i = 0; i < frame.Commands.Count; ++i)
            WriteCommand(w, frame.Commands[i]);
    }

    public static CommandFrame ReadFrame(BinaryReader r)
    {
        uint tick = r.ReadUInt32();
        int count = r.ReadInt32();
        var f = new CommandFrame(tick);
        for (int i = 0; i < count; ++i)
            f.Add(ReadCommand(r));
        return f;
    }

    public static byte[] SerializeFrame(CommandFrame frame)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
            WriteFrame(w, frame);
        return ms.ToArray();
    }

    public static CommandFrame DeserializeFrame(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);
        return ReadFrame(r);
    }
}

/// <summary>
/// Records command frames by tick — the replay artifact and the network tail used for join/resync.
/// Frames are sorted on record so the log is always in canonical order.
/// </summary>
public sealed class CommandLog
{
    readonly SortedDictionary<uint, CommandFrame> Frames = new();

    public int Count => Frames.Count;

    public void Record(CommandFrame frame)
    {
        frame.Sort();
        Frames[frame.Tick] = frame;
    }

    public CommandFrame Get(uint tick) => Frames.TryGetValue(tick, out CommandFrame f) ? f : null;

    public IEnumerable<CommandFrame> InOrder()
    {
        foreach (KeyValuePair<uint, CommandFrame> kv in Frames)
            yield return kv.Value;
    }

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write(Frames.Count);
            foreach (KeyValuePair<uint, CommandFrame> kv in Frames)
                CommandSerializer.WriteFrame(w, kv.Value);
        }
        return ms.ToArray();
    }

    public static CommandLog Deserialize(byte[] bytes)
    {
        var log = new CommandLog();
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);
        int count = r.ReadInt32();
        for (int i = 0; i < count; ++i)
            log.Record(CommandSerializer.ReadFrame(r));
        return log;
    }
}

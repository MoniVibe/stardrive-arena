using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SDLockstep;
using SDUtils;

namespace Ship_Game.Multiplayer.Authoritative;

public sealed class Authoritative4XReceivedSave
{
    public readonly FileInfo SaveFile;
    public readonly FileInfo MetadataFile;
    public readonly Authoritative4XSessionMetadata Metadata;
    public readonly string Sha256;
    public readonly string Reason;

    public Authoritative4XReceivedSave(FileInfo saveFile, FileInfo metadataFile,
        Authoritative4XSessionMetadata metadata, string sha256, string reason)
    {
        SaveFile = saveFile;
        MetadataFile = metadataFile;
        Metadata = metadata;
        Sha256 = sha256 ?? "";
        Reason = reason ?? "";
    }
}

public static class Authoritative4XSaveTransfer
{
    public const int DefaultChunkSize = 256 * 1024;
    public const int MaxSaveBytes = 256 * 1024 * 1024;

    public static LockstepMessage[] CreateMessages(FileInfo saveFile,
        Authoritative4XSessionMetadata metadata, int fromPeer, int transferId,
        string reason = "", int chunkSize = DefaultChunkSize)
    {
        if (saveFile == null)
            throw new ArgumentNullException(nameof(saveFile));
        if (!saveFile.Exists)
            throw new FileNotFoundException($"Authoritative save file was not found: {saveFile.FullName}");
        if (metadata == null)
            throw new ArgumentNullException(nameof(metadata));
        if (chunkSize <= 0 || chunkSize > DefaultChunkSize)
            throw new ArgumentOutOfRangeException(nameof(chunkSize),
                $"Chunk size must be between 1 and {DefaultChunkSize}.");

        byte[] bytes = File.ReadAllBytes(saveFile.FullName);
        if (bytes.Length > MaxSaveBytes)
            throw new InvalidDataException($"Authoritative save is too large to transfer ({bytes.Length} bytes).");

        string sha = Sha256(bytes);
        int totalChunks = Math.Max(1, (bytes.Length + chunkSize - 1) / chunkSize);
        var messages = new List<LockstepMessage>(totalChunks + 2)
        {
            new AuthoritativeSaveTransferBeginMessage
            {
                FromPeer = fromPeer,
                TransferId = transferId,
                TotalBytes = bytes.Length,
                TotalChunks = totalChunks,
                ChunkSize = chunkSize,
                SaveFileName = Path.GetFileName(saveFile.Name),
                MetadataYaml = Authoritative4XSessionSave.SerializeMetadata(metadata),
                Sha256 = sha,
                Reason = reason ?? "",
            }
        };

        for (int i = 0; i < totalChunks; ++i)
        {
            int offset = i * chunkSize;
            int count = Math.Min(chunkSize, bytes.Length - offset);
            byte[] chunk = new byte[count];
            Array.Copy(bytes, offset, chunk, 0, count);
            messages.Add(new AuthoritativeSaveTransferChunkMessage
            {
                FromPeer = fromPeer,
                TransferId = transferId,
                ChunkIndex = i,
                Offset = offset,
                Data = chunk,
            });
        }

        messages.Add(new AuthoritativeSaveTransferEndMessage
        {
            FromPeer = fromPeer,
            TransferId = transferId,
            Sha256 = sha,
        });
        return messages.ToArray();
    }

    public static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes ?? Array.Empty<byte>()));
}

public sealed class Authoritative4XSaveTransferReceiver
{
    readonly DirectoryInfo Root;
    Transfer InFlight;

    public Authoritative4XSaveTransferReceiver(DirectoryInfo root = null)
    {
        Root = root ?? new DirectoryInfo(Path.Combine(Path.GetTempPath(), "stardrive-auth4x-transfers"));
    }

    public bool TryAccept(LockstepMessage message, out Authoritative4XReceivedSave received,
        out string error)
    {
        received = null;
        error = "";
        try
        {
            switch (message)
            {
                case AuthoritativeSaveTransferBeginMessage begin:
                    Begin(begin);
                    return false;
                case AuthoritativeSaveTransferChunkMessage chunk:
                    AcceptChunk(chunk);
                    return false;
                case AuthoritativeSaveTransferEndMessage end:
                    received = End(end);
                    return received != null;
                default:
                    return false;
            }
        }
        catch (Exception e)
        {
            InFlight = null;
            error = e.Message;
            return false;
        }
    }

    void Begin(AuthoritativeSaveTransferBeginMessage begin)
    {
        if (begin == null)
            throw new ArgumentNullException(nameof(begin));
        if (begin.TransferId <= 0)
            throw new InvalidDataException("Authoritative save transfer id must be positive.");
        if (begin.TotalBytes < 0 || begin.TotalBytes > Authoritative4XSaveTransfer.MaxSaveBytes)
            throw new InvalidDataException($"Invalid authoritative save transfer size {begin.TotalBytes}.");
        if (begin.TotalChunks <= 0)
            throw new InvalidDataException("Authoritative save transfer must contain at least one chunk.");
        if (begin.ChunkSize <= 0 || begin.ChunkSize > Authoritative4XSaveTransfer.DefaultChunkSize)
            throw new InvalidDataException($"Invalid authoritative save chunk size {begin.ChunkSize}.");

        Authoritative4XSessionMetadata metadata =
            Authoritative4XSessionSave.DeserializeMetadata(begin.MetadataYaml);
        InFlight = new Transfer(begin.TransferId, begin.TotalBytes, begin.TotalChunks,
            begin.ChunkSize, Path.GetFileName(begin.SaveFileName.NotEmpty() ? begin.SaveFileName : "mp-session.sav"),
            begin.Sha256 ?? "", begin.Reason ?? "", metadata);
    }

    void AcceptChunk(AuthoritativeSaveTransferChunkMessage chunk)
    {
        Transfer transfer = RequireTransfer(chunk?.TransferId ?? 0);
        if (chunk.ChunkIndex < 0 || chunk.ChunkIndex >= transfer.Received.Length)
            throw new InvalidDataException($"Invalid authoritative save chunk index {chunk.ChunkIndex}.");
        if (transfer.Received[chunk.ChunkIndex])
            return;

        byte[] data = chunk.Data ?? Array.Empty<byte>();
        if (chunk.Offset < 0 || chunk.Offset + data.Length > transfer.Buffer.Length)
            throw new InvalidDataException($"Authoritative save chunk {chunk.ChunkIndex} is out of range.");
        Array.Copy(data, 0, transfer.Buffer, chunk.Offset, data.Length);
        transfer.Received[chunk.ChunkIndex] = true;
    }

    Authoritative4XReceivedSave End(AuthoritativeSaveTransferEndMessage end)
    {
        Transfer transfer = RequireTransfer(end?.TransferId ?? 0);
        if (transfer.Received.Any(received => !received))
            throw new InvalidDataException("Authoritative save transfer ended before all chunks arrived.");

        string sha = Authoritative4XSaveTransfer.Sha256(transfer.Buffer);
        string expected = (end.Sha256.NotEmpty() ? end.Sha256 : transfer.Sha256) ?? "";
        if (!string.Equals(sha, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Authoritative save transfer checksum mismatch {sha} != {expected}.");

        DirectoryInfo dir = new(Path.Combine(Root.FullName,
            $"transfer-{transfer.TransferId}-{Guid.NewGuid():N}"));
        dir.Create();
        var saveFile = new FileInfo(Path.Combine(dir.FullName, transfer.SaveFileName));
        File.WriteAllBytes(saveFile.FullName, transfer.Buffer);
        FileInfo metadataFile = Authoritative4XSessionSave.MetadataFileFor(saveFile);
        Authoritative4XSessionSave.SaveMetadata(metadataFile, transfer.Metadata);
        InFlight = null;
        return new Authoritative4XReceivedSave(saveFile, metadataFile, transfer.Metadata, sha, transfer.Reason);
    }

    Transfer RequireTransfer(int transferId)
    {
        if (InFlight == null || InFlight.TransferId != transferId)
            throw new InvalidDataException($"No active authoritative save transfer {transferId}.");
        return InFlight;
    }

    sealed class Transfer
    {
        public readonly int TransferId;
        public readonly string SaveFileName;
        public readonly string Sha256;
        public readonly string Reason;
        public readonly Authoritative4XSessionMetadata Metadata;
        public readonly byte[] Buffer;
        public readonly bool[] Received;

        public Transfer(int transferId, int totalBytes, int totalChunks, int chunkSize,
            string saveFileName, string sha256, string reason, Authoritative4XSessionMetadata metadata)
        {
            TransferId = transferId;
            SaveFileName = saveFileName.NotEmpty() ? saveFileName : "mp-session.sav";
            Sha256 = sha256 ?? "";
            Reason = reason ?? "";
            Metadata = metadata;
            Buffer = new byte[totalBytes];
            Received = new bool[totalChunks];
        }
    }
}

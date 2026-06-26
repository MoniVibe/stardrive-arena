using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SDUtils.Deterministic;

namespace Ship_Game.Multiplayer.Authoritative;

public sealed class Authoritative4XLiveTelemetry : IDisposable
{
    public static string OutputDirectoryOverride;
    public static bool? EnabledOverride;

    readonly object Sync = new();
    readonly StreamWriter SessionWriter;
    readonly StreamWriter LastWriter;

    public readonly string SessionPath;
    public readonly string LastSessionPath;

    Authoritative4XLiveTelemetry(string sessionPath, string lastSessionPath)
    {
        SessionPath = sessionPath;
        LastSessionPath = lastSessionPath;
        SessionWriter = new StreamWriter(new FileStream(sessionPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
        LastWriter = new StreamWriter(new FileStream(lastSessionPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    public static Authoritative4XLiveTelemetry Start(Authoritative4XLiveRole role, int localPeerId,
        int localEmpireId, IReadOnlyDictionary<int, int> empireByPeer, int[] humanEmpireIds)
    {
        if (!IsEnabled())
            return null;

        string dir = string.IsNullOrWhiteSpace(OutputDirectoryOverride)
            ? Path.Combine(Directory.GetCurrentDirectory(), "sim-output")
            : OutputDirectoryOverride;
        Directory.CreateDirectory(dir);

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        string roleText = role.ToString().ToLowerInvariant();
        string unique = Guid.NewGuid().ToString("N")[..8];
        string sessionPath = Path.Combine(dir,
            $"authoritative-4x-{stamp}-{roleText}-peer{localPeerId}-{Environment.ProcessId}-{unique}.log");
        string lastPath = Path.Combine(dir, $"authoritative-4x-last-{roleText}.log");
        var telemetry = new Authoritative4XLiveTelemetry(sessionPath, lastPath);
        telemetry.Write("BEGIN",
            $"role={role} peer={localPeerId} empire={localEmpireId} localTime={DateTime.Now:O} "
            + $"utc={DateTime.UtcNow:O} pid={Environment.ProcessId} machine={Environment.MachineName}");
        telemetry.Write("ENV",
            $"game='{GlobalStats.ExtendedVersionNoHash}' mod='{GlobalStats.ModName}' "
            + $"modVersion='{GlobalStats.ModVersion}' runtime='{Environment.Version}' "
            + $"os='{Environment.OSVersion}' processors={Environment.ProcessorCount}");
        telemetry.Write("PEERS",
            $"empireByPeer='{PeerMap(empireByPeer)}' humanEmpires='{string.Join(",", humanEmpireIds ?? Array.Empty<int>())}'");
        telemetry.Write("PATHS", $"session='{sessionPath}' last='{lastPath}'");
        return telemetry;
    }

    public void Event(string name, string details = "")
        => Write(name, details);

    public void Command(string source, int peerId, AuthoritativePlayerCommand command)
    {
        if (command == null)
            return;
        Write("COMMAND",
            $"source={source} peer={peerId} seq={command.Sequence} empire={command.EmpireId} "
            + $"kind={command.Kind} subject={command.SubjectId} target={command.TargetId} "
            + $"pos=({command.Position.X:0.###},{command.Position.Y:0.###}) name='{command.Text ?? ""}'");
    }

    public void Result(AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot)
    {
        if (result == null)
            return;
        string hash = snapshot == null ? "" : $"0x{snapshot.HashHi:X16}:0x{snapshot.HashLo:X16}";
        Write("RESULT",
            $"origin={result.OriginPeer} seq={result.Sequence} tick={result.Tick} "
            + $"accepted={result.Accepted} reason='{result.Reason ?? ""}' hash={hash} "
            + $"digest='{snapshot?.SyncDigest ?? ""}'");
        Snapshot(snapshot);
    }

    public void Snapshot(AuthoritativeStateSnapshot snapshot)
    {
        if (snapshot?.Payload == null)
            return;

        var payloadHash = DetHash.New();
        payloadHash.AddString(snapshot.Payload);
        Write("SNAPSHOT",
            $"tick={snapshot.Tick} digest='{snapshot.SyncDigest}' payloadHash=0x{payloadHash.Value:X16} "
            + $"payloadChars={snapshot.Payload.Length} rows='{PayloadRowCounts(snapshot.Payload)}'");
    }

    public void Control(string source, bool paused, float gameSpeed)
        => Write("CONTROL", $"source={source} paused={paused} speed={gameSpeed:0.###}");

    public void Popup(AuthoritativeDiplomacyPopup popup)
    {
        if (popup == null)
            return;
        Write("POPUP",
            $"proposal={popup.ProposalId} type={popup.ProposalType} proposer={popup.ProposerEmpireId} "
            + $"target={popup.TargetEmpireId} response={popup.RequiresResponse} message='{popup.Message ?? ""}'");
    }

    public void NetworkError(string error)
    {
        if (!string.IsNullOrWhiteSpace(error))
            Write("NETWORK_ERROR", error);
    }

    public void Dispose()
    {
        Write("END", $"utc={DateTime.UtcNow:O}");
        SessionWriter.Dispose();
        LastWriter.Dispose();
    }

    void Write(string name, string details)
    {
        string line = $"{DateTime.UtcNow:O} {name} {details ?? ""}".TrimEnd();
        lock (Sync)
        {
            SessionWriter.WriteLine(line);
            LastWriter.WriteLine(line);
        }
    }

    static bool IsEnabled()
    {
        if (EnabledOverride.HasValue)
            return EnabledOverride.Value;
        return !AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => string.Equals(a.GetName().Name, "UnitTests", StringComparison.OrdinalIgnoreCase));
    }

    static string PeerMap(IReadOnlyDictionary<int, int> empireByPeer)
    {
        if (empireByPeer == null || empireByPeer.Count == 0)
            return "";
        return string.Join(",", empireByPeer.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));
    }

    static string PayloadRowCounts(string payload)
    {
        if (string.IsNullOrEmpty(payload))
            return "";

        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string line in payload.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            int pipe = line.IndexOf('|');
            string key = pipe > 0 ? line[..pipe] : line;
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        return string.Join(",", counts.Select(kv => $"{kv.Key}:{kv.Value}"));
    }
}

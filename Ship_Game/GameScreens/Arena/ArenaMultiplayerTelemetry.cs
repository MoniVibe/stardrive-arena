using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using SDLockstep;

namespace Ship_Game.GameScreens.Arena;

public sealed class ArenaMultiplayerTelemetry : IDisposable
{
    public static string OutputDirectoryOverride;

    readonly object Sync = new();
    readonly StreamWriter SessionWriter;
    readonly StreamWriter LastWriter;

    public readonly string SessionPath;
    public readonly string LastSessionPath;

    ArenaMultiplayerTelemetry(string sessionPath, string lastSessionPath)
    {
        SessionPath = sessionPath;
        LastSessionPath = lastSessionPath;
        SessionWriter = new StreamWriter(new FileStream(sessionPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };
        LastWriter = new StreamWriter(new FileStream(lastSessionPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };
    }

    public static ArenaMultiplayerTelemetry Start(string role, string surface, ArenaMultiplayerSettings settings)
    {
        string dir = string.IsNullOrWhiteSpace(OutputDirectoryOverride)
            ? Path.Combine(Directory.GetCurrentDirectory(), "sim-output")
            : OutputDirectoryOverride;
        Directory.CreateDirectory(dir);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        string safeRole = Safe(role);
        string safeSurface = Safe(surface);
        string unique = Guid.NewGuid().ToString("N")[..8];
        string sessionPath = Path.Combine(dir,
            $"arena-multiplayer-{stamp}-{safeSurface}-{safeRole}-{Environment.ProcessId}-{unique}.log");
        string lastPath = Path.Combine(dir, "arena-multiplayer-last-session.log");
        var telemetry = new ArenaMultiplayerTelemetry(sessionPath, lastPath);
        telemetry.Write("BEGIN",
            $"surface={surface} role={role} localTime={DateTime.Now:O} utc={DateTime.UtcNow:O} "
            + $"pid={Environment.ProcessId} machine={Environment.MachineName}");
        telemetry.Write("ENV",
            $"game='{GlobalStats.ExtendedVersionNoHash}' mod='{GlobalStats.ModName}' modVersion='{GlobalStats.ModVersion}' "
            + $"env={ArenaMultiplayerPeerSignature.EnvironmentHash()} summary='{ArenaMultiplayerPeerSignature.EnvironmentSummary()}'");
        if (settings != null)
        {
            ArenaMultiplayerSettings s = settings.WithResolvedFleets();
            telemetry.Write("SETTINGS",
                $"protocol={ArenaMultiplayerSettings.ProtocolVersion} matchSeed={s.MatchSeed} rngSeed={s.RngSeed} "
                + $"turns={s.MaxTurns} inputDelay={s.InputDelay} commandEvery={s.CommandEveryTurns} "
                + $"speed={s.GameSpeed:0.###} startPaused={s.StartPaused} settingsHash={s.SettingsHash} "
                + $"sessionHash={ArenaMultiplayerPeerSignature.Hash(s)}");
            telemetry.Write("FLEETS",
                $"hostRace='{s.HostRacePreference}' joinRace='{s.JoinRacePreference}' "
                + $"host=[{string.Join(",", s.HostFleetDesignNames)}] join=[{string.Join(",", s.JoinFleetDesignNames)}]");
        }
        telemetry.Write("PATHS", $"session='{sessionPath}' last='{lastPath}'");
        return telemetry;
    }

    public void Event(string name, string details = "")
        => Write(name, details);

    public void Status(string status)
        => Write("STATUS", status ?? "");

    public void NetworkError(string error)
        => Write("NETWORK_ERROR", error ?? "");

    public void Turn(uint turn, ArenaMultiplayerRole role, string hash, uint simTick,
        long remoteChecksumTick, int commandsSubmitted, int playerAlive, int enemyAlive, bool forced = false)
    {
        if (!forced && turn > 5 && turn % 60 != 0)
            return;

        Write("TURN",
            $"turn={turn} role={role} simTick={simTick} remoteChecksumTick={remoteChecksumTick} "
            + $"hash={hash} commandsSubmitted={commandsSubmitted} playerAlive={playerAlive} enemyAlive={enemyAlive}");
    }

    public void Desync(uint observedTurn, ArenaMultiplayerRunResult result, DesyncDetector desync)
    {
        string detail = desync?.HasDesync == true
            ? ArenaMultiplayerSession.DesyncSummary(desync)
            : result?.DesyncReason ?? "";
        Write("DESYNC",
            $"observedTurn={observedTurn} resultTurn={result?.DesyncTurn ?? -1} "
            + $"final={result?.FinalHash ?? ""} detail='{detail}'");
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

    static string Safe(string text)
    {
        text = string.IsNullOrWhiteSpace(text) ? "unknown" : text.ToLowerInvariant();
        foreach (char c in Path.GetInvalidFileNameChars())
            text = text.Replace(c, '-');
        return text;
    }
}

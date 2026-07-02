using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using SDLockstep;
using SDUtils.Deterministic;
using Ship_Game;
using Ship_Game.AI;
using Ship_Game.Commands.Goals;
using Ship_Game.Data;
using Ship_Game.Determinism;
using Ship_Game.Gameplay;
using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.Ships;
using Ship_Game.SpriteSystem;
using Ship_Game.Universe.SolarBodies;
using SynapseGaming.LightingSystem.Core;
using Vector2 = SDGraphics.Vector2;

namespace StarDrive.Tools.Authoritative4XProbe;

static class Program
{
    static int Main(string[] args)
    {
        AuthoritativeProbeOptions options;
        try
        {
            options = AuthoritativeProbeOptions.Parse(args);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(AuthoritativeProbeOptions.Usage);
            return 2;
        }

        if (options.Help)
        {
            Console.WriteLine(AuthoritativeProbeOptions.Usage);
            return 0;
        }

        try
        {
            using var bootstrap = GameContentBootstrap.Load(options);
            if (options.SelfTest)
            {
                AuthoritativeProbeResult result = AuthoritativeProbeRunner.RunSelfTest(options);
                Console.WriteLine(result.Summary);
                return result.Passed ? 0 : 3;
            }

            if (options.LiveParity)
            {
                LiveParityVerdict verdict = AuthoritativeLiveParityProbe.Run(options);
                string json = LiveParityJson.WriteAndValidate(verdict, options);
                Console.WriteLine(json);
                return verdict.Passed ? 0 : 3;
            }

            AuthoritativeProbeResult run = options.Role switch
            {
                AuthoritativeProbeRole.Host => AuthoritativeProbeRunner.RunHost(options),
                AuthoritativeProbeRole.Join => AuthoritativeProbeRunner.RunJoin(options),
                _ => throw new ArgumentException("Set --role host|join or SD_AUTH4X_ROLE=host|join."),
            };

            Console.WriteLine(run.Summary);
            return run.Passed ? 0 : 3;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("[auth4x-probe] failed");
            Console.Error.WriteLine(e);
            return 1;
        }
        finally
        {
            Log.Close();
        }
    }
}

enum AuthoritativeProbeRole
{
    None,
    Host,
    Join,
}

sealed class AuthoritativeProbeOptions
{
    public AuthoritativeProbeRole Role;
    public bool Help;
    public bool SelfTest;
    public bool LiveParity;
    public string Host = "127.0.0.1";
    public int Port = 47377;
    public int Turns = 600;
    public int TimeoutSeconds = 180;
    public int Seed = 54545;
    public int ClientCount = 4;
    public int HazardLatencyMs = 8;
    public int HazardJitterMs = 3;
    public int HazardBurstEvery = 17;
    public int HazardBurstDelayMs = 35;
    public int ForceDriftSequence = 6;
    public int ForceResyncSequence = 12;
    public string GameRoot = "";
    public string ModPath = "Mods/Combined Arms";
    public string OutputDir = "";
    public string JsonOutput = "";
    public string HostRace = "";
    public string JoinRace = "";
    public string HostTraits = "";
    public string JoinTraits = "";
    public float GameSpeed = 1f;

    public static string Usage =>
        """
        Authoritative4XProbe --role host|join [options]

        Options:
          --role host|join       Host listens or join connects. Env: SD_AUTH4X_ROLE.
          --live-parity          Run the in-process loopback TCP live parity harness and emit JSON.
          --clients <2|4|8>      Total human peers for --live-parity, including host. Default 4.
          --host <ip>            Host IP for join mode. Env: SD_AUTH4X_HOST.
          --port <port>          TCP port. Default 47377. Env: SD_AUTH4X_PORT.
          --turns <n>            Scripted authoritative turns. Default 600. Env: SD_AUTH4X_TURNS.
          --timeout <seconds>    Wait timeout. Default 180.
          --hazard-latency-ms <n> Base TCP write delay for --live-parity. Default 8.
          --hazard-jitter-ms <n>  Seeded +/- jitter around base delay. Default 3.
          --hazard-burst-every <n> Delay every nth TCP write. Default 17.
          --hazard-burst-delay-ms <n> Extra burst delay. Default 35.
          --force-drift-seq <n>  Sequence where --live-parity mutates one client replica. Default 6.
          --force-resync-seq <n> Sequence where --live-parity triggers a clean multi-client resync. Default 12.
          --json-output <path>   JSON verdict path. Default under --output.
          --game-root <path>     Folder containing Content/ and Mods/. Defaults to ./game or current dir.
          --mod <path>           Mod folder relative to game root. Default "Mods/Combined Arms". Use "" for vanilla.
          --output <path>        Artifact folder. Default ./sim-output/authoritative4x-probe.
          --seed <n>             Deterministic generation seed. Default 54545.
          --host-race <name>     Host race. Default first loaded race.
          --join-race <name>     Join race. Default second loaded race.
          --host-traits <a|b>    Pipe/comma/semicolon-separated trait names.
          --join-traits <a|b>    Pipe/comma/semicolon-separated trait names.
          --game-speed <n>       0.25..8. Default 1.
          --self-test            Run the existing loopback lobby self-test after content load.
          --help                 Show this help.

        Diagnostics:
          SD_AUTH4X_WIRE_TRACE=1 enables verbose per-frame transport tracing.

        Example:
          Authoritative4XProbe.exe --role host --port 47377 --turns 1600 --game-root "C:\Games\StarDrive2"
          Authoritative4XProbe.exe --role join --host 192.0.2.10 --port 47377 --turns 1600 --game-root "C:\Games\StarDrive2"
          Authoritative4XProbe.exe --live-parity --clients 4 --turns 24 --game-root "C:\Games\StarDrive2"
        """;

    public static AuthoritativeProbeOptions Parse(string[] args)
    {
        var o = new AuthoritativeProbeOptions
        {
            Role = ParseRole(Environment.GetEnvironmentVariable("SD_AUTH4X_ROLE")),
            Host = Environment.GetEnvironmentVariable("SD_AUTH4X_HOST") ?? "127.0.0.1",
            GameRoot = Environment.GetEnvironmentVariable("SD_AUTH4X_GAME_ROOT") ?? "",
            ModPath = Environment.GetEnvironmentVariable("SD_AUTH4X_MOD") ?? "Mods/Combined Arms",
            OutputDir = Environment.GetEnvironmentVariable("SD_AUTH4X_OUTPUT") ?? "",
            JsonOutput = Environment.GetEnvironmentVariable("SD_AUTH4X_JSON_OUTPUT") ?? "",
            HostRace = Environment.GetEnvironmentVariable("SD_AUTH4X_HOST_RACE") ?? "",
            JoinRace = Environment.GetEnvironmentVariable("SD_AUTH4X_JOIN_RACE") ?? "",
            HostTraits = Environment.GetEnvironmentVariable("SD_AUTH4X_HOST_TRAITS") ?? "",
            JoinTraits = Environment.GetEnvironmentVariable("SD_AUTH4X_JOIN_TRAITS") ?? "",
        };
        if (int.TryParse(Environment.GetEnvironmentVariable("SD_AUTH4X_PORT"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int port))
            o.Port = port;
        if (int.TryParse(Environment.GetEnvironmentVariable("SD_AUTH4X_TURNS"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int turns))
            o.Turns = turns;
        if (int.TryParse(Environment.GetEnvironmentVariable("SD_AUTH4X_SEED"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int seed))
            o.Seed = seed;
        if (int.TryParse(Environment.GetEnvironmentVariable("SD_AUTH4X_CLIENTS"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int clients))
            o.ClientCount = clients;

        for (int i = 0; i < args.Length; ++i)
        {
            string arg = args[i];
            if (arg is "-h" or "--help" or "/?")
            {
                o.Help = true;
                continue;
            }
            if (arg == "--self-test")
            {
                o.SelfTest = true;
                continue;
            }
            if (arg == "--live-parity")
            {
                o.LiveParity = true;
                continue;
            }

            string value = Value(args, ref i, arg);
            switch (Key(arg))
            {
                case "--role": o.Role = ParseRole(value); break;
                case "--clients": o.ClientCount = ParseInt(value, "--clients"); break;
                case "--host": o.Host = value; break;
                case "--port": o.Port = ParseInt(value, "--port"); break;
                case "--turns": o.Turns = ParseInt(value, "--turns"); break;
                case "--timeout": o.TimeoutSeconds = ParseInt(value, "--timeout"); break;
                case "--seed": o.Seed = ParseInt(value, "--seed"); break;
                case "--hazard-latency-ms": o.HazardLatencyMs = ParseInt(value, "--hazard-latency-ms"); break;
                case "--hazard-jitter-ms": o.HazardJitterMs = ParseInt(value, "--hazard-jitter-ms"); break;
                case "--hazard-burst-every": o.HazardBurstEvery = ParseInt(value, "--hazard-burst-every"); break;
                case "--hazard-burst-delay-ms": o.HazardBurstDelayMs = ParseInt(value, "--hazard-burst-delay-ms"); break;
                case "--force-drift-seq": o.ForceDriftSequence = ParseInt(value, "--force-drift-seq"); break;
                case "--force-resync-seq": o.ForceResyncSequence = ParseInt(value, "--force-resync-seq"); break;
                case "--game-root": o.GameRoot = value; break;
                case "--mod": o.ModPath = value; break;
                case "--output": o.OutputDir = value; break;
                case "--json-output": o.JsonOutput = value; break;
                case "--host-race": o.HostRace = value; break;
                case "--join-race": o.JoinRace = value; break;
                case "--host-traits": o.HostTraits = value; break;
                case "--join-traits": o.JoinTraits = value; break;
                case "--game-speed": o.GameSpeed = ParseFloat(value, "--game-speed"); break;
                default: throw new ArgumentException($"Unknown argument '{arg}'.");
            }
        }

        if (o.SelfTest)
            return o;
        if (o.LiveParity)
        {
            if (o.ClientCount is not (2 or 4 or 8))
                throw new ArgumentException("--clients must be one of 2, 4, or 8.");
            if (o.Turns < Math.Max(ProbeCommandPlan.RequiredTurns, o.ForceResyncSequence + 2))
                throw new ArgumentException("--turns must run past the forced drift and resync sequences.");
            if (o.HazardLatencyMs < 0 || o.HazardJitterMs < 0 || o.HazardBurstEvery < 0 || o.HazardBurstDelayMs < 0)
                throw new ArgumentException("Hazard delay options must be non-negative.");
            if (o.ForceDriftSequence <= 1 || o.ForceResyncSequence <= o.ForceDriftSequence)
                throw new ArgumentException("--force-resync-seq must be greater than --force-drift-seq, and drift must be after sequence 1.");
            return o;
        }
        if (o.Role == AuthoritativeProbeRole.None)
            throw new ArgumentException("Missing --role host|join.");
        if (o.Port is <= 0 or > 65535)
            throw new ArgumentException("--port must be 1..65535.");
        if (o.Turns <= 0)
            throw new ArgumentException("--turns must be positive.");
        if (o.TimeoutSeconds <= 0)
            throw new ArgumentException("--timeout must be positive.");
        return o;
    }

    static string Key(string arg)
    {
        int eq = arg.IndexOf('=');
        return eq < 0 ? arg : arg[..eq];
    }

    static string Value(string[] args, ref int i, string arg)
    {
        int eq = arg.IndexOf('=');
        if (eq >= 0)
            return arg[(eq + 1)..];
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Argument '{arg}' requires a value.");
        return args[++i];
    }

    static int ParseInt(string value, string name)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new ArgumentException($"{name} must be an integer.");

    static float ParseFloat(string value, string name)
        => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : throw new ArgumentException($"{name} must be a number.");

    static AuthoritativeProbeRole ParseRole(string? value)
        => (value ?? "").Trim().ToLowerInvariant() switch
        {
            "host" => AuthoritativeProbeRole.Host,
            "join" or "client" => AuthoritativeProbeRole.Join,
            "" => AuthoritativeProbeRole.None,
            _ => throw new ArgumentException("Role must be host or join."),
        };
}

sealed class GameContentBootstrap : IDisposable
{
    readonly GameDummy Game;
    readonly string PreviousCwd;
    readonly string PreviousAtlasAppDataOverride;

    GameContentBootstrap(GameDummy game, string previousCwd,
        string previousAtlasAppDataOverride)
    {
        Game = game;
        PreviousCwd = previousCwd;
        PreviousAtlasAppDataOverride = previousAtlasAppDataOverride;
    }

    public static GameContentBootstrap Load(AuthoritativeProbeOptions options)
    {
        string previous = Directory.GetCurrentDirectory();
        string previousAtlasAppDataOverride = AtlasPath.StarDriveAppDataOverride;
        string root = ResolveGameRoot(options.GameRoot);
        if (string.IsNullOrWhiteSpace(options.OutputDir))
            options.OutputDir = Path.Combine(AppContext.BaseDirectory, "sim-output", "authoritative4x-probe");
        options.OutputDir = Path.GetFullPath(options.OutputDir);
        Directory.CreateDirectory(options.OutputDir);
        InstallPrivateUserConfigRoot(options);
        InstallPrivateTextureCacheRoot(options);
        Directory.SetCurrentDirectory(root);

        GlobalStats.SuppressConfigWrites = true;
        LoadConfigOrDefaults();
        InstallProbeLog(options.OutputDir);
        Log.Initialize(enableSentry: false, showHeader: false);
        Log.VerboseLogging = true;
        GlobalStats.MaxParallelism = 1;
        GlobalStats.DrawStarfield = false;
        GlobalStats.DrawNebulas = false;
        GlobalStats.AsteroidVisibility = ObjectVisibility.None;

        var game = new GameDummy(1024, 768, show: false);
        game.Create();
        Directory.CreateDirectory(SavedGame.DefaultSaveGameFolder);

        LoadModNoSave(options.ModPath);

        ResourceManager.InitContentDir();
        ResourceManager.UnloadAllData(ScreenManager.Instance);
        if (options.LiveParity)
        {
            ResourceManager.LoadContentForTesting();
            ResourceManager.LoadAllShipDesigns();
        }
        else
        {
            ResourceManager.LoadItAll(ScreenManager.Instance, GlobalStats.ActiveMod);
        }
        if (!string.IsNullOrWhiteSpace(options.ModPath) && !GlobalStats.HasMod)
            throw new InvalidOperationException($"Requested mod '{options.ModPath}' did not stay active after content load.");
        if (ResourceManager.MajorRaces.Count < 2)
            throw new InvalidOperationException("Authoritative probe needs at least two major races.");

        options.GameRoot = root;
        return new GameContentBootstrap(game, previous,
            previousAtlasAppDataOverride);
    }

    static void InstallPrivateUserConfigRoot(AuthoritativeProbeOptions options)
    {
        string role = options.Role == AuthoritativeProbeRole.None
            ? options.LiveParity ? "live-parity" : "self-test"
            : options.Role.ToString().ToLowerInvariant();
        string root = Path.Combine(options.OutputDir, "user-config", role);
        string roaming = Path.Combine(root, "Roaming");
        string local = Path.Combine(root, "Local");
        Directory.CreateDirectory(roaming);
        Directory.CreateDirectory(local);
        Environment.SetEnvironmentVariable("APPDATA", roaming);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", local);
    }

    static void InstallPrivateTextureCacheRoot(AuthoritativeProbeOptions options)
    {
        string root = Path.Combine(options.OutputDir, "user-config", "StarDrive");
        Directory.CreateDirectory(root);
        AtlasPath.StarDriveAppDataOverride = root;
    }

    public void Dispose()
    {
        ResourceManager.WaitForExit();
        Game.Dispose();
        AtlasPath.StarDriveAppDataOverride = PreviousAtlasAppDataOverride;
        Directory.SetCurrentDirectory(PreviousCwd);
    }

    static string ResolveGameRoot(string requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return FullExistingRoot(requested);

        string cwd = Directory.GetCurrentDirectory();
        if (Directory.Exists(Path.Combine(cwd, "Content")))
            return cwd;
        string game = Path.Combine(cwd, "game");
        if (Directory.Exists(Path.Combine(game, "Content")))
            return game;
        string repoGame = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "game"));
        if (Directory.Exists(Path.Combine(repoGame, "Content")))
            return repoGame;
        throw new DirectoryNotFoundException("Could not locate game root. Pass --game-root pointing at the folder that contains Content/.");
    }

    static void InstallProbeLog(string outputDir)
    {
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string path = Path.Combine(outputDir, $"blackbox-probe-{Environment.MachineName}-{Process.GetCurrentProcess().Id}-{stamp}.log");
        var writer = new StreamWriter(File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read),
            Encoding.ASCII, 32 * 1024)
        {
            AutoFlush = true
        };
        Type log = typeof(Log);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        log.GetField("LogFile", BindingFlags.Static | BindingFlags.NonPublic)
           ?.SetValue(null, writer);
        log.GetProperty(nameof(Log.LogFilePath), flags)
           ?.SetValue(null, path);
        log.GetProperty(nameof(Log.OldLogFilePath), flags)
           ?.SetValue(null, path + ".old");
    }

    static void LoadConfigOrDefaults()
    {
        try
        {
            GlobalStats.LoadConfig();
        }
        catch (ConfigurationErrorsException e) when (IsUserConfigAccessError(e))
        {
            Console.Error.WriteLine("[auth4x-probe] user config unavailable; continuing with content defaults: "
                                    + (e.InnerException?.Message ?? e.Message));
            GlobalStats.VanillaDefaults ??= GamePlayGlobals.Deserialize(new FileInfo("Content/Globals.yaml"));
            GlobalStats.SetActiveModNoSave(null);
        }
    }

    static bool IsUserConfigAccessError(ConfigurationErrorsException e)
        => e.InnerException is UnauthorizedAccessException
           || e.InnerException is IOException
           || e.Message.Contains("Access to the path", StringComparison.OrdinalIgnoreCase);

    static void LoadModNoSave(string modPath)
    {
        GlobalStats.SetActiveModNoSave(null);
        GlobalStats.VanillaDefaults ??= GamePlayGlobals.Deserialize(new FileInfo("Content/Globals.yaml"));
        if (string.IsNullOrWhiteSpace(modPath))
            return;

        var modInfo = new FileInfo(Path.Combine(modPath, "Globals.yaml"));
        if (!modInfo.Exists)
            throw new FileNotFoundException($"Requested mod globals were not found: {modInfo.FullName}");

        GamePlayGlobals settings = GamePlayGlobals.Deserialize(modInfo);
        GlobalStats.SetActiveModNoSave(new ModEntry(settings));
    }

    static string FullExistingRoot(string path)
    {
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(Path.Combine(full, "Content")))
            throw new DirectoryNotFoundException($"Game root '{full}' does not contain Content/.");
        return full;
    }
}

sealed class AuthoritativeProbeResult
{
    public bool Passed;
    public string Role = "";
    public int Turns;
    public int LastSequence;
    public uint LastTick;
    public string FinalHash = "";
    public string FinalDigest = "";
    public string ArtifactPath = "";
    public string Failure = "";

    public string Summary
        => Passed
            ? $"[auth4x-probe] OK role={Role} turns={Turns} seq={LastSequence} tick={LastTick} final={FinalHash}/{FinalDigest} artifact={ArtifactPath}"
            : $"[auth4x-probe] FAILED role={Role} turns={Turns} seq={LastSequence} tick={LastTick} failure={Failure} artifact={ArtifactPath}";
}

sealed class ProbeLog : IDisposable
{
    readonly StreamWriter Writer;
    readonly List<string> Lines = new();

    public string Path { get; }

    public ProbeLog(AuthoritativeProbeOptions options, string role)
    {
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        Path = System.IO.Path.Combine(options.OutputDir, $"auth4x-{role}-{stamp}-{Process.GetCurrentProcess().Id}.txt");
        Writer = new StreamWriter(File.Create(Path), Encoding.UTF8) { AutoFlush = true };
    }

    public void Line(string text)
    {
        Lines.Add(text);
        Console.WriteLine(text);
        Writer.WriteLine(text);
    }

    public bool HasCommand(string peerName, int sequence, AuthoritativePlayerCommandKind kind)
    {
        string needle = $"peer={peerName} seq={sequence} kind={kind}";
        return Lines.Any(line => line.Contains(needle, StringComparison.Ordinal)
                                 && !line.Contains("accepted=False", StringComparison.Ordinal));
    }

    public void Payload(string suffix, AuthoritativeStateSnapshot? snapshot)
    {
        if (snapshot == null)
            return;
        string path = System.IO.Path.ChangeExtension(Path, $".{suffix}.payload.txt");
        File.WriteAllText(path, snapshot.Payload ?? "", Encoding.UTF8);
        Line($"payload.{suffix}={path}");
    }

    public void Dispose() => Writer.Dispose();
}

static class AuthoritativeProbeRunner
{
    const int ProtocolVersion = 1;

    public static AuthoritativeProbeResult RunSelfTest(AuthoritativeProbeOptions options)
    {
        var flow = new Authoritative4XLobbyNetworkFlow();
        string[] races = DefaultRaces(options);
        var settings = BuildSettings(options);
        Authoritative4XLobbySelfTestResult result = flow.RunLoopbackSelfTest(settings,
            races[0], SplitTraits(options.HostTraits), races[1], SplitTraits(options.JoinTraits),
            ProtocolVersion, ProbeEnvironment.Hash(),
            ProbeEnvironment.Summary(), options.Turns);
        return new AuthoritativeProbeResult
        {
            Passed = result.Passed,
            Role = "self-test",
            Turns = options.Turns,
            LastSequence = result.CommandSequence,
            LastTick = result.CommandTick,
            FinalHash = result.FinalHash,
            FinalDigest = result.AuthorityDigest,
            Failure = result.FailureReason,
        };
    }

    public static AuthoritativeProbeResult RunHost(AuthoritativeProbeOptions options)
    {
        EnsureScriptTurns(options);
        using var log = new ProbeLog(options, "host");
        var result = new AuthoritativeProbeResult { Role = "host", Turns = options.Turns, ArtifactPath = log.Path };
        var flow = new Authoritative4XLobbyNetworkFlow();
        TcpLockstepTransport? transport = null;
        Authoritative4XGeneratedGameStart? generated = null;
        Authoritative4XNetworkHost? host = null;

        try
        {
            WriteHeader(log, options, "host");
            string[] races = DefaultRaces(options);
            var hostLobby = new Authoritative4XLobby(flow.HostPeerId, "Host");
            hostLobby.Join(flow.JoinPeerId, "Join");
            Require(hostLobby.SetSettings(flow.HostPeerId, BuildSettings(options)));
            Require(hostLobby.SetPlayerSelection(flow.HostPeerId, races[0], SplitTraits(options.HostTraits)));
            Require(hostLobby.SetReady(flow.HostPeerId, true));

            SessionLobbyMessage? receivedJoin = null;
            transport = TcpLockstepTransport.HostMulti(options.Port);
            if (Authoritative4XLiveTelemetry.IsWireTraceEnabled())
                transport.AuthoritativeFrameTrace = line => log.Line("wire " + line);
            transport.Register(flow.AuthorityPeerId, message =>
            {
                if (message is SessionLobbyMessage lobby)
                    receivedJoin = lobby;
            });
            log.Line($"listening port={options.Port}");
            if (!transport.WaitForConnections(1, TimeSpan.FromSeconds(options.TimeoutSeconds)))
                throw new TimeoutException($"No joiner connected within {options.TimeoutSeconds}s. transport='{transport.LastError}'");

            PumpUntil(() => receivedJoin != null, () => transport.Poll(), options, "join lobby");
            Require(flow.ApplyLobbyMessage(hostLobby, receivedJoin!));
            log.Line($"join peer={receivedJoin!.PeerId} race='{receivedJoin.RacePreference}' traits='{receivedJoin.TraitOptions}' build={receivedJoin.BuildHash}");

            SessionStartMessage start = flow.BuildStartMessage(hostLobby, ProtocolVersion,
                ProbeEnvironment.Hash(), ProbeEnvironment.Summary(),
                options.Turns);
            transport.Send(flow.JoinPeerId, start);
            log.Line(Authoritative4XLobbyNetworkFlow.StartTelemetrySummary(start));

            generated = flow.CreateGeneratedGame(start);
            log.Line(Authoritative4XLobbyNetworkFlow.EmpireMapTelemetrySummary(generated.EmpireIdByPeer,
                generated.HumanEmpireIds));
            host = new Authoritative4XNetworkHost(generated.AuthorityUniverse, transport,
                generated.EmpireIdByPeer, generated.HumanEmpireIds, flow.HostPeerId);

            ProbeCommandPlan plan = ProbeCommandPlan.Create(generated, flow.HostPeerId, flow.JoinPeerId);
            log.Line(plan.Describe());

            int lastJoinSeq = 0;
            int lastHostSeq = 0;
            int hostSubmittedSeq = 0;
            PumpUntil(() =>
            {
                host.Poll();
                foreach (Authoritative4XProcessedCommand processed in host.DrainProcessedCommands())
                {
                    if (processed.PeerId == flow.JoinPeerId)
                    {
                        lastJoinSeq = Math.Max(lastJoinSeq, processed.Command.Sequence);
                        result.LastSequence = lastJoinSeq;
                        result.LastTick = processed.Result.Tick;
                        result.FinalHash = SnapshotHash(processed.Snapshot);
                        result.FinalDigest = processed.Snapshot.SyncDigest;
                        if (lastJoinSeq <= ProbeCommandPlan.RequiredTurns
                            || lastJoinSeq == options.Turns
                            || ProbeCommandPlan.IsLateControlPulse(lastJoinSeq)
                            || lastJoinSeq % 100 == 0)
                            log.Line($"processed peer=join seq={lastJoinSeq} kind={processed.Command.Kind} tick={processed.Result.Tick} accepted={processed.Result.Accepted} hash={result.FinalHash}/{result.FinalDigest}");
                    }
                    else if (processed.PeerId == flow.HostPeerId)
                    {
                        lastHostSeq = Math.Max(lastHostSeq, processed.Command.Sequence);
                        result.LastTick = processed.Result.Tick;
                        result.FinalHash = SnapshotHash(processed.Snapshot);
                        result.FinalDigest = processed.Snapshot.SyncDigest;
                        log.Line($"processed peer=host seq={processed.Command.Sequence} kind={processed.Command.Kind} tick={processed.Result.Tick} accepted={processed.Result.Accepted} hash={result.FinalHash}/{result.FinalDigest}");
                    }

                    if (!processed.Result.Accepted)
                    {
                        string peer = processed.PeerId == flow.HostPeerId ? "host" : "join";
                        throw new InvalidOperationException($"Host rejected {peer} seq {processed.Command.Sequence} {processed.Command.Kind}: {processed.Result.Reason}");
                    }
                }

                while (hostSubmittedSeq < ProbeCommandPlan.RequiredTurns && lastJoinSeq >= 1)
                {
                    ++hostSubmittedSeq;
                    AuthoritativePlayerCommand command = plan.HostCommand(hostSubmittedSeq);
                    log.Line($"submit peer=host seq={hostSubmittedSeq} kind={command.Kind}");
                    host.SubmitLocal(flow.HostPeerId, command);
                }

                return lastJoinSeq >= options.Turns && lastHostSeq >= ProbeCommandPlan.RequiredTurns;
            }, static () => { }, options, $"join commands 1..{options.Turns}");

            plan.AssertApplied(generated, log, "host-authority", options.Turns);
            result.Passed = true;
            log.Line(result.Summary);
            return result;
        }
        catch (Exception e)
        {
            result.Failure = e.Message;
            log.Line(result.Summary);
            log.Line(e.ToString());
            log.Payload("authority", host?.LastAuthoritySnapshot);
            return result;
        }
        finally
        {
            host?.Dispose();
            generated?.Dispose();
            transport?.Dispose();
        }
    }

    public static AuthoritativeProbeResult RunJoin(AuthoritativeProbeOptions options)
    {
        EnsureScriptTurns(options);
        using var log = new ProbeLog(options, "join");
        var result = new AuthoritativeProbeResult { Role = "join", Turns = options.Turns, ArtifactPath = log.Path };
        var flow = new Authoritative4XLobbyNetworkFlow();
        TcpLockstepTransport? transport = null;
        Authoritative4XGeneratedGameStart? generated = null;
        Authoritative4XNetworkClient? client = null;

        try
        {
            WriteHeader(log, options, "join");
            string[] races = DefaultRaces(options);
            var joinLobby = new Authoritative4XLobby(flow.JoinPeerId, "Join");
            Require(joinLobby.SetPlayerSelection(flow.JoinPeerId, races[1], SplitTraits(options.JoinTraits)));
            Require(joinLobby.SetReady(flow.JoinPeerId, true));

            SessionStartMessage? receivedStart = null;
            transport = TcpLockstepTransport.JoinAsPeer(options.Host, options.Port, flow.JoinPeerId, flow.AuthorityPeerId);
            if (Authoritative4XLiveTelemetry.IsWireTraceEnabled())
                transport.AuthoritativeFrameTrace = line => log.Line("wire " + line);
            transport.Register(flow.JoinPeerId, message =>
            {
                if (message is SessionStartMessage start)
                    receivedStart = start;
            });
            log.Line($"connected host={options.Host}:{options.Port}");

            transport.Send(flow.AuthorityPeerId, flow.BuildLobbyMessage(joinLobby, flow.JoinPeerId,
                ProbeEnvironment.Hash(), ProbeEnvironment.Summary()));
            PumpUntil(() => receivedStart != null, () => transport.Poll(), options, "session start");
            string startError = flow.ValidateStartMessage(receivedStart!, ProtocolVersion,
                ProbeEnvironment.Hash());
            if (!string.IsNullOrEmpty(startError))
                throw new InvalidOperationException(startError);
            log.Line(Authoritative4XLobbyNetworkFlow.StartTelemetrySummary(receivedStart));

            generated = flow.CreateGeneratedGame(receivedStart!);
            log.Line(Authoritative4XLobbyNetworkFlow.EmpireMapTelemetrySummary(generated.EmpireIdByPeer,
                generated.HumanEmpireIds));
            client = new Authoritative4XNetworkClient(generated.AuthorityUniverse, transport,
                flow.JoinPeerId, generated.HumanEmpireIds);

            ProbeCommandPlan plan = ProbeCommandPlan.Create(generated, flow.HostPeerId, flow.JoinPeerId);
            log.Line(plan.Describe());
            var pendingPopups = new List<AuthoritativeDiplomacyPopup>();

            for (int seq = 1; seq <= options.Turns; ++seq)
            {
                AuthoritativePlayerCommand command = plan.RequiresTechnologyTradeResponse(seq)
                    ? WaitForTechnologyTradeAndBuildResponse(client, plan, pendingPopups, log, options, seq)
                    : plan.JoinCommand(seq);
                client.Submit(command);
                int expected = seq;
                bool sawExpected = false;
                PumpUntil(() =>
                {
                    client.Poll();
                    ThrowIfClientSyncMismatch(client);
                    pendingPopups.AddRange(client.DrainPopupsForClient());
                    foreach (Authoritative4XProcessedCommand processed in client.DrainProcessedCommands())
                    {
                        string peerName = processed.PeerId == flow.HostPeerId ? "host" : "join";
                        if (!processed.Result.Accepted)
                            throw new InvalidOperationException($"Host rejected {peerName} seq {processed.Command.Sequence} {processed.Command.Kind}: {processed.Result.Reason}");

                        if (processed.PeerId == flow.HostPeerId)
                        {
                            if (processed.Command.Sequence <= ProbeCommandPlan.RequiredTurns)
                            {
                                string rawDrift = client.LastRawHashDrift != null ? " rawHashDrift=true" : "";
                                log.Line($"applied peer=host seq={processed.Command.Sequence} kind={processed.Command.Kind} tick={processed.Result.Tick} hash={SnapshotHash(processed.Snapshot)}/{processed.Snapshot.SyncDigest}{rawDrift}");
                            }
                            continue;
                        }

                        if (processed.PeerId == flow.JoinPeerId)
                        {
                            result.LastSequence = processed.Command.Sequence;
                            result.LastTick = processed.Result.Tick;
                            result.FinalHash = SnapshotHash(processed.Snapshot);
                            result.FinalDigest = processed.Snapshot.SyncDigest;
                            if (processed.Command.Sequence <= ProbeCommandPlan.RequiredTurns
                                || processed.Command.Sequence == options.Turns
                                || ProbeCommandPlan.IsLateControlPulse(processed.Command.Sequence)
                                || processed.Command.Sequence % 100 == 0)
                            {
                                string rawDrift = client.LastRawHashDrift != null ? " rawHashDrift=true" : "";
                                log.Line($"applied peer=join seq={processed.Command.Sequence} kind={processed.Command.Kind} tick={result.LastTick} accepted={processed.Result.Accepted} hash={result.FinalHash}/{result.FinalDigest}{rawDrift}");
                            }
                            if (processed.Command.Sequence == expected)
                                sawExpected = true;
                        }
                    }
                    return sawExpected;
                }, static () => { }, options, $"result seq={expected}");
            }

            PumpUntil(() =>
            {
                client.Poll();
                ThrowIfClientSyncMismatch(client);
                foreach (Authoritative4XProcessedCommand processed in client.DrainProcessedCommands())
                {
                    string peerName = processed.PeerId == flow.HostPeerId ? "host" : "join";
                    if (!processed.Result.Accepted)
                        throw new InvalidOperationException($"Host rejected {peerName} seq {processed.Command.Sequence} {processed.Command.Kind}: {processed.Result.Reason}");
                    log.Line($"applied peer={peerName} seq={processed.Command.Sequence} kind={processed.Command.Kind} tick={processed.Result.Tick} hash={SnapshotHash(processed.Snapshot)}/{processed.Snapshot.SyncDigest}");
                    result.LastTick = processed.Result.Tick;
                    result.FinalHash = SnapshotHash(processed.Snapshot);
                    result.FinalDigest = processed.Snapshot.SyncDigest;
                }
                return plan.IsApplied(generated, options.Turns);
            }, static () => { }, options, "host and join command assertions");

            plan.AssertApplied(generated, log, "join-replica", options.Turns);
            result.Passed = true;
            log.Line(result.Summary);
            return result;
        }
        catch (Authoritative4XSyncMismatchException e)
        {
            result.Failure = e.Message;
            log.Line(result.Summary);
            log.Line(e.ToString());
            log.Payload("authority", e.AuthoritySnapshot);
            log.Payload("client", e.ClientSnapshot);
            return result;
        }
        catch (Exception e)
        {
            result.Failure = e.Message;
            log.Line(result.Summary);
            log.Line(e.ToString());
            log.Payload("authority", client?.LastAuthoritySnapshot);
            log.Payload("client", client?.LastClientSnapshot);
            return result;
        }
        finally
        {
            client?.Dispose();
            generated?.Dispose();
            transport?.Dispose();
        }
    }

    static AuthoritativePlayerCommand WaitForTechnologyTradeAndBuildResponse(Authoritative4XNetworkClient client,
        ProbeCommandPlan plan, List<AuthoritativeDiplomacyPopup> pendingPopups, ProbeLog log,
        AuthoritativeProbeOptions options, int sequence)
    {
        AuthoritativeDiplomacyPopup? offer = null;
        PumpUntil(() =>
        {
            client.Poll();
            pendingPopups.AddRange(client.DrainPopupsForClient());
            offer = pendingPopups.LastOrDefault(p =>
                p.ProposalType == AuthoritativeDiplomacyProposalType.TechnologyTrade
                && p.RequiresResponse);
            return offer != null;
        }, static () => { }, options, $"technology trade popup seq={sequence}");

        log.Line($"popup peer=join seq={sequence} type={offer!.ProposalType} proposal={offer.ProposalId} terms='{offer.Terms}'");
        return plan.JoinTechnologyTradeResponse(sequence, offer);
    }

    static Authoritative4XGameSettings BuildSettings(AuthoritativeProbeOptions options)
        => new()
        {
            GenerationSeed = options.Seed,
            Mode = RaceDesignScreen.GameMode.Sandbox,
            StarsCount = RaceDesignScreen.StarsAbundance.Rare,
            GalaxySize = GalSize.Tiny,
            Difficulty = GameDifficulty.Normal,
            NumOpponents = 1,
            Pace = 1f,
            TurnTimer = 5,
            GameSpeed = options.GameSpeed,
            StartPaused = false,
        };

    static string[] DefaultRaces(AuthoritativeProbeOptions options)
    {
        string host = string.IsNullOrWhiteSpace(options.HostRace)
            ? ResourceManager.MajorRaces[0].Name
            : options.HostRace;
        string join = string.IsNullOrWhiteSpace(options.JoinRace)
            ? ResourceManager.MajorRaces[1].Name
            : options.JoinRace;
        return new[] { host, join };
    }

    static string[] SplitTraits(string value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static void EnsureScriptTurns(AuthoritativeProbeOptions options)
    {
        if (options.Turns < ProbeCommandPlan.RequiredTurns)
            throw new ArgumentException($"The authoritative 4X probe requires at least {ProbeCommandPlan.RequiredTurns} turns to inject and assert both player command scripts.");
    }

    static Planet FirstPlanetForPeer(Authoritative4XGeneratedGameStart generated, int peerId)
    {
        int empireId = generated.EmpireIdForPeer(peerId);
        Empire empire = generated.AuthorityUniverse.UState.GetEmpireById(empireId);
        return empire?.GetPlanets().OrderBy(p => p.Id).FirstOrDefault()
               ?? throw new InvalidOperationException($"Peer {peerId} empire {empireId} has no planets.");
    }

    static void Require(Authoritative4XLobbyValidation validation)
    {
        if (validation == null)
            throw new InvalidOperationException("Validation returned null.");
        if (!validation.Valid)
            throw new InvalidOperationException(validation.Reason);
    }

    static void PumpUntil(Func<bool> done, Action poll, AuthoritativeProbeOptions options, string waitFor)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(options.TimeoutSeconds);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (done())
                    return;
                poll();
            }
            catch (Exception e)
            {
                last = e;
                throw;
            }
            Thread.Sleep(2);
        }
        throw new TimeoutException($"Timed out waiting for {waitFor}.{(last == null ? "" : " Last exception: " + last.Message)}");
    }

    static void ThrowIfClientSyncMismatch(Authoritative4XNetworkClient client)
    {
        if (client?.LastSyncMismatch != null)
            throw client.LastSyncMismatch;
    }

    static string SnapshotHash(AuthoritativeStateSnapshot? snapshot)
        => snapshot == null ? "" : $"0x{snapshot.HashLo:X16}:0x{snapshot.HashHi:X16}";

    static void WriteHeader(ProbeLog log, AuthoritativeProbeOptions options, string role)
    {
        log.Line($"role={role} host={options.Host} port={options.Port} turns={options.Turns} timeout={options.TimeoutSeconds}");
        log.Line($"machine={Environment.MachineName} user={Environment.UserName} os='{RuntimeInformation.OSDescription}' runtime='{RuntimeInformation.FrameworkDescription}'");
        log.Line($"gameRoot='{options.GameRoot}' mod='{GlobalStats.ModName}' modVersion='{GlobalStats.ModVersion}' version='{GlobalStats.Version}'");
        log.Line($"envHash={ProbeEnvironment.Hash()} envSummary='{ProbeEnvironment.Summary()}'");
        log.Line($"seed={options.Seed} races='{string.Join("|", DefaultRaces(options))}' traits='{options.HostTraits}'/'{options.JoinTraits}'");
    }

}

static class AuthoritativeLiveParityProbe
{
    public const int HostPlayerPeerId = 2;

    public static LiveParityVerdict Run(AuthoritativeProbeOptions options)
    {
        int port = options.Port == 47377 ? FreeTcpPort() : options.Port;
        string artifactDir = Path.Combine(options.OutputDir, "live-parity");
        Directory.CreateDirectory(artifactDir);

        var verdict = new LiveParityVerdict
        {
            Scenario = "live-parity-loopback-tcp",
            ClientCount = options.ClientCount,
            RemoteClientCount = options.ClientCount - 1,
            Seed = options.Seed,
            Turns = options.Turns,
            Port = port,
            StartedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Hazard = new LiveParityHazardVerdict
            {
                Seed = options.Seed,
                LatencyMs = options.HazardLatencyMs,
                JitterMs = options.HazardJitterMs,
                BurstEvery = options.HazardBurstEvery,
                BurstDelayMs = options.HazardBurstDelayMs,
            },
            ForcedDrift = new LiveParityForcedActionVerdict
            {
                Enabled = true,
                Sequence = options.ForceDriftSequence,
            },
            ForcedResync = new LiveParityForcedActionVerdict
            {
                Enabled = true,
                Sequence = options.ForceResyncSequence,
            },
        };

        TcpLockstepTransport? hostTransport = null;
        Authoritative4XNetworkHost? host = null;
        Authoritative4XLobbyStartResult? started = null;
        LiveParityWireTrace? wireTrace = null;
        var clients = new List<LiveParityClientHandle>();
        var stats = new Dictionary<int, LiveParityClientStats>();
        try
        {
            Authoritative4XLobby lobby = BuildLobby(options);
            started = lobby.StartInProcess();
            int[] peers = started.EmpireIdByPeer.Keys.OrderBy(peer => peer).ToArray();
            int[] remotePeers = peers.Where(peer => peer != HostPlayerPeerId).ToArray();
            foreach (int peer in peers)
                stats[peer] = new LiveParityClientStats(peer, peer == HostPlayerPeerId ? "host" : "client");

            hostTransport = TcpLockstepTransport.HostMulti(port);
            ConfigureHazards(hostTransport, options, HostPlayerPeerId);
            wireTrace = LiveParityWireTrace.TryOpen(artifactDir);
            wireTrace?.Attach(hostTransport, "host");
            Dictionary<int, TcpLockstepTransport> clientTransports = remotePeers.ToDictionary(
                peer => peer,
                peer =>
                {
                    TcpLockstepTransport transport = TcpLockstepTransport.JoinAsPeer("127.0.0.1", port,
                        peer, Authoritative4XNetworkHost.HostPeerId);
                    ConfigureHazards(transport, options, peer);
                    wireTrace?.Attach(transport, $"peer-{peer}");
                    return transport;
                });

            if (!hostTransport.WaitForConnections(remotePeers.Length, TimeSpan.FromSeconds(options.TimeoutSeconds)))
                throw new TimeoutException($"Live parity host did not accept {remotePeers.Length} loopback clients. error='{hostTransport.LastError}'");
            PumpTransportsUntil(() => remotePeers.All(peer => hostTransport.ConnectedRemotePeerIds.Contains(peer)),
                options, "peer mapping", new[] { hostTransport }.Concat(clientTransports.Values).ToArray());

            host = new Authoritative4XNetworkHost(started.AuthorityUniverse, hostTransport,
                started.EmpireIdByPeer, started.HumanEmpireIds, localPeerId: HostPlayerPeerId);

            foreach (Authoritative4XClientSpec spec in started.Clients.OrderBy(c => c.PeerId))
            {
                if (spec.PeerId == HostPlayerPeerId)
                    continue;
                var network = new Authoritative4XNetworkClient(spec.Universe, clientTransports[spec.PeerId],
                    spec.PeerId, started.HumanEmpireIds);
                clients.Add(new LiveParityClientHandle(spec, clientTransports[spec.PeerId], network));
            }

            var plan = new LiveParityCommandPlan(started.GeneratedGame, peers);
            verdict.Races = plan.Races;
            verdict.EmpireByPeer = started.EmpireIdByPeer
                .OrderBy(kv => kv.Key)
                .Select(kv => new LiveParityPeerEmpireVerdict { PeerId = kv.Key, EmpireId = kv.Value })
                .ToList();

            bool driftInjected = false;
            bool driftRecovered = false;
            bool forcedResyncDone = false;
            LiveParityClientHandle driftClient = clients.First();

            for (int sequence = 1; sequence <= options.Turns; ++sequence)
            {
                if (!forcedResyncDone && sequence == options.ForceResyncSequence)
                {
                    LiveParityResyncResult resync = TriggerForcedResync(host, started.GeneratedGame,
                        clients, stats, artifactDir, options, sequence);
                    verdict.ForcedResync.RequestingPeers = resync.RequestingPeers;
                    verdict.ForcedResync.Detected = resync.RequestingPeers.Count >= Math.Min(2, clients.Count);
                    verdict.ForcedResync.ResyncEpoch = resync.Epoch;
                    verdict.ForcedResync.AckPeers = resync.AckPeers;
                    verdict.ForcedResync.Repaired = VerifyConverged(host, clients, allowNoSnapshot: true,
                        out string forcedResyncFailure);
                    if (!verdict.ForcedResync.Repaired)
                        verdict.Failures.Add(forcedResyncFailure);
                    forcedResyncDone = true;
                }

                int originPeer = peers[(sequence - 1) % peers.Length];
                AuthoritativePlayerCommand command = plan.CommandFor(originPeer, sequence);

                if (!driftInjected && sequence == options.ForceDriftSequence)
                {
                    ForceReplicaDrift(driftClient, sequence);
                    verdict.ForcedDrift.PeerId = driftClient.PeerId;
                    driftInjected = true;
                    Submit(host, clients, originPeer, command);
                    List<AuthoritativeResyncRequestMessage> requests = WaitForResyncRequests(host, clients,
                        stats, options, "forced drift detection");
                    verdict.ForcedDrift.Detected = requests.Any(r => r.FromPeer == driftClient.PeerId);
                    stats[driftClient.PeerId].Mismatches++;
                    LiveParityResyncResult resync = RunResyncEpoch(host, started.GeneratedGame, clients,
                        stats, artifactDir, options, requests, "forced-drift");
                    verdict.ForcedDrift.RequestingPeers = resync.RequestingPeers;
                    verdict.ForcedDrift.ResyncEpoch = resync.Epoch;
                    verdict.ForcedDrift.AckPeers = resync.AckPeers;
                    driftRecovered = VerifyConverged(host, clients, allowNoSnapshot: true,
                        out string driftFailure);
                    verdict.ForcedDrift.Repaired = driftRecovered;
                    if (!verdict.ForcedDrift.Detected)
                        verdict.Failures.Add("Forced drift did not produce a resync request from the drifted peer.");
                    if (!driftRecovered)
                        verdict.Failures.Add(driftFailure);
                    continue;
                }

                long latencyMs = SubmitAndWait(host, clients, stats, options, originPeer, command,
                    $"peer {originPeer} seq {sequence}");
                stats[originPeer].RecordLatency(latencyMs);
            }

            bool converged = VerifyConverged(host, clients, allowNoSnapshot: false, out string convergenceFailure);
            verdict.ForcedDrift.Repaired = verdict.ForcedDrift.Repaired && converged;
            verdict.ForcedResync.Repaired = verdict.ForcedResync.Repaired && converged;
            if (!converged)
                verdict.Failures.Add(convergenceFailure);
            if (!driftInjected)
                verdict.Failures.Add("Forced drift sequence was not reached.");
            if (!driftRecovered)
                verdict.Failures.Add("Forced drift did not recover.");
            if (!forcedResyncDone)
                verdict.Failures.Add("Forced resync sequence was not reached.");
            if (options.ClientCount > 2 && verdict.ForcedResync.AckPeers.Count < 2)
                verdict.Failures.Add("Forced resync did not exercise multiple remote clients in one epoch.");

            verdict.Passed = verdict.Failures.Count == 0;
            verdict.FinalTick = host.LastAuthoritySnapshot?.Tick ?? 0;
            verdict.FinalDigest = host.LastAuthoritySnapshot?.SyncDigest ?? "";
            verdict.FinalHash = SnapshotHash(host.LastAuthoritySnapshot);
            int delayedMessages = hostTransport.HazardStats.DelayedMessages;
            long totalDelayMs = hostTransport.HazardStats.TotalDelayMs;
            foreach (LiveParityClientHandle client in clients)
            {
                delayedMessages += client.Transport.HazardStats.DelayedMessages;
                totalDelayMs += client.Transport.HazardStats.TotalDelayMs;
            }
            verdict.Hazard.DelayedMessages = delayedMessages;
            verdict.Hazard.TotalDelayMs = totalDelayMs;
            verdict.Hazard.MaxDelayMs = Math.Max(hostTransport.HazardStats.MaxDelayMs,
                clients.Count == 0 ? 0 : clients.Max(c => c.Transport.HazardStats.MaxDelayMs));
            verdict.Clients = stats.Values
                .OrderBy(s => s.PeerId)
                .Select(s => s.ToVerdict(host, clients.FirstOrDefault(c => c.PeerId == s.PeerId)))
                .ToList();
            return verdict;
        }
        catch (Exception e)
        {
            verdict.Passed = false;
            verdict.Failure = e.Message;
            verdict.Failures.Add(e.ToString());
            verdict.Clients = stats.Values
                .OrderBy(s => s.PeerId)
                .Select(s => s.ToVerdict(host, clients.FirstOrDefault(c => c.PeerId == s.PeerId)))
                .ToList();
            return verdict;
        }
        finally
        {
            foreach (LiveParityClientHandle client in clients)
                client.DisposeCurrentNetwork();
            host?.Dispose();
            started?.Dispose();
            wireTrace?.Dispose();
        }
    }

    static Authoritative4XLobby BuildLobby(AuthoritativeProbeOptions options)
    {
        int[] peers = Enumerable.Range(HostPlayerPeerId, options.ClientCount).ToArray();
        var lobby = new Authoritative4XLobby(HostPlayerPeerId, "Host");
        foreach (int peer in peers.Skip(1))
            lobby.Join(peer, "Client " + peer.ToString(CultureInfo.InvariantCulture));

        Authoritative4XGameSettings settings = new()
        {
            GenerationSeed = options.Seed,
            Mode = RaceDesignScreen.GameMode.Sandbox,
            StarsCount = RaceDesignScreen.StarsAbundance.Rare,
            GalaxySize = GalSize.Tiny,
            Difficulty = GameDifficulty.Normal,
            NumOpponents = Math.Max(1, options.ClientCount - 1),
            Pace = 1f,
            TurnTimer = 5,
            GameSpeed = options.GameSpeed,
            StartPaused = false,
            DisablePirates = true,
            DisableResearchStations = true,
            DisableMiningOps = true,
        };
        Require(lobby.SetSettings(HostPlayerPeerId, settings));

        string[] races = LiveParityRaces(options);
        for (int i = 0; i < peers.Length; ++i)
        {
            string traits = i == 0 ? options.HostTraits : i == 1 ? options.JoinTraits : "";
            Require(lobby.SetPlayerSelection(peers[i], races[i], SplitTraits(traits)));
            Require(lobby.SetReady(peers[i], true));
        }
        return lobby;
    }

    static string[] LiveParityRaces(AuthoritativeProbeOptions options)
    {
        List<string> races = ResourceManager.MajorRaces
            .Where(r => !r.IsFactionOrMinorRace)
            .OrderBy(RacePreference, StringComparer.Ordinal)
            .Select(RacePreference)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (races.Count < options.ClientCount)
            throw new InvalidOperationException($"Live parity needs {options.ClientCount} playable major races; loaded {races.Count}.");

        if (!string.IsNullOrWhiteSpace(options.HostRace))
            races.RemoveAll(r => string.Equals(r, options.HostRace, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(options.JoinRace))
            races.RemoveAll(r => string.Equals(r, options.JoinRace, StringComparison.OrdinalIgnoreCase));

        var selected = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.HostRace))
            selected.Add(options.HostRace);
        if (!string.IsNullOrWhiteSpace(options.JoinRace))
            selected.Add(options.JoinRace);
        selected.AddRange(races);
        return selected.Take(options.ClientCount).ToArray();
    }

    static string RacePreference(IEmpireData race)
        => !string.IsNullOrWhiteSpace(race.ArchetypeName) ? race.ArchetypeName : race.Name;

    static void ConfigureHazards(TcpLockstepTransport transport, AuthoritativeProbeOptions options, int peer)
    {
        transport.HazardProfile = new TcpLockstepHazardProfile
        {
            Seed = options.Seed ^ (peer * 1009),
            LatencyMs = options.HazardLatencyMs,
            JitterMs = options.HazardJitterMs,
            BurstEvery = options.HazardBurstEvery,
            BurstDelayMs = options.HazardBurstDelayMs,
        };
    }

    static long SubmitAndWait(Authoritative4XNetworkHost host, List<LiveParityClientHandle> clients,
        Dictionary<int, LiveParityClientStats> stats, AuthoritativeProbeOptions options,
        int originPeer, AuthoritativePlayerCommand command, string waitFor)
    {
        var sw = Stopwatch.StartNew();
        Submit(host, clients, originPeer, command);
        PumpUntil(() => clients.All(c => NetworkClientCaughtUp(c.Network, originPeer, command.Sequence)),
            host, clients, stats, options, waitFor);
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    static void Submit(Authoritative4XNetworkHost host, List<LiveParityClientHandle> clients,
        int originPeer, AuthoritativePlayerCommand command)
    {
        if (originPeer == HostPlayerPeerId)
        {
            host.SubmitLocal(originPeer, command);
            return;
        }

        LiveParityClientHandle client = clients.First(c => c.PeerId == originPeer);
        if (client.Network.IsWaitingForResync)
            throw new InvalidOperationException(
                $"Peer {originPeer} is waiting for resync before submitting seq={command.Sequence}.");
        client.Network.Submit(command);
    }

    static List<AuthoritativeResyncRequestMessage> WaitForResyncRequests(Authoritative4XNetworkHost host,
        List<LiveParityClientHandle> clients, Dictionary<int, LiveParityClientStats> stats,
        AuthoritativeProbeOptions options, string waitFor)
    {
        var requests = new List<AuthoritativeResyncRequestMessage>();
        PumpUntil(() =>
        {
            requests.AddRange(host.DrainResyncRequests());
            return requests.Count > 0;
        }, host, clients, stats, options, waitFor, allowClientResync: true);
        return requests;
    }

    static LiveParityResyncResult TriggerForcedResync(Authoritative4XNetworkHost host,
        Authoritative4XGeneratedGameStart generated, List<LiveParityClientHandle> clients,
        Dictionary<int, LiveParityClientStats> stats, string artifactDir,
        AuthoritativeProbeOptions options, int sequence)
    {
        LiveParityClientHandle[] requesters = clients.Take(Math.Min(2, clients.Count)).ToArray();
        foreach (LiveParityClientHandle requester in requesters)
        {
            requester.Network.SharedTransport.Send(Authoritative4XNetworkHost.HostPeerId,
                new AuthoritativeResyncRequestMessage
                {
                    FromPeer = requester.PeerId,
                    Tick = requester.Network.LastClientSnapshot?.Tick ?? 0,
                    ClientDigest = requester.Network.LastClientSnapshot?.SyncDigest ?? "",
                    Reason = $"probe forced resync seq={sequence}",
                });
        }

        var requests = new List<AuthoritativeResyncRequestMessage>();
        PumpUntil(() =>
        {
            requests.AddRange(host.DrainResyncRequests());
            return requests.Select(r => r.FromPeer).Distinct().Count() >= requesters.Length;
        }, host, clients, stats, options, "forced resync requests", allowClientResync: true);

        return RunResyncEpoch(host, generated, clients, stats, artifactDir, options,
            requests, "forced-resync");
    }

    static LiveParityResyncResult RunResyncEpoch(Authoritative4XNetworkHost host,
        Authoritative4XGeneratedGameStart generated, List<LiveParityClientHandle> clients,
        Dictionary<int, LiveParityClientStats> stats, string artifactDir,
        AuthoritativeProbeOptions options, IReadOnlyList<AuthoritativeResyncRequestMessage> requests,
        string reason)
    {
        AuthoritativeResyncRequestMessage request = requests.FirstOrDefault()
                                                    ?? new AuthoritativeResyncRequestMessage
                                                    {
                                                        FromPeer = clients.First().PeerId,
                                                        Reason = reason,
                                                    };
        int epoch = host.BeginResyncEpoch(request);
        if (epoch <= 0)
            throw new InvalidOperationException($"Could not begin resync epoch for {reason}.");

        var saveFile = new FileInfo(Path.Combine(artifactDir,
            $"auth4x-live-parity-{reason}-e{epoch}-{Guid.NewGuid():N}.sav"));
        Authoritative4XSessionMetadata metadata = Authoritative4XSessionMetadata.FromGenerated(generated,
            HostPlayerPeerId, HostPlayerPeerId, "auth4x-live-parity", ProbeEnvironment.Hash(),
            host.LastAuthoritySnapshot?.Tick ?? 0);
        Authoritative4XSessionSave.Save(generated.AuthorityUniverse, saveFile, metadata);
        foreach (int peer in host.RemotePeerIds)
            host.SendSaveTransfer(peer, saveFile, metadata, reason);

        PumpUntil(() => clients.All(c => c.Network.IsWaitingForResync && c.Network.ReceivedSaveCount > 0),
            host, clients, stats, options, $"{reason} save transfer", allowClientResync: true);

        uint recoveryTick = checked((uint)Math.Max(0, metadata.LastProcessedTick));
        foreach (LiveParityClientHandle client in clients)
        {
            Authoritative4XReceivedSave[] saves = client.Network.DrainReceivedSaves();
            if (saves.Length == 0)
                throw new InvalidOperationException($"Peer {client.PeerId} did not receive the {reason} save.");
            client.ReplaceFromSave(saves[^1], recoveryTick);
            stats[client.PeerId].Resyncs++;
            stats[client.PeerId].RecoveryTicks.Add(recoveryTick);
        }

        PumpUntil(() => !host.IsResyncInProgress, host, clients, stats, options, $"{reason} acks",
            allowClientResync: true);
        AuthoritativeResyncAckMessage[] acks = host.DrainResyncAcks();
        int[] ackPeers = acks.Select(a => a.FromPeer).OrderBy(peer => peer).ToArray();
        int[] expected = clients.Select(c => c.PeerId).OrderBy(peer => peer).ToArray();
        if (!expected.SequenceEqual(ackPeers))
            throw new InvalidOperationException($"{reason} acks were incomplete. expected={string.Join(",", expected)} actual={string.Join(",", ackPeers)}");

        return new LiveParityResyncResult
        {
            Epoch = epoch,
            RequestingPeers = requests.Select(r => r.FromPeer).Distinct().OrderBy(peer => peer).ToList(),
            AckPeers = ackPeers.ToList(),
        };
    }

    static void ForceReplicaDrift(LiveParityClientHandle client, int sequence)
    {
        Empire empire = client.Universe.UState.GetEmpireById(client.EmpireId)
                        ?? throw new InvalidOperationException($"Peer {client.PeerId} empire {client.EmpireId} missing.");
        Planet planet = empire.GetPlanets().OrderBy(p => p.Id).FirstOrDefault()
                        ?? throw new InvalidOperationException($"Peer {client.PeerId} has no planet to drift.");
        planet.CType = planet.CType == Planet.ColonyType.Research
            ? Planet.ColonyType.Colony
            : Planet.ColonyType.Research;
    }

    static void PumpUntil(Func<bool> done, Authoritative4XNetworkHost host,
        List<LiveParityClientHandle> clients, Dictionary<int, LiveParityClientStats> stats,
        AuthoritativeProbeOptions options, string waitFor, bool allowClientResync = false)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(options.TimeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            host.Poll();
            DrainHostProcessed(host, stats);
            foreach (LiveParityClientHandle client in clients)
            {
                client.Network.Poll();
                DrainClientProcessed(client, stats);
                if (!allowClientResync)
                    ThrowIfUnexpectedClientResync(client, stats, waitFor);
            }
            if (done())
                return;
            Thread.Sleep(2);
        }

        string clientErrors = string.Join("; ", clients.Select(c => $"{c.PeerId}:{c.Network.LastError}"));
        throw new TimeoutException($"Timed out waiting for {waitFor}. host='{host.LastError}' clients='{clientErrors}'");
    }

    static void ThrowIfUnexpectedClientResync(LiveParityClientHandle client,
        Dictionary<int, LiveParityClientStats> stats, string waitFor)
    {
        Authoritative4XSyncMismatchException mismatch = client.Network.LastSyncMismatch;
        if (mismatch != null)
        {
            stats[client.PeerId].Mismatches++;
            throw new InvalidOperationException(
                $"Peer {client.PeerId} requested an unexpected resync during {waitFor}: {mismatch.Message}");
        }

        if (client.Network.IsWaitingForResync)
        {
            throw new InvalidOperationException(
                $"Peer {client.PeerId} entered resync wait during {waitFor} without a recorded mismatch.");
        }
    }

    static void DrainHostProcessed(Authoritative4XNetworkHost host,
        Dictionary<int, LiveParityClientStats> stats)
    {
        foreach (Authoritative4XProcessedCommand processed in host.DrainProcessedCommands())
        {
            if (processed?.Result == null)
                continue;
            LiveParityClientStats stat = stats[processed.PeerId];
            stat.ObservedCommands++;
            stat.TicksRun = Math.Max(stat.TicksRun, processed.Result.Tick);
            stat.FinalDigest = processed.Snapshot?.SyncDigest ?? stat.FinalDigest;
            stat.FinalHash = SnapshotHash(processed.Snapshot);
            if (!processed.Result.Accepted)
                stat.Errors.Add($"Host rejected seq={processed.Result.Sequence} kind={processed.Command?.Kind}: {processed.Result.Reason}");
        }
    }

    static void DrainClientProcessed(LiveParityClientHandle client,
        Dictionary<int, LiveParityClientStats> stats)
    {
        foreach (Authoritative4XProcessedCommand processed in client.Network.DrainProcessedCommands())
        {
            if (processed?.Result == null)
                continue;
            LiveParityClientStats stat = stats[client.PeerId];
            stat.ObservedCommands++;
            stat.TicksRun = Math.Max(stat.TicksRun, processed.Result.Tick);
            stat.FinalDigest = processed.Snapshot?.SyncDigest ?? stat.FinalDigest;
            stat.FinalHash = SnapshotHash(processed.Snapshot);
            if (!processed.Result.Accepted)
                stat.Errors.Add($"Client observed reject origin={processed.PeerId} seq={processed.Result.Sequence}: {processed.Result.Reason}");
        }
    }

    static bool VerifyConverged(Authoritative4XNetworkHost host, List<LiveParityClientHandle> clients,
        bool allowNoSnapshot, out string failure)
    {
        failure = "";
        AuthoritativeStateSnapshot authority = host.LastAuthoritySnapshot;
        if (authority == null)
        {
            failure = "Host has no authority snapshot.";
            return false;
        }

        foreach (LiveParityClientHandle client in clients)
        {
            AuthoritativeStateSnapshot snapshot = client.Network.LastClientSnapshot;
            if (snapshot == null)
            {
                if (allowNoSnapshot)
                    continue;
                failure = $"Peer {client.PeerId} has no client snapshot.";
                return false;
            }
            if (!string.Equals(authority.SyncDigest, snapshot.SyncDigest, StringComparison.Ordinal))
            {
                failure = $"Peer {client.PeerId} digest mismatch host='{authority.SyncDigest}' client='{snapshot.SyncDigest}'.";
                return false;
            }
            if (!string.IsNullOrEmpty(authority.TransformDigest)
                && !string.Equals(authority.TransformDigest, snapshot.TransformDigest, StringComparison.Ordinal))
            {
                failure = $"Peer {client.PeerId} transform mismatch host='{authority.TransformDigest}' client='{snapshot.TransformDigest}'.";
                return false;
            }
        }
        return true;
    }

    static void PumpTransportsUntil(Func<bool> done, AuthoritativeProbeOptions options, string waitFor,
        params TcpLockstepTransport[] transports)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(options.TimeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            foreach (TcpLockstepTransport transport in transports)
                transport.Poll();
            if (done())
                return;
            Thread.Sleep(2);
        }
        throw new TimeoutException($"Timed out waiting for {waitFor}. errors='{string.Join("; ", transports.Select(t => t.LastError))}'");
    }

    static bool NetworkClientCaughtUp(Authoritative4XNetworkClient client, int originPeer, int sequence)
        => client.LastResult?.Sequence == sequence
           && client.LastResult.OriginPeer == originPeer
           && client.LastClientSnapshot != null
           && client.LastClientSnapshot.Tick == client.LastResult.Tick;

    static string[] SplitTraits(string value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static void Require(Authoritative4XLobbyValidation validation)
    {
        if (validation == null)
            throw new InvalidOperationException("Validation returned null.");
        if (!validation.Valid)
            throw new InvalidOperationException(validation.Reason);
    }

    static string SnapshotHash(AuthoritativeStateSnapshot? snapshot)
        => snapshot == null ? "" : $"0x{snapshot.HashLo:X16}:0x{snapshot.HashHi:X16}";

    static int FreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

sealed class LiveParityCommandPlan
{
    readonly Dictionary<int, PeerPlan> Plans;
    public List<string> Races { get; }

    public LiveParityCommandPlan(Authoritative4XGeneratedGameStart generated, int[] peers)
    {
        Plans = peers.ToDictionary(peer => peer, peer => BuildPeerPlan(generated, peer));
        Races = peers.Select(peer =>
        {
            int empireId = generated.EmpireIdForPeer(peer);
            Empire empire = generated.AuthorityUniverse.UState.GetEmpireById(empireId);
            return empire?.data?.Name ?? "";
        }).ToList();
    }

    public AuthoritativePlayerCommand CommandFor(int peerId, int sequence)
    {
        PeerPlan plan = Plans[peerId];
        return (sequence % 6) switch
        {
            0 when plan.ShipId != 0 => AuthoritativePlayerCommand.MoveShip(sequence, plan.EmpireId,
                plan.ShipId, plan.MoveTarget(sequence)),
            1 => AuthoritativePlayerCommand.SetColonyType(sequence, plan.EmpireId, plan.PlanetId,
                sequence % 12 == 1 ? Planet.ColonyType.Research : Planet.ColonyType.Colony),
            2 => AuthoritativePlayerCommand.QueueResearch(sequence, plan.EmpireId,
                plan.ResearchUidFor(sequence)),
            3 => AuthoritativePlayerCommand.SetEmpireBudget(sequence, plan.EmpireId,
                0.18f + ((sequence + peerId) % 5) * 0.01f,
                0.34f + ((sequence + peerId) % 4) * 0.02f,
                autoTaxes: false),
            4 => AuthoritativePlayerCommand.SetColonyLabor(sequence, plan.EmpireId, plan.PlanetId,
                plan.Food, plan.Production, plan.Research, foodLocked: true,
                productionLocked: true, researchLocked: true),
            _ => AuthoritativePlayerCommand.RenamePlanet(sequence, plan.EmpireId, plan.PlanetId,
                $"P5-{peerId}-{sequence}"),
        };
    }

    static PeerPlan BuildPeerPlan(Authoritative4XGeneratedGameStart generated, int peerId)
    {
        int empireId = generated.EmpireIdForPeer(peerId);
        Empire empire = generated.AuthorityUniverse.UState.GetEmpireById(empireId)
                        ?? throw new InvalidOperationException($"Peer {peerId} empire {empireId} missing.");
        Planet planet = empire.GetPlanets().OrderBy(p => p.Id).FirstOrDefault()
                        ?? throw new InvalidOperationException($"Peer {peerId} empire {empireId} has no planet.");
        Ship? ship = empire.OwnedShips
            .Where(s => s.Active && !s.IsPlatformOrStation)
            .OrderBy(s => s.Id)
            .FirstOrDefault();
        string[] researchUids = empire.TechEntries
            .Where(t => t.Discovered && t.CanBeResearched && !t.Unlocked)
            .OrderBy(t => t.UID, StringComparer.Ordinal)
            .Select(t => t.UID)
            .Take(12)
            .ToArray();
        if (researchUids.Length == 0)
            throw new InvalidOperationException($"Peer {peerId} empire {empireId} needs a research topic.");

        return new PeerPlan
        {
            PeerId = peerId,
            EmpireId = empireId,
            PlanetId = planet.Id,
            ShipId = ship?.Id ?? 0,
            Home = planet.Position,
            ResearchUids = researchUids,
            Food = planet.IsCybernetic ? 0f : 0.22f,
            Production = planet.IsCybernetic ? 0.60f : 0.48f,
            Research = planet.IsCybernetic ? 0.40f : 0.30f,
        };
    }

    sealed class PeerPlan
    {
        public int PeerId;
        public int EmpireId;
        public int PlanetId;
        public int ShipId;
        public Vector2 Home;
        public string[] ResearchUids = Array.Empty<string>();
        public float Food;
        public float Production;
        public float Research;

        public string ResearchUidFor(int sequence)
        {
            if (ResearchUids.Length == 0)
                throw new InvalidOperationException($"Peer {PeerId} has no research command topic.");
            return ResearchUids[Math.Abs((sequence / 6) + PeerId) % ResearchUids.Length];
        }

        public Vector2 MoveTarget(int sequence)
        {
            float offset = 1500f + (PeerId % 7) * 175f + (sequence % 5) * 90f;
            return Home + new Vector2(offset, offset * 0.5f);
        }
    }
}

sealed class LiveParityWireTrace : IDisposable
{
    readonly object Gate = new();
    readonly StreamWriter Writer;

    LiveParityWireTrace(string path)
    {
        Writer = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };
    }

    public static LiveParityWireTrace? TryOpen(string artifactDir)
    {
        string value = Environment.GetEnvironmentVariable("SD_AUTH4X_WIRE_TRACE") ?? "";
        if (!string.Equals(value, "1", StringComparison.Ordinal)
            && !string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            return null;
        Directory.CreateDirectory(artifactDir);
        return new LiveParityWireTrace(Path.Combine(artifactDir, "live-parity-wire.log"));
    }

    public void Attach(TcpLockstepTransport transport, string name)
    {
        transport.AuthoritativeFrameTrace = line =>
        {
            lock (Gate)
                Writer.WriteLine($"{DateTime.UtcNow:O} {name} {line}");
        };
    }

    public void Dispose()
    {
        lock (Gate)
            Writer.Dispose();
    }
}

sealed class LiveParityClientHandle
{
    readonly List<Authoritative4XLoadedSession> LoadedSessions = new();
    readonly Authoritative4XClientSpec Spec;

    public int PeerId => Spec.PeerId;
    public int EmpireId => Spec.EmpireId;
    public TcpLockstepTransport Transport { get; }
    public Authoritative4XNetworkClient Network { get; private set; }
    public UniverseScreen Universe => LoadedSessions.Count == 0 ? Spec.Universe : LoadedSessions[^1].Universe;

    public LiveParityClientHandle(Authoritative4XClientSpec spec, TcpLockstepTransport transport,
        Authoritative4XNetworkClient network)
    {
        Spec = spec;
        Transport = transport;
        Network = network;
    }

    public void ReplaceFromSave(Authoritative4XReceivedSave received, uint recoveryTick)
    {
        Authoritative4XLoadedSession loaded = Authoritative4XSessionSave.Load(received.SaveFile);
        AuthoritativeStateSnapshot snapshot = AuthoritativeStateSnapshot.Capture(loaded.Universe, recoveryTick);
        Network.SendResyncAck(snapshot.Tick, snapshot.SyncDigest, received.Sha256);
        LoadedSessions.Add(loaded);
        Network = new Authoritative4XNetworkClient(loaded.Universe, Transport, PeerId, loaded.HumanEmpireIds);
    }

    public void DisposeCurrentNetwork()
    {
        Network?.Dispose();
        foreach (Authoritative4XLoadedSession loaded in LoadedSessions)
            loaded.Dispose();
    }
}

sealed class LiveParityClientStats
{
    readonly List<long> Latencies = new();

    public int PeerId { get; }
    public string Role { get; }
    public uint TicksRun;
    public int ObservedCommands;
    public int Mismatches;
    public int Resyncs;
    public readonly List<uint> RecoveryTicks = new();
    public string FinalHash = "";
    public string FinalDigest = "";
    public readonly List<string> Errors = new();

    public LiveParityClientStats(int peerId, string role)
    {
        PeerId = peerId;
        Role = role;
    }

    public void RecordLatency(long latencyMs) => Latencies.Add(latencyMs);

    public LiveParityClientVerdict ToVerdict(Authoritative4XNetworkHost? host,
        LiveParityClientHandle? client)
    {
        if (PeerId == AuthoritativeLiveParityProbe.HostPlayerPeerId && host?.LastAuthoritySnapshot != null)
        {
            TicksRun = Math.Max(TicksRun, host.LastAuthoritySnapshot.Tick);
            FinalHash = SnapshotHash(host.LastAuthoritySnapshot);
            FinalDigest = host.LastAuthoritySnapshot.SyncDigest;
        }
        else if (client?.Network.LastClientSnapshot != null)
        {
            TicksRun = Math.Max(TicksRun, client.Network.LastClientSnapshot.Tick);
            FinalHash = SnapshotHash(client.Network.LastClientSnapshot);
            FinalDigest = client.Network.LastClientSnapshot.SyncDigest;
        }

        return new LiveParityClientVerdict
        {
            PeerId = PeerId,
            Role = Role,
            TicksRun = TicksRun,
            ObservedCommands = ObservedCommands,
            Mismatches = Mismatches,
            Resyncs = Resyncs,
            RecoveryTicks = RecoveryTicks.ToList(),
            SubmittedCommands = Latencies.Count,
            CommandLatency = new LiveParityLatencyVerdict
            {
                Count = Latencies.Count,
                MinMs = Latencies.Count == 0 ? 0 : Latencies.Min(),
                MaxMs = Latencies.Count == 0 ? 0 : Latencies.Max(),
                AvgMs = Latencies.Count == 0 ? 0 : Latencies.Average(),
            },
            FinalHash = FinalHash,
            FinalDigest = FinalDigest,
            Errors = Errors.ToList(),
        };
    }

    static string SnapshotHash(AuthoritativeStateSnapshot? snapshot)
        => snapshot == null ? "" : $"0x{snapshot.HashLo:X16}:0x{snapshot.HashHi:X16}";
}

sealed class LiveParityResyncResult
{
    public int Epoch;
    public List<int> RequestingPeers = new();
    public List<int> AckPeers = new();
}

sealed class LiveParityVerdict
{
    public string Schema { get; set; } = "stardrive.auth4x.liveParity.v1";
    public bool Passed { get; set; }
    public string Scenario { get; set; } = "";
    public string Failure { get; set; } = "";
    public int ClientCount { get; set; }
    public int RemoteClientCount { get; set; }
    public int Seed { get; set; }
    public int Turns { get; set; }
    public int Port { get; set; }
    public uint FinalTick { get; set; }
    public string FinalHash { get; set; } = "";
    public string FinalDigest { get; set; } = "";
    public string StartedUtc { get; set; } = "";
    public string ArtifactPath { get; set; } = "";
    public List<string> Races { get; set; } = new();
    public List<LiveParityPeerEmpireVerdict> EmpireByPeer { get; set; } = new();
    public LiveParityHazardVerdict Hazard { get; set; } = new();
    public LiveParityForcedActionVerdict ForcedDrift { get; set; } = new();
    public LiveParityForcedActionVerdict ForcedResync { get; set; } = new();
    public List<LiveParityClientVerdict> Clients { get; set; } = new();
    public List<string> Failures { get; set; } = new();
}

sealed class LiveParityPeerEmpireVerdict
{
    public int PeerId { get; set; }
    public int EmpireId { get; set; }
}

sealed class LiveParityHazardVerdict
{
    public int Seed { get; set; }
    public int LatencyMs { get; set; }
    public int JitterMs { get; set; }
    public int BurstEvery { get; set; }
    public int BurstDelayMs { get; set; }
    public int DelayedMessages { get; set; }
    public long TotalDelayMs { get; set; }
    public int MaxDelayMs { get; set; }
}

sealed class LiveParityForcedActionVerdict
{
    public bool Enabled { get; set; }
    public int PeerId { get; set; }
    public int Sequence { get; set; }
    public bool Detected { get; set; }
    public bool Repaired { get; set; }
    public int ResyncEpoch { get; set; }
    public List<int> RequestingPeers { get; set; } = new();
    public List<int> AckPeers { get; set; } = new();
}

sealed class LiveParityClientVerdict
{
    public int PeerId { get; set; }
    public string Role { get; set; } = "";
    public uint TicksRun { get; set; }
    public int SubmittedCommands { get; set; }
    public int ObservedCommands { get; set; }
    public int Mismatches { get; set; }
    public int Resyncs { get; set; }
    public List<uint> RecoveryTicks { get; set; } = new();
    public LiveParityLatencyVerdict CommandLatency { get; set; } = new();
    public string FinalHash { get; set; } = "";
    public string FinalDigest { get; set; } = "";
    public List<string> Errors { get; set; } = new();
}

sealed class LiveParityLatencyVerdict
{
    public int Count { get; set; }
    public long MinMs { get; set; }
    public long MaxMs { get; set; }
    public double AvgMs { get; set; }
}

static class LiveParityJson
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static string WriteAndValidate(LiveParityVerdict verdict, AuthoritativeProbeOptions options)
    {
        string path = string.IsNullOrWhiteSpace(options.JsonOutput)
            ? Path.Combine(options.OutputDir, "live-parity",
                $"auth4x-live-parity-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Process.GetCurrentProcess().Id}.json")
            : Path.GetFullPath(options.JsonOutput);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? options.OutputDir);
        verdict.ArtifactPath = path;
        string json = JsonSerializer.Serialize(verdict, Options);
        Validate(json, verdict.ClientCount, verdict.Passed);
        File.WriteAllText(path, json, Encoding.UTF8);
        using JsonDocument reparsed = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        Validate(reparsed.RootElement, verdict.ClientCount, verdict.Passed);
        return json;
    }

    static void Validate(string json, int expectedClientCount, bool requireFullClientSet)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Validate(document.RootElement, expectedClientCount, requireFullClientSet);
        LiveParityVerdict reparsed = JsonSerializer.Deserialize<LiveParityVerdict>(json, Options)
                                      ?? throw new InvalidDataException("Live parity JSON did not deserialize.");
        if (reparsed.Clients == null || (requireFullClientSet && reparsed.Clients.Count != expectedClientCount))
            throw new InvalidDataException("Live parity JSON client count changed after deserialize.");
    }

    static void Validate(JsonElement root, int expectedClientCount, bool requireFullClientSet)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Live parity JSON root must be an object.");
        if (!root.TryGetProperty("Schema", out JsonElement schema)
            || schema.GetString() != "stardrive.auth4x.liveParity.v1")
            throw new InvalidDataException("Live parity JSON schema is missing or invalid.");
        if (!root.TryGetProperty("Passed", out JsonElement passed)
            || passed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Live parity JSON must include a boolean Passed property.");
        if (!root.TryGetProperty("Clients", out JsonElement clients)
            || clients.ValueKind != JsonValueKind.Array
            || (requireFullClientSet && clients.GetArrayLength() != expectedClientCount))
            throw new InvalidDataException("Live parity JSON client array shape is invalid.");
        if (!root.TryGetProperty("Hazard", out JsonElement hazard)
            || hazard.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Live parity JSON hazard object is missing.");
    }
}

sealed class ProbeCommandPlan
{
    public const int RequiredTurns = 18;

    readonly int HostEmpireId;
    readonly int JoinEmpireId;
    readonly int HostPlanetId;
    readonly int JoinPlanetId;
    readonly int HostColonyShipId;
    readonly int JoinColonyShipId;
    readonly int HostColonizePlanetId;
    readonly int JoinColonizePlanetId;
    readonly float HostManualCivilianBudget;
    readonly float JoinManualCivilianBudget;
    readonly float HostTaxRate;
    readonly float HostTreasuryGoal;
    readonly float JoinTaxRate;
    readonly float JoinTreasuryGoal;
    readonly AuthoritativePlanetGovernorOptions HostGovernorOptions;
    readonly AuthoritativePlanetGovernorOptions JoinGovernorOptions;
    readonly TradeSlots HostTrade;
    readonly TradeSlots JoinTrade;
    readonly DefenseTargets HostDefense;
    readonly DefenseTargets JoinDefense;
    readonly string HostBuildingName;
    readonly string JoinBuildingName;
    readonly string HostDesignName;
    readonly string JoinDesignName;
    readonly string HostDesign64;
    readonly string JoinDesign64;
    readonly string HostResearchUid;
    readonly string JoinResearchUid;
    readonly string HostTechTradeUid;
    readonly LaborSplit HostLabor;
    readonly LaborSplit JoinLabor;

    ProbeCommandPlan(int hostEmpireId, int joinEmpireId, int hostPlanetId, int joinPlanetId,
        int hostColonyShipId, int joinColonyShipId, int hostColonizePlanetId, int joinColonizePlanetId,
        float hostManualCivilianBudget, float joinManualCivilianBudget,
        float hostTaxRate, float hostTreasuryGoal, float joinTaxRate, float joinTreasuryGoal,
        AuthoritativePlanetGovernorOptions hostGovernorOptions,
        AuthoritativePlanetGovernorOptions joinGovernorOptions,
        TradeSlots hostTrade, TradeSlots joinTrade,
        DefenseTargets hostDefense, DefenseTargets joinDefense,
        string hostBuildingName, string joinBuildingName,
        string hostDesignName, string joinDesignName,
        string hostDesign64, string joinDesign64,
        string hostResearchUid, string joinResearchUid,
        string hostTechTradeUid,
        LaborSplit hostLabor, LaborSplit joinLabor)
    {
        HostEmpireId = hostEmpireId;
        JoinEmpireId = joinEmpireId;
        HostPlanetId = hostPlanetId;
        JoinPlanetId = joinPlanetId;
        HostColonyShipId = hostColonyShipId;
        JoinColonyShipId = joinColonyShipId;
        HostColonizePlanetId = hostColonizePlanetId;
        JoinColonizePlanetId = joinColonizePlanetId;
        HostManualCivilianBudget = hostManualCivilianBudget;
        JoinManualCivilianBudget = joinManualCivilianBudget;
        HostTaxRate = hostTaxRate;
        HostTreasuryGoal = hostTreasuryGoal;
        JoinTaxRate = joinTaxRate;
        JoinTreasuryGoal = joinTreasuryGoal;
        HostGovernorOptions = hostGovernorOptions;
        JoinGovernorOptions = joinGovernorOptions;
        HostTrade = hostTrade;
        JoinTrade = joinTrade;
        HostDefense = hostDefense;
        JoinDefense = joinDefense;
        HostBuildingName = hostBuildingName;
        JoinBuildingName = joinBuildingName;
        HostDesignName = hostDesignName;
        JoinDesignName = joinDesignName;
        HostDesign64 = hostDesign64;
        JoinDesign64 = joinDesign64;
        HostResearchUid = hostResearchUid;
        JoinResearchUid = joinResearchUid;
        HostTechTradeUid = hostTechTradeUid;
        HostLabor = hostLabor;
        JoinLabor = joinLabor;
    }

    public static ProbeCommandPlan Create(Authoritative4XGeneratedGameStart generated, int hostPeerId, int joinPeerId)
    {
        int hostEmpireId = generated.EmpireIdForPeer(hostPeerId);
        int joinEmpireId = generated.EmpireIdForPeer(joinPeerId);
        Planet hostPlanet = FirstPlanet(generated, hostEmpireId);
        Planet joinPlanet = FirstPlanet(generated, joinEmpireId);
        Empire hostEmpire = generated.AuthorityUniverse.UState.GetEmpireById(hostEmpireId)
                            ?? throw new InvalidOperationException($"Host empire {hostEmpireId} missing.");
        Empire joinEmpire = generated.AuthorityUniverse.UState.GetEmpireById(joinEmpireId)
                            ?? throw new InvalidOperationException($"Join empire {joinEmpireId} missing.");

        string hostDesignName = $"MP QA Host Scout {hostEmpireId}-{hostPlanet.Id}";
        string joinDesignName = $"MP QA Join Scout {joinEmpireId}-{joinPlanet.Id}";
        ResourceManager.Ships.Delete(hostDesignName);
        ResourceManager.Ships.Delete(joinDesignName);
        ShipDesign hostDesign = BuildLegalPlayerDesign(hostEmpire, hostDesignName);
        ShipDesign joinDesign = BuildLegalPlayerDesign(joinEmpire, joinDesignName);
        LaborSplit hostLabor = LaborFor(hostPlanet, 0.20f, 0.50f, 0.30f);
        LaborSplit joinLabor = LaborFor(joinPlanet, 0.25f, 0.45f, 0.30f);
        (int hostColonizePlanetId, int joinColonizePlanetId) = PickColonizationTargets(generated,
            hostPlanet.Id, joinPlanet.Id);
        Ship? hostColony = EnsureProbeColonyShip(generated, hostEmpire, hostPlanet, hostColonizePlanetId);
        Ship? joinColony = EnsureProbeColonyShip(generated, joinEmpire, joinPlanet, joinColonizePlanetId);

        return new ProbeCommandPlan(hostEmpireId, joinEmpireId, hostPlanet.Id, joinPlanet.Id,
            hostColonyShipId: hostColony?.Id ?? 0,
            joinColonyShipId: joinColony?.Id ?? 0,
            hostColonizePlanetId: hostColony != null ? hostColonizePlanetId : 0,
            joinColonizePlanetId: joinColony != null ? joinColonizePlanetId : 0,
            hostManualCivilianBudget: 12.5f,
            joinManualCivilianBudget: 17.25f,
            hostTaxRate: 0.17f,
            hostTreasuryGoal: 0.41f,
            joinTaxRate: 0.23f,
            joinTreasuryGoal: 0.37f,
            hostGovernorOptions: AuthoritativePlanetGovernorOptions.GovOrbitals
                                 | AuthoritativePlanetGovernorOptions.ManualOrbitals
                                 | AuthoritativePlanetGovernorOptions.GovGroundDefense,
            joinGovernorOptions: AuthoritativePlanetGovernorOptions.DontScrapBuildings
                                 | AuthoritativePlanetGovernorOptions.Quarantine,
            hostTrade: new TradeSlots(1, 2, 0, 3, 4, 0),
            joinTrade: new TradeSlots(2, 1, 0, 4, 3, 0),
            hostDefense: new DefenseTargets(5, 2, 1, 1),
            joinDefense: new DefenseTargets(6, 3, 1, 2),
            hostBuildingName: TryPickBuildableBuilding(hostPlanet)?.Name ?? "",
            joinBuildingName: TryPickBuildableBuilding(joinPlanet)?.Name ?? "",
            hostDesignName: hostDesignName,
            joinDesignName: joinDesignName,
            hostDesign64: hostDesign.GetBase64DesignString(),
            joinDesign64: joinDesign.GetBase64DesignString(),
            hostResearchUid: PickResearchUid(hostEmpire),
            joinResearchUid: PickResearchUid(joinEmpire),
            hostTechTradeUid: PrepareTechnologyTrade(hostEmpire, joinEmpire),
            hostLabor: hostLabor,
            joinLabor: joinLabor);
    }

    public AuthoritativePlayerCommand HostCommand(int sequence)
        => ScriptCommand(sequence, hostSide: true);

    public AuthoritativePlayerCommand JoinCommand(int sequence)
        => ScriptCommand(sequence, hostSide: false);

    AuthoritativePlayerCommand ScriptCommand(int sequence, bool hostSide)
    {
        int empireId = hostSide ? HostEmpireId : JoinEmpireId;
        int planetId = hostSide ? HostPlanetId : JoinPlanetId;
        string buildingName = hostSide ? HostBuildingName : JoinBuildingName;
        string designName = hostSide ? HostDesignName : JoinDesignName;
        string design64 = hostSide ? HostDesign64 : JoinDesign64;
        string researchUid = hostSide ? HostResearchUid : JoinResearchUid;
        LaborSplit labor = hostSide ? HostLabor : JoinLabor;
        int colonyShipId = hostSide ? HostColonyShipId : JoinColonyShipId;
        int colonizePlanetId = hostSide ? HostColonizePlanetId : JoinColonizePlanetId;

        if (sequence > RequiredTurns)
            return LateControlCommand(sequence, hostSide);

        return sequence switch
        {
            1 => AuthoritativePlayerCommand.SetEmpireAutomation(sequence, empireId,
                AuthoritativeEmpireAutomationFlags.None, "", "", "", "", "", ""),
            2 => AuthoritativePlayerCommand.SetPlanetManualBudget(sequence, empireId, planetId,
                AuthoritativePlanetBudgetKind.Civilian, hostSide ? HostManualCivilianBudget : JoinManualCivilianBudget),
            3 => AuthoritativePlayerCommand.SetEmpireBudget(sequence, empireId,
                hostSide ? HostTaxRate : JoinTaxRate,
                hostSide ? HostTreasuryGoal : JoinTreasuryGoal, autoTaxes: false),
            4 => AuthoritativePlayerCommand.SetPlanetGovernorOptions(sequence, empireId, planetId,
                hostSide ? HostGovernorOptions : JoinGovernorOptions),
            5 => AuthoritativePlayerCommand.SetPlanetManualTradeSlots(sequence, empireId, planetId,
                (hostSide ? HostTrade : JoinTrade).FoodImport,
                (hostSide ? HostTrade : JoinTrade).ProdImport,
                (hostSide ? HostTrade : JoinTrade).ColoImport,
                (hostSide ? HostTrade : JoinTrade).FoodExport,
                (hostSide ? HostTrade : JoinTrade).ProdExport,
                (hostSide ? HostTrade : JoinTrade).ColoExport),
            6 => AuthoritativePlayerCommand.SetPlanetDefenseTargets(sequence, empireId, planetId,
                (hostSide ? HostDefense : JoinDefense).GarrisonSize,
                (hostSide ? HostDefense : JoinDefense).WantedPlatforms,
                (hostSide ? HostDefense : JoinDefense).WantedShipyards,
                (hostSide ? HostDefense : JoinDefense).WantedStations),
            7 => !string.IsNullOrEmpty(buildingName)
                ? AuthoritativePlayerCommand.QueueBuilding(sequence, empireId, planetId, buildingName)
                : AuthoritativePlayerCommand.NoOp(sequence, empireId),
            8 => AuthoritativePlayerCommand.DesignShip(sequence, empireId, design64),
            9 => AuthoritativePlayerCommand.QueueBuild(sequence, empireId, planetId, designName),
            10 => hostSide
                ? AuthoritativePlayerCommand.NoOp(sequence, empireId)
                : AuthoritativePlayerCommand.DiplomacyProposal(sequence, JoinEmpireId, HostEmpireId,
                AuthoritativeDiplomacyProposalType.DeclareWar, "probe declare war"),
            11 => AuthoritativePlayerCommand.SetColonyType(sequence, empireId, planetId,
                hostSide ? Planet.ColonyType.TradeHub : Planet.ColonyType.Colony),
            12 => AuthoritativePlayerCommand.SetColonyLabor(sequence, empireId, planetId,
                labor.Food, labor.Production, labor.Research,
                foodLocked: true, productionLocked: true, researchLocked: true),
            13 => AuthoritativePlayerCommand.QueueResearch(sequence, empireId, researchUid),
            14 => AuthoritativePlayerCommand.SetPlanetGoodsState(sequence, empireId, planetId,
                AuthoritativePlanetGoodsKind.Production, Planet.GoodState.EXPORT),
            15 => AuthoritativePlayerCommand.RenamePlanet(sequence, empireId, planetId,
                BasePlanetName(hostSide)),
            16 => AuthoritativePlayerCommand.SetPlanetManualBudget(sequence, empireId, planetId,
                AuthoritativePlanetBudgetKind.GroundDefense, hostSide ? 3.75f : 4.25f),
            17 => colonyShipId != 0 && colonizePlanetId != 0
                ? AuthoritativePlayerCommand.ShipPlanetOrder(sequence, empireId, colonyShipId,
                    colonizePlanetId, AuthoritativeShipPlanetOrderType.Colonize,
                    clearOrders: true, MoveOrder.Regular)
                : AuthoritativePlayerCommand.NoOp(sequence, empireId),
            18 => hostSide && !string.IsNullOrEmpty(HostTechTradeUid)
                ? AuthoritativePlayerCommand.DiplomacyProposal(sequence, HostEmpireId, JoinEmpireId,
                    AuthoritativeDiplomacyProposalType.TechnologyTrade, HostTechTradeUid)
                : AuthoritativePlayerCommand.NoOp(sequence, empireId),
            _ => AuthoritativePlayerCommand.NoOp(sequence, empireId),
        };
    }

    public bool RequiresTechnologyTradeResponse(int sequence)
        => sequence == 18 && !string.IsNullOrEmpty(HostTechTradeUid);

    public AuthoritativePlayerCommand JoinTechnologyTradeResponse(int sequence, AuthoritativeDiplomacyPopup popup)
        => AuthoritativePlayerCommand.DiplomacyResponse(sequence, JoinEmpireId,
            popup.ProposalId, AuthoritativeDiplomacyResponseKind.Accept);

    AuthoritativePlayerCommand LateControlCommand(int sequence, bool hostSide)
    {
        int empireId = hostSide ? HostEmpireId : JoinEmpireId;
        int planetId = hostSide ? HostPlanetId : JoinPlanetId;
        LaborSplit labor = hostSide ? HostLabor : JoinLabor;
        int phase = sequence % 100;
        return phase switch
        {
            0 => AuthoritativePlayerCommand.SetPlanetManualBudget(sequence, empireId, planetId,
                AuthoritativePlanetBudgetKind.Civilian, ExpectedCivilianBudget(hostSide, sequence)),
            25 => AuthoritativePlayerCommand.SetEmpireBudget(sequence, empireId,
                ExpectedTaxRate(hostSide, sequence), ExpectedTreasuryGoal(hostSide, sequence), autoTaxes: false),
            50 => AuthoritativePlayerCommand.SetColonyLabor(sequence, empireId, planetId,
                labor.Food, labor.Production, labor.Research,
                foodLocked: true, productionLocked: true, researchLocked: true),
            75 => AuthoritativePlayerCommand.RenamePlanet(sequence, empireId, planetId,
                LatePlanetName(hostSide, sequence)),
            _ => AuthoritativePlayerCommand.NoOp(sequence, empireId),
        };
    }

    public string Describe()
        => "script "
           + $"host empire={HostEmpireId} planet={HostPlanetId} building='{HostBuildingName}' design='{HostDesignName}'; "
           + $"join empire={JoinEmpireId} planet={JoinPlanetId} building='{JoinBuildingName}' design='{JoinDesignName}'; "
           + $"research='{HostResearchUid}|{JoinResearchUid}' techTrade='{HostTechTradeUid}'; "
           + $"colonize='{HostColonyShipId}->{HostColonizePlanetId}|{JoinColonyShipId}->{JoinColonizePlanetId}'; "
           + "seqs=automation,budget,tax,governor,trade,defense,building,design,ship-build,diplomacy,"
           + "colony-type,labor,research,goods,rename,ground-defense,colonize,tech-trade,late-control-pulses";

    public static bool IsLateControlPulse(int sequence)
        => sequence > RequiredTurns && sequence % 100 is 0 or 25 or 50 or 75;

    public void AssertApplied(Authoritative4XGeneratedGameStart generated, ProbeLog log, string label, int turns)
    {
        Empire host = generated.AuthorityUniverse.UState.GetEmpireById(HostEmpireId)
                      ?? throw new InvalidOperationException($"{label}: host empire {HostEmpireId} missing.");
        Empire join = generated.AuthorityUniverse.UState.GetEmpireById(JoinEmpireId)
                      ?? throw new InvalidOperationException($"{label}: join empire {JoinEmpireId} missing.");
        Planet hostPlanet = host.GetPlanets().FirstOrDefault(p => p.Id == HostPlanetId)
                            ?? throw new InvalidOperationException($"{label}: host planet {HostPlanetId} missing.");
        Planet joinPlanet = join.GetPlanets().FirstOrDefault(p => p.Id == JoinPlanetId)
                            ?? throw new InvalidOperationException($"{label}: join planet {JoinPlanetId} missing.");

        RequireControl(label, "host", host, hostPlanet, HostEmpireId, HostPlanetId);
        RequireControl(label, "join", join, joinPlanet, JoinEmpireId, JoinPlanetId);
        RequireClose(label, "host manual civilian budget", ExpectedCivilianBudget(hostSide: true, turns), hostPlanet.ManualCivilianBudget);
        RequireClose(label, "join manual civilian budget", ExpectedCivilianBudget(hostSide: false, turns), joinPlanet.ManualCivilianBudget);
        RequireClose(label, "host tax", ExpectedTaxRate(hostSide: true, turns), host.data.TaxRate);
        RequireClose(label, "host treasury", ExpectedTreasuryGoal(hostSide: true, turns), host.data.treasuryGoal);
        RequireClose(label, "join tax", ExpectedTaxRate(hostSide: false, turns), join.data.TaxRate);
        RequireClose(label, "join treasury", ExpectedTreasuryGoal(hostSide: false, turns), join.data.treasuryGoal);
        RequireAutomationOff(label, "host", host);
        RequireAutomationOff(label, "join", join);
        RequireGovernor(label, "host", hostPlanet, HostGovernorOptions);
        RequireGovernor(label, "join", joinPlanet, JoinGovernorOptions);
        RequireTrade(label, "host", hostPlanet, HostTrade);
        RequireTrade(label, "join", joinPlanet, JoinTrade);
        RequireDefense(label, "host", hostPlanet, HostDefense);
        RequireDefense(label, "join", joinPlanet, JoinDefense);
        RequireDurableColony(label, "host", host, hostPlanet, Planet.ColonyType.TradeHub, HostResearchUid,
            ExpectedPlanetName(hostSide: true, turns));
        RequireDurableColony(label, "join", join, joinPlanet, Planet.ColonyType.Colony, JoinResearchUid,
            ExpectedPlanetName(hostSide: false, turns));
        RequireCommandAccepted(log, label, "host", 11, AuthoritativePlayerCommandKind.SetColonyType);
        RequireCommandAccepted(log, label, "host", 12, AuthoritativePlayerCommandKind.SetColonyLabor);
        RequireCommandAccepted(log, label, "host", 13, AuthoritativePlayerCommandKind.QueueResearch);
        RequireCommandAccepted(log, label, "host", 14, AuthoritativePlayerCommandKind.SetPlanetGoodsState);
        RequireCommandAccepted(log, label, "host", 15, AuthoritativePlayerCommandKind.RenamePlanet);
        RequireCommandAccepted(log, label, "join", 11, AuthoritativePlayerCommandKind.SetColonyType);
        RequireCommandAccepted(log, label, "join", 12, AuthoritativePlayerCommandKind.SetColonyLabor);
        RequireCommandAccepted(log, label, "join", 13, AuthoritativePlayerCommandKind.QueueResearch);
        RequireCommandAccepted(log, label, "join", 14, AuthoritativePlayerCommandKind.SetPlanetGoodsState);
        RequireCommandAccepted(log, label, "join", 15, AuthoritativePlayerCommandKind.RenamePlanet);
        if (HasColonizationScript(hostSide: true))
        {
            RequireCommandAccepted(log, label, "host", 17, AuthoritativePlayerCommandKind.ShipPlanetOrder);
            RequireColonizationGoal(label, "host", host, HostColonyShipId, HostColonizePlanetId);
        }
        if (HasColonizationScript(hostSide: false))
        {
            RequireCommandAccepted(log, label, "join", 17, AuthoritativePlayerCommandKind.ShipPlanetOrder);
            RequireColonizationGoal(label, "join", join, JoinColonyShipId, JoinColonizePlanetId);
        }
        if (!string.IsNullOrEmpty(HostTechTradeUid))
        {
            RequireCommandAccepted(log, label, "host", 18, AuthoritativePlayerCommandKind.DiplomacyProposal);
            RequireCommandAccepted(log, label, "join", 18, AuthoritativePlayerCommandKind.DiplomacyResponse);
            if (!join.HasUnlocked(HostTechTradeUid))
                throw new InvalidOperationException($"{label}: join empire did not receive traded technology '{HostTechTradeUid}'.");
        }
        RequireLateControlCommands(log, label, turns);
        if (!string.IsNullOrEmpty(HostBuildingName))
            RequireQueuedBuilding(label, "host", hostPlanet, HostBuildingName);
        if (!string.IsNullOrEmpty(JoinBuildingName))
            RequireQueuedBuilding(label, "join", joinPlanet, JoinBuildingName);
        RequireDesignBuildable(label, "host", host, HostDesignName);
        RequireDesignBuildable(label, "join", join, JoinDesignName);
        RequireQueuedShip(label, "host", hostPlanet, HostDesignName);
        RequireQueuedShip(label, "join", joinPlanet, JoinDesignName);
        if (!join.IsAtWarWith(host) || !host.IsAtWarWith(join))
            throw new InvalidOperationException($"{label}: join declare-war command did not create bilateral war state.");

        log.Line($"assert {label} category=budget host={HostManualCivilianBudget:0.###}/{HostTaxRate:0.###}/{HostTreasuryGoal:0.###} "
                 + $"join={JoinManualCivilianBudget:0.###}/{JoinTaxRate:0.###}/{JoinTreasuryGoal:0.###}");
        log.Line($"assert {label} category=control hostEmpire={host.Id} hostPlanet={hostPlanet.Id} joinEmpire={join.Id} joinPlanet={joinPlanet.Id} joinHuman=True");
        log.Line($"assert {label} category=automation hostOff=True joinOff=True");
        log.Line($"assert {label} category=governor host={HostGovernorOptions} join={JoinGovernorOptions}");
        log.Line($"assert {label} category=trade host='{HostTrade}' join='{JoinTrade}'");
        log.Line($"assert {label} category=defense host='{HostDefense}' join='{JoinDefense}'");
        log.Line($"assert {label} category=colony hostType={hostPlanet.CType} joinType={joinPlanet.CType} hostName='{hostPlanet.Name}' joinName='{joinPlanet.Name}'");
        log.Line($"assert {label} category=research host='{HostResearchUid}' join='{JoinResearchUid}' hostQueue={host.data.ResearchQueue.Count} joinQueue={join.data.ResearchQueue.Count}");
        if (HasColonizationScript(hostSide: true) || HasColonizationScript(hostSide: false))
            log.Line($"assert {label} category=colonization host={HostColonyShipId}->{HostColonizePlanetId} join={JoinColonyShipId}->{JoinColonizePlanetId}");
        else
            log.Line($"assert {label} category=optional-colonization-unavailable reason='generated start lacked colony ship or neutral habitable target'");
        if (!string.IsNullOrEmpty(HostTechTradeUid))
            log.Line($"assert {label} category=tech-trade hostOffered='{HostTechTradeUid}' joinUnlocked=True");
        else
            log.Line($"assert {label} category=optional-tech-trade-unavailable reason='no legal host-to-join trade tech'");
        log.Line($"assert {label} category=command-stream colonyLabor=True research=True goods=True rename=True");
        log.Line($"assert {label} category=late-control turns={turns} joinBudget={joinPlanet.ManualCivilianBudget:0.###} joinTax={join.data.TaxRate:0.###} joinName='{joinPlanet.Name}'");
        if (!string.IsNullOrEmpty(HostBuildingName) || !string.IsNullOrEmpty(JoinBuildingName))
            log.Line($"assert {label} category=building-queue host='{HostBuildingName}' join='{JoinBuildingName}'");
        else
            log.Line($"assert {label} category=optional-building-unavailable reason='no legal building in generated tiny start'");
        log.Line($"assert {label} category=shipyard host='{HostDesignName}' join='{JoinDesignName}'");
        log.Line($"assert {label} category=diplomacy joinDeclaredWar=True");
    }

    public bool IsApplied(Authoritative4XGeneratedGameStart generated, int turns)
    {
        Empire? host = generated.AuthorityUniverse.UState.GetEmpireById(HostEmpireId);
        Empire? join = generated.AuthorityUniverse.UState.GetEmpireById(JoinEmpireId);
        Planet? hostPlanet = host?.GetPlanets().FirstOrDefault(p => p.Id == HostPlanetId);
        Planet? joinPlanet = join?.GetPlanets().FirstOrDefault(p => p.Id == JoinPlanetId);
        return Close(hostPlanet?.ManualCivilianBudget ?? float.NaN, ExpectedCivilianBudget(hostSide: true, turns))
               && Close(joinPlanet?.ManualCivilianBudget ?? float.NaN, ExpectedCivilianBudget(hostSide: false, turns))
               && Close(host?.data.TaxRate ?? float.NaN, ExpectedTaxRate(hostSide: true, turns))
               && Close(host?.data.treasuryGoal ?? float.NaN, ExpectedTreasuryGoal(hostSide: true, turns))
               && Close(join?.data.TaxRate ?? float.NaN, ExpectedTaxRate(hostSide: false, turns))
               && Close(join?.data.treasuryGoal ?? float.NaN, ExpectedTreasuryGoal(hostSide: false, turns))
               && hostPlanet != null
               && joinPlanet != null
               && HasControl(host, hostPlanet, HostEmpireId, HostPlanetId)
               && HasControl(join, joinPlanet, JoinEmpireId, JoinPlanetId)
               && AutomationOff(host)
               && AutomationOff(join)
               && HasGovernor(hostPlanet, HostGovernorOptions)
               && HasGovernor(joinPlanet, JoinGovernorOptions)
               && HasTrade(hostPlanet, joinSide: false)
               && HasTrade(joinPlanet, joinSide: true)
               && HasDefense(hostPlanet, HostDefense)
               && HasDefense(joinPlanet, JoinDefense)
               && HasDurableColony(host, hostPlanet, Planet.ColonyType.TradeHub, HostResearchUid,
                   ExpectedPlanetName(hostSide: true, turns))
               && HasDurableColony(join, joinPlanet, Planet.ColonyType.Colony, JoinResearchUid,
                   ExpectedPlanetName(hostSide: false, turns))
               && (string.IsNullOrEmpty(HostBuildingName) || ContainsQueuedOrCompletedBuilding(hostPlanet, HostBuildingName))
               && (string.IsNullOrEmpty(JoinBuildingName) || ContainsQueuedOrCompletedBuilding(joinPlanet, JoinBuildingName))
               && host!.CanBuildShip(HostDesignName)
               && join!.CanBuildShip(JoinDesignName)
               && ContainsQueuedOrOwnedShip(hostPlanet, HostDesignName)
               && ContainsQueuedOrOwnedShip(joinPlanet, JoinDesignName)
               && (!HasColonizationScript(hostSide: true)
                   || HasColonizationGoal(host, HostColonyShipId, HostColonizePlanetId))
               && (!HasColonizationScript(hostSide: false)
                   || HasColonizationGoal(join, JoinColonyShipId, JoinColonizePlanetId))
               && (string.IsNullOrEmpty(HostTechTradeUid) || join.HasUnlocked(HostTechTradeUid))
               && join.IsAtWarWith(host)
               && host.IsAtWarWith(join);
    }

    float ExpectedCivilianBudget(bool hostSide, int turns)
    {
        if (hostSide)
            return HostManualCivilianBudget;
        int pulse = LastPulse(turns, phase: 0);
        if (pulse <= 0)
            return JoinManualCivilianBudget;
        return LateCivilianBudget(hostSide, pulse);
    }

    float ExpectedTaxRate(bool hostSide, int turns)
    {
        if (hostSide)
            return HostTaxRate;
        int pulse = LastPulse(turns, phase: 25);
        if (pulse <= 0)
            return JoinTaxRate;
        return LateTaxRate(hostSide, pulse);
    }

    float ExpectedTreasuryGoal(bool hostSide, int turns)
    {
        if (hostSide)
            return HostTreasuryGoal;
        int pulse = LastPulse(turns, phase: 25);
        if (pulse <= 0)
            return JoinTreasuryGoal;
        return LateTreasuryGoal(hostSide, pulse);
    }

    string ExpectedPlanetName(bool hostSide, int turns)
    {
        if (hostSide)
            return BasePlanetName(hostSide);
        int pulse = LastPulse(turns, phase: 75);
        return pulse <= 0 ? BasePlanetName(hostSide) : LatePlanetName(hostSide, pulse);
    }

    static int LastPulse(int turns, int phase)
    {
        for (int sequence = turns; sequence > RequiredTurns; --sequence)
            if (sequence % 100 == phase)
                return sequence;
        return 0;
    }

    static float LateCivilianBudget(bool hostSide, int sequence)
        => (hostSide ? 9.5f : 11.5f) + ((sequence / 100) % 4);

    static float LateTaxRate(bool hostSide, int sequence)
        => (hostSide ? 0.18f : 0.24f) + ((sequence / 100) % 3) * 0.01f;

    static float LateTreasuryGoal(bool hostSide, int sequence)
        => (hostSide ? 0.42f : 0.36f) + ((sequence / 100) % 3) * 0.02f;

    string BasePlanetName(bool hostSide)
        => hostSide ? $"QAH-{HostPlanetId}" : $"QAJ-{JoinPlanetId}";

    string LatePlanetName(bool hostSide, int sequence)
        => $"{BasePlanetName(hostSide)}-{sequence}";

    static LaborSplit LaborFor(Planet planet, float food, float production, float research)
        => planet.IsCybernetic
            ? new LaborSplit(0f, 0.60f, 0.40f)
            : new LaborSplit(food, production, research);

    static string PickResearchUid(Empire empire)
        => empire.TechEntries
               .Where(t => t.Discovered && t.CanBeResearched && !t.Unlocked)
               .OrderBy(t => t.UID, StringComparer.Ordinal)
               .Select(t => t.UID)
               .FirstOrDefault()
           ?? throw new InvalidOperationException($"Empire {empire.Id} needs at least one discovered, researchable tech.");

    static void RequireControl(string label, string side, Empire empire, Planet planet, int empireId, int planetId)
    {
        if (!HasControl(empire, planet, empireId, planetId))
            throw new InvalidOperationException($"{label}: {side} control identity was lost for empire {empireId} planet {planetId}.");
    }

    static bool HasControl(Empire? empire, Planet? planet, int empireId, int planetId)
        => empire != null
           && planet != null
           && empire.Id == empireId
           && planet.Id == planetId
           && planet.Owner == empire
           && AuthoritativeHumanPlayers.IsHumanControlled(empire);

    static void RequireDurableColony(string label, string side, Empire empire, Planet planet,
        Planet.ColonyType type, string researchUid, string planetName)
    {
        if (!HasDurableColony(empire, planet, type, researchUid, planetName))
            throw new InvalidOperationException($"{label}: {side} colony/research controls did not persist.");
    }

    static bool HasDurableColony(Empire? empire, Planet? planet, Planet.ColonyType type,
        string researchUid, string planetName)
    {
        return empire != null
               && planet != null
               && planet.CType == type
               && string.Equals(planet.Name, planetName, StringComparison.Ordinal)
               && HasResearchAccepted(empire, researchUid);
    }

    static void RequireCommandAccepted(ProbeLog log, string label, string peerName, int sequence,
        AuthoritativePlayerCommandKind kind)
    {
        if (!log.HasCommand(peerName, sequence, kind))
            throw new InvalidOperationException($"{label}: {peerName} {kind} seq {sequence} was not accepted/applied.");
    }

    void RequireLateControlCommands(ProbeLog log, string label, int turns)
    {
        foreach (int sequence in LatePulseSequences(turns))
        {
            AuthoritativePlayerCommandKind expected = (sequence % 100) switch
            {
                0 => AuthoritativePlayerCommandKind.SetPlanetManualBudget,
                25 => AuthoritativePlayerCommandKind.SetEmpireBudget,
                50 => AuthoritativePlayerCommandKind.SetColonyLabor,
                75 => AuthoritativePlayerCommandKind.RenamePlanet,
                _ => AuthoritativePlayerCommandKind.NoOp,
            };
            RequireCommandAccepted(log, label, "join", sequence, expected);
        }
    }

    static IEnumerable<int> LatePulseSequences(int turns)
    {
        for (int sequence = RequiredTurns + 1; sequence <= turns; ++sequence)
        {
            if (IsLateControlPulse(sequence))
                yield return sequence;
        }
    }

    static bool HasResearchAccepted(Empire empire, string researchUid)
    {
        return string.Equals(empire.Research.Topic, researchUid, StringComparison.Ordinal)
               || empire.data.ResearchQueue.Contains(researchUid)
               || empire.UnlockedTechs.Any(t => string.Equals(t.UID, researchUid, StringComparison.Ordinal));
    }

    static Planet FirstPlanet(Authoritative4XGeneratedGameStart generated, int empireId)
    {
        Empire empire = generated.AuthorityUniverse.UState.GetEmpireById(empireId);
        return empire?.GetPlanets().OrderBy(p => p.Id).FirstOrDefault()
               ?? throw new InvalidOperationException($"Empire {empireId} has no planets.");
    }

    static Ship? PickColonyShip(Empire empire)
        => empire.OwnedShips
            .Where(s => s.Active && s.ShipData.IsColonyShip && !s.IsPlatformOrStation)
            .OrderBy(s => s.Id)
            .FirstOrDefault();

    static Ship? EnsureProbeColonyShip(Authoritative4XGeneratedGameStart generated, Empire empire,
        Planet homePlanet, int targetPlanetId)
    {
        if (targetPlanetId == 0)
            return null;

        Ship? existing = PickColonyShip(empire);
        if (existing != null)
            return existing;

        IShipDesign? design = empire.ShipsWeCanBuildSnapshot
            .Where(s => s.IsColonyShip && !s.IsPlatformOrStation && !s.IsShipyard)
            .OrderBy(s => s.BaseCost)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (design == null)
            return null;

        Vector2 spawn = homePlanet.Position + new Vector2(12_000f + empire.Id * 1_000f, 4_000f);
        Ship? ship = Ship.CreateShipAtPoint(generated.AuthorityUniverse.UState, design.Name, empire, spawn);
        generated.AuthorityUniverse.UState.Objects.UpdateLists(removeInactiveObjects: false);
        return ship;
    }

    static (int HostTargetId, int JoinTargetId) PickColonizationTargets(
        Authoritative4XGeneratedGameStart generated, int hostHomePlanetId, int joinHomePlanetId)
    {
        List<Planet> candidates = generated.AuthorityUniverse.UState.Systems
            .SelectMany(s => s.PlanetList)
            .Where(p => p.Habitable && p.Owner == null)
            .Where(p => p.Id != hostHomePlanetId && p.Id != joinHomePlanetId)
            .OrderBy(p => p.Id)
            .ToList();
        if (candidates.Count < 2)
        {
            foreach (Planet fallback in generated.AuthorityUniverse.UState.Systems
                         .SelectMany(s => s.PlanetList)
                         .Where(p => p.Owner == null)
                         .Where(p => p.Id != hostHomePlanetId && p.Id != joinHomePlanetId)
                         .Where(p => candidates.All(c => c.Id != p.Id))
                         .OrderBy(p => p.Id))
            {
                MakeProbeHabitable(fallback);
                candidates.Add(fallback);
                if (candidates.Count >= 2)
                    break;
            }
        }

        return candidates.Count switch
        {
            0 => (0, 0),
            1 => (candidates[0].Id, 0),
            _ => (candidates[0].Id, candidates[1].Id),
        };
    }

    static void MakeProbeHabitable(Planet planet)
    {
        PlanetType[] habitableTypes = ResourceManager.Planets.Types
            .Where(t => t.Habitable)
            .OrderBy(t => t.Id)
            .ToArray();
        if (habitableTypes.Length == 0)
            return;

        planet.PType = habitableTypes[0];
        PlanetGridSquare? tile = planet.TilesList.OrderBy(t => t.X).ThenBy(t => t.Y).FirstOrDefault();
        tile?.SetHabitable(true);
        planet.UpdateMaxPopulation();
        planet.UpdateIncomes();
    }

    static string PrepareTechnologyTrade(Empire hostEmpire, Empire joinEmpire)
    {
        string techUid = hostEmpire.TechEntries
                             .Where(t => t.Discovered && t.CanBeResearched && !t.IsMultiLevel)
                             .Where(t => joinEmpire.TryGetTechEntry(t.UID, out TechEntry targetTech)
                                         && !targetTech.Unlocked
                                         && t.TheyCanUseThis(hostEmpire, joinEmpire))
                             .OrderBy(t => t.TechCost)
                             .ThenBy(t => t.UID, StringComparer.Ordinal)
                             .Select(t => t.UID)
                             .FirstOrDefault()
                         ?? "";
        if (string.IsNullOrEmpty(techUid))
            return "";

        hostEmpire.UnlockTech(techUid, TechUnlockType.Normal);
        return techUid;
    }

    bool HasColonizationScript(bool hostSide)
        => hostSide
            ? HostColonyShipId != 0 && HostColonizePlanetId != 0
            : JoinColonyShipId != 0 && JoinColonizePlanetId != 0;

    static void RequireColonizationGoal(string label, string side, Empire empire, int shipId, int planetId)
    {
        if (!HasColonizationGoal(empire, shipId, planetId))
            throw new InvalidOperationException($"{label}: {side} colonization order did not create MarkForColonization or colonize target ship={shipId} planet={planetId}.");
    }

    static bool HasColonizationGoal(Empire empire, int shipId, int planetId)
    {
        if (empire.GetPlanets().Any(p => p.Id == planetId))
            return true;

        return empire.AI.FindGoals<MarkForColonization>()
            .Any(g => g.TargetPlanet?.Id == planetId
                      && g.FinishedShip?.Id == shipId
                      && g.IsManualColonizationOrder);
    }

    static Building? TryPickBuildableBuilding(Planet planet)
    {
        planet.RefreshBuildingsWeCanBuildHere();
        Building? building = FirstBuildableBuildingWithTile(planet);
        if (building == null)
        {
            AddDeterministicEmptyHabitableTile(planet);
            planet.RefreshBuildingsWeCanBuildHere();
            building = FirstBuildableBuildingWithTile(planet);
        }
        if (building == null)
            building = UnlockDeterministicQaBuilding(planet);
        return building;
    }

    static Building? FirstBuildableBuildingWithTile(Planet planet)
        => planet.GetBuildingsCanBuild()
            .Where(b => planet.TilesList.Any(tile => tile.CanEnqueueBuildingHere(b)))
            .OrderBy(b => b.ActualCost(planet.Owner))
            .ThenBy(b => b.Name, StringComparer.Ordinal)
            .FirstOrDefault();

    static Building? UnlockDeterministicQaBuilding(Planet planet)
    {
        if (planet.Owner == null)
            return null;

        foreach (Building candidate in ResourceManager.BuildingsDict
                     .Select(kv => kv.Value)
                     .Where(IsProbeSafeBuilding)
                     .OrderBy(b => b.ActualCost(planet.Owner))
                     .ThenBy(b => b.Name, StringComparer.Ordinal))
        {
            planet.Owner.UnlockEmpireBuilding(candidate.Name);
            planet.RefreshBuildingsWeCanBuildHere();
            Building? buildable = FirstBuildableBuildingWithTile(planet);
            if (buildable != null)
                return buildable;
        }
        return null;
    }

    static bool IsProbeSafeBuilding(Building building)
    {
        return building != null
               && !string.IsNullOrEmpty(building.Name)
               && building.NameTranslationIndex > 0
               && building.Cost > 0f
               && !building.EventHere
               && !building.NoRandomSpawn
               && !building.IsCommodity
               && !building.WinsGame
               && !building.CanBeCreatedFromLava;
    }

    static void AddDeterministicEmptyHabitableTile(Planet planet)
    {
        for (int y = 0; y < 16; ++y)
        {
            for (int x = 0; x < 16; ++x)
            {
                if (planet.TilesList.Any(tile => tile.X == x && tile.Y == y))
                    continue;
                planet.TilesList.Add(new PlanetGridSquare(planet, x, y, b: null, hab: true, terraformable: false));
                return;
            }
        }
    }

    static ShipDesign BuildLegalPlayerDesign(Empire empire, string name)
    {
        ShipDesign? source = empire.ShipsWeCanBuildSnapshot
            .OfType<ShipDesign>()
            .Where(d => !d.IsPlatformOrStation
                        && d.IsValidDesign
                        && d.NumDesignSlots > 0
                        && d.UniqueModuleUIDs.All(empire.IsModuleUnlocked))
            .OrderBy(d => d.BaseCost)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (source == null)
            throw new InvalidOperationException($"Empire {empire.Id} needs at least one legal mobile design to clone.");
        source.GetOrLoadDesignSlots();

        ShipDesign clone = source.GetClone(name);
        clone.IsPlayerDesign = true;
        clone.IsReadonlyDesign = false;
        return clone;
    }

    static void RequireAutomationOff(string label, string side, Empire empire)
    {
        if (!AutomationOff(empire))
            throw new InvalidOperationException($"{label}: {side} human empire still has automation/sidekick flags enabled.");
    }

    static bool AutomationOff(Empire? empire)
        => empire != null
           && !empire.AISidekickEnabled
           && !empire.OracleSidekickEnabled
           && !empire.AutoTaxes
           && !empire.AutoResearch
           && !empire.AutoColonize
           && !empire.AutoFreighters
           && !empire.AutoMilitary
           && !empire.AutoSpy;

    static void RequireGovernor(string label, string side, Planet planet, AuthoritativePlanetGovernorOptions expected)
    {
        if (!HasGovernor(planet, expected))
            throw new InvalidOperationException($"{label}: {side} governor options did not apply.");
    }

    static bool HasGovernor(Planet planet, AuthoritativePlanetGovernorOptions expected)
        => planet.GovOrbitals == expected.HasFlag(AuthoritativePlanetGovernorOptions.GovOrbitals)
           && planet.AutoBuildTroops == expected.HasFlag(AuthoritativePlanetGovernorOptions.AutoBuildTroops)
           && planet.DontScrapBuildings == expected.HasFlag(AuthoritativePlanetGovernorOptions.DontScrapBuildings)
           && planet.Quarantine == expected.HasFlag(AuthoritativePlanetGovernorOptions.Quarantine)
           && planet.ManualOrbitals == expected.HasFlag(AuthoritativePlanetGovernorOptions.ManualOrbitals)
           && planet.GovGroundDefense == expected.HasFlag(AuthoritativePlanetGovernorOptions.GovGroundDefense)
           && planet.SpecializedTradeHub == expected.HasFlag(AuthoritativePlanetGovernorOptions.SpecializedTradeHub);

    static void RequireTrade(string label, string side, Planet planet, TradeSlots expected)
    {
        if (!HasTrade(planet, expected))
            throw new InvalidOperationException($"{label}: {side} manual trade slots did not apply.");
    }

    bool HasTrade(Planet planet, bool joinSide) => HasTrade(planet, joinSide ? JoinTrade : HostTrade);

    static bool HasTrade(Planet planet, TradeSlots expected)
        => planet.ManualFoodImportSlots == expected.FoodImport
           && planet.ManualProdImportSlots == expected.ProdImport
           && planet.ManualColoImportSlots == expected.ColoImport
           && planet.ManualFoodExportSlots == expected.FoodExport
           && planet.ManualProdExportSlots == expected.ProdExport
           && planet.ManualColoExportSlots == expected.ColoExport;

    static void RequireDefense(string label, string side, Planet planet, DefenseTargets expected)
    {
        if (!HasDefense(planet, expected))
            throw new InvalidOperationException($"{label}: {side} defense targets did not apply.");
    }

    static bool HasDefense(Planet planet, DefenseTargets expected)
        => planet.GarrisonSize == expected.GarrisonSize
           && planet.WantedPlatforms == expected.WantedPlatforms
           && planet.WantedShipyards == expected.WantedShipyards
           && planet.WantedStations == expected.WantedStations;

    static void RequireQueuedBuilding(string label, string side, Planet planet, string buildingName)
    {
        if (!ContainsQueuedOrCompletedBuilding(planet, buildingName))
            throw new InvalidOperationException($"{label}: {side} planet {planet.Id} did not queue or complete building '{buildingName}'.");
    }

    static bool ContainsQueuedBuilding(Planet planet, string buildingName)
        => planet.Construction.GetConstructionQueueSnapshot()
            .Any(q => q.isBuilding && string.Equals(q.Building?.Name, buildingName, StringComparison.Ordinal));

    static bool ContainsQueuedOrCompletedBuilding(Planet planet, string buildingName)
        => ContainsQueuedBuilding(planet, buildingName)
           || planet.TilesList.Any(tile => string.Equals(tile.Building?.Name, buildingName, StringComparison.Ordinal));

    static void RequireDesignBuildable(string label, string side, Empire empire, string designName)
    {
        if (!empire.CanBuildShip(designName))
            throw new InvalidOperationException($"{label}: {side} empire {empire.Id} cannot build submitted design '{designName}'.");
    }

    static void RequireQueuedShip(string label, string side, Planet planet, string designName)
    {
        if (!ContainsQueuedOrOwnedShip(planet, designName))
            throw new InvalidOperationException($"{label}: {side} planet {planet.Id} did not queue or complete ship design '{designName}'.");
    }

    static bool ContainsQueuedShip(Planet planet, string designName)
        => planet.Construction.ContainsShipDesignName(designName);

    static bool ContainsQueuedOrOwnedShip(Planet planet, string designName)
        => ContainsQueuedShip(planet, designName)
           || planet.Owner?.OwnedShips.Any(s =>
                  string.Equals(s.Name, designName, StringComparison.Ordinal)
                  || string.Equals(s.ShipData?.Name, designName, StringComparison.Ordinal)) == true;

    static void RequireClose(string label, string field, float expected, float actual)
    {
        if (!Close(actual, expected))
            throw new InvalidOperationException($"{label}: expected {field} {expected:R}, got {actual:R}.");
    }

    static bool Close(float actual, float expected) => Math.Abs(expected - actual) <= 0.00001f;

    readonly struct LaborSplit
    {
        public readonly float Food;
        public readonly float Production;
        public readonly float Research;

        public LaborSplit(float food, float production, float research)
        {
            Food = food;
            Production = production;
            Research = research;
        }
    }

    readonly struct TradeSlots
    {
        public readonly int FoodImport;
        public readonly int ProdImport;
        public readonly int ColoImport;
        public readonly int FoodExport;
        public readonly int ProdExport;
        public readonly int ColoExport;

        public TradeSlots(int foodImport, int prodImport, int coloImport,
            int foodExport, int prodExport, int coloExport)
        {
            FoodImport = foodImport;
            ProdImport = prodImport;
            ColoImport = coloImport;
            FoodExport = foodExport;
            ProdExport = prodExport;
            ColoExport = coloExport;
        }

        public override string ToString()
            => $"{FoodImport},{ProdImport},{ColoImport}->{FoodExport},{ProdExport},{ColoExport}";
    }

    readonly struct DefenseTargets
    {
        public readonly int GarrisonSize;
        public readonly int WantedPlatforms;
        public readonly int WantedShipyards;
        public readonly int WantedStations;

        public DefenseTargets(int garrisonSize, int wantedPlatforms, int wantedShipyards, int wantedStations)
        {
            GarrisonSize = garrisonSize;
            WantedPlatforms = wantedPlatforms;
            WantedShipyards = wantedShipyards;
            WantedStations = wantedStations;
        }

        public override string ToString()
            => $"{GarrisonSize},{WantedPlatforms},{WantedShipyards},{WantedStations}";
    }
}

static class ProbeEnvironment
{
    public static string Hash()
    {
        var h = DetHash.New();
        h.AddString(GlobalStats.ExtendedVersionNoHash);
        h.AddString(GlobalStats.ExtendedVersion);
        h.AddString(GlobalStats.ModName);
        h.AddString(GlobalStats.ModVersion);
        h.AddString(typeof(TcpLockstepTransport).Assembly.GetName().Version?.ToString() ?? "");
        h.AddULong(BuildFingerprint.Compute(DeterminismProfile.MPSamePlatformPinnedFloat));
        return "0x" + h.Value.ToString("X16", CultureInfo.InvariantCulture);
    }

    public static string Summary()
    {
        string game = !string.IsNullOrEmpty(GlobalStats.ExtendedVersionNoHash)
            ? GlobalStats.ExtendedVersionNoHash
            : !string.IsNullOrEmpty(GlobalStats.ExtendedVersion) ? GlobalStats.ExtendedVersion : "unknown-game";
        string mod = GlobalStats.HasMod ? $"{GlobalStats.ModName} {GlobalStats.ModVersion}" : "Vanilla";
        return $"{game}; {mod}; auth4x-probe env {Hash()}";
    }
}

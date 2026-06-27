using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SDLockstep;
using SDUtils.Deterministic;
using Ship_Game;
using Ship_Game.Data;
using Ship_Game.Determinism;
using Ship_Game.Gameplay;
using Ship_Game.Multiplayer.Authoritative;
using SynapseGaming.LightingSystem.Core;

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
    public string Host = "127.0.0.1";
    public int Port = 47377;
    public int Turns = 600;
    public int TimeoutSeconds = 180;
    public int Seed = 54545;
    public string GameRoot = "";
    public string ModPath = "Mods/Combined Arms";
    public string OutputDir = "";
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
          --host <ip>            Host IP for join mode. Env: SD_AUTH4X_HOST.
          --port <port>          TCP port. Default 47377. Env: SD_AUTH4X_PORT.
          --turns <n>            Scripted authoritative turns. Default 600. Env: SD_AUTH4X_TURNS.
          --timeout <seconds>    Wait timeout. Default 180.
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

        Example:
          Authoritative4XProbe.exe --role host --port 47377 --turns 1600 --game-root "C:\Games\StarDrive2"
          Authoritative4XProbe.exe --role join --host 26.20.119.64 --port 47377 --turns 1600 --game-root "D:\Games\StarDrive2"
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

            string value = Value(args, ref i, arg);
            switch (Key(arg))
            {
                case "--role": o.Role = ParseRole(value); break;
                case "--host": o.Host = value; break;
                case "--port": o.Port = ParseInt(value, "--port"); break;
                case "--turns": o.Turns = ParseInt(value, "--turns"); break;
                case "--timeout": o.TimeoutSeconds = ParseInt(value, "--timeout"); break;
                case "--seed": o.Seed = ParseInt(value, "--seed"); break;
                case "--game-root": o.GameRoot = value; break;
                case "--mod": o.ModPath = value; break;
                case "--output": o.OutputDir = value; break;
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

    GameContentBootstrap(GameDummy game, string previousCwd)
    {
        Game = game;
        PreviousCwd = previousCwd;
    }

    public static GameContentBootstrap Load(AuthoritativeProbeOptions options)
    {
        string previous = Directory.GetCurrentDirectory();
        string root = ResolveGameRoot(options.GameRoot);
        if (string.IsNullOrWhiteSpace(options.OutputDir))
            options.OutputDir = Path.Combine(AppContext.BaseDirectory, "sim-output", "authoritative4x-probe");
        Directory.CreateDirectory(options.OutputDir);
        Directory.SetCurrentDirectory(root);

        GlobalStats.LoadConfig();
        InstallProbeLog(options.OutputDir);
        Log.Initialize(enableSentry: false, showHeader: false);
        Log.VerboseLogging = true;
        GlobalStats.DrawStarfield = false;
        GlobalStats.DrawNebulas = false;
        GlobalStats.AsteroidVisibility = ObjectVisibility.None;

        var game = new GameDummy(1024, 768, show: false);
        game.Create();
        Directory.CreateDirectory(SavedGame.DefaultSaveGameFolder);

        if (string.IsNullOrWhiteSpace(options.ModPath))
            GlobalStats.SetActiveModNoSave(null);
        else
            GlobalStats.LoadModInfo(options.ModPath);

        ResourceManager.InitContentDir();
        ResourceManager.UnloadAllData(ScreenManager.Instance);
        ResourceManager.LoadItAll(ScreenManager.Instance, GlobalStats.ActiveMod);
        if (!string.IsNullOrWhiteSpace(options.ModPath) && !GlobalStats.HasMod)
            throw new InvalidOperationException($"Requested mod '{options.ModPath}' did not stay active after content load.");
        if (ResourceManager.MajorRaces.Count < 2)
            throw new InvalidOperationException("Authoritative probe needs at least two major races.");

        options.GameRoot = root;
        return new GameContentBootstrap(game, previous);
    }

    public void Dispose()
    {
        ResourceManager.WaitForExit();
        Game.Dispose();
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

    public string Path { get; }

    public ProbeLog(AuthoritativeProbeOptions options, string role)
    {
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        Path = System.IO.Path.Combine(options.OutputDir, $"auth4x-{role}-{stamp}-{Process.GetCurrentProcess().Id}.txt");
        Writer = new StreamWriter(File.Create(Path), Encoding.UTF8) { AutoFlush = true };
    }

    public void Line(string text)
    {
        Console.WriteLine(text);
        Writer.WriteLine(text);
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

            int lastSeq = 0;
            PumpUntil(() =>
            {
                host.Poll();
                foreach (Authoritative4XProcessedCommand processed in host.DrainProcessedCommands())
                {
                    if (processed.PeerId == flow.JoinPeerId)
                    {
                        lastSeq = Math.Max(lastSeq, processed.Command.Sequence);
                        result.LastSequence = lastSeq;
                        result.LastTick = processed.Result.Tick;
                        result.FinalHash = SnapshotHash(processed.Snapshot);
                        result.FinalDigest = processed.Snapshot.SyncDigest;
                        if (lastSeq == 1 || lastSeq == options.Turns || lastSeq % 100 == 0)
                            log.Line($"processed seq={lastSeq} tick={processed.Result.Tick} accepted={processed.Result.Accepted} hash={result.FinalHash}/{result.FinalDigest}");
                    }
                }
                return lastSeq >= options.Turns;
            }, static () => { }, options, $"join commands 1..{options.Turns}");

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

            int empireId = generated.EmpireIdForPeer(flow.JoinPeerId);
            Planet planet = FirstPlanetForPeer(generated, flow.JoinPeerId);
            Planet.ColonyType target = planet.CType == Planet.ColonyType.Research
                ? Planet.ColonyType.Military
                : Planet.ColonyType.Research;
            log.Line($"script firstCommand=SetColonyType empire={empireId} planet={planet.Id} target={target}");

            for (int seq = 1; seq <= options.Turns; ++seq)
            {
                AuthoritativePlayerCommand command = seq == 1
                    ? AuthoritativePlayerCommand.SetColonyType(seq, empireId, planet.Id, target)
                    : AuthoritativePlayerCommand.NoOp(seq, empireId);
                client.Submit(command);
                int expected = seq;
                PumpUntil(() =>
                {
                    client.Poll();
                    return client.LastResult?.OriginPeer == flow.JoinPeerId
                           && client.LastResult.Sequence == expected
                           && client.LastClientSnapshot != null;
                }, static () => { }, options, $"result seq={expected}");

                result.LastSequence = seq;
                result.LastTick = client.LastResult!.Tick;
                result.FinalHash = SnapshotHash(client.LastClientSnapshot);
                result.FinalDigest = client.LastClientSnapshot!.SyncDigest;
                if (seq == 1 || seq == options.Turns || seq % 100 == 0)
                {
                    string rawDrift = client.LastRawHashDrift != null ? " rawHashDrift=true" : "";
                    log.Line($"applied seq={seq} tick={result.LastTick} accepted={client.LastResult.Accepted} hash={result.FinalHash}/{result.FinalDigest}{rawDrift}");
                }
                if (!client.LastResult.Accepted)
                    throw new InvalidOperationException($"Host rejected seq {seq}: {client.LastResult.Reason}");
            }

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

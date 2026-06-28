#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SDLockstep;
using SDUtils.Deterministic;

namespace StarDrive.Tools.ArenaLockstepProbe;

public enum PortableLockstepRole
{
    None,
    Host,
    Join,
}

public sealed class PortableLockstepOptions
{
    public const int DefaultGenerationSeed = 0x00005EED;
    public const uint DefaultRngSeed = 0xA12EA000u;
    public const int DefaultTurns = 600;
    public const int DefaultInputDelay = 3;
    public const int DefaultPort = 47377;
    public const double DefaultStepDt = 1.0 / 60.0;
    public const int ProtocolVersion = 1;

    public PortableLockstepRole Role { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = DefaultPort;
    public int GenerationSeed { get; set; } = DefaultGenerationSeed;
    public uint RngSeed { get; set; } = DefaultRngSeed;
    public int Turns { get; set; } = DefaultTurns;
    public int InputDelay { get; set; } = DefaultInputDelay;
    public double StepDt { get; set; } = DefaultStepDt;
    public bool SelfTest { get; set; }
    public bool ShowHelp { get; set; }
    public string OutputPath { get; set; } = "";

    public string SettingsHash
    {
        get
        {
            var h = DetHash.New();
            h.AddInt(ProtocolVersion);
            h.AddInt(GenerationSeed);
            h.AddUInt(RngSeed);
            h.AddInt(Turns);
            h.AddInt(InputDelay);
            h.AddDouble(StepDt);
            return "0x" + h.Value.ToString("X16", CultureInfo.InvariantCulture);
        }
    }

    public static string Usage =>
        "ArenaLockstepProbe --role host|join [--host IP] [--port N] [--turns N] [--self-test]\n" +
        "Environment mirrors args: SD_MP_ROLE, SD_MP_HOST, SD_MP_PORT, SD_MP_TURNS, SD_MP_SEED, SD_MP_RNG.\n" +
        "Example host: ArenaLockstepProbe.exe --role host --port 47377\n" +
        "Example join: ArenaLockstepProbe.exe --role join --host <host-ip> --port 47377";

    public PortableLockstepOptions Clone() => new()
    {
        Role = Role,
        Host = Host,
        Port = Port,
        GenerationSeed = GenerationSeed,
        RngSeed = RngSeed,
        Turns = Turns,
        InputDelay = InputDelay,
        StepDt = StepDt,
        SelfTest = SelfTest,
        ShowHelp = ShowHelp,
        OutputPath = OutputPath,
    };

    public SessionStartMessage ToStartMessage() => new()
    {
        FromPeer = LockstepHost.HostPeerId,
        ProtocolVersion = ProtocolVersion,
        MatchSeed = GenerationSeed,
        RngSeed = RngSeed,
        InputDelay = InputDelay,
        MaxTurns = Turns,
        SettingsHash = SettingsHash,
    };

    public static PortableLockstepOptions FromStartMessage(SessionStartMessage message, PortableLockstepOptions defaults)
    {
        var options = defaults.Clone();
        options.GenerationSeed = message.MatchSeed;
        options.RngSeed = message.RngSeed;
        options.InputDelay = Math.Max(0, message.InputDelay);
        options.Turns = Math.Max(1, message.MaxTurns);
        return options;
    }

    public static PortableLockstepOptions FromArgsAndEnvironment(string[] args, string baseDirectory)
    {
        var options = new PortableLockstepOptions
        {
            OutputPath = Path.Combine(baseDirectory, "sim-output", "arena-lockstep-probe.txt"),
        };
        ApplyEnvironment(options);

        for (int i = 0; i < args.Length; ++i)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                case "/?":
                    options.ShowHelp = true;
                    break;
                case "--self-test":
                    options.SelfTest = true;
                    break;
                case "--role":
                    options.Role = ParseRole(RequireValue(args, ref i, arg));
                    break;
                case "--host":
                    options.Host = RequireValue(args, ref i, arg);
                    break;
                case "--port":
                    options.Port = ParsePositiveInt(RequireValue(args, ref i, arg), arg);
                    break;
                case "--turns":
                    options.Turns = ParsePositiveInt(RequireValue(args, ref i, arg), arg);
                    break;
                case "--input-delay":
                    options.InputDelay = ParseNonNegativeInt(RequireValue(args, ref i, arg), arg);
                    break;
                case "--generation-seed":
                case "--seed":
                    options.GenerationSeed = unchecked((int)ParseUInt(RequireValue(args, ref i, arg), arg));
                    break;
                case "--rng-seed":
                    options.RngSeed = ParseUInt(RequireValue(args, ref i, arg), arg);
                    break;
                case "--out":
                    options.OutputPath = Path.GetFullPath(RequireValue(args, ref i, arg));
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }

        return options;
    }

    static void ApplyEnvironment(PortableLockstepOptions options)
    {
        string? role = Environment.GetEnvironmentVariable("SD_MP_ROLE");
        if (!string.IsNullOrWhiteSpace(role))
            options.Role = ParseRole(role);

        string? host = Environment.GetEnvironmentVariable("SD_MP_HOST");
        if (!string.IsNullOrWhiteSpace(host))
            options.Host = host;

        string? port = Environment.GetEnvironmentVariable("SD_MP_PORT");
        if (!string.IsNullOrWhiteSpace(port))
            options.Port = ParsePositiveInt(port, "SD_MP_PORT");

        string? turns = Environment.GetEnvironmentVariable("SD_MP_TURNS");
        if (!string.IsNullOrWhiteSpace(turns))
            options.Turns = ParsePositiveInt(turns, "SD_MP_TURNS");

        string? seed = Environment.GetEnvironmentVariable("SD_MP_SEED");
        if (!string.IsNullOrWhiteSpace(seed))
            options.GenerationSeed = unchecked((int)ParseUInt(seed, "SD_MP_SEED"));

        string? rng = Environment.GetEnvironmentVariable("SD_MP_RNG");
        if (!string.IsNullOrWhiteSpace(rng))
            options.RngSeed = ParseUInt(rng, "SD_MP_RNG");

        string? delay = Environment.GetEnvironmentVariable("SD_MP_INPUT_DELAY");
        if (!string.IsNullOrWhiteSpace(delay))
            options.InputDelay = ParseNonNegativeInt(delay, "SD_MP_INPUT_DELAY");
    }

    static PortableLockstepRole ParseRole(string text)
    {
        if (text.Equals("host", StringComparison.OrdinalIgnoreCase))
            return PortableLockstepRole.Host;
        if (text.Equals("join", StringComparison.OrdinalIgnoreCase) || text.Equals("client", StringComparison.OrdinalIgnoreCase))
            return PortableLockstepRole.Join;
        throw new ArgumentException("Role must be host or join.");
    }

    static string RequireValue(string[] args, ref int index, string arg)
    {
        if (++index >= args.Length)
            throw new ArgumentException($"{arg} requires a value.");
        return args[index];
    }

    static int ParsePositiveInt(string text, string arg)
    {
        int value = ParseNonNegativeInt(text, arg);
        if (value <= 0)
            throw new ArgumentException($"{arg} requires a positive integer.");
        return value;
    }

    static int ParseNonNegativeInt(string text, string arg)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0)
            throw new ArgumentException($"{arg} requires a non-negative integer.");
        return value;
    }

    static uint ParseUInt(string text, string arg)
    {
        NumberStyles style = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? NumberStyles.HexNumber
            : NumberStyles.Integer;
        string valueText = style == NumberStyles.HexNumber ? text[2..] : text;
        if (!uint.TryParse(valueText, style, CultureInfo.InvariantCulture, out uint value))
            throw new ArgumentException($"{arg} requires a uint value.");
        return value;
    }
}

public sealed class PortableLockstepSelfTestResult
{
    public required PortableLockstepResult InProcess { get; init; }
    public required PortableLockstepResult ForcedDesync { get; init; }
    public required PortableLockstepResult LoopbackHost { get; init; }
    public required PortableLockstepResult LoopbackJoin { get; init; }
}

public sealed class PortableLockstepResult
{
    public readonly List<string> HeaderLines = new();
    public readonly List<string> TurnLines = new();
    public bool Desynced;
    public long DesyncTurn = -1;
    public string DesyncReason = "";
    public int TurnsCompleted;
    public int CommandsSubmitted;
    public string FinalHash = "";
    public string SequenceSha256 = "";

    public void FinalizeDigest()
    {
        SequenceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", TurnLines))))
            .ToLowerInvariant();
    }

    public string ToFileText()
    {
        var sb = new StringBuilder();
        foreach (string line in HeaderLines)
            sb.AppendLine(line);
        sb.AppendLine($"FinalHash={FinalHash}");
        sb.AppendLine($"SequenceSha256={SequenceSha256}");
        sb.AppendLine($"TurnsCompleted={TurnsCompleted}");
        sb.AppendLine($"CommandsSubmitted={CommandsSubmitted}");
        sb.AppendLine($"Desynced={Desynced}");
        sb.AppendLine($"DesyncTurn={DesyncTurn}");
        sb.AppendLine($"DesyncReason={DesyncReason}");
        sb.AppendLine();
        foreach (string line in TurnLines)
            sb.AppendLine(line);
        return sb.ToString();
    }
}

public static class PortableLockstepRunner
{
    public const int HostPlayerPeerId = 1;
    public const int JoinPlayerPeerId = 2;
    const string Profile = "PortableNetworkLockstep-v1";

    public static PortableLockstepSelfTestResult RunSelfTest(PortableLockstepOptions options)
    {
        PortableLockstepOptions test = options.Clone();
        test.Turns = Math.Min(test.Turns, 240);
        if (test.Turns < 80)
            test.Turns = 80;

        PortableLockstepResult inProcess = RunInProcess(test);
        if (inProcess.Desynced)
            throw new InvalidOperationException($"In-process lockstep desynced at {inProcess.DesyncTurn}: {inProcess.DesyncReason}");

        PortableLockstepResult forced = RunInProcess(test, forceDivergenceTurn: 30);
        if (!forced.Desynced)
            throw new InvalidOperationException("Forced divergence did not trip desync detection.");

        (PortableLockstepResult host, PortableLockstepResult join) = RunLoopback(test);
        if (host.Desynced || join.Desynced)
            throw new InvalidOperationException($"Loopback desynced: host={host.DesyncReason} join={join.DesyncReason}");
        if (host.FinalHash != join.FinalHash)
            throw new InvalidOperationException($"Loopback final hash mismatch: host={host.FinalHash} join={join.FinalHash}");

        Write(host, test.OutputPath);
        return new PortableLockstepSelfTestResult
        {
            InProcess = inProcess,
            ForcedDesync = forced,
            LoopbackHost = host,
            LoopbackJoin = join,
        };
    }

    public static PortableLockstepResult RunInProcess(PortableLockstepOptions options, int forceDivergenceTurn = -1)
    {
        var transport = new FakeTransport();
        var host = new LockstepHost(transport);
        var simA = new SyntheticArenaLockstepSimulation(options.GenerationSeed, options.RngSeed, (float)options.StepDt);
        var simB = new SyntheticArenaLockstepSimulation(options.GenerationSeed, options.RngSeed, (float)options.StepDt);
        var clientA = new LockstepClient(transport, HostPlayerPeerId, simA);
        var clientB = new LockstepClient(transport, JoinPlayerPeerId, simB);
        host.AddClient(HostPlayerPeerId);
        host.AddClient(JoinPlayerPeerId);

        PortableLockstepResult result = NewResult(options, "in-process");
        for (uint turn = 0; turn < options.Turns; ++turn)
        {
            SubmitTurnInputs(options, turn, clientA, simA, clientB, simB, result);
            transport.Poll();
            host.CommitTick(turn);
            transport.Poll();
            clientA.Pump();
            clientB.Pump();
            if (forceDivergenceTurn >= 0 && turn == forceDivergenceTurn)
                simB.ForceDivergenceForTest();
            transport.Poll();

            RecordDualTurn(result, turn, simA.Hash(), simB.Hash(), host.Desync);
            if (result.Desynced)
                break;
        }
        Finish(result);
        return result;
    }

    public static (PortableLockstepResult host, PortableLockstepResult join) RunLoopback(PortableLockstepOptions options)
    {
        int port = FreeTcpPort();
        PortableLockstepOptions hostOptions = options.Clone();
        PortableLockstepOptions joinOptions = options.Clone();
        hostOptions.Port = port;
        joinOptions.Port = port;
        joinOptions.Host = "127.0.0.1";
        joinOptions.OutputPath = RoleOutputPath(options.OutputPath, "join");

        PortableLockstepResult? hostResult = null;
        PortableLockstepResult? joinResult = null;
        Exception? hostError = null;
        Exception? joinError = null;

        Task hostTask = Task.Run(() =>
        {
            try { hostResult = RunHost(hostOptions, _ => { }); }
            catch (Exception ex) { hostError = ex; }
        });
        Task.Delay(150).Wait();
        Task joinTask = Task.Run(() =>
        {
            try { joinResult = RunJoin(joinOptions, _ => { }); }
            catch (Exception ex) { joinError = ex; }
        });

        Task.WaitAll(new[] { hostTask, joinTask }, TimeSpan.FromSeconds(45));
        if (!hostTask.IsCompleted || !joinTask.IsCompleted)
            throw new TimeoutException("Loopback portable lockstep probe timed out.");
        if (hostError != null) throw new InvalidOperationException("Loopback host failed.", hostError);
        if (joinError != null) throw new InvalidOperationException("Loopback join failed.", joinError);
        return (hostResult!, joinResult!);
    }

    static string RoleOutputPath(string path, string role)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        string directory = Path.GetDirectoryName(path) ?? "";
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        return Path.Combine(directory, $"{name}-{role}{extension}");
    }

    public static PortableLockstepResult RunHost(PortableLockstepOptions options, Action<string>? log = null)
    {
        using TcpLockstepTransport transport = TcpLockstepTransport.Host(options.Port, JoinPlayerPeerId);
        log?.Invoke($"[arena-lockstep] HOST listening port={options.Port}");
        if (!transport.WaitForConnection(TimeSpan.FromSeconds(60)))
            throw new TimeoutException("Timed out waiting for join peer.");

        int remoteReadyCount = 0;
        transport.AddObserver(LockstepHost.HostPeerId, m =>
        {
            if (m is SessionReadyMessage ready && ready.PeerId == JoinPlayerPeerId && ready.Ready)
                remoteReadyCount++;
        });
        WaitFor(() => remoteReadyCount >= 1, transport, TimeSpan.FromSeconds(30), "Join peer connected but did not ready-up.");

        transport.Send(JoinPlayerPeerId, options.ToStartMessage());
        var sim = new SyntheticArenaLockstepSimulation(options.GenerationSeed, options.RngSeed, (float)options.StepDt);
        var host = new LockstepHost(transport);
        var client = new LockstepClient(transport, HostPlayerPeerId, sim);
        host.AddClient(HostPlayerPeerId);
        host.AddClient(JoinPlayerPeerId);

        long remoteChecksumTick = -1;
        var submittedInputs = new Dictionary<uint, HashSet<int>>();
        transport.AddObserver(LockstepHost.HostPeerId, m =>
        {
            if (m is ChecksumMessage c && c.FromPeer == JoinPlayerPeerId)
                remoteChecksumTick = Math.Max(remoteChecksumTick, c.Tick);
            if (m is SubmitCommandMessage s)
            {
                if (!submittedInputs.TryGetValue(s.Command.Tick, out HashSet<int>? peers))
                {
                    peers = new HashSet<int>();
                    submittedInputs[s.Command.Tick] = peers;
                }
                peers.Add(s.Command.PlayerId);
            }
        });

        WaitFor(() => remoteReadyCount >= 2, transport, TimeSpan.FromSeconds(30),
            "Join peer received settings but did not arm the simulation.");

        PortableLockstepResult result = NewResult(options, "host");
        for (uint turn = 0; turn < options.Turns; ++turn)
        {
            SubmitOneInput(options, turn, client, sim, HostPlayerPeerId, result);
            transport.Poll();
            if (turn >= options.InputDelay)
            {
                WaitFor(() => HasBothInputs(submittedInputs, turn), transport, TimeSpan.FromSeconds(15),
                    $"Both peers did not submit input for turn {turn}.");
            }
            host.CommitTick(turn);
            transport.Poll();
            client.Pump();
            transport.Poll();
            WaitFor(() => sim.Tick > turn, transport, TimeSpan.FromSeconds(5), $"Host local sim did not apply turn {turn}.");
            WaitFor(() => remoteChecksumTick >= turn || host.Desync.HasDesync, transport, TimeSpan.FromSeconds(15),
                $"Join peer did not report checksum for turn {turn}.");
            RecordSingleTurn(result, turn, sim.Hash(), host.Desync);
            if (result.Desynced)
            {
                transport.Send(JoinPlayerPeerId,
                    new SessionErrorMessage { FromPeer = LockstepHost.HostPeerId, Error = result.DesyncReason });
                break;
            }
        }
        Finish(result);
        Write(result, options.OutputPath);
        log?.Invoke($"[arena-lockstep] HOST final={result.FinalHash} sequence={result.SequenceSha256} desynced={result.Desynced}");
        return result;
    }

    public static PortableLockstepResult RunJoin(PortableLockstepOptions options, Action<string>? log = null)
    {
        using TcpLockstepTransport transport = TcpLockstepTransport.Join(options.Host, options.Port, LockstepHost.HostPeerId);
        log?.Invoke($"[arena-lockstep] JOIN connected {options.Host}:{options.Port}");

        SessionStartMessage? start = null;
        string remoteError = "";
        transport.AddObserver(JoinPlayerPeerId, m =>
        {
            if (m is SessionStartMessage s)
                start = s;
            if (m is SessionErrorMessage e)
                remoteError = e.Error;
        });
        transport.Send(LockstepHost.HostPeerId,
            new SessionHelloMessage { FromPeer = JoinPlayerPeerId, ProtocolVersion = PortableLockstepOptions.ProtocolVersion, PeerId = JoinPlayerPeerId, PlayerName = Environment.MachineName });
        transport.Send(LockstepHost.HostPeerId,
            new SessionReadyMessage { FromPeer = JoinPlayerPeerId, PeerId = JoinPlayerPeerId, Ready = true });
        WaitFor(() => start != null, transport, TimeSpan.FromSeconds(60), "Host did not send start settings.");

        PortableLockstepOptions match = PortableLockstepOptions.FromStartMessage(start!, options);
        var sim = new SyntheticArenaLockstepSimulation(match.GenerationSeed, match.RngSeed, (float)match.StepDt);
        var client = new LockstepClient(transport, JoinPlayerPeerId, sim);
        transport.Send(LockstepHost.HostPeerId,
            new SessionReadyMessage { FromPeer = JoinPlayerPeerId, PeerId = JoinPlayerPeerId, Ready = true });
        PortableLockstepResult result = NewResult(match, "join");

        for (uint turn = 0; turn < match.Turns; ++turn)
        {
            SubmitOneInput(match, turn, client, sim, JoinPlayerPeerId, result);
            transport.Poll();
            client.Pump();
            transport.Poll();
            WaitForClientTick(client, sim, transport, () => remoteError.Length > 0, turn, TimeSpan.FromSeconds(15),
                $"Did not receive/apply frame {turn}.");
            if (remoteError.Length > 0)
            {
                result.Desynced = true;
                result.DesyncTurn = turn;
                result.DesyncReason = remoteError;
                break;
            }
            RecordSingleTurn(result, turn, sim.Hash(), null);
        }
        Finish(result);
        Write(result, match.OutputPath);
        log?.Invoke($"[arena-lockstep] JOIN final={result.FinalHash} sequence={result.SequenceSha256} desynced={result.Desynced}");
        return result;
    }

    static PortableLockstepResult NewResult(PortableLockstepOptions options, string role)
    {
        var result = new PortableLockstepResult();
        result.HeaderLines.Add("# StarDrive Arena portable network lockstep probe");
        result.HeaderLines.Add($"GeneratedUtc={DateTime.UtcNow:O}");
        result.HeaderLines.Add("ContentMode=content-free synthetic arena lockstep");
        result.HeaderLines.Add($"Role={role}");
        result.HeaderLines.Add($"RuntimeVersion={RuntimeInformation.FrameworkDescription}");
        result.HeaderLines.Add($"OS={RuntimeInformation.OSDescription}");
        result.HeaderLines.Add($"ProcessArchitecture={RuntimeInformation.ProcessArchitecture}");
        result.HeaderLines.Add($"CpuModel={CpuModelForHeader()}");
        result.HeaderLines.Add($"ProcessorCount={Environment.ProcessorCount}");
        result.HeaderLines.Add($"MachineName={Environment.MachineName}");
        result.HeaderLines.Add($"Profile={Profile}");
        result.HeaderLines.Add($"GenerationSeed=0x{options.GenerationSeed:X8}");
        result.HeaderLines.Add($"RngSeed=0x{options.RngSeed:X8}");
        result.HeaderLines.Add($"Turns={options.Turns}");
        result.HeaderLines.Add($"InputDelay={options.InputDelay}");
        result.HeaderLines.Add($"StepDt={options.StepDt.ToString("R", CultureInfo.InvariantCulture)}");
        result.HeaderLines.Add($"SettingsHash={options.SettingsHash}");
        return result;
    }

    static void SubmitTurnInputs(PortableLockstepOptions options, uint turn,
        LockstepClient clientA, SyntheticArenaLockstepSimulation simA,
        LockstepClient clientB, SyntheticArenaLockstepSimulation simB,
        PortableLockstepResult result)
    {
        SubmitOneInput(options, turn, clientA, simA, HostPlayerPeerId, result);
        SubmitOneInput(options, turn, clientB, simB, JoinPlayerPeerId, result);
    }

    static void SubmitOneInput(PortableLockstepOptions options, uint turn,
        LockstepClient client, SyntheticArenaLockstepSimulation sim, int peerId,
        PortableLockstepResult result)
    {
        uint execTick = turn + (uint)Math.Max(0, options.InputDelay);
        client.Submit(sim.BuildFocusCommand(peerId, execTick, turn));
        result.CommandsSubmitted++;
    }

    static void RecordDualTurn(PortableLockstepResult result, uint turn,
        (ulong lo, ulong hi) a, (ulong lo, ulong hi) b, DesyncDetector desync)
    {
        string line = $"turn={turn:D4} host={Hex(a.hi)}:{Hex(a.lo)} join={Hex(b.hi)}:{Hex(b.lo)} match={a == b}";
        result.TurnLines.Add(line);
        result.TurnsCompleted = result.TurnLines.Count;
        if (desync.HasDesync || a != b)
        {
            result.Desynced = true;
            result.DesyncTurn = desync.HasDesync ? desync.FirstDivergentTick : turn;
            result.DesyncReason = desync.HasDesync
                ? $"host detector saw peer {desync.DivergentPeer} diverge"
                : "local hash comparison diverged";
        }
    }

    static void RecordSingleTurn(PortableLockstepResult result, uint turn,
        (ulong lo, ulong hi) hash, DesyncDetector? desync)
    {
        result.TurnLines.Add($"turn={turn:D4} hash={Hex(hash.hi)}:{Hex(hash.lo)}");
        result.TurnsCompleted = result.TurnLines.Count;
        if (desync != null && desync.HasDesync)
        {
            result.Desynced = true;
            result.DesyncTurn = desync.FirstDivergentTick;
            result.DesyncReason = $"host detector saw peer {desync.DivergentPeer} diverge";
        }
    }

    static void Finish(PortableLockstepResult result)
    {
        string last = result.TurnLines.Count == 0 ? "" : result.TurnLines[^1];
        int idx = last.IndexOf("hash=", StringComparison.Ordinal);
        if (idx >= 0)
            result.FinalHash = last[(idx + 5)..].Split(' ')[0];
        else
        {
            idx = last.IndexOf("host=", StringComparison.Ordinal);
            result.FinalHash = idx >= 0 ? last[(idx + 5)..].Split(' ')[0] : "";
        }
        result.FinalizeDigest();
    }

    static bool HasBothInputs(Dictionary<uint, HashSet<int>> submittedInputs, uint turn)
        => submittedInputs.TryGetValue(turn, out HashSet<int>? peers)
           && peers.Contains(HostPlayerPeerId)
           && peers.Contains(JoinPlayerPeerId);

    static string Write(PortableLockstepResult result, string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, result.ToFileText(), Encoding.UTF8);
        return fullPath;
    }

    static void WaitFor(Func<bool> done, ILockstepTransport transport, TimeSpan timeout, string error)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!done() && DateTime.UtcNow < deadline)
        {
            transport.Poll();
            Thread.Sleep(1);
        }
        if (!done())
            throw new TimeoutException(error);
    }

    static void WaitForClientTick(LockstepClient client, ILockstepSimulation sim, ILockstepTransport transport,
        Func<bool> stopEarly, uint turn, TimeSpan timeout, string error)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (sim.Tick <= turn && !stopEarly() && DateTime.UtcNow < deadline)
        {
            transport.Poll();
            client.Pump();
            transport.Poll();
            Thread.Sleep(1);
        }
        if (sim.Tick <= turn && !stopEarly())
            throw new TimeoutException(error);
    }

    static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    static string CpuModelForHeader()
    {
        string? id = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (!string.IsNullOrWhiteSpace(id))
            return id;
        return RuntimeInformation.ProcessArchitecture.ToString();
    }

    static string Hex(ulong value) => "0x" + value.ToString("X16", CultureInfo.InvariantCulture);
}

sealed class SyntheticArenaLockstepSimulation : ILockstepSimulation
{
    readonly List<SyntheticShip> Ships = new();
    readonly List<SyntheticProjectile> Projectiles = new();
    readonly float Dt;
    readonly int[] FocusTarget = { -1, -1 };
    DetRandom Rng;
    int NextProjectileId = 1;
    int LastShipCollisions;
    int LastProjectileHits;
    public uint Tick { get; private set; }

    const float ArenaCenterX = 950000f;
    const float ArenaCenterY = -725000f;
    const float ArenaRadius = 1800f;

    public SyntheticArenaLockstepSimulation(int generationSeed, uint rngSeed, float dt)
    {
        Dt = dt;
        ulong root = ((ulong)(uint)generationSeed << 32) ^ rngSeed;
        Rng = new DetRandom(root);
        SpawnShips();
    }

    public void Apply(CommandFrame frame)
    {
        for (int i = 0; i < frame.Commands.Count; ++i)
            ApplyCommand(frame.Commands[i]);
        Step();
        Tick++;
    }

    public (ulong lo, ulong hi) Hash()
    {
        SyntheticArenaHash hash = ComputeHash();
        return (hash.AuthLo, hash.AuthHi);
    }

    public SimCommand BuildFocusCommand(int peerId, uint tick, uint sequence)
    {
        int team = peerId == PortableLockstepRunner.HostPlayerPeerId ? 0 : 1;
        SyntheticShip? subject = FirstAlive(team);
        SyntheticShip? target = FirstAlive(1 - team);
        if (subject == null || target == null)
            return new SimCommand(tick, peerId, sequence, SimCommandKind.NoOp);
        return new SimCommand(tick, peerId, sequence, SimCommandKind.AttackTarget, subject.Id, target.Id);
    }

    public void ForceDivergenceForTest()
    {
        SyntheticShip? ship = FirstAlive(0);
        if (ship != null)
            ship.X += 7f;
    }

    void ApplyCommand(in SimCommand command)
    {
        if (command.Kind == SimCommandKind.NoOp)
            return;
        int team = command.PlayerId == PortableLockstepRunner.HostPlayerPeerId ? 0 : 1;
        if (command.Kind == SimCommandKind.StopShip)
        {
            FocusTarget[team] = -1;
            return;
        }
        if (command.Kind == SimCommandKind.AttackTarget && IsAliveEnemy(team, command.TargetId))
            FocusTarget[team] = command.TargetId;
    }

    void SpawnShips()
    {
        const int perSide = 6;
        for (int team = 0; team < 2; ++team)
        {
            float side = team == 0 ? -1f : 1f;
            for (int i = 0; i < perSide; ++i)
            {
                float lane = i - (perSide - 1) * 0.5f;
                float radius = 30f + (i % 3) * 7f + Rng.NextFloat(-2.5f, 2.5f);
                Ships.Add(new SyntheticShip
                {
                    Id = team * 100 + i + 1,
                    Team = team,
                    Model = $"SYN-{team}-{i}",
                    X = ArenaCenterX + side * (760f + 36f * i),
                    Y = ArenaCenterY + lane * 148f + Rng.NextFloat(-8f, 8f),
                    VX = -side * Rng.NextFloat(10f, 30f),
                    VY = Rng.NextFloat(-24f, 24f),
                    Rotation = team == 0 ? 0f : MathF.PI,
                    AngularVelocity = Rng.NextFloat(-0.08f, 0.08f),
                    Radius = radius,
                    Mass = radius * radius * 0.08f,
                    Hull = 145f + i * 11f + Rng.NextFloat(0f, 8f),
                    Shield = 70f + (perSide - i) * 4f,
                    Cooldown = Rng.NextFloat(0.05f, 0.7f),
                    Heat = Rng.NextFloat(0f, 0.2f),
                    Alive = true,
                });
            }
        }
    }

    void Step()
    {
        LastShipCollisions = 0;
        LastProjectileHits = 0;
        for (int i = 0; i < Ships.Count; ++i)
            UpdateShip(Ships[i]);
        for (int i = 0; i < Projectiles.Count; ++i)
            UpdateProjectile(Projectiles[i]);
        ResolveShipCollisions();
        ResolveProjectileHits();
        Projectiles.RemoveAll(p => !p.Alive || p.Ttl <= 0f);
    }

    void UpdateShip(SyntheticShip ship)
    {
        if (!ship.Alive) return;

        SyntheticShip? target = FocusedOrNearestEnemy(ship);
        if (target != null)
        {
            float dx = target.X - ship.X;
            float dy = target.Y - ship.Y;
            float desired = MathF.Atan2(dy, dx);
            float turn = NormalizeAngle(desired - ship.Rotation);
            ship.AngularVelocity += Clamp(turn * 0.9f, -1.2f, 1.2f) * Dt;
        }

        ship.AngularVelocity *= 0.985f;
        ship.Rotation = NormalizeAngle(ship.Rotation + ship.AngularVelocity * Dt);
        float thrust = 28f + ship.Heat * 8f + Rng.NextFloat(-2.0f, 2.0f);
        ship.VX += MathF.Cos(ship.Rotation) * thrust * Dt;
        ship.VY += MathF.Sin(ship.Rotation) * thrust * Dt;
        ship.VX *= 0.998f;
        ship.VY *= 0.998f;
        ApplyArenaBoundary(ship);
        ship.X += ship.VX * Dt;
        ship.Y += ship.VY * Dt;
        ship.Cooldown -= Dt;
        ship.Heat = MathF.Max(0f, ship.Heat - 0.15f * Dt);
        if (target != null && ship.Cooldown <= 0f)
            Fire(ship, target);
    }

    void UpdateProjectile(SyntheticProjectile projectile)
    {
        if (!projectile.Alive) return;
        projectile.X += projectile.VX * Dt;
        projectile.Y += projectile.VY * Dt;
        projectile.VX *= 0.9995f;
        projectile.VY *= 0.9995f;
        projectile.Ttl -= Dt;
    }

    void Fire(SyntheticShip ship, SyntheticShip target)
    {
        float dx = target.X - ship.X;
        float dy = target.Y - ship.Y;
        float distance = MathF.Max(1f, MathF.Sqrt(dx * dx + dy * dy));
        float nx = dx / distance;
        float ny = dy / distance;
        float jitter = Rng.NextFloat(-0.018f, 0.018f);
        float jx = nx * MathF.Cos(jitter) - ny * MathF.Sin(jitter);
        float jy = nx * MathF.Sin(jitter) + ny * MathF.Cos(jitter);
        Projectiles.Add(new SyntheticProjectile
        {
            Id = NextProjectileId++,
            Team = ship.Team,
            X = ship.X + jx * (ship.Radius + 8f),
            Y = ship.Y + jy * (ship.Radius + 8f),
            VX = ship.VX + jx * (620f + Rng.NextFloat(-25f, 25f)),
            VY = ship.VY + jy * (620f + Rng.NextFloat(-25f, 25f)),
            Radius = 5.5f,
            Damage = 13f + Rng.NextFloat(-1.5f, 2.5f),
            Ttl = 3.8f,
            Alive = true,
        });
        ship.Cooldown = 0.36f + ship.Radius * 0.003f + Rng.NextFloat(0.01f, 0.09f);
        ship.Heat += 0.08f;
    }

    void ResolveShipCollisions()
    {
        for (int i = 0; i < Ships.Count; ++i)
        {
            SyntheticShip a = Ships[i];
            if (!a.Alive) continue;
            for (int j = i + 1; j < Ships.Count; ++j)
            {
                SyntheticShip b = Ships[j];
                if (!b.Alive) continue;
                float dx = b.X - a.X;
                float dy = b.Y - a.Y;
                float radius = a.Radius + b.Radius;
                float distSq = dx * dx + dy * dy;
                if (distSq >= radius * radius)
                    continue;

                float dist = MathF.Max(0.001f, MathF.Sqrt(distSq));
                float nx = dx / dist;
                float ny = dy / dist;
                float overlap = radius - dist;
                float totalMass = a.Mass + b.Mass;
                a.X -= nx * overlap * (b.Mass / totalMass);
                a.Y -= ny * overlap * (b.Mass / totalMass);
                b.X += nx * overlap * (a.Mass / totalMass);
                b.Y += ny * overlap * (a.Mass / totalMass);

                float rvx = b.VX - a.VX;
                float rvy = b.VY - a.VY;
                float rel = rvx * nx + rvy * ny;
                if (rel < 0f)
                {
                    float impulse = -(1.22f * rel) / (1f / a.Mass + 1f / b.Mass);
                    float ix = impulse * nx;
                    float iy = impulse * ny;
                    a.VX -= ix / a.Mass;
                    a.VY -= iy / a.Mass;
                    b.VX += ix / b.Mass;
                    b.VY += iy / b.Mass;
                }
                float scrape = MathF.Min(2.5f, overlap * 0.015f);
                ApplyDamage(a, scrape);
                ApplyDamage(b, scrape);
                ++LastShipCollisions;
            }
        }
    }

    void ResolveProjectileHits()
    {
        for (int i = 0; i < Projectiles.Count; ++i)
        {
            SyntheticProjectile p = Projectiles[i];
            if (!p.Alive) continue;
            SyntheticShip? hit = null;
            float bestDistSq = float.MaxValue;
            for (int j = 0; j < Ships.Count; ++j)
            {
                SyntheticShip ship = Ships[j];
                if (!ship.Alive || ship.Team == p.Team)
                    continue;
                float dx = ship.X - p.X;
                float dy = ship.Y - p.Y;
                float radius = ship.Radius + p.Radius;
                float distSq = dx * dx + dy * dy;
                if (distSq <= radius * radius && distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    hit = ship;
                }
            }
            if (hit == null)
                continue;
            ApplyDamage(hit, p.Damage);
            p.Alive = false;
            ++LastProjectileHits;
        }
    }

    void ApplyDamage(SyntheticShip ship, float damage)
    {
        if (damage <= 0f || !ship.Alive) return;
        float shieldDamage = MathF.Min(ship.Shield, damage);
        ship.Shield -= shieldDamage;
        ship.Hull -= damage - shieldDamage;
        if (ship.Hull <= 0f)
        {
            ship.Hull = 0f;
            ship.Alive = false;
            ship.VX = 0f;
            ship.VY = 0f;
        }
    }

    void ApplyArenaBoundary(SyntheticShip ship)
    {
        float dx = ship.X - ArenaCenterX;
        float dy = ship.Y - ArenaCenterY;
        float distSq = dx * dx + dy * dy;
        float radius = ArenaRadius - ship.Radius;
        if (distSq <= radius * radius)
            return;
        float dist = MathF.Max(0.001f, MathF.Sqrt(distSq));
        float nx = dx / dist;
        float ny = dy / dist;
        ship.X = ArenaCenterX + nx * radius;
        ship.Y = ArenaCenterY + ny * radius;
        float outward = ship.VX * nx + ship.VY * ny;
        if (outward > 0f)
        {
            ship.VX -= 1.85f * outward * nx;
            ship.VY -= 1.85f * outward * ny;
        }
    }

    SyntheticShip? FocusedOrNearestEnemy(SyntheticShip ship)
    {
        int focusedId = FocusTarget[ship.Team];
        if (focusedId >= 0)
        {
            SyntheticShip? focused = Ships.FirstOrDefault(s => s.Id == focusedId && s.Alive && s.Team != ship.Team);
            if (focused != null)
                return focused;
        }
        return NearestEnemy(ship);
    }

    SyntheticShip? NearestEnemy(SyntheticShip ship)
    {
        SyntheticShip? best = null;
        float bestDistSq = float.MaxValue;
        for (int i = 0; i < Ships.Count; ++i)
        {
            SyntheticShip candidate = Ships[i];
            if (!candidate.Alive || candidate.Team == ship.Team)
                continue;
            float dx = candidate.X - ship.X;
            float dy = candidate.Y - ship.Y;
            float distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = candidate;
            }
        }
        return best;
    }

    SyntheticShip? FirstAlive(int team)
        => Ships.Where(s => s.Team == team && s.Alive).OrderBy(s => s.Id).FirstOrDefault();

    bool IsAliveEnemy(int team, int id)
        => Ships.Any(s => s.Id == id && s.Alive && s.Team != team);

    SyntheticArenaHash ComputeHash()
    {
        var auth = new Hash128Checksum();
        auth.WriteInt((int)Tick);
        auth.WriteULong(Rng.State);
        auth.WriteInt(NextProjectileId);
        auth.WriteInt(LastShipCollisions);
        auth.WriteInt(LastProjectileHits);
        auth.WriteInt(FocusTarget[0]);
        auth.WriteInt(FocusTarget[1]);
        WriteShips(auth);
        WriteProjectiles(auth);
        (ulong lo, ulong hi) = auth.Finish128();
        return new SyntheticArenaHash(lo, hi);
    }

    void WriteShips(IDeterminismChecksum checksum)
    {
        checksum.WriteInt(Ships.Count);
        for (int i = 0; i < Ships.Count; ++i)
        {
            SyntheticShip ship = Ships[i];
            checksum.WriteInt(ship.Id);
            checksum.WriteInt(ship.Team);
            checksum.WriteString(ship.Model);
            checksum.WriteBool(ship.Alive);
            checksum.FloatRaw(ship.X);
            checksum.FloatRaw(ship.Y);
            checksum.FloatRaw(ship.VX);
            checksum.FloatRaw(ship.VY);
            checksum.FloatRaw(ship.Rotation);
            checksum.FloatRaw(ship.AngularVelocity);
            checksum.FloatRaw(ship.Radius);
            checksum.FloatRaw(ship.Mass);
            checksum.FloatRaw(ship.Hull);
            checksum.FloatRaw(ship.Shield);
            checksum.FloatRaw(ship.Cooldown);
            checksum.FloatRaw(ship.Heat);
        }
    }

    void WriteProjectiles(IDeterminismChecksum checksum)
    {
        checksum.WriteInt(Projectiles.Count);
        for (int i = 0; i < Projectiles.Count; ++i)
        {
            SyntheticProjectile p = Projectiles[i];
            checksum.WriteInt(p.Id);
            checksum.WriteInt(p.Team);
            checksum.WriteBool(p.Alive);
            checksum.FloatRaw(p.X);
            checksum.FloatRaw(p.Y);
            checksum.FloatRaw(p.VX);
            checksum.FloatRaw(p.VY);
            checksum.FloatRaw(p.Radius);
            checksum.FloatRaw(p.Damage);
            checksum.FloatRaw(p.Ttl);
        }
    }

    static float NormalizeAngle(float radians)
    {
        while (radians > MathF.PI) radians -= MathF.Tau;
        while (radians < -MathF.PI) radians += MathF.Tau;
        return radians;
    }

    static float Clamp(float value, float min, float max)
        => value < min ? min : value > max ? max : value;

    sealed class SyntheticShip
    {
        public int Id;
        public int Team;
        public string Model = "";
        public float X;
        public float Y;
        public float VX;
        public float VY;
        public float Rotation;
        public float AngularVelocity;
        public float Radius;
        public float Mass;
        public float Hull;
        public float Shield;
        public float Cooldown;
        public float Heat;
        public bool Alive;
    }

    sealed class SyntheticProjectile
    {
        public int Id;
        public int Team;
        public float X;
        public float Y;
        public float VX;
        public float VY;
        public float Radius;
        public float Damage;
        public float Ttl;
        public bool Alive;
    }
}

readonly struct SyntheticArenaHash
{
    public readonly ulong AuthLo;
    public readonly ulong AuthHi;

    public SyntheticArenaHash(ulong lo, ulong hi)
    {
        AuthLo = lo;
        AuthHi = hi;
    }
}

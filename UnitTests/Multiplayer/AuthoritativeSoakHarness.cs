using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDGraphics;
using Ship_Game;
using Ship_Game.AI;
using Ship_Game.Data;
using Ship_Game.Determinism;
using Ship_Game.Fleets;
using Ship_Game.GameScreens.ShipDesign;
using Ship_Game.Gameplay;
using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.Ships;
using Ship_Game.Ships.AI;
using Ship_Game.Universe;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Rectangle = SDGraphics.Rectangle;
using Vector2 = SDGraphics.Vector2;
using Vector3d = SDGraphics.Vector3d;

namespace UnitTests.Multiplayer;

[Flags]
public enum AuthoritativeSoakHazards
{
    None = 0,
    FocusUnfocus = 1,
    ForcedResync = 2,
    CameraIndependence = 4,
    All = FocusUnfocus | ForcedResync | CameraIndependence,
}

public sealed class AuthoritativeSoakHarnessConfig
{
    public ulong[] Seeds = { 0x50440001UL };
    public int Ticks = 500;
    public int Clients = 2;
    public AuthoritativeSoakHazards Hazards = AuthoritativeSoakHazards.All;
    public string ArtifactDirectory = "";
    public bool PersistSuccessfulReplay;

    public AuthoritativeSoakHarnessConfig Clone()
        => new()
        {
            Seeds = Seeds?.ToArray() ?? Array.Empty<ulong>(),
            Ticks = Ticks,
            Clients = Clients,
            Hazards = Hazards,
            ArtifactDirectory = ArtifactDirectory,
            PersistSuccessfulReplay = PersistSuccessfulReplay,
        };
}

public enum AuthoritativeSoakOutcome
{
    Completed,
    KnownGapDetected,
    NewDivergence,
}

public sealed class AuthoritativeSoakMatrixResult
{
    public readonly List<AuthoritativeSoakRunResult> Runs = new();

    public bool HasNewDivergence => Runs.Any(r => r.Outcome == AuthoritativeSoakOutcome.NewDivergence);
    public bool HasKnownGap => Runs.Any(r => r.Outcome == AuthoritativeSoakOutcome.KnownGapDetected);
    public AuthoritativeSoakRunResult FirstNewDivergence => Runs.FirstOrDefault(r => r.Outcome == AuthoritativeSoakOutcome.NewDivergence);

    public string Summary
        => string.Join("; ", Runs.Select(r => r.Summary));
}

public sealed class AuthoritativeSoakRunResult
{
    public AuthoritativeSoakOutcome Outcome;
    public ulong Seed;
    public int TicksRequested;
    public int Clients;
    public AuthoritativeSoakHazards Hazards;
    public int DivergenceTick;
    public int DivergencePeer;
    public string DivergenceLabel = "";
    public string KnownGapDescriptor = "";
    public string FirstDiff = "";
    public string LaneDiff = "";
    public string ArtifactPath = "";
    public readonly List<AuthoritativeSoakCommandLogEntry> CommandLog = new();
    public readonly List<string> HostDigests = new();

    public string Summary
    {
        get
        {
            string seed = "0x" + Seed.ToString("X", CultureInfo.InvariantCulture);
            return Outcome switch
            {
                AuthoritativeSoakOutcome.Completed =>
                    $"completed seed={seed} ticks={TicksRequested} clients={Clients}",
                AuthoritativeSoakOutcome.KnownGapDetected =>
                    $"known-gap seed={seed} tick={DivergenceTick} peer={DivergencePeer} descriptor={KnownGapDescriptor}",
                _ =>
                    $"NEW-DIVERGENCE seed={seed} tick={DivergenceTick} peer={DivergencePeer} {FirstDiff}",
            };
        }
    }
}

public sealed class AuthoritativeSoakReplaySpec
{
    public ulong Seed;
    public int Clients;
    public int Ticks;
    public AuthoritativeSoakHazards Hazards;
    public readonly List<AuthoritativeSoakCommandLogEntry> Commands = new();
}

public sealed class AuthoritativeSoakCommandLogEntry
{
    public int Tick;
    public int PeerId;
    public AuthoritativePlayerCommand Command;

    public string Summary
        => $"tick={Tick} peer={PeerId} seq={Command.Sequence} empire={Command.EmpireId} kind={Command.Kind} "
           + $"subject={Command.SubjectId} target={Command.TargetId} pos=({Command.Position.X.ToString(CultureInfo.InvariantCulture)},"
           + $"{Command.Position.Y.ToString(CultureInfo.InvariantCulture)}) text='{Command.Text ?? ""}'";

    public string ToLogLine()
    {
        string text64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Command.Text ?? ""));
        return string.Join("|",
            Tick.ToString(CultureInfo.InvariantCulture),
            PeerId.ToString(CultureInfo.InvariantCulture),
            Command.Sequence.ToString(CultureInfo.InvariantCulture),
            Command.EmpireId.ToString(CultureInfo.InvariantCulture),
            ((byte)Command.Kind).ToString(CultureInfo.InvariantCulture),
            Command.SubjectId.ToString(CultureInfo.InvariantCulture),
            Command.TargetId.ToString(CultureInfo.InvariantCulture),
            BitConverter.SingleToUInt32Bits(Command.Position.X).ToString("X8", CultureInfo.InvariantCulture),
            BitConverter.SingleToUInt32Bits(Command.Position.Y).ToString("X8", CultureInfo.InvariantCulture),
            text64);
    }

    public static AuthoritativeSoakCommandLogEntry Parse(string line)
    {
        string[] p = line.Split('|');
        if (p.Length != 10)
            throw new InvalidDataException("Invalid P4 command-log row: " + line);

        float x = BitConverter.UInt32BitsToSingle(uint.Parse(p[7], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        float y = BitConverter.UInt32BitsToSingle(uint.Parse(p[8], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        string text = Encoding.UTF8.GetString(Convert.FromBase64String(p[9]));
        return new AuthoritativeSoakCommandLogEntry
        {
            Tick = int.Parse(p[0], CultureInfo.InvariantCulture),
            PeerId = int.Parse(p[1], CultureInfo.InvariantCulture),
            Command = new AuthoritativePlayerCommand
            {
                Sequence = int.Parse(p[2], CultureInfo.InvariantCulture),
                EmpireId = int.Parse(p[3], CultureInfo.InvariantCulture),
                Kind = (AuthoritativePlayerCommandKind)byte.Parse(p[4], CultureInfo.InvariantCulture),
                SubjectId = int.Parse(p[5], CultureInfo.InvariantCulture),
                TargetId = int.Parse(p[6], CultureInfo.InvariantCulture),
                Position = new Vector2(x, y),
                Text = text,
            }
        };
    }
}

public static class AuthoritativeSoakHarness
{
    static readonly string[] KnownGapDescriptorIds =
    {
        "FP.FleetPatrol",
        "BP.Blueprint",
        "G.Refit",
        "G.FleetRequisition",
        "G.DeepSpaceMovePosition",
        "F.Signatures",
        "S.PolicyFields",
        "D.DescriptiveFields",
    };

    static readonly HashSet<string> KnownGapDescriptors = new(KnownGapDescriptorIds, StringComparer.Ordinal);

    static readonly CombatState[] CombatStates =
    {
        CombatState.Artillery,
        CombatState.BroadsideLeft,
        CombatState.BroadsideRight,
        CombatState.OrbitLeft,
        CombatState.OrbitRight,
        CombatState.AttackRuns,
        CombatState.HoldPosition,
        CombatState.Evade,
        CombatState.ShortRange,
        CombatState.GuardMode,
    };

    static readonly Planet.ColonyType[] ColonyTypes =
        Enum.GetValues<Planet.ColonyType>()
            .Where(t => t != Planet.ColonyType.TradeHub)
            .ToArray();

    public static AuthoritativeSoakHarnessConfig SmokeConfigFromEnvironment()
        => new()
        {
            Seeds = ParseSeeds("SD_P4_SMOKE_SEEDS", new[] { 0x50440001UL }),
            Ticks = ReadInt("SD_P4_SMOKE_TICKS", 500),
            Clients = ReadInt("SD_P4_SMOKE_CLIENTS", 2),
            Hazards = ReadHazards("SD_P4_SMOKE_HAZARDS", AuthoritativeSoakHazards.All),
            ArtifactDirectory = ReadArtifactDirectory(),
        };

    public static AuthoritativeSoakHarnessConfig NightlyConfigFromEnvironment()
        => new()
        {
            Seeds = ParseSeeds("SD_P4_SOAK_SEEDS", new[] { 0x50440001UL, 0x50440002UL }),
            Ticks = ReadInt("SD_P4_SOAK_TICKS", 250),
            Clients = ReadInt("SD_P4_SOAK_CLIENTS", 2),
            Hazards = ReadHazards("SD_P4_SOAK_HAZARDS", AuthoritativeSoakHazards.All),
            ArtifactDirectory = ReadArtifactDirectory(),
        };

    public static AuthoritativeSoakMatrixResult RunMatrix(AuthoritativeSoakHarnessConfig config)
    {
        Normalize(config);
        var matrix = new AuthoritativeSoakMatrixResult();
        foreach (ulong seed in config.Seeds)
        {
            AuthoritativeSoakRunResult result = RunSeed(config, seed);
            matrix.Runs.Add(result);
            if (result.Outcome == AuthoritativeSoakOutcome.NewDivergence)
                break;
        }
        return matrix;
    }

    public static AuthoritativeSoakRunResult ExerciseReplayEntryPoint(AuthoritativeSoakHarnessConfig config)
    {
        string replayPath = Environment.GetEnvironmentVariable("SD_P4_REPLAY_PATH") ?? "";
        if (replayPath.NotEmpty())
            return RunReplay(ReadReplaySpec(replayPath));

        var captureConfig = config.Clone();
        captureConfig.Seeds = new[] { ParseSeed(Environment.GetEnvironmentVariable("SD_P4_REPLAY_SEED"), 0x5044BEEFUL) };
        captureConfig.Ticks = ReadInt("SD_P4_REPLAY_TICKS", 32);
        captureConfig.Clients = ReadInt("SD_P4_REPLAY_CLIENTS", 2);
        captureConfig.Hazards = ReadHazards("SD_P4_REPLAY_HAZARDS", AuthoritativeSoakHazards.FocusUnfocus);

        AuthoritativeSoakRunResult captured = RunSeed(captureConfig, captureConfig.Seeds[0]);
        var spec = new AuthoritativeSoakReplaySpec
        {
            Seed = captured.Seed,
            Clients = captured.Clients,
            Ticks = captured.CommandLog.Count,
            Hazards = captured.Hazards,
        };
        spec.Commands.AddRange(captured.CommandLog);
        return RunReplay(spec);
    }

    public static AuthoritativeSoakRunResult RunReplay(AuthoritativeSoakReplaySpec spec)
    {
        var config = new AuthoritativeSoakHarnessConfig
        {
            Seeds = new[] { spec.Seed },
            Clients = spec.Clients,
            Ticks = spec.Ticks,
            Hazards = spec.Hazards & ~AuthoritativeSoakHazards.CameraIndependence,
            ArtifactDirectory = ReadArtifactDirectory(),
        };
        Normalize(config);
        return RunOnce(config, spec.Seed, cameraVariant: 0, replayCommands: spec.Commands);
    }

    public static AuthoritativeSoakReplaySpec ReadReplaySpec(string path)
    {
        var spec = new AuthoritativeSoakReplaySpec();
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                continue;
            if (line.StartsWith("seed=", StringComparison.Ordinal))
            {
                spec.Seed = ParseSeed(line.Substring("seed=".Length), 0);
                continue;
            }
            if (line.StartsWith("clients=", StringComparison.Ordinal))
            {
                spec.Clients = int.Parse(line.Substring("clients=".Length), CultureInfo.InvariantCulture);
                continue;
            }
            if (line.StartsWith("ticks=", StringComparison.Ordinal))
            {
                spec.Ticks = int.Parse(line.Substring("ticks=".Length), CultureInfo.InvariantCulture);
                continue;
            }
            if (line.StartsWith("hazards=", StringComparison.Ordinal))
            {
                spec.Hazards = ParseHazards(line.Substring("hazards=".Length), AuthoritativeSoakHazards.None);
                continue;
            }
            spec.Commands.Add(AuthoritativeSoakCommandLogEntry.Parse(line));
        }

        if (spec.Seed == 0)
            throw new InvalidDataException("Replay log is missing seed.");
        if (spec.Clients <= 0)
            spec.Clients = spec.Commands.Select(c => c.PeerId).Distinct().Count();
        if (spec.Ticks <= 0)
            spec.Ticks = spec.Commands.Count;
        return spec;
    }

    static AuthoritativeSoakRunResult RunSeed(AuthoritativeSoakHarnessConfig config, ulong seed)
    {
        AuthoritativeSoakRunResult primary = RunOnce(config, seed, cameraVariant: 0, replayCommands: null);
        if (primary.Outcome != AuthoritativeSoakOutcome.Completed
            || !config.Hazards.HasFlag(AuthoritativeSoakHazards.CameraIndependence))
        {
            return primary;
        }

        var replay = new List<AuthoritativeSoakCommandLogEntry>(primary.CommandLog);
        AuthoritativeSoakRunResult alternate = RunOnce(config, seed, cameraVariant: 1, replayCommands: replay);
        if (alternate.Outcome != AuthoritativeSoakOutcome.Completed)
            return alternate;

        int count = Math.Min(primary.HostDigests.Count, alternate.HostDigests.Count);
        for (int i = 0; i < count; ++i)
        {
            if (string.Equals(primary.HostDigests[i], alternate.HostDigests[i], StringComparison.Ordinal))
                continue;

            alternate.Outcome = AuthoritativeSoakOutcome.NewDivergence;
            alternate.DivergenceTick = i + 1;
            alternate.DivergencePeer = 0;
            alternate.DivergenceLabel = "Camera-independence host digest mismatch";
            alternate.FirstDiff = $"camera A host digest='{primary.HostDigests[i]}' camera B host digest='{alternate.HostDigests[i]}'";
            alternate.ArtifactPath = PersistArtifact(config, alternate);
            return alternate;
        }

        return primary;
    }

    static AuthoritativeSoakRunResult RunOnce(AuthoritativeSoakHarnessConfig config, ulong seed, int cameraVariant,
        IReadOnlyList<AuthoritativeSoakCommandLogEntry> replayCommands)
    {
        Normalize(config);
        var result = new AuthoritativeSoakRunResult
        {
            Outcome = AuthoritativeSoakOutcome.Completed,
            Seed = seed,
            TicksRequested = replayCommands?.Count ?? config.Ticks,
            Clients = config.Clients,
            Hazards = config.Hazards,
        };

        using Authoritative4XLobbyStartResult started = StartSession(seed, config.Clients);
        ConfigureUniverses(started, seed, cameraVariant);
        var context = new SoakRunContext(config, seed, started);

        AuthoritativeSoakRunResult initial = CheckInitialSync(config, result, started);
        if (initial.Outcome != AuthoritativeSoakOutcome.Completed)
            return initial;

        int ticksToRun = replayCommands?.Count ?? config.Ticks;
        for (int tick = 1; tick <= ticksToRun; ++tick)
        {
            ApplyPreCommandHazards(context, tick);

            AuthoritativeSoakCommandLogEntry entry = replayCommands != null
                ? CloneReplayEntry(replayCommands[tick - 1], tick)
                : GenerateCommand(context, tick);

            result.CommandLog.Add(entry);
            try
            {
                started.Session.SubmitFromClient(entry.PeerId, entry.Command);
            }
            catch (Authoritative4XSyncMismatchException ex)
            {
                return BuildDivergenceResult(config, result, started, entry, ex);
            }

            result.HostDigests.Add(HostDigest(started.Session.LastAuthoritySnapshot));
            AuthoritativeSoakRunResult hazard = ApplyPostCommandHazards(context, result, tick);
            if (hazard.Outcome != AuthoritativeSoakOutcome.Completed)
                return hazard;
        }

        if (config.PersistSuccessfulReplay)
            result.ArtifactPath = PersistCommandLog(config, result, suffix: "completed");
        return result;
    }

    static Authoritative4XLobbyStartResult StartSession(ulong seed, int clientCount)
    {
        int[] peers = Enumerable.Range(2, clientCount).ToArray();
        IEmpireData[] races = ResourceManager.MajorRaces
            .Where(r => !r.IsFactionOrMinorRace)
            .OrderBy(RacePreference, StringComparer.Ordinal)
            .Take(clientCount)
            .ToArray();
        if (races.Length < clientCount)
            throw new InvalidOperationException($"P4 soak needs {clientCount} playable major races.");

        var settings = new Authoritative4XGameSettings
        {
            GenerationSeed = unchecked((int)(seed & 0x7FFFFFFF)),
            GalaxySize = GalSize.Tiny,
            StarsCount = RaceDesignScreen.StarsAbundance.Rare,
            Mode = RaceDesignScreen.GameMode.SmallClusters,
            Difficulty = GameDifficulty.Normal,
            NumOpponents = clientCount - 1,
            Pace = 1f,
            TurnTimer = 5,
            ExtraPlanets = 1,
            CustomMineralDecay = 1.0f,
            VolcanicActivity = 0.5f,
            StartingPlanetRichnessBonus = 0.75f,
            GameSpeed = 1f,
            StartPaused = false,
            AIUsesPlayerDesigns = false,
            DisablePirates = true,
            DisableResearchStations = true,
            DisableMiningOps = true,
        };

        var lobby = new Authoritative4XLobby(hostPlayerPeerId: peers[0], hostName: "Host");
        for (int i = 1; i < peers.Length; ++i)
            lobby.Join(peers[i], "P4 Client " + i.ToString(CultureInfo.InvariantCulture));

        Assert.IsTrue(lobby.SetSettings(peers[0], settings).Valid);
        for (int i = 0; i < peers.Length; ++i)
        {
            Assert.IsTrue(lobby.SetPlayerSelection(peers[i], RacePreference(races[i]), Array.Empty<string>()).Valid);
            Assert.IsTrue(lobby.SetReady(peers[i], true).Valid);
        }
        Assert.IsTrue(lobby.CanStart().Valid, lobby.CanStart().Reason);
        return lobby.StartInProcess();
    }

    static void ConfigureUniverses(Authoritative4XLobbyStartResult started, ulong seed, int cameraVariant)
    {
        UniverseScreen[] universes = started.Clients.Select(c => c.Universe)
            .Prepend(started.AuthorityUniverse)
            .ToArray();
        foreach (UniverseScreen universe in universes)
        {
            universe.UState.Events.Disabled = true;
            universe.UState.NoEliminationVictory = true;
            universe.UState.Objects.EnableParallelUpdate = false;
            universe.UState.EnableDeterministicRng(seed);
        }

        ApplyCameraVariant(started.AuthorityUniverse, cameraVariant, seed);
    }

    static AuthoritativeSoakRunResult CheckInitialSync(AuthoritativeSoakHarnessConfig config,
        AuthoritativeSoakRunResult result, Authoritative4XLobbyStartResult started)
    {
        AuthoritativeStateSnapshot authority = AuthoritativeStateSnapshot.Capture(started.AuthorityUniverse, 0);
        foreach (Authoritative4XClientSpec client in started.Clients)
        {
            AuthoritativeStateSnapshot replica = AuthoritativeStateSnapshot.Capture(client.Universe, 0);
            if (string.Equals(authority.SyncDigest, replica.SyncDigest, StringComparison.Ordinal)
                && string.Equals(authority.TransformDigest, replica.TransformDigest, StringComparison.Ordinal))
            {
                continue;
            }

            var entry = new AuthoritativeSoakCommandLogEntry
            {
                Tick = 0,
                PeerId = client.PeerId,
                Command = AuthoritativePlayerCommand.NoOp(0, client.EmpireId),
            };
            return BuildDivergenceResult(config, result, started, entry, authority, replica,
                "Initial host/client snapshot mismatch", client.PeerId);
        }
        return result;
    }

    static void ApplyPreCommandHazards(SoakRunContext context, int tick)
    {
        if (!context.Config.Hazards.HasFlag(AuthoritativeSoakHazards.FocusUnfocus))
            return;

        if (tick % 97 == 17)
            ApplySessionControl(context.Started, paused: true, gameSpeed: 0.25f);
        else if (tick % 97 == 19)
            ApplySessionControl(context.Started, paused: false, gameSpeed: 1f);
    }

    static AuthoritativeSoakRunResult ApplyPostCommandHazards(SoakRunContext context,
        AuthoritativeSoakRunResult result, int tick)
    {
        if (!context.Config.Hazards.HasFlag(AuthoritativeSoakHazards.ForcedResync)
            || tick != Math.Max(1, context.Config.Ticks / 2)
            || context.Started.Session.LastAuthoritySnapshot == null)
        {
            return result;
        }

        int peerIndex = (int)((context.Seed + (ulong)tick) % (ulong)context.PeerIds.Length);
        int peer = context.PeerIds[peerIndex];
        Authoritative4XClientSpec client = context.Started.Clients.First(c => c.PeerId == peer);
        AuthoritativeStateSnapshot authority = context.Started.Session.LastAuthoritySnapshot;
        authority.ApplyEmpireRuntimePayload(client.Universe.UState);
        AuthoritativeStateSnapshot replica = AuthoritativeStateSnapshot.Capture(client.Universe, authority.Tick);
        if (string.Equals(authority.SyncDigest, replica.SyncDigest, StringComparison.Ordinal)
            && string.Equals(authority.TransformDigest, replica.TransformDigest, StringComparison.Ordinal))
        {
            return result;
        }

        var entry = result.CommandLog.LastOrDefault()
                    ?? new AuthoritativeSoakCommandLogEntry
                    {
                        Tick = tick,
                        PeerId = peer,
                        Command = AuthoritativePlayerCommand.NoOp(0, client.EmpireId),
                    };
        return BuildDivergenceResult(context.Config, result, context.Started, entry, authority, replica,
            "Forced resync failed to converge", peer);
    }

    static void ApplySessionControl(Authoritative4XLobbyStartResult started, bool paused, float gameSpeed)
    {
#if DEBUG
        started.Session.ApplySessionControlForTest(paused, gameSpeed);
#endif
        foreach (Authoritative4XClientSpec client in started.Clients)
        {
            client.Universe.UState.Paused = paused;
            client.Universe.UState.GameSpeed = gameSpeed;
        }
    }

    static AuthoritativeSoakCommandLogEntry GenerateCommand(SoakRunContext context, int tick)
    {
        for (int attempt = 0; attempt < 10; ++attempt)
        {
            int peer = context.PeerIds[context.Rng.NextInt(context.PeerIds.Length)];
            int bucket = context.Rng.NextWeighted(10, 18, 12, 8, 20, 12, 8, 6);
            AuthoritativePlayerCommand command = bucket switch
            {
                0 => TryNoOp(context, peer),
                1 => TryColonyCommand(context, peer),
                2 => TryResearchCommand(context, peer),
                3 => TryDiplomacyCommand(context, peer),
                4 => TryShipCommand(context, peer),
                5 => TryFleetCommand(context, peer),
                6 => TryAutomationOrDesignCommand(context, peer),
                7 => TryGroundCommand(context, peer),
                _ => null,
            };
            if (command != null)
                return new AuthoritativeSoakCommandLogEntry { Tick = tick, PeerId = peer, Command = command };
        }

        int fallbackPeer = context.PeerIds[tick % context.PeerIds.Length];
        return new AuthoritativeSoakCommandLogEntry
        {
            Tick = tick,
            PeerId = fallbackPeer,
            Command = TryNoOp(context, fallbackPeer),
        };
    }

    static AuthoritativePlayerCommand TryNoOp(SoakRunContext context, int peer)
        => AuthoritativePlayerCommand.NoOp(context.NextSequence(peer), context.EmpireIdForPeer(peer));

    static AuthoritativePlayerCommand TryColonyCommand(SoakRunContext context, int peer)
    {
        Empire empire = context.ClientEmpire(peer);
        Planet planet = Pick(context.Rng, empire.GetPlanets().OrderBy(p => p.Id).ToArray());
        if (planet == null)
            return null;

        int choice = context.Rng.NextInt(13);
        switch (choice)
        {
            case 0:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitSetColonyType(planet, Pick(context.Rng, ColonyTypes)));
            case 1:
            {
                float food = context.Rng.NextFloat01();
                float production = context.Rng.NextFloat01();
                float research = context.Rng.NextFloat01();
                float total = Math.Max(0.001f, food + production + research);
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitSetColonyLabor(planet,
                        food / total, production / total, research / total,
                        context.Rng.NextBool(), context.Rng.NextBool(), context.Rng.NextBool()));
            }
            case 2:
            {
                Building building = PickBuildableBuilding(planet);
                if (building == null)
                    return null;
                PlanetGridSquare tile = PickBuildTile(context.Rng, planet, building);
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitQueueBuilding(planet, building.Name, tile));
            }
            case 3:
            {
                Troop troop = PickBuildableTroop(empire);
                return troop == null
                    ? null
                    : CaptureSingle(context, peer,
                        () => Authoritative4XClientContext.TrySubmitQueueTroop(planet, troop, repeat: 1));
            }
            case 4:
            {
                IShipDesign ship = PickBuildableShip(context.Rng, empire, d => !d.IsPlatformOrStation && !d.IsShipyard);
                return ship == null
                    ? null
                    : CaptureSingle(context, peer,
                        () => Authoritative4XClientContext.TrySubmitQueueShip(planet, ship, repeat: 1));
            }
            case 5:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitSetPlanetManualTradeSlots(planet,
                        context.Rng.NextInt(0, 3), context.Rng.NextInt(0, 3), context.Rng.NextInt(0, 3),
                        context.Rng.NextInt(0, 3), context.Rng.NextInt(0, 3), context.Rng.NextInt(0, 3)));
            case 6:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitSetPlanetDefenseTargets(planet,
                        context.Rng.NextInt(0, 6), context.Rng.NextInt(0, 4),
                        context.Rng.NextInt(0, 2), context.Rng.NextInt(0, 3)));
            case 7:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitSetPlanetManualBudget(planet,
                        Pick(context.Rng, Enum.GetValues<AuthoritativePlanetBudgetKind>()),
                        context.Rng.NextInt(0, 20)));
            case 8:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitSetPlanetGoodsState(planet,
                        Pick(context.Rng, Enum.GetValues<AuthoritativePlanetGoodsKind>()),
                        Pick(context.Rng, Enum.GetValues<Planet.GoodState>())));
            case 9:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitSetPlanetPrioritizedPort(planet, context.Rng.NextBool()));
            case 10:
            {
                QueueItem item = Pick(context.Rng, planet.Construction.GetConstructionQueueSnapshot());
                return item == null
                    ? null
                    : CaptureSingle(context, peer,
                        () => Authoritative4XClientContext.TrySubmitToggleConstructionRush(planet, item));
            }
            case 11:
            {
                QueueItem[] queue = planet.Construction.GetConstructionQueueSnapshot();
                if (queue.Length < 2)
                    return null;
                QueueItem item = Pick(context.Rng, queue);
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitReorderConstructionQueueItem(planet,
                        item, context.Rng.NextInt(queue.Length)));
            }
            default:
            {
                BlueprintsTemplate template = BuildBlueprint(context, planet);
                return template == null
                    ? null
                    : CaptureSingle(context, peer,
                        () => Authoritative4XClientContext.TrySubmitApplyColonyBlueprints(planet, template));
            }
        }
    }

    static AuthoritativePlayerCommand TryResearchCommand(SoakRunContext context, int peer)
    {
        Empire empire = context.ClientEmpire(peer);
        string[] candidates = empire.TechEntries
            .Where(t => t.Discovered && t.CanBeResearched)
            .OrderBy(t => t.UID, StringComparer.Ordinal)
            .Select(t => t.UID)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
            return null;

        int choice = context.Rng.NextInt(4);
        if (choice == 0)
            return CaptureSingle(context, peer,
                () => Authoritative4XClientContext.TrySubmitSetResearchTopic(empire, Pick(context.Rng, candidates)));
        if (choice == 1 || empire.data.ResearchQueue.Count == 0)
            return CaptureSingle(context, peer,
                () => Authoritative4XClientContext.TrySubmitQueueResearch(empire, Pick(context.Rng, candidates)));

        string queued = Pick(context.Rng, empire.data.ResearchQueue.ToArray());
        if (choice == 2)
            return CaptureSingle(context, peer,
                () => Authoritative4XClientContext.TrySubmitMoveResearchQueueItem(empire, queued,
                    Pick(context.Rng, Enum.GetValues<AuthoritativeResearchQueueMove>())));

        return CaptureSingle(context, peer,
            () => Authoritative4XClientContext.TrySubmitRemoveResearchQueueItem(empire, queued));
    }

    static AuthoritativePlayerCommand TryDiplomacyCommand(SoakRunContext context, int peer)
    {
        AuthoritativeDiplomacyPopup popup = context.Started.Session.PopupsFor(peer)
            .LastOrDefault(p => p.RequiresResponse);
        if (popup != null && context.Rng.NextBool())
        {
            return CaptureSingle(context, peer,
                () => Authoritative4XClientContext.TrySubmitDiplomacyResponse(popup.ProposalId,
                    Pick(context.Rng, Enum.GetValues<AuthoritativeDiplomacyResponseKind>())));
        }

        Empire empire = context.ClientEmpire(peer);
        Empire[] targets = context.Started.Clients
            .Where(c => c.PeerId != peer)
            .Select(c => context.ClientUniverse(peer).UState.GetEmpireById(c.EmpireId))
            .Where(e => e != null && e.Id != empire.Id)
            .OrderBy(e => e.Id)
            .ToArray();
        Empire target = Pick(context.Rng, targets);
        if (target == null)
            return null;

        AuthoritativeDiplomacyProposalType type = Pick(context.Rng,
            AuthoritativeDiplomacyProposalType.DeclareWar,
            AuthoritativeDiplomacyProposalType.Peace,
            AuthoritativeDiplomacyProposalType.TradeDeal,
            AuthoritativeDiplomacyProposalType.NonAggression);
        return CaptureSingle(context, peer,
            () => Authoritative4XClientContext.TrySubmitDiplomacyProposal(target, type));
    }

    static AuthoritativePlayerCommand TryShipCommand(SoakRunContext context, int peer)
    {
        Empire empire = context.ClientEmpire(peer);
        Ship[] ships = empire.OwnedShips
            .Where(s => s?.Active == true)
            .OrderBy(s => s.Id)
            .ToArray();
        Ship ship = Pick(context.Rng, ships);
        if (ship == null)
            return null;

        int choice = context.Rng.NextInt(11);
        switch (choice)
        {
            case 0:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitMoveShip(ship,
                        ship.Position + RandomOffset(context.Rng, 8_000f, 42_000f),
                        Pick(context.Rng, MoveOrder.Regular, MoveOrder.Aggressive, MoveOrder.StandGround)));
            case 1:
            {
                Ship target = PickHostileShip(context, peer, ship);
                return target == null
                    ? null
                    : CaptureSingle(context, peer,
                        () => Authoritative4XClientContext.TrySubmitAttackShip(ship, target, context.Rng.NextBool()));
            }
            case 2:
            {
                Ship target = PickAnyOtherShip(context, peer, ship);
                if (target == null)
                    return null;
                AuthoritativeShipTargetOrderType type = target.Loyalty == ship.Loyalty
                    ? AuthoritativeShipTargetOrderType.Escort
                    : AuthoritativeShipTargetOrderType.Attack;
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitShipTargetOrder(ship, target, type,
                        queue: type == AuthoritativeShipTargetOrderType.Attack && context.Rng.NextBool()));
            }
            case 3:
            {
                Planet planet = Pick(context.Rng, context.ClientUniverse(peer).UState.Planets.OrderBy(p => p.Id).ToArray());
                return planet == null
                    ? null
                    : CaptureSingle(context, peer,
                        () => Authoritative4XClientContext.TrySubmitShipPlanetOrder(ship, planet,
                            AuthoritativeShipPlanetOrderType.Orbit, clearOrders: context.Rng.NextBool(),
                            Pick(context.Rng, MoveOrder.Regular, MoveOrder.Aggressive, MoveOrder.StandGround)));
            }
            case 4:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitSetShipCombatStance(ship, Pick(context.Rng, CombatStates)));
            case 5:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitShipSpecialOrder(ship,
                        AuthoritativeShipSpecialOrderType.ClearOrders));
            case 6:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitRenameShip(ship,
                        "P4S" + context.NextNameSuffix(peer)));
            case 7:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitSetShipTradePolicy(ship,
                        Pick(context.Rng, Enum.GetValues<AuthoritativeShipTradePolicyKind>()),
                        context.Rng.NextBool()));
            case 8:
            {
                Planet planet = Pick(context.Rng, empire.GetPlanets().OrderBy(p => p.Id).ToArray());
                return planet == null
                    ? null
                    : CaptureSingle(context, peer,
                        () => Authoritative4XClientContext.TrySubmitSetShipTradeRoute(ship, planet, context.Rng.NextBool()));
            }
            case 9:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitSetShipAreaOfOperation(ship,
                        AuthoritativeShipAreaOfOperationAction.AddRectangle,
                        new Rectangle((int)ship.Position.X - 5000, (int)ship.Position.Y - 5000,
                            10_000 + context.Rng.NextInt(0, 30_000), 10_000 + context.Rng.NextInt(0, 30_000))));
            default:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitSetShipCarrierPolicy(ship,
                        Pick(context.Rng, Enum.GetValues<AuthoritativeShipCarrierPolicyKind>()),
                        context.Rng.NextBool()));
        }
    }

    static AuthoritativePlayerCommand TryFleetCommand(SoakRunContext context, int peer)
    {
        Empire empire = context.ClientEmpire(peer);
        int fleetKey = context.Rng.NextInt(Empire.FirstFleetKey, Empire.LastFleetKey + 1);
        Fleet fleet = empire.GetFleetOrNull(fleetKey);
        int choice = context.Rng.NextInt(10);

        if (fleet == null || choice == 0)
        {
            Ship[] ships = empire.OwnedShips
                .Where(s => s?.Active == true && s.CanBeAddedToFleets())
                .OrderBy(s => s.Id)
                .Take(1 + context.Rng.NextInt(0, 3))
                .ToArray();
            return CaptureSingle(context, peer,
                () => Authoritative4XClientContext.TrySubmitSetFleetAssignment(empire, fleetKey,
                    ships.Length == 0 ? AuthoritativeFleetAssignmentMode.Clear : AuthoritativeFleetAssignmentMode.Replace,
                    ships));
        }

        switch (choice)
        {
            case 1:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitMoveFleet(fleet,
                        fleet.AveragePosition() + RandomOffset(context.Rng, 12_000f, 60_000f),
                        RandomDirection(context.Rng), Pick(context.Rng, MoveOrder.Regular, MoveOrder.Aggressive)));
            case 2:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitRenameFleet(fleet,
                        "P4 Fleet " + context.NextNameSuffix(peer)));
            case 3:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitSetFleetIcon(fleet, context.Rng.NextInt(1, 31)));
            case 4:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitAutoArrangeFleet(fleet));
            case 5:
            {
                FleetDataNode[] nodes = fleet.DataNodes
                    .Select(n => new FleetDataNode(n)
                    {
                        Ship = n.Ship,
                        RelativeFleetOffset = n.RelativeFleetOffset + RandomOffset(context.Rng, 200f, 1600f),
                        CombatState = Pick(context.Rng, CombatStates),
                    })
                    .ToArray();
                return nodes.Length == 0
                    ? null
                    : CaptureSingle(context, peer,
                        () => Authoritative4XClientContext.TrySubmitSetFleetLayout(fleet, nodes));
            }
            case 6:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitCreateFleetPatrol(fleet,
                        BuildWaypoints(fleet.AveragePosition(), context.Rng)));
            case 7:
                return CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitClearFleetPatrol(fleet));
            case 8:
            {
                FleetPatrol patrol = Pick(context.Rng, empire.FleetPatrols.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray());
                return patrol == null
                    ? null
                    : CaptureSingle(context, peer,
                        () => Authoritative4XClientContext.TrySubmitLoadFleetPatrol(fleet, patrol));
            }
            default:
            {
                FleetPatrol patrol = Pick(context.Rng, empire.FleetPatrols.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray());
                return patrol == null
                    ? null
                    : CaptureSingle(context, peer,
                        () => Authoritative4XClientContext.TrySubmitRenameFleetPatrol(empire, patrol,
                            "P4 Patrol " + context.NextNameSuffix(peer)));
            }
        }
    }

    static AuthoritativePlayerCommand TryAutomationOrDesignCommand(SoakRunContext context, int peer)
    {
        Empire empire = context.ClientEmpire(peer);
        int choice = context.Rng.NextInt(5);
        if (choice == 0)
        {
            return CaptureSingle(context, peer,
                () => Authoritative4XClientContext.TrySubmitSetEmpireBudget(empire,
                    context.Rng.NextInt(5, 45) / 100f,
                    context.Rng.NextInt(10, 75) / 100f,
                    context.Rng.NextBool()));
        }
        if (choice == 1)
        {
            AuthoritativeEmpireAutomationFlags flags = PickAutomationFlags(context.Rng);
            return CaptureSingle(context, peer,
                () => Authoritative4XClientContext.TrySubmitEmpireAutomation(empire, flags,
                    PickOptionalDesignName(empire, d => d.IsFreighter),
                    PickOptionalDesignName(empire, d => d.IsColonyShip),
                    PickOptionalDesignName(empire,
                        d => d.Role == RoleName.scout || d.Role == RoleName.fighter || d.ShipCategory == ShipCategory.Recon),
                    PickOptionalDesignName(empire, d => d.IsConstructor),
                    PickOptionalDesignName(empire, d => d.IsResearchStation),
                    PickOptionalDesignName(empire, d => d.IsMiningStation)));
        }
        if (choice == 2)
        {
            var flags = AuthoritativeUniversePreferenceFlags.None;
            if (context.Rng.NextBool()) flags |= AuthoritativeUniversePreferenceFlags.AllowPlayerInterTrade;
            if (context.Rng.NextBool()) flags |= AuthoritativeUniversePreferenceFlags.PrioritizeProjectors;
            return CaptureSingle(context, peer,
                () => Authoritative4XClientContext.TrySubmitUniversePreferences(empire, flags));
        }
        if (choice == 3)
        {
            ShipDesign design = BuildLegalPlayerDesign(empire, "P4D" + context.NextNameSuffix(peer));
            return design == null
                ? null
                : CaptureSingle(context, peer,
                    () => Authoritative4XClientContext.TrySubmitDesignShip(empire, design));
        }

        Planet planet = Pick(context.Rng, empire.GetPlanets().OrderBy(p => p.Id).ToArray());
        IShipDesign station = PickBuildableShip(context.Rng, empire, d => d.IsPlatformOrStation && !d.IsShipyard);
        return planet == null || station == null
            ? null
            : CaptureSingle(context, peer,
                () => Authoritative4XClientContext.TrySubmitQueuePlanetOrbitalBuild(planet, station));
    }

    static AuthoritativePlayerCommand TryGroundCommand(SoakRunContext context, int peer)
    {
        Empire empire = context.ClientEmpire(peer);
        Planet planet = Pick(context.Rng, empire.GetPlanets().OrderBy(p => p.Id).ToArray());
        if (planet == null)
            return null;

        int choice = context.Rng.NextInt(3);
        if (choice == 0)
            return CaptureSingle(context, peer,
                () => Authoritative4XClientContext.TrySubmitLaunchGroundTroops(empire, planet));
        if (choice == 1)
            return CaptureSingle(context, peer,
                () => Authoritative4XClientContext.TrySubmitRecallGroundTroops(empire, planet));

        PlanetGridSquare source = planet.TilesList
            .OrderBy(t => t.X).ThenBy(t => t.Y)
            .FirstOrDefault(t => t.TroopsHere.Any(tr => tr?.Loyalty == empire && tr.CanMove));
        Troop troop = source?.TroopsHere.FirstOrDefault(t => t?.Loyalty == empire && t.CanMove);
        PlanetGridSquare target = planet.TilesList
            .OrderBy(t => t.X).ThenBy(t => t.Y)
            .FirstOrDefault(t => t != source && t.IsTileFree(empire));
        return troop == null || target == null
            ? null
            : CaptureSingle(context, peer,
                () => Authoritative4XClientContext.TrySubmitMoveGroundTroop(empire, planet, source, troop, target));
    }

    static AuthoritativePlayerCommand CaptureSingle(SoakRunContext context, int peer,
        Func<bool> submit)
    {
        return CaptureSingle(context, peer, () => submit()
            ? Authoritative4XUiCommandResult.Submitted
            : Authoritative4XUiCommandResult.Blocked);
    }

    static AuthoritativePlayerCommand CaptureSingle(SoakRunContext context, int peer,
        Func<Authoritative4XUiCommandResult> submit)
    {
        var commands = new List<AuthoritativePlayerCommand>(1);
        int firstSequence = context.PeekSequence(peer);
        using (Authoritative4XClientContext.Begin(peer, context.EmpireIdForPeer(peer), commands.Add, firstSequence))
        {
            if (submit() != Authoritative4XUiCommandResult.Submitted || commands.Count == 0)
                return null;
        }

        AuthoritativePlayerCommand command = commands[0];
        context.MarkSequenceUsed(peer, commands.Max(c => c.Sequence));
        return command;
    }

    static AuthoritativeSoakRunResult BuildDivergenceResult(AuthoritativeSoakHarnessConfig config,
        AuthoritativeSoakRunResult result, Authoritative4XLobbyStartResult started,
        AuthoritativeSoakCommandLogEntry entry, Authoritative4XSyncMismatchException exception)
    {
        int peer = FindDivergedPeer(started, exception.ClientSnapshot, entry.PeerId);
        return BuildDivergenceResult(config, result, started, entry, exception.AuthoritySnapshot,
            exception.ClientSnapshot, exception.Message, peer);
    }

    static AuthoritativeSoakRunResult BuildDivergenceResult(AuthoritativeSoakHarnessConfig config,
        AuthoritativeSoakRunResult result, Authoritative4XLobbyStartResult started,
        AuthoritativeSoakCommandLogEntry entry, AuthoritativeStateSnapshot authority,
        AuthoritativeStateSnapshot replica, string label, int peer)
    {
        PayloadDiff diff = FirstPayloadDiff(authority?.Payload, replica?.Payload);
        KnownGapMatch known = ClassifyKnownGap(diff);
        result.Outcome = known.IsKnown
            ? AuthoritativeSoakOutcome.KnownGapDetected
            : AuthoritativeSoakOutcome.NewDivergence;
        result.DivergenceTick = entry.Tick;
        result.DivergencePeer = peer;
        result.DivergenceLabel = label ?? "";
        result.KnownGapDescriptor = known.DescriptorId;
        result.FirstDiff = diff.ToString();
        result.LaneDiff = LaneDiff(started, peer);
        result.ArtifactPath = PersistArtifact(config, result);
        return result;
    }

    static int FindDivergedPeer(Authoritative4XLobbyStartResult started,
        AuthoritativeStateSnapshot exceptionSnapshot, int fallbackPeer)
    {
        if (exceptionSnapshot != null)
        {
            foreach (Authoritative4XClientSpec client in started.Clients)
            {
                AuthoritativeStateSnapshot snapshot = AuthoritativeStateSnapshot.Capture(client.Universe, exceptionSnapshot.Tick);
                if (string.Equals(snapshot.SyncDigest, exceptionSnapshot.SyncDigest, StringComparison.Ordinal)
                    && string.Equals(snapshot.TransformDigest, exceptionSnapshot.TransformDigest, StringComparison.Ordinal))
                {
                    return client.PeerId;
                }
            }
        }
        return fallbackPeer;
    }

    static string LaneDiff(Authoritative4XLobbyStartResult started, int peer)
    {
        Authoritative4XClientSpec client = started.Clients.FirstOrDefault(c => c.PeerId == peer);
        if (client.Universe == null)
            return "";

        DeterminismHashWriter authority = started.AuthorityUniverse.UState.ComputeDebugLaneHashes(
            DeterminismProfile.ReplayWinX64Float);
        DeterminismHashWriter replica = client.Universe.UState.ComputeDebugLaneHashes(
            DeterminismProfile.ReplayWinX64Float);
        var lanes = new List<string>();
        for (int i = 0; i < DeterminismHashWriter.LaneCount; ++i)
        {
            var lane = (DetLane)i;
            ulong a = authority.LaneHash(lane);
            ulong c = replica.LaneHash(lane);
            if (a != c)
                lanes.Add($"{lane}:0x{a:X16}->0x{c:X16}");
        }
        return lanes.Count == 0 ? "none" : string.Join(",", lanes);
    }

    static PayloadDiff FirstPayloadDiff(string authorityPayload, string clientPayload)
    {
        string[] authority = (authorityPayload ?? "").Split('\n');
        string[] client = (clientPayload ?? "").Split('\n');
        int count = Math.Max(authority.Length, client.Length);
        for (int i = 0; i < count; ++i)
        {
            string a = i < authority.Length ? authority[i].TrimEnd('\r') : "<missing>";
            string c = i < client.Length ? client[i].TrimEnd('\r') : "<missing>";
            if (!string.Equals(a, c, StringComparison.Ordinal))
                return new PayloadDiff(i + 1, a, c);
        }
        return new PayloadDiff(0, "payloads matched", "payloads matched");
    }

    static KnownGapMatch ClassifyKnownGap(PayloadDiff diff)
    {
        string line = diff.AuthorityLine != "<missing>" ? diff.AuthorityLine : diff.ClientLine;
        string prefix = AuthoritativeReplicationManifest.PrefixForLine(line);
        int field = diff.FirstDifferingFieldIndex;

        ReplicatedRowDescriptor descriptor = AuthoritativeReplicationManifest.DescriptorForLine(line);
        if (descriptor?.KnownGap == true && KnownGapDescriptors.Contains(descriptor.Id))
            return new KnownGapMatch(true, descriptor.Id);

        if (prefix == "FP")
            return new KnownGapMatch(true, "FP.FleetPatrol");
        if (prefix == "BP")
            return new KnownGapMatch(true, "BP.Blueprint");
        if (prefix == "F" && field >= 11)
            return new KnownGapMatch(true, "F.Signatures");
        if (prefix == "S" && (field == 2 || field >= 17))
            return new KnownGapMatch(true, "S.PolicyFields");
        if (prefix == "D" && field is >= 3 and <= 6)
            return new KnownGapMatch(true, "D.DescriptiveFields");
        if (prefix == "G")
        {
            string[] p = line.Split('|');
            string variant = p.Length > 2 ? p[2] : "";
            if (variant == "Refit")
                return new KnownGapMatch(true, "G.Refit");
            if (variant == "FleetRequisition")
                return new KnownGapMatch(true, "G.FleetRequisition");
            if (variant == "DeepSpace" && field is 12 or 13)
                return new KnownGapMatch(true, "G.DeepSpaceMovePosition");
        }

        return new KnownGapMatch(false, descriptor?.Id ?? prefix);
    }

    static string PersistArtifact(AuthoritativeSoakHarnessConfig config, AuthoritativeSoakRunResult result)
    {
        string logPath = PersistCommandLog(config, result,
            result.Outcome == AuthoritativeSoakOutcome.KnownGapDetected ? "known-gap" : "new-divergence");
        string summaryPath = Path.ChangeExtension(logPath, ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(summaryPath)!);
        File.WriteAllText(summaryPath,
            "StarDrive P4 soak result" + Environment.NewLine
            + "outcome=" + result.Outcome + Environment.NewLine
            + "seed=0x" + result.Seed.ToString("X", CultureInfo.InvariantCulture) + Environment.NewLine
            + "clients=" + result.Clients.ToString(CultureInfo.InvariantCulture) + Environment.NewLine
            + "ticksRequested=" + result.TicksRequested.ToString(CultureInfo.InvariantCulture) + Environment.NewLine
            + "divergenceTick=" + result.DivergenceTick.ToString(CultureInfo.InvariantCulture) + Environment.NewLine
            + "divergencePeer=" + result.DivergencePeer.ToString(CultureInfo.InvariantCulture) + Environment.NewLine
            + "label=" + result.DivergenceLabel + Environment.NewLine
            + "knownGapDescriptor=" + result.KnownGapDescriptor + Environment.NewLine
            + "firstDiff=" + result.FirstDiff + Environment.NewLine
            + "laneDiff=" + result.LaneDiff + Environment.NewLine
            + "commandLog=" + logPath + Environment.NewLine);
        return summaryPath;
    }

    static string PersistCommandLog(AuthoritativeSoakHarnessConfig config, AuthoritativeSoakRunResult result,
        string suffix)
    {
        string directory = config.ArtifactDirectory.NotEmpty()
            ? config.ArtifactDirectory
            : Path.Combine(Environment.CurrentDirectory, "TestResults", "DesyncP4");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory,
            $"p4_{suffix}_seed0x{result.Seed:X}_clients{result.Clients}_tick{Math.Max(0, result.DivergenceTick)}.p4cmdlog");
        using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
        writer.WriteLine("# StarDrive P4 command log v1");
        writer.WriteLine("seed=0x" + result.Seed.ToString("X", CultureInfo.InvariantCulture));
        writer.WriteLine("clients=" + result.Clients.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine("ticks=" + result.CommandLog.Count.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine("hazards=" + result.Hazards);
        foreach (AuthoritativeSoakCommandLogEntry entry in result.CommandLog)
            writer.WriteLine(entry.ToLogLine());
        return path;
    }

    static AuthoritativeSoakCommandLogEntry CloneReplayEntry(AuthoritativeSoakCommandLogEntry entry, int expectedTick)
    {
        if (entry.Tick != expectedTick)
            throw new InvalidDataException($"Replay command log tick mismatch: expected {expectedTick}, got {entry.Tick}.");
        return new AuthoritativeSoakCommandLogEntry
        {
            Tick = entry.Tick,
            PeerId = entry.PeerId,
            Command = new AuthoritativePlayerCommand
            {
                Sequence = entry.Command.Sequence,
                EmpireId = entry.Command.EmpireId,
                Kind = entry.Command.Kind,
                SubjectId = entry.Command.SubjectId,
                TargetId = entry.Command.TargetId,
                Position = entry.Command.Position,
                Text = entry.Command.Text ?? "",
            },
        };
    }

    static string HostDigest(AuthoritativeStateSnapshot snapshot)
        => snapshot == null
            ? "<none>"
            : $"0x{snapshot.HashLo:X16}:0x{snapshot.HashHi:X16}|{snapshot.SyncDigest}|{snapshot.TransformDigest}";

    static void ApplyCameraVariant(UniverseScreen universe, int variant, ulong seed)
    {
        Planet home = universe.Player?.GetPlanets().OrderBy(p => p.Id).FirstOrDefault();
        Vector2 anchor = home?.Position ?? Vector2.Zero;
        Vector3d camera = variant == 0
            ? new Vector3d(anchor.X, anchor.Y, 5_000)
            : new Vector3d(anchor.X + 111_111 + (int)(seed & 0xFF),
                anchor.Y - 77_777 - (int)((seed >> 8) & 0xFF), 750_000);
        universe.CamPos = camera;
        universe.CamDestination = camera;
    }

    static Building PickBuildableBuilding(Planet planet)
    {
        planet.RefreshBuildingsWeCanBuildHere();
        return planet.GetBuildingsCanBuild()
            .Where(b => planet.TilesList.Any(tile => tile.CanEnqueueBuildingHere(b)))
            .OrderBy(b => b.ActualCost(planet.Owner))
            .ThenBy(b => b.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    static PlanetGridSquare PickBuildTile(DeterministicSoakRng rng, Planet planet, Building building)
    {
        PlanetGridSquare[] tiles = planet.TilesList
            .Where(tile => tile.CanEnqueueBuildingHere(building))
            .OrderBy(t => t.X)
            .ThenBy(t => t.Y)
            .ToArray();
        return Pick(rng, tiles);
    }

    static Troop PickBuildableTroop(Empire empire)
        => ResourceManager.GetTroopTemplatesFor(empire)
            .OrderBy(t => t.ActualCost(empire))
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .FirstOrDefault();

    static IShipDesign PickBuildableShip(DeterministicSoakRng rng, Empire empire, Func<IShipDesign, bool> predicate)
    {
        IShipDesign[] designs = empire.ShipsWeCanBuildSnapshot
            .Where(s => s.IsShipGoodToBuild(empire) && predicate(s))
            .OrderBy(s => s.BaseCost)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToArray();
        return Pick(rng, designs);
    }

    static string PickOptionalDesignName(Empire empire, Func<IShipDesign, bool> predicate)
        => empire.ShipsWeCanBuildSnapshot
            .Where(s => s.IsShipGoodToBuild(empire) && predicate(s))
            .OrderBy(s => s.BaseCost)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => s.Name)
            .FirstOrDefault() ?? "";

    static BlueprintsTemplate BuildBlueprint(SoakRunContext context, Planet planet)
    {
        planet.RefreshBuildingsWeCanBuildHere();
        string[] buildings = planet.GetBuildingsCanBuild()
            .Where(b => b.IsSuitableForBlueprints)
            .OrderBy(b => b.ActualCost(planet.Owner))
            .ThenBy(b => b.Name, StringComparer.Ordinal)
            .Select(b => b.Name)
            .Take(1 + context.Rng.NextInt(0, 3))
            .ToArray();
        if (buildings.Length == 0)
            return null;
        return new BlueprintsTemplate("P4BP" + context.NextGlobalNameSuffix(), false, "",
            buildings.ToHashSet(StringComparer.Ordinal), planet.CType);
    }

    static ShipDesign BuildLegalPlayerDesign(Empire empire, string name)
    {
        ShipDesign source = empire.ShipsWeCanBuildSnapshot
            .OfType<ShipDesign>()
            .Where(d => !d.IsPlatformOrStation
                        && d.IsValidDesign
                        && d.NumDesignSlots > 0
                        && d.UniqueModuleUIDs.All(empire.IsModuleUnlocked))
            .OrderBy(d => d.BaseCost)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (source == null)
            return null;

        source.GetOrLoadDesignSlots();
        ShipDesign clone = source.GetClone(name);
        clone.IsPlayerDesign = true;
        clone.IsReadonlyDesign = false;
        return clone;
    }

    static Ship PickHostileShip(SoakRunContext context, int peer, Ship ship)
        => Pick(context.Rng, context.ClientUniverse(peer).UState.Ships
            .Where(s => s?.Active == true && s.Loyalty != null && s.Loyalty != ship.Loyalty)
            .OrderBy(s => s.Id)
            .ToArray());

    static Ship PickAnyOtherShip(SoakRunContext context, int peer, Ship ship)
        => Pick(context.Rng, context.ClientUniverse(peer).UState.Ships
            .Where(s => s?.Active == true && s != ship)
            .OrderBy(s => s.Id)
            .ToArray());

    static WayPoint[] BuildWaypoints(Vector2 origin, DeterministicSoakRng rng)
    {
        Vector2 a = origin + RandomOffset(rng, 6_000f, 18_000f);
        Vector2 b = origin + RandomOffset(rng, 18_000f, 34_000f);
        return new[]
        {
            new WayPoint(a, RandomDirection(rng)),
            new WayPoint(b, RandomDirection(rng)),
        };
    }

    static Vector2 RandomOffset(DeterministicSoakRng rng, float min, float max)
    {
        float angle = rng.NextFloat01() * MathF.PI * 2f;
        float distance = min + (max - min) * rng.NextFloat01();
        return new Vector2(MathF.Cos(angle) * distance, MathF.Sin(angle) * distance);
    }

    static Vector2 RandomDirection(DeterministicSoakRng rng)
    {
        float angle = rng.NextFloat01() * MathF.PI * 2f;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    static AuthoritativeEmpireAutomationFlags PickAutomationFlags(DeterministicSoakRng rng)
    {
        var flags = AuthoritativeEmpireAutomationFlags.None;
        if (rng.NextBool()) flags |= AuthoritativeEmpireAutomationFlags.AutoResearch;
        if (rng.NextBool()) flags |= AuthoritativeEmpireAutomationFlags.AutoTaxes;
        if (rng.NextBool()) flags |= AuthoritativeEmpireAutomationFlags.AutoExplore;
        if (rng.NextBool()) flags |= AuthoritativeEmpireAutomationFlags.AutoColonize;
        if (rng.NextBool()) flags |= AuthoritativeEmpireAutomationFlags.AutoFreighters;
        if (rng.NextBool()) flags |= AuthoritativeEmpireAutomationFlags.RushAllConstruction;
        return flags;
    }

    static T Pick<T>(DeterministicSoakRng rng, IReadOnlyList<T> values)
        => values == null || values.Count == 0 ? default : values[rng.NextInt(values.Count)];

    static T Pick<T>(DeterministicSoakRng rng, params T[] values)
        => Pick(rng, (IReadOnlyList<T>)values);

    static string RacePreference(IEmpireData race)
        => race.ArchetypeName.NotEmpty() ? race.ArchetypeName : race.Name;

    static void Normalize(AuthoritativeSoakHarnessConfig config)
    {
        config.Seeds = config.Seeds?.Where(s => s != 0).DefaultIfEmpty(0x50440001UL).ToArray()
                       ?? new[] { 0x50440001UL };
        config.Ticks = Math.Max(1, config.Ticks);
        config.Clients = Math.Clamp(config.Clients, 1, 8);
    }

    static int ReadInt(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;

    static ulong[] ParseSeeds(string name, ulong[] fallback)
    {
        string raw = Environment.GetEnvironmentVariable(name) ?? "";
        if (raw.IsEmpty())
            return fallback;
        return raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => ParseSeed(s, 0))
            .Where(s => s != 0)
            .ToArray();
    }

    static ulong ParseSeed(string raw, ulong fallback)
    {
        if (raw.IsEmpty())
            return fallback;
        raw = raw.Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(raw.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hex)
                ? hex
                : fallback;
        return ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong dec)
            ? dec
            : fallback;
    }

    static AuthoritativeSoakHazards ReadHazards(string name, AuthoritativeSoakHazards fallback)
        => ParseHazards(Environment.GetEnvironmentVariable(name), fallback);

    static AuthoritativeSoakHazards ParseHazards(string raw, AuthoritativeSoakHazards fallback)
    {
        if (raw.IsEmpty())
            return fallback;
        if (Enum.TryParse(raw, ignoreCase: true, out AuthoritativeSoakHazards parsed))
            return parsed;

        var hazards = AuthoritativeSoakHazards.None;
        foreach (string part in raw.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Enum.TryParse(part, ignoreCase: true, out AuthoritativeSoakHazards one))
                hazards |= one;
        }
        return hazards == AuthoritativeSoakHazards.None ? fallback : hazards;
    }

    static string ReadArtifactDirectory()
        => Environment.GetEnvironmentVariable("SD_P4_ARTIFACT_DIR") ?? "";

    readonly struct KnownGapMatch
    {
        public readonly bool IsKnown;
        public readonly string DescriptorId;

        public KnownGapMatch(bool isKnown, string descriptorId)
        {
            IsKnown = isKnown;
            DescriptorId = descriptorId ?? "";
        }
    }

    readonly struct PayloadDiff
    {
        public readonly int Line;
        public readonly string AuthorityLine;
        public readonly string ClientLine;

        public PayloadDiff(int line, string authorityLine, string clientLine)
        {
            Line = line;
            AuthorityLine = authorityLine ?? "";
            ClientLine = clientLine ?? "";
        }

        public int FirstDifferingFieldIndex
        {
            get
            {
                if (AuthorityLine == "<missing>" || ClientLine == "<missing>")
                    return -1;
                string[] a = AuthorityLine.Split('|');
                string[] c = ClientLine.Split('|');
                int count = Math.Max(a.Length, c.Length);
                for (int i = 0; i < count; ++i)
                {
                    string av = i < a.Length ? a[i] : "<missing>";
                    string cv = i < c.Length ? c[i] : "<missing>";
                    if (!string.Equals(av, cv, StringComparison.Ordinal))
                        return i;
                }
                return -1;
            }
        }

        public override string ToString()
            => Line <= 0
                ? "payloads matched"
                : $"line={Line} field={FirstDifferingFieldIndex} "
                  + $"{AuthoritativeReplicationManifest.DescribeDiff(AuthorityLine, ClientLine)} "
                  + $"authority='{AuthorityLine}' client='{ClientLine}'";
    }

    sealed class SoakRunContext
    {
        public readonly AuthoritativeSoakHarnessConfig Config;
        public readonly ulong Seed;
        public readonly Authoritative4XLobbyStartResult Started;
        public readonly DeterministicSoakRng Rng;
        public readonly int[] PeerIds;
        readonly Dictionary<int, int> NextSequenceByPeer = new();
        int GlobalNameSuffix;

        public SoakRunContext(AuthoritativeSoakHarnessConfig config, ulong seed,
            Authoritative4XLobbyStartResult started)
        {
            Config = config;
            Seed = seed;
            Started = started;
            Rng = new DeterministicSoakRng(seed ^ 0xD45E5A4B5044UL);
            PeerIds = started.Clients.Select(c => c.PeerId).OrderBy(p => p).ToArray();
            foreach (int peer in PeerIds)
                NextSequenceByPeer[peer] = 10_000 + peer * 100_000;
        }

        public int EmpireIdForPeer(int peer) => Started.EmpireIdForPeer(peer);
        public UniverseScreen ClientUniverse(int peer) => Started.Clients.First(c => c.PeerId == peer).Universe;
        public Empire ClientEmpire(int peer) => ClientUniverse(peer).UState.GetEmpireById(EmpireIdForPeer(peer));

        public int PeekSequence(int peer) => NextSequenceByPeer[peer];

        public int NextSequence(int peer)
        {
            int next = NextSequenceByPeer[peer];
            NextSequenceByPeer[peer] = next + 1;
            return next;
        }

        public void MarkSequenceUsed(int peer, int usedSequence)
            => NextSequenceByPeer[peer] = Math.Max(NextSequenceByPeer[peer], usedSequence + 1);

        public string NextNameSuffix(int peer)
            => peer.ToString(CultureInfo.InvariantCulture) + "_" + NextSequenceByPeer[peer].ToString(CultureInfo.InvariantCulture);

        public string NextGlobalNameSuffix()
        {
            GlobalNameSuffix++;
            return GlobalNameSuffix.ToString(CultureInfo.InvariantCulture);
        }
    }

    sealed class DeterministicSoakRng
    {
        ulong State;

        public DeterministicSoakRng(ulong seed)
        {
            State = seed != 0 ? seed : 0x9E3779B97F4A7C15UL;
        }

        public bool NextBool() => (NextULong() & 1UL) != 0;

        public int NextInt(int maxExclusive)
            => maxExclusive <= 1 ? 0 : (int)(NextULong() % (uint)maxExclusive);

        public int NextInt(int minInclusive, int maxExclusive)
            => maxExclusive <= minInclusive ? minInclusive : minInclusive + NextInt(maxExclusive - minInclusive);

        public float NextFloat01()
            => (NextULong() >> 40) * (1f / 16777216f);

        public int NextWeighted(params int[] weights)
        {
            int total = weights.Where(w => w > 0).Sum();
            if (total <= 0)
                return 0;
            int roll = NextInt(total);
            for (int i = 0; i < weights.Length; ++i)
            {
                int weight = Math.Max(0, weights[i]);
                if (roll < weight)
                    return i;
                roll -= weight;
            }
            return weights.Length - 1;
        }

        ulong NextULong()
        {
            ulong z = (State += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}

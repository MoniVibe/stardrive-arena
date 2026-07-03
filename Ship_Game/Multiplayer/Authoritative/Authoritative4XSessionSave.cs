using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Ship_Game.Data.Serialization;
using Ship_Game.Data.Yaml;
using Ship_Game.Data.YamlSerializer;
using Ship_Game.GameScreens.LoadGame;
using Ship_Game.Ships;
using Ship_Game.Universe;

namespace Ship_Game.Multiplayer.Authoritative;

[StarDataType]
public sealed class Authoritative4XPeerEmpireSave
{
    [StarData] public int PeerId;
    [StarData] public int EmpireId;
}

[StarDataType]
public sealed class Authoritative4XEmpireRuntimeSave
{
    [StarData] public int EmpireId;
    [StarData] public int MoneyBits;
    [StarData] public int TaxRateBits;
    [StarData] public int TreasuryGoalBits;
    [StarData] public int AutomationFlags;
    [StarData] public string CurrentAutoFreighter = "";
    [StarData] public string CurrentAutoColony = "";
    [StarData] public string CurrentAutoScout = "";
    [StarData] public string CurrentConstructor = "";
    [StarData] public string CurrentResearchStation = "";
    [StarData] public string CurrentMiningStation = "";
}

[StarDataType]
public sealed class Authoritative4XEmpireTechSave
{
    [StarData] public int EmpireId;
    [StarData] public string TechUid = "";
    [StarData] public int Level;
}

[StarDataType]
public sealed class Authoritative4XSessionMetadata
{
    public const int CurrentVersion = 1;

    [StarData] public int Version = CurrentVersion;
    [StarData] public string SessionId = "";
    [StarData] public string StartFingerprint = "";
    [StarData] public string SettingsHash = "";
    [StarData] public int GenerationSeed;
    [StarData] public int HostPeerId;
    [StarData] public int LocalPeerId;
    [StarData] public int LastProcessedTick;
    [StarData] public int[] HumanEmpireIds = Array.Empty<int>();
    [StarData] public Authoritative4XPeerEmpireSave[] EmpireIdByPeer = Array.Empty<Authoritative4XPeerEmpireSave>();
    [StarData] public Authoritative4XEmpireRuntimeSave[] EmpireRuntimeState = Array.Empty<Authoritative4XEmpireRuntimeSave>();
    [StarData] public Authoritative4XEmpireTechSave[] EmpireTechState = Array.Empty<Authoritative4XEmpireTechSave>();

    public IReadOnlyDictionary<int, int> ToPeerEmpireMap()
        => (EmpireIdByPeer ?? Array.Empty<Authoritative4XPeerEmpireSave>())
            .GroupBy(m => m.PeerId)
            .ToDictionary(g => g.Key, g => g.Last().EmpireId);

    public int[] NormalizedHumanEmpireIds()
        => (HumanEmpireIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

    public static Authoritative4XSessionMetadata FromGenerated(Authoritative4XGeneratedGameStart generated,
        int hostPeerId, int localPeerId, string sessionId = "", string startFingerprint = "",
        uint lastProcessedTick = 0)
    {
        if (generated == null)
            throw new ArgumentNullException(nameof(generated));

        return new Authoritative4XSessionMetadata
        {
            Version = CurrentVersion,
            SessionId = sessionId ?? "",
            StartFingerprint = startFingerprint ?? "",
            SettingsHash = generated.Settings?.SettingsHash ?? "",
            GenerationSeed = generated.Settings?.GenerationSeed ?? generated.AuthorityUniverse.UState.P.GenerationSeed,
            HostPeerId = hostPeerId,
            LocalPeerId = localPeerId,
            LastProcessedTick = checked((int)Math.Min(lastProcessedTick, int.MaxValue)),
            HumanEmpireIds = (generated.HumanEmpireIds ?? Array.Empty<int>()).OrderBy(id => id).ToArray(),
            EmpireIdByPeer = generated.EmpireIdByPeer
                .OrderBy(kv => kv.Key)
                .Select(kv => new Authoritative4XPeerEmpireSave { PeerId = kv.Key, EmpireId = kv.Value })
                .ToArray(),
            EmpireRuntimeState = Authoritative4XSessionSave.CaptureEmpireRuntimeState(generated.AuthorityUniverse),
            EmpireTechState = Authoritative4XSessionSave.CaptureEmpireTechState(generated.AuthorityUniverse),
        };
    }

    public string Summary()
        => string.Create(CultureInfo.InvariantCulture,
            $"version={Version} session='{SessionId}' seed={GenerationSeed} hostPeer={HostPeerId} "
            + $"localPeer={LocalPeerId} humans={string.Join(",", NormalizedHumanEmpireIds())} "
            + $"map={string.Join(",", ToPeerEmpireMap().OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"))}");
}

public sealed class Authoritative4XLoadedSession : IDisposable
{
    public readonly UniverseScreen Universe;
    public readonly Authoritative4XSessionMetadata Metadata;
    public IReadOnlyDictionary<int, int> EmpireIdByPeer => Metadata.ToPeerEmpireMap();
    public int[] HumanEmpireIds => Metadata.NormalizedHumanEmpireIds();

    public Authoritative4XLoadedSession(UniverseScreen universe, Authoritative4XSessionMetadata metadata)
    {
        Universe = universe ?? throw new ArgumentNullException(nameof(universe));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    public int EmpireIdForPeer(int peerId) => EmpireIdByPeer[peerId];

    public void Dispose()
    {
        AuthoritativeHumanPlayers.Clear(Universe.UState);
        Universe.Dispose();
    }
}

public static class Authoritative4XSessionSave
{
    public static FileInfo MetadataFileFor(FileInfo saveFile)
    {
        if (saveFile == null)
            throw new ArgumentNullException(nameof(saveFile));
        string dir = saveFile.DirectoryName ?? "";
        string name = Path.GetFileNameWithoutExtension(saveFile.Name) + ".auth4x.yaml";
        return new FileInfo(Path.Combine(dir, name));
    }

    public static void Save(UniverseScreen universe, FileInfo saveFile,
        Authoritative4XSessionMetadata metadata)
    {
        if (universe == null)
            throw new ArgumentNullException(nameof(universe));
        if (saveFile == null)
            throw new ArgumentNullException(nameof(saveFile));
        if (metadata == null)
            throw new ArgumentNullException(nameof(metadata));

        saveFile.Directory?.Create();
        metadata.EmpireRuntimeState = CaptureEmpireRuntimeState(universe);
        metadata.EmpireTechState = CaptureEmpireTechState(universe);
        new SavedGame(universe).SaveTo(saveFile);
        SaveMetadata(MetadataFileFor(saveFile), metadata);
    }

    public static void SaveMetadata(FileInfo metadataFile, Authoritative4XSessionMetadata metadata)
    {
        if (metadataFile == null)
            throw new ArgumentNullException(nameof(metadataFile));
        if (metadata == null)
            throw new ArgumentNullException(nameof(metadata));
        metadataFile.Directory?.Create();
        YamlSerializer.SerializeOne(metadataFile, EncodeYamlScalars(metadata));
    }

    public static string SerializeMetadata(Authoritative4XSessionMetadata metadata)
    {
        if (metadata == null)
            throw new ArgumentNullException(nameof(metadata));

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        YamlSerializer.SerializeOne(writer, EncodeYamlScalars(metadata));
        return writer.ToString();
    }

    public static Authoritative4XSessionMetadata DeserializeMetadata(string yaml)
    {
        using var reader = new StringReader(yaml ?? "");
        using var parser = new YamlParser("Authoritative4XSessionMetadata", reader);
        Authoritative4XSessionMetadata metadata = parser.DeserializeOne<Authoritative4XSessionMetadata>()
                                                  ?? throw new InvalidDataException(
                                                      "Authoritative 4X metadata was empty.");
        DecodeYamlScalars(metadata);
        ValidateMetadata(metadata);
        return metadata;
    }

    public static Authoritative4XSessionMetadata LoadMetadata(FileInfo saveFile)
    {
        FileInfo metadataFile = MetadataFileFor(saveFile);
        if (!metadataFile.Exists)
            throw new FileNotFoundException($"Authoritative 4X metadata sidecar was not found: {metadataFile.FullName}");

        Authoritative4XSessionMetadata metadata = YamlParser.DeserializeOne<Authoritative4XSessionMetadata>(metadataFile)
                                                  ?? throw new InvalidDataException(
                                                      $"Authoritative 4X metadata was empty: {metadataFile.FullName}");
        DecodeYamlScalars(metadata);
        ValidateMetadata(metadata);
        return metadata;
    }

    static void ValidateMetadata(Authoritative4XSessionMetadata metadata)
    {
        if (metadata.Version <= 0 || metadata.Version > Authoritative4XSessionMetadata.CurrentVersion)
            throw new InvalidDataException($"Unsupported authoritative 4X metadata version {metadata.Version}.");
        if (metadata.ToPeerEmpireMap().Count == 0)
            throw new InvalidDataException("Authoritative 4X metadata contains no peer-to-empire mappings.");
        if (metadata.NormalizedHumanEmpireIds().Length == 0)
            throw new InvalidDataException("Authoritative 4X metadata contains no human empire ids.");
    }

    public static Authoritative4XLoadedSession Load(FileInfo saveFile, bool startSimThread = false)
    {
        if (saveFile == null)
            throw new ArgumentNullException(nameof(saveFile));

        AuthoritativeMutationScope stateApplication = Authoritative4XClientContext.EnterStateApplication();
        try
        {
            Authoritative4XSessionMetadata metadata = LoadMetadata(saveFile);
            UniverseScreen universe = LoadGame.Load(saveFile, noErrorDialogs: true, startSimThread);
            if (universe == null)
                throw new InvalidOperationException($"Could not load authoritative 4X save: {saveFile.FullName}");
            ApplyMetadata(universe, metadata);
            return new Authoritative4XLoadedSession(universe, metadata);
        }
        finally
        {
            stateApplication.Dispose();
        }
    }

    public static void ApplyMetadata(UniverseScreen universe, Authoritative4XSessionMetadata metadata)
    {
        if (universe == null)
            throw new ArgumentNullException(nameof(universe));
        if (metadata == null)
            throw new ArgumentNullException(nameof(metadata));

        int[] humanEmpireIds = metadata.NormalizedHumanEmpireIds();
        AuthoritativeHumanPlayers.SetHumanControlledEmpires(universe.UState, humanEmpireIds);
        foreach (int empireId in humanEmpireIds)
            Authoritative4XLobby.DisableHumanEmpireAutomation(universe.UState.GetEmpireById(empireId));
        ApplyEmpireRuntimeState(universe.UState, metadata.EmpireRuntimeState);
        ApplyEmpireTechState(universe.UState, metadata.EmpireTechState);

        universe.CreateSimThread = false;
        universe.UState.Objects.EnableParallelUpdate = false;
        NormalizeLoadedConstructionQueueDesigns(universe.UState);
        int seed = metadata.GenerationSeed != 0 ? metadata.GenerationSeed : universe.UState.P.GenerationSeed;
        universe.UState.EnableDeterministicRng((uint)seed ^ 0x4D503458u);
    }

    public static Authoritative4XEmpireRuntimeSave[] CaptureEmpireRuntimeState(UniverseScreen universe)
        => universe != null
            ? CaptureEmpireRuntimeState(universe.UState)
            : Array.Empty<Authoritative4XEmpireRuntimeSave>();

    public static Authoritative4XEmpireTechSave[] CaptureEmpireTechState(UniverseScreen universe)
        => universe != null
            ? CaptureEmpireTechState(universe.UState)
            : Array.Empty<Authoritative4XEmpireTechSave>();

    static Authoritative4XEmpireRuntimeSave[] CaptureEmpireRuntimeState(UniverseState universe)
        => universe?.Empires
            .OrderBy(e => e.Id)
            .Select(e => new Authoritative4XEmpireRuntimeSave
            {
                EmpireId = e.Id,
                MoneyBits = FloatBits(e.Money),
                TaxRateBits = FloatBits(e.data.TaxRate),
                TreasuryGoalBits = FloatBits(e.data.treasuryGoal),
                AutomationFlags = (int)AutomationFlags(e),
                CurrentAutoFreighter = e.data.CurrentAutoFreighter ?? "",
                CurrentAutoColony = e.data.CurrentAutoColony ?? "",
                CurrentAutoScout = e.data.CurrentAutoScout ?? "",
                CurrentConstructor = e.data.CurrentConstructor ?? "",
                CurrentResearchStation = e.data.CurrentResearchStation ?? "",
                CurrentMiningStation = e.data.CurrentMiningStation ?? "",
            })
            .ToArray() ?? Array.Empty<Authoritative4XEmpireRuntimeSave>();

    static Authoritative4XEmpireTechSave[] CaptureEmpireTechState(UniverseState universe)
        => universe?.Empires
            .OrderBy(e => e.Id)
            .SelectMany(e => e.TechEntries
                .Where(t => t.Unlocked)
                .OrderBy(t => t.UID, StringComparer.Ordinal)
                .Select(t => new Authoritative4XEmpireTechSave
                {
                    EmpireId = e.Id,
                    TechUid = t.UID ?? "",
                    Level = t.Level,
                }))
            .ToArray() ?? Array.Empty<Authoritative4XEmpireTechSave>();

    static void ApplyEmpireRuntimeState(UniverseState universe, Authoritative4XEmpireRuntimeSave[] states)
    {
        if (universe == null || states == null || states.Length == 0)
            return;

        foreach (Authoritative4XEmpireRuntimeSave state in states)
        {
            Empire empire = state.EmpireId > 0 && state.EmpireId <= universe.Empires.Count
                ? universe.GetEmpireById(state.EmpireId)
                : null;
            if (empire == null)
                continue;

            empire.Money = FloatFromBits(state.MoneyBits);
            empire.data.TaxRate = FloatFromBits(state.TaxRateBits);
            empire.data.treasuryGoal = FloatFromBits(state.TreasuryGoalBits);
            ApplyAutomationFlags(empire, (AuthoritativeEmpireAutomationFlags)state.AutomationFlags);
            empire.data.CurrentAutoFreighter = state.CurrentAutoFreighter ?? "";
            empire.data.CurrentAutoColony = state.CurrentAutoColony ?? "";
            empire.data.CurrentAutoScout = state.CurrentAutoScout ?? "";
            empire.data.CurrentConstructor = state.CurrentConstructor ?? "";
            empire.data.CurrentResearchStation = state.CurrentResearchStation ?? "";
            empire.data.CurrentMiningStation = state.CurrentMiningStation ?? "";
        }
    }

    public static void ApplyEmpireTechState(UniverseState universe, Authoritative4XEmpireTechSave[] states)
    {
        if (universe == null || states == null || states.Length == 0)
            return;

        foreach (Authoritative4XEmpireTechSave state in states)
        {
            if (state.EmpireId <= 0 || state.EmpireId > universe.Empires.Count || string.IsNullOrEmpty(state.TechUid))
                continue;

            Empire empire = universe.GetEmpireById(state.EmpireId);
            if (empire == null || !empire.TryGetTechEntry(state.TechUid, out TechEntry tech) || tech == TechEntry.None)
                continue;

            ApplyUnlockedTech(empire, tech, state.Level);
        }
    }

    public static void ApplyUnlockedTech(Empire empire, TechEntry tech, int authoritativeLevel)
    {
        if (empire == null || tech == null || tech == TechEntry.None)
            return;

        authoritativeLevel = Math.Max(0, authoritativeLevel);
        if (!tech.Unlocked)
            empire.UnlockTech(tech, TechUnlockType.Normal, null);

        int guard = 0;
        while (tech.Unlocked && tech.Level < authoritativeLevel && guard++ < 32)
            empire.UnlockTech(tech, TechUnlockType.Normal, null);
    }

    static void ApplyAutomationFlags(Empire empire, AuthoritativeEmpireAutomationFlags flags)
    {
        if (empire == null)
            return;

        flags &= AuthoritativeEmpireAutomationFlags.All;
        empire.AutoPickConstructors = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickConstructors);
        empire.AutoPickBestColonizer = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestColonizer);
        empire.AutoPickBestFreighter = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestFreighter);
        empire.AutoResearch = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoResearch);
        empire.AutoBuildTerraformers = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildTerraformers);
        empire.AutoTaxes = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoTaxes);
        empire.AutoPickBestResearchStation = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestResearchStation);
        empire.AutoPickBestMiningStation = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestMiningStation);
        empire.AutoExplore = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoExplore);
        empire.AutoColonize = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoColonize);
        empire.AutoBuildSpaceRoads = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildSpaceRoads);
        empire.AutoFreighters = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoFreighters);
        empire.AutoBuildResearchStations = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildResearchStations);
        empire.AutoBuildMiningStations = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildMiningStations);
        empire.AutoMilitary = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoMilitary);

        bool rushAll = flags.HasFlag(AuthoritativeEmpireAutomationFlags.RushAllConstruction);
        empire.RushAllConstruction = rushAll;
    }

    static AuthoritativeEmpireAutomationFlags AutomationFlags(Empire e)
    {
        var flags = AuthoritativeEmpireAutomationFlags.None;
        if (e.AutoPickConstructors) flags |= AuthoritativeEmpireAutomationFlags.AutoPickConstructors;
        if (e.AutoPickBestColonizer) flags |= AuthoritativeEmpireAutomationFlags.AutoPickBestColonizer;
        if (e.AutoPickBestFreighter) flags |= AuthoritativeEmpireAutomationFlags.AutoPickBestFreighter;
        if (e.AutoResearch) flags |= AuthoritativeEmpireAutomationFlags.AutoResearch;
        if (e.AutoBuildTerraformers) flags |= AuthoritativeEmpireAutomationFlags.AutoBuildTerraformers;
        if (e.AutoTaxes) flags |= AuthoritativeEmpireAutomationFlags.AutoTaxes;
        if (e.AutoPickBestResearchStation) flags |= AuthoritativeEmpireAutomationFlags.AutoPickBestResearchStation;
        if (e.AutoPickBestMiningStation) flags |= AuthoritativeEmpireAutomationFlags.AutoPickBestMiningStation;
        if (e.AutoExplore) flags |= AuthoritativeEmpireAutomationFlags.AutoExplore;
        if (e.AutoColonize) flags |= AuthoritativeEmpireAutomationFlags.AutoColonize;
        if (e.AutoBuildSpaceRoads) flags |= AuthoritativeEmpireAutomationFlags.AutoBuildSpaceRoads;
        if (e.AutoFreighters) flags |= AuthoritativeEmpireAutomationFlags.AutoFreighters;
        if (e.AutoBuildResearchStations) flags |= AuthoritativeEmpireAutomationFlags.AutoBuildResearchStations;
        if (e.AutoBuildMiningStations) flags |= AuthoritativeEmpireAutomationFlags.AutoBuildMiningStations;
        if (e.AutoMilitary) flags |= AuthoritativeEmpireAutomationFlags.AutoMilitary;
        if (e.RushAllConstruction) flags |= AuthoritativeEmpireAutomationFlags.RushAllConstruction;
        return flags;
    }

    static int FloatBits(float value) => BitConverter.SingleToInt32Bits(value);
    static float FloatFromBits(int bits) => BitConverter.Int32BitsToSingle(bits);

    static void NormalizeLoadedConstructionQueueDesigns(UniverseState universe)
    {
        foreach (Planet planet in universe.Planets)
        {
            foreach (QueueItem item in planet.ConstructionQueue)
            {
                string name = item?.ShipData?.Name;
                if (string.IsNullOrEmpty(name) || !name.StartsWith("TEST_", StringComparison.Ordinal))
                    continue;

                string stockName = name.Substring("TEST_".Length);
                if (ResourceManager.Ships.GetDesign(stockName, out IShipDesign stock))
                    item.ShipData = stock;
            }
        }
    }

    static Authoritative4XSessionMetadata EncodeYamlScalars(Authoritative4XSessionMetadata metadata)
        => new()
        {
            Version = metadata.Version,
            SessionId = EncodeYamlScalar(metadata.SessionId),
            StartFingerprint = EncodeYamlScalar(metadata.StartFingerprint),
            SettingsHash = EncodeYamlScalar(metadata.SettingsHash),
            GenerationSeed = metadata.GenerationSeed,
            HostPeerId = metadata.HostPeerId,
            LocalPeerId = metadata.LocalPeerId,
            LastProcessedTick = metadata.LastProcessedTick,
            HumanEmpireIds = metadata.HumanEmpireIds?.ToArray() ?? Array.Empty<int>(),
            EmpireIdByPeer = metadata.EmpireIdByPeer?
                .Select(m => new Authoritative4XPeerEmpireSave { PeerId = m.PeerId, EmpireId = m.EmpireId })
                .ToArray() ?? Array.Empty<Authoritative4XPeerEmpireSave>(),
            EmpireRuntimeState = metadata.EmpireRuntimeState?
                .Select(e => new Authoritative4XEmpireRuntimeSave
                {
                    EmpireId = e.EmpireId,
                    MoneyBits = e.MoneyBits,
                    TaxRateBits = e.TaxRateBits,
                    TreasuryGoalBits = e.TreasuryGoalBits,
                    AutomationFlags = e.AutomationFlags,
                    CurrentAutoFreighter = EncodeYamlScalar(e.CurrentAutoFreighter),
                    CurrentAutoColony = EncodeYamlScalar(e.CurrentAutoColony),
                    CurrentAutoScout = EncodeYamlScalar(e.CurrentAutoScout),
                    CurrentConstructor = EncodeYamlScalar(e.CurrentConstructor),
                    CurrentResearchStation = EncodeYamlScalar(e.CurrentResearchStation),
                    CurrentMiningStation = EncodeYamlScalar(e.CurrentMiningStation),
                })
                .ToArray() ?? Array.Empty<Authoritative4XEmpireRuntimeSave>(),
            EmpireTechState = metadata.EmpireTechState?
                .Select(t => new Authoritative4XEmpireTechSave
                {
                    EmpireId = t.EmpireId,
                    TechUid = EncodeYamlScalar(t.TechUid),
                    Level = t.Level,
                })
                .ToArray() ?? Array.Empty<Authoritative4XEmpireTechSave>(),
        };

    static void DecodeYamlScalars(Authoritative4XSessionMetadata metadata)
    {
        metadata.SessionId = DecodeYamlScalar(metadata.SessionId);
        metadata.StartFingerprint = DecodeYamlScalar(metadata.StartFingerprint);
        metadata.SettingsHash = DecodeYamlScalar(metadata.SettingsHash);
        foreach (Authoritative4XEmpireRuntimeSave e in metadata.EmpireRuntimeState ?? Array.Empty<Authoritative4XEmpireRuntimeSave>())
        {
            e.CurrentAutoFreighter = DecodeYamlScalar(e.CurrentAutoFreighter);
            e.CurrentAutoColony = DecodeYamlScalar(e.CurrentAutoColony);
            e.CurrentAutoScout = DecodeYamlScalar(e.CurrentAutoScout);
            e.CurrentConstructor = DecodeYamlScalar(e.CurrentConstructor);
            e.CurrentResearchStation = DecodeYamlScalar(e.CurrentResearchStation);
            e.CurrentMiningStation = DecodeYamlScalar(e.CurrentMiningStation);
        }
        foreach (Authoritative4XEmpireTechSave t in metadata.EmpireTechState ?? Array.Empty<Authoritative4XEmpireTechSave>())
            t.TechUid = DecodeYamlScalar(t.TechUid);
    }

    static string EncodeYamlScalar(string value)
        => value != null && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? "hex-" + value
            : value ?? "";

    static string DecodeYamlScalar(string value)
        => value != null && value.StartsWith("hex-0x", StringComparison.OrdinalIgnoreCase)
            ? value.Substring(4)
            : value ?? "";
}

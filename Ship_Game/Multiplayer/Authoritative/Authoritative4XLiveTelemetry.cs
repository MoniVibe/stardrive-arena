using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Ship_Game.AI;
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
            + $"pos=({command.Position.X:0.###},{command.Position.Y:0.###}) "
            + $"textHash=0x{TextHash(command.Text):X16} textChars={(command.Text ?? "").Length} "
            + $"summary='{OneLine(CommandSummary(command))}' name='{OneLine(TextPreview(command.Text))}'");
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

    public void SyncMismatch(Authoritative4XSyncMismatchException mismatch)
    {
        if (mismatch == null)
            return;

        string authorityPayloadPath = WritePayloadArtifact("authority", mismatch.AuthoritySnapshot?.Payload);
        string clientPayloadPath = WritePayloadArtifact("client", mismatch.ClientSnapshot?.Payload);
        AuthoritativePlayerCommand command = mismatch.Command;
        AuthoritativeCommandResult result = mismatch.Result;
        Write("SYNC_MISMATCH",
            $"origin={result?.OriginPeer ?? 0} seq={result?.Sequence ?? 0} kind={command?.Kind.ToString() ?? ""} "
            + $"tick={mismatch.ClientSnapshot?.Tick ?? 0} accepted={result?.Accepted ?? false} "
            + $"authorityHash={SnapshotHash(mismatch.AuthoritySnapshot)} clientHash={SnapshotHash(mismatch.ClientSnapshot)} "
            + $"authorityDigest='{mismatch.AuthoritySnapshot?.SyncDigest ?? ""}' "
            + $"clientDigest='{mismatch.ClientSnapshot?.SyncDigest ?? ""}' "
            + $"authorityRows='{PayloadRowCounts(mismatch.AuthoritySnapshot?.Payload)}' "
            + $"clientRows='{PayloadRowCounts(mismatch.ClientSnapshot?.Payload)}' "
            + $"firstDiff='{OneLine(FirstPayloadDifference(mismatch.AuthoritySnapshot?.Payload, mismatch.ClientSnapshot?.Payload))}' "
            + $"authorityPayload='{authorityPayloadPath}' clientPayload='{clientPayloadPath}'");
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

    static ulong TextHash(string text)
    {
        var hash = DetHash.New();
        hash.AddString(text ?? "");
        return hash.Value;
    }

    static string TextPreview(string text)
    {
        text ??= "";
        const int MaxPreview = 256;
        return text.Length <= MaxPreview ? text : text[..MaxPreview] + "...";
    }

    static string CommandSummary(AuthoritativePlayerCommand command)
    {
        if (command == null)
            return "";

        switch (command.Kind)
        {
            case AuthoritativePlayerCommandKind.NoOp:
                return "payload=NoOp";
            case AuthoritativePlayerCommandKind.MoveShip:
                return $"payload=MoveShip order={(MoveOrder)command.TargetId} dest={Vec(command.Position)}";
            case AuthoritativePlayerCommandKind.SetColonyType:
                return Enum.IsDefined(typeof(Planet.ColonyType), (Planet.ColonyType)command.TargetId)
                    ? $"payload=ColonyType type={(Planet.ColonyType)command.TargetId}"
                    : $"payload=ColonyType invalid={command.TargetId}";
            case AuthoritativePlayerCommandKind.SetColonizationGoal:
                return $"payload=ColonizationGoal enabled={command.TargetId == 1}";
            case AuthoritativePlayerCommandKind.SetResearchTopic:
            case AuthoritativePlayerCommandKind.QueueResearch:
            case AuthoritativePlayerCommandKind.RemoveResearchQueueItem:
                return $"payload=Research tech='{OneLine(command.Text)}'";
            case AuthoritativePlayerCommandKind.MoveResearchQueueItem:
                return $"payload=ResearchMove tech='{OneLine(command.Text)}' move={(AuthoritativeResearchQueueMove)command.TargetId}";
            case AuthoritativePlayerCommandKind.DiplomacyProposal:
                return $"payload=DiplomacyProposal type={(AuthoritativeDiplomacyProposalType)command.TargetId} termsChars={(command.Text ?? "").Length}";
            case AuthoritativePlayerCommandKind.DiplomacyResponse:
                return $"payload=DiplomacyResponse kind={(AuthoritativeDiplomacyResponseKind)command.TargetId} proposal={command.SubjectId} termsChars={(command.Text ?? "").Length}";
            case AuthoritativePlayerCommandKind.DesignShip:
                return $"payload=DesignShip encodedChars={(command.Text ?? "").Length}";
            case AuthoritativePlayerCommandKind.QueueBuild:
            case AuthoritativePlayerCommandKind.QueueBuilding:
            case AuthoritativePlayerCommandKind.QueueTroop:
            case AuthoritativePlayerCommandKind.QueuePlanetOrbitalBuild:
                return $"payload={command.Kind} item='{OneLine(command.Text)}'";
            case AuthoritativePlayerCommandKind.AttackShip:
                return $"payload=AttackShip queued={string.Equals(command.Text, "queue", StringComparison.Ordinal)}";
            case AuthoritativePlayerCommandKind.ShipPlanetOrder:
                return TryParseShipPlanetOrder(command.Text, out AuthoritativeShipPlanetOrderType planetOrder,
                           out bool clearOrders, out MoveOrder moveOrder)
                    ? $"payload=ShipPlanetOrder order={planetOrder} clear={clearOrders} move={moveOrder}"
                    : "payload=ShipPlanetOrder invalid=true";
            case AuthoritativePlayerCommandKind.SetColonyLabor:
                return AuthoritativePlayerCommand.TryParseColonyLaborPayload(command.Text,
                           out float food, out float prod, out float res, out bool foodLocked,
                           out bool prodLocked, out bool resLocked)
                    ? $"payload=ColonyLabor food={food:0.###} prod={prod:0.###} res={res:0.###} locks={foodLocked},{prodLocked},{resLocked}"
                    : "payload=ColonyLabor invalid=true";
            case AuthoritativePlayerCommandKind.SetEmpireBudget:
                return AuthoritativePlayerCommand.TryParseEmpireBudgetPayload(command.Text,
                           out float taxRate, out float treasuryGoal, out bool autoTaxes)
                    ? $"payload=EmpireBudget tax={taxRate:0.###} treasury={treasuryGoal:0.###} auto={autoTaxes}"
                    : "payload=EmpireBudget invalid=true";
            case AuthoritativePlayerCommandKind.SetEmpireAutomation:
                return AuthoritativePlayerCommand.TryParseEmpireAutomationPayload(command.Text,
                           out string freighter, out string colony, out string scout,
                           out string constructor, out string researchStation, out string miningStation)
                    ? $"payload=EmpireAutomation flags={(AuthoritativeEmpireAutomationFlags)command.TargetId} "
                      + $"freighter='{OneLine(freighter)}' colony='{OneLine(colony)}' "
                      + $"scout='{OneLine(scout)}' constructor='{OneLine(constructor)}' "
                      + $"researchStation='{OneLine(researchStation)}' miningStation='{OneLine(miningStation)}'"
                    : "payload=EmpireAutomation invalid=true";
            case AuthoritativePlayerCommandKind.SetPlanetGoodsState:
                return $"payload=PlanetGoods kind={(AuthoritativePlanetGoodsKind)command.TargetId} state={command.Text}";
            case AuthoritativePlayerCommandKind.SetPlanetPrioritizedPort:
                return $"payload=PrioritizedPort enabled={command.TargetId == 1}";
            case AuthoritativePlayerCommandKind.SetPlanetManualBudget:
                return $"payload=ManualBudget kind={(AuthoritativePlanetBudgetKind)command.TargetId} percent={command.Text}";
            case AuthoritativePlayerCommandKind.SetFleetAssignment:
                return $"payload=FleetAssignment fleet={command.SubjectId} mode={(AuthoritativeFleetAssignmentMode)command.TargetId} ships='{OneLine(command.Text)}'";
            case AuthoritativePlayerCommandKind.MoveFleet:
                return $"payload=MoveFleet order={(MoveOrder)command.TargetId} dest={Vec(command.Position)}";
            case AuthoritativePlayerCommandKind.ShipSpecialOrder:
                return $"payload=ShipSpecialOrder type={(AuthoritativeShipSpecialOrderType)command.TargetId}";
            case AuthoritativePlayerCommandKind.SetShipCombatStance:
                return $"payload=ShipCombatStance state={command.TargetId}";
            case AuthoritativePlayerCommandKind.SetShipTradePolicy:
                return $"payload=ShipTradePolicy kind={(AuthoritativeShipTradePolicyKind)command.TargetId} enabled={command.Text == "1"}";
            case AuthoritativePlayerCommandKind.SetShipCarrierPolicy:
                return $"payload=ShipCarrierPolicy kind={(AuthoritativeShipCarrierPolicyKind)command.TargetId} enabled={command.Text == "1"}";
            case AuthoritativePlayerCommandKind.SetShipTradeRoute:
                return $"payload=ShipTradeRoute planet={command.TargetId} enabled={command.Text == "1"}";
            case AuthoritativePlayerCommandKind.SetShipAreaOfOperation:
                return AuthoritativePlayerCommand.TryParseRectanglePayload(command.Text, out SDGraphics.Rectangle area)
                    ? $"payload=ShipAreaOfOperation action={(AuthoritativeShipAreaOfOperationAction)command.TargetId} rect={area.X},{area.Y},{area.Width},{area.Height}"
                    : "payload=ShipAreaOfOperation invalid=true";
            case AuthoritativePlayerCommandKind.RenameFleet:
                return $"payload=RenameFleet name='{OneLine(command.Text)}'";
            case AuthoritativePlayerCommandKind.AutoArrangeFleet:
                return $"payload=AutoArrangeFleet fleet={command.SubjectId}";
            case AuthoritativePlayerCommandKind.ShipLifecycleOrder:
                return $"payload=ShipLifecycle type={(AuthoritativeShipLifecycleOrderType)command.TargetId}";
            case AuthoritativePlayerCommandKind.LoadFleetPatrol:
                return $"payload=LoadFleetPatrol name='{OneLine(command.Text)}'";
            case AuthoritativePlayerCommandKind.SetFleetLayout:
                return AuthoritativePlayerCommand.TryParseFleetLayout(command.Text,
                           out AuthoritativeFleetLayoutNode[] nodes)
                    ? $"payload=FleetLayout nodes={nodes.Length}"
                    : "payload=FleetLayout invalid=true";
            case AuthoritativePlayerCommandKind.QueueDeepSpaceBuild:
                return AuthoritativePlayerCommand.TryParseDeepSpaceBuildPayload(command.Text,
                           out string buildDesign, out SDGraphics.Vector2 tetherOffset)
                    ? $"payload=DeepSpaceBuild design='{OneLine(buildDesign)}' tether={Vec(tetherOffset)}"
                    : "payload=DeepSpaceBuild invalid=true";
            case AuthoritativePlayerCommandKind.CancelDeepSpaceBuild:
                return AuthoritativePlayerCommand.TryParseDeepSpaceCancelPayload(command.Text,
                           out string cancelDesign, out GoalType goalType)
                    ? $"payload=DeepSpaceCancel design='{OneLine(cancelDesign)}' goal={goalType}"
                    : "payload=DeepSpaceCancel invalid=true";
            case AuthoritativePlayerCommandKind.SetPlanetGovernorOptions:
                return $"payload=GovernorOptions flags={(AuthoritativePlanetGovernorOptions)command.TargetId}";
            case AuthoritativePlayerCommandKind.SetPlanetManualTradeSlots:
                return AuthoritativePlayerCommand.TryParseManualTradeSlotsPayload(command.Text,
                           out int foodImport, out int prodImport, out int coloImport,
                           out int foodExport, out int prodExport, out int coloExport)
                    ? $"payload=ManualTradeSlots import={foodImport},{prodImport},{coloImport} export={foodExport},{prodExport},{coloExport}"
                    : "payload=ManualTradeSlots invalid=true";
            case AuthoritativePlayerCommandKind.SetPlanetDefenseTargets:
                return AuthoritativePlayerCommand.TryParsePlanetDefenseTargetsPayload(command.Text,
                           out int garrison, out int platforms, out int shipyards, out int stations)
                    ? $"payload=DefenseTargets garrison={garrison} platforms={platforms} shipyards={shipyards} stations={stations}"
                    : "payload=DefenseTargets invalid=true";
            case AuthoritativePlayerCommandKind.RenameFleetPatrol:
                return AuthoritativePlayerCommand.TryParsePatrolRenamePayload(command.Text,
                           out string oldName, out string newName)
                    ? $"payload=RenameFleetPatrol old='{OneLine(oldName)}' new='{OneLine(newName)}'"
                    : "payload=RenameFleetPatrol invalid=true";
            case AuthoritativePlayerCommandKind.DeleteFleetPatrol:
                return $"payload=DeleteFleetPatrol name='{OneLine(command.Text)}'";
            case AuthoritativePlayerCommandKind.ClearFleetPatrol:
                return $"payload=ClearFleetPatrol fleet={command.SubjectId}";
            case AuthoritativePlayerCommandKind.CreateFleetPatrol:
                return AuthoritativePlayerCommand.TryParsePatrolWaypoints(command.Text, out var waypoints)
                    ? $"payload=CreateFleetPatrol waypoints={waypoints.Length}"
                    : "payload=CreateFleetPatrol invalid=true";
            case AuthoritativePlayerCommandKind.ApplyColonyBlueprints:
                return AuthoritativePlayerCommand.TryParseBlueprintsTemplate(command.Text,
                           out BlueprintsTemplate template)
                    ? $"payload=Blueprints name='{OneLine(template.Name)}' type={template.ColonyType} buildings={template.PlannedBuildings?.Count ?? 0}"
                    : "payload=Blueprints invalid=true";
            case AuthoritativePlayerCommandKind.ClearColonyBlueprints:
                return $"payload=ClearBlueprints planet={command.SubjectId}";
            case AuthoritativePlayerCommandKind.ScrapColonyTile:
                return $"payload=ScrapColonyTile kind={(AuthoritativeColonyTileScrapKind)command.TargetId} tile={Vec(command.Position)} expected='{OneLine(command.Text)}'";
            default:
                return $"payload={command.Kind}";
        }
    }

    static bool TryParseShipPlanetOrder(string text, out AuthoritativeShipPlanetOrderType orderType,
        out bool clearOrders, out MoveOrder moveOrder)
    {
        orderType = default;
        clearOrders = false;
        moveOrder = MoveOrder.Regular;

        string[] parts = (text ?? "").Split('|');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int orderValue)
            || !Enum.IsDefined(typeof(AuthoritativeShipPlanetOrderType),
                (AuthoritativeShipPlanetOrderType)orderValue)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clearValue)
            || clearValue is not (0 or 1)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int moveValue))
        {
            return false;
        }

        orderType = (AuthoritativeShipPlanetOrderType)orderValue;
        clearOrders = clearValue == 1;
        moveOrder = (MoveOrder)moveValue;
        return true;
    }

    static string Vec(SDGraphics.Vector2 vector)
        => string.Create(CultureInfo.InvariantCulture, $"({vector.X:0.###},{vector.Y:0.###})");

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

    string WritePayloadArtifact(string side, string payload)
    {
        if (payload == null || string.IsNullOrWhiteSpace(SessionPath))
            return "";

        string dir = Path.GetDirectoryName(SessionPath);
        string baseName = Path.GetFileNameWithoutExtension(SessionPath);
        string path = Path.Combine(dir ?? "", $"{baseName}-sync-mismatch-{side}.payload");
        try
        {
            File.WriteAllText(path, payload);
            return path;
        }
        catch (Exception e)
        {
            return $"write-failed:{e.GetType().Name}";
        }
    }

    static string SnapshotHash(AuthoritativeStateSnapshot snapshot)
        => snapshot == null ? "" : $"0x{snapshot.HashHi:X16}:0x{snapshot.HashLo:X16}";

    static string FirstPayloadDifference(string authorityPayload, string clientPayload)
    {
        string[] authority = (authorityPayload ?? "").Split('\n');
        string[] client = (clientPayload ?? "").Split('\n');
        int count = Math.Max(authority.Length, client.Length);
        for (int i = 0; i < count; ++i)
        {
            string a = i < authority.Length ? authority[i].TrimEnd('\r') : "<missing>";
            string c = i < client.Length ? client[i].TrimEnd('\r') : "<missing>";
            if (!string.Equals(a, c, StringComparison.Ordinal))
                return $"line={i + 1} authority=\"{a}\" client=\"{c}\"";
        }
        return "payloads differ only outside line comparison";
    }

    static string OneLine(string text)
        => (text ?? "").Replace("\\", "\\\\")
                       .Replace("'", "\\'")
                       .Replace("\r", "\\r")
                       .Replace("\n", "\\n");
}

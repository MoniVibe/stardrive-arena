using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SDLockstep;

namespace Ship_Game.Multiplayer.Authoritative;

public enum Authoritative4XLiveRole
{
    Host,
    Client,
}

/// <summary>
/// Runtime owner for a visible authoritative 4X multiplayer universe. It keeps the
/// existing single-player screens inert by only installing a command context after a
/// live session is explicitly attached.
/// </summary>
public sealed class Authoritative4XLiveSession : IDisposable
{
    const int FirstHeartbeatSequence = -1;
    const int HeartbeatBroadcastInterval = 4;
    static readonly float[] SpeedCycle = { 0.5f, 1f, 2f, 4f };

    readonly UniverseScreen Universe;
    readonly Authoritative4XNetworkHost Host;
    readonly Authoritative4XNetworkClient Client;
    readonly Authoritative4XClientContext UiContext;
    readonly Authoritative4XLiveTelemetry Telemetry;
    readonly IReadOnlyDictionary<int, int> EmpireByPeer;
    readonly int[] HumanEmpireIds;
    readonly string LiveSessionId;
    readonly string LiveStartFingerprint;
    readonly Queue<AuthoritativeDiplomacyPopup> DiplomacyPopups = new();
    int HeartbeatSequence = FirstHeartbeatSequence;
    string LastTelemetryResultKey = "";
    string LastTelemetryError = "";
    string LastTelemetryRawDriftKey = "";
    string LastTelemetrySyncMismatchKey = "";
    FileInfo LastSentSessionSave;
    FileInfo PendingHostRecoverySave;
    string PendingHostRecoveryReason = "";
    bool Disposed;

    public readonly Authoritative4XLiveRole Role;
    public readonly int LocalPeerId;
    public readonly int LocalEmpireId;
    public bool IsHost => Role == Authoritative4XLiveRole.Host;

    public string LastError => Role == Authoritative4XLiveRole.Host
        ? Host?.LastError ?? ""
        : Client?.LastError ?? "";

    public AuthoritativeCommandResult LastResult => Role == Authoritative4XLiveRole.Host
        ? Host?.LastResult
        : Client?.LastResult;

    public AuthoritativeStateSnapshot LastSnapshot => Role == Authoritative4XLiveRole.Host
        ? Host?.LastAuthoritySnapshot
        : Client?.LastClientSnapshot;
    public Authoritative4XSyncMismatchException LastSyncMismatch => Role == Authoritative4XLiveRole.Client
        ? Client?.LastSyncMismatch
        : null;
    public bool IsWaitingForResync => Role == Authoritative4XLiveRole.Client
                                      && Client?.IsWaitingForResync == true;
    public bool IsResyncInProgress => Role == Authoritative4XLiveRole.Host
        ? Host?.IsResyncInProgress == true
        : Client?.IsWaitingForResync == true;

    public string TelemetrySessionPath => Telemetry?.SessionPath ?? "";
    public string TelemetrySessionId => LiveSessionId;
    public string TelemetryStartFingerprint => LiveStartFingerprint;

    public void RecordViewPerf(string details)
        => Telemetry?.Event("VIEW_PERF", details ?? "");

    public void RecordUiOrderBlocked(string details)
        => Telemetry?.Event("UI_ORDER_BLOCKED", details ?? "");

    public void ActivateUiCommandContext()
        => UiContext?.Activate();

    Authoritative4XLiveSession(UniverseScreen universe, Authoritative4XLiveRole role,
        int localPeerId, int localEmpireId, Authoritative4XNetworkHost host,
        Authoritative4XNetworkClient client, IReadOnlyDictionary<int, int> empireByPeer,
        int[] humanEmpireIds, string sessionId = "", string startFingerprint = "",
        string startSummary = "", int firstUiSequence = 1)
    {
        Universe = universe ?? throw new ArgumentNullException(nameof(universe));
        Role = role;
        LocalPeerId = localPeerId;
        LocalEmpireId = localEmpireId;
        Host = host;
        Client = client;
        EmpireByPeer = new Dictionary<int, int>(empireByPeer ?? new Dictionary<int, int>());
        HumanEmpireIds = (humanEmpireIds ?? Array.Empty<int>()).ToArray();
        LiveSessionId = sessionId ?? "";
        LiveStartFingerprint = startFingerprint ?? "";
        Telemetry = Authoritative4XLiveTelemetry.Start(role, localPeerId, localEmpireId,
            empireByPeer, humanEmpireIds, LiveSessionId, LiveStartFingerprint, startSummary);
        TcpLockstepTransport transport = Host?.SharedTransport ?? Client?.SharedTransport;
        if (Telemetry != null && transport != null && Authoritative4XLiveTelemetry.IsWireTraceEnabled())
            transport.AuthoritativeFrameTrace = Telemetry.WireFrame;
        UiContext = Authoritative4XClientContext.Begin(localPeerId, localEmpireId, SubmitFromUi,
            Math.Max(1, firstUiSequence));
    }

    public static Authoritative4XLiveSession HostGame(UniverseScreen universe,
        TcpLockstepTransport transport, int localPeerId, IReadOnlyDictionary<int, int> empireByPeer,
        int[] humanEmpireIds, string sessionId = "", string startFingerprint = "",
        string startSummary = "")
    {
        if (empireByPeer == null || !empireByPeer.TryGetValue(localPeerId, out int localEmpireId))
            throw new ArgumentException($"Peer {localPeerId} is not mapped to an empire.", nameof(empireByPeer));

        var host = new Authoritative4XNetworkHost(universe, transport, empireByPeer, humanEmpireIds, localPeerId);
        return new Authoritative4XLiveSession(universe, Authoritative4XLiveRole.Host,
            localPeerId, localEmpireId, host, client: null, empireByPeer, humanEmpireIds,
            sessionId, startFingerprint, startSummary);
    }

    public static Authoritative4XLiveSession ClientGame(UniverseScreen universe,
        TcpLockstepTransport transport, int localPeerId, int localEmpireId, int[] humanEmpireIds,
        string sessionId = "", string startFingerprint = "", string startSummary = "",
        IReadOnlyDictionary<int, int> empireByPeerForTelemetry = null, int firstUiSequence = 1)
    {
        var client = new Authoritative4XNetworkClient(universe, transport, localPeerId, humanEmpireIds);
        IReadOnlyDictionary<int, int> empireByPeer = empireByPeerForTelemetry
            ?? new Dictionary<int, int> { [localPeerId] = localEmpireId };
        return new Authoritative4XLiveSession(universe, Authoritative4XLiveRole.Client,
            localPeerId, localEmpireId, host: null, client, empireByPeer, humanEmpireIds,
            sessionId, startFingerprint, startSummary, firstUiSequence);
    }

    public void Poll()
    {
        if (Disposed)
            return;

        try
        {
            if (Role == Authoritative4XLiveRole.Host)
            {
                Host.Poll();
                RecordNetworkError();
                HandleResyncRequests();
                RecordProcessedCommands();
                SubmitHeartbeat();
                RecordProcessedCommands();
                EnqueuePopups(Host.DrainLocalPopups());
            }
            else
            {
                Client.Poll();
                RecordNetworkError();
                RecordLastResult();
                RecordSyncMismatch();
                RecordRawHashDrift();
                EnqueuePopups(Client.DrainPopupsForClient());
            }
        }
        catch (Authoritative4XSyncMismatchException e)
        {
            RecordLastResult(force: true);
            Telemetry?.SyncMismatch(e);
            Telemetry?.Event("POLL_EXCEPTION", $"{e.GetType().Name}: {e.Message}");
            throw;
        }
        catch (Exception e)
        {
            Telemetry?.Event("POLL_EXCEPTION", $"{e.GetType().Name}: {e.Message}");
            throw;
        }
    }

    public bool TryTogglePause()
    {
        if (Disposed || Role != Authoritative4XLiveRole.Host)
            return false;
        SendControl(!Universe.UState.Paused, Universe.UState.GameSpeed);
        return true;
    }

    public bool TrySetGameSpeed(float speed)
    {
        if (Disposed || Role != Authoritative4XLiveRole.Host)
            return false;
        SendControl(Universe.UState.Paused, ClampGameSpeed(speed));
        return true;
    }

    public bool TryCycleGameSpeed()
    {
        if (Disposed || Role != Authoritative4XLiveRole.Host)
            return false;

        float current = ClampGameSpeed(Universe.UState.GameSpeed);
        float next = SpeedCycle[0];
        foreach (float candidate in SpeedCycle)
        {
            if (current < candidate - 0.001f)
            {
                next = candidate;
                break;
            }
        }
        if (current >= SpeedCycle[^1] - 0.001f)
            next = SpeedCycle[0];

        SendControl(Universe.UState.Paused, next);
        return true;
    }

    public bool TryApplyHostControl(bool paused, float speed)
    {
        if (Disposed || Role != Authoritative4XLiveRole.Host)
            return false;
        SendControl(paused, ClampGameSpeed(speed));
        return true;
    }

    public bool TrySaveSession(FileInfo saveFile, out string error)
    {
        error = "";
        if (Disposed)
        {
            error = "Authoritative session is disposed.";
            return false;
        }
        if (Role != Authoritative4XLiveRole.Host)
        {
            error = "Only the host can save an authoritative multiplayer session.";
            return false;
        }
        if (saveFile == null)
        {
            error = "Save file is missing.";
            return false;
        }

        try
        {
            Authoritative4XSessionSave.Save(Universe, saveFile, BuildMetadata());
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    public bool TryRecoverClientFromReceivedSave(out UniverseScreen recoveredUniverse, out string error)
    {
        recoveredUniverse = null;
        error = "";
        if (Disposed)
        {
            error = "Authoritative session is disposed.";
            return false;
        }
        if (Role != Authoritative4XLiveRole.Client)
        {
            error = "Only passive clients recover from authoritative save transfers.";
            return false;
        }

        Authoritative4XReceivedSave[] receivedSaves = DrainReceivedSessionSaves();
        if (receivedSaves.Length == 0)
            return false;

        Authoritative4XReceivedSave received = receivedSaves[^1];
        try
        {
            Authoritative4XLoadedSession loaded = Authoritative4XSessionSave.Load(received.SaveFile);
            IReadOnlyDictionary<int, int> empireByPeer = loaded.Metadata.ToPeerEmpireMap();
            if (!empireByPeer.TryGetValue(LocalPeerId, out int localEmpireId))
                throw new InvalidDataException(
                    $"Received authoritative save does not map local peer {LocalPeerId} to an empire.");

            string startSummary = $"resync reason='{received.Reason}' sha256={received.Sha256} "
                                  + loaded.Metadata.Summary();
            int nextSequence = Math.Max(1, (Client.LastResult?.Sequence ?? 0) + 1);
            Authoritative4XLiveSession recovered = ClientGame(loaded.Universe,
                Client.SharedTransport, LocalPeerId, localEmpireId, loaded.HumanEmpireIds,
                loaded.Metadata.SessionId, loaded.Metadata.StartFingerprint, startSummary, empireByPeer,
                nextSequence);
            loaded.Universe.AttachAuthoritative4XMultiplayer(recovered);
            loaded.Universe.RestoreAuthoritative4XClientViewFrom(Universe);
            AssertRecoveredLocalBinding(loaded.Universe, recovered, localEmpireId, "client");
            AuthoritativeStateSnapshot loadedSnapshot = AuthoritativeStateSnapshot.Capture(loaded.Universe,
                checked((uint)Math.Max(0, loaded.Metadata.LastProcessedTick)));
            Client.SendResyncAck(loadedSnapshot.Tick, loadedSnapshot.SyncDigest, received.Sha256);
            Telemetry?.Event("RESYNC_RECOVERED",
                $"epoch={Client.ResyncEpoch} file='{received.SaveFile.FullName}' "
                + $"sha256={received.Sha256} digest='{loadedSnapshot.SyncDigest}' reason='{received.Reason}'");
            DisposeRetainingTransportForRecovery();
            recoveredUniverse = loaded.Universe;
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            Telemetry?.Event("RESYNC_RECOVERY_ERROR", e.Message);
            return false;
        }
    }

    public bool TryRecoverHostFromLastSentSave(out UniverseScreen recoveredUniverse, out string error)
    {
        recoveredUniverse = null;
        error = "";
        if (Disposed)
        {
            error = "Authoritative session is disposed.";
            return false;
        }
        if (Role != Authoritative4XLiveRole.Host)
        {
            error = "Only hosts recover from their own authoritative save image.";
            return false;
        }
        if (PendingHostRecoverySave == null)
            return false;
        if (Host.IsResyncInProgress)
            return false;

        FileInfo save = PendingHostRecoverySave;
        string reason = PendingHostRecoveryReason;
        PendingHostRecoverySave = null;
        PendingHostRecoveryReason = "";
        try
        {
            Authoritative4XLoadedSession loaded = Authoritative4XSessionSave.Load(save);
            IReadOnlyDictionary<int, int> empireByPeer = loaded.Metadata.ToPeerEmpireMap();
            if (!empireByPeer.TryGetValue(LocalPeerId, out int localEmpireId))
                throw new InvalidDataException(
                    $"Reloaded authoritative save does not map host peer {LocalPeerId} to an empire.");

            string startSummary = $"host-resync reason='{reason}' " + loaded.Metadata.Summary();
            Authoritative4XLiveSession recovered = HostGame(loaded.Universe, Host.SharedTransport,
                LocalPeerId, empireByPeer, loaded.HumanEmpireIds, loaded.Metadata.SessionId,
                loaded.Metadata.StartFingerprint, startSummary);
            loaded.Universe.AttachAuthoritative4XMultiplayer(recovered);
            AssertRecoveredLocalBinding(loaded.Universe, recovered, localEmpireId, "host");
            Telemetry?.Event("RESYNC_HOST_RECOVERED",
                $"file='{save.FullName}' reason='{reason}'");
            DisposeRetainingTransportForRecovery();
            recoveredUniverse = loaded.Universe;
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            Telemetry?.Event("RESYNC_HOST_RECOVERY_ERROR", e.Message);
            return false;
        }
    }

    public bool TrySendSessionSaveToPeer(int peerId, FileInfo saveFile, out string error,
        string reason = "host-save-transfer")
    {
        error = "";
        if (Disposed)
        {
            error = "Authoritative session is disposed.";
            return false;
        }
        if (Role != Authoritative4XLiveRole.Host)
        {
            error = "Only the host can send authoritative multiplayer saves.";
            return false;
        }

        try
        {
            saveFile ??= new FileInfo(Path.Combine(Path.GetTempPath(), "stardrive-auth4x-host-saves",
                $"mp-session-{Guid.NewGuid():N}.sav"));
            Authoritative4XSessionMetadata metadata = BuildMetadata();
            Authoritative4XSessionSave.Save(Universe, saveFile, metadata);
            Host.SendSaveTransfer(peerId, saveFile, metadata, reason);
            LastSentSessionSave = saveFile;
            Telemetry?.Event("SAVE_TRANSFER_SENT",
                $"peer={peerId} file='{saveFile.FullName}' bytes={saveFile.Length} reason='{reason}'");
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            Telemetry?.Event("SAVE_TRANSFER_ERROR", e.Message);
            return false;
        }
    }

    public Authoritative4XReceivedSave[] DrainReceivedSessionSaves()
        => Role == Authoritative4XLiveRole.Client
            ? Client.DrainReceivedSaves()
            : Array.Empty<Authoritative4XReceivedSave>();

    static void AssertRecoveredLocalBinding(UniverseScreen universe, Authoritative4XLiveSession session,
        int localEmpireId, string role)
    {
        if (universe == null)
            throw new InvalidDataException($"Recovered authoritative {role} universe is missing.");
        if (session == null)
            throw new InvalidDataException($"Recovered authoritative {role} session is missing.");
        if (session.LocalEmpireId != localEmpireId)
            throw new InvalidDataException(
                $"Recovered authoritative {role} session maps local empire {session.LocalEmpireId}, expected {localEmpireId}.");

        Empire local = universe.UState.GetEmpire(localEmpireId);
        if (local == null)
            throw new InvalidDataException(
                $"Recovered authoritative {role} universe does not contain local empire {localEmpireId}.");
        if (!ReferenceEquals(universe.Authoritative4XLocalPlayerForUi, local))
            throw new InvalidDataException(
                $"Recovered authoritative {role} UI binding points at empire {universe.Authoritative4XLocalPlayerForUi?.Id ?? 0}, expected {localEmpireId}.");
        if (!ReferenceEquals(universe.Player, local))
            throw new InvalidDataException(
                $"Recovered authoritative {role} UniverseScreen.Player points at empire {universe.Player?.Id ?? 0}, expected {localEmpireId}.");
        if (!AuthoritativeHumanPlayers.IsHumanControlled(local))
            throw new InvalidDataException(
                $"Recovered authoritative {role} local empire {localEmpireId} is not registered as human-controlled.");
    }

    public AuthoritativeResyncRequestMessage[] DrainResyncRequests()
        => Role == Authoritative4XLiveRole.Host
            ? Host.DrainResyncRequests()
            : Array.Empty<AuthoritativeResyncRequestMessage>();

    public bool TryDequeueDiplomacyPopup(out AuthoritativeDiplomacyPopup popup)
    {
        if (DiplomacyPopups.Count == 0)
        {
            popup = null;
            return false;
        }

        popup = DiplomacyPopups.Dequeue();
        return true;
    }

    Authoritative4XSessionMetadata BuildMetadata()
        => new()
        {
            Version = Authoritative4XSessionMetadata.CurrentVersion,
            SessionId = LiveSessionId,
            StartFingerprint = LiveStartFingerprint,
            SettingsHash = "",
            GenerationSeed = Universe.UState.P.GenerationSeed,
            HostPeerId = LocalPeerId,
            LocalPeerId = LocalPeerId,
            LastProcessedTick = checked((int)Math.Min(LastSnapshot?.Tick ?? 0, int.MaxValue)),
            HumanEmpireIds = HumanEmpireIds.OrderBy(id => id).ToArray(),
            EmpireIdByPeer = EmpireByPeer.OrderBy(kv => kv.Key)
                .Select(kv => new Authoritative4XPeerEmpireSave { PeerId = kv.Key, EmpireId = kv.Value })
                .ToArray(),
            EmpireRuntimeState = Authoritative4XSessionSave.CaptureEmpireRuntimeState(Universe),
        };

    void EnqueuePopups(IEnumerable<AuthoritativeDiplomacyPopup> popups)
    {
        foreach (AuthoritativeDiplomacyPopup popup in popups ?? Enumerable.Empty<AuthoritativeDiplomacyPopup>())
        {
            DiplomacyPopups.Enqueue(popup);
            Telemetry?.Popup(popup);
        }
    }

    void SubmitFromUi(AuthoritativePlayerCommand command)
    {
        if (Disposed || command == null)
            return;

        if (IsResyncInProgress)
        {
            Telemetry?.Event("COMMAND_BLOCKED_RESYNC",
                $"peer={LocalPeerId} seq={command.Sequence} kind={command.Kind}");
            return;
        }

        Telemetry?.Command("ui", LocalPeerId, command);
        if (Role == Authoritative4XLiveRole.Host)
        {
            Host.SubmitLocal(LocalPeerId, command);
            RecordProcessedCommands(force: true);
        }
        else
            Client.Submit(command);
    }

    void HandleResyncRequests()
    {
        if (Role != Authoritative4XLiveRole.Host)
            return;

        foreach (AuthoritativeResyncAckMessage ack in Host.DrainResyncAcks())
        {
            Telemetry?.Event("RESYNC_ACK",
                $"peer={ack.FromPeer} epoch={ack.Epoch} tick={ack.Tick} "
                + $"digest='{ack.LoadedDigest}' sha256='{ack.SaveSha256}' error='{ack.Error}'");
        }

        AuthoritativeResyncRequestMessage[] requests = Host.DrainResyncRequests();
        if (requests.Length == 0)
            return;

        if (Host.IsResyncInProgress)
        {
            Telemetry?.Event("RESYNC_REQUEST_DURING_EPOCH",
                $"count={requests.Length} epoch={Host.CurrentResyncEpoch} pending='{string.Join(",", Host.PendingResyncPeerIds)}'");
            return;
        }

        AuthoritativeResyncRequestMessage request = requests[0];
        string reason = $"resync epoch={Host.CurrentResyncEpoch + 1} tick={request.Tick} "
                        + $"digest='{request.ClientDigest}' reason='{request.Reason ?? ""}'";
        int epoch = Host.BeginResyncEpoch(request);
        if (epoch == 0)
            return;

        try
        {
            FileInfo saveFile = new(Path.Combine(Path.GetTempPath(), "stardrive-auth4x-host-saves",
                $"mp-resync-e{epoch}-{Guid.NewGuid():N}.sav"));
            Authoritative4XSessionMetadata metadata = BuildMetadata();
            Authoritative4XSessionSave.Save(Universe, saveFile, metadata);
            foreach (int peerId in Host.RemotePeerIds)
                Host.SendSaveTransfer(peerId, saveFile, metadata, reason);

            LastSentSessionSave = saveFile;
            PendingHostRecoverySave = LastSentSessionSave;
            PendingHostRecoveryReason = reason;
            Telemetry?.Event("RESYNC_SAVE_BROADCAST",
                $"epoch={epoch} peers='{string.Join(",", Host.RemotePeerIds)}' file='{saveFile.FullName}' "
                + $"bytes={saveFile.Length} reason='{reason}'");
        }
        catch (Exception e)
        {
            Telemetry?.Event("RESYNC_SAVE_ERROR",
                $"epoch={epoch} peer={request.FromPeer} tick={request.Tick} error='{e.Message}'");
        }
    }

    void SendControl(bool paused, float speed)
    {
        speed = ClampGameSpeed(speed);
        Telemetry?.Control("host", paused, speed);
        ApplyControl(paused, speed);
        Host.BroadcastControl(paused, speed);
    }

    void ApplyControl(bool paused, float speed)
    {
        Universe.UState.Paused = paused;
        Universe.UState.GameSpeed = ClampGameSpeed(speed);
    }

    static float ClampGameSpeed(float speed)
        => float.IsFinite(speed) ? Math.Clamp(speed, 0.25f, 8f) : 1f;

    void SubmitHeartbeat()
    {
        if (Universe.UState.Paused || IsResyncInProgress)
            return;

        int sequence = HeartbeatSequence--;
        bool broadcast = (-sequence % HeartbeatBroadcastInterval) == 0;
        Host.SubmitLocal(LocalPeerId, AuthoritativePlayerCommand.NoOp(sequence, LocalEmpireId), broadcast);
    }

    void RecordNetworkError()
    {
        string error = LastError;
        if (string.IsNullOrWhiteSpace(error) || string.Equals(error, LastTelemetryError, StringComparison.Ordinal))
            return;
        LastTelemetryError = error;
        Telemetry?.NetworkError(error);
    }

    void RecordLastResult(bool force = false)
    {
        AuthoritativeCommandResult result = LastResult;
        if (result == null)
            return;

        if (!force && result.Sequence < 0 && result.Tick % 300 != 0)
            return;

        string key = $"{result.OriginPeer}:{result.Sequence}:{result.Tick}:{result.Accepted}:{LastSnapshot?.SyncDigest}";
        if (!force && string.Equals(key, LastTelemetryResultKey, StringComparison.Ordinal))
            return;

        LastTelemetryResultKey = key;
        Telemetry?.Result(result, LastSnapshot);
    }

    void RecordRawHashDrift()
    {
        if (Role != Authoritative4XLiveRole.Client)
            return;
        Authoritative4XRawHashDrift drift = Client?.LastRawHashDrift;
        if (drift == null)
            return;

        AuthoritativeCommandResult result = drift.Result;
        bool heartbeat = result?.Sequence < 0;
        if (heartbeat && result.Tick % 300 != 0)
            return;

        string key = $"{result?.OriginPeer}:{result?.Sequence}:"
                     + $"{drift.ClientSnapshot?.Tick}:{drift.AuthoritySnapshot?.HashLo}:"
                     + $"{drift.AuthoritySnapshot?.HashHi}:{drift.ClientSnapshot?.HashLo}:"
                     + $"{drift.ClientSnapshot?.HashHi}:{drift.ClientSnapshot?.SyncDigest}";
        if (string.Equals(key, LastTelemetryRawDriftKey, StringComparison.Ordinal))
            return;

        LastTelemetryRawDriftKey = key;
        Telemetry?.RawHashDrift(drift);
    }

    void RecordSyncMismatch()
    {
        if (Role != Authoritative4XLiveRole.Client)
            return;
        Authoritative4XSyncMismatchException mismatch = Client?.LastSyncMismatch;
        if (mismatch == null)
            return;

        string key = $"{mismatch.Result?.OriginPeer}:{mismatch.Result?.Sequence}:"
                     + $"{mismatch.ClientSnapshot?.Tick}:{mismatch.ClientSnapshot?.SyncDigest}:"
                     + $"{mismatch.AuthoritySnapshot?.SyncDigest}";
        if (string.Equals(key, LastTelemetrySyncMismatchKey, StringComparison.Ordinal))
            return;

        LastTelemetrySyncMismatchKey = key;
        Telemetry?.SyncMismatch(mismatch);
    }

    void RecordProcessedCommands(bool force = false)
    {
        if (Role != Authoritative4XLiveRole.Host)
            return;

        foreach (Authoritative4XProcessedCommand processed in Host.DrainProcessedCommands())
        {
            if (processed.Result == null)
                continue;

            bool remote = processed.PeerId != LocalPeerId;
            if (remote)
                Telemetry?.Command("network", processed.PeerId, processed.Command);

            bool heartbeat = processed.Command?.Kind == AuthoritativePlayerCommandKind.NoOp
                             && processed.Command.Sequence < 0;
            if (!force && heartbeat && processed.Result.Tick % 300 != 0)
                continue;

            string key = $"{processed.Result.OriginPeer}:{processed.Result.Sequence}:"
                         + $"{processed.Result.Tick}:{processed.Result.Accepted}:"
                         + $"{processed.Snapshot?.SyncDigest}";
            if (!force && string.Equals(key, LastTelemetryResultKey, StringComparison.Ordinal))
                continue;

            LastTelemetryResultKey = key;
            Telemetry?.Result(processed.Result, processed.Snapshot);
        }
    }

    void Dispose(bool disposeNetwork)
    {
        if (Disposed)
            return;
        Disposed = true;
        Telemetry?.Event("DISPOSE", $"role={Role} peer={LocalPeerId}");
        UiContext?.Dispose();
        if (disposeNetwork)
        {
            Host?.Dispose();
            Client?.Dispose();
        }
        Telemetry?.Dispose();
    }

    void DisposeRetainingTransportForRecovery() => Dispose(disposeNetwork: false);

    public void Dispose() => Dispose(disposeNetwork: true);
}

using System;
using System.Collections.Generic;
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
    static readonly float[] SpeedCycle = { 0.5f, 1f, 2f, 4f };

    readonly UniverseScreen Universe;
    readonly Authoritative4XNetworkHost Host;
    readonly Authoritative4XNetworkClient Client;
    readonly Authoritative4XClientContext UiContext;
    readonly Authoritative4XLiveTelemetry Telemetry;
    readonly Queue<AuthoritativeDiplomacyPopup> DiplomacyPopups = new();
    int HeartbeatSequence = FirstHeartbeatSequence;
    string LastTelemetryResultKey = "";
    string LastTelemetryError = "";
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

    public string TelemetrySessionPath => Telemetry?.SessionPath ?? "";

    Authoritative4XLiveSession(UniverseScreen universe, Authoritative4XLiveRole role,
        int localPeerId, int localEmpireId, Authoritative4XNetworkHost host,
        Authoritative4XNetworkClient client, IReadOnlyDictionary<int, int> empireByPeer,
        int[] humanEmpireIds)
    {
        Universe = universe ?? throw new ArgumentNullException(nameof(universe));
        Role = role;
        LocalPeerId = localPeerId;
        LocalEmpireId = localEmpireId;
        Host = host;
        Client = client;
        Telemetry = Authoritative4XLiveTelemetry.Start(role, localPeerId, localEmpireId,
            empireByPeer, humanEmpireIds);
        UiContext = Authoritative4XClientContext.Begin(localPeerId, localEmpireId, SubmitFromUi);
    }

    public static Authoritative4XLiveSession HostGame(UniverseScreen universe,
        TcpLockstepTransport transport, int localPeerId, IReadOnlyDictionary<int, int> empireByPeer,
        int[] humanEmpireIds)
    {
        if (empireByPeer == null || !empireByPeer.TryGetValue(localPeerId, out int localEmpireId))
            throw new ArgumentException($"Peer {localPeerId} is not mapped to an empire.", nameof(empireByPeer));

        var host = new Authoritative4XNetworkHost(universe, transport, empireByPeer, humanEmpireIds, localPeerId);
        return new Authoritative4XLiveSession(universe, Authoritative4XLiveRole.Host,
            localPeerId, localEmpireId, host, client: null, empireByPeer, humanEmpireIds);
    }

    public static Authoritative4XLiveSession ClientGame(UniverseScreen universe,
        TcpLockstepTransport transport, int localPeerId, int localEmpireId, int[] humanEmpireIds)
    {
        var client = new Authoritative4XNetworkClient(universe, transport, localPeerId, humanEmpireIds);
        var empireByPeer = new Dictionary<int, int> { [localPeerId] = localEmpireId };
        return new Authoritative4XLiveSession(universe, Authoritative4XLiveRole.Client,
            localPeerId, localEmpireId, host: null, client, empireByPeer, humanEmpireIds);
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
                RecordLastResult();
                SubmitHeartbeat();
                EnqueuePopups(Host.DrainLocalPopups());
            }
            else
            {
                Client.Poll();
                RecordNetworkError();
                RecordLastResult();
                EnqueuePopups(Client.DrainPopupsForClient());
            }
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

        Telemetry?.Command("ui", LocalPeerId, command);
        if (Role == Authoritative4XLiveRole.Host)
        {
            Host.SubmitLocal(LocalPeerId, command);
            RecordLastResult(force: true);
        }
        else
            Client.Submit(command);
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
        if (Universe.UState.Paused)
            return;

        Host.SubmitLocal(LocalPeerId, AuthoritativePlayerCommand.NoOp(HeartbeatSequence--, LocalEmpireId));
        RecordLastResult();
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

    public void Dispose()
    {
        if (Disposed)
            return;
        Disposed = true;
        Telemetry?.Event("DISPOSE", $"role={Role} peer={LocalPeerId}");
        UiContext?.Dispose();
        Host?.Dispose();
        Client?.Dispose();
        Telemetry?.Dispose();
    }
}

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
    readonly Queue<AuthoritativeDiplomacyPopup> DiplomacyPopups = new();
    int HeartbeatSequence = FirstHeartbeatSequence;
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

    Authoritative4XLiveSession(UniverseScreen universe, Authoritative4XLiveRole role,
        int localPeerId, int localEmpireId, Authoritative4XNetworkHost host,
        Authoritative4XNetworkClient client)
    {
        Universe = universe ?? throw new ArgumentNullException(nameof(universe));
        Role = role;
        LocalPeerId = localPeerId;
        LocalEmpireId = localEmpireId;
        Host = host;
        Client = client;
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
            localPeerId, localEmpireId, host, client: null);
    }

    public static Authoritative4XLiveSession ClientGame(UniverseScreen universe,
        TcpLockstepTransport transport, int localPeerId, int localEmpireId, int[] humanEmpireIds)
    {
        var client = new Authoritative4XNetworkClient(universe, transport, localPeerId, humanEmpireIds);
        return new Authoritative4XLiveSession(universe, Authoritative4XLiveRole.Client,
            localPeerId, localEmpireId, host: null, client);
    }

    public void Poll()
    {
        if (Disposed)
            return;

        if (Role == Authoritative4XLiveRole.Host)
        {
            Host.Poll();
            SubmitHeartbeat();
            EnqueuePopups(Host.DrainLocalPopups());
        }
        else
        {
            Client.Poll();
            EnqueuePopups(Client.DrainPopupsForClient());
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
            DiplomacyPopups.Enqueue(popup);
    }

    void SubmitFromUi(AuthoritativePlayerCommand command)
    {
        if (Disposed || command == null)
            return;

        if (Role == Authoritative4XLiveRole.Host)
            Host.SubmitLocal(LocalPeerId, command);
        else
            Client.Submit(command);
    }

    void SendControl(bool paused, float speed)
    {
        speed = ClampGameSpeed(speed);
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
    }

    public void Dispose()
    {
        if (Disposed)
            return;
        Disposed = true;
        UiContext?.Dispose();
        Host?.Dispose();
        Client?.Dispose();
    }
}

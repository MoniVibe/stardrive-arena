using System;
using System.Collections.Generic;
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

    readonly UniverseScreen Universe;
    readonly Authoritative4XNetworkHost Host;
    readonly Authoritative4XNetworkClient Client;
    readonly Authoritative4XClientContext UiContext;
    int HeartbeatSequence = FirstHeartbeatSequence;
    bool Disposed;

    public readonly Authoritative4XLiveRole Role;
    public readonly int LocalPeerId;
    public readonly int LocalEmpireId;

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
        }
        else
        {
            Client.Poll();
        }
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

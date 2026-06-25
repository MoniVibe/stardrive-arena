using System.Globalization;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using SDLockstep;
using SDUtils;
using SDUtils.Deterministic;
using Ship_Game.AI;
using Ship_Game.Determinism;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
using Ship_Game.Universe;

namespace Ship_Game.Multiplayer.Authoritative;

public sealed class AuthoritativeStateSnapshot
{
    public uint Tick;
    public ulong HashLo;
    public ulong HashHi;
    public string SyncDigest = "";
    public string Payload = "";

    public AuthoritativeStateSnapshotMessage ToMessage(int fromPeer)
        => new()
        {
            FromPeer = fromPeer,
            Tick = Tick,
            HashLo = HashLo,
            HashHi = HashHi,
            SyncDigest = SyncDigest,
            Payload = Payload,
        };

    public static AuthoritativeStateSnapshot FromMessage(AuthoritativeStateSnapshotMessage message)
        => new()
        {
            Tick = message.Tick,
            HashLo = message.HashLo,
            HashHi = message.HashHi,
            SyncDigest = message.SyncDigest ?? "",
            Payload = message.Payload ?? "",
        };

    public static AuthoritativeStateSnapshot Capture(UniverseScreen universe, uint tick,
        DeterminismProfile profile = DeterminismProfile.ReplayWinX64Float)
    {
        (ulong lo, ulong hi, _) = universe.UState.ComputeAuthoritativeStateHash(profile);
        string payload = BuildPayload(universe.UState);
        return new AuthoritativeStateSnapshot
        {
            Tick = tick,
            HashLo = lo,
            HashHi = hi,
            Payload = payload,
            SyncDigest = Digest(payload),
        };
    }

    static string BuildPayload(UniverseState us)
    {
        var sb = new StringBuilder(4096);
        foreach (Empire e in us.Empires.OrderBy(e => e.Id))
            sb.Append("E|").Append(e.Id)
              .Append('|').Append(e.Research.Topic ?? "")
              .Append('|').Append(FloatBits(e.Money))
              .AppendLine();

        foreach (Empire e in us.Empires.OrderBy(e => e.Id))
        {
            foreach (Relationship rel in e.AllRelations.OrderBy(r => r.Them.Id))
                sb.Append("R|").Append(e.Id)
                  .Append('|').Append(rel.Them.Id)
                  .Append('|').Append(rel.Known ? 1 : 0)
                  .Append('|').Append(rel.AtWar ? 1 : 0)
                  .Append('|').Append(rel.Treaty_NAPact ? 1 : 0)
                  .Append('|').Append(rel.Treaty_Trade ? 1 : 0)
                  .Append('|').Append(rel.Treaty_OpenBorders ? 1 : 0)
                  .Append('|').Append(rel.Treaty_Alliance ? 1 : 0)
                  .Append('|').Append(rel.Treaty_Peace ? 1 : 0)
                  .AppendLine();
        }

        foreach (Planet p in us.Planets.OrderBy(p => p.Id))
            sb.Append("P|").Append(p.Id)
              .Append('|').Append(p.Owner?.Id ?? 0)
              .Append('|').Append((int)p.CType)
              .Append('|').Append(p.ConstructionQueue.Count)
              .AppendLine();

        foreach (Ship s in us.Ships.OrderBy(s => s.Id))
            sb.Append("S|").Append(s.Id)
              .Append('|').Append(s.Loyalty?.Id ?? 0)
              .Append('|').Append((int)s.AI.State)
              .Append('|').Append(FloatBits(s.Position.X))
              .Append('|').Append(FloatBits(s.Position.Y))
              .Append('|').Append(FloatBits(s.AI.MovePosition.X))
              .Append('|').Append(FloatBits(s.AI.MovePosition.Y))
              .AppendLine();

        return sb.ToString();
    }

    static string Digest(string payload)
    {
        var h = DetHash.New();
        h.AddString(payload);
        return "0x" + h.Value.ToString("X16", CultureInfo.InvariantCulture);
    }

    static uint FloatBits(float value) => System.BitConverter.SingleToUInt32Bits(value);
}

public sealed class Authoritative4XAuthority
{
    readonly UniverseScreen Universe;
    readonly FixedSimTime Step;
    readonly Authoritative4XCommandApplicator Applicator;
    readonly AuthoritativeDiplomacyManager Diplomacy;

    public uint Tick { get; private set; }

    public Authoritative4XAuthority(UniverseScreen universe, float dt = 1f / 60f, int[] humanEmpireIds = null)
    {
        Universe = universe;
        Step = new FixedSimTime(dt);
        if (humanEmpireIds != null)
            AuthoritativeHumanPlayers.SetHumanControlledEmpires(universe.UState, humanEmpireIds);
        Diplomacy = new AuthoritativeDiplomacyManager(universe.UState);
        Applicator = new Authoritative4XCommandApplicator(universe.UState, Diplomacy);
    }

    public (AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot)
        Process(AuthoritativePlayerCommand command)
    {
        AuthoritativeCommandResult result = Applicator.Apply(command, Tick + 1);
        return Advance(result);
    }

    public (AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot)
        RejectAndAdvance(int sequence, string reason)
    {
        return Advance(new AuthoritativeCommandResult
        {
            Sequence = sequence,
            Accepted = false,
            Tick = Tick + 1,
            Reason = reason ?? "",
        });
    }

    (AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot) Advance(AuthoritativeCommandResult result)
    {
        Universe.SingleSimulationStep(Step);
        Tick++;
        return (result, AuthoritativeStateSnapshot.Capture(Universe, Tick));
    }

    public AuthoritativeDiplomacyPopup[] DrainDiplomacyPopups() => Diplomacy.DrainPopups();
}

public sealed class Authoritative4XClientReplica
{
    readonly UniverseScreen Universe;
    readonly FixedSimTime Step;
    readonly Authoritative4XCommandApplicator Applicator;
    readonly AuthoritativeDiplomacyManager Diplomacy;

    public uint Tick { get; private set; }
    public AuthoritativeStateSnapshot LastSnapshot { get; private set; }

    public Authoritative4XClientReplica(UniverseScreen universe, float dt = 1f / 60f, int[] humanEmpireIds = null)
    {
        Universe = universe;
        Step = new FixedSimTime(dt);
        if (humanEmpireIds != null)
            AuthoritativeHumanPlayers.SetHumanControlledEmpires(universe.UState, humanEmpireIds);
        Diplomacy = new AuthoritativeDiplomacyManager(universe.UState);
        Applicator = new Authoritative4XCommandApplicator(universe.UState, Diplomacy);
    }

    public void ApplyAuthoritativeResult(AuthoritativePlayerCommand command, AuthoritativeCommandResult result,
        AuthoritativeStateSnapshot authoritySnapshot)
    {
        if (result.Accepted)
        {
            AuthoritativeCommandResult local = Applicator.Apply(command, result.Tick);
            if (!local.Accepted)
                throw new System.InvalidOperationException($"Client replica rejected accepted command {command.Sequence}: {local.Reason}");
        }

        Universe.SingleSimulationStep(Step);
        Tick++;
        LastSnapshot = AuthoritativeStateSnapshot.Capture(Universe, Tick);
        if (LastSnapshot.HashLo != authoritySnapshot.HashLo || LastSnapshot.HashHi != authoritySnapshot.HashHi
            || LastSnapshot.SyncDigest != authoritySnapshot.SyncDigest)
        {
            throw new System.InvalidOperationException(
                $"Authoritative sync mismatch at tick {Tick}: authority " +
                $"0x{authoritySnapshot.HashLo:X16}:0x{authoritySnapshot.HashHi:X16}/{authoritySnapshot.SyncDigest}, " +
                $"client 0x{LastSnapshot.HashLo:X16}:0x{LastSnapshot.HashHi:X16}/{LastSnapshot.SyncDigest}");
        }
    }
}

public readonly struct Authoritative4XClientSpec
{
    public readonly int PeerId;
    public readonly int EmpireId;
    public readonly UniverseScreen Universe;

    public Authoritative4XClientSpec(int peerId, int empireId, UniverseScreen universe)
    {
        PeerId = peerId;
        EmpireId = empireId;
        Universe = universe;
    }
}

/// <summary>
/// Headless Phase-A harness: client submits primitive requests over the SDLockstep transport, host
/// validates/applies/advances, then sends ack + canonical sync snapshot back.
/// </summary>
public sealed class Authoritative4XInProcessSession
{
    const int HostPeer = 1;
    const int ClientPeer = 2;

    readonly ILockstepTransport Transport;
    readonly Authoritative4XAuthority Authority;
    readonly Authoritative4XClientReplica Client;
    readonly Map<int, AuthoritativePlayerCommand> Pending = new();

    public AuthoritativeCommandResult LastResult { get; private set; }
    public AuthoritativeStateSnapshot LastAuthoritySnapshot { get; private set; }
    public AuthoritativeStateSnapshot LastClientSnapshot => Client.LastSnapshot;

    public Authoritative4XInProcessSession(UniverseScreen authorityUniverse, UniverseScreen clientUniverse,
        ILockstepTransport transport = null)
    {
        Transport = transport ?? new FakeTransport();
        Authority = new Authoritative4XAuthority(authorityUniverse);
        Client = new Authoritative4XClientReplica(clientUniverse);
        Transport.Register(HostPeer, OnHostMessage);
        Transport.Register(ClientPeer, OnClientMessage);
    }

    public void SubmitFromClient(AuthoritativePlayerCommand command)
    {
        Pending[command.Sequence] = command;
        Transport.Send(HostPeer, command.ToMessage(ClientPeer));
        Pump();
    }

    void Pump()
    {
        Transport.Poll();
        Transport.Poll();
    }

    void OnHostMessage(LockstepMessage message)
    {
        if (message is not AuthoritativeCommandRequestMessage request)
            return;

        AuthoritativePlayerCommand command = AuthoritativePlayerCommand.FromMessage(request);
        (AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot) = Authority.Process(command);
        Transport.Send(ClientPeer, result.ToMessage(HostPeer));
        Transport.Send(ClientPeer, snapshot.ToMessage(HostPeer));
    }

    void OnClientMessage(LockstepMessage message)
    {
        switch (message)
        {
            case AuthoritativeCommandResultMessage resultMessage:
                LastResult = new AuthoritativeCommandResult
                {
                    Sequence = resultMessage.Sequence,
                    Accepted = resultMessage.Accepted,
                    Tick = resultMessage.Tick,
                    Reason = resultMessage.Reason ?? "",
                };
                break;
            case AuthoritativeStateSnapshotMessage snapshotMessage:
                LastAuthoritySnapshot = AuthoritativeStateSnapshot.FromMessage(snapshotMessage);
                if (LastResult == null || !Pending.TryGetValue(LastResult.Sequence, out AuthoritativePlayerCommand command))
                    throw new System.InvalidOperationException("Received authoritative snapshot without a matching command result.");
                Pending.Remove(LastResult.Sequence);
                Client.ApplyAuthoritativeResult(command, LastResult, LastAuthoritySnapshot);
                break;
        }
    }
}

/// <summary>
/// Multi-client in-process harness for Phase-A authoritative MP features that need targeted routing,
/// such as human-to-human diplomacy popups.
/// </summary>
public sealed class Authoritative4XInProcessMultiClientSession
{
    const int HostPeer = 1;

    readonly ILockstepTransport Transport;
    readonly Authoritative4XAuthority Authority;
    readonly Dictionary<int, Authoritative4XClientReplica> Clients = new();
    readonly Dictionary<int, int> EmpireByPeer = new();
    readonly Dictionary<int, int> PeerByEmpire = new();
    readonly Dictionary<int, AuthoritativePlayerCommand> Pending = new();
    readonly Dictionary<int, AuthoritativeCommandResult> LastResults = new();
    readonly Dictionary<int, AuthoritativeStateSnapshot> LastSnapshots = new();
    readonly Dictionary<int, List<AuthoritativeDiplomacyPopup>> Popups = new();

    public AuthoritativeStateSnapshot LastAuthoritySnapshot { get; private set; }

    public Authoritative4XInProcessMultiClientSession(UniverseScreen authorityUniverse,
        Authoritative4XClientSpec[] clients, ILockstepTransport transport = null)
    {
        Transport = transport ?? new FakeTransport();
        int[] humanEmpireIds = clients.Select(c => c.EmpireId).ToArray();
        AuthoritativeHumanPlayers.SetHumanControlledEmpires(authorityUniverse.UState, humanEmpireIds);
        Authority = new Authoritative4XAuthority(authorityUniverse, humanEmpireIds: humanEmpireIds);
        Transport.Register(HostPeer, OnHostMessage);

        foreach (Authoritative4XClientSpec spec in clients)
        {
            AuthoritativeHumanPlayers.SetHumanControlledEmpires(spec.Universe.UState, humanEmpireIds);
            Clients[spec.PeerId] = new Authoritative4XClientReplica(spec.Universe, humanEmpireIds: humanEmpireIds);
            EmpireByPeer[spec.PeerId] = spec.EmpireId;
            PeerByEmpire[spec.EmpireId] = spec.PeerId;
            Popups[spec.PeerId] = new List<AuthoritativeDiplomacyPopup>();
            int peer = spec.PeerId;
            Transport.Register(peer, message => OnClientMessage(peer, message));
        }
    }

    public void SubmitFromClient(int peerId, AuthoritativePlayerCommand command)
    {
        Pending[command.Sequence] = command;
        Transport.Send(HostPeer, command.ToMessage(peerId));
        Pump();
    }

    public AuthoritativeCommandResult LastResultFor(int peerId) => LastResults[peerId];
    public AuthoritativeStateSnapshot LastClientSnapshotFor(int peerId) => LastSnapshots[peerId];
    public AuthoritativeDiplomacyPopup[] PopupsFor(int peerId) => Popups[peerId].ToArray();

    void Pump()
    {
        for (int i = 0; i < 4; ++i)
            Transport.Poll();
    }

    void OnHostMessage(LockstepMessage message)
    {
        if (message is not AuthoritativeCommandRequestMessage request)
            return;

        AuthoritativePlayerCommand command = AuthoritativePlayerCommand.FromMessage(request);
        (AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot) =
            !EmpireByPeer.TryGetValue(request.FromPeer, out int allowedEmpire) || allowedEmpire != command.EmpireId
                ? Authority.RejectAndAdvance(command.Sequence,
                    $"Peer {request.FromPeer} does not control empire {command.EmpireId}.")
                : Authority.Process(command);
        LastAuthoritySnapshot = snapshot;

        foreach (int peer in Clients.Keys)
        {
            Transport.Send(peer, result.ToMessage(HostPeer));
            Transport.Send(peer, snapshot.ToMessage(HostPeer));
        }

        foreach (AuthoritativeDiplomacyPopup popup in Authority.DrainDiplomacyPopups())
        {
            if (PeerByEmpire.TryGetValue(popup.TargetEmpireId, out int targetPeer))
                Transport.Send(targetPeer, popup.ToMessage(HostPeer));
        }
    }

    void OnClientMessage(int peerId, LockstepMessage message)
    {
        switch (message)
        {
            case AuthoritativeCommandResultMessage resultMessage:
                LastResults[peerId] = new AuthoritativeCommandResult
                {
                    Sequence = resultMessage.Sequence,
                    Accepted = resultMessage.Accepted,
                    Tick = resultMessage.Tick,
                    Reason = resultMessage.Reason ?? "",
                };
                break;
            case AuthoritativeStateSnapshotMessage snapshotMessage:
                AuthoritativeStateSnapshot authoritySnapshot = AuthoritativeStateSnapshot.FromMessage(snapshotMessage);
                if (!LastResults.TryGetValue(peerId, out AuthoritativeCommandResult result)
                    || !Pending.TryGetValue(result.Sequence, out AuthoritativePlayerCommand command))
                    throw new System.InvalidOperationException("Received authoritative snapshot without a matching command result.");
                Clients[peerId].ApplyAuthoritativeResult(command, result, authoritySnapshot);
                LastSnapshots[peerId] = Clients[peerId].LastSnapshot;
                break;
            case AuthoritativeDiplomacyPopupMessage popupMessage:
                Popups[peerId].Add(AuthoritativeDiplomacyPopup.FromMessage(popupMessage));
                break;
        }
    }
}

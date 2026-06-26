using System;
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
              .Append('|').Append(ResearchQueueSignature(e))
              .Append('|').Append(FloatBits(e.Money))
              .Append('|').Append(FloatBits(e.data.TaxRate))
              .Append('|').Append(FloatBits(e.data.treasuryGoal))
              .Append('|').Append(e.AutoTaxes ? 1 : 0)
              .AppendLine();

        foreach (Empire e in us.Empires.OrderBy(e => e.Id))
        {
            foreach (IShipDesign design in e.ShipsWeCanBuildSnapshot
                         .Where(d => d.IsPlayerDesign)
                         .OrderBy(d => d.Name, StringComparer.Ordinal))
            {
                sb.Append("D|").Append(e.Id)
                  .Append('|').Append(design.Name)
                  .Append('|').Append(design.Hull)
                  .Append('|').Append((int)design.Role)
                  .Append('|').Append(FloatBits(design.BaseCost))
                  .Append('|').Append(DesignSlotSignature(design))
                  .AppendLine();
            }
        }

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
        {
            sb.Append("P|").Append(p.Id)
              .Append('|').Append(p.Owner?.Id ?? 0)
              .Append('|').Append((int)p.CType)
              .Append('|').Append(FloatBits(p.Food.Percent))
              .Append('|').Append(FloatBits(p.Prod.Percent))
              .Append('|').Append(FloatBits(p.Res.Percent))
              .Append('|').Append(p.Food.PercentLock ? 1 : 0)
              .Append('|').Append(p.Prod.PercentLock ? 1 : 0)
              .Append('|').Append(p.Res.PercentLock ? 1 : 0)
              .Append('|').Append((int)p.FS)
              .Append('|').Append((int)p.PS)
              .Append('|').Append(p.PrioritizedPort ? 1 : 0)
              .Append('|').Append(FloatBits(p.ManualCivilianBudget))
              .Append('|').Append(FloatBits(p.ManualGrdDefBudget))
              .Append('|').Append(FloatBits(p.ManualSpcDefBudget))
              .Append('|').Append(p.ConstructionQueue.Count)
              .AppendLine();

            QueueItem[] queue = p.Construction.GetConstructionQueueSnapshot();
            for (int i = 0; i < queue.Length; ++i)
            {
                QueueItem q = queue[i];
                sb.Append("Q|").Append(p.Id)
                  .Append('|').Append(i)
                  .Append('|').Append(q.isShip ? 1 : 0)
                  .Append('|').Append(q.isBuilding ? 1 : 0)
                  .Append('|').Append(q.isTroop ? 1 : 0)
                  .Append('|').Append((int)q.QType)
                  .Append('|').Append(q.ShipData?.Name ?? "")
                  .Append('|').Append(q.Building?.Name ?? "")
                  .Append('|').Append(q.TroopType ?? "")
                  .Append('|').Append(q.pgs?.X ?? -1)
                  .Append('|').Append(q.pgs?.Y ?? -1)
                  .Append('|').Append(FloatBits(q.Cost))
                  .Append('|').Append(FloatBits(q.ProductionSpent))
                  .Append('|').Append(q.Rush ? 1 : 0)
                  .AppendLine();
            }
        }

        foreach (Ship s in us.Ships.OrderBy(s => s.Id))
            sb.Append("S|").Append(s.Id)
              .Append('|').Append(s.Loyalty?.Id ?? 0)
              .Append('|').Append((int)s.AI.State)
              .Append('|').Append(FloatBits(s.Position.X))
              .Append('|').Append(FloatBits(s.Position.Y))
              .Append('|').Append(FloatBits(s.AI.MovePosition.X))
              .Append('|').Append(FloatBits(s.AI.MovePosition.Y))
              .Append('|').Append(s.AI.Target?.Id ?? 0)
              .Append('|').Append(s.AI.HasPriorityTarget ? 1 : 0)
              .Append('|').Append(TargetQueueSignature(s))
              .Append('|').Append(ShipOrderQueueSignature(s))
              .AppendLine();

        return sb.ToString();
    }

    static string Digest(string payload)
    {
        var h = DetHash.New();
        h.AddString(payload);
        return "0x" + h.Value.ToString("X16", CultureInfo.InvariantCulture);
    }

    static string DesignSlotSignature(IShipDesign design)
    {
        if (design is not ShipDesign shipDesign)
            return string.Join(",", design.UniqueModuleUIDs.OrderBy(uid => uid, StringComparer.Ordinal));

        DesignSlot[] slots = shipDesign.GetOrLoadDesignSlots() ?? Empty<DesignSlot>.Array;
        var sb = new StringBuilder(slots.Length * 32);
        for (int i = 0; i < slots.Length; ++i)
        {
            DesignSlot s = slots[i];
            if (i != 0) sb.Append(';');
            sb.Append(s.Pos.X).Append(',').Append(s.Pos.Y)
              .Append(',').Append(s.Size.X).Append('x').Append(s.Size.Y)
              .Append(',').Append((int)s.ModuleRot)
              .Append(',').Append(s.TurretAngle)
              .Append(',').Append(s.ModuleUID ?? "")
              .Append(',').Append(s.HangarShipUID ?? "");
        }
        return sb.ToString();
    }

    static string TargetQueueSignature(Ship ship)
        => string.Join(",", ship.AI.TargetQueue.Select(s => s?.Id ?? 0));

    static string ResearchQueueSignature(Empire empire)
        => string.Join(",", empire.data.ResearchQueue);

    static string ShipOrderQueueSignature(Ship ship)
    {
        ShipAI.ShipGoal[] goals = ship.AI.OrderQueue.ToArray();
        return string.Join(";", goals.Select(g =>
            $"{(int)g.Plan},{g.TargetPlanet?.Id ?? 0},{g.TargetShip?.Id ?? 0}," +
            $"{FloatBits(g.MovePosition.X):X8},{FloatBits(g.MovePosition.Y):X8},{(int)g.MoveOrder}"));
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

readonly struct AuthoritativeCommandKey : IEquatable<AuthoritativeCommandKey>
{
    public readonly int OriginPeer;
    public readonly int Sequence;

    public AuthoritativeCommandKey(int originPeer, int sequence)
    {
        OriginPeer = originPeer;
        Sequence = sequence;
    }

    public bool Equals(AuthoritativeCommandKey other)
        => OriginPeer == other.OriginPeer && Sequence == other.Sequence;
    public override bool Equals(object obj)
        => obj is AuthoritativeCommandKey other && Equals(other);
    public override int GetHashCode()
        => HashCode.Combine(OriginPeer, Sequence);
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
        result.OriginPeer = request.FromPeer;
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
                    OriginPeer = resultMessage.OriginPeer,
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
    readonly Dictionary<AuthoritativeCommandKey, AuthoritativePlayerCommand> Pending = new();
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
        result.OriginPeer = request.FromPeer;
        LastAuthoritySnapshot = snapshot;

        foreach (int peer in Clients.Keys)
        {
            Transport.Send(peer, command.ToMessage(request.FromPeer));
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
            case AuthoritativeCommandRequestMessage requestMessage:
                Pending[new AuthoritativeCommandKey(requestMessage.FromPeer, requestMessage.Sequence)] =
                    AuthoritativePlayerCommand.FromMessage(requestMessage);
                break;
            case AuthoritativeCommandResultMessage resultMessage:
                LastResults[peerId] = new AuthoritativeCommandResult
                {
                    Sequence = resultMessage.Sequence,
                    OriginPeer = resultMessage.OriginPeer,
                    Accepted = resultMessage.Accepted,
                    Tick = resultMessage.Tick,
                    Reason = resultMessage.Reason ?? "",
                };
                break;
            case AuthoritativeStateSnapshotMessage snapshotMessage:
                AuthoritativeStateSnapshot authoritySnapshot = AuthoritativeStateSnapshot.FromMessage(snapshotMessage);
                if (!LastResults.TryGetValue(peerId, out AuthoritativeCommandResult result)
                    || !Pending.TryGetValue(new AuthoritativeCommandKey(result.OriginPeer, result.Sequence),
                        out AuthoritativePlayerCommand command))
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

/// <summary>
/// TCP-backed authoritative 4X host spine. This is deliberately thin: the host owns the real
/// UniverseState, validates incoming PlayerCommand requests, broadcasts the accepted snapshot,
/// and routes human diplomacy popups to the peer that owns the target empire.
/// </summary>
public sealed class Authoritative4XNetworkHost : IDisposable
{
    public const int HostPeerId = 1;

    readonly TcpLockstepTransport Transport;
    readonly Authoritative4XAuthority Authority;
    readonly Dictionary<int, int> EmpireByPeer;
    readonly Dictionary<int, int> PeerByEmpire;
    readonly int[] PeerIds;
    readonly int LocalPeerId;
    readonly List<AuthoritativeDiplomacyPopup> LocalPopups = new();

    public AuthoritativeCommandResult LastResult { get; private set; }
    public AuthoritativeStateSnapshot LastAuthoritySnapshot { get; private set; }
    public string LastError => Transport.LastError;
    public AuthoritativeDiplomacyPopup[] DrainLocalPopups()
    {
        AuthoritativeDiplomacyPopup[] popups = LocalPopups.ToArray();
        LocalPopups.Clear();
        return popups;
    }

    public Authoritative4XNetworkHost(UniverseScreen authorityUniverse, TcpLockstepTransport transport,
        IReadOnlyDictionary<int, int> empireByPeer, int[] humanEmpireIds = null, int localPeerId = 0)
    {
        Transport = transport;
        EmpireByPeer = new Dictionary<int, int>(empireByPeer);
        PeerByEmpire = empireByPeer.ToDictionary(kv => kv.Value, kv => kv.Key);
        PeerIds = empireByPeer.Keys.OrderBy(peer => peer).ToArray();
        LocalPeerId = localPeerId;
        if (humanEmpireIds != null)
            AuthoritativeHumanPlayers.SetHumanControlledEmpires(authorityUniverse.UState, humanEmpireIds);
        Authority = new Authoritative4XAuthority(authorityUniverse, humanEmpireIds: humanEmpireIds);
        Transport.Register(HostPeerId, OnHostMessage);
    }

    public void Poll() => Transport.Poll();
    public void Dispose() => Transport.Dispose();

    public void SubmitLocal(int peerId, AuthoritativePlayerCommand command)
    {
        ProcessCommand(peerId, command);
    }

    void OnHostMessage(LockstepMessage message)
    {
        if (message is not AuthoritativeCommandRequestMessage request)
            return;

        ProcessCommand(request.FromPeer, AuthoritativePlayerCommand.FromMessage(request));
    }

    void ProcessCommand(int fromPeer, AuthoritativePlayerCommand command)
    {
        (AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot) =
            !EmpireByPeer.TryGetValue(fromPeer, out int allowedEmpire) || allowedEmpire != command.EmpireId
                ? Authority.RejectAndAdvance(command.Sequence,
                    $"Peer {fromPeer} does not control empire {command.EmpireId}.")
                : Authority.Process(command);
        result.OriginPeer = fromPeer;

        LastResult = result;
        LastAuthoritySnapshot = snapshot;
        foreach (int peer in PeerIds)
        {
            if (peer == LocalPeerId)
                continue;
            Transport.Send(peer, command.ToMessage(fromPeer));
            Transport.Send(peer, result.ToMessage(HostPeerId));
            Transport.Send(peer, snapshot.ToMessage(HostPeerId));
        }

        foreach (AuthoritativeDiplomacyPopup popup in Authority.DrainDiplomacyPopups())
        {
            if (!PeerByEmpire.TryGetValue(popup.TargetEmpireId, out int targetPeer))
                continue;
            if (targetPeer == LocalPeerId)
                LocalPopups.Add(popup);
            else
                Transport.Send(targetPeer, popup.ToMessage(HostPeerId));
        }
    }
}

/// <summary>
/// TCP-backed passive authoritative 4X client replica. UI code submits commands through this
/// object; it never mutates locally except when a host-accepted result and snapshot arrive.
/// </summary>
public sealed class Authoritative4XNetworkClient : IDisposable
{
    readonly TcpLockstepTransport Transport;
    readonly Authoritative4XClientReplica Replica;
    readonly Dictionary<AuthoritativeCommandKey, AuthoritativePlayerCommand> Pending = new();
    readonly List<AuthoritativeDiplomacyPopup> Popups = new();

    public int PeerId { get; }
    public AuthoritativeCommandResult LastResult { get; private set; }
    public AuthoritativeStateSnapshot LastAuthoritySnapshot { get; private set; }
    public AuthoritativeStateSnapshot LastClientSnapshot => Replica.LastSnapshot;
    public string LastError => Transport.LastError;

    public Authoritative4XNetworkClient(UniverseScreen clientUniverse, TcpLockstepTransport transport,
        int peerId, int[] humanEmpireIds = null)
    {
        PeerId = peerId;
        Transport = transport;
        if (humanEmpireIds != null)
            AuthoritativeHumanPlayers.SetHumanControlledEmpires(clientUniverse.UState, humanEmpireIds);
        Replica = new Authoritative4XClientReplica(clientUniverse, humanEmpireIds: humanEmpireIds);
        Transport.Register(peerId, OnClientMessage);
    }

    public void Submit(AuthoritativePlayerCommand command)
    {
        Transport.Send(Authoritative4XNetworkHost.HostPeerId, command.ToMessage(PeerId));
    }

    public void Poll() => Transport.Poll();
    public AuthoritativeDiplomacyPopup[] PopupsForClient() => Popups.ToArray();
    public AuthoritativeDiplomacyPopup[] DrainPopupsForClient()
    {
        AuthoritativeDiplomacyPopup[] popups = Popups.ToArray();
        Popups.Clear();
        return popups;
    }
    public void Dispose() => Transport.Dispose();

    void OnClientMessage(LockstepMessage message)
    {
        switch (message)
        {
            case AuthoritativeCommandRequestMessage requestMessage:
                Pending[new AuthoritativeCommandKey(requestMessage.FromPeer, requestMessage.Sequence)] =
                    AuthoritativePlayerCommand.FromMessage(requestMessage);
                break;
            case AuthoritativeCommandResultMessage resultMessage:
                LastResult = new AuthoritativeCommandResult
                {
                    Sequence = resultMessage.Sequence,
                    OriginPeer = resultMessage.OriginPeer,
                    Accepted = resultMessage.Accepted,
                    Tick = resultMessage.Tick,
                    Reason = resultMessage.Reason ?? "",
                };
                break;
            case AuthoritativeStateSnapshotMessage snapshotMessage:
                LastAuthoritySnapshot = AuthoritativeStateSnapshot.FromMessage(snapshotMessage);
                if (LastResult == null
                    || !Pending.TryGetValue(new AuthoritativeCommandKey(LastResult.OriginPeer, LastResult.Sequence),
                        out AuthoritativePlayerCommand command))
                    throw new InvalidOperationException("Received authoritative snapshot without a matching command result.");
                Pending.Remove(new AuthoritativeCommandKey(LastResult.OriginPeer, LastResult.Sequence));
                Replica.ApplyAuthoritativeResult(command, LastResult, LastAuthoritySnapshot);
                break;
            case AuthoritativeDiplomacyPopupMessage popupMessage:
                Popups.Add(AuthoritativeDiplomacyPopup.FromMessage(popupMessage));
                break;
        }
    }
}

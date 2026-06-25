using System.Globalization;
using System.Linq;
using System.Text;
using SDLockstep;
using SDUtils;
using SDUtils.Deterministic;
using Ship_Game.AI;
using Ship_Game.Determinism;
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

    public uint Tick { get; private set; }

    public Authoritative4XAuthority(UniverseScreen universe, float dt = 1f / 60f)
    {
        Universe = universe;
        Step = new FixedSimTime(dt);
        Applicator = new Authoritative4XCommandApplicator(universe.UState);
    }

    public (AuthoritativeCommandResult result, AuthoritativeStateSnapshot snapshot)
        Process(AuthoritativePlayerCommand command)
    {
        AuthoritativeCommandResult result = Applicator.Apply(command, Tick + 1);
        Universe.SingleSimulationStep(Step);
        Tick++;
        return (result, AuthoritativeStateSnapshot.Capture(Universe, Tick));
    }
}

public sealed class Authoritative4XClientReplica
{
    readonly UniverseScreen Universe;
    readonly FixedSimTime Step;
    readonly Authoritative4XCommandApplicator Applicator;

    public uint Tick { get; private set; }
    public AuthoritativeStateSnapshot LastSnapshot { get; private set; }

    public Authoritative4XClientReplica(UniverseScreen universe, float dt = 1f / 60f)
    {
        Universe = universe;
        Step = new FixedSimTime(dt);
        Applicator = new Authoritative4XCommandApplicator(universe.UState);
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

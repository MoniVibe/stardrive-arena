using System.IO;
using SDUtils.Deterministic;

namespace SDLockstep;

/// <summary>
/// A tiny deterministic <see cref="ILockstepSimulation"/> for the headless lockstep proof: N ships
/// steered by Move/Attack/Stop commands and integrated with fixed-point physics. The state hash is the
/// ships' positions/headings, so a missed or mis-ordered command makes clients DIVERGE — which is what
/// makes the lockstep-delivery test meaningful (it would catch a broken transport/ordering).
///
/// This is also the reference shape for StarDrive's real adapter: implement Apply() over UniverseState
/// (process the command frame, then SingleSimulationStep) and Hash() over ComputeAuthoritativeStateHash.
/// </summary>
public sealed class ToySimulation : ISnapshotableSimulation
{
    struct Ship
    {
        public FixVector2 Pos;
        public FixVector2 Vel;
        public Fixed64 Heading;
        public bool Thrusting;
    }

    readonly Ship[] Ships;
    readonly Fixed64 Dt = Fixed64.FromDouble(1.0 / 60.0);
    readonly Fixed64 Accel = Fixed64.FromInt(40);
    readonly Fixed64 MaxVel = Fixed64.FromInt(150);
    uint TickField;

    public uint Tick => TickField;

    public ToySimulation(ulong seed, int shipCount)
    {
        Ships = new Ship[shipCount];
        var rng = new DetRandom(seed);
        for (int i = 0; i < shipCount; ++i)
        {
            Ships[i].Pos = new FixVector2(Fixed64.FromInt(rng.NextInt(-500, 500)),
                                          Fixed64.FromInt(rng.NextInt(-500, 500)));
            Ships[i].Vel = FixVector2.Zero;
            Ships[i].Heading = Fixed64.FromRaw((long)(rng.NextULong() % (ulong)Fixed64.TwoPiRaw));
            Ships[i].Thrusting = false;
        }
    }

    public void Apply(CommandFrame frame)
    {
        // 1) process commands in canonical order
        for (int i = 0; i < frame.Commands.Count; ++i)
        {
            SimCommand c = frame.Commands[i];
            if (c.SubjectId < 0 || c.SubjectId >= Ships.Length) continue;
            switch (c.Kind)
            {
                case SimCommandKind.MoveShip:
                    FixVector2 target = new(Fixed64.FromRaw(c.PosXRaw), Fixed64.FromRaw(c.PosYRaw));
                    FixVector2 d = target - Ships[c.SubjectId].Pos;
                    Ships[c.SubjectId].Heading = Fixed64.Atan2(d.Y, d.X);
                    Ships[c.SubjectId].Thrusting = true;
                    break;
                case SimCommandKind.AttackTarget:
                    Ships[c.SubjectId].Thrusting = true;
                    break;
                case SimCommandKind.StopShip:
                    Ships[c.SubjectId].Thrusting = false;
                    break;
            }
        }

        // 2) integrate all ships one fixed tick
        for (int i = 0; i < Ships.Length; ++i)
        {
            Fixed64 acc = Ships[i].Thrusting ? Accel : Fixed64.Zero;
            var body = new DetPhysics2D.Body(Ships[i].Pos, Ships[i].Vel);
            body = DetPhysics2D.Step(body, Ships[i].Heading, acc, MaxVel, Dt);
            Ships[i].Pos = body.Position;
            Ships[i].Vel = body.Velocity;
        }

        TickField++;
    }

    public byte[] SaveState()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(TickField);
        w.Write(Ships.Length);
        for (int i = 0; i < Ships.Length; ++i)
        {
            w.Write(Ships[i].Pos.X.Raw);
            w.Write(Ships[i].Pos.Y.Raw);
            w.Write(Ships[i].Vel.X.Raw);
            w.Write(Ships[i].Vel.Y.Raw);
            w.Write(Ships[i].Heading.Raw);
            w.Write(Ships[i].Thrusting);
        }
        return ms.ToArray();
    }

    public void LoadState(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var r = new BinaryReader(ms);
        TickField = r.ReadUInt32();
        int n = r.ReadInt32();
        for (int i = 0; i < n && i < Ships.Length; ++i)
        {
            Ships[i].Pos = new FixVector2(Fixed64.FromRaw(r.ReadInt64()), Fixed64.FromRaw(r.ReadInt64()));
            Ships[i].Vel = new FixVector2(Fixed64.FromRaw(r.ReadInt64()), Fixed64.FromRaw(r.ReadInt64()));
            Ships[i].Heading = Fixed64.FromRaw(r.ReadInt64());
            Ships[i].Thrusting = r.ReadBoolean();
        }
    }

    public (ulong lo, ulong hi) Hash()
    {
        var c = new Hash128Checksum();
        c.WriteUInt(TickField);
        for (int i = 0; i < Ships.Length; ++i)
        {
            c.WriteLong(Ships[i].Pos.X.Raw);
            c.WriteLong(Ships[i].Pos.Y.Raw);
            c.WriteLong(Ships[i].Vel.X.Raw);
            c.WriteLong(Ships[i].Vel.Y.Raw);
            c.WriteLong(Ships[i].Heading.Raw);
        }
        return c.Finish128();
    }
}

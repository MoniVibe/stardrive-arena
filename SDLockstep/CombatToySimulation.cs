using SDUtils.Deterministic;

namespace SDLockstep;

/// <summary>
/// VS9 fixed-point-combat reference simulation: a richer deterministic <see cref="ILockstepSimulation"/>
/// than <see cref="ToySimulation"/>. It de-risks combat for the StarDrive lockstep effort by exercising
/// the parts a real battle adapter needs — target acquisition, ranged weapon fire, cooldown/reload, and
/// death — entirely in integer/fixed-point math so every client computes a bit-identical trajectory.
///
/// Determinism rules honored here (no exceptions):
///   * ALL combat math is <see cref="Fixed64"/> (range, damage, health, cooldown). No float / no
///     System.Random / no DateTime / no Math.* transcendentals.
///   * Initial placement and headings come only from <see cref="DetRandom"/> seeded in the ctor.
///   * Range checks use SQUARED distance compared against a squared range constant, so we avoid Sqrt
///     and the values stay small enough to never overflow the Q32.32 range (see WeaponRange note).
///
/// This is the shape StarDrive's real adapter would mirror: Apply() = (process the canonical command
/// frame) then (advance exactly one fixed tick of movement + combat); Hash() = checksum of the
/// authoritative per-entity state. A dropped/mis-ordered command diverges the hash, which is what makes
/// the lockstep-delivery test meaningful.
/// </summary>
public sealed class CombatToySimulation : ILockstepSimulation
{
    struct Ship
    {
        public FixVector2 Position;
        public FixVector2 Velocity;
        public Fixed64 Heading;       // radians; thrust direction derived by DetPhysics2D
        public Fixed64 Health;        // starts at 100; ship is dead at <= 0
        public bool Thrusting;
        public int AttackTargetId;    // -1 = no target
        public int WeaponCooldown;    // ticks remaining until the weapon can fire again
    }

    readonly Ship[] Ships;

    // ---- Fixed simulation constants (all Fixed64 / integer; never tweaked at runtime) ----
    readonly Fixed64 Dt = Fixed64.FromDouble(1.0 / 60.0); // one fixed tick = 1/60 s
    readonly Fixed64 ThrustAccel = Fixed64.FromInt(40);   // forward thrust acceleration
    readonly Fixed64 MaxVelocity = Fixed64.FromInt(150);  // speed clamp (units/s)

    // ---- Fixed combat constants (reported in the file header / final report) ----
    static readonly Fixed64 StartHealth = Fixed64.FromInt(100);
    static readonly Fixed64 WeaponDamage = Fixed64.FromInt(7);   // health removed per shot
    const int WeaponReloadTicks = 30;                            // 0.5 s between shots at 60 Hz
    // WeaponRange = 200 units. We compare SQUARED distance against this squared constant so no Sqrt is
    // needed. 200^2 = 40000 (Fixed64.FromInt(40000)); and even worst-case separations between ships
    // placed in [-800, 800] give a squared distance well under the Q32.32 ceiling, so no overflow.
    static readonly Fixed64 WeaponRangeSquared = Fixed64.FromInt(200 * 200);

    uint TickField;
    public uint Tick => TickField;

    /// <summary>Deterministic initial placement/headings/health from the seed. shipCount ships.</summary>
    public CombatToySimulation(ulong seed, int shipCount)
    {
        Ships = new Ship[shipCount];
        var rng = new DetRandom(seed);
        for (int i = 0; i < shipCount; ++i)
        {
            // Positions kept within +/- 800 units so all fixed-point products stay far from overflow.
            Ships[i].Position = new FixVector2(Fixed64.FromInt(rng.NextInt(-800, 800)),
                                               Fixed64.FromInt(rng.NextInt(-800, 800)));
            Ships[i].Velocity = FixVector2.Zero;
            Ships[i].Heading = Fixed64.FromRaw((long)(rng.NextULong() % (ulong)Fixed64.TwoPiRaw));
            Ships[i].Health = StartHealth;
            Ships[i].Thrusting = false;
            Ships[i].AttackTargetId = -1;
            Ships[i].WeaponCooldown = 0;
        }
    }

    public void Apply(CommandFrame frame)
    {
        // 1) Process commands in the given (already canonical) order.
        for (int i = 0; i < frame.Commands.Count; ++i)
        {
            SimCommand c = frame.Commands[i];
            if (c.SubjectId < 0 || c.SubjectId >= Ships.Length) continue;
            ref Ship s = ref Ships[c.SubjectId];
            if (s.Health <= Fixed64.Zero) continue; // dead ships ignore commands

            switch (c.Kind)
            {
                case SimCommandKind.MoveShip:
                    // Aim the heading at the destination and thrust; movement clears any attack order.
                    FixVector2 dest = new(Fixed64.FromRaw(c.PosXRaw), Fixed64.FromRaw(c.PosYRaw));
                    FixVector2 toDest = dest - s.Position;
                    s.Heading = Fixed64.Atan2(toDest.Y, toDest.X);
                    s.Thrusting = true;
                    s.AttackTargetId = -1;
                    break;

                case SimCommandKind.AttackTarget:
                    // Latch the target and thrust toward it (heading is refreshed each tick below).
                    s.AttackTargetId = c.TargetId;
                    s.Thrusting = true;
                    break;

                case SimCommandKind.StopShip:
                    s.Thrusting = false;
                    s.AttackTargetId = -1;
                    break;
            }
        }

        // 2) Integrate one fixed tick: steer toward target, move, then resolve weapon fire.
        for (int i = 0; i < Ships.Length; ++i)
        {
            ref Ship s = ref Ships[i];
            if (s.Health <= Fixed64.Zero)
            {
                // Dead: stop dead in place, deal no damage, hold no target.
                s.Velocity = FixVector2.Zero;
                s.Thrusting = false;
                s.AttackTargetId = -1;
                continue;
            }

            // If pursuing a live target, re-point the heading at it each tick.
            if (s.AttackTargetId >= 0 && s.AttackTargetId < Ships.Length)
            {
                ref Ship tgt = ref Ships[s.AttackTargetId];
                if (tgt.Health > Fixed64.Zero)
                {
                    FixVector2 toTarget = tgt.Position - s.Position;
                    s.Heading = Fixed64.Atan2(toTarget.Y, toTarget.X);
                    s.Thrusting = true;
                }
                else
                {
                    s.AttackTargetId = -1; // target died; drop the order
                }
            }

            // Move under fixed-point physics.
            Fixed64 accel = s.Thrusting ? ThrustAccel : Fixed64.Zero;
            var body = new DetPhysics2D.Body(s.Position, s.Velocity);
            body = DetPhysics2D.Step(body, s.Heading, accel, MaxVelocity, Dt);
            s.Position = body.Position;
            s.Velocity = body.Velocity;
        }

        // 3) Resolve weapon fire AFTER movement, in ship index order, so the result is deterministic.
        for (int i = 0; i < Ships.Length; ++i)
        {
            ref Ship s = ref Ships[i];
            if (s.Health <= Fixed64.Zero) continue;

            bool fired = false;
            if (s.WeaponCooldown == 0 && s.AttackTargetId >= 0 && s.AttackTargetId < Ships.Length)
            {
                ref Ship tgt = ref Ships[s.AttackTargetId];
                if (tgt.Health > Fixed64.Zero)
                {
                    // Squared-distance compare keeps us in-range-safe and avoids Sqrt entirely.
                    FixVector2 sep = tgt.Position - s.Position;
                    Fixed64 distSq = sep.LengthSquared();
                    if (distSq <= WeaponRangeSquared)
                    {
                        tgt.Health = tgt.Health - WeaponDamage; // may drop <= 0 (death handled next tick)
                        s.WeaponCooldown = WeaponReloadTicks;
                        fired = true;
                    }
                }
            }

            // Decrement cooldown toward 0 on any tick we did not fire.
            if (!fired && s.WeaponCooldown > 0)
                s.WeaponCooldown--;
        }

        TickField++;
    }

    /// <summary>
    /// 128-bit checksum over every ship in index order, then the tick. Hashing Velocity, Health,
    /// AttackTargetId, and WeaponCooldown (not just position/heading) means a divergence in any combat
    /// sub-state is caught at the tick it first appears.
    /// </summary>
    public (ulong lo, ulong hi) Hash()
    {
        var c = new Hash128Checksum();
        for (int i = 0; i < Ships.Length; ++i)
        {
            ref Ship s = ref Ships[i];
            c.WriteLong(s.Position.X.Raw);
            c.WriteLong(s.Position.Y.Raw);
            c.WriteLong(s.Velocity.X.Raw);
            c.WriteLong(s.Velocity.Y.Raw);
            c.WriteLong(s.Heading.Raw);
            c.WriteLong(s.Health.Raw);
            c.WriteInt(s.AttackTargetId);
            c.WriteInt(s.WeaponCooldown);
        }
        c.WriteUInt(TickField);
        return c.Finish128();
    }
}

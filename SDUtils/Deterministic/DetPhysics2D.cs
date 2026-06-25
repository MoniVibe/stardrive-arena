namespace SDUtils.Deterministic;

/// <summary>
/// Deterministic 2D point-mass integration in fixed point, mirroring StarDrive's
/// <c>PhysicsObject</c> forward-thrust model:
///   thrustDir = (sin(rot), -cos(rot));  v += thrustDir*accel*dt;  clamp |v| &lt;= vMax;  p += v*dt.
/// Integer-only =&gt; bit-identical on every CPU/JIT. This is the shape the sim's per-tick motion
/// math migrates onto for Tier-C cross-machine determinism (advisor plan §M6/§M11).
/// </summary>
public static class DetPhysics2D
{
    public readonly struct Body
    {
        public readonly FixVector2 Position;
        public readonly FixVector2 Velocity;

        public Body(FixVector2 position, FixVector2 velocity)
        {
            Position = position;
            Velocity = velocity;
        }
    }

    /// <summary>Advance one fixed timestep under forward thrust at the given rotation.</summary>
    public static Body Step(Body b, Fixed64 rotation, Fixed64 thrustAccel, Fixed64 maxVelocity, Fixed64 dt)
    {
        // matches PhysicsObject.GetThrustAcceleration: thrustDir.X = sin(rot), thrustDir.Y = -cos(rot)
        (Fixed64 cos, Fixed64 sin) = Fixed64.CosSin(rotation);
        FixVector2 thrustDir = new(sin, -cos);

        FixVector2 vel = b.Velocity + (thrustDir * thrustAccel) * dt;

        Fixed64 speed = vel.Length();
        if (speed > maxVelocity && speed > Fixed64.Zero)
            vel = vel * (maxVelocity / speed);

        FixVector2 pos = b.Position + vel * dt;
        return new Body(pos, vel);
    }
}

using System;

namespace SDUtils.Deterministic;

/// <summary>
/// 2D vector in Q32.32 fixed point. All arithmetic is integer-only via <see cref="Fixed64"/>, so
/// results are bit-identical on every machine — the Tier-C replacement for the sim's float Vector2.
/// </summary>
public readonly struct FixVector2 : IEquatable<FixVector2>
{
    public readonly Fixed64 X;
    public readonly Fixed64 Y;

    public FixVector2(Fixed64 x, Fixed64 y)
    {
        X = x;
        Y = y;
    }

    public static readonly FixVector2 Zero = new(Fixed64.Zero, Fixed64.Zero);

    public static FixVector2 FromFloat(float x, float y) => new(Fixed64.FromFloat(x), Fixed64.FromFloat(y));

    public static FixVector2 operator +(FixVector2 a, FixVector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static FixVector2 operator -(FixVector2 a, FixVector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static FixVector2 operator *(FixVector2 v, Fixed64 s) => new(v.X * s, v.Y * s);

    public Fixed64 LengthSquared() => X * X + Y * Y;
    public Fixed64 Length() => Fixed64.Sqrt(X * X + Y * Y);

    public bool Equals(FixVector2 o) => X.Equals(o.X) && Y.Equals(o.Y);
    public override bool Equals(object obj) => obj is FixVector2 o && Equals(o);
    public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
    public override string ToString() => $"({X}, {Y})";
}

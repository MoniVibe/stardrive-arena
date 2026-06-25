using System;

namespace SDUtils.Deterministic;

/// <summary>
/// Q32.32 fixed-point number: 64-bit, 32 integer bits + 32 fraction bits, stored as a raw long.
///
/// Why this exists: lockstep multiplayer requires bit-identical math on every client, but IEEE754
/// float results are NOT guaranteed identical across CPUs/compilers (FMA contraction, x87 vs SSE,
/// differing transcendental implementations). Fixed-point integer math is identical everywhere.
/// This type is the kernel the simulation's physics/geometry would migrate onto for cross-platform
/// determinism (Tier C).
///
/// Conversions to/from float exist for authoring and rendering boundaries only; simulation math
/// must stay in Fixed64. Multiply/divide use a 128-bit intermediate so no precision is lost.
/// Sqrt is an exact integer bitwise root. Sin/Cos/Atan2 use integer CORDIC with baked angle
/// constants, so they are portable and deterministic (approximate to a fixed precision).
/// </summary>
public readonly struct Fixed64 : IEquatable<Fixed64>, IComparable<Fixed64>
{
    public const int FractionBits = 32;
    public const long OneRaw = 1L << FractionBits;

    public readonly long Raw;

    Fixed64(long raw) => Raw = raw;

    public static Fixed64 FromRaw(long raw) => new(raw);
    public static Fixed64 FromInt(int value) => new((long)value << FractionBits);
    public static Fixed64 FromFloat(float value) => new((long)MathF.Round(value * OneRaw));
    public static Fixed64 FromDouble(double value) => new((long)Math.Round(value * OneRaw));

    public static readonly Fixed64 Zero = new(0);
    public static readonly Fixed64 One = new(OneRaw);
    public static readonly Fixed64 Half = new(OneRaw >> 1);

    public float ToFloat() => Raw / (float)OneRaw;
    public double ToDouble() => Raw / (double)OneRaw;
    public int ToInt() => (int)(Raw >> FractionBits); // floor toward negative infinity

    public static Fixed64 operator +(Fixed64 a, Fixed64 b) => new(a.Raw + b.Raw);
    public static Fixed64 operator -(Fixed64 a, Fixed64 b) => new(a.Raw - b.Raw);
    public static Fixed64 operator -(Fixed64 a) => new(-a.Raw);

    public static Fixed64 operator *(Fixed64 a, Fixed64 b)
        => new((long)(((Int128)a.Raw * b.Raw) >> FractionBits));

    public static Fixed64 operator /(Fixed64 a, Fixed64 b)
        => new((long)(((Int128)a.Raw << FractionBits) / b.Raw));

    public static bool operator <(Fixed64 a, Fixed64 b) => a.Raw < b.Raw;
    public static bool operator >(Fixed64 a, Fixed64 b) => a.Raw > b.Raw;
    public static bool operator <=(Fixed64 a, Fixed64 b) => a.Raw <= b.Raw;
    public static bool operator >=(Fixed64 a, Fixed64 b) => a.Raw >= b.Raw;
    public static bool operator ==(Fixed64 a, Fixed64 b) => a.Raw == b.Raw;
    public static bool operator !=(Fixed64 a, Fixed64 b) => a.Raw != b.Raw;

    public static Fixed64 Abs(Fixed64 a) => new(a.Raw < 0 ? -a.Raw : a.Raw);
    public static Fixed64 Min(Fixed64 a, Fixed64 b) => a.Raw <= b.Raw ? a : b;
    public static Fixed64 Max(Fixed64 a, Fixed64 b) => a.Raw >= b.Raw ? a : b;

    /// <summary>Deterministic fixed-point square root via integer (UInt128) bitwise root. Negative input returns 0.</summary>
    public static Fixed64 Sqrt(Fixed64 a)
    {
        if (a.Raw <= 0) return Zero;
        // result.Raw = isqrt(a.Raw << 32), computed in 128-bit to avoid overflow
        UInt128 n = (UInt128)(ulong)a.Raw << FractionBits;
        UInt128 res = UInt128.Zero;
        UInt128 bit = (UInt128)1 << 126;
        while (bit > n) bit >>= 2;
        while (bit != UInt128.Zero)
        {
            if (n >= res + bit)
            {
                n -= res + bit;
                res = (res >> 1) + bit;
            }
            else
            {
                res >>= 1;
            }
            bit >>= 2;
        }
        return new((long)res);
    }

    // ---- Trigonometry (integer CORDIC, baked constants => portable) ----

    public const long PiRaw = 13493037705L;       // round(pi   * 2^32)
    public const long TwoPiRaw = 26986075410L;    // round(2pi  * 2^32)
    public const long HalfPiRaw = 6746518852L;    // floor(pi/2 * 2^32)
    const long CordicGain = 2608131496L;          // round(0.60725293500888 * 2^32)
    const int CordicIterations = 29;

    // atan(2^-i) in Q32.32, i = 0..28
    static readonly long[] AtanTable =
    {
        3373259426L, 1991351318L, 1052175346L, 534100635L, 268086748L,
        134174063L, 67103403L, 33553749L, 16777131L, 8388597L,
        4194302L, 2097151L, 1048576L, 524288L, 262144L,
        131072L, 65536L, 32768L, 16384L, 8192L,
        4096L, 2048L, 1024L, 512L, 256L,
        128L, 64L, 32L, 16L,
    };

    /// <summary>Returns (cos, sin) of the angle (radians) using integer CORDIC rotation.</summary>
    public static (Fixed64 cos, Fixed64 sin) CosSin(Fixed64 radians)
    {
        // Reduce to quadrant-relative angle z' in [-pi/4, pi/4], tracking quadrant k (mod 4).
        long k = DivRound(radians.Raw, HalfPiRaw);
        long zRaw = radians.Raw - k * HalfPiRaw;
        int q = (int)(((k % 4) + 4) % 4);

        long x = CordicGain;
        long y = 0;
        long z = zRaw;
        for (int i = 0; i < CordicIterations; ++i)
        {
            long dx = x >> i;
            long dy = y >> i;
            if (z >= 0) { x -= dy; y += dx; z -= AtanTable[i]; }
            else        { x += dy; y -= dx; z += AtanTable[i]; }
        }

        // Base cos=x, sin=y for z' near 0; rotate by quadrant.
        return q switch
        {
            0 => (new(x), new(y)),
            1 => (new(-y), new(x)),
            2 => (new(-x), new(-y)),
            _ => (new(y), new(-x)),
        };
    }

    public static Fixed64 Sin(Fixed64 radians) => CosSin(radians).sin;
    public static Fixed64 Cos(Fixed64 radians) => CosSin(radians).cos;

    /// <summary>atan2(y, x) in radians via integer CORDIC vectoring. Returns 0 for (0,0).</summary>
    public static Fixed64 Atan2(Fixed64 y, Fixed64 x)
    {
        if (x.Raw == 0 && y.Raw == 0) return Zero;

        long xr = x.Raw, yr = y.Raw, z = 0;
        // CORDIC vectoring converges for x>0. Fold the left half-plane in by rotating pi,
        // then add +pi (original y>=0) or -pi (original y<0) back at the end.
        long addend = 0;
        if (xr < 0)
        {
            xr = -xr; yr = -yr;
            addend = y.Raw >= 0 ? PiRaw : -PiRaw;
        }

        for (int i = 0; i < CordicIterations; ++i)
        {
            long dx = xr >> i;
            long dy = yr >> i;
            if (yr > 0) { xr += dy; yr -= dx; z += AtanTable[i]; }
            else        { xr -= dy; yr += dx; z -= AtanTable[i]; }
        }
        return new(z + addend);
    }

    static long DivRound(long a, long b)
    {
        long h = b / 2;
        return a >= 0 ? (a + h) / b : (a - h) / b;
    }

    public bool Equals(Fixed64 other) => Raw == other.Raw;
    public override bool Equals(object obj) => obj is Fixed64 f && f.Raw == Raw;
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(Fixed64 other) => Raw.CompareTo(other.Raw);
    public override string ToString() => ToDouble().ToString("0.######");
}

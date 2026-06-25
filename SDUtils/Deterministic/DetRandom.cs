using System;
using System.Runtime.CompilerServices;

namespace SDUtils.Deterministic;

/// <summary>
/// Deterministic, seekable, cross-platform pseudo-random generator.
///
/// Properties required for lockstep multiplayer:
///  - Integer-only internal state (no float state) =&gt; bit-identical on any IEEE754 platform.
///  - Single 64-bit state =&gt; save/restore is exact (see <see cref="State"/>).
///  - Stream forking =&gt; independent sub-streams per entity, so parallel per-entity
///    consumption is order-independent: <see cref="Fork"/> derives a new generator
///    deterministically from (state, streamId).
///
/// Algorithm: SplitMix64 (Steele, Lea, Flood 2014). Fast, well-distributed, integer-only.
/// This is a mutable struct: hold it in a field and call its methods in place. Do NOT copy
/// it into a local and draw from the copy, or the owner's stream will not advance.
/// </summary>
public struct DetRandom
{
    const ulong Gamma = 0x9E3779B97F4A7C15UL;

    ulong StateField;

    /// <summary>Raw 64-bit state. Persist this for exact save/restore across save files and the network.</summary>
    public ulong State
    {
        readonly get => StateField;
        set => StateField = value;
    }

    /// <summary>Seed the generator. A zero seed is remapped so it never produces a degenerate stream.</summary>
    public DetRandom(ulong seed)
    {
        StateField = Mix64(seed == 0 ? Gamma : seed);
    }

    DetRandom(ulong state, ulong stream, bool _)
    {
        StateField = Mix64(state ^ Mix64(stream + Gamma));
    }

    /// <summary>
    /// Derive an independent deterministic sub-stream. Use one fork per entity so that
    /// parallel per-entity updates draw from disjoint streams and the result never
    /// depends on thread interleaving.
    /// </summary>
    public readonly DetRandom Fork(ulong streamId) => new(StateField, streamId, false);

    /// <summary>Advance state and return the next 64-bit value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong NextULong()
    {
        unchecked
        {
            StateField += Gamma;
            ulong z = StateField;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint NextUInt() => (uint)(NextULong() >> 32);

    /// <summary>Stateless 64-bit finalizer mix. Used for stream derivation and hashing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Mix64(ulong x)
    {
        unchecked
        {
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }

    /// <summary>
    /// Float in [0,1). Built from the top 24 bits via a single exact multiply by 2^-24.
    /// Both operands are exactly representable, so the result is deterministic on any
    /// IEEE754 round-to-nearest platform (no transcendental, no FMA-sensitive expression).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextFloat()
    {
        uint bits24 = (uint)(NextULong() >> 40); // [0, 2^24)
        return bits24 * (1.0f / 16777216.0f);
    }

    /// <summary>Float in [min, max).</summary>
    public float NextFloat(float min, float max) => min + NextFloat() * (max - min);

    /// <summary>Double in [0,1) from the top 53 bits via a single exact multiply by 2^-53.</summary>
    public double NextDouble()
    {
        ulong bits53 = NextULong() >> 11; // [0, 2^53)
        return bits53 * (1.0 / 9007199254740992.0);
    }

    /// <summary>
    /// Unbiased integer in [min, max) using Lemire's multiply-shift with rejection.
    /// Integer-only =&gt; deterministic. Returns min when the range is empty.
    /// </summary>
    public int NextInt(int min, int max)
    {
        if (max <= min) return min;
        uint range = (uint)((long)max - min);
        ulong m = (ulong)NextUInt() * range;
        uint low = (uint)m;
        if (low < range)
        {
            uint threshold = (0u - range) % range; // 2^32 mod range
            while (low < threshold)
            {
                m = (ulong)NextUInt() * range;
                low = (uint)m;
            }
        }
        return min + (int)(m >> 32);
    }

    /// <summary>Inclusive [min, max].</summary>
    public int NextIntInclusive(int min, int max)
        => NextInt(min, max == int.MaxValue ? max : max + 1);

    public bool NextBool() => (NextULong() & 1) != 0;

    /// <summary>Dice roll: TRUE with the given percent chance [0..100].</summary>
    public bool RollDice(float percent) => NextFloat(0f, 100f) < percent;
}

using System;
using System.Runtime.CompilerServices;

namespace SDUtils.Deterministic;

/// <summary>
/// Deterministic incremental 64-bit hash (FNV-1a) for simulation-state checksums.
///
/// This is the "measuring stick" for determinism: hash the canonical simulation state
/// every tick (or every N ticks) and compare the trajectory across machines and replays.
/// Two clients that have stayed in lockstep produce identical hash trajectories; the first
/// tick whose hashes diverge localizes the desync.
///
/// All inputs are reduced to raw integer bits before mixing, so floats are hashed by their
/// IEEE754 bit pattern (<see cref="AddFloat"/>) with no float arithmetic =&gt; fully portable.
/// </summary>
public struct DetHash
{
    public const ulong FnvOffset = 14695981039346656037UL;
    const ulong FnvPrime = 1099511628211UL;

    /// <summary>Accumulated hash. Read after mixing all state for the final checksum.</summary>
    public ulong Value;

    /// <summary>A fresh hash primed with the FNV offset basis.</summary>
    public static DetHash New() => new() { Value = FnvOffset };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddByte(byte b)
    {
        unchecked { Value = (Value ^ b) * FnvPrime; }
    }

    public void AddUInt(uint v)
    {
        AddByte((byte)v);
        AddByte((byte)(v >> 8));
        AddByte((byte)(v >> 16));
        AddByte((byte)(v >> 24));
    }

    public void AddInt(int v) => AddUInt((uint)v);

    public void AddULong(ulong v)
    {
        AddUInt((uint)v);
        AddUInt((uint)(v >> 32));
    }

    public void AddLong(long v) => AddULong((ulong)v);

    public void AddBool(bool b) => AddByte(b ? (byte)1 : (byte)0);

    /// <summary>Hash a float by its exact bit pattern (no float math =&gt; portable).</summary>
    public void AddFloat(float f) => AddUInt(BitConverter.SingleToUInt32Bits(f));

    /// <summary>Hash a double by its exact bit pattern.</summary>
    public void AddDouble(double d) => AddULong(BitConverter.DoubleToUInt64Bits(d));

    /// <summary>Hash a string's UTF-16 code units in order.</summary>
    public void AddString(string s)
    {
        if (s == null) { AddInt(-1); return; }
        AddInt(s.Length);
        for (int i = 0; i < s.Length; ++i)
            AddUInt(s[i]);
    }
}

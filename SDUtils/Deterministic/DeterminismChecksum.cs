using System;

namespace SDUtils.Deterministic;

/// <summary>
/// Versioned deterministic checksum surface (advisor plan, Rework 1). Every checksum carries an
/// <see cref="AlgorithmId"/> so logs and the multiplayer protocol never silently bake in a hash —
/// changing the algorithm is an explicit, detectable version bump. <see cref="Lane"/> is a hint that
/// lane-aware debug writers use to localize divergence; single-accumulator checksums ignore it.
///
/// Numeric writes are profile-aware (Rework 2): for Tier-A the sim is float-backed and
/// <see cref="FloatRaw"/> hashes the exact IEEE-754 bits; for Tier-C authoritative state is
/// fixed-point and <see cref="Fixed64Raw"/> hashes the integer representation.
/// </summary>
public interface IDeterminismChecksum
{
    string AlgorithmId { get; }

    void Lane(int laneIndex);

    void WriteByte(byte b);
    void WriteUInt(uint v);
    void WriteInt(int v);
    void WriteULong(ulong v);
    void WriteLong(long v);
    void WriteBool(bool b);
    void FloatRaw(float f);
    void WriteDouble(double d);
    void Fixed64Raw(long fixedRaw);
    void WriteString(string s);

    ulong Finish64();
    (ulong lo, ulong hi) Finish128();
}

/// <summary>Composite writes implemented in terms of <see cref="WriteByte"/>, so each algorithm only
/// supplies byte mixing + finalization. Lane defaults to a no-op (single-accumulator checksums).</summary>
public abstract class DeterminismChecksumBase : IDeterminismChecksum
{
    public abstract string AlgorithmId { get; }

    public virtual void Lane(int laneIndex) { }

    public abstract void WriteByte(byte b);

    public void WriteUInt(uint v)
    {
        WriteByte((byte)v);
        WriteByte((byte)(v >> 8));
        WriteByte((byte)(v >> 16));
        WriteByte((byte)(v >> 24));
    }

    public void WriteInt(int v) => WriteUInt((uint)v);

    public void WriteULong(ulong v)
    {
        WriteUInt((uint)v);
        WriteUInt((uint)(v >> 32));
    }

    public void WriteLong(long v) => WriteULong((ulong)v);
    public void WriteBool(bool b) => WriteByte(b ? (byte)1 : (byte)0);
    public void FloatRaw(float f) => WriteUInt(BitConverter.SingleToUInt32Bits(f));
    public void WriteDouble(double d) => WriteULong(BitConverter.DoubleToUInt64Bits(d));
    public void Fixed64Raw(long fixedRaw) => WriteLong(fixedRaw);

    public void WriteString(string s)
    {
        if (s == null) { WriteInt(-1); return; }
        WriteInt(s.Length);
        for (int i = 0; i < s.Length; ++i)
            WriteUInt(s[i]);
    }

    public abstract ulong Finish64();
    public abstract (ulong lo, ulong hi) Finish128();
}

/// <summary>FNV-1a 64-bit — fast diagnostic checksum for local determinism tests.</summary>
public sealed class Fnv1a64Checksum : DeterminismChecksumBase
{
    const ulong FnvOffset = 14695981039346656037UL;
    const ulong FnvPrime = 1099511628211UL;

    ulong _value = FnvOffset;

    public override string AlgorithmId => "Fnv1a64-v1";

    public override void WriteByte(byte b)
    {
        unchecked { _value = (_value ^ b) * FnvPrime; }
    }

    public override ulong Finish64() => _value;

    // A 128-bit view derived by an independent finalizer mix — adequate for low-rate desync checks
    // when paired with the 64-bit lane; use Hash128Checksum where collision margin matters most.
    public override (ulong lo, ulong hi) Finish128() => (_value, DetRandom.Mix64(_value ^ FnvPrime));
}

/// <summary>
/// Project-owned 128-bit non-cryptographic checksum for multiplayer desync detection (Rework 1).
/// Two decorrelated 64-bit lanes with distinct primes/rotations, cross-mixed and SplitMix64-finalized.
/// Integer-only =&gt; portable; the goal is collision margin + versioning, not cryptographic strength.
/// </summary>
public sealed class Hash128Checksum : DeterminismChecksumBase
{
    const ulong P1 = 0xFF51AFD7ED558CCDUL;
    const ulong P2 = 0xC4CEB9FE1A85EC53UL;

    ulong _h1 = 0x9E3779B97F4A7C15UL;
    ulong _h2 = 0xD1B54A32D192ED03UL;

    public override string AlgorithmId => "SdHash128-v1";

    public override void WriteByte(byte b)
    {
        unchecked
        {
            _h1 = Rotl((_h1 ^ b) * P1, 27);
            _h2 = Rotl((_h2 + b) * P2, 31);
        }
    }

    public override ulong Finish64() => Finish128().lo;

    public override (ulong lo, ulong hi) Finish128()
    {
        unchecked
        {
            ulong a = _h1 + _h2;
            ulong b = _h2 ^ _h1;
            return (DetRandom.Mix64(a), DetRandom.Mix64(b + P1));
        }
    }

    static ulong Rotl(ulong x, int r) => (x << r) | (x >> (64 - r));
}

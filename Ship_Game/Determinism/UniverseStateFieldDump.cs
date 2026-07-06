using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SDUtils.Deterministic;
using Ship_Game.Ships;
using Ship_Game.Universe;

namespace Ship_Game.Determinism;

/// <summary>
/// Field-level desync diagnostic (ARENA_DESYNC_INSTRUMENTATION_REPORT). Given a <see cref="UniverseState"/>,
/// produces a per-SHIP digest breakdown that mirrors — field-for-field, in the same canonical order — the
/// per-ship contribution of the authoritative wire checksum (<see cref="UniverseStateHash.WriteAuthoritative"/>),
/// plus a per-field breakdown of a chosen ship. This is PURE OBSERVATION: it folds into its OWN
/// <see cref="Hash128Checksum"/> instances and never mutates the sim or the wire checksum, so a flag-on run is
/// bit-identical to a flag-off run.
///
/// The point (per the report): after a live 2-machine desync, each peer dumps ITS OWN state for the diverging
/// turn and the turn before. Diffing the two machines' logs reveals (a) WHICH ship's digest diverged first, and
/// (b) WITHIN that ship, WHICH field(s) differ — so we can tell "many floats slightly off" (cross-machine FP
/// drift, the LOCKSTEP-across-different-CPUs hazard) from "one discrete value flipped" (an order/logic bug).
///
/// FIELD SET (must stay in lockstep with WriteAuthoritative's ship lane): Id, Position.X, Position.Y,
/// Velocity.X, Velocity.Y, Rotation, Health. Fields the wire checksum does NOT fold (shield, ordnance, per-module
/// health, AI target id) are deliberately excluded so a per-ship digest here equals that ship's exact checksum
/// contribution — a digest match here but a wire-checksum mismatch would then localize the divergence to the
/// EMPIRE/PLANET/RNG lanes rather than the ship lane. If WriteAuthoritative's ship lane gains a field, add it
/// here too (there is a guard test that folds both and asserts they agree).
/// </summary>
public static class UniverseStateFieldDump
{
    /// <summary>The canonical per-ship field names, in fold order. Mirrors WriteAuthoritative's ship lane.</summary>
    public static readonly string[] ShipFieldNames =
    {
        "PosX", "PosY", "VelX", "VelY", "Rotation", "Health",
    };

    public readonly struct ShipDigest
    {
        public readonly int Id;
        public readonly ulong Lo;
        public readonly ulong Hi;
        public readonly float PosX, PosY, VelX, VelY, Rotation, Health;

        public ShipDigest(int id, ulong lo, ulong hi,
            float posX, float posY, float velX, float velY, float rotation, float health)
        {
            Id = id; Lo = lo; Hi = hi;
            PosX = posX; PosY = posY; VelX = velX; VelY = velY; Rotation = rotation; Health = health;
        }

        public float Field(int index) => index switch
        {
            0 => PosX, 1 => PosY, 2 => VelX, 3 => VelY, 4 => Rotation, 5 => Health,
            _ => 0f,
        };

        public bool DigestEquals(ShipDigest other) => Lo == other.Lo && Hi == other.Hi;
    }

    /// <summary>Fold exactly the checksum's ship field set for ONE ship and return its 128-bit digest + the raw
    /// field values. The fold order + primitive calls match WriteAuthoritative's ship lane byte-for-byte.</summary>
    public static ShipDigest DigestShip(Ship s)
    {
        var c = new Hash128Checksum();
        c.WriteInt(s.Id);
        c.FloatRaw(s.Position.X);
        c.FloatRaw(s.Position.Y);
        c.FloatRaw(s.Velocity.X);
        c.FloatRaw(s.Velocity.Y);
        c.FloatRaw(s.Rotation);
        c.FloatRaw(s.Health);
        (ulong lo, ulong hi) = c.Finish128();
        return new ShipDigest(s.Id, lo, hi,
            s.Position.X, s.Position.Y, s.Velocity.X, s.Velocity.Y, s.Rotation, s.Health);
    }

    /// <summary>Per-ship digests for every ship, in the SAME stable Id order the wire checksum iterates.</summary>
    public static List<ShipDigest> ShipDigests(UniverseState us)
        => us.Ships.OrderBy(s => s.Id).Select(DigestShip).ToList();

    /// <summary>
    /// Build the human-readable field-level dump text for one turn. When <paramref name="prior"/> is supplied
    /// (the same peer's ship digests from the turn BEFORE the desync), the first ship whose CURRENT digest
    /// differs from its prior digest is expanded field-by-field (raw IEEE-754 bits + decimal), because a
    /// cross-machine desync surfaces on the machine as "which of my ships changed between the clean turn and
    /// the diverging turn". When <paramref name="prior"/> is null, the numerically-largest-Id ship is expanded
    /// as a fallback anchor so the two machines still have a common field breakdown to diff.
    /// </summary>
    public static string DumpTurn(UniverseState us, uint turn, IReadOnlyList<ShipDigest> prior = null)
        => DumpFromDigests(ShipDigests(us), turn, prior);

    /// <summary>
    /// Dump directly from a precomputed digest list — used to render the PRIOR (last-clean) turn from the
    /// digests cached BEFORE the sim advanced into the diverging turn, since the live sim state has already
    /// moved on by the time a desync is detected. When <paramref name="prior"/> is null the anchor falls back
    /// to the highest-Id ship (stable across peers) so both machines still expand the same ship.
    /// </summary>
    public static string DumpFromDigests(IReadOnlyList<ShipDigest> now, uint turn, IReadOnlyList<ShipDigest> prior = null)
    {
        var sb = new StringBuilder();
        sb.Append("turn=").Append(turn).Append(" ships=").Append(now.Count);

        // Per-ship digest roster: id:hiHash:loHash, so a peer-to-peer diff of these lines alone pinpoints
        // WHICH ship diverged (the first id whose hi:lo differs across the two machines' same-turn dumps).
        sb.Append(" roster=[");
        for (int i = 0; i < now.Count; ++i)
        {
            if (i > 0) sb.Append(';');
            ShipDigest d = now[i];
            sb.Append(d.Id).Append(':')
              .Append(d.Hi.ToString("X16", CultureInfo.InvariantCulture)).Append(':')
              .Append(d.Lo.ToString("X16", CultureInfo.InvariantCulture));
        }
        sb.Append(']');

        // Pick the anchor ship whose fields to expand.
        ShipDigest? anchor = null;
        string anchorReason;
        if (prior != null && prior.Count > 0)
        {
            var priorById = new Dictionary<int, ShipDigest>(prior.Count);
            foreach (ShipDigest p in prior)
                priorById[p.Id] = p;
            foreach (ShipDigest d in now)
            {
                if (!priorById.TryGetValue(d.Id, out ShipDigest p) || !d.DigestEquals(p))
                {
                    anchor = d;
                    break;
                }
            }
            anchorReason = anchor != null ? "first-ship-changed-since-prior-turn" : "no-ship-changed-anchor-highest-id";
        }
        else
        {
            anchorReason = "no-prior-anchor-highest-id";
        }
        if (anchor == null && now.Count > 0)
            anchor = now[now.Count - 1]; // highest Id (stable across peers)

        if (anchor != null)
        {
            ShipDigest a = anchor.Value;
            sb.Append(" fieldsOf=").Append(a.Id).Append(" (").Append(anchorReason).Append(") ");
            for (int f = 0; f < ShipFieldNames.Length; ++f)
            {
                if (f > 0) sb.Append(' ');
                float v = a.Field(f);
                uint bits = BitConverter.SingleToUInt32Bits(v);
                sb.Append(ShipFieldNames[f]).Append('=')
                  .Append(v.ToString("R", CultureInfo.InvariantCulture))
                  .Append("(0x").Append(bits.ToString("X8", CultureInfo.InvariantCulture)).Append(')');
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Direct host-vs-join field diagnosis (used by the in-process harness, where BOTH peers are in scope).
    /// Finds the FIRST ship (by stable Id order) whose digest differs across the two peers, then emits a
    /// per-field host-vs-join diff. Classifies the divergence:
    ///  - "FP-DRIFT" when the differing field(s) are close (ULP/relative-tiny) — the cross-machine
    ///    bit-inexact-float hazard the report predicts for a real 2-CPU lockstep run.
    ///  - "DISCRETE-FLIP" when a field differs by a large/exact amount (or ship membership differs) — an
    ///    order/logic bug (a tie-break resolving differently, a missing/extra ship).
    /// Returns a one-line summary. When the digests all match (should not happen on a real desync — that
    /// would mean the divergence is in the empire/planet/RNG lanes, not the ship lane), says so explicitly.
    /// </summary>
    public static string DiagnoseHostVsJoin(uint turn,
        IReadOnlyList<ShipDigest> host, IReadOnlyList<ShipDigest> join)
    {
        if (host == null || join == null)
            return $"turn={turn} no-digests";
        if (host.Count != join.Count)
            return $"turn={turn} DISCRETE-FLIP ship-count host={host.Count} join={join.Count} "
                   + "(ship membership diverged — a ship died/spawned on one peer only)";

        var joinById = new Dictionary<int, ShipDigest>(join.Count);
        foreach (ShipDigest j in join)
            joinById[j.Id] = j;

        foreach (ShipDigest h in host)
        {
            if (!joinById.TryGetValue(h.Id, out ShipDigest j))
                return $"turn={turn} DISCRETE-FLIP ship id={h.Id} present on host, absent on join";
            if (h.DigestEquals(j))
                continue;

            // Found the first divergent ship. Per-field diff.
            var sb = new StringBuilder();
            sb.Append("turn=").Append(turn).Append(" firstDivergentShip=").Append(h.Id).Append(' ');
            bool anyDiscrete = false;
            int diffFields = 0;
            for (int f = 0; f < ShipFieldNames.Length; ++f)
            {
                float hv = h.Field(f), jv = j.Field(f);
                uint hb = BitConverter.SingleToUInt32Bits(hv), jb = BitConverter.SingleToUInt32Bits(jv);
                if (hb == jb)
                    continue;
                ++diffFields;
                long ulps = UlpDistance(hb, jb);
                bool discrete = !IsFpDrift(hv, jv, ulps);
                anyDiscrete |= discrete;
                sb.Append(ShipFieldNames[f]).Append("[host=")
                  .Append(hv.ToString("R", CultureInfo.InvariantCulture)).Append("(0x")
                  .Append(hb.ToString("X8", CultureInfo.InvariantCulture)).Append(") join=")
                  .Append(jv.ToString("R", CultureInfo.InvariantCulture)).Append("(0x")
                  .Append(jb.ToString("X8", CultureInfo.InvariantCulture)).Append(") ulps=")
                  .Append(ulps).Append(discrete ? " DISCRETE] " : " fp] ");
            }
            string verdict = anyDiscrete ? "DISCRETE-FLIP" : "FP-DRIFT";
            return $"{verdict} {sb.ToString().TrimEnd()} "
                   + $"(diffFields={diffFields}; {(anyDiscrete ? "order/logic bug — a tie/branch resolved differently" : "cross-machine float inexactness — same code, different FP result")})";
        }

        return $"turn={turn} ship-lane-identical (digests all match — the divergence is in the EMPIRE/PLANET/RNG "
               + "lanes, not the ship lane; inspect the empire RNG-state canary and economy fields)";
    }

    // ULP distance between two float bit-patterns on the monotone total order. Large for a real numeric gap,
    // tiny (0..few) for the last-bit inexactness a different CPU's FMA/rounding produces.
    static long UlpDistance(uint ab, uint bb)
    {
        long d = ToOrdered(ab) - ToOrdered(bb);
        return d < 0 ? -d : d;
    }

    // Map an IEEE-754 float bit-pattern to a signed integer that is monotone in the float value, so integer
    // subtraction yields the ULP distance (the standard AlmostEqualUlps transform). Positives map to
    // [0, 0x7FFFFFFF]; negatives map to the negative range so -0.0 and +0.0 are adjacent.
    static long ToOrdered(uint bits)
        => (bits & 0x80000000u) != 0
            ? -(long)(bits & 0x7FFFFFFFu)   // negative float: magnitude negated
            : bits;                          // positive float: as-is

    // Max ULP gap still classified as cross-machine FP inexactness on the FIRST diverging turn. A genuine
    // bit-inexact-float divergence (different CPU FMA/rounding, same code) shows up as a 1-few-ULP tail on the
    // turn it first appears; a discrete logic/order flip (a tie resolving differently, a different branch taken)
    // moves the value by many ULPs at once — even a "small" absolute nudge like +3.0 is ~48 ULPs near a 1e6
    // coordinate, far above rounding noise. This threshold is scale-independent (ULPs, not absolute units), so
    // it holds whether the ship sits at the origin or at a 1e6 arena coordinate. NaN/Inf on one side is discrete.
    public const long FpDriftUlpThreshold = 8;

    static bool IsFpDrift(float a, float b, long ulps)
    {
        if (float.IsNaN(a) != float.IsNaN(b) || float.IsInfinity(a) != float.IsInfinity(b))
            return false;
        if (float.IsNaN(a) && float.IsNaN(b))
            return true;
        return ulps <= FpDriftUlpThreshold;
    }
}

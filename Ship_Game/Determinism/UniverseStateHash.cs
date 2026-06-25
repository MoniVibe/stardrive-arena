using System.Collections.Generic;
using System.Linq;
using SDUtils.Deterministic;
using Ship_Game.Ships;
using Ship_Game.Universe;
using Ship_Game.Universe.SolarBodies;

namespace Ship_Game.Determinism;

/// <summary>
/// Canonical simulation-state hash (advisor plan §M1 + Rework 4). A single authoritative traversal
/// (<see cref="WriteAuthoritative"/>) walks simulation-truth state in a FIXED order — entities sorted
/// by stable Id — and writes it to any <see cref="IDeterminismChecksum"/>. Renderer / UI / audio /
/// telemetry state is intentionally excluded.
///
/// Rework 4 separation:
///  - <see cref="ComputeAuthoritativeStateHash"/> hashes ONLY authoritative state with the MP-grade
///    128-bit checksum — this is the value the lockstep protocol compares. Diagnostic/cache changes
///    must never enter here, or they would become wire-protocol changes.
///  - <see cref="ComputeDebugLaneHashes"/> uses the lane writer and may include extra detail for
///    divergence localization only.
///
/// Remaining lanes (Fleets / Combat / Spatial / Diplomacy / Research / Production) and per-entity
/// queue contents are expanded per the §M1 checklist as the oracle widens.
/// </summary>
public static class UniverseStateHash
{
    /// <summary>Write all authoritative sim state, in canonical order, to the given checksum.</summary>
    public static void WriteAuthoritative(UniverseState us, IDeterminismChecksum c)
    {
        c.Lane((int)DetLane.Universe);
        c.FloatRaw(us.StarDate);

        List<Empire> empires = us.Empires.OrderBy(e => e.Id).ToList();
        c.Lane((int)DetLane.Empires);
        c.WriteInt(empires.Count);
        foreach (Empire e in empires)
        {
            c.Lane((int)DetLane.Empires);
            c.WriteInt(e.Id);

            // Economy/Production: outputs of the economic + colony planners
            c.Lane((int)DetLane.Economy);
            c.FloatRaw(e.Money);
            c.FloatRaw(e.TotalPopBillion);
            c.FloatRaw(e.NetPlanetIncomes);
            c.WriteInt(e.NumPlanets);

            // Research: the research planner's current choice + progress
            c.Lane((int)DetLane.Research);
            c.WriteInt(e.UnlockedTechs.Length);
            c.FloatRaw(e.Research.NetResearch);
            c.WriteString(e.Research.Topic);

            // AI planner outputs: goals queued + fleets formed
            c.Lane((int)DetLane.ShipAI);
            c.WriteInt(e.AI?.Goals.Count ?? 0);
            c.Lane((int)DetLane.Fleets);
            c.WriteInt(e.AllFleets.Count);

            // RNG stream state LAST — a canary for divergent random-consumption order
            if (e.Random != null && e.Random.TryGetState(out ulong empireRng))
            {
                c.Lane((int)DetLane.Rng);
                c.WriteULong(empireRng);
            }
        }

        Ship[] ships = us.Ships.OrderBy(s => s.Id).ToArray();
        c.Lane((int)DetLane.Ships);
        c.WriteInt(ships.Length);
        foreach (Ship s in ships)
        {
            c.WriteInt(s.Id);
            c.FloatRaw(s.Position.X);
            c.FloatRaw(s.Position.Y);
            c.FloatRaw(s.Velocity.X);
            c.FloatRaw(s.Velocity.Y);
            c.FloatRaw(s.Rotation);
            c.FloatRaw(s.Health);
        }

        Planet[] planets = us.Planets.OrderBy(p => p.Id).ToArray();
        c.Lane((int)DetLane.Planets);
        c.WriteInt(planets.Length);
        foreach (Planet p in planets)
        {
            c.Lane((int)DetLane.Planets);
            c.WriteInt(p.Id);
            c.WriteInt(p.Owner?.Id ?? 0);
            c.Lane((int)DetLane.Production);
            c.FloatRaw(p.PopulationBillion);
            c.WriteInt(p.ConstructionQueue.Count);
        }

        if (us.Random != null && us.Random.TryGetState(out ulong universeRng))
        {
            c.Lane((int)DetLane.Rng);
            c.WriteULong(universeRng);
        }
    }

    /// <summary>
    /// Authoritative per-tick checksum — the value the lockstep protocol compares. 128-bit, MP-grade.
    /// </summary>
    public static (ulong lo, ulong hi, string algorithm) ComputeAuthoritativeStateHash(
        this UniverseState us, DeterminismProfile profile)
    {
        var c = new Hash128Checksum();
        WriteAuthoritative(us, c);
        (ulong lo, ulong hi) v = c.Finish128();
        return (v.lo, v.hi, c.AlgorithmId);
    }

    /// <summary>Debug lane hashes for divergence localization (not the wire protocol).</summary>
    public static DeterminismHashWriter ComputeDebugLaneHashes(this UniverseState us, DeterminismProfile profile)
    {
        var w = new DeterminismHashWriter();
        WriteAuthoritative(us, w);
        return w;
    }

    /// <summary>Back-compat: combined debug hash via a lane writer (used by the existing §M1 tests).</summary>
    public static ulong ComputeStateHash(this UniverseState us, DeterminismHashWriter w)
    {
        WriteAuthoritative(us, w);
        return w.Full;
    }
}

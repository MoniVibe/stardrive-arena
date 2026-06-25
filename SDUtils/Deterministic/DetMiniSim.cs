using System.Threading.Tasks;

namespace SDUtils.Deterministic;

/// <summary>
/// A tiny deterministic N-body simulation exercising the full Tier-C stack together:
/// per-entity forked RNG (<see cref="DetRandom.Fork"/>) choosing thrust, fixed-point physics
/// (<see cref="DetPhysics2D"/>), and a per-tick state hash (<see cref="DetHash"/>).
///
/// It proves the two properties lockstep multiplayer depends on:
///  1. run-to-run determinism (same seed =&gt; same trajectory hash), and
///  2. PARALLEL == SEQUENTIAL: because each body's RNG is keyed by (rootSeed, bodyId, tick) — never
///     by thread scheduling — a multi-threaded update yields bit-identical results to a single-threaded
///     one. This is what lets StarDrive keep its parallel sim AND be deterministic (plan §M4/§M7).
/// </summary>
public static class DetMiniSim
{
    public static ulong Run(int bodyCount, int ticks, ulong rootSeed, bool parallel)
    {
        var bodies = new DetPhysics2D.Body[bodyCount];
        Fixed64 dt = Fixed64.FromDouble(1.0 / 60.0);
        Fixed64 acc = Fixed64.FromInt(50);
        Fixed64 vmax = Fixed64.FromInt(200);
        var root = new DetRandom(rootSeed);

        var hash = DetHash.New();
        for (int t = 0; t < ticks; ++t)
        {
            if (parallel)
                Parallel.For(0, bodyCount, i => bodies[i] = StepBody(bodies[i], root, i, t, acc, vmax, dt));
            else
                for (int i = 0; i < bodyCount; ++i)
                    bodies[i] = StepBody(bodies[i], root, i, t, acc, vmax, dt);

            // Hash in fixed body-id order (NOT thread-completion order) so the checksum is canonical.
            for (int i = 0; i < bodyCount; ++i)
            {
                hash.AddLong(bodies[i].Position.X.Raw);
                hash.AddLong(bodies[i].Position.Y.Raw);
            }
        }
        return hash.Value;
    }

    static DetPhysics2D.Body StepBody(DetPhysics2D.Body b, DetRandom root, int bodyId, int tick,
                                      Fixed64 acc, Fixed64 vmax, Fixed64 dt)
    {
        // Per-(body,tick) stream: depends only on inputs, never on thread interleaving.
        DetRandom rng = root.Fork((ulong)bodyId).Fork((ulong)tick);
        long rotRaw = (long)(rng.NextULong() % (ulong)Fixed64.TwoPiRaw);
        return DetPhysics2D.Step(b, Fixed64.FromRaw(rotRaw), acc, vmax, dt);
    }
}

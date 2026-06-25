using System.Collections.Generic;

namespace SDLockstep;

/// <summary>
/// Deterministic offline replay (advisor plan VS6 / VS10 foundation): drive a FRESH
/// <see cref="ILockstepSimulation"/> through a recorded <see cref="CommandLog"/> and produce the
/// per-tick authoritative hash trajectory. Because the sim is deterministic and the command log is the
/// only input, the replayed trajectory must equal the live one — which is what makes record/replay a
/// regression test, a desync-reproduction tool, and the basis for save/join/resync (VS10).
/// </summary>
public static class ReplayRunner
{
    public static List<(uint tick, ulong lo, ulong hi)> Replay(CommandLog log, ILockstepSimulation sim, uint ticks)
    {
        var trajectory = new List<(uint tick, ulong lo, ulong hi)>((int)ticks);
        for (uint t = 0; t < ticks; ++t)
        {
            CommandFrame frame = log.Get(t) ?? new CommandFrame(t);
            sim.Apply(frame);
            (ulong lo, ulong hi) = sim.Hash();
            trajectory.Add((t, lo, hi));
        }
        return trajectory;
    }

    /// <summary>
    /// Replay from the sim's CURRENT tick up to <paramref name="untilTick"/> — used after loading a
    /// mid-match snapshot to catch a joining/resyncing client up to the host (VS10).
    /// </summary>
    public static (ulong lo, ulong hi) ReplayFrom(CommandLog log, ILockstepSimulation sim, uint untilTick)
    {
        while (sim.Tick < untilTick)
        {
            uint t = sim.Tick;
            CommandFrame frame = log.Get(t) ?? new CommandFrame(t);
            sim.Apply(frame);
        }
        return sim.Hash();
    }
}

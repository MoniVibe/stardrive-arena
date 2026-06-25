using System;
using Ship_Game.Universe;
using Ship_Game.Utils;
using SDLockstep;

namespace Ship_Game.Determinism.Lockstep;

/// <summary>
/// VS8-real adapter (advisor plan RC1): drives a headless StarDrive <see cref="UniverseState"/> through
/// the engine-agnostic SDLockstep kernel. The universe is created + content-loaded by the caller
/// (test harness / scenario builder); this wraps it and advances it one fixed tick per <see cref="Apply"/>.
///
/// RC1 is NoOp-only — no command translation yet (RC3 adds the guarded StarDriveCommandApplicator;
/// RC4/RC5 add Move/Attack). RC6 implements real SaveState/LoadState via the binary save serializer.
///
/// Tick contract: Tick == last completed sim tick; Apply(frame) advances exactly one fixed tick, then
/// Tick == frame.Tick. Hash() = ComputeAuthoritativeStateHash(profile) over the completed tick.
/// </summary>
public sealed class UniverseStateLockstepSimulation : ISnapshotableSimulation
{
    readonly UniverseScreen Universe;
    readonly UniverseState UState;
    readonly DeterminismProfile Profile;
    readonly StarDriveCommandApplicator Applicator;
    readonly FixedSimTime Step;
    uint TickField;

    public uint Tick => TickField;

    /// <summary>Diagnostics: (commands applied, commands rejected) so far.</summary>
    public (long applied, long rejected) CommandStats => (Applicator.CommandsApplied, Applicator.CommandsRejected);

    public UniverseStateLockstepSimulation(UniverseScreen universe, DeterminismProfile profile, float dt = 1f / 60f)
    {
        Universe = universe;
        UState = universe.UState;
        Profile = profile;
        Applicator = new StarDriveCommandApplicator(UState);
        Step = new FixedSimTime(dt);
    }

    public void Apply(CommandFrame frame)
    {
        // RC3+: translate the frame's commands into real StarDrive orders (guarded), then advance
        // exactly one fixed simulation tick. NoOp frames apply nothing and just advance.
        Applicator.Apply(frame);
        // Slice scope (advisor): drive the object simulation (ships, physics, AI, combat, projectiles).
        // This excludes the empire-economy turn (ProcessTurnEmpires) which is not part of the skirmish
        // slice and which re-tasks idle ships. Matches the proven movement path in TestShipMove.
        UState.Objects.Update(Step);
        TickField++;
    }

    public (ulong lo, ulong hi) Hash()
    {
        (ulong lo, ulong hi, string _) = UState.ComputeAuthoritativeStateHash(Profile);
        return (lo, hi);
    }

    // RC6 will implement these over the deterministic binary snapshot (RNG stream states, queues, tick).
    public byte[] SaveState() => Array.Empty<byte>();
    public void LoadState(byte[] data) { }
}

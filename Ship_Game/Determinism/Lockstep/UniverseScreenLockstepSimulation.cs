using SDLockstep;
using Ship_Game.Universe;

namespace Ship_Game.Determinism.Lockstep;

/// <summary>
/// Full-universe lockstep adapter for regular 4X games. Unlike
/// <see cref="UniverseStateLockstepSimulation"/>, this advances the complete
/// <see cref="UniverseScreen.SingleSimulationStep"/> path: empire turns, AI,
/// economy, events, object simulation, and end-of-turn updates.
/// </summary>
public sealed class UniverseScreenLockstepSimulation : ISnapshotableSimulation
{
    readonly UniverseScreen Universe;
    readonly UniverseState UState;
    readonly DeterminismProfile Profile;
    readonly StarDriveCommandApplicator Applicator;
    readonly FixedSimTime Step;
    uint TickField;

    public uint Tick => TickField;
    public (long applied, long rejected) CommandStats => (Applicator.CommandsApplied, Applicator.CommandsRejected);

    public UniverseScreenLockstepSimulation(UniverseScreen universe,
        DeterminismProfile profile, float dt = 1f / 60f)
    {
        Universe = universe;
        UState = universe.UState;
        Profile = profile;
        Applicator = new StarDriveCommandApplicator(UState);
        Step = new FixedSimTime(dt);
    }

    public void Apply(CommandFrame frame)
    {
        Applicator.Apply(frame);
        Universe.SingleSimulationStep(Step);
        TickField++;
    }

    public (ulong lo, ulong hi) Hash()
    {
        (ulong lo, ulong hi, string _) = UState.ComputeAuthoritativeStateHash(Profile);
        return (lo, hi);
    }

    public byte[] SaveState() => System.Array.Empty<byte>();
    public void LoadState(byte[] data) { }
}

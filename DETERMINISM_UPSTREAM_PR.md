# Optional deterministic-RNG + stable-collision mode (off by default)

This is an upstream-ready PR spec for the minimal BlackBox engine subset needed by the Star Gladiator arena plugin's deterministic duel/career sims. It is intentionally narrower than the current StarDrive determinism branch: no `SDLockstep`, no `Ship_Game.Determinism.Lockstep`, no `UniverseStateHash`, no replay system, and no mandatory deterministic universe generation.

This document is the source of truth for a squash/import PR. Do not treat the current branch diff as the upstream patch: the current branch contains broader determinism and arena work. The manifest below is the minimal separable subset.

## Rationale

The arena plugin needs a way to run small headless sims reproducibly without changing normal live play. The required engine fixes are:

1. `UniverseObjectManager.UpdateSolarSystemShips` had an unconditional `Parallel.For`, ignoring `EnableParallelUpdate`. Even when a caller explicitly requested serial update for deterministic simulation, this phase still ran in scheduler-dependent order.
2. Native spatial collision pairs were resolved in native traversal/scheduler order. That can perturb damage application order and produce same-seed combat divergence. SDNative already has a `sortCollisionsById` parameter; the C# layer only needs a deterministic collision flag to pass `1` when deterministic mode is enabled.
3. The engine needs an opt-in deterministic RNG entry point that replaces world/empire/body RNG owners with integer-only streams derived from one root seed.

Everything is off by default.

## Verified Arena Surface

Current arena usage is limited to these engine switches:

- `ArenaFightScreen.cs:642`: calls `UniverseState.EnableDeterministicRng(seed)` for seeded live-screen creation.
- `ArenaFightScreen.cs:646`: calls `PlanetTypes.SeedDeterministicGeneration(generationSeed)` for reproducible generated planet types. This is optional for the minimal duel/career patch and should be a follow-up PR.
- `CareerLadder.cs:239/249`: calls `UniverseState.EnableDeterministicRng(seed)` before and after duel empire creation.
- `CareerLadder.cs:241`: sets `UniverseObjectManager.EnableParallelUpdate = false`.
- `CareerLadder.cs:425/433`: calls `UniverseState.EnableDeterministicRng(seed)` before and after team-duel empire creation.
- `CareerLadder.cs:427`: sets `UniverseObjectManager.EnableParallelUpdate = false`.
- `UniverseState.EnableDeterministicRng` sets `Spatial.DeterministicCollisions = true`, so arena callers do not touch spatial internals directly.

The duel/career sims do not require lockstep, replay, universe hashes, or deterministic galaxy generation.

## Off-By-Default Safety Contract

- `UniverseState.DeterministicRootSeed` defaults to `0`.
- `UniverseState.IsDeterministicRng` is `false` while the root seed is `0`.
- `UniverseState.Random`, `Empire.Random`, and `SolarSystemBody.Random` remain `ThreadSafeRandom` until deterministic mode is explicitly enabled.
- `SpatialManager.DeterministicCollisions` defaults to `false`.
- `NativeSpatial.DeterministicCollisions` defaults to `false`, so `CollisionParams.SortCollisionsById = 0` and native traversal order is unchanged.
- `UniverseObjectManager.EnableParallelUpdate` defaults to `true`, so normal live play still uses the existing parallel update path.
- `EnableDeterministicRng(rootSeed)` is explicit opt-in. Normal play does not call it.

The included tests prove both sides of the contract: inert by default; reproducible after explicit same-seed opt-in with serial update.

## File-By-File Manifest

### `SDUtils/Deterministic/DetRandom.cs` - new file

Add a pure SplitMix64 RNG primitive. It has no dependency on `Ship_Game` and uses integer-only state.

Essential API:

```csharp
namespace SDUtils.Deterministic;

public struct DetRandom
{
    const ulong Gamma = 0x9E3779B97F4A7C15UL;
    ulong StateField;

    public ulong State
    {
        readonly get => StateField;
        set => StateField = value;
    }

    public DetRandom(ulong seed)
    {
        StateField = Mix64(seed == 0 ? Gamma : seed);
    }

    DetRandom(ulong state, ulong stream, bool _)
    {
        StateField = Mix64(state ^ Mix64(stream + Gamma));
    }

    public readonly DetRandom Fork(ulong streamId) => new(StateField, streamId, false);

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

    public uint NextUInt() => (uint)(NextULong() >> 32);

    public static ulong Mix64(ulong x)
    {
        unchecked
        {
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }

    public float NextFloat()
    {
        uint bits24 = (uint)(NextULong() >> 40);
        return bits24 * (1.0f / 16777216.0f);
    }

    public double NextDouble()
    {
        ulong bits53 = NextULong() >> 11;
        return bits53 * (1.0 / 9007199254740992.0);
    }

    public int NextInt(int min, int max)
    {
        if (max <= min) return min;
        uint range = (uint)((long)max - min);
        ulong m = (ulong)NextUInt() * range;
        uint low = (uint)m;
        if (low < range)
        {
            uint threshold = (0u - range) % range;
            while (low < threshold)
            {
                m = (ulong)NextUInt() * range;
                low = (uint)m;
            }
        }
        return min + (int)(m >> 32);
    }
}
```

### `SDUtils/Deterministic/DetStreams.cs` - new file

Add deterministic stream derivation keyed by `(rootSeed, streamKind, stableId)` and optionally tick. No `Ship_Game` dependency.

```csharp
namespace SDUtils.Deterministic;

public static class DetStreams
{
    const ulong KindMul = 0x9E3779B97F4A7C15UL;
    const ulong IdAdd = 0xD1B54A32D192ED03UL;

    public static DetRandom ForEntity(ulong rootSeed, ulong streamKind, ulong stableId)
    {
        ulong seed = DetRandom.Mix64(rootSeed
                   ^ DetRandom.Mix64(streamKind * KindMul)
                   ^ DetRandom.Mix64(stableId + IdAdd));
        return new DetRandom(seed == 0 ? 1UL : seed);
    }

    public static DetRandom ForEntityTick(ulong rootSeed, ulong streamKind, ulong stableId, ulong tick)
        => ForEntity(rootSeed, streamKind, stableId).Fork(tick);
}
```

### `Ship_Game/Utils/RandomBase.cs` - new virtual methods

If upstream does not already expose deterministic state, add no-op virtual state methods to `RandomBase`:

```csharp
public virtual bool TryGetState(out ulong state) { state = 0; return false; }
public virtual void SetState(ulong state) { }
```

These are inert for existing `ThreadSafeRandom` and `SeededRandom` backends.

### `Ship_Game/Utils/DeterministicRandom.cs` - new file

Wrap `DetRandom` as a `RandomBase` implementation. This is the only new `Ship_Game` RNG backend.

```csharp
using SDUtils.Deterministic;

namespace Ship_Game.Utils;

public sealed class DeterministicRandom : RandomBase
{
    DetRandom Rng;

    public DeterministicRandom(int seed) : base(seed == 0 ? 1 : seed)
        => Rng = new DetRandom((ulong)(uint)Seed);

    public DeterministicRandom(ulong seed64) : base(seed64 == 0 ? 1 : unchecked((int)seed64))
        => Rng = new DetRandom(seed64 == 0 ? 1UL : seed64);

    DeterministicRandom(DetRandom forked, int seedTag) : base(seedTag == 0 ? 1 : seedTag)
        => Rng = forked;

    public static DeterministicRandom FromStream(DetRandom rng) => new(rng, 0);

    protected override double NextUnitDouble() => Rng.NextDouble();
    protected override int NextIntExclusive(int minInclusive, int maxExclusive)
        => Rng.NextInt(minInclusive, maxExclusive);

    public override bool TryGetState(out ulong state)
    {
        state = Rng.State;
        return true;
    }

    public override void SetState(ulong state) => Rng.State = state;

    public DeterministicRandom Fork(ulong streamId)
        => new(Rng.Fork(streamId), unchecked((int)streamId));
}
```

### No `Ship_Game.Determinism` namespace in this PR

The current development branch has `Ship_Game/Determinism/DeterministicStreams.cs`, but the upstream patch should not import that namespace. Inline the stream kind constants and `DetStreams.ForEntity(...)` calls at the three call sites:

```csharp
const ulong DeterministicUniverseStream = 1;
const ulong DeterministicEmpireStream = 2;
const ulong DeterministicSolarBodyStream = 3;
```

Then use:

```csharp
DeterministicRandom.FromStream(DetStreams.ForEntity(rootSeed, DeterministicUniverseStream, 0));
DeterministicRandom.FromStream(DetStreams.ForEntity(rootSeed, DeterministicEmpireStream, stableEmpireId));
DeterministicRandom.FromStream(DetStreams.ForEntity(rootSeed, DeterministicSolarBodyStream, stableBodyId));
```

### `Ship_Game/Universe/UniverseState.cs` - new field/properties/method

Line context in this branch: `Random` is around `UniverseState.cs:172`; add the deterministic fields and entry point there.

Tags: `new-field`, `new-property`, `new-method`.

```csharp
public RandomBase Random { get; private set; } = new ThreadSafeRandom();

public ulong DeterministicRootSeed { get; private set; }
public bool IsDeterministicRng => DeterministicRootSeed != 0;

const ulong DeterministicUniverseStream = 1;

public void EnableDeterministicRng(ulong rootSeed)
{
    DeterministicRootSeed = rootSeed;
    Random = DeterministicRandom.FromStream(
        DetStreams.ForEntity(rootSeed, DeterministicUniverseStream, 0));

    foreach (Empire e in Empires)
        e.UseDeterministicRandom(rootSeed);

    foreach (SolarSystem sys in Systems)
        foreach (Planet p in sys.PlanetList)
            p.UseDeterministicRandom(rootSeed);

    if (Spatial != null)
        Spatial.DeterministicCollisions = true;
}
```

Behavior note: callers pass non-zero seeds. If upstream wants to harden misuse, `rootSeed == 0` can either no-op or keep the existing `DetRandom` zero-remap; the public off-switch remains "never call `EnableDeterministicRng`."

### `Ship_Game/Empire.cs` - new method

Line context in this branch: `Random` is around `Empire.cs:263`; add the deterministic method beside it.

Tag: `new-method`.

```csharp
public RandomBase Random { get; private set; } = new ThreadSafeRandom();

const ulong DeterministicEmpireStream = 2;

public void UseDeterministicRandom(ulong rootSeed)
    => Random = DeterministicRandom.FromStream(
        DetStreams.ForEntity(rootSeed, DeterministicEmpireStream, (ulong)Id));
```

Optional universe-generation follow-up only: add a predicted-id seeding method for personality draws during empire creation. The duel/career sims do not require this.

### `Ship_Game/Universe/SolarBodies/SolarSystemBody.cs` - new method

Line context in this branch: `Random` is around `SolarSystemBody.cs:245`.

Tag: `new-method`.

```csharp
public RandomBase Random { get; private set; } = new ThreadSafeRandom();

const ulong DeterministicSolarBodyStream = 3;

public void UseDeterministicRandom(ulong rootSeed)
{
    if (rootSeed != 0)
        Random = DeterministicRandom.FromStream(
            DetStreams.ForEntity(rootSeed, DeterministicSolarBodyStream, (ulong)Id));
}
```

### `Ship_Game/Spatial/ISpatial.cs` - new property

Line context in this branch: property is around `ISpatial.cs:44`.

Tag: `new-property`.

```csharp
bool DeterministicCollisions { get; set; }
```

### `Ship_Game/Spatial/NativeSpatial.cs` - new property + in-place collision-param fix

Line context in this branch: property around `NativeSpatial.cs:84`; marshaling around `NativeSpatial.cs:214-221`.

Tags: `new-property`, `in-place-fix`.

```csharp
public bool DeterministicCollisions { get; set; }
```

Change `CollisionParams` construction from hardcoded native traversal order to:

```csharp
SortCollisionsById = (byte)(DeterministicCollisions ? 1 : 0),
```

The struct already contains:

```csharp
public byte SortCollisionsById;
```

### `Ship_Game/Spatial/Qtree.cs` - interface parity property

Line context in this branch: property is around `Qtree.cs:40`.

Tag: `new-property`.

```csharp
public bool DeterministicCollisions { get; set; }
```

Managed Qtree already traverses deterministically enough for this subset; the property keeps `ISpatial` parity and future-proofs manager recreation.

### `Ship_Game/Gameplay/SpatialManager.cs` - mirror/inherit deterministic collision flag

Line context in this branch: property around `SpatialManager.cs:24-36`; inheritance around `SpatialManager.cs:59`.

Tags: `new-field`, `new-property`, `in-place-fix`.

```csharp
bool DeterministicCollisionsField;
public bool DeterministicCollisions
{
    get => DeterministicCollisionsField;
    set
    {
        DeterministicCollisionsField = value;
        if (Spatial != null) Spatial.DeterministicCollisions = value;
        if (ResetToNewSpatial != null) ResetToNewSpatial.DeterministicCollisions = value;
    }
}
```

When recreating the spatial:

```csharp
newSpatial.DeterministicCollisions = DeterministicCollisionsField;
```

### `Ship_Game/Universe/UniverseScreen/UniverseObjectManager.cs` - serial-update fix

Line context in this branch: `UpdateSolarSystemShips` is around `UniverseObjectManager.cs:335-383`.

Tag: `in-place-fix`.

Replace the unconditional parallel call with:

```csharp
if (EnableParallelUpdate)
    Parallel.For(UState.Systems.Count, UpdateSystems, MaxTaskCores);
else
    UpdateSystems(0, UState.Systems.Count);
```

This makes `EnableParallelUpdate=false` consistently serial across ship/projectile/sensor/AI/system-assignment phases.

### SDNative note - no native rebuild required

SDNative already has the field and sort behavior:

- `SDNative/spatial/Collision.h`: `CollisionParams` contains `bool sortCollisionsById = false`.
- `SDNative/spatial/Collision.cpp`: `Collider::getResults` sorts results when `params.sortCollisionsById` is true.
- The C API already accepts `CollisionParams`.

The upstream PR only changes C# plumbing so deterministic mode passes `SortCollisionsById = 1`. Normal mode still passes `0`.

## Optional Follow-Up PR: deterministic universe generation

Not required for arena duel/career sims. Useful only for a seeded live universe path such as `ArenaFightScreen.Create(generationSeed)` or deterministic full-galaxy tests.

Files:

- `Ship_Game/Universe/SolarBodies/PlanetTypes.cs`: add `SeedDeterministicGeneration(int seed)` to route the shared planet-type picker through `SeededRandom(seed)` and restore `ThreadSafeRandom` for seed 0.
- `Ship_Game/GameScreens/NewGame/UniverseGenerator.cs`: call `ResourceManager.Planets.SeedDeterministicGeneration(p.GenerationSeed)` when `GenerationSeed != 0`.
- Generation-time empire personality seeding can use a predicted stable empire id if upstream needs personality trait draws to match the post-add empire stream topology.

Keep this separate so the minimal combat determinism PR remains low-risk.

## Tests To Include

The current branch adds `UnitTests/Determinism/ArenaDeterminismPatchContractTests.cs` with two upstream-quality tests:

1. `ArenaDeterminismPatch_ModeOffIsInert_Headless`
   - Creates a normal universe without calling `EnableDeterministicRng`.
   - Asserts root seed is 0, mode flag is false, universe/empire RNGs are `ThreadSafeRandom`, RNG state is not exposed, deterministic collisions is false, and parallel update defaults true.

2. `ArenaDeterminismPatch_SameSeedReproducible_DifferentSeedDiffers_Headless`
   - Creates a small two-ship sim.
   - Calls `EnableDeterministicRng(seed)`, sets `EnableParallelUpdate=false`, derives spawn offsets from deterministic `UState.Random`, runs serial `Objects.Update`, and compares a test-local digest of ship state.
   - Same seed must match exactly; different seed must differ.

These tests deliberately avoid `SDLockstep`, `UniverseStateHash`, and replay infrastructure.

## Import Checklist

1. Add `DetRandom`, `DetStreams`, and `DeterministicRandom`.
2. Add `RandomBase.TryGetState/SetState` only if upstream lacks them.
3. Add `UniverseState.EnableDeterministicRng`, `DeterministicRootSeed`, and `IsDeterministicRng`.
4. Add `Empire.UseDeterministicRandom`.
5. Add `SolarSystemBody.UseDeterministicRandom`.
6. Add `ISpatial.DeterministicCollisions`, implement it in `NativeSpatial` and `Qtree`, mirror it in `SpatialManager`.
7. Change `NativeSpatial` to pass `SortCollisionsById = DeterministicCollisions ? 1 : 0`.
8. Gate `UpdateSolarSystemShips` on `EnableParallelUpdate`.
9. Add the two tests.
10. Build `StarDrive.csproj` and `UnitTests/SDUnitTests.csproj`; run the relevant deterministic tests and the existing arena suite.

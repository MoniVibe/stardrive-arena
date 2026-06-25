using System;
using System.Collections.Generic;
using SDGraphics;
using SDUtils;

namespace Ship_Game.Utils;

/// <summary>
/// Provides a base class for Random number utilities.
///
/// Determinism note: the core draw surface is reduced to two primitives —
/// <see cref="NextUnitDouble"/> and <see cref="NextIntExclusive"/>. System.Random-backed
/// subclasses (<see cref="SeededRandom"/>, <see cref="ThreadSafeRandom"/>) implement them by
/// delegating to System.Random, preserving their exact historical sequences. The lockstep
/// backend (DeterministicRandom) implements them on an integer SplitMix64 stream, so its
/// output is bit-identical across machines and is save/restorable.
/// </summary>
public abstract class RandomBase
{
    protected int Seed;

    /// <summary>The seed this generator was initialized with (for diagnostics / save metadata).</summary>
    public int CurrentSeed => Seed;

    /// <summary>
    /// Initializes the pseudo-random with a seed value. If 0 seed is given,
    /// then a seed is automatically generated based on CPU clock ticks.
    /// NOTE: clock seeding is non-reproducible — deterministic owners must pass an explicit seed.
    /// </summary>
    protected RandomBase(int seed)
    {
        // Automatically initialize from system clock ticks
        if (seed == 0)
        {
            PerfTimer.GetTicks(out long ticks);
            Seed = (int)ticks;
        }
        else
        {
            Seed = seed;
        }
    }

    // ---- Core draw primitives (the only thing subclasses must implement) ----

    /// <summary>Next double in [0,1).</summary>
    protected abstract double NextUnitDouble();

    /// <summary>Next int in [minInclusive, maxExclusive). Caller guarantees maxExclusive &gt;= minInclusive.</summary>
    protected abstract int NextIntExclusive(int minInclusive, int maxExclusive);

    // ---- Save / restore (deterministic backends override) ----

    /// <summary>True if this generator exposes a restorable integer state (deterministic backends).</summary>
    public virtual bool TryGetState(out ulong state) { state = 0; return false; }

    /// <summary>Restore a previously captured integer state. No-op for non-deterministic backends.</summary>
    public virtual void SetState(ulong state) { }

    /// <summary>
    /// Generate a random float within inclusive [min, max]
    /// </summary>
    public float Float(float min, float max)
    {
        return min + ((float)NextUnitDouble() * (max - min));
    }

    /// <summary>
    /// Generate a random float within inclusive [0..1]
    /// </summary>
    public float Float()
    {
        return (float)NextUnitDouble();
    }

    /// <summary>
    /// Generate a random double within inclusive [min, max]
    /// </summary>
    public double Double(double min, double max)
    {
        return min + (NextUnitDouble() * (max - min));
    }

    /// <summary>
    /// Generate a random double within inclusive [0..1]
    /// </summary>
    public double Double()
    {
        return NextUnitDouble();
    }

    /// <summary>
    /// Generate random integer within inclusive [min, max]
    /// </summary>
    public int Int(int min, int max)
    {
        return NextIntExclusive(min, max == int.MaxValue ? max : max + 1);
    }

    /// <summary>
    /// Generates a random byte
    /// </summary>
    public byte Byte()
    {
        return (byte)NextIntExclusive(0, 255);
    }

    /// <summary>
    /// Generate random index, upper bound excluded: [startIndex, arrayLength)
    /// Example: int itemIndex = random.InRange(0, items.Length);
    /// </summary>
    public int InRange(int startIndex, int arrayLength)
    {
        return NextIntExclusive(startIndex, arrayLength);
    }

    /// <summary>
    /// Random index, in range [0, arrayLength)
    /// Example: int itemIndex = random.InRange(items.Length);
    /// </summary>
    public int InRange(int arrayLength)
    {
        return NextIntExclusive(0, arrayLength);
    }

    /// <summary>
    /// Chooses a random element from the list
    /// </summary>
    public T Item<T>(IReadOnlyList<T> items)
    {
        return items[InRange(items.Count)];
    }

    /// <summary>
    /// Chooses a random element from the span
    /// </summary>
    public T Item<T>(in Span<T> items)
    {
        return items[InRange(items.Length)];
    }

    /// <summary>
    /// Chooses a random element from the list, capped by maxItems count
    /// </summary>
    public T Item<T>(IReadOnlyList<T> items, int maxItems)
    {
        return items[InRange(Math.Min(items.Count, maxItems))];
    }

    /// <summary>
    /// Chooses a random element from the list, capped by maxItems count
    /// </summary>
    public T Item<T>(in Span<T> items, int maxItems)
    {
        return items[InRange(Math.Min(items.Length, maxItems))];
    }

    /// <summary>
    /// Filters all items and then chooses a random item from the passed ones.
    /// If there are no candidates, then null is returned
    /// </summary>
    public unsafe T ItemFilter<T>(IReadOnlyList<T> items, Predicate<T> filter)
    {
        // +4 to be more resilient to element add/remove and avoid buffer overrun here
        int* passedItemIndices = stackalloc int[items.Count + 4];
        int numPassed = 0;

        // using items.Count here to be a bit more resilient to element removal
        for (int i = 0; i < items.Count; ++i)
        {
            T item = items[i];
            if (filter(item)) passedItemIndices[numPassed++] = i;
        }

        // retry up to numPassed times, because arrays might be modified
        for (int i = 0; i < numPassed; ++i)
        {
            int selectedIndex = passedItemIndices[InRange(numPassed)];
            // double-check if it's still in bounds, because array could be modified
            if (selectedIndex < items.Count)
                return items[selectedIndex];
        }

        return default;
    }

    /// <summary>
    /// Filters all items and then chooses a random item from the passed ones.
    /// If there are no candidates, then null is returned
    /// </summary>
    public T ItemFilter<T>(IEnumerable<T> items, Predicate<T> filter)
    {
        // unfortunately we need allocate a helper array here
        // to avoid side effects from multiple-enumeration of IEnumerable
        Array<T> passed = new();
        foreach (T item in items)
            if (filter(item)) passed.Add(item);

        if (passed.Count > 0)
            return passed[InRange(passed.Count)];
        return default;
    }

    /// <summary>
    /// Fisher-Yates in-place shuffle using the seeded RNG.
    /// Use this instead of ListExt.Shuffle when the caller already owns a RandomBase
    /// (e.g. UniverseGenerator) so universe-seed reproducibility is preserved.
    /// </summary>
    public void Shuffle<T>(IList<T> list)
    {
        for (int n = list.Count - 1; n > 0; --n)
        {
            int k = NextIntExclusive(0, n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
    }

    /// <summary>
    /// Performs a dice-roll, where chance must be between [0..100]
    /// @return TRUE if random chance passed
    /// @example if (RandomMath.RollDice(33)) {..} // 33% chance
    /// </summary>
    public bool RollDice(float percent)
    {
        float randomFloat = Float(0f, 100f);
        return randomFloat < percent;
    }

    /// <summary>
    /// @return a specific die size roll, like 1d20, 1d6, etc. Minimum value can be changed
    /// Example: RollDie(10) will give a random int within [1, 10]
    /// Example: RollDie(12, minimum:4) will give a random int within [4, 12]
    /// </summary>
    public int RollDie(int dieSize, int minimum = 1)
    {
        return Math.Max(minimum, Int(1, dieSize));
    }

    /// <summary>
    /// Rolls a 100 sided dice 3 times and checks
    /// if the average result is less than percent
    /// </summary>
    public bool Roll3DiceAvg(float percent)
    {
        return RollDiceAvg(3) < percent;
    }

    float RollDiceAvg(int iterations)
    {
        if (iterations == 0)
            return 0f;

        float sum = 0;
        for (int i = 0; i < iterations; i++)
            sum += Float(0f, 100f);
        return (sum / iterations);
    }

    /// <summary>
    /// Generates a random point in Ring
    /// </summary>
    public Vector2 RandomPointInRing(float minRadius, float maxRadius)
    {
        float theta = Float(0f, 2f * (float)Math.PI);
        float w = Float(0f, 1f);
        float r = (float)Math.Sqrt((1f - w) * minRadius * minRadius + w * maxRadius * maxRadius);
        return new(r * Math.Cos(theta), r * Math.Sin(theta));
    }

    /// <summary>
    /// Generates a random 2D direction vector
    /// </summary>
    public Vector2 Direction2D()
    {
        float radians = Float(0f, RadMath.TwoPI);
        return radians.RadiansToDirection();
    }

    /// <summary>
    /// Generates a Vector2 with X and Y in range [-radius, +radius]
    /// </summary>
    public Vector2 Vector2D(float radius)
    {
        return new(Float(-radius, radius),
                   Float(-radius, radius));
    }

    /// <summary>
    /// Generates a Vector2 with X and Y in range [-min, +max]
    /// </summary>
    public Vector2 Vector2D(float min, float max)
    {
        return new(Float(min, max),
                   Float(min, max));
    }

    /// <summary>
    /// Generates a Vector3 with X Y Z in range [-radius, +radius]
    /// </summary>
    public Vector3 Vector3D(float radius)
    {
        return new(Float(-radius, radius),
                   Float(-radius, radius),
                   Float(-radius, radius));
    }

    /// <summary>
    /// Generates a Vector3 with Z set to 0.0f
    /// </summary>
    public Vector3 Vector32D(float radius)
    {
        return new(Float(-radius, radius),
                   Float(-radius, radius),
                   0f);
    }

    /// <summary>
    /// Generates a Vector3 with X Y Z in range [min, max]
    /// </summary>
    public Vector3 Vector3D(float min, float max)
    {
        return new(Float(min, max),
                   Float(min, max),
                   Float(min, max));
    }

    /// <summary>
    /// Generates a Vector3 with X Y Z in range [minValue, maxValue]
    /// </summary>
    public Vector3 Vector3D(in Vector3 minValue, in Vector3 maxValue)
    {
        return new(Float(minValue.X, maxValue.X),
                   Float(minValue.Y, maxValue.Y),
                   Float(minValue.Z, maxValue.Z));
    }

    /// <summary>
    /// Generates a Vector3 with X Y Z in range [-minMax, +minMax]
    /// </summary>
    public Vector3 Vector3D(in Vector3 minMax)
    {
        return new(Float(-minMax.X, minMax.X),
                   Float(-minMax.Y, minMax.Y),
                   Float(-minMax.Z, minMax.Z));
    }

    /// <summary>
    /// Average of multiple Float randoms
    /// </summary>
    public float AvgFloat(float min, float max, int iterations = 3)
    {
        if (iterations <= 1)
            return Float(min, max);

        float sum = 0;
        for (int x = 0; x < iterations; ++x)
            sum += Float(min, max);
        return sum / iterations;
    }

    /// <summary>
    /// Average of multiple Int randoms
    /// </summary>
    public int AvgInt(int min, int max, int iterations = 3)
    {
        if (iterations <= 1)
            return Int(min, max);

        int sum = 0;
        for (int x = 0; x < iterations; ++x)
            sum += Int(min, max);
        return sum / iterations;
    }
}

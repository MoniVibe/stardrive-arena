using System;
namespace Ship_Game.Utils;

/// <summary>
/// An implementation of RandomBase
/// NOTE: This is not thread-safe, @see ThreadSafeRandom
/// </summary>
public class SeededRandom : RandomBase
{
    readonly Random Rand;

    // Automatically initializes the seed with a unique seed value
    public SeededRandom() : this(0)
    {
    }

    public SeededRandom(int seed) : base(seed)
    {
        Rand = new(Seed);
    }

    protected override double NextUnitDouble() => Rand.NextDouble();

    protected override int NextIntExclusive(int minInclusive, int maxExclusive)
        => Rand.Next(minInclusive, maxExclusive);
}

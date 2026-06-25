using System;
using System.Threading;
namespace Ship_Game.Utils;

public sealed class ThreadSafeRandom : RandomBase, IDisposable
{
    // NOTE: This is really fast
    readonly ThreadLocal<Random> Randoms;

    // Automatically initializes the seed with a unique seed value
    public ThreadSafeRandom() : this(0)
    {
    }

    public ThreadSafeRandom(int seed) : base(seed)
    {
        Randoms = new ThreadLocal<Random>(() => new Random(Seed));
    }

    public void Dispose()
    {
        Randoms?.Dispose();
    }

    protected override double NextUnitDouble() => Randoms.Value.NextDouble();

    protected override int NextIntExclusive(int minInclusive, int maxExclusive)
        => Randoms.Value.Next(minInclusive, maxExclusive);
}

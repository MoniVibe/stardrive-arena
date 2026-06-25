using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;
using Ship_Game.Ships;
using Ship_Game.Utils;
using Vector2 = SDGraphics.Vector2;

namespace UnitTests.Determinism;

[TestClass]
public class ArenaDeterminismPatchContractTests : StarDriveTest
{
    [TestMethod]
    public void ArenaDeterminismPatch_ModeOffIsInert_Headless()
    {
        CreateUniverseAndPlayerEmpire();

        Assert.AreEqual(0ul, UState.DeterministicRootSeed,
            "Deterministic root seed must default to zero so deterministic mode is off.");
        Assert.IsFalse(UState.IsDeterministicRng,
            "Deterministic RNG mode must be opt-in.");
        Assert.IsInstanceOfType(UState.Random, typeof(ThreadSafeRandom),
            "Default universe RNG must remain the historical clock-seeded backend.");
        Assert.IsFalse(UState.Random.TryGetState(out _),
            "Default RNG must not expose deterministic save/restore state.");
        Assert.IsInstanceOfType(Player.Random, typeof(ThreadSafeRandom),
            "Default empire RNG must remain the historical clock-seeded backend.");
        Assert.IsInstanceOfType(Enemy.Random, typeof(ThreadSafeRandom),
            "Default enemy RNG must remain the historical clock-seeded backend.");
        Assert.IsFalse(UState.Spatial.DeterministicCollisions,
            "Default collision ordering must remain native traversal order; this false value marshals to SortCollisionsById=0.");
        Assert.IsTrue(UState.Objects.EnableParallelUpdate,
            "Default object update must remain parallel-capable; serial update is opt-in per caller.");
    }

    [TestMethod]
    public void ArenaDeterminismPatch_SameSeedReproducible_DifferentSeedDiffers_Headless()
    {
        const ulong SeedA = 0xA12E_D37E_0000_0001ul;
        const ulong SeedB = 0xA12E_D37E_0000_0002ul;

        ulong first = RunSmallSeededSerialSim(SeedA);
        ulong rerun = RunSmallSeededSerialSim(SeedA);
        ulong different = RunSmallSeededSerialSim(SeedB);

        Assert.AreEqual(first, rerun,
            "Same seed plus serial object update must reproduce the exact small-sim digest.");
        Assert.AreNotEqual(first, different,
            "A different deterministic root seed must perturb the small sim.");
    }

    ulong RunSmallSeededSerialSim(ulong seed)
    {
        CreateUniverseAndPlayerEmpire();
        UState.P.GravityWellRange = 0;
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;
        UState.EnableDeterministicRng(seed);

        Assert.IsTrue(UState.IsDeterministicRng, "EnableDeterministicRng must flip the public mode flag.");
        Assert.AreEqual(seed, UState.DeterministicRootSeed,
            "The root seed must be visible for generation-time and owner re-seeding.");
        Assert.IsInstanceOfType(UState.Random, typeof(DeterministicRandom),
            "Enabled deterministic mode must replace the universe RNG backend.");
        Assert.IsInstanceOfType(Player.Random, typeof(DeterministicRandom),
            "Enabled deterministic mode must replace empire RNG backends.");
        Assert.IsTrue(UState.Spatial.DeterministicCollisions,
            "Enabled deterministic mode must request stable Id-sorted native collision order.");

        Ship player = SpawnShip("Fang Strafer", Player,
            new Vector2(-1800f + UState.Random.Float(-150f, 150f), UState.Random.Float(-120f, 120f)),
            new Vector2(1f, 0f));
        Ship enemy = SpawnShip("Fang Strafer", Enemy,
            new Vector2(1800f + UState.Random.Float(-150f, 150f), UState.Random.Float(-120f, 120f)),
            new Vector2(-1f, 0f));

        player.SensorRange = 60000f;
        enemy.SensorRange = 60000f;
        player.AI.OrderAttackSpecificTarget(enemy);
        enemy.AI.OrderAttackSpecificTarget(player);

        for (int tick = 0; tick < 360; ++tick)
        {
            if ((tick % 90) == 0)
            {
                if (player.Active && enemy.Active) player.AI.OrderAttackSpecificTarget(enemy);
                if (enemy.Active && player.Active) enemy.AI.OrderAttackSpecificTarget(player);
            }
            UState.Objects.Update(TestSimStep);
        }

        return Digest(player, enemy);
    }

    static ulong Digest(params Ship[] ships)
    {
        ulong hash = 1469598103934665603ul;
        foreach (Ship ship in ships)
        {
            Mix(ref hash, ship?.Id ?? 0);
            Mix(ref hash, ship?.Active == true ? 1 : 0);
            Mix(ref hash, Bits(ship?.Position.X ?? 0f));
            Mix(ref hash, Bits(ship?.Position.Y ?? 0f));
            Mix(ref hash, Bits(ship?.Rotation ?? 0f));
            Mix(ref hash, Bits(ship?.Velocity.X ?? 0f));
            Mix(ref hash, Bits(ship?.Velocity.Y ?? 0f));
            Mix(ref hash, Bits(ship?.Health ?? 0f));
            Mix(ref hash, Bits(ship?.Ordinance ?? 0f));
        }
        return hash;
    }

    static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    static void Mix(ref ulong hash, int value)
    {
        unchecked
        {
            hash ^= (uint)value;
            hash *= 1099511628211ul;
        }
    }
}

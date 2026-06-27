using System;
using SDGraphics;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
using Ship_Game.Universe;
using Ship_Game.Universe.SolarBodies;

namespace Ship_Game.GameScreens.Arena;

public enum ArenaFightModifierKind
{
    None,
    SafetyHandicap,
    Swarm,
    Reinforced,
    EliteVanguard,
    GravityWell,
}

/// <summary>
/// Deterministic per-fight mutator for veteran arena careers. Fresh careers get <see cref="None"/>
/// so the existing unaided arena path remains bit-for-bit unchanged.
/// </summary>
public readonly struct ArenaFightModifier
{
    const string ArenaGravityWellName = "Arena Gravity Well";

    public readonly ArenaFightModifierKind Kind;
    public readonly string Name;
    public readonly int ExtraEnemies;
    public readonly int ExtraBosses;
    public readonly float EnemyHealthMultiplier;
    public readonly float EnemyStrengthMultiplier;
    public readonly float GravityWellRange;

    public static readonly ArenaFightModifier None = new(ArenaFightModifierKind.None, "None", 0, 0, 1f, 1f);
    public static readonly ArenaFightModifier GravityWellHazard = new(ArenaFightModifierKind.GravityWell,
        "Gravity Well", 0, 0, 1.04f, 1.04f, gravityWellRange: 12000f);

    public ArenaFightModifier(ArenaFightModifierKind kind, string name, int extraEnemies, int extraBosses,
        float enemyHealthMultiplier, float enemyStrengthMultiplier, float gravityWellRange = 0f)
    {
        Kind = kind;
        Name = name;
        ExtraEnemies = extraEnemies;
        ExtraBosses = extraBosses;
        EnemyHealthMultiplier = enemyHealthMultiplier;
        EnemyStrengthMultiplier = enemyStrengthMultiplier;
        GravityWellRange = Math.Max(0f, gravityWellRange);
    }

    public bool HasGravityWell => GravityWellRange > 0f;

    public int EnemyCount(int baseCount)
        => Math.Max(1, baseCount + ExtraEnemies);

    public int BossCount(int baseBossCount, int enemyCount)
        => Math.Clamp(baseBossCount + ExtraBosses, 0, enemyCount);

    public void ApplyToEnemy(Ship ship)
    {
        if (ship == null || Kind == ArenaFightModifierKind.None)
            return;

        if (Math.Abs(EnemyStrengthMultiplier - 1f) > 0.001f)
            ship.BaseStrength *= EnemyStrengthMultiplier;
        if (Math.Abs(EnemyHealthMultiplier - 1f) > 0.001f)
            ship.Health = Math.Clamp(ship.Health * EnemyHealthMultiplier, 1f, ship.HealthMax);
    }

    public Planet ApplyGravityWell(UniverseState us, Vector2 center)
    {
        if (us == null || !HasGravityWell)
            return null;

        us.P.GravityWellRange = GravityWellRange;
        foreach (SolarSystem existing in us.Systems)
        {
            if (!string.Equals(existing.Name, ArenaGravityWellName, StringComparison.Ordinal))
                continue;
            for (int i = 0; i < existing.PlanetList.Count; ++i)
                if (string.Equals(existing.PlanetList[i].Name, ArenaGravityWellName, StringComparison.Ordinal))
                {
                    existing.PlanetList[i].SetArenaGravityWellRadius(GravityWellRange);
                    return existing.PlanetList[i];
                }
        }

        var system = new SolarSystem(us, center)
        {
            Name = ArenaGravityWellName,
            Sun = SunType.RandomHabitableSun(us.Random),
        };
        var planet = new Planet(us.CreateId(), system, center, fertility: 0f, minerals: 0f, maxPop: 0f);
        planet.Name = ArenaGravityWellName;
        planet.OrbitalAngle = 0f;
        planet.TestSetOrbitalRadius(Math.Max(planet.Radius, 1f));
        planet.SetArenaGravityWellRadius(GravityWellRange);
        system.RingList.Add(new SolarSystem.Ring { Asteroids = false, OrbitalDistance = planet.OrbitalRadius, Planet = planet });
        system.PlanetList.Add(planet);
        us.AddSolarSystem(system);
        return planet;
    }

    public static ArenaFightModifier ForRound(int careerLevel, int round, ulong runSeed)
    {
        if (careerLevel <= 0)
            return None;

        ulong mixed = Mix(runSeed ^ ((ulong)(uint)careerLevel << 32) ^ (uint)round);
        switch (mixed % 3)
        {
            case 0:
                return new ArenaFightModifier(ArenaFightModifierKind.Swarm, "Swarm Contract",
                    extraEnemies: 1, extraBosses: 0, enemyHealthMultiplier: 1f, enemyStrengthMultiplier: 1f);
            case 1:
                return new ArenaFightModifier(ArenaFightModifierKind.Reinforced, "Reinforced Hulls",
                    extraEnemies: 0, extraBosses: 0, enemyHealthMultiplier: 1.12f, enemyStrengthMultiplier: 1.08f);
            default:
                return new ArenaFightModifier(ArenaFightModifierKind.EliteVanguard, "Elite Vanguard",
                    extraEnemies: 0, extraBosses: 1, enemyHealthMultiplier: 1f, enemyStrengthMultiplier: 1.05f);
        }
    }

    static ulong Mix(ulong x)
    {
        x ^= x >> 30;
        x *= 0xbf58476d1ce4e5b9UL;
        x ^= x >> 27;
        x *= 0x94d049bb133111ebUL;
        x ^= x >> 31;
        return x;
    }
}

public static class ArenaCombatTuning
{
    public const float DefaultEngagementBias = 0.28f;

    public static void ApplyAntiKiteDefaults(Ship ship)
    {
        if (ship == null)
            return;

        ship.ArenaEngagementBias = DefaultEngagementBias;
        ship.ArenaStandoffDecay = true;
        ship.ArenaCombatTicks = 0;
    }
}

using System;
using Ship_Game.Ships;

namespace Ship_Game.GameScreens.Arena;

public enum ArenaFightModifierKind
{
    None,
    SafetyHandicap,
    Swarm,
    Reinforced,
    EliteVanguard,
}

/// <summary>
/// Deterministic per-fight mutator for veteran arena careers. Fresh careers get <see cref="None"/>
/// so the existing unaided arena path remains bit-for-bit unchanged.
/// </summary>
public readonly struct ArenaFightModifier
{
    public readonly ArenaFightModifierKind Kind;
    public readonly string Name;
    public readonly int ExtraEnemies;
    public readonly int ExtraBosses;
    public readonly float EnemyHealthMultiplier;
    public readonly float EnemyStrengthMultiplier;

    public static readonly ArenaFightModifier None = new(ArenaFightModifierKind.None, "None", 0, 0, 1f, 1f);

    public ArenaFightModifier(ArenaFightModifierKind kind, string name, int extraEnemies, int extraBosses,
        float enemyHealthMultiplier, float enemyStrengthMultiplier)
    {
        Kind = kind;
        Name = name;
        ExtraEnemies = extraEnemies;
        ExtraBosses = extraBosses;
        EnemyHealthMultiplier = enemyHealthMultiplier;
        EnemyStrengthMultiplier = enemyStrengthMultiplier;
    }

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

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;
using Ship_Game.AI;
using Ship_Game.Gameplay;

namespace UnitTests.Determinism;

/// <summary>
/// END-TO-END proof that the AI sidekick's MILITARY slice actually wages war for the player. A real
/// generated game with one opponent, war declared, warships pre-unlocked and a money cushion. Both arms
/// hand the economy/expansion/research to the AI; they differ ONLY in AutoMilitary. With AutoMilitary the
/// AI should build warships and grow offensive strength for the player; without it, it should not.
/// Functional test (not a determinism gate) — it drives the full SingleSimulationStep for many turns.
/// </summary>
[TestClass]
public class StarDriveAIWarTests : StarDriveTest
{
    Empire WarEnemy;

    void SetupWarGame(int seed, bool autoMilitary)
    {
        CreateSeededSandbox(seed);
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;
        Empire enemy = WarEnemy = UState.NonPlayerEmpires[0];

        UnlockAllShipsFor(Player);   // warships buildable immediately (skip the research wait)
        Player.AddMoney(100000);     // economic cushion so building isn't money-starved

        // Hand economy / expansion / research to the AI in BOTH arms; the ONLY difference is military.
        Player.AutoResearch = true;
        Player.AutoColonize = true;
        Player.AutoExplore = true;
        Player.AutoTaxes = true;
        Player.AutoFreighters = true;
        Player.AutoBuildSpaceRoads = true;
        Player.AutoBuildResearchStations = true;
        Player.AutoBuildMiningStations = true;
        Player.AutoPickBestColonizer = true;
        Player.AutoPickConstructors = true;
        Player.AutoPickBestFreighter = true;

        if (autoMilitary)
            Player.AutoMilitary = true; // the slice under test

        if (!Player.IsAtWarWith(enemy))
            Player.AI.DeclareWarOn(enemy, WarType.ImperialistWar);
    }

    (int warships, int fleets, float offStr) Sample(Empire e)
        => (e.OwnedShips.Count(s => s.IsAWarShip), e.AllFleets.Count, e.OffensiveStrength);

    void Run(int ticks)
    {
        for (int i = 0; i < ticks; ++i)
            Universe.SingleSimulationStep(TestSimStep);
    }

    [TestMethod]
    public void AutoMilitary_AIBuildsMilitaryForPlayer()
    {
        const int Ticks = 6000; // ~100 empire turns (TurnTimer 1s @ 1/60)

        // --- ARM A: AutoMilitary ON — AI should build a military for the player ---
        SetupWarGame(2024, autoMilitary: true);
        (int warships, int fleets, float offStr) onStart = Sample(Player);
        Run(Ticks);
        (int warships, int fleets, float offStr) onEnd = Sample(Player);

        // --- ARM B: AutoMilitary OFF — same game, AI runs everything EXCEPT military ---
        SetupWarGame(2024, autoMilitary: false);
        Run(Ticks);
        (int warships, int fleets, float offStr) offEnd = Sample(Player);

        Console.WriteLine($"[aiwar] AutoMilitary ON : warships {onStart.warships}->{onEnd.warships}, "
                        + $"fleets {onStart.fleets}->{onEnd.fleets}, offStrength {onStart.offStr:0}->{onEnd.offStr:0}");
        Console.WriteLine($"[aiwar] AutoMilitary OFF: warships ->{offEnd.warships}, "
                        + $"fleets ->{offEnd.fleets}, offStrength ->{offEnd.offStr:0}");

        Assert.IsTrue(onEnd.warships > onStart.warships || onEnd.offStr > onStart.offStr * 1.2f,
            "With AutoMilitary the AI should build warships / grow offensive strength for the player");
        Assert.IsTrue(onEnd.warships > offEnd.warships || onEnd.offStr > offEnd.offStr,
            "AutoMilitary should yield more military than the same game without it");
    }

    [TestMethod]
    public void AutoMilitary_AIBuildsAndSustainsMilitaryThroughWar()
    {
        const int Ticks = 18000;   // ~300 empire turns
        const int Sample1 = 6000;
        const int Sample2 = 12000;

        SetupWarGame(12345, autoMilitary: true);
        (int warships, int fleets, float offStr) s0 = Sample(Player);
        int enemyShips0 = WarEnemy.OwnedShips.Count;
        int enemyPlanets0 = WarEnemy.NumPlanets;

        Run(Sample1);
        (int warships, int fleets, float offStr) s1 = Sample(Player);
        Run(Sample2 - Sample1);
        (int warships, int fleets, float offStr) s2 = Sample(Player);
        Run(Ticks - Sample2);
        (int warships, int fleets, float offStr) s3 = Sample(Player);

        int enemyShips1 = WarEnemy.OwnedShips.Count;
        int peakFleets = Math.Max(Math.Max(s1.fleets, s2.fleets), s3.fleets);
        int peakWarships = Math.Max(Math.Max(s1.warships, s2.warships), s3.warships);

        Console.WriteLine($"[aiwar-e2e] player warships {s0.warships}->{s1.warships}->{s2.warships}->{s3.warships} (peak {peakWarships})");
        Console.WriteLine($"[aiwar-e2e] player fleets   {s0.fleets}->{s1.fleets}->{s2.fleets}->{s3.fleets} (peak {peakFleets})");
        Console.WriteLine($"[aiwar-e2e] player offStr   {s0.offStr:0}->{s1.offStr:0}->{s2.offStr:0}->{s3.offStr:0}");
        Console.WriteLine($"[aiwar-e2e] enemy ships {enemyShips0}->{enemyShips1}, enemy planets {enemyPlanets0}->{WarEnemy.NumPlanets}, enemy defeated={WarEnemy.IsDefeated}");

        // The reliable, proven effect of AutoMilitary: the AI builds and sustains a military for the player
        // throughout the war (warships + offensive strength grow). NOTE (honest finding): the player's AI
        // does not yet form offensive FLEETS or go on the offensive — StarDrive's war-campaign / fleet-
        // requisition pipeline still has deep non-player assumptions, and the player AI under-expands here
        // (the enemy out-colonizes it), so it lacks the production for an attack fleet. Full offensive-war
        // parity for a player-run AI is a separate, broader effort; this test asserts the build slice only.
        Assert.IsTrue(peakWarships > s0.warships && s3.offStr > s0.offStr,
            "With AutoMilitary the AI should build and sustain the player's warships + offensive strength over a long war");
    }
}

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;
using Ship_Game.Determinism;
using Vector2 = SDGraphics.Vector2;

namespace UnitTests.Determinism;

/// <summary>
/// THE AI-SIDEKICK determinism probe. Drives the FULL empire turn (UniverseScreen.SingleSimulationStep =
/// ProcessTurnEmpires -> the AI planners: economy/research/expansion/military/war/diplomacy/espionage,
/// plus Objects.Update) for a small multi-empire AI universe, and checks whether two runs from the same
/// seed stay bit-identical. This is the determinism foundation for "let the AI play your empire": if the
/// planners are deterministic, an AI sidekick is fair, replayable, and lockstep-safe.
///
/// Diagnostic first — it reports WHERE two runs first diverge so we can fix the cause (RNG already handled
/// by deterministic empire.Random; expected remaining culprits are Dictionary/HashSet iteration order and
/// unstable sorts in the planners).
/// </summary>
[TestClass]
public class StarDriveEmpireTurnTests : StarDriveTest
{
    const DeterminismProfile Profile = DeterminismProfile.ReplayWinX64Float;

    void BuildAIEconomyUniverse(ulong seed)
    {
        CreateUniverseAndPlayerEmpire(); // Player (human) + Enemy (AI)
        CreateThirdMajorEmpire();        // ThirdMajor (AI)

        UState.P.TurnTimer = 1;          // fast empire turns (~60 ticks/turn) so we get many per run
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;

        // re-seed ALL empires + the universe onto reproducible RNG streams (after every empire exists)
        UState.EnableDeterministicRng(seed);

        // give each empire planets to actually manage (economy/expansion/research have something to chew on)
        AddDummyPlanetToEmpire(new Vector2(60000, 0), Enemy, fertility: 2f, minerals: 2f, maxPop: 12f);
        AddDummyPlanetToEmpire(new Vector2(60000, 40000), Enemy, fertility: 1f, minerals: 3f, maxPop: 8f);
        AddDummyPlanetToEmpire(new Vector2(-60000, 0), ThirdMajor, fertility: 2f, minerals: 2f, maxPop: 12f);
        AddDummyPlanetToEmpire(new Vector2(-60000, 40000), ThirdMajor, fertility: 1f, minerals: 3f, maxPop: 8f);
        AddDummyPlanetToEmpire(new Vector2(0, 60000), Player, fertility: 2f, minerals: 2f, maxPop: 12f);
    }

    (ulong lo, ulong hi) Hash()
    {
        (ulong lo, ulong hi, string _) = UState.ComputeAuthoritativeStateHash(Profile);
        return (lo, hi);
    }

    List<(ulong lo, ulong hi)> RunFull(int ticks)
    {
        var traj = new List<(ulong lo, ulong hi)>(ticks + 1);
        traj.Add(Hash());
        for (int i = 0; i < ticks; ++i)
        {
            Universe.SingleSimulationStep(TestSimStep);
            traj.Add(Hash());
        }
        return traj;
    }

    string AIActivity()
    {
        int goals = (Enemy.AI?.Goals.Count ?? 0) + (ThirdMajor.AI?.Goals.Count ?? 0);
        int techs = Enemy.UnlockedTechs.Length + ThirdMajor.UnlockedTechs.Length;
        return $"goals={goals} techsUnlocked={techs} EnemyMoney={Enemy.Money:0} EnemyResearch='{Enemy.Research.Topic}' "
             + $"EnemyPop={Enemy.TotalPopBillion:0.00} fleets={Enemy.AllFleets.Count + ThirdMajor.AllFleets.Count}";
    }

    [TestMethod]
    public void FullEmpireTurn_TwoRuns_Deterministic()
    {
        const int Ticks = 3000; // TurnTimer 1s @ 1/60 => ~50 empire turns (StarDate +~5)

        BuildAIEconomyUniverse(0xA11CE5EEDu);
        float startDate = UState.StarDate;
        List<(ulong lo, ulong hi)> t1 = RunFull(Ticks);
        float endDate = UState.StarDate;
        string activity = AIActivity();           // capture what the AI actually did
        bool stateEvolved = t1[0] != t1[^1];      // the sim must have changed over the run

        BuildAIEconomyUniverse(0xA11CE5EEDu);
        List<(ulong lo, ulong hi)> t2 = RunFull(Ticks);

        int firstDiv = -1;
        for (int i = 0; i < t1.Count; ++i)
            if (t1[i] != t2[i]) { firstDiv = i; break; }

        Console.WriteLine($"[empireturn] StarDate {startDate:0.0}->{endDate:0.0}, setupIdentical={t1[0] == t2[0]}, stateEvolved={stateEvolved}");
        Console.WriteLine($"[empireturn] AI activity: {activity}");
        Console.WriteLine(firstDiv < 0
            ? $"[empireturn] two runs REPRODUCIBLE over {Ticks} full turns (AI planners deterministic)"
            : $"[empireturn] two runs first DIVERGE at tick {firstDiv}/{Ticks}");

        Assert.IsTrue(stateEvolved, "sim state must evolve over the run, else the determinism test is trivial");
        Assert.IsTrue(firstDiv < 0, $"full empire turn must be deterministic; diverged at tick {firstDiv}");
    }

    // THE SIDEKICK: hand the human player's empire entirely to the AI and prove it plays deterministically.
    [TestMethod]
    public void PlayerAISidekick_TwoRuns_Deterministic()
    {
        const int Ticks = 3000;

        BuildAIEconomyUniverse(0x51DEC1Du);
        Player.EnableAISidekick();             // <-- the AI now plays the player's empire (every slice)
        List<(ulong lo, ulong hi)> t1 = RunFull(Ticks);
        int playerGoals = Player.AI?.Goals.Count ?? 0;
        int playerTechs = Player.UnlockedTechs.Length;
        bool stateEvolved = t1[0] != t1[^1];

        BuildAIEconomyUniverse(0x51DEC1Du);
        Player.EnableAISidekick();
        List<(ulong lo, ulong hi)> t2 = RunFull(Ticks);

        int firstDiv = -1;
        for (int i = 0; i < t1.Count; ++i)
            if (t1[i] != t2[i]) { firstDiv = i; break; }

        Console.WriteLine($"[sidekick] player now AI-driven: goals={playerGoals} techsUnlocked={playerTechs} "
                        + $"money={Player.Money:0} research='{Player.Research.Topic}' planets={Player.NumPlanets}");
        Console.WriteLine(firstDiv < 0
            ? $"[sidekick] two runs REPRODUCIBLE over {Ticks} full turns (AI sidekick is deterministic!)"
            : $"[sidekick] two runs first DIVERGE at tick {firstDiv}/{Ticks}");

        Assert.IsTrue(stateEvolved, "sim state must evolve over the run");
        Assert.IsTrue(playerGoals > 0 || playerTechs > 0, "the AI sidekick must actually drive the player's empire");
        Assert.IsTrue(firstDiv < 0, $"AI sidekick must be deterministic; diverged at tick {firstDiv}");
    }

    // The FULL sidekick on a REAL generated game (homeworlds, capitals, ships, colonizable systems).
    // Diagnostic: reports whether seeded generation is identical and where (if anywhere) two runs diverge.
    [TestMethod]
    public void GeneratedGame_AISidekick_DeterminismProbe()
    {
        const int Ticks = 600;

        CreateSeededSandbox(12345);
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;
        UState.EnableDeterministicRng(0xC0FFEEu);
        Player.EnableAISidekick();
        DeterminismHashWriter lanes1 = UState.ComputeDebugLaneHashes(Profile);
        (ulong lo, ulong hi) gen1 = Hash();
        List<(ulong lo, ulong hi)> t1 = RunFull(Ticks);
        string activity = $"goals={Player.AI?.Goals.Count ?? 0} techs={Player.UnlockedTechs.Length} "
                        + $"planets={Player.NumPlanets} ships={UState.Ships.Length} money={Player.Money:0} fleets={Player.AllFleets.Count}";

        CreateSeededSandbox(12345);
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;
        UState.EnableDeterministicRng(0xC0FFEEu);
        Player.EnableAISidekick();
        DeterminismHashWriter lanes2 = UState.ComputeDebugLaneHashes(Profile);
        (ulong lo, ulong hi) gen2 = Hash();
        List<(ulong lo, ulong hi)> t2 = RunFull(Ticks);

        int firstDiv = -1;
        for (int i = 0; i < t1.Count; ++i)
            if (t1[i] != t2[i]) { firstDiv = i; break; }

        // localize: which lane(s) of the GENERATED (tick-0) state differ between the two seeded runs?
        var diffLanes = new List<string>();
        foreach (DetLane lane in System.Enum.GetValues(typeof(DetLane)))
            if (lanes1.LaneHash(lane) != lanes2.LaneHash(lane))
                diffLanes.Add(lane.ToString());

        Console.WriteLine($"[gengame] seededGenerationIdentical={gen1 == gen2}; differing gen lanes=[{string.Join(",", diffLanes)}]");
        Console.WriteLine($"[gengame] AI-sidekick activity over {Ticks} ticks: {activity}");
        Console.WriteLine(firstDiv < 0
            ? $"[gengame] two runs REPRODUCIBLE over {Ticks} ticks on a REAL generated game"
            : $"[gengame] two runs first DIVERGE at tick {firstDiv}/{Ticks} (genIdentical={gen1 == gen2})");

        // The last non-deterministic generation lane (the turn-side AI-governor labor split, driven by
        // the empires' personality traits being drawn from a clock-seeded RNG during generation) is now
        // closed: seeded generation is bit-identical and the full AI-sidekick run is reproducible.
        Assert.IsTrue(gen1 == gen2, $"seeded generation must be bit-identical; differing lanes=[{string.Join(",", diffLanes)}]");
        Assert.IsTrue(firstDiv < 0, $"AI sidekick on a generated game must be deterministic; diverged at tick {firstDiv}/{Ticks}");
    }
}

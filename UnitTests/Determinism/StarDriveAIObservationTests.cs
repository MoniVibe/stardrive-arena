using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;

namespace UnitTests.Determinism;

/// <summary>
/// Observation harness to judge HOW WELL the AI plays. Runs a real generated game with several AI empires
/// and prints a per-empire timeline of the decisions that matter — expansion (planets/pop), economy
/// (money/income — is it hoarding?), research (techs + current topic), military (warships/fleets/strength),
/// goal load, and wars. Not an assertion gate; it exists to read the AI's actual behavior.
/// </summary>
[TestClass]
public class StarDriveAIObservationTests : StarDriveTest
{
    [TestMethod]
    public void ObserveAIPlay_Timeline()
    {
        CreateSeededSandbox(98765, numOpponents: 3, galSize: GalSize.Small);
        UState.Objects.EnableParallelUpdate = false;
        UState.Paused = false;
        Player.EnableAISidekick(); // make the human empire AI-run too, so every empire is a player for the AI

        Empire[] empires = UState.MajorEmpires.ToArray();
        Console.WriteLine($"[aiq] galaxy: {UState.Systems.Count} systems, {UState.Planets.Count} planets, {empires.Length} major empires");

        const int Samples = 12;
        const int TicksPerSample = 1500; // ~25 turns per sample (~300 turns total)
        for (int s = 0; s <= Samples; ++s)
        {
            if (s > 0)
                for (int i = 0; i < TicksPerSample; ++i)
                    Universe.SingleSimulationStep(TestSimStep);

            foreach (Empire e in empires)
            {
                if (e.IsDefeated) { Console.WriteLine($"[aiq] SD{UState.StarDate:0.0} {e.Name,-14} DEFEATED"); continue; }
                int warships = e.OwnedShips.Count(sh => sh.IsAWarShip);
                int atWar = empires.Count(o => o != e && e.IsAtWarWith(o));
                Console.WriteLine(
                    $"[aiq] SD{UState.StarDate,6:0.0} {(e.isPlayer ? "P*" : "AI")} {e.Name,-14} " +
                    $"plnts={e.NumPlanets,2} pop={e.TotalPopBillion,5:0.0} money={e.Money,7:0} inc={e.NetPlanetIncomes,5:0} " +
                    $"techs={e.UnlockedTechs.Length,3} rsch='{Trunc(e.Research.Topic, 18),-18}' " +
                    $"wars hips={warships,2} fleets={e.AllFleets.Count} offStr={e.OffensiveStrength,6:0} " +
                    $"goals={e.AI.Goals.Count,2} atWar={atWar}");
            }
            Console.WriteLine("[aiq] ----");
        }
        Assert.IsTrue(true); // observational
    }

    static string Trunc(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n));
}

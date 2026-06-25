using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game.GameScreens.Arena;

namespace UnitTests.Determinism;

[TestClass]
public class NetworkLockstepProbeTests : StarDriveTest
{
    [TestMethod]
    public void TwoMachineLockstepProbe_Manual()
    {
        string role = Environment.GetEnvironmentVariable("SD_MP_ROLE") ?? "";
        if (string.IsNullOrEmpty(role))
            Assert.Inconclusive("Set SD_MP_ROLE=host or SD_MP_ROLE=join to run the manual two-machine probe.");

        LoadAllGameData();
        string tempPath = Path.Combine(Path.GetTempPath(), $"sd_mp_probe_{Guid.NewGuid():N}.yaml");
        ArenaFightScreen.CareerSavePath = tempPath;
        ArenaFightScreen.PendingPlayerDesignName = null;

        try
        {
            var settings = new ArenaMultiplayerSettings
            {
                MatchSeed = EnvInt("SD_MP_SEED", 0x5EED),
                RngSeed = EnvUInt("SD_MP_RNG", 0xA12EA000u),
                InputDelay = EnvInt("SD_MP_INPUT_DELAY", 3),
                MaxTurns = EnvInt("SD_MP_TURNS", 420),
                CommandEveryTurns = 1,
            };
            int port = EnvInt("SD_MP_PORT", ArenaMultiplayerSession.DefaultPort);

            ArenaMultiplayerRunResult result;
            if (role.Equals("host", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[mp-probe] HOST port={port} seed=0x{settings.MatchSeed:X8} rng=0x{settings.RngSeed:X8} turns={settings.MaxTurns}");
                result = ArenaMultiplayerSession.RunNetworkHost(settings, port, Console.WriteLine);
            }
            else if (role.Equals("join", StringComparison.OrdinalIgnoreCase))
            {
                string host = Environment.GetEnvironmentVariable("SD_MP_HOST");
                Assert.IsFalse(string.IsNullOrEmpty(host), "SD_MP_HOST must name the host IP/address when SD_MP_ROLE=join.");
                Console.WriteLine($"[mp-probe] JOIN host={host} port={port}");
                result = ArenaMultiplayerSession.RunNetworkJoin(host, port, Console.WriteLine);
            }
            else
            {
                Assert.Fail("SD_MP_ROLE must be host or join.");
                return;
            }

            Assert.IsFalse(result.Desynced,
                $"Network Arena lockstep desynced at turn {result.DesyncTurn}: {result.DesyncReason}");
            Assert.AreEqual(settings.MaxTurns, result.TurnsCompleted);
            ArenaMultiplayerTurnHash final = result.TurnHashes.Last();
            Console.WriteLine($"[mp-probe] COMPLETE turns={result.TurnsCompleted} " +
                              $"finalHash=0x{final.HostHi:X16}:0x{final.HostLo:X16} " +
                              $"commands={result.CommandsSubmitted}");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    static int EnvInt(string name, int fallback)
    {
        string value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
            return fallback;
        NumberStyles style = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? NumberStyles.HexNumber
            : NumberStyles.Integer;
        string text = style == NumberStyles.HexNumber ? value[2..] : value;
        return int.Parse(text, style, CultureInfo.InvariantCulture);
    }

    static uint EnvUInt(string name, uint fallback)
    {
        string value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
            return fallback;
        NumberStyles style = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? NumberStyles.HexNumber
            : NumberStyles.Integer;
        string text = style == NumberStyles.HexNumber ? value[2..] : value;
        return uint.Parse(text, style, CultureInfo.InvariantCulture);
    }
}

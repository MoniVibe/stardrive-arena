using System;
using StarDrive.Tools.ArenaLockstepProbe;

PortableLockstepOptions options;
try
{
    options = PortableLockstepOptions.FromArgsAndEnvironment(args, AppContext.BaseDirectory);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(PortableLockstepOptions.Usage);
    return 2;
}

if (options.ShowHelp)
{
    Console.WriteLine(PortableLockstepOptions.Usage);
    return 0;
}

try
{
    if (options.SelfTest)
    {
        PortableLockstepSelfTestResult result = PortableLockstepRunner.RunSelfTest(options);
        Console.WriteLine("[arena-lockstep] self-test passed");
        Console.WriteLine($"[arena-lockstep] inProcessFinal={result.InProcess.FinalHash}");
        Console.WriteLine($"[arena-lockstep] loopbackHostFinal={result.LoopbackHost.FinalHash}");
        Console.WriteLine($"[arena-lockstep] loopbackJoinFinal={result.LoopbackJoin.FinalHash}");
        Console.WriteLine($"[arena-lockstep] forcedDesyncTurn={result.ForcedDesync.DesyncTurn}");
        return 0;
    }

    PortableLockstepResult run = options.Role switch
    {
        PortableLockstepRole.Host => PortableLockstepRunner.RunHost(options, Console.WriteLine),
        PortableLockstepRole.Join => PortableLockstepRunner.RunJoin(options, Console.WriteLine),
        _ => throw new ArgumentException("Set --role host|join or SD_MP_ROLE=host|join."),
    };

    Console.WriteLine($"[arena-lockstep] complete role={options.Role} turns={run.TurnsCompleted} " +
                      $"desynced={run.Desynced} final={run.FinalHash} sequence={run.SequenceSha256}");
    return run.Desynced ? 3 : 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("[arena-lockstep] failed:");
    Console.Error.WriteLine(ex);
    return 1;
}

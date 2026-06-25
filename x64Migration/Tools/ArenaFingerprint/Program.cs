using System;
using StarDrive.Tools.ArenaFingerprint;

PortableFingerprintOptions options;
try
{
    options = PortableFingerprintOptions.FromArgs(args, AppContext.BaseDirectory);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(PortableFingerprintOptions.Usage);
    return 2;
}

if (options.ShowHelp)
{
    Console.WriteLine(PortableFingerprintOptions.Usage);
    return 0;
}

try
{
    if (options.SelfTest)
    {
        PortableFingerprintSelfTestResult result = PortableFingerprintRunner.RunSelfTest(options);
        Console.WriteLine("[arena-fingerprint] self-test passed");
        Console.WriteLine($"[arena-fingerprint] sameSeedDigest={result.Baseline.SequenceSha256}");
        Console.WriteLine($"[arena-fingerprint] differentSeedDigest={result.DifferentSeed.SequenceSha256}");
        Console.WriteLine($"[arena-fingerprint] path={result.OutputPath}");
        return 0;
    }

    PortableFingerprintRun run = PortableFingerprintRunner.Run(options);
    string path = PortableFingerprintRunner.Write(run, options.OutputPath);
    Console.WriteLine($"[arena-fingerprint] path={path}");
    Console.WriteLine($"[arena-fingerprint] sequenceSha256={run.SequenceSha256}");
    foreach (string line in run.HeaderLines)
        Console.WriteLine("[arena-fingerprint] " + line);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("[arena-fingerprint] failed:");
    Console.Error.WriteLine(ex);
    return 1;
}

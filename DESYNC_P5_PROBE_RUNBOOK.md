# StarDrive Desync P5 Live Parity Probe

The Phase P5 live-path probe is part of `x64Migration/Tools/Authoritative4XProbe`.
It runs one host plus N total human peers over loopback TCP in a single process,
adds deterministic TCP write hazards, forces one client drift, forces a clean
multi-client resync epoch, then emits a JSON verdict.

## Build

```powershell
dotnet build x64Migration/Tools/Authoritative4XProbe/Authoritative4XProbe.csproj -c Debug -p:Platform=x64
```

If the machine is offline but packages are already restored, use:

```powershell
dotnet build x64Migration/Tools/Authoritative4XProbe/Authoritative4XProbe.csproj -c Debug -p:Platform=x64 --no-restore
```

## Run Scenarios

Pass `--game-root` when the current directory does not contain `Content/`.
The JSON verdict is written under `--output` and also printed to stdout.

Two total peers:

```powershell
dotnet run --project x64Migration/Tools/Authoritative4XProbe/Authoritative4XProbe.csproj -c Debug --no-build -- --live-parity --clients 2 --turns 24 --game-root "C:\Games\StarDrive2" --output .\sim-output\p5-live-parity
```

Four total peers, proving multiple remote clients resync in one epoch:

```powershell
dotnet run --project x64Migration/Tools/Authoritative4XProbe/Authoritative4XProbe.csproj -c Debug --no-build -- --live-parity --clients 4 --turns 30 --game-root "C:\Games\StarDrive2" --output .\sim-output\p5-live-parity
```

Eight total peers:

```powershell
dotnet run --project x64Migration/Tools/Authoritative4XProbe/Authoritative4XProbe.csproj -c Debug --no-build -- --live-parity --clients 8 --turns 30 --game-root "C:\Games\StarDrive2" --output .\sim-output\p5-live-parity
```

Useful hazard overrides:

```powershell
--hazard-latency-ms 12 --hazard-jitter-ms 5 --hazard-burst-every 11 --hazard-burst-delay-ms 50 --seed 54545
```

## Expected JSON Shape

The verdict root includes:

- `Schema`: `stardrive.auth4x.liveParity.v1`
- `Passed`: overall pass/fail
- `Hazard`: configured latency, jitter, burst delay, and observed delay stats
- `ForcedDrift`: drift peer, detection, resync epoch, ack peers, repair flag
- `ForcedResync`: requesting peers, epoch, ack peers, repair flag
- `Clients`: per-peer ticks, mismatches, resyncs, recovery ticks, latency stats, final digest

The probe serializes and parses the verdict before exit. A separate parse check:

```powershell
$json = Get-Content .\sim-output\p5-live-parity\live-parity\auth4x-live-parity-*.json -Raw
$doc = $json | ConvertFrom-Json
if ($doc.Schema -ne 'stardrive.auth4x.liveParity.v1') { throw 'bad schema' }
if (-not $doc.Passed) { throw 'probe failed' }
```

## Standard Suite Gates

```powershell
dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Authoritative4X"
dotnet test UnitTests/SDUnitTests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Arena"
```

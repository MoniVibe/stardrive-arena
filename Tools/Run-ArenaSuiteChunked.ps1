<#
.SYNOPSIS
  Runs the Arena/lockstep test classes each in a FRESH testhost process and aggregates results.

.DESCRIPTION
  The combined single-process "FullyQualifiedName~Arena" run is killed by a pre-existing,
  nondeterministic native AccessViolation in the test host when the heavy graphics render-smoke
  class shares a process with the many TCP-thread lockstep tests (each class passes in isolation).
  This script gives a reliable green gate by running one class per testhost invocation, so a
  cross-test native fault in one class can never invalidate the others.

  Exit code 0 iff every chunk passed. Prints a per-class summary and a final tally.

.PARAMETER Configuration
  Build configuration (default Debug). Assumes the test project is already built (--no-build).

.PARAMETER NoBuild
  Skip the build step (default: build once up front).
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'UnitTests\SDUnitTests.csproj'

# One class per fresh testhost. Order: cheap classes first, the heavy render-smoke class last.
$chunks = @(
    'ArenaMultiplayerLockstepTests',
    'ArenaPortableLockstepProbeTests',
    'ArenaPortableFingerprintTests',
    'ArenaDeterminismPatchContractTests',
    'LockstepNetworkTransportTests',
    'NetworkLockstepProbeTests',
    'ArenaRenderSmokeTests'
)

# Kill any zombie testhost holding blackbox.log before we start (a leftover from a detached run
# makes every test insta-fail at assembly init).
Get-Process testhost -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "[chunked] killing zombie testhost $($_.Id)" -ForegroundColor DarkYellow
    Stop-Process -Id $_.Id -Force
}

if (-not $NoBuild) {
    Write-Host "[chunked] building UnitTests ($Configuration)..." -ForegroundColor Cyan
    dotnet build $proj --no-restore -c $Configuration -v q
    if ($LASTEXITCODE -ne 0) { Write-Host "[chunked] BUILD FAILED" -ForegroundColor Red; exit 1 }
}

$allOk = $true
$totalPass = 0
$totalFail = 0
$summary = @()

foreach ($cls in $chunks) {
    Write-Host "`n[chunked] === $cls ===" -ForegroundColor Cyan
    $out = & dotnet test $proj --no-build -c $Configuration --filter "FullyQualifiedName~$cls" 2>&1
    $line = $out | Select-String -Pattern 'Passed!|Failed!' | Select-Object -Last 1
    Write-Host ($line ? $line.ToString().Trim() : ($out | Select-Object -Last 1))

    $pass = 0; $fail = 0; $skip = 0
    if ($line -and $line -match 'Passed:\s*(\d+)')  { $pass = [int]$Matches[1] }
    if ($line -and $line -match 'Failed:\s*(\d+)')  { $fail = [int]$Matches[1] }
    if ($line -and $line -match 'Skipped:\s*(\d+)') { $skip = [int]$Matches[1] }
    $totalPass += $pass
    $totalFail += $fail

    # A class whose only tests are [Ignore]/manual emits a Skipped-only summary (or none). That is
    # NOT a failure. Treat exit 0 with zero failures as OK regardless of pass count.
    $ok = ($LASTEXITCODE -eq 0) -and ($fail -eq 0)
    if (-not $ok) {
        $allOk = $false
        $summary += "  FAIL  $cls (pass=$pass fail=$fail exit=$LASTEXITCODE)"
    } elseif ($pass -eq 0 -and $skip -gt 0) {
        $summary += "  skip  $cls ($skip skipped, no runnable tests)"
    } else {
        $summary += "  ok    $cls ($pass passed)"
    }
}

Write-Host "`n[chunked] ===== SUMMARY =====" -ForegroundColor Cyan
$summary | ForEach-Object { Write-Host $_ ($_ -match 'FAIL' ? '' : '') -ForegroundColor ($_ -match 'FAIL' ? 'Red' : 'Green') }
Write-Host "[chunked] total: $totalPass passed, $totalFail failed" -ForegroundColor ($allOk ? 'Green' : 'Red')

if ($allOk) { Write-Host "[chunked] ALL ARENA CHUNKS GREEN" -ForegroundColor Green; exit 0 }
else { Write-Host "[chunked] SOME CHUNKS RED" -ForegroundColor Red; exit 1 }

[CmdletBinding()]
param(
    [string]$ClientSsh = "",
    [string]$HostAddress = "127.0.0.1",
    [string]$HostGameRoot = "",
    [string]$ClientGameRoot = "",
    [string]$RemoteRoot = "D:\auth4x-qa",
    [int]$Port = 47377,
    [int]$Turns = 600,
    [int]$Seed = 54545,
    [int]$Iterations = 1,
    [int]$TimeoutSeconds = 240,
    [string]$Configuration = "Debug",
    [switch]$SkipBuild,
    [switch]$SkipDeploy,
    [switch]$SkipRemoteDeploy
)

$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir '..\..')).Path
}

function Invoke-Checked {
    param([string]$FilePath, [string[]]$Arguments, [string]$WorkingDirectory)
    Write-Host ">> $FilePath $($Arguments -join ' ')"
    $p = Start-Process -FilePath $FilePath -ArgumentList $Arguments -WorkingDirectory $WorkingDirectory `
        -NoNewWindow -Wait -PassThru
    if ($p.ExitCode -ne 0) {
        throw "Command failed with exit code $($p.ExitCode): $FilePath $($Arguments -join ' ')"
    }
}

function Join-ArgumentLine {
    param([string[]]$Arguments)
    $quoted = foreach ($arg in $Arguments) {
        if ($null -eq $arg) {
            '""'
        } elseif ($arg -match '[\s"]') {
            '"' + ($arg -replace '"', '\"') + '"'
        } else {
            $arg
        }
    }
    return ($quoted -join ' ')
}

function New-CleanDirectory {
    param([string]$Path)
    if (Test-Path $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Copy-IfDifferent {
    param([string]$Source, [string]$Destination)
    $src = (Resolve-Path $Source).Path
    $dstFull = [System.IO.Path]::GetFullPath($Destination)
    if (Test-Path $dstFull) {
        $dst = (Resolve-Path $dstFull).Path
        if ([string]::Equals($src, $dst, [System.StringComparison]::OrdinalIgnoreCase)) {
            return
        }
    }
    $dir = Split-Path -Parent $dstFull
    if ($dir) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Copy-Item -Force $src $dstFull
}

function Copy-DllBundleToGameRoot {
    param([string]$BundleDir, [string]$GameRoot)
    if ([string]::IsNullOrWhiteSpace($GameRoot)) {
        return
    }
    $root = [System.IO.Path]::GetFullPath($GameRoot)
    if (-not (Test-Path (Join-Path $root 'Content'))) {
        throw "Game root '$root' does not contain Content."
    }
    New-Item -ItemType Directory -Path (Join-Path $root 'Plugins') -Force | Out-Null
    Copy-IfDifferent (Join-Path $BundleDir 'StarDrive.dll') (Join-Path $root 'StarDrive.dll')
    Copy-IfDifferent (Join-Path $BundleDir 'SDUtils.dll') (Join-Path $root 'SDUtils.dll')
    Copy-IfDifferent (Join-Path $BundleDir 'SDGraphics.dll') (Join-Path $root 'SDGraphics.dll')
    Copy-IfDifferent (Join-Path $BundleDir 'SDLockstep.dll') (Join-Path $root 'SDLockstep.dll')
    Copy-IfDifferent (Join-Path $BundleDir 'StarDriveArena.dll') (Join-Path $root 'Plugins\StarDriveArena.dll')
}

function ConvertTo-EncodedPowerShell {
    param([string]$Script)
    return [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Script))
}

function Invoke-RemotePowerShell {
    param([string]$Target, [string]$Script, [string]$StdOut, [string]$StdErr)
    $encoded = ConvertTo-EncodedPowerShell $Script
    $args = @($Target, 'powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded)
    Write-Host ">> ssh $Target powershell <encoded>"
    $p = Start-Process -FilePath 'ssh' -ArgumentList $args -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $StdOut -RedirectStandardError $StdErr
    return $p.ExitCode
}

function Start-ProbeProcess {
    param([string]$Exe, [string[]]$Arguments, [string]$OutFile, [string]$ErrFile)
    Write-Host ">> $Exe $($Arguments -join ' ')"
    return Start-Process -FilePath $Exe -ArgumentList (Join-ArgumentLine $Arguments) -WorkingDirectory (Split-Path -Parent $Exe) `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput $OutFile -RedirectStandardError $ErrFile
}

function Get-QASummary {
    param([string]$Directory)
    $files = Get-ChildItem $Directory -Recurse -File -Include *.txt,*.log -ErrorAction SilentlyContinue
    $summary = [ordered]@{
        Files = $files.Count
        Ok = 0
        Failed = 0
        SyncMismatch = 0
        RawHashDrift = 0
        NetworkError = 0
        ViewPerf = 0
        MaxDrawMs = 0.0
        HostApplied = 0
        JoinApplied = 0
        FailureKind = 'None'
        Evidence = ''
        FirstDiff = ''
        Final = ''
    }

    foreach ($file in $files) {
        foreach ($line in [IO.File]::ReadLines($file.FullName)) {
            if ($line -match '\[auth4x-probe\] OK') {
                $summary.Ok++
                if ($line -match 'final=([^ ]+)') { $summary.Final = $Matches[1] }
            }
            if ($line -match '\[auth4x-probe\] FAILED') {
                $summary.Failed++
                if ($summary.FailureKind -eq 'None') { $summary.FailureKind = 'ProcessFailure'; $summary.Evidence = $line }
            }
            if ($line -match 'applied peer=host') { $summary.HostApplied++ }
            if ($line -match 'applied peer=join') { $summary.JoinApplied++ }
            if ($line -match 'SYNC_MISMATCH|Authoritative sync mismatch') {
                $summary.SyncMismatch++
                $summary.FailureKind = 'SyncMismatch'
                $summary.Evidence = $line
                if ($line -match "firstDiff='([^']+)'") { $summary.FirstDiff = $Matches[1] }
            }
            if ($line -match 'RAW_HASH_DRIFT') { $summary.RawHashDrift++ }
            if ($line -match 'NETWORK_ERROR|connection attempt failed|actively refused|No joiner connected') {
                $summary.NetworkError++
                if ($summary.FailureKind -eq 'None' -or $summary.FailureKind -eq 'ProcessFailure') {
                    $summary.FailureKind = 'Connection'
                    $summary.Evidence = $line
                }
            }
            if ($line -match 'environment mismatch|env mismatch') {
                if ($summary.FailureKind -ne 'SyncMismatch') {
                    $summary.FailureKind = 'EnvironmentMismatch'
                    $summary.Evidence = $line
                }
            }
            if ($line -match 'Host rejected|accepted=False') {
                if ($summary.FailureKind -ne 'SyncMismatch' -and $summary.FailureKind -ne 'EnvironmentMismatch') {
                    $summary.FailureKind = 'CommandRejected'
                    $summary.Evidence = $line
                }
            }
            if ($line -match 'Timed out waiting|timed out|TimeoutException') {
                if ($summary.FailureKind -eq 'None' -or $summary.FailureKind -eq 'ProcessFailure') {
                    $summary.FailureKind = 'Timeout'
                    $summary.Evidence = $line
                }
            }
            if ($line -match 'VIEW_PERF') {
                $summary.ViewPerf++
                if ($line -match 'drawMs=([0-9.]+)') {
                    $draw = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
                    if ($draw -gt $summary.MaxDrawMs) { $summary.MaxDrawMs = $draw }
                }
            }
        }
    }

    return [pscustomobject]$summary
}

$repo = Get-RepoRoot
Set-Location $repo
if ([string]::IsNullOrWhiteSpace($HostGameRoot)) {
    $HostGameRoot = Join-Path $repo 'game'
}
if ([string]::IsNullOrWhiteSpace($ClientGameRoot)) {
    $ClientGameRoot = $HostGameRoot
}
if ($Iterations -lt 1) { throw "Iterations must be >= 1." }
if ($Turns -lt 2) { throw "Turns must be >= 2 so both command-injection assertions run." }

$dist = Join-Path $repo 'dist'
$dllBundle = Join-Path $dist 'arena-pc-dlls'
$dllZip = Join-Path $dist 'arena-pc-dlls.zip'
$probeOut = Join-Path $dist 'authoritative4x-probe'
$probeZip = Join-Path $dist 'authoritative4x-probe.zip'
$runRoot = Join-Path $repo ("sim-output\auth4x-cross-machine-qa\" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

if (-not $SkipBuild) {
    Invoke-Checked 'dotnet' @('build', 'StarDrive.csproj', '-c', $Configuration, '-p:Platform=x64') $repo
    Invoke-Checked 'dotnet' @('publish', 'x64Migration\Tools\Authoritative4XProbe\Authoritative4XProbe.csproj',
        '-c', $Configuration, '-p:Platform=x64', '-r', 'win-x64', '--self-contained', 'false', '-o', $probeOut) $repo
}

New-CleanDirectory $dllBundle
Copy-Item -Force (Join-Path $repo 'game\StarDrive.dll') (Join-Path $dllBundle 'StarDrive.dll')
Copy-Item -Force (Join-Path $repo 'game\SDUtils.dll') (Join-Path $dllBundle 'SDUtils.dll')
Copy-Item -Force (Join-Path $repo 'game\SDGraphics.dll') (Join-Path $dllBundle 'SDGraphics.dll')
Copy-Item -Force (Join-Path $repo 'SDLockstep\bin\x64\Debug\net8.0-windows\SDLockstep.dll') (Join-Path $dllBundle 'SDLockstep.dll')
Copy-Item -Force (Join-Path $repo 'game\Plugins\StarDriveArena.dll') (Join-Path $dllBundle 'StarDriveArena.dll')
if (Test-Path $dllZip) { Remove-Item $dllZip -Force }
Compress-Archive -Path (Join-Path $dllBundle '*') -DestinationPath $dllZip -Force
if (Test-Path $probeZip) { Remove-Item $probeZip -Force }
Compress-Archive -Path (Join-Path $probeOut '*') -DestinationPath $probeZip -Force

if (-not $SkipDeploy) {
    Copy-DllBundleToGameRoot $dllBundle $HostGameRoot
}

if (-not [string]::IsNullOrWhiteSpace($ClientSsh) -and -not $SkipRemoteDeploy) {
    $prepOut = Join-Path $runRoot 'remote-prep.out.txt'
    $prepErr = Join-Path $runRoot 'remote-prep.err.txt'
    $remotePrep = @"
`$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path '$RemoteRoot' -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path '$RemoteRoot' 'incoming') -Force | Out-Null
"@
    $code = Invoke-RemotePowerShell $ClientSsh $remotePrep $prepOut $prepErr
    if ($code -ne 0) { throw "Remote prep failed. See $prepOut / $prepErr" }
    Invoke-Checked 'scp' @($dllZip, "$ClientSsh`:arena-pc-dlls.zip") $repo
    Invoke-Checked 'scp' @($probeZip, "$ClientSsh`:authoritative4x-probe.zip") $repo

    $deployOut = Join-Path $runRoot 'remote-deploy.out.txt'
    $deployErr = Join-Path $runRoot 'remote-deploy.err.txt'
    $remoteShouldDeploy = (-not $SkipDeploy).ToString().ToLowerInvariant()
    $remoteDeploy = @"
`$ErrorActionPreference = 'Stop'
`$root = '$RemoteRoot'
`$dlls = Join-Path `$root 'dlls'
`$probe = Join-Path `$root 'probe'
Remove-Item -LiteralPath `$dlls -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath `$probe -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path `$dlls, `$probe -Force | Out-Null
Expand-Archive -Force -Path (Join-Path `$env:USERPROFILE 'arena-pc-dlls.zip') -DestinationPath `$dlls
Expand-Archive -Force -Path (Join-Path `$env:USERPROFILE 'authoritative4x-probe.zip') -DestinationPath `$probe
if ($remoteShouldDeploy) {
  `$game = '$ClientGameRoot'
  if (-not (Test-Path (Join-Path `$game 'Content'))) { throw "Client game root '`$game' does not contain Content." }
  New-Item -ItemType Directory -Path (Join-Path `$game 'Plugins') -Force | Out-Null
  Copy-Item -Force (Join-Path `$dlls 'StarDrive.dll') (Join-Path `$game 'StarDrive.dll')
  Copy-Item -Force (Join-Path `$dlls 'SDUtils.dll') (Join-Path `$game 'SDUtils.dll')
  Copy-Item -Force (Join-Path `$dlls 'SDGraphics.dll') (Join-Path `$game 'SDGraphics.dll')
  Copy-Item -Force (Join-Path `$dlls 'SDLockstep.dll') (Join-Path `$game 'SDLockstep.dll')
  Copy-Item -Force (Join-Path `$dlls 'StarDriveArena.dll') (Join-Path `$game 'Plugins\StarDriveArena.dll')
}
Get-ChildItem `$dlls | Get-FileHash -Algorithm SHA256 | Format-Table -AutoSize
"@
    $code = Invoke-RemotePowerShell $ClientSsh $remoteDeploy $deployOut $deployErr
    if ($code -ne 0) { throw "Remote deploy failed. See $deployOut / $deployErr" }
}

$probeExe = Join-Path $probeOut 'Authoritative4XProbe.exe'
if (-not (Test-Path $probeExe)) { throw "Missing probe exe '$probeExe'. Run without -SkipBuild first." }

for ($i = 0; $i -lt $Iterations; ++$i) {
    $runId = "run$($i + 1)-seed$($Seed + $i)"
    $runDir = Join-Path $runRoot $runId
    New-Item -ItemType Directory -Path $runDir -Force | Out-Null
    $hostArtifacts = Join-Path $runDir 'host-artifacts'
    $clientArtifacts = Join-Path $runDir 'client-artifacts'
    New-Item -ItemType Directory -Path $hostArtifacts, $clientArtifacts -Force | Out-Null

    $hostOut = Join-Path $runDir 'host.stdout.txt'
    $hostErr = Join-Path $runDir 'host.stderr.txt'
    $hostArgs = @('--role', 'host', '--port', "$Port", '--turns', "$Turns",
        '--timeout', "$TimeoutSeconds", '--seed', "$($Seed + $i)", '--game-root', $HostGameRoot,
        '--output', $hostArtifacts)
    $hostProcess = Start-ProbeProcess $probeExe $hostArgs $hostOut $hostErr
    Start-Sleep -Seconds 2

    if ([string]::IsNullOrWhiteSpace($ClientSsh)) {
        $clientOut = Join-Path $runDir 'client.stdout.txt'
        $clientErr = Join-Path $runDir 'client.stderr.txt'
        $clientArgs = @('--role', 'join', '--host', $HostAddress, '--port', "$Port", '--turns', "$Turns",
            '--timeout', "$TimeoutSeconds", '--seed', "$($Seed + $i)", '--game-root', $ClientGameRoot,
            '--output', $clientArtifacts)
        $client = Start-ProbeProcess $probeExe $clientArgs $clientOut $clientErr
        if (-not $client.WaitForExit(($TimeoutSeconds + 60) * 1000)) {
            $client.Kill()
            throw "Local client probe timed out for $runId."
        }
    } else {
        $clientOut = Join-Path $runDir 'client.ssh.stdout.txt'
        $clientErr = Join-Path $runDir 'client.ssh.stderr.txt'
        $remoteClientDir = Join-Path $RemoteRoot "runs\$runId\client"
        $remoteJoin = @"
`$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path '$remoteClientDir' -Force | Out-Null
& (Join-Path '$RemoteRoot' 'probe\Authoritative4XProbe.exe') --role join --host '$HostAddress' --port $Port --turns $Turns --timeout $TimeoutSeconds --seed $($Seed + $i) --game-root '$ClientGameRoot' --output '$remoteClientDir'
exit `$LASTEXITCODE
"@
        $clientExit = Invoke-RemotePowerShell $ClientSsh $remoteJoin $clientOut $clientErr
        $remoteCollectOut = Join-Path $runDir 'remote-collect.out.txt'
        $remoteCollectErr = Join-Path $runDir 'remote-collect.err.txt'
        $remoteZipName = "auth4x-qa-$runId-client.zip"
        $remoteCollect = @"
`$ErrorActionPreference = 'Stop'
`$zip = Join-Path `$env:USERPROFILE '$remoteZipName'
Remove-Item `$zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Force -Path (Join-Path '$remoteClientDir' '*') -DestinationPath `$zip
"@
        $collectExit = Invoke-RemotePowerShell $ClientSsh $remoteCollect $remoteCollectOut $remoteCollectErr
        if ($collectExit -eq 0) {
            Invoke-Checked 'scp' @("$ClientSsh`:$remoteZipName", (Join-Path $runDir 'client-artifacts.zip')) $repo
            Expand-Archive -Force -Path (Join-Path $runDir 'client-artifacts.zip') -DestinationPath $clientArtifacts
        }
        if ($clientExit -ne 0) {
            Write-Warning "Remote client exited $clientExit for $runId; collected artifacts where possible."
        }
    }

    if (-not $hostProcess.WaitForExit(($TimeoutSeconds + 60) * 1000)) {
        $hostProcess.Kill()
        throw "Host probe timed out for $runId."
    }

    $summary = Get-QASummary $runDir
    $summaryPath = Join-Path $runDir 'qa-summary.txt'
    $summary | Format-List | Out-String | Set-Content -Encoding UTF8 $summaryPath
    Write-Host "QA $runId => $($summary.FailureKind) ok=$($summary.Ok) final=$($summary.Final)"
    Write-Host "summary: $summaryPath"
}

$topSummary = Get-QASummary $runRoot
$topSummaryPath = Join-Path $runRoot 'qa-summary.txt'
$topSummary | Format-List | Out-String | Set-Content -Encoding UTF8 $topSummaryPath
Write-Host "QA artifacts: $runRoot"
Write-Host "QA summary:   $topSummaryPath"
if ($topSummary.FailureKind -ne 'None') {
    throw "Authoritative 4X QA failed: $($topSummary.FailureKind) evidence=$($topSummary.Evidence)"
}

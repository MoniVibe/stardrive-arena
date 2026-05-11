<#
.SYNOPSIS
    Verify Authenticode signatures on Jupiter binaries.

.DESCRIPTION
    Phase 5 §5.1.B + §5.1.D deliverable. Runs `signtool verify /pa /v` against
    each file. Fails with non-zero exit code if any binary is unsigned, has
    an invalid signature, or has a missing/expired RFC 3161 timestamp.

    Designed to be called from release.yml as the gate after signing, and
    locally after sign-binaries.ps1 to confirm the signature applied.

    /pa  = use the Authenticode policy (right one for executables/installers)
    /v   = verbose (writes signer + timestamp + cert chain to stdout)

.PARAMETER Files
    Paths to binaries to verify.

.EXAMPLE
    Deploy/SignedBinaryCheck.ps1 game/StarDrive.exe,game/SDNative.dll, `
                                 Deploy/upload/BlackBox_Jupiter_1.60.00237.exe
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string[]] $Files
)

$ErrorActionPreference = 'Stop'

function Find-SignTool {
    $sdkRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $sdkRoot) {
        $candidates = Get-ChildItem -Path $sdkRoot -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending
        if ($candidates.Count -gt 0) { return $candidates[0].FullName }
    }
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "signtool.exe not found. Install the Windows 10/11 SDK or add signtool to PATH."
}

$signtool = Find-SignTool
Write-Host "Using signtool: $signtool"

$failed = @()

foreach ($f in $Files) {
    if (-not (Test-Path $f)) {
        Write-Warning "File not found: $f"
        $failed += $f
        continue
    }
    Write-Host ""
    Write-Host "=== Verifying $f ==="
    & $signtool verify /pa /v $f
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Signature verification FAILED for $f (signtool exit $LASTEXITCODE)"
        $failed += $f
    }
}

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Error "Signature check failed for $($failed.Count) file(s):`n  $($failed -join "`n  ")"
    exit 1
}

Write-Host "All $($Files.Count) file(s) verified."
exit 0

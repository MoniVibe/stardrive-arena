<#
.SYNOPSIS
    Sign Jupiter binaries via Microsoft Trusted Signing (CI) or a local cert (dev).

.DESCRIPTION
    Phase 5 §5.1.B deliverable. Wraps signtool.exe to apply an Authenticode
    signature with an RFC 3161 timestamp so signatures stay valid past cert
    expiry. See Deploy/SIGNING.md for the procurement story + GitHub Actions
    secret setup.

    Two modes:

    1. Trusted Signing (default for CI). Reads required env vars:
         AZURE_TENANT_ID
         AZURE_CLIENT_ID
         AZURE_CLIENT_SECRET
         TRUSTED_SIGNING_ENDPOINT       e.g. https://eus.codesigning.azure.net/
         TRUSTED_SIGNING_ACCOUNT        Trusted Signing account name
         TRUSTED_SIGNING_CERT_PROFILE   certificate profile name
         TRUSTED_SIGNING_DLIB_PATH      path to Azure.CodeSigning.Dlib.dll

       In GitHub Actions the azure/trusted-signing-action sets these for you;
       this script is a fallback for runs that need direct signtool control or
       for non-GitHub CI environments.

    2. Local mode (-Thumbprint). Signs with a cert installed in the user's
       cert store — typically a self-signed cert created via
       New-SelfSignedCertificate for dry-run testing of the script plumbing
       before the Trusted Signing account is provisioned.

.PARAMETER Files
    Paths (relative to the working directory) of binaries to sign. Each must
    exist when the script runs.

.PARAMETER Thumbprint
    SHA-1 thumbprint of a cert in Cert:\CurrentUser\My or Cert:\LocalMachine\My.
    If supplied, runs in local mode and skips Trusted Signing entirely.

.PARAMETER TimestampUrl
    Override the timestamp service URL. Defaults:
      Trusted Signing: http://timestamp.acs.microsoft.com
      Local:           http://timestamp.digicert.com

.EXAMPLE
    # CI: sign post-build game binaries (env vars provided by Actions secrets)
    Deploy/sign-binaries.ps1 game/StarDrive.exe,game/SDNative.dll

.EXAMPLE
    # CI: sign installer EXE (called after MakeInstaller.py produces it)
    Deploy/sign-binaries.ps1 Deploy/upload/BlackBox_Jupiter_1.60.00237.exe

.EXAMPLE
    # Local dry-run with a self-signed cert. Create one first:
    $c = New-SelfSignedCertificate -Subject "CN=BlackBox Test Signer" `
                                   -CertStoreLocation Cert:\CurrentUser\My `
                                   -KeyUsage DigitalSignature -Type CodeSigningCert
    Deploy/sign-binaries.ps1 game/StarDrive.exe -Thumbprint $c.Thumbprint
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string[]] $Files,

    [string] $Thumbprint   = "",
    [string] $TimestampUrl = ""
)

$ErrorActionPreference = 'Stop'

function Find-SignTool {
    # Prefer the latest x64 signtool from the Windows 10/11 SDK; fall back to PATH.
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

foreach ($f in $Files) {
    if (-not (Test-Path $f)) { throw "File to sign not found: $f" }
}

if ($Thumbprint) {
    if (-not $TimestampUrl) { $TimestampUrl = 'http://timestamp.digicert.com' }
    Write-Host "Local mode: signing with thumbprint $Thumbprint via $TimestampUrl"

    foreach ($f in $Files) {
        Write-Host "  Signing $f"
        & $signtool sign `
            /sha1 $Thumbprint `
            /tr   $TimestampUrl `
            /td   sha256 `
            /fd   sha256 `
            $f
        if ($LASTEXITCODE -ne 0) { throw "signtool failed on $f (exit $LASTEXITCODE)" }
    }
    Write-Host "All $($Files.Count) file(s) signed locally."
    exit 0
}

if (-not $TimestampUrl) { $TimestampUrl = 'http://timestamp.acs.microsoft.com' }

$required = @(
    'AZURE_TENANT_ID', 'AZURE_CLIENT_ID', 'AZURE_CLIENT_SECRET',
    'TRUSTED_SIGNING_ENDPOINT', 'TRUSTED_SIGNING_ACCOUNT',
    'TRUSTED_SIGNING_CERT_PROFILE', 'TRUSTED_SIGNING_DLIB_PATH'
)
$missing = $required | Where-Object { -not (Test-Path "env:$_") }
if ($missing) {
    throw "Trusted Signing mode requires env vars: $($missing -join ', '). " +
          "Pass -Thumbprint <hex> for local-cert mode, or set the env vars from a " +
          "GitHub Actions secret. See Deploy/SIGNING.md."
}

$dlibPath = $env:TRUSTED_SIGNING_DLIB_PATH
if (-not (Test-Path $dlibPath)) {
    throw "TRUSTED_SIGNING_DLIB_PATH set but file not found: $dlibPath"
}

$metadata = [ordered]@{
    Endpoint               = $env:TRUSTED_SIGNING_ENDPOINT
    CodeSigningAccountName = $env:TRUSTED_SIGNING_ACCOUNT
    CertificateProfileName = $env:TRUSTED_SIGNING_CERT_PROFILE
    CorrelationId          = [guid]::NewGuid().ToString()
} | ConvertTo-Json

$metadataFile = [System.IO.Path]::GetTempFileName() + '.json'
Set-Content -Path $metadataFile -Value $metadata -Encoding UTF8
Write-Host "Trusted Signing metadata: $metadataFile"

try {
    foreach ($f in $Files) {
        Write-Host "  Trusted-Signing $f"
        & $signtool sign `
            /v `
            /fd   sha256 `
            /tr   $TimestampUrl `
            /td   sha256 `
            /dlib $dlibPath `
            /dmdf $metadataFile `
            $f
        if ($LASTEXITCODE -ne 0) { throw "signtool failed on $f (exit $LASTEXITCODE)" }
    }
} finally {
    Remove-Item $metadataFile -ErrorAction SilentlyContinue
}

Write-Host "All $($Files.Count) file(s) signed via Trusted Signing."

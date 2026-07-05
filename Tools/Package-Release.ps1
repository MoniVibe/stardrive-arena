#requires -Version 7.0

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $OutDir = (Join-Path 'dist' 'release'),

    [string] $Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    param([Parameter(Mandatory)][string] $StartDir)

    $dir = (Resolve-Path -LiteralPath $StartDir).Path
    while ($true) {
        if ((Test-Path -LiteralPath (Join-Path $dir 'StarDrive.csproj') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $dir 'StarDriveArena.csproj') -PathType Leaf)) {
            return $dir
        }

        $parent = Split-Path -Parent $dir
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $dir) {
            throw "Could not locate repository root from '$StartDir'."
        }
        $dir = $parent
    }
}

function Get-GitValue {
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)][string[]] $Arguments
    )

    $value = (& git -C $RepoRoot @Arguments 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($value)) {
        throw "git $($Arguments -join ' ') failed in '$RepoRoot'."
    }
    return ($value | Select-Object -First 1).Trim()
}

function Get-ArenaProtocolVersion {
    param([Parameter(Mandatory)][string] $RepoRoot)

    $arenaRoot = Join-Path $RepoRoot 'Ship_Game\GameScreens\Arena'
    if (-not (Test-Path -LiteralPath $arenaRoot -PathType Container)) {
        throw "Arena source directory not found: $arenaRoot"
    }

    $matches = foreach ($file in Get-ChildItem -LiteralPath $arenaRoot -Filter '*.cs' -Recurse) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        if ($text -match 'class\s+ArenaMultiplayerSettings\b' -and
            $text -match 'ProtocolVersion\s*=\s*(\d+)\s*;') {
            [pscustomobject]@{
                Path = $file.FullName
                ProtocolVersion = [int] $Matches[1]
            }
        }
    }

    $matches = @($matches)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one ArenaMultiplayerSettings.ProtocolVersion source match, found $($matches.Count)."
    }
    return $matches[0].ProtocolVersion
}

function Invoke-DotNetBuild {
    param(
        [Parameter(Mandatory)][string] $Project,
        [Parameter(Mandatory)][string] $Configuration
    )

    Write-Host "Building $Project ($Configuration|x64)"
    & dotnet build $Project -c $Configuration -p:Platform=x64
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for $Project."
    }
}

function Resolve-OutDir {
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)][string] $OutDir
    )

    if ([System.IO.Path]::IsPathRooted($OutDir)) {
        return [System.IO.Path]::GetFullPath($OutDir)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $OutDir))
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory)][string] $Parent,
        [Parameter(Mandatory)][string] $Child
    )

    $relative = [System.IO.Path]::GetRelativePath($Parent, $Child)
    if ($relative.StartsWith('..', [System.StringComparison]::Ordinal) -or
        [System.IO.Path]::IsPathRooted($relative)) {
        throw "Refusing to operate outside '$Parent': $Child"
    }
}

$repoRoot = Find-RepoRoot -StartDir $PSScriptRoot
$protocolVersion = Get-ArenaProtocolVersion -RepoRoot $repoRoot
$gitSha = Get-GitValue -RepoRoot $repoRoot -Arguments @('rev-parse', 'HEAD')
$gitShortSha = Get-GitValue -RepoRoot $repoRoot -Arguments @('rev-parse', '--short', 'HEAD')

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "g$gitShortSha-p$protocolVersion"
}

if ($Version -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
    throw "Version '$Version' is not safe for a file name. Use letters, numbers, dot, underscore, and hyphen."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet was not found on PATH."
}

$starDriveProject = Join-Path $repoRoot 'StarDrive.csproj'
$arenaProject = Join-Path $repoRoot 'StarDriveArena.csproj'
$installPath = Join-Path $repoRoot 'INSTALL.md'

if (-not (Test-Path -LiteralPath $installPath -PathType Leaf)) {
    throw "INSTALL.md must exist in the repository root before packaging."
}

Push-Location $repoRoot
try {
    Invoke-DotNetBuild -Project $starDriveProject -Configuration $Configuration
    Invoke-DotNetBuild -Project $arenaProject -Configuration $Configuration

    $outFull = Resolve-OutDir -RepoRoot $repoRoot -OutDir $OutDir
    New-Item -ItemType Directory -Path $outFull -Force | Out-Null

    $stageRoot = Join-Path $outFull "stage-$Version"
    $zipPath = Join-Path $outFull "stardrive-arena-$Version.zip"
    Assert-ChildPath -Parent $outFull -Child ([System.IO.Path]::GetFullPath($stageRoot))
    Assert-ChildPath -Parent $outFull -Child ([System.IO.Path]::GetFullPath($zipPath))

    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force
    }
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null

    $artifacts = @(
        @{ Source = 'game\StarDrive.dll'; PackagePath = 'StarDrive.dll' },
        @{ Source = 'game\SDUtils.dll'; PackagePath = 'SDUtils.dll' },
        @{ Source = 'game\SDGraphics.dll'; PackagePath = 'SDGraphics.dll' },
        @{ Source = 'game\SDLockstep.dll'; PackagePath = 'SDLockstep.dll' },
        @{ Source = 'game\Plugins\StarDriveArena.dll'; PackagePath = 'Plugins/StarDriveArena.dll' }
    )

    # NEW content files this fork adds on top of vanilla BlackBox. They must ship in the overlay so
    # the installed game has them; each extracts to <install>/<PackagePath>. Omitting one is not a
    # crash (the code carries an embedded fallback), but it disables the data-driven tuning the file
    # provides. Add any future fork-authored content file (yaml/xml/etc.) here as a one-line entry.
    $contentArtifacts = @(
        @{ Source = 'game\Content\PilotTraits.yaml'; PackagePath = 'Content/PilotTraits.yaml' }
    )

    $allArtifacts = @($artifacts) + @($contentArtifacts)

    $fileManifest = foreach ($artifact in $allArtifacts) {
        $sourcePath = Join-Path $repoRoot $artifact.Source
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Required release file is missing: $($artifact.Source)"
        }

        $destPath = Join-Path $stageRoot ($artifact.PackagePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)
        New-Item -ItemType Directory -Path (Split-Path -Parent $destPath) -Force | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $destPath -Force

        $item = Get-Item -LiteralPath $sourcePath
        [ordered]@{
            path = $artifact.PackagePath
            source = ($artifact.Source -replace '\\', '/')
            sha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
            bytes = $item.Length
        }
    }

    Copy-Item -LiteralPath $installPath -Destination (Join-Path $stageRoot 'INSTALL.md') -Force

    $manifest = [ordered]@{
        manifestVersion = 1
        package = 'stardrive-arena-overlay'
        version = $Version
        gitSha = $gitSha
        gitShortSha = $gitShortSha
        protocolVersion = $protocolVersion
        configuration = $Configuration
        platform = 'x64'
        buildUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffffffZ', [System.Globalization.CultureInfo]::InvariantCulture)
        overlayBase = 'BlackBox Jupiter 1.60 game folder'
        files = @($fileManifest)
    }

    $manifestJson = $manifest | ConvertTo-Json -Depth 8
    Set-Content -LiteralPath (Join-Path $stageRoot 'manifest.json') -Value $manifestJson -Encoding utf8

    Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $stageRoot).FullName -DestinationPath $zipPath -Force

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $expectedEntries = @('manifest.json', 'INSTALL.md') + @($fileManifest | ForEach-Object { $_['path'] })
        foreach ($entryPath in $expectedEntries) {
            if ($null -eq $zip.GetEntry($entryPath)) {
                throw "Zip validation failed: missing entry '$entryPath'."
            }
        }

        $manifestEntry = $zip.GetEntry('manifest.json')
        $reader = [System.IO.StreamReader]::new($manifestEntry.Open(), [System.Text.Encoding]::UTF8)
        try {
            $manifestText = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $roundTrip = ConvertFrom-Json -InputObject $manifestText
        if ($roundTrip.version -ne $Version -or $roundTrip.protocolVersion -ne $protocolVersion) {
            throw "Zip validation failed: manifest JSON round-trip did not preserve version/protocol."
        }
    }
    finally {
        $zip.Dispose()
    }

    Remove-Item -LiteralPath $stageRoot -Recurse -Force

    Write-Host "Created $zipPath"
    Write-Host "Package manifest:"
    $manifestJson
}
finally {
    Pop-Location
}

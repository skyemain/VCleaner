<#
.SYNOPSIS
    Builds, tests and publishes CleanSlate as a single self-contained Windows x64 .exe.

.DESCRIPTION
    Output: artifacts\release\CleanSlate-<version>-win-x64.exe (+ SHA256SUMS.txt). No .NET installation is needed on the target PC.
    Development builds are versioned MAJOR.MINOR.PATCH-dev.<commit count>+<commit>; -Release drops the -dev suffix.
    The base version comes from the VERSION file (change it with build\bump-version.ps1).

.PARAMETER Release
    Produce a release version (no -dev suffix). Use from a clean, tagged commit.

.PARAMETER Sign
    Authenticode-sign the .exe with build\sign.ps1 (see docs\SIGNING.md for the supported certificate sources).

.PARAMETER Folder
    Also keep the unpacked folder layout (artifacts\publish\win-x64). Starts faster than the single file, which unpacks native libraries on first run.

.PARAMETER ReadyToRun
    Precompile assemblies for faster startup (larger output).

.PARAMETER Zip
    Also create CleanSlate-<version>-win-x64.zip containing the .exe.

.PARAMETER ToRelease
    Also copy the .exe, the certificate (.cer, when signing) and checksums into the tracked release\ folder, replacing the previous CleanSlate-*.exe there.

.PARAMETER SkipTests
    Do not run the automated tests before publishing.

.EXAMPLE
    pwsh build\publish.ps1                    # development build
    pwsh build\publish.ps1 -Release -Sign     # signed release build
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$Release,
    [switch]$Sign,
    [switch]$Folder,
    [switch]$ReadyToRun,
    [switch]$Zip,
    [switch]$SkipTests,
    [switch]$ToRelease
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runtime = 'win-x64'
$project = Join-Path $root 'src\CleanSlate.App\CleanSlate.App.csproj'
$solution = Join-Path $root 'CleanSlate.sln'

function Invoke-Dotnet {
    param([Parameter(Mandatory = $true)][string[]]$DotnetArguments)
    Write-Host "> dotnet $($DotnetArguments -join ' ')" -ForegroundColor DarkGray
    & dotnet $DotnetArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($DotnetArguments[0]) failed with exit code $LASTEXITCODE" }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK (10.0 or newer) is required to build CleanSlate. Install it from https://dotnet.microsoft.com/download'
}

Push-Location $root
try {
    $baseVersion = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
    $sha = 'unknown'; $count = '0'
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $sha = (& git rev-parse --short=7 HEAD 2>$null); if ($LASTEXITCODE -ne 0) { $sha = 'unknown' }
        $count = (& git rev-list --count HEAD 2>$null); if ($LASTEXITCODE -ne 0) { $count = '0' }
    }
    $versionArgs = @("-p:BuildNumber=$count", "-p:SourceRevisionId=$sha")
    if ($Release) {
        $version = $baseVersion
        if (Get-Command git -ErrorAction SilentlyContinue) {
            if (& git status --porcelain 2>$null) { Write-Warning 'Building a release from a working tree with uncommitted changes.' }
        }
    }
    else {
        $version = "$baseVersion-dev.$count"
        $versionArgs += "-p:VersionSuffix=dev.$count"
    }
    Write-Host "Version: $version+$sha" -ForegroundColor Cyan

    Invoke-Dotnet -DotnetArguments @('restore', $solution)
    Invoke-Dotnet -DotnetArguments (@('build', $solution, '-c', $Configuration, '--no-restore') + $versionArgs)

    if (-not $SkipTests) {
        $onWindows = $env:OS -eq 'Windows_NT'
        # The Windows-only test project calls real registry/shell APIs and runs only on Windows.
        $filter = if ($onWindows) { @() } else { @('--filter', 'FullyQualifiedName!~WindowsTests') }
        Invoke-Dotnet -DotnetArguments (@('test', $solution, '-c', $Configuration, '--no-build') + $filter)
    }

    $r2r = "-p:PublishReadyToRun=$($ReadyToRun.IsPresent.ToString().ToLower())"
    $common = @('-c', $Configuration, '-r', $runtime, '--self-contained', 'true', $r2r, '-p:DebugType=none', '-p:DebugSymbols=false') + $versionArgs

    $work = Join-Path $root 'artifacts\publish-single'
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
    Invoke-Dotnet -DotnetArguments (@('publish', $project) + $common + @('-o', $work,
            '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true'))

    $releaseDir = Join-Path $root 'artifacts\release'
    New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
    $exe = Join-Path $releaseDir "CleanSlate-$version-$runtime.exe"
    Copy-Item (Join-Path $work 'CleanSlate.exe') $exe -Force

    if ($Folder) {
        $folderOut = Join-Path $root "artifacts\publish\$runtime"
        if (Test-Path $folderOut) { Remove-Item $folderOut -Recurse -Force }
        Invoke-Dotnet -DotnetArguments (@('publish', $project) + $common + @('-o', $folderOut))
        Write-Host "Folder layout: $folderOut"
    }

    if ($Sign) {
        & (Join-Path $PSScriptRoot 'sign.ps1') -Path $exe
    }
    else {
        Write-Host 'Not code-signed (use -Sign). Windows SmartScreen warns about unsigned downloads.' -ForegroundColor Yellow
    }

    $files = @($exe)
    if ($Zip) {
        $zipPath = Join-Path $releaseDir "CleanSlate-$version-$runtime.zip"
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        Compress-Archive -Path $exe -DestinationPath $zipPath
        $files += $zipPath
    }

    $sums = foreach ($f in $files) { "$((Get-FileHash $f -Algorithm SHA256).Hash.ToLower())  $(Split-Path $f -Leaf)" }
    $sums | Set-Content (Join-Path $releaseDir 'SHA256SUMS.txt') -Encoding ascii

    if ($ToRelease) {
        $releaseFolder = Join-Path $root 'release'
        New-Item -ItemType Directory -Force -Path $releaseFolder | Out-Null
        Get-ChildItem $releaseFolder -Filter 'CleanSlate-*.exe' | Remove-Item -Force
        Copy-Item $exe $releaseFolder -Force
        $cer = Join-Path $root 'artifacts\dev-cert\CleanSlate-dev.cer'
        if ($Sign -and (Test-Path $cer)) { Copy-Item $cer (Join-Path $releaseFolder 'CleanSlate-dev.cer') -Force }
        "$((Get-FileHash (Join-Path $releaseFolder (Split-Path $exe -Leaf)) -Algorithm SHA256).Hash.ToLower())  $(Split-Path $exe -Leaf)" |
            Set-Content (Join-Path $releaseFolder 'SHA256SUMS.txt') -Encoding ascii
        Write-Host "Copied to $releaseFolder"
    }

    Write-Host ''
    Write-Host ("Built {0} ({1:N0} MB)" -f $exe, ((Get-Item $exe).Length / 1MB)) -ForegroundColor Green
    $sums | ForEach-Object { Write-Host $_ }
}
finally {
    Pop-Location
}

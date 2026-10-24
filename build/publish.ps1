<#
.SYNOPSIS
    Builds, tests and publishes CleanSlate as a self-contained Windows x64 application.

.DESCRIPTION
    Default output is a folder (artifacts\publish\win-x64) containing CleanSlate.exe and everything it needs;
    no .NET installation is required on the target PC. The output is NOT code-signed.

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER Output
    Publish folder. Default: artifacts\publish\win-x64 under the repository root.

.PARAMETER SingleFile
    Also produce a single-file executable layout (self-extracting native libraries). Larger first start; less tested than the folder layout.

.PARAMETER ReadyToRun
    Precompile assemblies for faster startup (larger output, needs the runtime pack to be downloadable).

.PARAMETER Zip
    Create a .zip next to the publish folder and write SHA256SUMS.txt.

.PARAMETER SkipTests
    Do not run the automated tests before publishing.

.EXAMPLE
    pwsh build\publish.ps1 -Zip
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Output,
    [switch]$SingleFile,
    [switch]$ReadyToRun,
    [switch]$Zip,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$runtime = 'win-x64'
if (-not $Output) { $Output = Join-Path $root "artifacts\publish\$runtime" }
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
    Invoke-Dotnet -DotnetArguments @('restore', $solution)

    Invoke-Dotnet -DotnetArguments @('build', $solution, '-c', $Configuration, '--no-restore')

    if (-not $SkipTests) {
        $onWindows = $env:OS -eq 'Windows_NT'
        # The Windows-only test project calls real registry/shell APIs and runs only on Windows.
        $filter = if ($onWindows) { @() } else { @('--filter', 'FullyQualifiedName!~WindowsTests') }
        Invoke-Dotnet -DotnetArguments (@('test', $solution, '-c', $Configuration, '--no-build') + $filter)
    }

    if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

    $publishArgs = @(
        'publish', $project, '-c', $Configuration, '-r', $runtime, '--self-contained', 'true',
        '-o', $Output, "-p:PublishReadyToRun=$($ReadyToRun.IsPresent.ToString().ToLower())",
        '-p:DebugType=none', '-p:DebugSymbols=false'
    )
    Invoke-Dotnet -DotnetArguments $publishArgs

    if ($SingleFile) {
        $singleOut = Join-Path (Split-Path $Output -Parent) "$runtime-singlefile"
        if (Test-Path $singleOut) { Remove-Item $singleOut -Recurse -Force }
        $singleArgs = @(
            'publish', $project, '-c', $Configuration, '-r', $runtime, '--self-contained', 'true', '-o', $singleOut,
            '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true',
            "-p:PublishReadyToRun=$($ReadyToRun.IsPresent.ToString().ToLower())", '-p:DebugType=none', '-p:DebugSymbols=false'
        )
        Invoke-Dotnet -DotnetArguments $singleArgs
        Write-Host "Single-file layout: $singleOut"
    }

    $exe = Join-Path $Output 'CleanSlate.exe'
    if (-not (Test-Path $exe)) { throw "Publish finished but $exe was not produced." }

    if ($Zip) {
        $version = (Get-Item $exe).VersionInfo.ProductVersion
        $zipPath = Join-Path (Split-Path $Output -Parent) "CleanSlate-$version-$runtime.zip"
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        Compress-Archive -Path (Join-Path $Output '*') -DestinationPath $zipPath
        $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()
        "$hash  $(Split-Path $zipPath -Leaf)" | Set-Content (Join-Path (Split-Path $zipPath -Parent) 'SHA256SUMS.txt') -Encoding ascii
        Write-Host "Archive: $zipPath"
        Write-Host "SHA-256: $hash"
    }

    $size = (Get-ChildItem $Output -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
    Write-Host ''
    Write-Host ("Published CleanSlate to {0} ({1:N0} MB)" -f $Output, $size) -ForegroundColor Green
    Write-Host 'This build is not code-signed; Windows SmartScreen may warn on first launch.'
}
finally {
    Pop-Location
}

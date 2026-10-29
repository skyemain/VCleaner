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
$project = Join-Path $root 'src\Clenzy.App\Clenzy.App.csproj'
$solution = Join-Path $root 'Clenzy.sln'

function Invoke-Dotnet {
    param([Parameter(Mandatory = $true)][string[]]$DotnetArguments)
    Write-Host "> dotnet $($DotnetArguments -join ' ')" -ForegroundColor DarkGray
    & dotnet $DotnetArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($DotnetArguments[0]) failed with exit code $LASTEXITCODE" }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK (10.0 or newer) is required to build Clenzy. Install it from https://dotnet.microsoft.com/download'
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
    $exe = Join-Path $releaseDir "Clenzy-$version-$runtime.exe"
    Copy-Item (Join-Path $work 'Clenzy.exe') $exe -Force

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

    Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.txt') $releaseDir -Force
    $files = @($exe)
    if ($Zip) {
        $zipPath = Join-Path $releaseDir "Clenzy-$version-$runtime.zip"
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        Compress-Archive -Path $exe -DestinationPath $zipPath
        $files += $zipPath
    }

    $sums = foreach ($f in $files) { "$((Get-FileHash $f -Algorithm SHA256).Hash.ToLower())  $(Split-Path $f -Leaf)" }
    $sums | Set-Content (Join-Path $releaseDir 'SHA256SUMS.txt') -Encoding ascii

    if ($ToRelease) {
        $releaseFolder = Join-Path $root 'release'
        New-Item -ItemType Directory -Force -Path $releaseFolder | Out-Null
        Get-ChildItem $releaseFolder -Filter 'Clenzy-*.exe' | Remove-Item -Force
        Copy-Item $exe $releaseFolder -Force
        $cer = Join-Path $root 'artifacts\dev-cert\Clenzy-dev.cer'
        if ($Sign -and (Test-Path $cer)) { Copy-Item $cer (Join-Path $releaseFolder 'Clenzy-dev.cer') -Force }
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

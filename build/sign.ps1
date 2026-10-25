<#
.SYNOPSIS
    Signs a Windows executable with the self-signed Clenzy certificate (SHA-256, RFC 3161 timestamp) and verifies it.

.DESCRIPTION
    Uses a PFX file. By default artifacts\dev-cert\Clenzy-dev.pfx, created by build\new-dev-certificate.ps1.
    Override with environment variables (secrets never go on the command line):
        CLENZY_SIGN_PFX            path to the .pfx
        CLENZY_SIGN_PFX_PASSWORD   its password (default for the generated certificate: clenzy-dev)
        CLENZY_SIGN_TIMESTAMP_URL  timestamp server (default http://timestamp.digicert.com)
        CLENZY_SIGN_NO_TIMESTAMP=1 skip the timestamp (offline builds)

    A self-signed signature is valid only on PCs that trust the certificate; see README.md.

.PARAMETER Path
    The .exe (or .dll/.msi) to sign.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-Env([string]$name) { [Environment]::GetEnvironmentVariable($name) }

if (-not (Test-Path $Path)) { throw "File not found: $Path" }
$Path = (Resolve-Path $Path).Path

$pfx = Get-Env 'CLENZY_SIGN_PFX'
if (-not $pfx) { $pfx = Join-Path $PSScriptRoot '..\artifacts\dev-cert\Clenzy-dev.pfx' }
if (-not (Test-Path $pfx)) {
    throw "Signing certificate not found: $pfx. Run build\new-dev-certificate.ps1 first (see README.md)."
}
$pfx = (Resolve-Path $pfx).Path
$password = Get-Env 'CLENZY_SIGN_PFX_PASSWORD'
if (-not $password) { $password = 'clenzy-dev' }

function Find-SignTool {
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $kits) {
        $found = Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    throw 'signtool.exe was not found. Install the Windows SDK (signing tools) or add signtool.exe to PATH.'
}

$signtool = Find-SignTool
$signArgs = @('sign', '/fd', 'SHA256', '/d', 'Clenzy', '/f', $pfx, '/p', $password)
if ((Get-Env 'CLENZY_SIGN_NO_TIMESTAMP') -ne '1') {
    $timestamp = Get-Env 'CLENZY_SIGN_TIMESTAMP_URL'
    if (-not $timestamp) { $timestamp = 'http://timestamp.digicert.com' }
    $signArgs += @('/tr', $timestamp, '/td', 'SHA256')
}

Write-Host "> signtool sign ... $Path"
& $signtool @signArgs $Path
if ($LASTEXITCODE -ne 0) { throw "signtool sign failed with exit code $LASTEXITCODE" }

# /pa checks the machine's trust store, so this reports an untrusted root until the certificate is trusted on this PC.
& $signtool verify /pa /v $Path
if ($LASTEXITCODE -ne 0) {
    Write-Warning 'The file is signed, but this PC does not trust the certificate yet. Run build\new-dev-certificate.ps1 -Trust or import the .cer (README.md).'
}
Write-Host "Signed $Path" -ForegroundColor Green

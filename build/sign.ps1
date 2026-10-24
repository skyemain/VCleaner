<#
.SYNOPSIS
    Authenticode-signs a Windows executable with SHA-256 and an RFC 3161 timestamp, then verifies the signature.

.DESCRIPTION
    The certificate source is chosen from environment variables (first match wins), so no secret is ever passed on the command line
    or stored in the repository:

    1. Azure Artifact Signing (formerly Trusted Signing), the Microsoft-run service with short-lived certificates:
         CLEANSLATE_SIGN_AZURE_ENDPOINT   e.g. https://eus.codesigning.azure.net
         CLEANSLATE_SIGN_AZURE_ACCOUNT    code signing account name
         CLEANSLATE_SIGN_AZURE_PROFILE    certificate profile name
         CLEANSLATE_SIGN_AZURE_DLIB       path to Azure.CodeSigning.Dlib.dll (NuGet package Microsoft.Trusted.Signing.Client)
       Authentication uses the standard Azure variables (AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET) or `az login`.
    2. A certificate in the Windows certificate store, including hardware tokens and cloud HSM providers:
         CLEANSLATE_SIGN_THUMBPRINT       SHA-1 thumbprint of the certificate
    3. A PFX file (self-signed test certificates or a private CA; public CAs no longer issue exportable keys):
         CLEANSLATE_SIGN_PFX, CLEANSLATE_SIGN_PFX_PASSWORD

    CLEANSLATE_SIGN_TIMESTAMP_URL overrides the timestamp server (default http://timestamp.acs.microsoft.com for Azure,
    http://timestamp.digicert.com otherwise). See docs\SIGNING.md.

.PARAMETER Path
    The .exe (or .dll/.msi) to sign.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path $Path)) { throw "File not found: $Path" }
$Path = (Resolve-Path $Path).Path

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

function Get-Env([string]$name) { [Environment]::GetEnvironmentVariable($name) }

$signtool = Find-SignTool
$signArgs = @('sign', '/fd', 'SHA256', '/td', 'SHA256', '/d', 'CleanSlate')
$timestamp = Get-Env 'CLEANSLATE_SIGN_TIMESTAMP_URL'
$metadataFile = $null

if ((Get-Env 'CLEANSLATE_SIGN_AZURE_ENDPOINT') -and (Get-Env 'CLEANSLATE_SIGN_AZURE_ACCOUNT') -and
    (Get-Env 'CLEANSLATE_SIGN_AZURE_PROFILE') -and (Get-Env 'CLEANSLATE_SIGN_AZURE_DLIB')) {
    $metadataFile = Join-Path ([IO.Path]::GetTempPath()) "cleanslate-sign-$([guid]::NewGuid().ToString('N')).json"
    @{
        Endpoint               = (Get-Env 'CLEANSLATE_SIGN_AZURE_ENDPOINT')
        CodeSigningAccountName = (Get-Env 'CLEANSLATE_SIGN_AZURE_ACCOUNT')
        CertificateProfileName = (Get-Env 'CLEANSLATE_SIGN_AZURE_PROFILE')
    } | ConvertTo-Json | Set-Content $metadataFile -Encoding utf8
    if (-not $timestamp) { $timestamp = 'http://timestamp.acs.microsoft.com' }
    $signArgs += @('/tr', $timestamp, '/dlib', (Get-Env 'CLEANSLATE_SIGN_AZURE_DLIB'), '/dmdf', $metadataFile)
    Write-Host 'Signing with Azure Artifact Signing.'
}
elseif (Get-Env 'CLEANSLATE_SIGN_THUMBPRINT') {
    if (-not $timestamp) { $timestamp = 'http://timestamp.digicert.com' }
    $signArgs += @('/tr', $timestamp, '/sha1', (Get-Env 'CLEANSLATE_SIGN_THUMBPRINT'))
    Write-Host 'Signing with a certificate from the Windows certificate store.'
}
elseif (Get-Env 'CLEANSLATE_SIGN_PFX') {
    if (-not $timestamp) { $timestamp = 'http://timestamp.digicert.com' }
    $signArgs += @('/tr', $timestamp, '/f', (Get-Env 'CLEANSLATE_SIGN_PFX'))
    if (Get-Env 'CLEANSLATE_SIGN_PFX_PASSWORD') { $signArgs += @('/p', (Get-Env 'CLEANSLATE_SIGN_PFX_PASSWORD')) }
    Write-Host 'Signing with a PFX file.'
}
else {
    throw 'No signing configuration found. Set the CLEANSLATE_SIGN_* variables described in docs\SIGNING.md.'
}

try {
    Write-Host "> signtool sign ... $Path"
    & $signtool @signArgs $Path
    if ($LASTEXITCODE -ne 0) { throw "signtool sign failed with exit code $LASTEXITCODE" }

    # /pa validates against the Authenticode policy and the machine's trust store, so a self-signed test certificate
    # reports a trust error here until it is installed as a trusted root and publisher.
    & $signtool verify /pa /v $Path
    if ($LASTEXITCODE -ne 0) {
        $selfSigned = Get-Env 'CLEANSLATE_SIGN_PFX'
        if ($selfSigned) { Write-Warning 'Signature verification failed. For a self-signed test certificate, trust it first (docs\SIGNING.md).' }
        else { throw "signtool verify failed with exit code $LASTEXITCODE" }
    }
    Write-Host "Signed $Path" -ForegroundColor Green
}
finally {
    if ($metadataFile -and (Test-Path $metadataFile)) { Remove-Item $metadataFile -Force }
}

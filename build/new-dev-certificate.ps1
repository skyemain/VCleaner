<#
.SYNOPSIS
    Creates a SELF-SIGNED code-signing certificate for testing the signing pipeline on Windows.

.DESCRIPTION
    An existing certificate is kept (so every build is signed by the same one); use -Force to replace it.
    With -Trust the certificate is also trusted for the current user on THIS PC (see build\trust-certificate.ps1).
    Self-signed signatures are valid only on PCs that trust the certificate (README.md).
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\dev-cert'),
    [string]$Password = 'clenzy-dev',
    [switch]$Trust,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path

$pfx = Join-Path $OutputDirectory 'Clenzy-dev.pfx'
$cer = Join-Path $OutputDirectory 'Clenzy-dev.cer'
if ((Test-Path $pfx) -and -not $Force) {
    Write-Host "A certificate already exists at $pfx. Keeping it so every build and PC uses the same one (use -Force to replace it)."
    if ($Trust) { & (Join-Path $PSScriptRoot 'trust-certificate.ps1') -CertificatePath $cer }
    return
}

$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Clenzy Development, O=Clenzy' `
    -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy Exportable `
    -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(1)

$secure = ConvertTo-SecureString $Password -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $secure | Out-Null
Export-Certificate -Cert $cert -FilePath $cer | Out-Null

if ($Trust) {
    & (Join-Path $PSScriptRoot 'trust-certificate.ps1') -CertificatePath $cer
}

Write-Host "Created $pfx (password: $Password) and $cer"
Write-Host "Use it: `$env:CLENZY_SIGN_PFX='$pfx'; `$env:CLENZY_SIGN_PFX_PASSWORD='$Password'; build\publish.ps1 -Sign"
Write-Host 'Never commit the .pfx. artifacts\ is git-ignored.'

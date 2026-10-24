<#
.SYNOPSIS
    Creates a SELF-SIGNED code-signing certificate for testing the signing pipeline on Windows.

.DESCRIPTION
    This does NOT make SmartScreen trust the app. With -Trust the certificate is added to the current user's Trusted Root and
    Trusted Publishers stores on THIS machine only, so locally signed builds show as validly signed here.
    Real distribution needs a certificate from a public CA or Azure Artifact Signing (docs\SIGNING.md).
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\dev-cert'),
    [string]$Password = 'cleanslate-dev',
    [switch]$Trust
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path

$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=CleanSlate Development, O=CleanSlate' `
    -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy Exportable `
    -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(1)

$pfx = Join-Path $OutputDirectory 'CleanSlate-dev.pfx'
$cer = Join-Path $OutputDirectory 'CleanSlate-dev.cer'
$secure = ConvertTo-SecureString $Password -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $secure | Out-Null
Export-Certificate -Cert $cert -FilePath $cer | Out-Null

if ($Trust) {
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\CurrentUser\TrustedPublisher | Out-Null
    Write-Host 'Trusted for the current user on this machine.'
}

Write-Host "Created $pfx (password: $Password) and $cer"
Write-Host "Use it: `$env:CLEANSLATE_SIGN_PFX='$pfx'; `$env:CLEANSLATE_SIGN_PFX_PASSWORD='$Password'; build\publish.ps1 -Sign"
Write-Host 'Never commit the .pfx. artifacts\ is git-ignored.'

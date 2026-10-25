<#
.SYNOPSIS
    Trusts the Clenzy self-signed certificate for the CURRENT USER on this PC.

.DESCRIPTION
    Run once on every PC where you install Clenzy by hand, using the Clenzy-dev.cer that matches the .exe you copied.
    The certificate goes into Trusted Root Certification Authorities and Trusted Publishers for your user only (no admin needed).
    Windows shows a confirmation prompt for the root store; accept it. Remove it later with -Remove.
#>
[CmdletBinding()]
param(
    [string]$CertificatePath = (Join-Path $PSScriptRoot '..\artifacts\dev-cert\Clenzy-dev.cer'),
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $CertificatePath)) { throw "Certificate not found: $CertificatePath" }
$CertificatePath = (Resolve-Path $CertificatePath).Path
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $CertificatePath

foreach ($store in 'Root', 'TrustedPublisher') {
    $path = "Cert:\CurrentUser\$store"
    if ($Remove) {
        Get-ChildItem $path | Where-Object Thumbprint -eq $cert.Thumbprint | Remove-Item
    }
    else {
        Import-Certificate -FilePath $CertificatePath -CertStoreLocation $path | Out-Null
    }
}
if ($Remove) { Write-Host "Removed $($cert.Subject) ($($cert.Thumbprint))." }
else { Write-Host "Trusted $($cert.Subject) ($($cert.Thumbprint)) for the current user." }

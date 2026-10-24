# Code signing CleanSlate

Code signing is what lets Windows show "Verified publisher: <your name>" instead of "Unknown publisher", and it is a
requirement for SmartScreen to stop warning. **A signature only works if the certificate is issued to you by a certificate
authority Windows trusts. That needs a paid identity validation, which no script or tool in this repository can do for you.**
Everything else is set up and ready: the build produces one `.exe`, and signing is one switch.

## What to expect from SmartScreen

- **Unsigned:** SmartScreen shows "Windows protected your PC" for every new download.
- **Signed with a trusted certificate:** the publisher name appears and the warning is much less alarming. SmartScreen reputation
  is tied to the signing certificate and the file's download history, so a new publisher can still see a warning for a short
  time until reputation builds. Microsoft does not publish exact thresholds and EV certificates no longer bypass this
  automatically, so plan for it rather than promising your testers it never appears.
- **Signed with a self-signed certificate:** no improvement for anyone but machines where you installed that certificate by hand.

## Choose a certificate source

| Option | Good for | Notes |
| --- | --- | --- |
| **Azure Artifact Signing** (formerly Trusted Signing) | Most teams | Microsoft-run, about US$10/month, short-lived certificates, works from CI, no hardware token. Requires identity validation (individual developers and organizations in supported regions). |
| **OV or EV certificate from a public CA** (DigiCert, Sectigo, SSL.com, ...) | Teams that already have one | Since 2023 the private key must live on a hardware token or in a cloud HSM, so it cannot be exported to a `.pfx`. Sign with `CLEANSLATE_SIGN_THUMBPRINT` on a machine that has the token, or use the CA's cloud-signing tool. |
| **PFX file** | Private CA, internal distribution, tests | `CLEANSLATE_SIGN_PFX` and `CLEANSLATE_SIGN_PFX_PASSWORD`. |
| **Self-signed test certificate** | Testing the pipeline only | `build/new-dev-certificate.ps1` or `.sh`. |

## Signing a build

Set the variables for your option, then add `-Sign`:

```powershell
# Azure Artifact Signing (authenticate with `az login` or AZURE_TENANT_ID / AZURE_CLIENT_ID / AZURE_CLIENT_SECRET)
$env:CLEANSLATE_SIGN_AZURE_ENDPOINT = 'https://eus.codesigning.azure.net'
$env:CLEANSLATE_SIGN_AZURE_ACCOUNT  = 'my-signing-account'
$env:CLEANSLATE_SIGN_AZURE_PROFILE  = 'my-certificate-profile'
$env:CLEANSLATE_SIGN_AZURE_DLIB     = 'C:\tools\Azure.CodeSigning.Dlib.dll'

# or a certificate in the Windows store / on a token
$env:CLEANSLATE_SIGN_THUMBPRINT = '0123456789ABCDEF0123456789ABCDEF01234567'

# or a PFX
$env:CLEANSLATE_SIGN_PFX = 'C:\secure\cert.pfx'
$env:CLEANSLATE_SIGN_PFX_PASSWORD = '...'

pwsh build\publish.ps1 -Release -Sign
```

On Linux or macOS, only the PFX path is supported (`osslsigncode`): `CLEANSLATE_SIGN_PFX=... build/publish.sh --release --sign`.

`build/sign.ps1` signs with SHA-256, adds an RFC 3161 timestamp (so the signature stays valid after the certificate expires)
and runs `signtool verify /pa`. Secrets are read from environment variables, never from the command line or the repository.
`SHA256SUMS.txt` is computed after signing, so it matches the file you distribute.

In CI, `.github/workflows/ci.yml` signs automatically with Azure Artifact Signing when these repository secrets exist:
`AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `SIGN_ENDPOINT`, `SIGN_ACCOUNT`, `SIGN_PROFILE`.

## Setting up Azure Artifact Signing (one-time)

1. Create an Azure subscription and a *Trusted Signing / Artifact Signing* account in a supported region.
2. Complete **identity validation** (a business or individual identity check; this takes days).
3. Create a **certificate profile** (Public Trust).
4. Create a service principal, grant it the *Artifact Signing Certificate Profile Signer* role on the account, and store its
   credentials as the repository secrets above.

## Testing the signing pipeline with a self-signed certificate

```powershell
pwsh build\new-dev-certificate.ps1 -Trust      # trusts it for your user on this PC only
$env:CLEANSLATE_SIGN_PFX = (Resolve-Path artifacts\dev-cert\CleanSlate-dev.pfx)
$env:CLEANSLATE_SIGN_PFX_PASSWORD = 'cleanslate-dev'
pwsh build\publish.ps1 -Sign
```

To make another test PC show the signature as valid, import `artifacts\dev-cert\CleanSlate-dev.cer` into that user's
*Trusted Root Certification Authorities* and *Trusted Publishers*. Remove it when testing ends. SmartScreen still treats the
file as unrecognised on machines that do not have the certificate.

## Verifying a signed file

```powershell
signtool verify /pa /v CleanSlate-1.1.0-win-x64.exe
Get-AuthenticodeSignature .\CleanSlate-1.1.0-win-x64.exe | Format-List
```

Right-click the file, open **Properties > Digital Signatures**, and confirm the signer name and a timestamp are present.

## Keeping keys safe

Never commit a `.pfx`, `.p12` or `.key` (they are git-ignored), never put a password on a command line, and prefer a hardware
token or Azure Artifact Signing so there is no key file to leak.

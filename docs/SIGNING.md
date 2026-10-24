# Signing CleanSlate with a self-signed certificate

CleanSlate builds are signed with a **self-signed** certificate that you create once. This fits installing by hand on your own
PCs: each PC is told to trust the certificate, after which the `.exe` shows a valid signature and your name as publisher.

What it does not do: it does **not** make Windows SmartScreen trust the file on PCs that have not trusted your certificate.
Those PCs show "Unknown publisher" and may show the SmartScreen warning. If you later distribute to people you cannot
visit, you will need a certificate from a public certificate authority.

## One-time setup

On the Windows build machine:

```powershell
pwsh build\new-dev-certificate.ps1 -Trust
```

This creates `artifacts\dev-cert\CleanSlate-dev.pfx` (private key, password `cleanslate-dev`, keep it private) and
`CleanSlate-dev.cer` (public certificate, safe to copy around), and trusts the certificate for your user on this PC.
An existing certificate is kept, so every build is signed by the same one. It expires after a year; replace it with `-Force`.

On Linux or macOS: `build/new-dev-certificate.sh` (needs `openssl`).

## Signing a build

```powershell
pwsh build\publish.ps1 -Sign              # development build
pwsh build\publish.ps1 -Release -Sign     # release build
```

Linux or macOS: `build/publish.sh --sign` (needs `osslsigncode`). Signing adds an SHA-256 signature and an RFC 3161 timestamp,
then `SHA256SUMS.txt` is computed from the signed file. Use `CLEANSLATE_SIGN_PFX` and `CLEANSLATE_SIGN_PFX_PASSWORD` to point at a
different certificate, or `CLEANSLATE_SIGN_NO_TIMESTAMP=1` when offline.

## Installing on another PC

Copy two files: the signed `CleanSlate-<version>-win-x64.exe` and `CleanSlate-dev.cer`. Then on that PC:

```powershell
pwsh build\trust-certificate.ps1 -CertificatePath .\CleanSlate-dev.cer
```

(or double-click the `.cer`, choose **Install Certificate > Current User**, and place it in **Trusted Root Certification
Authorities**, then repeat for **Trusted Publishers**). Windows asks you to confirm the root certificate; accept. Remove it
later with `-Remove`.

Trust the certificate only on PCs you control: anyone holding the matching `.pfx` could sign software those PCs would accept.

## Verifying

```powershell
Get-AuthenticodeSignature .\CleanSlate-1.1.0-win-x64.exe | Format-List
signtool verify /pa /v .\CleanSlate-1.1.0-win-x64.exe
```

`Status : Valid` appears only on PCs that trust the certificate; elsewhere it shows `UnknownError` / an untrusted root, which
is expected. In Explorer, **Properties > Digital Signatures** shows the signer and timestamp.

A file downloaded through a browser or copied from a network share may still carry a "downloaded from the Internet" mark. Copy it
over USB, or right-click > Properties > **Unblock** to remove the prompt.

## Keeping the key safe

Never commit the `.pfx` (`*.pfx`, `*.p12` and `*.key` are git-ignored), and keep it only on the build machine. Copy the `.cer`,
not the `.pfx`, to other PCs.

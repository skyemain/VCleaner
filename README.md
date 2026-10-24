# CleanSlate

CleanSlate is a Windows 10 / Windows 11 desktop cleaner written in C# (.NET 10, WPF, MVVM). It removes system junk and
Chrome / Edge caches, reports registry entries that point to programs that are verifiably gone, and can repair them
after you approve each one. It is built around one rule: **never delete anything it cannot prove is safe to delete.**

> **Verification status (read this first).** This repository was developed and built on Linux. The application compiles
> and cross-publishes to a `win-x64` executable, and 134 automated tests of the safety-critical logic pass on Linux.
> **It has not been run on Windows.** The WPF user interface, the Windows registry / Recycle Bin / restore point /
> elevation code paths, DISM and the tray icon are compiled but unexercised. Work through
> [docs/VALIDATION.md](docs/VALIDATION.md) on a real Windows 10 and Windows 11 machine before relying on it. See
> [Verification status](#verification-status) for the exact breakdown.

## What it does

The home screen has one **Clean Now** button and five rows: System Junk, Chrome, Edge, Registry, Privacy.

| Area | Cleaned by default | Opt-in (Settings) |
| --- | --- | --- |
| **System junk** | User temp files (older than 24 h), Windows temp (24 h, admin), thumbnail cache, DirectX shader caches, Windows Error Reporting (user and, with admin, system; 7 days), crash dumps (7 days), old rotated Windows logs (30 days, admin) | Empty Recycle Bin, Windows Update component cleanup (DISM, admin), Delivery Optimization cache (admin) |
| **Chrome / Edge** | Disk, media, code, GPU and shader caches for **every profile**, only while that browser is closed | Website storage (Local Storage, IndexedDB, Session Storage, Cache Storage) |
| **Registry** | Read-only scan of `Run` values, `App Paths` entries and orphaned (non-MSI) uninstall keys. Nothing is changed during one-click cleaning. | Repairs, only from the registry review page |
| **Privacy** | Off | Run dialog history, typed Explorer paths, recent documents list and Recent folder shortcuts |

### Flows

* **Clean Now** (one click): scan, then clean only categories that are classified as safe to run unattended. If your
  settings enable something permanent or user-visible (Recycle Bin, DISM, privacy, website storage), CleanSlate opens
  the review screen instead of running it silently.
* **Review Before Cleaning** (Settings): always shows every category with sizes, risk and a checkbox before anything is deleted.
* **Registry review**: every issue shows the key, why it is flagged and the evidence. Nothing is selected by default.
  Each repair needs an explicit confirmation, a verified per-entry backup, a last-moment revalidation, and is logged.
  A System Restore point is attempted first when enabled and possible. Backups can be restored from the same page.
* **Results**: recovered space is the logical size of files that were actually deleted. Items that were skipped (in use,
  changed since the scan, protected, browser running, no permission) are counted and listed, not hidden.
* **History, logs and reports**: per-session history, a text report export, and a separate audit log of every deletion
  and registry change under `%LOCALAPPDATA%\CleanSlate`.

## Not supported (and why)

These are shown in the Settings page too.

* **Browser history, cookies and download history.** They live in live SQLite databases. Editing them while a browser
  may be using them, or deleting the files wholesale, risks corrupting a profile. The settings are forced off.
* **Saved passwords, autofill, bookmarks, extensions** are never touched.
* **Update check.** CleanSlate has no network code at all.
* **Recoverable deletion.** Caches and temp files are deleted permanently (not sent to the Recycle Bin). This is
  documented in the UI.
* **Measured space for DISM.** Component cleanup frees an amount Windows does not report, so none is claimed.
* **Registry areas beyond the three rules above** (COM/CLSID, services, installer data, file associations, policies, ...)
  are deliberately never scanned or modified. Generic "registry optimization" is not offered.

## Safety model

* **Deny-by-default deletion.** Every cleaner declares explicit approved roots. A deletion must pass, in order:
  canonicalization (rejects `..`, wildcards, UNC/device paths, alternate data streams), a protected-path denylist
  (Documents, Desktop, Downloads, Pictures, Videos, Music, OneDrive roots, the profile and AppData root folders,
  Windows system folders, Program Files, credential stores, recovery partitions, ...), approved-root containment, user exclusions, minimum age, read-only/system/offline checks, and
  a reparse-point rule (junctions and symlinks are never followed).
* **Preview equals execution.** The scan and the cleaner use the same eligibility function. Immediately before each
  file is deleted it is revalidated (path, ancestors, size and last-write identity, not locked), so a file that changed
  after the scan is skipped. A small unavoidable window remains between the final check and `File.Delete`.
* **No silent elevation.** The app runs as the invoking user (`asInvoker`). System-wide areas are skipped with a clear
  message unless you start it elevated. "Restart as administrator" is an explicit button that goes through the normal
  UAC prompt and handles a declined prompt.
* **Registry safety.** Allow-list of locations; targets are only reported missing when the evidence is unambiguous
  (absolute path on a fixed local drive, all interpretations checked, WOW64 variants considered); bare commands, UNC
  paths, removable drives and unknown variables are never flagged. Repairs delete only the exact value or leaf key,
  never recursively. Restore never overwrites data that has since changed.
* **Concurrency.** One scan, clean or repair at a time; a second request is rejected with a message.
* **Privacy of the tool itself.** Logs redact the user profile path and contain no browser or document content. No data
  leaves the machine.

## Repository layout

```
src/
  CleanSlate.Core          engine, safe deletion service, settings, history, logging (net10.0, no Windows APIs)
  CleanSlate.Cleaners      ICleanerModule implementations: system, browsers, registry, privacy (net10.0)
  CleanSlate.Presentation  MVVM view models and UI service interfaces (net10.0, UI-framework free)
  CleanSlate.Windows       registry, Recycle Bin, restore point, elevation, process, DWM (net10.0-windows)
  CleanSlate.App           WPF shell: views, themes, tray, composition root (net10.0-windows)
tests/
  CleanSlate.TestSupport      fakes and a harness that wires the full engine to a temp directory
  CleanSlate.UnitTests        safe deletion, browsers, registry rules/backup/repair, system modules, settings, history
  CleanSlate.IntegrationTests engine flows and view model tests with the real engine
  CleanSlate.WindowsTests     real-registry and Win32 tests (run on Windows only)
build/   publish.ps1, publish.sh, make-icon.py
docs/    VALIDATION.md (manual Windows 10/11 checklist)
```

New cleaners implement `ICleanerModule` (scan is read-only, clean revalidates each finding) and are added to
`CleanerCatalog`. There is no dynamic plug-in loading.

## Build, test, run

Requires the .NET SDK 10.0 or newer (`global.json` pins the feature band).

```powershell
dotnet build CleanSlate.sln -c Release
dotnet test  CleanSlate.sln -c Release          # on Windows this includes CleanSlate.WindowsTests
dotnet run --project src/CleanSlate.App -c Release
```

On Linux or macOS you can build everything and run the portable tests, but not start the app:

```bash
dotnet test CleanSlate.sln -c Release --filter "FullyQualifiedName!~WindowsTests"
```

### Build the single-file .exe

```powershell
pwsh build/publish.ps1                    # development build:  CleanSlate-1.1.0-dev.57-win-x64.exe
pwsh build/publish.ps1 -Release           # release build:      CleanSlate-1.1.0-win-x64.exe
pwsh build/publish.ps1 -Release -Sign     # release build, signed with the self-signed certificate
pwsh build/publish.ps1 -Folder -Zip       # also the unpacked folder layout and a .zip
```

The result is **one self-contained file**, `artifacts\release\CleanSlate-<version>-win-x64.exe` (about 73 MB), plus
`SHA256SUMS.txt`. No .NET runtime is needed on the target PC. On first start the single file unpacks WPF's native libraries
to a temporary folder, so the very first launch is slower; the `-Folder` layout starts faster. From Linux or macOS,
`build/publish.sh [--release] [--sign] [--folder]` produces the same file.

### Versioning

`VERSION` is the single source of truth (`MAJOR.MINOR.PATCH`). Every build stamps the executable's file and product version:

| Build | Product version | File version |
| --- | --- | --- |
| Development (default) | `1.1.0-dev.57+abc1234` | `1.1.0.57` |
| Release (`-Release`) | `1.1.0+abc1234` | `1.1.0.57` |

`57` is the git commit count and `abc1234` the commit, so any test build can be traced to the exact source. The version is
shown in **Settings** and written to the log at startup. To cut a release:

```powershell
pwsh build/bump-version.ps1 patch -Tag    # or minor / major / 1.4.0; commits "Release x.y.z" and tags vX.Y.Z
git push origin main --tags
```

The bump moves the `[Unreleased]` notes in [CHANGELOG.md](CHANGELOG.md) under the new version. Pushing a `v*` tag makes CI
build the release version and refuse a tag that does not match `VERSION`.

### Code signing (self-signed)

Builds are signed with a self-signed certificate you create once (`pwsh build/new-dev-certificate.ps1 -Trust`), then
`pwsh build/publish.ps1 -Sign`. Each PC you install on by hand trusts the matching `CleanSlate-dev.cer` once
(`build/trust-certificate.ps1`) and then shows a valid signature. PCs that do not trust the certificate still see an unknown
publisher and may see SmartScreen. Details: [docs/SIGNING.md](docs/SIGNING.md). The signing path was exercised on Linux
(`osslsigncode`); the Windows `signtool` scripts have not been run.

The icon is generated by `build/make-icon.py`; the manifest requests no elevation, enables Per-Monitor V2 DPI awareness and
long paths, and declares Windows 10/11 support.

## Data locations

`%LOCALAPPDATA%\CleanSlate\` holds `settings.json`, `logs\` (application and audit logs, rotated), `history\` and
`registry-backups\`. The optional startup entry is the single value `HKCU\...\Run\CleanSlate`.

## Verification status

| Area | State |
| --- | --- |
| Core engine, safe deletion, exclusions, dedup, plan policy, settings, history, logging | **Tested on Linux** (unit and integration tests, including symlink escape and swapped-directory cases) |
| Chrome/Edge profile discovery and cache cleaning | **Tested on Linux** against fixture profiles; real browsers not exercised |
| Registry scanner, backup, repair, restore | **Tested on Linux** against an in-memory registry; `WindowsTests` repeat the flow on a real isolated `HKCU` key but have **not been run** |
| View models (flow, review, cancel, errors, settings consent, registry review) | **Tested on Linux** with the real engine and fakes for dialogs/elevation |
| `Release` build, `win-x64` self-contained cross-publish, manifest and version info embedded | **Done on Linux** (PE32+ GUI executable produced and inspected) |
| WPF views, theming, dark title bar, tray icon, notifications, dialogs, keyboard/screen reader behavior | **Compiled only; never rendered or run** |
| Recycle Bin API, restore point API, UAC restart, startup entry, DISM and PowerShell invocation, real registry | **Compiled only; never run** |
| Single-file `.exe` with embedded version (`publish.sh`) | **Done on Linux**; the file runs only on Windows and has **never been launched** |
| Authenticode signing with a self-signed certificate (`sign.sh`) | **Done on Linux** (signature verifies when the certificate is trusted; a modified file fails) |
| `build/publish.ps1`, `sign.ps1` (signtool), `trust-certificate.ps1`, `bump-version.ps1`, `new-dev-certificate.ps1` | **Not executed** (no PowerShell or Windows here); the `.sh` equivalents of publish, sign, certificate and bump were run |
| SmartScreen behaviour on PCs that do not trust the certificate | **Not testable here**; expect "Unknown publisher" |
| Performance on a real, large profile | **Unmeasured**; scanning streams files and caps findings per module |

Manual validation on Windows 10 and Windows 11: [docs/VALIDATION.md](docs/VALIDATION.md).

## License

MIT, see [LICENSE](LICENSE).

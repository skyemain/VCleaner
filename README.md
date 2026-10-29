# Clenzy for Windows

Development candidate, not approved for production. Windows-only branch of skyemain/VCleaner. Native .NET 10 WPF app for Windows 10/11 x64.

## Build and verify

Install .NET SDK from global.json, then run `pwsh build/publish.ps1 -Release -ToRelease`. This builds all projects, runs unit, integration and Windows platform tests, and publishes a self-contained single-file executable with SHA256 checksums to release/. CI runs on Windows. No .NET runtime is needed on the target PC.

## Use and setup

Run Clenzy-<version>-win-x64.exe. Review cleaning categories and exclusions in Settings. Preview details before cleaning. Browser cache cleanup may ask browsers to quit normally. Recycle Bin deletion is permanent and optional. Registry changes have their own review/backup flow. Administrator access is requested only for operations that need it. Automatic cleanup and scheduled sign-in startup are optional.

The app uses %LOCALAPPDATA%/Clenzy for settings, history and backups. This rename starts a separate Clenzy installation; remove the legacy CleanSlate installation through its uninstaller to remove old startup entries. Never delete existing registry backups as part of an upgrade. No new automatic migration or Windows cleaning behavior was introduced by the rename.

## Signing

Unsigned candidates can show SmartScreen warnings. `build/sign.ps1` accepts a maintainer-provided signing identity; do not commit private keys. Development certificate helpers are for local development only. Production promotion requires explicit approval; main contains approved releases only.

## Maintainer reference

Projects: App (WPF), Presentation, Core (engine and safe deletion), Cleaners, Windows (native APIs). Keep tests, assets, manifests and build/signing scripts. Keep this as the only Markdown reference. Safety/API comments explain non-obvious behavior and should remain; no disabled/commented-out code is required. Retain path guards, cancellation, dry-run support, exclusions, registry backup checks and normal browser quit behavior.

Version 1.4.0 was renamed to Clenzy without changing cleaner rules. All supported build paths are PowerShell on Windows; obsolete shell wrappers and duplicate documentation were removed. Candidate files remain on windows; main is the approved production catalog.

Validation: [Native Windows build](https://github.com/skyemain/VCleaner/actions/runs/40237228726) passed all 197 tests (111 unit, 71 integration, 15 Windows) and published the renamed self-contained executable. The logger test reads active streams with Windows-compatible sharing. The app remains a development candidate.

Version 1.4.1 removes routine skipped-file counts and skip-only warning colors from completion rows and notifications. Detailed results retain all safety diagnostics; actual errors and permissions remain visible. The executable is also published as a GitHub prerelease, pending production approval.


## Windows 11 Cleanup (1.5.0 development candidate)

One home-screen button opens all Windows 11 controls. Sandbox preview is on by default, clearly marked generated fixtures. It uses an in-memory backend with separate sandbox journals; it never calls the live Windows backend. Scan, select, review exact changes, apply and restore are exercised with disposable fixtures. No selection is automatic. Safe exposes reversible current-user settings; Balanced adds Explorer preferences; Advanced adds allowlisted installed consumer apps. These are profiles of available actions, not claims of speed or FPS gains.

Uncheck Sandbox preview to inspect an actual Windows 11 client. Hardware, edition/build, PowerShell, privileges, AppX/provisioned packages, installed-program metadata, startup, services/tasks, Defender, firewall, BitLocker, Update and storage are audited; unavailable sections include the actual error. Read-only scans do not uninstall anything. Supported existing registry DWORD settings include advertising ID, Explorer provider suggestions, file extensions/hidden files, taskbar alignment/Task View and dark apps. Missing settings are unavailable instead of creating speculative keys. Current values are rechecked before application. No camera/microphone/location, security, Update, driver, Xbox or development services are disabled.

Only exact installed consumer package identities are offered for removal. Non-removable/framework/resource packages, detected dependents and detected running packages are blocked. Usage history is unknown, so the user must decide. Only the current user is affected; provisioned packages remain untouched. App removal requires a separate confirmation and a newly created, verified System Restore point. A request or an older restore point does not count. Failure blocks package removal. Reversible settings have exact value/type/existence backups even when System Protection is unavailable. App data recovery is not promised; reinstall through Microsoft Store. No arbitrary uninstall strings or downloaded scripts execute.

Backups/audits live under `%LOCALAPPDATA%/Clenzy/Windows11/{live,sandbox}`. Every planned setting value is durably journaled before writes, applying/verified states are recorded, and postconditions are checked. Rollback checks for later conflicting edits and preserves them. Cancellation stops between actions; an interrupted external operation requires rescan and journal review. No forced reboot or Explorer restart. Audit files include local system information; keep them private. Sandbox fixture state lasts for the dialog session; its journals are separate from real backups.

Run in Windows Sandbox stages only a copy of the published self-contained executable, maps it read-only, and disables networking, clipboard, GPU, audio/video and printer sharing. Only the staged app folder is shared, never user data. It launches the same app's cleanup dialog, still in fixture preview by default. This requires Windows Sandbox already installed on a supported Windows edition; Clenzy does not enable it or weaken security settings. Close the guest to discard guest changes. The Mac development machine and hosted Windows Server runner cannot validate a real Windows 11 Sandbox session.

System review links open supported Windows Settings for privacy, startup, storage, power, Game Mode, personalization, security, Update and recovery. They are manual Windows controls, not implemented automatic optimizations. Existing Clenzy disk cleaning remains available through Clean Now, with its existing review and safety rules. Automatic service/task disabling, OEM/antivirus removal, Recall/Click to Do/framework deletion, provisioned-package removal and automatic DISM changes are not shipped. The larger requirements remain future work, not hidden nonfunctional toggles.

References: [Windows Sandbox configuration](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file), [AppX removal](https://learn.microsoft.com/en-us/powershell/module/appx/remove-appxpackage), [Windows privacy controls](https://support.microsoft.com/en-us/windows/privacy/general-privacy-settings-in-windows). The PowerShell 5.1-compatible maintenance module is embedded from `src/Clenzy.Windows/Windows11/Maintenance.ps1`; its fixed operations consume JSON data without evaluating request strings as code.

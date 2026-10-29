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


## Windows 11 Cleanup (1.6.0 candidate)

One home-screen button contains preview, scan, filtered action review, apply, disk cleanup, administrator restart, audit and rollback. Fixture sandbox is on by default and clearly labeled; unchecking it enables live Windows 11 scanning. Nothing is selected automatically; “Select available Safe changes” is an explicit convenience action. All writes require review. Home/Pro/build differences and unavailable actions are reported.

The embedded, versioned catalog contains 79 actions: 47 optional current-user AppX apps, 25 registry preferences/policies, five supported current-user Run startup entries and two optional services. Optional apps include Microsoft consumer apps, third-party promotions and a small set of OEM registration/welcome apps. Detection does not establish that an app is unused: review and export its data first. Exact package identities only; frameworks, resource/nonremovable packages, detected dependencies and running apps are blocked. Store, Defender, Update, drivers, WebView2, gaming and development infrastructure are not removal targets. No provisioned/all-user removal or arbitrary Win32 uninstall commands.

Safe exposes existing reversible advertising/privacy/Explorer preferences. Balanced adds existing taskbar/Widgets-button/typing preferences and Run entries for Spotify, Discord, Steam, Epic Games Launcher and classic Teams. Startup entries disabled by Windows are preserved; only the Run entry is changed, not Startup folders, scheduled tasks or packaged startup tasks. Advanced offers packages, supported policies and services. No blanket service optimization: MapsBroker and WMPNetworkSvc can change from automatic to manual only when stopped, not delayed-start and without dependent services. Neither is stopped or disabled. Restore restores the saved start mode; security, networking, audio, update, virtualization and gaming services are untouched.

Policies are gated to supported editions/builds, preserve existing conflicting policy values and reject detected management. Required diagnostics remain enabled. Recall snapshot policy requires Windows 11 build 26100.3915 or later on a supported edition; Windows can delete saved snapshots. This gets destructive confirmation and a verified new restore point. Click to Do policy is not offered because current Microsoft documentation marks it Insider-only. No AI framework removal. Registry post-verification confirms stored values, not visual shell behavior or policy effectiveness; sign-out/restart may be needed and is never forced.

App removal and Recall changes require administrator access, separate destructive confirmation and a newly created verified System Restore point. Old restore points and failed creation do not count. Do not claim System Restore can recover deleted app data or Recall snapshots. Package reinstall is through Microsoft Store. Reversible settings/startup/services have exact existence/type/value backups before any write. Backups record the user, computer and live/sandbox environment. Conflicting later edits are preserved. Partial failures remain journaled; cancellation never pretends pending changes succeeded. Reports are private local files at %LOCALAPPDATA%/Clenzy/Windows11/{live,sandbox}.

“Review disk cleanup” calls the existing Clenzy cleaning engine, always requiring review. Its maintenance preset preserves Prefetch, shader caches, recent temporary files, browser site storage and usage histories; it does not close browsers. Existing exclusions, path/reparse guards, locked-file handling, previews and deletion accounting remain in force. Existing optional Recycle Bin, Delivery Optimization and DISM maintenance retain their review/elevation requirements. Saved user preferences are not overwritten by this one-run preset. Fixture mode does not invoke real disk cleaning.

Audit covers hardware/storage, top memory processes, AppX/provisioned packages, installed programs, startup, services/tasks, Defender, firewall, BitLocker and Update. Failed sections are reported. Memory snapshots are observations, not benchmark improvements. No promised FPS/boot-speed gains. Win32 OEM/antivirus tools, unknown services, scheduled tasks and unsupported policies remain review-only through Windows Settings.

Windows Sandbox launch shares only a staged executable read-only with networking, clipboard, GPU, audio/video and printers disabled. It requires Windows Sandbox already enabled; the app does not change host Windows features. The guest opens fixture preview by default. Simulation is not evidence of live Windows 11 compatibility.

### Research and licensing

Technical references: [Win11Debloat at d2cb97a](https://github.com/Raphire/Win11Debloat/tree/d2cb97a4ef8631e8cfa3307e89280e0b0326f540), [WinUtil at 113f0d6](https://github.com/ChrisTitusTech/winutil/tree/113f0d6e93cec3881092ef8f0644a9727c895775), [Microsoft Windows AI policy](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai), [diagnostic policy](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-system#allowtelemetry), [Windows Sandbox](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file). See THIRD-PARTY-NOTICES.txt for MIT notices. No reference-project script is downloaded or executed. Clenzy uses its existing WPF/.NET engine and an embedded PowerShell 5.1 module; request values are serialized as data, never interpolated commands.

### Validation status

The Mac host has not been modified. CI exercises fixture roundtrips, interrupted/denied operations, startup restore, package/service protections, real PowerShell 5.1 transport and the WPF preview. Live Windows 11 app uninstallation, restore-point creation, effective shell/policy changes and actual Sandbox boot remain unverified because no Windows 11 environment is available. This stays a prerelease candidate; main production is unchanged.

## Debloat flow in 1.9.0

Opening Debloat starts an Advanced live scan automatically. Only available changes are shown, initially unselected. Apply creates and verifies a new restore point before all selected operations, including reversible settings. If recovery creation fails, nothing is applied. Exact settings backups remain on disk. The inline final report identifies verified changes, unselected items and failed or unattempted operations. App removal and Recall deletion retain explicit destructive consent; those data cannot be restored from the settings backup. The page no longer exposes profile selectors, tabs, scan/filter controls or maintenance/report toolbars. Back returns home when idle. Closing during a write is blocked to protect the transaction. App launches elevated via its manifest.

## Windows Version 1 (1.0.0)

Version numbering restarts at 1.0.0 with fixes through 1.9.3 retained. Installing replaces the Clenzy executable and retargets its registered startup task. It retires the recognized per-user CleanSlate executable, matching Run/startup task/Start menu registration and uninstall metadata, preserving old data and backups. Running a downloaded executable without accepting installation does not replace installed apps. Loose downloaded copies remain. Same-version launches compare binary hashes before redirecting to the installed copy. Legacy migration is covered by command-matching tests; end-to-end migration on a Windows 11 PC remains unverified.

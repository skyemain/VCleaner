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

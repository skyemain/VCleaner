# Changelog

All notable changes to CleanSlate are recorded here. The version number lives in the `VERSION` file;
see "Versioning" in the README.

## [Unreleased]

## [1.4.0] - 2026-10-24

### Changed
- New "Ops Console" liquid-glass design: near-black mint-green console look, translucent glass cards with depth and glow,
  a radar-style clean ring with tick marks, `[ BRACKETED ]` section labels, and the system acrylic (or Mica) backdrop behind
  the window on Windows 11 22621+, falling back to the solid theme elsewhere. The light theme is a matching frosted variant.

### Added
- Automatic sign-in cleaning now frees RAM after the clean (same as the Free up RAM button). New Automation toggle,
  on by default.
- The first time CleanSlate runs as administrator while automatic sign-in cleaning is on, it upgrades the sign-in start to
  the elevated scheduled task by itself, so sign-in cleaning includes Windows Temp, Prefetch and Windows logs with no prompt.

## [1.3.2] - 2026-10-24

### Fixed
- The installer now removes the "downloaded from the internet" marker from the installed exe. Windows carried that marker
  over from the downloaded file, so SmartScreen treated every Start menu or sign-in launch of the installed copy as a new
  unapproved download and blocked it, even after the download itself had been approved once.
- The installed copy re-creates its sign-in startup entry (or, when enabled and elevated, the administrator task) at every
  launch, so automatic cleaning at sign-in heals itself when the entry was lost or stale.
- When opening the already-installed copy fails (for example it is blocked by policy), the downloaded copy now runs
  instead of exiting silently.

## [1.3.1] - 2026-10-24

### Added
- The exe embeds its self-signed certificate and trusts it when installing (or from Settings > Installation), after checking the exe is signed by it. Removed again with "Delete everything" on uninstall.

### Changed
- Running a copy whose version is already installed now just opens the installed copy; no question is asked.

## [1.3.0] - 2026-10-24

### Added
- Automatic cleaning at sign-in: window opens, 5 s cancelable countdown, scan, clean (unattended-safe categories only), result shown, then the app closes itself. Settings > Automation.
- Optional elevated start through a Windows scheduled task, so automatic cleaning can include Windows Temp, Prefetch and Windows logs without a UAC prompt.
- **Free up RAM** button: trims idle programs' working sets and reports the RAM freed.

## [1.2.0] - 2026-10-24

### Added
- Running Chrome or Edge is now closed (politely first, then forcibly) so their cache can be cleaned; sign-in data is never touched. A confirmation names the browser; both the closing and the question can be switched off in Settings.
- Prefetch cleaning (`*.pf` files only, administrator) and the option to include recently created temp files, so `%TEMP%` and `C:\Windows\Temp` are emptied of everything not in use.
- Built-in per-user installer: installing a newer build replaces the old one; Settings > Installation and `--uninstall [--quiet]` remove it; Apps & features and Start menu entries.

### Changed
- Wide, landscape, minimal and more futuristic interface (ring button, hairline cards, monospace labels, calmer scroll bars). Home fits without scrolling; Settings, review and details use two columns.

## [1.1.0] - 2026-10-24

### Added
- Single-file `CleanSlate-<version>-win-x64.exe` as the primary distributable.
- Version stamping: every build carries `MAJOR.MINOR.PATCH`, a `-dev.N` suffix for development builds and the git commit.
- Self-signed code signing (`build/new-dev-certificate.*`, `build/sign.*`, `build/trust-certificate.ps1`).
- `build/bump-version.ps1` / `build/bump-version.sh` to cut a release.

## [1.0.0] - 2026-10-23

- First complete feature set: system, browser, registry and privacy cleaning with review, history and Settings.

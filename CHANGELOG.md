# Changelog

All notable changes to CleanSlate are recorded here. The version number lives in the `VERSION` file;
see "Versioning" in the README.

## [Unreleased]

## [1.1.0] - 2026-10-24

### Added
- Single-file `CleanSlate-<version>-win-x64.exe` as the primary distributable.
- Version stamping: every build carries `MAJOR.MINOR.PATCH`, a `-dev.N` suffix for development builds and the git commit.
- Code-signing pipeline (`build/sign.ps1`, `build/sign.sh`) for Azure Artifact Signing, a certificate in the Windows store or a hardware token, or a PFX file.
- `build/bump-version.ps1` / `build/bump-version.sh` to cut a release.

## [1.0.0] - 2026-10-23

- First complete feature set: system, browser, registry and privacy cleaning with review, history and Settings.

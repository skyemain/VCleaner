# CleanSlate for macOS

The `mac` branch contains only the native SwiftUI app for macOS 13 and newer, including Apple silicon and Intel support.

The app, Xcode project, tests, packaging scripts and detailed documentation are in [`macos/`](macos/README.md).

## Build and test

Select a full Xcode installation, then run:

```bash
cd macos
scripts/verify-source.sh
xcodebuild -project CleanSlate.xcodeproj -scheme CleanSlate -destination 'platform=macOS' CODE_SIGNING_ALLOWED=NO test
scripts/build.sh
```

## Download and share

[Download the latest Mac DMG](https://github.com/skyemain/VCleaner/raw/refs/heads/mac/macos/release/CleanSlate-latest-mac-universal.dmg).

Every successful `mac` build updates [`macos/release/`](macos/release/) with the current version, a stable latest filename, and SHA-256 checksums. The previous DMG remains available until its replacement passes tests and packaging. The workflow also saves a downloadable build artifact.

See [the macOS guide](macos/README.md) for cleaning rules, Safari permissions, RAM monitoring, signing and notarization.

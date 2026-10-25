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

Pushing to `mac` runs the macOS workflow to test and package an ad-hoc signed universal DMG. Download the matching workflow artifact for the latest build. The committed `macos/release/CleanSlate-1.0.1-mac-universal.dmg` is an older build and does not include the current fixes.

See [the macOS guide](macos/README.md) for cleaning rules, Safari permissions, RAM monitoring, signing and notarization.

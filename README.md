# Clenzy — macOS version 1.0

Native SwiftUI cleaner for macOS 13+, Apple silicon and Intel. Work only on the `mac` branch of `skyemain/VCleaner`.

## Download and install

[Latest universal DMG](https://github.com/skyemain/VCleaner/raw/refs/heads/mac/macos/release/Clenzy-latest-mac-universal.dmg).
Quit the old app, drag Clenzy to Applications, eject the DMG and open Clenzy. If upgrading from CleanSlate, remove the old CleanSlate.app separately. No custom installer or required setup wizard.

## Behavior and safety

- Clean includes eligible home Trash files (permanent deletion), allowlisted caches, temporary files older than 24 hours, diagnostic reports older than 30 days, and archived logs. Xcode DerivedData is opt-in.
- Safari targets only Safari cache roots, never shared WebKit containers or browsing data. Chrome targets profile cache directories. Blocked roots are skipped and reported; accessible roots still clean.
- Deletion checks ownership, age, exclusions, approved roots and file identity. Never follows symlinks, removes protected personal data or changes OS security settings.
- Clean Up RAM samples running regular Dock apps and asks the five lowest-memory eligible apps to quit normally. Finder and Clenzy are excluded; save/cancel prompts are respected. Then the five largest remaining apps are shown for manual review. Unknown memory samples are omitted, not treated as zero. No forced quits or fake memory allocation.
- Dashboard shows RAM, storage, battery time and estimated battery health. Login cleanup never empties Trash or quits apps.

## Permissions and identity

Settings > Cleaning access offers optional exact-folder consent and access diagnostics. macOS may still require Full Disk Access for protected data. Ad-hoc signatures change between builds, so privacy approval may need renewal. Reinstalling cannot reset TCC. Keep `com.skyemain.CleanSlate` as the bundle identifier for identity continuity; product, module, executable and visible name are Clenzy. Local preferences/history use Application Support/Clenzy. No Apple Developer signing identity is configured.

## Build, test and release

From `macos/`:

```sh
python3 scripts/generate-xcodeproj.py
scripts/verify-source.sh
xcodebuild -project Clenzy.xcodeproj -scheme Clenzy -destination 'platform=macOS' CODE_SIGNING_ALLOWED=NO test
scripts/build.sh
codesign --force --deep --sign - build/Clenzy.app
scripts/package-dmg.sh 1.0
```

The GitHub macOS workflow tests, builds both architectures, signs ad-hoc, packages, and commits the versioned DMG, stable latest DMG and checksums to `macos/release/`. It refuses to publish stale source commits. For future releases update the generator marketing/build versions, workflow and package default; regenerate the project. Keep this as the only Markdown reference; retain source, tests, assets, license and build/signing scripts.

Version 1.0 renames CleanSlate to Clenzy, selects exactly up to five lowest-memory Dock apps instead of protecting the top five, and removes unused report code and duplicate documentation. Local test runs use temporary fixture files only and never close real user apps. Full native tests and universal packaging run in GitHub Actions.

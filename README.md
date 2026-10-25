# Clenzy — production releases

Main is the production download catalog. **Only macOS is approved for production.** Windows, iOS and Android are development candidates on their own branches and are not included here as production builds.

## Download and install on Mac

[Download Clenzy 1.0.1 for Apple silicon and Intel](https://github.com/skyemain/VCleaner/raw/refs/heads/main/releases/macos/Clenzy-latest-mac-universal.dmg) · [SHA256 checksums](releases/macos/SHA256SUMS.txt).

Requires macOS 13+. Quit Clenzy, open the DMG, drag Clenzy to Applications and replace the existing copy. Eject the DMG and open the installed app. If upgrading from CleanSlate, remove its old app separately. These builds are ad-hoc signed and not notarized. macOS may require approval through Privacy & Security for first launch. No paid Apple signing identity is configured.

Version 1.0.1 adds **Restart Clenzy after granting access** under Settings > Cleaning access. This prepares a helper, quits normally, waits for exit and reopens the same bundle. The app accepts system quit requests immediately. Native tests cover fixture restart sequencing; actual Full Disk Access interaction still needs confirmation on your Mac.

## Use and permissions

Clean removes eligible allowlisted caches, stale temporary files, archived logs and diagnostic reports, plus eligible home Trash files. **Manual Clean permanently deletes eligible Trash files.** Browser cache cleaning may ask Safari/Chrome to quit. Files denied by macOS are skipped and reported. No automatic installer/setup loop is used.

For Safari or Trash access: install in Applications, open Settings > Cleaning access, use the optional exact-folder chooser, or add the installed app to System Settings > Privacy & Security > Full Disk Access. Restart Clenzy after granting it, then Recheck. Ad-hoc updates can require renewing permissions. The app cannot grant itself Full Disk Access or bypass macOS protection.

Clean Up RAM asks the five lowest-memory eligible running Dock apps to quit normally, excluding Finder and Clenzy, then shows the five largest remaining apps for review. Save/cancel prompts are respected. RAM changes are measured; no forced termination or artificial RAM allocation is used. The dashboard also shows storage, battery time and estimated battery health.

## Platform source and candidate builds

[Mac source](https://github.com/skyemain/VCleaner/tree/mac), [Windows](https://github.com/skyemain/VCleaner/tree/windows), [iOS](https://github.com/skyemain/VCleaner/tree/ios), [Android](https://github.com/skyemain/VCleaner/tree/android). Each platform branch contains only its own code/build files and one README with setup and test details. Windows candidates are self-contained x64 executables; Android candidates are debug-signed APKs. The iOS workflow produces an unsigned device archive, which needs your own signing for a device. iOS/Android cleanup is limited to authorized media/documents, not other apps' private caches or system RAM.

## Maintainer reference and promotion

releases/manifest.json records approved platform/version, source commit, build run, signing and checksum. CI verifies that only approved platforms appear, checks every checksum and verifies the Mac executable/signature/architecture. Main holds distribution files and this guide; platform source stays on its branch. Git history preserves the former Windows layout.

Promote a new platform only after explicit user approval and successful platform builds/tests plus real-device validation. Copy only the verified final build and checksums into releases/<platform>, then update the manifest and this guide together. Mac is the currently approved platform; 1.0.1 is its tested maintenance fix. Do not copy unsigned iOS archives or debug Android APKs into production releases.

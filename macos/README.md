# CleanSlate for macOS

CleanSlate 1.0.1 is the native macOS counterpart of CleanSlate for Windows 1.4.0. It is a SwiftUI app for macOS 13 and newer, built as a universal binary (Apple silicon and Intel). It removes only cache and temporary files it can prove are disposable, and it does not pretend to be a registry cleaner or a RAM booster.

GitHub Actions builds the universal disk image when the `mac` branch is pushed. The copy in that image is ad-hoc signed so it can launch; it is not notarized, so the first open is a right-click → Open. Xcode is required to build it locally.

## What it cleans

| Area | Default | What actually happens |
| --- | --- | --- |
| Temporary files | On | Regular files in the directory returned by the system temporary-directory API, older than 24 hours. The path is never hardcoded. |
| User caches | On | `~/Library/Caches/com.apple.helpd`, files older than 7 days, plus loose files sitting directly in `~/Library/Caches`. Other application cache folders are left alone. |
| Diagnostic reports | On | `.ips`, `.crash`, `.spin`, `.diag`, and `.hang` files in `~/Library/Logs/DiagnosticReports` older than 30 days. Unified logs are not touched. |
| Chrome | On | `Cache`, `Code Cache`, `GPUCache`, and the known shader caches in every Chrome profile. Cookies, history, passwords, bookmarks, and extensions are not candidates. |
| Edge | On | The same rules, separately, for Microsoft Edge. |
| Safari | On when readable | Only `~/Library/Caches/com.apple.Safari/fsCachedData`. If macOS denies access, the result is “Safari cache unavailable: macOS access restrictions.” Bookmarks, history, cookies, passwords, and iCloud data are never opened. |
| Trash | Off | Counted only until you press **Empty Trash** and confirm. One-click cleaning never empties it. |
| Xcode DerivedData | Off | Opt-in. Archives and simulators are measured and never deleted. |
| Quick Look thumbnails | Shown as unsupported | Undocumented system thumbnail databases are not deleted. |
| Downloads | Never cleaned | Large files can be listed from System Health. There is no delete path. |
| Browsing history | Unsupported | No safe public API. The setting stays off. |
| App leftovers | Report only | A folder under Application Support is not treated as abandoned just because no matching app is in `/Applications`. |

Login cleaning runs the low-risk set only. It does not quit browsers, empty the Trash, or touch Xcode. If Chrome or Edge is open, that browser’s cache is skipped and the result says so. A manual clean can ask the browser to quit normally and waits; it never force-quits.

## System health

There is no Windows Registry, so there is no registry cleaner. System Health shows memory pressure, swap, disk capacity, and the filesystem name, using system APIs. It explains that macOS manages memory and does not purge it. User LaunchAgents whose plist points at a missing absolute executable can be backed up and disabled after you confirm that one item. Shell-script agents are not flagged.

## Automatic cleaning at login

Off by default. Turning it on registers the app with `SMAppService` (System Settings may ask you to allow it). At login the window counts down from 5 seconds, cleans, shows the real result, and closes after about 12 seconds unless you interact with it. Both the countdown and the auto-close can be turned off.

## Safety

Deletion is limited to an approved root for that operation. The root itself is kept. Symbolic links are not followed and not deleted. A file is opened with `O_NOFOLLOW` and checked again (device and inode) immediately before `unlink`. Reserved names such as `Cookies`, `History`, `Bookmarks`, and `Login Data` are skipped even if they sit inside a cache directory. Downloads, Documents, Desktop, Keychains, Safari’s profile, containers, archives, and simulators are not approved roots.

The app is not sandboxed. A cleaner has to read other applications’ caches, which a sandbox would block. macOS privacy protection still applies: Full Disk Access is optional, manual, and never toggled by the app. The app does not change SIP, Gatekeeper, or TCC, and it does not install certificates or clear the quarantine flag.

## Build

From this directory on a Mac with Xcode:

```bash
scripts/build.sh
scripts/verify-release.sh
```

`build.sh` produces an unsigned universal `build/CleanSlate.app`. That is a development build. Gatekeeper will not trust it.

To sign, package, and notarize, using credentials that stay outside the repository:

```bash
export DEVELOPER_ID_APPLICATION="Developer ID Application: Your Name (TEAMID)"
scripts/sign.sh
scripts/package-dmg.sh 1.0.1
xcrun notarytool store-credentials cleanslate --apple-id you@example.com --team-id TEAMID
NOTARY_KEYCHAIN_PROFILE=cleanslate scripts/notarize.sh release/CleanSlate-1.0.1-mac-universal.dmg
scripts/verify-release.sh
```

The disk image contains `CleanSlate.app` and a shortcut to `/Applications`. Drag the app onto Applications. Replacing the app in `/Applications` keeps settings and history in `~/Library/Application Support/CleanSlate`. There is no separate updater and no automatic download of new builds.

`scripts/verify-source.sh` only checks that the expected files exist and that the Swift sources do not contain shell-deletion shortcuts. It does not compile anything.

## Tests

The XCTest bundle covers approved-root refusals, symlink escapes, reserved browser files, temporary-file age, diagnostic retention, Chrome and Edge profile separation, Safari’s access-denied wording, login cleaning refusing Trash and Xcode, a second clean being refused while one is running, login-item settings, and history reports. Run them on a Mac with:

```bash
xcodebuild -project CleanSlate.xcodeproj -scheme CleanSlate -destination 'platform=macOS' test
```

They were not run in the environment that added this project.

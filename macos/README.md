# CleanSlate for macOS

CleanSlate 1.0.3 is a native macOS cleaner. It is a SwiftUI app for macOS 13 and newer, built as a universal binary (Apple silicon and Intel). It removes only cache and temporary files it can prove are disposable. It does not clean a registry, and Free RAM does not quit apps.

GitHub Actions builds the universal disk image when the `mac` branch is pushed. The copy in that image is ad-hoc signed so it can launch; it is not notarized, so the first open is a right-click → Open. Xcode is required to build it locally.

## What it cleans

| Area | Default | What actually happens |
| --- | --- | --- |
| Temporary files | On | Regular files in the directory returned by the system temporary-directory API, older than 24 hours. The path is never hardcoded. |
| User caches | On | Help Viewer (`com.apple.helpd`), Spotlight suggestions (`com.apple.parsecd`), and Tips (`com.apple.touristd`) under `~/Library/Caches`, files older than 7 days, plus loose files sitting directly in `~/Library/Caches`. Other application cache folders are left alone. |
| Diagnostic reports | On | `.ips`, `.crash`, `.spin`, `.diag`, and `.hang` files in `~/Library/Logs/DiagnosticReports` older than 30 days. Unified logs are not touched. |
| Chrome | On | `Cache`, `Code Cache`, `GPUCache`, and the known shader caches in every Chrome profile. Cookies, history, passwords, bookmarks, and extensions are not candidates. |
| Safari | On when readable | Safari’s own cache plus the WebKit network, web content, and GPU caches. These are the folders that actually grow. History, bookmarks, cookies, passwords, and website data are never opened. If macOS hides a cache, the Safari row says to turn on Full Disk Access instead of reporting that there was nothing to remove. |
| Trash | Off | Counted only until you press **Empty Trash** and confirm. One-click cleaning never empties it. |
| Xcode DerivedData | Off | Opt-in. Archives and simulators are measured and never deleted. |
| Quick Look thumbnails | Shown as unsupported | Undocumented system thumbnail databases are not deleted. |
| Downloads | Never cleaned | Large files can be listed from System Health. There is no delete path. |
| Browsing history | Unsupported | No safe public API. The setting stays off. |
| App leftovers | Report only | A folder under Application Support is not treated as abandoned just because no matching app is in `/Applications`. |

Login cleaning runs the low-risk set only. It does not quit browsers, empty the Trash, or touch Xcode. If Safari or Chrome is open, that browser’s cache is skipped and the result says so. A manual clean can ask the browser to quit normally and waits; it never force-quits. Edge is not a target.

## System health

System Health shows memory pressure, swap, disk capacity, and the filesystem name, using system APIs. **Free RAM** briefly claims inactive and purgeable memory, up to 1 GB, then gives it back so macOS can drop cached pages. Open apps stay open. User LaunchAgents whose plist points at a missing absolute executable can be backed up and disabled after you confirm that one item. Shell-script agents are not flagged.

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
scripts/package-dmg.sh 1.0.3
xcrun notarytool store-credentials cleanslate --apple-id you@example.com --team-id TEAMID
NOTARY_KEYCHAIN_PROFILE=cleanslate scripts/notarize.sh release/CleanSlate-1.0.3-mac-universal.dmg
scripts/verify-release.sh
```

The disk image contains `CleanSlate.app` and a shortcut to `/Applications`. Drag the app onto Applications. Replacing the app in `/Applications` keeps settings and history in `~/Library/Application Support/CleanSlate`. There is no separate updater and no automatic download of new builds.

`scripts/verify-source.sh` only checks that the expected files exist and that the Swift sources do not contain shell-deletion shortcuts. It does not compile anything.

## Tests

The home footer samples RAM every two seconds and shows used/total memory, usage percentage and memory pressure, with **Clean up RAM** beside it. The result reports the measured change; macOS manages memory automatically and cached-memory release may be temporary.

Safari permission failures, including blocked subdirectories, are reported rather than shown as an empty cache. Give the installed app Full Disk Access when requested and relaunch it before retrying. Cleaning from the review screen now also offers to quit a running browser.


The XCTest bundle covers approved-root refusals, symlink escapes, reserved browser files, temporary-file age, diagnostic retention, Chrome profile separation and Edge exclusion, Safari’s access-denied wording, login cleaning refusing Trash and Xcode, a second clean being refused while one is running, login-item settings, and history reports. Run them on a Mac with:

```bash
xcodebuild -project CleanSlate.xcodeproj -scheme CleanSlate -destination 'platform=macOS' test
```

They were not run in the environment that added this project.

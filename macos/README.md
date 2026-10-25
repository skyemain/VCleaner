# CleanSlate for macOS

CleanSlate 1.0.4 is a native macOS cleaner. It is a SwiftUI app for macOS 13 and newer, built as a universal binary (Apple silicon and Intel). It removes only cache and temporary files it can prove are disposable. RAM management offers a normal quit only after you choose and confirm one app.

GitHub Actions tests and builds the universal disk image when the `mac` branch is pushed, then commits the latest DMG and checksums to [`release/`](release/). Share [the stable latest download](https://github.com/skyemain/VCleaner/raw/refs/heads/mac/macos/release/CleanSlate-latest-mac-universal.dmg). The copy in that image is ad-hoc signed so it can launch; it is not notarized, so the first open is a right-click → Open. Xcode is required to build it locally.

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

System Overview explains RAM state, swap, disk capacity, and startup items that point to missing programs. Opening the report does not clean files or release memory. Downloads, Xcode archives, simulators and possible leftovers are reports only. A broken LaunchAgent can be backed up and disabled as a separate confirmed action.

The home footer shows RAM usage plus the five largest readable foreground apps and their helper processes. Percentages use total physical RAM as the denominator and use per-process physical footprints; inaccessible processes and shared memory mean these estimates can differ from Activity Monitor.

**Clean up RAM** opens a review of these apps. Each app has a **Quit…** action and a confirmation. CleanSlate requests a normal quit, allows save dialogs, never forces an app closed, and reports observed system RAM before and after. Other running apps affect that change, so it is not presented as a guaranteed amount recovered. The former allocation-based cache-reclamation method has been removed: macOS already manages caches, and an open app’s live memory cannot safely be erased.

## First clean and Safari access

The first manual Clean opens an access guide with a continue-with-available-access option. The header and blocked target rows reopen it. The guide links to Full Disk Access and shows current per-folder access checks. It cannot grant itself macOS privacy permission.

1. Install CleanSlate in Applications, eject the DMG, and run the installed copy.
2. For blocked Safari and other protected caches, open System Settings → Privacy & Security → Full Disk Access; add that installed copy and enable it.
3. Quit and reopen CleanSlate, then recheck access in the guide.
4. Quit Safari completely with ⌘Q, or approve the app’s normal browser-quit prompt, before cleaning.
5. If access still fails after an update, remove the older privacy entry, re-add the installed copy, relaunch, and inspect the blocked folder paths. Some system-protected or other-user files remain inaccessible. No SIP changes, administrator access, Accessibility or Screen Recording permission are needed.

Login-item approval and Downloads access apply only to their optional features. RAM monitoring and normal app quitting need no extra privacy permission. A missing cache directory is normal. Access probes check directory access, not the global Full Disk Access switch. One blocked system-cache folder no longer skips the other supported cache folders.

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
scripts/package-dmg.sh 1.0.4
xcrun notarytool store-credentials cleanslate --apple-id you@example.com --team-id TEAMID
NOTARY_KEYCHAIN_PROFILE=cleanslate scripts/notarize.sh release/CleanSlate-1.0.4-mac-universal.dmg
scripts/verify-release.sh
```

The disk image contains `CleanSlate.app` and a shortcut to `/Applications`. Drag the app onto Applications. Replacing the app in `/Applications` keeps settings and history in `~/Library/Application Support/CleanSlate`. There is no separate updater and no automatic download of new builds.

`scripts/verify-source.sh` only checks that the expected files exist and that the Swift sources do not contain shell-deletion shortcuts. It does not compile anything.

## Tests

The home footer refreshes RAM and the top five apps every two seconds while visible. It does not show a memory-pressure label or cache-reclamation claims.

Safari permission failures, including blocked subdirectories, are reported rather than shown as an empty cache. Give the installed app Full Disk Access when requested and relaunch it before retrying. Cleaning from the review screen now also offers to quit a running browser.


The XCTest bundle covers approved-root refusals, symlink escapes, reserved browser files, temporary-file age, diagnostic retention, Chrome profile separation and Edge exclusion, Safari’s access-denied wording, login cleaning refusing Trash and Xcode, a second clean being refused while one is running, login-item settings, and history reports. Run them on a Mac with:

```bash
xcodebuild -project CleanSlate.xcodeproj -scheme CleanSlate -destination 'platform=macOS' test
```

They were not run in the environment that added this project.

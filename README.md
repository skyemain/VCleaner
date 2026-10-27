# Clenzy for iOS

Version 1.0, build 3. Native iPhone/iPad app for iOS 17+. Development candidate; not approved for production. This branch contains only iOS source, tests, project and CI. Main remains the catalog of separately approved production builds.

## What works

Clean Storage and Review Photos open the real PhotoKit scan/review flow. Screenshots, videos (including unmeasured videos) and exact duplicates are available. SHA-256 compares complete original/adjustment resources, never partial hashes. Duplicate groups show copies side by side and suggest the oldest item to keep. Nothing is preselected; selecting every copy generates a warning. Photos deletion is explicitly confirmed and also affects iCloud Photos and synced devices. Items remain in Recently Deleted, so selected source bytes do not guarantee immediate device-space recovery.

Standard scans review up to 1,000 recent accessible assets and read up to 512 MiB, with a 128 MiB resource limit. Optional deep scans allow 10,000 assets / 4 GiB / 1 GiB per resource. Limits are conservative, with up to one Photos callback chunk of overrun. Oversized/unavailable resources remain unmeasured, never exact duplicates. Results disclose coverage. Active Photos resource requests are cancelled when the user cancels. Cloud originals are not downloaded automatically.

Limited Photos access is honored, with a picker to share more photos. Library changes clear selection and require a rescan; modification dates and availability are rechecked before deletion. Image previews and local video playback are supported. A cloud placeholder directs the user to download originals in Photos when needed.

The SwiftUI home uses NavigationStack, SF Symbols, material cards, gradients and a storage ring. DeviceManager enables battery monitoring and queries filesystem total/free bytes; HomeViewModel uses @Observable. Storage and battery readings refresh on foreground/battery events. Unknown readings stay unavailable. Filesystem capacity can differ from Settings accounting. There are no simulated cleanup results or battery-time predictions. The privacy manifest declares disk-space display reason 85F4.1.

Clenzy does not clean other apps' private caches, force-quit apps or purge system RAM. It has no analytics, ads or network requests of its own.

## Build and install

Open Clenzy.xcodeproj in full Xcode. Choose an iPhone simulator and Run. Regenerate the project after adding/removing Swift files with `python3 scripts/generate-xcodeproj.py`. CI runs XCTest, captures the native home screen and builds an unsigned iPhone/iPad archive.

For your own iPhone: sign in under Xcode Settings > Apple Accounts, connect/unlock/trust the device, enable Developer Mode under iPhone Settings > Privacy & Security when prompted and restart/confirm. In the Clenzy target's Signing & Capabilities choose automatic signing, your Personal Team and a unique bundle identifier. Select the connected phone and Run. Free Personal Team provisioning expires after seven days; rebuild/reinstall as needed. App Store/TestFlight distribution requires paid membership and signing. The CI archive is unsigned and is not a directly installable public IPA.

References: [Personal Team limits](https://developer.apple.com/help/account/basics/about-your-developer-account), [running on devices](https://developer.apple.com/documentation/xcode/running-your-app-on-simulated-or-physical-devices), [PhotoKit](https://developer.apple.com/documentation/photos/phphotolibrary).

## Validation and maintainer reference

[Build 3 validation](https://github.com/skyemain/VCleaner/actions/runs/40408029608) passed eight native tests and the unsigned device archive. Tests cover hardware bounds/unknown readings, real-clean navigation, cancellation/budget guards, duplicate groups/all-copy warnings and video filtering. The native home screenshot was visually reviewed. No user photos are deleted by tests. Actual Photos permission prompts, system deletion cancellation, iCloud, edits/Live Photos, accessibility sizes and large libraries still require physical-device testing before production approval.

HomeView receives observable state; cards remain modular. LibraryModel owns authorization/selection/deletion and library changes; PhotoScanner owns bounded resource IO; ReviewRules owns pure grouping/filtering. Keep this as the only Markdown file and never commit signing credentials.

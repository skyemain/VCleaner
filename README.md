# Clenzy for iOS

Development candidate for iPhone/iPad, iOS 17+. Not approved for production. Only iOS source, project, tests and CI live on this branch.

## What it actually cleans

Clenzy reviews Photos screenshots, local videos at least 100 MiB, and exact matching original/adjustment resource hashes. It streams local resources with SHA-256, without downloading iCloud originals, and scans at most the 1,000 newest accessible items per pass. Limited Photos access is honored. Unknown sizes/hashes are omitted from duplicate and large-video matches. Both originals remain visible; nothing is preselected. Tap a thumbnail to review, select individual items, then explicitly confirm deletion through PhotoKit. Deletion affects iCloud Photos and synced devices. Photos retains Recently Deleted items, so space may not be reclaimed immediately. Storage usage is read from the device.

An ordinary iOS app cannot clean Safari/other apps' private caches, force-quit other apps or purge system RAM. This app makes no such claims. It has no analytics, ads or network requests of its own. No deletion happens in tests or on launch.

## Setup, build and test

Open Clenzy.xcodeproj in full Xcode. Select an iPhone simulator and run. On your own iPhone select your signing team and a unique bundle identifier, then run through Xcode. Grant Photos access when Scan Photos is tapped. Denied access can be changed through Open Clenzy Settings. A free personal team can support local development subject to Apple's limits; App Store/TestFlight distribution requires a developer membership and signing. No installable public IPA is provided.

Regenerate with `python3 scripts/generate-xcodeproj.py`. CI runs native XCTest on an available iPhone simulator and builds an unsigned device archive. Tests use synthetic review metadata and injected hardware readings; real library authorization, deletion, limited access and iCloud behavior require testing on a device before approval. Cancel stops after the current local resource request completes.

## Maintainer reference

SwiftUI dashboard, LibraryModel for explicit authorization/deletion, PhotoScanner for bounded resource scans and ReviewRules for pure filtering. Keep one Markdown file. Platform docs: [Photo library authorization/change requests](https://developer.apple.com/documentation/photos/phphotolibrary), [local resource access](https://developer.apple.com/documentation/photos/phassetresourcerequestoptions/isnetworkaccessallowed). Main contains only separately approved production builds. Before promotion test duplicate edge cases (edits/Live Photos), cancellations, system deletion rejection and large real libraries.

Validation: [Build 2 native tests, screenshot and unsigned device archive](https://github.com/skyemain/VCleaner/actions/runs/40253636560) passed all eight tests. The simulator screenshot was visually reviewed and card spacing refined so storage, battery and time information fit above the fixed Clean button. Simulator battery readings can be unavailable; the app keeps them unknown instead of supplying fabricated percentages. Tests do not delete real photos; device Photos permissions, deletion and iCloud behavior still need manual validation before production approval.

## Native dashboard (build 2)

HomeView uses NavigationStack, SF Symbols, ultraThinMaterial cards, gradients, a storage ring and an always-visible Clean Storage action. StorageCard and BatteryCard are separate files. DeviceManager is a main-actor singleton behind DeviceReading; it enables UIDevice battery monitoring and reads total/free filesystem bytes with FileManager.attributesOfFileSystem, deriving used bytes. Unknown readings stay unavailable. Storage is filesystem capacity and may differ from Settings accounting. Battery notifications and foreground events refresh readings. HomeViewModel uses @Observable; dashboard/card views receive their data and keep no independent hardware/scanning state.

Time remaining is explicitly an illustrative 15-hour-at-100% model, not a measured runtime prediction. Clean Storage runs a cancellable preview animation and opens clearly labeled sample results; no files are scanned/deleted and no reclaimed space is claimed. The separate Review Photos route retains real PhotoKit review/deletion. Reduced Motion avoids the radar sweep. The privacy manifest declares on-device disk-space display (85F4.1).

Tests cover filesystem bounds/unknown values, unknown battery levels, the illustrative formula, refreshed hardware snapshots, preview completion and cancellation. Full Xcode/CI is required for native verification. iOS remains a development candidate; main production builds are unchanged.

## Install on your own iPhone with a free Personal Team

Requires an iPhone on iOS 17+ and full Xcode on a Mac. Download the ios branch source ZIP and open Clenzy.xcodeproj. In Xcode Settings > Apple Accounts, sign in with your Apple Account. Connect and unlock the iPhone, accept Trust, and select it as the run destination so Xcode can pair it. Enable Developer Mode in iPhone Settings > Privacy & Security when requested, restart and confirm.

Select the Clenzy app target > Signing & Capabilities. Enable Automatically manage signing, select your Personal Team and use a unique bundle identifier. Choose your connected iPhone (not a simulator) and press Run. Xcode creates the development provisioning profile and installs the app. The inherited Debug ad-hoc identity override was removed so Xcode can select Apple Development signing.

Apple's free Personal Team profiles expire after seven days; rebuild/reinstall through Xcode when needed. Source: [Apple Personal Team limits](https://developer.apple.com/help/account/basics/about-your-developer-account), [device setup and running](https://developer.apple.com/documentation/xcode/running-your-app-on-simulated-or-physical-devices), [Developer Mode](https://developer.apple.com/documentation/xcode/enabling-developer-mode-on-a-device). The existing CI archive remains unsigned and is not a directly installable public IPA.

Once installed, Clean Storage is the labeled scanning/results preview. Review Photos opens the real PhotoKit cleanup flow.

## Real cleanup update (build 3)

Clean Storage opens a real Photos scan. No simulated scan/results or battery lifetime predictions remain. Standard scans review up to 1,000 recent accessible assets and read up to 512 MiB (128 MiB per resource); the opt-in deep scan allows 10,000 assets / 4 GiB / 1 GiB per resource. Oversized and cloud resources remain unmeasured, never exact duplicates. Limits are conservative, with up to one Photos callback chunk of overrun. Cancellation cancels the active Photos request.

Duplicate groups suggest the oldest copy to keep but never auto-select. Selecting every copy produces an explicit warning. Photos changes clear selection and require a rescan; modified/unavailable assets are checked again before deletion. Video previews do not download cloud originals. Both dashboards use DeviceManager for storage. Deleted photos stay in Recently Deleted, and measured bytes do not promise immediate device-space recovery.

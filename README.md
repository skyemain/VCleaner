# Clenzy for iOS

Development candidate for iPhone/iPad, iOS 17+. Not approved for production. Only iOS source, project, tests and CI live on this branch.

## What it actually cleans

Clenzy reviews Photos screenshots, local videos at least 100 MiB, and exact matching original/adjustment resource hashes. It streams local resources with SHA-256, without downloading iCloud originals, and scans at most the 1,000 newest accessible items per pass. Limited Photos access is honored. Unknown sizes/hashes are omitted from duplicate and large-video matches. Both originals remain visible; nothing is preselected. Tap a thumbnail to review, select individual items, then explicitly confirm deletion through PhotoKit. Deletion affects iCloud Photos and synced devices. Photos retains Recently Deleted items, so space may not be reclaimed immediately. Storage usage is read from the device.

An ordinary iOS app cannot clean Safari/other apps' private caches, force-quit other apps or purge system RAM. This app makes no such claims. It has no analytics, ads or network requests of its own. No deletion happens in tests or on launch.

## Setup, build and test

Open Clenzy.xcodeproj in full Xcode. Select an iPhone simulator and run. On your own iPhone select your signing team and a unique bundle identifier, then run through Xcode. Grant Photos access when Scan Photos is tapped. Denied access can be changed through Open Clenzy Settings. A free personal team can support local development subject to Apple's limits; App Store/TestFlight distribution requires a developer membership and signing. No installable public IPA is provided.

Regenerate with `python3 scripts/generate-xcodeproj.py`. CI runs native XCTest on an available iPhone simulator and builds an unsigned device archive. Tests use synthetic review metadata only; real library authorization, deletion, limited access and iCloud behavior require testing on a device before approval. Cancel stops after the current local resource request completes.

## Maintainer reference

SwiftUI dashboard, LibraryModel for explicit authorization/deletion, PhotoScanner for bounded resource scans and ReviewRules for pure filtering. Keep one Markdown file. Platform docs: [Photo library authorization/change requests](https://developer.apple.com/documentation/photos/phphotolibrary), [local resource access](https://developer.apple.com/documentation/photos/phassetresourcerequestoptions/isnetworkaccessallowed). Main contains only separately approved production builds. Before promotion test duplicate edge cases (edits/Live Photos), cancellations, system deletion rejection and large real libraries.

Validation: [iOS simulator tests and unsigned device archive](https://github.com/skyemain/VCleaner/actions/runs/40237187888) passed. The two metadata tests do not delete real photos; device Photos permissions, deletion and iCloud behavior still need manual validation before production approval.

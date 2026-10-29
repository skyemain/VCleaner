# Clenzy for Android

Version 1.2.0. Native Android 8+ development candidate, not approved for production. Only Android source, tests, builds and CI belong on this branch. Main remains the catalog of approved production platforms.

## Cleanup and review

Photos & Videos scans authorized MediaStore images/videos, including selected-photo access on Android 14+. Filters include large files (100 MiB+), files older than 30 days, screenshots (filename match), videos and exact duplicates. Nothing is selected automatically. Duplicate copies appear together with an oldest suggested keeper; selecting every copy warns explicitly. Preview buttons open installed viewers, and thumbnails load where the provider supports them. Lists recycle rows and offer size/name sorting.

On Android 11+, selected media is rechecked against the scan and passed to Android's system-confirmed Trash flow (up to 200 items per request). Older Android versions support media review/preview; remove media in the gallery or through a selected document folder. Trashed items may still occupy space. Clenzy reports source sizes, not a promise of immediate space recovered.

Choose folder uses the Storage Access Framework. Only selected files from the scanned tree can be deleted; directories are never deleted. File size/modification time and duplicate hashes are rechecked. Folder deletion may be permanent and may remove synced cloud copies. File age alone never means junk. Providers can change files between checks; keep backups and review selections.

Folder scans cap at 5,000 entries and 12 levels; media scans at 10,000 items. Standard SHA-256 hashing checks same-sized files up to 64 MiB with a 512 MiB total budget. Optional deep scans allow 1 GiB per file and 4 GiB total. Results disclose checked/unhashed and inaccessible counts. Document providers may download content and consume data. Cancellation is cooperative; a slow provider read can delay stopping.

A retained ViewModel preserves scan/results/selection across Activity recreation. Process death starts a new review, retaining only the folder preference/grant. Denied/revoked access asks the user to choose/grant access again. No all-files, accessibility, internet or task-killing permission is used. Clenzy cannot clean other apps' private caches or system RAM.

## Build and signing

Use JDK 17, Gradle 8.11.1, Android SDK 35 and AGP 8.9.2. Run `gradle testDebugUnitTest lintDebug assembleDebug assembleRelease bundleRelease`. With an emulator/device, run `gradle connectedDebugAndroidTest`.

CI restores the permanent release key from encrypted repository secrets CLENZY_KEYSTORE_BASE64 and CLENZY_STORE_PASSWORD. PKCS12 alias: clenzy. Never commit the key or password; keep an independent secure backup. Local signing accepts CLENZY_KEYSTORE (absolute path) and CLENZY_STORE_PASSWORD environment variables. Missing secrets produce an unsigned release and prevent publication. Version codes increase with the workflow run number. The signed candidate is published under release/ after successful checks. Switching from the old debug candidate requires a one-time uninstall; subsequent upgrades must retain the same release key.

## Validation and architecture

[Validated signed build](https://github.com/skyemain/VCleaner/actions/runs/40574537461): four unit tests, four Android 35 instrumentation tests, lint, APK/AAB builds and release signature verification passed. [Download the signed APK](release/Clenzy-1.1-android-candidate.apk). Home and duplicate-review screenshots were captured from the emulator and inspected.

Unit tests cover filtering, metadata guards and all-copy warnings. Android 35 instrumentation covers the real system folder picker, fixture-only deletion, changed/unscanned-file protection, MediaStore duplicate detection, changed-media checks, cancellation and Activity recreation. Test screenshots use generated fixture images, not the user's photos. Additional physical-device/local/cloud provider, permission-denial and system Trash confirmation tests are required before production approval.

MainActivity renders the UI and system prompts. CleanupViewModel retains operations/state. DocumentCleaner handles SAF; MediaCleaner handles MediaStore. ReviewRules/ReviewFile provide pure filters and guards. AndroidX Activity/Lifecycle/RecyclerView are runtime dependencies. Keep this as the only Markdown file. [Android document access](https://developer.android.com/training/data-storage/shared/documents-files) and [media access](https://developer.android.com/training/data-storage/shared/media) are the platform references.

## Android 1.2.0 redesign

HomeActivity is the new launcher with Material 3 components, measured storage, Start Cleaning, Large Files, Duplicate Files, Downloads and Apps. Native Java views, AndroidX ViewModel and existing SAF/MediaStore engines are retained rather than introducing a second Compose/Kotlin UI stack. System/Light/Dark preferences use AppCompat. No app identity or signing-key change.

Start Cleaning explains source access and offers Clenzy cache, authorized photos/videos, or a user-selected folder. Cache cleanup is limited to unchanged files over 24 hours old in Clenzy’s own canonical cache directory; symlinks and unscanned files are rejected. Cache is the only automatically selected category. Personal files always start unchecked. Folder review includes small downloads through All accessible files, with name/size/date sorting, selection totals, previews and metadata revalidation. Large files and duplicates reuse the same scan/deletion engines. Media trash results are verified with is_trashed and never counted as reclaimed bytes. Document deletion is checked after the provider operation. Actual filesystem free space is measured separately.

Permissions are requested contextually. Partial media access is accepted; denied access offers folder review or Settings, with a retained continuation state. App management lists launcher apps Android makes visible and opens system app-info/uninstall screens. REQUEST_DELETE_PACKAGES is used only for user-requested system uninstall. No QUERY_ALL_PACKAGES, all-files, Accessibility, root or internet permissions. Broad photo/video permission use requires Google Play review; distribution eligibility is not asserted.

Cleanup history retains up to 20 local folder/cache cleanup entries, with the latest summary on Home. User preferences/history use the existing SharedPreferences approach. No analytics or server dependency. Model operations survive Activity recreation; process death intentionally requires a fresh scan before deletion.

Platform limits: no other-app private cache cleanup; no trustworthy automatic residual-app-file classification; no automatic empty-directory removal because Android providers may recursively delete contents changed by another app. Downloads require a supported folder/document grant. Package storage sizes are not exposed and are not invented. Folder providers may keep trash or represent cloud files; source bytes deleted are distinct from actual device free-space changes. Scan/deletion cancellation remains cooperative during slow provider calls.

Automated coverage adds cache eligibility/changed-file refusal, unique selection totals, small-download review, launcher recreation, primary-action routing and light/dark screenshots. Build, lint, emulator and signing verification results must be checked for the final commit before declaring the candidate validated. Physical-device, cloud-provider, permanent-denial and interruption stress testing remain required before production approval.

Validation for 1.2.0: https://github.com/skyemain/VCleaner/actions/runs/40917106034 passed unit tests, lint, debug/release APK and AAB builds, release signature verification and all nine Android 35 emulator instrumentation tests. The guided test exercises Home → cache source → real scan → selection → confirmation → verified results and asserts removal of its disposable fixture. Earlier light/dark screenshots were visually inspected; later changes affect review-button contrast and testing only. APK SHA256: ba57d6697ba1fb41a4204dad0b28713f707a509c43f862187ae3c85d1f20fbaa. Download release/Clenzy-1.2.0-android-candidate.apk. Signing certificate remains unchanged. This is a tested development candidate, not approval for production or a claim that physical-device/provider/permission edge cases are all covered.


## Android 1.3.0 shared-storage workflow
Start Cleaning, Large Files, Duplicates and Downloads now request one optional all-files grant (Android 11+) and scan primary shared storage automatically. Downloads scans Download directly. No folder picker is needed for these routes. Android/data and Android/obb are excluded; Android/media is included. Other apps’ private caches remain inaccessible. Android 10 uses media-only fallback; Android 8/9 use storage runtime permission.

Review provides Select all shown, Select extra copies (keeps the earliest dated copy in each exact-hash group), and Deselect all. Deletion still requires explicit confirmation. Shared-file deletes are permanent. Scans bound traversal at 100,000 entries and duplicate hashing at 16 GiB per scan; unmatched/unhashed files are never presented as verified duplicates. Only primary shared storage is currently scanned. Apps remains a launcher-app manager, not a private-cache cleaner.

All-files access requires separate Google Play eligibility review; this is a sideload candidate, not a claim of Play approval or complete CCleaner parity. Native tests cover automatic discovery, duplicate bulk selection, verified deletion, Downloads scope, stale files and symlink/private-data exclusions.

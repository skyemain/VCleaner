# Clenzy for Android

Version 1.1. Native Android 8+ development candidate, not approved for production. Only Android source, tests, builds and CI belong on this branch. Main remains the catalog of approved production platforms.

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

Unit tests cover filtering, metadata guards and all-copy warnings. Android 35 instrumentation covers the real system folder picker, fixture-only deletion, changed/unscanned-file protection, MediaStore duplicate detection, changed-media checks, cancellation and Activity recreation. Test screenshots use generated fixture images, not the user's photos. Additional physical-device/local/cloud provider, permission-denial and system Trash confirmation tests are required before production approval.

MainActivity renders the UI and system prompts. CleanupViewModel retains operations/state. DocumentCleaner handles SAF; MediaCleaner handles MediaStore. ReviewRules/ReviewFile provide pure filters and guards. AndroidX Activity/Lifecycle/RecyclerView are runtime dependencies. Keep this as the only Markdown file. [Android document access](https://developer.android.com/training/data-storage/shared/documents-files) and [media access](https://developer.android.com/training/data-storage/shared/media) are the platform references.

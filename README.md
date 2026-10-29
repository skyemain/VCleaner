# Clenzy for Android

Version 1.3.0. Development candidate; not approved for production. Commit Android changes only to `android`. Keep this as the only Markdown file. Main remains the approved production catalog.

## Android 1.3.0 shared-storage workflow
Start Cleaning, Large Files, Duplicates and Downloads now request one optional all-files grant (Android 11+) and scan primary shared storage automatically. Downloads scans Download directly. No folder picker is needed for these routes. Android/data and Android/obb are excluded; Android/media is included. Other apps’ private caches remain inaccessible. Android 10 uses media-only fallback; Android 8/9 use storage runtime permission.

Review provides Select all shown, Select extra copies (keeps the earliest dated copy in each exact-hash group), and Deselect all. Deletion still requires explicit confirmation. Shared-file deletes are permanent. Scans bound traversal at 100,000 entries and duplicate hashing at 16 GiB per scan; unmatched/unhashed files are never presented as verified duplicates. Only primary shared storage is currently scanned. Apps lists visible launcher apps sorted by measured app+data size with optional Usage access. Cache is shown separately without double counting. Last-use records are queried for 90 days; missing records are labeled unknown. Android Settings handles cache removal, and Android confirms uninstall.

All-files access requires separate Google Play eligibility review; this is a sideload candidate, not a claim of Play approval or complete CCleaner parity. Native tests cover automatic discovery, duplicate bulk selection, verified deletion, Downloads scope, stale files and symlink/private-data exclusions.

## Build and signing

Use JDK 17, Gradle 8.11.1, Android SDK 35 and AGP 8.9.2. Run `gradle testDebugUnitTest lintDebug assembleDebug assembleRelease bundleRelease`. With an emulator/device, run `gradle connectedDebugAndroidTest`.

CI restores the permanent release key from encrypted repository secrets CLENZY_KEYSTORE_BASE64 and CLENZY_STORE_PASSWORD. PKCS12 alias: clenzy. Never commit the key or password; keep an independent secure backup. Local signing accepts CLENZY_KEYSTORE (absolute path) and CLENZY_STORE_PASSWORD environment variables. Missing secrets produce an unsigned release and prevent publication. Version codes increase with the workflow run number. The signed candidate is published under release/ after successful checks. Switching from the old debug candidate requires a one-time uninstall; subsequent upgrades must retain the same release key.

## Architecture and additional tools
Native Java/Material 3/AppCompat UI with AndroidX ViewModel and RecyclerView. HomeActivity launches automatic scanning and the app-storage manager. MainActivity renders review, bulk selection, confirmation and results. CleanupViewModel owns background work and retained state. SharedStorageCleaner handles primary shared storage; DocumentCleaner handles SAF; MediaCleaner handles MediaStore; OwnCacheCleaner deletes only Clenzy’s unchanged cache files older than 24 hours. ReviewRules handles filters and keeper selection.

Settings provides Photos & videos and Clenzy cache as additional tools. Media access can be partial; Android 11+ trash operations use the system confirmation and report verified trash counts, never claimed reclaimed bytes. SAF supports authorized providers; folder deletion may affect synced copies. Folder scanning caps at 5,000 entries/12 levels; media at 10,000 items. Their default duplicate hashing budget is 512 MiB, 64 MiB per file; legacy deep scanning allows 4 GiB/1 GiB. Shared storage uses the separate limits above.

No internet, analytics, root, Accessibility service or task killing. Optional all-files and Usage access are granted through Android Settings. Model state survives Activity recreation; process death requires a fresh scan. Cleanup history stores up to 20 verified folder/cache/shared-file results locally. File size/time and duplicate hashes are rechecked before deletion; symlinks and unscanned paths are rejected. A retained duplicate keeper must still exist and match before extra copies are deleted.

## Remaining limits
No automatic private app-cache cleaning, residual-file classification, empty-directory deletion, similar-photo detection, or removable-volume scanning. Other-app cache cleaning opens Android app settings. Physical-device/OEM behavior, revoked permission and cloud-provider edge cases require more testing before production. Do not claim exact CCleaner parity. Google Play broad-storage/media permission eligibility is separate from successful builds.

## Verification
Unit and Android 35 instrumentation cover review rules, provider/media/cache operations, real automatic shared-storage scan and bulk duplicate deletion, retained-copy checks, changed files, Downloads scope, symlinks, private Android directories and UI recreation. The signed candidate is published only after CI passes.

Platform references: [all-files access](https://developer.android.com/training/data-storage/manage-all-files), [SAF](https://developer.android.com/training/data-storage/shared/documents-files), [media access](https://developer.android.com/training/data-storage/shared/media), [StorageStatsManager](https://developer.android.com/reference/android/app/usage/StorageStatsManager).

Validated candidate: [CI 40963593988](https://github.com/skyemain/VCleaner/actions/runs/40963593988) passed build, unit tests, lint, release-signature verification and all 14 Android 35 instrumentation tests. [Download 1.3.0](release/Clenzy-1.3.0-android-candidate.apk). APK SHA256: `aa5e33b57660f1647f567b827f28a1d66cf8f78f4a7784306760db282e89649f`. Permanent signing certificate unchanged. App-size/usage results and manufacturer-specific settings still require physical-device verification.

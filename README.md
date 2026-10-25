# Clenzy for Android

Native Android 8+ development candidate. Not approved for production. Only Android code, tests and build files live here.

## Useful cleanup within Android permissions

Choose a folder through Android's Storage Access Framework. Clenzy reviews large files (100 MiB+), files older than 30 days, and exact duplicate contents. Long-press a row to preview through an installed viewer. Nothing is selected automatically. Select files and confirm permanent deletion; cloud-provider deletion can also affect synced devices. Age alone does not make a file junk. There may be no recycle bin.

Scanning is bounded to 5,000 entries and 12 directory levels. SHA-256 duplicate hashing reads complete same-size files up to 64 MiB, with a 512 MiB total hash budget per scan. Missing/changed/unreadable metadata is reported or skipped. Before deletion the app rechecks document metadata and, for hash matches, contents. Only files from the current chosen-tree scan can be deleted. Directory deletion is refused. Providers may change files between checks; verify selections and keep backups.

The app requests no broad storage, accessibility, internet or background kill permissions. Android restricts system/data folders and some root selections; choose an accessible subfolder. Clenzy cannot erase other apps' private caches or clean system RAM. RAM and storage metrics come from Android, not estimates of recovered space. Permission denial/cancellation is handled without deletion; reselect the folder if a stored grant expires. Rotation cancels work and requires a new scan.

## Build, install and verify

Use Android Studio with JDK 17, Gradle 8.11.1, Android SDK 35 and AGP 8.9.2. From the root run `gradle testDebugUnitTest lintDebug assembleDebug assembleRelease`. CI also runs an Android 35 emulator smoke test and publishes `release/Clenzy-1.0-android-candidate.apk`. This APK is debug-signed for development and is not a production/store build. Installing may require allowing installs from the source used to open it. Release APKs require your own signing key; never commit private keys. Run `gradle connectedDebugAndroidTest` with a device/emulator.

## Maintainer reference

Framework Activity UI, DocumentCleaner for SAF enumeration/hashing/deletion and pure ReviewRules/ReviewFile for filtering and metadata guards. No external application libraries; AndroidX is used only in tests. Keep this as the only Markdown file. API reference: [Android shared document access](https://developer.android.com/training/data-storage/shared/documents-files). Before production approval test real local/cloud document providers, expired grants, cancellation, file modification races and confirmed deletion on a physical device. Main retains approved platform builds only.

import Darwin
import Foundation

struct DeletionOutcome: Equatable, Sendable {
    var accessDenied = false
    var deletedCount: Int = 0
    var deletedBytes: Int64 = 0
    var skippedCount: Int = 0
    var errors: [String] = []
}

/// Deletes only regular files that have been re-checked immediately before removal.
///
/// The approved root itself is never removed. Symbolic links are never followed and never deleted.
/// Nothing under a path that fails `ProtectedPaths` is touched, so a replaced directory or a link that
/// escapes the cache cannot widen the delete.
struct SafeDeletionService: Sendable {
    var allowXcodeDerivedData = false
    var allowTrash = false
    var includeHidden = false

    func deleteEligibleFiles(
        under root: URL,
        context: ScanContext,
        minimumAge: Date?,
        shouldCancel: @escaping () -> Bool = { false }
    ) -> DeletionOutcome {
        var outcome = DeletionOutcome()
        let resolvedRoot = root.standardizedFileURL.resolvingSymlinksInPath()
        guard ProtectedPaths.isApprovedRoot(
            resolvedRoot,
            home: context.locations.home,
            temporaryDirectory: context.locations.temporaryDirectory,
            allowXcodeDerivedData: allowXcodeDerivedData,
            allowTrash: allowTrash
        ) else {
            outcome.skippedCount += 1
            outcome.errors.append("Refused \(root.lastPathComponent): it is not an approved cache location.")
            return outcome
        }

        guard !context.dryRun else {
            let stats = DirectoryWalker.collect(
                root: resolvedRoot,
                minimumAge: minimumAge,
                exclusions: context.exclusions,
                fileManager: FileManager(), includeHidden: includeHidden
            )
            outcome.accessDenied = stats.accessDenied
            outcome.deletedCount = stats.fileCount
            outcome.deletedBytes = stats.byteCount
            outcome.skippedCount = stats.skippedCount
            return outcome
        }

        let files = DirectoryWalker.files(
            root: resolvedRoot,
            minimumAge: minimumAge,
            exclusions: context.exclusions,
            fileManager: FileManager(), includeHidden: includeHidden
        )
        outcome.accessDenied = files.accessDenied
        outcome.skippedCount += files.skippedCount
        for file in files.urls {
            if shouldCancel() {
                outcome.errors.append("Cancelled. Files already removed were not restored.")
                break
            }
            switch deleteOne(file, approvedRoot: resolvedRoot, context: context, minimumAge: minimumAge) {
            case .deleted(let bytes):
                outcome.deletedCount += 1
                outcome.deletedBytes += bytes
            case .skipped:
                outcome.skippedCount += 1
            case .failed(let message):
                outcome.skippedCount += 1
                if outcome.errors.count < 50 {
                    outcome.errors.append(message)
                }
            }
        }
        return outcome
    }

    /// Deletes specific files that are already known to sit inside an approved root. Used for filtered sets such as old diagnostic reports.
    func deleteListedFiles(_ urls: [URL], under root: URL, context: ScanContext, minimumAge: Date? = nil, shouldCancel: @escaping () -> Bool = { false }) -> DeletionOutcome {
        var outcome = DeletionOutcome()
        let resolvedRoot = root.standardizedFileURL.resolvingSymlinksInPath()
        guard ProtectedPaths.isApprovedRoot(
            resolvedRoot,
            home: context.locations.home,
            temporaryDirectory: context.locations.temporaryDirectory,
            allowXcodeDerivedData: allowXcodeDerivedData,
            allowTrash: allowTrash
        ) else {
            outcome.skippedCount += 1
            outcome.errors.append("Refused \(root.lastPathComponent): it is not an approved cache location.")
            return outcome
        }
        for url in urls {
            if shouldCancel() {
                outcome.errors.append("Cancelled. Files already removed were not restored.")
                break
            }
            if context.dryRun {
                let size = (try? url.resourceValues(forKeys: [.fileSizeKey]))?.fileSize ?? 0
                outcome.deletedCount += 1
                outcome.deletedBytes += Int64(size)
                continue
            }
            switch deleteOne(url, approvedRoot: resolvedRoot, context: context, minimumAge: minimumAge) {
            case .deleted(let bytes):
                outcome.deletedCount += 1
                outcome.deletedBytes += bytes
            case .skipped:
                outcome.skippedCount += 1
            case .failed(let message):
                outcome.skippedCount += 1
                if outcome.errors.count < 50 { outcome.errors.append(message) }
            }
        }
        return outcome
    }

    private enum OneShot {
        case deleted(Int64)
        case skipped
        case failed(String)
    }

    private func deleteOne(_ url: URL, approvedRoot: URL, context: ScanContext, minimumAge: Date?) -> OneShot {
        let path = url.path
        if context.isExcluded(path) || ProtectedPaths.isReserved(url) {
            return .skipped
        }
        var info = Darwin.stat()
        guard lstat(path, &info) == 0 else {
            return .skipped
        }
        if (info.st_mode & S_IFMT) == S_IFLNK {
            return .skipped
        }
        guard (info.st_mode & S_IFMT) == S_IFREG else {
            return .skipped
        }
        if info.st_uid != getuid() {
            return .skipped
        }
        let resolved = url.resolvingSymlinksInPath().standardizedFileURL.path
        let rootPath = approvedRoot.standardizedFileURL.resolvingSymlinksInPath().path
        guard resolved == rootPath || resolved.hasPrefix(rootPath + "/") else {
            return .failed("Skipped \(url.lastPathComponent): it resolved outside the approved folder.")
        }
        guard ProtectedPaths.isApprovedRoot(
            URL(fileURLWithPath: resolved),
            home: context.locations.home,
            temporaryDirectory: context.locations.temporaryDirectory,
            allowXcodeDerivedData: allowXcodeDerivedData,
            allowTrash: allowTrash
        ) else {
            return .skipped
        }

        let descriptor = open(path, O_RDONLY | O_NOFOLLOW | O_CLOEXEC)
        guard descriptor >= 0 else {
            return .failed("Could not open \(url.lastPathComponent).")
        }
        defer { close(descriptor) }
        var opened = Darwin.stat()
        guard fstat(descriptor, &opened) == 0 else {
            return .failed("Could not confirm \(url.lastPathComponent).")
        }
        guard opened.st_dev == info.st_dev, opened.st_ino == info.st_ino else {
            return .failed("Skipped \(url.lastPathComponent): it changed before it could be removed.")
        }
        if let minimumAge {
            let modified = Date(timeIntervalSince1970: Double(opened.st_mtimespec.tv_sec) + Double(opened.st_mtimespec.tv_nsec) / 1_000_000_000)
            if modified > minimumAge { return .skipped }
        }
        let bytes = Int64(opened.st_size)
        guard unlink(path) == 0 else {
            return .failed("Could not remove \(url.lastPathComponent).")
        }
        return .deleted(bytes)
    }
}

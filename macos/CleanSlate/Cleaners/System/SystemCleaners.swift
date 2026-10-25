import Darwin
import Foundation

enum SystemCleaners {
    static let diagnosticRetention: TimeInterval = 30 * 24 * 60 * 60
    static let temporaryFileAge: TimeInterval = 24 * 60 * 60
    static let looseCacheAge: TimeInterval = 7 * 24 * 60 * 60

    static func temporaryFiles(_ context: ScanContext) -> ModuleEstimate {
        estimate(
            id: "temp",
            title: "Temporary files",
            root: context.locations.temporaryDirectory,
            context: context,
            minimumAge: context.now.addingTimeInterval(-temporaryFileAge), includeHidden: true
        )
    }

    static func userCacheEstimates(_ context: ScanContext) -> [ModuleEstimate] {
        ProtectedPaths.systemCacheRelativeRoots.enumerated().map { index, relative in
            estimate(id: "user-cache-\(index)", title: "Cache: \(URL(fileURLWithPath: relative).lastPathComponent)",
                root: context.locations.url(relative), context: context,
                minimumAge: context.now.addingTimeInterval(-looseCacheAge))
        }
    }

    static func cleanUserCache(_ id: String, context: ScanContext, cancel: @escaping () -> Bool) -> ModuleCleanup {
        guard let index = Int(id.replacingOccurrences(of: "user-cache-", with: "")),
              ProtectedPaths.systemCacheRelativeRoots.indices.contains(index) else {
            return ModuleCleanup(deletedCount: 0, deletedBytes: 0, skippedCount: 0, errors: [], status: .unsupported("Unknown cache"))
        }
        return clean(idRoot: context.locations.url(ProtectedPaths.systemCacheRelativeRoots[index]),
            context: context, minimumAge: context.now.addingTimeInterval(-looseCacheAge),
            allowXcode: false, allowTrash: false, cancel: cancel)
    }

    static func userCaches(_ context: ScanContext) -> ModuleEstimate {
        let estimates = ProtectedPaths.systemCacheRelativeRoots.map { relative in
            estimate(id: "user-cache", title: "Supported user caches", root: context.locations.url(relative),
                     context: context, minimumAge: context.now.addingTimeInterval(-looseCacheAge))
        }
        return ModuleEstimate(id: "user-cache", title: "Supported user caches",
            status: estimates.first(where: { $0.status != .ready })?.status ?? .ready,
            fileCount: estimates.reduce(0) { $0 + $1.fileCount },
            byteCount: estimates.reduce(0) { $0 + $1.byteCount },
            samplePaths: Array(estimates.flatMap(\.samplePaths).prefix(20)))
    }

    static func looseCacheFiles(_ context: ScanContext) -> ModuleEstimate {
        let root = context.locations.url("Library/Caches")
        if DirectoryWalker.isUnreadable(root) {
            return ModuleEstimate(id: "loose-cache", title: "Loose cache files", status: .unavailable(PermissionService.accessHelp), fileCount: 0, byteCount: 0, samplePaths: [])
        }
        var estimate = ModuleEstimate(id: "loose-cache", title: "Loose cache files", status: .ready, fileCount: 0, byteCount: 0, samplePaths: [])
        let fm = FileManager()
        guard let contents = try? fm.contentsOfDirectory(at: root, includingPropertiesForKeys: [.isRegularFileKey, .fileSizeKey, .contentModificationDateKey, .isSymbolicLinkKey], options: [.skipsHiddenFiles]) else {
            return estimate
        }
        let cutoff = context.now.addingTimeInterval(-looseCacheAge)
        var count = 0
        var bytes: Int64 = 0
        var sample: [String] = []
        for url in contents {
            let values = try? url.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey, .contentModificationDateKey, .isSymbolicLinkKey])
            guard values?.isSymbolicLink != true, values?.isRegularFile == true else { continue }
            guard let modified = values?.contentModificationDate, modified <= cutoff else { continue }
            if context.isExcluded(url.path) || ProtectedPaths.isReserved(url) { continue }
            count += 1
            bytes += Int64(values?.fileSize ?? 0)
            if sample.count < 20 { sample.append(url.path) }
        }
        estimate = ModuleEstimate(id: "loose-cache", title: "Loose cache files", status: .ready, fileCount: count, byteCount: bytes, samplePaths: sample)
        return estimate
    }

    static func diagnosticReports(_ context: ScanContext) -> ModuleEstimate {
        let root = context.locations.url("Library/Logs/DiagnosticReports")
        let allowed: Set<String> = ["ips", "crash", "spin", "diag", "hang"]
        let cutoff = context.now.addingTimeInterval(-diagnosticRetention)
        let fm = FileManager()
        var count = 0
        var bytes: Int64 = 0
        var sample: [String] = []
        var skipped = 0
        guard fm.fileExists(atPath: root.path) else {
            return ModuleEstimate(id: "diagnostics", title: "Diagnostic reports", status: .ready, fileCount: 0, byteCount: 0, samplePaths: [])
        }
        if DirectoryWalker.isUnreadable(root) {
            return ModuleEstimate(id: "diagnostics", title: "Diagnostic reports", status: .unavailable(PermissionService.accessHelp), fileCount: 0, byteCount: 0, samplePaths: [])
        }
        let urls = (try? fm.contentsOfDirectory(at: root, includingPropertiesForKeys: [.isRegularFileKey, .fileSizeKey, .contentModificationDateKey, .isSymbolicLinkKey], options: [.skipsHiddenFiles])) ?? []
        for url in urls {
            let values = try? url.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey, .contentModificationDateKey, .isSymbolicLinkKey])
            guard values?.isSymbolicLink != true, values?.isRegularFile == true else { skipped += 1; continue }
            guard allowed.contains(url.pathExtension.lowercased()) else { skipped += 1; continue }
            guard let modified = values?.contentModificationDate, modified <= cutoff else { skipped += 1; continue }
            if context.isExcluded(url.path) { skipped += 1; continue }
            count += 1
            bytes += Int64(values?.fileSize ?? 0)
            if sample.count < 20 { sample.append(url.path) }
        }
        _ = skipped
        return ModuleEstimate(id: "diagnostics", title: "Diagnostic reports", status: .ready, fileCount: count, byteCount: bytes, samplePaths: sample)
    }

    /// Only archived log names: app.log.gz, app.log.1, app.log.2.gz, etc.
    static func isRotatedLog(_ url: URL) -> Bool {
        let name = url.lastPathComponent.lowercased()
        if name.hasSuffix(".log.gz") { return true }
        guard let marker = name.range(of: ".log.", options: .backwards) else { return false }
        var suffix = String(name[marker.upperBound...])
        if suffix.hasSuffix(".gz") { suffix.removeLast(3) }
        return !suffix.isEmpty && suffix.allSatisfy { $0.isNumber && $0.isASCII }
    }

    private static func rotatedLogFiles(_ context: ScanContext) -> WalkStats {
        DirectoryWalker.files(root: context.locations.url("Library/Logs"),
            minimumAge: context.now.addingTimeInterval(-diagnosticRetention),
            exclusions: context.exclusions, fileManager: FileManager())
    }

    static func rotatedLogs(_ context: ScanContext) -> ModuleEstimate {
        let stats = rotatedLogFiles(context)
        let files = stats.urls.filter(isRotatedLog)
        let bytes = files.reduce(Int64(0)) { total, url in
            total + Int64((try? url.resourceValues(forKeys: [.fileSizeKey]))?.fileSize ?? 0)
        }
        return ModuleEstimate(id: "rotated-logs", title: "Archived logs (30+ days)",
            status: stats.accessDenied ? .unavailable(PermissionService.accessHelp) : .ready,
            fileCount: files.count, byteCount: bytes, samplePaths: Array(files.prefix(20).map(\.path)))
    }

    static func cleanRotatedLogs(_ context: ScanContext, cancel: @escaping () -> Bool) -> ModuleCleanup {
        let files = rotatedLogFiles(context).urls.filter(isRotatedLog)
        let outcome = SafeDeletionService().deleteListedFiles(files, under: context.locations.url("Library/Logs"),
            context: context, minimumAge: context.now.addingTimeInterval(-diagnosticRetention), shouldCancel: cancel)
        return ModuleCleanup(deletedCount: outcome.deletedCount, deletedBytes: outcome.deletedBytes,
            skippedCount: outcome.skippedCount, errors: outcome.errors, status: .ready)
    }

    static func thumbnails() -> ModuleEstimate {
        ModuleEstimate(
            id: "thumbnails",
            title: "Thumbnails",
            status: .unsupported("Quick Look thumbnail caches are maintained by macOS. CleanSlate does not delete undocumented system cache databases."),
            fileCount: 0,
            byteCount: 0,
            samplePaths: []
        )
    }

    static func cleanTemporary(_ context: ScanContext, cancel: @escaping () -> Bool) -> ModuleCleanup {
        clean(idRoot: context.locations.temporaryDirectory, context: context, minimumAge: context.now.addingTimeInterval(-temporaryFileAge), allowXcode: false, allowTrash: false, includeHidden: true, cancel: cancel)
    }

    static func cleanHelpCaches(_ context: ScanContext, cancel: @escaping () -> Bool) -> ModuleCleanup {
        var total = ModuleCleanup(deletedCount: 0, deletedBytes: 0, skippedCount: 0, errors: [], status: .ready)
        for relative in ProtectedPaths.systemCacheRelativeRoots {
            if cancel() { total.errors.append("Cancelled."); break }
            let result = clean(idRoot: context.locations.url(relative), context: context,
                minimumAge: context.now.addingTimeInterval(-looseCacheAge), allowXcode: false, allowTrash: false, cancel: cancel)
            total.deletedCount += result.deletedCount
            total.deletedBytes += result.deletedBytes
            total.skippedCount += result.skippedCount
            total.errors.append(contentsOf: result.errors)
            if result.status != .ready { total.status = result.status }
        }
        return total
    }

    static func cleanLooseCaches(_ context: ScanContext, cancel: @escaping () -> Bool) -> ModuleCleanup {
        cleanLoose(context, cancel: cancel)
    }

    static func cleanDiagnostics(_ context: ScanContext, cancel: @escaping () -> Bool) -> ModuleCleanup {
        let root = context.locations.url("Library/Logs/DiagnosticReports")
        let allowed: Set<String> = ["ips", "crash", "spin", "diag", "hang"]
        let cutoff = context.now.addingTimeInterval(-diagnosticRetention)
        let fm = FileManager()
        let urls = ((try? fm.contentsOfDirectory(at: root, includingPropertiesForKeys: [.contentModificationDateKey, .isRegularFileKey], options: [.skipsHiddenFiles])) ?? []).filter { url in
            guard allowed.contains(url.pathExtension.lowercased()) else { return false }
            let values = try? url.resourceValues(forKeys: [.contentModificationDateKey, .isRegularFileKey])
            guard values?.isRegularFile == true, let modified = values?.contentModificationDate else { return false }
            return modified <= cutoff && !context.isExcluded(url.path)
        }
        let outcome = SafeDeletionService().deleteListedFiles(urls, under: root, context: context, minimumAge: cutoff, shouldCancel: cancel)
        return ModuleCleanup(deletedCount: outcome.deletedCount, deletedBytes: outcome.deletedBytes, skippedCount: outcome.skippedCount, errors: outcome.errors, status: .ready)
    }

    private static func estimate(id: String, title: String, root: URL, context: ScanContext, minimumAge: Date?, includeHidden: Bool = false) -> ModuleEstimate {
        if DirectoryWalker.isUnreadable(root) {
            return ModuleEstimate(id: id, title: title, status: .unavailable(PermissionService.accessHelp), fileCount: 0, byteCount: 0, samplePaths: [])
        }
        let stats = DirectoryWalker.collect(root: root, minimumAge: minimumAge, exclusions: context.exclusions, fileManager: FileManager(), includeHidden: includeHidden)
        return ModuleEstimate(id: id, title: title, status: stats.accessDenied ? .unavailable(PermissionService.accessHelp) : .ready, fileCount: stats.fileCount, byteCount: stats.byteCount, samplePaths: stats.samplePaths)
    }

    private static func clean(idRoot: URL, context: ScanContext, minimumAge: Date?, allowXcode: Bool, allowTrash: Bool, includeHidden: Bool = false, cancel: @escaping () -> Bool) -> ModuleCleanup {
        var service = SafeDeletionService(allowXcodeDerivedData: allowXcode, allowTrash: allowTrash, includeHidden: includeHidden)
        let outcome = service.deleteEligibleFiles(under: idRoot, context: context, minimumAge: minimumAge, shouldCancel: cancel)
        return ModuleCleanup(deletedCount: outcome.deletedCount, deletedBytes: outcome.deletedBytes, skippedCount: outcome.skippedCount, errors: outcome.errors, status: outcome.accessDenied ? .unavailable(PermissionService.accessHelp) : .ready)
    }

    private static func cleanLoose(_ context: ScanContext, cancel: @escaping () -> Bool) -> ModuleCleanup {
        let root = context.locations.url("Library/Caches")
        let cutoff = context.now.addingTimeInterval(-looseCacheAge)
        var cleanup = ModuleCleanup(deletedCount: 0, deletedBytes: 0, skippedCount: 0, errors: [], status: .ready)
        let fm = FileManager()
        if DirectoryWalker.isUnreadable(root) {
            cleanup.status = .unavailable(PermissionService.accessHelp)
            return cleanup
        }
        guard let contents = try? fm.contentsOfDirectory(at: root, includingPropertiesForKeys: [.isRegularFileKey, .contentModificationDateKey, .isSymbolicLinkKey, .fileSizeKey], options: [.skipsHiddenFiles]) else {
            return cleanup
        }
        for url in contents {
            if cancel() { cleanup.errors.append("Cancelled."); break }
            let values = try? url.resourceValues(forKeys: [.isRegularFileKey, .contentModificationDateKey, .isSymbolicLinkKey, .fileSizeKey])
            guard values?.isSymbolicLink != true, values?.isRegularFile == true else { continue }
            guard let modified = values?.contentModificationDate, modified <= cutoff else { continue }
            if context.isExcluded(url.path) || ProtectedPaths.isReserved(url) { cleanup.skippedCount += 1; continue }
            if context.dryRun {
                cleanup.deletedCount += 1
                cleanup.deletedBytes += Int64(values?.fileSize ?? 0)
                continue
            }
            let outcome = deleteLooseFile(url, context: context)
            switch outcome {
            case .deleted(let bytes):
                cleanup.deletedCount += 1
                cleanup.deletedBytes += bytes
            case .skipped:
                cleanup.skippedCount += 1
            case .failed(let message):
                cleanup.errors.append(message)
            }
        }
        return cleanup
    }

    private enum LooseResult {
        case deleted(Int64)
        case skipped
        case failed(String)
    }

    /// Deletes one regular file whose parent is exactly `~/Library/Caches`. Subfolders are never entered.
    private static func deleteLooseFile(_ url: URL, context: ScanContext) -> LooseResult {
        let parent = url.deletingLastPathComponent().standardizedFileURL.resolvingSymlinksInPath().path
        let caches = context.locations.url("Library/Caches").standardizedFileURL.resolvingSymlinksInPath().path
        guard parent == caches else { return .skipped }
        guard !ProtectedPaths.isReserved(url) else { return .skipped }
        var info = Darwin.stat()
        guard lstat(url.path, &info) == 0, (info.st_mode & S_IFMT) == S_IFREG, info.st_uid == getuid() else { return .skipped }
        let descriptor = open(url.path, O_RDONLY | O_NOFOLLOW | O_CLOEXEC)
        guard descriptor >= 0 else { return .failed("Could not open \(url.lastPathComponent).") }
        defer { close(descriptor) }
        var opened = Darwin.stat()
        guard fstat(descriptor, &opened) == 0, opened.st_dev == info.st_dev, opened.st_ino == info.st_ino else {
            return .failed("Skipped \(url.lastPathComponent): it changed before it could be removed.")
        }
        guard unlink(url.path) == 0 else { return .failed("Could not remove \(url.lastPathComponent).") }
        return .deleted(Int64(opened.st_size))
    }
}

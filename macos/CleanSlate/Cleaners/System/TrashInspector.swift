import Foundation

enum TrashInspector {
    static func estimate(_ context: ScanContext) -> ModuleEstimate {
        let trash = context.locations.home.appendingPathComponent(".Trash")
        let stats = DirectoryWalker.collect(root: trash, minimumAge: nil, exclusions: context.exclusions, fileManager: FileManager(), includeHidden: true)
        return ModuleEstimate(id: "trash", title: "Trash", status: stats.accessDenied ? .unavailable(PermissionService.trashAccessHelp) : .ready, fileCount: stats.fileCount, byteCount: stats.byteCount, samplePaths: stats.samplePaths)
    }

    /// Irreversible. The manual Clean action explicitly includes Trash; never runs at login.
    static func empty(_ context: ScanContext, cancel: @escaping () -> Bool) -> ModuleCleanup {
        let trash = context.locations.home.appendingPathComponent(".Trash")
        let outcome = SafeDeletionService(allowTrash: true, includeHidden: true).deleteEligibleFiles(under: trash, context: context, minimumAge: nil, shouldCancel: cancel)
        return ModuleCleanup(deletedCount: outcome.deletedCount, deletedBytes: outcome.deletedBytes, skippedCount: outcome.skippedCount, errors: outcome.errors, status: outcome.accessDenied ? .unavailable(PermissionService.trashAccessHelp) : .ready)
    }
}

enum DownloadsInspector {
    /// Lists large downloads. There is no delete path: Downloads is not an approved root.
    static func largeFiles(home: URL, largerThan bytes: Int64 = 100 * 1024 * 1024) -> [String] {
        let downloads = home.appendingPathComponent("Downloads")
        let fm = FileManager()
        let urls = (try? fm.contentsOfDirectory(at: downloads, includingPropertiesForKeys: [.fileSizeKey, .isRegularFileKey], options: [.skipsHiddenFiles])) ?? []
        return urls.compactMap { url in
            let values = try? url.resourceValues(forKeys: [.fileSizeKey, .isRegularFileKey])
            guard values?.isRegularFile == true, let size = values?.fileSize, Int64(size) >= bytes else { return nil }
            return "\(url.lastPathComponent) — \(ByteFormat.string(from: Int64(size)))"
        }
    }
}

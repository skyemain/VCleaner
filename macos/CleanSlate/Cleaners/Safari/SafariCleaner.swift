import Foundation

enum SafariCleaner {
    static let unavailableMessage = "Safari cache unavailable: macOS access restrictions."

    static func estimate(_ context: ScanContext) -> ModuleEstimate {
        let root = context.locations.url("Library/Caches/com.apple.Safari/fsCachedData")
        let fm = FileManager()
        var isDirectory: ObjCBool = false
        let exists = fm.fileExists(atPath: root.path, isDirectory: &isDirectory)
        if !exists {
            return ModuleEstimate(id: "safari", title: "SAFARI", status: .ready, fileCount: 0, byteCount: 0, samplePaths: [])
        }
        if DirectoryWalker.isUnreadable(root) {
            return ModuleEstimate(id: "safari", title: "SAFARI", status: .unavailable(unavailableMessage), fileCount: 0, byteCount: 0, samplePaths: [])
        }
        let stats = DirectoryWalker.collect(root: root, minimumAge: nil, exclusions: context.exclusions, fileManager: fm)
        return ModuleEstimate(id: "safari", title: "SAFARI", status: .ready, fileCount: stats.fileCount, byteCount: stats.byteCount, samplePaths: stats.samplePaths)
    }

    static func clean(_ context: ScanContext, cancel: @escaping () -> Bool) -> ModuleCleanup {
        let root = context.locations.url("Library/Caches/com.apple.Safari/fsCachedData")
        if DirectoryWalker.isUnreadable(root) {
            return ModuleCleanup(deletedCount: 0, deletedBytes: 0, skippedCount: 0, errors: [], status: .unavailable(unavailableMessage))
        }
        let outcome = SafeDeletionService().deleteEligibleFiles(under: root, context: context, minimumAge: nil, shouldCancel: cancel)
        return ModuleCleanup(deletedCount: outcome.deletedCount, deletedBytes: outcome.deletedBytes, skippedCount: outcome.skippedCount, errors: outcome.errors, status: .ready)
    }
}

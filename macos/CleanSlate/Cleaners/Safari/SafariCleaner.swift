import Foundation

enum SafariCleaner {
    static let bundleIdentifier = "com.apple.Safari"
    static let unavailableMessage = "Safari cache access was denied by macOS. Click Safari on the home screen for the access guide: add the installed CleanSlate app to Full Disk Access, quit and reopen it, then quit Safari and retry."

    static func estimate(_ context: ScanContext) -> ModuleEstimate {
        let totals = measure(context)
        if totals.blocked {
            return ModuleEstimate(id: "safari", title: "SAFARI", status: .unavailable(unavailableMessage), fileCount: 0, byteCount: 0, samplePaths: [])
        }
        return ModuleEstimate(id: "safari", title: "SAFARI", status: .ready, fileCount: totals.fileCount, byteCount: totals.byteCount, samplePaths: totals.samplePaths)
    }

    static func clean(_ context: ScanContext, cancel: @escaping () -> Bool) -> ModuleCleanup {
        var cleanup = ModuleCleanup(deletedCount: 0, deletedBytes: 0, skippedCount: 0, errors: [], status: .ready)
        var blocked = false
        for root in cacheRoots(context) {
            if cancel() {
                cleanup.errors.append("Cancelled.")
                break
            }
            if DirectoryWalker.isUnreadable(root) {
                blocked = true
                continue
            }
            let outcome = SafeDeletionService().deleteEligibleFiles(under: root, context: context, minimumAge: nil, shouldCancel: cancel)
            cleanup.deletedCount += outcome.deletedCount
            cleanup.deletedBytes += outcome.deletedBytes
            cleanup.skippedCount += outcome.skippedCount
            cleanup.errors.append(contentsOf: outcome.errors.prefix(20))
            blocked = blocked || outcome.accessDenied
        }
        if blocked {
            cleanup.status = .unavailable(unavailableMessage)
        } else if cleanup.deletedCount == 0, !cleanup.errors.isEmpty {
            cleanup.status = .skipped("Safari’s cache could not be removed. Quit Safari and try again.")
        }
        return cleanup
    }

    private struct Totals {
        var fileCount = 0
        var byteCount: Int64 = 0
        var samplePaths: [String] = []
        var blocked = false
    }

    private static func measure(_ context: ScanContext) -> Totals {
        var totals = Totals()
        for root in cacheRoots(context) {
            if DirectoryWalker.isUnreadable(root) {
                totals.blocked = true
                continue
            }
            let stats = DirectoryWalker.collect(root: root, minimumAge: nil, exclusions: context.exclusions, fileManager: FileManager())
            totals.blocked = totals.blocked || stats.accessDenied
            totals.fileCount += stats.fileCount
            totals.byteCount += stats.byteCount
            for path in stats.samplePaths where totals.samplePaths.count < 20 {
                totals.samplePaths.append(path)
            }
        }
        return totals
    }

    /// Keep explicit candidates even when TCC prevents fileExists from seeing them.
    private static func cacheRoots(_ context: ScanContext) -> [URL] {
        ProtectedPaths.safariCacheRelativeRoots.map { context.locations.url($0) }
    }
}

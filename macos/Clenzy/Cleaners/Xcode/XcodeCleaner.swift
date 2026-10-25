import Foundation

enum XcodeCleaner {
    /// DerivedData project folders only; archives and simulators are never deleted.
    static func estimate(_ context: ScanContext) -> ModuleEstimate {
        let derived = context.locations.url("Library/Developer/Xcode/DerivedData")
        let stats = DirectoryWalker.collect(root: derived, minimumAge: nil, exclusions: context.exclusions, fileManager: FileManager())
        return ModuleEstimate(id: "xcode", title: "Xcode DerivedData", status: .ready, fileCount: stats.fileCount, byteCount: stats.byteCount, samplePaths: stats.samplePaths)
    }

    static func clean(_ context: ScanContext, cancel: @escaping () -> Bool) -> ModuleCleanup {
        let derived = context.locations.url("Library/Developer/Xcode/DerivedData")
        let fm = FileManager()
        guard let projects = try? fm.contentsOfDirectory(at: derived, includingPropertiesForKeys: [.isDirectoryKey], options: [.skipsHiddenFiles]) else {
            return ModuleCleanup(deletedCount: 0, deletedBytes: 0, skippedCount: 0, errors: [], status: .ready)
        }
        var cleanup = ModuleCleanup(deletedCount: 0, deletedBytes: 0, skippedCount: 0, errors: [], status: .ready)
        var service = SafeDeletionService(allowXcodeDerivedData: true)
        for project in projects {
            if cancel() { cleanup.errors.append("Cancelled."); break }
            var isDirectory: ObjCBool = false
            guard fm.fileExists(atPath: project.path, isDirectory: &isDirectory), isDirectory.boolValue else { continue }
            let outcome = service.deleteEligibleFiles(under: project, context: context, minimumAge: nil, shouldCancel: cancel)
            cleanup.deletedCount += outcome.deletedCount
            cleanup.deletedBytes += outcome.deletedBytes
            cleanup.skippedCount += outcome.skippedCount
            cleanup.errors.append(contentsOf: outcome.errors.prefix(10))
        }
        return cleanup
    }

}

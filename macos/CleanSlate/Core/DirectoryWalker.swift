import Darwin
import Foundation

struct WalkStats: Equatable, Sendable {
    var fileCount: Int = 0
    var byteCount: Int64 = 0
    var skippedCount: Int = 0
    var samplePaths: [String] = []
    var urls: [URL] = []
}

enum DirectoryWalker {
    private static let sampleLimit = 20

    static func collect(root: URL, minimumAge: Date?, exclusions: [String], fileManager: FileManager) -> WalkStats {
        files(root: root, minimumAge: minimumAge, exclusions: exclusions, fileManager: fileManager, keepURLs: false)
    }

    static func files(root: URL, minimumAge: Date?, exclusions: [String], fileManager: FileManager, keepURLs: Bool = true) -> WalkStats {
        var stats = WalkStats()
        var isDirectory: ObjCBool = false
        guard fileManager.fileExists(atPath: root.path, isDirectory: &isDirectory), isDirectory.boolValue else {
            return stats
        }
        let keys: [URLResourceKey] = [.isRegularFileKey, .isSymbolicLinkKey, .fileSizeKey, .contentModificationDateKey]
        guard let enumerator = fileManager.enumerator(
            at: root,
            includingPropertiesForKeys: keys,
            options: [.skipsHiddenFiles],
            errorHandler: { _, _ in
                true
            }
        ) else {
            stats.skippedCount += 1
            return stats
        }
        let contextExclusions = exclusions
        for case let url as URL in enumerator {
            let values = try? url.resourceValues(forKeys: Set(keys))
            if values?.isSymbolicLink == true {
                enumerator.skipDescendants()
                stats.skippedCount += 1
                continue
            }
            guard values?.isRegularFile == true else { continue }
            if ProtectedPaths.isReserved(url) {
                stats.skippedCount += 1
                continue
            }
            if let minimumAge, let modified = values?.contentModificationDate, modified > minimumAge {
                stats.skippedCount += 1
                continue
            }
            let path = url.path
            if contextExclusions.contains(where: { path == $0 || path.hasPrefix($0.hasSuffix("/") ? $0 : $0 + "/") }) {
                stats.skippedCount += 1
                continue
            }
            stats.fileCount += 1
            stats.byteCount += Int64(values?.fileSize ?? 0)
            if stats.samplePaths.count < sampleLimit {
                stats.samplePaths.append(path)
            }
            if keepURLs {
                stats.urls.append(url)
            }
        }
        return stats
    }

    static func isUnreadable(_ url: URL) -> Bool {
        guard access(url.path, R_OK) != 0 else { return false }
        // TCC often returns EPERM, and a mode-0 directory returns EACCES. A missing folder is ENOENT.
        return errno == EACCES || errno == EPERM
    }
}

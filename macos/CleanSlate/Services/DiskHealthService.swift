import Darwin
import Foundation

struct DiskSnapshot: Equatable, Sendable {
    var totalBytes: Int64
    var availableBytes: Int64
    var usedBytes: Int64
    var fileSystem: String
    var volumeName: String
}

enum DiskHealthService {
    static func snapshot(at url: URL = URL(fileURLWithPath: "/")) -> DiskSnapshot? {
        let fm = FileManager()
        guard let values = try? url.resourceValues(forKeys: [
            .volumeTotalCapacityKey, .volumeAvailableCapacityKey, .volumeAvailableCapacityForImportantUsageKey, .volumeNameKey
        ]), let total = values.volumeTotalCapacity else {
            return nil
        }
        let available = values.volumeAvailableCapacityForImportantUsage ?? Int64(values.volumeAvailableCapacity ?? 0)
        let total64 = Int64(total)
        var system = statfs()
        let kind: String
        if statfs(url.path, &system) == 0 {
            kind = withUnsafeBytes(of: system.f_fstypename) { raw in
                guard let base = raw.bindMemory(to: CChar.self).baseAddress else { return "Unknown" }
                return String(cString: base)
            }
        } else {
            kind = "Unknown"
        }
        return DiskSnapshot(
            totalBytes: total64,
            availableBytes: available,
            usedBytes: max(0, total64 - available),
            fileSystem: kind,
            volumeName: values.volumeName ?? url.path
        )
    }
}

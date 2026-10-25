import AppKit
import Darwin
import Foundation

struct MemoryAppIdentity: Sendable {
    var path: String
    var name: String
    var pid: pid_t
}

struct AppMemoryUsage: Identifiable, Equatable, Sendable {
    var id: String
    var name: String
    var pid: pid_t
    var bytes: UInt64
    var percent: Double

    var percentText: String { String(format: "%.1f%%", percent) }
}

enum AppMemoryService {
    @MainActor static func identities() -> [MemoryAppIdentity] {
        NSWorkspace.shared.runningApplications.compactMap { app in
            guard app.activationPolicy == .regular, !app.isTerminated,
                  let url = app.bundleURL else { return nil }
            return MemoryAppIdentity(path: url.standardizedFileURL.resolvingSymlinksInPath().path,
                name: app.localizedName ?? url.deletingPathExtension().lastPathComponent,
                pid: app.processIdentifier)
        }
    }

    /// Group app helpers with their owning bundle; unreadable and other-user processes are omitted.
    static func sample(apps: [MemoryAppIdentity], totalBytes: UInt64, limit: Int = 5) -> [AppMemoryUsage] {
        let capacity = max(Int(proc_listallpids(nil, 0)) + 128, 512)
        var pids = [pid_t](repeating: 0, count: capacity)
        let count = pids.withUnsafeMutableBytes { proc_listallpids($0.baseAddress, Int32($0.count)) }
        guard count > 0 else { return [] }
        var totals: [String: UInt64] = [:]
        for pid in pids.prefix(min(Int(count), capacity)) where pid > 0 {
            var bsd = proc_bsdinfo()
            let size = Int32(MemoryLayout<proc_bsdinfo>.size)
            guard proc_pidinfo(pid, PROC_PIDTBSDINFO, 0, &bsd, size) == size,
                  bsd.pbi_uid == getuid() else { continue }
            var path = [CChar](repeating: 0, count: 4 * Int(MAXPATHLEN))
            let length = path.withUnsafeMutableBytes { proc_pidpath(pid, $0.baseAddress, UInt32($0.count)) }
            guard length > 0 else { continue }
            let executable = URL(fileURLWithPath: String(cString: path)).standardizedFileURL.resolvingSymlinksInPath().path
            guard let app = apps.first(where: { executable.hasPrefix($0.path + "/") }),
                  let bytes = footprint(pid) else { continue }
            totals[app.path, default: 0] += bytes
        }
        return ranked(apps: apps, bytesByPath: totals, totalBytes: totalBytes, limit: limit)
    }

    static func ranked(apps: [MemoryAppIdentity], bytesByPath: [String: UInt64], totalBytes: UInt64, limit: Int = 5) -> [AppMemoryUsage] {
        var seen = Set<String>()
        return apps.compactMap { app -> AppMemoryUsage? in
            guard seen.insert(app.path).inserted, let bytes = bytesByPath[app.path], bytes > 0 else { return nil }
            return AppMemoryUsage(id: app.path, name: app.name, pid: app.pid, bytes: bytes,
                percent: totalBytes > 0 ? Double(bytes) / Double(totalBytes) * 100 : 0)
        }.sorted { $0.bytes == $1.bytes ? $0.id < $1.id : $0.bytes > $1.bytes }.prefix(max(0, limit)).map { $0 }
    }

    /// Running regular applications appear in the Dock; Finder and Clenzy stay open.
    static func cleanupCandidates(_ ranked: [AppMemoryUsage], ownPID: pid_t) -> [AppMemoryUsage] {
        Array(ranked.filter {
            $0.pid != ownPID && $0.id != "/System/Library/CoreServices/Finder.app"
        }.sorted {
            $0.bytes == $1.bytes ? $0.id < $1.id : $0.bytes < $1.bytes
        }.prefix(5))
    }

    static func footprint(_ pid: pid_t) -> UInt64? {
        var info = rusage_info_v2()
        let result = withUnsafeMutablePointer(to: &info) { pointer in
            pointer.withMemoryRebound(to: rusage_info_t?.self, capacity: 1) {
                proc_pid_rusage(pid, RUSAGE_INFO_V2, $0)
            }
        }
        return result == 0 ? info.ri_phys_footprint : nil
    }
}

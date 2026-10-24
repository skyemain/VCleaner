import Darwin
import Foundation

enum PermissionService {
    struct Status: Equatable, Sendable {
        var cachesReadable: Bool
        var fullDiskAccessNote: String
    }

    static func canRead(_ url: URL) -> Bool {
        access(url.path, R_OK) == 0
    }

    static func status(home: URL) -> Status {
        Status(
            cachesReadable: canRead(home.appendingPathComponent("Library/Caches")),
            fullDiskAccessNote: "Full Disk Access is optional. Turn it on yourself in System Settings > Privacy & Security if a protected folder stays unreadable. CleanSlate never changes system security settings."
        )
    }
}

import AppKit
import Darwin
import Foundation

enum PermissionService {
    enum Access: String, Equatable, Sendable {
        case available = "Accessible"
        case missing = "Not present"
        case blocked = "Access denied"
        case unknown = "Could not check"
    }

    struct Check: Identifiable, Equatable, Sendable {
        var id: String
        var title: String
        var path: String
        var access: Access
    }

    struct Status: Equatable, Sendable {
        var cachesReadable: Bool
        var fullDiskAccessNote: String
    }

    static let accessHelp = "macOS denied access to this folder. Full Disk Access may allow protected caches; files owned by another account, system-protected files and files in use can still be skipped. Click for the access guide."

    static func canRead(_ url: URL) -> Bool { access(url.path, R_OK) == 0 }

    /// Probe a directory by listing names, without reading or modifying its files.
    static func check(_ root: URL) -> Access {
        do {
            _ = try FileManager().contentsOfDirectory(atPath: root.path)
            return access(root.path, R_OK | W_OK | X_OK) == 0 ? .available : .blocked
        } catch {
            let error = error as NSError
            if error.domain == NSCocoaErrorDomain {
                if error.code == NSFileNoSuchFileError || error.code == NSFileReadNoSuchFileError { return .missing }
                if error.code == NSFileReadNoPermissionError { return .blocked }
            }
            if error.domain == NSPOSIXErrorDomain {
                if error.code == Int(ENOENT) { return .missing }
                if [Int(EACCES), Int(EPERM)].contains(error.code) { return .blocked }
            }
            return .unknown
        }
    }

    static func checks(locations: Locations) -> [Check] {
        let roots: [(String, URL)] = [("Temporary files", locations.temporaryDirectory),
            ("User caches", locations.url("Library/Caches")),
            ("Diagnostic reports", locations.url("Library/Logs/DiagnosticReports"))]
            + ProtectedPaths.systemCacheRelativeRoots.map { ("System cache", locations.url($0)) }
            + ProtectedPaths.safariCacheRelativeRoots.map { ("Safari / WebKit cache", locations.url($0)) }
        return roots.map { title, root in
            Check(id: root.path, title: title, path: root.path.replacingOccurrences(of: locations.home.path, with: "~"), access: check(root))
        }
    }

    @MainActor static func openPrivacySettings() {
        guard let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles") else { return }
        if !NSWorkspace.shared.open(url), let fallback = URL(string: "x-apple.systempreferences:com.apple.preference.security") {
            NSWorkspace.shared.open(fallback)
        }
    }

    static func status(home: URL) -> Status {
        Status(cachesReadable: canRead(home.appendingPathComponent("Library/Caches")),
            fullDiskAccessNote: "For blocked Safari caches, open the access guide: install in Applications, grant CleanSlate Full Disk Access, then quit and reopen CleanSlate. This is a per-folder access check, not proof that Full Disk Access is enabled. Some system files remain protected.")
    }
}

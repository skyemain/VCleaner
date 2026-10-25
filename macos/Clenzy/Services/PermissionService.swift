import AppKit
import Darwin
import Foundation

enum PermissionService {
    enum Access: String, Equatable, Sendable {
        case available = "Readable"
        case missing = "Not present"
        case blocked = "Access denied"
        case unknown = "Could not check"
    }

    struct Check: Identifiable, Equatable, Sendable {
        var id: String
        var title: String
        var path: String
        var access: Access
        var detail: String = ""
    }

    struct Status: Equatable, Sendable {
        var cachesReadable: Bool
        var fullDiskAccessNote: String
    }

    static let trashAccessHelp = "Trash needs access. Open Settings → Cleaning Access → Open Full Disk Access. Add the Clenzy copy in Applications, enable it, then quit and reopen Clenzy. After an app update, you may need to remove and re-add that copy. Locked or other-user files remain untouched."

    static let accessHelp = "macOS denied access to this folder. Full Disk Access may allow protected caches; files owned by another account, system-protected files and files in use can still be skipped. Click for the access guide."

    struct Probe: Equatable, Sendable {
        var access: Access
        var detail: String
    }

    static func canRead(_ url: URL) -> Bool { probe(url).access == .available }
    static func check(_ root: URL) -> Access { probe(root).access }

    /// Test the operation we actually need, rather than rejecting the folder using access(2).
    /// This checks directory enumeration only; each deletion still checks ownership, locks and permissions.
    static func probe(_ root: URL) -> Probe {
        do {
            _ = try FileManager().contentsOfDirectory(atPath: root.path)
            return Probe(access: .available, detail: "Folder listing succeeded. File deletion is checked during Clean.")
        } catch { return classify(error as NSError) }
    }

    static func classify(_ error: NSError) -> Probe {
        if let underlying = error.userInfo[NSUnderlyingErrorKey] as? NSError {
            let cause = classify(underlying)
            if cause.access != .unknown { return cause }
        }
        if error.domain == NSPOSIXErrorDomain {
            switch error.code {
            case Int(ENOENT): return Probe(access: .missing, detail: "Folder does not exist (ENOENT).")
            case Int(EPERM): return Probe(access: .blocked, detail: "macOS privacy or protection denied the operation (EPERM / 1).")
            case Int(EACCES): return Probe(access: .blocked, detail: "File permissions or an access-control rule denied the operation (EACCES / 13).")
            default: break
            }
        }
        if error.domain == NSCocoaErrorDomain {
            if [NSFileNoSuchFileError, NSFileReadNoSuchFileError].contains(error.code) {
                return Probe(access: .missing, detail: "Folder does not exist.")
            }
            if [NSFileReadNoPermissionError, NSFileWriteNoPermissionError].contains(error.code) {
                return Probe(access: .blocked, detail: "macOS denied the operation (Cocoa \(error.code)).")
            }
        }
        return Probe(access: .unknown, detail: "\(error.domain) / \(error.code): \(error.localizedDescription)")
    }

    static func roots(locations: Locations) -> [(String, URL)] {
        let roots: [(String, URL)] = [("Temporary files", locations.temporaryDirectory),
            ("User caches", locations.url("Library/Caches")),
            ("Trash", locations.url(".Trash")),
            ("Archived logs", locations.url("Library/Logs")),
            ("Diagnostic reports", locations.url("Library/Logs/DiagnosticReports"))]
            + ProtectedPaths.systemCacheRelativeRoots.map { ("System cache", locations.url($0)) }
            + ProtectedPaths.safariCacheRelativeRoots.map { ("Safari / WebKit cache", locations.url($0)) }
        return roots
    }

    static func checks(locations: Locations) -> [Check] {
        roots(locations: locations).map { title, root in
            let result = probe(root)
            return Check(id: root.path, title: title, path: root.path.replacingOccurrences(of: locations.home.path, with: "~"),
                access: result.access, detail: result.detail)
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
            fullDiskAccessNote: "For blocked Safari caches, open the access guide: install in Applications, grant Clenzy Full Disk Access, then quit and reopen Clenzy. This is a per-folder access check, not proof that Full Disk Access is enabled. Some system files remain protected.")
    }
}

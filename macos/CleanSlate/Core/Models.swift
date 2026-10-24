import Foundation

/// Risk shown in the preview. Only `.low` categories may run during login cleaning.
enum RiskLevel: String, Codable, Sendable, Equatable {
    case low
    case medium
    case high
}

enum CleanMode: String, Codable, Sendable, Equatable {
    /// A person pressed Clean.
    case manual
    /// Started because the user signed in. Never quits apps and never touches Trash, Xcode, or leftovers.
    case login
}

enum ModuleStatus: Equatable, Sendable {
    case ready
    case unsupported(String)
    case unavailable(String)
    case skipped(String)
}

struct ModuleEstimate: Equatable, Sendable, Identifiable {
    let id: String
    let title: String
    let status: ModuleStatus
    let fileCount: Int
    let byteCount: Int64
    /// A short sample for the details view. The cleaner re-walks the folder at delete time, so this list is not the deletion set.
    let samplePaths: [String]

    var isActionable: Bool {
        if case .ready = status { return fileCount > 0 || byteCount > 0 }
        return false
    }
}

struct ModuleCleanup: Equatable, Sendable {
    var deletedCount: Int
    var deletedBytes: Int64
    var skippedCount: Int
    var errors: [String]
    var status: ModuleStatus
}

struct SessionRecord: Codable, Equatable, Sendable, Identifiable {
    let id: UUID
    let startedAt: Date
    let finishedAt: Date
    let mode: String
    let dryRun: Bool
    let modules: [String]
    let discoveredCount: Int
    let discoveredBytes: Int64
    let deletedCount: Int
    let deletedBytes: Int64
    let skippedCount: Int
    let errors: [String]
    let duration: TimeInterval
}

struct ScanContext: Sendable {
    var locations: Locations
    var now: Date
    var exclusions: [String]
    var dryRun: Bool
    var mode: CleanMode

    func isExcluded(_ path: String) -> Bool {
        let standard = URL(fileURLWithPath: path).standardizedFileURL.path
        return exclusions.contains { exclusion in
            let root = URL(fileURLWithPath: exclusion).standardizedFileURL.path
            return standard == root || standard.hasPrefix(root + "/")
        }
    }
}

struct Locations: Sendable, Equatable {
    var home: URL
    var temporaryDirectory: URL

    static func currentUser() -> Locations {
        Locations(
            home: FileManager.default.homeDirectoryForCurrentUser,
            temporaryDirectory: FileManager.default.temporaryDirectory
        )
    }

    func url(_ relativePath: String) -> URL {
        relativePath.split(separator: "/").reduce(home) { partial, component in
            partial.appendingPathComponent(String(component))
        }
    }
}

struct CleanRequest: Sendable {
    var mode: CleanMode
    var dryRun: Bool
    var categories: EnabledCategories
    var exclusions: [String]
    /// Manual runs may ask Chrome or Edge to quit. Login runs must leave this false.
    var allowBrowserQuit: Bool
    /// Set only after the person confirms an irreversible Trash empty.
    var emptyTrashConfirmed: Bool
}

struct EnabledCategories: Codable, Equatable, Sendable {
    var temporaryFiles: Bool = true
    var userCaches: Bool = true
    var diagnosticReports: Bool = true
    var chrome: Bool = true
    var edge: Bool = true
    var safari: Bool = true
    var emptyTrash: Bool = false
    var xcodeDerivedData: Bool = false
    var loginItemDiagnostics: Bool = true
    var leftoverScan: Bool = false
    var showMemory: Bool = true
    var showDisk: Bool = true

    /// Login cleaning is a subset: low risk, no Trash, no Xcode, no leftover removal.
    func forLogin() -> EnabledCategories {
        var copy = self
        copy.emptyTrash = false
        copy.xcodeDerivedData = false
        copy.leftoverScan = false
        return copy
    }
}

struct CleanRunResult: Equatable, Sendable {
    var modules: [ModuleCleanupReport]
    var record: SessionRecord

    var deletedBytes: Int64 { record.deletedBytes }
    var deletedCount: Int { record.deletedCount }
}

struct ModuleCleanupReport: Equatable, Sendable, Identifiable {
    var id: String { estimate.id }
    var estimate: ModuleEstimate
    var cleanup: ModuleCleanup
}

enum CleanEngineError: Error, Equatable {
    case alreadyRunning
}

enum ByteFormat {
    static func string(from bytes: Int64) -> String {
        let value = Double(max(0, bytes))
        let units = ["B", "KB", "MB", "GB", "TB"]
        var amount = value
        var unit = 0
        while amount >= 1024, unit < units.count - 1 {
            amount /= 1024
            unit += 1
        }
        if unit == 0 {
            return "\(Int(amount)) \(units[unit])"
        }
        return String(format: amount >= 10 ? "%.0f %@" : "%.1f %@", amount, units[unit])
    }
}

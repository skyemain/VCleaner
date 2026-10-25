import Foundation

enum AppTheme: String, Codable, Sendable, CaseIterable, Identifiable {
    case system
    case dark
    case light
    var id: String { rawValue }
}

struct CleaningPreferences: Codable, Equatable, Sendable {
    var startAtLogin: Bool = false
    var cleanAtLogin: Bool = false
    var showLoginCountdown: Bool = true
    var showCompletionResult: Bool = true
    var closeAfterAutoClean: Bool = true
    var theme: AppTheme = .dark
    var showMenuBarIcon: Bool = false

    var cleanTemporaryFiles: Bool = true
    var cleanUserCaches: Bool = true
    var cleanDiagnosticReports: Bool = true
    var cleanChrome: Bool = true
    /// Kept so older settings still decode. The Mac app does not offer Edge cleaning.
    var cleanEdge: Bool = false
    var cleanSafari: Bool = true
    var emptyTrash: Bool = false
    var cleanXcodeDerivedData: Bool = false
    /// Kept so the setting can be shown. There is no safe macOS API for this, so the cleaner never acts on it.
    var privacyHistoryCleaning: Bool = false
    /// Legacy preference retained for decoding; no leftover scan is performed.
    var appLeftoverScan: Bool = false

    var reviewBeforeCleaning: Bool = false
    var dryRun: Bool = false
    var excludedPaths: [String] = []

    var loginItemDiagnostics: Bool = true
    var showMemory: Bool = true
    var showDisk: Bool = true

    var categories: EnabledCategories {
        EnabledCategories(
            temporaryFiles: cleanTemporaryFiles,
            userCaches: cleanUserCaches,
            diagnosticReports: cleanDiagnosticReports,
            chrome: cleanChrome,
            edge: cleanEdge,
            safari: cleanSafari,
            emptyTrash: emptyTrash,
            xcodeDerivedData: cleanXcodeDerivedData,
            loginItemDiagnostics: loginItemDiagnostics,
            leftoverScan: appLeftoverScan,
            showMemory: showMemory,
            showDisk: showDisk
        )
    }
}

protocol PreferenceStoring: Sendable {
    func load() -> CleaningPreferences
    func save(_ preferences: CleaningPreferences) throws
}

struct JSONPreferenceStore: PreferenceStoring, Sendable {
    var fileURL: URL

    static func applicationSupport() -> JSONPreferenceStore {
        let root = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Clenzy", isDirectory: true)
        return JSONPreferenceStore(fileURL: root.appendingPathComponent("settings.json"))
    }

    func load() -> CleaningPreferences {
        guard let data = try? Data(contentsOf: fileURL) else { return CleaningPreferences() }
        return (try? JSONDecoder().decode(CleaningPreferences.self, from: data)) ?? CleaningPreferences()
    }

    func save(_ preferences: CleaningPreferences) throws {
        try FileManager.default.createDirectory(at: fileURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        let data = try JSONEncoder().encode(preferences)
        try data.write(to: fileURL, options: .atomic)
    }
}

final class MemoryPreferenceStore: PreferenceStoring, @unchecked Sendable {
    private let lock = NSLock()
    private var stored: CleaningPreferences

    init(_ preferences: CleaningPreferences = CleaningPreferences()) {
        stored = preferences
    }

    func load() -> CleaningPreferences {
        lock.lock()
        defer { lock.unlock() }
        return stored
    }

    func save(_ preferences: CleaningPreferences) throws {
        lock.lock()
        defer { lock.unlock() }
        stored = preferences
    }
}

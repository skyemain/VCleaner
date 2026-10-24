import Foundation
import OSLog

enum AppLog {
    static let clean = Logger(subsystem: "com.skyemain.CleanSlate", category: "clean")

    static func finished(_ record: SessionRecord) {
        clean.info("Clean finished mode=\(record.mode, privacy: .public) deleted=\(record.deletedCount, privacy: .public) bytes=\(record.deletedBytes, privacy: .public) errors=\(record.errors.count, privacy: .public)")
    }
}

struct HistoryStore: Sendable {
    var fileURL: URL
    var limit: Int = 50

    static func applicationSupport() -> HistoryStore {
        let root = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("CleanSlate", isDirectory: true)
        return HistoryStore(fileURL: root.appendingPathComponent("history.json"))
    }

    func load() -> [SessionRecord] {
        guard let data = try? Data(contentsOf: fileURL) else { return [] }
        return (try? JSONDecoder().decode([SessionRecord].self, from: data)) ?? []
    }

    func append(_ record: SessionRecord) throws {
        var records = load()
        records.append(record)
        if records.count > limit {
            records = Array(records.suffix(limit))
        }
        try FileManager.default.createDirectory(at: fileURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        try JSONEncoder().encode(records).write(to: fileURL, options: .atomic)
    }

    func clear() throws {
        if FileManager.default.fileExists(atPath: fileURL.path) {
            try FileManager.default.removeItem(at: fileURL)
        }
    }

    static func report(from records: [SessionRecord]) -> String {
        if records.isEmpty { return "CleanSlate has no cleaning history yet.\n" }
        return records.reversed().map { record in
            let when = ISO8601DateFormatter().string(from: record.finishedAt)
            let verb = record.dryRun ? "Would recover" : "Space recovered"
            return """
            \(when)  \(record.mode)
            \(verb): \(ByteFormat.string(from: record.deletedBytes))
            Files cleaned: \(record.deletedCount)
            Files skipped: \(record.skippedCount)
            Errors: \(record.errors.count)
            Duration: \(String(format: "%.1f", record.duration)) seconds
            """
        }.joined(separator: "\n\n")
    }
}


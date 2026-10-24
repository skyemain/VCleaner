import Foundation
import XCTest
@testable import CleanSlate

struct Fixture {
    let root: URL
    var home: URL { root.appendingPathComponent("home", isDirectory: true) }
    var temporary: URL { root.appendingPathComponent("tmp", isDirectory: true) }

    init() throws {
        root = URL(fileURLWithPath: NSTemporaryDirectory(), isDirectory: true)
            .appendingPathComponent("cleanslate-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: home, withIntermediateDirectories: true)
        try FileManager.default.createDirectory(at: temporary, withIntermediateDirectories: true)
    }

    var locations: Locations { Locations(home: home, temporaryDirectory: temporary) }

    func destroy() {
        try? FileManager.default.removeItem(at: root)
    }

    @discardableResult
    func write(relative: String, bytes: Int = 32, age: TimeInterval = -86_400 * 10, in base: URL? = nil) throws -> URL {
        let url = (base ?? home).appendingPathComponent(relative)
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data(repeating: 1, count: bytes).write(to: url)
        try FileManager.default.setAttributes([.modificationDate: Date().addingTimeInterval(age)], ofItemAtPath: url.path)
        return url
    }

    func context(dryRun: Bool = false, mode: CleanMode = .manual, exclusions: [String] = []) -> ScanContext {
        ScanContext(locations: locations, now: Date(), exclusions: exclusions, dryRun: dryRun, mode: mode)
    }
}

final class SlowApplications: ApplicationControlling, @unchecked Sendable {
    func isRunning(bundleIdentifier: String) async -> Bool {
        try? await Task.sleep(nanoseconds: 400_000_000)
        return false
    }

    func requestQuit(bundleIdentifier: String) async -> Bool { true }
}

final class RunningBrowser: ApplicationControlling, @unchecked Sendable {
    var quitCalls = 0
    func isRunning(bundleIdentifier: String) async -> Bool { true }
    func requestQuit(bundleIdentifier: String) async -> Bool {
        quitCalls += 1
        return false
    }
}

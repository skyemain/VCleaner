import Darwin
import XCTest
@testable import CleanSlate

final class SystemCleanerTests: XCTestCase {
    func testOldHiddenTemporaryFilesAreIncludedButFreshOnesStay() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let old = try fixture.write(relative: ".old-cache/data", bytes: 15, age: -86_400 * 3, in: fixture.temporary)
        let fresh = try fixture.write(relative: ".new", bytes: 20, age: -60, in: fixture.temporary)
        XCTAssertEqual(SystemCleaners.temporaryFiles(fixture.context()).byteCount, 15)
        XCTAssertEqual(SystemCleaners.cleanTemporary(fixture.context(), cancel: { false }).deletedBytes, 15)
        XCTAssertFalse(FileManager.default.fileExists(atPath: old.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: fresh.path))
    }

    func testArchivedLogsRespectAgeAndLeaveActiveLogsAndExclusions() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let old = try fixture.write(relative: "Library/Logs/App/app.log.2.gz", bytes: 10, age: -86_400 * 40)
        let active = try fixture.write(relative: "Library/Logs/App/app.log", age: -86_400 * 40)
        let fresh = try fixture.write(relative: "Library/Logs/App/app.log.1", age: -60)
        let excluded = try fixture.write(relative: "Library/Logs/App/keep.log.gz", age: -86_400 * 40)
        let context = fixture.context(exclusions: [excluded.path])
        XCTAssertEqual(SystemCleaners.rotatedLogs(context).byteCount, 10)
        XCTAssertEqual(SystemCleaners.cleanRotatedLogs(context, cancel: { false }).deletedBytes, 10)
        XCTAssertFalse(FileManager.default.fileExists(atPath: old.path))
        for file in [active, fresh, excluded] { XCTAssertTrue(FileManager.default.fileExists(atPath: file.path)) }
    }

    func testRecentTemporaryFilesAreKept() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        try fixture.write(relative: "fresh.tmp", bytes: 8, age: -60, in: fixture.temporary)
        try fixture.write(relative: "stale.tmp", bytes: 8, age: -86_400 * 3, in: fixture.temporary)
        let estimate = SystemCleaners.temporaryFiles(fixture.context())
        XCTAssertEqual(estimate.fileCount, 1)
        XCTAssertTrue(estimate.samplePaths.contains { $0.hasSuffix("stale.tmp") })
    }

    func testDiagnosticReportsRespectTheThirtyDayCutoff() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        try fixture.write(relative: "Library/Logs/DiagnosticReports/old.ips", age: -86_400 * 40)
        try fixture.write(relative: "Library/Logs/DiagnosticReports/new.ips", age: -86_400)
        try fixture.write(relative: "Library/Logs/DiagnosticReports/notes.txt", age: -86_400 * 40)
        let estimate = SystemCleaners.diagnosticReports(fixture.context())
        XCTAssertEqual(estimate.fileCount, 1)
        XCTAssertTrue(estimate.samplePaths[0].hasSuffix("old.ips"))
    }

    func testMissingDiagnosticFolderIsNotAnError() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let estimate = SystemCleaners.diagnosticReports(fixture.context())
        XCTAssertEqual(estimate.status, .ready)
        XCTAssertEqual(estimate.fileCount, 0)
    }

    func testSupportedServiceCachesRespectAgeExclusionsAndAllowlist() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let old = try fixture.write(relative: "Library/Caches/com.apple.parsecd/old", bytes: 20, age: -86_400 * 10)
        let recent = try fixture.write(relative: "Library/Caches/com.apple.touristd/new", bytes: 30, age: -60)
        let excluded = try fixture.write(relative: "Library/Caches/com.apple.helpd/keep", bytes: 40)
        let unrelated = try fixture.write(relative: "Library/Caches/other.app/data", bytes: 50)
        let context = fixture.context(exclusions: [excluded.path])
        XCTAssertEqual(SystemCleaners.userCaches(context).byteCount, 20)
        let cleanup = SystemCleaners.cleanHelpCaches(context, cancel: { false })
        XCTAssertEqual(cleanup.deletedBytes, 20)
        XCTAssertFalse(FileManager.default.fileExists(atPath: old.path))
        for kept in [recent, excluded, unrelated] {
            XCTAssertTrue(FileManager.default.fileExists(atPath: kept.path))
        }
    }

    func testThumbnailsStayUnsupported() {
        let estimate = SystemCleaners.thumbnails()
        guard case .unsupported = estimate.status else {
            return XCTFail("Expected thumbnails to be unsupported")
        }
    }

    func testExcludedPathIsNotCleaned() async throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let file = try fixture.write(relative: "kept.tmp", bytes: 12, age: -86_400 * 3, in: fixture.temporary)
        let engine = CleaningEngine(locations: fixture.locations)
        var categories = EnabledCategories()
        categories.userCaches = false
        categories.diagnosticReports = false
        categories.chrome = false
        categories.edge = false
        categories.safari = false
        _ = try await engine.clean(CleanRequest(mode: .manual, dryRun: false, categories: categories, exclusions: [file.path], allowBrowserQuit: false, emptyTrashConfirmed: false))
        XCTAssertTrue(FileManager.default.fileExists(atPath: file.path))
    }
}

final class AppMemoryTests: XCTestCase {
    func testRankingGroupsDuplicateBundlesUsesTotalRAMAndLimitsToFive() {
        let apps = (0..<7).map { MemoryAppIdentity(path: "/Apps/App\($0).app", name: "App\($0)", pid: Int32($0 + 1)) }
        let totals = Dictionary(uniqueKeysWithValues: apps.enumerated().map { ($0.element.path, UInt64(($0.offset + 1) * 100)) })
        let ranked = AppMemoryService.ranked(apps: apps + [apps[6]], bytesByPath: totals, totalBytes: 10_000)
        XCTAssertEqual(ranked.count, 5)
        XCTAssertEqual(ranked[0].name, "App6")
        XCTAssertEqual(ranked[0].percentText, "7.0%")
        XCTAssertEqual(ranked[4].bytes, 300)
    }

    func testBatchRAMCleanupKeepsTopFiveFinderAndItself() {
        let apps = (0..<9).map { MemoryAppIdentity(path: "/Apps/App\($0).app", name: "App\($0)", pid: Int32($0 + 1)) }
            + [MemoryAppIdentity(path: "/System/Library/CoreServices/Finder.app", name: "Finder", pid: 99)]
        let totals = Dictionary(uniqueKeysWithValues: apps.enumerated().map { ($0.element.path, UInt64(1000 - $0.offset * 50)) })
        let ranked = AppMemoryService.ranked(apps: apps, bytesByPath: totals, totalBytes: 10_000, limit: Int.max)
        XCTAssertEqual(ranked.count, 10)
        let candidates = AppMemoryService.cleanupCandidates(ranked, ownPID: 8)
        XCTAssertEqual(candidates.map(\.name), ["App8", "App6", "App5"])
        XCTAssertTrue(AppMemoryService.cleanupCandidates(Array(ranked.prefix(5)), ownPID: 123).isEmpty)
    }

    func testUnavailableAndZeroMemorySamplesAreNotInvented() {
        let apps = [MemoryAppIdentity(path: "/Apps/App.app", name: "App", pid: 1)]
        XCTAssertTrue(AppMemoryService.ranked(apps: apps, bytesByPath: [:], totalBytes: 100).isEmpty)
        let sample = AppMemoryService.ranked(apps: apps, bytesByPath: [apps[0].path: 5], totalBytes: 0)
        XCTAssertEqual(sample[0].percent, 0)
    }

    func testPermissionProbeDistinguishesMissingAndBlockedFolders() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        XCTAssertEqual(PermissionService.check(fixture.home.appendingPathComponent("missing")), .missing)
        let file = try fixture.write(relative: "Library/Caches/com.apple.helpd/test")
        let root = file.deletingLastPathComponent()
        XCTAssertEqual(PermissionService.check(root), .available)
        chmod(root.path, 0)
        defer { chmod(root.path, 0o755) }
        XCTAssertEqual(PermissionService.check(root), .blocked)
    }

    func testOneBlockedCacheDoesNotSkipOtherSystemCaches() async throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let blocked = try fixture.write(relative: "Library/Caches/com.apple.helpd/keep").deletingLastPathComponent()
        let removable = try fixture.write(relative: "Library/Caches/com.apple.touristd/remove")
        chmod(blocked.path, 0)
        defer { chmod(blocked.path, 0o755) }
        var categories = EnabledCategories()
        categories.safari = false
        categories.chrome = false
        let result = try await CleaningEngine(locations: fixture.locations).clean(CleanRequest(mode: .manual,
            dryRun: false, categories: categories, exclusions: [], allowBrowserQuit: false, emptyTrashConfirmed: false))
        XCTAssertFalse(FileManager.default.fileExists(atPath: removable.path))
        XCTAssertTrue(result.modules.contains { if case .unavailable = $0.cleanup.status { return true }; return false })
    }
}

final class BatteryInfoTests: XCTestCase {
    func testBatteryTimeNeverInventsAnEstimate() {
        XCTAssertEqual(BatteryInfoService.timeLabel(seconds: -1, pluggedIn: false), "Calculating…")
        XCTAssertEqual(BatteryInfoService.timeLabel(seconds: 10_800, pluggedIn: true), "Plugged in")
        XCTAssertEqual(BatteryInfoService.timeLabel(seconds: 3660, pluggedIn: false), "≈ 1 h 1 min")
        XCTAssertEqual(BatteryInfoService.fraction(50, 100), 0.5)
        XCTAssertEqual(BatteryInfoService.fraction(50, 0), nil)
    }
}

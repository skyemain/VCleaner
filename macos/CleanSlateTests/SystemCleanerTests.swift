import XCTest
@testable import CleanSlate

final class SystemCleanerTests: XCTestCase {
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

final class MemoryPurgeTests: XCTestCase {
    func testBudgetNeverAsksForMoreThanAGigabyteOrHalfOfPhysicalMemory() {
        XCTAssertEqual(MemoryPurgeService.budget(inactive: 50_000_000, purgeable: 50_000_000, physical: 16_000_000_000), 0)
        XCTAssertEqual(MemoryPurgeService.budget(inactive: 300_000_000, purgeable: 0, physical: 400_000_000), 200_000_000)
        XCTAssertEqual(MemoryPurgeService.budget(inactive: 2_000_000_000, purgeable: 2_000_000_000, physical: 16_000_000_000), 1024 * 1024 * 1024)
    }
}

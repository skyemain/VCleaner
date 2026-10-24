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

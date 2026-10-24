import XCTest
@testable import CleanSlate

final class SafeDeletionTests: XCTestCase {
    func testRefusesDownloadsAndTheHomeFolder() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        XCTAssertFalse(ProtectedPaths.isApprovedRoot(fixture.home.appendingPathComponent("Downloads"), home: fixture.home, temporaryDirectory: fixture.temporary, allowXcodeDerivedData: false, allowTrash: false))
        XCTAssertFalse(ProtectedPaths.isApprovedRoot(fixture.home, home: fixture.home, temporaryDirectory: fixture.temporary, allowXcodeDerivedData: false, allowTrash: false))
        XCTAssertFalse(ProtectedPaths.isApprovedRoot(fixture.home.appendingPathComponent("Library/Safari"), home: fixture.home, temporaryDirectory: fixture.temporary, allowXcodeDerivedData: false, allowTrash: false))
        XCTAssertFalse(ProtectedPaths.isApprovedRoot(fixture.home.appendingPathComponent("Library/Developer/Xcode/Archives"), home: fixture.home, temporaryDirectory: fixture.temporary, allowXcodeDerivedData: true, allowTrash: false))
    }

    func testDeletesAnOldTemporaryFileAndReportsItsSize() async throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let file = try fixture.write(relative: "old.tmp", bytes: 128, age: -86_400 * 3, in: fixture.temporary)
        let engine = CleaningEngine(locations: fixture.locations)
        var categories = EnabledCategories()
        categories.userCaches = false
        categories.diagnosticReports = false
        categories.chrome = false
        categories.edge = false
        categories.safari = false
        let result = try await engine.clean(CleanRequest(mode: .manual, dryRun: false, categories: categories, exclusions: [], allowBrowserQuit: false, emptyTrashConfirmed: false))
        XCTAssertFalse(FileManager.default.fileExists(atPath: file.path))
        XCTAssertEqual(result.deletedBytes, 128)
        XCTAssertEqual(result.deletedCount, 1)
    }

    func testDoesNotFollowASymlinkOutOfTheCache() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let secret = try fixture.write(relative: "Documents/secret.txt", bytes: 16)
        let cache = fixture.home.appendingPathComponent("Library/Caches/com.apple.helpd")
        try FileManager.default.createDirectory(at: cache, withIntermediateDirectories: true)
        let link = cache.appendingPathComponent("escape")
        try FileManager.default.createSymbolicLink(at: link, withDestinationURL: secret)
        let outcome = SafeDeletionService().deleteEligibleFiles(
            under: cache,
            context: fixture.context(),
            minimumAge: Date().addingTimeInterval(-86_400)
        )
        XCTAssertTrue(FileManager.default.fileExists(atPath: secret.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: link.path))
        XCTAssertEqual(outcome.deletedCount, 0)
    }

    func testSkipsReservedBrowserFilesInsideACache() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let cache = fixture.home.appendingPathComponent("Library/Application Support/Google/Chrome/Default/Cache")
        try FileManager.default.createDirectory(at: cache, withIntermediateDirectories: true)
        let cookies = cache.appendingPathComponent("Cookies")
        try Data(repeating: 2, count: 20).write(to: cookies)
        let junk = cache.appendingPathComponent("data_0")
        try Data(repeating: 3, count: 40).write(to: junk)
        let outcome = SafeDeletionService().deleteEligibleFiles(under: cache, context: fixture.context(), minimumAge: nil)
        XCTAssertTrue(FileManager.default.fileExists(atPath: cookies.path))
        XCTAssertFalse(FileManager.default.fileExists(atPath: junk.path))
        XCTAssertEqual(outcome.deletedBytes, 40)
    }

    func testDryRunRemovesNothing() async throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let file = try fixture.write(relative: "old.tmp", bytes: 10, age: -86_400 * 3, in: fixture.temporary)
        let engine = CleaningEngine(locations: fixture.locations)
        var categories = EnabledCategories()
        categories.userCaches = false
        categories.diagnosticReports = false
        categories.chrome = false
        categories.edge = false
        categories.safari = false
        let result = try await engine.clean(CleanRequest(mode: .manual, dryRun: true, categories: categories, exclusions: [], allowBrowserQuit: false, emptyTrashConfirmed: false))
        XCTAssertTrue(FileManager.default.fileExists(atPath: file.path))
        XCTAssertTrue(result.record.dryRun)
        XCTAssertGreaterThan(result.deletedBytes, 0)
    }
}

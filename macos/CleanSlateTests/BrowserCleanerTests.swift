import Darwin
import XCTest
@testable import CleanSlate

final class BrowserCleanerTests: XCTestCase {
    func testChromeFindsEveryProfileCacheAndLeavesCredentials() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        try fixture.write(relative: "Library/Application Support/Google/Chrome/Default/Cache/data_0", bytes: 50, age: 0)
        try fixture.write(relative: "Library/Application Support/Google/Chrome/Default/Cookies", bytes: 80, age: 0)
        try fixture.write(relative: "Library/Application Support/Google/Chrome/Default/History", bytes: 80, age: 0)
        try fixture.write(relative: "Library/Application Support/Google/Chrome/Default/Bookmarks", bytes: 20, age: 0)
        try fixture.write(relative: "Library/Application Support/Google/Chrome/Profile 1/Preferences", bytes: 10, age: 0)
        try fixture.write(relative: "Library/Application Support/Google/Chrome/Profile 1/Code Cache/index", bytes: 15, age: 0)
        let roots = ChromiumCacheCleaner.cacheRoots(KnownBrowsers.chrome, home: fixture.home)
        XCTAssertEqual(roots.count, 2)
        let estimate = ChromiumCacheCleaner.estimate(KnownBrowsers.chrome, context: fixture.context())
        XCTAssertEqual(estimate.fileCount, 2)
        XCTAssertFalse(estimate.samplePaths.contains { $0.hasSuffix("Cookies") || $0.hasSuffix("History") || $0.hasSuffix("Bookmarks") })
        let edge = ChromiumCacheCleaner.estimate(KnownBrowsers.edge, context: fixture.context())
        XCTAssertEqual(edge.fileCount, 0)
        XCTAssertNotEqual(estimate.id, edge.id)
    }

    func testSafariReportsAccessRestrictionsInsteadOfSuccess() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let cache = fixture.home.appendingPathComponent("Library/Caches/com.apple.Safari/fsCachedData")
        try FileManager.default.createDirectory(at: cache, withIntermediateDirectories: true)
        try fixture.write(relative: "Library/Caches/com.apple.Safari/fsCachedData/page", bytes: 8, age: 0)
        chmod(cache.path, 0)
        defer { chmod(cache.path, 0o755) }
        let estimate = SafariCleaner.estimate(fixture.context())
        chmod(cache.path, 0o755)
        XCTAssertEqual(estimate.status, .unavailable(SafariCleaner.unavailableMessage))
        XCTAssertEqual(estimate.fileCount, 0)
        XCTAssertTrue(FileManager.default.fileExists(atPath: cache.appendingPathComponent("page").path))
    }

    func testRunningBrowserIsNotForceQuitDuringLoginCleaning() async throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        try fixture.write(relative: "Library/Application Support/Google/Chrome/Default/Cache/data_0", bytes: 40, age: 0)
        let apps = RunningBrowser()
        let engine = CleaningEngine(locations: fixture.locations, applications: apps)
        let result = try await engine.clean(CleanRequest(mode: .login, dryRun: false, categories: EnabledCategories(), exclusions: [], allowBrowserQuit: true, emptyTrashConfirmed: true))
        XCTAssertEqual(apps.quitCalls, 0)
        XCTAssertTrue(FileManager.default.fileExists(atPath: fixture.home.appendingPathComponent("Library/Application Support/Google/Chrome/Default/Cache/data_0").path))
        XCTAssertTrue(result.modules.contains { $0.estimate.id == "chrome" && $0.cleanup.deletedCount == 0 })
    }
}

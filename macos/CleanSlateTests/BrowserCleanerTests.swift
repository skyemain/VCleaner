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

    }

    func testSafariReportsAccessRestrictionsInsteadOfSuccess() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let cache = fixture.home.appendingPathComponent("Library/Caches/com.apple.Safari")
        try FileManager.default.createDirectory(at: cache, withIntermediateDirectories: true)
        try fixture.write(relative: "Library/Caches/com.apple.Safari/page", bytes: 8, age: 0)
        chmod(cache.path, 0)
        defer { chmod(cache.path, 0o755) }
        let estimate = SafariCleaner.estimate(fixture.context())
        chmod(cache.path, 0o755)
        XCTAssertEqual(estimate.status, .unavailable(SafariCleaner.unavailableMessage))
        XCTAssertEqual(estimate.fileCount, 0)
        XCTAssertTrue(FileManager.default.fileExists(atPath: cache.appendingPathComponent("page").path))
    }

    func testSafariRemovesTheWebKitNetworkCacheAndLeavesTheProfile() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let cached = try fixture.write(relative: "Library/Containers/com.apple.WebKit.Networking/Data/Library/Caches/WebKit/NetworkCache/record", bytes: 200, age: 0)
        let cookies = try fixture.write(relative: "Library/Containers/com.apple.Safari/Data/Library/Cookies/Cookies.binarycookies", bytes: 40, age: 0)
        let history = try fixture.write(relative: "Library/Containers/com.apple.Safari/Data/Library/Safari/History.db", bytes: 80, age: 0)
        let estimate = SafariCleaner.estimate(fixture.context())
        XCTAssertEqual(estimate.byteCount, 200)
        XCTAssertEqual(estimate.fileCount, 1)
        let cleanup = SafariCleaner.clean(fixture.context(), cancel: { false })
        XCTAssertEqual(cleanup.deletedBytes, 200)
        XCTAssertFalse(FileManager.default.fileExists(atPath: cached.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: cookies.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: history.path))
    }

    func testSafariContainerCacheIsCleanedButWebsiteStorageStays() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let cached = try fixture.write(relative: "Library/Containers/com.apple.Safari/Data/Library/Caches/com.apple.Safari/WebKit/NetworkCache/data", bytes: 128)
        let storage = try fixture.write(relative: "Library/Containers/com.apple.Safari/Data/Library/Caches/Local Storage/site", bytes: 64)
        let estimate = SafariCleaner.estimate(fixture.context())
        XCTAssertEqual(estimate.byteCount, 128)
        let cleanup = SafariCleaner.clean(fixture.context(), cancel: { false })
        XCTAssertEqual(cleanup.deletedBytes, estimate.byteCount)
        XCTAssertFalse(FileManager.default.fileExists(atPath: cached.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: storage.path))
    }

    func testSafariNestedDeniedCacheIsReportedEvenWithReadableFiles() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        try fixture.write(relative: "Library/Caches/com.apple.Safari/readable", bytes: 12)
        let hidden = try fixture.write(relative: "Library/Caches/com.apple.Safari/blocked/data", bytes: 12).deletingLastPathComponent()
        chmod(hidden.path, 0)
        defer { chmod(hidden.path, 0o755) }
        let estimate = SafariCleaner.estimate(fixture.context())
        XCTAssertEqual(estimate.status, .unavailable(SafariCleaner.unavailableMessage))
    }

    func testAMacCleanDoesNotIncludeEdge() async throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let engine = CleaningEngine(locations: fixture.locations)
        var categories = EnabledCategories()
        categories.edge = true
        let estimates = try await engine.scan(CleanRequest(mode: .manual, dryRun: true, categories: categories, exclusions: [], allowBrowserQuit: false, emptyTrashConfirmed: false))
        XCTAssertFalse(estimates.contains { $0.id == "edge" })
        XCTAssertTrue(estimates.contains { $0.id == "safari" })
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

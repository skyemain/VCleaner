import XCTest
@testable import Clenzy

final class CleaningEngineTests: XCTestCase {
    func testTrashRequiresConfirmationAndIncludesHiddenFilesWhenConfirmed() async throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let file = try fixture.write(relative: ".Trash/.hidden", bytes: 23)
        var categories = EnabledCategories()
        categories.emptyTrash = true
        let engine = CleaningEngine(locations: fixture.locations)
        let no = CleanRequest(mode: .manual, dryRun: false, categories: categories, exclusions: [], allowBrowserQuit: false, emptyTrashConfirmed: false)
        _ = try await engine.clean(no)
        XCTAssertTrue(FileManager.default.fileExists(atPath: file.path))
        var yes = no
        yes.emptyTrashConfirmed = true
        let result = try await engine.clean(yes)
        XCTAssertEqual(result.deletedBytes, 23)
        XCTAssertFalse(FileManager.default.fileExists(atPath: file.path))
    }

    func testLoginCleanDoesNotEmptyTrashOrTouchXcode() async throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let trash = try fixture.write(relative: ".Trash/gone.txt", bytes: 24, age: -86_400 * 10)
        let derived = try fixture.write(relative: "Library/Developer/Xcode/DerivedData/App/Build/a.o", bytes: 24, age: -86_400 * 10)
        let temp = try fixture.write(relative: "old.tmp", bytes: 18, age: -86_400 * 3, in: fixture.temporary)
        var categories = EnabledCategories()
        categories.emptyTrash = true
        categories.xcodeDerivedData = true
        categories.userCaches = false
        categories.diagnosticReports = false
        categories.chrome = false
        categories.edge = false
        categories.safari = false
        let engine = CleaningEngine(locations: fixture.locations)
        let result = try await engine.clean(CleanRequest(mode: .login, dryRun: false, categories: categories, exclusions: [], allowBrowserQuit: true, emptyTrashConfirmed: true))
        XCTAssertTrue(FileManager.default.fileExists(atPath: trash.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: derived.path))
        XCTAssertFalse(FileManager.default.fileExists(atPath: temp.path))
        XCTAssertFalse(result.modules.contains { $0.estimate.id == "trash" || $0.estimate.id == "xcode" })
    }

    func testASecondCleanIsRefusedWhileOneIsRunning() async throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let engine = CleaningEngine(locations: fixture.locations, applications: SlowApplications())
        let request = CleanRequest(mode: .manual, dryRun: true, categories: EnabledCategories(), exclusions: [], allowBrowserQuit: false, emptyTrashConfirmed: false)
        let first = Task { try await engine.clean(request) }
        try await Task.sleep(nanoseconds: 50_000_000)
        do {
            _ = try await engine.clean(request)
            XCTFail("The second clean should have been refused.")
        } catch CleanEngineError.alreadyRunning {
        }
        _ = try await first.value
    }

    func testDiscoveredFilesAreNotDoubleCountedAcrossBrowsers() async throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        try fixture.write(relative: "Library/Application Support/Google/Chrome/Default/Cache/data_0", bytes: 100, age: 0)
        var categories = EnabledCategories()
        categories.temporaryFiles = false
        categories.userCaches = false
        categories.diagnosticReports = false
        categories.edge = false
        categories.safari = false
        let engine = CleaningEngine(locations: fixture.locations)
        let estimates = try await engine.scan(CleanRequest(mode: .manual, dryRun: true, categories: categories, exclusions: [], allowBrowserQuit: false, emptyTrashConfirmed: false))
        let chrome = estimates.filter { $0.id == "chrome" }
        XCTAssertEqual(chrome.count, 1)
        XCTAssertEqual(chrome[0].byteCount, 100)
    }
}

final class LoginAndSettingsTests: XCTestCase {
    @MainActor
    func testCleanAtLoginTurnsOnTheLoginItem() throws {
        let store = MemoryPreferenceStore()
        let login = FakeLoginItems()
        let settings = SettingsViewModel(store: store, loginItems: login)
        settings.update { $0.cleanAtLogin = true }
        XCTAssertTrue(settings.preferences.startAtLogin)
        XCTAssertTrue(login.enabled)
        settings.update { $0.startAtLogin = false }
        XCTAssertFalse(settings.preferences.cleanAtLogin)
        XCTAssertFalse(login.enabled)
    }

    @MainActor
    func testMenuBarAndOtherSettingsDoNotTouchTheLoginItem() {
        let login = FakeLoginItems()
        let settings = SettingsViewModel(store: MemoryPreferenceStore(), loginItems: login)
        let statusAfterInit = login.statusCalls
        let writesAfterInit = login.setCalls
        settings.setMenuBarVisible(true)
        settings.setMenuBarVisible(true)
        settings.update { $0.cleanTemporaryFiles = false }
        XCTAssertTrue(settings.preferences.showMenuBarIcon)
        XCTAssertEqual(login.statusCalls, statusAfterInit)
        XCTAssertEqual(login.setCalls, writesAfterInit)
    }

    @MainActor
    func testPreferencesRoundTrip() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let store = JSONPreferenceStore(fileURL: fixture.root.appendingPathComponent("settings.json"))
        var original = CleaningPreferences()
        original.cleanChrome = false
        original.excludedPaths = ["/tmp/keep"]
        try store.save(original)
        XCTAssertEqual(store.load(), original)
    }

    @MainActor
    func testCancellingTheLoginCountdownDeletesNothing() async throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let file = try fixture.write(relative: "old.tmp", bytes: 9, age: -86_400 * 3, in: fixture.temporary)
        let box = CancelBox()
        let settings = SettingsViewModel(store: MemoryPreferenceStore(), loginItems: FakeLoginItems())
        let model = MainViewModel(
            settings: settings,
            engine: CleaningEngine(locations: fixture.locations),
            history: HistoryStore(fileURL: fixture.root.appendingPathComponent("history.json")),
            sleep: { _ in box.model?.cancel() },
            closeApp: {},
            loginLaunchDetector: { false }
        )
        box.model = model
        model.onAppear(arguments: ["--login-clean"])
        try await Task.sleep(nanoseconds: 200_000_000)
        XCTAssertTrue(FileManager.default.fileExists(atPath: file.path))
        XCTAssertNotEqual(model.phase, .completed)
    }
}

final class CancelBox: @unchecked Sendable {
    @MainActor var model: MainViewModel?
}

final class FakeLoginItems: LoginItemRegistering, @unchecked Sendable {
    var enabled = false
    var statusCalls = 0
    var setCalls = 0
    func status() -> LoginItemState {
        statusCalls += 1
        return enabled ? .enabled : .notRegistered
    }
    func setEnabled(_ enabled: Bool) throws {
        setCalls += 1
        self.enabled = enabled
    }
}

final class PermissionAndHistoryTests: XCTestCase {
    func testPermissionDenialIsReported() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let folder = fixture.home.appendingPathComponent("Library/Caches/com.apple.Safari/fsCachedData")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        chmod(folder.path, 0)
        defer { chmod(folder.path, 0o755) }
        XCTAssertTrue(DirectoryWalker.isUnreadable(folder))
        XCTAssertFalse(PermissionService.canRead(folder))
    }

    func testHistoryReportUsesRealRecords() throws {
        let record = SessionRecord(
            id: UUID(), startedAt: Date(timeIntervalSince1970: 0), finishedAt: Date(timeIntervalSince1970: 8),
            mode: "manual", dryRun: false, modules: ["temp"], discoveredCount: 2, discoveredBytes: 20,
            deletedCount: 2, deletedBytes: 20, skippedCount: 1, errors: [], duration: 8
        )
        let report = HistoryStore.report(from: [record])
        XCTAssertTrue(report.contains("Space recovered"))
        XCTAssertTrue(report.contains("Files cleaned: 2"))
        XCTAssertFalse(report.contains("password"))
    }
}

final class MainInteractionTests: XCTestCase {
    @MainActor
    func testSettingsAndAccessGuideOpenTheSamePanelOnDifferentTabs() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let model = MainViewModel(settings: SettingsViewModel(store: MemoryPreferenceStore(), loginItems: FakeLoginItems()),
            engine: CleaningEngine(locations: fixture.locations), history: HistoryStore(fileURL: fixture.root.appendingPathComponent("history.json")),
            closeApp: {})
        model.openSettings()
        XCTAssertTrue(model.showSettings)
        XCTAssertEqual(model.settingsTab, .preferences)
        model.openAccessGuide()
        XCTAssertTrue(model.showSettings)
        XCTAssertEqual(model.settingsTab, .access)
    }

    @MainActor
    func testNormalLaunchAndLegacyInstallerArgumentOpenDashboardWithoutSetup() throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let model = MainViewModel(settings: SettingsViewModel(store: MemoryPreferenceStore(), loginItems: FakeLoginItems()),
            engine: CleaningEngine(locations: fixture.locations), history: HistoryStore(fileURL: fixture.root.appendingPathComponent("history.json")),
            closeApp: {}, loginLaunchDetector: { false })
        for arguments in [[], ["--installation-complete"], []] {
            model.onAppear(arguments: arguments)
            XCTAssertFalse(model.showSettings)
            XCTAssertFalse(model.showDetails)
            XCTAssertEqual(model.phase, .idle)
        }
    }

    @MainActor
    func testManualCleanIncludesTrashAndRecordsHistory() async throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let file = try fixture.write(relative: ".Trash/.hidden", bytes: 17)
        let store = HistoryStore(fileURL: fixture.root.appendingPathComponent("history.json"))
        let model = MainViewModel(settings: SettingsViewModel(store: MemoryPreferenceStore(), loginItems: FakeLoginItems()),
            engine: CleaningEngine(locations: fixture.locations), history: store, closeApp: {})
        model.cleanNow()
        for _ in 0..<200 { if model.phase == .completed { break }; try await Task.sleep(nanoseconds: 10_000_000) }
        XCTAssertEqual(model.phase, .completed)
        XCTAssertFalse(FileManager.default.fileExists(atPath: file.path))
        XCTAssertEqual(store.load().last?.deletedBytes, 17)
        XCTAssertFalse(model.showSettings)
    }

    @MainActor
    func testBlockedTrashDoesNotStopSystemJunkCleanup() async throws {
        let fixture = try Fixture()
        defer { fixture.destroy() }
        let trash = try fixture.write(relative: ".Trash/keep", bytes: 12)
        let temp = try fixture.write(relative: "old.tmp", bytes: 10, age: -86_400 * 3, in: fixture.temporary)
        let model = MainViewModel(settings: SettingsViewModel(store: MemoryPreferenceStore(), loginItems: FakeLoginItems()),
            engine: CleaningEngine(locations: fixture.locations), history: HistoryStore(fileURL: fixture.root.appendingPathComponent("history.json")),
            closeApp: {})
        let trashRoot = trash.deletingLastPathComponent()
        chmod(trashRoot.path, 0)
        defer { chmod(trashRoot.path, 0o755) }
        model.cleanNow()
        for _ in 0..<200 { if model.phase == .completed { break }; try await Task.sleep(nanoseconds: 10_000_000) }
        XCTAssertEqual(model.phase, .completed)
        chmod(trashRoot.path, 0o755)
        XCTAssertTrue(FileManager.default.fileExists(atPath: trash.path))
        XCTAssertFalse(FileManager.default.fileExists(atPath: temp.path))
        XCTAssertTrue(model.rows.first { $0.id == "junk" }?.needsAttention == true)
        XCTAssertFalse(model.rows.contains { $0.id == "health" || $0.id == "trash" })
    }
}

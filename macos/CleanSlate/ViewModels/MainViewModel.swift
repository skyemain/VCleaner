import AppKit
import Foundation

struct HomeRow: Identifiable, Equatable {
    var id: String
    var title: String
    var detail: String
}

struct HealthSnapshot: Equatable {
    var memory: MemorySnapshot?
    var disk: DiskSnapshot?
    var agents: [LaunchAgentFinding] = []
    var leftovers: [LeftoverCandidate] = []
    var archiveBytes: Int64 = 0
    var simulatorBytes: Int64 = 0
    var largeDownloads: [String] = []
    var permissions: PermissionService.Status?
}

@MainActor
final class MainViewModel: ObservableObject {
    enum Phase: Equatable {
        case idle
        case countingDown
        case working
        case completed
        case failed
    }

    @Published var phase: Phase = .idle
    @Published var countdownText = ""
    @Published var statusText = "Ready"
    @Published var rows: [HomeRow] = MainViewModel.placeholderRows
    @Published var result: CleanRunResult?
    @Published var estimates: [ModuleEstimate] = []
    @Published var errorMessage = ""
    @Published var lastCleanedText = "Last cleaned: Never"
    @Published var recoveredText = "Recovered space: 0 MB"
    @Published var health = HealthSnapshot()
    @Published var showDetails = false
    @Published var showHealth = false
    @Published var browserPrompt: String?
    @Published var trashPrompt = false
    @Published var freeingMemory = false
    @Published var memoryNote = ""
    private var pendingBrowserQuit = false

    let settings: SettingsViewModel
    private let engine: CleaningEngine
    private let history: HistoryStore
    private let sleep: (TimeInterval) async -> Void
    private let closeApp: () -> Void
    private let loginLaunchDetector: () -> Bool
    private var userHeldOpen = false
    private var cancelRequested = false
    private var runTask: Task<Void, Never>?

    init(
        settings: SettingsViewModel,
        engine: CleaningEngine? = nil,
        history: HistoryStore = .applicationSupport(),
        sleep: @escaping (TimeInterval) async -> Void = { seconds in
            try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
        },
        closeApp: @escaping () -> Void = { NSApp.terminate(nil) },
        loginLaunchDetector: @escaping () -> Bool = LoginLaunchDetection.current
    ) {
        self.settings = settings
        self.engine = engine ?? CleaningEngine(locations: .currentUser(), applications: MacApplicationController())
        self.history = history
        self.sleep = sleep
        self.closeApp = closeApp
        self.loginLaunchDetector = loginLaunchDetector
        refreshLastCleaned()
    }

    func onAppear(arguments: [String]) {
        let forced = arguments.contains("--login-clean")
        guard forced || (loginLaunchDetector() && settings.preferences.cleanAtLogin) else { return }
        guard runTask == nil else { return }
        runTask = Task { await runLoginClean() }
    }

    func freeMemory() {
        guard !freeingMemory, phase != .working, phase != .countingDown else { return }
        freeingMemory = true
        memoryNote = "Releasing cached memory"
        Task {
            let outcome = await Task.detached(priority: .utility) {
                MemoryPurgeService.purge()
            }.value
            freeingMemory = false
            memoryNote = outcome.message
        }
    }

    func cleanNow() {
        guard phase == .idle || phase == .completed || phase == .failed else { return }
        runTask = Task {
            if await browsersNeedQuitPrompt() {
                browserPrompt = "Safari or Chrome is open. Quit it normally so the cache can be cleaned? CleanSlate will not force the browser to quit."
                return
            }
            await run(mode: .manual, fromReview: false)
        }
    }

    func answerBrowserQuit(_ allow: Bool) {
        guard browserPrompt != nil else { return }
        browserPrompt = nil
        pendingBrowserQuit = allow
        runTask = Task { await run(mode: .manual, fromReview: false) }
    }

    func requestEmptyTrash() {
        trashPrompt = true
    }

    func answerEmptyTrash(_ empty: Bool) {
        trashPrompt = false
        guard empty else { return }
        runTask = Task { await emptyTrash() }
    }

    func cleanFromReview() {
        guard phase == .idle || phase == .completed else { return }
        showDetails = false
        runTask = Task { await run(mode: .manual, fromReview: true) }
    }

    func cancel() {
        cancelRequested = true
        runTask?.cancel()
        userHeldOpen = true
    }

    func holdOpen() {
        userHeldOpen = true
    }

    func loadHealth() {
        let home = FileManager.default.homeDirectoryForCurrentUser
        var snapshot = HealthSnapshot()
        if settings.preferences.showMemory {
            snapshot.memory = MemoryInfoService.snapshot()
        }
        if settings.preferences.showDisk {
            snapshot.disk = DiskHealthService.snapshot()
        }
        if settings.preferences.loginItemDiagnostics {
            snapshot.agents = LaunchAgentScanner.brokenAgents(home: home)
        }
        if settings.preferences.appLeftoverScan {
            snapshot.leftovers = LeftoverScanner.candidates(
                home: home,
                applicationFolders: [
                    URL(fileURLWithPath: "/Applications"),
                    URL(fileURLWithPath: "/System/Applications"),
                    home.appendingPathComponent("Applications")
                ]
            )
        }
        snapshot.archiveBytes = XcodeCleaner.archiveByteCount(home: home)
        snapshot.simulatorBytes = XcodeCleaner.simulatorByteCount(home: home)
        snapshot.largeDownloads = DownloadsInspector.largeFiles(home: home)
        snapshot.permissions = PermissionService.status(home: home)
        health = snapshot
    }

    func quarantine(_ finding: LaunchAgentFinding) {
        let home = FileManager.default.homeDirectoryForCurrentUser
        do {
            _ = try LaunchAgentScanner.quarantine(finding, home: home)
            loadHealth()
        } catch {
            errorMessage = "That login agent could not be backed up."
        }
    }

    func preview() async {
        let request = makeRequest(mode: .manual, emptyTrashConfirmed: false, allowBrowserQuit: false)
        do {
            estimates = try await engine.scan(request)
            showDetails = true
        } catch CleanEngineError.alreadyRunning {
            errorMessage = "A clean is already running."
        } catch {
            errorMessage = "The scan could not be completed."
        }
    }

    private func runLoginClean() async {
        phase = .countingDown
        cancelRequested = false
        userHeldOpen = false
        if settings.preferences.showLoginCountdown {
            for second in stride(from: 5, through: 1, by: -1) {
                if cancelRequested || Task.isCancelled {
                    phase = .idle
                    countdownText = ""
                    return
                }
                countdownText = "Automatic clean starts in \(second) s"
                await sleep(1)
            }
        }
        countdownText = ""
        await run(mode: .login, fromReview: true)
        guard phase == .completed, settings.preferences.closeAfterAutoClean, settings.preferences.showCompletionResult else { return }
        for second in stride(from: 12, through: 1, by: -1) {
            if userHeldOpen || cancelRequested || Task.isCancelled { return }
            countdownText = "Closing in \(second) s"
            await sleep(1)
        }
        if !userHeldOpen && !cancelRequested {
            closeApp()
        }
    }

    private func run(mode: CleanMode, fromReview: Bool) async {
        errorMessage = ""
        cancelRequested = false
        if mode == .manual && settings.preferences.reviewBeforeCleaning && !fromReview {
            await preview()
            return
        }
        phase = .working
        statusText = "Cleaning"
        let allowQuit = mode == .manual && pendingBrowserQuit
        pendingBrowserQuit = false
        let request = makeRequest(mode: mode, emptyTrashConfirmed: false, allowBrowserQuit: allowQuit)
        do {
            let outcome = try await engine.clean(request)
            result = outcome
            try? history.append(outcome.record)
            AppLog.finished(outcome.record)
            apply(outcome)
            phase = .completed
            statusText = outcome.record.dryRun ? "Dry run" : "Cleaning complete"
            recoveredText = (outcome.record.dryRun ? "Would recover: " : "Recovered space: ") + ByteFormat.string(from: outcome.record.deletedBytes)
            refreshLastCleaned()
        } catch CleanEngineError.alreadyRunning {
            phase = .failed
            errorMessage = "A clean is already running."
        } catch {
            phase = .failed
            errorMessage = "Cleaning stopped because of an unexpected error."
        }
    }

    private func emptyTrash() async {
        phase = .working
        let context = ScanContext(
            locations: .currentUser(),
            now: Date(),
            exclusions: settings.preferences.excludedPaths,
            dryRun: settings.preferences.dryRun,
            mode: .manual
        )
        let cleanup = TrashInspector.empty(context, cancel: { Task.isCancelled })
        statusText = settings.preferences.dryRun ? "Dry run" : "Trash emptied"
        recoveredText = (settings.preferences.dryRun ? "Would recover: " : "Recovered space: ") + ByteFormat.string(from: cleanup.deletedBytes)
        phase = .completed
        errorMessage = cleanup.errors.first ?? ""
    }

    private func browsersNeedQuitPrompt() async -> Bool {
        let request = makeRequest(mode: .manual, emptyTrashConfirmed: false, allowBrowserQuit: false)
        let running = (try? await engine.scan(request)) ?? []
        let busy = running.contains { estimate in
            if case .skipped(let reason) = estimate.status, reason.contains("running") { return true }
            return false
        }
        if busy {
            pendingBrowserQuit = true
        }
        return busy
    }

    private func makeRequest(mode: CleanMode, emptyTrashConfirmed: Bool, allowBrowserQuit: Bool) -> CleanRequest {
        let preferences = settings.preferences
        return CleanRequest(
            mode: mode,
            dryRun: preferences.dryRun,
            categories: mode == .login ? preferences.categories.forLogin() : preferences.categories,
            exclusions: preferences.excludedPaths,
            allowBrowserQuit: allowBrowserQuit,
            emptyTrashConfirmed: emptyTrashConfirmed
        )
    }

    private func apply(_ outcome: CleanRunResult) {
        func detail(_ ids: [String], idle: String) -> String {
            let matched = outcome.modules.filter { ids.contains($0.estimate.id) }
            guard !matched.isEmpty else { return idle }
            if let blocked = matched.first(where: {
                if case .unavailable = $0.cleanup.status { return true }
                if case .unsupported = $0.cleanup.status { return true }
                if case .skipped = $0.cleanup.status { return true }
                return false
            }) {
                switch blocked.cleanup.status {
                case .unavailable(let text), .unsupported(let text), .skipped(let text): return text
                case .ready: break
                }
            }
            let bytes = matched.reduce(Int64(0)) { $0 + $1.cleanup.deletedBytes }
            return bytes > 0 ? ByteFormat.string(from: bytes) : "Nothing to remove"
        }
        rows = [
            HomeRow(id: "junk", title: "SYSTEM JUNK", detail: detail(["temp", "user-cache", "loose-cache", "diagnostics"], idle: "Ready")),
            HomeRow(id: "safari", title: "SAFARI", detail: detail(["safari"], idle: "Ready")),
            HomeRow(id: "chrome", title: "CHROME", detail: detail(["chrome"], idle: "Ready")),
            HomeRow(id: "health", title: "SYSTEM HEALTH", detail: "Ready")
        ]
    }

    private func refreshLastCleaned() {
        let records = history.load().filter { !$0.dryRun }
        guard let latest = records.last else {
            lastCleanedText = "Last cleaned: Never"
            return
        }
        let formatter = DateFormatter()
        formatter.dateStyle = .medium
        formatter.timeStyle = .short
        lastCleanedText = "Last cleaned: \(formatter.string(from: latest.finishedAt))"
        recoveredText = "Recovered space: \(ByteFormat.string(from: latest.deletedBytes))"
    }

    private static let placeholderRows = [
        HomeRow(id: "junk", title: "SYSTEM JUNK", detail: "Ready"),
        HomeRow(id: "safari", title: "SAFARI", detail: "Ready"),
        HomeRow(id: "chrome", title: "CHROME", detail: "Ready"),
        HomeRow(id: "health", title: "SYSTEM HEALTH", detail: "Ready")
    ]
}

enum LoginLaunchDetection {
    static func current() -> Bool {
        guard let event = NSAppleEventManager.shared().currentAppleEvent else { return false }
        guard event.eventID == kAEOpenApplication else { return false }
        return event.paramDescriptor(forKeyword: keyAELaunchedAsLogInItem)?.booleanValue == true
    }
}

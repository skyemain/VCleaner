import AppKit
import Foundation

struct HomeRow: Identifiable, Equatable {
    var id: String
    var title: String
    var detail: String
    var needsAttention = false
}

struct HealthSnapshot: Equatable {
    var memory: MemorySnapshot?
    var disk: DiskSnapshot?
    var battery: BatterySnapshot?
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
    @Published var browserPrompt: String?
    @Published var freeingMemory = false
    @Published var memoryNote = ""
    @Published var memory = MemoryInfoService.snapshot()
    @Published var topMemoryApps: [AppMemoryUsage] = []
    private var allMemoryApps: [AppMemoryUsage] = []
    @Published var showMemoryApps = false
    @Published var showSettings = false
    @Published var settingsTab = SettingsTab.preferences
    enum SettingsTab: Hashable { case preferences, access }
    @Published var permissionChecks: [PermissionService.Check] = []
    @Published var checkingPermissions = false
    private var cleanAfterGuide = false
    private var permissionsAcknowledged = UserDefaults.standard.bool(forKey: "accessGuide.v1.acknowledged")

    func refreshMemory() async {
        let identities = AppMemoryService.identities()
        let snapshot = await Task.detached(priority: .utility) {
            let memory = MemoryInfoService.snapshot()
            return (memory, AppMemoryService.sample(apps: identities, totalBytes: memory.totalBytes, limit: Int.max))
        }.value
        memory = snapshot.0
        allMemoryApps = snapshot.1
        topMemoryApps = Array(snapshot.1.prefix(5))
    }

    var cleaningAccessPending: Bool { cleanAfterGuide }

    func openSettings() {
        cleanAfterGuide = false
        settingsTab = .preferences
        showSettings = true
    }

    func openAccessGuide(forClean: Bool = false) {
        settingsTab = .access
        cleanAfterGuide = forClean
        if showDetails {
            showDetails = false
            Task {
                try? await Task.sleep(nanoseconds: 350_000_000)
                showSettings = true
                await recheckPermissions()
            }
        } else {
            showSettings = true
            Task { await recheckPermissions() }
        }
    }

    func recheckPermissions() async {
        guard !checkingPermissions else { return }
        checkingPermissions = true
        permissionChecks = await Task.detached(priority: .utility) {
            PermissionService.checks(locations: .currentUser())
        }.value
        checkingPermissions = false
    }

    func continueFromAccessGuide() {
        UserDefaults.standard.set(true, forKey: "accessGuide.v1.acknowledged")
        permissionsAcknowledged = true
        showSettings = false
        let shouldClean = cleanAfterGuide
        cleanAfterGuide = false
        if shouldClean {
            Task {
                try? await Task.sleep(nanoseconds: 350_000_000)
                cleanNow()
            }
        }
    }

    func selectTarget(_ row: HomeRow) {
        if row.needsAttention {
            openAccessGuide()
        } else {
            Task { await preview() }
        }
    }
    private var pendingBrowserQuit = false
    private var pendingReview = false

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
        loginLaunchDetector: @escaping () -> Bool = LoginLaunchDetection.current,
        accessGuideAcknowledged: Bool? = nil
    ) {
        self.settings = settings
        if let accessGuideAcknowledged { permissionsAcknowledged = accessGuideAcknowledged }
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
        memoryNote = "Reviewing smaller apps…"
        Task {
            await refreshMemory()
            let before = memory.usedBytes
            let candidates = AppMemoryService.cleanupCandidates(allMemoryApps, ownPID: getpid())
            var requested: [(AppMemoryUsage, NSRunningApplication)] = []
            for candidate in candidates {
                guard let app = NSRunningApplication(processIdentifier: candidate.pid), !app.isTerminated,
                      app.activationPolicy == .regular,
                      app.bundleIdentifier != "com.apple.finder",
                      app.bundleURL?.standardizedFileURL.resolvingSymlinksInPath().path == candidate.id else { continue }
                // Normal quit preserves each app's own save/cancel prompts. No forced termination.
                requested.append((candidate, app))
                _ = app.terminate()
            }
            for _ in 0..<20 {
                if requested.allSatisfy({ $0.1.isTerminated }) { break }
                try? await Task.sleep(nanoseconds: 250_000_000)
            }
            let closed = requested.filter { $0.1.isTerminated }
            let remaining = requested.filter { !$0.1.isTerminated }
            await refreshMemory()
            let result = requested.isEmpty ? "No smaller measured apps to close." : "Closed \(closed.count) of \(requested.count) smaller apps."
            let waiting = remaining.isEmpty ? "" : " Still open: \(remaining.map { $0.0.name }.joined(separator: ", ")). Save your work or respond to their quit dialogs."
            memoryNote = result + " RAM used: \(ByteFormat.string(from: Int64(before))) → \(ByteFormat.string(from: Int64(memory.usedBytes))). Other apps affect this reading." + waiting
            freeingMemory = false
            showMemoryApps = true
        }
    }

    /// Called only after the person confirms one named app in the RAM review.
    func quitMemoryApp(_ candidate: AppMemoryUsage) {
        guard !freeingMemory, candidate.pid != getpid(),
              let app = NSRunningApplication(processIdentifier: candidate.pid),
              app.bundleURL?.standardizedFileURL.resolvingSymlinksInPath().path == candidate.id else {
            memoryNote = "This app is no longer available. Refresh the list."
            return
        }
        freeingMemory = true
        Task {
            let before = memory.usedBytes
            let sent = app.terminate()
            for _ in 0..<20 {
                if app.isTerminated || !sent { break }
                try? await Task.sleep(nanoseconds: 250_000_000)
            }
            await refreshMemory()
            freeingMemory = false
            if app.isTerminated {
                memoryNote = "\(candidate.name) closed. RAM used: \(ByteFormat.string(from: Int64(before))) → \(ByteFormat.string(from: Int64(memory.usedBytes))). Other apps also affect this reading."
            } else {
                memoryNote = "\(candidate.name) is still open. Save your work or respond to its quit dialog. No forced quit was attempted."
            }
        }
    }

    func cleanNow() {
        guard !freeingMemory, phase == .idle || phase == .completed || phase == .failed else { return }
        if !permissionsAcknowledged {
            openAccessGuide(forClean: true)
            return
        }
        pendingReview = false
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
        let fromReview = pendingReview
        pendingReview = false
        runTask = Task { await run(mode: .manual, fromReview: fromReview) }
    }

    func cleanFromReview() {
        guard !freeingMemory, phase == .idle || phase == .completed else { return }
        showDetails = false
        if !permissionsAcknowledged {
            openAccessGuide(forClean: true)
            return
        }
        pendingReview = true
        runTask = Task {
            if await browsersNeedQuitPrompt() {
                browserPrompt = "Safari or Chrome is open. Quit it normally so the cache can be cleaned? CleanSlate will not force the browser to quit."
                return
            }
            pendingReview = false
            await run(mode: .manual, fromReview: true)
        }
    }

    func cancel() {
        cancelRequested = true
        runTask?.cancel()
        userHeldOpen = true
    }

    func holdOpen() {
        userHeldOpen = true
    }

    func refreshOverview() async {
        let snapshot = await Task.detached(priority: .utility) {
            HealthSnapshot(memory: MemoryInfoService.snapshot(), disk: DiskHealthService.snapshot(),
                battery: BatteryInfoService.snapshot())
        }.value
        health = snapshot
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
        let request = makeRequest(mode: mode, emptyTrashConfirmed: mode == .manual, allowBrowserQuit: allowQuit)
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
        var categories = mode == .login ? preferences.categories.forLogin() : preferences.categories
        categories.emptyTrash = mode == .manual
        return CleanRequest(
            mode: mode,
            dryRun: preferences.dryRun,
            categories: categories,
            exclusions: preferences.excludedPaths,
            allowBrowserQuit: allowBrowserQuit,
            emptyTrashConfirmed: emptyTrashConfirmed
        )
    }

    private func apply(_ outcome: CleanRunResult) {
        func detail(_ ids: [String], idle: String) -> String {
            let matched = outcome.modules.filter { ids.contains($0.estimate.id) || ids.contains("user-cache") && $0.estimate.id.hasPrefix("user-cache-") }
            guard !matched.isEmpty else { return idle }
            let blocked = matched.filter {
                if case .unavailable = $0.cleanup.status { return true }
                return false
            }
            let bytes = matched.reduce(Int64(0)) { $0 + $1.cleanup.deletedBytes }
            if !blocked.isEmpty {
                return (bytes > 0 ? "\(ByteFormat.string(from: bytes)) cleaned · " : "") + "\(blocked.count) skipped — click for access guide"
            }
            return bytes > 0 ? ByteFormat.string(from: bytes) : "Nothing to remove"
        }
        func needsAccess(_ ids: [String]) -> Bool {
            outcome.modules.contains { module in
                guard ids.contains(module.estimate.id) || ids.contains("user-cache") && module.estimate.id.hasPrefix("user-cache-") else { return false }
                if case .unavailable = module.cleanup.status { return true }
                return false
            }
        }
        rows = [
            HomeRow(id: "junk", title: "SYSTEM JUNK", detail: detail(["temp", "user-cache", "loose-cache", "diagnostics", "rotated-logs", "trash"], idle: "Ready"), needsAttention: needsAccess(["temp", "user-cache", "loose-cache", "diagnostics", "rotated-logs", "trash"])),
            HomeRow(id: "safari", title: "SAFARI", detail: detail(["safari"], idle: "Ready"), needsAttention: needsAccess(["safari"])),
            HomeRow(id: "chrome", title: "CHROME", detail: detail(["chrome"], idle: "Ready"), needsAttention: needsAccess(["chrome"]))
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
        HomeRow(id: "chrome", title: "CHROME", detail: "Ready")
    ]
}

enum LoginLaunchDetection {
    static func current() -> Bool {
        guard let event = NSAppleEventManager.shared().currentAppleEvent else { return false }
        guard event.eventID == kAEOpenApplication else { return false }
        return event.paramDescriptor(forKeyword: keyAELaunchedAsLogInItem)?.booleanValue == true
    }
}

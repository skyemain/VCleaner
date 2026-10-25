import Foundation

protocol ApplicationControlling: Sendable {
    func isRunning(bundleIdentifier: String) async -> Bool
    /// Asks the application to quit normally. Returns true when it has exited. Must not force-quit.
    func requestQuit(bundleIdentifier: String) async -> Bool
}

struct IdleApplications: ApplicationControlling {
    func isRunning(bundleIdentifier: String) async -> Bool { false }
    func requestQuit(bundleIdentifier: String) async -> Bool { true }
}

actor CleaningEngine {
    private let locations: Locations
    private let clock: @Sendable () -> Date
    private let applications: any ApplicationControlling
    private var running = false

    init(locations: Locations, clock: @escaping @Sendable () -> Date = { Date() }, applications: any ApplicationControlling = IdleApplications()) {
        self.locations = locations
        self.clock = clock
        self.applications = applications
    }

    func scan(_ request: CleanRequest) async throws -> [ModuleEstimate] {
        try begin()
        defer { running = false }
        return await estimates(for: request)
    }

    func clean(_ request: CleanRequest) async throws -> CleanRunResult {
        try begin()
        defer { running = false }
        let started = clock()
        var request = request
        if request.mode == .login {
            request.categories = request.categories.forLogin()
            request.allowBrowserQuit = false
            request.emptyTrashConfirmed = false
        }
        let found = await estimates(for: request)
        var reports: [ModuleCleanupReport] = []
        var cancelled = false
        let cancel = { cancelled }

        for estimate in found {
            if Task.isCancelled { cancelled = true }
            let cleanup: ModuleCleanup
            if cancelled {
                cleanup = ModuleCleanup(deletedCount: 0, deletedBytes: 0, skippedCount: 0, errors: ["Cancelled."], status: .skipped("Cancelled"))
            } else {
                cleanup = await perform(estimate: estimate, request: request, cancel: cancel)
            }
            reports.append(ModuleCleanupReport(estimate: estimate, cleanup: cleanup))
        }

        let finished = clock()
        let record = SessionRecord(
            id: UUID(),
            startedAt: started,
            finishedAt: finished,
            mode: request.mode.rawValue,
            dryRun: request.dryRun,
            modules: reports.map(\.estimate.id),
            discoveredCount: found.reduce(0) { $0 + $1.fileCount },
            discoveredBytes: found.reduce(0) { $0 + $1.byteCount },
            deletedCount: reports.reduce(0) { $0 + $1.cleanup.deletedCount },
            deletedBytes: reports.reduce(0) { $0 + $1.cleanup.deletedBytes },
            skippedCount: reports.reduce(0) { $0 + $1.cleanup.skippedCount },
            errors: reports.flatMap(\.cleanup.errors).prefix(50).map { $0 },
            duration: finished.timeIntervalSince(started)
        )
        return CleanRunResult(modules: reports, record: record)
    }

    private func begin() throws {
        if running { throw CleanEngineError.alreadyRunning }
        running = true
    }

    private func context(_ request: CleanRequest) -> ScanContext {
        ScanContext(locations: locations, now: clock(), exclusions: request.exclusions, dryRun: request.dryRun, mode: request.mode)
    }

    private func estimates(for request: CleanRequest) async -> [ModuleEstimate] {
        let context = context(request)
        var modules: [ModuleEstimate] = []
        let categories = request.mode == .login ? request.categories.forLogin() : request.categories
        if categories.temporaryFiles {
            modules.append(SystemCleaners.temporaryFiles(context))
        }
        if categories.userCaches {
            modules.append(contentsOf: SystemCleaners.userCacheEstimates(context))
            modules.append(SystemCleaners.looseCacheFiles(context))
        }
        if categories.diagnosticReports {
            modules.append(SystemCleaners.diagnosticReports(context))
            modules.append(SystemCleaners.rotatedLogs(context))
        }
        modules.append(SystemCleaners.thumbnails())
        if categories.safari {
            modules.append(await safariEstimate(context, allowQuit: request.allowBrowserQuit && request.mode == .manual))
        }
        if categories.chrome {
            modules.append(await browserEstimate(KnownBrowsers.chrome, context: context, allowQuit: request.allowBrowserQuit && request.mode == .manual))
        }
        if categories.xcodeDerivedData && request.mode == .manual {
            modules.append(XcodeCleaner.estimate(context))
        }
        if categories.emptyTrash && request.mode == .manual {
            modules.append(TrashInspector.estimate(context))
        }
        return modules
    }

    private func safariEstimate(_ context: ScanContext, allowQuit: Bool) async -> ModuleEstimate {
        var estimate = SafariCleaner.estimate(context)
        if await applications.isRunning(bundleIdentifier: SafariCleaner.bundleIdentifier), !allowQuit {
            estimate = ModuleEstimate(
                id: estimate.id,
                title: estimate.title,
                status: .skipped("Safari is running. Automatic cleaning does not quit browsers."),
                fileCount: estimate.fileCount,
                byteCount: estimate.byteCount,
                samplePaths: estimate.samplePaths
            )
        }
        return estimate
    }

    private func cleanSafari(context: ScanContext, request: CleanRequest, cancel: @escaping () -> Bool) async -> ModuleCleanup {
        if await applications.isRunning(bundleIdentifier: SafariCleaner.bundleIdentifier) {
            let mayQuit = request.allowBrowserQuit && request.mode == .manual
            var didQuit = false
            if mayQuit {
                didQuit = await applications.requestQuit(bundleIdentifier: SafariCleaner.bundleIdentifier)
            }
            if !didQuit {
                return ModuleCleanup(
                    deletedCount: 0,
                    deletedBytes: 0,
                    skippedCount: 0,
                    errors: [],
                    status: .skipped("Safari is still running, so its cache was left alone.")
                )
            }
        }
        return SafariCleaner.clean(context, cancel: cancel)
    }

    private func browserEstimate(_ browser: BrowserDefinition, context: ScanContext, allowQuit: Bool) async -> ModuleEstimate {
        var estimate = ChromiumCacheCleaner.estimate(browser, context: context)
        if await applications.isRunning(bundleIdentifier: browser.bundleIdentifier) {
            if !allowQuit {
                estimate = ModuleEstimate(
                    id: estimate.id,
                    title: estimate.title,
                    status: .skipped("\(browser.title) is running. Automatic cleaning does not quit browsers."),
                    fileCount: estimate.fileCount,
                    byteCount: estimate.byteCount,
                    samplePaths: estimate.samplePaths
                )
            }
        }
        return estimate
    }

    private func perform(estimate: ModuleEstimate, request: CleanRequest, cancel: @escaping () -> Bool) async -> ModuleCleanup {
        switch estimate.status {
        case .unsupported(let reason):
            return ModuleCleanup(deletedCount: 0, deletedBytes: 0, skippedCount: 0, errors: [], status: .unsupported(reason))
        case .unavailable(let reason):
            return ModuleCleanup(deletedCount: 0, deletedBytes: 0, skippedCount: 0, errors: [], status: .unavailable(reason))
        case .skipped(let reason):
            return ModuleCleanup(deletedCount: 0, deletedBytes: 0, skippedCount: estimate.fileCount, errors: [], status: .skipped(reason))
        case .ready:
            break
        }
        let context = context(request)
        if estimate.id.hasPrefix("user-cache-") {
            return SystemCleaners.cleanUserCache(estimate.id, context: context, cancel: cancel)
        }
        switch estimate.id {
        case "temp":
            return SystemCleaners.cleanTemporary(context, cancel: cancel)
        case "user-cache":
            return SystemCleaners.cleanHelpCaches(context, cancel: cancel)
        case "loose-cache":
            return SystemCleaners.cleanLooseCaches(context, cancel: cancel)
        case "rotated-logs":
            return SystemCleaners.cleanRotatedLogs(context, cancel: cancel)
        case "diagnostics":
            return SystemCleaners.cleanDiagnostics(context, cancel: cancel)
        case "safari":
            return await cleanSafari(context: context, request: request, cancel: cancel)
        case "chrome":
            return await cleanBrowser(KnownBrowsers.chrome, context: context, request: request, cancel: cancel, cleaner: ChromeCleaner.clean)
        case "xcode":
            guard request.mode == .manual else {
                return ModuleCleanup(deletedCount: 0, deletedBytes: 0, skippedCount: 0, errors: [], status: .skipped("Xcode cleanup does not run at login."))
            }
            return XcodeCleaner.clean(context, cancel: cancel)
        case "trash":
            guard request.mode == .manual, request.emptyTrashConfirmed else {
                return ModuleCleanup(deletedCount: 0, deletedBytes: 0, skippedCount: estimate.fileCount, errors: [], status: .skipped("Trash is emptied only after a separate confirmation."))
            }
            return TrashInspector.empty(context, cancel: cancel)
        default:
            return ModuleCleanup(deletedCount: 0, deletedBytes: 0, skippedCount: 0, errors: [], status: .unsupported("No cleaner is registered for \(estimate.id)."))
        }
    }

    private func cleanBrowser(
        _ browser: BrowserDefinition,
        context: ScanContext,
        request: CleanRequest,
        cancel: @escaping () -> Bool,
        cleaner: (ScanContext, @escaping () -> Bool) -> ModuleCleanup
    ) async -> ModuleCleanup {
        if await applications.isRunning(bundleIdentifier: browser.bundleIdentifier) {
            let mayQuit = request.allowBrowserQuit && request.mode == .manual
            var didQuit = false
            if mayQuit {
                didQuit = await applications.requestQuit(bundleIdentifier: browser.bundleIdentifier)
            }
            if !didQuit {
                return ModuleCleanup(
                    deletedCount: 0,
                    deletedBytes: 0,
                    skippedCount: 0,
                    errors: [],
                    status: .skipped("\(browser.title) is still running, so its cache was left alone.")
                )
            }
        }
        return cleaner(context, cancel)
    }
}

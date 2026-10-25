import Foundation

struct BrowserDefinition: Sendable, Equatable {
    var id: String
    var title: String
    var bundleIdentifier: String
    var supportRelativePath: String
    var cacheRelativePath: String
}

enum KnownBrowsers {
    static let chrome = BrowserDefinition(
        id: "chrome",
        title: "CHROME",
        bundleIdentifier: "com.google.Chrome",
        supportRelativePath: "Library/Application Support/Google/Chrome",
        cacheRelativePath: "Library/Caches/Google/Chrome"
    )
    static let cacheDirectoryNames: Set<String> = [
        "Cache", "Code Cache", "GPUCache", "DawnGraphiteCache", "DawnWebGPUCache", "ShaderCache", "GrShaderCache"
    ]

    static let ignoredProfileNames: Set<String> = [
        "System Profile", "Crashpad", "ShaderCache", "GrShaderCache", "GraphiteDawnCache",
        "Safe Browsing", "BrowserMetrics", "Crowd Deny", "CertificateRevocation", "OptimizationHints"
    ]
}

enum ChromiumCacheCleaner {
    static func estimate(_ browser: BrowserDefinition, context: ScanContext) -> ModuleEstimate {
        let roots = cacheRoots(browser, home: context.locations.home)
        if roots.contains(where: { DirectoryWalker.isUnreadable($0) }) {
            return ModuleEstimate(id: browser.id, title: browser.title, status: .unavailable("macOS access restrictions"), fileCount: 0, byteCount: 0, samplePaths: [])
        }
        var count = 0
        var bytes: Int64 = 0
        var sample: [String] = []
        for root in roots {
            let stats = DirectoryWalker.collect(root: root, minimumAge: nil, exclusions: context.exclusions, fileManager: FileManager())
            count += stats.fileCount
            bytes += stats.byteCount
            for path in stats.samplePaths where sample.count < 20 {
                sample.append(path)
            }
        }
        return ModuleEstimate(id: browser.id, title: browser.title, status: .ready, fileCount: count, byteCount: bytes, samplePaths: sample)
    }

    static func clean(_ browser: BrowserDefinition, context: ScanContext, cancel: @escaping () -> Bool) -> ModuleCleanup {
        var cleanup = ModuleCleanup(deletedCount: 0, deletedBytes: 0, skippedCount: 0, errors: [], status: .ready)
        for root in cacheRoots(browser, home: context.locations.home) {
            if cancel() {
                cleanup.errors.append("Cancelled.")
                break
            }
            let outcome = SafeDeletionService().deleteEligibleFiles(under: root, context: context, minimumAge: nil, shouldCancel: cancel)
            cleanup.deletedCount += outcome.deletedCount
            cleanup.deletedBytes += outcome.deletedBytes
            cleanup.skippedCount += outcome.skippedCount
            cleanup.errors.append(contentsOf: outcome.errors.prefix(20))
        }
        return cleanup
    }

    /// Profile cache directories only. Cookies, history, passwords, and bookmarks live elsewhere and are never roots.
    static func cacheRoots(_ browser: BrowserDefinition, home: URL) -> [URL] {
        let fm = FileManager()
        var roots: [URL] = []
        for relative in [browser.supportRelativePath, browser.cacheRelativePath] {
            let base = home.appendingPathComponent(relative)
            guard let profiles = try? fm.contentsOfDirectory(at: base, includingPropertiesForKeys: [.isDirectoryKey], options: [.skipsHiddenFiles]) else {
                continue
            }
            for profile in profiles {
                let name = profile.lastPathComponent
                guard !KnownBrowsers.ignoredProfileNames.contains(name) else { continue }
                var isDirectory: ObjCBool = false
                guard fm.fileExists(atPath: profile.path, isDirectory: &isDirectory), isDirectory.boolValue else { continue }
                let looksLikeProfile = name == "Default" || name == "Guest Profile" || name.hasPrefix("Profile ")
                    || fm.fileExists(atPath: profile.appendingPathComponent("Preferences").path)
                guard looksLikeProfile else { continue }
                for cacheName in KnownBrowsers.cacheDirectoryNames.sorted() {
                    let cache = profile.appendingPathComponent(cacheName)
                    var cacheIsDirectory: ObjCBool = false
                    if fm.fileExists(atPath: cache.path, isDirectory: &cacheIsDirectory), cacheIsDirectory.boolValue {
                        roots.append(cache)
                    }
                }
            }
        }
        return roots
    }
}

import Foundation

enum EdgeCleaner {
    static func estimate(_ context: ScanContext) -> ModuleEstimate {
        ChromiumCacheCleaner.estimate(KnownBrowsers.edge, context: context)
    }

    static func clean(_ context: ScanContext, cancel: @escaping () -> Bool) -> ModuleCleanup {
        ChromiumCacheCleaner.clean(KnownBrowsers.edge, context: context, cancel: cancel)
    }
}

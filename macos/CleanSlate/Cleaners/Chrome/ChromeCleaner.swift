import Foundation

enum ChromeCleaner {
    static func estimate(_ context: ScanContext) -> ModuleEstimate {
        ChromiumCacheCleaner.estimate(KnownBrowsers.chrome, context: context)
    }

    static func clean(_ context: ScanContext, cancel: @escaping () -> Bool) -> ModuleCleanup {
        ChromiumCacheCleaner.clean(KnownBrowsers.chrome, context: context, cancel: cancel)
    }
}

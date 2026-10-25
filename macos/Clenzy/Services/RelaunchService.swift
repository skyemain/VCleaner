import AppKit
import Foundation

struct RelaunchPlan {
    let bundleURL: URL
    let pid: Int32

    static let script = """
    attempts=0
    while /bin/kill -0 "$1" 2>/dev/null; do
      attempts=$((attempts + 1))
      if [ "$attempts" -ge 120 ]; then exit 1; fi
      /bin/sleep 0.25
    done
    exec "$3" -n "$2" --args --after-access-restart
    """

    func arguments(opener: String = "/usr/bin/open") -> [String] {
        ["-c", Self.script, "clenzy-relaunch", String(pid), bundleURL.path, opener]
    }
}

@MainActor
enum RelaunchService {
    static func restart() throws {
        let bundle = Bundle.main.bundleURL
        guard bundle.pathExtension == "app", FileManager.default.isExecutableFile(atPath: Bundle.main.executablePath ?? "") else {
            throw CocoaError(.executableNotLoadable)
        }
        let helper = Process()
        helper.executableURL = URL(fileURLWithPath: "/bin/sh")
        helper.arguments = RelaunchPlan(bundleURL: bundle, pid: getpid()).arguments()
        helper.standardInput = FileHandle.nullDevice
        helper.standardOutput = FileHandle.nullDevice
        helper.standardError = FileHandle.nullDevice
        try helper.run()
        NSApp.terminate(nil)
    }
}

@MainActor
final class AppLifecycleDelegate: NSObject, NSApplicationDelegate {
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        .terminateNow
    }
}

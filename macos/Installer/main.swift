import AppKit

/// A distinct launcher prevents Finder from activating an already-running old copy.
@main
struct InstallerLauncher {
    @MainActor static func main() {
        let application = NSApplication.shared
        application.setActivationPolicy(.accessory)
        Task {
            let source = Bundle.main.bundleURL.deletingLastPathComponent().appendingPathComponent("CleanSlate.app")
            guard Bundle(url: source)?.bundleIdentifier == "com.skyemain.CleanSlate" else {
                showError("Keep Install CleanSlate beside CleanSlate.app inside the downloaded disk image.")
                application.terminate(nil)
                return
            }
            let configuration = NSWorkspace.OpenConfiguration()
            configuration.createsNewApplicationInstance = true
            do {
                _ = try await NSWorkspace.shared.openApplication(at: source, configuration: configuration)
            } catch {
                showError(error.localizedDescription)
            }
            application.terminate(nil)
        }
        application.run()
    }

    @MainActor private static func showError(_ message: String) {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = "Could not open the CleanSlate installer"
        alert.informativeText = message
        alert.runModal()
    }
}

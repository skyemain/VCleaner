import AppKit
import Foundation

@main
struct LifecycleSmokeTest {
    @MainActor static func main() {
        let app = NSApplication.shared
        let delegate = AppLifecycleDelegate()
        app.delegate = delegate
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 320, height: 200), styleMask: [.titled], backing: .buffered, defer: false)
        let sheet = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 280, height: 140), styleMask: [.titled], backing: .buffered, defer: false)
        sheet.contentView = QuitFriendlyWindowView()
        precondition(!sheet.preventsApplicationTerminationWhenModal)
        DispatchQueue.main.async {
            window.orderFront(nil)
            window.beginSheet(sheet)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                precondition(window.attachedSheet === sheet)
                app.terminate(nil)
            }
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 5) {
            fputs("FAIL: modal sheet blocked application termination\n", stderr)
            exit(2)
        }
        withExtendedLifetime(delegate) { app.run() }
    }
}

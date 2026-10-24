import AppKit
import Foundation
import ServiceManagement

enum LoginItemState: Equatable, Sendable {
    case enabled
    case notRegistered
    case requiresApproval
    case unavailable(String)
}

protocol LoginItemRegistering: Sendable {
    func status() -> LoginItemState
    func setEnabled(_ enabled: Bool) throws
}

struct ServiceManagementLoginItems: LoginItemRegistering {
    func status() -> LoginItemState {
        switch SMAppService.mainApp.status {
        case .enabled: return .enabled
        case .notRegistered: return .notRegistered
        case .requiresApproval: return .requiresApproval
        case .notFound: return .unavailable("The login item could not be found.")
        @unknown default: return .unavailable("macOS returned an unknown login-item status.")
        }
    }

    func setEnabled(_ enabled: Bool) throws {
        if enabled {
            try SMAppService.mainApp.register()
        } else {
            try SMAppService.mainApp.unregister()
        }
    }
}

struct MacApplicationController: ApplicationControlling {
    func isRunning(bundleIdentifier: String) async -> Bool {
        let identifier = bundleIdentifier
        return await MainActor.run {
            NSWorkspace.shared.runningApplications.contains { $0.bundleIdentifier == identifier }
        }
    }

    func requestQuit(bundleIdentifier: String) async -> Bool {
        let identifier = bundleIdentifier
        let asked = await MainActor.run { () -> Bool in
            let matches = NSWorkspace.shared.runningApplications.filter { $0.bundleIdentifier == identifier }
            guard !matches.isEmpty else { return true }
            matches.forEach { $0.terminate() }
            return false
        }
        if asked { return true }
        for _ in 0..<16 {
            try? await Task.sleep(nanoseconds: 500_000_000)
            if await isRunning(bundleIdentifier: identifier) == false {
                return true
            }
        }
        return false
    }
}

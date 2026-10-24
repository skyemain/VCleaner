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
    /// `SMAppService` talks to `smd` with a synchronous XPC call. Doing that on the main
    /// thread deadlocks: the reply needs the main thread, and the main thread is waiting
    /// for the reply. Real login-item calls must hop off the main thread first.
    var mustLeaveMainThread: Bool { get }
    func status() -> LoginItemState
    func setEnabled(_ enabled: Bool) throws
}

extension LoginItemRegistering {
    var mustLeaveMainThread: Bool { false }
}

struct ServiceManagementLoginItems: LoginItemRegistering {
    var mustLeaveMainThread: Bool { true }

    func status() -> LoginItemState {
        LoginItemGate.shared.run { Self.readStatus() }
            ?? .unavailable("macOS did not answer the login-item request. Cleaning still works.")
    }

    func setEnabled(_ enabled: Bool) throws {
        let attempt = LoginItemGate.shared.run { () -> LoginAttempt in
            do {
                if enabled {
                    try SMAppService.mainApp.register()
                } else {
                    try SMAppService.mainApp.unregister()
                }
                return .ok
            } catch {
                return .failed
            }
        }
        switch attempt {
        case .ok:
            return
        case .failed, .none:
            throw LoginItemError.refused
        }
    }

    private static func readStatus() -> LoginItemState {
        switch SMAppService.mainApp.status {
        case .enabled: return .enabled
        case .notRegistered: return .notRegistered
        case .requiresApproval: return .requiresApproval
        case .notFound: return .unavailable("The login item could not be found.")
        @unknown default: return .unavailable("macOS returned an unknown login-item status.")
        }
    }
}

enum LoginItemError: Error {
    case refused
}

private enum LoginAttempt: Sendable {
    case ok
    case failed
}

/// Serializes `SMAppService` calls onto a background queue and gives up if `smd` never answers.
/// A stuck call must not be waited on from the main thread, and a second call must not pile on
/// while the first one is still inside the synchronous XPC.
private final class LoginItemGate: @unchecked Sendable {
    static let shared = LoginItemGate()
    private let lock = NSLock()
    private var busy = false

    func run<T: Sendable>(_ work: @escaping @Sendable () -> T) -> T? {
        if Thread.isMainThread {
            return nil
        }
        lock.lock()
        if busy {
            lock.unlock()
            return nil
        }
        busy = true
        lock.unlock()

        let box = ResultBox<T>()
        let waiter = Waiter()
        DispatchQueue.global(qos: .utility).async {
            box.value = work()
            waiter.signal()
            self.lock.lock()
            self.busy = false
            self.lock.unlock()
        }
        guard waiter.wait(seconds: 4) else {
            return nil
        }
        return box.value
    }
}

private final class Waiter: @unchecked Sendable {
    private let semaphore = DispatchSemaphore(value: 0)
    func signal() { semaphore.signal() }
    func wait(seconds: Double) -> Bool {
        semaphore.wait(timeout: .now() + seconds) == .success
    }
}

private final class ResultBox<T>: @unchecked Sendable {
    var value: T?
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

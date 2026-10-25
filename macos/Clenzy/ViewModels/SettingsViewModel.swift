import Combine
import Foundation

@MainActor
final class SettingsViewModel: ObservableObject {
    @Published var preferences: CleaningPreferences
    @Published var message = ""
    @Published var loginState: LoginItemState = .notRegistered

    private let store: any PreferenceStoring
    private let loginItems: any LoginItemRegistering

    init(store: any PreferenceStoring = JSONPreferenceStore.applicationSupport(), loginItems: any LoginItemRegistering = ServiceManagementLoginItems()) {
        self.store = store
        self.loginItems = loginItems
        preferences = store.load()
        if loginItems.mustLeaveMainThread {
            refreshLoginStatus()
        } else {
            loginState = loginItems.status()
        }
    }

    func update(_ body: (inout CleaningPreferences) -> Void) {
        let previousLogin = preferences.startAtLogin
        body(&preferences)
        if previousLogin && !preferences.startAtLogin {
            preferences.cleanAtLogin = false
        }
        if preferences.cleanAtLogin {
            preferences.startAtLogin = true
        }
        if !preferences.startAtLogin {
            preferences.cleanAtLogin = false
        }
        let loginChanged = preferences.startAtLogin != previousLogin
        persist()
        if loginChanged {
            applyLoginRegistration()
        }
    }

    /// Menu bar insertion writes this binding during ordinary window updates, including a click
    /// on Clean. It must only store the flag. Calling `SMAppService` from that path freezes the app.
    func setMenuBarVisible(_ visible: Bool) {
        guard preferences.showMenuBarIcon != visible else { return }
        preferences.showMenuBarIcon = visible
        persist()
    }

    func addExclusion(_ path: String) {
        let trimmed = path.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, !preferences.excludedPaths.contains(trimmed) else { return }
        preferences.excludedPaths.append(trimmed)
        persist()
    }

    func removeExclusion(_ path: String) {
        preferences.excludedPaths.removeAll { $0 == path }
        persist()
    }

    func reset() {
        preferences = CleaningPreferences()
        persist()
        applyLoginRegistration()
        message = "Settings were reset."
    }

    private func persist() {
        do {
            try store.save(preferences)
            message = ""
        } catch {
            message = "Settings could not be saved."
        }
    }

    private var loginGeneration = 0

    private func refreshLoginStatus() {
        let items = loginItems
        loginGeneration += 1
        let generation = loginGeneration
        Task.detached { [weak self] in
            let state = items.status()
            await self?.apply(LoginItemReconciler.Outcome(state: state, message: nil), generation: generation)
        }
    }

    private func applyLoginRegistration() {
        let desired = preferences.startAtLogin
        let items = loginItems
        if !items.mustLeaveMainThread {
            apply(LoginItemReconciler.reconcile(desired: desired, loginItems: items), generation: loginGeneration)
            return
        }
        loginGeneration += 1
        let generation = loginGeneration
        Task.detached { [weak self] in
            let outcome = LoginItemReconciler.reconcile(desired: desired, loginItems: items)
            await self?.apply(outcome, generation: generation)
        }
    }

    private func apply(_ outcome: LoginItemReconciler.Outcome, generation: Int) {
        guard generation == loginGeneration else { return }
        loginState = outcome.state
        if let message = outcome.message {
            self.message = message
        }
    }
}

enum LoginItemReconciler {
    struct Outcome: Sendable {
        var state: LoginItemState
        var message: String?
    }

    static func reconcile(desired: Bool, loginItems: any LoginItemRegistering) -> Outcome {
        let current = loginItems.status()
        if desired && current == .enabled {
            return Outcome(state: current, message: nil)
        }
        if !desired && current == .notRegistered {
            return Outcome(state: current, message: nil)
        }
        if case .unavailable(let reason) = current {
            return Outcome(state: current, message: reason)
        }
        do {
            try loginItems.setEnabled(desired)
            let state = loginItems.status()
            if state == .requiresApproval {
                return Outcome(state: state, message: "Allow Clenzy in System Settings > General > Login Items.")
            }
            return Outcome(state: state, message: nil)
        } catch {
            let state = loginItems.status()
            return Outcome(state: state, message: "macOS refused the login item change. Cleaning still works.")
        }
    }
}

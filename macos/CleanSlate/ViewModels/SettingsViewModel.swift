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
        loginState = loginItems.status()
    }

    func update(_ body: (inout CleaningPreferences) -> Void) {
        body(&preferences)
        if preferences.cleanAtLogin {
            preferences.startAtLogin = true
        }
        if !preferences.startAtLogin {
            preferences.cleanAtLogin = false
        }
        persist()
        applyLoginRegistration()
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

    private func applyLoginRegistration() {
        let desired = preferences.startAtLogin
        let current = loginItems.status()
        if desired && current == .enabled {
            loginState = current
            return
        }
        if !desired && current == .notRegistered {
            loginState = current
            return
        }
        do {
            try loginItems.setEnabled(desired)
            loginState = loginItems.status()
            if loginState == .requiresApproval {
                message = "Allow CleanSlate in System Settings > General > Login Items."
            }
        } catch {
            loginState = loginItems.status()
            message = "macOS refused the login item change."
        }
    }
}

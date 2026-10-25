import SwiftUI

struct SettingsView: View {
    @EnvironmentObject private var settings: SettingsViewModel
    @State private var exclusion = ""

    var body: some View {
        Form {
            Section("General") {
                Toggle("Start at login", isOn: binding(\.startAtLogin))
                Toggle("Automatically clean at login", isOn: binding(\.cleanAtLogin))
                Toggle("Show login countdown", isOn: binding(\.showLoginCountdown))
                Toggle("Show completion result", isOn: binding(\.showCompletionResult))
                Toggle("Close the window after an automatic clean", isOn: binding(\.closeAfterAutoClean))
                Picker("Theme", selection: binding(\.theme)) {
                    Text("System").tag(AppTheme.system)
                    Text("Dark").tag(AppTheme.dark)
                    Text("Light").tag(AppTheme.light)
                }
                Toggle("Show menu bar icon", isOn: binding(\.showMenuBarIcon))
                Text(loginText).font(.caption).foregroundStyle(.secondary)
            }
            Section("Cleaning") {
                Toggle("User temporary files", isOn: binding(\.cleanTemporaryFiles))
                Toggle("Supported user caches", isOn: binding(\.cleanUserCaches))
                Text("System Junk removes temporary files older than 24 hours; Help Viewer, Spotlight suggestions, Tips and loose cache files older than 7 days; and diagnostic reports older than 30 days. Documents and application data stay.")
                    .font(.caption).foregroundStyle(.secondary)
                Toggle("Eligible diagnostic reports", isOn: binding(\.cleanDiagnosticReports))
                Toggle("Chrome safe cache", isOn: binding(\.cleanChrome))
                Toggle("Safari safe cache", isOn: binding(\.cleanSafari))
                Text("Safari cleaning removes the web cache, including the WebKit network cache. History, bookmarks, cookies, and passwords stay. If the Safari row says access is blocked, turn on Full Disk Access for CleanSlate.")
                    .font(.caption).foregroundStyle(.secondary)
                Toggle("Offer to empty Trash", isOn: binding(\.emptyTrash))
                Toggle("Xcode DerivedData", isOn: binding(\.cleanXcodeDerivedData))
                Toggle("Privacy history cleaning", isOn: .constant(false))
                    .disabled(true)
                Text("Browsing history stays. macOS does not provide a safe API that clears it without editing protected databases.")
                    .font(.caption).foregroundStyle(.secondary)
                Toggle("App leftover report", isOn: binding(\.appLeftoverScan))
                Text("The leftover report is analysis only. CleanSlate does not delete Application Support folders.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section("Advanced") {
                Toggle("Review before cleaning", isOn: binding(\.reviewBeforeCleaning))
                Toggle("Dry run", isOn: binding(\.dryRun))
                Text("Dry run changes nothing and reports what a clean would remove.")
                    .font(.caption).foregroundStyle(.secondary)
                HStack {
                    TextField("Folder to exclude", text: $exclusion)
                    Button("Add") {
                        settings.addExclusion(exclusion)
                        exclusion = ""
                    }
                }
                ForEach(settings.preferences.excludedPaths, id: \.self) { path in
                    HStack {
                        Text(path).lineLimit(1)
                        Spacer()
                        Button("Remove") { settings.removeExclusion(path) }
                    }
                }
                Button("Reset settings") { settings.reset() }
            }
            if !settings.message.isEmpty {
                Text(settings.message).foregroundStyle(.orange)
            }
        }
        .formStyle(.grouped)
        .frame(width: 520, height: 640)
    }

    private var loginText: String {
        switch settings.loginState {
        case .enabled: return "CleanSlate is registered to open at login."
        case .notRegistered: return "CleanSlate does not open at login."
        case .requiresApproval: return "macOS is waiting for you to allow CleanSlate in Login Items."
        case .unavailable(let reason): return reason
        }
    }

    private func binding<Value>(_ keyPath: WritableKeyPath<CleaningPreferences, Value>) -> Binding<Value> {
        Binding(
            get: { settings.preferences[keyPath: keyPath] },
            set: { newValue in settings.update { $0[keyPath: keyPath] = newValue } }
        )
    }
}

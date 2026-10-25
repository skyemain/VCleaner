import AppKit
import Combine
import SwiftUI

@MainActor
final class AppContainer: ObservableObject {
    let settings: SettingsViewModel
    let main: MainViewModel
    private var subscriptions = Set<AnyCancellable>()

    init(settings: SettingsViewModel, main: MainViewModel) {
        self.settings = settings
        self.main = main
        settings.objectWillChange.sink { [weak self] _ in
            self?.objectWillChange.send()
        }.store(in: &subscriptions)
        main.objectWillChange.sink { [weak self] _ in
            self?.objectWillChange.send()
        }.store(in: &subscriptions)
    }
}

@main
struct CleanSlateApp: App {
    @StateObject private var container: AppContainer

    init() {
        let settings = SettingsViewModel()
        let main = MainViewModel(settings: settings)
        _container = StateObject(wrappedValue: AppContainer(settings: settings, main: main))
    }

    var body: some Scene {
        WindowGroup {
            MainView(model: container.main)
                .environmentObject(container.settings)
                .frame(minWidth: 920, minHeight: 640)
                .preferredColorScheme(colorScheme)
        }
        .windowStyle(.hiddenTitleBar)
        .defaultSize(width: 1040, height: 800)
        .commands {
            CommandGroup(replacing: .newItem) {}
            CommandGroup(replacing: .appSettings) {
                Button("Settings…") { container.main.openSettings() }
                    .keyboardShortcut(",", modifiers: .command)
            }
        }

        MenuBarExtra("CleanSlate", systemImage: "circle.hexagongrid", isInserted: menuBarInserted) {
            Button("Open CleanSlate") {
                NSApp.activate(ignoringOtherApps: true)
            }
            Button("Quit") { NSApp.terminate(nil) }
        }
    }

    private var menuBarInserted: Binding<Bool> {
        Binding(
            get: { container.settings.preferences.showMenuBarIcon },
            set: { value in container.settings.setMenuBarVisible(value) }
        )
    }

    private var colorScheme: ColorScheme? {
        switch container.settings.preferences.theme {
        case .system: return nil
        case .dark: return .dark
        case .light: return .light
        }
    }
}

import SwiftUI

@main
struct ClenzyApp: App {
    @State private var home = HomeViewModel()
    var body: some Scene {
        WindowGroup { HomeView(model: home) }
    }
}

import SwiftUI

struct InstallationView: View {
    @ObservedObject var model: MainViewModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text("[ INSTALL CLEANSLATE ]")
                .font(.system(size: 13, weight: .semibold, design: .monospaced)).foregroundStyle(OpsTheme.mint)
            Text("One installed copy. A fresh start.").font(.title2.weight(.semibold))
            Text("Install this version in Applications. CleanSlate will quit older running copies, verify the new app, replace the existing installation and remove the old app bundles from Applications and your personal Applications folder. Your preferences and cleaning history stay.")
                .font(.system(size: 13)).foregroundStyle(OpsTheme.secondary)
            Text("After installation, the new copy opens Cleaning Access so you can grant and verify access to Trash and Safari. Replacing an app does not reset macOS privacy permissions.")
                .font(.system(size: 12)).foregroundStyle(OpsTheme.secondary)
            if !model.installationMessage.isEmpty {
                Text(model.installationMessage).font(.system(size: 12)).foregroundStyle(OpsTheme.amber).textSelection(.enabled)
            }
            HStack {
                Button("Later") { dismiss() }.disabled(model.installing)
                Spacer()
                Button(model.installing ? "Installing…" : "Install & replace older versions") { model.installApp() }
                    .buttonStyle(.borderedProminent).tint(OpsTheme.mintDeep).disabled(model.installing)
            }
        }
        .padding(28).frame(width: 540)
        .background(OpsTheme.background).foregroundStyle(OpsTheme.text)
        .interactiveDismissDisabled(model.installing)
    }
}

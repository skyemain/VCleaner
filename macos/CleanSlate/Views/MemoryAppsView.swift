import AppKit
import SwiftUI

struct MemoryAppsView: View {
    @ObservedObject var model: MainViewModel
    @Environment(\.dismiss) private var dismiss
    @State private var pending: AppMemoryUsage?

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text("[ MANAGE RAM ]").font(.system(size: 13, weight: .semibold, design: .monospaced))
                .foregroundStyle(OpsTheme.mint)
            Text("Release memory by closing an app you no longer need.").font(.headline)
            Text("macOS already manages cached memory. CleanSlate cannot safely erase memory belonging to an open app. Choose an app below to request a normal quit; it may ask you to save your work. Nothing is selected or closed automatically.")
                .font(.system(size: 12)).foregroundStyle(OpsTheme.secondary)
            ForEach(model.topMemoryApps) { app in
                HStack {
                    Text(app.name).lineLimit(1)
                    Spacer()
                    Text(app.percentText).monospacedDigit().foregroundStyle(OpsTheme.mint)
                    Button("Quit…") { pending = app }
                        .disabled(model.freeingMemory || app.pid == getpid())
                }.modifier(GlassCard())
            }
            if model.topMemoryApps.isEmpty {
                Text("No readable app memory samples yet. Refresh or open Activity Monitor.")
            }
            Text("Percentages are each app’s share of total physical RAM, including readable helpers. Protected processes may be omitted; shared memory and sampling can differ from Activity Monitor.")
                .font(.caption).foregroundStyle(OpsTheme.secondary)
            if !model.memoryNote.isEmpty {
                Text(model.memoryNote).font(.system(size: 12)).foregroundStyle(OpsTheme.mint)
            }
            HStack {
                Button("Refresh") { Task { await model.refreshMemory() } }
                    .disabled(model.freeingMemory)
                Button("Activity Monitor") {
                    NSWorkspace.shared.open(URL(fileURLWithPath: "/System/Applications/Utilities/Activity Monitor.app"))
                }
                Spacer()
                Button("Done") { dismiss() }
            }
        }
        .padding(24).frame(width: 610)
        .background(OpsTheme.background).foregroundStyle(OpsTheme.text)
        .alert("Quit \(pending?.name ?? "this app")?", isPresented: Binding(
            get: { pending != nil }, set: { if !$0 { pending = nil } }
        )) {
            Button("Quit app") {
                if let app = pending { model.quitMemoryApp(app) }
                pending = nil
            }
            Button("Cancel", role: .cancel) { pending = nil }
        } message: {
            Text("Save your work first. CleanSlate will ask this app to quit normally. It will never force it to close.")
        }
    }
}

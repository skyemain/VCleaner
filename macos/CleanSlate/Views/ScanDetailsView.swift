import SwiftUI

struct ScanDetailsView: View {
    @ObservedObject var model: MainViewModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("[ PREVIEW ]")
                .font(.system(size: 12, weight: .semibold, design: .monospaced))
                .foregroundStyle(OpsTheme.mint)
            Text("Estimated recoverable space. Cleaning re-checks every file before it is removed.")
                .foregroundStyle(OpsTheme.secondary)
                .font(.system(size: 12))
            List(model.estimates) { estimate in
                VStack(alignment: .leading, spacing: 4) {
                    HStack {
                        Text(estimate.title)
                            .font(.system(size: 13, weight: .semibold, design: .monospaced))
                        Spacer()
                        Text(status(estimate))
                            .font(.system(size: 12, design: .monospaced))
                            .foregroundStyle(OpsTheme.secondary)
                    }
                    Text("\(estimate.fileCount) files · \(ByteFormat.string(from: estimate.byteCount))")
                        .font(.system(size: 12))
                        .foregroundStyle(OpsTheme.secondary)
                }
                .padding(.vertical, 4)
            }
            .scrollContentBackground(.hidden)
            HStack {
                Button("Close") { dismiss() }
                Spacer()
                Button("Clean") {
                    dismiss()
                    model.cleanFromReview()
                }
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(24)
        .frame(minWidth: 560, minHeight: 420)
        .background(OpsTheme.background)
        .foregroundStyle(OpsTheme.text)
    }

    private func status(_ estimate: ModuleEstimate) -> String {
        switch estimate.status {
        case .ready: return estimate.fileCount == 0 ? "Nothing to remove" : "Ready"
        case .unsupported(let text), .unavailable(let text), .skipped(let text): return text
        }
    }
}

struct HealthView: View {
    @ObservedObject var model: MainViewModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("[ SYSTEM HEALTH ]")
                    .font(.system(size: 12, weight: .semibold, design: .monospaced))
                    .foregroundStyle(OpsTheme.mint)
                if let memory = model.health.memory {
                    Text("Memory \(ByteFormat.string(from: Int64(memory.usedBytes))) used of \(ByteFormat.string(from: Int64(memory.totalBytes))). Pressure: \(memory.pressure).")
                    Text(memory.explanation).foregroundStyle(OpsTheme.secondary)
                    if let swap = memory.swapUsedBytes {
                        Text("Swap in use: \(ByteFormat.string(from: Int64(swap)))").foregroundStyle(OpsTheme.secondary)
                    }
                }
                if let disk = model.health.disk {
                    Text("\(disk.volumeName) · \(disk.fileSystem) · \(ByteFormat.string(from: disk.availableBytes)) available of \(ByteFormat.string(from: disk.totalBytes)).")
                    Text("APFS snapshots and Time Machine backups are never deleted.").foregroundStyle(OpsTheme.secondary)
                }
                Text("Xcode archives \(ByteFormat.string(from: model.health.archiveBytes)) and simulators \(ByteFormat.string(from: model.health.simulatorBytes)) are shown for review only.")
                    .foregroundStyle(OpsTheme.secondary)
                if !model.health.largeDownloads.isEmpty {
                    Text("Large downloads, not deleted")
                        .font(.system(size: 12, weight: .semibold, design: .monospaced))
                    ForEach(model.health.largeDownloads, id: \.self) { Text($0).font(.system(size: 12)) }
                }
                if !model.health.agents.isEmpty {
                    Text("Login agents pointing at a missing program")
                        .font(.system(size: 12, weight: .semibold, design: .monospaced))
                    ForEach(model.health.agents) { agent in
                        HStack {
                            Text(agent.summary).font(.system(size: 12))
                            Spacer()
                            Button("Back up and disable") { model.quarantine(agent) }
                        }
                    }
                } else {
                    Text("No broken user login agents were found.").foregroundStyle(OpsTheme.secondary)
                }
                if !model.health.leftovers.isEmpty {
                    Text("Possible leftovers. Nothing in this list is deleted.")
                        .font(.system(size: 12, weight: .semibold, design: .monospaced))
                    ForEach(model.health.leftovers.prefix(30)) { item in
                        Text(item.note).font(.system(size: 12)).foregroundStyle(OpsTheme.secondary)
                    }
                }
                if let permissions = model.health.permissions {
                    Text(permissions.cachesReadable ? "User caches are readable." : "User caches are not readable.")
                    Text(permissions.fullDiskAccessNote).foregroundStyle(OpsTheme.secondary)
                }
                Button("Close") { dismiss() }
            }
            .padding(24)
        }
        .frame(minWidth: 620, minHeight: 480)
        .background(OpsTheme.background)
        .foregroundStyle(OpsTheme.text)
    }
}

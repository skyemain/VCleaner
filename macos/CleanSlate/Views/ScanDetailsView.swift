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

struct HomeMetricsView: View {
    @ObservedObject var model: MainViewModel
    var body: some View {
        HStack(spacing: 12) {
                metric(title: "STORAGE USED", icon: "internaldrive", value: model.health.disk.map { "\(Int(storageFraction($0) * 100))%" } ?? "—",
                    detail: model.health.disk.map { "\(ByteFormat.string(from: $0.availableBytes)) available of \(ByteFormat.string(from: $0.totalBytes))" } ?? "Unavailable",
                    fraction: model.health.disk.map(storageFraction))
                metric(title: "BATTERY TIME LEFT", icon: "battery.75percent", value: model.health.battery?.timeRemaining ?? "—",
                    detail: model.health.battery?.powerState ?? "Reading…", fraction: model.health.battery?.chargeFraction)
                metric(title: "BATTERY HEALTH", icon: "heart", value: model.health.battery?.healthFraction.map { "\(Int($0 * 100))%" } ?? model.health.battery?.health ?? "—",
                    detail: model.health.battery?.healthFraction != nil ? "Estimated maximum capacity" : "Reported by macOS",
                    fraction: model.health.battery?.healthFraction)
        }
        .task {
            while !Task.isCancelled {
                await model.refreshOverview()
                do { try await Task.sleep(nanoseconds: 5_000_000_000) } catch { break }
            }
        }
    }

    private func storageFraction(_ disk: DiskSnapshot) -> Double {
        disk.totalBytes > 0 ? min(1, max(0, Double(disk.usedBytes) / Double(disk.totalBytes))) : 0
    }

    private func metric(title: String, icon: String, value: String, detail: String, fraction: Double?) -> some View {
        VStack(alignment: .leading, spacing: 9) {
            HStack {
                Image(systemName: icon).foregroundStyle(OpsTheme.mint)
                Text(title).font(.system(size: 10, weight: .semibold, design: .monospaced))
                    .foregroundStyle(OpsTheme.secondary)
            }
            Text(value).font(.system(size: 24, weight: .semibold, design: .rounded))
                .lineLimit(1).minimumScaleFactor(0.65)
            if let fraction {
                ProgressView(value: fraction).tint(OpsTheme.mint)
            } else {
                RoundedRectangle(cornerRadius: 3).fill(OpsTheme.secondary.opacity(0.15)).frame(height: 4)
            }
            Text(detail).font(.system(size: 11)).foregroundStyle(OpsTheme.secondary)
                .lineLimit(2).frame(height: 28, alignment: .top)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(16).background(OpsTheme.card, in: RoundedRectangle(cornerRadius: 16))
        .overlay(RoundedRectangle(cornerRadius: 16).stroke(OpsTheme.cardStroke, lineWidth: 1))
    }
}

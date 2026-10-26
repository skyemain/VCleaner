import SwiftUI

struct StorageCard: View {
    let storage: DeviceStorage?
    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            CardHeading(title: "Storage", symbol: "internaldrive")
            if let storage {
                HStack(spacing: 24) {
                    MetricRing(fraction: storage.usedFraction, title: "\(Int((storage.usedFraction * 100).rounded()))%", subtitle: "used")
                        .frame(width: 130, height: 130)
                    VStack(alignment: .leading, spacing: 6) {
                        Text(format(storage.freeBytes)).font(.system(.largeTitle, design: .rounded, weight: .semibold)).minimumScaleFactor(0.55).lineLimit(1)
                        Text("free to make more memories").font(.subheadline).foregroundStyle(HomeTheme.muted).fixedSize(horizontal: false, vertical: true)
                    }
                }
                Divider().overlay(.white.opacity(0.04))
                HStack {
                    metric("Used", bytes: storage.usedBytes)
                    Spacer()
                    metric("Free", bytes: storage.freeBytes)
                    Spacer()
                    metric("Total", bytes: storage.totalBytes)
                }
            } else {
                Label("Storage unavailable", systemImage: "externaldrive.badge.questionmark")
                Text("Pull to refresh to try reading device storage again.").font(.footnote).foregroundStyle(HomeTheme.muted)
            }
        }.glassCard().accessibilityIdentifier("storageCard")
    }
    private func format(_ bytes: Int64) -> String { ByteCountFormatter.string(fromByteCount: bytes, countStyle: .decimal) }
    private func metric(_ title: String, bytes: Int64) -> some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(title).font(.caption).foregroundStyle(HomeTheme.muted)
            Text(format(bytes)).font(.system(.subheadline, design: .rounded, weight: .semibold)).lineLimit(1).minimumScaleFactor(0.65)
        }
    }
}

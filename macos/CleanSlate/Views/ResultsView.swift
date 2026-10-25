import SwiftUI

struct ResultsView: View {
    let result: CleanRunResult

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Image(systemName: "checkmark.circle.fill").foregroundStyle(OpsTheme.mint)
                Text(result.record.dryRun ? "Dry run" : "Cleaning complete")
                    .font(.system(size: 13, weight: .semibold))
                Spacer()
                Text(ByteFormat.string(from: result.record.deletedBytes))
                    .font(.system(size: 18, weight: .semibold, design: .rounded))
                    .foregroundStyle(OpsTheme.mint)
            }
            HStack(spacing: 12) {
                Text("\(result.record.deletedCount) files")
                Text("\(result.record.skippedCount) skipped")
                Text("\(result.record.errors.count) errors")
                Spacer(minLength: 0)
                Text(String(format: "%.1f s", result.record.duration))
            }
            .font(.system(size: 10, design: .monospaced)).foregroundStyle(OpsTheme.secondary)
        }
        .modifier(GlassCard())
        .accessibilityElement(children: .combine)
    }
}

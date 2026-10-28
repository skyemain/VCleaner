import SwiftUI

struct ResultsView: View {
    let result: CleanRunResult

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Image(systemName: (result.blockedModuleCount > 0 || !result.record.errors.isEmpty) ? "exclamationmark.circle.fill" : "checkmark.circle.fill")
                    .foregroundStyle((result.blockedModuleCount > 0 || !result.record.errors.isEmpty) ? OpsTheme.amber : OpsTheme.mint)
                Text(result.completionTitle)
                    .font(.system(size: 13, weight: .semibold))
                Spacer()
                Text(ByteFormat.string(from: result.record.deletedBytes))
                    .font(.system(size: 18, weight: .semibold, design: .rounded))
                    .foregroundStyle(OpsTheme.mint)
            }
            if result.blockedModuleCount > 0 {
                Text("\(result.blockedModuleCount) cleaning targets blocked · open Settings → Cleaning access")
                    .font(.system(size: 11)).foregroundStyle(OpsTheme.amber)
            }
            HStack(spacing: 12) {
                Text("\(result.record.deletedCount) files")
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

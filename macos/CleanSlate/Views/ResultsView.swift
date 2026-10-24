import SwiftUI

struct ResultsView: View {
    let result: CleanRunResult

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(result.record.dryRun ? "Dry run" : "Cleaning complete")
                .font(.system(size: 16, weight: .semibold, design: .rounded))
            labeled(result.record.dryRun ? "Would recover" : "Space recovered", ByteFormat.string(from: result.record.deletedBytes))
            labeled("Files cleaned", "\(result.record.deletedCount)")
            labeled("Files skipped", "\(result.record.skippedCount)")
            labeled("Errors", "\(result.record.errors.count)")
            labeled("Duration", String(format: "%.1f seconds", result.record.duration))
        }
        .frame(maxWidth: 280, alignment: .leading)
        .modifier(GlassCard())
        .accessibilityElement(children: .combine)
    }

    private func labeled(_ title: String, _ value: String) -> some View {
        HStack {
            Text(title).foregroundStyle(OpsTheme.secondary)
            Spacer()
            Text(value).font(.system(size: 12, design: .monospaced))
        }
        .font(.system(size: 12))
    }
}

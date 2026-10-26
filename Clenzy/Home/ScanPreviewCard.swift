import SwiftUI

struct ScanPreviewCard: View {
    let progress: Double
    let caption: String
    let reduceMotion: Bool
    let cancel: () -> Void
    var body: some View {
        VStack(spacing: 18) {
            ZStack {
                ForEach(0..<3) { index in Circle().stroke(HomeTheme.mint.opacity(0.07), lineWidth: 1).padding(CGFloat(index) * 18) }
                Circle().trim(from: 0, to: progress).stroke(HomeTheme.mint, style: StrokeStyle(lineWidth: 3, lineCap: .round)).rotationEffect(.degrees(-90))
                if !reduceMotion {
                    Circle().fill(AngularGradient(stops: [.init(color: .clear, location: 0), .init(color: .clear, location: 0.72), .init(color: HomeTheme.mint.opacity(0.22), location: 1)], center: .center))
                        .rotationEffect(.degrees(progress * 720))
                }
                VStack(spacing: 5) {
                    Image(systemName: "sparkles").font(.title).foregroundStyle(HomeTheme.mint)
                    Text("\(Int(progress * 100))%").font(.system(.title3, design: .rounded, weight: .medium)).monospacedDigit()
                }
            }.frame(width: 140, height: 140)
                .animation(reduceMotion ? nil : .linear(duration: 0.08), value: progress)
            Text(caption).font(.subheadline.weight(.medium)).multilineTextAlignment(.center)
            Text("Preview animation · no files are being scanned or deleted")
                .font(.caption).foregroundStyle(HomeTheme.muted).multilineTextAlignment(.center)
            Button("Cancel preview", action: cancel).font(.caption.weight(.semibold)).tint(HomeTheme.mint)
        }.frame(maxWidth: .infinity).glassCard().accessibilityIdentifier("scanPreview")
    }
}

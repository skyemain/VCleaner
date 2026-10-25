import AppKit
import SwiftUI

enum OpsTheme {
    static let background = Color(red: 0.024, green: 0.039, blue: 0.035)
    static let mint = Color(red: 0.239, green: 1.0, blue: 0.788)
    static let mintDeep = Color(red: 0.086, green: 0.725, blue: 0.600)
    static let text = Color(red: 0.902, green: 1.0, blue: 0.965)
    static let secondary = Color(red: 0.525, green: 0.659, blue: 0.604)
    static let amber = Color(red: 1.0, green: 0.824, blue: 0.247)
    static let card = Color.white.opacity(0.06)
    static let cardStroke = Color(red: 0.239, green: 1.0, blue: 0.788).opacity(0.22)
}

struct GlassCard: ViewModifier {
    func body(content: Content) -> some View {
        content
            .padding(.horizontal, 18)
            .padding(.vertical, 12)
            .background(OpsTheme.card, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .stroke(OpsTheme.cardStroke, lineWidth: 1)
            )
    }
}

struct RadarButton: View {
    var title: String
    var spinning: Bool
    var action: () -> Void
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        Button(action: action) {
            ZStack {
                Circle()
                    .stroke(OpsTheme.mint.opacity(0.35), style: StrokeStyle(lineWidth: 1.5, dash: [2, 7]))
                    .frame(width: 236, height: 236)
                Circle()
                    .trim(from: 0, to: spinning && !reduceMotion ? 0.72 : 1)
                    .stroke(
                        AngularGradient(colors: [OpsTheme.mint, OpsTheme.mintDeep, OpsTheme.mint], center: .center),
                        style: StrokeStyle(lineWidth: 3, lineCap: .round)
                    )
                    .frame(width: 210, height: 210)
                    .rotationEffect(.degrees(spinning && !reduceMotion ? 360 : 0))
                    .animation(spinning && !reduceMotion ? .linear(duration: 2.2).repeatForever(autoreverses: false) : .default, value: spinning)
                Circle()
                    .fill(Color.black.opacity(0.45))
                    .frame(width: 176, height: 176)
                    .overlay(Circle().stroke(OpsTheme.mint.opacity(0.25), lineWidth: 1))
                VStack(spacing: 8) {
                    Image(systemName: "sparkle")
                        .foregroundStyle(OpsTheme.mint)
                    Text(title)
                        .font(.system(size: 20, weight: .semibold, design: .monospaced))
                        .tracking(6)
                }
            }
            .shadow(color: OpsTheme.mint.opacity(0.35), radius: spinning ? 28 : 18)
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Clean. Removes eligible caches, temporary files and Trash files. Trash deletion is permanent.")
    }
}

struct VisualEffectBackground: NSViewRepresentable {
    func makeNSView(context: Context) -> NSVisualEffectView {
        let view = NSVisualEffectView()
        view.material = .hudWindow
        view.blendingMode = .behindWindow
        view.state = .followsWindowActiveState
        return view
    }

    func updateNSView(_ nsView: NSVisualEffectView, context: Context) {}
}

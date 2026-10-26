import SwiftUI

enum HomeTheme {
    static let mint = Color(red: 0.32, green: 1, blue: 0.8)
    static let teal = Color(red: 0.02, green: 0.68, blue: 0.68)
    static let muted = Color(red: 0.61, green: 0.71, blue: 0.71)
    static let ink = Color(red: 0.02, green: 0.055, blue: 0.065)
    static var background: some View {
        ZStack {
            LinearGradient(colors: [Color(red: 0.025, green: 0.105, blue: 0.13), ink, Color(red: 0.02, green: 0.04, blue: 0.065)], startPoint: .topLeading, endPoint: .bottomTrailing)
            Ellipse().fill(teal.opacity(0.16)).frame(width: 300, height: 380).blur(radius: 85).offset(x: 130, y: -280)
            Ellipse().fill(mint.opacity(0.07)).frame(width: 270, height: 270).blur(radius: 80).offset(x: -150, y: 260)
        }.ignoresSafeArea()
    }
}

struct GlassCard: ViewModifier {
    func body(content: Content) -> some View {
        content
            .padding(18)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 24, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 24, style: .continuous).strokeBorder(LinearGradient(colors: [.white.opacity(0.18), .white.opacity(0.035)], startPoint: .topLeading, endPoint: .bottomTrailing)))
            .shadow(color: .black.opacity(0.12), radius: 20, y: 12)
    }
}

extension View {
    func glassCard() -> some View { modifier(GlassCard()) }
}

struct CardHeading: View {
    let title: String
    let symbol: String
    var body: some View {
        Label(title.uppercased(), systemImage: symbol)
            .font(.system(.caption, design: .rounded, weight: .semibold))
            .tracking(1.7).foregroundStyle(HomeTheme.muted)
    }
}

struct MetricRing: View {
    let fraction: Double
    let title: String
    let subtitle: String
    var body: some View {
        ZStack {
            Circle().stroke(.white.opacity(0.07), lineWidth: 11)
            Circle().trim(from: 0, to: max(0, min(1, fraction)))
                .stroke(AngularGradient(colors: [HomeTheme.teal, HomeTheme.mint, HomeTheme.teal], center: .center), style: StrokeStyle(lineWidth: 11, lineCap: .round))
                .rotationEffect(.degrees(-90))
            VStack(spacing: 4) {
                Text(title).font(.system(.title, design: .rounded, weight: .semibold)).monospacedDigit().minimumScaleFactor(0.7)
                Text(subtitle).font(.caption).foregroundStyle(HomeTheme.muted)
            }.padding(18)
        }.accessibilityElement(children: .ignore).accessibilityLabel("\(title) \(subtitle)")
    }
}

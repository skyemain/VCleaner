import SwiftUI

struct BatteryCard: View {
    let battery: DeviceBattery
    let illustrativeMinutes: Int?
    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            HStack {
                CardHeading(title: "Battery", symbol: "battery.75percent")
                Spacer()
                Label(battery.state.rawValue, systemImage: battery.state.symbol)
                    .font(.caption.weight(.medium)).foregroundStyle(HomeTheme.mint)
            }
            HStack(alignment: .firstTextBaseline, spacing: 5) {
                Text(battery.percentage.map(String.init) ?? "—").font(.system(size: 48, weight: .semibold, design: .rounded)).monospacedDigit()
                Text("%").font(.title2).foregroundStyle(HomeTheme.muted)
                Spacer()
                Image(systemName: battery.state == .charging || battery.state == .full ? "bolt.batteryblock.fill" : "battery.75percent")
                    .font(.system(size: 35, weight: .light)).foregroundStyle(HomeTheme.mint)
            }
            if let fraction = battery.fraction {
                GeometryReader { geometry in
                    ZStack(alignment: .leading) {
                        Capsule().fill(.white.opacity(0.07))
                        Capsule().fill(LinearGradient(colors: [HomeTheme.teal, HomeTheme.mint], startPoint: .leading, endPoint: .trailing))
                            .frame(width: geometry.size.width * fraction)
                    }
                }.frame(height: 7).accessibilityLabel("Battery \(battery.percentage ?? 0) percent")
            } else {
                Text("Battery readings may be unavailable in the simulator.").font(.caption).foregroundStyle(HomeTheme.muted)
            }
            Divider()
            HStack(alignment: .top, spacing: 16) {
                Image(systemName: "clock").font(.title3).foregroundStyle(HomeTheme.mint).padding(.top, 2)
                VStack(alignment: .leading, spacing: 5) {
                    Text("Time remaining").font(.subheadline.weight(.medium))
                    Text("Illustrative estimate").font(.caption).foregroundStyle(HomeTheme.muted)
                }
                Spacer()
                Text(timeText).font(.system(.title3, design: .rounded, weight: .semibold)).monospacedDigit()
            }
            Text("15-hour model at 100% · not a live battery prediction\(battery.state == .charging || battery.state == .full ? " · assumes unplugged" : "")")
                .font(.caption2).foregroundStyle(HomeTheme.muted).fixedSize(horizontal: false, vertical: true)
        }.glassCard().accessibilityIdentifier("batteryCard")
    }
    private var timeText: String {
        guard let minutes = illustrativeMinutes else { return "—" }
        return minutes >= 60 ? "\(minutes / 60)h \(minutes % 60)m" : "\(minutes)m"
    }
}

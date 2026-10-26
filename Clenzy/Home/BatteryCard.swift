import SwiftUI

struct BatteryCard: View {
    let battery: DeviceBattery
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                CardHeading(title: "Battery", symbol: "battery.75percent")
                Spacer()
                Label(battery.state.rawValue, systemImage: battery.state.symbol)
                    .font(.caption.weight(.medium)).foregroundStyle(HomeTheme.mint)
            }
            HStack(alignment: .firstTextBaseline, spacing: 5) {
                Text(battery.percentage.map(String.init) ?? "—").font(.system(size: 40, weight: .semibold, design: .rounded)).monospacedDigit()
                Text("%").font(.title2).foregroundStyle(HomeTheme.muted)
                Spacer()
                Image(systemName: battery.state == .charging || battery.state == .full ? "bolt.batteryblock.fill" : "battery.75percent")
                    .font(.system(size: 29, weight: .light)).foregroundStyle(HomeTheme.mint)
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
        }.glassCard().accessibilityIdentifier("batteryCard")
    }
}

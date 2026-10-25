import Foundation
import IOKit
import IOKit.ps

struct BatterySnapshot: Equatable, Sendable {
    var present: Bool
    var chargeFraction: Double?
    var timeRemaining: String
    var powerState: String
    var health: String
    var healthFraction: Double?
}

enum BatteryInfoService {
    static func snapshot() -> BatterySnapshot {
        guard let info = IOPSCopyPowerSourcesInfo()?.takeRetainedValue(),
              let sources = IOPSCopyPowerSourcesList(info)?.takeRetainedValue() as? [CFTypeRef] else {
            return unavailable
        }
        for source in sources {
            guard let description = IOPSGetPowerSourceDescription(info, source)?.takeUnretainedValue() as? [String: Any],
                  description[kIOPSTypeKey] as? String == kIOPSInternalBatteryType else { continue }
            let current = (description[kIOPSCurrentCapacityKey] as? NSNumber)?.doubleValue
            let maximum = (description[kIOPSMaxCapacityKey] as? NSNumber)?.doubleValue
            let nominal = (description[kIOPSNominalCapacityKey] as? NSNumber)?.doubleValue
            let design = (description[kIOPSDesignCapacityKey] as? NSNumber)?.doubleValue
            let pluggedIn = description[kIOPSPowerSourceStateKey] as? String == kIOPSACPowerValue
            let charging = description[kIOPSIsChargingKey] as? Bool == true
            let condition = description[kIOPSBatteryHealthKey] as? String
            let registryHealth = capacityHealthEstimate()
            return BatterySnapshot(present: true, chargeFraction: fraction(current, maximum),
                timeRemaining: timeLabel(seconds: IOPSGetTimeRemainingEstimate(), pluggedIn: pluggedIn),
                powerState: pluggedIn ? (charging ? "Charging" : "Power adapter") : "On battery · estimate varies with use",
                health: condition == kIOPSGoodValue ? "Normal" : condition == kIOPSFairValue || condition == kIOPSPoorValue ? "Service recommended" : condition ?? "Unavailable",
                healthFraction: fraction(nominal, design) ?? registryHealth)
        }
        return BatterySnapshot(present: false, chargeFraction: nil, timeRemaining: "No battery",
            powerState: "Desktop Mac / no internal battery", health: "Not applicable", healthFraction: nil)
    }

    static func fraction(_ value: Double?, _ total: Double?) -> Double? {
        guard let value, let total, value.isFinite, total.isFinite, value >= 0, total > 0 else { return nil }
        return min(1, max(0, value / total))
    }

    static func timeLabel(seconds: Double, pluggedIn: Bool) -> String {
        if pluggedIn { return "Plugged in" }
        if seconds == kIOPSTimeRemainingUnlimited { return "Unavailable" }
        guard seconds.isFinite, seconds > 0 else { return "Calculating…" }
        let minutes = max(1, Int(seconds / 60))
        return minutes >= 60 ? "≈ \(minutes / 60) h \(minutes % 60) min" : "≈ \(minutes) min"
    }

    /// Battery drivers may expose capacity data even when the public power-source dictionary omits health.
    /// This ratio is an estimate, not Apple's calibrated Battery Health percentage.
    private static func capacityHealthEstimate() -> Double? {
        let service = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("AppleSmartBattery"))
        guard service != 0 else { return nil }
        defer { IOObjectRelease(service) }
        guard let data = IORegistryEntryCreateCFProperty(service, "BatteryData" as CFString, kCFAllocatorDefault, 0)?.takeRetainedValue() as? [String: Any] else { return nil }
        return fraction((data["NominalChargeCapacity"] as? NSNumber)?.doubleValue,
            (data["DesignCapacity"] as? NSNumber)?.doubleValue)
    }

    static let unavailable = BatterySnapshot(present: false, chargeFraction: nil, timeRemaining: "Unavailable",
        powerState: "Battery data not reported", health: "Unavailable", healthFraction: nil)
}

import Foundation
import UIKit

struct DeviceStorage: Equatable, Sendable {
    let totalBytes: Int64
    let freeBytes: Int64
    var usedBytes: Int64 { max(0, totalBytes - freeBytes) }
    var usedFraction: Double { totalBytes > 0 ? min(1, Double(usedBytes) / Double(totalBytes)) : 0 }

    static func read(attributes: [FileAttributeKey: Any]) -> DeviceStorage? {
        guard let total = (attributes[.systemSize] as? NSNumber)?.int64Value,
              let free = (attributes[.systemFreeSize] as? NSNumber)?.int64Value,
              total > 0, free >= 0 else { return nil }
        return DeviceStorage(totalBytes: total, freeBytes: min(free, total))
    }
}

enum ChargingState: String, Sendable {
    case unknown = "Status unavailable", unplugged = "On battery", charging = "Charging", full = "Fully charged"
    var symbol: String {
        switch self {
        case .charging, .full: return "bolt.fill"
        case .unplugged: return "battery.75percent"
        case .unknown: return "battery.0percent"
        }
    }
}

struct DeviceBattery: Equatable, Sendable {
    let fraction: Double?
    let state: ChargingState
    var percentage: Int? { fraction.map { Int(($0 * 100).rounded()) } }

    init(level: Float, state: ChargingState) {
        fraction = level.isFinite && level >= 0 ? min(1, Double(level)) : nil
        self.state = state
    }
}

struct DeviceSnapshot: Equatable, Sendable {
    var storage: DeviceStorage?
    var battery: DeviceBattery
}

@MainActor
protocol DeviceReading {
    func snapshot() -> DeviceSnapshot
}

@MainActor
final class DeviceManager: DeviceReading {
    static let shared = DeviceManager()
    private let fileManager: FileManager
    private let device: UIDevice

    init(fileManager: FileManager = .default, device: UIDevice = .current) {
        self.fileManager = fileManager
        self.device = device
        device.isBatteryMonitoringEnabled = true
    }

    func snapshot() -> DeviceSnapshot {
        let attributes = try? fileManager.attributesOfFileSystem(forPath: NSHomeDirectory())
        let storage = attributes.flatMap(DeviceStorage.read)
        let charging: ChargingState
        switch device.batteryState {
        case .unplugged: charging = .unplugged
        case .charging: charging = .charging
        case .full: charging = .full
        case .unknown: charging = .unknown
        @unknown default: charging = .unknown
        }
        return DeviceSnapshot(storage: storage, battery: DeviceBattery(level: device.batteryLevel, state: charging))
    }
}

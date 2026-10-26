import Foundation
import Observation

@MainActor
@Observable
final class HomeViewModel {
    enum Destination: Hashable { case previewResults, photos }
    enum ScanState: Equatable { case idle, scanning, complete }

    private(set) var device: DeviceSnapshot
    private(set) var scanState: ScanState = .idle
    private(set) var scanProgress = 0.0
    var navigationPath: [Destination] = []
    @ObservationIgnored private let hardware: any DeviceReading
    @ObservationIgnored private var scanTask: Task<Void, Never>?
    @ObservationIgnored private let stepDelay: UInt64

    init(hardware: any DeviceReading = DeviceManager.shared, stepDelay: UInt64 = 40_000_000) {
        self.hardware = hardware
        self.stepDelay = stepDelay
        device = hardware.snapshot()
    }

    var isScanning: Bool { scanState == .scanning }
    var batteryTimeMinutes: Int? {
        Self.illustrativeMinutes(batteryFraction: device.battery.fraction)
    }
    var scanCaption: String {
        if scanProgress < 0.34 { return "Preparing your storage preview" }
        if scanProgress < 0.7 { return "Exploring example categories" }
        return "Putting your sample results together"
    }

    static func illustrativeMinutes(batteryFraction: Double?) -> Int? {
        guard let fraction = batteryFraction, fraction.isFinite else { return nil }
        return Int((max(0, min(1, fraction)) * 15 * 60).rounded())
    }

    func refreshDevice() { device = hardware.snapshot() }

    func startScanPreview() {
        guard !isScanning else { return }
        scanTask?.cancel()
        scanProgress = 0
        scanState = .scanning
        let delay = stepDelay
        scanTask = Task { [weak self] in
            for step in 1...60 {
                do { try await Task.sleep(nanoseconds: delay) }
                catch { return }
                guard !Task.isCancelled else { return }
                self?.scanProgress = Double(step) / 60
            }
            guard !Task.isCancelled, let self else { return }
            self.scanState = .complete
            self.scanTask = nil
            self.navigationPath.append(.previewResults)
        }
    }

    func cancelScanPreview() {
        guard isScanning else { return }
        scanTask?.cancel()
        scanTask = nil
        scanState = .idle
        scanProgress = 0
    }

    func openPhotos() {
        cancelScanPreview()
        navigationPath.append(.photos)
    }
}

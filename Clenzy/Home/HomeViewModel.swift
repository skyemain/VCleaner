import Foundation
import Observation

@MainActor
@Observable
final class HomeViewModel {
    enum Destination: Hashable { case photos }
    private(set) var device: DeviceSnapshot
    var navigationPath: [Destination] = []
    @ObservationIgnored private let hardware: any DeviceReading
    init(hardware: any DeviceReading = DeviceManager.shared) {
        self.hardware = hardware
        device = hardware.snapshot()
    }
    func refreshDevice() { device = hardware.snapshot() }
    func openPhotos() { if navigationPath.isEmpty { navigationPath.append(.photos) } }
}

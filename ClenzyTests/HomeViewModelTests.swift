import XCTest
@testable import Clenzy

@MainActor
private final class FakeDevice: DeviceReading {
    var value = DeviceSnapshot(storage: DeviceStorage(totalBytes: 1000, freeBytes: 400), battery: DeviceBattery(level: 0.5, state: .charging))
    func snapshot() -> DeviceSnapshot { value }
}

final class HomeViewModelTests: XCTestCase {
    func testFilesystemAccountingHandlesInvalidAndInconsistentValues() {
        XCTAssertNil(DeviceStorage.read(attributes: [:]))
        XCTAssertNil(DeviceStorage.read(attributes: [.systemSize: NSNumber(value: 0), .systemFreeSize: NSNumber(value: 0)]))
        XCTAssertNil(DeviceStorage.read(attributes: [.systemSize: NSNumber(value: 1000), .systemFreeSize: NSNumber(value: -1)]))
        let normal = DeviceStorage.read(attributes: [.systemSize: NSNumber(value: 1000), .systemFreeSize: NSNumber(value: 400)])
        XCTAssertEqual(normal?.usedBytes, 600)
        XCTAssertEqual(normal?.usedFraction, 0.6)
        let inconsistent = DeviceStorage.read(attributes: [.systemSize: NSNumber(value: 1000), .systemFreeSize: NSNumber(value: 2000)])
        XCTAssertEqual(inconsistent?.usedBytes, 0)
        XCTAssertEqual(inconsistent?.freeBytes, 1000)
    }

    func testUnknownBatteryIsNotShownAsZeroOrFull() {
        XCTAssertNil(DeviceBattery(level: -1, state: .unknown).percentage)
        XCTAssertNil(DeviceBattery(level: .nan, state: .unknown).percentage)
        XCTAssertEqual(DeviceBattery(level: 0, state: .unplugged).percentage, 0)
        XCTAssertEqual(DeviceBattery(level: 2, state: .full).percentage, 100)
    }

    @MainActor
    func testIllustrativeTimeUsesFifteenHoursWithoutInventingUnknownReadings() {
        XCTAssertEqual(HomeViewModel.illustrativeMinutes(batteryFraction: 1), 900)
        XCTAssertEqual(HomeViewModel.illustrativeMinutes(batteryFraction: 0.5), 450)
        XCTAssertEqual(HomeViewModel.illustrativeMinutes(batteryFraction: 0), 0)
        XCTAssertNil(HomeViewModel.illustrativeMinutes(batteryFraction: nil))
        XCTAssertNil(HomeViewModel.illustrativeMinutes(batteryFraction: .nan))
    }

    @MainActor
    func testRefreshUsesNewHardwareSnapshot() {
        let hardware = FakeDevice()
        let model = HomeViewModel(hardware: hardware)
        XCTAssertEqual(model.device.storage?.usedBytes, 600)
        hardware.value.storage = nil
        hardware.value.battery = DeviceBattery(level: -1, state: .unknown)
        model.refreshDevice()
        XCTAssertNil(model.device.storage)
        XCTAssertNil(model.batteryTimeMinutes)
    }

    @MainActor
    func testPreviewCompletesOnceAndDoesNotAlterHardwareOrClaimDeletion() async throws {
        let hardware = FakeDevice()
        let before = hardware.value
        let model = HomeViewModel(hardware: hardware, stepDelay: 1)
        model.startScanPreview()
        model.startScanPreview()
        for _ in 0..<500 {
            if model.scanState == .complete { break }
            try await Task.sleep(nanoseconds: 2_000_000)
        }
        XCTAssertEqual(model.scanState, .complete)
        XCTAssertEqual(model.scanProgress, 1)
        XCTAssertEqual(model.navigationPath, [.previewResults])
        XCTAssertEqual(hardware.value, before)
        XCTAssertEqual(model.device, before)
    }

    @MainActor
    func testCancelledPreviewCannotNavigateLater() async throws {
        let model = HomeViewModel(hardware: FakeDevice(), stepDelay: 1_000_000)
        model.startScanPreview()
        model.cancelScanPreview()
        try await Task.sleep(nanoseconds: 100_000_000)
        XCTAssertEqual(model.scanState, .idle)
        XCTAssertEqual(model.scanProgress, 0)
        XCTAssertTrue(model.navigationPath.isEmpty)
    }
}

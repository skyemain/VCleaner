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
    func testRefreshUsesNewHardwareSnapshot() {
        let hardware = FakeDevice()
        let model = HomeViewModel(hardware: hardware)
        XCTAssertEqual(model.device.storage?.usedBytes, 600)
        hardware.value.storage = nil
        hardware.value.battery = DeviceBattery(level: -1, state: .unknown)
        model.refreshDevice()
        XCTAssertNil(model.device.storage)
    }

    @MainActor
    func testCleanActionNavigatesToRealPhotosOnlyOnce() {
        let hardware = FakeDevice()
        let model = HomeViewModel(hardware: hardware)
        model.openPhotos()
        model.openPhotos()
        XCTAssertEqual(model.navigationPath, [.photos])
        XCTAssertEqual(model.device, hardware.value)
    }

    func testCancellationBeforeResourceIdentifierIsRetained() {
        let request = ResourceRequest()
        request.cancel()
        XCTAssertThrowsError(try request.result(error: nil)) { XCTAssertTrue($0 is CancellationError) }
    }
    func testOversizedResourceNeverProducesDuplicateDigest() {
        let request = ResourceRequest()
        request.append(Data(repeating: 1, count: 11), limit: 10)
        XCTAssertThrowsError(try request.result(error: nil))
    }
}

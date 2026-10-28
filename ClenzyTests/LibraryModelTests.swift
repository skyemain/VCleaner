import XCTest
import Photos
@testable import Clenzy

@MainActor
private final class FakePhotoAccess: PhotoLibraryAccessing {
    var status: PHAuthorizationStatus = .denied
    var deletionError: Error?
    var deleted = Set<String>()
    var requests: [[ReviewItem]] = []
    func authorization() async -> PHAuthorizationStatus { status }
    func delete(_ review: [ReviewItem]) async throws -> Set<String> {
        requests.append(review)
        if let deletionError { throw deletionError }
        return deleted
    }
}

@MainActor
final class LibraryModelTests: XCTestCase {
    private let item = ReviewItem(id: "scanned", name: "test", bytes: 10, isScreenshot: true, isVideo: false, digest: nil, date: nil)
    private func waitUntilIdle(_ model: LibraryModel) async throws {
        for _ in 0..<100 {
            if !model.busy { return }
            try await Task.sleep(nanoseconds: 10_000_000)
        }
        XCTFail("Operation did not finish")
    }
    func testDeniedPermissionDoesNotReplaceReviewOrDelete() async throws {
        let access = FakePhotoAccess(); let model = LibraryModel(access: access)
        model.items = [item]; model.scan()
        try await waitUntilIdle(model)
        XCTAssertEqual(model.items, [item]); XCTAssertTrue(model.message.contains("Allow Photos")); XCTAssertTrue(access.requests.isEmpty)
    }
    func testDeletionRejectionPreservesItemsAndSelection() async throws {
        let access = FakePhotoAccess(); access.deletionError = CancellationError()
        let model = LibraryModel(access: access); model.items = [item]; model.selected = [item.id]
        model.deleteSelected(); try await waitUntilIdle(model)
        XCTAssertEqual(model.items, [item]); XCTAssertEqual(model.selected, [item.id]); XCTAssertTrue(model.message.contains("not completed"))
    }
    func testOnlyScannedSelectionsReachDeletionAndOnlyConfirmedItemsDisappear() async throws {
        let access = FakePhotoAccess(); access.deleted = [item.id]
        let other = ReviewItem(id: "changed", name: "other", bytes: 20, isScreenshot: true, isVideo: false, digest: nil, date: nil)
        let model = LibraryModel(access: access); model.items = [item,other]; model.selected = [item.id,other.id,"unscanned"]
        model.deleteSelected(); try await waitUntilIdle(model)
        XCTAssertEqual(Set(access.requests[0].map(\.id)), [item.id,other.id]); XCTAssertEqual(model.items, [other]); XCTAssertTrue(model.message.contains("1 changed"))
    }
    func testStaleLibraryCannotDeleteBeforeRescan() {
        let access = FakePhotoAccess(); let model = LibraryModel(access: access)
        model.items = [item]; model.selected = [item.id]; model.needsRescan = true
        model.deleteSelected()
        XCTAssertFalse(model.busy); XCTAssertTrue(access.requests.isEmpty); XCTAssertEqual(model.items, [item])
    }
}

import XCTest
@testable import Clenzy

final class ReviewRulesTests: XCTestCase {
    func testDuplicatesRequireCompleteHashesAndKeepEveryOriginalForReview() {
        let a = ReviewItem(id: "a", name: "a", bytes: 10, isScreenshot: false, isVideo: false, digest: "same", date: nil)
        let b = ReviewItem(id: "b", name: "b", bytes: 10, isScreenshot: false, isVideo: false, digest: "same", date: nil)
        let cloud = ReviewItem(id: "cloud", name: "cloud", bytes: nil, isScreenshot: true, isVideo: true, digest: nil, date: nil)
        XCTAssertEqual(ReviewRules.items([a, b, cloud], category: .duplicates).map(\.id), ["a", "b"])
        XCTAssertTrue(ReviewRules.items([a, cloud], category: .duplicates).isEmpty)
        XCTAssertTrue(ReviewRules.items([cloud], category: .videos).isEmpty)
        XCTAssertEqual(ReviewRules.items([cloud], category: .screenshots).map(\.id), ["cloud"])
    }
    func testLargeVideoThresholdDoesNotIncludePhotos() {
        let video = ReviewItem(id: "video", name: "video", bytes: 104_857_600, isScreenshot: false, isVideo: true, digest: nil, date: nil)
        let photo = ReviewItem(id: "photo", name: "photo", bytes: 200_000_000, isScreenshot: false, isVideo: false, digest: nil, date: nil)
        XCTAssertEqual(ReviewRules.items([video, photo], category: .videos).map(\.id), ["video"])
    }
}

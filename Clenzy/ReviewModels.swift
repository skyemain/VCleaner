import Foundation

struct ReviewItem: Identifiable, Hashable, Sendable {
    let id: String
    let name: String
    let bytes: Int64?
    let isScreenshot: Bool
    let isVideo: Bool
    let digest: String?
    let date: Date?
}

enum ReviewCategory: String, CaseIterable {
    case screenshots = "Screenshots", videos = "Large videos", duplicates = "Exact duplicates"
}

enum ReviewRules {
    static func items(_ items: [ReviewItem], category: ReviewCategory) -> [ReviewItem] {
        switch category {
        case .screenshots: return items.filter(\.isScreenshot)
        case .videos: return items.filter { $0.isVideo && ($0.bytes ?? 0) >= 100 * 1024 * 1024 }
        case .duplicates:
            let groups = Dictionary(grouping: items.filter { $0.digest != nil && $0.bytes != nil }, by: { $0.digest! })
            return groups.values.filter { $0.count > 1 }.flatMap { $0 }.sorted { $0.id < $1.id }
        }
    }
}

import Foundation

struct ReviewItem: Identifiable, Hashable, Sendable {
    let id: String
    let name: String
    let bytes: Int64?
    let isScreenshot: Bool
    let isVideo: Bool
    let digest: String?
    let date: Date?
    var modified: Date? = nil
}

struct DuplicateGroup: Identifiable {
    let id: String
    let items: [ReviewItem]
    var keeper: ReviewItem { items[0] }
}

enum ReviewCategory: String, CaseIterable {
    case screenshots = "Screenshots", videos = "Videos", duplicates = "Duplicates"
}

enum ReviewRules {
    static func groups(_ items: [ReviewItem]) -> [DuplicateGroup] {
        Dictionary(grouping: items.filter { $0.digest != nil && $0.bytes != nil }, by: { $0.digest! })
            .filter { $0.value.count > 1 }.map { key, values in
                DuplicateGroup(id: key, items: values.sorted {
                    if $0.date != $1.date { return ($0.date ?? .distantFuture) < ($1.date ?? .distantFuture) }
                    return $0.id < $1.id
                })
            }.sorted { $0.keeper.id < $1.keeper.id }
    }
    static func selectsEveryCopy(_ selected: Set<String>, items: [ReviewItem]) -> Bool {
        groups(items).contains { Set($0.items.map(\.id)).isSubset(of: selected) }
    }
    static func items(_ items: [ReviewItem], category: ReviewCategory) -> [ReviewItem] {
        switch category {
        case .screenshots: return items.filter(\.isScreenshot)
        case .videos: return items.filter { $0.isVideo }.sorted { ($0.bytes ?? -1) > ($1.bytes ?? -1) }
        case .duplicates: return groups(items).flatMap(\.items)
        }
    }
}

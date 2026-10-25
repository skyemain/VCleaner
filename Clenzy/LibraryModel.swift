import Photos
import SwiftUI

@MainActor
final class LibraryModel: ObservableObject {
    @Published var items: [ReviewItem] = []
    @Published var selected = Set<String>()
    @Published var busy = false
    @Published var message = "Review screenshots, large videos and exact duplicate photo resources. Nothing is selected automatically."
    @Published var limited = false
    @Published var totalStorage: Int64 = 0
    @Published var availableStorage: Int64 = 0
    private var worker: Task<PhotoScan, Error>?

    func refreshStorage() {
        let url = URL(fileURLWithPath: NSHomeDirectory())
        let values = try? url.resourceValues(forKeys: [.volumeTotalCapacityKey, .volumeAvailableCapacityForImportantUsageKey])
        totalStorage = Int64(values?.volumeTotalCapacity ?? 0)
        availableStorage = values?.volumeAvailableCapacityForImportantUsage ?? 0
    }

    func scan() {
        guard !busy else { return }
        busy = true
        Task {
            let status = await PHPhotoLibrary.requestAuthorization(for: .readWrite)
            limited = status == .limited
            guard status == .authorized || limited else {
                message = "Allow Photos access in Settings to review your library."
                busy = false
                return
            }
            message = "Scanning up to 1,000 recent accessible items on this device. iCloud originals are not downloaded."
            let task = Task.detached(priority: .utility) { try await PhotoScanner.scan() }
            worker = task
            do {
                let scan = try await task.value
                items = scan.items
                selected.removeAll()
                message = "Reviewed \(scan.scanned) of \(scan.total) accessible items. \(scan.unreadable) could not be measured locally. Duplicate matches compare all accessible original/adjustment resources; review both versions before deleting."
            } catch is CancellationError { message = "Scan cancelled. Your previous review is unchanged." }
            catch { message = "Scan failed: \(error.localizedDescription)" }
            worker = nil
            busy = false
            refreshStorage()
        }
    }

    func cancel() { worker?.cancel() }

    func deleteSelected() {
        guard !busy, !selected.isEmpty else { return }
        let ids = selected.intersection(Set(items.map(\.id)))
        guard !ids.isEmpty else { return }
        busy = true
        Task {
            let assets = PHAsset.fetchAssets(withLocalIdentifiers: Array(ids), options: nil)
            do {
                try await PHPhotoLibrary.shared().performChanges {
                    PHAssetChangeRequest.deleteAssets(assets)
                }
                items.removeAll { ids.contains($0.id) }
                selected.removeAll()
                message = "Selected items removed from Photos. They remain in Recently Deleted until Photos removes them; device space may not increase immediately."
            } catch { message = "Nothing confirmed deleted: \(error.localizedDescription)" }
            busy = false
            refreshStorage()
        }
    }
}

import Photos
import SwiftUI

@MainActor
protocol PhotoLibraryAccessing {
    func authorization() async -> PHAuthorizationStatus
    func delete(_ review: [ReviewItem]) async throws -> Set<String>
}

@MainActor
struct PhotoLibraryAccess: PhotoLibraryAccessing {
    func authorization() async -> PHAuthorizationStatus { await PHPhotoLibrary.requestAuthorization(for: .readWrite) }
    func delete(_ review: [ReviewItem]) async throws -> Set<String> {
        let fetched = PHAsset.fetchAssets(withLocalIdentifiers: review.map(\.id), options: nil)
        var eligible: [PHAsset] = []
        fetched.enumerateObjects { asset, _, _ in
            if let old = review.first(where: { $0.id == asset.localIdentifier }), old.modified == asset.modificationDate { eligible.append(asset) }
        }
        if !eligible.isEmpty {
            try await PHPhotoLibrary.shared().performChanges { PHAssetChangeRequest.deleteAssets(eligible as NSArray) }
        }
        return Set(eligible.map(\.localIdentifier))
    }
}

@MainActor
final class LibraryModel: NSObject, ObservableObject, PHPhotoLibraryChangeObserver {
    @Published var items: [ReviewItem] = []
    @Published var selected = Set<String>()
    @Published var busy = false
    @Published var message = "Screenshots, videos and exact duplicates. You choose what goes."
    @Published var limited = false
    @Published var progress = 0.0
    @Published var storage: DeviceStorage?
    @Published var needsRescan = false
    private let access: any PhotoLibraryAccessing
    private var worker: Task<Void, Never>?
    private var scanning: Task<PhotoScan, Error>?

    init(access: (any PhotoLibraryAccessing)? = nil) {
        self.access = access ?? PhotoLibraryAccess()
        super.init(); PHPhotoLibrary.shared().register(self); refreshStorage()
    }
    deinit { PHPhotoLibrary.shared().unregisterChangeObserver(self); worker?.cancel(); scanning?.cancel() }
    nonisolated func photoLibraryDidChange(_ changeInstance: PHChange) {
        Task { @MainActor [weak self] in
            guard let self else { return }
            self.needsRescan = true
            self.selected.removeAll()
        }
    }
    func refreshStorage() { storage = DeviceManager.shared.snapshot().storage }
    func scan(deep: Bool = false) {
        guard !busy else { return }
        busy = true; progress = 0; selected.removeAll()
        worker = Task {
            let status = await access.authorization()
            limited = status == .limited
            guard !Task.isCancelled else { busy = false; return }
            guard status == .authorized || limited else {
                message = "Allow Photos access in Settings, or select photos to share with Clenzy."
                busy = false; return
            }
            needsRescan = false
            message = deep ? "Deep scan · up to 10,000 assets / 4 GiB read. Cloud originals stay in iCloud." : "Scanning · up to 1,000 assets / 512 MiB read. Cloud originals stay in iCloud."
            let task = Task.detached(priority: .utility) { [weak self] in
                try await PhotoScanner.scan(limits: deep ? .deep : .standard) { done, total in
                    Task { @MainActor [weak self] in self?.progress = Double(done) / Double(max(total, 1)) }
                }
            }
            scanning = task
            do {
                let scan = try await task.value
                try Task.checkCancellation()
                items = scan.items
                message = "\(scan.scanned) of \(scan.total) reviewed · \(scan.measured) checked for duplicates · \(scan.unreadable) unavailable · \(scan.limited) limited by scan budget."
            } catch is CancellationError { message = "Scan cancelled. Nothing deleted." }
            catch { message = "Scan failed: \(error.localizedDescription)" }
            scanning = nil; busy = false; refreshStorage()
        }
    }
    func cancel() { worker?.cancel(); scanning?.cancel() }
    func deleteSelected() {
        guard !busy, !selected.isEmpty, !needsRescan else { return }
        let review = items.filter { selected.contains($0.id) }
        busy = true
        Task {
            do {
                let ids = try await access.delete(review)
                items.removeAll { ids.contains($0.id) }; selected.removeAll()
                message = "\(ids.count) moved to Recently Deleted · \(review.count - ids.count) changed or unavailable items skipped. Space is not necessarily freed yet."
            } catch { message = "Deletion not completed: \(error.localizedDescription)" }
            busy = false; refreshStorage()
        }
    }
}

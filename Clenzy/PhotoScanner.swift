import Photos
import CryptoKit
import Foundation

struct ScanLimits: Sendable {
    let assets: Int
    let bytes: Int64
    let resourceBytes: Int64
    static let standard = ScanLimits(assets: 1000, bytes: 512 * 1024 * 1024, resourceBytes: 128 * 1024 * 1024)
    static let deep = ScanLimits(assets: 10000, bytes: 4 * 1024 * 1024 * 1024, resourceBytes: 1024 * 1024 * 1024)
}

// Cancellation can arrive before Photos returns its request identifier.
final class ResourceRequest: @unchecked Sendable {
    private let lock = NSLock()
    private var id: PHAssetResourceDataRequestID?
    private var cancelled = false
    private var hasher = SHA256()
    private var count: Int64 = 0
    private var exceeded = false
    func install(_ id: PHAssetResourceDataRequestID) {
        lock.lock(); self.id = id; let stop = cancelled || exceeded; lock.unlock()
        if stop { PHAssetResourceManager.default().cancelDataRequest(id) }
    }
    func cancel() {
        lock.lock(); cancelled = true; let request = id; lock.unlock()
        if let request { PHAssetResourceManager.default().cancelDataRequest(request) }
    }
    func append(_ data: Data, limit: Int64) {
        lock.lock()
        count += Int64(data.count)
        exceeded = exceeded || count > limit
        if !exceeded && !cancelled { hasher.update(data: data) }
        let stop = exceeded; let request = id
        lock.unlock()
        if stop, let request { PHAssetResourceManager.default().cancelDataRequest(request) }
    }
    func result(error: Error?) throws -> (Int64, String) {
        lock.lock(); defer { lock.unlock() }
        if cancelled { throw CancellationError() }
        if exceeded { throw ScanError.budget }
        if let error { throw error }
        return (count, hasher.finalize().map { String(format: "%02x", $0) }.joined())
    }
}

enum ScanError: Error { case budget }
struct PhotoScan: Sendable {
    let items: [ReviewItem]
    let scanned: Int
    let total: Int
    let unreadable: Int
    let measured: Int
    let limited: Int
}

enum PhotoScanner {
    static func scan(limits: ScanLimits = .standard, progress: @escaping @Sendable (Int, Int) -> Void = { _, _ in }) async throws -> PhotoScan {
        let options = PHFetchOptions()
        options.sortDescriptors = [NSSortDescriptor(key: "creationDate", ascending: false)]
        let assets = PHAsset.fetchAssets(with: options)
        var items: [ReviewItem] = []
        var unreadable = 0, measured = 0, limited = 0
        var remaining = limits.bytes
        let count = min(assets.count, limits.assets)
        for index in 0..<count {
            try Task.checkCancellation()
            let asset = assets.object(at: index)
            let resources = PHAssetResource.assetResources(for: asset).sorted {
                $0.type.rawValue == $1.type.rawValue ? $0.originalFilename < $1.originalFilename : $0.type.rawValue < $1.type.rawValue
            }
            var total: Int64 = 0
            var parts: [String] = []
            var complete = !resources.isEmpty
            for resource in resources {
                try Task.checkCancellation()
                guard remaining > 0 else { complete = false; limited += 1; break }
                let allowance = min(remaining, limits.resourceBytes)
                do {
                    let result = try await measure(resource, limit: allowance)
                    remaining -= result.0
                    total += result.0
                    parts.append("\(resource.type.rawValue):\(result.0):\(result.1)")
                } catch is CancellationError { throw CancellationError() }
                catch ScanError.budget { remaining -= allowance; complete = false; limited += 1; break }
                catch { remaining -= allowance; complete = false; unreadable += 1; break }
            }
            if complete { measured += 1 }
            items.append(ReviewItem(id: asset.localIdentifier, name: resources.first?.originalFilename ?? "Photo",
                bytes: complete ? total : nil, isScreenshot: asset.mediaSubtypes.contains(.photoScreenshot),
                isVideo: asset.mediaType == .video, digest: complete ? parts.joined(separator: "|") : nil,
                date: asset.creationDate, modified: asset.modificationDate))
            progress(index + 1, count)
        }
        try Task.checkCancellation()
        return PhotoScan(items: items, scanned: count, total: assets.count, unreadable: unreadable, measured: measured, limited: limited)
    }

    private static func measure(_ resource: PHAssetResource, limit: Int64) async throws -> (Int64, String) {
        let request = ResourceRequest()
        let options = PHAssetResourceRequestOptions()
        options.isNetworkAccessAllowed = false
        return try await withTaskCancellationHandler {
            try Task.checkCancellation()
            return try await withCheckedThrowingContinuation { continuation in
                let id = PHAssetResourceManager.default().requestData(for: resource, options: options, dataReceivedHandler: {
                    request.append($0, limit: limit)
                }, completionHandler: { error in
                    do { continuation.resume(returning: try request.result(error: error)) }
                    catch { continuation.resume(throwing: error) }
                })
                request.install(id)
            }
        } onCancel: { request.cancel() }
    }
}

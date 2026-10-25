import Photos
import CryptoKit
import Foundation

private final class ResourceDigest: @unchecked Sendable {
    private let lock = NSLock()
    private var hasher = SHA256()
    private var count: Int64 = 0
    func append(_ data: Data) {
        lock.lock(); defer { lock.unlock() }
        hasher.update(data: data)
        count += Int64(data.count)
    }
    func result() -> (Int64, String) {
        lock.lock(); defer { lock.unlock() }
        return (count, hasher.finalize().map { String(format: "%02x", $0) }.joined())
    }
}

struct PhotoScan: Sendable {
    let items: [ReviewItem]
    let scanned: Int
    let total: Int
    let unreadable: Int
}

enum PhotoScanner {
    static func scan() async throws -> PhotoScan {
        let options = PHFetchOptions()
        options.sortDescriptors = [NSSortDescriptor(key: "creationDate", ascending: false)]
        let assets = PHAsset.fetchAssets(with: options)
        var items: [ReviewItem] = []
        var unreadable = 0
        for index in 0..<min(assets.count, 1000) {
            try Task.checkCancellation()
            let asset = assets.object(at: index)
            let resources = PHAssetResource.assetResources(for: asset).sorted {
                $0.type.rawValue == $1.type.rawValue ? $0.originalFilename < $1.originalFilename : $0.type.rawValue < $1.type.rawValue
            }
            var total: Int64 = 0
            var parts: [String] = []
            var complete = !resources.isEmpty
            for resource in resources {
                do {
                    let measured = try await measure(resource)
                    total += measured.0
                    parts.append("\(resource.type.rawValue):\(measured.0):\(measured.1)")
                } catch { complete = false; break }
            }
            if !complete { unreadable += 1 }
            items.append(ReviewItem(id: asset.localIdentifier, name: resources.first?.originalFilename ?? "Photo",
                bytes: complete ? total : nil, isScreenshot: asset.mediaSubtypes.contains(.photoScreenshot),
                isVideo: asset.mediaType == .video, digest: complete ? parts.joined(separator: "|") : nil, date: asset.creationDate))
        }
        return PhotoScan(items: items, scanned: items.count, total: assets.count, unreadable: unreadable)
    }

    private static func measure(_ resource: PHAssetResource) async throws -> (Int64, String) {
        let accumulator = ResourceDigest()
        let options = PHAssetResourceRequestOptions()
        options.isNetworkAccessAllowed = false
        return try await withCheckedThrowingContinuation { continuation in
            PHAssetResourceManager.default().requestData(for: resource, options: options, dataReceivedHandler: {
                accumulator.append($0)
            }, completionHandler: { error in
                if let error { continuation.resume(throwing: error) }
                else { continuation.resume(returning: accumulator.result()) }
            })
        }
    }
}

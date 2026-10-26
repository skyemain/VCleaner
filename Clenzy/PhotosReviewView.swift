import SwiftUI
import Photos
import PhotosUI
import AVKit

struct PhotosReviewView: View {
    @StateObject private var model = LibraryModel()
    @State private var category = ReviewCategory.screenshots
    @State private var confirmDelete = false
    @State private var confirmDeep = false
    @State private var preview: ReviewItem?
    @State private var started = false
    private var visible: [ReviewItem] { ReviewRules.items(model.items, category: category) }
    private var selectedBytes: Int64 { model.items.filter { model.selected.contains($0.id) }.reduce(0) { $0 + ($1.bytes ?? 0) } }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                Text("Keep the moments.\nLose the clutter.").font(.system(.largeTitle, design: .rounded, weight: .semibold))
                if model.busy {
                    VStack(alignment: .leading, spacing: 12) {
                        HStack { Label("Reviewing your library", systemImage: "sparkle"); Spacer(); Button("Cancel", action: model.cancel) }
                        ProgressView(value: model.progress).tint(HomeTheme.mint)
                    }.glassCard()
                } else {
                    HStack {
                        Button("Scan Photos") { model.scan() }.buttonStyle(.borderedProminent)
                        Button("Deep scan") { confirmDeep = true }.buttonStyle(.bordered)
                    }
                }
                Text(model.message).font(.caption).foregroundStyle(HomeTheme.muted)
                if model.needsRescan { Label("Library changed. Scan again before deleting.", systemImage: "arrow.clockwise").font(.callout).foregroundStyle(.orange) }
                if model.limited {
                    Button("Choose more photos to review", action: manageAccess).buttonStyle(.bordered)
                }
                Picker("Review", selection: $category) {
                    ForEach(ReviewCategory.allCases, id: \.self) { Text($0.rawValue).tag($0) }
                }.pickerStyle(.segmented)
                if !model.busy && visible.isEmpty {
                    ContentUnavailableView("Nothing to review here", systemImage: "checkmark.shield", description: Text("Try another category or a deeper scan. Unmeasured videos still appear for manual review."))
                }
                if category == .duplicates {
                    ForEach(ReviewRules.groups(model.items)) { group in
                        VStack(alignment: .leading, spacing: 14) {
                            HStack { Text("\(group.items.count) matching copies").font(.headline); Spacer(); Image(systemName: "square.on.square") }
                            ScrollView(.horizontal) {
                                HStack(alignment: .top, spacing: 12) {
                                    ForEach(group.items) { item in
                                        VStack(alignment: .leading, spacing: 8) {
                                            Button { preview = item } label: { AssetThumbnail(id: item.id).frame(width: 130, height: 130).clipped().clipShape(RoundedRectangle(cornerRadius: 16)) }
                                            Text(item.id == group.keeper.id ? "Suggested keep" : "Matching copy").font(.caption.weight(.semibold)).foregroundStyle(HomeTheme.mint)
                                            Text(item.name).font(.caption).lineLimit(1)
                                            selection(item)
                                        }.frame(width: 130)
                                    }
                                }
                            }
                        }.glassCard()
                    }
                } else {
                    LazyVStack(spacing: 12) {
                        ForEach(visible) { item in
                            HStack(spacing: 12) {
                                Button { preview = item } label: {
                                    AssetThumbnail(id: item.id).frame(width: 66, height: 66).clipped().clipShape(RoundedRectangle(cornerRadius: 14))
                                        .overlay { if item.isVideo { Image(systemName: "play.circle.fill").font(.title).foregroundStyle(.white) } }
                                }.accessibilityLabel("Preview \(item.name)")
                                VStack(alignment: .leading, spacing: 4) {
                                    Text(item.name).font(.subheadline.weight(.semibold)).lineLimit(1)
                                    Text(item.bytes.map(format) ?? "Size unavailable · cloud or scan limit").font(.caption).foregroundStyle(HomeTheme.muted)
                                    if let date = item.date { Text(date, style: .date).font(.caption2).foregroundStyle(HomeTheme.muted) }
                                }
                                Spacer(minLength: 0)
                                selection(item)
                            }.glassCard()
                        }
                    }
                }
                Label("Nothing is selected automatically.", systemImage: "hand.raised").font(.caption).foregroundStyle(HomeTheme.muted)
                Text("Photos deletions also affect iCloud Photos and synced devices. Items stay in Recently Deleted; selected bytes are not a promise of space recovered.").font(.caption).foregroundStyle(HomeTheme.muted)
                Button("Photos access settings") { if let url = URL(string: UIApplication.openSettingsURLString) { UIApplication.shared.open(url) } }
            }.padding(22).frame(maxWidth: 700).frame(maxWidth: .infinity)
        }
        .background(HomeTheme.background).navigationTitle("Review Photos").navigationBarTitleDisplayMode(.inline)
        .toolbar(.visible, for: .navigationBar)
        .safeAreaInset(edge: .bottom) {
            if !model.selected.isEmpty {
                Button("Review deletion · \(model.selected.count) · \(format(selectedBytes)) measured") { confirmDelete = true }
                    .buttonStyle(.borderedProminent).tint(.red).padding().frame(maxWidth: .infinity).background(.ultraThinMaterial)
                    .disabled(model.busy || model.needsRescan)
            }
        }
        .confirmationDialog("Delete selected Photos items?", isPresented: $confirmDelete, titleVisibility: .visible) {
            Button("Delete selected items", role: .destructive) { model.deleteSelected() }
        } message: {
            Text((ReviewRules.selectsEveryCopy(model.selected, items: model.items) ? "Warning: you selected EVERY copy in a duplicate group. " : "") + "This affects iCloud Photos and synced devices. Recently Deleted retains items before space is freed.")
        }
        .confirmationDialog("Run a deeper scan?", isPresented: $confirmDeep, titleVisibility: .visible) {
            Button("Scan up to 10,000 items") { model.scan(deep: true) }
        } message: { Text("Reads up to 4 GiB locally, with a 1 GiB limit per resource. This takes longer and uses battery. Cloud originals are not downloaded.") }
        .sheet(item: $preview) { AssetPreview(item: $0) }
        .onAppear { if !started { started = true; model.scan() }; model.refreshStorage() }
        .onDisappear { model.cancel() }
    }
    private func selection(_ item: ReviewItem) -> some View {
        Button {
            if !model.selected.insert(item.id).inserted { model.selected.remove(item.id) }
        } label: { Image(systemName: model.selected.contains(item.id) ? "checkmark.circle.fill" : "circle").font(.title2).frame(minWidth: 44, minHeight: 44) }
            .disabled(model.busy || model.needsRescan).accessibilityLabel("\(model.selected.contains(item.id) ? "Deselect" : "Select") \(item.name)")
    }
    private func manageAccess() {
        guard let scene = UIApplication.shared.connectedScenes.first as? UIWindowScene,
              var controller = scene.windows.first(where: \.isKeyWindow)?.rootViewController else { return }
        while let presented = controller.presentedViewController { controller = presented }
        PHPhotoLibrary.shared().presentLimitedLibraryPicker(from: controller)
    }
    private func format(_ bytes: Int64) -> String { ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file) }
}

struct AssetThumbnail: View {
    let id: String
    var size: CGFloat = 160
    @State private var image: UIImage?
    @State private var request: PHImageRequestID?
    var body: some View {
        Group {
            if let image { Image(uiImage: image).resizable().scaledToFill() }
            else { Image(systemName: "icloud").resizable().scaledToFit().padding(20).foregroundStyle(HomeTheme.muted) }
        }.onAppear {
            guard let asset = PHAsset.fetchAssets(withLocalIdentifiers: [id], options: nil).firstObject else { return }
            let options = PHImageRequestOptions(); options.isNetworkAccessAllowed = false
            request = PHImageManager.default().requestImage(for: asset, targetSize: CGSize(width: size, height: size), contentMode: .aspectFit, options: options) { result, _ in
                DispatchQueue.main.async { image = result }
            }
        }.onDisappear { if let request { PHImageManager.default().cancelImageRequest(request) } }
    }
}

struct AssetPreview: View {
    let item: ReviewItem
    @Environment(\.dismiss) private var dismiss
    @State private var player: AVPlayer?
    @State private var request: PHImageRequestID?
    @State private var unavailable = false
    var body: some View {
        NavigationStack {
            VStack(spacing: 20) {
                if item.isVideo {
                    if let player { VideoPlayer(player: player) }
                    else { ContentUnavailableView(unavailable ? "Video unavailable locally" : "Loading video", systemImage: "icloud", description: Text("Open it in Photos to download the original, then try again.")) }
                } else { AssetThumbnail(id: item.id, size: 1200).scaledToFit().clipped() }
                Text(item.name).font(.caption)
            }.padding().background(HomeTheme.background).toolbar { Button("Done") { dismiss() } }
        }.onAppear {
            guard item.isVideo, let asset = PHAsset.fetchAssets(withLocalIdentifiers: [item.id], options: nil).firstObject else { return }
            let options = PHVideoRequestOptions(); options.isNetworkAccessAllowed = false
            request = PHImageManager.default().requestPlayerItem(forVideo: asset, options: options) { result, _ in
                DispatchQueue.main.async { if let result { player = AVPlayer(playerItem: result) }; unavailable = result == nil }
            }
        }.onDisappear { player?.pause(); if let request { PHImageManager.default().cancelImageRequest(request) } }
    }
}

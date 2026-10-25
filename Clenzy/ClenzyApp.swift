import SwiftUI
import Photos

private let mint = Color(red: 0.15, green: 0.95, blue: 0.76)

@main
struct ClenzyApp: App {
    var body: some Scene { WindowGroup { Dashboard() } }
}

struct Dashboard: View {
    @StateObject private var model = LibraryModel()
    @State private var category = ReviewCategory.screenshots
    @State private var confirmDelete = false
    @State private var preview: ReviewItem?
    private var visible: [ReviewItem] { ReviewRules.items(model.items, category: category) }
    private var selectedBytes: Int64 { model.items.filter { model.selected.contains($0.id) }.reduce(0) { $0 + ($1.bytes ?? 0) } }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 22) {
                    Text("Make room for what matters.").font(.title2.bold())
                    VStack(alignment: .leading, spacing: 10) {
                        Label("DEVICE STORAGE", systemImage: "internaldrive").font(.caption.bold()).foregroundStyle(mint)
                        if model.totalStorage > 0 {
                            Text("\(format(model.availableStorage)) available").font(.title.bold())
                            ProgressView(value: Double(max(0, model.totalStorage - model.availableStorage)), total: Double(model.totalStorage)).tint(mint)
                        } else { Text("Storage unavailable") }
                    }.card()
                    HStack {
                        Button(model.busy ? "Scanning…" : "Scan Photos") { model.scan() }.buttonStyle(.borderedProminent).tint(mint).foregroundStyle(.black).disabled(model.busy)
                        if model.busy { Button("Cancel") { model.cancel() } }
                        Spacer()
                    }
                    if model.limited { Text("Limited Photos access: only shared items are included. Change access in Settings to review more.").font(.caption).foregroundStyle(mint) }
                    Text(model.message).font(.footnote).foregroundStyle(.secondary)
                    Picker("Review", selection: $category) { ForEach(ReviewCategory.allCases, id: \.self) { Text($0.rawValue).tag($0) } }.pickerStyle(.menu).tint(mint)
                    if !model.busy && visible.isEmpty { ContentUnavailableView("No matches in this review", systemImage: "checkmark.shield", description: Text("Scan Photos or choose another category.")) }
                    LazyVStack(spacing: 12) {
                        ForEach(visible) { item in
                            HStack(spacing: 12) {
                                Button { preview = item } label: { AssetThumbnail(id: item.id).frame(width: 64, height: 64).clipped().cornerRadius(12) }
                                VStack(alignment: .leading, spacing: 4) {
                                    Text(item.name).font(.subheadline.bold()).lineLimit(1)
                                    Text(item.bytes.map(format) ?? "Not measured · may be in iCloud").font(.caption).foregroundStyle(.secondary)
                                    if let date = item.date { Text(date, style: .date).font(.caption2).foregroundStyle(.secondary) }
                                }
                                Spacer()
                                Button {
                                    if !model.selected.insert(item.id).inserted { model.selected.remove(item.id) }
                                } label: { Image(systemName: model.selected.contains(item.id) ? "checkmark.circle.fill" : "circle").font(.title2) }.tint(mint).disabled(model.busy).accessibilityLabel("Select \(item.name)")
                            }.card()
                        }
                    }
                    Text("Clenzy can manage the Photos items you authorize. iOS manages system caches and RAM. Deleting Photos also affects iCloud Photos and synced devices.").font(.caption).foregroundStyle(.secondary)
                    Button("Open Clenzy Settings") { if let url = URL(string: UIApplication.openSettingsURLString) { UIApplication.shared.open(url) } }.tint(mint)
                }.padding(20)
            }
            .background(Color(red: 0.035, green: 0.06, blue: 0.055))
            .navigationTitle("CLENZY")
            .safeAreaInset(edge: .bottom) {
                if !model.selected.isEmpty {
                    Button("Delete \(model.selected.count) selected · \(format(selectedBytes)) measured") { confirmDelete = true }
                        .buttonStyle(.borderedProminent).tint(.red).padding().disabled(model.busy)
                }
            }
            .confirmationDialog("Delete selected Photos items?", isPresented: $confirmDelete, titleVisibility: .visible) {
                Button("Delete selected items", role: .destructive) { model.deleteSelected() }
            } message: { Text("This also deletes from iCloud Photos and synced devices. Photos keeps deleted items in Recently Deleted. Review originals before confirming.") }
            .sheet(item: $preview) { item in
                VStack { AssetThumbnail(id: item.id, size: 900).scaledToFit(); Text(item.name); Button("Done") { preview = nil } }.padding().preferredColorScheme(.dark)
            }
            .onAppear { model.refreshStorage() }
        }.preferredColorScheme(.dark)
    }

    private func format(_ bytes: Int64) -> String { ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file) }
}

struct AssetThumbnail: View {
    let id: String
    var size: CGFloat = 160
    @State private var image: UIImage?
    var body: some View {
        Group {
            if let image { Image(uiImage: image).resizable().scaledToFill() }
            else { Image(systemName: "photo").resizable().scaledToFit().padding(12).foregroundStyle(.secondary) }
        }.task(id: id) {
            guard let asset = PHAsset.fetchAssets(withLocalIdentifiers: [id], options: nil).firstObject else { return }
            let options = PHImageRequestOptions()
            options.isNetworkAccessAllowed = false
            PHImageManager.default().requestImage(for: asset, targetSize: CGSize(width: size, height: size), contentMode: .aspectFit, options: options) { result, _ in
                DispatchQueue.main.async { image = result }
            }
        }
    }
}

private extension View {
    func card() -> some View { padding(16).background(Color.white.opacity(0.055), in: RoundedRectangle(cornerRadius: 20)).overlay(RoundedRectangle(cornerRadius: 20).stroke(mint.opacity(0.18))) }
}

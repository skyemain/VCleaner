import SwiftUI

struct PreviewResultsView: View {
    let openPhotos: () -> Void
    private let examples: [(String, String, String)] = [
        ("video", "Example large videos", "1.2 GB"),
        ("camera.viewfinder", "Example screenshots", "720 MB"),
        ("square.on.square", "Example duplicates", "480 MB")
    ]
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 24) {
                Text("SAMPLE RESULTS").font(.caption.bold()).tracking(2).foregroundStyle(HomeTheme.mint)
                Text("A glimpse of\nwhat’s possible.").font(.system(size: 34, weight: .semibold, design: .rounded))
                VStack(spacing: 16) {
                    Image(systemName: "sparkles").font(.system(size: 42, weight: .light)).foregroundStyle(HomeTheme.mint)
                    Text("2.4 GB").font(.system(size: 48, weight: .semibold, design: .rounded))
                    Text("Example space to review").foregroundStyle(HomeTheme.muted)
                    Text("Demonstration only. These amounts are invented. No photos or files were scanned, cleaned or removed.")
                        .font(.footnote).foregroundStyle(HomeTheme.muted).multilineTextAlignment(.center)
                }.frame(maxWidth: .infinity).glassCard()
                ForEach(examples, id: \.1) { example in
                    HStack(spacing: 16) {
                        Image(systemName: example.0).font(.title3).foregroundStyle(HomeTheme.mint)
                        Text(example.1).font(.subheadline)
                        Spacer()
                        Text(example.2).font(.subheadline.monospacedDigit()).foregroundStyle(HomeTheme.muted)
                    }.glassCard()
                }
                Button(action: openPhotos) {
                    Label("Review real Photos", systemImage: "photo.on.rectangle")
                        .font(.headline).frame(maxWidth: .infinity).padding(12)
                }.buttonStyle(.borderedProminent).tint(HomeTheme.mint).foregroundStyle(HomeTheme.ink)
                Text("The real tool asks for Photos access and lets you select and confirm every deletion.").font(.caption).foregroundStyle(HomeTheme.muted)
            }.padding(22).frame(maxWidth: 650).frame(maxWidth: .infinity)
        }.background(HomeTheme.background).navigationTitle("Scanning preview").navigationBarTitleDisplayMode(.inline).toolbar(.visible, for: .navigationBar)
            .accessibilityIdentifier("previewResults")
    }
}

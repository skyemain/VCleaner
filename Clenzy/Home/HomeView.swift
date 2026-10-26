import SwiftUI
import UIKit

struct HomeView: View {
    @Bindable var model: HomeViewModel
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        NavigationStack(path: $model.navigationPath) {
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    header
                    StorageCard(storage: model.device.storage)
                    BatteryCard(battery: model.device.battery, illustrativeMinutes: model.batteryTimeMinutes)
                    Button(action: model.openPhotos) {
                        HStack(spacing: 14) {
                            Image(systemName: "photo.on.rectangle.angled").font(.title2).foregroundStyle(HomeTheme.mint)
                            VStack(alignment: .leading, spacing: 5) {
                                Text("Review Photos").font(.headline).foregroundStyle(.white)
                                Text("Real cleanup, with your approval").font(.caption).foregroundStyle(HomeTheme.muted)
                            }
                            Spacer()
                            Image(systemName: "arrow.up.right").foregroundStyle(HomeTheme.muted)
                        }.glassCard()
                    }.buttonStyle(.plain).accessibilityIdentifier("reviewPhotosButton")
                    Label("Your photos stay in your control.", systemImage: "lock.shield")
                        .font(.caption).foregroundStyle(HomeTheme.muted).frame(maxWidth: .infinity).padding(.vertical, 8)
                }.padding(.horizontal, 22).padding(.bottom, 24).frame(maxWidth: 650).frame(maxWidth: .infinity)
            }
            .background(HomeTheme.background)
            .safeAreaInset(edge: .bottom, spacing: 0) {
                Group {
                    if model.isScanning {
                        ScanPreviewCard(progress: model.scanProgress, caption: model.scanCaption, reduceMotion: reduceMotion, cancel: model.cancelScanPreview)
                    } else {
                        cleanButton
                    }
                }.padding(.horizontal, 22).padding(.top, 12).padding(.bottom, 8)
                    .background(.ultraThinMaterial)
            }
            .toolbar(.hidden, for: .navigationBar)
            .refreshable { model.refreshDevice() }
            .navigationDestination(for: HomeViewModel.Destination.self) { destination in
                switch destination {
                case .previewResults: PreviewResultsView(openPhotos: model.openPhotos)
                case .photos: PhotosReviewView()
                }
            }
            .onAppear { model.refreshDevice() }
            .onDisappear { model.cancelScanPreview() }
            .onReceive(NotificationCenter.default.publisher(for: UIDevice.batteryLevelDidChangeNotification)) { _ in model.refreshDevice() }
            .onReceive(NotificationCenter.default.publisher(for: UIDevice.batteryStateDidChangeNotification)) { _ in model.refreshDevice() }
            .onReceive(NotificationCenter.default.publisher(for: UIApplication.didBecomeActiveNotification)) { _ in model.refreshDevice() }
        }.preferredColorScheme(.dark).tint(HomeTheme.mint)
    }

    private var header: some View {
        HStack(alignment: .center) {
            VStack(alignment: .leading, spacing: 8) {
                HStack(spacing: 8) {
                    Image(systemName: "sparkle").foregroundStyle(HomeTheme.mint)
                    Text("CLENZY").tracking(4).font(.system(.caption, design: .rounded, weight: .bold)).foregroundStyle(HomeTheme.mint)
                }
                Text("A little lighter.").font(.system(size: 34, weight: .semibold, design: .rounded)).tracking(-1)
                Text("A clear view of your device.").font(.subheadline).foregroundStyle(HomeTheme.muted)
            }
            Spacer(minLength: 4)
            Image(systemName: "iphone.gen3").font(.system(size: 24, weight: .light)).foregroundStyle(HomeTheme.mint)
                .frame(width: 52, height: 52).background(.ultraThinMaterial, in: Circle()).overlay(Circle().stroke(.white.opacity(0.1)))
        }.padding(.top, 26).padding(.bottom, 6)
    }

    private var cleanButton: some View {
        VStack(spacing: 10) {
            Button(action: model.startScanPreview) {
                HStack(spacing: 12) {
                    Image(systemName: "sparkles")
                    Text("Clean Storage").fontWeight(.semibold)
                    Spacer()
                    Image(systemName: "arrow.right")
                }.font(.headline).foregroundStyle(HomeTheme.ink).padding(22).frame(maxWidth: .infinity)
                    .background(LinearGradient(colors: [HomeTheme.mint, Color(red: 0.29, green: 0.88, blue: 0.88)], startPoint: .topLeading, endPoint: .bottomTrailing), in: RoundedRectangle(cornerRadius: 24, style: .continuous))
                    .shadow(color: HomeTheme.mint.opacity(0.13), radius: 20, y: 7)
            }.buttonStyle(.plain).accessibilityIdentifier("cleanStorageButton")
            Text("Try the scanning preview. No files will be changed.").font(.caption).foregroundStyle(HomeTheme.muted)
        }.padding(.vertical, 3)
    }
}

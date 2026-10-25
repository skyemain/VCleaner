import AppKit
import SwiftUI

struct AccessGuideView: View {
    @ObservedObject var model: MainViewModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack {
                Text("[ CLEANING ACCESS ]").font(.system(size: 13, weight: .semibold, design: .monospaced))
                    .foregroundStyle(OpsTheme.mint)
                Spacer()
                Button("Close") { dismiss() }
            }
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    Text("A short setup for your first clean")
                        .font(.title2.weight(.semibold))
                    Text("Basic temporary and cache cleaning works with the access you already have. Safari and some protected caches may need Full Disk Access. macOS requires you to grant it yourself; CleanSlate cannot switch it on.")
                    step("1", "Install the copy you will use", "Drag CleanSlate from the disk image to Applications, eject the disk image, and open Applications → CleanSlate. Grant access to that copy, not the one inside the disk image.")
                    step("2", "Allow protected cache access", "Open System Settings → Privacy & Security → Full Disk Access. Use + to add Applications → CleanSlate, then turn its switch on. This permits access to sensitive files; CleanSlate’s cleanup rules still restrict deletion to its cache allowlist.")
                    HStack {
                        Button("Open Full Disk Access") { PermissionService.openPrivacySettings() }
                            .tint(OpsTheme.mint)
                        Button("Show this app in Finder") {
                            NSWorkspace.shared.activateFileViewerSelecting([Bundle.main.bundleURL])
                        }
                    }.buttonStyle(.bordered)
                    step("3", "Quit and reopen CleanSlate", "After changing access, quit CleanSlate completely (⌘Q), reopen the installed copy, then click Recheck access here. A switch that is on is not proof that every folder is accessible.")
                    step("4", "For Safari: quit the browser too", "Use Safari → Quit Safari (⌘Q), not just the red window button. Alternatively, approve Quit and clean when CleanSlate asks. If macOS asks whether CleanSlate may access another app’s data, allow it only if you want that cache cleaned.")
                    Text("Still blocked?").font(.headline)
                    Text("If Full Disk Access is already on, remove the old CleanSlate entry, add the installed copy again, and relaunch it. Check the paths below. Ownership, files in use and macOS system protection can still prevent cleaning. Do not disable SIP or change folder permissions. You can continue and leave blocked items alone.")
                    Text("Other features").font(.headline)
                    Text("Start at login is optional: allow CleanSlate under General → Login Items if macOS requests approval. Downloads access is needed only for the optional large-file report. RAM monitoring and normal app quitting do not need Full Disk Access. Accessibility, Screen Recording and administrator access are not required for cache cleaning.")
                    HStack {
                        Text("Current folder access").font(.headline)
                        Spacer()
                        Button(model.checkingPermissions ? "Checking…" : "Recheck access") {
                            Task { await model.recheckPermissions() }
                        }.disabled(model.checkingPermissions)
                    }
                    Text("These checks list folder names and test access; they do not read browsing data or delete files. “Not present” is normal for unused cache locations. Nested files may have different access.")
                        .font(.caption).foregroundStyle(OpsTheme.secondary)
                    ForEach(model.permissionChecks) { item in
                        HStack(alignment: .top, spacing: 12) {
                            VStack(alignment: .leading, spacing: 3) {
                                Text(item.title).font(.system(size: 12, weight: .medium))
                                Text(item.path).font(.system(size: 10, design: .monospaced))
                                    .foregroundStyle(OpsTheme.secondary).textSelection(.enabled)
                            }
                            Spacer()
                            Text(item.access.rawValue).font(.system(size: 11))
                                .foregroundStyle(item.access == .blocked || item.access == .unknown ? OpsTheme.amber : OpsTheme.secondary)
                        }
                        Divider()
                    }
                }
            }
            Button("Continue with available access") { model.continueFromAccessGuide() }
                .buttonStyle(.borderedProminent).tint(OpsTheme.mintDeep)
        }
        .padding(24).frame(width: 660, height: 650)
        .background(OpsTheme.background).foregroundStyle(OpsTheme.text)
    }

    private func step(_ number: String, _ title: String, _ text: String) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Text(number).font(.system(size: 12, weight: .bold, design: .monospaced))
                .foregroundStyle(OpsTheme.mint).frame(width: 22)
            VStack(alignment: .leading, spacing: 5) {
                Text(title).font(.headline)
                Text(text).font(.system(size: 12)).foregroundStyle(OpsTheme.secondary)
            }
        }
    }
}

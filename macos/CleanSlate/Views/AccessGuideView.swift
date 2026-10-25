import AppKit
import SwiftUI

struct AccessGuideView: View {
    @ObservedObject var model: MainViewModel
    var embedded = false
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    Text("Allow cleaning access").font(.title2.weight(.semibold))
                    Text("For each blocked folder, click Choose folder and approve that exact folder in the macOS picker. No files are deleted during setup. If macOS still denies access, use Full Disk Access below and restart the installed copy.")
                        .font(.system(size: 12)).foregroundStyle(OpsTheme.secondary)
                    HStack {
                        Button("Open Full Disk Access") { PermissionService.openPrivacySettings() }
                        Button("Show this app") { NSWorkspace.shared.activateFileViewerSelecting([Bundle.main.bundleURL]) }
                        Spacer()
                        Button(model.checkingPermissions ? "Checking…" : "Recheck") {
                            Task { await model.recheckPermissions() }
                        }.disabled(model.checkingPermissions || model.grantingAccess)
                    }.buttonStyle(.bordered)
                    Text("Running copy: \(Bundle.main.bundleURL.path)")
                        .font(.system(size: 10, design: .monospaced)).foregroundStyle(OpsTheme.secondary).textSelection(.enabled)
                    if !model.accessMessage.isEmpty {
                        Text(model.accessMessage).font(.system(size: 12)).foregroundStyle(OpsTheme.mint).textSelection(.enabled)
                    }
                    Text("Folder checks list names only. A readable folder may still contain locked or protected files; deletion is checked separately.")
                        .font(.caption).foregroundStyle(OpsTheme.secondary)
                    ForEach(model.permissionChecks) { item in
                        HStack(alignment: .top, spacing: 12) {
                            VStack(alignment: .leading, spacing: 3) {
                                Text(item.title).font(.system(size: 12, weight: .medium))
                                Text(item.path).font(.system(size: 10, design: .monospaced))
                                    .foregroundStyle(OpsTheme.secondary).textSelection(.enabled)
                            }
                            Spacer()
                            VStack(alignment: .trailing, spacing: 6) {
                                Text(item.access.rawValue).font(.system(size: 11))
                                    .foregroundStyle(item.access == .blocked || item.access == .unknown ? OpsTheme.amber : OpsTheme.secondary)
                                if item.access == .blocked || item.access == .unknown {
                                    Button("Choose folder…") { model.grantFolderAccess(item) }
                                        .buttonStyle(.bordered).disabled(model.grantingAccess || model.checkingPermissions)
                                }
                            }
                        }
                        if item.access == .blocked || item.access == .unknown {
                            Text(item.detail).font(.system(size: 10)).foregroundStyle(OpsTheme.amber).textSelection(.enabled)
                        }
                        Divider()
                    }
                    DisclosureGroup("Full Disk Access, Safari and reinstall help") {
                        VStack(alignment: .leading, spacing: 12) {
                            step("1", "Install from the new DMG", "Quit CleanSlate, drag the new app from the DMG to Applications and choose Replace in Finder. Eject the DMG and open CleanSlate from Applications. The app opens directly to the dashboard; this access guide is optional.")
                            step("2", "Allow the installed copy", "In System Settings → Privacy & Security → Full Disk Access, remove an older CleanSlate entry, add Applications → CleanSlate and enable it. Quit and reopen CleanSlate, then Recheck. These builds are ad-hoc signed; grants may need renewal after updates.")
                            step("3", "For Safari", "Quit Safari with ⌘Q before cleaning. Allow the macOS prompt for Safari’s app data if you want its cache cleaned. Only the two cache folders above are targets, not cookies or history.")
                            Text("An EPERM error can come from privacy or other macOS protection; EACCES points to file permissions or ACLs. A folder-picker grant cannot override every macOS protection. Do not change ownership, disable SIP or modify the privacy database. Manual Clean permanently deletes eligible Trash files. Login cleans leave Trash alone.")
                                .font(.caption).foregroundStyle(OpsTheme.secondary)
                        }.padding(.top, 8)
                    }
                }
            }
            Button("Done") { model.closeAccessGuide() }
                .buttonStyle(.borderedProminent).tint(OpsTheme.mintDeep)
        }
        .padding(20).frame(maxWidth: .infinity, maxHeight: .infinity)
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

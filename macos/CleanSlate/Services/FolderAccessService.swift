import AppKit
import Foundation

/// Access is granted only by the person selecting a specific cleanup folder in macOS's picker.
/// Holding these scopes open also covers cleanup work performed on the engine's actor.
@MainActor
final class FolderAccessService {
    private let defaults: UserDefaults
    private let locations: Locations
    private var active: [String: URL] = [:]
    private let key = "cleaningFolderBookmarks.v1"

    init(defaults: UserDefaults = .standard, locations: Locations = .currentUser()) {
        self.defaults = defaults
        self.locations = locations
        restore()
    }

    nonisolated static func accepts(_ selected: URL, expected: URL) -> Bool {
        selected.isFileURL
            && (try? selected.resourceValues(forKeys: [.isSymbolicLinkKey]))?.isSymbolicLink != true
            && (try? expected.resourceValues(forKeys: [.isSymbolicLinkKey]))?.isSymbolicLink != true
            && selected.standardizedFileURL.resolvingSymlinksInPath().path == expected.standardizedFileURL.resolvingSymlinksInPath().path
    }

    func choose(_ check: PermissionService.Check) async -> String {
        guard let root = PermissionService.roots(locations: locations).first(where: { $0.1.path == check.id })?.1 else {
            return "This folder is not a cleaning target."
        }
        let panel = NSOpenPanel()
        panel.title = "Allow access to \(check.title)"
        panel.message = "Select this exact folder: \(check.path). This grants folder access only; no files are deleted now. Press ⌘⇧G and paste the path if needed."
        panel.prompt = "Allow folder"
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = false
        panel.allowsMultipleSelection = false
        panel.showsHiddenFiles = true
        panel.directoryURL = root
        let response = await panel.begin()
        guard response == .OK, let url = panel.url else { return "Folder selection cancelled. No cleaning was started." }
        guard Self.accepts(url, expected: root) else { return "Choose the exact folder shown, not its parent or a different folder. No cleaning was started." }
        if active[root.path] == nil, url.startAccessingSecurityScopedResource() { active[root.path] = url }
        let probe = PermissionService.probe(root)
        guard probe.access == .available else {
            return "macOS still blocks this folder after selection. \(probe.detail) Use Full Disk Access for this installed copy, quit and reopen it, then recheck."
        }
        do {
            let data = try url.bookmarkData(options: .withSecurityScope, includingResourceValuesForKeys: nil, relativeTo: nil)
            var saved = defaults.dictionary(forKey: key) as? [String: Data] ?? [:]
            saved[root.path] = data
            defaults.set(saved, forKey: key)
            return "Folder access verified and saved. Run Clean again when ready."
        } catch {
            return "Folder access verified for this session. macOS could not save the grant; you may need to choose it again after reopening. Run Clean when ready."
        }
    }

    private func restore() {
        let saved = defaults.dictionary(forKey: key) as? [String: Data] ?? [:]
        let allowed = PermissionService.roots(locations: locations).map(\.1)
        for (path, data) in saved {
            guard let expected = allowed.first(where: { $0.path == path }) else { continue }
            var stale = false
            guard let url = try? URL(resolvingBookmarkData: data, options: [.withSecurityScope, .withoutUI, .withoutMounting],
                relativeTo: nil, bookmarkDataIsStale: &stale), Self.accepts(url, expected: expected) else { continue }
            if url.startAccessingSecurityScopedResource() { active[path] = url }
            if stale, let refreshed = try? url.bookmarkData(options: .withSecurityScope, includingResourceValuesForKeys: nil, relativeTo: nil) {
                var updated = defaults.dictionary(forKey: key) as? [String: Data] ?? [:]
                updated[path] = refreshed
                defaults.set(updated, forKey: key)
            }
        }
    }
}

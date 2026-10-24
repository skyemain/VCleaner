import Foundation

struct LaunchAgentFinding: Equatable, Sendable, Identifiable {
    var id: String { path }
    var path: String
    var executable: String
    var summary: String
}

enum LaunchAgentScanner {
    /// Read-only. A finding means the plist names an absolute executable that is not on disk.
    /// Shell commands and paths that are not absolute are ignored so dynamic launchers are not flagged.
    static func brokenAgents(home: URL) -> [LaunchAgentFinding] {
        let folder = home.appendingPathComponent("Library/LaunchAgents")
        let fm = FileManager()
        let plists = (try? fm.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles])) ?? []
        return plists.filter { $0.pathExtension == "plist" }.compactMap { url in
            guard let data = try? Data(contentsOf: url),
                  let object = try? PropertyListSerialization.propertyList(from: data, format: nil) as? [String: Any] else {
                return nil
            }
            guard let executable = executablePath(in: object) else { return nil }
            guard executable.hasPrefix("/") else { return nil }
            if isShell(executable) { return nil }
            if fm.fileExists(atPath: executable) { return nil }
            return LaunchAgentFinding(
                path: url.path,
                executable: executable,
                summary: "\(url.lastPathComponent) points to a missing program."
            )
        }
    }

    /// Copies the plist into CleanSlate's backup folder and removes the original so launchd stops loading it.
    /// The copy is the backup. This never runs unless a person confirms that one finding.
    static func quarantine(_ finding: LaunchAgentFinding, home: URL) throws -> URL {
        let source = URL(fileURLWithPath: finding.path)
        let backupRoot = home
            .appendingPathComponent("Library/Application Support/CleanSlate/Backups", isDirectory: true)
        try FileManager.default.createDirectory(at: backupRoot, withIntermediateDirectories: true)
        let destination = backupRoot.appendingPathComponent(source.lastPathComponent)
        if FileManager.default.fileExists(atPath: destination.path) {
            try FileManager.default.removeItem(at: destination)
        }
        try FileManager.default.copyItem(at: source, to: destination)
        try FileManager.default.removeItem(at: source)
        return destination
    }

    private static func executablePath(in plist: [String: Any]) -> String? {
        if let program = plist["Program"] as? String, !program.isEmpty {
            return program
        }
        if let arguments = plist["ProgramArguments"] as? [String], let first = arguments.first, !first.isEmpty {
            return first
        }
        return nil
    }

    private static func isShell(_ path: String) -> Bool {
        let shells = ["/bin/sh", "/bin/bash", "/bin/zsh", "/usr/bin/env", "/bin/csh"]
        return shells.contains(path)
    }
}

struct LeftoverCandidate: Equatable, Sendable, Identifiable {
    var id: String { path }
    var path: String
    var note: String
}

enum LeftoverScanner {
    /// Informational only. A folder under Application Support with no matching .app in the usual
    /// application folders might still belong to a tool installed somewhere else, so nothing here is deletable.
    static func candidates(home: URL, applicationFolders: [URL]) -> [LeftoverCandidate] {
        let support = home.appendingPathComponent("Library/Application Support")
        let fm = FileManager()
        let installed = applicationNames(in: applicationFolders, fileManager: fm)
        let folders = (try? fm.contentsOfDirectory(at: support, includingPropertiesForKeys: [.isDirectoryKey], options: [.skipsHiddenFiles])) ?? []
        return folders.compactMap { url in
            var isDirectory: ObjCBool = false
            guard fm.fileExists(atPath: url.path, isDirectory: &isDirectory), isDirectory.boolValue else { return nil }
            let name = url.lastPathComponent
            if installed.contains(name.lowercased()) || name == "CleanSlate" { return nil }
            return LeftoverCandidate(
                path: url.path,
                note: "No application named \(name) was found in the checked application folders. This is not removed."
            )
        }
    }

    private static func applicationNames(in folders: [URL], fileManager: FileManager) -> Set<String> {
        var names = Set<String>()
        for folder in folders {
            let apps = (try? fileManager.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles])) ?? []
            for app in apps where app.pathExtension == "app" {
                names.insert(app.deletingPathExtension().lastPathComponent.lowercased())
            }
        }
        return names
    }
}

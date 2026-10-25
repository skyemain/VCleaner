import AppKit
import Foundation

@MainActor
enum InstallationService {
    nonisolated static let bundleID = "com.skyemain.CleanSlate"
    static let destination = URL(fileURLWithPath: "/Applications/CleanSlate.app", isDirectory: true)
    static var isInstalledCopy: Bool { Bundle.main.bundleURL.standardizedFileURL.path == destination.path }

    struct Failure: LocalizedError {
        let message: String
        var errorDescription: String? { message }
    }

    nonisolated static func canReplace(_ url: URL, expectedBundleID: String = bundleID) -> Bool {
        guard (try? url.resourceValues(forKeys: [.isSymbolicLinkKey]))?.isSymbolicLink != true else { return false }
        return Bundle(url: url)?.bundleIdentifier == expectedBundleID
    }

    /// Copies the new bundle before touching the old one, rolls back a failed replacement,
    /// and removes only validated old CleanSlate bundles. User settings and privacy decisions stay intact.
    static func install() async throws -> URL {
        let source = Bundle.main.bundleURL
        guard !isInstalledCopy else {
            throw Failure(message: "Open the new copy from the downloaded DMG to repair this installation.")
        }
        try verifySignature(source)
        let olderApps = NSRunningApplication.runningApplications(withBundleIdentifier: bundleID).filter { $0.processIdentifier != getpid() }
        for app in olderApps { _ = app.terminate() }
        for _ in 0..<40 {
            if olderApps.allSatisfy(\.isTerminated) { break }
            try await Task.sleep(nanoseconds: 250_000_000)
        }
        guard olderApps.allSatisfy(\.isTerminated) else {
            throw Failure(message: "An older CleanSlate copy is still running. Quit it normally and try again. Nothing was replaced.")
        }
        return try replaceBundle(source: source, destination: destination,
            legacy: FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Applications/CleanSlate.app"),
            verify: verifySignature)
    }

    nonisolated static func replaceBundle(source: URL, destination: URL, legacy: URL?,
        fileManager fm: FileManager = FileManager(), verify: (URL) throws -> Void) throws -> URL {
        let staging = destination.deletingLastPathComponent().appendingPathComponent(".CleanSlate-install-\(UUID().uuidString).app")
        let backup = destination.deletingLastPathComponent().appendingPathComponent(".CleanSlate-previous-\(UUID().uuidString)")
        if fm.fileExists(atPath: destination.path), !canReplace(destination) {
            throw Failure(message: "Applications/CleanSlate.app is not a CleanSlate bundle. It was left untouched.")
        }
        guard source.standardizedFileURL.path != destination.standardizedFileURL.path, canReplace(source) else {
            throw Failure(message: "Open the new CleanSlate copy from the downloaded DMG to install.")
        }
        for existing in [destination] + (legacy.map { [$0] } ?? []) where canReplace(existing) {
            if let installed = Bundle(url: existing)?.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String,
               let incoming = Bundle(url: source)?.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String,
               incoming.compare(installed, options: .numeric) == .orderedAscending {
                throw Failure(message: "A newer version (\(installed)) is already installed at \(existing.path). This older copy was not installed.")
            }
        }
        try verify(source)
        do {
            try fm.copyItem(at: source, to: staging)
            try verify(staging)
        } catch {
            try? fm.removeItem(at: staging)
            throw Failure(message: "Could not stage the app in Applications: \(error.localizedDescription). You can drag the new app to Applications in Finder if macOS requires administrator approval.")
        }
        var backedUp = false
        do {
            if fm.fileExists(atPath: destination.path) {
                try fm.moveItem(at: destination, to: backup)
                backedUp = true
            }
            try fm.moveItem(at: staging, to: destination)
        } catch {
            try? fm.removeItem(at: staging)
            if backedUp {
                do { try fm.moveItem(at: backup, to: destination) }
                catch { throw Failure(message: "Replacement failed. The previous app is preserved at \(backup.path). Restore it in Finder.") }
            }
            throw Failure(message: "The app could not be replaced. The previous installation was preserved: \(error.localizedDescription)")
        }
        if backedUp {
            do { try fm.removeItem(at: backup) }
            catch { throw Failure(message: "The new app is installed, but the old backup could not be removed: \(backup.path). Open Applications/CleanSlate.app.") }
        }
        // A legacy per-user installation can otherwise keep launching the old version.
        if let legacy, legacy.standardizedFileURL.path != source.standardizedFileURL.path, fm.fileExists(atPath: legacy.path), canReplace(legacy) {
            do { try fm.removeItem(at: legacy) }
            catch { throw Failure(message: "The new app is installed. Remove the older copy at \(legacy.path) in Finder, then open Applications/CleanSlate.app.") }
        }
        return destination
    }

    nonisolated private static func verifySignature(_ url: URL) throws {
        let task = Process()
        task.executableURL = URL(fileURLWithPath: "/usr/bin/codesign")
        task.arguments = ["--verify", "--deep", "--strict", url.path]
        task.standardOutput = FileHandle.nullDevice
        task.standardError = FileHandle.nullDevice
        try task.run()
        task.waitUntilExit()
        guard task.terminationStatus == 0 else { throw Failure(message: "The app’s code signature failed verification. Download a fresh DMG.") }
    }
}

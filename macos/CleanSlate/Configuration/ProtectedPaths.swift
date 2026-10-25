import Foundation

/// Decides which paths the deletion service is allowed to touch.
///
/// A path may be deleted only when it sits strictly inside an approved cache root for this run.
/// Broad folders such as Downloads, Documents, Keychains, Safari's profile, and Xcode archives are never approved roots.
enum ProtectedPaths {
    /// File names that are never junk, even inside an otherwise approved cache directory.
    static let reservedFileNames: Set<String> = [
        "Cookies", "Cookies-journal",
        "Login Data", "Login Data-journal", "Login Data For Account", "Login Data For Account-journal",
        "History", "History-journal",
        "Bookmarks", "Bookmarks.bak",
        "Favicons", "Favicons-journal",
        "Web Data", "Web Data-journal",
        "Preferences", "Secure Preferences",
        "Extension Cookies", "Extension Cookies-journal",
        "Account Web Data", "Account Web Data-journal",
        "Network Persistent State", "TransportSecurity",
        "Visited Links", "Top Sites", "Shortcuts",
        "Current Session", "Current Tabs", "Last Session", "Last Tabs"
    ]

    static let reservedDirectoryNames: Set<String> = [
        "Local Storage", "Session Storage", "IndexedDB", "Sessions", "databases",
        "Platform Notifications", "Service Worker", "Storage"
    ]

    /// Cache directories Safari and WebKit actually fill. The container itself, cookies, and the Safari profile stay refused.
    static let safariCacheRelativeRoots: [String] = [
        "Library/Caches/com.apple.Safari",
        "Library/Containers/com.apple.Safari/Data/Library/Caches"
    ]

    /// Rebuildable Apple service caches only; never sweep all application caches.
    static let systemCacheRelativeRoots = [
        "Library/Caches/com.apple.helpd",
        "Library/Caches/com.apple.parsecd",
        "Library/Caches/com.apple.touristd"
    ]

    /// Relative to the home directory. These trees are refused as approved roots.
    static let forbiddenRelativeRoots: [String] = [
        "Documents", "Desktop", "Downloads", "Pictures", "Movies", "Music",
        "Library/Keychains", "Library/Preferences", "Library/Mail", "Library/Messages",
        "Library/Safari", "Library/Accounts", "Library/Cookies",
        "Library/Containers", "Library/Group Containers",
        "Library/Photos", "Library/Mobile Documents",
        "Library/Developer/Xcode/Archives",
        "Library/Developer/Xcode/UserData",
        "Library/Developer/CoreSimulator",
        ".ssh", ".gnupg"
    ]

    static func isReserved(_ url: URL) -> Bool {
        let name = url.lastPathComponent
        if reservedFileNames.contains(name) || reservedDirectoryNames.contains(name) {
            return true
        }
        return url.pathComponents.contains { reservedDirectoryNames.contains($0) }
    }

    /// An approved root is a specific cache directory, never a broad folder such as `~/Library` or `~/Downloads`.
    static func isApprovedRoot(_ url: URL, home: URL, temporaryDirectory: URL, allowXcodeDerivedData: Bool, allowTrash: Bool) -> Bool {
        let path = standardized(url)
        let homePath = standardized(home)
        let temp = standardized(temporaryDirectory)
        if path == temp || path.hasPrefix(temp + "/") {
            return !isForbiddenLocation(temp, home: home)
        }
        guard path.hasPrefix(homePath + "/") else {
            return false
        }
        if safariCacheRelativeRoots.contains(where: { isSpecificCache(path, home: home, prefix: $0) }) {
            return true
        }
        if isInside(path, home: home, relative: "Downloads")
            || isInside(path, home: home, relative: "Documents")
            || isInside(path, home: home, relative: "Desktop")
            || isInside(path, home: home, relative: "Library/Keychains")
            || isInside(path, home: home, relative: "Library/Safari")
            || isInside(path, home: home, relative: "Library/Containers")
            || isInside(path, home: home, relative: "Library/Group Containers")
            || isInside(path, home: home, relative: ".ssh") {
            return false
        }
        if path == homePath || path == standardized(home.appendingPathComponent("Library")) {
            return false
        }
        let trash = standardized(home.appendingPathComponent(".Trash"))
        if allowTrash, path == trash || path.hasPrefix(trash + "/") {
            return true
        }
        if systemCacheRelativeRoots.contains(where: { isSpecificCache(path, home: home, prefix: $0) }) { return true }
        if isSpecificCache(path, home: home, prefix: "Library/Logs") { return true }
        if chromiumCache(path, home: home, support: "Library/Application Support/Google/Chrome") { return true }
        if chromiumCache(path, home: home, support: "Library/Caches/Google/Chrome") { return true }
        if allowXcodeDerivedData, isInside(path, home: home, relative: "Library/Developer/Xcode/DerivedData"), path != standardized(home.appendingPathComponent("Library/Developer/Xcode/DerivedData")) {
            return true
        }
        return false
    }

    private static func isForbiddenLocation(_ path: String, home: URL) -> Bool {
        forbiddenRelativeRoots.contains { isInside(path, home: home, relative: $0) || path == standardized(home.appendingPathComponent($0)) }
    }

    static func standardized(_ url: URL) -> String {
        url.standardizedFileURL.resolvingSymlinksInPath().path
    }

    private static func isInside(_ path: String, home: URL, relative: String) -> Bool {
        let root = standardized(home.appendingPathComponent(relative))
        return path == root || path.hasPrefix(root + "/")
    }

    private static func isSpecificCache(_ path: String, home: URL, prefix: String) -> Bool {
        let root = standardized(home.appendingPathComponent(prefix))
        return path == root || path.hasPrefix(root + "/")
    }

    /// `.../<browser>/<profile>/<known cache directory>` and nothing shallower.
    private static func chromiumCache(_ path: String, home: URL, support: String) -> Bool {
        let root = standardized(home.appendingPathComponent(support))
        guard path.hasPrefix(root + "/") else { return false }
        let remainder = path.dropFirst(root.count + 1)
        let parts = remainder.split(separator: "/").map(String.init)
        guard parts.count >= 2 else { return false }
        let cacheNames: Set<String> = [
            "Cache", "Code Cache", "GPUCache", "DawnGraphiteCache", "DawnWebGPUCache", "ShaderCache", "GrShaderCache"
        ]
        return cacheNames.contains(parts[1])
    }
}

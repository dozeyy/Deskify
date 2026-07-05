import AppKit
import DeskifyCore

/// An installed application discovered on disk.
struct InstalledApp {
    let name: String
    let path: String
}

/// Enumerates installed applications the same way macOS itself would list
/// them — the standard Applications folders (system, local, and per-user) —
/// so adding an app to a project is picking from a list instead of hunting
/// through the file system. Browse still covers anything the scan misses
/// (apps in unusual locations, developer builds, etc).
enum InstalledAppsScanner {
    private static var roots: [String] {
        [
            "/Applications",
            "/System/Applications",
            "/System/Applications/Utilities",
            ("~/Applications" as NSString).expandingTildeInPath,
        ]
    }

    static func scan() -> [InstalledApp] {
        var byPath: [String: InstalledApp] = [:]
        for root in roots {
            collect(from: root, depth: 0, into: &byPath)
        }

        return byPath.values
            .filter { !ProtectedApps.isProtected(path: $0.path, name: $0.name) && !InstalledAppFilter.isExcluded($0) }
            // The same product can appear twice (e.g. a copy in a vendor
            // subfolder) — keep one row per name, preferring the shorter,
            // usually top-level, path.
            .reduce(into: [String: InstalledApp]()) { unique, app in
                let key = app.name.lowercased()
                if let existing = unique[key], existing.path.count <= app.path.count { return }
                unique[key] = app
            }
            .values
            .sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
    }

    /// Walks a directory, collecting .app bundles. Vendors sometimes install
    /// into a subfolder (/Applications/Adobe …/), so recurse two levels — but
    /// never into a bundle itself.
    private static func collect(from directory: String, depth: Int, into result: inout [String: InstalledApp]) {
        guard depth <= 2 else { return }
        let fm = FileManager.default
        guard let entries = try? fm.contentsOfDirectory(atPath: directory) else { return }

        for entry in entries {
            let path = (directory as NSString).appendingPathComponent(entry)
            if entry.hasSuffix(".app") {
                let name = fm.displayName(atPath: path)
                result[path.lowercased()] = InstalledApp(
                    name: name.hasSuffix(".app") ? String(name.dropLast(4)) : name,
                    path: path)
            } else if !entry.hasPrefix(".") {
                var isDir: ObjCBool = false
                if fm.fileExists(atPath: path, isDirectory: &isDir), isDir.boolValue {
                    collect(from: path, depth: depth + 1, into: &result)
                }
            }
        }
    }
}

/// Decides whether a discovered install belongs in the "Add Application"
/// picker. macOS app folders are far cleaner than Windows' registry, but
/// uninstallers, updaters, and vendor helper apps still show up — none of
/// which belongs in a workspace layout. Heuristic, not exhaustive: favors
/// hiding obvious noise over risking a false positive on a real app.
enum InstalledAppFilter {
    private static let noiseSubstrings: [String] = [
        "uninstall", "installer", "install ", "updater", "update assistant",
        "crash reporter", "bug report", "diagnostic", "readme", "read me",
        "license", "eula", "helper",
    ]

    private static let exactNoiseNames: Set<String> = [
        "migration assistant", "boot camp assistant", "feedback assistant",
        "ticket viewer", "airport utility", "audio midi setup",
        "bluetooth file exchange", "colorsync utility", "console",
        "digital color meter", "grapher", "keychain access", "screen sharing",
        "system information", "voiceover utility", "wireless diagnostics",
    ]

    static func isExcluded(_ app: InstalledApp) -> Bool {
        let name = app.name.trimmingCharacters(in: .whitespaces).lowercased()
        if exactNoiseNames.contains(name) { return true }
        if noiseSubstrings.contains(where: { name.contains($0) }) { return true }
        return false
    }
}

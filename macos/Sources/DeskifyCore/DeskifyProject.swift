import Foundation

/// A saved workspace: apps, folders, urls and their window layout.
///
/// Serialized with the exact same JSON shape as the Windows version
/// (`%APPDATA%\Deskify\projects\*.json`), so the project format — field names,
/// launch order, layout semantics — is one format across platforms. Only the
/// paths inside differ (`.app` bundles instead of `.exe`s).
public final class DeskifyProject: Codable, Identifiable {
    public static let defaultOrder = ["apps", "urls", "folders"]

    public var name: String = "New Project"
    public var apps: [AppEntry] = []
    public var folders: [String] = []
    public var urls: [String] = []

    /// Optional group order, e.g. ["apps","urls","folders"]. Nil = default.
    public var launchOrder: [String]?

    /// Strict = verify + re-apply positions after launch; soft = best effort.
    public var strictLayout: Bool = false

    public var lastUsed: Date?

    /// Free-form reminders — "things to do" for this workspace.
    public var notes: String?

    /// Pinned projects sort first in the sidebar/project list.
    public var pinned: Bool = false

    /// Where this project lives on disk. Not serialized.
    public var filePath: URL?

    public let id = UUID()

    public init() {}

    public var effectiveLaunchOrder: [String] {
        if let order = launchOrder, !order.isEmpty { return order }
        return Self.defaultOrder
    }

    public var lastUsedText: String {
        guard let t = lastUsed else { return "Never launched" }
        let formatter = DateFormatter()
        formatter.dateFormat = "yyyy-MM-dd HH:mm"
        return "Last used \(formatter.string(from: t))"
    }

    public var summaryText: String {
        var parts: [String] = []
        if !apps.isEmpty { parts.append("\(apps.count) app\(apps.count == 1 ? "" : "s")") }
        if !urls.isEmpty { parts.append("\(urls.count) site\(urls.count == 1 ? "" : "s")") }
        if !folders.isEmpty { parts.append("\(folders.count) folder\(folders.count == 1 ? "" : "s")") }
        return parts.isEmpty ? "Empty project" : parts.joined(separator: "  ·  ")
    }

    public func clone() -> DeskifyProject {
        let data = (try? ProjectJSON.encoder.encode(self)) ?? Data()
        let copy = (try? ProjectJSON.decoder.decode(DeskifyProject.self, from: data)) ?? DeskifyProject()
        copy.filePath = filePath
        return copy
    }

    // MARK: Codable — tolerant of hand-edited/partial files, same as Windows.

    private enum CodingKeys: String, CodingKey {
        case name, apps, folders, urls, launchOrder, strictLayout, lastUsed, notes, pinned
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        name = try c.decodeIfPresent(String.self, forKey: .name) ?? "New Project"
        apps = try c.decodeIfPresent([AppEntry].self, forKey: .apps) ?? []
        folders = try c.decodeIfPresent([String].self, forKey: .folders) ?? []
        urls = try c.decodeIfPresent([String].self, forKey: .urls) ?? []
        launchOrder = try c.decodeIfPresent([String].self, forKey: .launchOrder)
        strictLayout = try c.decodeIfPresent(Bool.self, forKey: .strictLayout) ?? false
        lastUsed = try c.decodeIfPresent(Date.self, forKey: .lastUsed)
        notes = try c.decodeIfPresent(String.self, forKey: .notes)
        pinned = try c.decodeIfPresent(Bool.self, forKey: .pinned) ?? false
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(name, forKey: .name)
        try c.encode(apps, forKey: .apps)
        try c.encode(folders, forKey: .folders)
        try c.encode(urls, forKey: .urls)
        try c.encodeIfPresent(launchOrder, forKey: .launchOrder)
        try c.encode(strictLayout, forKey: .strictLayout)
        try c.encodeIfPresent(lastUsed, forKey: .lastUsed)
        try c.encodeIfPresent(notes, forKey: .notes)
        try c.encode(pinned, forKey: .pinned)
    }
}

public final class AppEntry: Codable, Identifiable {
    public var name: String = ""
    public var path: String = ""
    public var args: String?
    public var window: WindowLayout?

    /// True for Finder/browser entries auto-added alongside a folder or website —
    /// they exist so that window gets layout tracking, but they're opened via the
    /// Folders/Urls launch group, not launched independently (which would open a
    /// second, duplicate window).
    public var autoLinked: Bool = false

    public let id = UUID()

    public init(name: String = "", path: String = "", args: String? = nil,
                window: WindowLayout? = nil, autoLinked: Bool = false) {
        self.name = name
        self.path = path
        self.args = args
        self.window = window
        self.autoLinked = autoLinked
    }

    private enum CodingKeys: String, CodingKey { case name, path, args, window, autoLinked }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        name = try c.decodeIfPresent(String.self, forKey: .name) ?? ""
        path = try c.decodeIfPresent(String.self, forKey: .path) ?? ""
        args = try c.decodeIfPresent(String.self, forKey: .args)
        window = try c.decodeIfPresent(WindowLayout.self, forKey: .window)
        autoLinked = try c.decodeIfPresent(Bool.self, forKey: .autoLinked) ?? false
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(name, forKey: .name)
        try c.encode(path, forKey: .path)
        try c.encodeIfPresent(args, forKey: .args)
        try c.encodeIfPresent(window, forKey: .window)
        if autoLinked { try c.encode(autoLinked, forKey: .autoLinked) }
    }
}

/// Saved window placement. X/Y are points relative to the target monitor's
/// top-left corner, so layouts survive monitor rearrangement. `monitor` is
/// 0-based in left-to-right order — same semantics as the Windows format.
public struct WindowLayout: Codable, Equatable {
    public var monitor: Int
    public var x: Int
    public var y: Int
    public var width: Int
    public var height: Int
    /// "normal" or "maximized".
    public var state: String

    public init(monitor: Int, x: Int, y: Int, width: Int, height: Int, state: String = "normal") {
        self.monitor = monitor
        self.x = x
        self.y = y
        self.width = width
        self.height = height
        self.state = state
    }

    public var isMaximized: Bool { state.caseInsensitiveCompare("maximized") == .orderedSame }
}

/// Shared JSON coding for all project/settings files. Dates are ISO-8601 to
/// stay interchangeable with the Windows (System.Text.Json) files; decoding
/// accepts both fractional and whole-second timestamps.
public enum ProjectJSON {
    public static let encoder: JSONEncoder = {
        let e = JSONEncoder()
        e.outputFormatting = [.prettyPrinted, .withoutEscapingSlashes]
        e.dateEncodingStrategy = .custom { date, encoder in
            var c = encoder.singleValueContainer()
            try c.encode(isoFractional.string(from: date))
        }
        return e
    }()

    public static let decoder: JSONDecoder = {
        let d = JSONDecoder()
        d.dateDecodingStrategy = .custom { decoder in
            let c = try decoder.singleValueContainer()
            let s = try c.decode(String.self)
            if let date = isoFractional.date(from: s) ?? isoPlain.date(from: s) { return date }
            throw DecodingError.dataCorruptedError(in: c, debugDescription: "Unrecognized date: \(s)")
        }
        return d
    }()

    private static let isoFractional: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    private static let isoPlain: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f
    }()
}

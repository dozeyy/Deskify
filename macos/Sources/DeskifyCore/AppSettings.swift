import Foundation

public struct AppSettings: Codable {
    public var detectTimeoutSeconds: Int = 15
    public var retryIntervalMs: Int = 500
    public var strictLayoutDefault: Bool = false

    /// Grid size (points) used when "Snap" is on in the layout editor. 0 = no snapping.
    public var snapGridSize: Int = 16

    /// Color theme name — "Dark" (default) or "Light". Applies instantly.
    public var themeName: String = "Dark"

    /// Whether "Close Other Apps" shows a confirmation checklist first.
    /// False once the user checks "Don't ask again" on that dialog.
    public var confirmCloseOthers: Bool = true

    public init() {}

    /// ~/Library/Application Support/Deskify — the macOS counterpart of %APPDATA%\Deskify.
    public static var dataDir: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Deskify", isDirectory: true)
    }

    private static var settingsPath: URL { dataDir.appendingPathComponent("settings.json") }

    public static func load() -> AppSettings {
        do {
            let path = settingsPath
            if FileManager.default.fileExists(atPath: path.path) {
                var loaded = try ProjectJSON.decoder.decode(AppSettings.self, from: Data(contentsOf: path))
                // Sanitize hand-edited values.
                loaded.detectTimeoutSeconds = min(max(loaded.detectTimeoutSeconds, 1), 120)
                loaded.retryIntervalMs = min(max(loaded.retryIntervalMs, 100), 5000)
                loaded.snapGridSize = loaded.snapGridSize <= 0 ? 0 : min(max(loaded.snapGridSize, 2), 200)
                if loaded.themeName != "Dark" && loaded.themeName != "Light" { loaded.themeName = "Dark" }
                return loaded
            }
        } catch {
            Log.error("Failed to load settings, using defaults", error)
        }
        return AppSettings()
    }

    public func save() {
        do {
            try FileManager.default.createDirectory(at: Self.dataDir, withIntermediateDirectories: true)
            try ProjectJSON.encoder.encode(self).write(to: Self.settingsPath, options: .atomic)
        } catch {
            Log.error("Failed to save settings", error)
        }
    }

    // Tolerant decoding so partial/hand-edited files keep their defaults.
    private enum CodingKeys: String, CodingKey {
        case detectTimeoutSeconds, retryIntervalMs, strictLayoutDefault,
             snapGridSize, themeName, confirmCloseOthers
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        detectTimeoutSeconds = try c.decodeIfPresent(Int.self, forKey: .detectTimeoutSeconds) ?? 15
        retryIntervalMs = try c.decodeIfPresent(Int.self, forKey: .retryIntervalMs) ?? 500
        strictLayoutDefault = try c.decodeIfPresent(Bool.self, forKey: .strictLayoutDefault) ?? false
        snapGridSize = try c.decodeIfPresent(Int.self, forKey: .snapGridSize) ?? 16
        themeName = try c.decodeIfPresent(String.self, forKey: .themeName) ?? "Dark"
        confirmCloseOthers = try c.decodeIfPresent(Bool.self, forKey: .confirmCloseOthers) ?? true
    }
}

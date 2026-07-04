import Foundation

/// Simple append-only file log at ~/Library/Application Support/Deskify/deskify.log
/// (same role as %APPDATA%\Deskify\deskify.log on Windows). Never throws —
/// logging must not be able to take anything down.
public enum Log {
    private static let queue = DispatchQueue(label: "deskify.log", qos: .utility)
    private static var logPath: URL { AppSettings.dataDir.appendingPathComponent("deskify.log") }

    private static let stamp: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd HH:mm:ss"
        return f
    }()

    public static func info(_ message: String) { write("INFO", message) }

    public static func error(_ message: String, _ error: Error? = nil) {
        write("ERROR", error.map { "\(message): \($0.localizedDescription)" } ?? message)
    }

    private static func write(_ level: String, _ message: String) {
        queue.async {
            let line = "\(stamp.string(from: Date())) [\(level)] \(message)\n"
            guard let data = line.data(using: .utf8) else { return }
            do {
                let fm = FileManager.default
                try fm.createDirectory(at: AppSettings.dataDir, withIntermediateDirectories: true)
                if let handle = try? FileHandle(forWritingTo: logPath) {
                    defer { try? handle.close() }
                    try handle.seekToEnd()
                    try handle.write(contentsOf: data)
                } else {
                    try data.write(to: logPath)
                }
            } catch {
                // Best effort only.
            }
        }
    }
}

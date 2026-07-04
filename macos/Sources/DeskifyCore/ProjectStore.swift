import Foundation

/// JSON persistence: one file per project in
/// ~/Library/Application Support/Deskify/projects (human-editable, same format
/// as the Windows version's %APPDATA%\Deskify\projects).
public enum ProjectStore {
    public static var projectsDir: URL { AppSettings.dataDir.appendingPathComponent("projects", isDirectory: true) }

    public static func loadAll() -> [DeskifyProject] {
        var projects: [DeskifyProject] = []
        do {
            let fm = FileManager.default
            try fm.createDirectory(at: projectsDir, withIntermediateDirectories: true)
            let files = try fm.contentsOfDirectory(at: projectsDir, includingPropertiesForKeys: nil)
                .filter { $0.pathExtension.lowercased() == "json" }
            for file in files {
                do {
                    let project = try ProjectJSON.decoder.decode(DeskifyProject.self, from: Data(contentsOf: file))
                    project.filePath = file
                    projects.append(project)
                } catch {
                    Log.error("Skipping unreadable project file \(file.lastPathComponent)", error)
                }
            }
        } catch {
            Log.error("Failed to enumerate projects", error)
        }

        // Most recently used first, then alphabetical.
        projects.sort { a, b in
            switch (a.lastUsed, b.lastUsed) {
            case let (x?, y?) where x != y: return x > y
            case (.some, .none): return true
            case (.none, .some): return false
            default: return a.name.localizedCaseInsensitiveCompare(b.name) == .orderedAscending
            }
        }
        return projects
    }

    /// Writes the project file. Never throws — a failure here (locked file,
    /// full disk, permissions) shouldn't take down whatever the caller was in
    /// the middle of doing, and shouldn't look identical to a real success.
    @discardableResult
    public static func save(_ project: DeskifyProject) -> Bool {
        do {
            try FileManager.default.createDirectory(at: projectsDir, withIntermediateDirectories: true)
            if project.filePath == nil { project.filePath = uniquePath(for: project.name) }
            try ProjectJSON.encoder.encode(project).write(to: project.filePath!, options: .atomic)
            return true
        } catch {
            Log.error("Failed to save project \"\(project.name)\"", error)
            return false
        }
    }

    /// Deletes the project file. Returns false if the delete failed so the
    /// caller can keep the project in the list instead of showing it gone
    /// while the file still exists on disk.
    @discardableResult
    public static func delete(_ project: DeskifyProject) -> Bool {
        do {
            if let path = project.filePath, FileManager.default.fileExists(atPath: path.path) {
                try FileManager.default.removeItem(at: path)
            }
            project.filePath = nil
            return true
        } catch {
            Log.error("Failed to delete project \"\(project.name)\"", error)
            return false
        }
    }

    private static func uniquePath(for name: String) -> URL {
        let slug = slug(name)
        var path = projectsDir.appendingPathComponent(slug + ".json")
        var n = 2
        while FileManager.default.fileExists(atPath: path.path) {
            path = projectsDir.appendingPathComponent("\(slug)-\(n).json")
            n += 1
        }
        return path
    }

    private static func slug(_ name: String) -> String {
        var slug = String(name.trimmingCharacters(in: .whitespaces).lowercased()
            .map { $0.isLetter || $0.isNumber ? $0 : "-" })
        while slug.contains("--") { slug = slug.replacingOccurrences(of: "--", with: "-") }
        slug = slug.trimmingCharacters(in: CharacterSet(charactersIn: "-"))
        return slug.isEmpty ? "project" : slug
    }
}

/// Autosaves an in-progress wizard session (new project or edit) when the app
/// closes mid-edit, so nothing typed is lost. One draft at a time — restored
/// into the wizard on next startup, then cleared. Saving or cancelling the
/// wizard normally also clears it, so drafts never resurrect stale edits.
public enum DraftStore {
    private static var draftPath: URL { AppSettings.dataDir.appendingPathComponent("draft.json") }

    public struct Draft: Codable {
        public var project: DeskifyProject
        public var isNew: Bool
        public var step: Int
        /// Original project file when the draft is an edit of an existing project
        /// (filePath itself isn't serialized on the project, so it's carried here).
        public var filePath: String?

        public init(project: DeskifyProject, isNew: Bool, step: Int, filePath: String?) {
            self.project = project
            self.isNew = isNew
            self.step = step
            self.filePath = filePath
        }
    }

    public static func save(_ draft: Draft) {
        do {
            try FileManager.default.createDirectory(at: AppSettings.dataDir, withIntermediateDirectories: true)
            try ProjectJSON.encoder.encode(draft).write(to: draftPath, options: .atomic)
        } catch {
            Log.error("Failed to autosave wizard draft", error)
        }
    }

    public static func load() -> Draft? {
        do {
            guard FileManager.default.fileExists(atPath: draftPath.path) else { return nil }
            return try ProjectJSON.decoder.decode(Draft.self, from: Data(contentsOf: draftPath))
        } catch {
            Log.error("Failed to load wizard draft", error)
            return nil
        }
    }

    public static func clear() {
        do {
            if FileManager.default.fileExists(atPath: draftPath.path) {
                try FileManager.default.removeItem(at: draftPath)
            }
        } catch {
            Log.error("Failed to clear wizard draft", error)
        }
    }
}

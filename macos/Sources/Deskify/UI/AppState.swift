import SwiftUI
import AppKit
import DeskifyCore

/// Everything the wizard is editing, kept separate from the saved project
/// until the user hits Create/Save (mirrors the Windows wizard's working copy).
final class WizardModel: ObservableObject {
    let project: DeskifyProject // clone for edits, fresh instance for new
    let isNew: Bool

    @Published var step = 0
    @Published var name: String
    @Published var strictLayout: Bool
    @Published var apps: [AppEntry]
    @Published var folders: [String]
    @Published var urls: [String]
    @Published var urlInput = ""
    @Published var urlHint: String?

    static let stepTitles = ["Name", "Apps", "Folders", "Websites"]

    init(project: DeskifyProject, isNew: Bool) {
        self.project = project
        self.isNew = isNew
        name = project.name
        strictLayout = project.strictLayout
        apps = project.apps
        folders = project.folders
        urls = project.urls
    }

    /// Pulls the wizard's current field values into the project being edited.
    /// Shared by Save and by the close-time draft autosave, so whatever is on
    /// screen is always exactly what gets persisted.
    func collectInto(_ target: DeskifyProject) {
        let trimmed = name.trimmingCharacters(in: .whitespaces)
        target.name = trimmed.isEmpty ? "Untitled" : trimmed
        target.strictLayout = strictLayout
        target.apps = apps
        target.folders = folders
        target.urls = urls
    }
}

/// Ask-first request for closing non-project windows.
struct CloseOthersRequest: Identifiable {
    enum Mode { case closeOnly, workspaceSwitch }
    let id = UUID()
    let project: DeskifyProject
    let windows: [WindowInfo]
    let mode: Mode
}

@MainActor
final class AppState: ObservableObject {
    enum Screen { case projects, detail, wizard, settings }
    enum Sheet: Identifiable {
        case appPicker
        case capture(intoWizard: Bool)
        case closeOthers(CloseOthersRequest)

        var id: String {
            switch self {
            case .appPicker: return "appPicker"
            case .capture(let intoWizard): return "capture-\(intoWizard)"
            case .closeOthers(let request): return "closeOthers-\(request.id)"
            }
        }
    }

    // MARK: State

    @Published var projects: [DeskifyProject] = []
    @Published var settings: AppSettings
    @Published var screen: Screen = .projects
    @Published var selected: DeskifyProject?
    @Published var wizard: WizardModel?
    @Published var activeSheet: Sheet?

    @Published var launching = false
    @Published var statusText = ""
    @Published var errors: [String] = []

    @Published var sidebarCollapsed = false
    @Published var layoutToolbarVisible = false
    @Published var snapEnabled = false
    @Published var deleteConfirm: DeskifyProject?

    @Published var accessibilityGranted: Bool

    var theme: Theme { Theme.named(settings.themeName) }

    // MARK: Services

    let platform = Platform.mac
    let engine: LaunchEngine
    let closer: WindowCloser
    private let hotkey = HotkeyManager()
    private var quickSwitch: QuickSwitchController?
    private var notesSaveTask: Task<Void, Never>?

    init() {
        settings = AppSettings.load()
        engine = LaunchEngine(platform: platform)
        closer = WindowCloser(platform: platform)
        accessibilityGranted = platform.windows.hasPermission
        projects = ProjectStore.loadAll()

        // Self-heal: older or hand-edited project files can be missing the
        // hidden entries that let Edit Layout/Launch find a window for each
        // folder or website. Persist the fix so it sticks.
        for project in projects {
            let before = project.apps.count
            engine.syncAutoLinkedEntries(project: project)
            if project.apps.count != before { ProjectStore.save(project) }
        }

        applyAppearance()
        restoreDraftIfAny()

        quickSwitch = QuickSwitchController(appState: self)
        hotkey.onHotkey = { [weak self] in
            Task { @MainActor in self?.quickSwitch?.toggle() }
        }
        if !hotkey.register() {
            Log.error("Quick-switch hotkey (⌃Space) is already claimed by another app — quick switch won't respond to it.")
        }
        Log.info("Deskify started")
    }

    // MARK: - Navigation

    func showProjects() {
        exitLayoutMode()
        screen = .projects
        sortProjects()
    }

    func showDetails(_ project: DeskifyProject) {
        exitLayoutMode()
        selected = project
        statusText = ""
        errors = []
        screen = .detail
    }

    func showSettings() {
        exitLayoutMode()
        screen = .settings
    }

    func sortProjects() {
        projects.sort { a, b in
            if a.pinned != b.pinned { return a.pinned }
            switch (a.lastUsed, b.lastUsed) {
            case let (x?, y?) where x != y: return x > y
            case (.some, .none): return true
            case (.none, .some): return false
            default: return a.name.localizedCaseInsensitiveCompare(b.name) == .orderedAscending
            }
        }
    }

    // MARK: - Launch (full workspace switch)

    /// Launching a project is a full workspace switch: close everything that
    /// doesn't belong (asking first, unless disabled in Settings), then launch.
    /// Shared by the row Launch button, the detail-view Launch button, and
    /// Quick Switch — one path, so all three switch workspaces identically.
    func requestLaunch(_ project: DeskifyProject) {
        guard !launching else { return }
        showDetails(project)
        engine.syncAutoLinkedEntries(project: project)
        refreshAccessibility()

        let others = closer.otherWindows(project: project)
        if others.isEmpty {
            Task { await performLaunch(project, closing: []) }
        } else if settings.confirmCloseOthers {
            activeSheet = .closeOthers(CloseOthersRequest(project: project, windows: others, mode: .workspaceSwitch))
        } else {
            Task { await performLaunch(project, closing: others) }
        }
    }

    /// Called by the confirmation sheet. `windows` is what the user left checked.
    func confirmCloseOthers(_ request: CloseOthersRequest, windows: [WindowInfo], dontAskAgain: Bool) {
        activeSheet = nil
        if dontAskAgain {
            settings.confirmCloseOthers = false
            settings.save()
        }
        switch request.mode {
        case .workspaceSwitch:
            Task { await performLaunch(request.project, closing: windows) }
        case .closeOnly:
            guard !windows.isEmpty else { return }
            Task { await performClose(windows) }
        }
    }

    private func performLaunch(_ project: DeskifyProject, closing: [WindowInfo]) async {
        launching = true
        errors = []
        defer { launching = false }

        var closeErrors: [String] = []
        if !closing.isEmpty {
            statusText = "Closing other apps…"
            closeErrors = await closer.closeAndVerify(closing)
        }

        let result = await engine.launch(project: project, settings: settings) { [weak self] message in
            Task { @MainActor in self?.statusText = message }
        }
        errors = closeErrors + result.errors

        project.lastUsed = Date()
        if !ProjectStore.save(project) {
            errors.append("Couldn't save the project file (last-used time wasn't updated) — everything else above still launched normally.")
        }
        objectWillChange.send()
    }

    // MARK: - Close Other Apps

    func closeOthersClicked() {
        guard let project = selected, !launching else { return }
        refreshAccessibility()
        let others = closer.otherWindows(project: project)
        if others.isEmpty {
            statusText = accessibilityGranted
                ? "Nothing else is open."
                : "Deskify needs Accessibility permission to see other apps' windows — grant it in Settings."
            return
        }
        if settings.confirmCloseOthers {
            activeSheet = .closeOthers(CloseOthersRequest(project: project, windows: others, mode: .closeOnly))
        } else {
            Task { await performClose(others) }
        }
    }

    private func performClose(_ windows: [WindowInfo]) async {
        launching = true
        defer { launching = false }
        statusText = "Closing other apps…"
        let failed = await closer.closeAndVerify(windows)
        errors = failed
        statusText = "Closed other apps."
    }

    // MARK: - Layout editor

    func editLayoutClicked() {
        guard let project = selected, !launching else { return }
        engine.syncAutoLinkedEntries(project: project)
        guard !project.apps.isEmpty else {
            statusText = "Add apps, folders, or websites to this project first — layout applies to their windows."
            return
        }
        refreshAccessibility()
        if !accessibilityGranted {
            statusText = "Deskify needs Accessibility permission to arrange windows — grant it in Settings, then try again."
            platform.windows.requestPermission()
            return
        }

        Task {
            launching = true
            defer { launching = false }
            let result = LaunchResult()
            statusText = "Opening anything that isn't running yet…"
            let launched = await engine.launchMissingApps(project: project, settings: settings, result: result)
            errors = result.errors
            statusText = launched > 0
                ? "Started \(launched) app\(launched == 1 ? "" : "s"). Arrange the windows, then save the layout."
                : "All apps are already running. Arrange the windows, then save the layout."
            layoutToolbarVisible = true
        }
    }

    func saveLayoutClicked() {
        guard let project = selected else { return }
        let missing = engine.captureLayouts(project: project)
        if snapEnabled { snapLayouts(project, grid: settings.snapGridSize) }
        let saved = ProjectStore.save(project)
        layoutToolbarVisible = false
        objectWillChange.send()

        let captured = project.apps.count - missing.count
        guard saved else {
            // Distinct from "some app's window wasn't found": the write to disk
            // itself failed, so nothing from this attempt persisted.
            statusText = "Couldn't save the project file — nothing from this attempt was written to disk. Check that the project file isn't open elsewhere or read-only, then try again."
            errors = []
            return
        }
        statusText = missing.isEmpty
            ? "Layout saved for all \(project.apps.count) app\(project.apps.count == 1 ? "" : "s")."
            : "Layout saved for \(captured)/\(project.apps.count) apps — the rest of the project was saved normally."
        errors = missing.map { "\($0): no open window found — its layout wasn't updated, but everything else was saved." }
    }

    func cancelLayoutClicked() {
        layoutToolbarVisible = false
        statusText = "Layout editing cancelled — nothing saved."
    }

    private func exitLayoutMode() {
        layoutToolbarVisible = false
    }

    /// Round captured window positions/sizes to the nearest grid multiple.
    private func snapLayouts(_ project: DeskifyProject, grid: Int) {
        guard grid >= 2 else { return }
        func round(_ v: Int) -> Int { Int((Double(v) / Double(grid)).rounded()) * grid }
        for app in project.apps {
            guard var w = app.window, !w.isMaximized else { continue }
            w.x = round(w.x)
            w.y = round(w.y)
            w.width = max(grid, round(w.width))
            w.height = max(grid, round(w.height))
            app.window = w
        }
    }

    // MARK: - Create / edit / delete

    func newProject() {
        let project = DeskifyProject()
        project.strictLayout = settings.strictLayoutDefault
        openWizard(project, isNew: true)
    }

    func newCaptureProject(_ apps: [AppEntry]) {
        guard !apps.isEmpty else { return }
        let formatter = DateFormatter()
        formatter.dateFormat = "MMM d HH:mm"
        let project = DeskifyProject()
        project.name = "Captured \(formatter.string(from: Date()))"
        project.apps = apps
        project.strictLayout = settings.strictLayoutDefault
        openWizard(project, isNew: true)
    }

    func editProject() {
        guard let selected else { return }
        openWizard(selected.clone(), isNew: false)
    }

    func duplicateProject() {
        guard let selected else { return }
        let copy = selected.clone()
        copy.filePath = nil
        copy.name += " (copy)"
        copy.lastUsed = nil
        guard ProjectStore.save(copy) else {
            // Don't show a duplicate that doesn't actually exist on disk.
            statusText = "Couldn't save the duplicate — check that the projects folder isn't read-only, then try again."
            return
        }
        projects.append(copy)
        showDetails(copy)
    }

    func deleteProject(_ project: DeskifyProject) {
        guard ProjectStore.delete(project) else {
            // The file is still on disk — keep the project in the list rather
            // than showing it gone and having it reappear on next start.
            statusText = "Couldn't delete the project file — check that it isn't open elsewhere, then try again."
            return
        }
        projects.removeAll { $0 === project }
        selected = nil
        showProjects()
    }

    func togglePin(_ project: DeskifyProject) {
        project.pinned.toggle()
        guard ProjectStore.save(project) else {
            // Don't leave the UI showing a pin state that isn't on disk.
            project.pinned.toggle()
            statusText = "Couldn't save — check that the project file isn't open elsewhere or read-only."
            return
        }
        sortProjects()
        objectWillChange.send()
    }

    // MARK: - Notes

    func updateNotes(_ project: DeskifyProject, text: String) {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        let notes = trimmed.isEmpty ? nil : trimmed
        guard project.notes != notes else { return }
        project.notes = notes

        // Debounced write — saves shortly after typing stops rather than on
        // every keystroke, and saveNotesNow() on exit covers the final state.
        notesSaveTask?.cancel()
        notesSaveTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 800_000_000)
            guard !Task.isCancelled else { return }
            await MainActor.run {
                if !ProjectStore.save(project) {
                    self?.statusText = "Couldn't save your notes — check that the project file isn't open elsewhere or read-only."
                }
            }
        }
    }

    private func saveNotesNow() {
        notesSaveTask?.cancel()
        if let selected { ProjectStore.save(selected) }
    }

    // MARK: - Wizard

    func openWizard(_ project: DeskifyProject, isNew: Bool) {
        exitLayoutMode()
        wizard = WizardModel(project: project, isNew: isNew)
        screen = .wizard
    }

    func addUrlFromWizard() {
        guard let wizard else { return }
        wizard.urlHint = nil
        var url = wizard.urlInput.trimmingCharacters(in: .whitespaces)
        guard !url.isEmpty else { return }
        if !url.lowercased().hasPrefix("http://") && !url.lowercased().hasPrefix("https://") {
            url = "https://" + url
        }
        guard let parsed = URL(string: url), let host = parsed.host, !host.isEmpty else {
            wizard.urlHint = "That doesn't look like a web address — try something like example.com."
            return
        }
        if ProtectedApps.isProtected(path: url) {
            wizard.urlHint = "Riot Games/Valorant links can't be added to Deskify."
            wizard.urlInput = ""
            return
        }
        if wizard.urls.contains(where: { $0.caseInsensitiveCompare(url) == .orderedSame }) {
            wizard.urlHint = "That website is already in the list."
            wizard.urlInput = ""
            return
        }
        wizard.urls.append(url)
        wizard.urlInput = ""

        // Also add the default browser as an app entry (once) so its window can
        // be positioned via Edit Layout — all sites open as tabs in the same
        // window, so one entry covers every website in the project.
        if let browser = platform.browser.defaultBrowser(),
           !wizard.apps.contains(where: { pathsEqual($0.path, browser.path) }) {
            wizard.apps.append(AppEntry(name: browser.name, path: browser.path, autoLinked: true))
        }
    }

    func addFolderFromWizard(_ folder: String) {
        guard let wizard else { return }
        // Already in the list — adding it again would open it twice on launch.
        guard !wizard.folders.contains(where: { pathsEqual($0, folder) }) else { return }
        wizard.folders.append(folder)

        // Also add Finder as an app entry so this folder's window can be
        // positioned via Edit Layout — it opens through the Folders group,
        // not launched again from here (see AppEntry.autoLinked).
        let leaf = (folder as NSString).lastPathComponent
        wizard.apps.append(AppEntry(
            name: leaf.isEmpty ? folder : leaf,
            path: platform.fileBrowserPath,
            args: folder,
            autoLinked: true))
    }

    func removeWizardString(_ value: String) {
        guard let wizard else { return }
        if let idx = wizard.folders.firstIndex(of: value) {
            wizard.folders.remove(at: idx)
            wizard.apps.removeAll { $0.autoLinked && pathsEqual($0.path, platform.fileBrowserPath) && pathsEqual($0.args, value) }
        } else if let idx = wizard.urls.firstIndex(of: value) {
            wizard.urls.remove(at: idx)
            if wizard.urls.isEmpty, let browser = platform.browser.defaultBrowser() {
                wizard.apps.removeAll { $0.autoLinked && pathsEqual($0.path, browser.path) }
            }
        }
    }

    /// Save from the wizard's final step. Returns false when the disk write
    /// failed so the wizard stays open instead of losing the edits.
    func saveWizard() -> Bool {
        guard let wizard else { return true }
        addUrlFromWizard() // anything typed but not added shouldn't be lost
        let project = wizard.project
        wizard.collectInto(project)
        engine.syncAutoLinkedEntries(project: project)

        guard ProjectStore.save(project) else { return false }

        if wizard.isNew {
            projects.append(project)
        } else if let selected, let idx = projects.firstIndex(where: { $0 === selected }) {
            projects[idx] = project
        }
        selected = project
        self.wizard = nil
        DraftStore.clear()
        showDetails(project)
        return true
    }

    func cancelWizard() {
        wizard = nil
        DraftStore.clear()
        if let selected { showDetails(selected) } else { showProjects() }
    }

    // MARK: - Drafts (nothing typed is lost on quit)

    /// Reopens the wizard exactly where it was when the app closed mid-edit.
    /// The draft is cleared immediately so it can't resurrect later — closing
    /// again mid-edit simply writes a fresh one.
    private func restoreDraftIfAny() {
        guard let draft = DraftStore.load() else { return }
        DraftStore.clear()

        let project = draft.project
        var isNew = draft.isNew
        if !isNew, let filePath = draft.filePath {
            if let original = projects.first(where: { $0.filePath?.path == filePath }) {
                selected = original
                project.filePath = URL(fileURLWithPath: filePath)
            } else {
                isNew = true // original project file is gone — keep the edits as a new project
            }
        }
        openWizard(project, isNew: isNew)
        wizard?.step = min(max(draft.step, 0), 3)
    }

    /// Nothing typed should be lost just because the app was quit: a wizard
    /// mid-edit is saved as a draft (restored on next start) and unsaved notes
    /// are written out.
    func autosaveOnExit() {
        if let wizard {
            wizard.collectInto(wizard.project)
            // An untouched brand-new wizard has no progress worth restoring.
            let emptyNew = wizard.isNew
                && wizard.project.apps.isEmpty && wizard.project.folders.isEmpty && wizard.project.urls.isEmpty
                && (wizard.project.name == "Untitled" || wizard.project.name == "New Project")
            if !emptyNew {
                DraftStore.save(DraftStore.Draft(
                    project: wizard.project,
                    isNew: wizard.isNew,
                    step: wizard.step,
                    filePath: wizard.project.filePath?.path))
            }
        }
        saveNotesNow()
        Log.info("Deskify exiting")
    }

    // MARK: - Settings / appearance / permissions

    func selectTheme(_ name: String) {
        guard settings.themeName != name else { return }
        settings.themeName = name
        settings.save()
        applyAppearance()
    }

    func applyAppearance() {
        // Force the whole app (native controls, sheets, menus) to follow the
        // chosen theme rather than the system, matching the Windows behavior
        // of an explicit Dark/Light choice.
        NSApp.appearance = NSAppearance(named: theme.isDark ? .darkAqua : .aqua)
    }

    func refreshAccessibility() {
        accessibilityGranted = platform.windows.hasPermission
    }

    func requestAccessibility() {
        platform.windows.requestPermission()
        // The grant takes effect without relaunching; poll briefly so the
        // Settings card flips to "granted" as soon as the user allows it.
        Task { [weak self] in
            for _ in 0..<30 {
                try? await Task.sleep(nanoseconds: 1_000_000_000)
                guard let self else { return }
                self.refreshAccessibility()
                if self.accessibilityGranted { break }
            }
        }
    }

    func openDataFolder() {
        try? FileManager.default.createDirectory(at: AppSettings.dataDir, withIntermediateDirectories: true)
        NSWorkspace.shared.open(AppSettings.dataDir)
    }

    // MARK: - Quick switch

    func quickSwitchChose(_ project: DeskifyProject) {
        NSApp.activate(ignoringOtherApps: true)
        if let window = NSApp.windows.first(where: { $0.isVisible || $0.isMiniaturized }) {
            if window.isMiniaturized { window.deminiaturize(nil) }
            window.makeKeyAndOrderFront(nil)
        }
        requestLaunch(project)
    }
}

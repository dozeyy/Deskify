import Foundation

public final class LaunchResult {
    public var errors: [String] = []
    public var positioned = 0
    public var layoutTargets = 0
    public var success: Bool { errors.isEmpty }
    public init() {}
}

/// Launches a project's apps/urls/folders and restores the saved window layout.
/// All OS access goes through `Platform`, so this orchestration is shared
/// behavior, not platform code.
public final class LaunchEngine {
    private let platform: Platform

    public init(platform: Platform) {
        self.platform = platform
    }

    // MARK: - Launch

    public func launch(project: DeskifyProject, settings: AppSettings,
                       status: @escaping @Sendable (String) -> Void) async -> LaunchResult {
        let result = LaunchResult()

        // Snapshot windows that existed before launch so we can prefer NEW
        // windows when matching (critical for single-instance/single-window
        // apps), and so an app that's already running isn't launched twice.
        let scanned = platform.windows.scanWindows()
        let preExisting = Set(scanned.map(\.windowID))
        var launchedPids = Set<Int32>()

        for group in project.effectiveLaunchOrder {
            switch group.lowercased() {
            case "apps":
                let toLaunch = project.apps.filter { !$0.autoLinked }
                if !toLaunch.isEmpty {
                    status("Launching \(toLaunch.count) app\(toLaunch.count == 1 ? "" : "s")…")
                }
                for app in toLaunch {
                    // Already running — don't spawn a duplicate instance.
                    if findBestWindow(in: scanned, for: app, launchedPids: [], preExisting: [], claimed: []) != nil {
                        continue
                    }
                    await launchApp(app, launchedPids: &launchedPids, result: result)
                    try? await Task.sleep(nanoseconds: 150_000_000)
                }

            case "urls":
                if !project.urls.isEmpty { status("Opening websites…") }
                await openURLs(project.urls, result: result)

            case "folders":
                if !project.folders.isEmpty { status("Opening folders…") }
                for folder in project.folders {
                    await openFolder(folder, result: result)
                    try? await Task.sleep(nanoseconds: 250_000_000)
                }

            default:
                break
            }
        }

        await positionWindows(project: project, settings: settings, status: status,
                              preExisting: preExisting, launchedPids: launchedPids, result: result)

        status(result.success
            ? (result.layoutTargets > 0
                ? "Workspace restored — \(result.positioned)/\(result.layoutTargets) windows positioned."
                : "Workspace launched.")
            : "Done with \(result.errors.count) issue\(result.errors.count == 1 ? "" : "s").")
        return result
    }

    private func launchApp(_ app: AppEntry, launchedPids: inout Set<Int32>, result: LaunchResult) async {
        if ProtectedApps.isProtected(path: app.path, name: app.name) {
            result.errors.append("\(app.name): blocked — Riot Games/Valorant apps can't be launched by Deskify")
            Log.error("Blocked launch attempt: \(app.name) (\(app.path))")
            return
        }

        // A Finder entry with a folder in its args (a captured folder window)
        // means "open that folder", not "activate Finder" — launching the
        // Finder app itself would never bring the folder window back.
        if pathsEqual(app.path, platform.fileBrowserPath),
           let folder = app.args?.trimmingCharacters(in: CharacterSet(charactersIn: "\" ")), !folder.isEmpty {
            await openFolder(folder, result: result)
            return
        }

        guard FileManager.default.fileExists(atPath: app.path) else {
            result.errors.append("\(app.name): app not found (\(app.path))")
            return
        }

        do {
            if let pid = try await platform.launcher.launchApp(path: app.path, args: app.args) {
                launchedPids.insert(pid)
            }
            Log.info("Launched \(app.name) (\(app.path))")
        } catch {
            result.errors.append("\(app.name): \(error.localizedDescription)")
            Log.error("Launch failed for \(app.path)", error)
        }
    }

    /// Opens every website in one hand-off to the default browser rather than
    /// one at a time. Firing them individually races the browser's cold start —
    /// URLs sent before its single-instance channel is ready end up in separate
    /// windows. One open call gives one window with every site as a tab.
    private func openURLs(_ urls: [String], result: LaunchResult) async {
        var allowed: [String] = []
        for url in urls {
            if ProtectedApps.isProtected(path: url) {
                result.errors.append("URL \(url): blocked — Riot Games/Valorant links can't be opened by Deskify")
                Log.error("Blocked URL open attempt: \(url)")
            } else {
                allowed.append(url)
            }
        }
        guard !allowed.isEmpty else { return }

        let browserPath = allowed.count > 1 ? platform.browser.defaultBrowser()?.path : nil
        do {
            try await platform.launcher.openURLs(allowed, browserPath: browserPath)
            for url in allowed { Log.info("Opened URL \(url)") }
        } catch {
            result.errors.append("Websites: \(error.localizedDescription)")
            Log.error("Failed to open websites", error)
        }
    }

    /// Opens one folder in its own Finder window.
    private func openFolder(_ folder: String, result: LaunchResult) async {
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: (folder as NSString).expandingTildeInPath, isDirectory: &isDir),
              isDir.boolValue else {
            result.errors.append("Folder not found: \(folder)")
            return
        }
        do {
            try await platform.launcher.openFolder(folder)
            Log.info("Opened folder \(folder)")
        } catch {
            result.errors.append("Folder \(folder): \(error.localizedDescription)")
            Log.error("Failed to open folder \(folder)", error)
        }
    }

    // MARK: - Window positioning

    private func positionWindows(project: DeskifyProject, settings: AppSettings,
                                 status: @escaping @Sendable (String) -> Void,
                                 preExisting: Set<Int>, launchedPids: Set<Int32>,
                                 result: LaunchResult) async {
        let targets = project.apps.filter { $0.window != nil && !$0.path.isEmpty }
        result.layoutTargets = targets.count
        guard !targets.isEmpty else { return }

        if !platform.windows.hasPermission {
            result.errors.append("Window layout skipped — Deskify needs Accessibility permission to move windows. Grant it in Settings.")
            return
        }

        status("Waiting for windows…")
        let screens = platform.screens.screens()
        var claimed = Set<Int>()
        var applied: [(WindowInfo, WindowLayout)] = []
        var remaining = targets
        let deadline = Date().addingTimeInterval(TimeInterval(settings.detectTimeoutSeconds))

        // Retry loop: poll for windows until every target is matched or we time out.
        while !remaining.isEmpty && Date() < deadline {
            let windows = platform.windows.scanWindows()

            for i in stride(from: remaining.count - 1, through: 0, by: -1) {
                let app = remaining[i]
                guard let match = findBestWindow(in: windows, for: app, launchedPids: launchedPids,
                                                 preExisting: preExisting, claimed: claimed) else { continue }

                claimed.insert(match.windowID)
                if platform.windows.apply(app.window!, to: match, screens: screens) {
                    result.positioned += 1
                    applied.append((match, app.window!))
                    status("Positioned \(app.name) (\(result.positioned)/\(targets.count))")
                } else {
                    // Don't fail silently: the window was found but the OS
                    // refused to move it.
                    result.errors.append("\(app.name): found its window but couldn't move it")
                }
                remaining.remove(at: i)
            }

            if !remaining.isEmpty {
                try? await Task.sleep(nanoseconds: UInt64(settings.retryIntervalMs) * 1_000_000)
            }
        }

        for app in remaining {
            result.errors.append("\(app.name): window not found within \(settings.detectTimeoutSeconds)s — layout not applied")
        }

        // Strict mode: some apps restore their own size shortly after startup.
        // Verify after a settle delay and re-apply anything that drifted.
        if project.strictLayout && !applied.isEmpty {
            status("Double-checking window positions…")
            for _ in 0..<3 {
                try? await Task.sleep(nanoseconds: 1_000_000_000)
                var allGood = true
                for (window, layout) in applied {
                    if !platform.windows.matches(window, layout: layout, screens: screens, tolerance: 16) {
                        allGood = false
                        platform.windows.apply(layout, to: window, screens: screens)
                    }
                }
                if allGood { break }
            }
        }
    }

    /// Rank candidate windows for an app entry. Preference order:
    /// new window from a launched pid → any new window → pre-existing window.
    /// On macOS every window already belongs unambiguously to its app bundle,
    /// so "same app" is a bundle-path comparison — no process-tree walking or
    /// install-folder fallbacks needed. Finder entries additionally require the
    /// window to be showing the entry's folder, since Finder hosts every folder
    /// window in one shared process.
    func findBestWindow(in windows: [WindowInfo], for app: AppEntry,
                        launchedPids: Set<Int32>, preExisting: Set<Int>,
                        claimed: Set<Int>) -> WindowInfo? {
        let targetFolder = folderTarget(for: app)

        func find(skipClaimed: Bool) -> WindowInfo? {
            var best: WindowInfo?
            var bestScore = 0
            for w in windows {
                if skipClaimed && claimed.contains(w.windowID) { continue }
                guard pathsEqual(w.appPath, app.path) else { continue }
                if let folder = targetFolder, !platform.windows.windowShowsFolder(w, folder: folder) { continue }

                let isNew = !preExisting.contains(w.windowID)
                let score = launchedPids.contains(w.pid) && isNew ? 4
                          : isNew ? 3
                          : 2
                if score > bestScore {
                    bestScore = score
                    best = w
                }
            }
            return best
        }

        let match = find(skipClaimed: true)
        // Tabbed Finder: two project folders can legitimately live in ONE
        // window, so if every window showing this folder is already claimed by
        // another folder entry, share it rather than reporting "not found".
        if match == nil, targetFolder != nil {
            return find(skipClaimed: false)
        }
        return match
    }

    /// For an auto-linked Finder entry, the folder its window should be showing.
    private func folderTarget(for app: AppEntry) -> String? {
        guard pathsEqual(app.path, platform.fileBrowserPath),
              let args = app.args?.trimmingCharacters(in: .whitespaces), !args.isEmpty else { return nil }
        return args.trimmingCharacters(in: CharacterSet(charactersIn: "\""))
    }

    // MARK: - Layout capture

    /// Find a currently-open window for each project app and capture its
    /// placement. Used by "Save Layout" — a single, immediate scan, on purpose:
    /// by the time you're arranging windows and hitting Save, they're already
    /// on screen. Returns names of apps whose window couldn't be found.
    public func captureLayouts(project: DeskifyProject) -> [String] {
        var missing: [String] = []
        guard platform.windows.hasPermission else {
            return project.apps.map(\.name) // nothing can be captured without permission
        }
        let windows = platform.windows.scanWindows()
        let screens = platform.screens.screens()
        var claimed = Set<Int>()

        for app in project.apps {
            guard let match = findBestWindow(in: windows, for: app, launchedPids: [],
                                             preExisting: [], claimed: claimed) else {
                missing.append(app.name)
                continue
            }
            claimed.insert(match.windowID)
            if let layout = platform.windows.capture(match, screens: screens) {
                app.window = layout
            } else {
                missing.append(app.name)
            }
        }
        return missing
    }

    // MARK: - Edit Layout support

    /// Launch only the project apps that have no window on screen right now
    /// (used by Edit Layout so the user can arrange everything). Waits for each
    /// newly-launched app to actually show a window before returning — a
    /// browser's cold start can take several seconds, and returning early let
    /// people hit Save Layout before a slow app had opened anything.
    public func launchMissingApps(project: DeskifyProject, settings: AppSettings,
                                  result: LaunchResult) async -> Int {
        let windows = platform.windows.scanWindows()
        var launched = 0
        var pids = Set<Int32>()
        var launchedApps: [AppEntry] = []

        // The auto-linked browser entry only stores the browser's app, never
        // the URLs (those live on the project). Open the project's sites the
        // same way Launch does — one call, every site as a tab — instead of
        // launching a blank browser (or skipping an already-open one entirely).
        let browserPath = project.urls.isEmpty ? nil : platform.browser.defaultBrowser()?.path
        func isBrowserEntry(_ a: AppEntry) -> Bool {
            guard let browserPath else { return false }
            return a.autoLinked && pathsEqual(a.path, browserPath)
        }

        if !project.urls.isEmpty {
            await openURLs(project.urls, result: result)
            launched += 1
            // Wait for the browser window so Save Layout can find it.
            if let browserEntry = project.apps.first(where: isBrowserEntry) {
                launchedApps.append(browserEntry)
            }
            try? await Task.sleep(nanoseconds: 300_000_000)
        }

        for app in project.apps {
            if isBrowserEntry(app) { continue } // opened via openURLs above

            // Reuses the same matching as layout capture/positioning.
            if findBestWindow(in: windows, for: app, launchedPids: [], preExisting: [], claimed: []) != nil {
                continue
            }
            // launchApp handles Finder-folder entries by opening their folder.
            await launchApp(app, launchedPids: &pids, result: result)
            launched += 1
            launchedApps.append(app)
            // Back-to-back launches with no gap can race each other's window creation.
            try? await Task.sleep(nanoseconds: 300_000_000)
        }

        if !launchedApps.isEmpty {
            await waitForWindows(apps: launchedApps, launchedPids: pids,
                                 timeoutSeconds: settings.detectTimeoutSeconds)
        }
        return launched
    }

    /// Polls until every given app has a matching window or the timeout elapses.
    /// Best effort — if an app never opens a window (blocked, crashed, needs
    /// manual sign-in), Edit Layout still returns and Save Layout will simply
    /// report it as not found.
    private func waitForWindows(apps: [AppEntry], launchedPids: Set<Int32>, timeoutSeconds: Int) async {
        var remaining = apps
        let deadline = Date().addingTimeInterval(TimeInterval(timeoutSeconds))

        while !remaining.isEmpty && Date() < deadline {
            let windows = platform.windows.scanWindows()
            remaining.removeAll { app in
                findBestWindow(in: windows, for: app, launchedPids: launchedPids,
                               preExisting: [], claimed: []) != nil
            }
            if !remaining.isEmpty { try? await Task.sleep(nanoseconds: 300_000_000) }
        }
    }

    // MARK: - Auto-linked entries

    /// Keeps the hidden "autoLinked" app entries (one Finder entry per folder,
    /// one shared default-browser entry for all websites) in sync with a
    /// project's folders/urls lists. These entries are what let Edit Layout and
    /// Launch actually find and position a window for each folder/website.
    /// Folders each get their own entry (their own window, their own saved
    /// position); websites share one entry because they're tabs in the same
    /// browser window. Safe to call anytime: it adds what's missing, drops
    /// entries for folders/urls that were removed, and never touches
    /// manually-added (non-autoLinked) apps.
    public func syncAutoLinkedEntries(project: DeskifyProject) {
        let finderPath = platform.fileBrowserPath
        func isFinderEntry(_ a: AppEntry) -> Bool { a.autoLinked && pathsEqual(a.path, finderPath) }

        // Drop folder entries whose folder was removed from the project.
        project.apps.removeAll { a in
            isFinderEntry(a) && !project.folders.contains { pathsEqual(a.args, $0) }
        }

        // Add entries for folders that don't have one yet.
        for folder in project.folders {
            let exists = project.apps.contains { isFinderEntry($0) && pathsEqual($0.args, folder) }
            if exists { continue }
            let leaf = (folder as NSString).lastPathComponent
            project.apps.append(AppEntry(
                name: leaf.isEmpty ? folder : leaf,
                path: finderPath,
                args: folder,
                autoLinked: true))
        }

        // Browser entry: one shared entry covers every website (they're tabs in
        // the same window). Drop it if there are no websites left; add it if
        // there are websites but no browser entry yet.
        let browser = project.urls.isEmpty ? nil : platform.browser.defaultBrowser()
        project.apps.removeAll { a in
            a.autoLinked && !isFinderEntry(a) &&
            (browser == nil || !pathsEqual(a.path, browser!.path))
        }

        if let browser, !project.apps.contains(where: { $0.autoLinked && pathsEqual($0.path, browser.path) }) {
            project.apps.append(AppEntry(name: browser.name, path: browser.path, autoLinked: true))
        }
    }
}

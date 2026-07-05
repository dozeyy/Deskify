import Foundation

/// Finds and closes windows/apps that don't belong to a given project.
public final class WindowCloser {
    private let platform: Platform

    public init(platform: Platform) {
        self.platform = platform
    }

    /// Currently open windows, excluding the given project's own apps.
    /// Identified the same way launch/layout matching identifies them (by app
    /// bundle path), so "other" windows mean the same thing everywhere.
    public func otherWindows(project: DeskifyProject) -> [WindowInfo] {
        let keepPaths = project.apps.map(\.path)
        return platform.windows.scanWindows().filter { w in
            !keepPaths.contains { pathsEqual($0, w.appPath) }
        }
    }

    /// Closes windows and makes sure the apps behind them actually go away:
    /// a polite quit first (Finder windows are closed individually — Finder
    /// itself is never quit), then polls until each app has really exited, then
    /// force-terminates whatever's left. Many apps keep running after their
    /// windows close — this is what stops them piling up across repeated
    /// workspace switches. Returns a message per app that couldn't be fully
    /// closed, for status/error reporting.
    public func closeAndVerify(_ windows: [WindowInfo], graceSeconds: Double = 4) async -> [String] {
        var failed: [String] = []
        guard !windows.isEmpty else { return failed }

        // One representative window per pid — an app can own several of the
        // windows being closed.
        var byPid: [Int32: WindowInfo] = [:]
        for w in windows where byPid[w.pid] == nil { byPid[w.pid] = w }

        var awaitingExit = Set<Int32>()
        for (pid, w) in byPid {
            if isFileManager(w) {
                // Close each Finder window politely; never quit Finder itself —
                // that's the shell. (Same rule as never killing explorer.exe.)
                for finderWindow in windows where finderWindow.pid == pid {
                    platform.windows.closeWindow(finderWindow)
                }
            } else {
                platform.launcher.requestTerminate(pid: pid)
                awaitingExit.insert(pid)
            }
        }

        let deadline = Date().addingTimeInterval(graceSeconds)
        while !awaitingExit.isEmpty && Date() < deadline {
            try? await Task.sleep(nanoseconds: 300_000_000)
            awaitingExit = awaitingExit.filter { platform.launcher.isProcessRunning(pid: $0) }
        }

        for pid in awaitingExit {
            let w = byPid[pid]!
            guard isSafeToForceKill(w) else {
                failed.append("\(w.appName): left running (protected — never force-closed)")
                continue
            }
            if platform.launcher.forceTerminate(pid: pid) {
                Log.info("Force-closed \(w.appName) (pid \(pid)) after it didn't respond to a normal quit")
            } else {
                failed.append("\(w.appName): didn't quit and couldn't be force-closed")
                Log.error("Failed to force-close \(w.appName) (pid \(pid))")
            }
        }
        return failed
    }

    private func isFileManager(_ w: WindowInfo) -> Bool {
        pathsEqual(w.appPath, platform.fileBrowserPath)
    }

    /// Hard safety net for the force-kill escalation — never terminate the OS
    /// shell, system apps, or Deskify itself.
    private func isSafeToForceKill(_ w: WindowInfo) -> Bool {
        if isFileManager(w) { return false }
        if w.pid == ProcessInfo.processInfo.processIdentifier { return false }
        if ProtectedApps.isProtected(path: w.appPath, name: w.appName) { return false }
        // Anything under /System is an OS component, not a user app.
        if w.appPath.hasPrefix("/System/") { return false }
        return true
    }
}

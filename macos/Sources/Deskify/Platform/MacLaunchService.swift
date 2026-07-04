import AppKit
import DeskifyCore

/// App launching, URL/folder opening, and process termination via NSWorkspace
/// and NSRunningApplication — the supported, sandbox-friendly equivalents of
/// ShellExecute/Process.Start and WM_CLOSE/TerminateProcess.
final class MacLaunchService: LaunchService {

    func launchApp(path: String, args: String?) async throws -> Int32? {
        let url = URL(fileURLWithPath: path)
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.activates = true
        if let args, !args.trimmingCharacters(in: .whitespaces).isEmpty {
            configuration.arguments = Self.splitArguments(args)
        }

        return try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Int32?, Error>) in
            NSWorkspace.shared.openApplication(at: url, configuration: configuration) { app, error in
                if let app {
                    continuation.resume(returning: app.processIdentifier)
                } else {
                    continuation.resume(throwing: error ?? NSError(
                        domain: "Deskify", code: 1,
                        userInfo: [NSLocalizedDescriptionKey: "the app couldn't be opened"]))
                }
            }
        }
    }

    func openURLs(_ urls: [String], browserPath: String?) async throws {
        let links = urls.compactMap { URL(string: $0) }
        guard !links.isEmpty else { return }

        guard let browserPath else {
            // Single URL (or no detectable default browser): the plain OS
            // hand-off, same as clicking a link.
            onMainThread { for link in links { NSWorkspace.shared.open(link) } }
            return
        }

        // One open call with every URL — each becomes a tab in one browser
        // window instead of racing a cold-starting browser into separate windows.
        let browserURL = URL(fileURLWithPath: browserPath)
        _ = try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Bool, Error>) in
            NSWorkspace.shared.open(links, withApplicationAt: browserURL,
                                    configuration: NSWorkspace.OpenConfiguration()) { _, error in
                if let error { continuation.resume(throwing: error) }
                else { continuation.resume(returning: true) }
            }
        }
    }

    func openFolder(_ path: String) async throws {
        let url = URL(fileURLWithPath: (path as NSString).expandingTildeInPath, isDirectory: true)
        let opened = onMainThread { NSWorkspace.shared.open(url) }
        if !opened {
            throw NSError(domain: "Deskify", code: 2,
                          userInfo: [NSLocalizedDescriptionKey: "Finder couldn't open the folder"])
        }
    }

    @discardableResult
    func requestTerminate(pid: Int32) -> Bool {
        // terminate() sends the app a normal Quit — the same as Cmd-Q — so it
        // can save documents and clean up, exactly like WM_CLOSE on Windows.
        NSRunningApplication(processIdentifier: pid)?.terminate() ?? false
    }

    @discardableResult
    func forceTerminate(pid: Int32) -> Bool {
        NSRunningApplication(processIdentifier: pid)?.forceTerminate() ?? false
    }

    func isProcessRunning(pid: Int32) -> Bool {
        guard let app = NSRunningApplication(processIdentifier: pid) else { return false }
        return !app.isTerminated
    }

    /// Splits a stored args string into argv-style pieces, honoring double
    /// quotes ("path with spaces" stays one argument).
    static func splitArguments(_ args: String) -> [String] {
        var result: [String] = []
        var current = ""
        var inQuotes = false
        for ch in args {
            switch ch {
            case "\"":
                inQuotes.toggle()
            case " " where !inQuotes:
                if !current.isEmpty { result.append(current); current = "" }
            default:
                current.append(ch)
            }
        }
        if !current.isEmpty { result.append(current) }
        return result
    }
}

/// Current display arrangement, published in a single global coordinate space
/// with a top-left origin so it composes directly with the Accessibility API's
/// window frames (AppKit's own NSScreen space is bottom-left-origin).
final class MacScreenService: ScreenService {
    func screens() -> [ScreenInfo] {
        let nsScreens = onMainThread { NSScreen.screens }
        guard let primary = nsScreens.first else { return [] }
        let primaryMaxY = primary.frame.maxY

        func flip(_ r: CGRect) -> CGRect {
            CGRect(x: r.minX, y: primaryMaxY - r.maxY, width: r.width, height: r.height)
        }

        // 0-based, left-to-right — the same monitor numbering the project
        // format has always used, so layouts survive monitor rearrangement.
        return nsScreens
            .map { (frame: flip($0.frame), visible: flip($0.visibleFrame)) }
            .sorted { a, b in
                a.frame.minX != b.frame.minX ? a.frame.minX < b.frame.minX : a.frame.minY < b.frame.minY
            }
            .enumerated()
            .map { ScreenInfo(index: $0.offset, frame: $0.element.frame, visibleFrame: $0.element.visible) }
    }
}

/// Reads the user's chosen default web browser — the same answer macOS itself
/// gives when handing off an https link.
final class MacBrowserService: BrowserService {
    func defaultBrowser() -> (path: String, name: String)? {
        guard let probe = URL(string: "https://www.example.com"),
              let appURL = NSWorkspace.shared.urlForApplication(toOpen: probe) else { return nil }
        let name = FileManager.default.displayName(atPath: appURL.path)
        return (appURL.path, name)
    }
}

extension Platform {
    /// The one place the app wires concrete macOS services into the core.
    static let mac = Platform(
        windows: MacWindowService(),
        launcher: MacLaunchService(),
        screens: MacScreenService(),
        browser: MacBrowserService(),
        fileBrowserPath: "/System/Library/CoreServices/Finder.app",
        fileBrowserName: "Finder")
}

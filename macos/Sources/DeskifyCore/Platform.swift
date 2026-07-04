import Foundation
import CoreGraphics

/// A visible top-level window and the app that owns it.
public struct WindowInfo {
    /// Opaque platform window handle (an AXUIElement wrapper on macOS).
    public let handle: AnyObject
    /// Stable identifier for the same window across scans — used to tell new
    /// windows from pre-existing ones and to claim matches.
    public let windowID: Int
    public let pid: Int32
    public let title: String
    /// The owning application's install path (.app bundle on macOS).
    public let appPath: String
    public let appName: String

    public init(handle: AnyObject, windowID: Int, pid: Int32, title: String, appPath: String, appName: String) {
        self.handle = handle
        self.windowID = windowID
        self.pid = pid
        self.title = title
        self.appPath = appPath
        self.appName = appName
    }
}

/// One display, in a shared global coordinate space with a top-left origin
/// (matching how layouts are stored). `index` is 0-based, left-to-right.
public struct ScreenInfo {
    public let index: Int
    public let frame: CGRect
    /// Frame minus menu bar/Dock — where a "maximized" window actually goes.
    public let visibleFrame: CGRect

    public init(index: Int, frame: CGRect, visibleFrame: CGRect) {
        self.index = index
        self.frame = frame
        self.visibleFrame = visibleFrame
    }

    public var displayText: String { "Monitor \(index + 1)" }
}

public extension Array where Element == ScreenInfo {
    /// Screen by saved index, clamped so a layout saved on a 3-monitor setup
    /// still lands somewhere sensible on a laptop alone.
    func byIndex(_ index: Int) -> ScreenInfo {
        guard !isEmpty else { return ScreenInfo(index: 0, frame: .zero, visibleFrame: .zero) }
        return self[Swift.min(Swift.max(index, 0), count - 1)]
    }

    /// The screen containing the window's center, else the nearest one.
    func containing(_ rect: CGRect) -> ScreenInfo {
        guard !isEmpty else { return ScreenInfo(index: 0, frame: .zero, visibleFrame: .zero) }
        let center = CGPoint(x: rect.midX, y: rect.midY)
        if let hit = first(where: { $0.frame.contains(center) }) { return hit }
        return self.min { a, b in
            distanceSquared(from: center, to: a.frame) < distanceSquared(from: center, to: b.frame)
        }!
    }

    private func distanceSquared(from p: CGPoint, to r: CGRect) -> CGFloat {
        let dx = Swift.max(r.minX - p.x, 0, p.x - r.maxX)
        let dy = Swift.max(r.minY - p.y, 0, p.y - r.maxY)
        return dx * dx + dy * dy
    }
}

// MARK: - Platform service protocols
//
// Everything OS-specific lives behind these. DeskifyCore's launch/close/layout
// logic only ever talks to the protocols, so the Windows and macOS platform
// layers can evolve independently while sharing the same behavior.

/// Enumerating, measuring, moving, and closing other apps' windows.
/// On macOS this is the Accessibility (AX) API and needs user permission.
public protocol WindowService: AnyObject {
    /// Whether the OS currently lets Deskify read/move other apps' windows.
    var hasPermission: Bool { get }
    /// Ask the OS to prompt for that permission (no-op if already granted).
    func requestPermission()

    /// Every real, user-visible application window right now.
    func scanWindows() -> [WindowInfo]
    /// Capture a window's placement relative to its monitor.
    func capture(_ window: WindowInfo, screens: [ScreenInfo]) -> WindowLayout?
    /// Apply a saved placement. Best effort: returns false if the OS refused.
    @discardableResult
    func apply(_ layout: WindowLayout, to window: WindowInfo, screens: [ScreenInfo]) -> Bool
    /// True if the window's current rect is within tolerance of the saved layout.
    func matches(_ window: WindowInfo, layout: WindowLayout, screens: [ScreenInfo], tolerance: Int) -> Bool
    /// Politely close one window — same as clicking its own close button.
    func closeWindow(_ window: WindowInfo)
    /// True if the given window is a file-manager window currently showing `folder`.
    func windowShowsFolder(_ window: WindowInfo, folder: String) -> Bool
}

/// Starting apps and opening urls/folders the way the OS itself would.
public protocol LaunchService: AnyObject {
    /// Launch an application bundle. Returns the new process id when known.
    func launchApp(path: String, args: String?) async throws -> Int32?
    /// Open every URL in one hand-off to the given browser so they become tabs
    /// in a single window instead of racing each other into separate windows.
    func openURLs(_ urls: [String], browserPath: String?) async throws
    /// Open one folder in its own file-manager window.
    func openFolder(_ path: String) async throws

    /// Ask an app to quit politely (equivalent of Cmd-Q / WM_CLOSE).
    @discardableResult func requestTerminate(pid: Int32) -> Bool
    /// Force-kill an app that ignored the polite request.
    @discardableResult func forceTerminate(pid: Int32) -> Bool
    func isProcessRunning(pid: Int32) -> Bool
}

/// Current display arrangement.
public protocol ScreenService: AnyObject {
    func screens() -> [ScreenInfo]
}

/// The user's default web browser (used for layout tracking of website windows).
public protocol BrowserService: AnyObject {
    func defaultBrowser() -> (path: String, name: String)?
}

/// One bundle of everything platform-specific the core logic needs.
public struct Platform {
    public let windows: WindowService
    public let launcher: LaunchService
    public let screens: ScreenService
    public let browser: BrowserService
    /// The OS file manager used for folder entries (Finder on macOS,
    /// File Explorer on Windows).
    public let fileBrowserPath: String
    public let fileBrowserName: String

    public init(windows: WindowService, launcher: LaunchService, screens: ScreenService,
                browser: BrowserService, fileBrowserPath: String, fileBrowserName: String) {
        self.windows = windows
        self.launcher = launcher
        self.screens = screens
        self.browser = browser
        self.fileBrowserPath = fileBrowserPath
        self.fileBrowserName = fileBrowserName
    }
}

/// Path comparison that ignores trailing separators and case (both APFS's and
/// NTFS's defaults are case-insensitive), so "…/X/" and "…/x" never read as
/// two different folders.
public func pathsEqual(_ a: String?, _ b: String?) -> Bool {
    guard let a, let b else { return false }
    func normalize(_ s: String) -> String {
        var s = (s as NSString).expandingTildeInPath
        while s.count > 1 && s.hasSuffix("/") { s.removeLast() }
        return s.lowercased()
    }
    return normalize(a) == normalize(b)
}

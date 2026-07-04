import AppKit
import ApplicationServices
import DeskifyCore

/// Wraps an AXUIElement so it can travel through DeskifyCore's opaque
/// `WindowInfo.handle`. AXUIElements for the same on-screen window compare
/// equal (CFEqual) and hash equal (CFHash) across scans, which is what makes
/// `windowID` stable enough for new-window detection and claim tracking.
final class AXWindowHandle {
    let element: AXUIElement
    init(_ element: AXUIElement) { self.element = element }
}

/// Window enumeration and control via the Accessibility API — the macOS
/// counterpart of EnumWindows/SetWindowPos. Requires the user to grant
/// Deskify Accessibility permission (System Settings → Privacy & Security).
final class MacWindowService: WindowService {

    var hasPermission: Bool { AXIsProcessTrusted() }

    func requestPermission() {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        AXIsProcessTrustedWithOptions(options)
    }

    // MARK: - Scanning

    func scanWindows() -> [WindowInfo] {
        guard hasPermission else { return [] }
        var results: [WindowInfo] = []
        let ownPid = ProcessInfo.processInfo.processIdentifier

        // NSWorkspace's app list is main-thread state; the AX calls below are
        // plain IPC and safe from any thread (LaunchEngine polls off-main).
        let runningApps = onMainThread { NSWorkspace.shared.runningApplications }

        // .regular = apps with a Dock icon and real windows; background agents,
        // menu-bar utilities, and system services never appear.
        for app in runningApps where app.activationPolicy == .regular {
            let pid = app.processIdentifier
            if pid == ownPid || pid <= 0 { continue }
            guard let bundleURL = app.bundleURL else { continue }
            let appPath = bundleURL.path
            let appName = app.localizedName ?? bundleURL.deletingPathExtension().lastPathComponent
            if ProtectedApps.isProtected(path: appPath, name: appName) { continue }

            let axApp = AXUIElementCreateApplication(pid)
            // A hung app would otherwise block this scan for the default 6s
            // AX timeout — cap it so one stuck process can't stall Deskify.
            AXUIElementSetMessagingTimeout(axApp, 0.3)

            var value: CFTypeRef?
            guard AXUIElementCopyAttributeValue(axApp, kAXWindowsAttribute as CFString, &value) == .success,
                  let windows = value as? [AXUIElement] else { continue }

            for window in windows {
                // Standard windows only — palettes, sheets, and popovers are
                // not part of a workspace layout.
                guard stringAttribute(window, kAXSubroleAttribute) == kAXStandardWindowSubrole else { continue }
                results.append(WindowInfo(
                    handle: AXWindowHandle(window),
                    windowID: Int(bitPattern: CFHash(window)),
                    pid: pid,
                    title: stringAttribute(window, kAXTitleAttribute) ?? "",
                    appPath: appPath,
                    appName: appName))
            }
        }
        return results
    }

    // MARK: - Capture / apply / verify

    func capture(_ window: WindowInfo, screens: [ScreenInfo]) -> WindowLayout? {
        guard let element = element(of: window), let rect = frame(of: element) else { return nil }
        let screen = screens.containing(rect)

        // macOS has no "maximized" window state — the closest native concept is
        // a window filling the screen's visible area (what the zoom button
        // does). Detect and restore it the same way.
        if approximatelyEquals(rect, screen.visibleFrame, tolerance: 8) {
            return WindowLayout(
                monitor: screen.index,
                x: Int(screen.visibleFrame.minX - screen.frame.minX),
                y: Int(screen.visibleFrame.minY - screen.frame.minY),
                width: Int(screen.visibleFrame.width),
                height: Int(screen.visibleFrame.height),
                state: "maximized")
        }

        return WindowLayout(
            monitor: screen.index,
            x: Int(rect.minX - screen.frame.minX),
            y: Int(rect.minY - screen.frame.minY),
            width: Int(rect.width),
            height: Int(rect.height),
            state: "normal")
    }

    @discardableResult
    func apply(_ layout: WindowLayout, to window: WindowInfo, screens: [ScreenInfo]) -> Bool {
        guard let element = element(of: window) else { return false }
        let screen = screens.byIndex(layout.monitor)

        // A minimized window ignores position/size changes — restore it first
        // (same reason the Windows version leaves minimized/maximized state
        // before calling SetWindowPos).
        var minimized: CFTypeRef?
        if AXUIElementCopyAttributeValue(element, kAXMinimizedAttribute as CFString, &minimized) == .success,
           (minimized as? Bool) == true {
            AXUIElementSetAttributeValue(element, kAXMinimizedAttribute as CFString, kCFBooleanFalse)
        }

        let target = layout.isMaximized
            ? screen.visibleFrame
            : CGRect(x: screen.frame.minX + CGFloat(layout.x),
                     y: screen.frame.minY + CGFloat(layout.y),
                     width: CGFloat(max(layout.width, 100)),
                     height: CGFloat(max(layout.height, 80)))

        // Position → size → position: moving across screens first ensures the
        // size isn't clamped to the old screen, and the final position fixes
        // any shift the resize introduced.
        var ok = setPosition(element, target.origin)
        ok = setSize(element, target.size) && ok
        setPosition(element, target.origin)
        return ok
    }

    func matches(_ window: WindowInfo, layout: WindowLayout, screens: [ScreenInfo], tolerance: Int) -> Bool {
        guard let element = element(of: window), let rect = frame(of: element) else { return false }
        let screen = screens.byIndex(layout.monitor)
        let expected = layout.isMaximized
            ? screen.visibleFrame
            : CGRect(x: screen.frame.minX + CGFloat(layout.x),
                     y: screen.frame.minY + CGFloat(layout.y),
                     width: CGFloat(layout.width),
                     height: CGFloat(layout.height))
        return approximatelyEquals(rect, expected, tolerance: CGFloat(tolerance))
    }

    // MARK: - Closing

    /// Same as clicking the window's own red close button.
    func closeWindow(_ window: WindowInfo) {
        guard let element = element(of: window) else { return }
        var button: CFTypeRef?
        if AXUIElementCopyAttributeValue(element, kAXCloseButtonAttribute as CFString, &button) == .success,
           let button {
            // CFTypeRef → AXUIElement: AX buttons are always AXUIElements.
            AXUIElementPerformAction(button as! AXUIElement, kAXPressAction as CFString)
        }
    }

    // MARK: - Finder folder windows

    /// Finder hosts every folder window in one shared process, so multiple
    /// captured folder "apps" are indistinguishable by app alone — what the
    /// window is showing is the only signal. AXDocument carries the folder's
    /// file URL when Finder exposes it; the window title (the folder's display
    /// name) is the fallback.
    func windowShowsFolder(_ window: WindowInfo, folder: String) -> Bool {
        let expanded = (folder as NSString).expandingTildeInPath
        guard let element = element(of: window) else { return false }

        if let doc = stringAttribute(element, kAXDocumentAttribute),
           let url = URL(string: doc), url.isFileURL {
            if pathsEqual(url.path, expanded) { return true }
        }

        let title = window.title
        if pathsEqual(title, expanded) { return true }
        let leaf = FileManager.default.displayName(atPath: expanded)
        return title.caseInsensitiveCompare(leaf) == .orderedSame
            || title.caseInsensitiveCompare((expanded as NSString).lastPathComponent) == .orderedSame
    }

    // MARK: - AX plumbing

    private func element(of window: WindowInfo) -> AXUIElement? {
        (window.handle as? AXWindowHandle)?.element
    }

    private func frame(of element: AXUIElement) -> CGRect? {
        var posRef: CFTypeRef?
        var sizeRef: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXPositionAttribute as CFString, &posRef) == .success,
              AXUIElementCopyAttributeValue(element, kAXSizeAttribute as CFString, &sizeRef) == .success,
              let posRef, let sizeRef else { return nil }

        var origin = CGPoint.zero
        var size = CGSize.zero
        guard AXValueGetValue(posRef as! AXValue, .cgPoint, &origin),
              AXValueGetValue(sizeRef as! AXValue, .cgSize, &size) else { return nil }
        return CGRect(origin: origin, size: size)
    }

    @discardableResult
    private func setPosition(_ element: AXUIElement, _ point: CGPoint) -> Bool {
        var p = point
        guard let value = AXValueCreate(.cgPoint, &p) else { return false }
        return AXUIElementSetAttributeValue(element, kAXPositionAttribute as CFString, value) == .success
    }

    private func setSize(_ element: AXUIElement, _ size: CGSize) -> Bool {
        var s = size
        guard let value = AXValueCreate(.cgSize, &s) else { return false }
        return AXUIElementSetAttributeValue(element, kAXSizeAttribute as CFString, value) == .success
    }

    private func stringAttribute(_ element: AXUIElement, _ attribute: String) -> String? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute as CFString, &value) == .success else { return nil }
        return value as? String
    }

    private func approximatelyEquals(_ a: CGRect, _ b: CGRect, tolerance: CGFloat) -> Bool {
        abs(a.minX - b.minX) <= tolerance
            && abs(a.minY - b.minY) <= tolerance
            && abs(a.width - b.width) <= tolerance
            && abs(a.height - b.height) <= tolerance
    }
}

/// Runs AppKit main-thread-only reads safely from LaunchEngine's background
/// polling. The main thread is never blocked on the engine (everything up
/// there is async), so a sync hop can't deadlock.
func onMainThread<T>(_ body: () -> T) -> T {
    Thread.isMainThread ? body() : DispatchQueue.main.sync(execute: body)
}

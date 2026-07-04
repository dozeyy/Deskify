import SwiftUI
import AppKit
import UniformTypeIdentifiers
import DeskifyCore

// MARK: - App picker

/// Lists every real, user-recognizable application on the Mac — the standard
/// Applications folders with utilities/uninstaller noise filtered out —
/// searchable, multi-select. Browse still covers anything the scan misses.
struct AppPickerView: View {
    @EnvironmentObject var app: AppState
    @Environment(\.theme) private var theme
    let onAdd: ([AppEntry]) -> Void

    @State private var all: [PickerItem] = []
    @State private var query = ""
    @State private var loading = true

    struct PickerItem: Identifiable {
        let id = UUID()
        let name: String
        let path: String
        var selected = false
    }

    private var filtered: [PickerItem] {
        let q = query.trimmingCharacters(in: .whitespaces)
        guard !q.isEmpty else { return all }
        return all.filter {
            $0.name.localizedCaseInsensitiveContains(q) || $0.path.localizedCaseInsensitiveContains(q)
        }
    }

    var body: some View {
        SheetChrome(title: "Add applications") {
            DeskTextField(placeholder: "Search apps…", text: $query)
                .padding(.bottom, 10)

            ScrollView {
                LazyVStack(spacing: 4) {
                    ForEach(filtered) { item in
                        SelectableRow(selected: binding(for: item)) {
                            AppIconView(path: item.path, size: 18)
                            VStack(alignment: .leading, spacing: 1) {
                                Text(item.name)
                                    .font(.system(size: 13, weight: .medium))
                                    .foregroundStyle(theme.text)
                                    .lineLimit(1)
                                Text(item.path)
                                    .font(.system(size: 11))
                                    .foregroundStyle(theme.textMuted)
                                    .lineLimit(1)
                                    .truncationMode(.middle)
                            }
                            Spacer(minLength: 4)
                        }
                    }
                    if !loading && filtered.isEmpty {
                        Text("No apps match your search.")
                            .font(.system(size: 12))
                            .foregroundStyle(theme.textMuted)
                            .padding(.top, 30)
                    }
                }
            }
            .frame(minHeight: 320)
        } footer: {
            Text(countText)
                .font(.system(size: 12))
                .foregroundStyle(theme.textMuted)
            Spacer()
            Button("Browse…") { browse() }
                .buttonStyle(DeskButtonStyle())
            Button("Cancel") { app.activeSheet = nil }
                .buttonStyle(GhostButtonStyle())
            Button("Add Selected") { addSelected() }
                .buttonStyle(AccentButtonStyle())
                .disabled(!all.contains(where: \.selected))
        }
        .task {
            let scanned = await Task.detached(priority: .userInitiated) { InstalledAppsScanner.scan() }.value
            all = scanned.map { PickerItem(name: $0.name, path: $0.path) }
            loading = false
        }
    }

    private var countText: String {
        if loading { return "Scanning installed apps…" }
        let selected = all.filter(\.selected).count
        return selected > 0
            ? "\(filtered.count) shown · \(selected) selected"
            : "\(filtered.count) app\(filtered.count == 1 ? "" : "s") found"
    }

    private func binding(for item: PickerItem) -> Binding<Bool> {
        Binding(
            get: { all.first(where: { $0.id == item.id })?.selected ?? false },
            set: { value in
                if let idx = all.firstIndex(where: { $0.id == item.id }) { all[idx].selected = value }
            })
    }

    private func addSelected() {
        let entries = all.filter(\.selected).map { AppEntry(name: $0.name, path: $0.path) }
        guard !entries.isEmpty else { return }
        onAdd(entries)
        app.activeSheet = nil
    }

    private func browse() {
        let panel = NSOpenPanel()
        panel.title = "Add application"
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowedContentTypes = [.applicationBundle]
        panel.directoryURL = URL(fileURLWithPath: "/Applications")
        guard panel.runModal() == .OK, let url = panel.url else { return }

        // Resolve aliases/symlinks so the stored path is the app's real home.
        let resolved = url.resolvingSymlinksInPath()
        let name = FileManager.default.displayName(atPath: resolved.path)
        let cleanName = name.hasSuffix(".app") ? String(name.dropLast(4)) : name
        if ProtectedApps.isProtected(path: resolved.path, name: cleanName) {
            NSSound.beep()
            return
        }
        onAdd([AppEntry(name: cleanName, path: resolved.path)])
        app.activeSheet = nil
    }
}

// MARK: - Capture session

/// Lists running application windows; the user picks which become project apps.
/// Nothing is saved automatically — the caller decides what to do with the result.
struct CaptureView: View {
    @EnvironmentObject var app: AppState
    @Environment(\.theme) private var theme
    let onAdd: ([AppEntry]) -> Void

    @State private var items: [CaptureItem] = []

    struct CaptureItem: Identifiable {
        let id = UUID()
        let window: WindowInfo
        let monitorText: String
        var selected = false
    }

    var body: some View {
        SheetChrome(title: "Capture session",
                    subtitle: "Pick from windows open right now — their current positions become the layout.") {
            if !app.accessibilityGranted {
                permissionNotice
            }
            ScrollView {
                LazyVStack(spacing: 4) {
                    ForEach(items) { item in
                        SelectableRow(selected: binding(for: item)) {
                            AppIconView(path: item.window.appPath, size: 18)
                            VStack(alignment: .leading, spacing: 1) {
                                Text(item.window.title.isEmpty ? item.window.appName : item.window.title)
                                    .font(.system(size: 13, weight: .medium))
                                    .foregroundStyle(theme.text)
                                    .lineLimit(1)
                                Text(item.window.appPath)
                                    .font(.system(size: 11))
                                    .foregroundStyle(theme.textMuted)
                                    .lineLimit(1)
                                    .truncationMode(.middle)
                            }
                            Spacer(minLength: 4)
                            Chip(text: item.monitorText)
                        }
                    }
                    if items.isEmpty && app.accessibilityGranted {
                        Text("No application windows found.")
                            .font(.system(size: 12))
                            .foregroundStyle(theme.textMuted)
                            .padding(.top, 30)
                    }
                }
            }
            .frame(minHeight: 300)
        } footer: {
            Text(countText)
                .font(.system(size: 12))
                .foregroundStyle(theme.textMuted)
            Spacer()
            Button {
                rescan()
            } label: {
                Label("Refresh", systemImage: "arrow.counterclockwise")
            }
            .buttonStyle(DeskButtonStyle())
            Button("Cancel") { app.activeSheet = nil }
                .buttonStyle(GhostButtonStyle())
            Button("Add Selected") { addSelected() }
                .buttonStyle(AccentButtonStyle())
                .disabled(!items.contains(where: \.selected))
        }
        .onAppear { rescan() }
    }

    private var permissionNotice: some View {
        HStack {
            Text("Deskify needs Accessibility permission to see other apps' windows.")
                .font(.system(size: 12))
                .foregroundStyle(theme.danger)
            Spacer()
            Button("Grant Access") { app.requestAccessibility() }
                .buttonStyle(DeskButtonStyle())
        }
        .padding(.bottom, 8)
    }

    private var countText: String {
        let selected = items.filter(\.selected).count
        return selected > 0
            ? "\(items.count) window\(items.count == 1 ? "" : "s") · \(selected) selected"
            : "\(items.count) window\(items.count == 1 ? "" : "s") found"
    }

    private func binding(for item: CaptureItem) -> Binding<Bool> {
        Binding(
            get: { items.first(where: { $0.id == item.id })?.selected ?? false },
            set: { value in
                if let idx = items.firstIndex(where: { $0.id == item.id }) { items[idx].selected = value }
            })
    }

    private func rescan() {
        app.refreshAccessibility()
        let screens = app.platform.screens.screens()
        items = app.platform.windows.scanWindows()
            .map { w in
                let layout = app.platform.windows.capture(w, screens: screens)
                return CaptureItem(window: w, monitorText: "Monitor \((layout?.monitor ?? 0) + 1)")
            }
            .sorted {
                let a = ($0.window.appName, $0.window.title)
                let b = ($1.window.appName, $1.window.title)
                return a < b
            }
    }

    private func addSelected() {
        // Capture placements at confirm time so last-second window moves count.
        let screens = app.platform.screens.screens()
        var entries: [AppEntry] = []
        for item in items where item.selected {
            let w = item.window
            let entry = AppEntry(
                name: w.appName,
                path: w.appPath,
                window: app.platform.windows.capture(w, screens: screens))

            // Finder hosts every folder window in one shared process —
            // capture the specific folder this window is showing so launch
            // can reopen (and layout can find) the right window.
            if pathsEqual(w.appPath, app.platform.fileBrowserPath) {
                entry.name = w.title.isEmpty ? "Finder" : w.title
                entry.args = folderPath(for: w)
            }
            entries.append(entry)
        }
        guard !entries.isEmpty else { return }
        onAdd(entries)
        app.activeSheet = nil
    }

    /// Best-effort folder path for a Finder window. AXDocument gives the real
    /// path when Finder exposes it; otherwise the window title (the folder's
    /// display name) is probed against common locations. Returns nil when no
    /// real directory can be recovered — the entry then behaves like any app
    /// whose window can't be re-opened to a specific document.
    private func folderPath(for window: WindowInfo) -> String? {
        let fm = FileManager.default
        func realDir(_ path: String) -> String? {
            let expanded = (path as NSString).expandingTildeInPath
            var isDir: ObjCBool = false
            return fm.fileExists(atPath: expanded, isDirectory: &isDir) && isDir.boolValue ? expanded : nil
        }

        let title = window.title
        guard !title.isEmpty else { return nil }
        if title.hasPrefix("/") || title.hasPrefix("~") { return realDir(title) }

        let home = fm.homeDirectoryForCurrentUser.path
        let candidates = [
            "\(home)/\(title)", "\(home)/Desktop/\(title)", "\(home)/Documents/\(title)",
            "\(home)/Downloads/\(title)", "/Applications/\(title)", "/\(title)",
        ]
        return candidates.compactMap(realDir).first
    }
}

// MARK: - Close Other Apps confirmation

/// Confirms which non-project windows to close before "Close Other Apps" or a
/// workspace switch runs. Nothing closes until the user confirms.
struct CloseOthersView: View {
    @EnvironmentObject var app: AppState
    @Environment(\.theme) private var theme
    let request: CloseOthersRequest

    @State private var selection: [Bool]
    @State private var dontAskAgain = false

    init(request: CloseOthersRequest) {
        self.request = request
        // Everything checked by default — sized here so the ForEach bindings
        // are valid on the very first render.
        _selection = State(initialValue: Array(repeating: true, count: request.windows.count))
    }

    var body: some View {
        SheetChrome(
            title: request.mode == .workspaceSwitch ? "Switch workspace?" : "Close other apps?",
            subtitle: request.mode == .workspaceSwitch
                ? "These aren't part of \"\(request.project.name)\" — they'll be closed before it launches. Uncheck anything you want to leave open."
                : "These aren't part of \"\(request.project.name)\". Uncheck anything you want to leave open."
        ) {
            ScrollView {
                LazyVStack(spacing: 4) {
                    ForEach(sortedIndices, id: \.self) { idx in
                        let w = request.windows[idx]
                        SelectableRow(selected: $selection[idx]) {
                            AppIconView(path: w.appPath, size: 18)
                            VStack(alignment: .leading, spacing: 1) {
                                Text(w.title.isEmpty ? w.appName : w.title)
                                    .font(.system(size: 13, weight: .medium))
                                    .foregroundStyle(theme.text)
                                    .lineLimit(1)
                                Text(w.appName)
                                    .font(.system(size: 11))
                                    .foregroundStyle(theme.textMuted)
                                    .lineLimit(1)
                            }
                            Spacer(minLength: 4)
                        }
                    }
                }
            }
            .frame(minHeight: 240)

            LabeledCheckBox(isOn: $dontAskAgain, label: "Don't ask again")
                .padding(.top, 10)
        } footer: {
            Text(countText)
                .font(.system(size: 12))
                .foregroundStyle(theme.textMuted)
            Spacer()
            Button("Cancel") { app.activeSheet = nil }
                .buttonStyle(GhostButtonStyle())
            Button(confirmText) { confirm() }
                .buttonStyle(AccentButtonStyle())
                .disabled(selectedCount == 0)
        }
    }

    private var sortedIndices: [Int] {
        request.windows.indices.sorted {
            let a = (request.windows[$0].appName, request.windows[$0].title)
            let b = (request.windows[$1].appName, request.windows[$1].title)
            return a < b
        }
    }

    private var selectedCount: Int { selection.filter { $0 }.count }

    private var countText: String {
        "\(selectedCount) of \(request.windows.count) window\(request.windows.count == 1 ? "" : "s") selected"
    }

    private var confirmText: String {
        request.mode == .workspaceSwitch
            ? "Switch Workspace"
            : (selectedCount == 1 ? "Close 1 Window" : "Close \(selectedCount) Windows")
    }

    private func confirm() {
        let chosen = request.windows.indices.filter { selection.indices.contains($0) && selection[$0] }
            .map { request.windows[$0] }
        app.confirmCloseOthers(request, windows: chosen, dontAskAgain: dontAskAgain)
    }
}

// MARK: - Shared sheet scaffolding

/// Common sheet frame: title, optional subtitle, content, footer button row.
struct SheetChrome<Content: View, Footer: View>: View {
    @Environment(\.theme) private var theme
    let title: String
    var subtitle: String?
    @ViewBuilder var content: Content
    @ViewBuilder var footer: Footer

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(title)
                .font(.system(size: 17, weight: .semibold))
                .foregroundStyle(theme.text)
            if let subtitle {
                Text(subtitle)
                    .font(.system(size: 12))
                    .foregroundStyle(theme.textMuted)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.top, 4)
            }
            content
                .padding(.top, 14)
            HStack(spacing: 8) { footer }
                .padding(.top, 14)
        }
        .padding(20)
        .frame(width: 560)
        .background(theme.bg)
    }
}

/// Row with a leading checkbox; clicking anywhere toggles.
struct SelectableRow<Content: View>: View {
    @Environment(\.theme) private var theme
    @Binding var selected: Bool
    @ViewBuilder var content: Content

    var body: some View {
        HStack(spacing: 10) {
            CheckBox(isOn: $selected)
            content
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(selected ? theme.accentSoft : theme.card)
        .overlay(RoundedRectangle(cornerRadius: 8).stroke(theme.strokeSoft, lineWidth: 1))
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .contentShape(RoundedRectangle(cornerRadius: 8))
        .onTapGesture { selected.toggle() }
    }
}

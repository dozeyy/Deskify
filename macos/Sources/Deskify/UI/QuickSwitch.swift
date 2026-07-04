import SwiftUI
import AppKit
import DeskifyCore

/// Global-hotkey popup (⌃Space): opens with the search box focused — type to
/// filter, ↑/↓ to move, Return or click to launch, Esc or clicking away to
/// close. Nothing launches until the user explicitly confirms, so an
/// accidental hotkey press never launches a whole workspace by itself.
@MainActor
final class QuickSwitchController {
    private weak var appState: AppState?
    private var panel: NSPanel?
    private var keyMonitor: Any?
    private var model: QuickSwitchModel?

    init(appState: AppState) {
        self.appState = appState
    }

    /// ⌃Space is a toggle: pressing it again while the popup is open closes it.
    func toggle() {
        if panel != nil { dismiss() } else { show() }
    }

    private func show() {
        guard let appState else { return }

        let model = QuickSwitchModel(projects: appState.projects)
        model.onChoose = { [weak self] project in
            self?.dismiss()
            self?.appState?.quickSwitchChose(project)
        }
        self.model = model

        let content = QuickSwitchView(model: model)
            .environment(\.theme, appState.theme)
        let hosting = NSHostingController(rootView: AnyView(content))

        let panel = KeyablePanel(contentViewController: hosting)
        panel.styleMask = [.nonactivatingPanel, .borderless]
        panel.level = .floating
        panel.collectionBehavior = [.canJoinAllSpaces, .transient]
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false

        // Centered horizontally, upper third of the screen the pointer is on.
        let screen = NSScreen.screens.first { $0.frame.contains(NSEvent.mouseLocation) } ?? NSScreen.main
        if let screen {
            let size = NSSize(width: 560, height: 420)
            panel.setFrame(NSRect(
                x: screen.visibleFrame.midX - size.width / 2,
                y: screen.visibleFrame.maxY - size.height - screen.visibleFrame.height * 0.18,
                width: size.width, height: size.height), display: false)
        }

        self.panel = panel
        panel.makeKeyAndOrderFront(nil)

        // Navigation keys are handled here so they work even while the search
        // field has keyboard focus.
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, self.panel?.isKeyWindow == true else { return event }
            switch event.keyCode {
            case 53: self.dismiss(); return nil                 // esc
            case 125: self.model?.moveSelection(1); return nil  // down
            case 126: self.model?.moveSelection(-1); return nil // up
            case 36, 76: self.model?.confirm(); return nil      // return / enter
            default: return event
            }
        }

        // Clicking away (the panel losing key status) dismisses, same as Esc.
        NotificationCenter.default.addObserver(
            self, selector: #selector(panelResignedKey),
            name: NSWindow.didResignKeyNotification, object: panel)
    }

    @objc private func panelResignedKey() { dismiss() }

    func dismiss() {
        if let keyMonitor { NSEvent.removeMonitor(keyMonitor) }
        keyMonitor = nil
        if let panel {
            NotificationCenter.default.removeObserver(self, name: NSWindow.didResignKeyNotification, object: panel)
            panel.orderOut(nil)
        }
        panel = nil
        model = nil
    }
}

/// A borderless, non-activating panel that can still take keyboard focus —
/// what lets Quick Switch accept typing without yanking the user out of
/// whatever app they were in.
private final class KeyablePanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }
}

// MARK: - Model

final class QuickSwitchModel: ObservableObject {
    @Published var query = "" {
        didSet { applyFilter() }
    }
    @Published private(set) var filtered: [DeskifyProject] = []
    @Published var selectedIndex = 0

    var onChoose: ((DeskifyProject) -> Void)?
    private let all: [DeskifyProject]

    init(projects: [DeskifyProject]) {
        all = projects.sorted { a, b in
            if a.pinned != b.pinned { return a.pinned }
            switch (a.lastUsed, b.lastUsed) {
            case let (x?, y?) where x != y: return x > y
            case (.some, .none): return true
            case (.none, .some): return false
            default: return a.name.localizedCaseInsensitiveCompare(b.name) == .orderedAscending
            }
        }
        applyFilter()
    }

    private func applyFilter() {
        let q = query.trimmingCharacters(in: .whitespaces)
        filtered = q.isEmpty ? all : all.filter { $0.name.localizedCaseInsensitiveContains(q) }
        selectedIndex = 0
    }

    func moveSelection(_ delta: Int) {
        guard !filtered.isEmpty else { return }
        selectedIndex = min(max(selectedIndex + delta, 0), filtered.count - 1)
    }

    func confirm() {
        guard filtered.indices.contains(selectedIndex) else { return }
        onChoose?(filtered[selectedIndex])
    }
}

// MARK: - View

struct QuickSwitchView: View {
    @Environment(\.theme) private var theme
    @ObservedObject var model: QuickSwitchModel
    @FocusState private var searchFocused: Bool

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                Image(systemName: "magnifyingglass")
                    .font(.system(size: 14))
                    .foregroundStyle(theme.textMuted)
                TextField("Search projects…", text: $model.query)
                    .textFieldStyle(.plain)
                    .font(.system(size: 16))
                    .foregroundStyle(theme.text)
                    .focused($searchFocused)
                    .onSubmit { model.confirm() }
            }
            .padding(EdgeInsets(top: 14, leading: 16, bottom: 14, trailing: 16))

            Rectangle().fill(theme.strokeSoft).frame(height: 1)

            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(spacing: 2) {
                        ForEach(Array(model.filtered.enumerated()), id: \.element.id) { index, project in
                            row(project, selected: index == model.selectedIndex)
                                .id(index)
                                .onTapGesture { model.onChoose?(project) }
                        }
                        if model.filtered.isEmpty {
                            Text("No matching projects.")
                                .font(.system(size: 12))
                                .foregroundStyle(theme.textMuted)
                                .padding(.top, 30)
                        }
                    }
                    .padding(8)
                }
                .onChange(of: model.selectedIndex) { index in
                    withAnimation(.easeOut(duration: 0.1)) { proxy.scrollTo(index) }
                }
            }
        }
        .frame(width: 560, height: 420)
        .background(theme.panel)
        .overlay(RoundedRectangle(cornerRadius: 14).stroke(theme.stroke, lineWidth: 1))
        .clipShape(RoundedRectangle(cornerRadius: 14))
        .onAppear { searchFocused = true }
    }

    private func row(_ project: DeskifyProject, selected: Bool) -> some View {
        HStack(spacing: 10) {
            if project.pinned {
                Image(systemName: "pin.fill")
                    .font(.system(size: 10))
                    .foregroundStyle(theme.accent)
            }
            VStack(alignment: .leading, spacing: 2) {
                Text(project.name)
                    .font(.system(size: 14, weight: .medium))
                    .foregroundStyle(theme.text)
                    .lineLimit(1)
                Text("\(project.summaryText)  ·  \(project.lastUsedText)")
                    .font(.system(size: 11))
                    .foregroundStyle(theme.textMuted)
                    .lineLimit(1)
            }
            Spacer(minLength: 8)
            IconCluster(paths: project.apps.map(\.path))
        }
        .padding(EdgeInsets(top: 10, leading: 12, bottom: 10, trailing: 12))
        .background(selected ? theme.accentSoft : .clear)
        .overlay(RoundedRectangle(cornerRadius: 8).stroke(selected ? theme.stroke : .clear, lineWidth: 1))
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .contentShape(RoundedRectangle(cornerRadius: 8))
    }
}

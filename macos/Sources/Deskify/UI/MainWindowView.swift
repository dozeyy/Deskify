import SwiftUI
import DeskifyCore

/// Three-column shell: sidebar · center · right context — the same layout as
/// the Windows main window, with the floating layout toolbar overlaid at the
/// bottom while Edit Layout is active.
struct MainWindowView: View {
    @EnvironmentObject var app: AppState

    var body: some View {
        let theme = app.theme
        ZStack {
            HStack(spacing: 0) {
                SidebarView()

                centerView
                    .frame(maxWidth: .infinity, maxHeight: .infinity)

                if app.screen == .detail, let project = app.selected {
                    RightPanelView(project: project)
                        .frame(width: 316)
                        .background(theme.panel)
                        .overlay(alignment: .leading) {
                            Rectangle().fill(theme.strokeSoft).frame(width: 1)
                        }
                }
            }

            if app.layoutToolbarVisible {
                VStack {
                    Spacer()
                    LayoutToolbar()
                        .padding(.bottom, 30)
                }
            }
        }
        .background(theme.bg)
        .environment(\.theme, theme)
        .sheet(item: $app.activeSheet) { sheet in
            sheetView(sheet)
                .environment(\.theme, theme)
        }
        .alert("Delete project", isPresented: Binding(
            get: { app.deleteConfirm != nil },
            set: { if !$0 { app.deleteConfirm = nil } })
        ) {
            Button("Delete", role: .destructive) {
                if let project = app.deleteConfirm { app.deleteProject(project) }
                app.deleteConfirm = nil
            }
            Button("Cancel", role: .cancel) { app.deleteConfirm = nil }
        } message: {
            Text("Delete \"\(app.deleteConfirm?.name ?? "")\"? This cannot be undone.")
        }
    }

    @ViewBuilder
    private var centerView: some View {
        switch app.screen {
        case .projects: ProjectsListView()
        case .detail: ProjectDetailView()
        case .wizard: WizardView()
        case .settings: SettingsView()
        }
    }

    @ViewBuilder
    private func sheetView(_ sheet: AppState.Sheet) -> some View {
        switch sheet {
        case .appPicker:
            AppPickerView { entries in
                guard let wizard = app.wizard else { return }
                for entry in entries where !wizard.apps.contains(where: { pathsEqual($0.path, entry.path) }) {
                    wizard.apps.append(entry)
                }
            }
        case .capture(let intoWizard):
            CaptureView { entries in
                if intoWizard {
                    app.wizard?.apps.append(contentsOf: entries)
                } else {
                    app.newCaptureProject(entries)
                }
            }
        case .closeOthers(let request):
            CloseOthersView(request: request)
        }
    }
}

// MARK: - Sidebar

struct SidebarView: View {
    @EnvironmentObject var app: AppState
    @Environment(\.theme) private var theme

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            // Brand
            HStack(spacing: 11) {
                BrandMark(size: 30)
                if !app.sidebarCollapsed {
                    Text("Deskify")
                        .font(.system(size: 16, weight: .semibold))
                        .foregroundStyle(theme.text)
                }
            }
            .frame(maxWidth: .infinity, alignment: app.sidebarCollapsed ? .center : .leading)
            .padding(.horizontal, 4)
            .padding(.bottom, 22)

            // Nav
            navButton("Projects", icon: "square.stack.3d.up",
                      active: app.screen != .settings) { app.showProjects() }
                .padding(.bottom, 4)
            navButton("Settings", icon: "slider.horizontal.3",
                      active: app.screen == .settings) { app.showSettings() }

            Spacer()

            navButton(app.sidebarCollapsed ? "" : "Collapse",
                      icon: app.sidebarCollapsed ? "chevron.right" : "chevron.left",
                      active: false) {
                withAnimation(.easeOut(duration: 0.15)) { app.sidebarCollapsed.toggle() }
            }
            .help(app.sidebarCollapsed ? "Expand sidebar" : "Collapse sidebar")
        }
        .padding(EdgeInsets(top: 18, leading: 14, bottom: 14, trailing: 14))
        .frame(width: app.sidebarCollapsed ? 76 : 216)
        .frame(maxHeight: .infinity)
        .background(theme.panel)
        .overlay(alignment: .trailing) {
            Rectangle().fill(theme.strokeSoft).frame(width: 1)
        }
    }

    private func navButton(_ label: String, icon: String, active: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            HStack(spacing: 12) {
                Image(systemName: icon)
                    .font(.system(size: 14, weight: .medium))
                    .frame(width: 19)
                if !app.sidebarCollapsed && !label.isEmpty {
                    Text(label).font(.system(size: 13))
                    Spacer(minLength: 0)
                }
            }
            .foregroundStyle(active ? theme.text : theme.textDim)
            .padding(.horizontal, 12)
            .padding(.vertical, 9)
            .frame(maxWidth: .infinity, alignment: app.sidebarCollapsed ? .center : .leading)
            .background(active ? theme.accentSoft : .clear)
            .clipShape(RoundedRectangle(cornerRadius: 8))
            .contentShape(RoundedRectangle(cornerRadius: 8))
        }
        .buttonStyle(.plain)
    }
}

// MARK: - Floating layout toolbar

struct LayoutToolbar: View {
    @EnvironmentObject var app: AppState
    @Environment(\.theme) private var theme

    var body: some View {
        HStack(spacing: 4) {
            Image(systemName: "rectangle.split.2x1")
                .font(.system(size: 13, weight: .medium))
                .foregroundStyle(theme.accent)
                .padding(.leading, 4)

            Text(app.snapEnabled
                 ? "Snap on — positions round to \(app.settings.snapGridSize)pt on save"
                 : "Arrange your windows, then save")
                .font(.system(size: 12.5))
                .foregroundStyle(theme.textDim)
                .padding(.horizontal, 10)

            Rectangle().fill(theme.stroke).frame(width: 1, height: 20)
                .padding(.trailing, 6)

            Button {
                app.snapEnabled.toggle()
            } label: {
                Label("Snap", systemImage: "square.grid.3x3")
                    .foregroundStyle(app.snapEnabled ? theme.accent : theme.textDim)
            }
            .buttonStyle(GhostButtonStyle())
            .help("Snap saved positions to the grid")

            Button {
                app.cancelLayoutClicked()
            } label: {
                Label("Reset", systemImage: "arrow.counterclockwise")
            }
            .buttonStyle(GhostButtonStyle())

            Button {
                app.saveLayoutClicked()
            } label: {
                Label("Save Layout", systemImage: "square.and.arrow.down")
            }
            .buttonStyle(AccentButtonStyle())
        }
        .padding(EdgeInsets(top: 9, leading: 10, bottom: 9, trailing: 10))
        .background(theme.card)
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(theme.stroke, lineWidth: 1))
        .clipShape(RoundedRectangle(cornerRadius: 12))
        .shadow(color: .black.opacity(0.45), radius: 14, y: 6)
    }
}

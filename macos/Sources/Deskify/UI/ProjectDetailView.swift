import SwiftUI
import DeskifyCore

/// Center column of the detail screen: name, actions, layout card, status.
struct ProjectDetailView: View {
    @EnvironmentObject var app: AppState
    @Environment(\.theme) private var theme

    var body: some View {
        ScrollView {
            if let project = app.selected {
                VStack(alignment: .leading, spacing: 0) {
                    Button {
                        app.showProjects()
                    } label: {
                        Label("Projects", systemImage: "chevron.left")
                    }
                    .buttonStyle(GhostButtonStyle())
                    .padding(.leading, -6)
                    .padding(.bottom, 10)

                    HStack(alignment: .center) {
                        VStack(alignment: .leading, spacing: 4) {
                            Text(project.name)
                                .font(.system(size: 22, weight: .semibold))
                                .foregroundStyle(theme.text)
                            Text("\(project.summaryText)  ·  \(project.lastUsedText)")
                                .font(.system(size: 12))
                                .foregroundStyle(theme.textMuted)
                        }
                        Spacer()
                        Button {
                            app.closeOthersClicked()
                        } label: {
                            Label("Close Other Apps", systemImage: "xmark.rectangle")
                        }
                        .buttonStyle(DeskButtonStyle())
                        .disabled(app.launching)
                        .help("Close every open app except this project's")

                        Button {
                            app.requestLaunch(project)
                        } label: {
                            Label("Launch", systemImage: "play.fill")
                        }
                        .buttonStyle(DeskButtonStyle())
                        .disabled(app.launching)
                    }

                    IconCluster(paths: project.apps.map(\.path))
                        .padding(.top, 16)

                    // Layout controls
                    CardView {
                        HStack {
                            VStack(alignment: .leading, spacing: 3) {
                                Text("Window layout")
                                    .font(.system(size: 15, weight: .semibold))
                                    .foregroundStyle(theme.text)
                                Text(layoutModeText(project))
                                    .font(.system(size: 12))
                                    .foregroundStyle(theme.textMuted)
                            }
                            Spacer()
                            Button {
                                app.editLayoutClicked()
                            } label: {
                                Label("Edit Layout", systemImage: "rectangle.split.2x1")
                            }
                            .buttonStyle(DeskButtonStyle())
                            .disabled(app.launching)
                        }
                    }
                    .padding(.top, 20)

                    if !app.statusText.isEmpty {
                        Text(app.statusText)
                            .font(.system(size: 12))
                            .foregroundStyle(theme.textDim)
                            .padding(.top, 14)
                            .padding(.leading, 2)
                    }
                    ForEach(app.errors, id: \.self) { error in
                        Text(error)
                            .font(.system(size: 12))
                            .foregroundStyle(theme.danger)
                            .padding(.top, 2)
                            .padding(.leading, 2)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
                .frame(maxWidth: 720, alignment: .leading)
                .frame(maxWidth: .infinity)
            }
        }
        .padding(EdgeInsets(top: 20, leading: 32, bottom: 28, trailing: 32))
    }

    private func layoutModeText(_ project: DeskifyProject) -> String {
        let withLayout = project.apps.filter { $0.window != nil }.count
        return withLayout > 0
            ? "Saved for \(withLayout) app\(withLayout == 1 ? "" : "s") · positions \(project.strictLayout ? "locked" : "flexible")"
            : "No layout saved yet — use Edit Layout."
    }
}

// MARK: - Right context panel

struct RightPanelView: View {
    @EnvironmentObject var app: AppState
    @Environment(\.theme) private var theme
    let project: DeskifyProject
    @State private var notesText = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Eyebrow(text: "Contents")
                .padding(.bottom, 8)

            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    Eyebrow(text: "Notes")
                        .padding(.top, 8)
                        .padding(.bottom, 6)
                    TextEditor(text: $notesText)
                        .font(.system(size: 12))
                        .foregroundStyle(theme.text)
                        .scrollContentBackground(.hidden)
                        .padding(6)
                        .frame(minHeight: 72, maxHeight: 200)
                        .background(theme.card)
                        .overlay(RoundedRectangle(cornerRadius: 8).stroke(theme.stroke, lineWidth: 1))
                        .clipShape(RoundedRectangle(cornerRadius: 8))
                        .onValueChange(of: notesText) { newValue in
                            app.updateNotes(project, text: newValue)
                        }

                    Eyebrow(text: "Apps")
                        .padding(.top, 18)
                        .padding(.bottom, 6)
                    if project.apps.isEmpty {
                        emptyLabel("No apps.")
                    } else {
                        ForEach(project.apps) { entry in
                            AppRowReadonly(entry: entry)
                        }
                    }

                    Eyebrow(text: "Websites")
                        .padding(.top, 16)
                        .padding(.bottom, 6)
                    if project.urls.isEmpty {
                        emptyLabel("No websites.")
                    } else {
                        ForEach(project.urls, id: \.self) { url in
                            stringRow(url)
                        }
                    }

                    Eyebrow(text: "Folders")
                        .padding(.top, 16)
                        .padding(.bottom, 6)
                    if project.folders.isEmpty {
                        emptyLabel("No folders.")
                    } else {
                        ForEach(project.folders, id: \.self) { folder in
                            stringRow(folder)
                        }
                    }
                }
            }

            Spacer(minLength: 14)

            VStack(spacing: 6) {
                panelButton("Edit project", icon: "pencil") { app.editProject() }
                panelButton("Duplicate", icon: "doc.on.doc") { app.duplicateProject() }
                Button {
                    app.deleteConfirm = project
                } label: {
                    Label("Delete", systemImage: "trash")
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                .buttonStyle(DeskButtonStyle(danger: true))
            }
        }
        .padding(EdgeInsets(top: 22, leading: 18, bottom: 18, trailing: 18))
        .onAppear { notesText = project.notes ?? "" }
        .id(project.id) // reset notes state when a different project is shown
    }

    private func emptyLabel(_ text: String) -> some View {
        Text(text).font(.system(size: 12)).foregroundStyle(theme.textMuted)
    }

    private func stringRow(_ text: String) -> some View {
        Text(text)
            .font(.system(size: 12))
            .foregroundStyle(theme.text)
            .lineLimit(1)
            .truncationMode(.middle)
            .padding(.horizontal, 10)
            .padding(.vertical, 8)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(theme.card)
            .overlay(RoundedRectangle(cornerRadius: 8).stroke(theme.strokeSoft, lineWidth: 1))
            .clipShape(RoundedRectangle(cornerRadius: 8))
            .padding(.bottom, 4)
    }

    private func panelButton(_ label: String, icon: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Label(label, systemImage: icon)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .buttonStyle(DeskButtonStyle())
    }
}

/// Read-only app row: icon, name, path, layout chip (right context panel).
struct AppRowReadonly: View {
    @Environment(\.theme) private var theme
    let entry: AppEntry

    var body: some View {
        HStack(spacing: 10) {
            AppIconView(path: entry.path, size: 18)
            VStack(alignment: .leading, spacing: 1) {
                Text(entry.name)
                    .font(.system(size: 12, weight: .medium))
                    .foregroundStyle(theme.text)
                    .lineLimit(1)
                Text(entry.path)
                    .font(.system(size: 10))
                    .foregroundStyle(theme.textMuted)
                    .lineLimit(1)
                    .truncationMode(.middle)
            }
            Spacer(minLength: 4)
            if let layout = entry.window {
                Chip(text: layoutText(layout))
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 8)
        .background(theme.card)
        .overlay(RoundedRectangle(cornerRadius: 8).stroke(theme.strokeSoft, lineWidth: 1))
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .padding(.bottom, 4)
    }

    private func layoutText(_ w: WindowLayout) -> String {
        w.isMaximized ? "Mon \(w.monitor + 1) · max" : "Mon \(w.monitor + 1) · \(w.width)×\(w.height)"
    }
}

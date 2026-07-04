import SwiftUI
import DeskifyCore

/// Home screen: header with Capture session / New Project, then the project rows.
struct ProjectsListView: View {
    @EnvironmentObject var app: AppState
    @Environment(\.theme) private var theme

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            // Header
            HStack(alignment: .center) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Projects")
                        .font(.system(size: 22, weight: .semibold))
                        .foregroundStyle(theme.text)
                    Text(countText)
                        .font(.system(size: 12))
                        .foregroundStyle(theme.textMuted)
                }
                Spacer()
                Button {
                    app.activeSheet = .capture(intoWizard: false)
                } label: {
                    Label("Capture session", systemImage: "display")
                }
                .buttonStyle(DeskButtonStyle())
                .help("Build a project from windows open right now")

                Button {
                    app.newProject()
                } label: {
                    Label("New Project", systemImage: "plus")
                }
                .buttonStyle(AccentButtonStyle())
            }
            .padding(.bottom, 16)

            ScrollView {
                LazyVStack(spacing: 10) {
                    ForEach(app.projects) { project in
                        ProjectRowView(project: project)
                    }
                }
                if app.projects.isEmpty {
                    emptyState.padding(.top, 80)
                }
            }
        }
        .padding(EdgeInsets(top: 20, leading: 32, bottom: 20, trailing: 32))
    }

    private var countText: String {
        switch app.projects.count {
        case 0: return "No projects yet"
        case 1: return "1 project"
        default: return "\(app.projects.count) projects"
        }
    }

    private var emptyState: some View {
        VStack(spacing: 0) {
            BrandMark(size: 52)
            Text("No projects yet")
                .font(.system(size: 15, weight: .semibold))
                .foregroundStyle(theme.text)
                .padding(.top, 16)
            Text("Use New Project above to create your first workspace.")
                .font(.system(size: 13))
                .foregroundStyle(theme.textMuted)
                .multilineTextAlignment(.center)
                .frame(maxWidth: 320)
                .padding(.top, 6)
        }
        .frame(maxWidth: .infinity)
    }
}

struct ProjectRowView: View {
    @EnvironmentObject var app: AppState
    @Environment(\.theme) private var theme
    // Plain reference — rows re-render off AppState's objectWillChange, so the
    // shared core model doesn't need Combine plumbing.
    let project: DeskifyProject
    @State private var hovering = false

    var body: some View {
        let p = project
        HStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 5) {
                HStack(spacing: 6) {
                    if p.pinned {
                        Image(systemName: "pin.fill")
                            .font(.system(size: 10))
                            .foregroundStyle(theme.accent)
                    }
                    Text(p.name)
                        .font(.system(size: 17, weight: .semibold))
                        .foregroundStyle(theme.text)
                        .lineLimit(1)
                }
                Text("\(p.summaryText)  ·  \(p.lastUsedText)")
                    .font(.system(size: 12))
                    .foregroundStyle(theme.textMuted)
                    .lineLimit(1)
            }
            Spacer(minLength: 16)

            IconCluster(paths: p.apps.map(\.path))
                .padding(.trailing, 14)

            Button {
                app.togglePin(p)
            } label: {
                Image(systemName: p.pinned ? "pin.fill" : "pin")
                    .foregroundStyle(p.pinned ? theme.accent : theme.textDim)
            }
            .buttonStyle(IconButtonStyle())
            .help(p.pinned ? "Unpin project" : "Pin project")
            .padding(.trailing, 14)

            Button {
                app.requestLaunch(p)
            } label: {
                Label("Launch", systemImage: "play.fill")
            }
            .buttonStyle(AccentButtonStyle())
            .disabled(app.launching)
        }
        .padding(EdgeInsets(top: 18, leading: 20, bottom: 18, trailing: 20))
        .background(hovering ? theme.cardAlt.opacity(theme.isDark ? 0.35 : 0.8) : theme.panel)
        .overlay(RoundedRectangle(cornerRadius: 10).stroke(theme.strokeSoft, lineWidth: 1))
        .clipShape(RoundedRectangle(cornerRadius: 10))
        .contentShape(RoundedRectangle(cornerRadius: 10))
        .rowHover($hovering)
        .onTapGesture { app.showDetails(p) }
    }
}

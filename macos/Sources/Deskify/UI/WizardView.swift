import SwiftUI
import AppKit
import DeskifyCore

/// The create/edit wizard: Name → Apps → Folders → Websites, with the same
/// progress segments and navigation as the Windows version.
struct WizardView: View {
    @EnvironmentObject var app: AppState
    @Environment(\.theme) private var theme
    @State private var saveFailed = false

    var body: some View {
        ScrollView {
            if let wizard = app.wizard {
                WizardContent(wizard: wizard, saveFailed: $saveFailed)
                    .frame(maxWidth: 640, alignment: .leading)
                    .frame(maxWidth: .infinity)
            }
        }
        .padding(EdgeInsets(top: 20, leading: 32, bottom: 28, trailing: 32))
        .alert("Save failed", isPresented: $saveFailed) {
            Button("OK") {}
        } message: {
            Text("Couldn't save the project — check that the projects folder isn't read-only, then try again.")
        }
    }
}

private struct WizardContent: View {
    @EnvironmentObject var app: AppState
    @Environment(\.theme) private var theme
    @ObservedObject var wizard: WizardModel
    @Binding var saveFailed: Bool
    @FocusState private var nameFocused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(wizard.isNew ? "New Project" : "Edit — \(wizard.project.name)")
                .font(.system(size: 22, weight: .semibold))
                .foregroundStyle(theme.text)
            Text("Step \(wizard.step + 1) of 4 · \(WizardModel.stepTitles[wizard.step])")
                .font(.system(size: 12))
                .foregroundStyle(theme.textMuted)
                .padding(.top, 4)
                .padding(.bottom, 14)

            // Progress segments
            HStack(spacing: 8) {
                ForEach(0..<4, id: \.self) { i in
                    RoundedRectangle(cornerRadius: 2)
                        .fill(i <= wizard.step ? theme.accent : theme.stroke)
                        .frame(height: 4)
                }
            }
            .padding(.bottom, 26)

            stepContent

            // Wizard nav
            HStack {
                if wizard.step > 0 {
                    Button {
                        wizard.step -= 1
                    } label: {
                        Label("Back", systemImage: "chevron.left")
                    }
                    .buttonStyle(GhostButtonStyle())
                }
                Spacer()
                Button("Cancel") { app.cancelWizard() }
                    .buttonStyle(GhostButtonStyle())
                Button {
                    if wizard.step == 3 {
                        if !app.saveWizard() { saveFailed = true }
                    } else {
                        wizard.step += 1
                    }
                } label: {
                    Text(wizard.step == 3 ? (wizard.isNew ? "Create" : "Save") : "Next")
                        .frame(minWidth: 80)
                }
                .buttonStyle(AccentButtonStyle())
            }
            .padding(.top, 30)
        }
    }

    @ViewBuilder
    private var stepContent: some View {
        switch wizard.step {
        case 0:
            stepHeader("Name your project", "Give this project a short, recognizable name.")
            DeskTextField(placeholder: "Project name", text: $wizard.name, fontSize: 15)
                .focused($nameFocused)
                .onAppear { nameFocused = true }

        case 1:
            stepHeader("Add applications", "Choose installed apps or pick from windows open right now.")
            ForEach(wizard.apps) { entry in
                editableAppRow(entry)
            }
            HStack(spacing: 8) {
                Button {
                    app.activeSheet = .appPicker
                } label: {
                    Label("Add app", systemImage: "plus")
                }
                .buttonStyle(DeskButtonStyle())
                Button {
                    app.activeSheet = .capture(intoWizard: true)
                } label: {
                    Label("Add running apps", systemImage: "macwindow")
                }
                .buttonStyle(DeskButtonStyle())
            }
            .padding(.top, 4)

        case 2:
            stepHeader("Add folders", "These open in Finder when the project launches.")
            ForEach(wizard.folders, id: \.self) { folder in
                editableStringRow(folder)
            }
            Button {
                pickFolder()
            } label: {
                Label("Add folder", systemImage: "folder")
            }
            .buttonStyle(DeskButtonStyle())
            .padding(.top, 4)

        default:
            stepHeader("Add websites", "Opened in your default browser on launch.")
            ForEach(wizard.urls, id: \.self) { url in
                editableStringRow(url)
            }
            HStack(spacing: 8) {
                DeskTextField(placeholder: "example.com", text: $wizard.urlInput) {
                    app.addUrlFromWizard()
                }
                Button("Add") { app.addUrlFromWizard() }
                    .buttonStyle(DeskButtonStyle())
            }
            .padding(.top, 4)
            if let hint = wizard.urlHint {
                Text(hint)
                    .font(.system(size: 12))
                    .foregroundStyle(theme.danger)
                    .padding(.top, 6)
                    .padding(.leading, 2)
            }
            LabeledCheckBox(isOn: $wizard.strictLayout,
                            label: "Lock window positions (fixes them again if an app moves itself after opening)")
                .padding(.top, 20)
        }
    }

    @ViewBuilder
    private func stepHeader(_ title: String, _ subtitle: String) -> some View {
        Text(title)
            .font(.system(size: 15, weight: .semibold))
            .foregroundStyle(theme.text)
        Text(subtitle)
            .font(.system(size: 12))
            .foregroundStyle(theme.textMuted)
            .padding(.top, 4)
            .padding(.bottom, 14)
    }

    private func editableAppRow(_ entry: AppEntry) -> some View {
        HStack(spacing: 10) {
            AppIconView(path: entry.path, size: 18)
            VStack(alignment: .leading, spacing: 1) {
                Text(entry.name)
                    .font(.system(size: 13, weight: .medium))
                    .foregroundStyle(theme.text)
                    .lineLimit(1)
                Text(entry.path)
                    .font(.system(size: 11))
                    .foregroundStyle(theme.textMuted)
                    .lineLimit(1)
                    .truncationMode(.middle)
            }
            Spacer(minLength: 4)
            if let layout = entry.window {
                Chip(text: layout.isMaximized
                     ? "Mon \(layout.monitor + 1) · max"
                     : "Mon \(layout.monitor + 1) · \(layout.width)×\(layout.height)")
            }
            Button {
                wizard.apps.removeAll { $0 === entry }
            } label: {
                Image(systemName: "xmark")
            }
            .buttonStyle(IconButtonStyle())
            .help("Remove")
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .background(theme.card)
        .overlay(RoundedRectangle(cornerRadius: 8).stroke(theme.strokeSoft, lineWidth: 1))
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .padding(.bottom, 6)
    }

    private func editableStringRow(_ value: String) -> some View {
        HStack {
            Text(value)
                .font(.system(size: 13))
                .foregroundStyle(theme.text)
                .lineLimit(1)
                .truncationMode(.middle)
            Spacer(minLength: 4)
            Button {
                app.removeWizardString(value)
            } label: {
                Image(systemName: "xmark")
            }
            .buttonStyle(IconButtonStyle())
            .help("Remove")
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .background(theme.card)
        .overlay(RoundedRectangle(cornerRadius: 8).stroke(theme.strokeSoft, lineWidth: 1))
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .padding(.bottom, 6)
    }

    private func pickFolder() {
        let panel = NSOpenPanel()
        panel.title = "Add folder"
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        if panel.runModal() == .OK, let url = panel.url {
            app.addFolderFromWizard(url.path)
        }
    }
}

import SwiftUI
import DeskifyCore

/// Settings screen — same sections as the Windows version, plus the
/// macOS-required Permissions card for Accessibility.
struct SettingsView: View {
    @EnvironmentObject var app: AppState
    @Environment(\.theme) private var theme

    @State private var timeoutText = ""
    @State private var retryText = ""
    @State private var snapText = ""
    @State private var strictDefault = false
    @State private var confirmCloseOthers = true
    @State private var status = ""

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 0) {
                Text("Settings")
                    .font(.system(size: 22, weight: .semibold))
                    .foregroundStyle(theme.text)

                section("Appearance") {
                    CardView {
                        VStack(alignment: .leading, spacing: 0) {
                            Text("Theme").font(.system(size: 13)).foregroundStyle(theme.text)
                            Text("Dark or light — applies instantly.")
                                .font(.system(size: 11))
                                .foregroundStyle(theme.textMuted)
                                .padding(.top, 2)
                                .padding(.bottom, 16)
                            HStack(spacing: 22) {
                                ForEach(Theme.available, id: \.self) { name in
                                    themeSwatch(name)
                                }
                            }
                        }
                    }
                }

                section("Launching") {
                    CardView {
                        VStack(spacing: 14) {
                            numberRow(title: "How long to wait for apps (seconds)",
                                      subtitle: "If an app takes longer than this to open, Deskify stops waiting for it.",
                                      text: $timeoutText)
                            numberRow(title: "Check-in speed (ms)",
                                      subtitle: "How often Deskify checks if apps have opened yet. Lower is faster to react, higher is easier on your Mac.",
                                      text: $retryText)
                        }
                    }
                }

                section("Window snapping") {
                    CardView {
                        numberRow(title: "Snap size (pt)",
                                  subtitle: "While arranging a layout with Snap turned on, windows line up to this spacing.",
                                  text: $snapText)
                    }
                }

                section("Defaults") {
                    CardView {
                        VStack(alignment: .leading, spacing: 12) {
                            LabeledCheckBox(isOn: $strictDefault,
                                            label: "New projects lock window positions by default")
                            LabeledCheckBox(isOn: $confirmCloseOthers,
                                            label: "Ask before closing other apps")
                        }
                    }
                }

                section("Permissions") {
                    CardView {
                        HStack(alignment: .top) {
                            VStack(alignment: .leading, spacing: 4) {
                                Text(app.accessibilityGranted
                                     ? "Accessibility access granted"
                                     : "Accessibility access needed")
                                    .font(.system(size: 13, weight: .semibold))
                                    .foregroundStyle(app.accessibilityGranted ? theme.success : theme.danger)
                                Text("Deskify uses macOS Accessibility to see, arrange, and close other apps' windows — the heart of saving and restoring layouts. Grant it once in System Settings → Privacy & Security → Accessibility.")
                                    .font(.system(size: 11))
                                    .foregroundStyle(theme.textMuted)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                            Spacer()
                            if !app.accessibilityGranted {
                                Button("Grant Access") { app.requestAccessibility() }
                                    .buttonStyle(AccentButtonStyle())
                            }
                        }
                    }
                }

                section("Quick switch") {
                    CardView {
                        VStack(alignment: .leading, spacing: 4) {
                            Text("⌃ Space").font(.system(size: 13, weight: .semibold)).foregroundStyle(theme.text)
                            Text("Opens the project switcher from anywhere, even while Deskify is in the background. Type to search, press Return or click to launch, Esc to close.")
                                .font(.system(size: 11))
                                .foregroundStyle(theme.textMuted)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                    }
                }

                section("Anti-cheat safety") {
                    CardView {
                        VStack(alignment: .leading, spacing: 4) {
                            Text("Riot Games apps are never touched")
                                .font(.system(size: 13, weight: .semibold))
                                .foregroundStyle(theme.text)
                            Text("Riot Client, VALORANT, League of Legends, and Vanguard are permanently excluded from capture, layout matching, positioning, and Close Other Apps. Vanguard's anti-cheat can flag external window control as tampering, which risks your account — Deskify won't go near it, by design, not by accident.")
                                .font(.system(size: 11))
                                .foregroundStyle(theme.textMuted)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                    }
                }

                HStack(spacing: 8) {
                    Button("Save Settings") { saveSettings() }
                        .buttonStyle(AccentButtonStyle())
                    Button("Open Data Folder") { app.openDataFolder() }
                        .buttonStyle(DeskButtonStyle())
                }
                .padding(.top, 20)

                if !status.isEmpty {
                    Text(status)
                        .font(.system(size: 12))
                        .foregroundStyle(theme.textDim)
                        .padding(.top, 12)
                        .padding(.leading, 2)
                }
                Text("Deskify 0.1.0 for macOS")
                    .font(.system(size: 11))
                    .foregroundStyle(theme.textMuted)
                    .padding(.top, 28)
                    .padding(.leading, 2)
            }
            .frame(maxWidth: 580, alignment: .leading)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(EdgeInsets(top: 20, leading: 32, bottom: 28, trailing: 32))
        .onAppear {
            timeoutText = String(app.settings.detectTimeoutSeconds)
            retryText = String(app.settings.retryIntervalMs)
            snapText = String(app.settings.snapGridSize)
            strictDefault = app.settings.strictLayoutDefault
            confirmCloseOthers = app.settings.confirmCloseOthers
            status = ""
            app.refreshAccessibility()
        }
        // Leaving Settings by any route counts as "done editing" — valid
        // changes are applied automatically instead of being silently dropped.
        .onDisappear { autoSave() }
    }

    @ViewBuilder
    private func section(_ title: String, @ViewBuilder content: () -> some View) -> some View {
        Eyebrow(text: title)
            .padding(.top, 20)
            .padding(.bottom, 8)
        content()
    }

    private func themeSwatch(_ name: String) -> some View {
        let swatch = Theme.named(name)
        let active = app.settings.themeName == name
        return VStack(spacing: 7) {
            Button {
                app.selectTheme(name)
            } label: {
                ZStack {
                    Circle().fill(swatch.bg)
                    Circle().stroke(active ? theme.accent : theme.stroke, lineWidth: active ? 2 : 1)
                    if active {
                        Image(systemName: "checkmark")
                            .font(.system(size: 12, weight: .bold))
                            .foregroundStyle(swatch.text)
                    }
                }
                .frame(width: 40, height: 40)
            }
            .buttonStyle(.plain)
            .help(name)
            Text(name)
                .font(.system(size: 11))
                .foregroundStyle(theme.textDim)
        }
    }

    private func numberRow(title: String, subtitle: String, text: Binding<String>) -> some View {
        HStack(alignment: .center, spacing: 14) {
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.system(size: 13)).foregroundStyle(theme.text)
                Text(subtitle)
                    .font(.system(size: 11))
                    .foregroundStyle(theme.textMuted)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer()
            DeskTextField(placeholder: "", text: text)
                .frame(width: 92)
                .multilineTextAlignment(.center)
        }
    }

    private func saveSettings() {
        guard let timeout = Int(timeoutText), (1...120).contains(timeout) else {
            status = "Timeout must be 1–120 seconds."
            return
        }
        guard let retry = Int(retryText), (100...5000).contains(retry) else {
            status = "Retry interval must be 100–5000 ms."
            return
        }
        guard let snap = Int(snapText), (2...200).contains(snap) else {
            status = "Snap grid size must be 2–200 pt."
            return
        }
        app.settings.detectTimeoutSeconds = timeout
        app.settings.retryIntervalMs = retry
        app.settings.snapGridSize = snap
        app.settings.strictLayoutDefault = strictDefault
        app.settings.confirmCloseOthers = confirmCloseOthers
        app.settings.save()
        status = "Saved."
    }

    /// Silent counterpart of Save for when the user leaves the page without
    /// pressing Save: applies whatever is valid and changed, leaves invalid
    /// text alone rather than guessing.
    private func autoSave() {
        var changed = false
        if let timeout = Int(timeoutText), (1...120).contains(timeout), timeout != app.settings.detectTimeoutSeconds {
            app.settings.detectTimeoutSeconds = timeout
            changed = true
        }
        if let retry = Int(retryText), (100...5000).contains(retry), retry != app.settings.retryIntervalMs {
            app.settings.retryIntervalMs = retry
            changed = true
        }
        if let snap = Int(snapText), (2...200).contains(snap), snap != app.settings.snapGridSize {
            app.settings.snapGridSize = snap
            changed = true
        }
        if strictDefault != app.settings.strictLayoutDefault {
            app.settings.strictLayoutDefault = strictDefault
            changed = true
        }
        if confirmCloseOthers != app.settings.confirmCloseOthers {
            app.settings.confirmCloseOthers = confirmCloseOthers
            changed = true
        }
        if changed { app.settings.save() }
    }
}

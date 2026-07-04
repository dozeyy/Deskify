import SwiftUI
import AppKit

@main
struct DeskifyApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @StateObject private var state = AppState()

    var body: some Scene {
        WindowGroup {
            MainWindowView()
                .environmentObject(state)
                .frame(minWidth: 900, minHeight: 580)
                .preferredColorScheme(state.theme.isDark ? .dark : .light)
                .onAppear {
                    delegate.appState = state
                    state.applyAppearance()
                }
        }
        .defaultSize(width: 1160, height: 760)
        .commands {
            CommandGroup(replacing: .newItem) {
                Button("New Project") { state.newProject() }
                    .keyboardShortcut("n")
                Button("Capture Session") { state.activeSheet = .capture(intoWizard: false) }
                    .keyboardShortcut("n", modifiers: [.command, .shift])
            }
        }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    var appState: AppState?

    /// The Windows app exits when its window closes; match that instead of
    /// lingering as a windowless process.
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    func applicationWillTerminate(_ notification: Notification) {
        // Nothing typed should be lost just because the app was quit — a
        // wizard mid-edit becomes a draft, unsaved notes are written out.
        appState?.autosaveOnExit()
    }
}

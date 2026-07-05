import Foundation

/// Riot Games' Vanguard anti-cheat treats external processes that enumerate,
/// reposition, or otherwise touch its protected games' windows as potential
/// tampering — this can risk a player's account. Deskify never captures,
/// matches, positions, launches, or closes anything Riot-related, and this
/// exclusion cannot be bypassed. Same policy as the Windows version; League of
/// Legends (and Vanguard with it) ships on macOS too, so the gate carries over.
public enum ProtectedApps {
    /// App/bundle names that are always excluded, even if the keyword check
    /// were ever to miss them.
    private static let blockedAppNames: Set<String> = [
        "riot client", "league of legends", "valorant", "riot vanguard",
    ]

    /// True if a path or display name refers to Riot Games/Valorant software.
    /// This is the hard gate checked wherever Deskify could touch an app at all —
    /// browsing to an app, capturing a running window, launching a saved project,
    /// or closing other apps. Keyword-based (not just the fixed name list) so it
    /// also catches installer paths, aliases, and future Riot titles.
    public static func isProtected(path: String?, name: String? = nil) -> Bool {
        if let path, !path.isEmpty {
            let appName = (path as NSString).lastPathComponent
                .replacingOccurrences(of: ".app", with: "").lowercased()
            if blockedAppNames.contains(appName) { return true }
        }
        return containsBlockedWord(path) || containsBlockedWord(name)
    }

    private static func containsBlockedWord(_ s: String?) -> Bool {
        guard let s, !s.isEmpty else { return false }
        let lower = s.lowercased()
        return lower.contains("riot") || lower.contains("valorant")
    }
}

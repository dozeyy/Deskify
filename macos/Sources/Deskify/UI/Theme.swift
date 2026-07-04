import SwiftUI

/// Deskify's monochrome palette — the same Dark/Light values as the Windows
/// themes (UI/Themes/*.xaml), so the product reads identically on both
/// platforms: depth via lightness only, one "accent" tone allowed to pop.
struct Theme: Equatable {
    let name: String
    let isDark: Bool

    // Surfaces
    let bg: Color
    let panel: Color
    let card: Color
    let cardAlt: Color
    let stroke: Color
    let strokeSoft: Color

    // Text
    let text: Color
    let textDim: Color
    let textMuted: Color

    // Accent (a tone, not a hue)
    let accent: Color
    let accentHover: Color
    let accentPress: Color
    let accentSoft: Color
    let accentText: Color

    let danger: Color
    let success: Color

    static let dark = Theme(
        name: "Dark", isDark: true,
        bg: Color(hex: 0x121212), panel: Color(hex: 0x1A1A1A),
        card: Color(hex: 0x2A2A2A), cardAlt: Color(hex: 0x333333),
        stroke: Color(hex: 0x3A3A3A), strokeSoft: Color(hex: 0x282828),
        text: Color(hex: 0xE0E0E0), textDim: Color(hex: 0xB0B0B0), textMuted: Color(hex: 0x888888),
        accent: Color(hex: 0xE0E0E0), accentHover: Color(hex: 0xEDEDED),
        accentPress: Color(hex: 0xC9C9C9), accentSoft: Color(hex: 0x2A2A2A),
        accentText: Color(hex: 0x121212),
        danger: Color(hex: 0xC77A73), success: Color(hex: 0x7FA98F))

    static let light = Theme(
        name: "Light", isDark: false,
        bg: Color(hex: 0xF4F4F4), panel: Color(hex: 0xEAEAEA),
        card: Color(hex: 0xFFFFFF), cardAlt: Color(hex: 0xEFEFEF),
        stroke: Color(hex: 0xD9D9D9), strokeSoft: Color(hex: 0xE4E4E4),
        text: Color(hex: 0x1A1A1A), textDim: Color(hex: 0x4F4F4F), textMuted: Color(hex: 0x7A7A7A),
        accent: Color(hex: 0x1A1A1A), accentHover: Color(hex: 0x333333),
        accentPress: Color(hex: 0x000000), accentSoft: Color(hex: 0xEAEAEA),
        accentText: Color(hex: 0xFFFFFF),
        danger: Color(hex: 0xB23A33), success: Color(hex: 0x2F7D53))

    static let available = ["Dark", "Light"]

    static func named(_ name: String) -> Theme { name == "Light" ? .light : .dark }
}

extension Color {
    init(hex: UInt32) {
        self.init(.sRGB,
                  red: Double((hex >> 16) & 0xFF) / 255,
                  green: Double((hex >> 8) & 0xFF) / 255,
                  blue: Double(hex & 0xFF) / 255,
                  opacity: 1)
    }
}

private struct ThemeKey: EnvironmentKey {
    static let defaultValue = Theme.dark
}

extension EnvironmentValues {
    var theme: Theme {
        get { self[ThemeKey.self] }
        set { self[ThemeKey.self] = newValue }
    }
}

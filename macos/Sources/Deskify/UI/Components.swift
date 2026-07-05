import SwiftUI
import AppKit

// Shared building blocks mirroring the Windows Styles.xaml design tokens:
// layered surfaces, 8px spacing grid, quiet borders, a single accent tone.

// MARK: - Buttons

/// Standard button — card surface, subtle stroke (Windows' default Button style).
struct DeskButtonStyle: ButtonStyle {
    @Environment(\.theme) private var theme
    var danger = false

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 13))
            .foregroundStyle(danger ? theme.danger : theme.text)
            .padding(.horizontal, 14)
            .padding(.vertical, 7)
            .background(configuration.isPressed ? theme.cardAlt : theme.card)
            .overlay(RoundedRectangle(cornerRadius: 8).stroke(danger ? theme.danger.opacity(0.5) : theme.stroke, lineWidth: 1))
            .clipShape(RoundedRectangle(cornerRadius: 8))
            .contentShape(RoundedRectangle(cornerRadius: 8))
    }
}

/// Primary action — filled with the accent tone (Windows' AccentBtn).
struct AccentButtonStyle: ButtonStyle {
    @Environment(\.theme) private var theme

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 13, weight: .semibold))
            .foregroundStyle(theme.accentText)
            .padding(.horizontal, 16)
            .padding(.vertical, 7)
            .background(configuration.isPressed ? theme.accentPress : theme.accent)
            .clipShape(RoundedRectangle(cornerRadius: 8))
            .contentShape(RoundedRectangle(cornerRadius: 8))
    }
}

/// Borderless, low-emphasis action (Windows' GhostBtn).
struct GhostButtonStyle: ButtonStyle {
    @Environment(\.theme) private var theme

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 13))
            .foregroundStyle(theme.textDim)
            .padding(.horizontal, 10)
            .padding(.vertical, 6)
            .background(configuration.isPressed ? theme.accentSoft : .clear)
            .clipShape(RoundedRectangle(cornerRadius: 8))
            .contentShape(RoundedRectangle(cornerRadius: 8))
    }
}

/// Small square icon-only button (Windows' IconBtn).
struct IconButtonStyle: ButtonStyle {
    @Environment(\.theme) private var theme

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 12, weight: .medium))
            .foregroundStyle(theme.textDim)
            .frame(width: 26, height: 26)
            .background(configuration.isPressed ? theme.cardAlt : .clear)
            .clipShape(RoundedRectangle(cornerRadius: 6))
            .contentShape(RoundedRectangle(cornerRadius: 6))
    }
}

// MARK: - Surfaces

/// Panel card (Windows' Card style).
struct CardView<Content: View>: View {
    @Environment(\.theme) private var theme
    private let content: Content

    init(@ViewBuilder content: () -> Content) {
        self.content = content()
    }

    var body: some View {
        content
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(theme.panel)
            .overlay(RoundedRectangle(cornerRadius: 10).stroke(theme.strokeSoft, lineWidth: 1))
            .clipShape(RoundedRectangle(cornerRadius: 10))
    }
}

/// Small rounded chip (layout badges).
struct Chip: View {
    @Environment(\.theme) private var theme
    let text: String

    var body: some View {
        Text(text)
            .font(.system(size: 11))
            .foregroundStyle(theme.textDim)
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .background(theme.accentSoft)
            .overlay(Capsule().stroke(theme.stroke, lineWidth: 1))
            .clipShape(Capsule())
    }
}

/// Section label (Windows' Eyebrow style).
struct Eyebrow: View {
    @Environment(\.theme) private var theme
    let text: String

    var body: some View {
        Text(text.uppercased())
            .font(.system(size: 10, weight: .medium))
            .kerning(0.8)
            .foregroundStyle(theme.textMuted)
    }
}

// MARK: - Brand mark

/// The Deskify mark — a tiled window layout (tall pane + two stacked panes),
/// the same geometry as the Windows icon, drawn natively so it's crisp at any
/// Retina scale.
struct BrandMark: View {
    @Environment(\.theme) private var theme
    var size: CGFloat = 30

    var body: some View {
        let pane = size * 0.06
        ZStack {
            RoundedRectangle(cornerRadius: size * 0.27)
                .fill(theme.accent)
            HStack(spacing: pane) {
                RoundedRectangle(cornerRadius: pane).fill(theme.accentText)
                VStack(spacing: pane) {
                    RoundedRectangle(cornerRadius: pane).fill(theme.accentText)
                    RoundedRectangle(cornerRadius: pane).fill(theme.accentText)
                }
            }
            .padding(size * 0.24)
        }
        .frame(width: size, height: size)
    }
}

// MARK: - App icons

/// App icon lookup with a small cache — NSWorkspace already returns the right
/// Retina representations.
enum IconLoader {
    private static var cache: [String: NSImage] = [:]

    static func icon(forPath path: String) -> NSImage {
        if let cached = cache[path] { return cached }
        let icon = NSWorkspace.shared.icon(forFile: path)
        cache[path] = icon
        return icon
    }
}

struct AppIconView: View {
    let path: String
    var size: CGFloat = 18

    var body: some View {
        Image(nsImage: IconLoader.icon(forPath: path))
            .resizable()
            .interpolation(.high)
            .frame(width: size, height: size)
    }
}

/// Up to five app icons in small framed tiles (the icon cluster on project rows).
struct IconCluster: View {
    @Environment(\.theme) private var theme
    let paths: [String]

    var body: some View {
        HStack(spacing: 6) {
            ForEach(Array(paths.prefix(5).enumerated()), id: \.offset) { _, path in
                AppIconView(path: path, size: 15)
                    .frame(width: 26, height: 26)
                    .background(theme.card)
                    .overlay(RoundedRectangle(cornerRadius: 7).stroke(theme.stroke, lineWidth: 1))
                    .clipShape(RoundedRectangle(cornerRadius: 7))
            }
        }
    }
}

// MARK: - Checkbox row

/// Deskify-styled checkbox (native Toggle checkboxes don't take the theme's
/// monochrome palette well in a custom-chrome window).
struct CheckBox: View {
    @Environment(\.theme) private var theme
    @Binding var isOn: Bool

    var body: some View {
        Button {
            isOn.toggle()
        } label: {
            ZStack {
                RoundedRectangle(cornerRadius: 4)
                    .fill(isOn ? theme.accent : theme.card)
                RoundedRectangle(cornerRadius: 4)
                    .stroke(theme.stroke, lineWidth: 1)
                if isOn {
                    Image(systemName: "checkmark")
                        .font(.system(size: 9, weight: .bold))
                        .foregroundStyle(theme.accentText)
                }
            }
            .frame(width: 16, height: 16)
        }
        .buttonStyle(.plain)
    }
}

struct LabeledCheckBox: View {
    @Environment(\.theme) private var theme
    @Binding var isOn: Bool
    let label: String

    var body: some View {
        HStack(alignment: .top, spacing: 8) {
            CheckBox(isOn: $isOn)
            Text(label)
                .font(.system(size: 13))
                .foregroundStyle(theme.text)
                .fixedSize(horizontal: false, vertical: true)
                .onTapGesture { isOn.toggle() }
        }
    }
}

// MARK: - Text fields

/// Themed single-line text field matching the Windows TextBox style.
struct DeskTextField: View {
    @Environment(\.theme) private var theme
    let placeholder: String
    @Binding var text: String
    var fontSize: CGFloat = 13
    var onSubmit: (() -> Void)?

    var body: some View {
        TextField(placeholder, text: $text)
            .textFieldStyle(.plain)
            .font(.system(size: fontSize))
            .foregroundStyle(theme.text)
            .padding(.horizontal, 11)
            .padding(.vertical, 8)
            .background(theme.card)
            .overlay(RoundedRectangle(cornerRadius: 8).stroke(theme.stroke, lineWidth: 1))
            .clipShape(RoundedRectangle(cornerRadius: 8))
            .onSubmit { onSubmit?() }
    }
}

// MARK: - Hover helper

extension View {
    /// Row hover highlight used across all list rows.
    func rowHover(_ hovering: Binding<Bool>) -> some View {
        onHover { hovering.wrappedValue = $0 }
    }

    /// Deployment-target-safe `onChange`. The two/zero-parameter
    /// `onChange(of:initial:_:)` is macOS 14+ only, and the one-parameter
    /// `onChange(of:perform:)` is deprecated there — this picks the right one
    /// at compile time so the code is warning-free on new SDKs yet still runs
    /// on macOS 13 (Ventura).
    @ViewBuilder
    func onValueChange<V: Equatable>(of value: V, perform action: @escaping (V) -> Void) -> some View {
        if #available(macOS 14.0, *) {
            self.onChange(of: value) { _, newValue in action(newValue) }
        } else {
            self.onChange(of: value, perform: action)
        }
    }
}

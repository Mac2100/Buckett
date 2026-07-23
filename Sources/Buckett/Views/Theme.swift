import SwiftUI

/// Shared visual language: brand gradient, glass cards, and small reusable pieces.
enum Brand {
    static let indigo = Color(red: 0.33, green: 0.29, blue: 0.90)
    static let teal = Color(red: 0.05, green: 0.60, blue: 0.55)

    static let gradient = LinearGradient(
        colors: [indigo, teal],
        startPoint: .topLeading,
        endPoint: .bottomTrailing
    )

    /// Small app glyph used in the sidebar header and welcome screen.
    static func glyph(size: CGFloat) -> some View {
        RoundedRectangle(cornerRadius: size * 0.24, style: .continuous)
            .fill(gradient)
            .frame(width: size, height: size)
            .overlay {
                Image(systemName: "archivebox.fill")
                    .font(.system(size: size * 0.5, weight: .semibold))
                    .foregroundStyle(.white)
            }
            .shadow(color: indigo.opacity(0.35), radius: size * 0.12, y: size * 0.05)
    }
}

struct GlassCardModifier: ViewModifier {
    var cornerRadius: CGFloat = 14
    var padding: CGFloat = 16

    func body(content: Content) -> some View {
        content
            .padding(padding)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: cornerRadius, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                    .strokeBorder(Color.primary.opacity(0.08), lineWidth: 1)
            )
            .shadow(color: .black.opacity(0.07), radius: 8, y: 3)
    }
}

extension View {
    func glassCard(cornerRadius: CGFloat = 14, padding: CGFloat = 16) -> some View {
        modifier(GlassCardModifier(cornerRadius: cornerRadius, padding: padding))
    }
}

/// Rounded search field matching the in-content control row.
struct SearchField: View {
    @Binding var text: String
    var prompt: String = "Search"

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: "magnifyingglass")
                .foregroundStyle(.secondary)
                .font(.system(size: 12, weight: .medium))
            TextField(prompt, text: $text)
                .textFieldStyle(.plain)
            if !text.isEmpty {
                Button {
                    text = ""
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .foregroundStyle(.tertiary)
                }
                .buttonStyle(.plain)
            }
        }
        .padding(.horizontal, 9)
        .padding(.vertical, 5)
        .background(.quaternary.opacity(0.6), in: Capsule())
        .frame(width: 200)
    }
}

/// Capsule segmented control used for the detail tabs and grid/list toggle.
struct CapsuleSegments<T: Hashable>: View {
    let options: [(value: T, label: String, symbol: String?)]
    @Binding var selection: T
    var showLabels = true

    var body: some View {
        HStack(spacing: 2) {
            ForEach(options, id: \.value) { option in
                Button {
                    withAnimation(.snappy(duration: 0.18)) {
                        selection = option.value
                    }
                } label: {
                    HStack(spacing: 5) {
                        if let symbol = option.symbol {
                            Image(systemName: symbol)
                                .font(.system(size: 12, weight: .medium))
                        }
                        if showLabels {
                            Text(option.label)
                                .font(.system(size: 12, weight: .medium))
                        }
                    }
                    .padding(.horizontal, 11)
                    .padding(.vertical, 5)
                    .background(
                        Capsule().fill(
                            selection == option.value
                                ? AnyShapeStyle(.background)
                                : AnyShapeStyle(Color.clear)
                        )
                    )
                    .overlay(
                        Capsule().strokeBorder(
                            selection == option.value
                                ? Color.primary.opacity(0.12)
                                : Color.clear,
                            lineWidth: 1
                        )
                    )
                    .contentShape(Capsule())
                }
                .buttonStyle(.plain)
                .foregroundStyle(selection == option.value ? .primary : .secondary)
                .help(option.label)
            }
        }
        .padding(3)
        .background(.quaternary.opacity(0.55), in: Capsule())
    }
}

extension Date {
    var briefFormatted: String {
        formatted(date: .abbreviated, time: .shortened)
    }
}

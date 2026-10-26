import SwiftUI

/// One colour as a plain hex value, so the app can calculate with it (contrast) and not only paint with it.
nonisolated struct Swatch: Equatable, Sendable {
    let hex: UInt32

    init(_ hex: UInt32) {
        self.hex = hex
    }

    var color: Color {
        Color(red: Double((hex >> 16) & 0xFF) / 255, green: Double((hex >> 8) & 0xFF) / 255, blue: Double(hex & 0xFF) / 255)
    }

    /// WCAG 2.x relative luminance of the sRGB colour.
    private var luminance: Double {
        func linear(_ channel: UInt32) -> Double {
            let value = Double(channel) / 255
            return value <= 0.03928 ? value / 12.92 : pow((value + 0.055) / 1.055, 2.4)
        }
        return 0.2126 * linear((hex >> 16) & 0xFF) + 0.7152 * linear((hex >> 8) & 0xFF) + 0.0722 * linear(hex & 0xFF)
    }

    /// The WCAG contrast ratio, 1 to 21; the same in both directions.
    func contrast(with other: Swatch) -> Double {
        let lighter = max(luminance, other.luminance)
        let darker = min(luminance, other.luminance)
        return (lighter + 0.05) / (darker + 0.05)
    }
}

/// Every colour of the app, in one place (UI-01). Screens take colours from here and never write their own; the app is
/// always black (decisions K1-A, K7-B) and has one accent, used sparingly (K2-A).
nonisolated enum Palette {
    /// The screen: near black, so cards still step out of it (pure black would lose that step).
    static let background = Swatch(0x0A0A0B)
    /// Cards and rows.
    static let surface = Swatch(0x141416)
    /// A card on a card: user messages, chips, the input capsule.
    static let raised = Swatch(0x1C1C1F)
    /// The thin line between rows and around cards.
    static let hairline = Swatch(0x26262A)

    static let primaryText = Swatch(0xEDEDEF)
    static let secondaryText = Swatch(0x8E8E93)

    /// Champagne gold: send, links, the selected state. Never a fill for large areas.
    static let accent = Swatch(0xC9A96E)
    /// The label on an accent-filled button.
    static let onAccent = Swatch(0x0A0A0B)
    /// Money coming in; spending stays in the normal text colour so a normal purchase does not look like a warning.
    static let incoming = Swatch(0x5FCB8A)
    static let failure = Swatch(0xFF6B6B)
}

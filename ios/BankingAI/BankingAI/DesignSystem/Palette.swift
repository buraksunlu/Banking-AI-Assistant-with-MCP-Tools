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

extension Swatch {
    /// How different two colours look, as the CIE76 ΔE in the Lab colour space: 0 is the same colour, 100 is white against black,
    /// and above about 25 two colours are plainly different. Contrast says whether a colour can be read; this says whether two
    /// colours can be told apart (UI-05 K13-A).
    func distance(to other: Swatch) -> Double {
        let (l1, a1, b1) = lab
        let (l2, a2, b2) = other.lab
        return ((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2)).squareRoot()
    }

    /// This colour painted at `opacity` over `background`: what the eye gets from a translucent fill, so a test can measure it.
    func over(_ background: Swatch, opacity: Double) -> Swatch {
        let opacity = min(max(opacity, 0), 1)
        func mix(_ shift: UInt32) -> UInt32 {
            let front = Double((hex >> shift) & 0xFF)
            let back = Double((background.hex >> shift) & 0xFF)
            return UInt32((opacity * front + (1 - opacity) * back).rounded())
        }
        return Swatch(mix(16) << 16 | mix(8) << 8 | mix(0))
    }

    /// CIE Lab of the sRGB colour, D65 white.
    private var lab: (Double, Double, Double) {
        func linear(_ channel: UInt32) -> Double {
            let value = Double(channel) / 255
            return value <= 0.04045 ? value / 12.92 : pow((value + 0.055) / 1.055, 2.4)
        }
        let r = linear((hex >> 16) & 0xFF), g = linear((hex >> 8) & 0xFF), b = linear(hex & 0xFF)
        let x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047
        let y = 0.2126 * r + 0.7152 * g + 0.0722 * b
        let z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883
        func f(_ t: Double) -> Double { t > 0.008856 ? cbrt(t) : 7.787 * t + 16.0 / 116 }
        return (116 * f(y) - 16, 500 * (f(x) - f(y)), 200 * (f(y) - f(z)))
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

    /// The icon's blue (UI-04 K11-B, replacing UI-01's gold): send, links, the selected state. Never a fill for large
    /// areas, and never green, which is the colour of money coming in.
    static let accent = Swatch(0x3D8BFF)
    /// The label on an accent-filled button.
    static let onAccent = Swatch(0x0A0A0B)
    /// Money coming in; spending stays in the normal text colour so a normal purchase does not look like a warning.
    static let incoming = Swatch(0x5FCB8A)
    static let failure = Swatch(0xFF6B6B)
}

extension CategoryTone {
    /// The colour of each category tone (UI-05 K6-A, K11-A), chosen so that no two look alike and none looks like the accent,
    /// money coming in or an error (`CategoryPaletteTests`). The names are the hue each one is nearest to (K12-B): lime is
    /// the old green, which is only for money coming in; magenta is the old red, slate the old blue, which was the accent's twin.
    var swatch: Swatch {
        switch self {
        case .orange: Swatch(0xB47226)
        case .lime: Swatch(0xCAD37E)
        case .brown: Swatch(0xB79174)
        case .pink: Swatch(0xD6568C)
        case .purple: Swatch(0xA56DDA)
        case .indigo: Swatch(0x9C96D9)
        case .teal: Swatch(0x2FA3A7)
        case .cyan: Swatch(0x248ABD)
        case .magenta: Swatch(0xE734D4)
        case .mint: Swatch(0xA1DCC3)
        case .slate: Swatch(0xB0BBCB)
        case .yellow: Swatch(0xDDBC27)
        case .gray: Swatch(0x6E6E73)
        }
    }
}

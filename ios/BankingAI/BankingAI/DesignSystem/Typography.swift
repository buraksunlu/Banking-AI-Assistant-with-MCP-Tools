import SwiftUI

/// The type scale (UI-01, K6-A): the system font, sized with Dynamic Type, and equal-width digits wherever a figure is shown.
nonisolated enum Typography {
    /// A balance on a card: large and light.
    static let balance = Font.largeTitle.weight(.light).monospacedDigit()
    static let title = Font.title3.weight(.semibold)
    static let body = Font.body
    static let caption = Font.footnote
    /// The small label above a section ("SON İŞLEMLER"); see ``label(_:)``.
    static let eyebrow = Font.caption.weight(.medium)

    /// Capital letters the Turkish way: plain `uppercased()` would write "SON IŞLEMLER" without the dot.
    static func label(_ text: String) -> String {
        text.uppercased(with: Locale(identifier: "tr_TR"))
    }
}

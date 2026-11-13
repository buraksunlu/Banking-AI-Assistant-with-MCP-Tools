import Foundation
import Testing
import UIKit
@testable import BankingAI

// UI-01: the two rules in the design system that are logic, not looks: the contrast formula (so a palette change cannot
// quietly make text unreadable) and the Turkish capital letters of the small section labels.
struct DesignSystemTests {
    // WCAG 2.x reference values: white on black is 21:1 and #767676 on white is the known 4.54 edge case.
    @Test func contrastFollowsTheWCAGFormula() {
        #expect(abs(Swatch(0xFFFFFF).contrast(with: Swatch(0x000000)) - 21.0) < 0.01)
        #expect(abs(Swatch(0x767676).contrast(with: Swatch(0xFFFFFF)) - 4.54) < 0.01)
        #expect(abs(Swatch(0x000000).contrast(with: Swatch(0x000000)) - 1.0) < 0.001)
    }

    @Test func contrastDoesNotDependOnTheOrder() {
        let a = Swatch(0xC9A96E), b = Swatch(0x0A0A0B)
        #expect(a.contrast(with: b) == b.contrast(with: a))
    }

    // Every text colour on every surface it is drawn on must reach 4.5:1 (WCAG AA for normal text).
    @Test(arguments: [
        ("primary on background", Palette.primaryText, Palette.background),
        ("primary on surface", Palette.primaryText, Palette.surface),
        ("primary on raised", Palette.primaryText, Palette.raised),
        ("secondary on background", Palette.secondaryText, Palette.background),
        ("secondary on surface", Palette.secondaryText, Palette.surface),
        ("secondary on raised", Palette.secondaryText, Palette.raised),
        ("accent on background", Palette.accent, Palette.background),
        ("accent on surface", Palette.accent, Palette.surface),
        ("accent on raised", Palette.accent, Palette.raised),
        ("incoming on surface", Palette.incoming, Palette.surface),
        ("failure on surface", Palette.failure, Palette.surface),
        ("button label on the button fill", Palette.onAccent, Palette.accentFill),
    ])
    func everyTextColourIsReadableOnItsSurface(pair: (String, Swatch, Swatch)) {
        #expect(pair.1.contrast(with: pair.2) >= 4.5, "\(pair.0)")
    }

    @Test func theBackgroundIsTheWarmCreamNotPureWhite() {
        // UX-01 Z3: a warm step between the screen and the white cards, which a white screen would lose.
        #expect(Palette.background == Swatch(0xF6F1E7))
        #expect(Palette.surface == Swatch(0xFFFFFF))
        #expect(Palette.surface != Palette.background)
        #expect(Palette.raised != Palette.surface)
    }

    // The trap: plain uppercased() writes "SON IŞLEMLER" (no dot); Turkish keeps the dotted İ.
    // The yellow is a button fill only: as text on a card it would not be read, so its low contrast is pinned on purpose.
    @Test func theButtonFillIsNeverReadAsText() {
        #expect(Palette.accentFill.contrast(with: Palette.surface) < 3)
    }

    @Test func sectionLabelsUseTurkishCapitals() {
        #expect(Typography.label("son işlemler") == "SON İŞLEMLER")
        #expect(Typography.label("hesaplarım") == "HESAPLARIM")
        #expect(Typography.label("Harcama analizi") == "HARCAMA ANALİZİ")
    }

    // The accent lives twice: in code (Palette) and in the asset catalogue (what the system draws, e.g. the back button).
    // If one is edited alone, the two accents drift apart without anything failing.
    @Test(arguments: [UIUserInterfaceStyle.light, .dark])
    func theAssetCatalogueAccentIsTheSameColourAsThePalette(style: UIUserInterfaceStyle) throws {
        let asset = try #require(UIColor(named: "AccentColor", in: Bundle.main, compatibleWith: UITraitCollection(userInterfaceStyle: style)))
        var (red, green, blue, alpha): (CGFloat, CGFloat, CGFloat, CGFloat) = (0, 0, 0, 0)
        asset.getRed(&red, green: &green, blue: &blue, alpha: &alpha)
        let hex = Palette.accent.hex
        // The catalogue stores three decimals, so half a step of 1/255 is the most it can differ by.
        #expect(abs(red - CGFloat((hex >> 16) & 0xFF) / 255) < 0.003)
        #expect(abs(green - CGFloat((hex >> 8) & 0xFF) / 255) < 0.003)
        #expect(abs(blue - CGFloat(hex & 0xFF) / 255) < 0.003)
    }
}

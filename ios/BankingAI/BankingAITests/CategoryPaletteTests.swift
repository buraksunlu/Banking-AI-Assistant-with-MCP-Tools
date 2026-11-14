import Foundation
import Testing
@testable import BankingAI

// UI-05 PR2: the category colours are looked at by eye, but three rules about them are logic and are kept by tests, so a
// later colour edit cannot quietly make two slices alike or make a slice look like money, an error or the accent.
struct CategoryPaletteTests {
    private var allSwatches: [(CategoryTone, Swatch)] { CategoryTone.allCases.map { ($0, $0.swatch) } }

    // K13-A: the distance is CIE76 ΔE; white against black is 100 by definition, a colour against itself is 0.
    @Test func theColourDistanceIsTheLabDistance() {
        #expect(abs(Swatch(0xFFFFFF).distance(to: Swatch(0x000000)) - 100) < 0.01)
        #expect(Palette.accent.distance(to: Palette.accent) == 0)
        #expect(Swatch(0x123456).distance(to: Swatch(0xABCDEF)) == Swatch(0xABCDEF).distance(to: Swatch(0x123456)))
    }

    // UX-01 R1-A: category colours are fills beside a name written in the primary text colour, so they are graphics and
    // WCAG 1.4.11 asks 3:1 of them on the card; the text threshold of 4,5 only left garish colours on a light theme.
    @Test func everyCategoryColourIsSeenAsAGraphicOnTheCard() {
        for (tone, swatch) in allSwatches {
            #expect(swatch.contrast(with: Palette.surface) >= 3, "\(tone)")
        }
    }

    // The closest pair of real categories is at least 25 apart; the unclassified grey has its own rule below.
    @Test func noTwoCategoriesLookAlike() {
        for (a, left) in allSwatches where a != .gray {
            for (b, right) in allSwatches where b != .gray && "\(a)" < "\(b)" {
                #expect(left.distance(to: right) >= 25, "\(a) and \(b)")
            }
        }
    }

    // Green is money coming in, the accent is a choice, red is an error: no category wears those colours. R1-A: 20 apart,
    // because a warm theme's orange category cannot be 25 from both the amber accent and the red while staying 3:1.
    @Test func noCategoryLooksLikeTheAccentMoneyInOrAnError() {
        for (tone, swatch) in allSwatches where tone != .gray {
            for meaning in [Palette.accent, Palette.accentFill, Palette.incoming, Palette.failure] {
                #expect(swatch.distance(to: meaning) >= 20, "\(tone)")
            }
        }
    }

    // The unclassified slice sits beside the real ones (an income share's "unknown" next to "essential"): never alike.
    @Test func noCategoryLooksLikeTheUnclassifiedGrey() {
        for (tone, swatch) in allSwatches where tone != .gray {
            #expect(swatch.distance(to: CategoryTone.gray.swatch) >= 20, "\(tone)")
        }
    }

    @Test func theUnclassifiedGroupKeepsItsGrey() {
        #expect(CategoryTone.gray.swatch == Swatch(0x8A7F70))
    }

    // K7-A: every bar is the accent; a month still running is paler, and a month other than the chosen one is paler still.
    @Test func aBarsOpacityFollowsItsMonthAndTheSelection() {
        #expect(AnalyticsDisplay.barOpacity(isPartial: false, id: "2026-09", selectedID: nil) == 1)
        #expect(AnalyticsDisplay.barOpacity(isPartial: true, id: "2026-10", selectedID: nil) == 0.7)
        #expect(AnalyticsDisplay.barOpacity(isPartial: false, id: "2026-09", selectedID: "2026-08") == 0.35)
        #expect(AnalyticsDisplay.barOpacity(isPartial: true, id: "2026-10", selectedID: "2026-10") == 0.7)
        #expect(AnalyticsDisplay.barOpacity(isPartial: false, id: "2026-09", selectedID: "2026-09") == 1)
    }

    // The pale month is the only bar that must still be seen as a bar (the dot under it is the second cue): 3:1 on the card.
    @Test func theMonthStillRunningIsPaleButStillAGraphic() {
        let pale = Palette.accent.over(Palette.surface, opacity: AnalyticsDisplay.barOpacity(isPartial: true, id: "a", selectedID: nil))
        #expect(pale.contrast(with: Palette.surface) >= 3)
        #expect(Palette.accent.over(Palette.surface, opacity: 1) == Palette.accent)
        #expect(Palette.accent.over(Palette.surface, opacity: 0) == Palette.surface)
    }

    // An opacity outside 0…1 is taken as its nearest edge rather than producing a negative channel, which would crash.
    @Test func anOpacityOutsideZeroToOneIsClamped() {
        #expect(Palette.accent.over(Palette.surface, opacity: 1.5) == Palette.accent)
        #expect(Palette.accent.over(Palette.surface, opacity: -0.5) == Palette.surface)
    }
}

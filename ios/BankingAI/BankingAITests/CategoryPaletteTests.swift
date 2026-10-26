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

    // Graphics need 3:1 against the card; the unclassified grey is the lowest (3,63), every real category has 4,5 or more.
    @Test func everyCategoryColourIsReadableOnTheCard() {
        for (tone, swatch) in allSwatches {
            #expect(swatch.contrast(with: Palette.surface) >= 3, "\(tone)")
            if tone != .gray { #expect(swatch.contrast(with: Palette.surface) >= 4.5, "\(tone)") }
        }
    }

    // The closest pair in the chosen palette is 28,7 apart; the old system palette had teal and cyan 14 apart.
    @Test func noTwoCategoriesLookAlike() {
        for (a, left) in allSwatches {
            for (b, right) in allSwatches where "\(a)" < "\(b)" {
                #expect(left.distance(to: right) >= 25, "\(a) and \(b)")
            }
        }
    }

    // Green is money coming in, the accent is a choice, red is an error: no category wears those colours.
    @Test func noCategoryLooksLikeTheAccentMoneyInOrAnError() {
        for (tone, swatch) in allSwatches where tone != .gray {
            for meaning in [Palette.accent, Palette.incoming, Palette.failure] {
                #expect(swatch.distance(to: meaning) >= 25, "\(tone)")
            }
        }
    }

    @Test func theUnclassifiedGroupKeepsItsGrey() {
        #expect(CategoryTone.gray.swatch == Swatch(0x6E6E73))
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

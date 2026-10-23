import Foundation
import Testing
@testable import BankingAI

private func slice(_ code: String?, _ name: String, _ amount: String, _ percentage: String, count: Int = 1) -> CategorySpending {
    CategorySpending(categoryCode: code, displayName: name, amount: Decimal(string: amount)!, transactionCount: count,
                     percentage: Decimal(string: percentage)!)
}

// Ada's October 2026 so far (docs/api.md): FOOD 590,00 and the unclassified 145,00 of 735,00.
private let adaSlices = [slice("FOOD", "Yeme & İçme", "590.00", "80.27", count: 4),
                         slice(nil, "Sınıflandırılmamış", "145.00", "19.73")]

// IOS-08: the pure rules behind the category chart. No network, no state.
struct AnalyticsDisplayTests {
    @Test func percentagesAreWrittenTheTurkishWayWithTwoDigits() {
        #expect(AnalyticsDisplay.percentText(Decimal(string: "80.27")!) == "%80,27")
        #expect(AnalyticsDisplay.percentText(Decimal(string: "19.73")!) == "%19,73")
        #expect(AnalyticsDisplay.percentText(0) == "%0,00")
        #expect(AnalyticsDisplay.percentText(100) == "%100,00")
    }

    @Test func aSelectedSliceReadsNameAmountAndPercentage() {
        #expect(AnalyticsDisplay.summary(adaSlices[0], currency: "TRY") == "Yeme & İçme, 590,00 TL, %80,27")
        #expect(AnalyticsDisplay.summary(adaSlices[1], currency: "TRY") == "Sınıflandırılmamış, 145,00 TL, %19,73")
    }

    @Test func voiceOverGetsTheSameSentenceWithTheCount() {
        #expect(AnalyticsDisplay.accessibilityLabel(adaSlices[0], currency: "TRY")
                == "Yeme & İçme, 590,00 TL, yüzde 80,27, 4 işlem")
    }

    @Test func theUnclassifiedSliceIsAlwaysGrayAndNeverTheOtherCategorysColor() {
        #expect(AnalyticsDisplay.tone(forCategory: nil) == .gray)
        #expect(AnalyticsDisplay.tone(forCategory: "OTHER") != .gray)
        #expect(AnalyticsDisplay.tone(forCategory: "OTHER") != AnalyticsDisplay.tone(forCategory: nil))
    }

    @Test func everyKnownCategoryHasItsOwnFixedColor() {
        let codes = ["FOOD", "GROCERY", "CLOTHING", "TRANSPORTATION", "ENTERTAINMENT", "SUBSCRIPTION", "TRAVEL", "FUEL",
                     "BILLS", "HEALTH", "OTHER"]
        let tones = codes.map { AnalyticsDisplay.tone(forCategory: $0) }
        #expect(Set(tones).count == 11)
        // Same code, same color, whatever the order of the slices.
        #expect(AnalyticsDisplay.tone(forCategory: "FOOD") == AnalyticsDisplay.tone(forCategory: "FOOD"))
    }

    @Test func aCategoryTheAppDoesNotKnowStillGetsAColorOfItsOwn() {
        let tone = AnalyticsDisplay.tone(forCategory: "NEW_CATEGORY")
        #expect(tone != .gray)
        #expect(!["FOOD", "GROCERY", "CLOTHING", "TRANSPORTATION", "ENTERTAINMENT", "SUBSCRIPTION", "TRAVEL", "FUEL", "BILLS",
                  "HEALTH", "OTHER"].map { AnalyticsDisplay.tone(forCategory: $0) }.contains(tone))
    }

    @Test func anAngleOnTheRingFindsTheSliceItBelongsTo() {
        // 0…590 is FOOD, 590…735 is the unclassified slice; an edge belongs to the slice that ends there.
        #expect(AnalyticsDisplay.slice(atAmount: 100, in: adaSlices)?.id == "FOOD")
        #expect(AnalyticsDisplay.slice(atAmount: 590, in: adaSlices)?.id == "FOOD")
        #expect(AnalyticsDisplay.slice(atAmount: 590.01, in: adaSlices)?.id == AnalyticsDisplay.uncategorizedID)
        #expect(AnalyticsDisplay.slice(atAmount: 735, in: adaSlices)?.id == AnalyticsDisplay.uncategorizedID)
    }

    @Test func anAngleBeyondTheRingOrOnAnEmptyRingFindsNothing() {
        #expect(AnalyticsDisplay.slice(atAmount: 735.5, in: adaSlices) == nil)
        #expect(AnalyticsDisplay.slice(atAmount: 10, in: []) == nil)
    }
}

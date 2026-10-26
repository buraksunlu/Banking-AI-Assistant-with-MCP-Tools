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

    // IOS-08 PR2: the wording of the monthly chart.

    @Test func monthsHaveTheirShortTurkishNames() {
        #expect((1...12).map { AnalyticsDisplay.shortMonth($0) } == ["Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara"])
    }

    @Test func aMonthIsNamedWithItsYearAndAPartialOneSaysItIsStillRunning() {
        #expect(AnalyticsDisplay.monthLabel(year: 2026, month: 10, isPartial: true) == "Eki 2026 (devam ediyor)")
        #expect(AnalyticsDisplay.monthLabel(year: 2026, month: 9, isPartial: false) == "Eyl 2026")
        // The year border: December belongs to the year before January.
        #expect(AnalyticsDisplay.monthLabel(year: 2025, month: 12, isPartial: false) == "Ara 2025")
        #expect(AnalyticsDisplay.monthLabel(year: 2026, month: 1, isPartial: false) == "Oca 2026")
    }

    @Test func aSelectedMonthReadsMonthAndAmount() {
        let september = MonthlySpending(year: 2026, month: 9, isPartial: false, amount: Decimal(string: "31000.00")!, transactionCount: 1)
        let october = MonthlySpending(year: 2026, month: 10, isPartial: true, amount: Decimal(string: "11350.00")!, transactionCount: 3)
        #expect(AnalyticsDisplay.monthSummary(september, currency: "TRY") == "Eyl 2026, 31.000,00 TL")
        #expect(AnalyticsDisplay.monthSummary(october, currency: "TRY") == "Eki 2026 (devam ediyor), 11.350,00 TL")
        #expect(AnalyticsDisplay.monthAccessibilityLabel(october, currency: "TRY") == "Eki 2026 (devam ediyor), 11.350,00 TL, 3 işlem")
    }

    @Test func aMonthWithoutSpendingIsStillAZeroAmountNotMissing() {
        let may = MonthlySpending(year: 2026, month: 5, isPartial: false, amount: 0, transactionCount: 0)
        #expect(AnalyticsDisplay.monthSummary(may, currency: "TRY") == "May 2026, 0,00 TL")
    }
}

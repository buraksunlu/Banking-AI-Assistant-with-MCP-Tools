import Foundation

/// The colors a category can have; ``CategoryTone/swatch`` (Palette.swift) gives each its colour.
nonisolated enum CategoryTone: CaseIterable {
    case orange, lime, brown, pink, purple, indigo, teal, cyan, magenta, mint, slate
    /// Only for categories the app has no color for yet.
    case yellow
    /// Only for the unclassified group.
    case gray
}

/// Pure rules behind the analytics charts (IOS-08): wording, colors and which slice a point on the ring belongs to.
/// No state and no network, so the rules are tested without a screen.
enum AnalyticsDisplay {
    /// The slice id of the unclassified group, which has no category code.
    static let uncategorizedID = "UNCATEGORIZED"

    private static let turkish = Locale(identifier: "tr_TR")

    /// A fixed color per category code, so a category looks the same in every period (decision IOS-08 K9-B).
    private static let tones: [String: CategoryTone] = [
        "FOOD": .orange, "GROCERY": .lime, "FUEL": .brown, "CLOTHING": .pink, "ENTERTAINMENT": .purple, "BILLS": .indigo,
        "TRANSPORTATION": .teal, "TRAVEL": .cyan, "SUBSCRIPTION": .magenta, "HEALTH": .mint, "OTHER": .slate,
    ]

    static func tone(forCategory code: String?) -> CategoryTone {
        guard let code else { return .gray }
        return tones[code] ?? .yellow
    }

    /// 80.27 → "%80,27". The backend's percentage is only written, never recalculated.
    static func percentText(_ percentage: Decimal) -> String {
        "%" + percentage.formatted(.number.precision(.fractionLength(2)).locale(turkish))
    }

    /// "Yeme & İçme, 590,00 TL, %80,27": what the screen says about the selected slice.
    static func summary(_ slice: CategorySpending, currency: String) -> String {
        "\(slice.displayName), \(Money.text(slice.amount, currency: currency)), \(percentText(slice.percentage))"
    }

    /// The same facts for VoiceOver, with the transaction count; color alone never carries information.
    static func accessibilityLabel(_ slice: CategorySpending, currency: String) -> String {
        let percent = percentText(slice.percentage).dropFirst()
        return "\(slice.displayName), \(Money.text(slice.amount, currency: currency)), yüzde \(percent), \(slice.transactionCount) işlem"
    }

    /// The slice a point on the ring belongs to, given the ring's own scale (the sum of the amounts so far): a point on an
    /// edge belongs to the slice that ends there; beyond the last edge there is none.
    static func slice(atAmount value: Double, in slices: [CategorySpending]) -> CategorySpending? {
        var upperEdge = Decimal(0)
        for slice in slices {
            upperEdge += slice.amount
            if value <= NSDecimalNumber(decimal: upperEdge).doubleValue { return slice }
        }
        return nil
    }

    /// How solid a monthly bar is (UI-05 K7-A): the month still running is paler (it is not a whole month, IOS-08 K12-A; the dot
    /// under it is the second cue), and while one bar is chosen the others step back. Pale enough to read as unfinished, solid
    /// enough to stay a graphic: 3:1 on the card, which a test keeps.
    static func barOpacity(isPartial: Bool, id: String, selectedID: String?) -> Double {
        if let selectedID, selectedID != id { return 0.35 }
        return isPartial ? 0.7 : 1
    }

    private static let monthNames = ["Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara"]

    /// 1…12 → "Oca" … "Ara"; the chart's axis writes this under each bar.
    static func shortMonth(_ month: Int) -> String { monthNames[month - 1] }

    /// "Eki 2026", or "Eki 2026 (devam ediyor)" for the month still running.
    static func monthLabel(year: Int, month: Int, isPartial: Bool) -> String {
        "\(shortMonth(month)) \(year)" + (isPartial ? " (devam ediyor)" : "")
    }

    /// "Eki 2026 (devam ediyor), 11.350,00 TL": what the screen says about the selected bar.
    static func monthSummary(_ spending: MonthlySpending, currency: String) -> String {
        "\(monthLabel(year: spending.year, month: spending.month, isPartial: spending.isPartial)), "
            + Money.text(spending.amount, currency: currency)
    }

    /// The same facts for VoiceOver, with the transaction count.
    static func monthAccessibilityLabel(_ spending: MonthlySpending, currency: String) -> String {
        "\(monthSummary(spending, currency: currency)), \(spending.transactionCount) işlem"
    }
}

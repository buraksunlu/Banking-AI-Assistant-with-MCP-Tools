import Foundation

/// The colors a category can have; the view maps each to a system color so light and dark mode both stay readable.
nonisolated enum CategoryTone: CaseIterable {
    case orange, green, brown, pink, purple, indigo, teal, cyan, red, mint, blue
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
        "FOOD": .orange, "GROCERY": .green, "FUEL": .brown, "CLOTHING": .pink, "ENTERTAINMENT": .purple, "BILLS": .indigo,
        "TRANSPORTATION": .teal, "TRAVEL": .cyan, "SUBSCRIPTION": .red, "HEALTH": .mint, "OTHER": .blue,
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

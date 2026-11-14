import Foundation

/// The small pieces of text of the home header, decided without a view (UI-03).
enum HomeDisplay {
    /// "Murat", "Yılmaz" → "MY". Turkish capitals, so "ilker işık" is "İİ" and not "II".
    static func initials(first: String, last: String) -> String {
        [first, last]
            .compactMap { $0.trimmingCharacters(in: .whitespaces).first.map(String.init) }
            .joined()
            .uppercased(with: Locale(identifier: "tr_TR"))
    }

    /// The backend's day "2026-10-26" as "26 Ekim 2026"; nil when it cannot be read, so the header shows no date instead of
    /// a wrong one.
    static func dayLine(_ localDate: String?) -> String? {
        guard let localDate else { return nil }
        let parts = localDate.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 3, (1...12).contains(parts[1]), (1...31).contains(parts[2]) else { return nil }
        return TransactionDisplay.date(localDate)
    }
}

/// The words of the home panel (UX-01 PR2). Every figure is the backend's; the only choices made here are which word goes with a
/// sign and how many rows fit (K2-A, K5-C).
extension HomeDisplay {
    /// K2-A: the card shows the first three categories in the backend's order; "Tümü" opens the rest.
    static let shownCategories = 3

    /// "Bu ay harcama · 10 gün": the day count is the backend's own day of the month, read from its `endDate`.
    static func paceTitle(_ endDate: String?) -> String {
        guard let endDate else { return "Bu ay harcama" }
        let parts = endDate.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 3, (1...31).contains(parts[2]) else { return "Bu ay harcama" }
        return "Bu ay harcama · \(parts[2]) gün"
    }

    /// The big figure, or words when nothing has been spent yet (a "0,00 TL" reads like a broken card).
    static func paceHeadline(_ pace: CurrencyComparison) -> String {
        pace.currentAmount == 0 ? "Henüz harcama yok" : Money.text(pace.currentAmount, currency: pace.currencyCode)
    }

    /// "Aynı günlerden 635,00 TL az (%7,15)". The amount and the rate are the backend's `change` and `percentageChange`
    /// without their sign; the sign only picks the word. No rate is never written as "%0".
    static func paceLine(_ pace: CurrencyComparison) -> String {
        guard let percentage = pace.percentageChange else { return "Geçen ayın aynı günlerinde harcama yok" }
        if pace.change == 0 { return "Aynı günlerle aynı" }
        let word = pace.change < 0 ? "az" : "fazla"
        return "Aynı günlerden \(Money.text(pace.change.magnitude, currency: pace.currencyCode)) \(word) "
            + "(\(AnalyticsDisplay.percentText(percentage.magnitude)))"
    }

    /// The arrow beside the line: direction only, never a colour that judges more as bad or less as good.
    static func paceSymbol(_ pace: CurrencyComparison) -> String? {
        guard pace.percentageChange != nil, pace.change != 0 else { return nil }
        return pace.change < 0 ? "arrow.down.right" : "arrow.up.right"
    }

    static func topCategories(_ currency: CurrencyCategoryBreakdown) -> [CategorySpending] {
        Array(currency.categories.prefix(shownCategories))
    }

    /// The backend's percentage as a part of the bar's width, kept between empty and full.
    static func barFraction(_ percentage: Decimal) -> Double {
        let part = min(max(percentage / 100, 0), 1)
        return NSDecimalNumber(decimal: part).doubleValue
    }

    /// "Gelir payı · Eylül" from the income model's "Eylül 2026": the year is left to the card below.
    static func incomeTitle(_ month: String?) -> String {
        guard let name = month?.split(separator: " ").first else { return "Gelir payı" }
        return "Gelir payı · \(name)"
    }

    /// The backend's share of income, or the INC-04 sentence when there is no salary to share against (K4-B).
    static func incomeGlance(_ currency: CurrencyAdvice) -> String {
        guard let share = currency.spentShare else { return IncomeShareDisplay.headline(currency) }
        return AnalyticsDisplay.percentText(share)
    }

    /// "Nerede kısarım?" only makes sense against an income (K4-B).
    static func offersAdvice(_ currency: CurrencyAdvice) -> Bool {
        currency.spentShare != nil
    }
}

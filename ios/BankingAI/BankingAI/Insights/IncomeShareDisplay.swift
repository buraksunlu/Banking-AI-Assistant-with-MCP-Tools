import Foundation

/// Pure rules behind the income card (INC-04): its words, its bar and its colours. The figures are the backend's; nothing here
/// adds, subtracts or recalculates a share.
enum IncomeShareDisplay {
    /// One coloured part of the bar: a group's share of income as a part of the bar's width.
    struct Segment: Equatable, Identifiable {
        let group: SpendingGroup
        let fraction: Double
        let label: String

        var id: SpendingGroup { group }
    }

    static let title = "Gelire göre harcama"
    static let footnote = "Kurallara göre hesaplandı; yatırım tavsiyesi değildir."

    private static let months = ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim",
                                 "Kasım", "Aralık"]

    /// "2026-09-01" → "Eylül 2026"; nil when the day cannot be read, so the card shows no month rather than a wrong one.
    static func month(_ startDate: String) -> String? {
        let parts = startDate.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 3, (1...12).contains(parts[1]) else { return nil }
        return "\(months[parts[1] - 1]) \(parts[0])"
    }

    /// "Gelirinizin %90,36 kadarı". "kadarı" takes no suffix, so the percentage is never followed by a wrong "'sı"/"'ü".
    static func headline(_ currency: CurrencyAdvice) -> String {
        guard let share = currency.spentShare else { return "Maaş geliri görünmediği için gelir payı hesaplanamıyor." }
        return "Gelirinizin \(AnalyticsDisplay.percentText(share)) kadarı"
    }

    /// The groups' shares in the backend's order. Above income (K3-A) the bar is full and the rest is cut at its end; the
    /// headline still writes the whole figure. No income, no bar.
    static func segments(_ currency: CurrencyAdvice) -> [Segment] {
        // Decimal until the end, so 1 − 0,8 is 0,2 and not 0,19999999999999996.
        var room: Decimal = 1
        return currency.groups.compactMap { spending in
            guard let share = spending.incomeShare else { return nil }
            let part = min(share / 100, room)
            room -= part
            return Segment(group: spending.group, fraction: NSDecimalNumber(decimal: part).doubleValue,
                           label: "\(name(spending.group)) \(AnalyticsDisplay.percentText(share))")
        }
    }

    /// True when the month cost more than the income base, which the bar alone cannot show.
    static func isAboveIncome(_ currency: CurrencyAdvice) -> Bool {
        (currency.spentShare ?? 0) > 100
    }

    /// "Eğlence · ayda 236,18 TL, yılda 2.834,20 TL". The share rule has no category: it is about the whole group.
    static func adviceLine(_ advice: AdviceItem, currency: String) -> String {
        let subject = advice.categoryDisplayName ?? "İsteğe bağlı harcamalar"
        return "\(subject) · ayda \(Money.text(advice.monthlySaving, currency: currency)), "
            + "yılda \(Money.text(advice.annualSaving, currency: currency))"
    }

    /// At most two (INC-02 K9-A), even if a changed backend sent more.
    static func shownAdvice(_ currency: CurrencyAdvice) -> [AdviceItem] {
        Array(currency.advice.prefix(2))
    }

    /// The line under the bar when there is no advice; nil without income, which the headline already explains (K5-B).
    static func reasonText(_ reason: NoAdviceReason) -> String? {
        switch reason {
        case .noRuleMatched: "Öneri yok; harcamalarınız kuralların içinde kalıyor."
        case .noIncome: nil
        }
    }

    static func name(_ group: SpendingGroup) -> String {
        switch group {
        case .essential: "Temel"
        case .discretionary: "İsteğe bağlı"
        case .unknown: "Bilinmiyor"
        }
    }

    /// The group the advice rules look at takes the accent; the other two stay neutral (K2-B). Never green or red.
    static func swatch(_ group: SpendingGroup) -> Swatch {
        switch group {
        case .essential: CategoryTone.slate.swatch
        case .discretionary: Palette.accent
        case .unknown: CategoryTone.gray.swatch
        }
    }
}

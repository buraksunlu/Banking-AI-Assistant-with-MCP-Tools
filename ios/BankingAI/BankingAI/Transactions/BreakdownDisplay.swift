import Foundation

/// The breakdown's words (DEMO-02). Only writes the backend's fields; nothing is added up or recomputed.
enum BreakdownDisplay {
    static let routineTitle = "Rutin ve temel"
    static let nonRoutineTitle = "Rutin olmayan"
    static let investmentTitle = "Bu harcamalar yatırılsaydı"
    static let singleInvestmentTitle = "Bu harcama yatırılsaydı"
    static let methodLine = "Her harcama kendi gününde alınmış sayıldı."
    static let unknownTag = "Kategorisi belirsiz"

    private static let turkish = Locale(identifier: "tr_TR")
    private static let assetNames = ["USDTRY": "Dolar", "EURTRY": "Euro", "GRAM_GOLD_TRY": "Gram altın"]

    /// "Abonelik", "Fatura", "Taksit 4/12", "Düzenli ödeme", "Temel".
    static func label(_ reason: RoutineReason, installment: String?) -> String {
        switch reason {
        case .subscription: "Abonelik"
        case .bill: "Fatura"
        case .installment: installment.map { "Taksit \(installmentText($0))" } ?? "Taksit"
        case .recurring: "Düzenli ödeme"
        case .essential: "Temel"
        }
    }

    /// The detail screen's tag: the routine label, or nothing for a non-routine purchase.
    static func insightTag(_ insight: TransactionInsight) -> String? {
        insight.routine.map { label($0, installment: insight.installment) }
    }

    /// "Dolar", "Euro", "Gram altın"; another asset keeps the backend's name.
    static func assetName(_ asset: InvestmentAsset) -> String { assetNames[asset.symbol] ?? asset.name }

    /// The asset's value today, or a dash when its price was missing or stale.
    static func valueText(_ asset: InvestmentAsset) -> String {
        asset.value.map { Money.text($0, currency: "TRY") } ?? "—"
    }

    /// "+164,90 TL (%4,40)" / "−436,48 TL (%−11,64)": the backend's difference and percent, the sign from the figure itself.
    static func differenceText(_ asset: InvestmentAsset) -> String {
        guard let difference = asset.difference, let percent = asset.differencePercent else { return "Fiyat yok" }
        let sign = difference < 0 ? "−" : "+"
        let percentText = percent.magnitude.formatted(.number.precision(.fractionLength(2)).locale(turkish))
        return "\(sign)\(Money.text(difference.magnitude, currency: "TRY")) (%\(percent < 0 ? "−" : "")\(percentText))"
    }

    /// "Son 7 günün 120,50 TL'si dahil değil." when something recent was left out.
    static func excludedText(_ investment: BreakdownInvestment) -> String? {
        investment.excludedRecent > 0 ? "Son 7 günün \(Money.text(investment.excludedRecent, currency: "TRY"))'si dahil değil." : nil
    }

    /// A group's header: "2 işlem · 3.750,00 TL".
    static func groupSummary(count: Int, total: Decimal, currency: String) -> String {
        "\(count) işlem · \(Money.text(total, currency: currency))"
    }

    /// "04/12" → "4/12".
    private static func installmentText(_ text: String) -> String {
        let parts = text.split(separator: "/").compactMap { Int($0) }
        return parts.count == 2 ? "\(parts[0])/\(parts[1])" : text
    }
}

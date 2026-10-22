import Foundation

/// How the app writes an amount: the way the assistant writes it (ai/service/formatting.py `money`), so a figure on
/// screen and in a chat answer look the same (decision IOS-04 K3-A). Display only; the app never calculates with it.
enum Money {
    private static let turkish = Locale(identifier: "tr_TR")
    private static let names = ["TRY": "TL"]

    /// 577490.18 TRY → "577.490,18 TL", 500 USD → "500,00 USD".
    static func text(_ amount: Decimal, currency: String) -> String {
        // The API's amounts have two decimals at most (numeric(18,2)), so two fixed digits never round anything away.
        let number = amount.formatted(.number.precision(.fractionLength(2)).locale(turkish))
        return "\(number) \(names[currency] ?? currency)"
    }
}

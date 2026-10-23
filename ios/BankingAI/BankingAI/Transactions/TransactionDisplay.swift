import Foundation

/// What one transaction row says, decided from the backend's fields only (IOS-07 K3-A, K4-A, K5-A, K6-A, K9-A). Nothing
/// is calculated: the sign follows `direction`, the day is the backend's `localDate`.
struct TransactionDisplay {
    let title: String
    /// The kind or category, then the day: "Yeme & İçme · 8 Ekim 2026".
    let subtitle: String
    /// "−165,00 TL" or "+300,00 TL".
    let amount: String
    /// Money in, which the row shows green.
    let isIncoming: Bool
    /// An SF Symbol name.
    let symbol: String

    private static let months = ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim",
                                 "Kasım", "Aralık"]

    /// The eleven category codes of the database; a code added later falls back to the card.
    private static let categorySymbols = [
        "FOOD": "fork.knife", "GROCERY": "cart", "CLOTHING": "tshirt", "TRANSPORTATION": "bus", "ENTERTAINMENT": "ticket",
        "SUBSCRIPTION": "repeat", "TRAVEL": "airplane", "FUEL": "fuelpump", "BILLS": "bolt", "HEALTH": "cross.case",
        "OTHER": "square.grid.2x2",
    ]

    init(_ item: TransactionItem) {
        let label: String
        switch item.kind {
        case .purchase:
            // The bank's own record when no merchant is known; the customer knows it from the statement (K3-A).
            title = item.merchant ?? item.rawDescription
            // Unclassified is not "Diğer" (OTHER): a domain rule.
            label = item.categoryDisplayName ?? "Sınıflandırılmamış"
            symbol = item.categoryCode.flatMap { Self.categorySymbols[$0] } ?? "creditcard"
        case .transfer:
            title = item.counterparty ?? item.rawDescription
            label = item.direction == .credit ? "Gelen transfer" : "Giden transfer"
            symbol = "arrow.left.arrow.right"
        case .salary:
            title = "Maaş"
            label = "Gelir"
            symbol = "banknote"
        case .sale:
            title = item.counterparty ?? "Satış"
            label = "Satış geliri"
            symbol = "storefront"
        case .openingFunding:
            title = "Açılış bakiyesi"
            label = "Açılış"
            symbol = "building.columns"
        }
        subtitle = "\(label) · \(Self.date(item.localDate))"
        isIncoming = item.direction == .credit
        // U+2212, the minus sign, not a hyphen.
        amount = (isIncoming ? "+" : "\u{2212}") + Money.text(item.amount, currency: item.currencyCode)
    }

    /// "2026-10-08" → "8 Ekim 2026". Written by hand rather than through a calendar: the day is already the customer's,
    /// and any time zone would be a chance to move it. Anything else is shown as it came.
    static func date(_ localDate: String) -> String {
        let parts = localDate.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 3, (1...12).contains(parts[1]), (1...31).contains(parts[2]) else { return localDate }
        return "\(parts[2]) \(months[parts[1] - 1]) \(parts[0])"
    }

    /// "13:05" in the customer's time zone from the API's UTC instant ("2026-10-06T09:00:00.123456+00:00"); nil when the
    /// text is not a UTC instant. Read by hand: the microseconds defeat the standard ISO 8601 parsers on older iOS.
    static func time(utc: String, in zone: TimeZone) -> String? {
        guard utc.count >= 19, utc.hasSuffix("+00:00") || utc.hasSuffix("Z") else { return nil }
        let reader = DateFormatter()
        reader.locale = Locale(identifier: "en_US_POSIX")
        reader.timeZone = TimeZone(secondsFromGMT: 0)
        reader.dateFormat = "yyyy-MM-dd'T'HH:mm:ss"
        guard let instant = reader.date(from: String(utc.prefix(19))) else { return nil }
        let writer = DateFormatter()
        writer.locale = Locale(identifier: "en_US_POSIX")
        writer.timeZone = zone
        writer.dateFormat = "HH:mm"
        return writer.string(from: instant)
    }
}

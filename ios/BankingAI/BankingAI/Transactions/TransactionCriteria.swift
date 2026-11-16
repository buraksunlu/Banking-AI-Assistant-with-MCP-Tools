import Foundation

/// What a list is narrowed to besides its period: the kind of transaction, the kind of account, a category and a merchant, and
/// (ACC-01b PR2) a transfer's direction and person and an amount limit with its currency. Only chat links set them (CHAT-07);
/// the account statement and the whole history use ``none``. The backend names them in the link, the phone passes them on as
/// they are.
struct TransactionCriteria: Equatable, Hashable {
    var kind: TransactionKind?
    var accountType: AccountType?
    var categoryCode: String?
    var merchant: String?
    var direction: TransactionDirection?
    var counterparty: String?
    var minAmount: Decimal?
    var maxAmount: Decimal?
    var currencyCode: String?

    static let none = TransactionCriteria()

    init(kind: TransactionKind? = nil, accountType: AccountType? = nil, categoryCode: String? = nil, merchant: String? = nil,
         direction: TransactionDirection? = nil, counterparty: String? = nil, minAmount: Decimal? = nil, maxAmount: Decimal? = nil,
         currencyCode: String? = nil) {
        self.kind = kind
        self.accountType = accountType
        self.categoryCode = categoryCode
        self.merchant = merchant
        self.direction = direction
        self.counterparty = counterparty
        self.minAmount = minAmount
        self.maxAmount = maxAmount
        self.currencyCode = currencyCode
    }

    /// Query fields in a fixed order, after the period's.
    var query: [URLQueryItem] {
        var items: [URLQueryItem] = []
        if let kind { items.append(URLQueryItem(name: "kind", value: kind.rawValue)) }
        if let accountType { items.append(URLQueryItem(name: "accountType", value: accountType.rawValue)) }
        if let categoryCode { items.append(URLQueryItem(name: "categoryCode", value: categoryCode)) }
        if let merchant { items.append(URLQueryItem(name: "merchant", value: merchant)) }
        if let direction { items.append(URLQueryItem(name: "direction", value: direction.rawValue)) }
        if let counterparty { items.append(URLQueryItem(name: "counterparty", value: counterparty)) }
        if let minAmount { items.append(URLQueryItem(name: "minAmount", value: Self.plain(minAmount))) }
        if let maxAmount { items.append(URLQueryItem(name: "maxAmount", value: Self.plain(maxAmount))) }
        if let currencyCode { items.append(URLQueryItem(name: "currencyCode", value: currencyCode)) }
        return items
    }

    /// An amount the way the API reads it: digits and a dot, no grouping ("2000", "5000.5").
    private static func plain(_ amount: Decimal) -> String {
        NSDecimalNumber(decimal: amount).stringValue
    }
}

extension AssistantLink {
    /// The criteria the link's list runs with.
    var criteria: TransactionCriteria {
        TransactionCriteria(kind: query.kind, accountType: query.accountType, categoryCode: query.categoryCode, merchant: query.merchant,
                            direction: query.direction, counterparty: query.counterparty, minAmount: query.minAmount,
                            maxAmount: query.maxAmount, currencyCode: query.currencyCode)
    }

    /// One line that says what the list shows: "Bu ay · Harcamalar · Kişisel hesaplar" (decision CHAT-07 K3-A). The days
    /// under it are the ones the backend resolved, so the line only names the period, a range included.
    var summary: String {
        let period = filter.title
        let kind: String = switch query.kind {
        case .purchase: "Harcamalar"
        case .sale: "Satışlar"
        case .transfer: switch query.direction {
            case .debit: "Gönderilen transferler"
            case .credit: "Gelen transferler"
            case nil: "Transferler"
        }
        case .salary: "Maaşlar"
        case .openingFunding: "Açılış bakiyesi"
        }
        let account = query.accountType == .personal ? "Kişisel hesaplar" : "İşletme hesapları"
        return [period, kind, account, query.merchant, query.counterparty, limit].compactMap { $0 }.joined(separator: " · ")
    }

    /// The amount limit as the answer's facts write it ("2.000,00 TL ve üstü"); nil without one or without its currency.
    private var limit: String? {
        guard let currency = query.currencyCode else { return nil }
        switch (query.minAmount, query.maxAmount) {
        case let (low?, high?): return "\(Money.text(low, currency: currency)) – \(Money.text(high, currency: currency))"
        case let (low?, nil): return "\(Money.text(low, currency: currency)) ve üstü"
        case let (nil, high?): return "\(Money.text(high, currency: currency)) ve altı"
        case (nil, nil): return nil
        }
    }
}

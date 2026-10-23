import Foundation

/// What a list is narrowed to besides its period: the kind of transaction, the kind of account, a category and a merchant.
/// Only chat links set them (CHAT-07); the account statement and the whole history use ``none``. The backend names them in
/// the link, the phone passes them on as they are.
struct TransactionCriteria: Equatable, Hashable {
    var kind: TransactionKind?
    var accountType: AccountType?
    var categoryCode: String?
    var merchant: String?

    static let none = TransactionCriteria()

    init(kind: TransactionKind? = nil, accountType: AccountType? = nil, categoryCode: String? = nil, merchant: String? = nil) {
        self.kind = kind
        self.accountType = accountType
        self.categoryCode = categoryCode
        self.merchant = merchant
    }

    /// Query fields in a fixed order, after the period's.
    var query: [URLQueryItem] {
        var items: [URLQueryItem] = []
        if let kind { items.append(URLQueryItem(name: "kind", value: kind.rawValue)) }
        if let accountType { items.append(URLQueryItem(name: "accountType", value: accountType.rawValue)) }
        if let categoryCode { items.append(URLQueryItem(name: "categoryCode", value: categoryCode)) }
        if let merchant { items.append(URLQueryItem(name: "merchant", value: merchant)) }
        return items
    }
}

extension AssistantLink {
    /// The criteria the link's list runs with.
    var criteria: TransactionCriteria {
        TransactionCriteria(kind: query.kind, accountType: query.accountType, categoryCode: query.categoryCode, merchant: query.merchant)
    }

    /// One line that says what the list shows: "Bu ay · Harcamalar · Kişisel hesaplar" (decision CHAT-07 K3-A). The days
    /// under it are the ones the backend resolved, so the line only names the period, a range included.
    var summary: String {
        let period = filter.title
        let kind: String = switch query.kind {
        case .purchase: "Harcamalar"
        case .sale: "Satışlar"
        case .transfer: "Transferler"
        case .salary: "Maaşlar"
        case .openingFunding: "Açılış bakiyesi"
        }
        let account = query.accountType == .personal ? "Kişisel hesaplar" : "İşletme hesapları"
        return [period, kind, account, query.merchant].compactMap { $0 }.joined(separator: " · ")
    }
}

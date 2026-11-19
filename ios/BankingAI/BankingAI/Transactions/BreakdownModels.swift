import Foundation

/// Why a purchase is in the "Rutin ve temel" group (DEMO-02; the API writes the exact names). An unknown name is a decoding error:
/// the phone never shows a guessed reason.
enum RoutineReason: String, Decodable, CaseIterable {
    case subscription = "Subscription"
    case bill = "Bill"
    case installment = "Installment"
    case recurring = "Recurring"
    case essential = "Essential"
}

/// The spending group of a transaction's category (the API's names).
enum SpendingGroupKind: String, Decodable {
    case essential = "Essential"
    case discretionary = "Discretionary"
    case unknown = "Unknown"
}

/// One asset of a comparison: its value today and the difference, as the backend computed them; null figures when a price was
/// missing or stale.
struct InvestmentAsset: Decodable, Equatable {
    let symbol: String
    let name: String
    let valuePriceDate: String?
    let value: Decimal?
    let difference: Decimal?
    let differencePercent: Decimal?
}

/// "Had this been invested": every purchase bought on its own day (`method`), valued on `valueDate`; `excludedRecent` is the
/// spending of the last days left out; no assets when everything was too recent.
struct BreakdownInvestment: Decodable, Equatable {
    let amount: Decimal
    let excludedRecent: Decimal
    let method: String
    let valueDate: String
    let assets: [InvestmentAsset]
    let disclaimer: String
}

/// One habit advice: a fixed sentence the backend filled in.
struct HabitAdvice: Decodable, Equatable, Hashable {
    let merchant: String
    let message: String
}

/// A purchase of the routine group and why it is there; `installment` is "04/12" for a numbered installment.
struct RoutineItem: Decodable, Equatable, Identifiable {
    let transaction: TransactionItem
    let reason: RoutineReason
    let installment: String?
    var id: UUID { transaction.id }
}

/// A non-routine purchase; not `eligible` when its category is unknown, so it is not compared.
struct NonRoutineItem: Decodable, Equatable, Identifiable {
    let transaction: TransactionItem
    let eligible: Bool
    var id: UUID { transaction.id }
}

struct RoutineGroup: Decodable, Equatable {
    let total: Decimal
    let count: Int
    let items: [RoutineItem]
}

struct NonRoutineGroup: Decodable, Equatable {
    let total: Decimal
    let count: Int
    let items: [NonRoutineItem]
    let investment: BreakdownInvestment?
    let advice: [HabitAdvice]
}

/// One currency's two groups; currencies are never added together.
struct CurrencyBreakdown: Decodable, Equatable, Identifiable {
    let currencyCode: String
    let routine: RoutineGroup
    let nonRoutine: NonRoutineGroup
    var id: String { currencyCode }
}

/// Reply of `GET /api/transactions/breakdown` (DEMO-02). `periodTooLong`: the backend does not break down periods over 31 days.
struct SpendingBreakdown: Decodable, Equatable {
    let period: SearchedPeriod
    let periodTooLong: Bool
    let currencies: [CurrencyBreakdown]
}

/// Reply of `GET /api/transactions/{id}/insight`: the transaction's group and routine reason, and its comparison or the note
/// that says why there is none.
struct TransactionInsight: Decodable, Equatable {
    let transaction: TransactionItem
    let routine: RoutineReason?
    let installment: String?
    let group: SpendingGroupKind
    let investment: BreakdownInvestment?
    let note: String?
}

extension APIRequest where Response == SpendingBreakdown {
    /// `GET /api/transactions/breakdown`: the same period and category or merchant a chat link names (days only for a range).
    static func breakdown(filter: TransactionFilter, criteria: TransactionCriteria) -> APIRequest<SpendingBreakdown> {
        var query = filter.query
        if let category = criteria.categoryCode { query.append(URLQueryItem(name: "categoryCode", value: category)) }
        if let merchant = criteria.merchant { query.append(URLQueryItem(name: "merchant", value: merchant)) }
        return APIRequest(method: "GET", path: "api/transactions/breakdown", query: query, body: nil, requiresToken: true)
    }
}

extension APIRequest where Response == TransactionInsight {
    /// `GET /api/transactions/{id}/insight`: one transaction as the breakdown sees it.
    static func insight(id: UUID) -> APIRequest<TransactionInsight> {
        APIRequest(method: "GET", path: "api/transactions/\(id.uuidString)/insight", body: nil, requiresToken: true)
    }
}

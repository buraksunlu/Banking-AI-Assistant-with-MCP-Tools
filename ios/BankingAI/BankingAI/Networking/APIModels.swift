import Foundation

/// Body of `POST /api/auth/login`.
struct LoginRequest: Encodable {
    let username: String
    let password: String
}

/// A successful login: the bearer token and the moment the API stops accepting it (60 minutes, SEC-01b).
struct LoginResponse: Decodable, Equatable {
    let token: String
    let expiresAt: Date
}

/// `GET /api/me`: who the token belongs to. The internal customer id is deliberately not part of it.
struct MeResponse: Decodable, Equatable {
    let username: String
    let customerNumber: String
    let firstName: String
    let lastName: String
    let preferredCurrency: String
    let timeZone: String
}

/// The API's error body (ProblemDetails with `code`, API-13); only what the app uses.
struct ProblemBody: Decodable {
    let title: String?
    let status: Int?
    let code: String?
    let errors: [String: [String]]?
}

/// Body of `POST /api/assistant/messages`: only the user's text; the customer comes from the token.
struct AssistantMessageRequest: Encodable {
    let message: String
}

/// What kind of reply the assistant gave (the API writes the exact names).
enum AssistantReplyType: String, Decodable {
    case answer = "Answer"
    case clarification = "Clarification"
    case outOfScope = "OutOfScope"
}

/// Reply of `POST /api/assistant/messages`. The API also sends the tools and figures used; the app does not show them yet
/// (IOS-11, decision AI-08 K13-A), so they are not decoded. `suggestions` are the questions to offer under the answer
/// (CHAT-06); required, so a reply without them is a contract mismatch rather than "none" (K6-A). `links` are the buttons
/// under the answer (CHAT-07): required as a list too, but one link this app cannot read is dropped, not the whole answer
/// (CHAT-07 K5-A), so an older app still shows what a newer API answers.
struct AssistantReply: Decodable, Equatable {
    let type: AssistantReplyType
    let answer: String
    let suggestions: [String]
    let links: [AssistantLink]

    private enum Keys: String, CodingKey { case type, answer, suggestions, links }

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: Keys.self)
        type = try values.decode(AssistantReplyType.self, forKey: .type)
        answer = try values.decode(String.self, forKey: .answer)
        suggestions = try values.decode([String].self, forKey: .suggestions)
        links = try values.decode([Skippable<AssistantLink>].self, forKey: .links).compactMap(\.value)
    }
}

/// An element that may fail to decode without failing the list around it.
private struct Skippable<Value: Decodable>: Decodable {
    let value: Value?

    init(from decoder: Decoder) throws {
        value = try? Value(from: decoder)
    }
}

/// The period a chat link names: the API's period names, `custom` carrying two days (docs/api.md, CHAT-07).
enum LinkPeriod: String, Decodable {
    case today = "Today", yesterday = "Yesterday", thisWeek = "ThisWeek", lastWeek = "LastWeek"
    case last7Days = "Last7Days", last30Days = "Last30Days", thisMonth = "ThisMonth", lastMonth = "LastMonth"
    case last3Months = "Last3Months", last6Months = "Last6Months", thisYear = "ThisYear", custom = "Custom"
}

/// The transaction search a link opens, as the backend wrote it.
struct LinkQuery: Decodable, Hashable {
    let period: LinkPeriod
    let from: String?
    let to: String?
    let kind: TransactionKind
    let accountType: AccountType
    let categoryCode: String?
    let merchant: String?

    /// The filter the search runs with; nil when the days do not fit the period (a range needs two real days, a named
    /// period takes none), so a link the phone would misread is never followed.
    var filter: TransactionFilter? {
        if period == .custom {
            guard let from, let to, TransactionFilter.date(from) != nil, TransactionFilter.date(to) != nil else { return nil }
            return .custom(from: from, to: to)
        }
        guard from == nil, to == nil else { return nil }
        return switch period {
        case .today: .today
        case .yesterday: .yesterday
        case .thisWeek: .thisWeek
        case .lastWeek: .lastWeek
        case .last7Days: .last7Days
        case .last30Days: .last30Days
        case .thisMonth: .thisMonth
        case .lastMonth: .lastMonth
        case .last3Months: .last3Months
        case .last6Months: .last6Months
        case .thisYear: .thisYear
        case .custom: nil
        }
    }
}

/// A button under a chat answer: the title and the list it opens (CHAT-07). Built by the backend from the answer's checked
/// tool call; the phone only asks the search the link names.
struct AssistantLink: Decodable, Hashable {
    let title: String
    let query: LinkQuery
    /// `query`'s period as the filter the list runs with, checked once when the link is read.
    let filter: TransactionFilter

    private enum Keys: String, CodingKey { case title, query }

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: Keys.self)
        title = try values.decode(String.self, forKey: .title)
        query = try values.decode(LinkQuery.self, forKey: .query)
        guard let filter = query.filter else {
            throw DecodingError.dataCorruptedError(forKey: .query, in: values, debugDescription: "The period and its days do not fit.")
        }
        self.filter = filter
    }
}

/// Reply of `GET /api/assistant/suggestions`: the questions to show when the chat opens (CHAT-06).
struct AssistantSuggestions: Decodable, Equatable {
    let suggestions: [String]
}

/// Reply of `GET /api/assistant/status`: whether the chat can take questions right now.
struct AssistantStatus: Decodable, Equatable {
    let available: Bool
}

/// Whether an account is the customer's own or their shop's (the API writes the exact names).
enum AccountType: String, Decodable {
    case personal = "Personal"
    case business = "Business"
}

/// One item of `GET /api/accounts`. `createdAt` is not decoded: the screen does not show it, the order already comes from
/// the API, and its microseconds are more than `.iso8601` accepts on older systems.
struct Account: Decodable, Equatable, Identifiable {
    let id: UUID
    let accountName: String
    let accountNumber: String
    let currencyCode: String
    let accountType: AccountType
    let balance: Decimal
}

/// What a transaction is economically (the API writes the exact names). Spending is `purchase` only.
enum TransactionKind: String, Decodable {
    case purchase = "Purchase"
    case sale = "Sale"
    case transfer = "Transfer"
    case salary = "Salary"
    case openingFunding = "OpeningFunding"
}

/// Which way the money went for this account.
enum TransactionDirection: String, Decodable {
    case debit = "Debit"
    case credit = "Credit"
}

/// One transaction as every list of the API returns it (`TransactionItem`, docs/api.md). The rows show the backend's
/// `localDate`, the day in the customer's own time zone (decision IOS-07 K5-A); `occurredAtUtc` is kept as text and only the
/// detail screen turns it into a local time (IOS-06), because the API writes microseconds older iOS date parsers reject.
struct TransactionItem: Decodable, Equatable, Hashable, Identifiable {
    let id: UUID
    let accountName: String
    let occurredAtUtc: String
    /// The local day as the API writes it, "2026-10-08".
    let localDate: String
    let kind: TransactionKind
    let direction: TransactionDirection
    let amount: Decimal
    let currencyCode: String
    let merchant: String?
    let categoryCode: String?
    let categoryDisplayName: String?
    let rawDescription: String
    /// Masked ("Ege Y***"), or the account's name for an own account or a shop.
    let counterparty: String?
    let counterpartyIsOwnAccount: Bool
}

/// The days a search covered, as local dates; without a period the API searches the last three months, with `AllTime` from
/// the day of the oldest transaction.
struct SearchedPeriod: Decodable, Equatable {
    let startDate: String
    let endDate: String
}

/// Reply of `GET /api/transactions`: one page of the customer's transactions, newest first.
struct TransactionSearchResult: Decodable, Equatable {
    let period: SearchedPeriod
    let totalCount: Int
    let hasMore: Bool
    /// Asks for the page after this one; nil on the last page (API-17).
    let nextCursor: String?
    let items: [TransactionItem]
}

/// The named periods the analytics screens offer (IOS-08 K4-B). The API resolves each name with its business clock, so the
/// phone never works out a date; `AllTime` is not here because the analytics endpoints refuse it.
enum AnalyticsPeriod: String, CaseIterable, Identifiable {
    case thisMonth = "ThisMonth", lastMonth = "LastMonth", last3Months = "Last3Months", thisYear = "ThisYear"

    var id: String { rawValue }

    var title: String {
        switch self {
        case .thisMonth: "Bu ay"
        case .lastMonth: "Geçen ay"
        case .last3Months: "Son 3 ay"
        case .thisYear: "Bu yıl"
        }
    }
}

/// `GET /api/analytics/category-breakdown`: one block per currency, never added together. The `period` object of the answer
/// is not decoded; the screen already knows which period it asked for.
struct CategoryBreakdown: Decodable, Equatable {
    let currencies: [CurrencyCategoryBreakdown]
}

/// One currency's spending split by category, biggest first (the API's order).
struct CurrencyCategoryBreakdown: Decodable, Equatable, Identifiable {
    let currencyCode: String
    let totalAmount: Decimal
    let categories: [CategorySpending]

    var id: String { currencyCode }
}

/// One slice. `categoryCode == nil` is the unclassified group, which is not "Diğer" (`OTHER`). `percentage` is the backend's.
struct CategorySpending: Decodable, Equatable, Identifiable {
    let categoryCode: String?
    let displayName: String
    let amount: Decimal
    let transactionCount: Int
    let percentage: Decimal

    var id: String { categoryCode ?? AnalyticsDisplay.uncategorizedID }
}

/// `GET /api/analytics/spending-summary`: what was spent in the period, per currency and never added together. Only what the
/// home card shows is decoded; `period` carries the backend's own days (the card's date line, UI-03 K1-A).
struct SpendingSummary: Decodable, Equatable {
    let period: SearchedPeriod
    let currencies: [CurrencySpending]
}

/// One currency's spending in the period (purchases only, personal accounts).
struct CurrencySpending: Decodable, Equatable, Identifiable {
    let currencyCode: String
    let totalAmount: Decimal
    let transactionCount: Int

    var id: String { currencyCode }
}

/// `GET /api/analytics/monthly-trend`: one block per currency, never added together. The `months` list of windows and the
/// `merchantMatch` field of the answer are not decoded; the chart draws the months of each currency.
struct MonthlyTrend: Decodable, Equatable {
    let currencies: [CurrencyTrend]
}

/// One currency's months, oldest first. A month without spending is in the list with an amount of 0.
struct CurrencyTrend: Decodable, Equatable, Identifiable {
    let currencyCode: String
    let months: [MonthlySpending]

    var id: String { currencyCode }
}

/// One calendar month. `isPartial` is the month still running: its amount is not comparable with a whole month.
struct MonthlySpending: Decodable, Equatable, Identifiable {
    let year: Int
    let month: Int
    let isPartial: Bool
    let amount: Decimal
    let transactionCount: Int

    var id: String { String(format: "%04d-%02d", year, month) }
}

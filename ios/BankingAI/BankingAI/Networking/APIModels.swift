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
/// (CHAT-06); required, so a reply without them is a contract mismatch rather than "none" (K6-A).
struct AssistantReply: Decodable, Equatable {
    let type: AssistantReplyType
    let answer: String
    let suggestions: [String]
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

/// One transaction as every list of the API returns it (`TransactionItem`, docs/api.md). `occurredAtUtc` is not decoded:
/// the screens show the backend's `localDate`, the day in the customer's own time zone (decision IOS-07 K5-A).
struct TransactionItem: Decodable, Equatable, Identifiable {
    let id: UUID
    let accountName: String
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

/// The days a search covered, as local dates; without a period the API searches the last three months.
struct SearchedPeriod: Decodable, Equatable {
    let startDate: String
    let endDate: String
}

/// Reply of `GET /api/transactions`: one page of the customer's transactions, newest first.
struct TransactionSearchResult: Decodable, Equatable {
    let period: SearchedPeriod
    let totalCount: Int
    let hasMore: Bool
    let items: [TransactionItem]
}

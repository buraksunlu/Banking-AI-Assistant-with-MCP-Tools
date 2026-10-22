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

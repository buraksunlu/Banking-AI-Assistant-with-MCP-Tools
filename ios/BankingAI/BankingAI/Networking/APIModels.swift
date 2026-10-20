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

import Foundation

/// One call to the API: where it goes, what it sends and whether it needs the user's token.
struct APIRequest<Response: Decodable> {
    let method: String
    let path: String
    /// Query parameters, sent as they are; `appending(path:)` would escape a "?" written into the path.
    var query: [URLQueryItem] = []
    let body: Data?
    let requiresToken: Bool
    /// How long to wait for this call; nil keeps URLSession's default.
    var timeout: TimeInterval? = nil
}

extension APIRequest where Response == LoginResponse {
    /// `POST /api/auth/login`. The only call without a token; a 401 here means wrong credentials, not an expired session.
    static func login(username: String, password: String) -> APIRequest<LoginResponse> {
        APIRequest(method: "POST", path: "api/auth/login",
                   body: try? JSONEncoder().encode(LoginRequest(username: username, password: password)), requiresToken: false)
    }
}

extension APIRequest where Response == MeResponse {
    /// `GET /api/me`: who is signed in.
    static var me: APIRequest<MeResponse> {
        APIRequest(method: "GET", path: "api/me", body: nil, requiresToken: true)
    }
}

extension APIRequest where Response == [Account] {
    /// `GET /api/accounts`: every account of the customer, oldest first; `[]` when there are none.
    static var accounts: APIRequest<[Account]> {
        APIRequest(method: "GET", path: "api/accounts", body: nil, requiresToken: true)
    }
}

extension APIRequest where Response == CategoryBreakdown {
    /// `GET /api/analytics/category-breakdown`: where the money of one named period went, per currency.
    static func categoryBreakdown(period: AnalyticsPeriod) -> APIRequest<CategoryBreakdown> {
        APIRequest(method: "GET", path: "api/analytics/category-breakdown",
                   query: [URLQueryItem(name: "period", value: period.rawValue)], body: nil, requiresToken: true)
    }
}

extension APIRequest where Response == SpendingSummary {
    /// `GET /api/analytics/spending-summary`: what one named period cost, per currency.
    static func spendingSummary(period: AnalyticsPeriod) -> APIRequest<SpendingSummary> {
        APIRequest(method: "GET", path: "api/analytics/spending-summary",
                   query: [URLQueryItem(name: "period", value: period.rawValue)], body: nil, requiresToken: true)
    }
}

extension APIRequest where Response == MonthlyTrend {
    /// `GET /api/analytics/monthly-trend`: the last `months` whole months and the month still running, per currency.
    static func monthlyTrend(months: Int) -> APIRequest<MonthlyTrend> {
        APIRequest(method: "GET", path: "api/analytics/monthly-trend",
                   query: [URLQueryItem(name: "months", value: String(months))], body: nil, requiresToken: true)
    }
}

extension APIRequest where Response == Account {
    /// Reads an account opened from its home card, checking ownership on the server.
    static func account(_ id: UUID) -> APIRequest<Account> {
        APIRequest(method: "GET", path: "api/accounts/\(id.uuidString)", body: nil, requiresToken: true)
    }
}

extension APIRequest where Response == TransactionSearchResult {
    /// `GET /api/transactions`: the newest transactions of every account. No period: the API's default (the last three
    /// months of the business day, which the phone does not know) decides (IOS-07 K11-D).
    static func transactions(limit: Int) -> APIRequest<TransactionSearchResult> {
        APIRequest(method: "GET", path: "api/transactions", query: [URLQueryItem(name: "limit", value: String(limit))],
                   body: nil, requiresToken: true)
    }

    /// `GET /api/transactions?period=AllTime`: the whole history of every account, newest first (API-17). `cursor` is the
    /// previous page's `nextCursor`; without it the newest page comes.
    static func history(limit: Int, cursor: String? = nil) -> APIRequest<TransactionSearchResult> {
        history(accountID: nil, filter: .allTime, limit: limit, cursor: cursor)
    }

    /// The same paging contract for one account's statement and the customer's whole history. `criteria` narrows the
    /// customer's search further when a chat link opens it (CHAT-07); the period's fields come first, then the criteria's.
    static func history(accountID: UUID?, filter: TransactionFilter, criteria: TransactionCriteria = .none, limit: Int,
                        cursor: String? = nil) -> APIRequest<TransactionSearchResult> {
        var query = filter.query + criteria.query + [URLQueryItem(name: "limit", value: String(limit))]
        if let cursor {
            query.append(URLQueryItem(name: "cursor", value: cursor))
        }
        let path = accountID.map { "api/accounts/\($0.uuidString)/transactions" } ?? "api/transactions"
        return APIRequest(method: "GET", path: path, query: query, body: nil, requiresToken: true)
    }
}

extension APIRequest where Response == AssistantStatus {
    /// `GET /api/assistant/status`: asked when the chat screen opens (decision AI-08 K7-A).
    static var assistantStatus: APIRequest<AssistantStatus> {
        APIRequest(method: "GET", path: "api/assistant/status", body: nil, requiresToken: true)
    }
}

extension APIRequest where Response == AssistantSuggestions {
    /// `GET /api/assistant/suggestions`: the starter questions of an empty chat, chosen from the user's data (CHAT-06).
    static var assistantSuggestions: APIRequest<AssistantSuggestions> {
        APIRequest(method: "GET", path: "api/assistant/suggestions", body: nil, requiresToken: true)
    }
}

extension APIRequest where Response == AssistantReply {
    /// `POST /api/assistant/messages`: one question to the assistant.
    static func assistantMessage(_ text: String) -> APIRequest<AssistantReply> {
        // 40 s: above the API's own 35 s for a chat turn, so its clearer 504 arrives first (decision AI-08 K12-A).
        APIRequest(method: "POST", path: "api/assistant/messages",
                   body: try? JSONEncoder().encode(AssistantMessageRequest(message: text)), requiresToken: true, timeout: 40)
    }
}

/// The app's only way to the API (RULE.md: View → ViewModel → API service → URLSession). Views never touch the network.
final class APIClient {
    private let baseURL: URL
    private let session: URLSession
    private let token: () -> String?
    private let onUnauthorized: () -> Void
    private let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        // The API writes moments as ISO 8601 with an offset, in whole seconds ("2026-10-20T10:27:31+00:00").
        decoder.dateDecodingStrategy = .iso8601
        return decoder
    }()

    init(baseURL: URL, session: URLSession = .shared, token: @escaping () -> String?, onUnauthorized: @escaping () -> Void) {
        self.baseURL = baseURL
        self.session = session
        self.token = token
        self.onUnauthorized = onUnauthorized
    }

    /// Sends the request and decodes the answer; every failure is an ``APIError``.
    func send<Response: Decodable>(_ request: APIRequest<Response>) async throws -> Response {
        var url = baseURL.appending(path: request.path)
        if !request.query.isEmpty {
            url.append(queryItems: request.query)
        }
        var urlRequest = URLRequest(url: url)
        urlRequest.httpMethod = request.method
        urlRequest.setValue("application/json", forHTTPHeaderField: "Accept")
        if let timeout = request.timeout {
            urlRequest.timeoutInterval = timeout
        }
        if let body = request.body {
            urlRequest.httpBody = body
            urlRequest.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }
        if request.requiresToken {
            // No valid token: the same answer the API would give, without a pointless round trip.
            guard let token = token() else {
                onUnauthorized()
                throw APIError.problem(status: 401, code: "unauthorized", title: "Giriş gerekli.", retryAfterSeconds: nil)
            }
            urlRequest.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }

        let (data, response) = try await load(urlRequest)
        guard let http = response as? HTTPURLResponse else { throw APIError.invalidResponse(status: 0) }
        guard (200..<300).contains(http.statusCode) else {
            throw failure(http, data, signedIn: request.requiresToken)
        }
        do {
            return try decoder.decode(Response.self, from: data)
        } catch {
            throw APIError.invalidResponse(status: http.statusCode)
        }
    }

    private func load(_ request: URLRequest) async throws -> (Data, URLResponse) {
        do {
            return try await session.data(for: request)
        } catch is CancellationError {
            throw APIError.cancelled
        } catch let error as URLError where error.code == .cancelled {
            throw APIError.cancelled
        } catch {
            throw APIError.unreachable
        }
    }

    private func failure(_ response: HTTPURLResponse, _ data: Data, signedIn: Bool) -> APIError {
        // A 401 on a signed-in call means the token is no longer accepted (expired, or the customer is gone). On the
        // login call it only means wrong credentials, so the session is left alone.
        if response.statusCode == 401 && signedIn {
            onUnauthorized()
        }
        guard let body = try? decoder.decode(ProblemBody.self, from: data), let code = body.code, let title = body.title else {
            return .invalidResponse(status: response.statusCode)
        }
        if response.statusCode == 400, let errors = body.errors, !errors.isEmpty {
            return .validation(title: title, errors: errors)
        }
        let retryAfter = response.value(forHTTPHeaderField: "Retry-After").flatMap { Int($0) }
        return .problem(status: response.statusCode, code: code, title: title, retryAfterSeconds: retryAfter)
    }
}

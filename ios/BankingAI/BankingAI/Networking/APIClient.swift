import Foundation

/// One call to the API: where it goes, what it sends and whether it needs the user's token.
struct APIRequest<Response: Decodable> {
    let method: String
    let path: String
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
        var urlRequest = URLRequest(url: baseURL.appending(path: request.path))
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
        let retryAfter = response.value(forHTTPHeaderField: "Retry-After").flatMap { Int($0) }
        return .problem(status: response.statusCode, code: code, title: title, retryAfterSeconds: retryAfter)
    }
}

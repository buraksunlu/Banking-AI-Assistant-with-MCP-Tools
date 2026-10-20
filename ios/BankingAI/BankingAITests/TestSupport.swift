import Foundation
@testable import BankingAI

/// One request as the app sent it.
struct RecordedRequest {
    let method: String
    let path: String
    let authorization: String?
    let body: String?
}

/// Answers the app's URLSession requests from memory instead of a server. Every test gets its own host name, and the
/// answers are looked up by host, so tests running in parallel never see each other's answers.
final class MockURLProtocol: URLProtocol {
    typealias Handler = @Sendable (URLRequest) throws -> (status: Int, headers: [String: String], body: String)

    private static let lock = NSLock()
    nonisolated(unsafe) private static var handlers: [String: Handler] = [:]
    nonisolated(unsafe) private static var recorded: [String: [RecordedRequest]] = [:]

    /// Registers the answers for one test and returns the base URL that reaches them.
    static func register(_ handler: @escaping Handler) -> URL {
        let host = "test-\(UUID().uuidString.lowercased()).invalid"
        lock.withLock { handlers[host] = handler }
        return URL(string: "http://\(host)/")!
    }

    static func requests(to baseURL: URL) -> [RecordedRequest] {
        lock.withLock { recorded[baseURL.host() ?? ""] ?? [] }
    }

    static var session: URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [MockURLProtocol.self]
        return URLSession(configuration: configuration)
    }

    override class func canInit(with request: URLRequest) -> Bool { true }

    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        let host = request.url?.host() ?? ""
        // URLSession hands a protocol the body as a stream, not as httpBody.
        let body = request.httpBody ?? request.httpBodyStream.map(Self.readAll)
        let handler = Self.lock.withLock {
            Self.recorded[host, default: []].append(RecordedRequest(
                method: request.httpMethod ?? "", path: request.url?.path() ?? "",
                authorization: request.value(forHTTPHeaderField: "Authorization"),
                body: body.map { String(decoding: $0, as: UTF8.self) }))
            return Self.handlers[host]
        }
        do {
            guard let handler else { throw URLError(.unsupportedURL) }
            let answer = try handler(request)
            let response = HTTPURLResponse(url: request.url!, statusCode: answer.status, httpVersion: "HTTP/1.1",
                                           headerFields: answer.headers)!
            client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            client?.urlProtocol(self, didLoad: Data(answer.body.utf8))
            client?.urlProtocolDidFinishLoading(self)
        } catch {
            client?.urlProtocol(self, didFailWithError: error)
        }
    }

    override func stopLoading() {}

    private static func readAll(_ stream: InputStream) -> Data {
        stream.open()
        defer { stream.close() }
        var data = Data()
        var buffer = [UInt8](repeating: 0, count: 4096)
        while stream.hasBytesAvailable {
            let read = stream.read(&buffer, maxLength: buffer.count)
            if read <= 0 { break }
            data.append(buffer, count: read)
        }
        return data
    }
}

/// A session kept in memory instead of the Keychain.
final class InMemorySessionStorage: SessionStorage {
    var stored: StoredSession?

    init(_ stored: StoredSession? = nil) {
        self.stored = stored
    }

    func load() -> StoredSession? { stored }
    func save(_ session: StoredSession) { stored = session }
    func clear() { stored = nil }
}

/// A JSON answer.
func json(_ status: Int, _ body: String, headers: [String: String] = [:]) -> (status: Int, headers: [String: String], body: String) {
    (status, ["Content-Type": status < 400 ? "application/json" : "application/problem+json"].merging(headers) { $1 }, body)
}

/// The API's problem body (API-13).
func problem(_ status: Int, _ code: String, _ title: String, headers: [String: String] = [:]) -> (status: Int, headers: [String: String], body: String) {
    json(status, #"{"title":"\#(title)","status":\#(status),"code":"\#(code)","traceId":"t"}"#, headers: headers)
}

/// A signed-in (or signed-out) app wired like the real one: a 401 on a signed-in call signs out.
@MainActor
struct TestApp {
    static let now = Date(timeIntervalSince1970: 1_792_000_000)
    static let validSession = StoredSession(token: "token-abc", expiresAt: now.addingTimeInterval(3600))

    let baseURL: URL
    let storage: InMemorySessionStorage
    let session: SessionStore
    let api: APIClient

    init(signedIn: Bool = true, _ handler: @escaping MockURLProtocol.Handler) {
        baseURL = MockURLProtocol.register(handler)
        storage = InMemorySessionStorage(signedIn ? Self.validSession : nil)
        let session = SessionStore(storage: storage, now: { Self.now })
        self.session = session
        api = APIClient(baseURL: baseURL, session: MockURLProtocol.session,
                        token: { session.validToken() }, onUnauthorized: { session.signOut() })
    }

    var requests: [RecordedRequest] { MockURLProtocol.requests(to: baseURL) }
}

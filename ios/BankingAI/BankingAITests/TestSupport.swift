import Foundation
@testable import BankingAI

/// One request as the app sent it.
struct RecordedRequest {
    let method: String
    let path: String
    let authorization: String?
    let body: String?
    let timeout: TimeInterval
}

/// Answers the app's URLSession requests from memory instead of a server. Every test gets its own host name, and the
/// answers are looked up by host, so tests running in parallel never see each other's answers.
final class MockURLProtocol: URLProtocol {
    typealias Answer = (status: Int, headers: [String: String], body: String)
    typealias Handler = @Sendable (URLRequest) throws -> Answer

    private struct Route {
        let handler: Handler
        let delay: TimeInterval
    }

    private static let lock = NSLock()
    nonisolated(unsafe) private static var routes: [String: Route] = [:]
    nonisolated(unsafe) private static var recorded: [String: [RecordedRequest]] = [:]
    nonisolated(unsafe) private static var cancelled: [String: Int] = [:]

    private let finishLock = NSLock()
    nonisolated(unsafe) private var finished = false

    /// Registers the answers for one test and returns the base URL that reaches them. With a delay, the answer comes that
    /// much later unless the request is cancelled first (the "Durdur" tests).
    static func register(delay: TimeInterval = 0, _ handler: @escaping Handler) -> URL {
        let host = "test-\(UUID().uuidString.lowercased()).invalid"
        lock.lock()
        routes[host] = Route(handler: handler, delay: delay)
        lock.unlock()
        return URL(string: "http://\(host)/")!
    }

    static func requests(to baseURL: URL) -> [RecordedRequest] {
        lock.lock()
        defer { lock.unlock() }
        return recorded[baseURL.host() ?? ""] ?? []
    }

    /// How many requests to this test's host were cancelled before their answer came.
    static func cancellations(to baseURL: URL) -> Int {
        lock.lock()
        defer { lock.unlock() }
        return cancelled[baseURL.host() ?? ""] ?? 0
    }

    static var session: URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [MockURLProtocol.self]
        return URLSession(configuration: configuration)
    }

    override class func canInit(with request: URLRequest) -> Bool { true }

    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        let request: URLRequest = self.request
        let host: String = request.url?.host() ?? ""
        // URLSession hands a protocol the body as a stream, not as httpBody.
        let body: Data? = request.httpBody ?? request.httpBodyStream.map(Self.readAll)
        let sent = RecordedRequest(method: request.httpMethod ?? "", path: request.url?.path() ?? "",
                                   authorization: request.value(forHTTPHeaderField: "Authorization"),
                                   body: body.map { String(decoding: $0, as: UTF8.self) }, timeout: request.timeoutInterval)
        let route: Route? = Self.record(sent, host: host)
        let delay: TimeInterval = route?.delay ?? 0
        DispatchQueue.global().asyncAfter(deadline: .now() + delay) {
            self.deliver(request, route: route)
        }
    }

    override func stopLoading() {
        // Called when the app cancels the request, and also after a normal finish; only the first counts.
        guard finish() else { return }
        let host: String = request.url?.host() ?? ""
        Self.lock.lock()
        Self.cancelled[host, default: 0] += 1
        Self.lock.unlock()
    }

    private static func record(_ request: RecordedRequest, host: String) -> Route? {
        lock.lock()
        defer { lock.unlock() }
        recorded[host, default: []].append(request)
        return routes[host]
    }

    /// True only for the first caller: either the answer is delivered or the request was cancelled, never both.
    private func finish() -> Bool {
        finishLock.lock()
        defer { finishLock.unlock() }
        if finished { return false }
        finished = true
        return true
    }

    private func deliver(_ request: URLRequest, route: Route?) {
        guard finish() else { return }
        do {
            guard let route else { throw URLError(.unsupportedURL) }
            let answer: Answer = try route.handler(request)
            let response = HTTPURLResponse(url: request.url!, statusCode: answer.status, httpVersion: "HTTP/1.1",
                                           headerFields: answer.headers)!
            client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            client?.urlProtocol(self, didLoad: Data(answer.body.utf8))
            client?.urlProtocolDidFinishLoading(self)
        } catch {
            client?.urlProtocol(self, didFailWithError: error)
        }
    }

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
func json(_ status: Int, _ body: String, headers: [String: String] = [:]) -> MockURLProtocol.Answer {
    var all: [String: String] = ["Content-Type": status < 400 ? "application/json" : "application/problem+json"]
    for (name, value) in headers {
        all[name] = value
    }
    return (status, all, body)
}

/// The API's problem body (API-13).
func problem(_ status: Int, _ code: String, _ title: String, headers: [String: String] = [:]) -> MockURLProtocol.Answer {
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

    init(signedIn: Bool = true, delay: TimeInterval = 0, _ handler: @escaping MockURLProtocol.Handler) {
        baseURL = MockURLProtocol.register(delay: delay, handler)
        storage = InMemorySessionStorage(signedIn ? Self.validSession : nil)
        let session = SessionStore(storage: storage, now: { Self.now })
        self.session = session
        api = APIClient(baseURL: baseURL, session: MockURLProtocol.session,
                        token: { session.validToken() }, onUnauthorized: { session.signOut() })
    }

    var requests: [RecordedRequest] { MockURLProtocol.requests(to: baseURL) }
}

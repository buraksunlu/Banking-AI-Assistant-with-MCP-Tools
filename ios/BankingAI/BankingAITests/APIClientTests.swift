import Foundation
import Testing
@testable import BankingAI

// IOS-03 core: one place that talks to the API, sends the token, and turns every failure into an APIError.
@MainActor
struct APIClientTests {
    @Test func loginPostsTheCredentialsWithoutAToken() async throws {
        let app = TestApp(signedIn: false) { _ in json(200, #"{"token":"t1","expiresAt":"2026-10-20T10:27:31+00:00"}"#) }

        let response = try await app.api.send(.login(username: "ada", password: "pw"))

        #expect(response == LoginResponse(token: "t1", expiresAt: Date(timeIntervalSince1970: 1_792_492_051)))
        let request = try #require(app.requests.first)
        #expect((request.method, request.path, request.authorization) == ("POST", "/api/auth/login", nil))
        #expect(request.body.flatMap { try? JSONSerialization.jsonObject(with: Data($0.utf8)) as? [String: String] }
            == ["username": "ada", "password": "pw"])
    }

    @Test func signedInCallsCarryTheBearerToken() async throws {
        let app = TestApp { _ in
            json(200, #"{"username":"ada","customerNumber":"C-1","firstName":"Ada","lastName":"Kahve","preferredCurrency":"TRY","timeZone":"Europe/Istanbul"}"#)
        }

        let me = try await app.api.send(.me)

        #expect(me.firstName == "Ada")
        #expect(app.requests.map(\.authorization) == ["Bearer token-abc"])
    }

    @Test func aProblemBodyBecomesAProblemError() async {
        let app = TestApp { _ in problem(502, "assistant_failed", "Asistan bu soruya cevap üretemedi; farklı sormayı deneyin.") }

        await #expect(throws: APIError.problem(status: 502, code: "assistant_failed",
                                               title: "Asistan bu soruya cevap üretemedi; farklı sormayı deneyin.",
                                               retryAfterSeconds: nil)) {
            try await app.api.send(.me)
        }
    }

    @Test func theWaitOfA429IsReadFromRetryAfter() async {
        let app = TestApp { _ in problem(429, "too_many_requests", "Çok fazla deneme; biraz sonra tekrar deneyin.", headers: ["Retry-After": "37"]) }

        await #expect(throws: APIError.problem(status: 429, code: "too_many_requests",
                                               title: "Çok fazla deneme; biraz sonra tekrar deneyin.", retryAfterSeconds: 37)) {
            try await app.api.send(.me)
        }
    }

    @Test func a401OnASignedInCallSignsOut() async {
        let app = TestApp { _ in problem(401, "session_invalid", "Oturum geçersiz; yeniden giriş yapın.") }

        await #expect(throws: APIError.self) { try await app.api.send(.me) }

        #expect(!app.session.isSignedIn)
        #expect(app.storage.stored == nil)
    }

    @Test func a401OnLoginMeansWrongCredentialsNotAnExpiredSession() async {
        let app = TestApp { _ in problem(401, "unauthorized", "Giriş gerekli.") }

        await #expect(throws: APIError.self) { try await app.api.send(.login(username: "ada", password: "wrong")) }

        #expect(app.session.isSignedIn)
    }

    @Test func aSignedInCallWithoutATokenNeverReachesTheNetwork() async {
        let app = TestApp(signedIn: false) { _ in json(200, "{}") }

        await #expect(throws: APIError.problem(status: 401, code: "unauthorized", title: "Giriş gerekli.", retryAfterSeconds: nil)) {
            try await app.api.send(.me)
        }
        #expect(app.requests.isEmpty)
    }

    @Test(arguments: [URLError.Code.cannotConnectToHost, .notConnectedToInternet, .timedOut, .networkConnectionLost])
    func anAPIThatCannotBeReachedIsUnreachable(code: URLError.Code) async {
        let app = TestApp { _ in throw URLError(code) }

        await #expect(throws: APIError.unreachable) { try await app.api.send(.me) }
    }

    @Test func aCancelledRequestIsCancelled() async {
        let app = TestApp { _ in throw URLError(.cancelled) }

        await #expect(throws: APIError.cancelled) { try await app.api.send(.me) }
    }

    @Test func anErrorWithoutAProblemBodyIsOutsideTheContract() async {
        let app = TestApp { _ in (500, [:], "") }

        await #expect(throws: APIError.invalidResponse(status: 500)) { try await app.api.send(.me) }
    }

    @Test func anAnswerThatDoesNotDecodeIsOutsideTheContract() async {
        let app = TestApp { _ in json(200, "not json") }

        await #expect(throws: APIError.invalidResponse(status: 200)) { try await app.api.send(.me) }
    }

    @Test(arguments: [
        (APIError.problem(status: 502, code: "assistant_failed", title: "Asistan bu soruya cevap üretemedi.", retryAfterSeconds: nil),
         "Asistan bu soruya cevap üretemedi."),
        (APIError.problem(status: 429, code: "too_many_requests", title: "Çok fazla deneme; biraz sonra tekrar deneyin.", retryAfterSeconds: 37),
         "Çok fazla deneme; 37 sn sonra tekrar deneyin."),
        (APIError.unreachable, "Sunucuya ulaşılamıyor; bağlantıyı kontrol edip tekrar deneyin."),
        (APIError.invalidResponse(status: 500), "Beklenmeyen bir cevap alındı; tekrar deneyin."),
    ])
    func everyErrorHasATurkishSentence(error: APIError, message: String) {
        #expect(error.message == message)
    }
}

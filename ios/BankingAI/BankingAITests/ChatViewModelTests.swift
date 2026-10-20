import Foundation
import Testing
@testable import BankingAI

private let answer = #"{"type":"Answer","answer":"Dün **145,00 TL** harcadınız.","toolCalls":[{"tool":"get_spending_summary","arguments":{"period":"Yesterday"}}],"facts":[{"label":"Toplam harcama","text":"145,00 TL"}]}"#

/// The assistant's answers by path: the status call and the message call.
private func assistant(status: String = #"{"available":true}"#,
                       message: @escaping @Sendable () throws -> MockURLProtocol.Answer = { json(200, answer) })
    -> MockURLProtocol.Handler {
    { request in request.url?.path() == "/api/assistant/status" ? json(200, status) : try message() }
}

// AI-08: the chat screen's rules, without a screen. Waiting, stopping, the three kinds of failure (K6-A), the banner
// (K4-C, K7-A), Markdown (K8-A) and one message at a time.
@MainActor
struct ChatViewModelTests {
    private func messageRequests(_ app: TestApp) -> [RecordedRequest] {
        app.requests.filter { $0.path == "/api/assistant/messages" }
    }

    // MARK: Sending

    @Test func aTrimmedQuestionGoesOutAndTheAnswerComesBack() async {
        let app = TestApp(assistant())
        let model = ChatViewModel(api: app.api)

        await model.send("  Dün ne harcadım?  ")

        let sent = messageRequests(app)
        #expect(sent.count == 1)
        #expect(sent.first?.authorization == "Bearer token-abc")
        #expect(sent.first?.body.flatMap { try? JSONSerialization.jsonObject(with: Data($0.utf8)) as? [String: String] }
            == ["message": "Dün ne harcadım?"])
        #expect(model.messages.map(\.role) == [.user, .assistant])
        #expect(model.messages.map(\.text) == ["Dün ne harcadım?", "Dün **145,00 TL** harcadınız."])
        #expect(model.messages.map(\.state) == [.sent, .sent])
        #expect(!model.isWaiting)
    }

    @Test func onlyTheChatCallWaitsFortySeconds() async {
        let app = TestApp(assistant())
        let model = ChatViewModel(api: app.api)

        await model.checkStatus()
        await model.send("Dün ne harcadım?")

        // 40 s: above the API's own 35 s, so its clearer 504 arrives first (decision AI-08 K12-A).
        #expect(app.requests.map(\.timeout) == [60, 40])
    }

    @Test(arguments: [("Clarification", "Hangi dönemi soruyorsunuz?"), ("OutOfScope", "Bu konuda yardımcı olamıyorum.")])
    func clarificationsAndOutOfScopeRepliesAreAnswersToo(type: String, text: String) async throws {
        let app = TestApp(assistant(message: { json(200, #"{"type":"\#(type)","answer":"\#(text)","toolCalls":[],"facts":[]}"#) }))
        let model = ChatViewModel(api: app.api)

        await model.send("Ne harcadım?")

        let last = try #require(model.messages.last)
        #expect((last.role, last.text, last.state) == (.assistant, text, .sent))
    }

    @Test(arguments: ["", "   ", String(repeating: "a", count: 501)])
    func blankOrTooLongMessagesAreNotSent(text: String) async {
        let app = TestApp(assistant())
        let model = ChatViewModel(api: app.api)
        model.draft = text

        #expect(!model.canSendDraft)
        await model.sendDraft()

        #expect(messageRequests(app).isEmpty)
        #expect(model.messages.isEmpty)
    }

    @Test func aDraftOfExactly500CharactersIsSentAndCleared() async {
        let app = TestApp(assistant())
        let model = ChatViewModel(api: app.api)
        model.draft = String(repeating: "ı", count: 500)

        #expect(model.canSendDraft)
        await model.sendDraft()

        #expect(messageRequests(app).count == 1)
        #expect(model.draft == "")
    }

    @Test func theExamplesAreThreeRealQuestions() {
        #expect(ChatViewModel.examples.count == 3)
        #expect(ChatViewModel.examples.allSatisfy { $0.hasSuffix("?") })
    }

    // MARK: Failures (K6-A)

    @Test func aFailedAnswerCanBeRetriedWithoutASecondBalloon() async throws {
        nonisolated(unsafe) var calls = 0
        let app = TestApp(assistant(message: {
            calls += 1
            return calls == 1 ? problem(502, "assistant_failed", "Asistan bu soruya cevap üretemedi; farklı sormayı deneyin.") : json(200, answer)
        }))
        let model = ChatViewModel(api: app.api)

        await model.send("Dün ne harcadım?")
        let question = try #require(model.messages.first)
        #expect(model.messages.map(\.state) == [.failed("Asistan bu soruya cevap üretemedi; farklı sormayı deneyin.")])

        await model.retry(question.id)

        #expect(model.messages.map(\.role) == [.user, .assistant])
        #expect(model.messages.first?.id == question.id)
        #expect(model.messages.first?.state == .sent)
        #expect(messageRequests(app).map(\.body) == [question.text, question.text].map { #"{"message":"\#($0)"}"# })
    }

    @Test func aTimeoutSaysToTryAgain() async {
        let app = TestApp(assistant(message: { problem(504, "assistant_timeout", "Asistan zamanında cevap veremedi; tekrar deneyin.") }))
        let model = ChatViewModel(api: app.api)

        await model.send("Dün ne harcadım?")

        #expect(model.messages.map(\.state) == [.failed("Asistan zamanında cevap veremedi; tekrar deneyin.")])
        #expect(model.isAvailable != false)
    }

    @Test func anUnavailableAssistantClosesTheInputUntilItIsBack() async {
        // The message call fails with 503; the status check afterwards says the assistant is back.
        let app = TestApp(assistant(message: { problem(503, "assistant_unavailable", "Asistan şu an kullanılamıyor.") }))
        let model = ChatViewModel(api: app.api)

        await model.send("Dün ne harcadım?")

        #expect(model.isAvailable == false)
        #expect(!model.canSend)
        #expect(model.unavailableMessage == "Asistan şu an kullanılamıyor.")

        await model.checkStatus()

        #expect(model.isAvailable == true)
        #expect(model.canSend)
        #expect(model.unavailableMessage == nil)
    }

    @Test(arguments: [(false, false), (true, true)])
    func theStatusDecidesTheBannerWhenTheScreenOpens(available: Bool, canSend: Bool) async {
        let app = TestApp(assistant(status: #"{"available":\#(available)}"#))
        let model = ChatViewModel(api: app.api)

        await model.checkStatus()

        #expect(model.isAvailable == available)
        #expect(model.canSend == canSend)
    }

    @Test func tooManyMessagesSayHowLongToWait() async {
        let app = TestApp(assistant(message: {
            problem(429, "too_many_requests", "Çok fazla deneme; biraz sonra tekrar deneyin.", headers: ["Retry-After": "37"])
        }))
        let model = ChatViewModel(api: app.api)

        await model.send("Dün ne harcadım?")

        #expect(model.messages.map(\.state) == [.failed("Çok fazla deneme; 37 sn sonra tekrar deneyin.")])
    }

    @Test func aRefusedTokenSignsOut() async {
        let app = TestApp(assistant(message: { problem(401, "session_invalid", "Oturum geçersiz; yeniden giriş yapın.") }))
        let model = ChatViewModel(api: app.api)

        await model.send("Dün ne harcadım?")

        #expect(!app.session.isSignedIn)
    }

    // MARK: Waiting and stopping (K5-C)

    @Test func stoppingCancelsTheRequestAndShowsNoError() async throws {
        let app = TestApp(delay: 5, assistant())
        let model = ChatViewModel(api: app.api)

        let sending = Task { await model.send("Dün ne harcadım?") }
        try await waitUntil { model.isWaiting }
        model.stop()
        await sending.value

        #expect(model.messages.map(\.state) == [.stopped])
        #expect(!model.isWaiting)
        #expect(MockURLProtocol.cancellations(to: app.baseURL) == 1)
    }

    @Test func aSecondMessageWhileWaitingIsIgnored() async throws {
        let app = TestApp(delay: 0.5, assistant())
        let model = ChatViewModel(api: app.api)

        let sending = Task { await model.send("Dün ne harcadım?") }
        try await waitUntil { model.isWaiting }
        #expect(!model.canSend)
        await model.send("Bu hafta ne harcadım?")
        await sending.value

        #expect(messageRequests(app).count == 1)
        #expect(model.messages.map(\.text) == ["Dün ne harcadım?", "Dün **145,00 TL** harcadınız."])
    }

    // MARK: Markdown (K8-A)

    @Test func boldFiguresAreShownBold() throws {
        let text = ChatMessage(role: .assistant, text: "Dün **145,00 TL** harcadınız.").displayText

        #expect(String(text.characters) == "Dün 145,00 TL harcadınız.")
        let bold = text.runs.filter { $0.inlinePresentationIntent == .stronglyEmphasized }.map { String(text[$0.range].characters) }
        #expect(bold == ["145,00 TL"])
    }

    @Test func brokenMarkdownLosesNoText() {
        #expect(String(ChatMessage(role: .assistant, text: "**açık uçlu").displayText.characters) == "**açık uçlu")
    }
}

/// Polls until the condition holds (at most 2 s), for states that start inside a running task.
@MainActor
func waitUntil(_ condition: @MainActor () -> Bool) async throws {
    for _ in 0..<200 where !condition() {
        try await Task.sleep(for: .milliseconds(10))
    }
    #expect(condition())
}

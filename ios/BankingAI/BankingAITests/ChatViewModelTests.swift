import Foundation
import Testing
@testable import BankingAI

private let answer = #"{"type":"Answer","answer":"Dün **145,00 TL** harcadınız.","toolCalls":[{"tool":"get_spending_summary","arguments":{"period":"Yesterday"}}],"facts":[{"label":"Toplam harcama","text":"145,00 TL"}],"suggestions":["Kategorilere göre?","Peki işyerlerine göre?"]}"#

private let starters = #"{"suggestions":["Önümüzdeki 7 günde hangi ödemelerim çıkacak?","Bu ay ne kadar harcadım?","Aboneliklerim neler?"]}"#

/// The assistant's answers by path: the status call, the starter questions and the message call.
private func assistant(status: String = #"{"available":true}"#,
                       suggestions: @escaping @Sendable () throws -> MockURLProtocol.Answer = { json(200, starters) },
                       message: @escaping @Sendable () throws -> MockURLProtocol.Answer = { json(200, answer) })
    -> MockURLProtocol.Handler {
    { request in
        switch request.url?.path() {
        case "/api/assistant/status": json(200, status)
        case "/api/assistant/suggestions": try suggestions()
        default: try message()
        }
    }
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
        let app = TestApp(assistant(message: { json(200, #"{"type":"\#(type)","answer":"\#(text)","toolCalls":[],"facts":[],"suggestions":[]}"#) }))
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

    @Test func theExamplesAreTheBackendsAlwaysAnswerableQuestions() {
        // The same three the out-of-scope reply offers (CHAT-06 K5-A): every customer gets an answer to them, even with no
        // Starbucks among their merchants.
        #expect(ChatViewModel.examples == ["Bu ay ne kadar harcadım?", "Geçen ay en çok hangi kategoriye harcadım?",
                                           "Geçen ay toplam ne kadar harcadım?"])
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
        // The request itself must be on its way: a stop before URLSession starts it cancels nothing on the wire, and the
        // count below stayed 0 now and then on a busy machine.
        try await waitUntil { model.isWaiting && !app.requests.isEmpty }
        model.stop()
        await sending.value

        #expect(model.messages.map(\.state) == [.stopped])
        #expect(!model.isWaiting)
        // URLSession tells the protocol about the cancel on its own queue, a moment after the task has already thrown.
        try await waitUntil { MockURLProtocol.cancellations(to: app.baseURL) == 1 }
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

// CHAT-06: the suggestions under the last answer (K1-B, K2-A) and the starter questions of an empty chat (K3-B, K4-B).
@MainActor
struct ChatSuggestionTests {
    private func suggestionRequests(_ app: TestApp) -> [RecordedRequest] {
        app.requests.filter { $0.path == "/api/assistant/suggestions" }
    }

    @Test func theAnswersSuggestionsAreOfferedUnderIt() async {
        let model = ChatViewModel(api: TestApp(assistant()).api)

        await model.send("Dün ne harcadım?")

        #expect(model.suggestions == ["Kategorilere göre?", "Peki işyerlerine göre?"])
    }

    @Test func aTappedSuggestionGoesOutExactlyAsWritten() async throws {
        let app = TestApp(assistant())
        let model = ChatViewModel(api: app.api)
        await model.send("Dün ne harcadım?")

        await model.send(try #require(model.suggestions.first))

        // The AI service runs the call it offered only for this very text (CHAT-06 K3-C).
        #expect(app.requests.filter { $0.path == "/api/assistant/messages" }.last?.body == #"{"message":"Kategorilere göre?"}"#)
    }

    @Test func sendingClearsTheOldSuggestionsAtOnce() async throws {
        let model = ChatViewModel(api: TestApp(delay: 0.3, assistant()).api)
        await model.send("Dün ne harcadım?")

        let sending = Task { await model.send("Kategorilere göre?") }
        try await waitUntil { model.isWaiting }
        // The server forgets its offer with the next message, so the buttons must not outlive it.
        #expect(model.suggestions.isEmpty)
        await sending.value
    }

    @Test func aFailedOrStoppedQuestionLeavesNoSuggestions() async throws {
        nonisolated(unsafe) var calls = 0
        let model = ChatViewModel(api: TestApp(assistant(message: {
            calls += 1
            return calls == 1 ? json(200, answer) : problem(502, "assistant_failed", "Asistan bu soruya cevap üretemedi; farklı sormayı deneyin.")
        })).api)
        await model.send("Dün ne harcadım?")

        await model.send("Kategorilere göre?")

        #expect(model.suggestions.isEmpty)
    }

    @Test func aQuestionAskedBackOffersNothing() async {
        let model = ChatViewModel(api: TestApp(assistant(message: {
            json(200, #"{"type":"Clarification","answer":"Hangi dönem için?","toolCalls":[],"facts":[],"suggestions":[]}"#)
        })).api)

        await model.send("Ne kadar harcadım?")

        #expect(model.suggestions.isEmpty)
    }

    @Test func aReplyWithoutSuggestionsIsOutsideTheContract() async {
        // K6-A: a missing list is a mismatch to see, not "no suggestions".
        let model = ChatViewModel(api: TestApp(assistant(message: {
            json(200, #"{"type":"Answer","answer":"Dün 145,00 TL harcadınız.","toolCalls":[],"facts":[]}"#)
        })).api)

        await model.send("Dün ne harcadım?")

        #expect(model.messages.map(\.state) == [.failed("Beklenmeyen bir cevap alındı; tekrar deneyin.")])
    }

    @Test func anEmptyChatAsksForItsStartersAndTheStatus() async {
        let app = TestApp(assistant())
        let model = ChatViewModel(api: app.api)
        #expect(model.starters.isEmpty)

        await model.open()

        #expect(model.starters == ["Önümüzdeki 7 günde hangi ödemelerim çıkacak?", "Bu ay ne kadar harcadım?", "Aboneliklerim neler?"])
        #expect(model.isAvailable == true)
        let sent = suggestionRequests(app)
        #expect(sent.map(\.method) == ["GET"])
        #expect(sent.first?.authorization == "Bearer token-abc")
    }

    @Test(arguments: [problem(503, "assistant_unavailable", "Asistan şu an kullanılamıyor."),
                      problem(504, "assistant_timeout", "Asistan zamanında cevap veremedi; tekrar deneyin."),
                      json(200, "bozuk"), json(200, #"{"suggestions":[]}"#)])
    func withoutStartersTheFixedExamplesAreShown(reply: MockURLProtocol.Answer) async {
        let model = ChatViewModel(api: TestApp(assistant(suggestions: { reply })).api)

        await model.open()

        #expect(model.starters == ChatViewModel.examples)
        // Only the status check opens the banner (K4-B): the starters are a nicety.
        #expect(model.isAvailable == true)
    }

    @Test func aChatWithHistoryDoesNotAskForStarters() async {
        let app = TestApp(assistant())
        let model = ChatViewModel(api: app.api)
        await model.send("Dün ne harcadım?")

        await model.open()

        #expect(suggestionRequests(app).isEmpty)
        #expect(app.requests.contains { $0.path == "/api/assistant/status" })
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

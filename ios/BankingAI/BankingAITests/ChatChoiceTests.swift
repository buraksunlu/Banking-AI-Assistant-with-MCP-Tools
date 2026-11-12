import Foundation
import Testing
@testable import BankingAI

private let periodQuestion = #"""
{"type":"Clarification","answer":"Hangi dönem için?","toolCalls":[],"facts":[],"suggestions":[],
"choices":[{"label":"bu ay","detail":""},{"label":"geçen ay","detail":""},{"label":"son 3 ay","detail":""}],"inputHint":"","links":[]}
"""#

private let scenarioQuestion = #"""
{"type":"Clarification","answer":"Bunu iki şekilde hesaplayabilirim. Hangisini istersiniz?","toolCalls":[],"facts":[],"suggestions":[],
"choices":[{"label":"tek seferlik","detail":"bir dönemdeki harcamanızla o dönemin başında dolar, euro ya da altın alsaydınız"},
{"label":"aylık birikim","detail":"son 12 ay boyunca her ay belirli bir TL tutarı biriktirseydiniz"}],"inputHint":"","links":[]}
"""#

private let answer = #"""
{"type":"Answer","answer":"Geçen ay **624,98 TL** harcadınız.","toolCalls":[],"facts":[],"suggestions":["Kategorilere göre?"],"choices":[],"inputHint":"","links":[]}
"""#

private let failure = problem(502, "assistant_failed", "Kılavuz bu soruya cevap üretemedi; farklı sormayı deneyin.")

/// The message call answers with each reply in turn; the last one repeats.
private func replies(_ answers: [MockURLProtocol.Answer]) -> MockURLProtocol.Handler {
    nonisolated(unsafe) var calls = 0
    return { request in
        switch request.url?.path() {
        case "/api/assistant/status": return json(200, #"{"available":true}"#)
        default:
            calls += 1
            return answers[min(calls, answers.count) - 1]
        }
    }
}

// CHAT-10 PR3b: the options of a question asked back, one tappable row each under the last answer (K4-A, K5-B, K8-A; P1-D,
// P4-A, P6-A; PR3b D1-A, D2-A, D4-A).
@MainActor
struct ChatChoiceTests {
    private func sentMessages(_ app: TestApp) -> [String?] {
        app.requests.filter { $0.path == "/api/assistant/messages" }.map(\.body)
    }

    @Test func aQuestionsOptionsAreOfferedUnderIt() async {
        let model = ChatViewModel(api: TestApp(replies([json(200, periodQuestion)])).api)

        await model.send("Ne kadar harcadım?")

        #expect(model.choices.map(\.label) == ["bu ay", "geçen ay", "son 3 ay"])
        #expect(model.choices.map(\.detail) == ["", "", ""])
        #expect(model.messages.last?.text == "Hangi dönem için?")
    }

    @Test func anOptionsDetailComesAsWritten() async {
        let model = ChatViewModel(api: TestApp(replies([json(200, scenarioQuestion)])).api)

        await model.send("Dolar alsaydım ne olurdu?")

        #expect(model.choices == [
            AssistantChoice(label: "tek seferlik", detail: "bir dönemdeki harcamanızla o dönemin başında dolar, euro ya da altın alsaydınız"),
            AssistantChoice(label: "aylık birikim", detail: "son 12 ay boyunca her ay belirli bir TL tutarı biriktirseydiniz"),
        ])
    }

    @Test(arguments: [
        #"{"type":"Clarification","answer":"Hangi dönem için?","toolCalls":[],"facts":[],"suggestions":[],"links":[]}"#,
        #"{"type":"Clarification","answer":"Hangi dönem için?","toolCalls":[],"facts":[],"suggestions":[],"choices":[{"detail":""}],"links":[]}"#,
        #"{"type":"Clarification","answer":"Hangi dönem için?","toolCalls":[],"facts":[],"suggestions":[],"choices":[{"label":"bu ay"}],"links":[]}"#,
        #"{"type":"Clarification","answer":"Hangi dönem için?","toolCalls":[],"facts":[],"suggestions":[],"choices":[],"links":[]}"#,
        #"{"type":"Clarification","answer":"Hangi dönem için?","toolCalls":[],"facts":[],"suggestions":[],"choices":[],"inputHint":null,"links":[]}"#,
    ])
    func aMissingOrBrokenOptionIsOutsideTheContract(reply: String) async {
        // D2-A: an option is the question itself; half a choice would be worse than a visible failure.
        let model = ChatViewModel(api: TestApp(replies([json(200, reply)])).api)

        await model.send("Ne kadar harcadım?")

        #expect(model.messages.map(\.state) == [.failed("Beklenmeyen bir cevap alındı; tekrar deneyin.")])
        #expect(model.choices.isEmpty)
    }

    @Test func aTappedOptionSendsItsLabelAndTheRowsGoAtOnce() async throws {
        let app = TestApp(delay: 0.3, replies([json(200, periodQuestion), json(200, answer)]))
        let model = ChatViewModel(api: app.api)
        await model.send("Ne kadar harcadım?")

        // Not choices[1]: an index into an empty list would crash the whole test process instead of failing this test.
        let tapped = try #require(model.choices.dropFirst().first)
        let sending = Task { await model.send(tapped.label) }
        try await waitUntil { model.isWaiting }
        #expect(model.choices.isEmpty)
        await sending.value

        // P4-A: the label only, which the AI service already matches to the waiting question.
        #expect(sentMessages(app).last == #"{"message":"geçen ay"}"#)
        #expect(model.messages.map(\.text).suffix(2) == ["geçen ay", "Geçen ay **624,98 TL** harcadınız."])
    }

    @Test func aSecondQuestionReplacesTheFirstOnesRows() async throws {
        // The simulator trial of 12 Nov 2026: "aylık birikim" was answered with the amount question, and its rows must be the
        // amount examples, not the scenario rows still on screen.
        let amountQuestion = #"""
        {"type":"Clarification","answer":"Ayda kaç TL biriktirirdiniz?","toolCalls":[],"facts":[],"suggestions":[],
        "choices":[{"label":"500 TL","detail":""},{"label":"1.000 TL","detail":""},{"label":"2.000 TL","detail":""}],"inputHint":"Başka bir tutar yazın","links":[]}
        """#
        let model = ChatViewModel(api: TestApp(replies([json(200, scenarioQuestion), json(200, amountQuestion)])).api)
        await model.send("Dolar alsaydım ne olurdu?")

        await model.send(try #require(model.choices.last).label)

        #expect(model.choices.map(\.label) == ["500 TL", "1.000 TL", "2.000 TL"])
    }

    @Test func anAmountQuestionInvitesAnAmountOfOnesOwnUntilAnythingIsSent() async throws {
        // PR3b D6-A: the card's last row; it opens the keyboard, so it must go with the rows it belongs to.
        let amountQuestion = #"""
        {"type":"Clarification","answer":"Ayda kaç TL biriktirirdiniz?","toolCalls":[],"facts":[],"suggestions":[],
        "choices":[{"label":"1.000 TL","detail":""}],"inputHint":"Başka bir tutar yazın","links":[]}
        """#
        let model = ChatViewModel(api: TestApp(delay: 0.3, replies([json(200, amountQuestion), json(200, answer)])).api)
        await model.send("Dolar alsaydım ne olurdu?")
        #expect(model.inputHint == "Başka bir tutar yazın")

        let sending = Task { await model.send("2.500") }
        try await waitUntil { model.isWaiting }
        #expect(model.inputHint == "")
        await sending.value
        #expect(model.inputHint == "")
    }

    @Test func aQuestionWithoutAHintHasNone() async {
        let model = ChatViewModel(api: TestApp(replies([json(200, periodQuestion)])).api)

        await model.send("Ne kadar harcadım?")

        #expect(model.inputHint == "")
    }

    @Test func anAnswerAfterTheQuestionBringsNoOldRowsBack() async {
        // D1-A: only the last question's options can be tapped (K8-A); the server knows no other.
        let model = ChatViewModel(api: TestApp(replies([json(200, periodQuestion), json(200, answer)])).api)
        await model.send("Ne kadar harcadım?")

        await model.send("geçen ay")

        #expect(model.choices.isEmpty)
        #expect(model.suggestions == ["Kategorilere göre?"])
    }

    @Test func aTypedAnswerWorksTooAndClearsTheRows() async {
        let app = TestApp(replies([json(200, periodQuestion), json(200, answer)]))
        let model = ChatViewModel(api: app.api)
        await model.send("Ne kadar harcadım?")

        model.draft = "  2 "
        await model.sendDraft()

        #expect(sentMessages(app).last == #"{"message":"2"}"#)
        #expect(model.choices.isEmpty)
    }

    @Test func aFailedTurnHidesTheRowsAndTryAgainSendsTheSameLabel() async throws {
        let app = TestApp(replies([json(200, periodQuestion), failure, json(200, answer)]))
        let model = ChatViewModel(api: app.api)
        await model.send("Ne kadar harcadım?")

        await model.send("bu ay")
        // P6-A: the waiting question is kept on the server, but its rows would invite a second answer under a failed one.
        #expect(model.choices.isEmpty)
        let tapped = try #require(model.messages.last)
        #expect(tapped.state == .failed("Kılavuz bu soruya cevap üretemedi; farklı sormayı deneyin."))

        await model.retry(tapped.id)

        #expect(sentMessages(app).suffix(2) == [#"{"message":"bu ay"}"#, #"{"message":"bu ay"}"#])
        #expect(model.messages.last?.text == "Geçen ay **624,98 TL** harcadınız.")
    }

    @Test func optionsWinOverSuggestionsInTheSameReply() async {
        // D4-A: the backend sends none with a question today; if it ever did, "ask something else" chips would compete
        // with the question waiting for an answer.
        let both = #"""
        {"type":"Clarification","answer":"Hangi dönem için?","toolCalls":[],"facts":[],"suggestions":["Kategorilere göre?"],
        "choices":[{"label":"bu ay","detail":""}],"inputHint":"","links":[]}
        """#
        let model = ChatViewModel(api: TestApp(replies([json(200, both)])).api)

        await model.send("Ne kadar harcadım?")

        #expect(model.choices.map(\.label) == ["bu ay"])
        #expect(model.suggestions.isEmpty)
    }

    @Test(arguments: [("tek seferlik", "Tek seferlik"), ("ılık", "Ilık"), ("işyeri", "İşyeri"), ("geçen ay (Ekim)", "Geçen ay (Ekim)"),
                      ("%25", "%25"), ("1.000 TL", "1.000 TL"), ("", "")])
    func aRowShowsItsLabelWithATurkishCapitalButSendsItAsWritten(label: String, shown: String) {
        // The row reads like a sentence; the label a tap sends is untouched (P4-A), so the AI service still matches it.
        #expect(ChoiceDisplay.title(label) == shown)
    }
}

import Foundation
import Testing
@testable import BankingAI

private let answer = #"{"type":"Answer","answer":"Bu ay **8.241,40 TL** harcadınız.","toolCalls":[],"facts":[],"suggestions":[],"choices":[],"inputHint":"","links":[]}"#

// The presentation's remote (FIN sunum, K1-A K3-A K4-A): `compass://ask?q=…` sends a question as if it were typed, and
// `compass://tab/<name>` shows a tab. Debug builds only; anything else is ignored, never guessed.
@MainActor
struct DemoLinkTests {
    private func link(_ text: String) -> DemoLink? { DemoLink(url: URL(string: text)!) }

    @Test func aQuestionKeepsItsTurkishLetters() {
        #expect(link("compass://ask?q=Bu%20ay%20ne%20kadar%20harcad%C4%B1m%3F") == .ask("Bu ay ne kadar harcadım?"))
        #expect(link("compass://ask?q=%20%20Migros%27a%20ne%20kadar%3F%20") == .ask("Migros'a ne kadar?"))
    }

    @Test(arguments: [("home", AppTab.home), ("analysis", .analysis), ("assistant", .assistant), ("profile", .profile)])
    func aTabIsNamedByItsId(name: String, tab: AppTab) {
        #expect(link("compass://tab/\(name)") == .show(tab))
    }

    @Test(arguments: ["https://ask?q=Merhaba", "compass://ask", "compass://ask?q=%20%20", "compass://tab/settings",
                      "compass://tab", "compass://transfer?to=deniz&amount=1000", "compass://ASK?q=Merhaba"])
    func anythingElseIsIgnored(text: String) {
        #expect(link(text) == nil)
    }

    // The API's own limit (500 characters as it counts them); a longer one would only come back as a 400.
    @Test func aQuestionOverTheLimitIsIgnored() {
        let long = String(repeating: "a", count: ChatViewModel.maxLength + 1)
        #expect(link("compass://ask?q=\(long)") == nil)
        #expect(link("compass://ask?q=\(String(repeating: "ı", count: ChatViewModel.maxLength))") != nil)
    }

    @Test func aQuestionOpensTheAssistantAndGoesOut() async {
        let app = TestApp { _ in json(200, answer) }
        let chat = ChatViewModel(api: app.api)
        var shown: [AppTab] = []

        await DemoLink.ask("Bu ay ne kadar harcadım?").perform(in: chat) { shown.append($0) }

        #expect(shown == [.assistant])
        #expect(chat.messages.map(\.text) == ["Bu ay ne kadar harcadım?", "Bu ay **8.241,40 TL** harcadınız."])
        #expect(app.requests.map(\.path) == ["/api/assistant/messages"])
    }

    @Test func aTabLinkSendsNothing() async {
        let app = TestApp { _ in json(200, answer) }
        let chat = ChatViewModel(api: app.api)
        var shown: [AppTab] = []

        await DemoLink.show(.analysis).perform(in: chat) { shown.append($0) }

        #expect(shown == [.analysis])
        #expect(chat.messages.isEmpty)
        #expect(app.requests.isEmpty)
    }

    // Two quick clicks on the slide: the second waits its turn in nobody's queue, it is simply dropped (one message at a time).
    @Test func aSecondQuestionWhileWaitingIsDropped() async {
        let app = TestApp(delay: 0.3) { _ in json(200, answer) }
        let chat = ChatViewModel(api: app.api)

        async let first: Void = DemoLink.ask("Bu ay ne kadar harcadım?").perform(in: chat) { _ in }
        try? await Task.sleep(for: .milliseconds(50))
        await DemoLink.ask("Geçen ay ne kadar harcadım?").perform(in: chat) { _ in }
        await first

        #expect(chat.messages.filter { $0.role == .user }.map(\.text) == ["Bu ay ne kadar harcadım?"])
        #expect(app.requests.count == 1)
    }
}

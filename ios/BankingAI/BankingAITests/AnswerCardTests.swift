import Foundation
import Testing
@testable import BankingAI

/// Murat's September market card (UX-01 PR4 test_cards values): one bar block in each part, a change line, the code's sentence.
private let marketCard = #"""
{"title":"Market · Eylül 2026","headline":{"amount":18016.50,"currency":"TRY","text":"18.016,50 TL"},
 "change":{"percent":50.84,"direction":"up","against":"Ağustos 2026","againstText":"11.944,50 TL","text":"%50,84 artış"},
 "simple":[{"kind":"bars","rows":[{"label":"BİM","amount":6216.00,"text":"6.216,00 TL","share":1.0,"direction":null},
                                  {"label":"Migros Jet","amount":5317.50,"text":"5.317,50 TL","share":0.5,"direction":null},
                                  {"label":"A101","amount":0,"text":"0,00 TL","share":0,"direction":null}],
            "points":[],"items":[],"highlight":null}],
 "detailed":[{"kind":"stats","rows":[],"points":[],"items":[{"label":"İşlem","text":"8"}],"highlight":null}],
 "sentence":"Market: önceki döneme göre 6.072,00 TL fazla."}
"""#

private let simpleOnly = #"""
{"title":"Harcama · 9 Ekim 2026","headline":{"amount":145.00,"currency":"TRY","text":"145,00 TL"},"change":null,
 "simple":[{"kind":"stats","rows":[],"points":[],"items":[{"label":"İşlem","text":"1"}],"highlight":null}],"detailed":[],
 "sentence":"9 Ekim 2026: 1 harcama, toplam 145,00 TL."}
"""#

private let broken = #"{"title":"Bozuk","headline":{"amount":1,"currency":"TRY","text":"1,00 TL"},"change":null,"simple":[{"kind":"pie","rows":[],"points":[],"items":[],"highlight":null}],"detailed":[],"sentence":"x"}"#

private func reply(cards: String?) -> String {
    let field = cards.map { #","cards":[\#($0)]"# } ?? ""
    return #"{"type":"Answer","answer":"Market: önceki döneme göre 6.072,00 TL fazla.","toolCalls":[],"facts":[],"suggestions":[],"choices":[],"inputHint":"","links":[]\#(field)}"#
}

private func decoded(_ body: String) throws -> AssistantReply {
    try JSONDecoder().decode(AssistantReply.self, from: Data(body.utf8))
}

// UX-01 PR5: the answer cards. The phone draws them as they come: every text is the card's own and the only number it uses is the
// backend's `share`, for a bar's length (K3-B).
@MainActor
struct AnswerCardTests {
    // MARK: The contract

    @Test func aReplyCarriesItsCardsAsTheyCame() throws {
        let card = try #require(try decoded(reply(cards: marketCard)).cards.first)

        #expect(card.title == "Market · Eylül 2026")
        #expect(card.headline.text == "18.016,50 TL")
        #expect(card.change?.text == "%50,84 artış" && card.change?.direction == .up)
        #expect(card.simple.first?.rows.map(\.text) == ["6.216,00 TL", "5.317,50 TL", "0,00 TL"])
        #expect(card.detailed.first?.items == [CardStat(label: "İşlem", text: "8")])
        #expect(card.sentence == "Market: önceki döneme göre 6.072,00 TL fazla.")
    }

    @Test func aCardThisAppCannotReadIsLeftOutAndTheReplyStays() throws {
        let read = try decoded(reply(cards: "\(broken),\(simpleOnly)"))

        #expect(read.cards.map(\.title) == ["Harcama · 9 Ekim 2026"])
        #expect(read.answer == "Market: önceki döneme göre 6.072,00 TL fazla.")
    }

    @Test func aReplyWithoutCardsHasNone() throws {
        #expect(try decoded(reply(cards: nil)).cards.isEmpty)
    }

    // MARK: Drawing

    @Test(arguments: [(1.0, 1.0), (0.5, 0.5), (0, 0), (1.4, 1.0), (-0.2, 0)])
    func aBarIsAsLongAsItsShareKeptBetweenEmptyAndFull(share: Double, fraction: Double) {
        #expect(CardDisplay.fraction(share) == fraction)
    }

    @Test func aRowWithANoughtShareIsStillARow() throws {
        let card = try #require(try decoded(reply(cards: marketCard)).cards.first)

        #expect(CardDisplay.blocks(card, mode: .simple).first?.rows.last?.label == "A101")
    }

    @Test func simpleShowsTheSimplePartAndDetailedAddsTheRest() throws {
        let card = try #require(try decoded(reply(cards: marketCard)).cards.first)

        #expect(CardDisplay.blocks(card, mode: .simple).map(\.kind) == [.bars])
        #expect(CardDisplay.blocks(card, mode: .detailed).map(\.kind) == [.bars, .stats])
    }

    @Test func aCardWithNothingMoreHasNoSwitch() throws {
        let market = try #require(try decoded(reply(cards: marketCard)).cards.first)
        let plain = try #require(try decoded(reply(cards: simpleOnly)).cards.first)

        #expect(CardDisplay.hasDetail(market))
        #expect(!CardDisplay.hasDetail(plain))
    }

    @Test(arguments: [(CardDirection.up, "arrow.up.right" as String?), (.down, "arrow.down.right"), (.same, nil)])
    func theChangeArrowIsTheDirectionOnly(direction: CardDirection, symbol: String?) {
        #expect(CardDisplay.symbol(direction) == symbol)
    }

    // MARK: Basit / Detaylı

    @Test func theDefaultIsSimpleAndIsKeptOnTheDevice() throws {
        let defaults = try #require(UserDefaults(suiteName: "answer-mode-\(UUID())"))
        #expect(AnswerPreferences(defaults: defaults).mode == .simple)

        AnswerPreferences(defaults: defaults).mode = .detailed

        #expect(AnswerPreferences(defaults: defaults).mode == .detailed)
    }

    @Test func anUntouchedCardFollowsTheDefaultAndATouchedOneKeepsItsOwn() async throws {
        let app = TestApp { _ in json(200, reply(cards: "\(marketCard),\(marketCard)")) }
        let preferences = AnswerPreferences(defaults: try #require(UserDefaults(suiteName: "answer-mode-\(UUID())")))
        let model = ChatViewModel(api: app.api, preferences: preferences)
        await model.send("Geçen ay markete ne kadar harcadım?")
        let answer = try #require(model.messages.last)
        #expect(answer.cards.count == 2)

        model.setMode(.detailed, for: answer.id, card: 0)
        preferences.mode = .detailed
        preferences.mode = .simple

        #expect(model.mode(for: answer.id, card: 0) == .detailed)
        #expect(model.mode(for: answer.id, card: 1) == .simple)
    }

    @Test func switchingACardAsksNothing() async throws {
        let app = TestApp { _ in json(200, reply(cards: marketCard)) }
        let model = ChatViewModel(api: app.api,
                                  preferences: AnswerPreferences(defaults: try #require(UserDefaults(suiteName: "m-\(UUID())"))))
        await model.send("Geçen ay markete ne kadar harcadım?")
        let sent = app.requests.count
        let answer = try #require(model.messages.last)

        model.setMode(.detailed, for: answer.id, card: 0)
        model.setMode(.simple, for: answer.id, card: 0)

        #expect(app.requests.count == sent)
    }

    // MARK: The home panel's questions (K1-C)

    @Test func aCategoryRowAsksThisMonthsPatternInTheServersWord() {
        #expect(HomeDisplay.question(forCategory: "GROCERY") == "Bu ay market harcamam ne kadar?")
        #expect(HomeDisplay.question(forCategory: "FOOD") == "Bu ay yeme içme harcamam ne kadar?")
    }

    @Test(arguments: ["OTHER", nil, "UNKNOWN"])
    func aRowWithoutAWordAsksNothing(code: String?) {
        #expect(HomeDisplay.question(forCategory: code) == nil)
    }

    @Test func theWordsAreTheServersTen() {
        // The same list as ai/service/test_suggestions.py; a change there must be made here too.
        #expect(HomeDisplay.askWords.map(\.code) == [
            "GROCERY", "FOOD", "CLOTHING", "TRANSPORTATION", "ENTERTAINMENT", "SUBSCRIPTION", "TRAVEL", "FUEL", "BILLS", "HEALTH"])
        #expect(HomeDisplay.askWords.map(\.word) == [
            "market", "yeme içme", "giyim", "ulaşım", "eğlence", "abonelik", "seyahat", "yakıt", "fatura", "sağlık"])
    }

    @Test func aPanelQuestionGoesToTheSameChat() async throws {
        let app = TestApp { _ in json(200, reply(cards: marketCard)) }
        let chat = ChatViewModel(api: app.api)
        let sheet = AskSheet()

        await sheet.ask("Bu ay market harcamam ne kadar?", in: chat)

        #expect(sheet.isPresented)
        #expect(chat.messages.first?.text == "Bu ay market harcamam ne kadar?")
        #expect(chat.messages.last?.cards.count == 1)
    }

    @Test func openingTheSheetEmptyAsksNothing() async {
        let app = TestApp { _ in json(200, reply(cards: nil)) }
        let chat = ChatViewModel(api: app.api)
        let sheet = AskSheet()

        await sheet.ask(nil, in: chat)

        #expect(sheet.isPresented && sheet.startsWriting)
        #expect(chat.messages.isEmpty && app.requests.isEmpty)
    }
}

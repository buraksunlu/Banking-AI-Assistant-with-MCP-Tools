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

/// UX-01 PR6a: Murat's upcoming payments as a list block, with code's note and a headline that is not money.
private let listCard = #"""
{"title":"Yaklaşan ödemeler · 10-17 Ekim 2026","headline":{"amount":null,"currency":null,"text":"229,99 TL"},"change":null,
 "simple":[{"kind":"list","rows":[],"points":[],"items":[],"highlight":null,
            "entries":[{"label":"Netflix","detail":"12 Ekim 2026 · 2 gün sonra","text":"229,99 TL","direction":null}]}],
 "detailed":[],"sentence":"Tahmini toplam 229,99 TL.","notes":["Tarih ve tutarlar tahmindir."]}
"""#

private let broken = #"{"title":"Bozuk","headline":{"amount":1,"currency":"TRY","text":"1,00 TL"},"change":null,"simple":[{"kind":"pie","rows":[],"points":[],"items":[],"highlight":null}],"detailed":[],"sentence":"x"}"#

/// Murat's September categories (UX-01 PR4): three bars in the simple part, the other four in the detailed one.
private let categoriesCard = #"""
{"title":"Kategoriler · Eylül 2026","headline":{"amount":53388.39,"currency":"TRY","text":"53.388,39 TL"},"change":null,
 "simple":[{"kind":"bars","rows":[\#(bar("Faturalar")),\#(bar("Market")),\#(bar("Yakıt"))],"points":[],"items":[],"highlight":null}],
 "detailed":[{"kind":"bars","rows":[\#(bar("Yeme & İçme")),\#(bar("Diğer")),\#(bar("Eğlence")),\#(bar("Abonelik"))],"points":[],"items":[],
              "highlight":null}],
 "sentence":"En büyük pay Faturalar: %51,40."}
"""#

private func bar(_ label: String) -> String {
    #"{"label":"\#(label)","amount":1,"text":"1,00 TL","share":0.5,"direction":null}"#
}

private let summaryLink = #"""
{"title":"İşlemleri gör","query":{"period":"ThisMonth","from":null,"to":null,"kind":"Purchase","accountType":"Personal",
 "categoryCode":null,"merchant":null}}
"""#

private func reply(cards: String?, links: String = "") -> String {
    let field = cards.map { #","cards":[\#($0)]"# } ?? ""
    return #"{"type":"Answer","answer":"Market: önceki döneme göre 6.072,00 TL fazla.","toolCalls":[],"facts":[],"suggestions":[],"choices":[],"inputHint":"","links":[\#(links)]\#(field)}"#
}

@MainActor private func decoded(_ body: String) throws -> AssistantReply {
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

    // Simulator trial 17 Nov 2026: the balance card has no sentence (K1-A), the advice rows a second line under the amount (K3-C).
    @Test func aCardWithoutASentenceShowsNone() {
        #expect(!CardDisplay.showsSentence("", withTransactions: false))
    }

    @Test func aBarRowCarriesItsSecondLineAndAnOlderServicesHasAnEmptyOne() throws {
        let advice = marketCard.replacingOccurrences(of: #""text":"6.216,00 TL","share":1.0,"direction":null}"#,
                                                     with: #""text":"6.216,00 TL","share":1.0,"direction":null,"detail":"gelirin %60,31"}"#)
        let rows = try decoded(reply(cards: advice)).cards[0].simple[0].rows
        #expect(rows[0].detail == "gelirin %60,31")
        #expect(rows[1].detail == "")
    }

    @Test func aReplyWithoutCardsHasNone() throws {
        #expect(try decoded(reply(cards: nil)).cards.isEmpty)
    }

    @Test func aListCardCarriesItsLinesAndItsNotes() throws {
        let card = try #require(try decoded(reply(cards: listCard)).cards.first)

        #expect(card.simple.first?.kind == .list)
        #expect(card.simple.first?.entries == [CardEntry(label: "Netflix", detail: "12 Ekim 2026 · 2 gün sonra", text: "229,99 TL",
                                                         direction: nil)])
        #expect(card.headline.text == "229,99 TL")
        #expect(card.notes == ["Tarih ve tutarlar tahmindir."])
    }

    @Test func aCardOfAnOlderServiceHasNoNotesAndNoLines() throws {
        // PR4's cards had neither field: they are read as empty, not as broken.
        let card = try #require(try decoded(reply(cards: marketCard)).cards.first)

        #expect(card.notes.isEmpty)
        #expect(card.simple.first?.entries.isEmpty == true)
    }

    @Test(arguments: [false, true])
    func theNotesAreShownOpenOrClosed(expanded: Bool) throws {
        let card = try #require(try decoded(reply(cards: listCard)).cards.first)

        #expect(CardDisplay.notes(card) == ["Tarih ve tutarlar tahmindir."])
    }

    @Test(arguments: [(1.0, 1.0), (0.5, 0.5), (0, 0), (1.4, 1.0), (-0.2, 0)])
    func aBarIsAsLongAsItsShareKeptBetweenEmptyAndFull(share: Double, fraction: Double) {
        #expect(CardDisplay.fraction(share) == fraction)
    }

    @Test func aRowWithANoughtShareIsStillARow() throws {
        let card = try #require(try decoded(reply(cards: marketCard)).cards.first)

        #expect(CardDisplay.blocks(card, expanded: false).first?.rows.last?.label == "A101")
    }

    @Test func aClosedCardShowsTheFirstPartAndAnOpenOneTheRestToo() throws {
        let card = try #require(try decoded(reply(cards: marketCard)).cards.first)

        #expect(CardDisplay.blocks(card, expanded: false).map(\.kind) == [.bars])
        #expect(CardDisplay.blocks(card, expanded: true).map(\.kind) == [.bars, .stats])
    }

    @Test func aCardWithNothingMoreHasNoMoreLine() throws {
        let market = try #require(try decoded(reply(cards: marketCard)).cards.first)
        let plain = try #require(try decoded(reply(cards: simpleOnly)).cards.first)

        #expect(CardDisplay.moreTitle(market, expanded: false) != nil)
        #expect(CardDisplay.moreTitle(plain, expanded: false) == nil)
    }

    @Test(arguments: [(CardDirection.up, "arrow.up.right" as String?), (.down, "arrow.down.right"), (.same, nil)])
    func theChangeArrowIsTheDirectionOnly(direction: CardDirection, symbol: String?) {
        #expect(CardDisplay.symbol(direction) == symbol)
    }

    // MARK: Tümünü göster (16 Kasım 2026 simulator review, Bulgu 2: the Basit/Detaylı switch is replaced)

    @Test func theMoreLineCountsEveryRowTheOpenCardWillShow() throws {
        let card = try #require(try decoded(reply(cards: categoriesCard)).cards.first)

        #expect(CardDisplay.moreTitle(card, expanded: false) == "Tümünü göster (7)")
        #expect(CardDisplay.moreTitle(card, expanded: true) == "Daha az göster")
    }

    @Test func aMoreLineOverFiguresWithoutRowsAsksForTheDetails() throws {
        let card = try #require(try decoded(reply(cards: marketCard)).cards.first)

        #expect(CardDisplay.moreTitle(card, expanded: false) == "Ayrıntıları göster")
    }

    @Test func openingACardAsksNothingAndLeavesTheOtherCardsClosed() async throws {
        let app = TestApp { _ in json(200, reply(cards: "\(marketCard),\(marketCard)")) }
        let model = ChatViewModel(api: app.api)
        await model.send("Geçen ay markete ne kadar harcadım?")
        let sent = app.requests.count
        let answer = try #require(model.messages.last)

        model.toggleExpanded(answer.id, card: 0)

        #expect(model.isExpanded(answer.id, card: 0))
        #expect(!model.isExpanded(answer.id, card: 1))
        #expect(app.requests.count == sent)
        model.toggleExpanded(answer.id, card: 0)
        #expect(!model.isExpanded(answer.id, card: 0))
    }

    // MARK: Transactions in the card (Bulgu 1)

    @Test func aCardWithoutRowsTakesItsTransactionsInside() throws {
        let reply = try decoded(reply(cards: simpleOnly, links: summaryLink))

        #expect(CardDisplay.cardTransactions(reply.cards, links: reply.links) == reply.links.first)
    }

    @Test(arguments: [categoriesCard, listCard])
    func aCardWithItsOwnRowsKeepsTheLinkUnderIt(card: String) throws {
        let reply = try decoded(reply(cards: card, links: summaryLink))

        #expect(CardDisplay.cardTransactions(reply.cards, links: reply.links) == nil)
    }

    @Test func twoCardsKeepTheLinkUnderThem() throws {
        let reply = try decoded(reply(cards: "\(simpleOnly),\(simpleOnly)", links: summaryLink))

        #expect(CardDisplay.cardTransactions(reply.cards, links: reply.links) == nil)
    }

    @Test(arguments: [(4, nil as String?), (5, nil), (6, "Tümünü göster (6)"), (12, "Tümünü göster (12)")])
    func theCardShowsFiveTransactionsAndOffersTheRest(total: Int, title: String?) {
        #expect(CardDisplay.previewRows == 5)
        #expect(CardDisplay.allTransactionsTitle(total: total) == title)
    }

    @Test func theSentenceGivesWayToTheTransactions() {
        #expect(CardDisplay.showsSentence("Toplam 145,00 TL.", withTransactions: false))
        #expect(!CardDisplay.showsSentence("Toplam 145,00 TL.", withTransactions: true))
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

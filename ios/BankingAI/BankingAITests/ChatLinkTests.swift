import Foundation
import Testing
@testable import BankingAI

// CHAT-07 (iPhone). A chat answer carries "İşlemleri gör" buttons; each opens the list the figure was computed from. The
// backend builds the link (docs/api.md); the phone only reads it, shows it under its own answer (K4-A) and asks the same
// search the link names, with the same paging. Expected query strings are hand-written, never produced by the code.

private func linkJSON(period: String = "ThisMonth", from: String? = nil, to: String? = nil, kind: String = "Purchase",
                      accountType: String = "Personal", categoryCode: String? = nil, merchant: String? = nil) -> String {
    func text(_ value: String?) -> String { value.map { "\"\($0)\"" } ?? "null" }
    return """
    {"title":"İşlemleri gör","query":{"period":"\(period)","from":\(text(from)),"to":\(text(to)),"kind":"\(kind)",
    "accountType":"\(accountType)","categoryCode":\(text(categoryCode)),"merchant":\(text(merchant))}}
    """
}

private func replyJSON(links: String?, suggestions: String = "[]") -> String {
    let field = links.map { #","links":\#($0)"# } ?? ""
    return #"{"type":"Answer","answer":"Bu ay **8.241,40 TL** harcadınız.","toolCalls":[],"facts":[],"suggestions":\#(suggestions),"choices":[],"inputHint":""\#(field)}"#
}

@MainActor private func decode(_ json: String) throws -> AssistantReply {
    try JSONDecoder().decode(AssistantReply.self, from: Data(json.utf8))
}

/// One transaction row; `cursor` makes the page say another one follows.
private func linkPage(cursor: String? = nil, total: Int = 4) -> String {
    let token = cursor.map { "\"\($0)\"" } ?? "null"
    return """
    {"period":{"startDate":"2026-10-01","endDate":"2026-10-10"},"totalCount":\(total),"hasMore":\(cursor != nil),"nextCursor":\(token),
    "items":[{"id":"30000000-0000-0000-0000-000000000001","accountName":"Murat aile TRY","occurredAtUtc":"2026-10-09T10:00:00.123456+00:00","localDate":"2026-10-09","kind":"Purchase",
    "direction":"Debit","amount":2658.50,"currencyCode":"TRY","merchant":"Akaryakıt İstasyonu","categoryCode":"FUEL",
    "categoryDisplayName":"Yakıt","rawDescription":"POS TEST","counterparty":null,"counterpartyIsOwnAccount":false}]}
    """
}

@MainActor private func link(_ linkText: String) throws -> AssistantLink {
    let links = try decode(replyJSON(links: "[\(linkText)]")).links
    return try #require(links.first)
}

@MainActor
struct ChatLinkTests {
    // MARK: Reading the reply

    @Test func aReplyCarriesItsLinks() throws {
        let reply = try decode(replyJSON(links: "[\(linkJSON())]"))

        #expect(reply.links.count == 1)
        #expect(reply.links[0].title == "İşlemleri gör")
        #expect(reply.links[0].query.kind == .purchase)
        #expect(reply.links[0].query.accountType == .personal)
    }

    @Test func aReplyWithoutLinksIsAContractMismatchNotNone() {
        // Like the suggestions (CHAT-06 K6-A): a missing list is a different API, not "no buttons".
        #expect(throws: DecodingError.self) { try decode(replyJSON(links: nil)) }
    }

    @Test func anEmptyListMeansNoButtons() throws {
        #expect(try decode(replyJSON(links: "[]")).links.isEmpty)
    }

    @Test(arguments: [
        linkJSON(period: "Someday"),                                       // a period this app does not know
        linkJSON(period: "Custom"),                                        // a range needs both days
        linkJSON(period: "Custom", from: "2026-09-01"),
        linkJSON(period: "ThisMonth", from: "2026-09-01", to: "2026-09-05"), // a named period takes no days
        linkJSON(kind: "Gift"),
        linkJSON(accountType: "Joint")
    ])
    func aLinkThatCannotBeReadIsDroppedAndTheAnswerStays(bad: String) throws {
        // K5-A: one unreadable button must not cost the customer the answer; the good one next to it stays.
        let reply = try decode(replyJSON(links: "[\(bad),\(linkJSON(period: "LastMonth"))]"))

        #expect(reply.answer == "Bu ay **8.241,40 TL** harcadınız.")
        #expect(reply.links.map(\.query.period) == [.lastMonth])
    }

    // MARK: Under the answer

    @Test func theButtonStaysUnderItsOwnAnswerAfterTheNextQuestion() async throws {
        let replies = Answers([{ json(200, replyJSON(links: "[\(linkJSON())]")) }, { json(200, replyJSON(links: "[]")) }])
        let app = TestApp { request in
            switch request.url?.path() {
            case "/api/assistant/status": json(200, #"{"available":true}"#)
            default: try replies.next(request)
            }
        }
        let model = ChatViewModel(api: app.api)

        await model.send("Bu ay ne harcadım?")
        await model.send("Kategorilere göre?")

        // K4-A: the link is the evidence of the answer it came with, so it is not cleared like the suggestions are.
        let answers = model.messages.filter { $0.role == .assistant }
        #expect(answers.map(\.links.count) == [1, 0])
        #expect(model.messages.filter { $0.role == .user }.allSatisfy { $0.links.isEmpty })
    }

    // MARK: The list a link opens

    private func opened(_ linkText: String, pages: [String] = [linkPage()]) async throws -> (TransactionHistoryViewModel, TestApp) {
        let answers = Answers(pages.map { page in { json(200, page) } })
        let app = TestApp(answers.next)
        let model = ChatViewModel(api: app.api).transactions(for: try link(linkText))
        await model.loadMore()
        return (model, app)
    }

    @Test(arguments: [
        (linkJSON(), "period=ThisMonth&kind=Purchase&accountType=Personal&limit=20"),
        (linkJSON(period: "Yesterday"), "period=Yesterday&kind=Purchase&accountType=Personal&limit=20"),
        // A range goes as its two days only: the API refuses a period name together with days (the IOS-05 bug).
        (linkJSON(period: "Custom", from: "2026-09-01", to: "2026-09-05"), "from=2026-09-01&to=2026-09-05&kind=Purchase&accountType=Personal&limit=20"),
        (linkJSON(period: "LastMonth", merchant: "Starbucks"), "period=LastMonth&kind=Purchase&accountType=Personal&merchant=Starbucks&limit=20"),
        (linkJSON(categoryCode: "FOOD"), "period=ThisMonth&kind=Purchase&accountType=Personal&categoryCode=FOOD&limit=20"),
        (linkJSON(period: "Last7Days", merchant: "Kahve Durağı"), "period=Last7Days&kind=Purchase&accountType=Personal&merchant=Kahve%20Dura%C4%9F%C4%B1&limit=20")
    ])
    func theListAsksTheSameSearchTheLinkNames(linkJson: String, expected: String) async throws {
        let (_, app) = try await opened(linkJson)

        #expect(app.requests.map(\.query) == [expected])
        #expect(app.requests.map(\.path) == ["/api/transactions"])
        #expect(app.requests.allSatisfy { $0.authorization == "Bearer token-abc" })
    }

    @Test func thePagesFollowTheCursorWithTheSameQuery() async throws {
        let (model, app) = try await opened(linkJSON(), pages: [linkPage(cursor: "c1"), linkPage()])
        await model.loadMore()

        #expect(app.requests.map(\.query) == [
            "period=ThisMonth&kind=Purchase&accountType=Personal&limit=20",
            "period=ThisMonth&kind=Purchase&accountType=Personal&limit=20&cursor=c1"
        ])
        #expect(!model.hasMore)
    }

    @Test func aRefreshStartsFromTheFirstPageOfTheSameQuery() async throws {
        let (model, app) = try await opened(linkJSON(), pages: [linkPage(cursor: "c1")])
        await model.refresh()

        #expect(app.requests.map(\.query) == Array(repeating: "period=ThisMonth&kind=Purchase&accountType=Personal&limit=20", count: 2))
    }

    @Test func aPeriodWithoutMatchesSaysSo() async throws {
        let empty = #"{"period":{"startDate":"2026-09-01","endDate":"2026-09-01"},"totalCount":0,"hasMore":false,"nextCursor":null,"items":[]}"#
        let (model, _) = try await opened(linkJSON(period: "Yesterday"), pages: [empty])

        #expect(model.isEmpty)
        #expect(model.emptyMessage == "Seçilen dönemde işlem yok.")
    }

    @Test func theListShowsTheCountTheBackendGaveAndNeverASum() async throws {
        let (model, _) = try await opened(linkJSON(), pages: [linkPage(total: 4)])

        #expect(model.totalCount == 4)
        #expect(model.periodText == "1 Ekim 2026 – 10 Ekim 2026")
    }

    // MARK: Words

    @Test(arguments: [
        (linkJSON(), "Bu ay · Harcamalar · Kişisel hesaplar"),
        (linkJSON(period: "Today"), "Bugün · Harcamalar · Kişisel hesaplar"),
        (linkJSON(period: "Yesterday"), "Dün · Harcamalar · Kişisel hesaplar"),
        (linkJSON(period: "ThisWeek"), "Bu hafta · Harcamalar · Kişisel hesaplar"),
        (linkJSON(period: "LastWeek"), "Geçen hafta · Harcamalar · Kişisel hesaplar"),
        (linkJSON(period: "Last7Days"), "Son 7 gün · Harcamalar · Kişisel hesaplar"),
        (linkJSON(period: "Last30Days"), "Son 30 gün · Harcamalar · Kişisel hesaplar"),
        (linkJSON(period: "LastMonth"), "Geçen ay · Harcamalar · Kişisel hesaplar"),
        (linkJSON(period: "Last3Months"), "Son 3 ay · Harcamalar · Kişisel hesaplar"),
        (linkJSON(period: "Last6Months"), "Son 6 ay · Harcamalar · Kişisel hesaplar"),
        (linkJSON(period: "ThisYear"), "Bu yıl · Harcamalar · Kişisel hesaplar"),
        // The days are on the line under it, as the backend resolved them; the summary does not repeat them.
        (linkJSON(period: "Custom", from: "2026-09-01", to: "2026-09-05"), "Özel tarih aralığı · Harcamalar · Kişisel hesaplar"),
        (linkJSON(period: "LastMonth", merchant: "Starbucks"), "Geçen ay · Harcamalar · Kişisel hesaplar · Starbucks")
    ])
    func theListSaysWhatItShowsInOneLine(linkJson: String, expected: String) throws {
        #expect(try link(linkJson).summary == expected)
    }

    @Test func theNewNamedPeriodsAreNotOfferedInTheFilterPanel() {
        // The seven names exist for links only; the panel's list (IOS-05) is unchanged.
        #expect(TransactionFilter.presets == [.allTime, .thisMonth, .lastMonth, .last3Months, .last6Months])
    }
}

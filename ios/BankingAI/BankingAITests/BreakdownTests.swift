import Foundation
import Testing
@testable import BankingAI

// DEMO-02 (iPhone). A spending link of a chat answer opens the backend's breakdown: "Rutin ve temel" / "Rutin olmayan", the
// comparison with dollars, euros and gold and the habit advice; a transaction's detail shows its own comparison or the reason
// there is none. The phone adds nothing up: every figure on screen is a field of the answer. JSON is the backend's shape
// (docs/api.md, DEMO-02 PR1), trimmed; figures are Murat's 5 September 2026 on the live data.

private func transaction(_ number: Int, merchant: String, amount: String, category: String = "CLOTHING",
                         raw: String = "POS TEST") -> String {
    let id = String(format: "30000000-0000-0000-0000-%012d", number)
    return """
    {"id":"\(id)","accountId":"20000000-0000-0000-0000-000000000009","accountName":"Murat aile TRY",
    "occurredAtUtc":"2026-09-05T11:06:00+00:00","localDate":"2026-09-05","kind":"Purchase","direction":"Debit","amount":\(amount),
    "currencyCode":"TRY","merchant":"\(merchant)","categoryCode":"\(category)","categoryDisplayName":"Giyim","rawDescription":"\(raw)",
    "counterparty":null,"counterpartyIsOwnAccount":false}
    """
}

private let investment = """
{"amount":3750.0,"excludedRecent":120.5,"method":"EachOnItsOwnDay","valueDate":"2026-11-19",
"assets":[
 {"symbol":"USDTRY","name":"ABD Doları / Türk Lirası","baseUnit":"USD","buyDate":"2026-09-05","buyPriceDate":null,"buyPrice":null,
  "valueDate":"2026-11-19","valuePriceDate":"2026-11-18","valuePrice":42.1,"units":91.2,"value":3914.9,"difference":164.9,
  "differencePercent":4.4,"status":"Ok","source":"TCMB"},
 {"symbol":"GRAM_GOLD_TRY","name":"Gram Altın","baseUnit":"gram","buyDate":"2026-09-05","buyPriceDate":null,"buyPrice":null,
  "valueDate":"2026-11-19","valuePriceDate":"2026-11-18","valuePrice":6000.0,"units":0.5,"value":3313.52,"difference":-436.48,
  "differencePercent":-11.64,"status":"Ok","source":"GOLDAPI+TCMB"}],
"disclaimer":"Geçmiş fiyatlarla yapılmış bir hesaptır."}
"""

private func breakdown(tooLong: Bool = false, reason: String = "Installment") -> String {
    let currencies = tooLong ? "[]" : """
    [{"currencyCode":"TRY",
      "routine":{"total":7972.5,"count":2,"items":[
        {"transaction":\(transaction(1, merchant: "Apple Store", amount: "4250.0", category: "OTHER", raw: "APPLE STORE TAKSIT 04/12")),
         "reason":"\(reason)","installment":"04/12"},
        {"transaction":\(transaction(2, merchant: "Migros Jet", amount: "1850.0", category: "GROCERY")),"reason":"Essential","installment":null}]},
      "nonRoutine":{"total":3750.0,"count":2,"items":[
        {"transaction":\(transaction(3, merchant: "LC Waikiki", amount: "2300.0")),"eligible":true},
        {"transaction":\(transaction(4, merchant: "D&R", amount: "1450.0", category: "ENTERTAINMENT")),"eligible":true}],
        "investment":\(investment),
        "advice":[{"merchant":"Trendyol","count":4,"amount":10840.0,"usualMonthly":2254.92,"excess":8585.08,"message":"Trendyol için…"}]}}]
    """
    return """
    {"period":{"kind":"Custom","startUtc":"2026-09-04T21:00:00+00:00","endUtc":"2026-09-05T21:00:00+00:00","startDate":"2026-09-05",
    "endDate":"2026-09-05"},"periodTooLong":\(tooLong),"merchantMatch":null,"currencies":\(currencies)}
    """
}

private func linkJSON(period: String = "Custom", from: String? = "2026-09-05", to: String? = "2026-09-05", kind: String = "Purchase",
                      category: String? = nil, extra: String = "") -> String {
    func text(_ value: String?) -> String { value.map { "\"\($0)\"" } ?? "null" }
    return """
    {"title":"İşlemleri gör","query":{"period":"\(period)","from":\(text(from)),"to":\(text(to)),"kind":"\(kind)",
    "accountType":"Personal","categoryCode":\(text(category)),"merchant":null\(extra)}}
    """
}

@MainActor private func link(_ text: String) throws -> AssistantLink {
    try JSONDecoder().decode(AssistantLink.self, from: Data(text.utf8))
}

@MainActor
struct BreakdownTests {
    // MARK: Reading the answer

    @Test func theBreakdownKeepsTheBackendsGroupsAndFigures() throws {
        let answer = try JSONDecoder().decode(SpendingBreakdown.self, from: Data(breakdown().utf8))
        let lira = try #require(answer.currencies.first)

        #expect((lira.routine.total, lira.routine.count, lira.nonRoutine.total, lira.nonRoutine.count) == (7972.5, 2, 3750, 2))
        #expect(lira.routine.items.map(\.reason) == [.installment, .essential])
        #expect(lira.nonRoutine.investment?.assets.map(\.value) == [3914.9, 3313.52])
        #expect(lira.nonRoutine.advice.map(\.message) == ["Trendyol için…"])
    }

    @Test func anUnknownReasonIsAnErrorNotAGuess() {
        #expect(throws: DecodingError.self) {
            try JSONDecoder().decode(SpendingBreakdown.self, from: Data(breakdown(reason: "Sometimes").utf8))
        }
    }

    @Test(arguments: [
        (RoutineReason.subscription, nil as String?, "Abonelik"), (.bill, nil, "Fatura"), (.installment, "04/12", "Taksit 4/12"),
        (.recurring, nil, "Düzenli ödeme"), (.essential, nil, "Temel"),
    ])
    func everyRoutineReasonHasItsLabel(reason: RoutineReason, installment: String?, label: String) {
        #expect(BreakdownDisplay.label(reason, installment: installment) == label)
    }

    @Test func theAssetLinesAreTheBackendsFiguresWritten() throws {
        let answer = try JSONDecoder().decode(BreakdownInvestment.self, from: Data(investment.utf8))

        #expect(answer.assets.map(BreakdownDisplay.assetName) == ["Dolar", "Gram altın"])
        #expect(answer.assets.map(BreakdownDisplay.valueText) == ["3.914,90 TL", "3.313,52 TL"])
        #expect(answer.assets.map(BreakdownDisplay.differenceText) == ["+164,90 TL (%4,40)", "−436,48 TL (%−11,64)"])
        #expect(BreakdownDisplay.excludedText(answer) == "Son 7 günün 120,50 TL'si dahil değil.")
    }

    // MARK: Which links are broken down

    @Test func aSpendingLinkIsBrokenDownAndOthersAreNot() throws {
        let app = TestApp { _ in json(200, "{}") }
        let chat = ChatViewModel(api: app.api)

        #expect(chat.breakdown(for: try link(linkJSON())) != nil)
        #expect(chat.breakdown(for: try link(linkJSON(kind: "Transfer"))) == nil)
        let limited = #","direction":null,"counterparty":null,"minAmount":2000,"maxAmount":null,"currencyCode":"TRY""#
        #expect(chat.breakdown(for: try link(linkJSON(extra: limited))) == nil)
    }

    @Test func theBreakdownAsksTheLinksDaysAndCategoryOnly() async throws {
        let app = TestApp { _ in json(200, breakdown()) }
        let model = try #require(ChatViewModel(api: app.api).breakdown(for: try link(linkJSON(category: "CLOTHING"))))

        await model.load()

        #expect(app.requests.map(\.path) == ["/api/transactions/breakdown"])
        #expect(app.requests.map(\.query) == ["from=2026-09-05&to=2026-09-05&categoryCode=CLOTHING"])
        #expect(app.requests.first?.authorization == "Bearer token-abc")
    }

    @Test func aNamedPeriodIsSentByName() async throws {
        let app = TestApp { _ in json(200, breakdown()) }
        let model = try #require(ChatViewModel(api: app.api).breakdown(for: try link(linkJSON(period: "LastMonth", from: nil, to: nil))))

        await model.load()

        #expect(app.requests.map(\.query) == ["period=LastMonth"])
    }

    // MARK: The screen's states

    @Test func aLoadedBreakdownIsShown() async throws {
        let app = TestApp { _ in json(200, breakdown()) }
        let model = try #require(ChatViewModel(api: app.api).breakdown(for: try link(linkJSON())))

        await model.load()

        guard case .loaded(let currencies) = model.state else { Issue.record("\(model.state)"); return }
        #expect(currencies.map(\.currencyCode) == ["TRY"])
        #expect(model.periodText == "5 Eylül 2026")
        #expect(!model.showsPlainList)
    }

    @Test func aLongPeriodFallsBackToThePlainList() async throws {
        let app = TestApp { _ in json(200, breakdown(tooLong: true)) }
        let model = try #require(ChatViewModel(api: app.api).breakdown(for: try link(linkJSON())))

        await model.load()

        #expect(model.showsPlainList)
    }

    @Test func aFailedBreakdownSaysSoAndCanBeTriedAgain() async throws {
        let failure = #"{"title":"Beklenmeyen bir hata oluştu.","status":500,"code":"internal_error"}"#
        let success = breakdown()
        let answers = Answers([{ json(500, failure) }, { json(200, success) }])
        let app = TestApp(answers.next)
        let model = try #require(ChatViewModel(api: app.api).breakdown(for: try link(linkJSON())))

        await model.load()
        guard case .failed = model.state else { Issue.record("\(model.state)"); return }
        await model.load()

        guard case .loaded = model.state else { Issue.record("\(model.state)"); return }
    }

    // MARK: A transaction's insight

    @Test func aNonRoutinePurchaseShowsItsComparison() async throws {
        let body = #"{"transaction":\#(transaction(3, merchant: "LC Waikiki", amount: "2300.0")),"routine":null,"installment":null,"group":"Discretionary","investment":\#(investment),"note":null}"#
        let app = TestApp { _ in json(200, body) }
        let model = TransactionInsightViewModel(api: app.api, id: UUID(uuidString: "30000000-0000-0000-0000-000000000003")!)

        await model.load()

        #expect(app.requests.map(\.path) == ["/api/transactions/30000000-0000-0000-0000-000000000003/insight"])
        guard case .loaded(let insight) = model.state else { Issue.record("\(model.state)"); return }
        #expect(insight.investment?.assets.count == 2)
        #expect(insight.group == .discretionary)
    }

    @Test func aRoutinePaymentShowsItsReasonInstead() async throws {
        let body = #"{"transaction":\#(transaction(1, merchant: "Apple Store", amount: "4250.0", category: "OTHER")),"routine":"Installment","installment":"04/12","group":"Unknown","investment":null,"note":"Rutin ödeme"}"#
        let app = TestApp { _ in json(200, body) }
        let model = TransactionInsightViewModel(api: app.api, id: UUID())

        await model.load()

        guard case .loaded(let insight) = model.state else { Issue.record("\(model.state)"); return }
        #expect((insight.investment, insight.note, insight.routine) == (nil, "Rutin ödeme", .installment))
        #expect(BreakdownDisplay.insightTag(insight) == "Taksit 4/12")
    }
}

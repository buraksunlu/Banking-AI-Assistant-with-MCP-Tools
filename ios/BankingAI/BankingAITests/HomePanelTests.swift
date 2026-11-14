import Foundation
import Testing
@testable import BankingAI

// UX-01 PR2: the home screen's "bu ay" panel — three cards from three REST answers, no model. Figures are the backend's at the
// demo moment 10 October 2026, checked by hand and with SQL (todo UX-01 PR2): the phone writes them and draws bars, never adds,
// subtracts or rounds.

// Murat, MonthToDate: 1–10 October 8.241,40 against 1–10 September 8.876,40 → −635,00, −7,15 % (635 / 8.876,40 = 7,1537).
private let muratPace = """
{
  "current":  { "kind": "Custom", "startDate": "2026-10-01", "endDate": "2026-10-10" },
  "previous": { "kind": "Custom", "startDate": "2026-09-01", "endDate": "2026-09-10" },
  "currencies": [ { "currencyCode": "TRY", "currentAmount": 8241.40, "currentCount": 4, "previousAmount": 8876.40,
                    "previousCount": 5, "change": -635.00, "percentageChange": -7.15, "categories": [] } ]
}
"""

// Zeynep: TRY 5.118,50 / 5.507,50 (−389,00, −7,06 %) and USD 22,99 / 22,99 (0, 0 %): two lines, never one total.
private let zeynepPace = """
{
  "current":  { "kind": "Custom", "startDate": "2026-10-01", "endDate": "2026-10-10" },
  "previous": { "kind": "Custom", "startDate": "2026-09-01", "endDate": "2026-09-10" },
  "currencies": [
    { "currencyCode": "TRY", "currentAmount": 5118.50, "currentCount": 6, "previousAmount": 5507.50, "previousCount": 7,
      "change": -389.00, "percentageChange": -7.06, "categories": [] },
    { "currencyCode": "USD", "currentAmount": 22.99, "currentCount": 1, "previousAmount": 22.99, "previousCount": 1,
      "change": 0.00, "percentageChange": 0.00, "categories": [] } ]
}
"""

// Ada: 735,00 against nothing; the backend gives no percentage for growth from zero.
private let adaPace = """
{
  "current":  { "kind": "Custom", "startDate": "2026-10-01", "endDate": "2026-10-10" },
  "previous": { "kind": "Custom", "startDate": "2026-09-01", "endDate": "2026-09-10" },
  "currencies": [ { "currencyCode": "TRY", "currentAmount": 735.00, "currentCount": 5, "previousAmount": 0.00,
                    "previousCount": 0, "change": 735.00, "percentageChange": null, "categories": [] } ]
}
"""

// Hasan: nothing yet this month, 1.797,50 in the same days of September; the row comes from the union of both periods.
private let hasanPace = """
{
  "current":  { "kind": "Custom", "startDate": "2026-10-01", "endDate": "2026-10-10" },
  "previous": { "kind": "Custom", "startDate": "2026-09-01", "endDate": "2026-09-10" },
  "currencies": [ { "currencyCode": "TRY", "currentAmount": 0.00, "currentCount": 0, "previousAmount": 1797.50,
                    "previousCount": 3, "change": -1797.50, "percentageChange": -100.00, "categories": [] } ]
}
"""

private let noPace = """
{ "current":  { "kind": "Custom", "startDate": "2026-10-01", "endDate": "2026-10-10" },
  "previous": { "kind": "Custom", "startDate": "2026-09-01", "endDate": "2026-09-10" }, "currencies": [] }
"""

// Murat, ThisMonth by category: Market 5.133,00 · Akaryakıt 2.658,50 · Fatura 449,90 (sum 8.241,40), plus a made-up fourth
// slice to show that the card stops at three.
private let muratCategories = """
{
  "period": { "kind": "ThisMonth", "startDate": "2026-10-01", "endDate": "2026-10-10" },
  "currencies": [ { "currencyCode": "TRY", "totalAmount": 8341.40, "categories": [
    { "categoryCode": "GROCERY", "displayName": "Market", "amount": 5133.00, "transactionCount": 2, "percentage": 61.54 },
    { "categoryCode": "FUEL", "displayName": "Akaryakıt", "amount": 2658.50, "transactionCount": 1, "percentage": 31.87 },
    { "categoryCode": "BILLS", "displayName": "Fatura", "amount": 449.90, "transactionCount": 1, "percentage": 5.39 },
    { "categoryCode": "FOOD", "displayName": "Yeme & İçme", "amount": 100.00, "transactionCount": 1, "percentage": 1.20 } ] } ]
}
"""

private let muratAdvice = """
{
  "period": { "kind": "LastMonth", "startDate": "2026-09-01", "endDate": "2026-09-30" },
  "currencies": [ {
    "currencyCode": "TRY", "totalSpent": 53388.39, "incomeBase": 85000.00, "spentShare": 62.81,
    "groups": [ { "group": "Essential", "amount": 52515.90, "incomeShare": 61.78 },
                { "group": "Discretionary", "amount": 872.49, "incomeShare": 1.03 },
                { "group": "Unknown", "amount": 0.00, "incomeShare": 0.00 } ],
    "advice": [], "reason": "NoRuleMatched" } ]
}
"""

private let canAdvice = """
{
  "period": { "kind": "LastMonth", "startDate": "2026-09-01", "endDate": "2026-09-30" },
  "currencies": [ {
    "currencyCode": "TRY", "totalSpent": 15037.99, "incomeBase": null, "spentShare": null,
    "groups": [ { "group": "Essential", "amount": 12000.00, "incomeShare": null },
                { "group": "Discretionary", "amount": 3037.99, "incomeShare": null },
                { "group": "Unknown", "amount": 0.00, "incomeShare": null } ],
    "advice": [], "reason": "NoIncome" } ]
}
"""

private func paces(_ state: ListState<CurrencyComparison>) -> [CurrencyComparison] {
    if case .loaded(let currencies) = state { return currencies }
    return []
}

private func comparison(_ body: String) throws -> CurrencyComparison {
    try #require(try JSONDecoder().decode(PeriodComparison.self, from: Data(body.utf8)).currencies.first)
}

private func advice(_ body: String) throws -> CurrencyAdvice {
    try #require(try JSONDecoder().decode(SpendingAdvice.self, from: Data(body.utf8)).currencies.first)
}

// Card 1: "Bu ay harcama · N gün" from `period-comparison?comparison=MonthToDate` (K7-A: it replaced the spending summary).
@MainActor
struct MonthPaceViewModelTests {
    @Test func theComparisonIsAskedForWithTheTokenAndTheMonthToDateName() async {
        let app = TestApp { _ in json(200, muratPace) }

        await MonthPaceViewModel(api: app.api).load()

        #expect(app.requests.map(\.method) == ["GET"])
        #expect(app.requests.map(\.path) == ["/api/analytics/period-comparison"])
        #expect(app.requests.map(\.query) == ["comparison=MonthToDate"])
        #expect(app.requests.first?.authorization == "Bearer token-abc")
    }

    @Test func muratsMonthKeepsTheBackendsFiguresAndDay() async {
        let app = TestApp { _ in json(200, muratPace) }
        let model = MonthPaceViewModel(api: app.api)

        await model.load()

        let rows = paces(model.state)
        #expect(rows.map(\.currencyCode) == ["TRY"])
        #expect(rows.map(\.currentAmount) == [Decimal(string: "8241.40")!])
        #expect(rows.map(\.change) == [Decimal(string: "-635.00")!])
        #expect(rows.map(\.percentageChange) == [Decimal(string: "-7.15")!])
        #expect(rows.map(\.currentCount) == [4])
        #expect(model.endDate == "2026-10-10")
    }

    @Test func twoCurrenciesStayTwoLines() async {
        let app = TestApp { _ in json(200, zeynepPace) }
        let model = MonthPaceViewModel(api: app.api)

        await model.load()

        let rows = paces(model.state)
        #expect(rows.map(\.currencyCode) == ["TRY", "USD"])
        #expect(rows.map { Money.text($0.currentAmount, currency: $0.currencyCode) } == ["5.118,50 TL", "22,99 USD"])
    }

    @Test func noRowIsTheEmptyStateAndTheDayIsStillKnown() async {
        let app = TestApp { _ in json(200, noPace) }
        let model = MonthPaceViewModel(api: app.api)

        await model.load()

        #expect(model.state == .empty)
        #expect(model.endDate == "2026-10-10")
    }

    @Test func aFirstLoadThatFailsShowsTheMessage() async {
        let app = TestApp { _ in problem(500, "internal_error", "Beklenmeyen bir hata oluştu.") }
        let model = MonthPaceViewModel(api: app.api)

        await model.load()

        #expect(model.state == .failed("Beklenmeyen bir hata oluştu."))
        #expect(model.endDate == nil)
    }

    @Test func aFailedRefreshKeepsTheFiguresAndTheDay() async {
        let answers = Answers([{ json(200, muratPace) }, { throw URLError(.notConnectedToInternet) }])
        let app = TestApp { try answers.next($0) }
        let model = MonthPaceViewModel(api: app.api)

        await model.load()
        await model.load()

        #expect(paces(model.state).map(\.currentAmount) == [Decimal(string: "8241.40")!])
        #expect(model.endDate == "2026-10-10")
        #expect(model.refreshError == "Sunucuya ulaşılamıyor; bağlantıyı kontrol edip tekrar deneyin.")
    }

    @Test func anExpiredSessionShowsNoErrorOnTheCard() async {
        let app = TestApp { _ in problem(401, "unauthorized", "Oturum geçersiz.") }
        let model = MonthPaceViewModel(api: app.api)

        await model.load()

        #expect(model.refreshError == nil)
        #expect(model.state == .loading)
    }
}

// The words of the panel (K5-C): the backend's change and percentage, the direction word chosen from the sign only.
struct HomePanelDisplayTests {
    @Test func lessThanTheSameDaysWritesTheAmountAndThePercentage() throws {
        #expect(HomeDisplay.paceLine(try comparison(muratPace)) == "Aynı günlerden 635,00 TL az (%7,15)")
    }

    @Test func moreThanTheSameDaysSaysMore() {
        let elif = CurrencyComparison(currencyCode: "EUR", currentAmount: Decimal(string: "1555.03")!, currentCount: 9,
                                      previousAmount: Decimal(string: "1471.11")!, change: Decimal(string: "83.92")!,
                                      percentageChange: Decimal(string: "5.70")!)
        #expect(HomeDisplay.paceLine(elif) == "Aynı günlerden 83,92 EUR fazla (%5,70)")
    }

    @Test func noChangeSaysTheSame() throws {
        let decoded = try JSONDecoder().decode(PeriodComparison.self, from: Data(zeynepPace.utf8))
        #expect(HomeDisplay.paceLine(decoded.currencies[1]) == "Aynı günlerle aynı")
    }

    // A missing percentage is never written as "%0" (growth from zero has no rate).
    @Test func noPercentageSaysTheSameDaysHadNoSpending() throws {
        #expect(HomeDisplay.paceLine(try comparison(adaPace)) == "Geçen ayın aynı günlerinde harcama yok")
    }

    @Test func nothingYetThisMonthIsSaidInWords() throws {
        let hasan = try comparison(hasanPace)
        #expect(HomeDisplay.paceHeadline(hasan) == "Henüz harcama yok")
        #expect(HomeDisplay.paceLine(hasan) == "Aynı günlerden 1.797,50 TL az (%100,00)")
        #expect(HomeDisplay.paceHeadline(try comparison(muratPace)) == "8.241,40 TL")
    }

    @Test func theCardTitleCountsTheBackendsDays() {
        #expect(HomeDisplay.paceTitle("2026-10-10") == "Bu ay harcama · 10 gün")
        #expect(HomeDisplay.paceTitle("2026-10-01") == "Bu ay harcama · 1 gün")
        #expect(HomeDisplay.paceTitle(nil) == "Bu ay harcama")
        #expect(HomeDisplay.paceTitle("10/10/2026") == "Bu ay harcama")
    }

    // K2-A: at most three categories, in the backend's order, each bar as long as the backend's percentage.
    @Test func whereItWentShowsTheFirstThreeInTheBackendsOrder() throws {
        let breakdown = try JSONDecoder().decode(CategoryBreakdown.self, from: Data(muratCategories.utf8))
        let rows = HomeDisplay.topCategories(try #require(breakdown.currencies.first))
        #expect(rows.map(\.displayName) == ["Market", "Akaryakıt", "Fatura"])
        #expect(rows.map { Money.text($0.amount, currency: "TRY") } == ["5.133,00 TL", "2.658,50 TL", "449,90 TL"])
        #expect(HomeDisplay.barFraction(rows[0].percentage) == 0.6154)
        #expect(HomeDisplay.barFraction(Decimal(150)) == 1)
        #expect(HomeDisplay.barFraction(Decimal(-5)) == 0)
    }

    // K3-D / K4-B: the short income card writes the backend's share; without salary it says why and offers no advice.
    @Test func theIncomeGlanceWritesTheShareOrTheReason() throws {
        let murat = try advice(muratAdvice)
        #expect(HomeDisplay.incomeGlance(murat) == "%62,81")
        #expect(HomeDisplay.offersAdvice(murat))
        let can = try advice(canAdvice)
        #expect(HomeDisplay.incomeGlance(can) == "Maaş geliri görünmediği için gelir payı hesaplanamıyor.")
        #expect(!HomeDisplay.offersAdvice(can))
    }

    @Test func theIncomeTitleNamesTheBackendsMonth() {
        #expect(HomeDisplay.incomeTitle("Eylül 2026") == "Gelir payı · Eylül")
        #expect(HomeDisplay.incomeTitle(nil) == "Gelir payı")
    }
}

// The panel's three cards do not depend on each other nor on the assistant (review focus 5).
@MainActor
struct HomePanelIndependenceTests {
    private static func server(failing failedPath: String) -> TestApp {
        TestApp { request in
            let path = request.url?.path() ?? ""
            if path == failedPath { return problem(500, "internal_error", "Beklenmeyen bir hata oluştu.") }
            switch path {
            case "/api/analytics/period-comparison": return json(200, muratPace)
            case "/api/analytics/category-breakdown": return json(200, muratCategories)
            case "/api/analytics/spending-advice": return json(200, muratAdvice)
            default: return problem(503, "assistant_unavailable", "Kılavuz şu anda kullanılamıyor.")
            }
        }
    }

    @Test(arguments: ["/api/analytics/period-comparison", "/api/analytics/category-breakdown",
                      "/api/analytics/spending-advice", "/api/assistant/messages"])
    func oneFailingAnswerLeavesTheOtherCardsFull(failedPath: String) async {
        let app = Self.server(failing: failedPath)
        let pace = MonthPaceViewModel(api: app.api)
        let categories = CategoryBreakdownViewModel(api: app.api)
        let income = IncomeShareViewModel(api: app.api)

        async let paceLoaded: Void = pace.load()
        async let categoriesLoaded: Void = categories.load()
        async let incomeLoaded: Void = income.load()
        _ = await (paceLoaded, categoriesLoaded, incomeLoaded)

        let message = "Beklenmeyen bir hata oluştu."
        // Exactly the card whose answer failed shows the error; the other two are full.
        #expect((pace.state == .failed(message)) == failedPath.hasSuffix("period-comparison"))
        #expect((categories.state == .failed(message)) == failedPath.hasSuffix("category-breakdown"))
        #expect((income.state == .failed(message)) == failedPath.hasSuffix("spending-advice"))
        if !failedPath.hasSuffix("period-comparison") { #expect(paces(pace.state).count == 1) }
        if !failedPath.hasSuffix("category-breakdown") {
            if case .loaded = categories.state {} else { Issue.record("the category card is not full") }
        }
        if !failedPath.hasSuffix("spending-advice") {
            if case .loaded = income.state {} else { Issue.record("the income card is not full") }
        }
        // The panel never asks the assistant: it stays full when the model is down.
        #expect(!app.requests.contains { $0.path.hasPrefix("/api/assistant") })
        #expect(app.requests.count == 3)
    }

    @Test func thePanelAsksForThisMonthsCategories() async {
        let app = Self.server(failing: "")
        await CategoryBreakdownViewModel(api: app.api).load()
        #expect(app.requests.map(\.query) == ["period=ThisMonth"])
    }
}

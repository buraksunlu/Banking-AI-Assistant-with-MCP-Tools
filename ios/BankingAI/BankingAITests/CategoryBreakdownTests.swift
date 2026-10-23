import Foundation
import Testing
@testable import BankingAI

// Ada, ThisMonth, demo moment 10 October 2026 (docs/api.md): 735,00 TRY, FOOD 590,00 (80,27 %), unclassified 145,00 (19,73 %).
private let adaThisMonth = """
{
  "period": { "kind": "ThisMonth", "startDate": "2026-10-01", "endDate": "2026-10-10" },
  "currencies": [
    { "currencyCode": "TRY", "totalAmount": 735.00, "categories": [
      { "categoryCode": "FOOD", "displayName": "Yeme & İçme", "amount": 590.00, "transactionCount": 4, "percentage": 80.27 },
      { "categoryCode": null, "displayName": "Sınıflandırılmamış", "amount": 145.00, "transactionCount": 1, "percentage": 19.73 }
    ] }
  ]
}
"""

private let adaLastMonth = """
{
  "period": { "kind": "LastMonth", "startDate": "2026-09-01", "endDate": "2026-09-30" },
  "currencies": [
    { "currencyCode": "TRY", "totalAmount": 624.98, "categories": [
      { "categoryCode": "GROCERY", "displayName": "Market", "amount": 624.98, "transactionCount": 3, "percentage": 100.00 }
    ] }
  ]
}
"""

// Selin lives in two currencies: they stay two charts and are never added together.
private let twoCurrencies = """
{
  "period": { "kind": "ThisMonth" },
  "currencies": [
    { "currencyCode": "TRY", "totalAmount": 1000.00, "categories": [
      { "categoryCode": "FOOD", "displayName": "Yeme & İçme", "amount": 1000.00, "transactionCount": 2, "percentage": 100.00 } ] },
    { "currencyCode": "EUR", "totalAmount": 50.00, "categories": [
      { "categoryCode": "TRANSPORTATION", "displayName": "Ulaşım", "amount": 50.00, "transactionCount": 1, "percentage": 100.00 } ] }
  ]
}
"""

private let noSpending = #"{ "period": { "kind": "ThisMonth" }, "currencies": [] }"#
private let unreachableMessage = "Sunucuya ulaşılamıyor; bağlantıyı kontrol edip tekrar deneyin."

private func loaded(_ state: CategoryBreakdownViewModel.State) -> [CurrencyCategoryBreakdown] {
    if case .loaded(let currencies) = state { return currencies }
    return []
}

/// Answers by the `period` the app asked for; `slow` waits first so an older request can finish after a newer one.
private func byPeriod(slow: String? = nil, _ answers: [String: String]) -> MockURLProtocol.Handler {
    { request in
        let items = URLComponents(url: request.url!, resolvingAgainstBaseURL: false)?.queryItems ?? []
        let period = items.first { $0.name == "period" }?.value ?? ""
        if period == slow { Thread.sleep(forTimeInterval: 0.4) }
        return json(200, answers[period] ?? noSpending)
    }
}

// IOS-08: the category ring's data, its period choice and what happens when a request goes wrong.
@MainActor
struct CategoryBreakdownViewModelTests {
    @Test func adasMonthKeepsTheBackendsFiguresAndOrder() async {
        let app = TestApp { _ in json(200, adaThisMonth) }
        let model = CategoryBreakdownViewModel(api: app.api)

        await model.load()

        let currencies = loaded(model.state)
        #expect(currencies.map(\.currencyCode) == ["TRY"])
        #expect(currencies.first?.totalAmount == Decimal(string: "735.00")!)
        let slices = currencies.first?.categories ?? []
        #expect(slices.map(\.displayName) == ["Yeme & İçme", "Sınıflandırılmamış"])
        #expect(slices.map(\.amount) == [Decimal(string: "590.00")!, Decimal(string: "145.00")!])
        // The percentages are the backend's, digit for digit; the phone calculates nothing.
        #expect(slices.map(\.percentage) == [Decimal(string: "80.27")!, Decimal(string: "19.73")!])
        #expect(slices.map(\.transactionCount) == [4, 1])
        #expect(model.refreshError == nil)
    }

    @Test func theUnclassifiedSliceStaysItsOwnSliceWithoutACode() async {
        let app = TestApp { _ in json(200, adaThisMonth) }
        let model = CategoryBreakdownViewModel(api: app.api)

        await model.load()

        let slices = loaded(model.state).first?.categories ?? []
        #expect(slices.map(\.categoryCode) == ["FOOD", nil])
        #expect(slices.map(\.id) == ["FOOD", AnalyticsDisplay.uncategorizedID])
    }

    @Test func theRingIsAskedForWithTheTokenAndTheThisMonthName() async {
        let app = TestApp { _ in json(200, adaThisMonth) }

        await CategoryBreakdownViewModel(api: app.api).load()

        #expect(app.requests.map(\.method) == ["GET"])
        #expect(app.requests.map(\.path) == ["/api/analytics/category-breakdown"])
        #expect(app.requests.map(\.query) == ["period=ThisMonth"])
        #expect(app.requests.first?.authorization == "Bearer token-abc")
    }

    @Test func noSpendingIsTheEmptyState() async {
        let app = TestApp { _ in json(200, noSpending) }
        let model = CategoryBreakdownViewModel(api: app.api)

        await model.load()

        #expect(model.state == .empty)
    }

    @Test func currenciesStayApartAndNothingIsAddedAcrossThem() async {
        let app = TestApp { _ in json(200, twoCurrencies) }
        let model = CategoryBreakdownViewModel(api: app.api)

        await model.load()

        let currencies = loaded(model.state)
        #expect(currencies.map(\.currencyCode) == ["TRY", "EUR"])
        #expect(currencies.map(\.totalAmount) == [Decimal(string: "1000.00")!, Decimal(string: "50.00")!])
    }

    @Test func aMissingFieldIsAContractMismatchNotAHalfDrawnRing() async {
        let broken = adaThisMonth.replacingOccurrences(of: #", "percentage": 80.27"#, with: "")
        let app = TestApp { _ in json(200, broken) }
        let model = CategoryBreakdownViewModel(api: app.api)

        await model.load()

        #expect(model.state == .failed("Beklenmeyen bir cevap alındı; tekrar deneyin."))
    }

    @Test func aFirstLoadThatFailsShowsTheMessageAndTheSelectedPeriodStays() async {
        let app = TestApp { _ in problem(500, "internal_error", "Beklenmeyen bir hata oluştu.") }
        let model = CategoryBreakdownViewModel(api: app.api)

        await model.select(.lastMonth)

        #expect(model.state == .failed("Beklenmeyen bir hata oluştu."))
        #expect(model.period == .lastMonth)
    }

    @Test func choosingAnotherPeriodAsksForItAndShowsItsFigures() async {
        let app = TestApp(byPeriod(["ThisMonth": adaThisMonth, "LastMonth": adaLastMonth]))
        let model = CategoryBreakdownViewModel(api: app.api)
        await model.load()

        await model.select(.lastMonth)

        #expect(model.period == .lastMonth)
        #expect(loaded(model.state).first?.totalAmount == Decimal(string: "624.98")!)
        #expect(app.requests.map(\.query) == ["period=ThisMonth", "period=LastMonth"])
    }

    @Test func eachPeriodGoesToTheAPIUnderItsOwnName() async {
        let app = TestApp(byPeriod([:]))
        let model = CategoryBreakdownViewModel(api: app.api)

        for period in AnalyticsPeriod.allCases { await model.select(period) }

        #expect(app.requests.map(\.query) == ["period=ThisMonth", "period=LastMonth", "period=Last3Months", "period=ThisYear"])
        #expect(AnalyticsPeriod.allCases.map(\.title) == ["Bu ay", "Geçen ay", "Son 3 ay", "Bu yıl"])
    }

    @Test func aFailedRefreshKeepsTheOldRingAndTheOldPeriod() async {
        let answers = Answers([{ json(200, adaThisMonth) }, { problem(500, "internal_error", "Beklenmeyen bir hata oluştu.") }])
        let app = TestApp { try answers.next($0) }
        let model = CategoryBreakdownViewModel(api: app.api)
        await model.load()

        await model.select(.lastMonth)

        #expect(loaded(model.state).first?.totalAmount == Decimal(string: "735.00")!)
        // The chips must say what the ring shows, so the choice snaps back.
        #expect(model.period == .thisMonth)
        #expect(model.refreshError == "Beklenmeyen bir hata oluştu.")
    }

    @Test func aSuccessfulLoadClearsTheRefreshError() async {
        let answers = Answers([{ json(200, adaThisMonth) }, { problem(500, "internal_error", "Beklenmeyen bir hata oluştu.") },
                               { json(200, adaThisMonth) }])
        let app = TestApp { try answers.next($0) }
        let model = CategoryBreakdownViewModel(api: app.api)
        await model.load()
        await model.load()
        #expect(model.refreshError != nil)

        await model.load()

        #expect(model.refreshError == nil)
    }

    @Test func anOlderAnswerArrivingLateNeverReplacesTheNewerChoice() async {
        let app = TestApp(byPeriod(slow: "ThisMonth", ["ThisMonth": adaThisMonth, "LastMonth": adaLastMonth]))
        let model = CategoryBreakdownViewModel(api: app.api)
        let older = Task { await model.load() }
        try? await Task.sleep(for: .milliseconds(100))

        await model.select(.lastMonth)
        await older.value

        #expect(model.period == .lastMonth)
        #expect(loaded(model.state).first?.totalAmount == Decimal(string: "624.98")!)
    }

    @Test func anExpiredSessionShowsNoErrorOnTheRing() async {
        let app = TestApp { _ in problem(401, "session_invalid", "Oturum geçersiz.") }
        let model = CategoryBreakdownViewModel(api: app.api)

        await model.load()

        #expect(model.state == .loading)
        #expect(model.refreshError == nil)
        #expect(!app.session.isSignedIn)
    }

    @Test func noConnectionIsTheUnreachableMessage() async {
        let app = TestApp { _ in throw URLError(.notConnectedToInternet) }
        let model = CategoryBreakdownViewModel(api: app.api)

        await model.load()

        #expect(model.state == .failed(unreachableMessage))
    }

    @Test func tappingASliceSelectsItAndTappingItAgainClearsIt() async {
        let app = TestApp { _ in json(200, adaThisMonth) }
        let model = CategoryBreakdownViewModel(api: app.api)
        await model.load()
        let food = loaded(model.state)[0].categories[0]

        model.toggle(food, currency: "TRY")
        #expect(model.selectedID(currency: "TRY") == "FOOD")

        model.toggle(food, currency: "TRY")
        #expect(model.selectedID(currency: "TRY") == nil)
    }

    @Test func aSelectionIsKeptPerCurrency() async {
        let app = TestApp { _ in json(200, twoCurrencies) }
        let model = CategoryBreakdownViewModel(api: app.api)
        await model.load()
        let currencies = loaded(model.state)

        model.toggle(currencies[0].categories[0], currency: "TRY")
        model.toggle(currencies[1].categories[0], currency: "EUR")

        #expect(model.selectedID(currency: "TRY") == "FOOD")
        #expect(model.selectedID(currency: "EUR") == "TRANSPORTATION")
    }

    @Test func aNewPeriodClearsTheSelection() async {
        let app = TestApp(byPeriod(["ThisMonth": adaThisMonth, "LastMonth": adaLastMonth]))
        let model = CategoryBreakdownViewModel(api: app.api)
        await model.load()
        model.toggle(loaded(model.state)[0].categories[0], currency: "TRY")

        await model.select(.lastMonth)

        #expect(model.selectedID(currency: "TRY") == nil)
    }
}

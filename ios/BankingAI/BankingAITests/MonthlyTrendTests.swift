import Foundation
import Testing
@testable import BankingAI

private func month(_ year: Int, _ month: Int, _ amount: String, count: Int = 1, partial: Bool = false) -> String {
    #"{ "year": \#(year), "month": \#(month), "isPartial": \#(partial), "amount": \#(amount), "transactionCount": \#(count) }"#
}

// Deniz, months=6, demo moment 10 October 2026 (docs/api.md): April to September complete, October still running.
private let denizSixMonths = """
{
  "months": [],
  "merchantMatch": null,
  "currencies": [
    { "currencyCode": "TRY", "months": [
      \(month(2026, 4, "12000.00")), \(month(2026, 5, "14000.00")), \(month(2026, 6, "16000.00")), \(month(2026, 7, "18000.00")),
      \(month(2026, 8, "24000.00")), \(month(2026, 9, "31000.00")), \(month(2026, 10, "11350.00", partial: true))
    ] }
  ]
}
"""

// A quiet month arrives as 0, it is not left out.
private let withQuietMonth = """
{ "months": [], "merchantMatch": null, "currencies": [
  { "currencyCode": "TRY", "months": [
    \(month(2026, 4, "1200.00")), \(month(2026, 5, "0.00", count: 0)), \(month(2026, 6, "700.00"))
  ] } ] }
"""

private let twoCurrencies = """
{ "months": [], "merchantMatch": null, "currencies": [
  { "currencyCode": "TRY", "months": [ \(month(2026, 9, "1000.00")), \(month(2026, 10, "500.00", partial: true)) ] },
  { "currencyCode": "EUR", "months": [ \(month(2026, 9, "40.00")), \(month(2026, 10, "10.00", partial: true)) ] }
] }
"""

private let noSpending = #"{ "months": [], "merchantMatch": null, "currencies": [] }"#
private let unreachableMessage = "Sunucuya ulaşılamıyor; bağlantıyı kontrol edip tekrar deneyin."

private func loaded(_ state: MonthlyTrendViewModel.State) -> [CurrencyTrend] {
    if case .loaded(let currencies) = state { return currencies }
    return []
}

// IOS-08 PR2: the monthly chart's data and what happens when a request goes wrong.
@MainActor
struct MonthlyTrendViewModelTests {
    @Test func denizsSixMonthsKeepTheBackendsOrderAndExactAmounts() async {
        let app = TestApp { _ in json(200, denizSixMonths) }
        let model = MonthlyTrendViewModel(api: app.api)

        await model.load()

        let months = loaded(model.state).first?.months ?? []
        #expect(months.count == 7)
        #expect(months.map(\.month) == [4, 5, 6, 7, 8, 9, 10])
        #expect(months.map(\.amount) == ["12000.00", "14000.00", "16000.00", "18000.00", "24000.00", "31000.00", "11350.00"]
            .map { Decimal(string: $0)! })
        #expect(months.map(\.id) == ["2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10"])
        #expect(model.refreshError == nil)
    }

    @Test func onlyTheMonthStillRunningIsMarkedPartial() async {
        let app = TestApp { _ in json(200, denizSixMonths) }
        let model = MonthlyTrendViewModel(api: app.api)

        await model.load()

        let months = loaded(model.state).first?.months ?? []
        #expect(months.map(\.isPartial) == [false, false, false, false, false, false, true])
    }

    @Test func theTrendIsAskedForSixMonthsWithTheToken() async {
        let app = TestApp { _ in json(200, denizSixMonths) }

        await MonthlyTrendViewModel(api: app.api).load()

        #expect(app.requests.map(\.method) == ["GET"])
        #expect(app.requests.map(\.path) == ["/api/analytics/monthly-trend"])
        #expect(app.requests.map(\.query) == ["months=6"])
        #expect(app.requests.first?.authorization == "Bearer token-abc")
    }

    @Test func aQuietMonthStaysInTheListWithZero() async {
        let app = TestApp { _ in json(200, withQuietMonth) }
        let model = MonthlyTrendViewModel(api: app.api)

        await model.load()

        let months = loaded(model.state).first?.months ?? []
        #expect(months.map(\.month) == [4, 5, 6])
        #expect(months.map(\.amount) == [Decimal(string: "1200.00")!, 0, Decimal(string: "700.00")!])
        #expect(months.map(\.transactionCount) == [1, 0, 1])
    }

    @Test func noSpendingAtAllIsTheEmptyState() async {
        let app = TestApp { _ in json(200, noSpending) }
        let model = MonthlyTrendViewModel(api: app.api)

        await model.load()

        #expect(model.state == .empty)
    }

    @Test func currenciesStayApartAndNothingIsAddedAcrossThem() async {
        let app = TestApp { _ in json(200, twoCurrencies) }
        let model = MonthlyTrendViewModel(api: app.api)

        await model.load()

        let currencies = loaded(model.state)
        #expect(currencies.map(\.currencyCode) == ["TRY", "EUR"])
        #expect(currencies.map { $0.months.map(\.amount) }
                == [[Decimal(string: "1000.00")!, Decimal(string: "500.00")!], [Decimal(string: "40.00")!, Decimal(string: "10.00")!]])
    }

    @Test func aMissingFieldIsAContractMismatchNotAHalfDrawnChart() async {
        let broken = denizSixMonths.replacingOccurrences(of: #""isPartial": true, "#, with: "")
        let app = TestApp { _ in json(200, broken) }
        let model = MonthlyTrendViewModel(api: app.api)

        await model.load()

        #expect(model.state == .failed("Beklenmeyen bir cevap alındı; tekrar deneyin."))
    }

    @Test func aFirstLoadThatFailsShowsTheMessage() async {
        let app = TestApp { _ in problem(500, "internal_error", "Beklenmeyen bir hata oluştu.") }
        let model = MonthlyTrendViewModel(api: app.api)

        await model.load()

        #expect(model.state == .failed("Beklenmeyen bir hata oluştu."))
    }

    @Test func aFailedRefreshKeepsTheOldChartAndShowsABand() async {
        let answers = Answers([{ json(200, denizSixMonths) }, { problem(500, "internal_error", "Beklenmeyen bir hata oluştu.") }])
        let app = TestApp { try answers.next($0) }
        let model = MonthlyTrendViewModel(api: app.api)
        await model.load()

        await model.load()

        #expect(loaded(model.state).first?.months.count == 7)
        #expect(model.refreshError == "Beklenmeyen bir hata oluştu.")
    }

    @Test func aSuccessfulLoadClearsTheRefreshError() async {
        let answers = Answers([{ json(200, denizSixMonths) }, { problem(500, "internal_error", "Beklenmeyen bir hata oluştu.") },
                               { json(200, denizSixMonths) }])
        let app = TestApp { try answers.next($0) }
        let model = MonthlyTrendViewModel(api: app.api)
        await model.load()
        await model.load()
        #expect(model.refreshError != nil)

        await model.load()

        #expect(model.refreshError == nil)
    }

    @Test func anOlderAnswerArrivingLateNeverReplacesTheNewerOne() async {
        let calls = Answers([{ Thread.sleep(forTimeInterval: 0.4); return json(200, denizSixMonths) }, { json(200, withQuietMonth) }])
        let app = TestApp { try calls.next($0) }
        let model = MonthlyTrendViewModel(api: app.api)
        let older = Task { await model.load() }
        try? await Task.sleep(for: .milliseconds(100))

        await model.load()
        await older.value

        #expect(loaded(model.state).first?.months.count == 3)
    }

    @Test func anExpiredSessionShowsNoErrorOnTheChart() async {
        let app = TestApp { _ in problem(401, "session_invalid", "Oturum geçersiz.") }
        let model = MonthlyTrendViewModel(api: app.api)

        await model.load()

        #expect(model.state == .loading)
        #expect(model.refreshError == nil)
        #expect(!app.session.isSignedIn)
    }

    @Test func noConnectionIsTheUnreachableMessage() async {
        let app = TestApp { _ in throw URLError(.notConnectedToInternet) }
        let model = MonthlyTrendViewModel(api: app.api)

        await model.load()

        #expect(model.state == .failed(unreachableMessage))
    }

    @Test func tappingABarSelectsItAndTappingItAgainClearsIt() async {
        let app = TestApp { _ in json(200, denizSixMonths) }
        let model = MonthlyTrendViewModel(api: app.api)
        await model.load()
        let september = loaded(model.state)[0].months[5]

        model.toggle(september, currency: "TRY")
        #expect(model.selectedID(currency: "TRY") == "2026-09")

        model.toggle(september, currency: "TRY")
        #expect(model.selectedID(currency: "TRY") == nil)
    }

    @Test func aSelectionIsKeptPerCurrency() async {
        let app = TestApp { _ in json(200, twoCurrencies) }
        let model = MonthlyTrendViewModel(api: app.api)
        await model.load()
        let currencies = loaded(model.state)

        model.toggle(currencies[0].months[0], currency: "TRY")
        model.toggle(currencies[1].months[1], currency: "EUR")

        #expect(model.selectedID(currency: "TRY") == "2026-09")
        #expect(model.selectedID(currency: "EUR") == "2026-10")
    }

    @Test func aMonthThatIsNoLongerThereLeavesNoSelection() async {
        let answers = Answers([{ json(200, denizSixMonths) }, { json(200, withQuietMonth) }])
        let app = TestApp { try answers.next($0) }
        let model = MonthlyTrendViewModel(api: app.api)
        await model.load()
        model.toggle(loaded(model.state)[0].months[6], currency: "TRY")
        #expect(model.selectedID(currency: "TRY") == "2026-10")

        await model.load()

        #expect(model.selectedID(currency: "TRY") == nil)
    }
}

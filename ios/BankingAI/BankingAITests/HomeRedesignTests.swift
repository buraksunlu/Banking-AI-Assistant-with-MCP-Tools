import Foundation
import Testing
@testable import BankingAI

// Ada, ThisMonth, demo moment 10 October 2026 (docs/api.md): 735,00 TRY in 5 purchases, 145,00 of them unclassified.
private let adaThisMonth = """
{
  "period": { "kind": "ThisMonth", "startDate": "2026-10-01", "endDate": "2026-10-10" },
  "currencies": [
    { "currencyCode": "TRY", "totalAmount": 735.00, "transactionCount": 5, "uncategorizedAmount": 145.00, "uncategorizedCount": 1 }
  ]
}
"""

// Two currencies stay two lines and are never added together.
private let twoCurrencies = """
{
  "period": { "kind": "ThisMonth", "startDate": "2026-10-01", "endDate": "2026-10-26" },
  "currencies": [
    { "currencyCode": "TRY", "totalAmount": 8241.40, "transactionCount": 4, "uncategorizedAmount": 0.00, "uncategorizedCount": 0 },
    { "currencyCode": "EUR", "totalAmount": 120.00, "transactionCount": 1, "uncategorizedAmount": 0.00, "uncategorizedCount": 0 }
  ]
}
"""

private let noSpending = """
{ "period": { "kind": "ThisMonth", "startDate": "2026-10-01", "endDate": "2026-10-10" }, "currencies": [] }
"""

private let unreachableMessage = "Sunucuya ulaşılamıyor; bağlantıyı kontrol edip tekrar deneyin."

private func loaded(_ state: ListState<CurrencySpending>) -> [CurrencySpending] {
    if case .loaded(let currencies) = state { return currencies }
    return []
}

// UI-03: the "Bu ay harcama" card on the home screen takes the backend's own figure for the running month.
@MainActor
struct SpendingSummaryViewModelTests {
    @Test func theSummaryIsAskedForWithTheTokenAndTheThisMonthName() async {
        let app = TestApp { _ in json(200, adaThisMonth) }

        await SpendingSummaryViewModel(api: app.api).load()

        #expect(app.requests.map(\.method) == ["GET"])
        #expect(app.requests.map(\.path) == ["/api/analytics/spending-summary"])
        #expect(app.requests.map(\.query) == ["period=ThisMonth"])
        #expect(app.requests.first?.authorization == "Bearer token-abc")
    }

    @Test func adasMonthKeepsTheBackendsFigureAndDay() async {
        let app = TestApp { _ in json(200, adaThisMonth) }
        let model = SpendingSummaryViewModel(api: app.api)

        await model.load()

        let currencies = loaded(model.state)
        #expect(currencies.map(\.currencyCode) == ["TRY"])
        #expect(currencies.map(\.totalAmount) == [Decimal(string: "735.00")!])
        #expect(currencies.map(\.transactionCount) == [5])
        #expect(model.endDate == "2026-10-10")
        #expect(model.refreshError == nil)
    }

    @Test func currenciesStayApartAndNothingIsAddedAcrossThem() async {
        let app = TestApp { _ in json(200, twoCurrencies) }
        let model = SpendingSummaryViewModel(api: app.api)

        await model.load()

        let currencies = loaded(model.state)
        #expect(currencies.map(\.currencyCode) == ["TRY", "EUR"])
        #expect(currencies.map { Money.text($0.totalAmount, currency: $0.currencyCode) } == ["8.241,40 TL", "120,00 EUR"])
    }

    @Test func noSpendingIsTheEmptyStateAndTheDayIsStillKnown() async {
        let app = TestApp { _ in json(200, noSpending) }
        let model = SpendingSummaryViewModel(api: app.api)

        await model.load()

        #expect(model.state == .empty)
        #expect(model.endDate == "2026-10-10")
    }

    @Test func aFirstLoadThatFailsShowsTheMessage() async {
        let app = TestApp { _ in problem(500, "internal_error", "Beklenmeyen bir hata oluştu.") }
        let model = SpendingSummaryViewModel(api: app.api)

        await model.load()

        #expect(model.state == .failed("Beklenmeyen bir hata oluştu."))
        #expect(model.endDate == nil)
    }

    @Test func aFailedRefreshKeepsTheFigureAndTheDayAndSaysSo() async {
        let answers = Answers([{ json(200, adaThisMonth) }, { throw URLError(.notConnectedToInternet) }])
        let app = TestApp { try answers.next($0) }
        let model = SpendingSummaryViewModel(api: app.api)

        await model.load()
        await model.load()

        #expect(loaded(model.state).map(\.totalAmount) == [Decimal(string: "735.00")!])
        #expect(model.endDate == "2026-10-10")
        #expect(model.refreshError == unreachableMessage)
    }

    @Test func anExpiredSessionShowsNoErrorOnTheCard() async {
        let app = TestApp { _ in problem(401, "unauthorized", "Oturum geçersiz.") }
        let model = SpendingSummaryViewModel(api: app.api)

        await model.load()

        #expect(model.refreshError == nil)
        #expect(model.state == .loading)
    }
}

// UI-03: the small pieces of text the new home screen writes, decided without a view.
struct HomeDisplayTests {
    // The shortened number keeps the part after the last dash, the one that tells two accounts of a customer apart.
    @Test(arguments: [
        ("DUMMY-ADA-TRY-001", "•••• 001"),
        ("DUMMY-ADA-EMPTY-001", "•••• 001"),
        ("TR12345678", "•••• 5678"),
        ("12", "12"),
        ("", ""),
    ])
    func theAccountNumberIsShortened(pair: (String, String)) {
        #expect(AccountDisplay.shortNumber(pair.0) == pair.1)
    }

    // Turkish capitals: "i" becomes "İ", and a plain uppercased() would write "II".
    @Test(arguments: [
        ("Murat", "Yılmaz", "MY"),
        ("Ege", "", "E"),
        ("ilker", "işık", "İİ"),
        ("", "", ""),
    ])
    func theInitialsAreTurkishCapitals(person: (String, String, String)) {
        #expect(HomeDisplay.initials(first: person.0, last: person.1) == person.2)
    }

    @Test func theDayLineIsTheBackendsDayWrittenByHand() {
        #expect(HomeDisplay.dayLine("2026-10-10") == "10 Ekim 2026")
        #expect(HomeDisplay.dayLine("2026-10-26") == "26 Ekim 2026")
        #expect(HomeDisplay.dayLine("2026-01-05") == "5 Ocak 2026")
    }

    @Test func aDayThatCannotBeReadIsNotShown() {
        #expect(HomeDisplay.dayLine("26/10/2026") == nil)
        #expect(HomeDisplay.dayLine("2026-13-05") == nil)
        #expect(HomeDisplay.dayLine(nil) == nil)
    }

    @Test func theCardNamesTheAccountAndItsNumberForVoiceOver() {
        let account = Account(id: UUID(), accountName: "Ada günlük TRY", accountNumber: "DUMMY-ADA-TRY-001",
                              currencyCode: "TRY", accountType: .personal, balance: Decimal(string: "577490.18")!)
        #expect(AccountDisplay.spokenSummary(account) == "Ada günlük TRY, 577.490,18 TL, hesap sonu 001")
    }
}

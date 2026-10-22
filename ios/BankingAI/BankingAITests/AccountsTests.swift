import Foundation
import Testing
@testable import BankingAI

// Ada's accounts as `GET /api/accounts` returns them (docs/api.md): same opening moment, so ordered by account number.
private let adaAccounts = """
[
  { "id": "20000000-0000-0000-0000-000000000005", "accountName": "Ada boş TRY", "accountNumber": "DUMMY-ADA-EMPTY-001", "currencyCode": "TRY", "accountType": "Personal", "balance": 0.00, "createdAt": "2026-10-09T14:08:38.294505+00:00" },
  { "id": "20000000-0000-0000-0000-000000000001", "accountName": "Ada günlük TRY", "accountNumber": "DUMMY-ADA-TRY-001", "currencyCode": "TRY", "accountType": "Personal", "balance": 577490.18, "createdAt": "2026-10-09T14:08:38.294505+00:00" },
  { "id": "20000000-0000-0000-0000-000000000002", "accountName": "Ada USD", "accountNumber": "DUMMY-ADA-USD-001", "currencyCode": "USD", "accountType": "Personal", "balance": 500.00, "createdAt": "2026-10-09T14:08:38.294505+00:00" }
]
"""

private let denizAccounts = """
[
  { "id": "20000000-0000-0000-0000-000000000003", "accountName": "Deniz aile TRY", "accountNumber": "DUMMY-DENIZ-TRY-001", "currencyCode": "TRY", "accountType": "Personal", "balance": 522900.00, "createdAt": "2026-10-09T14:08:38.294505+00:00" },
  { "id": "20000000-0000-0000-0000-000000000007", "accountName": "Kahve Durağı – Kadıköy", "accountNumber": "DUMMY-DENIZ-SHOP-001", "currencyCode": "TRY", "accountType": "Business", "balance": 2595.00, "createdAt": "2026-10-10T19:09:17.268563+00:00" }
]
"""

private let unreachableMessage = "Sunucuya ulaşılamıyor; bağlantıyı kontrol edip tekrar deneyin."

private func accounts(_ state: AccountsViewModel.State) -> [Account] {
    if case .loaded(let accounts) = state { return accounts }
    return []
}

// IOS-04: the customer's accounts on the home screen, with its loading, empty, error and list states.
@MainActor
struct AccountsViewModelTests {
    @Test func adasAccountsKeepTheAPIsOrderAndExactBalances() async {
        let app = TestApp { _ in json(200, adaAccounts) }
        let model = AccountsViewModel(api: app.api)

        await model.load()

        let shown = accounts(model.state)
        #expect(shown.map(\.accountName) == ["Ada boş TRY", "Ada günlük TRY", "Ada USD"])
        #expect(shown.map(\.accountNumber) == ["DUMMY-ADA-EMPTY-001", "DUMMY-ADA-TRY-001", "DUMMY-ADA-USD-001"])
        #expect(shown.map(\.currencyCode) == ["TRY", "TRY", "USD"])
        // Decimal, not Double: 577490.18 must arrive digit for digit.
        #expect(shown.map(\.balance) == [Decimal(string: "0.00")!, Decimal(string: "577490.18")!, Decimal(string: "500.00")!])
        #expect(shown.first?.id == UUID(uuidString: "20000000-0000-0000-0000-000000000005"))
        #expect(model.refreshError == nil)
    }

    @Test func theListIsAskedForWithTheToken() async {
        let app = TestApp { _ in json(200, adaAccounts) }

        await AccountsViewModel(api: app.api).load()

        #expect(app.requests.map(\.method) == ["GET"])
        #expect(app.requests.map(\.path) == ["/api/accounts"])
        #expect(app.requests.first?.authorization == "Bearer token-abc")
    }

    @Test func aBusinessAccountIsKnownAsOne() async {
        let app = TestApp { _ in json(200, denizAccounts) }
        let model = AccountsViewModel(api: app.api)

        await model.load()

        #expect(accounts(model.state).map(\.accountType) == [.personal, .business])
    }

    @Test func anUnknownAccountTypeIsAContractMismatch() async {
        let app = TestApp { _ in json(200, adaAccounts.replacingOccurrences(of: #""USD", "accountType": "Personal""#,
                                                                             with: #""USD", "accountType": "Savings""#)) }
        let model = AccountsViewModel(api: app.api)

        await model.load()

        #expect(model.state == .failed("Beklenmeyen bir cevap alındı; tekrar deneyin."))
    }

    @Test func noAccountsIsTheEmptyState() async {
        let app = TestApp { _ in json(200, "[]") }
        let model = AccountsViewModel(api: app.api)

        await model.load()

        #expect(model.state == .empty)
    }

    @Test func aServerErrorShowsTheAPIsTitle() async {
        let app = TestApp { _ in problem(500, "internal_error", "Beklenmeyen bir hata oluştu.") }
        let model = AccountsViewModel(api: app.api)

        await model.load()

        #expect(model.state == .failed("Beklenmeyen bir hata oluştu."))
    }

    @Test func anUnreachableAPIIsShown() async {
        let app = TestApp { _ in throw URLError(.cannotConnectToHost) }
        let model = AccountsViewModel(api: app.api)

        await model.load()

        #expect(model.state == .failed(unreachableMessage))
    }

    @Test func aRefusedTokenGoesBackToLoginWithoutAnError() async {
        let app = TestApp { _ in problem(401, "session_invalid", "Oturum geçersiz; yeniden giriş yapın.") }
        let model = AccountsViewModel(api: app.api)

        await model.load()

        #expect(!app.session.isSignedIn)
        #expect(model.state == .loading)
        #expect(model.refreshError == nil)
    }

    @Test func aCancelledLoadShowsNothing() async throws {
        let app = TestApp(delay: 2) { _ in json(200, adaAccounts) }
        let model = AccountsViewModel(api: app.api)

        let loading = Task { await model.load() }
        try await waitUntil { !app.requests.isEmpty }
        loading.cancel()
        await loading.value

        #expect(model.state == .loading)
        #expect(model.refreshError == nil)
    }

    // K6-A: a failed refresh keeps the balances already on screen and says so above them.
    @Test func aFailedRefreshKeepsTheList() async {
        let answers = Answers([{ json(200, adaAccounts) }, { throw URLError(.cannotConnectToHost) }])
        let app = TestApp(answers.next)
        let model = AccountsViewModel(api: app.api)

        await model.load()
        await model.load()

        #expect(accounts(model.state).count == 3)
        #expect(model.refreshError == unreachableMessage)
    }

    @Test func aSuccessfulRefreshClearsTheBanner() async {
        let answers = Answers([{ json(200, adaAccounts) }, { throw URLError(.cannotConnectToHost) }, { json(200, denizAccounts) }])
        let app = TestApp(answers.next)
        let model = AccountsViewModel(api: app.api)

        await model.load()
        await model.load()
        await model.load()

        #expect(accounts(model.state).map(\.accountName) == ["Deniz aile TRY", "Kahve Durağı – Kadıköy"])
        #expect(model.refreshError == nil)
    }

    @Test func retryingAfterAFailureShowsTheList() async {
        let answers = Answers([{ throw URLError(.cannotConnectToHost) }, { json(200, adaAccounts) }])
        let app = TestApp(answers.next)
        let model = AccountsViewModel(api: app.api)

        await model.load()
        #expect(model.state == .failed(unreachableMessage))
        await model.load()

        #expect(accounts(model.state).count == 3)
        #expect(model.refreshError == nil)
    }
}

// K3-A: amounts read the same as in the assistant's answers (ai/service/formatting.py `money`).
struct MoneyTests {
    @Test(arguments: [
        ("577490.18", "TRY", "577.490,18 TL"),
        ("0", "TRY", "0,00 TL"),
        ("2595", "TRY", "2.595,00 TL"),
        ("522900.00", "TRY", "522.900,00 TL"),
        ("0.5", "TRY", "0,50 TL"),
        ("500", "USD", "500,00 USD"),
        ("1000", "EUR", "1.000,00 EUR"),
    ])
    func amountsAreWrittenTheTurkishWay(amount: String, currency: String, expected: String) {
        #expect(Money.text(Decimal(string: amount)!, currency: currency) == expected)
    }
}

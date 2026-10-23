import Foundation
import Testing
@testable import BankingAI

private let accountID = UUID(uuidString: "20000000-0000-0000-0000-000000000001")!
private let accountJSON = #"{"id":"20000000-0000-0000-0000-000000000001","accountName":"Günlük","accountNumber":"DUMMY-ADA-TRY-001","currencyCode":"TRY","accountType":"Personal","balance":577490.18}"#

@MainActor private func account() throws -> Account {
    try JSONDecoder().decode(Account.self, from: Data(accountJSON.utf8))
}

private func page(_ id: Int, cursor: String? = nil, start: String = "2025-04-05", total: Int = 48) -> String {
    let token = cursor.map { "\"\($0)\"" } ?? "null"
    let rows = id == 0 ? "" : """
    {"id":"30000000-0000-0000-0000-\(String(format: "%012d", id))","accountName":"Günlük","localDate":"2026-10-09",
    "kind":"Purchase","direction":"Debit","amount":145.00,"currencyCode":"TRY","merchant":null,"categoryCode":null,
    "categoryDisplayName":null,"rawDescription":"POS TEST","counterparty":null,"counterpartyIsOwnAccount":false}
    """
    return """
    {"period":{"startDate":"\(start)","endDate":"2026-10-10"},"totalCount":\(total),"hasMore":\(cursor != nil),"nextCursor":\(token),"items":[\(rows)]}
    """
}

@MainActor
struct AccountDetailTests {
    @Test func theCardIsImmediateAndTheAccountIsRefreshed() async throws {
        let app = TestApp { request in
            request.url!.path.hasSuffix("transactions") ? json(200, page(1)) : json(200, accountJSON.replacingOccurrences(of: "577490.18", with: "600000.25"))
        }
        let model = AccountDetailViewModel(account: try account(), api: app.api)
        #expect(model.account.balance == Decimal(string: "577490.18"))
        await model.open()
        #expect(model.account.balance == Decimal(string: "600000.25"))
        #expect(model.history.totalCount == 48)
        #expect(Set(app.requests.map(\.path)) == ["/api/accounts/\(accountID.uuidString)", "/api/accounts/\(accountID.uuidString)/transactions"])
        #expect(app.requests.allSatisfy { $0.authorization == "Bearer token-abc" })
        await model.open()
        #expect(app.requests.count == 2)
    }

    @Test func accountFailureKeepsTheCardAndTheTransactions() async throws {
        let app = TestApp { request in
            if request.url!.path.hasSuffix("transactions") { return json(200, page(1)) }
            throw URLError(.cannotConnectToHost)
        }
        let model = AccountDetailViewModel(account: try account(), api: app.api)
        await model.open()
        #expect(model.account.balance == Decimal(string: "577490.18"))
        #expect(model.accountError != nil)
        #expect(model.history.items.count == 1)
    }

    @Test func aMissingAccountHidesItsOldCard() async throws {
        let app = TestApp { _ in problem(404, "not_found", "Bulunamadı") }
        let model = AccountDetailViewModel(account: try account(), api: app.api)
        await model.open()
        #expect(model.isUnavailable)
    }

    @Test func unauthorizedSignsOutWithoutShowingAnAccountError() async throws {
        let app = TestApp { _ in problem(401, "session_invalid", "Giriş gerekli") }
        let model = AccountDetailViewModel(account: try account(), api: app.api)
        await model.open()
        #expect(!app.session.isSignedIn)
        #expect(model.accountError == nil)
    }

    @Test func openingAgainKeepsTheSelectedPeriod() async throws {
        let app = TestApp { request in request.url!.path.hasSuffix("transactions") ? json(200, page(1)) : json(200, accountJSON) }
        let model = AccountDetailViewModel(account: try account(), api: app.api)
        await model.open()
        _ = await model.history.apply(.lastMonth)
        await model.open()
        #expect(model.history.filter == .lastMonth)
        #expect(app.requests.count == 3)
    }

    @Test func refreshUpdatesBothPartsUsingTheSelectedPeriod() async throws {
        let app = TestApp { request in request.url!.path.hasSuffix("transactions") ? json(200, page(1)) : json(200, accountJSON) }
        let model = AccountDetailViewModel(account: try account(), api: app.api)
        await model.open()
        _ = await model.history.apply(.lastMonth)
        await model.refresh()
        #expect(app.requests.count == 5)
        #expect(app.requests.filter { $0.path.hasSuffix("transactions") }.last?.query == "period=LastMonth&limit=20")
        #expect(model.history.filter == .lastMonth)
    }

    @Test func retryingTheHeaderDoesNotReloadTheStatement() async throws {
        let app = TestApp { request in request.url!.path.hasSuffix("transactions") ? json(200, page(1)) : json(200, accountJSON) }
        let model = AccountDetailViewModel(account: try account(), api: app.api)
        await model.open()
        await model.refreshAccount()
        #expect(app.requests.filter { $0.path.hasSuffix("transactions") }.count == 1)
        #expect(app.requests.count == 3)
    }

    @Test func aNewDetailDoesNotInheritAnotherVisitsFilter() async throws {
        let app = TestApp { request in request.url!.path.hasSuffix("transactions") ? json(200, page(1)) : json(200, accountJSON) }
        let first = AccountDetailViewModel(account: try account(), api: app.api)
        _ = await first.history.apply(.lastMonth)
        let second = AccountDetailViewModel(account: try account(), api: app.api)
        #expect(second.history.filter == .allTime)
        #expect(second.history.items.isEmpty)
    }
}

@MainActor
struct AccountHistoryTests {
    @Test func accountPagingKeepsTheScopeAndTheCursor() async {
        let app = TestApp { request in json(200, page(1, cursor: request.url!.query()!.contains("cursor=") ? nil : "after-first")) }
        let model = TransactionHistoryViewModel(api: app.api, accountID: accountID)
        await model.loadMore()
        await model.loadMore()
        #expect(app.requests.map(\.query) == ["period=AllTime&limit=20", "period=AllTime&limit=20&cursor=after-first"])
        #expect(app.requests.allSatisfy { $0.path == "/api/accounts/\(accountID.uuidString)/transactions" })
        #expect(!model.hasMore)
    }

    @Test func aNewPeriodStartsWithoutTheOldCursor() async {
        let app = TestApp { _ in json(200, page(1, cursor: "next")) }
        let model = TransactionHistoryViewModel(api: app.api, accountID: accountID)
        await model.loadMore()
        let applied = await model.apply(.lastMonth)
        #expect(applied)
        #expect(model.filter == .lastMonth)
        #expect(model.items.count == 1)
        #expect(app.requests.last?.query == "period=LastMonth&limit=20")
        await model.loadMore()
        #expect(app.requests.last?.query == "period=LastMonth&limit=20&cursor=next")
    }

    @Test func failedFilterKeepsTheOldRowsPeriodAndCursor() async {
        let answers = Answers([{ json(200, page(1, cursor: "old-next")) }, { throw URLError(.cannotConnectToHost) }, { json(200, page(2)) }])
        let app = TestApp(answers.next)
        let model = TransactionHistoryViewModel(api: app.api, accountID: accountID)
        await model.loadMore()
        let applied = await model.apply(.thisMonth)
        #expect(!applied)
        #expect(model.filter == .allTime)
        #expect(model.items.count == 1)
        #expect(model.periodText == "5 Nisan 2025 – 10 Ekim 2026")
        #expect(model.filterError != nil)
        await model.loadMore()
        #expect(app.requests.last?.query == "period=AllTime&limit=20&cursor=old-next")
    }

    @Test func customDatesAreSentAsDaysAndNeverAsInstants() async {
        let app = TestApp { _ in json(200, page(1)) }
        let model = TransactionHistoryViewModel(api: app.api, accountID: accountID)
        _ = await model.apply(.custom(from: "2026-09-01", to: "2026-09-05"))
        #expect(app.requests.last?.query == "period=Custom&from=2026-09-01&to=2026-09-05&limit=20")
    }

    @Test func validationExplainsTheDateAndDoesNotReplaceTheList() async {
        let answers = Answers([{ json(200, page(1)) }, {
            json(400, #"{"status":400,"code":"validation_failed","title":"Geçersiz istek.","errors":{"to":["Bitiş tarihi gelecekte olamaz."]}}"#)
        }])
        let app = TestApp(answers.next)
        let model = TransactionHistoryViewModel(api: app.api, accountID: accountID)
        await model.loadMore()
        _ = await model.apply(.custom(from: "2026-10-11", to: "2026-10-15"))
        #expect(model.filterError == "Bitiş tarihi gelecekte olamaz.")
        #expect(model.filter == .allTime)
        #expect(model.items.count == 1)
    }

    @Test func refreshRetryRestartsAtTheFirstPage() async {
        let answers = Answers([{ json(200, page(1, cursor: "old-next")) }, { throw URLError(.cannotConnectToHost) }, { json(200, page(9)) }])
        let app = TestApp(answers.next)
        let model = TransactionHistoryViewModel(api: app.api, accountID: accountID)
        await model.loadMore()
        await model.refresh()
        await model.retry()
        #expect(app.requests.last?.query == "period=AllTime&limit=20")
        #expect(model.items.count == 1)
        #expect(model.items.first?.id.uuidString.hasSuffix("000000000009") == true)
    }

    @Test func lateOldPageCannotOverwriteTheNewFilter() async throws {
        let app = TestApp(delay: 0.15) { request in
            let changed = request.url!.query()!.contains("LastMonth")
            return json(200, page(changed ? 9 : 1, start: changed ? "2026-09-01" : "2025-04-05"))
        }
        let model = TransactionHistoryViewModel(api: app.api, accountID: accountID)
        let old = Task { await model.loadMore() }
        try await waitUntil { !app.requests.isEmpty }
        let applied = await model.apply(.lastMonth)
        await old.value
        #expect(applied)
        #expect(model.filter == .lastMonth)
        #expect(model.items.count == 1)
        #expect(model.items.first?.id.uuidString.hasSuffix("000000000009") == true)
        #expect(model.periodText == "1 Eylül 2026 – 10 Ekim 2026")
    }

    @Test func aFilteredEmptyResultIsNotAnEmptyAccount() async {
        let app = TestApp { _ in json(200, page(0, total: 0)) }
        let model = TransactionHistoryViewModel(api: app.api, accountID: accountID)
        await model.loadMore()
        #expect(model.emptyMessage == "Bu hesapta henüz işlem yok.")
        _ = await model.apply(.lastMonth)
        #expect(model.emptyMessage == "Seçilen dönemde işlem yok.")
    }

    @Test func cancellingAFilterKeepsTheOldDataWithoutAnError() async throws {
        let app = TestApp(delay: 0.1) { _ in json(200, page(1)) }
        let model = TransactionHistoryViewModel(api: app.api, accountID: accountID)
        await model.loadMore()
        let pending = Task { await model.apply(.lastMonth) }
        try await waitUntil { app.requests.count >= 2 }
        pending.cancel()
        _ = await pending.value
        #expect(model.filter == .allTime)
        #expect(model.filterError == nil)
        #expect(!model.isLoading)
    }

    @Test func aReversedRangeMakesNoRequest() async {
        let app = TestApp { _ in json(200, page(1)) }
        let model = TransactionHistoryViewModel(api: app.api, accountID: accountID)
        let applied = await model.apply(.custom(from: "2026-09-05", to: "2026-09-01"))
        #expect(!applied)
        #expect(app.requests.isEmpty)
        #expect(model.filterError == "Başlangıç tarihi bitiş tarihinden sonra olamaz.")
    }

    @Test func aSuccessfulEmptyFilterReplacesTheOldRowsAndCount() async {
        let answers = Answers([{ json(200, page(1, cursor: "old-next")) }, { json(200, page(0, total: 0)) }])
        let app = TestApp(answers.next)
        let model = TransactionHistoryViewModel(api: app.api, accountID: accountID)
        await model.loadMore()
        let applied = await model.apply(.lastMonth)
        #expect(applied)
        #expect(model.items.isEmpty)
        #expect(model.totalCount == 0)
        #expect(!model.hasMore)
        await model.loadMore()
        #expect(app.requests.count == 2)
    }

    @Test func pagingFailureRetriesTheSameAccountAndFilteredCursor() async {
        let answers = Answers([{ json(200, page(1, cursor: "filtered-next")) }, { throw URLError(.cannotConnectToHost) }, { json(200, page(2)) }])
        let app = TestApp(answers.next)
        let model = TransactionHistoryViewModel(api: app.api, accountID: accountID)
        _ = await model.apply(.last6Months)
        await model.loadMore()
        #expect(model.items.count == 1)
        #expect(model.errorMessage != nil)
        await model.retry()
        #expect(model.items.count == 2)
        #expect(app.requests.last?.query == "period=Last6Months&limit=20&cursor=filtered-next")
        #expect(app.requests.allSatisfy { $0.path == "/api/accounts/\(accountID.uuidString)/transactions" })
    }

    @Test func aMissingStatementInvalidatesTheDetail() async {
        let app = TestApp { _ in problem(404, "not_found", "Bulunamadı") }
        let model = TransactionHistoryViewModel(api: app.api, accountID: accountID)
        await model.loadMore()
        #expect(model.isUnavailable)
        await model.loadMore()
        #expect(app.requests.count == 1)
    }

    @Test func theServerCanRejectADateEvenWhenThePhoneWouldAllowIt() async {
        let app = TestApp { _ in json(400, #"{"status":400,"code":"validation_failed","title":"İstek geçersiz.","errors":{"to":["Bitiş tarihi gelecekte olamaz."]}}"#) }
        let model = TransactionHistoryViewModel(api: app.api, accountID: accountID)
        _ = await model.apply(.custom(from: "2026-10-11", to: "2026-10-15"))
        #expect(model.filterError == "Bitiş tarihi gelecekte olamaz.")
        #expect(model.filter == .allTime)
        #expect(!model.loadedOnce)
    }
}

@MainActor
struct TransactionFilterTests {
    @Test func aReversedRangeIsRejected() {
        #expect(TransactionFilter.custom(from: "2026-09-05", to: "2026-09-01").validationMessage != nil)
    }

    @Test func twentyFourCalendarMonthsAreAllowedButOneMoreDayIsNot() {
        #expect(TransactionFilter.custom(from: "2024-09-05", to: "2026-09-05").validationMessage == nil)
        #expect(TransactionFilter.custom(from: "2024-09-04", to: "2026-09-05").validationMessage != nil)
    }

    @Test func invalidDatesAreRejectedAndLeapDaysWork() {
        #expect(TransactionFilter.custom(from: "2026-02-31", to: "2026-03-01").validationMessage != nil)
        #expect(TransactionFilter.custom(from: "2024-02-29", to: "2024-03-01").validationMessage == nil)
    }

    @Test func calendarSelectionsRoundTripWithoutDeviceTimeZone() throws {
        let date = try #require(TransactionFilter.date("2026-09-01"))
        #expect(TransactionFilter.day(date) == "2026-09-01")
        #expect(TransactionFilter.day(date.addingTimeInterval(86400)) == "2026-09-02")
    }
}

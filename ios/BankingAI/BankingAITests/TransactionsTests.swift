import Foundation
import Testing
@testable import BankingAI

/// One `TransactionItem` as the API writes it; only the fields a test cares about are parameters.
private func item(_ number: Int, kind: String = "Purchase", direction: String = "Debit", amount: String = "100.00",
                  currency: String = "TRY", merchant: String? = nil, categoryCode: String? = nil, category: String? = nil,
                  raw: String = "POS TEST", counterparty: String? = nil, ownAccount: Bool = false,
                  localDate: String = "2026-10-08") -> String {
    func text(_ value: String?) -> String { value.map { #""\#($0)""# } ?? "null" }
    let id = String(format: "30000000-0000-0000-0000-%012d", number)
    return """
    { "id": "\(id)", "accountId": "20000000-0000-0000-0000-000000000001", "accountName": "Ada günlük TRY", \
    "occurredAtUtc": "\(localDate)T10:00:00.123456+00:00", "localDate": "\(localDate)", "kind": "\(kind)", \
    "direction": "\(direction)", "amount": \(amount), "currencyCode": "\(currency)", "merchant": \(text(merchant)), \
    "categoryCode": \(text(categoryCode)), "categoryDisplayName": \(text(category)), "rawDescription": "\(raw)", \
    "counterparty": \(text(counterparty)), "counterpartyIsOwnAccount": \(ownAccount) }
    """
}

private func result(_ items: [String], totalCount: Int? = nil, hasMore: Bool = false) -> String {
    """
    { "period": { "kind": "Last3Months", "startUtc": "2026-07-09T21:00:00+00:00", "endUtc": "2026-10-10T21:00:00+00:00", \
    "startDate": "2026-07-10", "endDate": "2026-10-10" }, "merchantMatch": null, "totalCount": \(totalCount ?? items.count), \
    "hasMore": \(hasMore), "items": [\(items.joined(separator: ","))] }
    """
}

// Ada's five newest transactions (demo database, newest first; docs/ios.md "iOS-e").
private let adaLatest = result([
    item(612, amount: "145.00", raw: "POS 6604 ONLINE ODEME 4471", localDate: "2026-10-09"),
    item(611, kind: "Transfer", direction: "Credit", amount: "300.00", raw: "Gelen para transferi", counterparty: "Ege Y***",
         localDate: "2026-10-08"),
    item(610, amount: "165.00", merchant: "Starbucks", categoryCode: "FOOD", category: "Yeme & İçme",
         raw: "POS 4412 STARBUCKS KADIKOY", localDate: "2026-10-08"),
    item(609, amount: "95.00", merchant: "Kahve Durağı", categoryCode: "FOOD", category: "Yeme & İçme",
         raw: "POS 7001 KAHVE DURAGI KADIKOY", counterparty: "Kahve Durağı – Kadıköy", localDate: "2026-10-07"),
    item(602, amount: "210.00", merchant: "Starbucks", categoryCode: "FOOD", category: "Yeme & İçme",
         raw: "POS 4412 STARBUCKS KADIKOY", localDate: "2026-10-06"),
], totalCount: 21, hasMore: true)

private let unreachableMessage = "Sunucuya ulaşılamıyor; bağlantıyı kontrol edip tekrar deneyin."

private func decoded(_ json: String) throws -> TransactionItem {
    try JSONDecoder().decode(TransactionItem.self, from: Data(json.utf8))
}

private func recent(_ state: RecentTransactionsViewModel.State) -> [TransactionItem] {
    if case .loaded(let items) = state { return items }
    return []
}

/// A history page holding the items `numbers`, in that order (the order is the API's; the tests only check it is kept).
private func page(_ numbers: ClosedRange<Int>, hasMore: Bool) -> String {
    result(numbers.map { item($0) }, totalCount: 45, hasMore: hasMore)
}

private func numbers(_ items: [TransactionItem]) -> [Int] {
    items.map { Int($0.id.uuidString.suffix(12))! }
}

// IOS-07: the five newest transactions under the accounts (decisions K1-A, K2-B).
@MainActor
struct RecentTransactionsViewModelTests {
    @Test func adasNewestKeepTheAPIsOrderAndExactAmounts() async throws {
        let app = TestApp { _ in json(200, adaLatest) }
        let model = RecentTransactionsViewModel(api: app.api)

        await model.load()

        let shown = recent(model.state)
        #expect(numbers(shown) == [612, 611, 610, 609, 602])
        #expect(shown.map(\.amount) == ["145.00", "300.00", "165.00", "95.00", "210.00"].map { Decimal(string: $0)! })
        #expect(shown.map(\.kind) == [.purchase, .transfer, .purchase, .purchase, .purchase])
        #expect(shown.map(\.direction) == [.debit, .credit, .debit, .debit, .debit])
        let unnamed = try #require(shown.first)
        #expect(unnamed.merchant == nil && unnamed.categoryDisplayName == nil)
        #expect(model.hasMore)
        #expect(model.refreshError == nil)
    }

    @Test func fiveAreAskedForWithTheTokenAndNoPeriod() async {
        let app = TestApp { _ in json(200, adaLatest) }

        await RecentTransactionsViewModel(api: app.api).load()

        #expect(app.requests.map(\.method) == ["GET"])
        #expect(app.requests.map(\.path) == ["/api/transactions"])
        // The API's own default period (last three months) decides; the phone does not know the business day.
        #expect(app.requests.map(\.query) == ["limit=5"])
        #expect(app.requests.first?.authorization == "Bearer token-abc")
    }

    @Test func allOfThemFittingMeansNoLinkToMore() async {
        let app = TestApp { _ in json(200, result([item(1)], hasMore: false)) }
        let model = RecentTransactionsViewModel(api: app.api)

        await model.load()

        #expect(!model.hasMore)
    }

    @Test(arguments: [#""kind": "Purchase""#, #""direction": "Debit""#])
    func anUnknownKindOrDirectionIsAContractMismatch(field: String) async {
        let broken = adaLatest.replacingOccurrences(of: field, with: field.replacingOccurrences(of: "Purchase", with: "Refund")
            .replacingOccurrences(of: "Debit", with: "Out"))
        let app = TestApp { _ in json(200, broken) }
        let model = RecentTransactionsViewModel(api: app.api)

        await model.load()

        #expect(model.state == .failed("Beklenmeyen bir cevap alındı; tekrar deneyin."))
    }

    @Test func noTransactionsIsTheEmptyState() async {
        let app = TestApp { _ in json(200, result([])) }
        let model = RecentTransactionsViewModel(api: app.api)

        await model.load()

        #expect(model.state == .empty)
    }

    @Test func aServerErrorShowsTheAPIsTitle() async {
        let app = TestApp { _ in problem(500, "internal_error", "Beklenmeyen bir hata oluştu.") }
        let model = RecentTransactionsViewModel(api: app.api)

        await model.load()

        #expect(model.state == .failed("Beklenmeyen bir hata oluştu."))
    }

    @Test func aRefusedTokenGoesBackToLoginWithoutAnError() async {
        let app = TestApp { _ in problem(401, "session_invalid", "Oturum geçersiz; yeniden giriş yapın.") }
        let model = RecentTransactionsViewModel(api: app.api)

        await model.load()

        #expect(!app.session.isSignedIn)
        #expect(model.state == .loading)
    }

    @Test func aFailedRefreshKeepsTheList() async {
        let answers = Answers([{ json(200, adaLatest) }, { throw URLError(.cannotConnectToHost) }])
        let app = TestApp(answers.next)
        let model = RecentTransactionsViewModel(api: app.api)

        await model.load()
        await model.load()

        #expect(recent(model.state).count == 5)
        #expect(model.refreshError == unreachableMessage)
    }
}

// K7-B + K11-D: every transaction of the API's period (today the last three months), 20 at a time while scrolling.
@MainActor
struct TransactionHistoryViewModelTests {
    @Test func theFirstPageAndItsPeriodArrive() async {
        let app = TestApp { _ in json(200, page(1...20, hasMore: true)) }
        let model = TransactionHistoryViewModel(api: app.api)

        await model.loadMore()

        #expect(numbers(model.items) == Array(1...20))
        #expect(model.hasMore)
        #expect(model.periodText == "10 Temmuz 2026 – 10 Ekim 2026")
        #expect(app.requests.map(\.query) == ["limit=20"])
    }

    @Test func scrollingOnAsksForTheNextPageAfterWhatIsShown() async {
        let answers = Answers([{ json(200, page(1...20, hasMore: true)) }, { json(200, page(21...40, hasMore: true)) },
                               { json(200, page(41...45, hasMore: false)) }])
        let app = TestApp(answers.next)
        let model = TransactionHistoryViewModel(api: app.api)

        await model.loadMore()
        await model.loadMore()
        await model.loadMore()

        #expect(numbers(model.items) == Array(1...45))
        #expect(app.requests.map(\.query) == ["limit=20", "limit=20&offset=20", "limit=20&offset=40"])
        #expect(!model.hasMore)
    }

    @Test func afterTheLastPageNothingMoreIsAsked() async {
        let app = TestApp { _ in json(200, page(1...5, hasMore: false)) }
        let model = TransactionHistoryViewModel(api: app.api)

        await model.loadMore()
        await model.loadMore()

        #expect(app.requests.count == 1)
    }

    // API-17: a transaction added while scrolling pushes the last one of a page onto the next; it is shown once.
    @Test func aTransactionRepeatedAtAPageEdgeIsShownOnce() async {
        let answers = Answers([{ json(200, page(1...20, hasMore: true)) }, { json(200, page(20...39, hasMore: false)) }])
        let app = TestApp(answers.next)
        let model = TransactionHistoryViewModel(api: app.api)

        await model.loadMore()
        await model.loadMore()

        #expect(numbers(model.items) == Array(1...39))
    }

    @Test func aFailedNextPageKeepsTheListAndIsAskedAgain() async {
        let answers = Answers([{ json(200, page(1...20, hasMore: true)) }, { throw URLError(.cannotConnectToHost) },
                               { json(200, page(21...25, hasMore: false)) }])
        let app = TestApp(answers.next)
        let model = TransactionHistoryViewModel(api: app.api)

        await model.loadMore()
        await model.loadMore()
        #expect(numbers(model.items) == Array(1...20))
        #expect(model.errorMessage == unreachableMessage)
        #expect(model.hasMore)

        await model.loadMore()

        #expect(numbers(model.items) == Array(1...25))
        #expect(model.errorMessage == nil)
        #expect(app.requests.map(\.query) == ["limit=20", "limit=20&offset=20", "limit=20&offset=20"])
    }

    @Test func aFailedFirstPageIsAnErrorWithNothingShown() async {
        let app = TestApp { _ in problem(500, "internal_error", "Beklenmeyen bir hata oluştu.") }
        let model = TransactionHistoryViewModel(api: app.api)

        await model.loadMore()

        #expect(model.items.isEmpty)
        #expect(model.errorMessage == "Beklenmeyen bir hata oluştu.")
        #expect(!model.isEmpty)
    }

    @Test func noTransactionsIsEmpty() async {
        let app = TestApp { _ in json(200, result([])) }
        let model = TransactionHistoryViewModel(api: app.api)

        #expect(!model.isEmpty)
        await model.loadMore()

        #expect(model.isEmpty)
    }

    @Test func refreshingStartsAgainFromTheNewest() async {
        let answers = Answers([{ json(200, page(1...20, hasMore: true)) }, { json(200, page(21...40, hasMore: true)) },
                               { json(200, page(101...120, hasMore: true)) }])
        let app = TestApp(answers.next)
        let model = TransactionHistoryViewModel(api: app.api)

        await model.loadMore()
        await model.loadMore()
        await model.refresh()

        #expect(numbers(model.items) == Array(101...120))
        #expect(app.requests.last?.query == "limit=20")
    }

    @Test func aFailedRefreshKeepsWhatIsShown() async {
        let answers = Answers([{ json(200, page(1...20, hasMore: true)) }, { throw URLError(.cannotConnectToHost) }])
        let app = TestApp(answers.next)
        let model = TransactionHistoryViewModel(api: app.api)

        await model.loadMore()
        await model.refresh()

        #expect(numbers(model.items) == Array(1...20))
        #expect(model.errorMessage == unreachableMessage)
    }

    @Test func aSecondScrollWhileAPageIsComingAsksOnce() async throws {
        let app = TestApp(delay: 0.3) { _ in json(200, page(1...20, hasMore: true)) }
        let model = TransactionHistoryViewModel(api: app.api)

        let first = Task { await model.loadMore() }
        try await waitUntil { model.isLoading }
        await model.loadMore()
        await first.value

        #expect(app.requests.count == 1)
        #expect(model.items.count == 20)
    }

    @Test func aRefusedTokenGoesBackToLoginWithoutAnError() async {
        let app = TestApp { _ in problem(401, "session_invalid", "Oturum geçersiz; yeniden giriş yapın.") }
        let model = TransactionHistoryViewModel(api: app.api)

        await model.loadMore()

        #expect(!app.session.isSignedIn)
        #expect(model.errorMessage == nil)
    }

    @Test func leavingTheScreenWhileLoadingShowsNothing() async throws {
        let app = TestApp(delay: 2) { _ in json(200, page(1...20, hasMore: true)) }
        let model = TransactionHistoryViewModel(api: app.api)

        let loading = Task { await model.loadMore() }
        try await waitUntil { !app.requests.isEmpty }
        loading.cancel()
        await loading.value

        #expect(model.items.isEmpty)
        #expect(model.errorMessage == nil)
        #expect(!model.isLoading)
    }
}

// K3-A, K4-A, K5-A, K6-A, K9-A: what one row says. Pure rules, so every kind is checked without a screen.
struct TransactionDisplayTests {
    @Test func aPurchaseIsNamedByItsMerchant() throws {
        let shown = TransactionDisplay(try decoded(item(1, amount: "165.00", merchant: "Starbucks", categoryCode: "FOOD",
                                                        category: "Yeme & İçme", raw: "POS 4412 STARBUCKS KADIKOY")))

        #expect(shown.title == "Starbucks")
        #expect(shown.subtitle == "Yeme & İçme · 8 Ekim 2026")
        #expect(shown.amount == "−165,00 TL")
        #expect(!shown.isIncoming)
        #expect(shown.symbol == "fork.knife")
    }

    @Test func aPurchaseWithoutMerchantShowsWhatTheBankRecordedAndIsNotOther() throws {
        let shown = TransactionDisplay(try decoded(item(1, amount: "145.00", raw: "POS 6604 ONLINE ODEME 4471",
                                                        localDate: "2026-10-09")))

        #expect(shown.title == "POS 6604 ONLINE ODEME 4471")
        // Unclassified is not "Diğer" (OTHER), a domain rule.
        #expect(shown.subtitle == "Sınıflandırılmamış · 9 Ekim 2026")
        #expect(shown.symbol == "creditcard")
    }

    @Test func anIncomingTransferIsNamedByTheMaskedSender() throws {
        let shown = TransactionDisplay(try decoded(item(1, kind: "Transfer", direction: "Credit", amount: "300.00",
                                                        raw: "Gelen para transferi", counterparty: "Ege Y***")))

        #expect(shown.title == "Ege Y***")
        #expect(shown.subtitle == "Gelen transfer · 8 Ekim 2026")
        #expect(shown.amount == "+300,00 TL")
        #expect(shown.isIncoming)
        #expect(shown.symbol == "arrow.left.arrow.right")
    }

    @Test func anOutgoingTransferToAnOwnAccountIsNamedByThatAccount() throws {
        let shown = TransactionDisplay(try decoded(item(1, kind: "Transfer", direction: "Debit", amount: "1000.00",
                                                        raw: "Para gönderimi", counterparty: "Ada USD", ownAccount: true)))

        #expect(shown.title == "Ada USD")
        #expect(shown.subtitle == "Giden transfer · 8 Ekim 2026")
        #expect(shown.amount == "−1.000,00 TL")
    }

    @Test func aSalaryIsIncome() throws {
        let shown = TransactionDisplay(try decoded(item(1, kind: "Salary", direction: "Credit", amount: "45000.00",
                                                        raw: "Ada maaş ödemesi 2026-10", localDate: "2026-10-05")))

        #expect(shown.title == "Maaş")
        #expect(shown.subtitle == "Gelir · 5 Ekim 2026")
        #expect(shown.amount == "+45.000,00 TL")
        #expect(shown.symbol == "banknote")
    }

    @Test func aShopSaleIsNamedByTheMaskedBuyer() throws {
        let shown = TransactionDisplay(try decoded(item(1, kind: "Sale", direction: "Credit", amount: "95.00",
                                                        raw: "Kahve Durağı satış geliri", counterparty: "Ada K***",
                                                        localDate: "2026-10-07")))

        #expect(shown.title == "Ada K***")
        #expect(shown.subtitle == "Satış geliri · 7 Ekim 2026")
        #expect(shown.amount == "+95,00 TL")
        #expect(shown.symbol == "storefront")
    }

    @Test func aSaleWithoutBuyerAndTheOpeningFundingHaveFixedNames() throws {
        let sale = TransactionDisplay(try decoded(item(1, kind: "Sale", direction: "Credit", raw: "Satış")))
        let opening = TransactionDisplay(try decoded(item(2, kind: "OpeningFunding", direction: "Credit", amount: "500.00",
                                                          currency: "USD", raw: "Hesap açılış bakiyesi")))

        #expect(sale.title == "Satış")
        #expect(opening.title == "Açılış bakiyesi")
        #expect(opening.subtitle == "Açılış · 8 Ekim 2026")
        #expect(opening.amount == "+500,00 USD")
        #expect(opening.symbol == "building.columns")
    }

    @Test(arguments: [("FOOD", "fork.knife"), ("GROCERY", "cart"), ("TRANSPORTATION", "bus"), ("BILLS", "bolt"),
                      ("FUEL", "fuelpump"), ("NEW_CODE", "creditcard")])
    func aPurchaseSymbolFollowsItsCategory(code: String, symbol: String) throws {
        let shown = TransactionDisplay(try decoded(item(1, categoryCode: code, category: "X")))

        #expect(shown.symbol == symbol)
    }

    @Test(arguments: [("2026-10-08", "8 Ekim 2026"), ("2026-02-28", "28 Şubat 2026"), ("2026-08-01", "1 Ağustos 2026"),
                      ("2025-12-31", "31 Aralık 2025"), ("2026-13-01", "2026-13-01"), ("dün", "dün")])
    func aLocalDateIsWrittenInTurkish(localDate: String, expected: String) {
        #expect(TransactionDisplay.date(localDate) == expected)
    }
}

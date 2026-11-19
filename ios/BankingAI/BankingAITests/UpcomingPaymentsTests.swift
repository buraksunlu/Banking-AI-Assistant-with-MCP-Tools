import Foundation
import Testing
@testable import BankingAI

// DEMO-03: the home panel's third card is the next 30 days' recurring payments (K1-B, K2-A), from
// `GET /api/analytics/upcoming-payments?days=30`. Figures are Murat's on the live data on 19 November 2026 (checked by hand
// against the API): 8 payments, 9.519,97 TL. The phone writes the backend's dates, days and amounts; it adds nothing up.

private func payment(_ merchant: String, _ date: String, days: Int, amount: String, routine: String = "Subscription",
                     installment: String? = nil, overdue: Bool = false, currency: String = "TRY") -> String {
    let next = installment.map { "\"\($0)\"" } ?? "null"
    return """
    {"merchant":"\(merchant)","categoryCode":"OTHER","categoryDisplayName":"Diğer","accountName":"Murat aile TRY",
     "currencyCode":"\(currency)","estimatedDate":"\(date)","estimatedAmount":\(amount),"daysUntil":\(days),"isOverdue":\(overdue),
     "pattern":{"occurrenceCount":3,"latestAmount":\(amount),"minAmount":\(amount),"maxAmount":\(amount),"firstDate":"2026-08-25",
      "lastDate":"2026-10-25","minIntervalDays":30,"maxIntervalDays":31,"nextExpectedDate":"\(date)"},
     "routine":"\(routine)","installment":\(next)}
    """
}

private let murat = """
{"from":"2026-11-19","to":"2026-12-19","payments":[
 \(payment("Su Faturası", "2026-11-22", days: 3, amount: "380.0", routine: "Bill")),
 \(payment("IKEA", "2026-11-25", days: 6, amount: "2800.0", routine: "Installment", installment: "04/09")),
 \(payment("Spotify", "2026-12-03", days: 14, amount: "119.99")),
 \(payment("Apple Store", "2026-12-05", days: 16, amount: "4250.0", routine: "Installment", installment: "07/12")),
 \(payment("MACFit", "2026-12-05", days: 16, amount: "1250.0", routine: "Recurring")),
 \(payment("iCloud", "2026-12-09", days: 20, amount: "39.99")),
 \(payment("Turkcell", "2026-12-10", days: 21, amount: "450.0", routine: "Bill")),
 \(payment("Netflix", "2026-12-12", days: 23, amount: "229.99"))],
"totals":[{"currencyCode":"TRY","estimatedTotal":9519.97}]}
"""

private let none = #"{"from":"2026-11-19","to":"2026-12-19","payments":[],"totals":[]}"#

@MainActor
struct UpcomingPaymentsTests {
    @Test func theCardAsksTheNextThirtyDays() async {
        let app = TestApp { _ in json(200, murat) }
        let model = UpcomingPaymentsViewModel(api: app.api)

        await model.load()

        #expect(app.requests.map(\.path) == ["/api/analytics/upcoming-payments"])
        #expect(app.requests.map(\.query) == ["days=30"])
        #expect(app.requests.first?.authorization == "Bearer token-abc")
    }

    // K3-A: the backend's total, its three earliest payments and "Tümü (8)".
    @Test func theCardShowsTheTotalAndTheFirstThree() async throws {
        let app = TestApp { _ in json(200, murat) }
        let model = UpcomingPaymentsViewModel(api: app.api)

        await model.load()

        guard case .loaded(let payments) = model.state else { Issue.record("\(model.state)"); return }
        #expect(UpcomingDisplay.preview(payments).map(\.merchant) == ["Su Faturası", "IKEA", "Spotify"])
        #expect(model.totals.map(UpcomingDisplay.totalText) == ["9.519,97 TL"])
        #expect(UpcomingDisplay.allTitle(count: payments.count) == "Tümü (8)")
        #expect(UpcomingDisplay.allTitle(count: 3) == nil)
    }

    @Test func aRowWritesTheBackendsDateDaysTagAndAmount() throws {
        let answer = try JSONDecoder().decode(UpcomingPayments.self, from: Data(murat.utf8))
        let ikea = answer.payments[1]

        #expect(UpcomingDisplay.dateText(ikea) == "25 Kasım")
        #expect(UpcomingDisplay.whenText(ikea) == "6 gün sonra")
        #expect(UpcomingDisplay.tag(ikea) == "Taksit 4/9")
        #expect(UpcomingDisplay.tag(answer.payments[0]) == "Fatura")
        #expect(UpcomingDisplay.tag(answer.payments[4]) == "Düzenli ödeme")
        #expect(Money.text(ikea.estimatedAmount, currency: ikea.currencyCode) == "2.800,00 TL")
    }

    @Test(arguments: [(0, false, "bugün"), (1, false, "yarın"), (16, false, "16 gün sonra"), (-2, true, "2 gün gecikti")])
    func theDaysAreTheBackendsCount(days: Int, overdue: Bool, text: String) throws {
        let row = try JSONDecoder().decode(UpcomingPayment.self,
                                           from: Data(payment("Netflix", "2026-11-19", days: days, amount: "229.99", overdue: overdue).utf8))
        #expect(UpcomingDisplay.whenText(row) == text)
    }

    @Test func noPaymentIsAnEmptyCardNotAZero() async {
        let app = TestApp { _ in json(200, none) }
        let model = UpcomingPaymentsViewModel(api: app.api)

        await model.load()

        #expect(model.state == .empty)
        #expect(UpcomingDisplay.emptyText == "Önümüzdeki 30 günde beklenen düzenli ödeme yok.")
    }

    @Test func anUnknownReasonIsAnErrorNotAGuess() async {
        let app = TestApp { _ in json(200, murat.replacingOccurrences(of: "\"Recurring\"", with: "\"Sometimes\"")) }
        let model = UpcomingPaymentsViewModel(api: app.api)

        await model.load()

        guard case .failed = model.state else { Issue.record("\(model.state)"); return }
    }

    @Test func twoCurrenciesKeepTwoTotals() async {
        let body = """
        {"from":"2026-11-19","to":"2026-12-19","payments":[\(payment("Netflix", "2026-11-22", days: 3, amount: "229.99")),
         \(payment("Spotify", "2026-11-23", days: 4, amount: "9.99", currency: "USD"))],
        "totals":[{"currencyCode":"TRY","estimatedTotal":229.99},{"currencyCode":"USD","estimatedTotal":9.99}]}
        """
        let app = TestApp { _ in json(200, body) }
        let model = UpcomingPaymentsViewModel(api: app.api)

        await model.load()

        #expect(model.totals.map(UpcomingDisplay.totalText) == ["229,99 TL", "9,99 USD"])
    }

    @Test func aFailedRefreshKeepsTheList() async {
        let failure = #"{"title":"Beklenmeyen bir hata oluştu.","status":500,"code":"internal_error"}"#
        let answers = Answers([{ json(200, murat) }, { json(500, failure) }])
        let app = TestApp(answers.next)
        let model = UpcomingPaymentsViewModel(api: app.api)

        await model.load()
        await model.load()

        guard case .loaded(let payments) = model.state else { Issue.record("\(model.state)"); return }
        #expect(payments.count == 8)
        #expect(model.refreshError == "Beklenmeyen bir hata oluştu.")
        #expect(model.totals.map(UpcomingDisplay.totalText) == ["9.519,97 TL"])
    }
}

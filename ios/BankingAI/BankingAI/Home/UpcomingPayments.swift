import Foundation
import Observation

/// One expected recurring payment as `GET /api/analytics/upcoming-payments` returns it (ANA-12, DEMO-03). Date, days and
/// amount are the backend's estimates; `routine` is its reason and `installment` the next numbered one ("04/09").
struct UpcomingPayment: Decodable, Equatable, Identifiable {
    let merchant: String
    let categoryCode: String?
    let accountName: String
    let currencyCode: String
    let estimatedDate: String
    let estimatedAmount: Decimal
    let daysUntil: Int
    let isOverdue: Bool
    let routine: RoutineReason
    let installment: String?

    /// The backend groups by account, currency and merchant, so these name one payment.
    var id: String { "\(accountName)|\(currencyCode)|\(merchant)" }
}

/// The estimated total in one currency, the backend's sum.
struct UpcomingTotal: Decodable, Equatable {
    let currencyCode: String
    let estimatedTotal: Decimal
}

/// The whole answer: the window and the payments, earliest first.
struct UpcomingPayments: Decodable, Equatable {
    let from: String
    let to: String
    let payments: [UpcomingPayment]
    let totals: [UpcomingTotal]
}

extension APIRequest where Response == UpcomingPayments {
    /// `GET /api/analytics/upcoming-payments?days=30` (DEMO-03 K2-A, the assistant's own default window).
    static var upcomingPayments: APIRequest<UpcomingPayments> {
        APIRequest(method: "GET", path: "api/analytics/upcoming-payments",
                   query: [URLQueryItem(name: "days", value: String(UpcomingDisplay.days))], body: nil, requiresToken: true)
    }
}

/// The home panel's third card and its full list (DEMO-03): one request, shared by both, so "Tümü" sends nothing new (K4-A).
@Observable
final class UpcomingPaymentsViewModel {
    typealias State = ListState<UpcomingPayment>
    private let loader = ListLoader<UpcomingPayment>()
    var state: State { loader.state }
    var refreshError: String? { loader.refreshError }
    /// The totals of the last successful answer; a failed refresh keeps them with the list.
    private(set) var totals: [UpcomingTotal] = []
    @ObservationIgnored private let api: APIClient

    init(api: APIClient) { self.api = api }

    func load() async {
        await loader.load {
            let answer = try await self.api.send(.upcomingPayments)
            self.totals = answer.totals
            return answer.payments
        }
    }
}

/// The card's words, all from the backend's fields: the phone reads no calendar and adds nothing up.
enum UpcomingDisplay {
    static let days = 30
    static let previewRows = 3
    static let title = "Yaklaşan ödemeler"
    static let windowText = "Önümüzdeki \(days) gün"
    static let emptyText = "Önümüzdeki \(days) günde beklenen düzenli ödeme yok."
    static let note = "Tahmini; son ödemelere göre."

    static func preview(_ payments: [UpcomingPayment]) -> [UpcomingPayment] { Array(payments.prefix(previewRows)) }

    /// "Tümü (8)" when the card hides some; nil when it shows them all.
    static func allTitle(count: Int) -> String? { count > previewRows ? "Tümü (\(count))" : nil }

    static func totalText(_ total: UpcomingTotal) -> String { Money.text(total.estimatedTotal, currency: total.currencyCode) }

    /// "2026-11-25" → "25 Kasım": the window is a month, so the year would only crowd the row.
    static func dateText(_ payment: UpcomingPayment) -> String {
        let full = TransactionDisplay.date(payment.estimatedDate)
        return full == payment.estimatedDate ? full : full.split(separator: " ").prefix(2).joined(separator: " ")
    }

    /// The backend's `daysUntil` in words; an overdue payment says how late it is.
    static func whenText(_ payment: UpcomingPayment) -> String {
        if payment.isOverdue { return "\(-payment.daysUntil) gün gecikti" }
        switch payment.daysUntil {
        case 0: return "bugün"
        case 1: return "yarın"
        default: return "\(payment.daysUntil) gün sonra"
        }
    }

    /// The same tags as the breakdown's routine rows (DEMO-02): Abonelik / Fatura / Taksit 4/9 / Düzenli ödeme.
    static func tag(_ payment: UpcomingPayment) -> String {
        BreakdownDisplay.label(payment.routine, installment: payment.installment)
    }
}

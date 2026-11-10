import Foundation
import Observation

/// The "Gelire göre harcama" card of the home screen (INC-04): last month's spending against income and the rule-based advice,
/// as `spending-advice` returns them. Only whole months have a share (INC-01), so the card always asks for `LastMonth`; the
/// backend decides which month that is.
@Observable
final class IncomeShareViewModel {
    typealias State = ListState<CurrencyAdvice>
    private let loader = ListLoader<CurrencyAdvice>()
    var state: State { loader.state }
    var refreshError: String? { loader.refreshError }
    /// "Eylül 2026", from the backend's first day of the month; nil until an answer has come.
    private(set) var month: String?
    @ObservationIgnored private let api: APIClient

    init(api: APIClient) { self.api = api }

    /// Loads or refreshes; a failed refresh keeps the card and the month that are already there.
    func load() async {
        await loader.load {
            let advice = try await self.api.send(.spendingAdvice(period: .lastMonth))
            self.month = IncomeShareDisplay.month(advice.period.startDate)
            return advice.currencies
        }
    }
}

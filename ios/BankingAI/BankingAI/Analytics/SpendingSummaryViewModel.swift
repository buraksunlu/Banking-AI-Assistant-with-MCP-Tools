import Foundation
import Observation

/// The "Bu ay harcama" card of the home screen: the running month's spending as the backend sums it (UI-03 K4-A). It also
/// keeps the backend's last day of that month, which the header writes as today's date, so the phone never reads a calendar.
@Observable
final class SpendingSummaryViewModel {
    typealias State = ListState<CurrencySpending>
    private let loader = ListLoader<CurrencySpending>()
    var state: State { loader.state }
    var refreshError: String? { loader.refreshError }
    /// The `endDate` of the last successful answer, "2026-10-10"; nil until one has come.
    private(set) var endDate: String?
    @ObservationIgnored private let api: APIClient

    init(api: APIClient) { self.api = api }

    /// Loads or refreshes; a failed refresh keeps the figure and the day that are already there.
    func load() async {
        await loader.load {
            let summary = try await self.api.send(.spendingSummary(period: .thisMonth))
            self.endDate = summary.period.endDate
            return summary.currencies
        }
    }
}

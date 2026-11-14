import Foundation
import Observation

/// The first card of the home panel (UX-01 PR2): this month so far against the same days of the month before, as
/// `period-comparison?comparison=MonthToDate` returns it. It also keeps the backend's last day of the running month, which
/// the header writes as today's date and the card counts as "N gün", so the phone never reads a calendar (K7-A: it took the
/// place of the spending summary, the same figure from one request less).
@Observable
final class MonthPaceViewModel {
    typealias State = ListState<CurrencyComparison>
    private let loader = ListLoader<CurrencyComparison>()
    var state: State { loader.state }
    var refreshError: String? { loader.refreshError }
    /// The `current.endDate` of the last successful answer, "2026-10-10"; nil until one has come.
    private(set) var endDate: String?
    @ObservationIgnored private let api: APIClient

    init(api: APIClient) { self.api = api }

    /// Loads or refreshes; a failed refresh keeps the figures and the day that are already there.
    func load() async {
        await loader.load {
            let comparison = try await self.api.send(.monthToDateComparison)
            self.endDate = comparison.current.endDate
            return comparison.currencies
        }
    }
}

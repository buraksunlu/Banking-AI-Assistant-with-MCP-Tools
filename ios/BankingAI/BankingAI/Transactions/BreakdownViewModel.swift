import Foundation
import Observation

/// The breakdown behind a chat answer's spending link (DEMO-02): the backend's two groups per currency. A period the backend does
/// not break down (`periodTooLong`) leaves the screen to the plain, paged list.
@Observable
final class BreakdownViewModel {
    typealias State = ListState<CurrencyBreakdown>
    private let loader = ListLoader<CurrencyBreakdown>()
    var state: State { loader.state }
    var refreshError: String? { loader.refreshError }
    /// True once the backend answered that the period is too long to break down.
    private(set) var showsPlainList = false
    /// "5 Eylül 2026" or "1 Ekim 2026 – 31 Ekim 2026", the days the backend resolved.
    private(set) var periodText: String?
    @ObservationIgnored private let api: APIClient
    @ObservationIgnored private let filter: TransactionFilter
    @ObservationIgnored private let criteria: TransactionCriteria

    init(api: APIClient, filter: TransactionFilter, criteria: TransactionCriteria) {
        self.api = api
        self.filter = filter
        self.criteria = criteria
    }

    /// Loads, retries or refreshes; a failed refresh keeps what is shown.
    func load() async {
        await loader.load {
            let answer = try await self.api.send(.breakdown(filter: self.filter, criteria: self.criteria))
            self.showsPlainList = answer.periodTooLong
            let (start, end) = (answer.period.startDate, answer.period.endDate)
            self.periodText = start == end ? TransactionDisplay.date(start)
                : "\(TransactionDisplay.date(start)) – \(TransactionDisplay.date(end))"
            return answer.currencies
        }
    }
}

/// One transaction's place in the breakdown, for its detail screen (DEMO-02).
@Observable
final class TransactionInsightViewModel {
    enum State: Equatable {
        case loading
        case loaded(TransactionInsight)
        case failed(String)
    }

    private(set) var state: State = .loading
    @ObservationIgnored private let api: APIClient
    @ObservationIgnored private let id: UUID

    init(api: APIClient, id: UUID) {
        self.api = api
        self.id = id
    }

    /// Loads or retries; cancellation and an expired session show nothing.
    func load() async {
        if case .failed = state { state = .loading }
        do {
            state = .loaded(try await api.send(.insight(id: id)))
        } catch let error as APIError {
            if !error.isSilent { state = .failed(error.message) }
        } catch {
            state = .failed(APIError.invalidResponse(status: 0).message)
        }
    }
}

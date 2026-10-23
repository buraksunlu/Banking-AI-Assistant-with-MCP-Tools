import Foundation
import Observation

/// The category ring of the analysis screen: the data of the chosen period, the period choice and the slice the customer
/// tapped (IOS-08). The backend sums and shares; this class only keeps what the screen shows.
@Observable
final class CategoryBreakdownViewModel {
    enum State: Equatable {
        case loading
        case loaded([CurrencyCategoryBreakdown])
        case empty
        case failed(String)
    }

    private(set) var state: State = .loading
    /// The period the customer chose; after a failed refresh it goes back to the one the ring still shows.
    private(set) var period: AnalyticsPeriod = .thisMonth
    private(set) var refreshError: String?
    private var selection: [String: String] = [:]
    /// Counts requests, so an older answer arriving late is dropped instead of replacing a newer choice.
    @ObservationIgnored private var generation = 0
    @ObservationIgnored private var shownPeriod: AnalyticsPeriod = .thisMonth
    @ObservationIgnored private let api: APIClient

    init(api: APIClient) { self.api = api }

    /// Loads or refreshes the chosen period; a failed refresh keeps the ring that is already there.
    func load() async {
        generation += 1
        let mine = generation
        let asked = period
        do {
            let answer = try await api.send(.categoryBreakdown(period: asked))
            guard mine == generation else { return }
            if shownPeriod != asked { selection = [:] }
            shownPeriod = asked
            state = answer.currencies.isEmpty ? .empty : .loaded(answer.currencies)
            refreshError = nil
        } catch let error as APIError {
            guard mine == generation, !error.isSilent else { return }
            show(error.message)
        } catch {
            guard mine == generation else { return }
            show(APIError.invalidResponse(status: 0).message)
        }
    }

    /// Switches the period and loads it.
    func select(_ newPeriod: AnalyticsPeriod) async {
        period = newPeriod
        await load()
    }

    /// Taps a slice: selects it, or clears the selection when it was already selected (decision K8-A).
    func toggle(_ slice: CategorySpending, currency: String) {
        selection[currency] = selection[currency] == slice.id ? nil : slice.id
    }

    func selectedID(currency: String) -> String? { selection[currency] }

    private func show(_ message: String) {
        if case .loaded = state {
            // The ring still belongs to the period it was loaded for, so the choice snaps back to it.
            period = shownPeriod
            refreshError = message
        } else {
            state = .failed(message)
        }
    }
}

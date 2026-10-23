import Foundation
import Observation

/// The monthly bars of the analysis screen: the last six whole months and the running one, per currency, and the bar the
/// customer tapped (IOS-08 PR2). The backend sums the months; this class only keeps what the screen shows.
@Observable
final class MonthlyTrendViewModel {
    enum State: Equatable {
        case loading
        case loaded([CurrencyTrend])
        case empty
        case failed(String)
    }

    /// Six whole months plus the running one (decision K7-A); the spending is not filtered.
    static let months = 6

    private(set) var state: State = .loading
    private(set) var refreshError: String?
    private var selection: [String: String] = [:]
    /// Counts requests, so an older answer arriving late is dropped instead of replacing a newer one.
    @ObservationIgnored private var generation = 0
    @ObservationIgnored private let api: APIClient

    init(api: APIClient) { self.api = api }

    /// Loads or refreshes; a failed refresh keeps the chart that is already there.
    func load() async {
        generation += 1
        let mine = generation
        do {
            let answer = try await api.send(.monthlyTrend(months: Self.months))
            guard mine == generation else { return }
            state = answer.currencies.isEmpty ? .empty : .loaded(answer.currencies)
            refreshError = nil
            dropSelectionsOfMissingMonths(answer.currencies)
        } catch let error as APIError {
            guard mine == generation, !error.isSilent else { return }
            show(error.message)
        } catch {
            guard mine == generation else { return }
            show(APIError.invalidResponse(status: 0).message)
        }
    }

    /// Taps a bar: selects it, or clears the selection when it was already selected.
    func toggle(_ month: MonthlySpending, currency: String) {
        selection[currency] = selection[currency] == month.id ? nil : month.id
    }

    func selectedID(currency: String) -> String? { selection[currency] }

    private func dropSelectionsOfMissingMonths(_ currencies: [CurrencyTrend]) {
        selection = selection.filter { code, id in
            currencies.first { $0.currencyCode == code }?.months.contains { $0.id == id } == true
        }
    }

    private func show(_ message: String) {
        if case .loaded = state { refreshError = message } else { state = .failed(message) }
    }
}

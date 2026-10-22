import Foundation
import Observation

/// The five newest transactions of every account, under the accounts on the home screen (IOS-07). The same states and
/// refresh rules as ``AccountsViewModel``; the two are folded together when a third list arrives (IOS-05, decision K10-C).
@Observable
final class RecentTransactionsViewModel {
    enum State: Equatable {
        case loading
        case loaded([TransactionItem])
        case empty
        case failed(String)
    }

    static let count = 5

    private(set) var state: State = .loading
    /// More transactions than these five, so the screen offers the full list (K7-B).
    private(set) var hasMore = false
    /// A refresh that failed while a list was on screen: the list stays and this sentence shows above it.
    private(set) var refreshError: String?
    @ObservationIgnored private let api: APIClient

    init(api: APIClient) {
        self.api = api
    }

    /// The first load, a pull to refresh and "Tekrar dene" all come here.
    func load() async {
        if case .failed = state {
            state = .loading
        }
        do {
            let result = try await api.send(.transactions(limit: Self.count))
            state = result.items.isEmpty ? .empty : .loaded(result.items)
            hasMore = result.hasMore
            refreshError = nil
        } catch let error as APIError {
            // A 401 has already signed out (APIClient); the login screen replaces this one, nothing to show here.
            if case .problem(status: 401, _, _, _) = error { return }
            if case .cancelled = error { return }
            show(error.message)
        } catch {
            show(APIError.invalidResponse(status: 0).message)
        }
    }

    private func show(_ message: String) {
        if case .loaded = state {
            refreshError = message
        } else {
            state = .failed(message)
        }
    }
}

import Foundation
import Observation

/// The customer's accounts on the home screen (IOS-04): loading, none, an error, or the list in the API's order.
@Observable
final class AccountsViewModel {
    enum State: Equatable {
        case loading
        case loaded([Account])
        case empty
        case failed(String)
    }

    private(set) var state: State = .loading
    /// A refresh that failed while a list was on screen: the list stays and this sentence shows above it (K6-A).
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
            let accounts = try await api.send(.accounts)
            state = accounts.isEmpty ? .empty : .loaded(accounts)
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

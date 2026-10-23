import Foundation
import Observation

/// The four states shared by the small lists on the home screen.
enum ListState<Item: Equatable>: Equatable {
    case loading
    case loaded([Item])
    case empty
    case failed(String)
}

/// Loads a small list while preserving successful data when a refresh fails.
@Observable
final class ListLoader<Item: Equatable> {
    private(set) var state: ListState<Item> = .loading
    private(set) var refreshError: String?
    private var isLoading = false

    /// Runs the screen's request; cancellation and session expiry never become list errors.
    func load(_ request: () async throws -> [Item]) async {
        guard !isLoading else { return }
        isLoading = true
        defer { isLoading = false }
        if case .failed = state { state = .loading }
        do {
            let items = try await request()
            state = items.isEmpty ? .empty : .loaded(items)
            refreshError = nil
        } catch let error as APIError {
            if error.isSilent { return }
            show(error.message)
        } catch {
            show(APIError.invalidResponse(status: 0).message)
        }
    }

    private func show(_ message: String) {
        if case .loaded = state { refreshError = message }
        else { state = .failed(message) }
    }
}

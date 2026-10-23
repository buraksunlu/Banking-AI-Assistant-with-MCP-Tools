import Foundation
import Observation

/// The five newest transactions, using the same loading policy as the account list.
@Observable
final class RecentTransactionsViewModel {
    typealias State = ListState<TransactionItem>
    static let count = 5
    private let loader = ListLoader<TransactionItem>()
    var state: State { loader.state }
    var refreshError: String? { loader.refreshError }
    private(set) var hasMore = false
    @ObservationIgnored private let api: APIClient

    init(api: APIClient) { self.api = api }

    /// Loads the recent page; metadata changes only with a successful response.
    func load() async {
        await loader.load {
            let result = try await self.api.send(.transactions(limit: Self.count))
            self.hasMore = result.hasMore
            return result.items
        }
    }
}

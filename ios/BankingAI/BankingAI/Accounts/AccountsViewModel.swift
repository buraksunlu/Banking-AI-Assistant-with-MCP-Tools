import Foundation
import Observation

/// The customer's accounts in API order, sharing the home lists' loading and refresh policy.
@Observable
final class AccountsViewModel {
    typealias State = ListState<Account>
    private let loader = ListLoader<Account>()
    var state: State { loader.state }
    var refreshError: String? { loader.refreshError }
    @ObservationIgnored private let api: APIClient

    init(api: APIClient) { self.api = api }

    /// Loads or refreshes while preserving successful data on a connection error.
    func load() async {
        await loader.load { try await self.api.send(.accounts) }
    }

    /// A fresh navigation lifetime seeded from the tapped card.
    func detail(for account: Account) -> AccountDetailViewModel {
        AccountDetailViewModel(account: account, api: api)
    }
}

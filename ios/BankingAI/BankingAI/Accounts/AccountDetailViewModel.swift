import Foundation
import Observation

/// Shows a home card immediately and refreshes account details independently of the statement.
@Observable
final class AccountDetailViewModel {
    private(set) var account: Account
    let history: TransactionHistoryViewModel
    private(set) var accountError: String?
    private(set) var isRefreshingAccount = false
    private var missingAccount = false
    private var opened = false
    @ObservationIgnored private let api: APIClient

    init(account: Account, api: APIClient) {
        self.account = account
        self.api = api
        history = TransactionHistoryViewModel(api: api, accountID: account.id)
    }

    /// A 404 from either request invalidates the entire detail screen, including the stale home card.
    var isUnavailable: Bool { missingAccount || history.isUnavailable }

    /// Opening a new detail loads once; returning from a child preserves filter and position.
    func open() async {
        guard !opened else { return }
        opened = true
        async let header: Void = refreshAccount()
        async let statement: Void = history.loadMore()
        _ = await (header, statement)
    }

    /// Pull to refresh updates the card and starts the selected period again at its first page.
    func refresh() async {
        async let header: Void = refreshAccount()
        async let statement: Void = history.refresh()
        _ = await (header, statement)
    }

    /// Retains the last known balance on a network error; it never derives a balance from a page of rows.
    func refreshAccount() async {
        guard !isRefreshingAccount else { return }
        isRefreshingAccount = true
        defer { isRefreshingAccount = false }
        do {
            account = try await api.send(.account(account.id))
            accountError = nil
            missingAccount = false
        } catch let error as APIError {
            if error.isSilent { return }
            if case .problem(status: 404, _, _, _) = error {
                missingAccount = true
                history.cancel()
            } else { accountError = error.message }
        } catch { accountError = APIError.invalidResponse(status: 0).message }
    }
}

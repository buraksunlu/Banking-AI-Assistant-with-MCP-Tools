import Foundation
import Observation

/// "Tüm işlemler": the customer's whole history, 20 more each time the list reaches its end (IOS-07 K7-B, API-17). Each
/// page continues after the previous page's cursor, so a transaction added while scrolling never repeats a row.
@Observable
final class TransactionHistoryViewModel {
    static let pageSize = 20

    private(set) var items: [TransactionItem] = []
    /// The API's cursor for the page after what is shown; nil before the first page and after the last.
    @ObservationIgnored private var nextCursor: String?
    private(set) var hasMore = true
    private(set) var isLoading = false
    /// Why the last page did not come. The items already shown stay; the next `loadMore` asks for the same page again.
    private(set) var errorMessage: String?
    /// The searched days as the API resolved them, from the oldest transaction's day: "1 Nisan 2025 – 10 Ekim 2026".
    private(set) var periodText: String?
    @ObservationIgnored private var loadedOnce = false
    @ObservationIgnored private let api: APIClient

    init(api: APIClient) {
        self.api = api
    }

    /// The API answered and the customer has no transaction at all.
    var isEmpty: Bool { loadedOnce && items.isEmpty }

    /// The next page after what is shown; the first page when nothing is.
    func loadMore() async {
        guard hasMore, !isLoading else { return }
        await fetch(after: nextCursor)
    }

    /// Pull to refresh: back to the newest page. What is shown stays until the new page is there.
    func refresh() async {
        guard !isLoading else { return }
        await fetch(after: nil)
    }

    private func fetch(after cursor: String?) async {
        isLoading = true
        defer { isLoading = false }
        do {
            let page = try await api.send(.history(limit: Self.pageSize, cursor: cursor))
            items = (cursor == nil ? [] : items) + page.items
            nextCursor = page.nextCursor
            hasMore = page.nextCursor != nil
            periodText = "\(TransactionDisplay.date(page.period.startDate)) – \(TransactionDisplay.date(page.period.endDate))"
            errorMessage = nil
            loadedOnce = true
        } catch let error as APIError {
            if case .problem(status: 401, _, _, _) = error { return }
            if case .cancelled = error { return }
            errorMessage = error.message
        } catch {
            errorMessage = APIError.invalidResponse(status: 0).message
        }
    }
}

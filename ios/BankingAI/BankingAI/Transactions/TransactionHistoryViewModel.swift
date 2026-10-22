import Foundation
import Observation

/// "Tüm işlemler": every transaction of the API's period, 20 more each time the list reaches its end (IOS-07 K7-B).
/// The period is the API's default, today the last three months; the whole history comes with API-17 (K11-D).
@Observable
final class TransactionHistoryViewModel {
    static let pageSize = 20

    private(set) var items: [TransactionItem] = []
    private(set) var hasMore = true
    private(set) var isLoading = false
    /// Why the last page did not come. The items already shown stay; the next `loadMore` asks for the same page again.
    private(set) var errorMessage: String?
    /// The searched days as the API resolved them, "10 Temmuz 2026 – 10 Ekim 2026".
    private(set) var periodText: String?
    @ObservationIgnored private var loadedOnce = false
    @ObservationIgnored private let api: APIClient

    init(api: APIClient) {
        self.api = api
    }

    /// The API answered and there is nothing in its period.
    var isEmpty: Bool { loadedOnce && items.isEmpty }

    /// The next page after what is shown; the first page when nothing is.
    func loadMore() async {
        guard hasMore, !isLoading else { return }
        await fetch(offset: items.count)
    }

    /// Pull to refresh: back to the newest page. What is shown stays until the new page is there.
    func refresh() async {
        guard !isLoading else { return }
        await fetch(offset: 0)
    }

    private func fetch(offset: Int) async {
        isLoading = true
        defer { isLoading = false }
        do {
            let page = try await api.send(.transactions(limit: Self.pageSize, offset: offset))
            let kept = offset == 0 ? [] : items
            // Offset paging (API-17): a transaction added meanwhile pushes the last one of a page onto the next. Showing
            // it twice would also give the list two rows with one identity.
            let shown = Set(kept.map(\.id))
            items = kept + page.items.filter { !shown.contains($0.id) }
            hasMore = page.hasMore
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

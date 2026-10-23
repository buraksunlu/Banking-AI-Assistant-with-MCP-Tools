import Foundation
import Observation

/// A statement of all accounts or one account. Rows, period and cursor are committed together on success.
@Observable
final class TransactionHistoryViewModel {
    static let pageSize = 20
    private(set) var items: [TransactionItem] = []
    private(set) var filter: TransactionFilter = .allTime
    private(set) var totalCount = 0
    private(set) var hasMore = true
    private(set) var isLoading = false
    private(set) var errorMessage: String?
    private(set) var filterError: String?
    private(set) var periodText: String?
    private(set) var period: SearchedPeriod?
    private(set) var isUnavailable = false
    private(set) var loadedOnce = false
    @ObservationIgnored private var nextCursor: String?
    @ObservationIgnored private let accountID: UUID?
    @ObservationIgnored private let api: APIClient
    @ObservationIgnored private var generation = 0
    @ObservationIgnored private var pending: Task<TransactionSearchResult, Error>?
    @ObservationIgnored private var failedCursor: String?

    init(api: APIClient, accountID: UUID? = nil) {
        self.api = api
        self.accountID = accountID
    }

    /// True only after a successful empty response, never after an initial error.
    var isEmpty: Bool { loadedOnce && items.isEmpty }

    /// Distinguishes an unused account from a period without matches.
    var emptyMessage: String {
        filter != .allTime ? "Seçilen dönemde işlem yok." :
            (accountID == nil ? "Henüz işlem yok" : "Bu hesapta henüz işlem yok.")
    }

    /// Continues only the displayed query with its own opaque cursor.
    func loadMore() async {
        guard hasMore, !isLoading, !isUnavailable else { return }
        _ = await fetch(filter, after: nextCursor, applying: false)
    }

    /// Refreshes the displayed filter from the first page, preserving rows on failure.
    func refresh() async {
        guard !isLoading, !isUnavailable else { return }
        _ = await fetch(filter, after: nil, applying: false)
    }

    /// Repeats the failed operation, including the first page after a failed refresh.
    func retry() async {
        guard !isLoading, !isUnavailable else { return }
        _ = await fetch(filter, after: failedCursor, applying: false)
    }

    /// Supersedes a pending page; a new selection commits only after its first page succeeds.
    func apply(_ selection: TransactionFilter) async -> Bool {
        if let message = selection.validationMessage {
            filterError = message
            return false
        }
        return await fetch(selection, after: nil, applying: true)
    }

    /// Starts a new panel draft without the previous draft's error.
    func clearFilterError() { filterError = nil }

    /// Invalidates the result even if network cancellation arrives too late.
    func cancel() {
        generation += 1
        pending?.cancel()
        pending = nil
        isLoading = false
    }

    private func fetch(_ selection: TransactionFilter, after cursor: String?, applying: Bool) async -> Bool {
        generation += 1
        let ticket = generation
        pending?.cancel()
        isLoading = true
        if applying { filterError = nil }
        else { errorMessage = nil }
        let request = APIRequest<TransactionSearchResult>.history(accountID: accountID, filter: selection,
                                                                  limit: Self.pageSize, cursor: cursor)
        let task = Task { try await api.send(request) }
        pending = task
        defer {
            if ticket == generation {
                isLoading = false
                pending = nil
            }
        }
        do {
            let page = try await withTaskCancellationHandler {
                try await task.value
            } onCancel: {
                task.cancel()
            }
            guard ticket == generation, !Task.isCancelled else { return false }
            items = (cursor == nil ? [] : items) + page.items
            filter = selection
            nextCursor = page.nextCursor
            hasMore = page.nextCursor != nil
            totalCount = page.totalCount
            period = page.period
            periodText = "\(TransactionDisplay.date(page.period.startDate)) – \(TransactionDisplay.date(page.period.endDate))"
            errorMessage = nil
            filterError = nil
            isUnavailable = false
            loadedOnce = true
            return true
        } catch {
            guard ticket == generation, !Task.isCancelled else { return false }
            let failure = error as? APIError ?? .invalidResponse(status: 0)
            if failure.isSilent { return false }
            if case .problem(status: 404, _, _, _) = failure { isUnavailable = true }
            if applying { filterError = failure.message }
            else {
                errorMessage = failure.message
                failedCursor = cursor
            }
            return false
        }
    }
}

import SwiftUI

/// The transactions behind a chat answer's figure. A spending link shows the backend's breakdown (DEMO-02): "Rutin ve temel",
/// "Rutin olmayan", the comparison and the advice; any other link, or a period the backend does not break down, shows the list
/// the link names, paged like every other list. The line on top says what the list shows, the days under it are the ones the
/// backend resolved, and the count is the backend's; the phone adds nothing up.
struct LinkedTransactionsView: View {
    private let summary: String
    @State private var model: TransactionHistoryViewModel
    @State private var breakdown: BreakdownViewModel?
    private let insight: (TransactionItem) -> TransactionInsightViewModel?

    init(link: AssistantLink, model: TransactionHistoryViewModel, breakdown: BreakdownViewModel? = nil,
         insight: @escaping (TransactionItem) -> TransactionInsightViewModel? = { _ in nil }) {
        summary = link.summary
        _model = State(initialValue: model)
        _breakdown = State(initialValue: breakdown)
        self.insight = insight
    }

    /// The breakdown, unless there is none or the backend left this period to the plain list.
    private var shownBreakdown: BreakdownViewModel? {
        guard let breakdown, !breakdown.showsPlainList else { return nil }
        return breakdown
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Layout.medium) {
                VStack(alignment: .leading, spacing: Layout.tight) {
                    Text(summary)
                        .font(Typography.title)
                        .foregroundStyle(Palette.primaryText.color)
                    if let shown = shownBreakdown {
                        if let period = shown.periodText { caption(period) }
                    } else {
                        if let period = model.periodText { caption(period) }
                        if model.loadedOnce { caption("\(model.totalCount) işlem") }
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                if let shown = shownBreakdown {
                    BreakdownView(model: shown, insight: insight)
                } else {
                    TransactionRows(model: model)
                }
            }
            .padding(.horizontal, Layout.regular)
            .padding(.top, Layout.small)
        }
        .blackScreen()
        .navigationTitle("Cevabın işlemleri")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await reload() }
        .task { await reload() }
    }

    private func reload() async {
        if let breakdown {
            await breakdown.load()
            if !breakdown.showsPlainList { return }
        }
        if model.items.isEmpty { await model.loadMore() }
        else { await model.refresh() }
    }

    private func caption(_ text: String) -> some View {
        Text(text).font(Typography.caption).foregroundStyle(Palette.secondaryText.color)
    }
}

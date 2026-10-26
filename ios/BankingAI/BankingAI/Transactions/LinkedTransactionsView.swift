import SwiftUI

/// The transactions behind a chat answer's figure: the list the link names, paged like every other list. The line on top says
/// what the list shows, the days under it are the ones the backend resolved, and the count is the backend's; the phone adds
/// nothing up.
struct LinkedTransactionsView: View {
    private let summary: String
    @State private var model: TransactionHistoryViewModel

    init(link: AssistantLink, model: TransactionHistoryViewModel) {
        summary = link.summary
        _model = State(initialValue: model)
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Layout.medium) {
                VStack(alignment: .leading, spacing: Layout.tight) {
                    Text(summary)
                        .font(Typography.title)
                        .foregroundStyle(Palette.primaryText.color)
                    if let period = model.periodText { caption(period) }
                    if model.loadedOnce { caption("\(model.totalCount) işlem") }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                TransactionRows(model: model)
            }
            .padding(.horizontal, Layout.regular)
            .padding(.top, Layout.small)
        }
        .blackScreen()
        .navigationTitle("Cevabın işlemleri")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await model.refresh() }
        .task {
            if model.items.isEmpty { await model.loadMore() }
            else { await model.refresh() }
        }
    }

    private func caption(_ text: String) -> some View {
        Text(text).font(Typography.caption).foregroundStyle(Palette.secondaryText.color)
    }
}

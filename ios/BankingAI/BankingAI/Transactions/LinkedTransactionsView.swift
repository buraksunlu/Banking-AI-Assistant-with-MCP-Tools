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
        List {
            Section {
                TransactionRows(model: model)
            } header: {
                VStack(alignment: .leading, spacing: 4) {
                    Text(summary).font(.subheadline.bold()).foregroundStyle(.primary)
                    if let period = model.periodText { Text(period) }
                    if model.loadedOnce { Text("\(model.totalCount) işlem") }
                }
                .textCase(nil)
            }
        }
        .navigationTitle("Cevabın işlemleri")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await model.refresh() }
        .task {
            if model.items.isEmpty { await model.loadMore() }
            else { await model.refresh() }
        }
    }
}

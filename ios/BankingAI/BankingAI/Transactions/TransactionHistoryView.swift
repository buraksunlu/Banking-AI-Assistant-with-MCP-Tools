import SwiftUI

/// The whole customer's history. Filtering stays on account details (IOS-05 K7-A).
struct TransactionHistoryView: View {
    let model: TransactionHistoryViewModel

    var body: some View {
        List {
            Section {
                TransactionRows(model: model)
            } header: {
                if let period = model.periodText { Text(period) }
            }
        }
        .navigationTitle("Tüm işlemler")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await model.refresh() }
        .task {
            if model.items.isEmpty { await model.loadMore() }
            else { await model.refresh() }
        }
    }
}

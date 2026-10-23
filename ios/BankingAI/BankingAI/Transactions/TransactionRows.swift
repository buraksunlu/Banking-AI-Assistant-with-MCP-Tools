import SwiftUI

/// Shared statement rows and paging states, without owning navigation.
struct TransactionRows: View {
    let model: TransactionHistoryViewModel

    var body: some View {
        ForEach(model.items) { item in
            NavigationLink(value: item) { TransactionRow(item: item) }
        }
        if model.isEmpty {
            ContentUnavailableView(model.emptyMessage, systemImage: "list.bullet.rectangle")
        }
        if let message = model.errorMessage {
            VStack(alignment: .leading, spacing: 8) {
                Label(message, systemImage: "wifi.exclamationmark").font(.subheadline)
                Button("Tekrar dene") { Task { await model.retry() } }.font(.subheadline.bold())
            }
        } else if model.hasMore || model.isLoading {
            ProgressView()
                .frame(maxWidth: .infinity)
                .task(id: model.items.count) { await model.loadMore() }
        } else if !model.items.isEmpty {
            Text("Bütün işlemler gösterildi.")
                .font(.footnote).foregroundStyle(.secondary).frame(maxWidth: .infinity)
        }
    }
}

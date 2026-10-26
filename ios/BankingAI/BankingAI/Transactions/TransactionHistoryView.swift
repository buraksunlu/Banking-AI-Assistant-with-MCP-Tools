import SwiftUI

/// The whole customer's history. Filtering stays on account details (IOS-05 K7-A).
struct TransactionHistoryView: View {
    let model: TransactionHistoryViewModel

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Layout.medium) {
                if let period = model.periodText {
                    Text(period)
                        .font(Typography.caption)
                        .foregroundStyle(Palette.secondaryText.color)
                }
                TransactionRows(model: model)
            }
            .padding(.horizontal, Layout.regular)
            .padding(.top, Layout.small)
        }
        .blackScreen()
        .navigationTitle("Tüm işlemler")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await model.refresh() }
        .task {
            if model.items.isEmpty { await model.loadMore() }
            else { await model.refresh() }
        }
    }
}

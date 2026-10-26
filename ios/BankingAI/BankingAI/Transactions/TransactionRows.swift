import SwiftUI

/// Shared statement rows and paging states in one card (UI-05 K1-B), without owning navigation or scrolling: the screen puts
/// this in its `ScrollView`. The card is lazy, so the "next page" spinner is only built, and only asks for more, once the
/// reader has scrolled to it.
struct TransactionRows: View {
    let model: TransactionHistoryViewModel

    var body: some View {
        if model.isEmpty {
            // A failed refresh of an empty list must still say so; otherwise the empty result looks current.
            if let message = model.errorMessage {
                StatusBand(text: message, actionTitle: "Tekrar dene") { Task { await model.retry() } }
            }
            ContentUnavailableView(model.emptyMessage, systemImage: "list.bullet.rectangle")
        } else {
            LazyVStack(spacing: 0) {
                ForEach(model.items) { item in
                    NavigationLink(value: item) {
                        TransactionRow(item: item)
                            .padding(.horizontal, Layout.regular)
                            .padding(.vertical, Layout.medium)
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    HairlineDivider(leading: 64)
                }
                footer
            }
            .cardSurface(padding: 0)
        }
    }

    @ViewBuilder private var footer: some View {
        if let message = model.errorMessage {
            StatusBand(text: message, actionTitle: "Tekrar dene") { Task { await model.retry() } }
                .padding(Layout.medium)
        } else if model.hasMore || model.isLoading {
            ProgressView()
                .tint(Palette.secondaryText.color)
                .frame(maxWidth: .infinity)
                .padding(Layout.regular)
                .task(id: model.items.count) { await model.loadMore() }
        } else if !model.items.isEmpty {
            Text("Bütün işlemler gösterildi.")
                .font(Typography.caption)
                .foregroundStyle(Palette.secondaryText.color)
                .frame(maxWidth: .infinity)
                .padding(Layout.regular)
        }
    }
}

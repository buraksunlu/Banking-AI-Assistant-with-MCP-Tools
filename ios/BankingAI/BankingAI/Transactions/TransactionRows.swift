import SwiftUI

/// Shared statement rows and paging states in one card (UI-05 K1-B), without owning navigation or scrolling: the screen puts
/// this in its `ScrollView`. The card is lazy, so the "next page" placeholder rows are only built, and only ask for more, once
/// the reader has scrolled to them.
struct TransactionRows: View {
    let model: TransactionHistoryViewModel

    var body: some View {
        if model.isEmpty {
            // A failed refresh of an empty list must still say so; otherwise the empty result looks current.
            if let message = model.errorMessage {
                StatusBand(text: message, actionTitle: "Tekrar dene") { Task { await model.retry() } }
            }
            StateMessage(title: model.emptyMessage, symbol: "list.bullet.rectangle", size: .screen)
        } else if model.items.isEmpty && model.errorMessage == nil {
            // Before the first page: a card of placeholder rows that asks for the page like the footer does (the model drops a
            // second ask while one is running).
            TransactionSkeletonRows(items: Skeleton.history(firstPage: true))
                .cardSurface(padding: 0)
                .skeleton()
                .task { await model.loadMore() }
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
            TransactionSkeletonRows(items: Skeleton.history(firstPage: false))
                .skeleton()
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

/// Placeholder statement rows: the real ``TransactionRow`` with made-up items, for `.skeleton()` to grey out (UI-06 K1-C).
struct TransactionSkeletonRows: View {
    let items: [TransactionItem]
    var verticalPadding = Layout.medium

    var body: some View {
        VStack(spacing: 0) {
            ForEach(items) { item in
                TransactionRow(item: item)
                    .padding(.horizontal, Layout.regular)
                    .padding(.vertical, verticalPadding)
                if item.id != items.last?.id { HairlineDivider(leading: 64) }
            }
        }
    }
}

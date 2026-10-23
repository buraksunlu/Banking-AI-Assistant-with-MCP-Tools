import SwiftUI

/// "Tüm işlemler" (IOS-07 K7-B): the list grows by a page whenever its end comes into view. It shows
/// ``TransactionHistoryViewModel``'s state; there is no logic here.
struct TransactionHistoryView: View {
    let model: TransactionHistoryViewModel

    var body: some View {
        Group {
            if model.isEmpty {
                ContentUnavailableView("Henüz işlem yok", systemImage: "list.bullet.rectangle")
            } else if model.items.isEmpty, let message = model.errorMessage {
                ContentUnavailableView {
                    Label("İşlemler yüklenemedi", systemImage: "exclamationmark.triangle")
                } description: {
                    Text(message)
                } actions: {
                    Button("Tekrar dene") {
                        Task { await model.loadMore() }
                    }
                    .buttonStyle(.bordered)
                }
            } else {
                list
            }
        }
        .navigationTitle("Tüm işlemler")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await model.refresh() }
        // Opening the screen again starts from the newest; what was shown stays until the new page is there.
        .task {
            if model.items.isEmpty {
                await model.loadMore()
            } else {
                await model.refresh()
            }
        }
    }

    private var list: some View {
        List {
            Section {
                ForEach(model.items) { item in
                    TransactionRow(item: item)
                }
                footer
            } header: {
                if let period = model.periodText {
                    Text(period)
                }
            }
        }
    }

    @ViewBuilder
    private var footer: some View {
        if let message = model.errorMessage {
            VStack(alignment: .leading, spacing: 8) {
                Label(message, systemImage: "wifi.exclamationmark")
                    .font(.subheadline)
                Button("Tekrar dene") {
                    Task { await model.loadMore() }
                }
                .font(.subheadline.bold())
            }
        } else if model.hasMore {
            // Coming into view asks for the next page; keyed on the count so a short page that leaves it in view asks again.
            ProgressView()
                .frame(maxWidth: .infinity)
                .task(id: model.items.count) { await model.loadMore() }
        } else if !model.items.isEmpty {
            Text("Bütün işlemler gösterildi.")
                .font(.footnote)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity)
        }
    }
}

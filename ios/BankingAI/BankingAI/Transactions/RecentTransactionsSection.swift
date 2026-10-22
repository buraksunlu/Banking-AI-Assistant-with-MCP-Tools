import SwiftUI

/// Pushes "Tüm işlemler"; ``HomeView`` builds the screen only when it is opened.
struct TransactionHistoryRoute: Hashable {}

/// The "Son işlemler" part of the home screen. It shows ``RecentTransactionsViewModel``'s state; there is no logic here.
struct RecentTransactionsSection: View {
    let model: RecentTransactionsViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Son işlemler")
                .font(.title3.bold())
            switch model.state {
            case .loading:
                ProgressView()
                    .frame(maxWidth: .infinity, minHeight: 120)
            case .empty:
                ContentUnavailableView("Son 3 ayda işlem yok", systemImage: "list.bullet.rectangle",
                                       description: Text("Yeni işlemler burada görünür."))
            case .failed(let message):
                ContentUnavailableView {
                    Label("İşlemler yüklenemedi", systemImage: "exclamationmark.triangle")
                } description: {
                    Text(message)
                } actions: {
                    Button("Tekrar dene") {
                        Task { await model.load() }
                    }
                    .buttonStyle(.bordered)
                }
            case .loaded(let items):
                if let message = model.refreshError {
                    Label(message, systemImage: "wifi.exclamationmark")
                        .font(.subheadline)
                        .padding(12)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(Color.orange.opacity(0.15), in: RoundedRectangle(cornerRadius: 12))
                }
                VStack(spacing: 0) {
                    ForEach(items) { item in
                        TransactionRow(item: item)
                            .padding(.horizontal)
                            .padding(.vertical, 10)
                        if item.id != items.last?.id {
                            Divider()
                                .padding(.leading, 64)
                        }
                    }
                    if model.hasMore {
                        Divider()
                        NavigationLink(value: TransactionHistoryRoute()) {
                            HStack {
                                Text("Tüm işlemleri gör")
                                Spacer()
                                Image(systemName: "chevron.right")
                                    .font(.footnote.bold())
                            }
                            .font(.subheadline.weight(.semibold))
                            .padding()
                            .contentShape(Rectangle())
                        }
                    }
                }
                .background(Color(.secondarySystemGroupedBackground), in: RoundedRectangle(cornerRadius: 16))
            }
        }
        // Like the accounts: only a list that never arrived is asked for again when the screen comes back.
        .task {
            if model.state == .loading { await model.load() }
        }
    }
}

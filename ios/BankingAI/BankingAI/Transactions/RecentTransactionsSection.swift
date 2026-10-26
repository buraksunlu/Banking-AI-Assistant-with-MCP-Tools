import SwiftUI

/// Pushes "Tüm işlemler"; ``HomeView`` builds the screen only when it is opened.
struct TransactionHistoryRoute: Hashable {}

/// The "Son işlemler" part of the home screen. It shows ``RecentTransactionsViewModel``'s state; there is no logic here.
struct RecentTransactionsSection: View {
    let model: RecentTransactionsViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            SectionTitle(text: "Son işlemler")
            switch model.state {
            case .loading:
                ProgressView()
                    .tint(Palette.secondaryText.color)
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
                    .buttonStyle(SecondaryButtonStyle())
                }
            case .loaded(let items):
                if let message = model.refreshError {
                    StatusBand(text: message)
                }
                VStack(spacing: 0) {
                    ForEach(items) { item in
                        NavigationLink(value: item) {
                            TransactionRow(item: item)
                                .padding(.horizontal)
                                .padding(.vertical, 10)
                        }
                        .buttonStyle(.plain)
                        if item.id != items.last?.id {
                            HairlineDivider(leading: 64)
                        }
                    }
                    if model.hasMore {
                        HairlineDivider()
                        NavigationLink(value: TransactionHistoryRoute()) {
                            HStack {
                                Text("Tüm işlemleri gör")
                                Spacer()
                                Image(systemName: "chevron.right")
                                    .font(.footnote.bold())
                            }
                            .font(.subheadline.weight(.semibold))
                            .foregroundStyle(Palette.accent.color)
                            .padding()
                            .contentShape(Rectangle())
                        }
                    }
                }
                .cardSurface(padding: 0)
            }
        }
        // Like the accounts: only a list that never arrived is asked for again when the screen comes back.
        .task {
            if model.state == .loading { await model.load() }
        }
    }
}

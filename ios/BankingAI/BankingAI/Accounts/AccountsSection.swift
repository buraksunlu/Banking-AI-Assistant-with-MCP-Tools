import SwiftUI

/// The "Hesaplarım" part of the home screen. It shows ``AccountsViewModel``'s state; there is no logic here.
struct AccountsSection: View {
    let model: AccountsViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            SectionTitle(text: "Hesaplarım")
            switch model.state {
            case .loading:
                VStack(spacing: Layout.medium) {
                    ForEach(Skeleton.accounts) { AccountRow(account: $0) }
                }
                .skeleton()
            case .empty:
                StateMessage(title: "Henüz hesabınız yok", symbol: "creditcard", message: "Açılan hesaplar burada görünür.")
                    .cardSurface(padding: 0)
            case .failed(let message):
                StateMessage(title: "Hesaplar yüklenemedi", symbol: "exclamationmark.triangle", message: message,
                             actionTitle: "Tekrar dene") { Task { await model.load() } }
                    .cardSurface(padding: 0)
            case .loaded(let accounts):
                if let message = model.refreshError {
                    StatusBand(text: message)
                }
                ForEach(accounts) { account in
                    NavigationLink {
                        AccountDetailView(model: model.detail(for: account))
                    } label: {
                        AccountRow(account: account)
                    }
                    .buttonStyle(.plain)
                    .accessibilityHint("Hesap bilgilerini ve hareketlerini açar")
                }
            }
        }
        .motion(value: model.state)
        // Coming back from the chat runs this again; the balances cannot have changed there, so only a list that never
        // arrived is asked for (decision IOS-04 K5-A: refreshing is the user's pull).
        .task {
            if model.state == .loading { await model.load() }
        }
    }
}

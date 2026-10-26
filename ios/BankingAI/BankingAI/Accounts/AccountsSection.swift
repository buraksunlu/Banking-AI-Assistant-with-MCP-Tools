import SwiftUI

/// The "Hesaplarım" part of the home screen. It shows ``AccountsViewModel``'s state; there is no logic here.
struct AccountsSection: View {
    let model: AccountsViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            SectionTitle(text: "Hesaplarım")
            switch model.state {
            case .loading:
                ProgressView()
                    .tint(Palette.secondaryText.color)
                    .frame(maxWidth: .infinity, minHeight: 120)
            case .empty:
                ContentUnavailableView("Henüz hesabınız yok", systemImage: "creditcard",
                                       description: Text("Açılan hesaplar burada görünür."))
            case .failed(let message):
                ContentUnavailableView {
                    Label("Hesaplar yüklenemedi", systemImage: "exclamationmark.triangle")
                } description: {
                    Text(message)
                } actions: {
                    Button("Tekrar dene") {
                        Task { await model.load() }
                    }
                    .buttonStyle(SecondaryButtonStyle())
                }
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
        // Coming back from the chat runs this again; the balances cannot have changed there, so only a list that never
        // arrived is asked for (decision IOS-04 K5-A: refreshing is the user's pull).
        .task {
            if model.state == .loading { await model.load() }
        }
    }
}

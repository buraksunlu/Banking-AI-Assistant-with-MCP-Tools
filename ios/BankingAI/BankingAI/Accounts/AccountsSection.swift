import SwiftUI

/// The "Hesaplarım" part of the home screen. It shows ``AccountsViewModel``'s state; there is no logic here.
struct AccountsSection: View {
    let model: AccountsViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Hesaplarım")
                .font(.title3.bold())
            switch model.state {
            case .loading:
                ProgressView()
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
                    .buttonStyle(.bordered)
                }
            case .loaded(let accounts):
                if let message = model.refreshError {
                    Label(message, systemImage: "wifi.exclamationmark")
                        .font(.subheadline)
                        .padding(12)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(Color.orange.opacity(0.15), in: RoundedRectangle(cornerRadius: 12))
                }
                ForEach(accounts) { account in
                    AccountRow(account: account)
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

import SwiftUI

/// One account's refreshed balance and statement. The filter never changes what the balance means.
struct AccountDetailView: View {
    @State var model: AccountDetailViewModel
    @State private var showingFilter = false
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        Group {
            if model.isUnavailable {
                ContentUnavailableView {
                    Label("Hesap bulunamadı", systemImage: "creditcard.trianglebadge.exclamationmark")
                } description: {
                    Text("Hesap bulunamadı veya artık erişilemiyor.")
                } actions: {
                    Button("Hesaplara dön") { dismiss() }
                }
            } else { statement }
        }
        .navigationTitle("Hesap detayı")
        .navigationBarTitleDisplayMode(.inline)
        .task { await model.open() }
        .sheet(isPresented: $showingFilter) { TransactionFilterSheet(model: model.history) }
    }

    private var statement: some View {
        List {
            Section {
                VStack(alignment: .leading, spacing: 10) {
                    Text(model.account.accountName).font(.title2.bold())
                    Text(model.account.accountNumber).font(.subheadline).textSelection(.enabled)
                    Label(model.account.accountType == .business ? "İşletme hesabı" : "Kişisel hesap",
                          systemImage: model.account.accountType == .business ? "storefront" : "creditcard")
                        .font(.subheadline).foregroundStyle(.secondary)
                    Divider()
                    Text("Güncel bakiye").font(.caption).foregroundStyle(.secondary)
                    Text(Money.text(model.account.balance, currency: model.account.currencyCode))
                        .font(.title.bold()).monospacedDigit().textSelection(.enabled)
                    if model.isRefreshingAccount { ProgressView("Hesap bilgileri yenileniyor…").font(.caption) }
                }
                .padding(.vertical, 8)
                if let message = model.accountError {
                    VStack(alignment: .leading, spacing: 8) {
                        Label("Hesap bilgileri yenilenemedi. \(message)", systemImage: "wifi.exclamationmark")
                            .font(.subheadline).foregroundStyle(.orange)
                        Button("Tekrar dene") { Task { await model.refreshAccount() } }
                    }
                }
            }
            Section {
                TransactionRows(model: model.history)
            } header: {
                VStack(alignment: .leading, spacing: 8) {
                    ViewThatFits(in: .horizontal) {
                        HStack {
                            Text("Hesap hareketleri")
                            Spacer()
                            filterButton
                        }
                        VStack(alignment: .leading, spacing: 8) {
                            Text("Hesap hareketleri")
                            filterButton
                        }
                    }
                    if let period = model.history.periodText { Text(period).font(.caption) }
                    if model.history.loadedOnce { Text("\(model.history.totalCount) işlem").font(.caption) }
                }
                .textCase(nil).padding(.vertical, 4)
            }
        }
        .refreshable { await model.refresh() }
    }

    private var filterButton: some View {
        Button {
            model.history.clearFilterError()
            showingFilter = true
        } label: {
            Label(model.history.filter.title, systemImage: "line.3.horizontal.decrease")
        }
        .buttonStyle(.bordered)
        .accessibilityLabel("İşlem dönemi: \(model.history.filter.title)")
        .accessibilityHint("Dönem ve tarih aralığı seçer")
    }
}

import SwiftUI

/// One account's refreshed balance and statement. The filter never changes what the balance means.
struct AccountDetailView: View {
    @State var model: AccountDetailViewModel
    @State private var showingFilter = false
    @ScaledMetric(relativeTo: .largeTitle) private var balanceSize: CGFloat = 44
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
                        .buttonStyle(SecondaryButtonStyle())
                }
            } else { statement }
        }
        .blackScreen()
        .navigationTitle("Hesap detayı")
        .navigationBarTitleDisplayMode(.inline)
        .task { await model.open() }
        .sheet(isPresented: $showingFilter) {
            TransactionFilterSheet(model: model.history)
                .presentationDetents([.medium, .large])
                .presentationBackground(Palette.background.color)
        }
    }

    private var statement: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Layout.large) {
                header
                if let message = model.accountError {
                    StatusBand(text: "Hesap bilgileri yenilenemedi. \(message)", actionTitle: "Tekrar dene") {
                        Task { await model.refreshAccount() }
                    }
                }
                VStack(alignment: .leading, spacing: Layout.medium) {
                    ViewThatFits(in: .horizontal) {
                        HStack {
                            SectionTitle(text: "Hesap hareketleri")
                            Spacer()
                            filterButton
                        }
                        VStack(alignment: .leading, spacing: Layout.small) {
                            SectionTitle(text: "Hesap hareketleri")
                            filterButton
                        }
                    }
                    if model.history.periodText != nil || model.history.loadedOnce {
                        Text([model.history.periodText, model.history.loadedOnce ? "\(model.history.totalCount) işlem" : nil]
                            .compactMap { $0 }.joined(separator: " · "))
                            .font(Typography.caption)
                            .foregroundStyle(Palette.secondaryText.color)
                    }
                    TransactionRows(model: model.history)
                }
            }
            .padding(.horizontal, Layout.regular)
            .padding(.top, Layout.small)
        }
        .refreshable { await model.refresh() }
    }

    /// No card (UI-05 K2-A): the name small, the balance very large and light, the full number selectable.
    private var header: some View {
        VStack(alignment: .leading, spacing: Layout.small) {
            HStack {
                Text(model.account.accountName)
                    .font(Typography.caption)
                    .foregroundStyle(Palette.secondaryText.color)
                Spacer(minLength: Layout.small)
                if model.account.accountType == .business { BusinessTag() }
            }
            Text(Money.text(model.account.balance, currency: model.account.currencyCode))
                .font(.system(size: balanceSize, weight: .light).monospacedDigit())
                .foregroundStyle(Palette.primaryText.color)
                .lineLimit(1)
                .minimumScaleFactor(0.5)
                .textSelection(.enabled)
            Text(model.account.accountNumber)
                .font(Typography.caption)
                .monospacedDigit()
                .foregroundStyle(Palette.secondaryText.color)
                .textSelection(.enabled)
            Text(AccountDisplay.typeText(model.account))
                .font(Typography.caption)
                .foregroundStyle(Palette.secondaryText.color)
            if model.isRefreshingAccount {
                ProgressView("Hesap bilgileri yenileniyor…")
                    .font(Typography.caption)
                    .tint(Palette.secondaryText.color)
            }
        }
    }

    private var filterButton: some View {
        Button {
            model.history.clearFilterError()
            showingFilter = true
        } label: {
            Label(model.history.filter.title, systemImage: "line.3.horizontal.decrease")
        }
        .buttonStyle(ChipButtonStyle(isSelected: model.history.filter != .allTime))
        .accessibilityLabel("İşlem dönemi: \(model.history.filter.title)")
        .accessibilityHint("Dönem ve tarih aralığı seçer")
    }
}

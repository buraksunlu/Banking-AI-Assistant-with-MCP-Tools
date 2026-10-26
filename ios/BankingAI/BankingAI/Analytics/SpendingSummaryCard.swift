import SwiftUI

/// The "Bu ay harcama" card of the home screen. It shows ``SpendingSummaryViewModel``'s state; there is no logic here. The
/// whole card opens the analysis screen (UI-03 K4-A).
struct SpendingSummaryCard: View {
    let model: SpendingSummaryViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            HStack {
                SectionTitle(text: "Bu ay harcama")
                Spacer()
                Image(systemName: "chevron.right")
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(Palette.secondaryText.color)
                    .accessibilityHidden(true)
            }
            switch model.state {
            case .loading:
                Text("0,00 TL")
                    .font(Typography.balance)
                    .redacted(reason: .placeholder)
                    // VoiceOver would otherwise read a made-up "0,00 TL" as this month's spending.
                    .accessibilityLabel("Yükleniyor")
            case .empty:
                Text("Bu ay henüz harcama yok")
                    .font(Typography.body)
                    .foregroundStyle(Palette.secondaryText.color)
            case .failed(let message):
                Text(message)
                    .font(Typography.caption)
                    .foregroundStyle(Palette.secondaryText.color)
                Button("Tekrar dene") {
                    Task { await model.load() }
                }
                .buttonStyle(SecondaryButtonStyle())
            case .loaded(let currencies):
                if let message = model.refreshError {
                    StatusBand(text: message)
                }
                ForEach(currencies) { spending in
                    VStack(alignment: .leading, spacing: Layout.tight) {
                        Text(Money.text(spending.totalAmount, currency: spending.currencyCode))
                            .font(Typography.balance)
                            .foregroundStyle(Palette.primaryText.color)
                            .lineLimit(1)
                            .minimumScaleFactor(0.6)
                        Text("\(spending.transactionCount) alışveriş")
                            .font(Typography.caption)
                            .foregroundStyle(Palette.secondaryText.color)
                    }
                    .accessibilityElement(children: .combine)
                }
            }
            Text("Harcama analizi")
                .font(Typography.caption.weight(.semibold))
                .foregroundStyle(Palette.accent.color)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .cardSurface()
        // Coming back from the chat runs this again; only a figure that never arrived is asked for (pull to refresh does the rest).
        .task {
            if model.state == .loading { await model.load() }
        }
    }
}

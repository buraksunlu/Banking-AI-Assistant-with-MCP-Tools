import SwiftUI

/// "Gelir payı · <ay>", the third card of the home panel (UX-01 PR2, K3-D): last month's spending as a share of income, one
/// figure and the INC-04 bar. The whole card with its advice lives on the analysis tab now; "Nerede kısarım?" goes there
/// until the assistant sheet takes it over (PR5). It reads the same ``IncomeShareViewModel``, so there is no second request.
struct IncomeGlanceCard: View {
    let model: IncomeShareViewModel
    let showAdvice: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            SectionTitle(text: HomeDisplay.incomeTitle(model.month))
            switch model.state {
            case .loading:
                GlanceBlock(currency: Skeleton.incomeShare, showsCurrency: false)
                    .skeleton()
            case .empty:
                Text("Geçen ay harcama yok")
                    .font(Typography.body)
                    .foregroundStyle(Palette.secondaryText.color)
            case .failed(let message):
                StateMessage(title: message, symbol: "exclamationmark.triangle", actionTitle: "Tekrar dene") {
                    Task { await model.load() }
                }
            case .loaded(let currencies):
                if let message = model.refreshError {
                    StatusBand(text: message)
                }
                ForEach(currencies) { currency in
                    GlanceBlock(currency: currency, showsCurrency: currencies.count > 1)
                }
                // Without salary there is nothing to cut against (K4-B).
                if currencies.contains(where: HomeDisplay.offersAdvice) {
                    HairlineDivider()
                    Button(action: showAdvice) {
                        HStack {
                            Text("Nerede kısarım?")
                            Spacer()
                            Image(systemName: "chevron.right").font(.caption.weight(.semibold))
                        }
                        .font(Typography.caption.weight(.semibold))
                        .foregroundStyle(Palette.accent.color)
                        .frame(minHeight: 44)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityHint("Gelire göre harcama önerilerini açar")
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .cardSurface()
        .motion(value: model.state)
        .task {
            if model.state == .loading { await model.load() }
        }
    }
}

/// One currency: the share (or why there is none) and the group bar.
private struct GlanceBlock: View {
    let currency: CurrencyAdvice
    let showsCurrency: Bool

    var body: some View {
        let segments = IncomeShareDisplay.segments(currency)
        VStack(alignment: .leading, spacing: Layout.small) {
            if showsCurrency {
                Text(currency.currencyCode)
                    .font(Typography.eyebrow)
                    .foregroundStyle(Palette.secondaryText.color)
            }
            Text(HomeDisplay.incomeGlance(currency))
                .font(currency.spentShare == nil ? Typography.body : Typography.title)
                .foregroundStyle(currency.spentShare == nil ? Palette.secondaryText.color : Palette.primaryText.color)
                .fixedSize(horizontal: false, vertical: true)
            if !segments.isEmpty {
                IncomeShareBar(segments: segments)
            }
        }
        .accessibilityElement(children: .combine)
    }
}

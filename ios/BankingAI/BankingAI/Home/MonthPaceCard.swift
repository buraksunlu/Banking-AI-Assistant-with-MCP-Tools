import SwiftUI

/// "Bu ay harcama · N gün", the first card of the home panel (UX-01 PR2): the running month's figure and how it stands
/// against the same days of the month before. It shows ``MonthPaceViewModel``'s state; the words come from ``HomeDisplay``.
struct MonthPaceCard: View {
    let model: MonthPaceViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            HStack {
                SectionTitle(text: HomeDisplay.paceTitle(model.endDate))
                Spacer()
                Image(systemName: "chevron.right")
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(Palette.secondaryText.color)
                    .accessibilityHidden(true)
            }
            switch model.state {
            case .loading:
                PaceBlock(pace: Skeleton.pace, showsCurrency: false)
                    .skeleton()
            case .empty:
                Text("Bu ay henüz harcama yok")
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
                // Currencies are never added together: one figure and one line each.
                ForEach(currencies) { pace in
                    PaceBlock(pace: pace, showsCurrency: currencies.count > 1)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .cardSurface()
        .motion(value: model.state)
        // Coming back from another tab runs this again; only a figure that never arrived is asked for.
        .task {
            if model.state == .loading { await model.load() }
        }
    }
}

/// One currency: the figure, the count and the line against the same days.
private struct PaceBlock: View {
    let pace: CurrencyComparison
    let showsCurrency: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.tight) {
            if showsCurrency {
                Text(pace.currencyCode)
                    .font(Typography.eyebrow)
                    .foregroundStyle(Palette.secondaryText.color)
            }
            Text(HomeDisplay.paceHeadline(pace))
                .font(pace.currentAmount == 0 ? Typography.title : Typography.balance)
                .foregroundStyle(Palette.primaryText.color)
                .lineLimit(1)
                .minimumScaleFactor(0.6)
                .contentTransition(.numericText())
            HStack(alignment: .firstTextBaseline, spacing: Layout.tight) {
                if let symbol = HomeDisplay.paceSymbol(pace) {
                    Image(systemName: symbol)
                        .accessibilityHidden(true)
                }
                Text(HomeDisplay.paceLine(pace))
                    .fixedSize(horizontal: false, vertical: true)
            }
            .font(Typography.caption)
            .foregroundStyle(Palette.secondaryText.color)
            if pace.currentCount > 0 {
                Text("\(pace.currentCount) alışveriş")
                    .font(Typography.caption)
                    .foregroundStyle(Palette.secondaryText.color)
            }
        }
        .accessibilityElement(children: .combine)
    }
}

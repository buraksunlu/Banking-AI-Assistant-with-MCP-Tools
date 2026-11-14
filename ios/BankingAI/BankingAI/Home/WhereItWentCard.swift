import SwiftUI

/// "Nereye gitti", the second card of the home panel (UX-01 PR2): this month's first three categories with the backend's
/// share as a bar (K2-A). Its own ``CategoryBreakdownViewModel`` stays on this month, whatever the analysis tab shows.
/// The rows are not buttons yet: tapping one opens the assistant sheet in PR5 (K5-A).
struct WhereItWentCard: View {
    let model: CategoryBreakdownViewModel
    let showAll: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            HStack {
                SectionTitle(text: "Nereye gitti")
                Spacer()
                Button(action: showAll) {
                    HStack(spacing: Layout.tight) {
                        Text("Tümü")
                        Image(systemName: "chevron.right").font(.caption.weight(.semibold))
                    }
                    .font(Typography.caption.weight(.semibold))
                    .foregroundStyle(Palette.accent.color)
                    .frame(minHeight: 44)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityHint("Harcama analizini açar")
            }
            switch model.state {
            case .loading:
                CategoryBars(categories: Array(Skeleton.categories.prefix(HomeDisplay.shownCategories)), currency: "TRY")
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
                ForEach(currencies) { currency in
                    VStack(alignment: .leading, spacing: Layout.small) {
                        if currencies.count > 1 {
                            Text(currency.currencyCode)
                                .font(Typography.eyebrow)
                                .foregroundStyle(Palette.secondaryText.color)
                        }
                        CategoryBars(categories: HomeDisplay.topCategories(currency), currency: currency.currencyCode)
                    }
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

/// Name, bar and amount per category; at the largest text sizes the amount goes under the name.
private struct CategoryBars: View {
    let categories: [CategorySpending]
    let currency: String
    @Environment(\.dynamicTypeSize) private var textSize

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            ForEach(categories) { slice in
                VStack(alignment: .leading, spacing: Layout.tight) {
                    let layout = textSize.isAccessibilitySize ? AnyLayout(VStackLayout(alignment: .leading, spacing: Layout.tight))
                                                              : AnyLayout(HStackLayout(alignment: .firstTextBaseline))
                    layout {
                        Text(slice.displayName)
                            .font(Typography.body)
                            .foregroundStyle(Palette.primaryText.color)
                        if !textSize.isAccessibilitySize { Spacer(minLength: Layout.small) }
                        Text(Money.text(slice.amount, currency: currency))
                            .font(Typography.body.monospacedDigit())
                            .foregroundStyle(Palette.primaryText.color)
                    }
                    ShareBar(fraction: HomeDisplay.barFraction(slice.percentage),
                             color: AnalyticsDisplay.tone(forCategory: slice.categoryCode).swatch.color)
                }
                .accessibilityElement(children: .ignore)
                .accessibilityLabel(AnalyticsDisplay.summary(slice, currency: currency))
            }
        }
    }
}

/// A thin bar on an empty track, as long as the backend's share.
private struct ShareBar: View {
    let fraction: Double
    let color: Color

    var body: some View {
        GeometryReader { proxy in
            Capsule()
                .fill(color)
                .frame(width: proxy.size.width * fraction)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Palette.raised.color, in: Capsule())
        }
        .frame(height: 6)
        .accessibilityHidden(true)
    }
}

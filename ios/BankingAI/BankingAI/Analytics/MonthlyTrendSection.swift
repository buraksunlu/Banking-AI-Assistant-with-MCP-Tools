import Charts
import SwiftUI

/// The "Aylık harcama" part of the analysis screen: one bar chart per currency. It shows ``MonthlyTrendViewModel``'s state;
/// there is no logic here beyond mapping a tap to a bar.
struct MonthlyTrendSection: View {
    let model: MonthlyTrendViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            SectionTitle(text: "Aylık harcama")
            switch model.state {
            case .loading:
                BarsSkeleton()
            case .empty:
                StateMessage(title: "Son aylarda harcama yok", symbol: "chart.bar",
                             message: "Harcama yapıldığında aylar burada görünür.")
                    .cardSurface(padding: 0)
            case .failed(let message):
                StateMessage(title: "Aylık harcamalar yüklenemedi", symbol: "exclamationmark.triangle", message: message,
                             actionTitle: "Tekrar dene") { Task { await model.load() } }
                    .cardSurface(padding: 0)
            case .loaded(let currencies):
                if let message = model.refreshError {
                    StatusBand(text: message)
                }
                ForEach(currencies) { currency in
                    CurrencyBars(currency: currency, model: model)
                }
            }
        }
        .motion(value: model.state)
    }
}

/// The bar card while it loads: seven grey bars of fixed heights in the real chart's height.
private struct BarsSkeleton: View {
    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            Text("TRY").font(Typography.eyebrow)
            GeometryReader { space in
                HStack(alignment: .bottom, spacing: Layout.medium) {
                    ForEach(Skeleton.barHeights.indices, id: \.self) { index in
                        RoundedRectangle(cornerRadius: 4)
                            .fill(Skeleton.fill)
                            .frame(height: space.size.height * Skeleton.barHeights[index])
                    }
                }
                .frame(maxHeight: .infinity, alignment: .bottom)
            }
            .frame(height: 180)
            Text("Ayrıntı için bir çubuğa dokunun.").font(Typography.caption).frame(maxWidth: .infinity)
        }
        .cardSurface()
        .skeleton()
    }
}

/// One currency's bars. Currencies are never merged: each has its own chart. No value axis: an amount is written in full
/// when a bar is tapped, never rounded into a label (decision K11-A); the grid lines only give the scale.
private struct CurrencyBars: View {
    let currency: CurrencyTrend
    let model: MonthlyTrendViewModel
    @State private var tappedID: String?

    private var selectedID: String? { model.selectedID(currency: currency.currencyCode) }

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            Text(currency.currencyCode)
                .font(Typography.eyebrow)
                .tracking(1)
                .foregroundStyle(Palette.secondaryText.color)
            Chart(currency.months) { spending in
                BarMark(x: .value("Ay", spending.id),
                        y: .value("Tutar", NSDecimalNumber(decimal: spending.amount).doubleValue))
                    .foregroundStyle(Palette.accent.color)
                    // The month still running is paler: it is not a whole month (K12-A).
                    .opacity(AnalyticsDisplay.barOpacity(isPartial: spending.isPartial, id: spending.id, selectedID: selectedID))
                    .accessibilityLabel(AnalyticsDisplay.monthAccessibilityLabel(spending, currency: currency.currencyCode))
            }
            .chartXSelection(value: $tappedID)
            .chartXAxis {
                AxisMarks(values: currency.months.map(\.id)) { value in
                    AxisValueLabel {
                        if let id = value.as(String.self), let spending = currency.months.first(where: { $0.id == id }) {
                            VStack(spacing: 2) {
                                Text(AnalyticsDisplay.shortMonth(spending.month))
                                    .foregroundStyle(Palette.secondaryText.color)
                                // A second cue besides the pale color.
                                if spending.isPartial {
                                    Circle().fill(Palette.secondaryText.color).frame(width: 4, height: 4)
                                }
                            }
                        }
                    }
                }
            }
            .chartYAxis {
                AxisMarks { _ in AxisGridLine().foregroundStyle(Palette.hairline.color) }
            }
            .frame(height: 180)
            .onChange(of: tappedID) { _, id in
                guard let id, id != selectedID, let spending = currency.months.first(where: { $0.id == id }) else { return }
                model.toggle(spending, currency: currency.currencyCode)
            }
            Text(selectedSummary)
                .font(Typography.caption)
                .foregroundStyle(Palette.secondaryText.color)
                .frame(maxWidth: .infinity, alignment: .center)
        }
        .cardSurface()
    }

    private var selectedSummary: String {
        guard let spending = currency.months.first(where: { $0.id == selectedID }) else { return "Ayrıntı için bir çubuğa dokunun." }
        return AnalyticsDisplay.monthSummary(spending, currency: currency.currencyCode)
    }
}

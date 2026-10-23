import Charts
import SwiftUI

/// The "Aylık harcama" part of the analysis screen: one bar chart per currency. It shows ``MonthlyTrendViewModel``'s state;
/// there is no logic here beyond mapping a tap to a bar.
struct MonthlyTrendSection: View {
    let model: MonthlyTrendViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Aylık harcama")
                .font(.title3.bold())
            switch model.state {
            case .loading:
                ProgressView()
                    .frame(maxWidth: .infinity, minHeight: 160)
            case .empty:
                ContentUnavailableView("Son aylarda harcama yok", systemImage: "chart.bar",
                                       description: Text("Harcama yapıldığında aylar burada görünür."))
            case .failed(let message):
                ContentUnavailableView {
                    Label("Aylık harcamalar yüklenemedi", systemImage: "exclamationmark.triangle")
                } description: {
                    Text(message)
                } actions: {
                    Button("Tekrar dene") { Task { await model.load() } }
                        .buttonStyle(.bordered)
                }
            case .loaded(let currencies):
                if let message = model.refreshError {
                    Label(message, systemImage: "wifi.exclamationmark")
                        .font(.subheadline)
                        .padding(12)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(Color.orange.opacity(0.15), in: RoundedRectangle(cornerRadius: 12))
                }
                ForEach(currencies) { currency in
                    CurrencyBars(currency: currency, model: model)
                }
            }
        }
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
        VStack(alignment: .leading, spacing: 12) {
            Text(currency.currencyCode).font(.headline)
            Chart(currency.months) { spending in
                BarMark(x: .value("Ay", spending.id),
                        y: .value("Tutar", NSDecimalNumber(decimal: spending.amount).doubleValue))
                    .foregroundStyle(Color.accentColor)
                    // The month still running is paler: it is not a whole month (K12-A).
                    .opacity(opacity(of: spending))
                    .accessibilityLabel(AnalyticsDisplay.monthAccessibilityLabel(spending, currency: currency.currencyCode))
            }
            .chartXSelection(value: $tappedID)
            .chartXAxis {
                AxisMarks(values: currency.months.map(\.id)) { value in
                    AxisValueLabel {
                        if let id = value.as(String.self), let spending = currency.months.first(where: { $0.id == id }) {
                            VStack(spacing: 2) {
                                Text(AnalyticsDisplay.shortMonth(spending.month))
                                // A second cue besides the pale color.
                                if spending.isPartial { Circle().frame(width: 4, height: 4) }
                            }
                        }
                    }
                }
            }
            .chartYAxis {
                AxisMarks { _ in AxisGridLine() }
            }
            .frame(height: 180)
            .onChange(of: tappedID) { _, id in
                guard let id, id != selectedID, let spending = currency.months.first(where: { $0.id == id }) else { return }
                model.toggle(spending, currency: currency.currencyCode)
            }
            Text(selectedSummary)
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, alignment: .center)
        }
        .padding()
        .background(Color(.secondarySystemGroupedBackground), in: RoundedRectangle(cornerRadius: 16))
    }

    private func opacity(of spending: MonthlySpending) -> Double {
        if let selectedID, selectedID != spending.id { return 0.35 }
        return spending.isPartial ? 0.45 : 1
    }

    private var selectedSummary: String {
        guard let spending = currency.months.first(where: { $0.id == selectedID }) else { return "Ayrıntı için bir çubuğa dokunun." }
        return AnalyticsDisplay.monthSummary(spending, currency: currency.currencyCode)
    }
}

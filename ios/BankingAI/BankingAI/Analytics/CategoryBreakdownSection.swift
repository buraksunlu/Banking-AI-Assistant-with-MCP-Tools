import Charts
import SwiftUI

/// Pushes the analysis screen; ``HomeView`` builds it only when it is opened.
struct AnalyticsRoute: Hashable {}

extension CategoryTone {
    /// System colors, so light and dark mode both stay readable.
    var color: Color {
        switch self {
        case .orange: .orange
        case .green: .green
        case .brown: .brown
        case .pink: .pink
        case .purple: .purple
        case .indigo: .indigo
        case .teal: .teal
        case .cyan: .cyan
        case .red: .red
        case .mint: .mint
        case .blue: .blue
        case .yellow: .yellow
        case .gray: .gray
        }
    }
}

/// The "Kategorilere göre" part of the analysis screen: the period choice and one ring per currency. It shows
/// ``CategoryBreakdownViewModel``'s state; there is no logic here beyond mapping a tap to a slice.
struct CategoryBreakdownSection: View {
    @Bindable var model: CategoryBreakdownViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Kategorilere göre")
                .font(.title3.bold())
            Picker("Dönem", selection: Binding(get: { model.period }, set: { period in Task { await model.select(period) } })) {
                ForEach(AnalyticsPeriod.allCases) { period in
                    Text(period.title).tag(period)
                }
            }
            .pickerStyle(.segmented)
            switch model.state {
            case .loading:
                ProgressView()
                    .frame(maxWidth: .infinity, minHeight: 200)
            case .empty:
                ContentUnavailableView("Bu dönemde harcama yok", systemImage: "chart.pie",
                                       description: Text("Harcama yapıldığında kategoriler burada görünür."))
            case .failed(let message):
                ContentUnavailableView {
                    Label("Harcamalar yüklenemedi", systemImage: "exclamationmark.triangle")
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
                    CurrencyRing(currency: currency, model: model)
                }
            }
        }
    }
}

/// One currency's ring and its list. Currencies are never merged: each has its own ring and its own total.
private struct CurrencyRing: View {
    let currency: CurrencyCategoryBreakdown
    let model: CategoryBreakdownViewModel
    @State private var angle: Double?

    private var selectedID: String? { model.selectedID(currency: currency.currencyCode) }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text(currency.currencyCode).font(.headline)
                Spacer()
                Text(Money.text(currency.totalAmount, currency: currency.currencyCode))
                    .font(.headline).monospacedDigit()
            }
            Chart(currency.categories) { slice in
                SectorMark(angle: .value("Tutar", NSDecimalNumber(decimal: slice.amount).doubleValue),
                           innerRadius: .ratio(0.62), angularInset: 1.5)
                    .foregroundStyle(AnalyticsDisplay.tone(forCategory: slice.categoryCode).color)
                    .opacity(selectedID == nil || selectedID == slice.id ? 1 : 0.35)
            }
            .chartAngleSelection(value: $angle)
            .chartLegend(.hidden)
            .frame(height: 200)
            .accessibilityHidden(true)
            .onChange(of: angle) { _, value in
                guard let value, let slice = AnalyticsDisplay.slice(atAmount: value, in: currency.categories),
                      slice.id != selectedID else { return }
                model.toggle(slice, currency: currency.currencyCode)
            }
            Text(selectedSummary)
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, alignment: .center)
            VStack(spacing: 0) {
                ForEach(currency.categories) { slice in
                    Button { model.toggle(slice, currency: currency.currencyCode) } label: { row(slice) }
                        .buttonStyle(.plain)
                        .accessibilityLabel(AnalyticsDisplay.accessibilityLabel(slice, currency: currency.currencyCode))
                        .accessibilityAddTraits(selectedID == slice.id ? .isSelected : [])
                    if slice.id != currency.categories.last?.id { Divider() }
                }
            }
        }
        .padding()
        .background(Color(.secondarySystemGroupedBackground), in: RoundedRectangle(cornerRadius: 16))
    }

    private var selectedSummary: String {
        guard let slice = currency.categories.first(where: { $0.id == selectedID }) else { return "Ayrıntı için bir dilime dokunun." }
        return AnalyticsDisplay.summary(slice, currency: currency.currencyCode)
    }

    private func row(_ slice: CategorySpending) -> some View {
        HStack(spacing: 10) {
            Circle()
                .fill(AnalyticsDisplay.tone(forCategory: slice.categoryCode).color)
                .frame(width: 10, height: 10)
            Text(slice.displayName)
            Spacer()
            VStack(alignment: .trailing) {
                Text(Money.text(slice.amount, currency: currency.currencyCode)).monospacedDigit()
                Text(AnalyticsDisplay.percentText(slice.percentage)).font(.caption).foregroundStyle(.secondary)
            }
        }
        .padding(.vertical, 8)
        .contentShape(Rectangle())
        .fontWeight(selectedID == slice.id ? .semibold : .regular)
    }
}

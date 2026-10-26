import Charts
import SwiftUI

/// Pushes the analysis screen; ``HomeView`` builds it only when it is opened.
struct AnalyticsRoute: Hashable {}

/// The "Kategorilere göre" part of the analysis screen: the period choice and one ring per currency. It shows
/// ``CategoryBreakdownViewModel``'s state; there is no logic here beyond mapping a tap to a slice.
struct CategoryBreakdownSection: View {
    @Bindable var model: CategoryBreakdownViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            SectionTitle(text: "Kategorilere göre")
            Picker("Dönem", selection: Binding(get: { model.period }, set: { period in Task { await model.select(period) } })) {
                ForEach(AnalyticsPeriod.allCases) { period in
                    Text(period.title).tag(period)
                }
            }
            .pickerStyle(.segmented)
            switch model.state {
            case .loading:
                RingSkeleton()
            case .empty:
                StateMessage(title: "Bu dönemde harcama yok", symbol: "chart.pie",
                             message: "Harcama yapıldığında kategoriler burada görünür.")
                    .cardSurface(padding: 0)
            case .failed(let message):
                StateMessage(title: "Harcamalar yüklenemedi", symbol: "exclamationmark.triangle", message: message,
                             actionTitle: "Tekrar dene") { Task { await model.load() } }
                    .cardSurface(padding: 0)
            case .loaded(let currencies):
                if let message = model.refreshError {
                    StatusBand(text: message)
                }
                ForEach(currencies) { currency in
                    CurrencyRing(currency: currency, model: model)
                }
            }
        }
    }
}

/// The ring card while it loads: a grey ring of the real size over grey rows shaped like the list's.
private struct RingSkeleton: View {
    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            Text("TRY").font(Typography.eyebrow)
            Circle()
                .stroke(Skeleton.fill, lineWidth: 34)
                .padding(17)
                .frame(height: 200)
                .frame(maxWidth: .infinity)
            VStack(spacing: 0) {
                ForEach(Skeleton.categories) { slice in
                    HStack(spacing: Layout.medium) {
                        // Shapes are not greyed by `redacted`; a bright dot would look like a real category.
                        Circle().fill(Skeleton.fill).frame(width: 10, height: 10)
                        Text(slice.displayName)
                        Spacer()
                        Text(Money.text(slice.amount, currency: "TRY")).monospacedDigit()
                    }
                    .padding(.vertical, Layout.small)
                }
            }
        }
        .cardSurface()
        .skeleton()
    }
}

/// One currency's ring and its list. Currencies are never merged: each has its own ring and its own total.
private struct CurrencyRing: View {
    let currency: CurrencyCategoryBreakdown
    let model: CategoryBreakdownViewModel
    @State private var angle: Double?

    private var selectedID: String? { model.selectedID(currency: currency.currencyCode) }

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            Text(currency.currencyCode)
                .font(Typography.eyebrow)
                .tracking(1)
                .foregroundStyle(Palette.secondaryText.color)
            Chart(currency.categories) { slice in
                SectorMark(angle: .value("Tutar", NSDecimalNumber(decimal: slice.amount).doubleValue),
                           innerRadius: .ratio(0.62), angularInset: 1.5)
                    .foregroundStyle(AnalyticsDisplay.tone(forCategory: slice.categoryCode).swatch.color)
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
            // The backend's total in the hole of the ring (UI-05 K14-B); the phone adds nothing up.
            .overlay { total }
            Text(selectedSummary)
                .font(Typography.caption)
                .foregroundStyle(Palette.secondaryText.color)
                .frame(maxWidth: .infinity, alignment: .center)
            VStack(spacing: 0) {
                ForEach(currency.categories) { slice in
                    Button { model.toggle(slice, currency: currency.currencyCode) } label: { row(slice) }
                        .buttonStyle(.plain)
                        .accessibilityLabel(AnalyticsDisplay.accessibilityLabel(slice, currency: currency.currencyCode))
                        .accessibilityAddTraits(selectedID == slice.id ? .isSelected : [])
                    if slice.id != currency.categories.last?.id { HairlineDivider() }
                }
            }
        }
        .cardSurface()
    }

    private var total: some View {
        VStack(spacing: Layout.tight) {
            Text(Typography.label("Toplam"))
                .font(Typography.eyebrow)
                .tracking(1)
                .foregroundStyle(Palette.secondaryText.color)
            Text(Money.text(currency.totalAmount, currency: currency.currencyCode))
                .font(Font.title3.weight(.light).monospacedDigit())
                .foregroundStyle(Palette.primaryText.color)
                .lineLimit(1)
                .minimumScaleFactor(0.5)
        }
        .frame(maxWidth: 110)
        .accessibilityElement(children: .combine)
    }

    private var selectedSummary: String {
        guard let slice = currency.categories.first(where: { $0.id == selectedID }) else { return "Ayrıntı için bir dilime dokunun." }
        return AnalyticsDisplay.summary(slice, currency: currency.currencyCode)
    }

    private func row(_ slice: CategorySpending) -> some View {
        HStack(spacing: Layout.medium) {
            Circle()
                .fill(AnalyticsDisplay.tone(forCategory: slice.categoryCode).swatch.color)
                .frame(width: 10, height: 10)
            Text(slice.displayName)
                .foregroundStyle(Palette.primaryText.color)
            Spacer()
            VStack(alignment: .trailing) {
                Text(Money.text(slice.amount, currency: currency.currencyCode))
                    .monospacedDigit()
                    .foregroundStyle(Palette.primaryText.color)
                Text(AnalyticsDisplay.percentText(slice.percentage))
                    .font(.caption)
                    .foregroundStyle(Palette.secondaryText.color)
            }
        }
        .padding(.vertical, Layout.small)
        .contentShape(Rectangle())
        .fontWeight(selectedID == slice.id ? .semibold : .regular)
    }
}

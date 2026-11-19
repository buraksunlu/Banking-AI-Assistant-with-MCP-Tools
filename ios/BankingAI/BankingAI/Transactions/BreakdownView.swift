import SwiftUI

/// The breakdown of a chat answer's spending (DEMO-02): per currency, "Rutin ve temel" and "Rutin olmayan" with the backend's
/// totals and counts, a tag on every routine row, and under the non-routine rows the comparison and the habit advice. A row opens
/// the transaction's detail with its own insight.
struct BreakdownView: View {
    let model: BreakdownViewModel
    let insight: (TransactionItem) -> TransactionInsightViewModel?

    var body: some View {
        switch model.state {
        case .loading:
            TransactionSkeletonRows(items: Skeleton.history(firstPage: true))
                .cardSurface(padding: 0)
                .skeleton()
        case .failed(let message):
            StateMessage(title: "Döküm yüklenemedi", symbol: "exclamationmark.triangle", message: message, size: .screen,
                         actionTitle: "Tekrar dene") { Task { await model.load() } }
        case .empty:
            StateMessage(title: "Seçilen dönemde harcama yok.", symbol: "list.bullet.rectangle", size: .screen)
        case .loaded(let currencies):
            if let message = model.refreshError {
                StatusBand(text: message, actionTitle: "Tekrar dene") { Task { await model.load() } }
            }
            ForEach(currencies) { currency in
                section(BreakdownDisplay.routineTitle, count: currency.routine.count, total: currency.routine.total,
                        currency: currency.currencyCode) {
                    ForEach(currency.routine.items) { item in
                        row(item.transaction, tag: BreakdownDisplay.label(item.reason, installment: item.installment))
                    }
                }
                section(BreakdownDisplay.nonRoutineTitle, count: currency.nonRoutine.count, total: currency.nonRoutine.total,
                        currency: currency.currencyCode) {
                    ForEach(currency.nonRoutine.items) { item in
                        row(item.transaction, tag: item.eligible ? nil : BreakdownDisplay.unknownTag)
                    }
                }
                if let investment = currency.nonRoutine.investment {
                    InvestmentCard(title: BreakdownDisplay.investmentTitle, investment: investment)
                }
                ForEach(currency.nonRoutine.advice, id: \.self) { advice in
                    Label(advice.message, systemImage: "lightbulb")
                        .font(Typography.caption)
                        .foregroundStyle(Palette.primaryText.color)
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .cardSurface()
                }
            }
        }
    }

    @ViewBuilder
    private func section<Rows: View>(_ title: String, count: Int, total: Decimal, currency: String,
                                     @ViewBuilder rows: () -> Rows) -> some View {
        if count > 0 {
            VStack(alignment: .leading, spacing: Layout.small) {
                SectionTitle(text: title)
                Text(BreakdownDisplay.groupSummary(count: count, total: total, currency: currency))
                    .font(Typography.caption.monospacedDigit())
                    .foregroundStyle(Palette.secondaryText.color)
                LazyVStack(spacing: 0) { rows() }
                    .cardSurface(padding: 0)
            }
        }
    }

    private func row(_ item: TransactionItem, tag: String?) -> some View {
        NavigationLink {
            TransactionDetailView(item: item, insight: insight(item))
        } label: {
            VStack(alignment: .leading, spacing: 2) {
                TransactionRow(item: item)
                if let tag {
                    Text(tag)
                        .font(Typography.eyebrow)
                        .foregroundStyle(Palette.accent.color)
                        .padding(.leading, 44)
                }
            }
            .padding(.horizontal, Layout.regular)
            .padding(.vertical, Layout.medium)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

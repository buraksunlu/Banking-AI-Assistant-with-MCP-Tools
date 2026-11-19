import SwiftUI

/// "Yaklaşan ödemeler", the third card of the home panel (DEMO-03, in place of the income glance): the backend's total for the
/// next 30 days, its three earliest payments and "Tümü (N)" for the rest (K3-A, K4-A).
struct UpcomingPaymentsCard: View {
    let model: UpcomingPaymentsViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            SectionTitle(text: UpcomingDisplay.title)
            switch model.state {
            case .loading:
                UpcomingRows(payments: Skeleton.upcoming)
                    .skeleton()
            case .empty:
                Text(UpcomingDisplay.emptyText)
                    .font(Typography.body)
                    .foregroundStyle(Palette.secondaryText.color)
            case .failed(let message):
                StateMessage(title: message, symbol: "exclamationmark.triangle", actionTitle: "Tekrar dene") {
                    Task { await model.load() }
                }
            case .loaded(let payments):
                if let message = model.refreshError {
                    StatusBand(text: message)
                }
                UpcomingTotals(totals: model.totals)
                UpcomingRows(payments: UpcomingDisplay.preview(payments))
                if let title = UpcomingDisplay.allTitle(count: payments.count) {
                    HairlineDivider()
                    NavigationLink {
                        UpcomingPaymentsView(model: model)
                    } label: {
                        HStack {
                            Text(title)
                            Spacer()
                            Image(systemName: "chevron.right").font(.caption.weight(.semibold))
                        }
                        .font(Typography.caption.weight(.semibold))
                        .foregroundStyle(Palette.accent.color)
                        .frame(minHeight: 44)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityHint("Bütün yaklaşan ödemeleri açar")
                }
                UpcomingNote()
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

/// Every expected payment of the window, from the card's own answer (K4-A: no second request).
struct UpcomingPaymentsView: View {
    let model: UpcomingPaymentsViewModel

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Layout.medium) {
                if case .loaded(let payments) = model.state {
                    UpcomingTotals(totals: model.totals)
                    UpcomingRows(payments: payments)
                        .cardSurface()
                    UpcomingNote()
                }
            }
            .padding(.horizontal, Layout.regular)
            .padding(.vertical, Layout.medium)
        }
        .background(Palette.background.color)
        .navigationTitle(UpcomingDisplay.title)
        .navigationBarTitleDisplayMode(.inline)
    }
}

/// "Önümüzdeki 30 gün · 9.519,97 TL", one line per currency (never one total).
private struct UpcomingTotals: View {
    let totals: [UpcomingTotal]

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(UpcomingDisplay.windowText)
                .font(Typography.caption)
                .foregroundStyle(Palette.secondaryText.color)
            ForEach(totals, id: \.currencyCode) { total in
                Text(UpcomingDisplay.totalText(total))
                    .font(Typography.title.monospacedDigit())
                    .foregroundStyle(Palette.primaryText.color)
            }
        }
        .accessibilityElement(children: .combine)
    }
}

private struct UpcomingRows: View {
    let payments: [UpcomingPayment]

    var body: some View {
        VStack(spacing: 0) {
            ForEach(Array(payments.enumerated()), id: \.element.id) { index, payment in
                if index > 0 { HairlineDivider() }
                UpcomingRow(payment: payment)
            }
        }
    }
}

/// Merchant and tag on the left, the amount and the day on the right; wraps at the largest text sizes.
private struct UpcomingRow: View {
    let payment: UpcomingPayment
    @Environment(\.dynamicTypeSize) private var textSize

    var body: some View {
        let layout = textSize.isAccessibilitySize
            ? AnyLayout(VStackLayout(alignment: .leading, spacing: 2)) : AnyLayout(HStackLayout(spacing: Layout.small))
        layout {
            VStack(alignment: .leading, spacing: 2) {
                Text(payment.merchant)
                    .font(Typography.body.weight(.medium))
                    .foregroundStyle(Palette.primaryText.color)
                Text(UpcomingDisplay.tag(payment))
                    .font(Typography.eyebrow)
                    .foregroundStyle(Palette.accent.color)
            }
            if !textSize.isAccessibilitySize { Spacer(minLength: Layout.small) }
            VStack(alignment: textSize.isAccessibilitySize ? .leading : .trailing, spacing: 2) {
                Text(Money.text(payment.estimatedAmount, currency: payment.currencyCode))
                    .font(Typography.body.monospacedDigit())
                    .foregroundStyle(Palette.primaryText.color)
                Text("\(UpcomingDisplay.dateText(payment)) · \(UpcomingDisplay.whenText(payment))")
                    .font(Typography.caption)
                    .foregroundStyle(payment.isOverdue ? Palette.failure.color : Palette.secondaryText.color)
            }
        }
        .fixedSize(horizontal: false, vertical: true)
        .padding(.vertical, Layout.small)
        .accessibilityElement(children: .combine)
    }
}

private struct UpcomingNote: View {
    var body: some View {
        Text(UpcomingDisplay.note)
            .font(Typography.caption)
            .foregroundStyle(Palette.secondaryText.color)
    }
}

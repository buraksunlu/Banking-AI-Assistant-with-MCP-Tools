import SwiftUI

/// The "Gelire göre harcama" card of the home screen (INC-04): last month against income as a three-group bar and at most two
/// pieces of advice, each opening the backend's own sentence. It shows ``IncomeShareViewModel``'s state; the words and the bar
/// come from ``IncomeShareDisplay``. No model is involved, so the card is the same whether the assistant is up or not.
struct IncomeShareCard: View {
    let model: IncomeShareViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            VStack(alignment: .leading, spacing: Layout.tight) {
                SectionTitle(text: IncomeShareDisplay.title)
                if let month = model.month {
                    Text(month)
                        .font(Typography.caption)
                        .foregroundStyle(Palette.secondaryText.color)
                }
            }
            switch model.state {
            case .loading:
                IncomeShareBlock(currency: Skeleton.incomeShare, showsCurrency: false)
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
                    IncomeShareBlock(currency: currency, showsCurrency: currencies.count > 1)
                }
            }
            Text(IncomeShareDisplay.footnote)
                .font(.caption2)
                .foregroundStyle(Palette.secondaryText.color)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .cardSurface()
        .motion(value: model.state)
        // Coming back from another screen runs this again; only a card that never arrived is asked for.
        .task {
            if model.state == .loading { await model.load() }
        }
    }
}

/// One currency of the card: the headline, the bar with its legend and the advice or the reason there is none.
private struct IncomeShareBlock: View {
    let currency: CurrencyAdvice
    let showsCurrency: Bool

    var body: some View {
        let segments = IncomeShareDisplay.segments(currency)
        VStack(alignment: .leading, spacing: Layout.medium) {
            VStack(alignment: .leading, spacing: Layout.tight) {
                if showsCurrency {
                    Text(currency.currencyCode)
                        .font(Typography.eyebrow)
                        .foregroundStyle(Palette.secondaryText.color)
                }
                Text(IncomeShareDisplay.headline(currency))
                    .font(Typography.title)
                    .foregroundStyle(Palette.primaryText.color)
                    .fixedSize(horizontal: false, vertical: true)
                if IncomeShareDisplay.isAboveIncome(currency) {
                    Label("Harcama gelirin üzerinde", systemImage: "arrow.up.right")
                        .font(Typography.caption)
                        .foregroundStyle(Palette.secondaryText.color)
                }
            }
            if !segments.isEmpty {
                IncomeShareBar(segments: segments)
                IncomeShareLegend(segments: segments)
            }
            let advice = IncomeShareDisplay.shownAdvice(currency)
            if advice.isEmpty {
                if let reason = currency.reason, let text = IncomeShareDisplay.reasonText(reason) {
                    Text(text)
                        .font(Typography.caption)
                        .foregroundStyle(Palette.secondaryText.color)
                }
            } else {
                ForEach(advice) { item in
                    AdviceRow(item: item, currency: currency.currencyCode)
                }
            }
        }
    }
}

/// The bar: each group's share of income from the left, the rest an empty track. The legend says the same in words, so the
/// bar itself is not read by VoiceOver.
struct IncomeShareBar: View {
    let segments: [IncomeShareDisplay.Segment]

    var body: some View {
        GeometryReader { proxy in
            HStack(spacing: 0) {
                ForEach(segments) { segment in
                    Rectangle()
                        .fill(IncomeShareDisplay.swatch(segment.group).color)
                        .frame(width: proxy.size.width * segment.fraction)
                }
                Spacer(minLength: 0)
            }
            .background(Palette.raised.color)
            .clipShape(Capsule())
        }
        .frame(height: 10)
        .accessibilityHidden(true)
    }
}

/// "Temel %55,44 · İsteğe bağlı %28,92 · Bilinmiyor %6,00" with a dot of each group's colour; one under another at the largest
/// text sizes.
private struct IncomeShareLegend: View {
    let segments: [IncomeShareDisplay.Segment]
    @Environment(\.dynamicTypeSize) private var textSize

    var body: some View {
        let layout = textSize.isAccessibilitySize ? AnyLayout(VStackLayout(alignment: .leading, spacing: Layout.tight))
                                                  : AnyLayout(HStackLayout(spacing: Layout.medium))
        layout {
            ForEach(segments) { segment in
                HStack(spacing: Layout.tight) {
                    Circle()
                        .fill(IncomeShareDisplay.swatch(segment.group).color)
                        .frame(width: 8, height: 8)
                        .accessibilityHidden(true)
                    Text(segment.label)
                        .font(Typography.caption)
                        .foregroundStyle(Palette.secondaryText.color)
                        .lineLimit(1)
                        .minimumScaleFactor(0.8)
                }
            }
        }
    }
}

/// One piece of advice as a short line; a tap opens the backend's whole sentence under it (K4-B).
private struct AdviceRow: View {
    let item: AdviceItem
    let currency: String
    @State private var isOpen = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        Button {
            withAnimation(Motion.animation(Motion.standard, reduceMotion: reduceMotion)) { isOpen.toggle() }
        } label: {
            VStack(alignment: .leading, spacing: Layout.small) {
                HStack(alignment: .firstTextBaseline, spacing: Layout.small) {
                    Image(systemName: "lightbulb")
                        .foregroundStyle(Palette.accent.color)
                        .accessibilityHidden(true)
                    Text(IncomeShareDisplay.adviceLine(item, currency: currency))
                        .font(.subheadline.weight(.medium))
                        .foregroundStyle(Palette.primaryText.color)
                        .multilineTextAlignment(.leading)
                        .frame(maxWidth: .infinity, alignment: .leading)
                    Image(systemName: isOpen ? "chevron.up" : "chevron.down")
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(Palette.secondaryText.color)
                        .accessibilityHidden(true)
                }
                if isOpen {
                    Text(item.message)
                        .font(Typography.caption)
                        .foregroundStyle(Palette.secondaryText.color)
                        .multilineTextAlignment(.leading)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .frame(minHeight: 44)
            .padding(Layout.medium)
            .background(Palette.raised.color, in: RoundedRectangle(cornerRadius: Layout.controlRadius))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityHint(isOpen ? "Ayrıntıyı kapatır" : "Önerinin ayrıntısını açar")
    }
}

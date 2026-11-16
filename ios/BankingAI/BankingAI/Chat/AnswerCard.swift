import SwiftUI

/// One answer card (UX-01 B): the subject and period, the big figure, its change, the blocks of the chosen mode and the code's
/// one sentence. It draws the card as it came; the switch in its corner shows or hides the detailed part (UX-E).
struct AnswerCard: View {
    let card: AssistantCard
    let mode: AnswerMode
    let select: (AnswerMode) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            HStack(alignment: .center, spacing: Layout.small) {
                Text(card.title)
                    .font(Typography.eyebrow)
                    .foregroundStyle(Palette.secondaryText.color)
                    .accessibilityAddTraits(.isHeader)
                Spacer(minLength: Layout.small)
                if CardDisplay.hasDetail(card) {
                    Picker("Cevap biçimi", selection: Binding(get: { mode }, set: select)) {
                        ForEach(AnswerMode.allCases, id: \.self) { Text($0.title).tag($0) }
                    }
                    .pickerStyle(.segmented)
                    .fixedSize()
                }
            }
            VStack(alignment: .leading, spacing: Layout.tight) {
                Text(card.headline.text)
                    .font(Typography.balance)
                    .foregroundStyle(Palette.primaryText.color)
                if let change = card.change {
                    HStack(spacing: Layout.tight) {
                        if let symbol = CardDisplay.symbol(change.direction) {
                            Image(systemName: symbol).accessibilityHidden(true)
                        }
                        Text("\(change.text) · \(change.against) \(change.againstText)")
                    }
                    .font(Typography.caption)
                    .foregroundStyle(Palette.secondaryText.color)
                }
            }
            ForEach(Array(CardDisplay.blocks(card, mode: mode).enumerated()), id: \.offset) { _, block in
                CardBlockView(block: block)
            }
            Text(card.sentence)
                .font(Typography.body)
                .foregroundStyle(Palette.primaryText.color)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .cardSurface()
        .motion(value: mode)
    }
}

/// One block of a card, by its kind (K3-B): three templates for every tool, so the cards of PR6 need no new view.
struct CardBlockView: View {
    let block: CardBlock

    var body: some View {
        switch block.kind {
        case .bars: BarsBlock(rows: block.rows)
        case .series: SeriesBlock(points: block.points, highlight: block.highlight)
        case .stats: StatsBlock(items: block.items)
        }
    }
}

/// Rows with a bar as long as their share; a change's driver also has its arrow.
private struct BarsBlock: View {
    let rows: [CardBar]
    @Environment(\.dynamicTypeSize) private var textSize

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            ForEach(Array(rows.enumerated()), id: \.offset) { _, row in
                VStack(alignment: .leading, spacing: Layout.tight) {
                    let layout = textSize.isAccessibilitySize ? AnyLayout(VStackLayout(alignment: .leading, spacing: Layout.tight))
                                                              : AnyLayout(HStackLayout(alignment: .firstTextBaseline))
                    layout {
                        Text(row.label)
                            .font(Typography.body)
                            .foregroundStyle(Palette.primaryText.color)
                        if !textSize.isAccessibilitySize { Spacer(minLength: Layout.small) }
                        HStack(spacing: Layout.tight) {
                            if let direction = row.direction, let symbol = CardDisplay.symbol(direction) {
                                Image(systemName: symbol).font(.caption).accessibilityHidden(true)
                            }
                            Text(row.text).font(Typography.body.monospacedDigit())
                        }
                        .foregroundStyle(Palette.primaryText.color)
                    }
                    ShareBar(fraction: CardDisplay.fraction(row.share), color: Palette.accent.color)
                }
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("\(row.label), \(row.text)")
            }
        }
    }
}

/// The months as columns as tall as their share; the running month is paler, the pointed-out month in the accent.
private struct SeriesBlock: View {
    let points: [CardPoint]
    let highlight: String?
    private static let height: CGFloat = 96

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.small) {
            HStack(alignment: .bottom, spacing: Layout.tight) {
                ForEach(Array(points.enumerated()), id: \.offset) { _, point in
                    VStack(spacing: Layout.tight) {
                        RoundedRectangle(cornerRadius: 4)
                            .fill(color(point))
                            .frame(height: max(Self.height * CardDisplay.fraction(point.share), 2))
                        Text(point.label)
                            .font(.caption2)
                            .foregroundStyle(Palette.secondaryText.color)
                            .lineLimit(1)
                            .minimumScaleFactor(0.6)
                    }
                    .frame(maxWidth: .infinity)
                    .accessibilityElement(children: .ignore)
                    .accessibilityLabel("\(point.label), \(point.text)\(point.partial ? ", devam ediyor" : "")")
                }
            }
            .frame(height: Self.height + 20, alignment: .bottom)
            if let shown = points.first(where: { $0.label == highlight }) {
                Text("\(shown.label): \(shown.text)")
                    .font(Typography.caption)
                    .foregroundStyle(Palette.secondaryText.color)
            }
        }
    }

    private func color(_ point: CardPoint) -> Color {
        let base = point.label == highlight ? Palette.accent.color : Palette.accent.color.opacity(0.45)
        return point.partial ? base.opacity(0.4) : base
    }
}

private struct StatsBlock: View {
    let items: [CardStat]

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(Array(items.enumerated()), id: \.offset) { index, item in
                if index > 0 { HairlineDivider() }
                DetailRow(label: item.label, value: item.text)
            }
        }
    }
}

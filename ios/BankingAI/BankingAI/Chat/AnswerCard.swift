import SwiftUI

/// One answer card (UX-01 B): the subject and period, the big figure, its change, the first rows and the code's one sentence.
/// "Tümünü göster" under the rows opens the rest in place (16 Nov 2026 review, replacing Basit/Detaylı); a card without rows has
/// its answer's transactions inside, the first five, with the way to the whole list.
struct AnswerCard: View {
    let card: AssistantCard
    let expanded: Bool
    /// The link whose transactions go inside this card, or nil (`CardDisplay.cardTransactions`).
    var transactions: AssistantLink?
    var list: (AssistantLink) -> TransactionHistoryViewModel = { _ in fatalError("A card with transactions needs their list.") }
    var breakdown: (AssistantLink) -> BreakdownViewModel? = { _ in nil }
    var insight: (TransactionItem) -> TransactionInsightViewModel? = { _ in nil }
    let toggle: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            Text(card.title)
                .font(Typography.eyebrow)
                .foregroundStyle(Palette.secondaryText.color)
                .accessibilityAddTraits(.isHeader)
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
            ForEach(Array(CardDisplay.blocks(card, expanded: expanded).enumerated()), id: \.offset) { _, block in
                CardBlockView(block: block)
            }
            if let title = CardDisplay.moreTitle(card, expanded: expanded) {
                MoreLine(title: title, expanded: expanded, action: toggle)
            }
            if let transactions {
                CardTransactions(link: transactions, list: list, breakdown: breakdown, insight: insight)
            }
            if CardDisplay.showsSentence(card.sentence, withTransactions: transactions != nil) {
                Text(card.sentence)
                    .font(Typography.body)
                    .foregroundStyle(Palette.primaryText.color)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            ForEach(Array(CardDisplay.notes(card).enumerated()), id: \.offset) { _, note in
                Text(note)
                    .font(Typography.caption)
                    .foregroundStyle(Palette.secondaryText.color)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .cardSurface()
        .motion(value: expanded)
    }
}

/// "Tümünü göster (7)" / "Daha az göster": the whole line is the button, at least 44 pt tall.
private struct MoreLine: View {
    let title: String
    let expanded: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: Layout.tight) {
                Text(title)
                Image(systemName: expanded ? "chevron.up" : "chevron.down")
                    .font(.caption.weight(.semibold))
                    .accessibilityHidden(true)
                Spacer(minLength: 0)
            }
            .font(.subheadline.weight(.medium))
            .foregroundStyle(Palette.accent.color)
            .frame(minHeight: 44)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

/// The answer's transactions inside its card (Bulgu 1): the first page of the link's own search, its first five rows (each opens
/// its detail), and the whole list when there are more. The rows and the count are the backend's; the phone adds nothing up.
private struct CardTransactions: View {
    let link: AssistantLink
    let list: (AssistantLink) -> TransactionHistoryViewModel
    let breakdown: (AssistantLink) -> BreakdownViewModel?
    let insight: (TransactionItem) -> TransactionInsightViewModel?
    @State private var model: TransactionHistoryViewModel

    init(link: AssistantLink, list: @escaping (AssistantLink) -> TransactionHistoryViewModel,
         breakdown: @escaping (AssistantLink) -> BreakdownViewModel?, insight: @escaping (TransactionItem) -> TransactionInsightViewModel?) {
        self.link = link
        self.list = list
        self.breakdown = breakdown
        self.insight = insight
        _model = State(initialValue: list(link))
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            if model.items.isEmpty, let message = model.errorMessage {
                StatusBand(text: message, actionTitle: "Tekrar dene") { Task { await model.retry() } }
            } else if model.items.isEmpty && !model.loadedOnce {
                TransactionSkeletonRows(items: Array(Skeleton.history(firstPage: false).prefix(CardDisplay.previewRows)),
                                        verticalPadding: Layout.small)
                    .skeleton()
            } else {
                ForEach(Array(model.items.prefix(CardDisplay.previewRows))) { item in
                    HairlineDivider()
                    // A short answer shows its rows here and has no "see all" link, so the row itself opens the detail
                    // and its investment comparison (DEMO-02 simulator finding).
                    NavigationLink {
                        TransactionDetailView(item: item, insight: insight(item))
                    } label: {
                        TransactionRow(item: item)
                            .padding(.vertical, Layout.small)
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
                if let title = CardDisplay.allTransactionsTitle(total: model.totalCount) {
                    HairlineDivider()
                    NavigationLink {
                        LinkedTransactionsView(link: link, model: list(link), breakdown: breakdown(link), insight: insight)
                    } label: {
                        HStack(spacing: Layout.tight) {
                            Text(title)
                            Image(systemName: "chevron.right")
                                .font(.caption.weight(.semibold))
                                .accessibilityHidden(true)
                            Spacer(minLength: 0)
                        }
                        .font(.subheadline.weight(.medium))
                        .foregroundStyle(Palette.accent.color)
                        .frame(minHeight: 44)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
            }
        }
        .task { if model.items.isEmpty { await model.loadMore() } }
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
        case .list: ListBlock(entries: block.entries)
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
                        VStack(alignment: textSize.isAccessibilitySize ? .leading : .trailing, spacing: 0) {
                            HStack(spacing: Layout.tight) {
                                if let direction = row.direction, let symbol = CardDisplay.symbol(direction) {
                                    Image(systemName: symbol).font(.caption).accessibilityHidden(true)
                                }
                                Text(row.text).font(Typography.body.monospacedDigit())
                            }
                            .foregroundStyle(Palette.primaryText.color)
                            // Simulator trial 17 Nov 2026: the share of the income on its own line; on the amount's line the row broke.
                            if !row.detail.isEmpty {
                                Text(row.detail)
                                    .font(Typography.caption.monospacedDigit())
                                    .foregroundStyle(Palette.secondaryText.color)
                            }
                        }
                    }
                    ShareBar(fraction: CardDisplay.fraction(row.share), color: Palette.accent.color)
                }
                .accessibilityElement(children: .ignore)
                .accessibilityLabel(row.detail.isEmpty ? "\(row.label), \(row.text)" : "\(row.label), \(row.text), \(row.detail)")
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

/// Payments or purchases, one under another: the name and its amount on the first line, where and when on the second.
private struct ListBlock: View {
    let entries: [CardEntry]
    @Environment(\.dynamicTypeSize) private var textSize

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(Array(entries.enumerated()), id: \.offset) { index, entry in
                if index > 0 { HairlineDivider() }
                VStack(alignment: .leading, spacing: Layout.tight) {
                    let layout = textSize.isAccessibilitySize ? AnyLayout(VStackLayout(alignment: .leading, spacing: Layout.tight))
                                                              : AnyLayout(HStackLayout(alignment: .firstTextBaseline))
                    layout {
                        Text(entry.label)
                            .font(Typography.body)
                            .foregroundStyle(Palette.primaryText.color)
                        if !textSize.isAccessibilitySize { Spacer(minLength: Layout.small) }
                        HStack(spacing: Layout.tight) {
                            if let direction = entry.direction, let symbol = CardDisplay.symbol(direction) {
                                Image(systemName: symbol).font(.caption).accessibilityHidden(true)
                            }
                            Text(entry.text).font(Typography.body.monospacedDigit())
                        }
                        .foregroundStyle(Palette.primaryText.color)
                    }
                    Text(entry.detail)
                        .font(Typography.caption)
                        .foregroundStyle(Palette.secondaryText.color)
                }
                .padding(.vertical, Layout.small)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("\(entry.label), \(entry.text), \(entry.detail)")
            }
        }
    }
}

import Foundation

/// The few choices made on the phone about a card (UX-01 PR5): which parts are shown and how long a bar is. No figure is
/// formatted or computed here; every text is the card's own (K3-B), the counts are rows the card already has.
enum CardDisplay {
    /// Transactions shown inside a card before "Tümünü göster" opens the whole list.
    static let previewRows = 5

    /// The first part always, the rest once the card is opened (16 Nov 2026 review: "Tümünü göster" replaces Basit/Detaylı).
    static func blocks(_ card: AssistantCard, expanded: Bool) -> [CardBlock] {
        expanded ? card.simple + card.detailed : card.simple
    }

    /// Code's notes go under the card open or closed (UX-01 PR6 P3-A): an estimate's rule or an investment warning is never hidden.
    static func notes(_ card: AssistantCard) -> [String] {
        card.notes
    }

    /// The line that opens or closes a card; nil when the card has nothing more. Rows are counted so the line says how long the
    /// open list is; a card whose rest is figures, not rows, offers its details instead.
    static func moreTitle(_ card: AssistantCard, expanded: Bool) -> String? {
        guard !card.detailed.isEmpty else { return nil }
        if expanded { return "Daha az göster" }
        let hidden = rows(card.detailed)
        return hidden > 0 ? "Tümünü göster (\(rows(card.simple) + hidden))" : "Ayrıntıları göster"
    }

    /// The link whose transactions go inside the card (Bulgu 1): only for a single card that has no rows of its own (a total,
    /// a merchant), where the list is the answer's content; a card of bars or lines keeps the link under it.
    static func cardTransactions(_ cards: [AssistantCard], links: [AssistantLink]) -> AssistantLink? {
        guard cards.count == 1, links.count == 1, rows(cards[0].simple + cards[0].detailed) == 0 else { return nil }
        return links[0]
    }

    /// The way to the whole list once it is longer than the card shows; the count is the backend's.
    static func allTransactionsTitle(total: Int) -> String? {
        total > previewRows ? "Tümünü göster (\(total))" : nil
    }

    /// The code's sentence repeats the headline; the transactions say more, so it gives way to them.
    static func showsSentence(withTransactions: Bool) -> Bool {
        !withTransactions
    }

    /// The AI service's share as a part of the bar, kept between empty and full.
    static func fraction(_ share: Double) -> Double {
        min(max(share, 0), 1)
    }

    static func symbol(_ direction: CardDirection) -> String? {
        switch direction {
        case .up: "arrow.up.right"
        case .down: "arrow.down.right"
        case .same: nil
        }
    }

    private static func rows(_ blocks: [CardBlock]) -> Int {
        blocks.reduce(0) { $0 + $1.rows.count + $1.entries.count }
    }
}

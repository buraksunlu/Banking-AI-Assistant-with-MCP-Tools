import Foundation

/// The few choices made on the phone about a card (UX-01 PR5): which parts are shown and how long a bar is. No figure is
/// formatted or computed here; every text is the card's own (K3-B).
enum CardDisplay {
    /// The simple part always, the detailed part on request (UX-E).
    static func blocks(_ card: AssistantCard, mode: AnswerMode) -> [CardBlock] {
        mode == .detailed ? card.simple + card.detailed : card.simple
    }

    /// A card with nothing more to show has no switch (§6.3).
    static func hasDetail(_ card: AssistantCard) -> Bool {
        !card.detailed.isEmpty
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
}

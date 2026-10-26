import SwiftUI

/// One transaction: symbol, title, "kind · day" and the signed amount, incoming money in green (IOS-07 K4-A). At the
/// accessibility text sizes the row becomes one column without the symbol (UI-06 K9-A): side by side, the title was cut to a
/// few letters and the amount broke over three lines.
struct TransactionRow: View {
    let item: TransactionItem
    @Environment(\.dynamicTypeSize) private var textSize

    var body: some View {
        let shown = TransactionDisplay(item)
        Group {
            if textSize.isAccessibilitySize {
                VStack(alignment: .leading, spacing: 2) {
                    title(shown)
                    subtitle(shown)
                    amount(shown)
                }
                // Wrap, never cut with "…".
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
            } else {
                HStack(spacing: 12) {
                    Image(systemName: shown.symbol)
                        .font(.body)
                        .foregroundStyle(Palette.secondaryText.color)
                        .frame(width: 36, height: 36)
                        .background(Palette.raised.color, in: Circle())
                    VStack(alignment: .leading, spacing: 2) {
                        title(shown).lineLimit(1)
                        subtitle(shown)
                    }
                    Spacer(minLength: 8)
                    amount(shown)
                }
            }
        }
        .accessibilityElement(children: .combine)
    }

    private func title(_ shown: TransactionDisplay) -> some View {
        Text(shown.title)
            .font(.subheadline.weight(.semibold))
            .foregroundStyle(Palette.primaryText.color)
    }

    private func subtitle(_ shown: TransactionDisplay) -> some View {
        Text(shown.subtitle)
            .font(.caption)
            .foregroundStyle(Palette.secondaryText.color)
    }

    private func amount(_ shown: TransactionDisplay) -> some View {
        Text(shown.amount)
            .font(.subheadline.weight(.semibold))
            .monospacedDigit()
            .foregroundStyle(shown.isIncoming ? Palette.incoming.color : Palette.primaryText.color)
    }
}

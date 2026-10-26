import SwiftUI

/// One transaction: symbol, title, "kind · day" and the signed amount, incoming money in green (IOS-07 K4-A).
struct TransactionRow: View {
    let item: TransactionItem

    var body: some View {
        let shown = TransactionDisplay(item)
        HStack(spacing: 12) {
            Image(systemName: shown.symbol)
                .font(.body)
                .foregroundStyle(Palette.secondaryText.color)
                .frame(width: 36, height: 36)
                .background(Palette.raised.color, in: Circle())
            VStack(alignment: .leading, spacing: 2) {
                Text(shown.title)
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(Palette.primaryText.color)
                    .lineLimit(1)
                Text(shown.subtitle)
                    .font(.caption)
                    .foregroundStyle(Palette.secondaryText.color)
            }
            Spacer(minLength: 8)
            Text(shown.amount)
                .font(.subheadline.weight(.semibold))
                .monospacedDigit()
                .foregroundStyle(shown.isIncoming ? Palette.incoming.color : Palette.primaryText.color)
        }
        .accessibilityElement(children: .combine)
    }
}

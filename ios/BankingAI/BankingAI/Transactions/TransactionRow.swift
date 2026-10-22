import SwiftUI

/// One transaction: symbol, title, "kind · day" and the signed amount, incoming money in green (IOS-07 K4-A).
struct TransactionRow: View {
    let item: TransactionItem

    var body: some View {
        let shown = TransactionDisplay(item)
        HStack(spacing: 12) {
            Image(systemName: shown.symbol)
                .font(.body)
                .foregroundStyle(.tint)
                .frame(width: 36, height: 36)
                .background(.tint.opacity(0.12), in: Circle())
            VStack(alignment: .leading, spacing: 2) {
                Text(shown.title)
                    .font(.subheadline.weight(.semibold))
                    .lineLimit(1)
                Text(shown.subtitle)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer(minLength: 8)
            Text(shown.amount)
                .font(.subheadline.weight(.semibold))
                .monospacedDigit()
                .foregroundStyle(shown.isIncoming ? Color.green : Color.primary)
        }
        .accessibilityElement(children: .combine)
    }
}

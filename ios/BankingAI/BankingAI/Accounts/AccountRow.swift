import SwiftUI

/// One account as a card: name and number on the left, balance on the right, "İşletme" on a shop's account.
struct AccountRow: View {
    let account: Account

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: account.accountType == .business ? "storefront" : "creditcard")
                .font(.title3)
                .foregroundStyle(.tint)
                .frame(width: 44, height: 44)
                .background(.tint.opacity(0.12), in: Circle())
            VStack(alignment: .leading, spacing: 2) {
                Text(account.accountName)
                    .font(.headline)
                // In full: the numbers are synthetic and the customer's own (decision IOS-04 K2-A).
                Text(account.accountNumber)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer(minLength: 8)
            VStack(alignment: .trailing, spacing: 4) {
                Text(Money.text(account.balance, currency: account.currencyCode))
                    .font(.headline)
                    .monospacedDigit()
                if account.accountType == .business {
                    Text("İşletme")
                        .font(.caption2.bold())
                        .padding(.horizontal, 8)
                        .padding(.vertical, 2)
                        .background(.tint.opacity(0.12), in: Capsule())
                        .foregroundStyle(.tint)
                }
            }
            Image(systemName: "chevron.right")
                .font(.caption.weight(.semibold))
                .foregroundStyle(.tertiary)
                .accessibilityHidden(true)
        }
        .padding()
        // One step above the screen, so the card does not melt into the near-black behind it.
        .background(Palette.surface.color, in: RoundedRectangle(cornerRadius: 16))
        .accessibilityElement(children: .combine)
    }
}

import SwiftUI

/// One account as a card (UI-03 K3-A): the name small, the balance large and light, the shortened number below, "İŞLETME" on
/// a shop's account. The full number is on the detail screen.
struct AccountRow: View {
    let account: Account

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.small) {
            HStack {
                Text(account.accountName)
                    .font(Typography.caption)
                    .foregroundStyle(Palette.secondaryText.color)
                    // Wraps beside the tag at the largest sizes instead of ending in "…".
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: Layout.small)
                if account.accountType == .business {
                    BusinessTag()
                }
            }
            Text(Money.text(account.balance, currency: account.currencyCode))
                .font(Typography.balance)
                .foregroundStyle(Palette.primaryText.color)
                .lineLimit(1)
                .minimumScaleFactor(0.6)
                // Turns over when a refresh brings a new balance; the section's state change carries the animation.
                .contentTransition(.numericText())
            HStack {
                Text(AccountDisplay.shortNumber(account.accountNumber))
                    .font(Typography.caption)
                    .monospacedDigit()
                    .foregroundStyle(Palette.secondaryText.color)
                Spacer()
                Image(systemName: "chevron.right")
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(Palette.secondaryText.color)
                    .accessibilityHidden(true)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .cardSurface()
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(AccountDisplay.spokenSummary(account))
    }
}

import SwiftUI

/// "Had this been invested" (DEMO-02): each asset's value today and the difference, as the backend computed them, with the
/// method, the left-out recent spending and the fixed warning. Used under the non-routine group and on a transaction's detail.
struct InvestmentCard: View {
    let title: String
    let investment: BreakdownInvestment

    var body: some View {
        VStack(alignment: .leading, spacing: Layout.medium) {
            Text(title)
                .font(Typography.body.weight(.semibold))
                .foregroundStyle(Palette.primaryText.color)
            if investment.assets.isEmpty {
                caption("Karşılaştırmak için erken: bu harcamalar son 7 günde yapıldı.")
            } else {
                caption("\(Money.text(investment.amount, currency: "TRY")) bugün:")
                VStack(spacing: 0) {
                    ForEach(Array(investment.assets.enumerated()), id: \.offset) { index, asset in
                        if index > 0 { HairlineDivider() }
                        AssetLine(asset: asset)
                    }
                }
                caption(BreakdownDisplay.methodLine)
            }
            if let excluded = BreakdownDisplay.excludedText(investment) { caption(excluded) }
            caption(investment.disclaimer)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .cardSurface()
    }

    private func caption(_ text: String) -> some View {
        Text(text)
            .font(Typography.caption)
            .foregroundStyle(Palette.secondaryText.color)
            .fixedSize(horizontal: false, vertical: true)
    }
}

/// One asset: its name on the left, the value and the difference on the right; stacked at the largest text sizes.
private struct AssetLine: View {
    let asset: InvestmentAsset
    @Environment(\.dynamicTypeSize) private var textSize

    var body: some View {
        let layout = textSize.isAccessibilitySize ? AnyLayout(VStackLayout(alignment: .leading, spacing: 2))
            : AnyLayout(HStackLayout(spacing: Layout.small))
        layout {
            Text(BreakdownDisplay.assetName(asset))
                .font(Typography.body)
                .foregroundStyle(Palette.primaryText.color)
            if !textSize.isAccessibilitySize { Spacer(minLength: Layout.small) }
            VStack(alignment: textSize.isAccessibilitySize ? .leading : .trailing, spacing: 2) {
                Text(BreakdownDisplay.valueText(asset))
                    .font(Typography.body.monospacedDigit())
                    .foregroundStyle(Palette.primaryText.color)
                Text(BreakdownDisplay.differenceText(asset))
                    .font(Typography.caption.monospacedDigit())
                    .foregroundStyle(Palette.secondaryText.color)
            }
        }
        .padding(.vertical, Layout.small)
        .accessibilityElement(children: .combine)
    }
}

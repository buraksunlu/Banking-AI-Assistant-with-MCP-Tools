import SwiftUI

/// An empty or failed state in the app's own look (UI-06 K4-C), replacing the system's `ContentUnavailableView`: a symbol in a
/// thin ring, a title, an optional line and an optional quiet button. `.inline` is the compact one for a part of a screen (the
/// caller puts it on a card where the content would have been); `.screen` fills a screen of its own. The texts are the
/// screens' own (K5-A).
struct StateMessage: View {
    enum Size { case inline, screen }

    let title: String
    let symbol: String
    var message: String?
    var size: Size = .inline
    var actionTitle: String?
    var action: (() -> Void)?

    @ScaledMetric(relativeTo: .body) private var ring: CGFloat = 40

    var body: some View {
        VStack(spacing: size == .screen ? Layout.regular : Layout.medium) {
            VStack(spacing: size == .screen ? Layout.regular : Layout.medium) {
                Image(systemName: symbol)
                    .font(size == .screen ? .title3 : .body)
                    .foregroundStyle(Palette.secondaryText.color)
                    .frame(width: ringSize, height: ringSize)
                    .overlay(Circle().stroke(Palette.hairline.color, lineWidth: 1))
                    .accessibilityHidden(true)
                VStack(spacing: Layout.tight) {
                    Text(title)
                        .font(size == .screen ? Typography.title : .subheadline.weight(.semibold))
                        .foregroundStyle(Palette.primaryText.color)
                    if let message {
                        Text(message)
                            .font(Typography.caption)
                            .foregroundStyle(Palette.secondaryText.color)
                    }
                }
                .multilineTextAlignment(.center)
                // Wrap, never cut with "…": at the largest sizes the card would otherwise keep one line of each.
                .fixedSize(horizontal: false, vertical: true)
            }
            // The title and its line are one sentence for VoiceOver; the button stays its own element.
            .accessibilityElement(children: .combine)
            if let actionTitle, let action {
                Button(actionTitle, action: action)
                    .buttonStyle(SecondaryButtonStyle())
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, size == .screen ? Layout.huge : Layout.large)
        .padding(.horizontal, Layout.regular)
    }

    private var ringSize: CGFloat { size == .screen ? ring * 1.4 : ring }
}

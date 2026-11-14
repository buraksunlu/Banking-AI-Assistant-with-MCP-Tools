import SwiftUI

extension View {
    /// The card: a surface one step above the screen with a hairline around it.
    func cardSurface(padding: CGFloat = Layout.regular) -> some View {
        self.padding(padding)
            .background(Palette.surface.color, in: RoundedRectangle(cornerRadius: Layout.cardRadius))
            .overlay(RoundedRectangle(cornerRadius: Layout.cardRadius).stroke(Palette.hairline.color, lineWidth: 1))
    }
}

/// The small capital label above a section.
struct SectionTitle: View {
    let text: String

    var body: some View {
        Text(Typography.label(text))
            .font(Typography.eyebrow)
            .tracking(1)
            .foregroundStyle(Palette.secondaryText.color)
            .accessibilityAddTraits(.isHeader)
    }
}

/// The one filled button of a screen: the yellow button fill with a dark label.
struct PrimaryButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.body.weight(.semibold))
            .foregroundStyle(Palette.onAccent.color)
            .frame(maxWidth: .infinity)
            .padding(.vertical, Layout.regular)
            .background(Palette.accentFill.color.opacity(configuration.isPressed ? 0.8 : 1),
                        in: RoundedRectangle(cornerRadius: Layout.controlRadius))
    }
}

/// A quiet button: a hairline around the label, no fill.
struct SecondaryButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.body.weight(.medium))
            .foregroundStyle(Palette.primaryText.color)
            .padding(.horizontal, Layout.regular)
            .padding(.vertical, Layout.medium)
            .overlay(RoundedRectangle(cornerRadius: Layout.controlRadius).stroke(Palette.hairline.color, lineWidth: 1))
            .opacity(configuration.isPressed ? 0.6 : 1)
    }
}

/// A suggestion to tap: a thin outlined capsule. A chosen one (a period in use, UI-05 K3-A) takes the accent.
struct ChipButtonStyle: ButtonStyle {
    var isSelected = false

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.footnote)
            .foregroundStyle(isSelected ? Palette.accent.color : Palette.primaryText.color)
            .padding(.horizontal, Layout.medium)
            .padding(.vertical, Layout.small)
            .overlay(Capsule().stroke(isSelected ? Palette.accent.color : Palette.hairline.color, lineWidth: 1))
            .opacity(configuration.isPressed ? 0.6 : 1)
    }
}

/// A note above a list or a screen ("could not refresh"); it replaces the orange bands screen by screen. An action, when
/// given, sits at its end in the accent colour (UI-02 K8-A).
struct StatusBand: View {
    let text: String
    var symbol = "wifi.exclamationmark"
    var actionTitle: String?
    var action: (() -> Void)?
    @Environment(\.dynamicTypeSize) private var textSize

    var body: some View {
        // Side by side at the largest sizes the text was hyphenated a syllable a line; the action then goes under it.
        let layout = textSize.isAccessibilitySize ? AnyLayout(VStackLayout(alignment: .leading, spacing: Layout.small))
                                                  : AnyLayout(HStackLayout(spacing: Layout.small))
        layout {
            Label(text, systemImage: symbol)
                .font(.subheadline)
                .foregroundStyle(Palette.primaryText.color)
                .frame(maxWidth: .infinity, alignment: .leading)
            if let actionTitle, let action {
                Button(actionTitle, action: action)
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(Palette.accent.color)
            }
        }
        .padding(Layout.medium)
        .background(Palette.raised.color, in: RoundedRectangle(cornerRadius: Layout.controlRadius))
    }
}

/// The assistant's mark: its initial (``Brand/assistantInitial``) in a thin accent ring (UI-02 K2-B, UI-04 K2). Decoration only; the label beside it names
/// the assistant for VoiceOver.
struct AssistantMark: View {
    var size: CGFloat = 20

    var body: some View {
        Text(Brand.assistantInitial)
            .font(.system(size: size * 0.5, weight: .semibold))
            .foregroundStyle(Palette.accent.color)
            .frame(width: size, height: size)
            .overlay(Circle().stroke(Palette.accent.color, lineWidth: 1))
            .accessibilityHidden(true)
    }
}

extension View {
    /// A text field on the screen (UI-04 K4-B): a raised fill with a hairline that takes the accent while the field has the
    /// focus. The label is not inside the field, so it stays visible once the user has typed.
    func inputField(focused: Bool) -> some View {
        self.padding(Layout.regular)
            .background(Palette.raised.color, in: RoundedRectangle(cornerRadius: Layout.controlRadius))
            .overlay(RoundedRectangle(cornerRadius: Layout.controlRadius)
                .stroke(focused ? Palette.accent.color : Palette.hairline.color, lineWidth: 1))
    }
}

/// The app's name as a mark: large, light, in the accent, with the tagline under it (UI-04 K1). Read as one heading.
struct BrandMark: View {
    @ScaledMetric(relativeTo: .largeTitle) private var size: CGFloat = 44

    var body: some View {
        VStack(spacing: Layout.small) {
            Text(Brand.appName)
                .font(.system(size: size, weight: .light))
                .tracking(2)
                .foregroundStyle(Palette.accent.color)
            Text(Brand.tagline)
                .font(Typography.caption)
                .foregroundStyle(Palette.secondaryText.color)
        }
        .multilineTextAlignment(.center)
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(.isHeader)
    }
}

/// The thin line between rows of a card.
struct HairlineDivider: View {
    var leading: CGFloat = 0

    var body: some View {
        Rectangle().fill(Palette.hairline.color).frame(height: 1).padding(.leading, leading)
            .accessibilityHidden(true)
    }
}

/// A label with its value (UI-05 K5-A): side by side, and one above the other when the text size leaves no room.
struct DetailRow: View {
    let label: String
    let value: String

    var body: some View {
        ViewThatFits(in: .horizontal) {
            HStack(alignment: .firstTextBaseline) {
                labelText
                Spacer(minLength: Layout.medium)
                valueText.multilineTextAlignment(.trailing)
            }
            VStack(alignment: .leading, spacing: Layout.tight) {
                labelText
                valueText
            }
        }
        .padding(.vertical, Layout.medium)
        .accessibilityElement(children: .combine)
    }

    private var labelText: some View {
        Text(label).font(Typography.caption).foregroundStyle(Palette.secondaryText.color)
    }

    private var valueText: some View {
        Text(value).font(Typography.body).foregroundStyle(Palette.primaryText.color).textSelection(.enabled)
    }
}

extension View {
    /// A pushed screen on the screen colour: the same colour behind the content and behind the navigation bar, so the bar
    /// does not turn system grey while the content scrolls under it (UI-05).
    func blackScreen() -> some View {
        self.background(Palette.background.color)
            .toolbarBackground(Palette.background.color, for: .navigationBar)
    }
}

/// "İŞLETME" on a shop's account: a small outlined capsule in the accent.
struct BusinessTag: View {
    var body: some View {
        Text(Typography.label("İşletme"))
            .font(Typography.eyebrow)
            .tracking(1)
            .foregroundStyle(Palette.accent.color)
            .padding(.horizontal, Layout.small)
            .padding(.vertical, Layout.tight)
            .overlay(Capsule().stroke(Palette.accent.color, lineWidth: 1))
            // One word in a capsule: broken over two lines it no longer reads as a tag.
            .fixedSize()
    }
}

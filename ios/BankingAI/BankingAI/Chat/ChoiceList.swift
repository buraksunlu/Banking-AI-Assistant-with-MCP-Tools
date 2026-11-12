import SwiftUI

/// How an option of a question asked back is written on its row (CHAT-10 PR3b).
enum ChoiceDisplay {
    private static let turkish = Locale(identifier: "tr_TR")

    /// The label with a Turkish capital first letter ("ılık" → "Ilık", "işyeri" → "İşyeri"), so the row reads like a sentence. Only
    /// the row shows it: a tap sends the label exactly as the backend wrote it (P4-A).
    static func title(_ label: String) -> String {
        guard let first = label.first else { return label }
        return String(first).uppercased(with: turkish) + label.dropFirst()
    }
}

/// The options of a question asked back: one card under the question, a row per option split by hairlines, the way grouped
/// lists read on iOS. A row is the option, a short line of what it means when there is one, and the accent chevron that says
/// "tap me" (UI-04: the accent is the one colour that invites a tap).
struct ChoiceList: View {
    let options: [AssistantChoice]
    /// A last row inviting an answer of the user's own (PR3b D6-A); "" for none.
    var inputHint = ""
    let isEnabled: Bool
    let choose: (AssistantChoice) -> Void
    /// Opens the keyboard: the hint row sends nothing, the user types the answer.
    var write: () -> Void = {}

    var body: some View {
        VStack(spacing: 0) {
            ForEach(Array(options.enumerated()), id: \.element) { index, option in
                if index > 0 {
                    Rectangle()
                        .fill(Palette.hairline.color)
                        .frame(height: 1)
                        .padding(.leading, Layout.regular)
                }
                Button { choose(option) } label: { row(option) }
                    .buttonStyle(ChoiceRowStyle())
                    // One element: the option and what it means read together (D5-A); the chevron is only a picture.
                    .accessibilityElement(children: .combine)
            }
            if !inputHint.isEmpty {
                Rectangle()
                    .fill(Palette.hairline.color)
                    .frame(height: 1)
                    .padding(.leading, Layout.regular)
                Button(action: write) { hintRow }
                    .buttonStyle(ChoiceRowStyle())
                    .accessibilityHint("Klavyeyi açar")
            }
        }
        .background(Palette.raised.color)
        .clipShape(RoundedRectangle(cornerRadius: Layout.cardRadius))
        .overlay(RoundedRectangle(cornerRadius: Layout.cardRadius).stroke(Palette.hairline.color, lineWidth: 1))
        .disabled(!isEnabled)
        .opacity(isEnabled ? 1 : 0.5)
    }

    /// Looks like an option but in the secondary colour with a pencil: it opens the keyboard instead of sending.
    private var hintRow: some View {
        HStack(spacing: Layout.medium) {
            Text(inputHint)
                .font(Typography.body)
                .foregroundStyle(Palette.secondaryText.color)
                .multilineTextAlignment(.leading)
                .frame(maxWidth: .infinity, alignment: .leading)
            Image(systemName: "pencil")
                .font(.footnote.weight(.semibold))
                .foregroundStyle(Palette.accent.color)
                .accessibilityHidden(true)
        }
        .padding(.horizontal, Layout.regular)
        .padding(.vertical, Layout.medium)
        .frame(minHeight: 44)
        .contentShape(Rectangle())
    }

    private func row(_ option: AssistantChoice) -> some View {
        HStack(spacing: Layout.medium) {
            VStack(alignment: .leading, spacing: Layout.tight) {
                Text(ChoiceDisplay.title(option.label))
                    .font(Typography.body.weight(.medium))
                    .foregroundStyle(Palette.primaryText.color)
                if !option.detail.isEmpty {
                    Text(option.detail)
                        .font(Typography.caption)
                        .foregroundStyle(Palette.secondaryText.color)
                }
            }
            .multilineTextAlignment(.leading)
            .frame(maxWidth: .infinity, alignment: .leading)
            Image(systemName: "chevron.right")
                .font(.footnote.weight(.semibold))
                .foregroundStyle(Palette.accent.color)
                .accessibilityHidden(true)
        }
        .padding(.horizontal, Layout.regular)
        .padding(.vertical, Layout.medium)
        // 44 to tap (UI-06 K9-A), whatever the text size.
        .frame(minHeight: 44)
        .contentShape(Rectangle())
    }
}

/// A row that lights up while it is held, like a cell of a grouped list.
private struct ChoiceRowStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .background(configuration.isPressed ? Palette.hairline.color : .clear)
    }
}

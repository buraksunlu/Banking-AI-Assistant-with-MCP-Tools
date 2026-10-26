import SwiftUI

/// One message: the user's in a dark capsule on the right, the assistant's written on the screen itself, under its mark
/// (UI-02, K3-A K2-B). A failed question shows why, with "Tekrar dene"; a stopped one can be sent again too.
struct MessageBubble: View {
    let message: ChatMessage
    let canRetry: Bool
    /// The list a link under this answer opens; built when the button is tapped (CHAT-07).
    let list: (AssistantLink) -> TransactionHistoryViewModel
    let retry: () -> Void

    var body: some View {
        switch message.role {
        case .user: question
        case .assistant: answer
        }
    }

    private var question: some View {
        HStack {
            Spacer(minLength: 48)
            VStack(alignment: .trailing, spacing: Layout.tight) {
                Text(message.displayText)
                    .font(Typography.body)
                    .foregroundStyle(Palette.primaryText.color)
                    .padding(.horizontal, Layout.regular)
                    .padding(.vertical, Layout.medium)
                    .background(Palette.raised.color, in: RoundedRectangle(cornerRadius: Layout.cardRadius))
                    .textSelection(.enabled)
                status
            }
        }
    }

    // Full width, no balloon: an answer is read, not chatted (K3-A).
    private var answer: some View {
        VStack(alignment: .leading, spacing: Layout.small) {
            AssistantHeader()
            Text(message.displayText)
                .font(Typography.body)
                .foregroundStyle(Palette.primaryText.color)
                .frame(maxWidth: .infinity, alignment: .leading)
                .textSelection(.enabled)
            // Under its own answer for good, unlike the suggestions: the list is the proof of this answer's figure.
            ForEach(message.links, id: \.self) { link in
                // A link with its own destination, not a value: a value link pushed after the chat's own destination link
                // makes the unnamed stack pop straight back (found on the simulator, not by any unit test).
                NavigationLink {
                    LinkedTransactionsView(link: link, model: list(link))
                } label: {
                    HStack(spacing: Layout.tight) {
                        Text(link.title)
                        Image(systemName: "chevron.right")
                            .font(.caption.weight(.semibold))
                            .accessibilityHidden(true)
                    }
                    .font(.subheadline.weight(.medium))
                    .foregroundStyle(Palette.accent.color)
                }
                .buttonStyle(.plain)
            }
        }
    }

    @ViewBuilder
    private var status: some View {
        switch message.state {
        case .failed(let reason):
            VStack(alignment: .trailing, spacing: Layout.tight) {
                Label(reason, systemImage: "exclamationmark.circle")
                    .font(Typography.caption)
                    .foregroundStyle(Palette.failure.color)
                    .multilineTextAlignment(.trailing)
                retryButton("Tekrar dene")
            }
        case .stopped:
            HStack(spacing: Layout.small) {
                Text("Durduruldu")
                    .font(Typography.caption)
                    .foregroundStyle(Palette.secondaryText.color)
                retryButton("Tekrar gönder")
            }
        case .sent, .waiting:
            EmptyView()
        }
    }

    private func retryButton(_ title: String) -> some View {
        Button(title, action: retry)
            .font(Typography.caption.weight(.semibold))
            .foregroundStyle(canRetry ? Palette.accent.color : Palette.secondaryText.color)
            .disabled(!canRetry)
    }
}

/// The line above an answer and above the typing dots: the mark and the assistant's name.
struct AssistantHeader: View {
    var body: some View {
        HStack(spacing: Layout.small) {
            AssistantMark()
            SectionTitle(text: Brand.assistantName)
        }
    }
}

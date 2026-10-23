import SwiftUI

/// One balloon: the user's on the right, the assistant's on the left. A failed question shows why, with "Tekrar dene";
/// a stopped one can be sent again too.
struct MessageBubble: View {
    let message: ChatMessage
    let canRetry: Bool
    /// The list a link under this answer opens; built when the button is tapped (CHAT-07).
    let list: (AssistantLink) -> TransactionHistoryViewModel
    let retry: () -> Void

    var body: some View {
        HStack {
            if message.role == .user { Spacer(minLength: 48) }
            VStack(alignment: message.role == .user ? .trailing : .leading, spacing: 4) {
                Text(message.displayText)
                    .padding(.horizontal, 14)
                    .padding(.vertical, 10)
                    .background(message.role == .user ? Color.accentColor : Color(.secondarySystemBackground),
                                in: RoundedRectangle(cornerRadius: 18))
                    .foregroundStyle(message.role == .user ? Color.white : Color.primary)
                    .textSelection(.enabled)
                // Under its own answer for good, unlike the suggestions: the list is the proof of this answer's figure.
                ForEach(message.links, id: \.self) { link in
                    // A link with its own destination, not a value: a value link pushed after the chat's own destination link
                    // makes the unnamed stack pop straight back (found on the simulator, not by any unit test).
                    NavigationLink {
                        LinkedTransactionsView(link: link, model: list(link))
                    } label: {
                        Label(link.title, systemImage: "list.bullet.rectangle")
                    }
                    .buttonStyle(.bordered)
                    .controlSize(.small)
                }
                status
            }
            if message.role == .assistant { Spacer(minLength: 48) }
        }
    }

    @ViewBuilder
    private var status: some View {
        switch message.state {
        case .failed(let reason):
            VStack(alignment: .trailing, spacing: 4) {
                Label(reason, systemImage: "exclamationmark.circle")
                    .font(.footnote)
                    .foregroundStyle(.red)
                    .multilineTextAlignment(.trailing)
                Button("Tekrar dene", action: retry)
                    .font(.footnote.bold())
                    .disabled(!canRetry)
            }
        case .stopped:
            HStack(spacing: 8) {
                Text("Durduruldu")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
                Button("Tekrar gönder", action: retry)
                    .font(.footnote.bold())
                    .disabled(!canRetry)
            }
        case .sent, .waiting:
            EmptyView()
        }
    }
}

import SwiftUI

/// One balloon: the user's on the right, the assistant's on the left. A failed question shows why, with "Tekrar dene";
/// a stopped one can be sent again too.
struct MessageBubble: View {
    let message: ChatMessage
    let canRetry: Bool
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

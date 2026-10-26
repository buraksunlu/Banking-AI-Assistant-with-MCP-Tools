import Foundation

/// One balloon of the chat: who wrote it, its text and, for the user's own messages, what became of them.
struct ChatMessage: Identifiable, Equatable {
    enum Role: Equatable {
        case user
        case assistant
    }

    enum State: Equatable {
        /// Delivered (or, for the assistant, received).
        case sent
        /// Waiting for the assistant's reply.
        case waiting
        /// The user stopped waiting; nothing went wrong.
        case stopped
        /// No reply: the sentence to show under the balloon, with "Tekrar dene".
        case failed(String)
    }

    let id: UUID
    let role: Role
    let text: String
    var state: State
    /// The buttons that came with an assistant answer (CHAT-07). They stay with their answer: they are its evidence.
    let links: [AssistantLink]

    init(role: Role, text: String, state: State = .sent, links: [AssistantLink] = [], id: UUID = UUID()) {
        self.id = id
        self.role = role
        self.text = text
        self.state = state
        self.links = links
    }

    /// The text as shown: inline Markdown (bold, italic, links) applied (decision AI-08 K8-A) and, in an answer, every
    /// figure bold (UI-02 K1-B).
    var displayText: AttributedString {
        // Inline only: answers are one to three sentences (AI-05), and line breaks must stay where the assistant put them.
        // Text that is not valid Markdown is shown as it is rather than lost.
        let shown = (try? AttributedString(markdown: text, options: .init(interpretedSyntax: .inlineOnlyPreservingWhitespace)))
            ?? AttributedString(text)
        return role == .assistant ? AnswerHighlight.emphasized(shown) : shown
    }
}

import Foundation
import Observation

/// The chat screen's whole state and its actions (AI-08). The screen only shows it; every rule lives here, so it is tested
/// without a screen.
@Observable
final class ChatViewModel {
    /// Shown while the chat is empty; a tap sends one (decision AI-08 K9-A). The same questions the out-of-scope reply suggests.
    static let examples = ["Bu ay ne kadar harcadım?", "Geçen ay en çok hangi kategoriye harcadım?", "Son 3 ayda Starbucks'a ne kadar harcadım?"]

    /// The API's limit for one message (AssistantMessageRequest), counted the same way.
    static let maxLength = 500

    var draft = ""
    private(set) var messages: [ChatMessage] = []
    private(set) var isWaiting = false
    /// nil until the status is known; false shows the banner and closes the input.
    private(set) var isAvailable: Bool?
    @ObservationIgnored private let api: APIClient
    @ObservationIgnored private var pending: Task<Void, Never>?

    init(api: APIClient) {
        self.api = api
    }

    /// One message at a time: the single local model answers in turn, and the API allows ten a minute (SEC-06).
    var canSend: Bool { isAvailable != false && !isWaiting }

    var canSendDraft: Bool {
        let text = Self.trimmed(draft)
        // Counted like the API counts (Unicode characters), not like Swift's grapheme clusters.
        return canSend && !text.isEmpty && text.unicodeScalars.count <= Self.maxLength
    }

    var unavailableMessage: String? {
        isAvailable == false ? "Asistan şu an kullanılamıyor." : nil
    }

    /// Asks whether the assistant can answer (decision AI-08 K7-A). A failed check leaves the state as it was.
    func checkStatus() async {
        if let status = try? await api.send(.assistantStatus) {
            isAvailable = status.available
        }
    }

    func sendDraft() async {
        guard canSendDraft else { return }
        let text = Self.trimmed(draft)
        draft = ""
        await send(text)
    }

    func send(_ text: String) async {
        let text = Self.trimmed(text)
        guard canSend, !text.isEmpty else { return }
        let message = ChatMessage(role: .user, text: text, state: .waiting)
        messages.append(message)
        await ask(message.id)
    }

    /// Sends a failed or stopped message again, in its own balloon (no second copy of the question).
    func retry(_ id: UUID) async {
        guard canSend, let index = messages.firstIndex(where: { $0.id == id }), messages[index].role == .user else { return }
        messages[index].state = .waiting
        await ask(id)
    }

    /// Stops waiting: the request is cancelled and the message is marked stopped, not failed (decision AI-08 K5-C).
    func stop() {
        pending?.cancel()
    }

    private func ask(_ id: UUID) async {
        guard let text = messages.first(where: { $0.id == id })?.text else { return }
        isWaiting = true
        // A task of its own, so stop() can cancel exactly this request.
        let task = Task { [api] in
            do {
                let reply = try await api.send(.assistantMessage(text))
                self.received(reply, for: id)
            } catch let error as APIError {
                self.failed(error, for: id)
            } catch {
                self.failed(.invalidResponse(status: 0), for: id)
            }
        }
        pending = task
        await task.value
        pending = nil
        isWaiting = false
    }

    private func received(_ reply: AssistantReply, for id: UUID) {
        setState(.sent, for: id)
        messages.append(ChatMessage(role: .assistant, text: reply.answer))
    }

    private func failed(_ error: APIError, for id: UUID) {
        switch error {
        case .cancelled:
            setState(.stopped, for: id)
        case .problem(status: 503, _, _, _):
            // The assistant is down: the banner says so and the input closes until a status check says it is back.
            isAvailable = false
            setState(.failed(error.message), for: id)
        default:
            // 401 has already signed out (APIClient); the login screen replaces this one.
            setState(.failed(error.message), for: id)
        }
    }

    private func setState(_ state: ChatMessage.State, for id: UUID) {
        if let index = messages.firstIndex(where: { $0.id == id }) {
            messages[index].state = state
        }
    }

    private static func trimmed(_ text: String) -> String {
        text.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}

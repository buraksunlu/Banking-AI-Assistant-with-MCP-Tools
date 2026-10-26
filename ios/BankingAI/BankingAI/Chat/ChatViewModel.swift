import Foundation
import Observation

/// The chat screen's whole state and its actions (AI-08). The screen only shows it; every rule lives here, so it is tested
/// without a screen.
@Observable
final class ChatViewModel {
    /// Shown in an empty chat when the starter questions cannot be had (decision AI-08 K9-A, CHAT-06 K4-B). The same three the
    /// out-of-scope reply offers: every customer gets an answer to them (CHAT-06 K5-A).
    static let examples = ["Bu ay ne kadar harcadım?", "Geçen ay en çok hangi kategoriye harcadım?", "Geçen ay toplam ne kadar harcadım?"]

    /// The API's limit for one message (AssistantMessageRequest), counted the same way.
    static let maxLength = 500

    var draft = ""
    private(set) var messages: [ChatMessage] = []
    private(set) var isWaiting = false
    /// nil until the status is known; false shows the banner and closes the input.
    private(set) var isAvailable: Bool?
    /// The questions offered under the last answer (CHAT-06 K1-B); emptied as soon as anything is sent, because the server
    /// recognises its offer in the next message only.
    private(set) var suggestions: [String] = []
    /// The questions of an empty chat; empty while they are still being asked for.
    private(set) var starters: [String] = []
    @ObservationIgnored private let api: APIClient
    @ObservationIgnored private var pending: Task<Void, Never>?

    init(api: APIClient) {
        self.api = api
    }

    /// One message at a time: the single local model answers in turn, and the API allows ten a minute (SEC-06).
    var canSend: Bool { isAvailable != false && !isWaiting }

    /// The draft's length as the API counts it (Unicode characters, not Swift's grapheme clusters); the screen's counter
    /// shows the same number the limit checks (UI-02 K5-C).
    var draftLength: Int { Self.trimmed(draft).unicodeScalars.count }

    var canSendDraft: Bool {
        canSend && draftLength > 0 && draftLength <= Self.maxLength
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

    /// What the screen asks for when it opens: the status and, for an empty chat only, the starters, side by side (CHAT-06 K3-B).
    func open() async {
        async let status: Void = checkStatus()
        if messages.isEmpty {
            await loadStarters()
        }
        await status
    }

    /// The starter questions; the fixed examples when there are none (K4-B). Never touches the banner: only the status does.
    private func loadStarters() async {
        let loaded = (try? await api.send(.assistantSuggestions))?.suggestions ?? []
        starters = loaded.isEmpty ? Self.examples : loaded
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
        suggestions = []
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
        messages.append(ChatMessage(role: .assistant, text: reply.answer, links: reply.links))
        suggestions = reply.suggestions
    }

    /// The list a link under an answer opens: the same paged search, narrowed the way the backend wrote it (CHAT-07).
    func transactions(for link: AssistantLink) -> TransactionHistoryViewModel {
        TransactionHistoryViewModel(api: api, filter: link.filter, criteria: link.criteria)
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

#if DEBUG
import Foundation

/// The presentation's remote (FIN sunum, K1-A): a slide sends `compass://ask?q=…` or `compass://tab/<name>` to the simulator
/// through `xcrun simctl openurl`. Debug builds only (K4-A): a link that makes a banking app send messages is not for a
/// release. It can only do what a tap could: ask a question on the normal chat path (token, rate limit, AI, MCP) or show a tab.
enum DemoLink: Equatable {
    case ask(String)
    case show(AppTab)

    static let scheme = "compass"

    /// The tab names a slide may use; English so the link needs no Turkish letters.
    private static let tabs: [String: AppTab] = ["home": .home, "analysis": .analysis, "assistant": .assistant, "profile": .profile]

    /// Reads a link; nil for anything it does not know, so an unknown command is never guessed into a known one.
    init?(url: URL) {
        guard url.scheme == Self.scheme, let parts = URLComponents(url: url, resolvingAgainstBaseURL: false) else { return nil }
        switch parts.host {
        case "ask":
            let question = (parts.queryItems?.first { $0.name == "q" }?.value ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            // The API counts Unicode characters; a longer question would only come back as a 400 on stage.
            guard !question.isEmpty, question.unicodeScalars.count <= ChatViewModel.maxLength else { return nil }
            self = .ask(question)
        case "tab":
            guard let tab = Self.tabs[String(parts.path.dropFirst())] else { return nil }
            self = .show(tab)
        default:
            return nil
        }
    }

    /// Shows the tab and, for a question, sends it as if typed; `send` already drops it while an answer is awaited (K3-A).
    func perform(in chat: ChatViewModel, show: (AppTab) -> Void) async {
        switch self {
        case .ask(let question):
            show(.assistant)
            await chat.send(question)
        case .show(let tab):
            show(tab)
        }
    }
}
#endif

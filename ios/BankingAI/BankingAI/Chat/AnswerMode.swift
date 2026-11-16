import Foundation
import Observation

/// How much of an answer card is shown (UX-01 UX-E): the simple part, or the detailed part too.
enum AnswerMode: String, CaseIterable, Hashable {
    case simple, detailed

    var title: String {
        switch self {
        case .simple: "Basit"
        case .detailed: "Detaylı"
        }
    }
}

/// The profile's default answer mode, kept on the device only: a choice of how to show, never sent to the backend (UX-01 §6.3).
/// It is a device preference, so signing out keeps it, unlike the chat.
@Observable
final class AnswerPreferences {
    private static let key = "answerMode"
    @ObservationIgnored private let defaults: UserDefaults

    var mode: AnswerMode {
        didSet { defaults.set(mode.rawValue, forKey: Self.key) }
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        mode = defaults.string(forKey: Self.key).flatMap(AnswerMode.init(rawValue:)) ?? .simple
    }
}

/// The home screen's question sheet (UX-01 F1, K5-A): it shows the one chat of the app, so what is asked here is in the assistant
/// tab too (K2-B).
@Observable
final class AskSheet {
    var isPresented = false
    /// Opened from the "Kılavuz'a sor…" field: the sheet's input takes the keyboard at once.
    private(set) var startsWriting = false

    /// Opens the sheet; a panel row's question is sent at once, the field's opening sends nothing.
    func ask(_ question: String?, in chat: ChatViewModel) async {
        startsWriting = question == nil
        isPresented = true
        if let question {
            await chat.send(question)
        }
    }
}

import Foundation
import Observation

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

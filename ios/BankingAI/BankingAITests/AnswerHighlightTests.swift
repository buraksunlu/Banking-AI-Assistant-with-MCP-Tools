import Foundation
import Testing
@testable import BankingAI

// UI-02 K1-B: the figures of an answer are found on the phone and written bold. Only the two shapes the backend writes
// (ai/service/formatting.py `money` and `percent`) count; a year, a count or a day stays plain.
struct AnswerHighlightTests {
    private func figures(_ text: String) -> [String] {
        AnswerHighlight.figures(in: text).map { String(text[$0]) }
    }

    /// The bold parts of the shown text, in order.
    private func bold(_ text: AttributedString) -> [String] {
        text.runs
            .filter { $0.inlinePresentationIntent?.contains(.stronglyEmphasized) == true }
            .map { String(text[$0.range].characters) }
    }

    @Test(arguments: [
        ("Bu ay 735,00 TL harcadınız.", ["735,00 TL"]),
        ("Bakiyeniz 577.490,18 TL.", ["577.490,18 TL"]),
        ("Market payı %57,14 oldu.", ["%57,14"]),
        ("Geçen aya göre -%27,14 azaldı.", ["-%27,14"]),
        ("Ekim 2026'da 12 işlemde 2.235,00 TL harcadınız.", ["2.235,00 TL"]),
        ("İade -150,00 TL", ["-150,00 TL"]),
        ("120,50 USD ve 80,00 EUR", ["120,50 USD", "80,00 EUR"]),
        ("Bu dönemde harcama yok.", []),
    ])
    func onlyAmountsAndPercentagesAreFigures(pair: (String, [String])) {
        #expect(figures(pair.0) == pair.1)
    }

    @Test func aFigureIsNotCutFromALongerNumberOrWord() {
        // "1500,00 TL" is not how the backend writes it (no thousands dot); half of it ("500,00 TL") must not light up either.
        #expect(figures("1500,00 TL") == [])
        #expect(figures("12,00 TLsi") == [])
    }

    @Test func theFiguresOfAPlainAnswerAreBold() {
        let shown = AnswerHighlight.emphasized(AttributedString("Market payı %57,14, toplam 735,00 TL."))
        #expect(bold(shown) == ["%57,14", "735,00 TL"])
        #expect(String(shown.characters) == "Market payı %57,14, toplam 735,00 TL.")
    }

    @Test func aFigureTheAssistantAlreadyBoldedStaysOneBoldFigure() {
        let message = ChatMessage(role: .assistant, text: "Dün **145,00 TL** harcadınız.")
        #expect(bold(message.displayText) == ["145,00 TL"])
        #expect(String(message.displayText.characters) == "Dün 145,00 TL harcadınız.")
    }

    @Test func theUsersOwnTextIsShownAsTyped() {
        // Only answers carry the backend's figures; the user's question is theirs.
        let message = ChatMessage(role: .user, text: "735,00 TL nereye gitti?")
        #expect(bold(message.displayText) == [])
    }
}

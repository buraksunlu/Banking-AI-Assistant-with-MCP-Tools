import Foundation

/// Finds the figures of an answer so the screen can write them bold (UI-02 K1-B). Display only: nothing is read as a number.
enum AnswerHighlight {
    // The two shapes the backend writes (ai/service/formatting.py): "-150,00 TL", "577.490,18 TL", "120,50 USD" and
    // "%57,14", "-%27,14". The word boundaries keep half a number ("500 TL" of "1500 TL") and "TLX" out.
    private static let figure = #/-?%\d+(?:,\d+)?|-?\b\d{1,3}(?:\.\d{3})*,\d{2} (?:TL|[A-Z]{3})\b/#

    static func figures(in text: String) -> [Range<String.Index>] {
        text.matches(of: figure).map(\.range)
    }

    /// The same text with every figure bold; bold the assistant already wrote stays as it was.
    static func emphasized(_ text: AttributedString) -> AttributedString {
        var result = text
        let plain = String(text.characters)
        for range in figures(in: plain) {
            // The character view counts like String, so an offset in one is the same place in the other.
            let start = result.characters.index(result.startIndex, offsetBy: plain.distance(from: plain.startIndex, to: range.lowerBound))
            let end = result.characters.index(start, offsetBy: plain.distance(from: range.lowerBound, to: range.upperBound))
            for run in result[start..<end].runs {
                var intent = run.inlinePresentationIntent ?? []
                intent.insert(.stronglyEmphasized)
                result[run.range].inlinePresentationIntent = intent
            }
        }
        return result
    }
}

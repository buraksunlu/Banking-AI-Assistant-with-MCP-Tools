import Foundation

/// The small pieces of text of the home header, decided without a view (UI-03).
enum HomeDisplay {
    /// "Murat", "Yılmaz" → "MY". Turkish capitals, so "ilker işık" is "İİ" and not "II".
    static func initials(first: String, last: String) -> String {
        [first, last]
            .compactMap { $0.trimmingCharacters(in: .whitespaces).first.map(String.init) }
            .joined()
            .uppercased(with: Locale(identifier: "tr_TR"))
    }

    /// The backend's day "2026-10-26" as "26 Ekim 2026"; nil when it cannot be read, so the header shows no date instead of
    /// a wrong one.
    static func dayLine(_ localDate: String?) -> String? {
        guard let localDate else { return nil }
        let parts = localDate.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 3, (1...12).contains(parts[1]), (1...31).contains(parts[2]) else { return nil }
        return TransactionDisplay.date(localDate)
    }
}

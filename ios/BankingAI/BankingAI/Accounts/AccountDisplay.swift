import Foundation

/// How an account is written on its card (UI-03 K2-B): the full number stays on the detail screen.
enum AccountDisplay {
    /// "DUMMY-ADA-TRY-001" → "•••• 001": the part after the last dash tells a customer's accounts apart. Without a dash the
    /// last four characters are kept; a number of four or fewer is shown as it is.
    static func shortNumber(_ number: String) -> String {
        let tail = lastPart(number)
        return tail == number && number.count <= 4 ? number : "•••• " + tail
    }

    /// "İşletme hesabı" or "Kişisel hesap", as the detail screen writes the type (UI-05).
    static func typeText(_ account: Account) -> String {
        account.accountType == .business ? "İşletme hesabı" : "Kişisel hesap"
    }

    /// What VoiceOver reads for a card as one sentence: "Ada günlük TRY, 577.490,18 TL, hesap sonu 001".
    static func spokenSummary(_ account: Account) -> String {
        var text = "\(account.accountName), \(Money.text(account.balance, currency: account.currencyCode)), hesap sonu \(lastPart(account.accountNumber))"
        if account.accountType == .business { text += ", işletme hesabı" }
        return text
    }

    private static func lastPart(_ number: String) -> String {
        if let dash = number.lastIndex(of: "-") {
            let tail = String(number[number.index(after: dash)...])
            if !tail.isEmpty { return tail }
        }
        return number.count > 4 ? String(number.suffix(4)) : number
    }
}

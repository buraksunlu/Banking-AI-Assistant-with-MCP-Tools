import Foundation
import Testing
@testable import BankingAI

// The "Bu ay harcama" card moved to `period-comparison` in UX-01 PR2 (K7-A); its tests are in HomePanelTests.

// UI-03: the small pieces of text the new home screen writes, decided without a view.
struct HomeDisplayTests {
    // The shortened number keeps the part after the last dash, the one that tells two accounts of a customer apart.
    @Test(arguments: [
        ("DUMMY-ADA-TRY-001", "•••• 001"),
        ("DUMMY-ADA-EMPTY-001", "•••• 001"),
        ("TR12345678", "•••• 5678"),
        ("12", "12"),
        ("", ""),
    ])
    func theAccountNumberIsShortened(pair: (String, String)) {
        #expect(AccountDisplay.shortNumber(pair.0) == pair.1)
    }

    // Turkish capitals: "i" becomes "İ", and a plain uppercased() would write "II".
    @Test(arguments: [
        ("Murat", "Yılmaz", "MY"),
        ("Ege", "", "E"),
        ("ilker", "işık", "İİ"),
        ("", "", ""),
    ])
    func theInitialsAreTurkishCapitals(person: (String, String, String)) {
        #expect(HomeDisplay.initials(first: person.0, last: person.1) == person.2)
    }

    @Test func theDayLineIsTheBackendsDayWrittenByHand() {
        #expect(HomeDisplay.dayLine("2026-10-10") == "10 Ekim 2026")
        #expect(HomeDisplay.dayLine("2026-10-26") == "26 Ekim 2026")
        #expect(HomeDisplay.dayLine("2026-01-05") == "5 Ocak 2026")
    }

    @Test func aDayThatCannotBeReadIsNotShown() {
        #expect(HomeDisplay.dayLine("26/10/2026") == nil)
        #expect(HomeDisplay.dayLine("2026-13-05") == nil)
        #expect(HomeDisplay.dayLine(nil) == nil)
    }

    @Test func theCardNamesTheAccountAndItsNumberForVoiceOver() {
        let account = Account(id: UUID(), accountName: "Ada günlük TRY", accountNumber: "DUMMY-ADA-TRY-001",
                              currencyCode: "TRY", accountType: .personal, balance: Decimal(string: "577490.18")!)
        #expect(AccountDisplay.spokenSummary(account) == "Ada günlük TRY, 577.490,18 TL, hesap sonu 001")
    }
}

import Foundation
import Testing
@testable import BankingAI

@MainActor private func transaction(kind: String, direction: String) throws -> TransactionItem {
    let json = """
    { "id": "30000000-0000-0000-0000-000000000001", "accountId": "20000000-0000-0000-0000-000000000001", \
    "accountName": "Ada günlük TRY", "occurredAtUtc": "2026-10-08T10:00:00+00:00", "localDate": "2026-10-08", \
    "kind": "\(kind)", "direction": "\(direction)", "amount": 100.00, "currencyCode": "TRY", "merchant": null, \
    "categoryCode": null, "categoryDisplayName": null, "rawDescription": "POS TEST", "counterparty": null, \
    "counterpartyIsOwnAccount": false }
    """
    return try JSONDecoder().decode(TransactionItem.self, from: Data(json.utf8))
}

@MainActor private func account(type: String) throws -> Account {
    let json = """
    { "id": "20000000-0000-0000-0000-000000000001", "accountName": "Ada günlük TRY", "accountNumber": "DUMMY-ADA-TRY-001", \
    "currencyCode": "TRY", "accountType": "\(type)", "balance": 10.00, "createdAt": "2026-10-09T14:08:38+00:00" }
    """
    return try JSONDecoder().decode(Account.self, from: Data(json.utf8))
}

// UI-05: the two wordings the detail screens used to decide inside the view; pure now, so they are checked without a screen.
@MainActor
struct DetailTextTests {
    @Test(arguments: [
        ("Purchase", "Debit", "Harcama"),
        ("Sale", "Credit", "Satış"),
        ("Transfer", "Credit", "Gelen transfer"),
        ("Transfer", "Debit", "Giden transfer"),
        ("Salary", "Credit", "Maaş"),
        ("OpeningFunding", "Credit", "Açılış bakiyesi"),
    ])
    func theKindIsWrittenInTurkish(kind: String, direction: String, expected: String) throws {
        #expect(TransactionDisplay.kindText(try transaction(kind: kind, direction: direction)) == expected)
    }

    @Test func anAccountTypeIsWrittenInTurkish() throws {
        #expect(AccountDisplay.typeText(try account(type: "Business")) == "İşletme hesabı")
        #expect(AccountDisplay.typeText(try account(type: "Personal")) == "Kişisel hesap")
    }
}

import Foundation
import Testing
@testable import BankingAI

// UI-06 PR1: the loading placeholders are drawn from made-up rows, so the rules worth guarding are about those rows: as many as
// the real list shows (nothing jumps when the data arrives), each with its own identity (a repeated id breaks `ForEach`),
// nothing that looks like a real customer's data, and a grey that is seen on the black screen without reading as content.
struct LoadingStateTests {
    @Test func thePlaceholdersHaveAsManyRowsAsTheRealLists() {
        #expect(Skeleton.accounts.count == 2)
        #expect(Skeleton.recent.count == RecentTransactionsViewModel.count)
        #expect(Skeleton.categories.count == 4)
        // Six whole months and the running one, as the real chart.
        #expect(Skeleton.barHeights.count == MonthlyTrendViewModel.months + 1)
        #expect(Skeleton.history(firstPage: true).count == 6)
        #expect(Skeleton.history(firstPage: false).count == 2)
    }

    @Test func everyPlaceholderRowHasItsOwnIdentity() {
        #expect(Set(Skeleton.accounts.map(\.id)).count == Skeleton.accounts.count)
        #expect(Set(Skeleton.recent.map(\.id)).count == Skeleton.recent.count)
        #expect(Set(Skeleton.categories.map(\.id)).count == Skeleton.categories.count)
        let page = Skeleton.history(firstPage: true)
        #expect(Set(page.map(\.id)).count == page.count)
    }

    // Grey blocks only, but the text under them is still there: it must never be a persona or a real-looking figure.
    @Test func thePlaceholdersCarryOnlyNeutralWords() {
        #expect(Skeleton.accounts.allSatisfy { $0.accountName == "Hesap adı" && $0.balance == 10_000 })
        #expect(Skeleton.recent.allSatisfy { $0.merchant == "İşyeri adı" && $0.amount == 100 && $0.counterparty == nil })
        // Codes no category has (a real code would take that category's colour and symbol), and distinct, because the code is
        // the row's id.
        #expect(Skeleton.categories.allSatisfy { $0.displayName == "Kategori" && $0.categoryCode?.hasPrefix("placeholder-") == true })
    }

    @Test func thePlaceholderRowsAreWrittenLikeRealOnes() throws {
        let row = TransactionDisplay(try #require(Skeleton.recent.first))
        #expect(row.title == "İşyeri adı")
        #expect(row.amount == "−100,00 TL")
    }

    // Full fill: plainly seen on the screen and on a card (ΔE ≥ 12), yet far below the 3:1 of a control, so it reads as "not
    // here yet" and not as a button or a bar of data.
    @Test(arguments: [("background", Palette.background), ("surface", Palette.surface)])
    func theBlockGreyIsSeenButDoesNotLookLikeContent(_ name: String, ground: Swatch) {
        let block = Palette.primaryText.over(ground, opacity: Skeleton.fillOpacity)
        #expect(block.distance(to: ground) >= 12, "\(name)")
        #expect(block.contrast(with: ground) < 3, "\(name)")
    }

    // The pulse (K2-B) goes down to half and back; with "Hareketi azalt" on it stays still at full.
    @Test func thePulseStopsWhenMotionIsReduced() {
        #expect(Skeleton.dimmedOpacity(reduceMotion: false) == 0.5)
        #expect(Skeleton.dimmedOpacity(reduceMotion: true) == 1)
    }
}

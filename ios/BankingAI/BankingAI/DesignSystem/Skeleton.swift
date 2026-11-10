import SwiftUI

/// What a list shows while it loads (UI-06 K1-C): its real rows filled with neutral made-up data and drawn as grey blocks
/// (`.skeleton()`), so the layout is the real one at every text size and nothing jumps when the data arrives. Charts cannot be
/// greyed that way, so they get plain shapes in ``fill``. Nothing here is ever shown as data.
enum Skeleton {
    /// The block grey: the text colour at this opacity, seen on the screen and on a card but far from looking like content
    /// (`LoadingStateTests`).
    static let fillOpacity = 0.16

    static var fill: Color { Palette.primaryText.color.opacity(fillOpacity) }

    static let accounts: [Account] = (1...2).map { index in
        Account(id: id(index), accountName: "Hesap adı", accountNumber: "HESAP-00\(index)", currencyCode: "TRY",
                accountType: .personal, balance: 10_000)
    }

    static let recent: [TransactionItem] = rows(RecentTransactionsViewModel.count)

    static let categories: [CategorySpending] = (1...4).map { index in
        CategorySpending(categoryCode: "placeholder-\(index)", displayName: "Kategori", amount: 100, transactionCount: 1,
                         percentage: 25)
    }

    /// The income card while it loads: half the bar in three parts and no advice, a shape and not a figure.
    static let incomeShare = CurrencyAdvice(
        currencyCode: "TRY", totalSpent: 100, incomeBase: 200, spentShare: 50,
        groups: zip(SpendingGroup.allCases, [30, 15, 5] as [Decimal]).map { GroupSpending(group: $0, amount: 100, incomeShare: $1) },
        advice: [], reason: .noRuleMatched)

    /// Fixed bar heights of the placeholder chart, as parts of its height: a shape, not data.
    static let barHeights: [Double] = [0.45, 0.7, 0.55, 0.85, 0.6, 0.75, 0.35]

    /// The rows standing in for a statement page: a whole screen before the first page, two under the rows already shown.
    static func history(firstPage: Bool) -> [TransactionItem] {
        rows(firstPage ? 6 : 2)
    }

    /// The low point of the pulse (K2-B); with "Hareketi azalt" on the blocks stay still at full.
    static func dimmedOpacity(reduceMotion: Bool) -> Double {
        reduceMotion ? 1 : 0.5
    }

    private static func rows(_ count: Int) -> [TransactionItem] {
        (1...count).map { index in
            TransactionItem(id: id(index), accountName: "Hesap adı", occurredAtUtc: "2026-01-01T00:00:00Z", localDate: "2026-01-01",
                            kind: .purchase, direction: .debit, amount: 100, currencyCode: "TRY", merchant: "İşyeri adı",
                            categoryCode: nil, categoryDisplayName: "Kategori", rawDescription: "İşlem", counterparty: nil,
                            counterpartyIsOwnAccount: false)
        }
    }

    /// The same ids on every build of the view, so the placeholder rows are not torn down and rebuilt while they pulse.
    private static func id(_ index: Int) -> UUID {
        UUID(uuidString: String(format: "00000000-0000-0000-0000-%012d", index))!
    }
}

extension View {
    /// Draws this view as the loading placeholder: grey blocks that pulse slowly, cannot be tapped and are read by VoiceOver
    /// as one "Yükleniyor" (the made-up rows under the blocks are never read). `spoken: false` hides it from VoiceOver
    /// altogether, for a line whose neighbour already says it is loading.
    func skeleton(spoken: Bool = true) -> some View {
        modifier(SkeletonModifier(spoken: spoken))
    }
}

private struct SkeletonModifier: ViewModifier {
    let spoken: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var dimmed = false

    func body(content: Content) -> some View {
        content
            .redacted(reason: .placeholder)
            .allowsHitTesting(false)
            .opacity(dimmed ? Skeleton.dimmedOpacity(reduceMotion: reduceMotion) : 1)
            .accessibilityElement(children: .ignore)
            .accessibilityLabel("Yükleniyor")
            .accessibilityHidden(!spoken)
            .onAppear {
                guard !reduceMotion else { return }
                withAnimation(.easeInOut(duration: 1).repeatForever(autoreverses: true)) { dimmed = true }
            }
    }
}

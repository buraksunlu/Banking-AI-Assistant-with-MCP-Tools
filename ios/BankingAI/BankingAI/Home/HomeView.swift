import SwiftUI

/// The home tab (UI-03, UI-07): the header, this month's spending, last month against income (INC-04), the customer's
/// accounts (IOS-04, decision K1-D) and the newest transactions with the way to all of them (IOS-07). The analysis, the
/// assistant and the profile are tabs of their own; the spending card and the profile circle switch to them (K4-A, K8-B).
struct HomeView: View {
    let model: HomeViewModel
    let accounts: AccountsViewModel
    let recent: RecentTransactionsViewModel
    let history: TransactionHistoryViewModel
    let summary: SpendingSummaryViewModel
    let income: IncomeShareViewModel
    /// Switches the tab bar to another tab.
    let show: (AppTab) -> Void

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: Layout.large) {
                    HomeHeader(model: model, dayLine: HomeDisplay.dayLine(summary.endDate)) { show(.profile) }
                    // A failed card is not a button: its own "Tekrar dene" must not also switch to the analysis tab.
                    if case .failed = summary.state {
                        SpendingSummaryCard(model: summary)
                    } else {
                        Button { show(.analysis) } label: {
                            SpendingSummaryCard(model: summary)
                        }
                        .buttonStyle(.plain)
                    }
                    IncomeShareCard(model: income)
                    AccountsSection(model: accounts)
                    RecentTransactionsSection(model: recent)
                }
                .padding(.horizontal, Layout.regular)
                .padding(.top, Layout.small)
            }
            .background(Palette.background.color)
            .refreshable {
                async let accountsLoaded: Void = accounts.load()
                async let recentLoaded: Void = recent.load()
                async let summaryLoaded: Void = summary.load()
                async let incomeLoaded: Void = income.load()
                _ = await (accountsLoaded, recentLoaded, summaryLoaded, incomeLoaded)
            }
            .navigationDestination(for: TransactionHistoryRoute.self) { _ in
                TransactionHistoryView(model: history)
            }
            .navigationDestination(for: TransactionItem.self) { item in
                TransactionDetailView(item: item)
            }
            // The header replaces the navigation bar; screens pushed from here bring their own. The title is still needed:
            // it is their back button's label, which would otherwise be the system's English "Back".
            .navigationTitle(AppTab.home.title)
            .toolbar(.hidden, for: .navigationBar)
            .task { await model.load() }
        }
    }
}

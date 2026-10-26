import SwiftUI

/// The signed-in home screen (UI-03): the header, this month's spending, the customer's accounts (IOS-04, decision K1-D), the
/// newest transactions with the way to all of them (IOS-07) and the way to the assistant. Sign-out sits in the header's
/// profile menu.
struct HomeView: View {
    @State var model: HomeViewModel
    @State var accounts: AccountsViewModel
    @State var recent: RecentTransactionsViewModel
    @State var history: TransactionHistoryViewModel
    @State var summary: SpendingSummaryViewModel
    @State var category: CategoryBreakdownViewModel
    @State var monthly: MonthlyTrendViewModel
    /// Lives as long as this screen, so going back and forth keeps the conversation; signing out ends both (K4-D).
    @State var chat: ChatViewModel

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: Layout.large) {
                    HomeHeader(model: model, dayLine: HomeDisplay.dayLine(summary.endDate))
                    // A failed card is not a link: its own "Tekrar dene" must not also open the analysis screen.
                    if case .failed = summary.state {
                        SpendingSummaryCard(model: summary)
                    } else {
                        NavigationLink(value: AnalyticsRoute()) {
                            SpendingSummaryCard(model: summary)
                        }
                        .buttonStyle(.plain)
                    }
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
                _ = await (accountsLoaded, recentLoaded, summaryLoaded)
            }
            .navigationDestination(for: TransactionHistoryRoute.self) { _ in
                TransactionHistoryView(model: history)
            }
            .navigationDestination(for: AnalyticsRoute.self) { _ in
                AnalyticsView(category: category, monthly: monthly)
            }
            .navigationDestination(for: TransactionItem.self) { item in
                TransactionDetailView(item: item)
            }
            // Floats over the list and stays in reach below a long account list; UI-07 turns it into a tab.
            .safeAreaInset(edge: .bottom) {
                NavigationLink {
                    ChatView(model: chat)
                } label: {
                    HStack(spacing: Layout.small) {
                        AssistantMark(size: 24)
                        Text(Brand.askAssistant)
                            .font(.body.weight(.medium))
                            .foregroundStyle(Palette.primaryText.color)
                    }
                    .padding(.horizontal, Layout.large)
                    .padding(.vertical, Layout.medium)
                    .background(Palette.raised.color, in: Capsule())
                    .overlay(Capsule().stroke(Palette.hairline.color, lineWidth: 1))
                }
                .buttonStyle(.plain)
                .padding(.bottom, Layout.small)
            }
            // The header replaces the navigation bar; screens pushed from here bring their own. The title is still needed:
            // it is their back button's label, which would otherwise be the system's English "Back".
            .navigationTitle("Ana sayfa")
            .toolbar(.hidden, for: .navigationBar)
            .task { await model.load() }
        }
        .environment(\.customerTimeZone, model.timeZone)
    }
}

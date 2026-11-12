import SwiftUI

/// The signed-in app (UI-07): the system tab bar with four tabs, each with its own navigation stack (K1-A). It owns every
/// screen's model, so switching tabs keeps the chat and the charts as they were; signing out removes this view and with it
/// all of them (AI-08 K4-D).
struct MainTabView: View {
    @State private var selection: AppTab = .home
    @State private var home: HomeViewModel
    @State private var accounts: AccountsViewModel
    @State private var recent: RecentTransactionsViewModel
    @State private var history: TransactionHistoryViewModel
    @State private var summary: SpendingSummaryViewModel
    @State private var income: IncomeShareViewModel
    @State private var category: CategoryBreakdownViewModel
    @State private var monthly: MonthlyTrendViewModel
    @State private var chat: ChatViewModel

    init(api: APIClient, session: SessionStore) {
        _home = State(initialValue: HomeViewModel(api: api, session: session))
        _accounts = State(initialValue: AccountsViewModel(api: api))
        _recent = State(initialValue: RecentTransactionsViewModel(api: api))
        _history = State(initialValue: TransactionHistoryViewModel(api: api))
        _summary = State(initialValue: SpendingSummaryViewModel(api: api))
        _income = State(initialValue: IncomeShareViewModel(api: api))
        _category = State(initialValue: CategoryBreakdownViewModel(api: api))
        _monthly = State(initialValue: MonthlyTrendViewModel(api: api))
        _chat = State(initialValue: ChatViewModel(api: api))
    }

    var body: some View {
        TabView(selection: $selection) {
            HomeView(model: home, accounts: accounts, recent: recent, history: history, summary: summary, income: income,
                     show: show)
                .tabItem { label(.home) }
                .tag(AppTab.home)
            NavigationStack {
                AnalyticsView(category: category, monthly: monthly)
            }
            .tabItem { label(.analysis) }
            .tag(AppTab.analysis)
            NavigationStack {
                ChatView(model: chat)
            }
            .tabItem { label(.assistant) }
            .tag(AppTab.assistant)
            NavigationStack {
                ProfileView(model: home)
            }
            .tabItem { label(.profile) }
            .tag(AppTab.profile)
        }
        // Above every tab, not only the home stack: the chat's "İşlemleri gör" opens transaction details in its own stack,
        // and they show times in the customer's zone.
        .environment(\.customerTimeZone, home.timeZone)
    }

    private func label(_ tab: AppTab) -> some View {
        Label(tab.title, systemImage: tab.symbol)
    }

    /// The home screen's ways to the other tabs: its spending card and the profile circle (K4-A, K8-B).
    private func show(_ tab: AppTab) {
        selection = tab
    }
}

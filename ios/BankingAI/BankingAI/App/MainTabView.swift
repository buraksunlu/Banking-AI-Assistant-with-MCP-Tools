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
    @State private var pace: MonthPaceViewModel
    /// The home panel's own categories, always this month; the analysis tab's ring has its own period choice.
    @State private var homeCategories: CategoryBreakdownViewModel
    @State private var income: IncomeShareViewModel
    @State private var category: CategoryBreakdownViewModel
    @State private var monthly: MonthlyTrendViewModel
    @State private var chat: ChatViewModel
    /// The home screen's question sheet; it shows the same chat as the assistant tab (UX-01 K2-B).
    @State private var sheet = AskSheet()

    init(api: APIClient, session: SessionStore) {
        _home = State(initialValue: HomeViewModel(api: api, session: session))
        _accounts = State(initialValue: AccountsViewModel(api: api))
        _recent = State(initialValue: RecentTransactionsViewModel(api: api))
        _history = State(initialValue: TransactionHistoryViewModel(api: api))
        _pace = State(initialValue: MonthPaceViewModel(api: api))
        _homeCategories = State(initialValue: CategoryBreakdownViewModel(api: api))
        _income = State(initialValue: IncomeShareViewModel(api: api))
        _category = State(initialValue: CategoryBreakdownViewModel(api: api))
        _monthly = State(initialValue: MonthlyTrendViewModel(api: api))
        _chat = State(initialValue: ChatViewModel(api: api))
    }

    var body: some View {
        TabView(selection: $selection) {
            HomeView(model: home, pace: pace, categories: homeCategories, income: income, show: show, ask: ask)
                .tabItem { label(.home) }
                .tag(AppTab.home)
            NavigationStack {
                AnalyticsView(category: category, monthly: monthly, income: income)
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
                    .navigationDestination(for: MyAccountsRoute.self) { _ in
                        MyAccountsView(accounts: accounts, recent: recent)
                    }
                    .navigationDestination(for: TransactionHistoryRoute.self) { _ in
                        TransactionHistoryView(model: history)
                    }
                    .navigationDestination(for: TransactionItem.self) { item in
                        TransactionDetailView(item: item)
                    }
            }
            .tabItem { label(.profile) }
            .tag(AppTab.profile)
        }
        // Above every tab, not only the home stack: the chat's "İşlemleri gör" opens transaction details in its own stack,
        // and they show times in the customer's zone.
        .environment(\.customerTimeZone, home.timeZone)
        .sheet(isPresented: $sheet.isPresented) {
            NavigationStack {
                ChatView(model: chat, startsWriting: sheet.startsWriting)
            }
            // Half open over the panel, pulled up to the whole screen (K5-A); pulled down, it closes and the chat stays.
            .presentationDetents([.medium, .large])
            .presentationDragIndicator(.visible)
            .environment(\.customerTimeZone, home.timeZone)
        }
    }

    /// A panel row's question or the empty field opens the sheet (K5-A).
    private func ask(_ question: String?) {
        Task { await sheet.ask(question, in: chat) }
    }

    private func label(_ tab: AppTab) -> some View {
        Label(tab.title, systemImage: tab.symbol)
    }

    /// The home screen's ways to the other tabs: its panel's links and the profile circle (K4-A, K8-B, UX-01 PR2).
    private func show(_ tab: AppTab) {
        selection = tab
    }
}

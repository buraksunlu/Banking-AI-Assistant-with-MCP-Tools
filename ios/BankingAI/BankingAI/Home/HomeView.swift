import SwiftUI

/// The signed-in home screen: the greeting from `GET /api/me`, the customer's accounts (IOS-04, decision K1-D), the newest
/// transactions with the way to all of them (IOS-07), the way to the assistant and sign-out.
struct HomeView: View {
    @State var model: HomeViewModel
    @State var accounts: AccountsViewModel
    @State var recent: RecentTransactionsViewModel
    @State var history: TransactionHistoryViewModel
    @State var category: CategoryBreakdownViewModel
    @State var monthly: MonthlyTrendViewModel
    /// Lives as long as this screen, so going back and forth keeps the conversation; signing out ends both (K4-D).
    @State var chat: ChatViewModel

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 24) {
                    if let greeting = model.greeting {
                        Text(greeting)
                            .font(.title2.bold())
                    } else if let message = model.errorMessage {
                        Label(message, systemImage: "exclamationmark.triangle")
                            .foregroundStyle(.red)
                    } else {
                        ProgressView()
                    }
                    NavigationLink(value: AnalyticsRoute()) {
                        Label("Harcama analizi", systemImage: "chart.pie")
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding()
                            .background(Color(.secondarySystemGroupedBackground), in: RoundedRectangle(cornerRadius: 16))
                    }
                    .buttonStyle(.plain)
                    AccountsSection(model: accounts)
                    RecentTransactionsSection(model: recent)
                }
                .padding()
            }
            .background(Color(.systemGroupedBackground))
            .refreshable {
                async let accountsLoaded: Void = accounts.load()
                async let recentLoaded: Void = recent.load()
                _ = await (accountsLoaded, recentLoaded)
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
            // Stays in reach below a long account list.
            .safeAreaInset(edge: .bottom) {
                NavigationLink {
                    ChatView(model: chat)
                } label: {
                    Label("Asistana sor", systemImage: "bubble.left.and.text.bubble.right")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .padding()
                .background(.bar)
            }
            .navigationTitle("BankingAI")
            .toolbar {
                Button("Çıkış", action: model.signOut)
            }
            .task { await model.load() }
        }
        .environment(\.customerTimeZone, model.timeZone)
    }
}

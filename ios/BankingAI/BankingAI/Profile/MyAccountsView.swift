import SwiftUI

/// "Hesaplarım", opened from the profile tab (UX-01 PR2, K6-A): the account cards and the newest transactions that used to
/// fill the home screen, unchanged. Their routes (an account, a transaction, the whole history) are served by the profile stack.
struct MyAccountsView: View {
    let accounts: AccountsViewModel
    let recent: RecentTransactionsViewModel

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Layout.large) {
                AccountsSection(model: accounts)
                RecentTransactionsSection(model: recent)
            }
            .padding(.horizontal, Layout.regular)
            .padding(.top, Layout.small)
            .padding(.bottom, Layout.large)
        }
        .blackScreen()
        .navigationTitle("Hesaplarım")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable {
            async let accountsLoaded: Void = accounts.load()
            async let recentLoaded: Void = recent.load()
            _ = await (accountsLoaded, recentLoaded)
        }
    }
}

/// Pushes ``MyAccountsView`` from the profile tab.
struct MyAccountsRoute: Hashable {}

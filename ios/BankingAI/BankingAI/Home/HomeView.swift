import SwiftUI

/// The signed-in home screen: the greeting from `GET /api/me`, the customer's accounts (IOS-04, decision K1-D), the way to
/// the assistant and sign-out. Recent transactions (IOS-07) will join the accounts here.
struct HomeView: View {
    @State var model: HomeViewModel
    @State var accounts: AccountsViewModel
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
                    AccountsSection(model: accounts)
                }
                .padding()
            }
            .background(Color(.systemGroupedBackground))
            .refreshable { await accounts.load() }
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
    }
}

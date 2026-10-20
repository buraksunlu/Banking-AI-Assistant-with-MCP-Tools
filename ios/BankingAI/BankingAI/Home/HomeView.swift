import SwiftUI

/// The signed-in home screen: it greets the customer from `GET /api/me`, opens the assistant and offers sign-out. Account
/// screens (IOS-04…) will join it here (decision AI-08 K11-B).
struct HomeView: View {
    @State var model: HomeViewModel
    /// Lives as long as this screen, so going back and forth keeps the conversation; signing out ends both (K4-D).
    @State var chat: ChatViewModel

    var body: some View {
        NavigationStack {
            VStack(spacing: 24) {
                if let greeting = model.greeting {
                    Text(greeting)
                        .font(.title2)
                } else if let message = model.errorMessage {
                    Label(message, systemImage: "exclamationmark.triangle")
                        .foregroundStyle(.red)
                } else {
                    ProgressView()
                }
                NavigationLink {
                    ChatView(model: chat)
                } label: {
                    Label("Asistana sor", systemImage: "bubble.left.and.text.bubble.right")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
            }
            .padding()
            .navigationTitle("BankingAI")
            .toolbar {
                Button("Çıkış", action: model.signOut)
            }
            .task { await model.load() }
        }
    }
}

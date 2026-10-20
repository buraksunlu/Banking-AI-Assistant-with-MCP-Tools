import SwiftUI

/// The signed-in screen until the chat arrives (AI-08): it greets the customer from `GET /api/me` and offers sign-out.
struct HomeView: View {
    @State var model: HomeViewModel

    var body: some View {
        NavigationStack {
            VStack(spacing: 16) {
                if let greeting = model.greeting {
                    Text(greeting)
                        .font(.title2)
                } else if let message = model.errorMessage {
                    Label(message, systemImage: "exclamationmark.triangle")
                        .foregroundStyle(.red)
                } else {
                    ProgressView()
                }
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

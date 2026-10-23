import SwiftUI

/// The chat screen (AI-08). It shows ``ChatViewModel`` and passes the user's taps to it; there is no logic here.
struct ChatView: View {
    @Bindable var model: ChatViewModel
    @Environment(\.scenePhase) private var scenePhase

    var body: some View {
        VStack(spacing: 0) {
            if let message = model.unavailableMessage {
                banner(message)
            }
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 12) {
                        if model.messages.isEmpty {
                            starters
                        }
                        ForEach(model.messages) { message in
                            MessageBubble(message: message, canRetry: model.canSend, list: model.transactions(for:)) {
                                Task { await model.retry(message.id) }
                            }
                            .id(message.id)
                            // Only under the last answer: the server knows only that offer (CHAT-06 K1-B, K2-A).
                            if message.role == .assistant && message.id == model.messages.last?.id && !model.suggestions.isEmpty {
                                questions(model.suggestions)
                                    .controlSize(.small)
                            }
                        }
                        if model.isWaiting {
                            typing
                                .id("typing")
                        }
                    }
                    .padding()
                }
                .onChange(of: model.messages.count) {
                    withAnimation {
                        if model.isWaiting {
                            proxy.scrollTo("typing")
                        } else if let last = model.messages.last {
                            proxy.scrollTo(last.id)
                        }
                    }
                }
            }
            Divider()
            input
        }
        .navigationTitle("Asistan")
        .navigationBarTitleDisplayMode(.inline)
        .task { await model.open() }
        // Leaving the screen or the app stops a pending question (decision AI-08 K5-C).
        .onDisappear { model.stop() }
        .onChange(of: scenePhase) {
            if scenePhase == .background { model.stop() }
        }
    }

    private func banner(_ message: String) -> some View {
        HStack {
            Label(message, systemImage: "wifi.exclamationmark")
                .font(.subheadline)
            Spacer()
            Button("Yeniden kontrol et") {
                Task { await model.checkStatus() }
            }
            .font(.subheadline.bold())
        }
        .padding()
        .background(Color.orange.opacity(0.15))
    }

    private var starters: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Harcamalarınızla ilgili sorabilirsiniz:")
                .font(.subheadline)
                .foregroundStyle(.secondary)
            if model.starters.isEmpty {
                ProgressView()
            } else {
                questions(model.starters)
            }
        }
    }

    /// Questions to tap; a tap sends the text exactly as shown.
    private func questions(_ texts: [String]) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            ForEach(texts, id: \.self) { text in
                Button(text) {
                    Task { await model.send(text) }
                }
                .buttonStyle(.bordered)
                .disabled(!model.canSend)
            }
        }
    }

    private var typing: some View {
        HStack(spacing: 8) {
            ProgressView()
            Text("Asistan yazıyor…")
                .font(.footnote)
                .foregroundStyle(.secondary)
        }
    }

    private var input: some View {
        HStack(alignment: .bottom, spacing: 8) {
            TextField("Sorunuzu yazın", text: $model.draft, axis: .vertical)
                .lineLimit(1...4)
                .textFieldStyle(.roundedBorder)
                .disabled(model.isAvailable == false)
                .onSubmit { Task { await model.sendDraft() } }
            if model.isWaiting {
                Button("Durdur", systemImage: "stop.circle.fill", action: model.stop)
                    .labelStyle(.iconOnly)
                    .font(.title2)
                    .tint(.red)
            } else {
                Button("Gönder", systemImage: "arrow.up.circle.fill") {
                    Task { await model.sendDraft() }
                }
                .labelStyle(.iconOnly)
                .font(.title2)
                .disabled(!model.canSendDraft)
            }
        }
        .padding()
    }
}

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
                            examples
                        }
                        ForEach(model.messages) { message in
                            MessageBubble(message: message, canRetry: model.canSend) {
                                Task { await model.retry(message.id) }
                            }
                            .id(message.id)
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
        .task { await model.checkStatus() }
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

    private var examples: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Harcamalarınızla ilgili sorabilirsiniz:")
                .font(.subheadline)
                .foregroundStyle(.secondary)
            ForEach(ChatViewModel.examples, id: \.self) { example in
                Button(example) {
                    Task { await model.send(example) }
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

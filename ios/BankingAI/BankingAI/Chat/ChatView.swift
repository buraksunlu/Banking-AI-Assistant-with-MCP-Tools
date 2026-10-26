import SwiftUI

/// The chat screen (AI-08, redesigned in UI-02). It shows ``ChatViewModel`` and passes the user's taps to it; there is no
/// logic here.
struct ChatView: View {
    @Bindable var model: ChatViewModel
    @Environment(\.scenePhase) private var scenePhase

    /// The counter appears this close to the limit (K5-C): earlier it would only be noise.
    private static let counterFrom = 450

    var body: some View {
        VStack(spacing: 0) {
            if let message = model.unavailableMessage {
                StatusBand(text: message, actionTitle: "Yeniden kontrol et") {
                    Task { await model.checkStatus() }
                }
                .padding(.horizontal, Layout.regular)
                .padding(.top, Layout.small)
            }
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: Layout.large) {
                        if model.messages.isEmpty {
                            welcome
                        }
                        ForEach(model.messages) { message in
                            MessageBubble(message: message, canRetry: model.canSend, list: model.transactions(for:)) {
                                Task { await model.retry(message.id) }
                            }
                            .id(message.id)
                            // Only under the last answer: the server knows only that offer (CHAT-06 K1-B, K2-A).
                            if message.role == .assistant && message.id == model.messages.last?.id && !model.suggestions.isEmpty {
                                suggestions(model.suggestions)
                            }
                        }
                        if model.isWaiting {
                            VStack(alignment: .leading, spacing: Layout.small) {
                                AssistantHeader()
                                TypingDots()
                            }
                            .id("typing")
                        }
                    }
                    .padding(Layout.regular)
                }
                .scrollDismissesKeyboard(.interactively)
                .onChange(of: model.messages.count) {
                    withAnimation {
                        if model.isWaiting {
                            proxy.scrollTo("typing")
                        } else if let last = model.messages.last {
                            proxy.scrollTo(last.id)
                        }
                    }
                }
                // Floats over the list (K5-C); the list scrolls on under it.
                .safeAreaInset(edge: .bottom) { input }
            }
        }
        .background(Palette.background.color)
        .navigationTitle(Brand.assistantName)
        .navigationBarTitleDisplayMode(.inline)
        .task { await model.open() }
        // Leaving the screen or the app stops a pending question (decision AI-08 K5-C).
        .onDisappear { model.stop() }
        .onChange(of: scenePhase) {
            if scenePhase == .background { model.stop() }
        }
    }

    /// An empty chat (K6-C): the mark, a title and the starter questions, one full-width row each (K3-D).
    private var welcome: some View {
        VStack(alignment: .leading, spacing: Layout.regular) {
            AssistantMark(size: 44)
            VStack(alignment: .leading, spacing: Layout.tight) {
                Text("Ne öğrenmek istersiniz?")
                    .font(Typography.title)
                    .foregroundStyle(Palette.primaryText.color)
                    .accessibilityAddTraits(.isHeader)
                Text("Hesaplarınız ve harcamalarınız hakkında sorun.")
                    .font(Typography.caption)
                    .foregroundStyle(Palette.secondaryText.color)
            }
            if model.starters.isEmpty {
                TypingDots()
                    .accessibilityLabel("Sorular yükleniyor")
            } else {
                VStack(spacing: Layout.small) {
                    ForEach(model.starters, id: \.self) { text in
                        Button {
                            Task { await model.send(text) }
                        } label: {
                            Text(text)
                                .multilineTextAlignment(.leading)
                                .frame(maxWidth: .infinity, alignment: .leading)
                        }
                        .buttonStyle(SecondaryButtonStyle())
                        .disabled(!model.canSend)
                    }
                }
            }
        }
        .padding(.top, Layout.huge)
    }

    /// The questions offered under the last answer: one row of chips that scrolls sideways, so it never pushes the chat
    /// down (K3-D). A tap sends the text exactly as shown.
    private func suggestions(_ texts: [String]) -> some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: Layout.small) {
                ForEach(texts, id: \.self) { text in
                    Button(text) {
                        Task { await model.send(text) }
                    }
                    .buttonStyle(ChipButtonStyle())
                    .disabled(!model.canSend)
                }
            }
        }
        .scrollClipDisabled()
    }

    private var input: some View {
        VStack(alignment: .trailing, spacing: Layout.tight) {
            if model.draftLength >= Self.counterFrom {
                Text("\(model.draftLength)/\(ChatViewModel.maxLength)")
                    .font(Typography.caption.monospacedDigit())
                    .foregroundStyle(model.draftLength > ChatViewModel.maxLength ? Palette.failure.color : Palette.secondaryText.color)
                    .padding(.horizontal, Layout.regular)
            }
            HStack(alignment: .bottom, spacing: Layout.small) {
                TextField("Sorunuzu yazın", text: $model.draft, axis: .vertical)
                    .lineLimit(1...4)
                    .textFieldStyle(.plain)
                    .font(Typography.body)
                    .foregroundStyle(Palette.primaryText.color)
                    .padding(.vertical, Layout.small)
                    .disabled(model.isAvailable == false)
                    .onSubmit { Task { await model.sendDraft() } }
                if model.isWaiting {
                    Button(action: model.stop) {
                        Image(systemName: "stop.fill")
                            .font(.footnote)
                            .foregroundStyle(Palette.primaryText.color)
                            .frame(width: 34, height: 34)
                            .overlay(Circle().stroke(Palette.hairline.color, lineWidth: 1))
                    }
                    .accessibilityLabel("Durdur")
                } else {
                    Button {
                        Task { await model.sendDraft() }
                    } label: {
                        // Grey, not faded gold, while it cannot be tapped: gold means "tap me" (K2-A).
                        Image(systemName: "arrow.up")
                            .font(.body.weight(.semibold))
                            .foregroundStyle(model.canSendDraft ? Palette.onAccent.color : Palette.secondaryText.color)
                            .frame(width: 34, height: 34)
                            .background(model.canSendDraft ? Palette.accent.color : Palette.hairline.color, in: Circle())
                    }
                    .disabled(!model.canSendDraft)
                    .accessibilityLabel("Gönder")
                }
            }
            .padding(.leading, Layout.regular)
            .padding(.trailing, Layout.tight)
            .padding(.vertical, Layout.tight)
            .background(Palette.raised.color, in: RoundedRectangle(cornerRadius: Layout.cardRadius))
            .overlay(RoundedRectangle(cornerRadius: Layout.cardRadius).stroke(Palette.hairline.color, lineWidth: 1))
        }
        .padding(.horizontal, Layout.regular)
        .padding(.bottom, Layout.small)
        .padding(.top, Layout.medium)
        // The list fades out under the capsule instead of ending at a hard line.
        .background(
            LinearGradient(colors: [Palette.background.color.opacity(0), Palette.background.color],
                           startPoint: .top, endPoint: .center)
        )
    }
}

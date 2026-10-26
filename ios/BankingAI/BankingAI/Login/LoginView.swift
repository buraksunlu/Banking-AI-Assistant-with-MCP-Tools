import SwiftUI

/// The login screen (IOS-13, redesigned in UI-04). The password field never shows its text and is cleared after every attempt.
struct LoginView: View {
    @State var model: LoginViewModel
    @FocusState private var focus: Field?

    private enum Field { case username, password }

    var body: some View {
        ScrollView {
            VStack(spacing: Layout.huge) {
                BrandMark()
                    .padding(.top, Layout.huge)

                VStack(alignment: .leading, spacing: Layout.regular) {
                    field("Kullanıcı adı", focus: .username) {
                        TextField("", text: $model.username)
                            .textContentType(.username)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()
                            .submitLabel(.next)
                            .focused($focus, equals: .username)
                            .onSubmit { focus = .password }
                    }

                    field("Şifre", focus: .password) {
                        SecureField("", text: $model.password)
                            .textContentType(.password)
                            .submitLabel(.go)
                            .focused($focus, equals: .password)
                            .onSubmit { Task { await model.signIn() } }
                    }

                    if let message = model.errorMessage {
                        Label(message, systemImage: "exclamationmark.triangle")
                            .font(Typography.caption)
                            .foregroundStyle(Palette.failure.color)
                    }

                    Button {
                        Task { await model.signIn() }
                    } label: {
                        HStack(spacing: Layout.small) {
                            if model.isSigningIn {
                                ProgressView().tint(Palette.onAccent.color)
                            }
                            Text(model.isSigningIn ? "Giriş yapılıyor…" : "Giriş yap")
                        }
                    }
                    .buttonStyle(PrimaryButtonStyle())
                    .disabled(!model.canSignIn)
                    .opacity(model.canSignIn || model.isSigningIn ? 1 : 0.55)
                    .padding(.top, Layout.small)
                }
            }
            .padding(.horizontal, Layout.large)
            .frame(maxWidth: 480)
            .frame(maxWidth: .infinity)
        }
        .scrollBounceBehavior(.basedOnSize)
        .background(Palette.background.color.ignoresSafeArea())
        // The first thing to type is the name; the keyboard is already up when the screen is.
        .task { focus = .username }
        // The error appears while VoiceOver is on the button, far from the line: say it aloud, or a wrong password is silent.
        .onChange(of: model.errorMessage) { _, message in
            if let message { AccessibilityNotification.Announcement(message).post() }
        }
    }

    private func field<Content: View>(_ title: String, focus field: Field, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: Layout.small) {
            // The field itself carries the name for VoiceOver; read here too, it would be said twice and listed as a heading.
            SectionTitle(text: title)
                .accessibilityHidden(true)
            content()
                .foregroundStyle(Palette.primaryText.color)
                .inputField(focused: focus == field)
                // The padding is part of the box the user sees, so a tap anywhere in it must open the keyboard.
                .contentShape(RoundedRectangle(cornerRadius: Layout.controlRadius))
                .onTapGesture { focus = field }
                .accessibilityLabel(title)
        }
    }
}

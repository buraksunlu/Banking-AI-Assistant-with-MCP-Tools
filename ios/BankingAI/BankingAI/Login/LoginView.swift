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
                    field("Kullanıcı adı") {
                        TextField("", text: $model.username)
                            .textContentType(.username)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()
                            .submitLabel(.next)
                            .focused($focus, equals: .username)
                            .onSubmit { focus = .password }
                    } focused: { focus == .username }

                    field("Şifre") {
                        SecureField("", text: $model.password)
                            .textContentType(.password)
                            .submitLabel(.go)
                            .focused($focus, equals: .password)
                            .onSubmit { Task { await model.signIn() } }
                    } focused: { focus == .password }

                    if let message = model.errorMessage {
                        Label(message, systemImage: "exclamationmark.triangle")
                            .font(Typography.caption)
                            .foregroundStyle(Palette.failure.color)
                            // The error appears while the user is looking at the button, not the fields: say it aloud.
                            .accessibilityAddTraits(.updatesFrequently)
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
    }

    private func field<Content: View>(_ title: String, @ViewBuilder content: () -> Content, focused: () -> Bool) -> some View {
        VStack(alignment: .leading, spacing: Layout.small) {
            SectionTitle(text: title)
            content()
                .foregroundStyle(Palette.primaryText.color)
                .inputField(focused: focused())
                .accessibilityLabel(title)
        }
    }
}

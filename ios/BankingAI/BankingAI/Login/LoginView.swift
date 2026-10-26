import SwiftUI

/// The login screen (IOS-13, redesigned in UI-04). The password is shown only while the person asks for it (UI-06 K8-A), is
/// hidden again when an attempt starts and is cleared after every attempt.
struct LoginView: View {
    @State var model: LoginViewModel
    @FocusState private var focus: Field?
    @State private var showsPassword = false

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

                    field("Şifre", focus: .password, accessory: AnyView(eyeButton)) {
                        Group {
                            if showsPassword {
                                TextField("", text: $model.password)
                                    .textInputAutocapitalization(.never)
                                    .autocorrectionDisabled()
                            } else {
                                SecureField("", text: $model.password)
                            }
                        }
                        .textContentType(.password)
                        .submitLabel(.go)
                        .focused($focus, equals: .password)
                        .onSubmit { Task { await model.signIn() } }
                        // Room for the eye, so the last characters never run under it.
                        .padding(.trailing, Layout.huge)
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
        // Felt as well as read (K7-A); the message is cleared when an attempt starts, so a second wrong try is felt again.
        .sensoryFeedback(.error, trigger: model.errorMessage) { _, message in message != nil }
        // An attempt never leaves the password on screen.
        .onChange(of: model.isSigningIn) { _, signing in
            if signing { showsPassword = false }
        }
    }

    /// Shows or hides the password; 44 points to tap, inside the field's box.
    private var eyeButton: some View {
        Button {
            showsPassword.toggle()
        } label: {
            Image(systemName: showsPassword ? "eye.slash" : "eye")
                .foregroundStyle(Palette.secondaryText.color)
                .frame(width: 44, height: 44)
                .contentShape(Rectangle())
        }
        .accessibilityLabel(showsPassword ? "Şifreyi gizle" : "Şifreyi göster")
        .padding(.trailing, Layout.tight)
    }

    private func field<Content: View>(_ title: String, focus field: Field, accessory: AnyView? = nil,
                                      @ViewBuilder content: () -> Content) -> some View {
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
                // Over the box, not in it: a separate button for VoiceOver, and a tap on it is not a tap on the field.
                .overlay(alignment: .trailing) { accessory }
        }
    }
}

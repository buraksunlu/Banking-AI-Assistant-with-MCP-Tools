import SwiftUI

/// The login screen (IOS-13). The password field never shows its text and is cleared after every attempt.
struct LoginView: View {
    @State var model: LoginViewModel

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Kullanıcı adı", text: $model.username)
                        .textContentType(.username)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                    SecureField("Şifre", text: $model.password)
                        .textContentType(.password)
                        .onSubmit { Task { await model.signIn() } }
                }

                if let message = model.errorMessage {
                    Section {
                        Label(message, systemImage: "exclamationmark.triangle")
                            .foregroundStyle(.red)
                    }
                }

                Section {
                    Button {
                        Task { await model.signIn() }
                    } label: {
                        if model.isSigningIn {
                            ProgressView()
                                .frame(maxWidth: .infinity)
                        } else {
                            Text("Giriş yap")
                                .frame(maxWidth: .infinity)
                        }
                    }
                    .disabled(!model.canSignIn)
                }
            }
            .navigationTitle("BankingAI")
        }
    }
}

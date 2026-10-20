import Foundation
import Testing
@testable import BankingAI

private let me = #"{"username":"ada","customerNumber":"C-1","firstName":"Ada","lastName":"Kahve","preferredCurrency":"TRY","timeZone":"Europe/Istanbul"}"#
private let expiry = "2026-10-20T10:27:31+00:00"

// IOS-13: signing in, and what the user sees when it fails. The password never stays on screen or in a message.
@MainActor
struct LoginViewModelTests {
    private func model(_ app: TestApp, username: String = "ada", password: String = "secret-pw") -> LoginViewModel {
        let model = LoginViewModel(api: app.api, session: app.session)
        model.username = username
        model.password = password
        return model
    }

    @Test func rightCredentialsSignInAndClearThePassword() async {
        let app = TestApp(signedIn: false) { _ in json(200, #"{"token":"t-new","expiresAt":"\#(expiry)"}"#) }
        let model = model(app, username: "  ada  ")

        await model.signIn()

        #expect(app.session.isSignedIn)
        #expect(app.storage.stored?.token == "t-new")
        #expect((model.password, model.errorMessage, model.isSigningIn) == ("", nil, false))
        // Leading and trailing spaces are not part of a username.
        #expect(app.requests.first?.body?.contains(#""username":"ada""#) == true)
    }

    @Test func wrongCredentialsSayItPlainlyAndNeverShowThePassword() async {
        let app = TestApp(signedIn: false) { _ in problem(401, "unauthorized", "Giriş gerekli.") }
        let model = model(app)

        await model.signIn()

        #expect(model.errorMessage == "Kullanıcı adı veya şifre hatalı.")
        #expect(!app.session.isSignedIn)
        #expect(model.password == "")
        #expect(model.errorMessage?.contains("secret-pw") == false)
    }

    @Test func tooManyAttemptsSayHowLongToWait() async {
        let app = TestApp(signedIn: false) { _ in
            problem(429, "too_many_requests", "Çok fazla deneme; biraz sonra tekrar deneyin.", headers: ["Retry-After": "42"])
        }
        let model = model(app)

        await model.signIn()

        #expect(model.errorMessage == "Çok fazla deneme; 42 sn sonra tekrar deneyin.")
    }

    @Test func anUnreachableAPISaysSo() async {
        let app = TestApp(signedIn: false) { _ in throw URLError(.cannotConnectToHost) }
        let model = model(app)

        await model.signIn()

        #expect(model.errorMessage == "Sunucuya ulaşılamıyor; bağlantıyı kontrol edip tekrar deneyin.")
    }

    @Test(arguments: [("", "pw"), ("   ", "pw"), ("ada", "")])
    func blankFieldsCannotBeSentAndSendNothing(username: String, password: String) async {
        let app = TestApp(signedIn: false) { _ in json(200, "{}") }
        let model = model(app, username: username, password: password)

        #expect(!model.canSignIn)
        await model.signIn()

        #expect(app.requests.isEmpty)
    }
}

// The signed-in screen until the chat arrives: it proves the token works, and a 401 brings the login screen back.
@MainActor
struct HomeViewModelTests {
    @Test func theCustomerIsGreetedByFirstName() async {
        let app = TestApp { _ in json(200, me) }
        let model = HomeViewModel(api: app.api, session: app.session)

        await model.load()

        #expect(model.greeting == "Merhaba, Ada!")
    }

    @Test func aRefusedTokenGoesBackToLogin() async {
        let app = TestApp { _ in problem(401, "session_invalid", "Oturum geçersiz; yeniden giriş yapın.") }
        let model = HomeViewModel(api: app.api, session: app.session)

        await model.load()

        #expect(!app.session.isSignedIn)
    }

    @Test func anUnreachableAPIIsShown() async {
        let app = TestApp { _ in throw URLError(.cannotConnectToHost) }
        let model = HomeViewModel(api: app.api, session: app.session)

        await model.load()

        #expect(model.errorMessage == "Sunucuya ulaşılamıyor; bağlantıyı kontrol edip tekrar deneyin.")
        #expect(app.session.isSignedIn)
    }

    @Test func signingOutForgetsTheToken() {
        let app = TestApp { _ in json(200, me) }

        HomeViewModel(api: app.api, session: app.session).signOut()

        #expect(!app.session.isSignedIn)
        #expect(app.storage.stored == nil)
    }
}

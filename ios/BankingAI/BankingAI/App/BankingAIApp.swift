import SwiftUI

/// The app's entry point: builds the session and the API client once and shows ``RootView``.
@main
struct BankingAIApp: App {
    @State private var session: SessionStore
    private let api: APIClient

    init() {
        let session = SessionStore(storage: KeychainSessionStorage())
        // A 401 on any signed-in call means the token is no longer accepted: back to the login screen (IOS-13).
        api = APIClient(baseURL: AppConfig.apiBaseURL, token: { session.validToken() }, onUnauthorized: { session.signOut() })
        _session = State(initialValue: session)
    }

    var body: some Scene {
        WindowGroup {
            RootView(session: session, api: api)
        }
    }
}

/// Login while nobody is signed in, the signed-in screens otherwise; it follows the session on its own.
struct RootView: View {
    let session: SessionStore
    let api: APIClient

    var body: some View {
        if session.isSignedIn {
            HomeView(model: HomeViewModel(api: api, session: session))
        } else {
            LoginView(model: LoginViewModel(api: api, session: session))
        }
    }
}

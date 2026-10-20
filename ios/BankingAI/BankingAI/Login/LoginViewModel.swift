import Foundation
import Observation

/// The login screen's state and its one action (IOS-13).
@Observable
final class LoginViewModel {
    var username = ""
    var password = ""
    private(set) var isSigningIn = false
    private(set) var errorMessage: String?
    @ObservationIgnored private let api: APIClient
    @ObservationIgnored private let session: SessionStore

    init(api: APIClient, session: SessionStore) {
        self.api = api
        self.session = session
    }

    var canSignIn: Bool {
        !trimmedUsername.isEmpty && !password.isEmpty && !isSigningIn
    }

    func signIn() async {
        guard canSignIn else { return }
        isSigningIn = true
        errorMessage = nil
        // The password leaves the screen whatever happens: it is never kept in memory longer than one attempt.
        let password = password
        self.password = ""
        defer { isSigningIn = false }

        do {
            let response = try await api.send(.login(username: trimmedUsername, password: password))
            session.signIn(StoredSession(token: response.token, expiresAt: response.expiresAt))
        } catch let error as APIError {
            errorMessage = Self.message(for: error)
        } catch {
            errorMessage = APIError.invalidResponse(status: 0).message
        }
    }

    private var trimmedUsername: String { username.trimmingCharacters(in: .whitespacesAndNewlines) }

    private static func message(for error: APIError) -> String? {
        switch error {
        // On login a 401 only means wrong credentials; the API answers both cases alike so names never leak.
        case .problem(status: 401, _, _, _):
            "Kullanıcı adı veya şifre hatalı."
        case .cancelled:
            nil
        default:
            error.message
        }
    }
}

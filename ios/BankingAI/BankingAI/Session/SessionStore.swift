import Foundation
import Observation

/// Whether someone is signed in, and with which token. The screens follow ``isSignedIn``: when it turns false (sign-out,
/// expiry, a 401 from the API) the app goes back to the login screen.
@Observable
final class SessionStore {
    private(set) var session: StoredSession?
    @ObservationIgnored private let storage: SessionStorage
    @ObservationIgnored private let now: () -> Date

    init(storage: SessionStorage, now: @escaping () -> Date = Date.init) {
        self.storage = storage
        self.now = now
        session = storage.load()
        // A token that expired while the app was closed would only earn a 401 on the first call.
        if let session, session.expiresAt <= now() {
            signOut()
        }
    }

    var isSignedIn: Bool { session != nil }

    /// The token while it is valid. An expired one signs the user out (the API's limit is 60 minutes, SEC-01b).
    func validToken() -> String? {
        guard let session else { return nil }
        guard session.expiresAt > now() else {
            signOut()
            return nil
        }
        return session.token
    }

    func signIn(_ session: StoredSession) {
        storage.save(session)
        self.session = session
    }

    func signOut() {
        storage.clear()
        session = nil
    }
}

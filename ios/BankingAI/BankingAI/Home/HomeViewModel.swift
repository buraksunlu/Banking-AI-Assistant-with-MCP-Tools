import Foundation
import Observation

/// The signed-in home screen: who is signed in, and signing out.
@Observable
final class HomeViewModel {
    private(set) var greeting: String?
    /// "MY" for the profile circle (UI-03 K1-A).
    private(set) var initials: String?
    /// The customer's own IANA time zone from `GET /api/me`; the detail screen shows times in it.
    private(set) var timeZone: TimeZone?
    private(set) var errorMessage: String?
    @ObservationIgnored private let api: APIClient
    @ObservationIgnored private let session: SessionStore

    init(api: APIClient, session: SessionStore) {
        self.api = api
        self.session = session
    }

    func load() async {
        errorMessage = nil
        do {
            let me = try await api.send(.me)
            greeting = "Merhaba, \(me.firstName)!"
            initials = HomeDisplay.initials(first: me.firstName, last: me.lastName)
            timeZone = TimeZone(identifier: me.timeZone)
        } catch let error as APIError {
            // A 401 has already signed out (APIClient); the login screen replaces this one, nothing to show here.
            if case .problem(status: 401, _, _, _) = error { return }
            if case .cancelled = error { return }
            errorMessage = error.message
        } catch {
            errorMessage = APIError.invalidResponse(status: 0).message
        }
    }

    func signOut() {
        session.signOut()
    }
}

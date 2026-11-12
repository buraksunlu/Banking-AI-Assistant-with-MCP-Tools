import Foundation
import Observation

/// Who is signed in, for the home header and the profile tab, and signing out.
@Observable
final class HomeViewModel {
    private(set) var greeting: String?
    /// "MY" for the profile circle (UI-03 K1-A).
    private(set) var initials: String?
    /// The customer's own IANA time zone from `GET /api/me`; the detail screen shows times in it.
    private(set) var timeZone: TimeZone?
    /// The whole `GET /api/me` answer; the profile tab reads it from here instead of asking again (UI-07).
    private(set) var profile: MeResponse?
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
            profile = me
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

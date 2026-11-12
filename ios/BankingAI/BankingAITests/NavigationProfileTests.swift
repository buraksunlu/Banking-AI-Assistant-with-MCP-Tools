import Foundation
import Testing
@testable import BankingAI

private let me = #"{"username":"ada","customerNumber":"C-1","firstName":"Ada","lastName":"Kahve","preferredCurrency":"TRY","timeZone":"Europe/Istanbul"}"#

/// 12 November 2026 12:00 UTC, and 1 July 2026 12:00 UTC (summer time in Berlin).
private let november = Date(timeIntervalSince1970: 1_794_484_800)
private let july = Date(timeIntervalSince1970: 1_782_907_200)

// UI-07: the tab bar's order and names (K2-A, K3-A).
struct AppTabTests {
    @Test func theTabsComeInTheDecidedOrder() {
        #expect(AppTab.allCases == [.home, .analysis, .assistant, .profile])
        #expect(AppTab.allCases.map(\.title) == ["Ana sayfa", "Analiz", "Asistan", "Profil"])
        #expect(AppTab.allCases.map(\.symbol) == ["house", "chart.pie", "bubble.left.and.text.bubble.right", "person.crop.circle"])
    }
}

// UI-07: the profile screen's lines, decided without a view (K6-B, K9-C).
struct ProfileDisplayTests {
    @Test func theFullNameIsFirstAndLast() {
        #expect(ProfileDisplay.fullName(first: "Ada", last: "Kahve") == "Ada Kahve")
        #expect(ProfileDisplay.fullName(first: " Ada ", last: "Kahve ") == "Ada Kahve")
    }

    @Test func theVersionIsMarketingVersionAndBuild() {
        #expect(ProfileDisplay.version(["CFBundleShortVersionString": "1.0", "CFBundleVersion": "1"]) == "1.0 (1)")
    }

    @Test(arguments: [["CFBundleShortVersionString": "1.0"], ["CFBundleVersion": "1"], [:]])
    func aMissingVersionIsNotMadeUp(info: [String: String]) {
        #expect(ProfileDisplay.version(info) == nil)
    }

    @Test func theTimeZoneShowsItsNameAndTodaysOffset() {
        #expect(ProfileDisplay.timeZone("Europe/Istanbul", at: november) == "Europe/Istanbul · GMT+03:00")
        #expect(ProfileDisplay.timeZone("Europe/Berlin", at: november) == "Europe/Berlin · GMT+01:00")
    }

    @Test func summerTimeChangesTheOffset() {
        #expect(ProfileDisplay.timeZone("Europe/Berlin", at: july) == "Europe/Berlin · GMT+02:00")
    }

    @Test func aWestOfGreenwichOffsetIsNegative() {
        #expect(ProfileDisplay.timeZone("America/New_York", at: november) == "America/New_York · GMT-05:00")
    }

    @Test func anUnknownTimeZoneShowsOnlyItsName() {
        #expect(ProfileDisplay.timeZone("Mars/Base", at: november) == "Mars/Base")
    }
}

// UI-07: the profile reads the same `GET /api/me` answer as the home header; there is no second request.
@MainActor
struct HomeProfileTests {
    @Test func theProfileComesWithTheGreeting() async {
        let app = TestApp { _ in json(200, me) }
        let model = HomeViewModel(api: app.api, session: app.session)

        await model.load()

        #expect(model.profile?.username == "ada")
        #expect(model.profile?.customerNumber == "C-1")
        #expect(model.profile?.preferredCurrency == "TRY")
        #expect(app.requests.count == 1)
    }

    @Test func aFailedLoadHasNoProfile() async {
        let app = TestApp { _ in throw URLError(.cannotConnectToHost) }
        let model = HomeViewModel(api: app.api, session: app.session)

        await model.load()

        #expect(model.profile == nil)
        #expect(model.errorMessage != nil)
    }
}

import Foundation
import Testing
@testable import BankingAI

// IOS-13: the token lives only in the Keychain, survives a relaunch until it expires, and is gone after sign-out.
@MainActor
struct SessionStoreTests {
    @Test func aSignedInSessionIsThereAfterARelaunch() {
        let storage = InMemorySessionStorage()
        SessionStore(storage: storage, now: { TestApp.now }).signIn(TestApp.validSession)

        let relaunched = SessionStore(storage: storage, now: { TestApp.now })

        #expect(relaunched.isSignedIn)
        #expect(relaunched.validToken() == "token-abc")
    }

    @Test func anExpiredSessionIsNotSignedInAtLaunchAndIsForgotten() {
        let storage = InMemorySessionStorage(StoredSession(token: "old", expiresAt: TestApp.now.addingTimeInterval(-1)))

        let store = SessionStore(storage: storage, now: { TestApp.now })

        #expect(!store.isSignedIn)
        #expect(storage.stored == nil)
    }

    @Test func aTokenThatExpiresWhileTheAppIsOpenSignsOut() {
        let storage = InMemorySessionStorage(TestApp.validSession)
        var now = TestApp.now
        let store = SessionStore(storage: storage, now: { now })

        now = TestApp.validSession.expiresAt
        let token = store.validToken()

        #expect(token == nil)
        #expect(!store.isSignedIn)
        #expect(storage.stored == nil)
    }

    @Test func signingOutForgetsTheSession() {
        let storage = InMemorySessionStorage(TestApp.validSession)
        let store = SessionStore(storage: storage, now: { TestApp.now })

        store.signOut()

        #expect(!store.isSignedIn)
        #expect(storage.stored == nil)
    }
}

// The real Keychain in the simulator, under a service name of its own, so the app's own session is never touched.
@MainActor
struct KeychainSessionStorageTests {
    @Test func aSessionIsSavedReadReplacedAndCleared() {
        let storage = KeychainSessionStorage(service: "com.bankingai.tests.\(UUID().uuidString)")
        defer { storage.clear() }
        let first = StoredSession(token: "first", expiresAt: TestApp.now)
        let second = StoredSession(token: "second", expiresAt: TestApp.now.addingTimeInterval(60))

        storage.save(first)
        #expect(storage.load() == first)
        storage.save(second)
        #expect(storage.load() == second)
        storage.clear()
        #expect(storage.load() == nil)
    }
}

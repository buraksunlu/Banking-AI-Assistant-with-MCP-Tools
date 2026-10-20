import Foundation
import Security

/// What is kept between launches: the token and when it expires. The password is never kept (IOS-13).
struct StoredSession: Codable, Equatable {
    let token: String
    let expiresAt: Date
}

/// Where the session is kept; the app uses the Keychain, tests use memory.
protocol SessionStorage {
    func load() -> StoredSession?
    func save(_ session: StoredSession)
    func clear()
}

/// The session in the Keychain, readable only on this device while it is unlocked (never UserDefaults, IOS-13).
final class KeychainSessionStorage: SessionStorage {
    private let service: String
    private let account = "session"

    init(service: String = "com.bankingai.session") {
        self.service = service
    }

    func load() -> StoredSession? {
        var query = baseQuery
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess, let data = item as? Data else { return nil }
        return try? JSONDecoder().decode(StoredSession.self, from: data)
    }

    func save(_ session: StoredSession) {
        guard let data = try? JSONEncoder().encode(session) else { return }
        // Replace rather than update: one item per service, and SecItemAdd fails on an existing one.
        SecItemDelete(baseQuery as CFDictionary)
        var item = baseQuery
        item[kSecValueData as String] = data
        // Not in backups or on other devices, and only while the phone is unlocked.
        item[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        SecItemAdd(item as CFDictionary, nil)
    }

    func clear() {
        SecItemDelete(baseQuery as CFDictionary)
    }

    private var baseQuery: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
    }
}

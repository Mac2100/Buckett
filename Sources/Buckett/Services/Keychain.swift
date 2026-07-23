import Foundation
import Security

/// Secret access keys are stored only in the local macOS Keychain — encrypted at rest
/// by the OS, never written to disk by the app, never sent anywhere except the
/// storage provider's API endpoint during request signing.
enum Keychain {
    private static let service = "com.mac2100.Buckett"

    private static func baseQuery(for accountID: UUID) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: accountID.uuidString
        ]
    }

    @discardableResult
    static func setSecret(_ secret: String, for accountID: UUID) -> Bool {
        let data = Data(secret.utf8)
        var query = baseQuery(for: accountID)

        let update: [String: Any] = [kSecValueData as String: data]
        let updateStatus = SecItemUpdate(query as CFDictionary, update as CFDictionary)
        if updateStatus == errSecSuccess { return true }

        query[kSecValueData as String] = data
        return SecItemAdd(query as CFDictionary, nil) == errSecSuccess
    }

    static func secret(for accountID: UUID) -> String? {
        var query = baseQuery(for: accountID)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne

        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        guard status == errSecSuccess, let data = result as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    static func deleteSecret(for accountID: UUID) {
        SecItemDelete(baseQuery(for: accountID) as CFDictionary)
    }
}

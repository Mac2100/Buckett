import Foundation

/// Persists non-secret account configuration as JSON in Application Support.
/// Secrets go to the Keychain (see `Keychain.swift`).
@MainActor
final class AccountStore: ObservableObject {
    @Published private(set) var accounts: [Account] = []

    static var supportDirectory: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)
            .first ?? FileManager.default.temporaryDirectory
        return base.appendingPathComponent("Buckett", isDirectory: true)
    }

    private var fileURL: URL {
        Self.supportDirectory.appendingPathComponent("accounts.json")
    }

    init() {
        load()
    }

    func load() {
        guard let data = try? Data(contentsOf: fileURL),
              let decoded = try? JSONDecoder().decode([Account].self, from: data)
        else { return }
        accounts = decoded
    }

    private func save() {
        do {
            try FileManager.default.createDirectory(
                at: Self.supportDirectory, withIntermediateDirectories: true
            )
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            let data = try encoder.encode(accounts)
            try data.write(to: fileURL, options: .atomic)
        } catch {
            NSLog("Buckett: failed to save accounts: \(error)")
        }
    }

    func upsert(_ account: Account, secretKey: String?) {
        if let index = accounts.firstIndex(where: { $0.id == account.id }) {
            accounts[index] = account
        } else {
            accounts.append(account)
        }
        if let secretKey, !secretKey.isEmpty {
            Keychain.setSecret(secretKey, for: account.id)
        }
        save()
    }

    func remove(_ account: Account) {
        accounts.removeAll { $0.id == account.id }
        Keychain.deleteSecret(for: account.id)
        save()
    }

    func secretKey(for account: Account) -> String? {
        Keychain.secret(for: account.id)
    }

    func client(for account: Account) -> S3Client? {
        guard let secret = secretKey(for: account) else { return nil }
        return S3Client(account: account, secretKey: secret)
    }
}

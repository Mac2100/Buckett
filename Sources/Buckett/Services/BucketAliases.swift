import Foundation

/// User-defined display names for buckets. Aliases are purely cosmetic — every
/// API request still uses the real bucket name. Keyed by "accountUUID|bucket".
@MainActor
final class BucketAliases: ObservableObject {
    static let shared = BucketAliases()

    @Published private(set) var aliases: [String: String] = [:]

    private var fileURL: URL {
        AccountStore.supportDirectory.appendingPathComponent("bucket-aliases.json")
    }

    private init() {
        if let data = try? Data(contentsOf: fileURL),
           let decoded = try? JSONDecoder().decode([String: String].self, from: data) {
            aliases = decoded
        }
    }

    private static func key(accountID: UUID, bucket: String) -> String {
        "\(accountID.uuidString)|\(bucket)"
    }

    func alias(accountID: UUID, bucket: String) -> String? {
        aliases[Self.key(accountID: accountID, bucket: bucket)]
    }

    /// Alias when set, otherwise the real bucket name.
    func displayName(accountID: UUID, bucket: String) -> String {
        alias(accountID: accountID, bucket: bucket) ?? bucket
    }

    /// Sets (or clears with nil/empty) the alias for a bucket.
    func setAlias(_ alias: String?, accountID: UUID, bucket: String) {
        let key = Self.key(accountID: accountID, bucket: bucket)
        let trimmed = alias?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        if trimmed.isEmpty {
            aliases.removeValue(forKey: key)
        } else {
            aliases[key] = trimmed
        }
        save()
    }

    private func save() {
        try? FileManager.default.createDirectory(
            at: AccountStore.supportDirectory, withIntermediateDirectories: true
        )
        if let data = try? JSONEncoder().encode(aliases) {
            try? data.write(to: fileURL, options: .atomic)
        }
    }
}
